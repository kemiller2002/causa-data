namespace Causa.GitHub

open System
open System.Threading.Tasks
open IndyInitRootCauseInvestigator.Engine
open Causa.Contract

/// Investigation-level persistence over the provider (CONTRACT §9.2-§9.4).
[<RequireQualifiedAccess>]
module Sync =
    type Remote =
        { Token: ProviderToken
          Files: InvestigationFiles option
          Loaded: Loaded option
          BlobShas: (string * string) list }

    let private failureText (f: Failure) =
        match f with
        | Network m -> "network: " + m
        | Auth m -> "auth: " + m
        | NotFastForward m -> "not a fast-forward: " + m
        | Other m -> m

    let private outcomeOf (f: Failure) =
        match f with
        | Auth m -> SyncOutcome.AuthFailed m
        | NotFastForward m -> SyncOutcome.Conflict m
        | other -> SyncOutcome.Pending(failureText other)

    /// Read one investigation directory at a single consistent commit.
    let loadAt (b: Backend) (set: SchemaCheck.SchemaSet) (token: ProviderToken) (prefix: string) (id: string)
        : Task<Result<Remote, Choice<Failure, CausaError>>> =
        task {
            match! b.ListUnder token.TreeSha prefix with
            | Error f -> return Error(Choice1Of2 f)
            | Ok [] -> return Ok { Token = token; Files = None; Loaded = None; BlobShas = [] }
            | Ok entries ->
                let names = entries |> List.map (fun (p, _) -> p.Substring prefix.Length) |> List.sort
                if names <> List.sort Store.fileNames then
                    return Error(Choice2Of2(CausaError.create Codes.pathInvalid ("unexpected files under " + prefix + ": " + String.Join(",", names))))
                else
                    let fetch name =
                        let sha = entries |> List.find (fun (p, _) -> p = prefix + name) |> snd
                        b.Blob sha
                    let! m = fetch "manifest.json"
                    let! e = fetch "events.ndjson"
                    let! s = fetch "snapshot.json"
                    match m, e, s with
                    | Ok m, Ok e, Ok s ->
                        let files = { DirectoryId = id; Manifest = m; Events = e; Snapshot = s }
                        match Store.validate set files with
                        | Ok loaded -> return Ok { Token = token; Files = Some files; Loaded = Some loaded; BlobShas = entries }
                        | Error err -> return Error(Choice2Of2 err)
                    | Error f, _, _ | _, Error f, _ | _, _, Error f -> return Error(Choice1Of2 f)
        }

    let load (b: Backend) set prefix id =
        task {
            match! b.Head() with
            | Error f -> return Error(Choice1Of2 f)
            | Ok token -> return! loadAt b set token prefix id
        }

    let private sameBytes (a: InvestigationFiles option) (b: InvestigationFiles option) =
        match a, b with
        | None, None -> true
        | Some x, Some y -> x.Events = y.Events && x.Manifest = y.Manifest && x.Snapshot = y.Snapshot
        | _ -> false

    /// Re-decide pending commands against a newer remote state (CONTRACT §9.4.2): each pending
    /// command group is mapped back to its command and decided again through the engine.
    let rebase (set: SchemaCheck.SchemaSet) (local: LocalState) (remote: Remote) : Result<LocalState, CausaError> =
        let groups =
            local.Pending
            |> List.fold (fun acc e ->
                match acc with
                | (g: Event list) :: rest when g.Head.CommandId = e.CommandId -> (g @ [ e ]) :: rest
                | _ -> [ e ] :: acc) []
            |> List.rev
        let start = remote.Loaded |> Option.map (fun l -> l.State)
        let remoteEvents = remote.Loaded |> Option.map (fun l -> l.Events) |> Option.defaultValue []
        let sameEvent (a: Event) (b: Event) = a.CommandId = b.CommandId && a.Body = b.Body && a.Actor = b.Actor && a.At = b.At
        // A pending command that already landed remotely (e.g. its confirmation was lost) is dropped,
        // not re-decided; a remote command with the same id but different events is a real conflict.
        let landed (group: Event list) =
            let remoteGroup = remoteEvents |> List.filter (fun e -> e.CommandId = group.Head.CommandId)
            match remoteGroup with
            | [] -> Ok false
            | rg when rg.Length = group.Length && List.forall2 sameEvent rg group -> Ok true
            | _ -> CausaError.fail Codes.correlationReused ("command " + Uuid.text group.Head.CommandId + " exists remotely with different events")
        groups
        |> traverse (fun g -> landed g |> Result.map (fun l -> g, l))
        |> Result.map (List.filter (snd >> not) >> List.map fst)
        |> Result.bind (fun unlanded ->
        unlanded
        |> List.fold
            (fun acc group ->
                acc
                |> Result.bind (fun (state: Investigation option, produced: Event list) ->
                    let first = List.head group
                    match History.commandOf first.Investigation (group |> List.map (fun e -> e.Body)) with
                    | None -> CausaError.fail Codes.historyInadmissible "a pending event group does not correspond to a command"
                    | Some command ->
                        let env =
                            { CommandId = first.CommandId; Investigation = first.Investigation
                              ExpectedVersion = state |> Option.map (fun s -> s.Version)
                              Actor = first.Actor; RequestedAt = first.At; Command = command }
                        match Decide.execute state env with
                        | Ok events -> Ok(events |> List.fold (fun s e -> Some(Evolve.apply s e)) state, produced @ events)
                        | Error errors ->
                            Error { Code = Codes.historyInadmissible; DomainCode = Some (List.head errors).Code
                                    Detail = "pending command no longer applies: " + String.Join("; ", errors |> List.map (fun e -> e.Code + " " + e.Message)) }))
            (Ok(start, [])))
        |> Result.map (fun (_, events) -> { local with Synced = remote.Files; SyncedToken = Some remote.Token; Pending = events })

    type Attempt =
        { Outcome: SyncOutcome
          Local: LocalState
          Commit: string option
          Attempts: int }

    /// Save pending events atomically. Success only after the ref update is confirmed AND a
    /// re-read at the new head returns the written bytes (CONTRACT §9.4.3).
    let save (b: Backend) (stop: Stage -> bool) (set: SchemaCheck.SchemaSet) (local: LocalState) : Task<Attempt> =
        let prefixR = Paths.investigation local.RunId local.InvestigationId
        let rec attempt (n: int) =
            task {
                match prefixR with
                | Error e -> return { Outcome = SyncOutcome.Refused e; Local = local; Commit = None; Attempts = n }
                | Ok prefix ->
                    match! load b set prefix local.InvestigationId with
                    | Error(Choice1Of2 f) -> return { Outcome = outcomeOf f; Local = local; Commit = None; Attempts = n }
                    | Error(Choice2Of2 e) -> return { Outcome = SyncOutcome.Refused e; Local = local; Commit = None; Attempts = n }
                    | Ok remote ->
                        let baseEvents = local.Synced |> Option.map (fun f -> Store.validate set f |> Result.map (fun l -> l.Events) |> Result.defaultValue []) |> Option.defaultValue []
                        let ours = baseEvents @ local.Pending
                        let remoteEvents = remote.Loaded |> Option.map (fun l -> l.Events) |> Option.defaultValue []
                        if local.Pending.IsEmpty then
                            return { Outcome = (if sameBytes remote.Files local.Synced then SyncOutcome.Synced remote.Token.HeadCommit else SyncOutcome.Conflict "remote differs from the local synced copy");
                                     Local = local; Commit = None; Attempts = n }
                        elif not local.Pending.IsEmpty && remoteEvents = ours then
                            // Lost response: our write landed although we never saw the confirmation.
                            return { Outcome = SyncOutcome.Synced remote.Token.HeadCommit
                                     Local = { local with Synced = remote.Files; SyncedToken = Some remote.Token; Pending = [] }
                                     Commit = Some remote.Token.HeadCommit; Attempts = n }
                        elif not (sameBytes remote.Files local.Synced) then
                            return { Outcome = SyncOutcome.Conflict("investigation changed remotely (remote version " + string remoteEvents.Length + ", local base " + string baseEvents.Length + ")")
                                     Local = local; Commit = None; Attempts = n }
                        else
                            let expected = int64 baseEvents.Length
                            let built =
                                match local.Pending with
                                | first :: _ when first.Sequence <> expected + 1L ->
                                    CausaError.fail Codes.versionStale ("pending events start at " + string first.Sequence + " but the base is at " + string expected)
                                | _ -> Store.build set ours
                            match built with
                            | Error e -> return { Outcome = SyncOutcome.Refused e; Local = local; Commit = None; Attempts = n }
                            | Ok(files, loaded) ->
                                let message = "causa: " + local.InvestigationId + " v" + string expected + " -> v" + string loaded.State.Version
                                let payload = [ prefix + "manifest.json", files.Manifest; prefix + "events.ndjson", files.Events; prefix + "snapshot.json", files.Snapshot ]
                                match! b.Publish stop remote.Token prefix payload message with
                                | Error(NotFastForward _) when n < 3 ->
                                    // The branch moved. Retry only if this investigation is unchanged at the new head.
                                    match! b.Head() with
                                    | Ok token ->
                                        match! b.ListUnder token.TreeSha prefix with
                                        | Ok shas when shas = remote.BlobShas -> return! attempt (n + 1)
                                        | Ok _ -> return { Outcome = SyncOutcome.Conflict "investigation changed remotely during the write"; Local = local; Commit = None; Attempts = n }
                                        | Error f -> return { Outcome = outcomeOf f; Local = local; Commit = None; Attempts = n }
                                    | Error f -> return { Outcome = outcomeOf f; Local = local; Commit = None; Attempts = n }
                                | Error f -> return { Outcome = outcomeOf f; Local = local; Commit = None; Attempts = n }
                                | Ok commit ->
                                    // Confirm by reading the tree of the commit we published, not the (moving) branch head.
                                    match! b.TreeOf commit with
                                    | Error f -> return { Outcome = SyncOutcome.Pending("written but unconfirmed: " + failureText f); Local = local; Commit = Some commit; Attempts = n }
                                    | Ok tree ->
                                        let token = { HeadCommit = commit; TreeSha = tree }
                                        match! loadAt b set token prefix local.InvestigationId with
                                        | Ok confirmed when sameBytes confirmed.Files (Some files) ->
                                            return { Outcome = SyncOutcome.Synced commit
                                                     Local = { local with Synced = Some files; SyncedToken = Some token; Pending = [] }
                                                     Commit = Some commit; Attempts = n }
                                        | Ok _ -> return { Outcome = SyncOutcome.Pending "re-read did not return the written bytes"; Local = local; Commit = Some commit; Attempts = n }
                                        | Error _ -> return { Outcome = SyncOutcome.Pending "re-read failed"; Local = local; Commit = Some commit; Attempts = n }
            }
        attempt 1
