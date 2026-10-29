namespace IndyInitRootCauseInvestigator.Engine

open System
open System.Text
open System.Text.Json.Nodes

/// Where an investigation is persisted remotely. The token is authentication
/// material: it may only ever travel in the Authorization header (R9.9, R11.3).
type RemoteConfig =
    { Owner: string
      Repository: string
      Branch: string
      Token: string }

/// A pure description of an HTTP call; the shell performs it.
type HttpRequest =
    { Method: string
      Url: string
      Headers: (string * string) list
      Body: string option }

type WriteOutcome =
    | Written of sha: string
    | RemoteConflict of detail: string
    | AuthFailed of detail: string
    | WriteFailed of detail: string

/// Local truth distinction (PERSISTENCE-CONTRACT §20, R10.6): pending is never success.
type SyncStatus =
    | LocalOnly
    | Pending
    | Sending
    | Synced
    | Conflicted of string
    | SyncUnknown

[<RequireQualifiedAccess>]
module SyncStatus =
    let label (status: SyncStatus) =
        match status with
        | LocalOnly -> "Saved on this device only"
        | Pending -> "Changes pending upload"
        | Sending -> "Uploading changes"
        | Synced -> "Saved to GitHub"
        | Conflicted detail -> "Conflict with GitHub: " + detail
        | SyncUnknown -> "Sync state unknown"

    /// True only when the remote has confirmed the write.
    let isRemoteConfirmed (status: SyncStatus) =
        match status with
        | Synced -> true
        | LocalOnly | Pending | Sending | Conflicted _ | SyncUnknown -> false

/// GitHub contents API provider mapping (PERSISTENCE-CONTRACT §11–§14).
[<RequireQualifiedAccess>]
module GitHub =
    let apiVersion = "2022-11-28"

    let contentPath (investigation: InvestigationId) = "investigations/" + Id.text investigation + "/events.json"

    let private contentsUrl (config: RemoteConfig) (investigation: InvestigationId) =
        "https://api.github.com/repos/"
        + Uri.EscapeDataString config.Owner
        + "/"
        + Uri.EscapeDataString config.Repository
        + "/contents/"
        + contentPath investigation

    let private headers (config: RemoteConfig) =
        [ "Authorization", "Bearer " + config.Token
          "Accept", "application/vnd.github+json"
          "X-GitHub-Api-Version", apiVersion ]

    /// Defensive: authentication material never enters committed content (R11.3).
    let private redact (config: RemoteConfig) (text: string) =
        if String.IsNullOrEmpty config.Token then text else text.Replace(config.Token, "[REDACTED]")

    let readRequest (config: RemoteConfig) (investigation: InvestigationId) : HttpRequest =
        { Method = "GET"
          Url = contentsUrl config investigation + "?ref=" + Uri.EscapeDataString config.Branch
          Headers = headers config
          Body = None }

    let writeRequest
        (config: RemoteConfig)
        (investigation: InvestigationId)
        (eventsJson: string)
        (sha: string option)
        (message: string)
        : HttpRequest =
        let content = Convert.ToBase64String(Encoding.UTF8.GetBytes(redact config eventsJson))
        let shaField = sha |> Option.map (fun s -> [ "sha", Json.str s ]) |> Option.defaultValue []
        let body =
            Json.obj ([ "message", Json.str (redact config message); "content", Json.str content; "branch", Json.str config.Branch ] @ shaField)
            |> Json.render
        { Method = "PUT"
          Url = contentsUrl config investigation
          Headers = headers config @ [ "Content-Type", "application/json" ]
          Body = Some body }

    let private gitHubMessage (body: string) =
        match Json.parse body |> Result.bind (Json.asObject "response") with
        | Ok o ->
            match Json.getOptString o "message" with
            | Ok(Some m) -> m
            | _ -> body
        | Error _ -> body

    let private describe (status: int) (body: string) =
        let message = gitHubMessage body
        if String.IsNullOrWhiteSpace message then "HTTP " + string status else "HTTP " + string status + ": " + message

    /// Some(sha, decoded file text) for 200, None for 404 (nothing stored yet).
    let interpretRead (status: int) (body: string) : Result<(string * string) option, string> =
        match status with
        | 200 ->
            try
                result {
                    let! o = Json.parse body |> Result.bind (Json.asObject "contents response")
                    let! sha = Json.getString o "sha"
                    let! encoding = Json.getOptString o "encoding"
                    let! content = Json.getString o "content"
                    match encoding with
                    | Some "base64"
                    | None ->
                        let compact = content.Replace("\n", "").Replace("\r", "")
                        return Some(sha, Encoding.UTF8.GetString(Convert.FromBase64String compact))
                    | Some other -> return! Error("unsupported content encoding '" + other + "' (file too large for the contents API?)")
                }
            with :? FormatException -> Error "the stored content is not valid base64"
        | 404 -> Ok None
        | 401
        | 403 -> Error("GitHub refused access (" + describe status body + ")")
        | _ -> Error("GitHub read failed (" + describe status body + ")")

    let interpretWrite (status: int) (body: string) : WriteOutcome =
        match status with
        | 200
        | 201 ->
            let sha =
                Json.parse body
                |> Result.bind (Json.asObject "write response")
                |> Result.bind (fun o -> Json.field o "content")
                |> Result.bind (Json.asObject "content")
                |> Result.bind (fun c -> Json.getString c "sha")
            match sha with
            | Ok s -> Written s
            | Error e -> WriteFailed("GitHub accepted the write but the response was unreadable: " + e)
        | 409
        | 422 -> RemoteConflict(describe status body)
        | 401
        | 403 -> AuthFailed(describe status body)
        | _ -> WriteFailed(describe status body)

/// Semantic reconciliation of local and remote history (R10.4, PERSISTENCE-CONTRACT §12, §24).
[<RequireQualifiedAccess>]
module Reconcile =
    type Plan =
        | UpToDate
        | PushLocal
        | AdoptRemote
        | Diverged of detail: string

    let private same (a: Event) (b: Event) = a.Sequence = b.Sequence && a.CommandId = b.CommandId

    /// Never overwrite divergent history: only a strict prefix relation permits push or adopt.
    let plan (local: Event list) (remote: Event list) : Plan =
        let rec firstDifference (l: Event list) (r: Event list) =
            match l, r with
            | a :: ls, b :: rs when same a b && a = b -> firstDifference ls rs
            | a :: _, b :: _ -> Some(a, b)
            | _ -> None
        match firstDifference local remote with
        | Some(a, b) when same a b ->
            Diverged("event " + string a.Sequence + " has the same command but different content locally and on GitHub")
        | Some(a, b) ->
            Diverged(
                "local and GitHub history diverge at sequence "
                + string (min a.Sequence b.Sequence)
                + " (local command "
                + a.CommandId.ToString("D")
                + ", GitHub command "
                + b.CommandId.ToString("D")
                + ")"
            )
        | None ->
            match compare local.Length remote.Length with
            | 0 -> UpToDate
            | c when c > 0 -> PushLocal
            | _ -> AdoptRemote
