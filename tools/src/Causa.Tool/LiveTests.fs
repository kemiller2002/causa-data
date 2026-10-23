namespace Causa.Tool

open System
open System.IO
open System.Net.Http
open System.Threading.Tasks
open IndyInitRootCauseInvestigator.Engine
open Causa.Contract
open Causa.GitHub

/// Live GitHub persistence tests (Gate 5). Every test talks to the real GitHub API; fault
/// injection only wraps the real transport. Writes go to a disposable branch and an isolated
/// workspaces/demo/<runId>/ namespace; canonical fixtures are never written.
module LiveTests =
    type TestResult = { Name: string; Passed: bool; Detail: string; Commits: string list }

    let private pass name detail commits = { Name = name; Passed = true; Detail = detail; Commits = commits }
    let private failT name detail commits = { Name = name; Passed = false; Detail = detail; Commits = commits }

    let private now () = Timestamp.truncateToMillisecond DateTimeOffset.UtcNow

    let private outcomeText (o: SyncOutcome) =
        match o with
        | SyncOutcome.Synced c -> "Synced " + c
        | SyncOutcome.Conflict d -> "Conflict: " + d
        | SyncOutcome.Pending d -> "Pending: " + d
        | SyncOutcome.AuthFailed d -> "AuthFailed: " + d
        | SyncOutcome.Refused e -> "Refused " + e.Code + ": " + e.Detail

    let private isSynced o = match o with SyncOutcome.Synced _ -> true | _ -> false

    type Env =
        { Backend: Backend
          Rest: Backend
          RestConfig: Config
          Set: SchemaCheck.SchemaSet
          RunId: string
          LocalRoot: string
          RepoRoot: string
          Human: Actor }

    /// Decide one command against the state implied by synced + pending events.
    let private decide (env: Env) (local: LocalState) (command: Command) (commandName: string) : LocalState =
        let synced = local.Synced |> Option.map (Store.validate env.Set >> Result.map (fun l -> l.Events) >> Result.defaultValue []) |> Option.defaultValue []
        let history = synced @ local.Pending
        let state = Evolve.replay history
        let envelope =
            { CommandId = Uuid.v5 ScenarioKit.fixtureNamespace (env.RunId + "/" + local.InvestigationId + "/" + commandName)
              Investigation = Id.create (Guid.Parse local.InvestigationId)
              ExpectedVersion = state |> Option.map (fun s -> s.Version)
              Actor = env.Human
              RequestedAt = now ()
              Command = command }
        match Decide.execute state envelope with
        | Ok events -> { local with Pending = local.Pending @ events }
        | Error errors -> failwithf "engine refused %s: %s" commandName (String.Join("; ", errors |> List.map (fun e -> e.Code)))

    let private save (env: Env) (local: LocalState) = Sync.save env.Backend (fun _ -> false) env.Set local

    let private changedPaths (env: Env) (parent: string) (commit: string) =
        task {
            match! env.Backend.ChangedPaths parent commit with
            | Ok ps -> return List.sort ps
            | Error _ -> return [ "<compare failed>" ]
        }

    let private prefixOf (local: LocalState) = Paths.investigation local.RunId local.InvestigationId |> Result.defaultWith (fun e -> failwith e.Detail)

    let private head (env: Env) =
        task {
            match! env.Backend.Head() with
            | Ok t -> return t
            | Error _ -> return failwith "cannot read branch head"
        }

    let private remoteEvents (env: Env) (local: LocalState) =
        task {
            match! Sync.load env.Backend env.Set (prefixOf local) local.InvestigationId with
            | Ok r -> return r.Files |> Option.map (fun f -> f.Events)
            | Error _ -> return None
        }

    let private newInvestigation (env: Env) (name: string) title =
        let id = Uuid.text (Uuid.v5 ScenarioKit.fixtureNamespace (env.RunId + "/investigation/" + name))
        let local = { RunId = env.RunId; InvestigationId = id; Synced = None; SyncedToken = None; Pending = [] }
        let started =
            decide env local
                (StartInvestigation(Id.create (Guid.Parse id), title, Id.create (Uuid.v5 ScenarioKit.fixtureNamespace (env.RunId + "/" + name + "/obs0")),
                                    "Synthetic live persistence test investigation created by run " + env.RunId + "."))
                "start"
        decide env started ActivateInvestigation "activate"

    let private observe (env: Env) (local: LocalState) (tag: string) =
        decide env local
            (RecordObservation(Id.create (Uuid.v5 ScenarioKit.fixtureNamespace (env.RunId + "/" + local.InvestigationId + "/obs/" + tag)), "Synthetic observation " + tag + ".", None))
            ("observe-" + tag)

    /// Backend wrapper: run `before` once, just before the first publish is attempted.
    let private interceptPublishOnce (inner: Backend) (before: unit -> Task<unit>) : Backend =
        let fired = ref false
        { inner with
            Publish =
                fun stop token prefix files message ->
                    task {
                        if not fired.Value then
                            fired.Value <- true
                            do! before ()
                        return! inner.Publish stop token prefix files message
                    } }

    /// Backend wrapper: the first publish is applied by GitHub but its confirmation is lost.
    let private loseConfirmationOnce (inner: Backend) : Backend =
        let fired = ref false
        { inner with
            Publish =
                fun stop token prefix files message ->
                    task {
                        let! r = inner.Publish stop token prefix files message
                        if not fired.Value then
                            fired.Value <- true
                            return Error(Network "injected lost response after the remote applied the update")
                        else return r
                    } }

    /// Backend wrapper: the first `count` head reads fail with a network error.
    let private failFirstHeads (inner: Backend) (count: int) : Backend =
        let n = ref 0
        { inner with
            Head =
                fun () ->
                    task {
                        n.Value <- n.Value + 1
                        if n.Value <= count then return Error(Network("injected network failure #" + string n.Value))
                        else return! inner.Head()
                    } }

    let private countingBackend (inner: Backend) (counter: int ref) : Backend =
        let tick () = counter.Value <- counter.Value + 1
        { inner with
            Head = fun () -> tick (); inner.Head()
            Publish = fun a b c d e -> tick (); inner.Publish a b c d e }

    /// Phase A: create, reload, append, and leave a pending event on disk for the restart test.
    let phaseA (env: Env) : Task<TestResult list * LocalState> =
        task {
            let x0 = newInvestigation env "x" "Live persistence test investigation X"
            let! startHead = head env
            let! created = save env x0
            let r1 =
                if isSynced created.Outcome then pass "create-investigation" (outcomeText created.Outcome) (Option.toList created.Commit)
                else failT "create-investigation" (outcomeText created.Outcome) []
            let! createdPaths = match created.Commit with Some c -> changedPaths env startHead.HeadCommit c | None -> Task.FromResult []
            let prefix = prefixOf x0
            let expectedPaths = Store.fileNames |> List.map (fun f -> prefix + f) |> List.sort
            let r2 =
                if createdPaths = expectedPaths then pass "atomic-create-single-commit" ("one commit adds exactly: " + String.Join(", ", createdPaths)) (Option.toList created.Commit)
                else failT "atomic-create-single-commit" (String.Join(", ", createdPaths)) (Option.toList created.Commit)
            // Fresh reader with no local state: load the complete history from GitHub.
            let! fresh = Sync.load env.Rest env.Set prefix x0.InvestigationId
            let r3 =
                match fresh with
                | Ok { Loaded = Some l } when l.Events.Length = 2 && l.State.Lifecycle = Active ->
                    pass "load-investigation-and-history" (sprintf "fresh REST reader (no local state) loaded %d events, lifecycle active, state %s" l.Events.Length l.StateSha256) []
                | Ok r -> failT "load-investigation-and-history" (sprintf "unexpected: %A" (r.Loaded |> Option.map (fun l -> l.Events.Length))) []
                | Error e -> failT "load-investigation-and-history" (sprintf "%A" e) []
            let x1 = observe env created.Local "a1"
            let before = created.Local.SyncedToken |> Option.map (fun t -> t.HeadCommit) |> Option.defaultValue ""
            let! appended = save env x1
            let! appendPaths = match appended.Commit with Some c -> changedPaths env before c | None -> Task.FromResult []
            let r4 =
                if isSynced appended.Outcome && appendPaths = expectedPaths then
                    pass "append-events-atomic-manifest-snapshot" ("one commit updates exactly: " + String.Join(", ", appendPaths)) (Option.toList appended.Commit)
                else failT "append-events-atomic-manifest-snapshot" (outcomeText appended.Outcome + " " + String.Join(",", appendPaths)) (Option.toList appended.Commit)
            // Leave one pending, unsaved event on disk; phase B runs in a new process.
            let pending = observe env appended.Local "restart"
            Local.save env.LocalRoot pending
            return [ r1; r2; r3; r4 ], pending
        }

    /// Phase B (separate OS process): restart recovery, concurrency, faults, safety.
    let phaseB (env: Env) (investigationId: string) (fixtureCommit: string) : Task<TestResult list> =
        task {
            let results = Collections.Generic.List<TestResult>()
            let add r = results.Add r
            match Local.load env.LocalRoot env.RunId investigationId with
            | None -> return [ failT "reload-after-restart" "no local state on disk" [] ]
            | Some restored ->
                // ---- restart recovery ----
                let! resumed = save env restored
                add (if isSynced resumed.Outcome && restored.Pending.Length = 1 then pass "reload-after-restart" ("pending event restored from disk in a new process and saved: " + outcomeText resumed.Outcome) (Option.toList resumed.Commit)
                     else failT "reload-after-restart" (sprintf "pending=%d %s" restored.Pending.Length (outcomeText resumed.Outcome)) [])
                let history = Collections.Generic.List<byte[]>()
                let! e0 = remoteEvents env resumed.Local
                e0 |> Option.iter history.Add
                // ---- concurrent modification / stale write / pending preserved ----
                let a = observe env resumed.Local "client-a"
                let b = observe env resumed.Local "client-b"
                let! savedA = save env a
                let! savedB = save env b
                Local.save env.LocalRoot { savedB.Local with InvestigationId = savedB.Local.InvestigationId }
                let reloadedB = Local.load env.LocalRoot env.RunId investigationId
                let! afterConflict = remoteEvents env resumed.Local
                afterConflict |> Option.iter history.Add
                let bPreserved = reloadedB |> Option.exists (fun l -> l.Pending = b.Pending)
                add (match savedA.Outcome, savedB.Outcome with
                     | SyncOutcome.Synced _, SyncOutcome.Conflict d when bPreserved -> pass "detect-concurrent-modification" ("client A synced; client B got SyncOutcome.Conflict (" + d + "); B's pending event preserved on disk") (Option.toList savedA.Commit)
                     | oa, ob -> failT "detect-concurrent-modification" (outcomeText oa + " / " + outcomeText ob + sprintf " preserved=%b" bPreserved) [])
                let aEvents = savedA.Local.Synced |> Option.map (fun f -> f.Events)
                add (if afterConflict = aEvents then pass "conflict-never-overwrites" "remote history equals client A's write after B's refused save" []
                     else failT "conflict-never-overwrites" "remote history differs from client A's write" [])
                // Raw stale publish: a commit built on the pre-A head must be refused by the non-forced ref update.
                let staleToken = resumed.Local.SyncedToken |> Option.get
                let! stale = env.Backend.Publish (fun _ -> false) staleToken (prefixOf b) [ prefixOf b + "events.ndjson", [| 0x7Buy; 0x7Duy; 0x0Auy |] ] "causa: stale write probe"
                let! headAfterStale = head env
                add (match stale with
                     | Error(NotFastForward d) when Some headAfterStale.HeadCommit = (savedA.Local.SyncedToken |> Option.map (fun t -> t.HeadCommit)) ->
                         pass "reject-stale-write" ("provider refused stale-base ref update: " + d) []
                     | other -> failT "reject-stale-write" (sprintf "%A" other) [])
                let semanticStale =
                    match savedA.Local.Synced |> Option.map (Store.validate env.Set) with
                    | Some(Ok loadedA) -> Store.append env.Set loadedA (loadedA.State.Version - 1L) b.Pending
                    | _ -> CausaError.fail "X" "no state"
                add (match semanticStale with
                     | Error e when e.Code = Codes.versionStale -> pass "reject-stale-expected-version" e.Detail []
                     | other -> failT "reject-stale-expected-version" (sprintf "%A" other) [])
                // ---- rebase pending onto the newer remote, then save ----
                let! remoteNow = Sync.load env.Backend env.Set (prefixOf b) investigationId
                let rebased = match remoteNow with Ok r -> Sync.rebase env.Set (reloadedB |> Option.get) r | Error e -> CausaError.fail "X" (sprintf "%A" e)
                let! savedRebased =
                    match rebased with
                    | Ok l -> save env l
                    | Error e -> Task.FromResult { Sync.Outcome = SyncOutcome.Refused e; Sync.Local = b; Sync.Commit = None; Sync.Attempts = 0 }
                let! afterRebase = remoteEvents env resumed.Local
                afterRebase |> Option.iter history.Add
                add (if isSynced savedRebased.Outcome then pass "pending-preserved-and-rebased" ("B's preserved command was re-decided on A's state and saved: " + outcomeText savedRebased.Outcome) (Option.toList savedRebased.Commit)
                     else failT "pending-preserved-and-rebased" (outcomeText savedRebased.Outcome) [])
                // ---- non-conflicting concurrent write to another investigation ----
                let y = newInvestigation env "y" "Live persistence test investigation Y"
                let current = savedRebased.Local
                let interfering = observe env current "interleaved"
                let interferingSave () = task { let! _ = save env interfering in return () }
                let! savedY = Sync.save (interceptPublishOnce env.Backend interferingSave) (fun _ -> false) env.Set y
                add (if isSynced savedY.Outcome && savedY.Attempts = 2 then pass "branch-moved-other-investigation-retry" ("ref update for Y hit a non-fast-forward caused by a write to X, re-checked Y unchanged, retried: attempts=2, " + outcomeText savedY.Outcome) (Option.toList savedY.Commit)
                     else failT "branch-moved-other-investigation-retry" (sprintf "attempts=%d %s" savedY.Attempts (outcomeText savedY.Outcome)) [])
                // ---- prevent partial semantic commits ----
                let! xNow = Sync.load env.Backend env.Set (prefixOf current) investigationId
                let xLocal = match xNow with Ok r -> { current with Synced = r.Files; SyncedToken = Some r.Token; Pending = [] } | Error _ -> current
                let partial = observe env xLocal "partial"
                let! h0 = head env
                let! stopped = Sync.save env.Backend (fun s -> s = BeforeRefUpdate) env.Set partial
                let! h1 = head env
                add (match stopped.Outcome with
                     | SyncOutcome.Pending _ when h0.HeadCommit = h1.HeadCommit -> pass "prevent-partial-semantic-commit" "blobs, tree and commit were created, ref update interrupted: branch head unchanged, outcome SyncOutcome.Pending (never Synced)" []
                     | o -> failT "prevent-partial-semantic-commit" (outcomeText o) [])
                // ---- temporary network failure, then recovery ----
                let! netFail = Sync.save (failFirstHeads env.Backend 1) (fun _ -> false) env.Set partial
                let! h2 = head env
                let! recovered = save env partial
                add (match netFail.Outcome, recovered.Outcome with
                     | SyncOutcome.Pending _, SyncOutcome.Synced _ when h2.HeadCommit = h1.HeadCommit -> pass "recover-after-network-failure" ("first attempt SyncOutcome.Pending (" + outcomeText netFail.Outcome + "), branch unchanged; retry " + outcomeText recovered.Outcome) (Option.toList recovered.Commit)
                     | o1, o2 -> failT "recover-after-network-failure" (outcomeText o1 + " / " + outcomeText o2) [])
                let! afterRecover = remoteEvents env resumed.Local
                afterRecover |> Option.iter history.Add
                // ---- lost response: write applied, confirmation lost; retry must not duplicate ----
                let lost = observe env recovered.Local "lost-response"
                let! lostAttempt = Sync.save (loseConfirmationOnce env.Backend) (fun _ -> false) env.Set lost
                let! h3 = head env
                let! lostRetry = save env lostAttempt.Local
                let! h4 = head env
                add (match lostAttempt.Outcome, lostRetry.Outcome with
                     | SyncOutcome.Pending _, SyncOutcome.Synced c when h3.HeadCommit = h4.HeadCommit && c = h4.HeadCommit ->
                         pass "lost-response-reconciled-without-duplicate" ("write landed but response lost -> Pending; retry recognised remote == synced + pending -> SyncOutcome.Synced at " + c + " with no second commit") [ c ]
                     | o1, o2 -> failT "lost-response-reconciled-without-duplicate" (outcomeText o1 + " / " + outcomeText o2) [])
                let! afterLost = remoteEvents env resumed.Local
                afterLost |> Option.iter history.Add
                // ---- unauthorized and invalid paths ----
                use badClient = new HttpClient()
                let badRest = Provider.rest env.RestConfig (Http.transport env.RestConfig "invalid-token-for-negative-test" badClient)
                let extra = observe env lostRetry.Local "unauthorized"
                let! unauth = Sync.save badRest (fun _ -> false) env.Set extra
                let! h5 = head env
                add (match unauth.Outcome with
                     | SyncOutcome.AuthFailed d when h5.HeadCommit = h4.HeadCommit -> pass "reject-unauthorized" ("bad credentials -> SyncOutcome.AuthFailed (" + d + "); nothing written") []
                     | o -> failT "reject-unauthorized" (outcomeText o) [])
                let calls = ref 0
                let probes =
                    [ "../fixtures", investigationId; "UPPER", investigationId; "ok-run", "../../fixtures/v1.0.0"; "ok-run", investigationId.ToUpperInvariant() ]
                let! pathResults =
                    probes
                    |> List.map (fun (run, id) -> Sync.save (countingBackend env.Backend calls) (fun _ -> false) env.Set { extra with RunId = run; InvestigationId = id })
                    |> Task.WhenAll
                add (if pathResults |> Array.forall (fun r -> match r.Outcome with SyncOutcome.Refused e -> e.Code = Codes.pathInvalid | _ -> false) && calls.Value = 0 then
                         pass "reject-invalid-paths" (sprintf "%d traversal/case probes refused with CAUSA.PATH_INVALID before any request" probes.Length) []
                     else failT "reject-invalid-paths" (sprintf "calls=%d %A" calls.Value (pathResults |> Array.map (fun r -> outcomeText r.Outcome))) [])
                let! headForGuard = head env
                let! guard = env.Backend.Publish (fun _ -> false) headForGuard (prefixOf extra) [ "fixtures/v1.0.0/manifest.json", [| 0x7Buy; 0x7Duy; 0x0Auy |] ] "causa: namespace guard probe"
                let! h6 = head env
                add (match guard with
                     | Error(Other d) when d.StartsWith "refusing to publish" && h6.HeadCommit = headForGuard.HeadCommit -> pass "refuse-write-outside-workspace" d []
                     | other -> failT "refuse-write-outside-workspace" (sprintf "%A" other) [])
                // ---- historical preservation and frozen fixtures ----
                let prefixes = history |> Seq.pairwise |> Seq.forall (fun (older, newer) -> newer.Length >= older.Length && Array.sub newer 0 older.Length = older)
                add (if prefixes && history.Count >= 4 then pass "preserve-historical-events" (sprintf "%d successive remote histories, each a byte prefix of the next" history.Count) []
                     else failT "preserve-historical-events" (sprintf "count=%d prefixes=%b" history.Count prefixes) [])
                let! tokenNow = head env
                let! fixturesNow = env.Backend.ListUnder tokenNow.TreeSha "fixtures/"
                let! fixtureTree = env.Backend.TreeOf fixtureCommit
                match fixtureTree with
                | Error f -> add (failT "canonical-fixtures-unmutated" (sprintf "%A" f) [])
                | Ok tree ->
                    let! fixturesAtBase = env.Backend.ListUnder tree "fixtures/"
                    add (match fixturesNow, fixturesAtBase with
                         | Ok now', Ok base' when now' = base' && not base'.IsEmpty ->
                             pass "canonical-fixtures-unmutated" (sprintf "%d fixture blobs identical at workspace head %s and fixture commit %s" base'.Length tokenNow.HeadCommit fixtureCommit) []
                         | a', b' -> failT "canonical-fixtures-unmutated" (sprintf "%A vs %A" (a' |> Result.map List.length) (b' |> Result.map List.length)) [])
                    // ---- frozen fixtures read back from GitHub at the fixture commit ----
                    let rootManifest = File.ReadAllText(Path.Combine(env.RepoRoot, "fixtures", "v1.0.0", "manifest.json")).TrimEnd('\n') |> Json.parse |> Result.defaultWith failwith
                    let entries = match Negatives.get [ "investigations" ] rootManifest with JArr xs -> xs | _ -> []
                    let token = { HeadCommit = fixtureCommit; TreeSha = tree }
                    let! loads =
                        entries
                        |> List.map (fun e ->
                            let id = match Negatives.get [ "investigationId" ] e with JStr s -> s | _ -> ""
                            let want = match Negatives.get [ "expectedStateSha256" ] e with JStr s -> s | _ -> ""
                            task {
                                let! r = Sync.loadAt env.Rest env.Set token ("fixtures/v1.0.0/investigations/" + id + "/") id
                                return match r with
                                       | Ok { Loaded = Some l } -> l.StateSha256 = want
                                       | _ -> false
                            })
                        |> Task.WhenAll
                    add (if loads.Length = 5 && Array.forall id loads then pass "load-frozen-fixtures-from-github-rest" (sprintf "all %d frozen investigations loaded through the GitHub REST API at %s, validated and replayed to their expected state digests" loads.Length fixtureCommit) []
                         else failT "load-frozen-fixtures-from-github-rest" (sprintf "%A" loads) [])
                return results |> List.ofSeq
        }

    let resultJson (header: (string * JValue) list) (rs: TestResult list) =
        JObj(
            header
            @ [ "results",
                JArr(rs |> List.map (fun r -> JObj [ "name", JStr r.Name; "passed", JBool r.Passed; "detail", JStr r.Detail; "commits", JArr(r.Commits |> List.map JStr) ])) ])

    /// Workspace writes use the existing branch; this only reports its head.
    let ensureBranch (env: Env) (_fromCommit: string) =
        task {
            match! env.Backend.Head() with
            | Ok t -> return "branch head before run " + t.HeadCommit
            | Error f -> return failwithf "cannot read branch head: %A" f
        }
