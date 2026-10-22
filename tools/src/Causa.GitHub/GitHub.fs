namespace Causa.GitHub

open System
open System.Net.Http
open System.Text
open System.Threading.Tasks
open IndyInitRootCauseInvestigator.Engine
open Causa.Contract

/// A provider request/response, so transports can be wrapped (fault injection) without mocks
/// of GitHub semantics: every live test still talks to the real API.
type Request = { Method: string; Path: string; Body: JValue option }
type Response = { Status: int; Body: string }
type Transport = Request -> Task<Response>

type Config =
    { Owner: string
      Repository: string
      Branch: string
      ApiBase: string }

/// Provider-only metadata (CONTRACT §9.4): never written into any contract file.
type ProviderToken = { HeadCommit: string; TreeSha: string }

type Failure =
    | Network of string
    | Auth of string
    | NotFastForward of string
    | Other of string

/// Stages of an atomic publish; a fault injector can stop between any two.
type Stage =
    | BeforeBlobs
    | BeforeTree
    | BeforeCommit
    | BeforeRefUpdate

/// A provider implementation as a record of functions (CONTRACT §9.4). The REST backend
/// (Git Data API) and the git smart-HTTP backend have identical semantics: one commit per
/// semantic transition, published by a non-forced ref update.
type Backend =
    { Name: string
      Head: unit -> Task<Result<ProviderToken, Failure>>
      ListUnder: string -> string -> Task<Result<(string * string) list, Failure>>
      Blob: string -> Task<Result<byte[], Failure>>
      TreeOf: string -> Task<Result<string, Failure>>
      ChangedPaths: string -> string -> Task<Result<string list, Failure>>
      Publish: (Stage -> bool) -> ProviderToken -> string -> (string * byte[]) list -> string -> Task<Result<string, Failure>> }

[<RequireQualifiedAccess>]
type SyncOutcome =
    | Synced of commit: string
    | Conflict of detail: string
    | Pending of detail: string
    | AuthFailed of detail: string
    | Refused of CausaError

[<RequireQualifiedAccess>]
module Http =
    let apiVersion = "2022-11-28"

    /// The real transport. The token travels only in the Authorization header.
    let transport (config: Config) (token: string) (client: HttpClient) : Transport =
        fun req ->
            task {
                use msg = new HttpRequestMessage(HttpMethod(req.Method), config.ApiBase.TrimEnd('/') + req.Path)
                msg.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token) |> ignore
                msg.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json") |> ignore
                msg.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", apiVersion) |> ignore
                msg.Headers.TryAddWithoutValidation("User-Agent", "causa-storage-contract/1.0.0") |> ignore
                req.Body
                |> Option.iter (fun b ->
                    let text = Json.canonical b |> Result.defaultWith failwith
                    msg.Content <- new StringContent(text, Encoding.UTF8, "application/json"))
                use! resp = client.SendAsync msg
                let! body = resp.Content.ReadAsStringAsync()
                return { Status = int resp.StatusCode; Body = body }
            }

[<RequireQualifiedAccess>]
module Paths =
    let private runIdPattern = Text.RegularExpressions.Regex(@"^[a-z0-9][a-z0-9-]{0,63}\z")

    /// CONTRACT §3.2: the only writable namespace.
    let workspace (runId: string) : Result<string, CausaError> =
        if runIdPattern.IsMatch runId then Ok("workspaces/demo/" + runId + "/investigations/")
        else CausaError.fail Codes.pathInvalid ("runId '" + runId + "' is not a valid run identifier")

    let investigation (runId: string) (investigationId: string) : Result<string, CausaError> =
        workspace runId
        |> Result.bind (fun prefix ->
            match Uuid.tryParse investigationId with
            | Some g when Uuid.text g = investigationId -> Ok(prefix + investigationId + "/")
            | _ -> CausaError.fail Codes.pathInvalid ("investigation id '" + investigationId + "' is not canonical"))

/// GitHub provider over the Git Data API (CONTRACT §9.4). All functions are pure descriptions
/// of API calls composed over an injected transport.
[<RequireQualifiedAccess>]
module Provider =

    let private describe (r: Response) =
        let message =
            match Json.parse r.Body with
            | Ok(JObj o) -> (match Json.field "message" o with Some(JStr m) -> m | _ -> "")
            | _ -> ""
        "HTTP " + string r.Status + (if message = "" then "" else ": " + message)

    let private call (send: Transport) (req: Request) : Task<Result<JValue, Failure>> =
        task {
            try
                let! r = send req
                match r.Status with
                | s when s >= 200 && s < 300 ->
                    match Json.parse r.Body with
                    | Ok v -> return Ok v
                    | Error e -> return Error(Other("unreadable response: " + e))
                | 401 | 403 -> return Error(Auth(describe r))
                | 409 | 422 -> return Error(NotFastForward(describe r))
                | _ -> return Error(Other(describe r))
            with
            | :? HttpRequestException as ex -> return Error(Network ex.Message)
            | :? TaskCanceledException as ex -> return Error(Network("timeout: " + ex.Message))
            | ex when ex.Message.StartsWith "injected" -> return Error(Network ex.Message)
        }

    let private str (path: string list) (v: JValue) =
        let rec go p x =
            match p, x with
            | [], JStr s -> Some s
            | k :: rest, JObj o -> Json.field k o |> Option.bind (go rest)
            | _ -> None
        go path v

    let private repo (c: Config) = "/repos/" + Uri.EscapeDataString c.Owner + "/" + Uri.EscapeDataString c.Repository

    let head (c: Config) (send: Transport) : Task<Result<ProviderToken, Failure>> =
        task {
            let! r = call send { Method = "GET"; Path = repo c + "/git/ref/heads/" + c.Branch; Body = None }
            match r |> Result.map (str [ "object"; "sha" ]) with
            | Error e -> return Error e
            | Ok None -> return Error(Other "ref response without sha")
            | Ok(Some commit) ->
                let! cr = call send { Method = "GET"; Path = repo c + "/git/commits/" + commit; Body = None }
                match cr |> Result.map (str [ "tree"; "sha" ]) with
                | Ok(Some tree) -> return Ok { HeadCommit = commit; TreeSha = tree }
                | Ok None -> return Error(Other "commit response without tree")
                | Error e -> return Error e
        }

    /// Blob shas of the files under `prefix` in the given tree (one consistent snapshot).
    let listUnder (c: Config) (send: Transport) (treeSha: string) (prefix: string) : Task<Result<(string * string) list, Failure>> =
        task {
            let! r = call send { Method = "GET"; Path = repo c + "/git/trees/" + treeSha + "?recursive=1"; Body = None }
            return
                r
                |> Result.bind (fun v ->
                    match v with
                    | JObj o ->
                        match Json.field "truncated" o with
                        | Some(JBool true) -> Error(Other "tree listing truncated")
                        | _ ->
                            match Json.field "tree" o with
                            | Some(JArr entries) ->
                                entries
                                |> List.choose (fun e ->
                                    match str [ "path" ] e, str [ "type" ] e, str [ "sha" ] e with
                                    | Some p, Some "blob", Some s when p.StartsWith prefix -> Some(p, s)
                                    | _ -> None)
                                |> Ok
                            | _ -> Error(Other "tree response without entries")
                    | _ -> Error(Other "tree response is not an object"))
        }

    let blob (c: Config) (send: Transport) (sha: string) : Task<Result<byte[], Failure>> =
        task {
            let! r = call send { Method = "GET"; Path = repo c + "/git/blobs/" + sha; Body = None }
            return
                r
                |> Result.bind (fun v ->
                    match str [ "content" ] v, str [ "encoding" ] v with
                    | Some content, Some "base64" -> Ok(Convert.FromBase64String(content.Replace("\n", "")))
                    | _ -> Error(Other "blob response is not base64"))
        }

    /// 409/422 mean "not a fast-forward" only for the ref update; elsewhere they are plain failures.
    let private notRef (r: Result<'T, Failure>) = r |> Result.mapError (function NotFastForward m -> Other m | f -> f)

    let private createBlob (c: Config) (send: Transport) (bytes: byte[]) =
        task {
            let! r0 = call send { Method = "POST"; Path = repo c + "/git/blobs"; Body = Some(JObj [ "content", JStr(Convert.ToBase64String bytes); "encoding", JStr "base64" ]) }
            let r = notRef r0
            return r |> Result.bind (fun v -> str [ "sha" ] v |> Option.map Ok |> Option.defaultValue (Error(Other "blob create without sha")))
        }

    /// Paths changed between two commits (used as the writable-namespace guard).
    let changedPaths (c: Config) (send: Transport) (baseCommit: string) (commit: string) : Task<Result<string list, Failure>> =
        task {
            let! r = call send { Method = "GET"; Path = repo c + "/compare/" + baseCommit + "..." + commit; Body = None }
            // Fail closed: a missing or possibly truncated file list (compare API cap: 300) is an error.
            return
                notRef r
                |> Result.bind (fun v ->
                    match v with
                    | JObj o ->
                        match Json.field "files" o with
                        | Some(JArr fs) when fs.Length < 300 ->
                            Ok(fs |> List.collect (fun f -> [ str [ "filename" ] f; str [ "previous_filename" ] f ] |> List.choose id))
                        | Some(JArr _) -> Error(Other "compare response may be truncated (300 files)")
                        | _ -> Error(Other "compare response without a file list")
                    | _ -> Error(Other "compare response is not an object"))
        }

    /// One commit containing every file of the semantic transition, published by a
    /// non-forced ref update (CONTRACT §9.4.1-2).
    let publish
        (c: Config)
        (send: Transport)
        (stop: Stage -> bool)
        (baseToken: ProviderToken)
        (prefix: string)
        (files: (string * byte[]) list)
        (message: string)
        : Task<Result<string, Failure>> =
        task {
            let halt stage = if stop stage then Some(Error(Network("injected stop " + string stage))) else None
            match halt BeforeBlobs with
            | Some e -> return e
            | None ->
                let! blobs =
                    files
                    |> List.fold
                        (fun (acc: Task<Result<(string * string) list, Failure>>) (path, bytes) ->
                            task {
                                match! acc with
                                | Error e -> return Error e
                                | Ok done' ->
                                    let! b = createBlob c send bytes
                                    return b |> Result.map (fun sha -> done' @ [ path, sha ])
                            })
                        (Task.FromResult(Ok []))
                match blobs, halt BeforeTree with
                | Error e, _ -> return Error e
                | _, Some e -> return e
                | Ok entries, None ->
                    let tree =
                        JObj
                            [ "base_tree", JStr baseToken.TreeSha
                              "tree", JArr(entries |> List.map (fun (p, s) -> JObj [ "path", JStr p; "mode", JStr "100644"; "type", JStr "blob"; "sha", JStr s ])) ]
                    let! t0 = call send { Method = "POST"; Path = repo c + "/git/trees"; Body = Some tree }
                    let t = notRef t0
                    match t |> Result.map (str [ "sha" ]), halt BeforeCommit with
                    | Error e, _ -> return Error e
                    | Ok None, _ -> return Error(Other "tree create without sha")
                    | _, Some e -> return e
                    | Ok(Some treeSha), None ->
                        let commit = JObj [ "message", JStr message; "tree", JStr treeSha; "parents", JArr [ JStr baseToken.HeadCommit ] ]
                        let! cm0 = call send { Method = "POST"; Path = repo c + "/git/commits"; Body = Some commit }
                        let cm = notRef cm0
                        match cm |> Result.map (str [ "sha" ]) with
                        | Error e -> return Error e
                        | Ok None -> return Error(Other "commit create without sha")
                        | Ok(Some commitSha) ->
                            // Namespace guard: the new commit may differ from its base only under `prefix`.
                            let! changed = changedPaths c send baseToken.HeadCommit commitSha
                            match changed with
                            | Error e -> return Error e
                            | Ok paths when paths |> List.exists (fun p -> not (p.StartsWith prefix)) ->
                                return Error(Other("refusing to publish: changes outside " + prefix + ": " + String.Join(",", paths)))
                            | Ok _ ->
                                match halt BeforeRefUpdate with
                                | Some e -> return e
                                | None ->
                                    let! u =
                                        call send { Method = "PATCH"; Path = repo c + "/git/refs/heads/" + c.Branch; Body = Some(JObj [ "sha", JStr commitSha; "force", JBool false ]) }
                                    return u |> Result.map (fun _ -> commitSha)
        }

    let treeOf (c: Config) (send: Transport) (commit: string) : Task<Result<string, Failure>> =
        task {
            let! r = call send { Method = "GET"; Path = repo c + "/git/commits/" + commit; Body = None }
            return r |> Result.bind (fun v -> str [ "tree"; "sha" ] v |> Option.map Ok |> Option.defaultValue (Error(Other "commit without tree")))
        }

    let createBranch (c: Config) (send: Transport) (fromCommit: string) =
        call send { Method = "POST"; Path = repo c + "/git/refs"; Body = Some(JObj [ "ref", JStr("refs/heads/" + c.Branch); "sha", JStr fromCommit ]) }

    /// The REST backend over an HTTP transport.
    let rest (c: Config) (send: Transport) : Backend =
        { Name = "rest"
          Head = fun () -> head c send
          ListUnder = listUnder c send
          Blob = blob c send
          TreeOf = treeOf c send
          ChangedPaths = changedPaths c send
          Publish = publish c send }

/// Durable local store (CONTRACT §9.2): the last remote-confirmed files plus pending events,
/// on disk, so pending work survives restarts and conflicts.
type LocalState =
    { RunId: string
      InvestigationId: string
      Synced: InvestigationFiles option
      SyncedToken: ProviderToken option
      Pending: Event list }

[<RequireQualifiedAccess>]
module Local =
    open System.IO

    let private dir (root: string) (runId: string) (id: string) = Path.Combine(root, runId, id)

    let save (root: string) (s: LocalState) =
        let d = dir root s.RunId s.InvestigationId
        Directory.CreateDirectory(Path.Combine(d, "synced")) |> ignore
        s.Synced
        |> Option.iter (fun f ->
            File.WriteAllBytes(Path.Combine(d, "synced", "manifest.json"), f.Manifest)
            File.WriteAllBytes(Path.Combine(d, "synced", "events.ndjson"), f.Events)
            File.WriteAllBytes(Path.Combine(d, "synced", "snapshot.json"), f.Snapshot))
        let token = s.SyncedToken |> Option.map (fun t -> JObj [ "headCommit", JStr t.HeadCommit; "treeSha", JStr t.TreeSha ]) |> Option.defaultValue JNull
        File.WriteAllBytes(Path.Combine(d, "provider-token.json"), Json.fileBytes token |> Result.defaultWith failwith)
        let pending = s.Pending |> List.map (EventWire.encode >> Result.bind (Json.canonical >> Result.mapError (CausaError.create Codes.unrepresentable)) >> Result.defaultWith (fun e -> failwith e.Detail))
        File.WriteAllText(Path.Combine(d, "pending.ndjson"), String.Join("", pending |> List.map (fun l -> l + "\n")))

    let load (root: string) (runId: string) (id: string) : LocalState option =
        let d = dir root runId id
        if not (Directory.Exists d) then None
        else
            let synced =
                if File.Exists(Path.Combine(d, "synced", "events.ndjson")) then
                    Some { DirectoryId = id
                           Manifest = File.ReadAllBytes(Path.Combine(d, "synced", "manifest.json"))
                           Events = File.ReadAllBytes(Path.Combine(d, "synced", "events.ndjson"))
                           Snapshot = File.ReadAllBytes(Path.Combine(d, "synced", "snapshot.json")) }
                else None
            let token =
                match File.ReadAllText(Path.Combine(d, "provider-token.json")).TrimEnd('\n') |> Json.parse with
                | Ok(JObj o) ->
                    match Json.field "headCommit" o, Json.field "treeSha" o with
                    | Some(JStr h), Some(JStr t) -> Some { HeadCommit = h; TreeSha = t }
                    | _ -> None
                | _ -> None
            let pending =
                File.ReadAllText(Path.Combine(d, "pending.ndjson")).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                |> List.ofArray
                |> List.map (Json.parse >> Result.mapError (CausaError.create Codes.jsonMalformed) >> Result.bind EventWire.decode >> Result.map (fun x -> x.Event) >> Result.defaultWith (fun e -> failwith e.Detail))
            Some { RunId = runId; InvestigationId = id; Synced = synced; SyncedToken = token; Pending = pending }
