module Causa.Tool.Program

open System
open System.IO
open Causa.Contract

let private schemasOrExit root =
    match Store.loadSchemas (Layout.schemas root) with
    | Ok s -> s
    | Error e ->
        eprintfn "schema bundle unusable: %s %s" e.Code e.Detail
        exit 2

let private generateInto (root: string) =
    let set = schemasOrExit root
    match Dataset.generate set with
    | Error e ->
        eprintfn "generation failed: %s" e
        exit 1
    | Ok g ->
        Dataset.write root set g
        g.Runs |> List.iter (fun r -> printfn "%-22s commands=%d events=%d refusals=%d" r.Scenario.Key r.Scenario.Steps.Length r.Events.Length r.Refusals.Length)

/// Every generated path, relative to the repository root.
let private generatedFiles (root: string) =
    [ Layout.bundle root; Layout.rootManifest root ]
    @ (Directory.GetFiles(Layout.golden root) |> List.ofArray)
    @ (Directory.GetFiles(Layout.investigations root, "*", SearchOption.AllDirectories) |> List.ofArray)
    @ (Directory.GetFiles(Layout.negative root) |> List.ofArray)
    |> List.map (fun p -> Layout.posix (Path.GetRelativePath(root, p)))
    |> List.sort

let private printChecks (checks: Dataset.Check list) =
    checks |> List.iter (fun c -> printfn "%s %s — %s" (if c.Passed then "PASS" else "FAIL") c.Name c.Detail)
    let failed = checks |> List.filter (fun c -> not c.Passed)
    printfn "checks=%d passed=%d failed=%d" checks.Length (checks.Length - failed.Length) failed.Length
    if failed.IsEmpty then 0 else 1

let private copyDir (src: string) (dst: string) =
    Directory.GetFiles(src, "*", SearchOption.AllDirectories)
    |> Array.iter (fun f ->
        let target = Path.Combine(dst, Path.GetRelativePath(src, f))
        Directory.CreateDirectory(Path.GetDirectoryName target |> nonNull) |> ignore
        File.Copy(f, target, true))

/// Regenerate from the committed schemas into a scratch tree and require byte-identical output.
let private reproduce (root: string) =
    let scratch = Path.Combine(Path.GetTempPath(), "causa-reproduce-" + string Environment.ProcessId)
    if Directory.Exists scratch then Directory.Delete(scratch, true)
    copyDir (Layout.schemas root) (Layout.schemas scratch)
    File.Delete(Layout.bundle scratch)
    generateInto scratch
    let committed = generatedFiles root
    let regenerated = generatedFiles scratch
    let diffs =
        (Set.union (Set.ofList committed) (Set.ofList regenerated))
        |> Set.toList
        |> List.filter (fun rel ->
            let a, b = Path.Combine(root, rel), Path.Combine(scratch, rel)
            not (File.Exists a && File.Exists b && File.ReadAllBytes a = File.ReadAllBytes b))
    Directory.Delete(scratch, true)
    diffs |> List.iter (printfn "DIFF %s")
    printfn "reproduce: files=%d differing=%d" committed.Length diffs.Length
    if diffs.IsEmpty then 0 else 1

let private githubLive (phase: string) (root: string) (runId: string) (fixtureCommit: string) (investigationId: string option) =
    match Environment.GetEnvironmentVariable "GITHUB_TOKEN" |> Option.ofObj |> Option.filter (String.IsNullOrEmpty >> not) with
    | None ->
        eprintfn "GITHUB_TOKEN is not set"
        2
    | Some token ->
        let branch =
            Environment.GetEnvironmentVariable "CAUSA_LIVE_BRANCH" |> Option.ofObj |> Option.defaultValue "claude/causa-storage-contract-v1-8aymdx"
        let config : Causa.GitHub.Config = { Owner = "kemiller2002"; Repository = "causa-data"; Branch = branch; ApiBase = "https://api.github.com" }
        use client = new Net.Http.HttpClient()
        let rest = Causa.GitHub.Provider.rest config (Causa.GitHub.Http.transport config token client)
        let scratch = Path.Combine(root, ".local", "git-backend", runId + "-" + phase + "-" + string Environment.ProcessId + ".git")
        // CAUSA_WRITE_BACKEND=rest writes through the Git Data API (the browser application's path);
        // the default git smart-HTTP backend exists because some sandboxes deny REST writes.
        let git =
            match Environment.GetEnvironmentVariable "CAUSA_WRITE_BACKEND" with
            | "rest" -> rest
            | _ -> (Causa.GitHub.GitBackend.create scratch "https://github.com/kemiller2002/causa-data" branch).GetAwaiter().GetResult()
        let env : LiveTests.Env =
            { Backend = git; Rest = rest; RestConfig = config; Set = schemasOrExit root; RunId = runId
              LocalRoot = Path.Combine(root, ".local", "github-live"); RepoRoot = root; Human = IndyInitRootCauseInvestigator.Engine.Actor.human "live-test-operator" }
        let started = DateTimeOffset.UtcNow
        let branchState = (LiveTests.ensureBranch env fixtureCommit).GetAwaiter().GetResult()
        let results, extra =
            match phase, investigationId with
            | "a", _ ->
                let rs, local = (LiveTests.phaseA env).GetAwaiter().GetResult()
                rs, [ "investigationId", JStr local.InvestigationId ]
            | "b", Some id -> (LiveTests.phaseB env id fixtureCommit).GetAwaiter().GetResult(), [ "investigationId", JStr id ]
            | _ -> failwith "usage: github-live (a|b) <root> <runId> <fixtureCommit> [investigationId]"
        let header =
            [ "phase", JStr phase; "runId", JStr runId; "repository", JStr "kemiller2002/causa-data"; "branch", JStr config.Branch
              "writeBackend", JStr git.Name; "readBackend", JStr rest.Name; "branchSetup", JStr branchState; "fixtureCommit", JStr fixtureCommit; "githubApiVersion", JStr Causa.GitHub.Http.apiVersion
              "processId", JInt(int64 Environment.ProcessId)
              "startedAt", JStr(started.ToString("O")); "finishedAt", JStr(DateTimeOffset.UtcNow.ToString("O")) ] @ extra
        let out = Path.Combine(root, ".local", "github-live", runId, "results-" + phase + ".json")
        Directory.CreateDirectory(Path.GetDirectoryName out |> nonNull) |> ignore
        File.WriteAllBytes(out, Json.fileBytes (LiveTests.resultJson header results) |> Result.defaultWith failwith)
        results |> List.iter (fun r -> printfn "%s %s — %s %s" (if r.Passed then "PASS" else "FAIL") r.Name r.Detail (String.Join(" ", r.Commits)))
        extra |> List.iter (fun (k, v) -> printfn "%s=%s" k (match v with JStr s -> s | _ -> ""))
        if results |> List.forall (fun r -> r.Passed) then 0 else 1

let private historicalBuild (root: string) =
    let set = schemasOrExit root
    match Historical.Catalog.buildAll set root Historical.Cases.all with
    | Error e ->
        eprintfn "historical catalog build failed: %s" e
        1
    | Ok built ->
        Historical.Catalog.write root built
        built |> List.iter (fun b ->
            printfn "%-20s commands=%d events=%d evidence=%d lifecycle=%s refusals=%d" b.Case.Key b.Case.Steps.Length b.Events.Length
                b.Loaded.State.Evidence.Count (Enums.lifecycle.ToWire b.Loaded.State.Lifecycle) b.Refusals.Length
            b.Refusals |> List.iter (fun (i, code, all) -> printfn "  refused step %d as expected: %s (%s)" i code all))
        0

let private historicalVerify (root: string) (report: string option) =
    let set = schemasOrExit root
    let checks = Historical.CatalogVerify.verify root (Historical.Catalog.buildAll set root Historical.Cases.all)
    report |> Option.iter (fun path ->
        let json =
            JObj [ "checks", JArr(checks |> List.map (fun c -> JObj [ "name", JStr c.Name; "passed", JBool c.Passed; "detail", JStr c.Detail ]))
                   "passed", JInt(checks |> List.filter (fun c -> c.Passed) |> List.length |> int64)
                   "failed", JInt(checks |> List.filter (fun c -> not c.Passed) |> List.length |> int64) ]
        File.WriteAllBytes(path, Json.fileBytes json |> Result.defaultWith failwith))
    printChecks checks

let private option name (args: string list) =
    args
    |> List.tryFindIndex ((=) name)
    |> Option.bind (fun index -> args |> List.tryItem (index + 1))

let private workspaceRoot args = option "--root" args |> Option.defaultValue "." |> Path.GetFullPath

let private printJson value =
    printfn "%s" (Json.canonical value |> Result.defaultWith failwith)
    0

let private workspaceError (error: CausaError) =
    eprintfn "%s %s" error.Code error.Detail
    1

let private runWorkspaceVerify args =
    match option "--workspace" args, option "--template" args with
    | Some workspace, Some template ->
        match option "--mode" args with
        | None | Some "initialized" ->
            Workspace.verifyWorkspace (workspaceRoot args) workspace template WorkspaceVerificationMode.Initialized
            |> Result.map (Workspace.verificationJson >> printJson)
            |> Result.defaultWith workspaceError
        | Some "modified" ->
            Workspace.verifyWorkspace (workspaceRoot args) workspace template WorkspaceVerificationMode.Modified
            |> Result.map (Workspace.verificationJson >> printJson)
            |> Result.defaultWith workspaceError
        | Some mode -> eprintfn "unsupported workspace verification mode: %s" mode; 2
    | _ -> eprintfn "workspace-verify requires --workspace NAME --template NAME [--mode initialized|modified] [--root PATH]"; 2

let private runWorkspaceCreate args =
    match option "--workspace" args, option "--template" args with
    | Some workspace, Some template ->
        Workspace.createWorkspace (workspaceRoot args) workspace template
        |> Result.map (Workspace.verificationJson >> printJson)
        |> Result.defaultWith workspaceError
    | _ -> eprintfn "workspace-create requires --workspace NAME --template NAME [--root PATH]"; 2

let private runRehearsalInit args =
    match option "--number" args, option "--template" args with
    | Some number, Some template ->
        match Workspace.rehearsalName number with
        | Error error -> workspaceError error
        | Ok name ->
            match Workspace.createWorkspace (workspaceRoot args) name template with
            | Error error -> workspaceError error
            | Ok verification ->
                let receipt = Workspace.rehearsalReceipt number verification
                let path =
                    option "--receipt" args
                    |> Option.map Path.GetFullPath
                    |> Option.defaultValue (Path.Combine(workspaceRoot args, ".local", "rehearsal-receipts", name + ".json"))
                Directory.CreateDirectory(Path.GetDirectoryName path |> nonNull) |> ignore
                File.WriteAllBytes(path, Json.fileBytes receipt |> Result.defaultWith failwith)
                printJson receipt
    | _ -> eprintfn "rehearsal-init requires --number N --template NAME [--receipt PATH] [--root PATH]"; 2

let private runWorkspacePublish args =
    match option "--workspace" args, option "--template" args, option "--repository" args, option "--branch" args with
    | Some workspace, Some template, Some repository, Some branch ->
        Workspace.publishWorkspace (workspaceRoot args) workspace template repository branch
        |> fun operation -> operation.GetAwaiter().GetResult()
        |> Result.map (Workspace.publicationJson >> printJson)
        |> Result.defaultWith workspaceError
    | _ -> eprintfn "workspace-publish requires --workspace NAME --template NAME --repository OWNER/NAME --branch NAME [--root PATH]"; 2

let private runRemoteCheck args =
    match option "--workspace" args, option "--repository" args, option "--branch" args with
    | Some workspace, Some repository, Some branch ->
        Workspace.checkRemoteAbsent (workspaceRoot args) workspace repository branch
        |> fun operation -> operation.GetAwaiter().GetResult()
        |> Result.map (fun () -> JObj [ "workspaceIdentifier", JStr workspace; "repository", JStr repository; "branch", JStr branch; "remoteDestinationResult", JStr "ABSENT" ] |> printJson)
        |> Result.defaultWith workspaceError
    | _ -> eprintfn "workspace-remote-check requires --workspace NAME --repository OWNER/NAME --branch NAME [--root PATH]"; 2

let private runApplicationVerify args =
    match option "--workspace" args, option "--template" args with
    | Some workspace, Some template ->
        Workspace.verifyApplicationCompatibility (workspaceRoot args) workspace template
        |> Result.map (Workspace.applicationJson >> printJson)
        |> Result.defaultWith workspaceError
    | _ -> eprintfn "workspace-application-verify requires --workspace NAME --template NAME [--root PATH]"; 2

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    | "template-create" :: args when option "--template" args = Some Workspace.templateId ->
        Workspace.createHistoricalTemplate (workspaceRoot args)
        |> Result.map (fun template -> JObj [ "templateIdentifier", JStr template.TemplateId; "templateDigest", JStr template.Digest; "result", JStr "CREATED" ] |> printJson)
        |> Result.defaultWith workspaceError
    | "template-verify" :: args ->
        match option "--template" args with
        | Some template ->
            Workspace.verifyTemplate (workspaceRoot args) template
            |> Result.map (fun verified ->
                JObj
                    [ "templateIdentifier", JStr verified.TemplateId
                      "templateVersion", JStr verified.TemplateVersion
                      "templateDigest", JStr verified.Digest
                      "storageContractVersion", JStr verified.StorageContractVersion
                      "sourceRepository", JStr verified.SourceRepository
                      "sourceCommit", JStr verified.SourceCommit
                      "investigationCount", JInt(int64 verified.Investigations.Length)
                      "investigationIdentifiers", JArr(verified.Investigations |> List.map (fun investigation -> JStr investigation.InvestigationId))
                      "result", JStr "VERIFIED" ]
                |> printJson)
            |> Result.defaultWith workspaceError
        | None -> eprintfn "template-verify requires --template NAME [--root PATH]"; 2
    | "workspace-create" :: args -> runWorkspaceCreate args
    | "workspace-verify" :: args -> runWorkspaceVerify args
    | "workspace-remote-check" :: args -> runRemoteCheck args
    | "workspace-application-verify" :: args -> runApplicationVerify args
    | "workspace-publish" :: args -> runWorkspacePublish args
    | "rehearsal-init" :: args -> runRehearsalInit args
    | [ "generate"; root ] -> generateInto (Path.GetFullPath root); 0
    | [ "verify"; root ] -> Dataset.verify (Path.GetFullPath root) |> printChecks
    | [ "reproduce"; root ] -> reproduce (Path.GetFullPath root)
    | [ "validate-dir"; root; dir ] ->
        // One investigation directory through every reader stage (incl. History.validate); prints the primary code.
        match Store.readDirectory (Path.GetFullPath dir) |> Result.bind (Store.validate (schemasOrExit (Path.GetFullPath root))) with
        | Ok loaded -> printfn "VALID %s events=%d state=%s" (Uuid.text loaded.InvestigationId) loaded.Events.Length loaded.StateSha256; 0
        | Error e -> printfn "INVALID %s%s %s" e.Code (e.DomainCode |> Option.map (fun d -> "/" + d) |> Option.defaultValue "") e.Detail; 1
    | [ "historical-try"; root; key ] ->
        // Build one case in memory through the engine and report; nothing is written.
        let full = Path.GetFullPath root
        match Historical.Cases.all |> List.tryFind (fun d -> d.Key = key) with
        | None -> eprintfn "unknown case %s" key; 2
        | Some d ->
            match Historical.Catalog.buildCase (schemasOrExit full) full d with
            | Error e -> eprintfn "%s" e; 1
            | Ok b ->
                printfn "%s commands=%d events=%d evidence=%d lifecycle=%s" key b.Case.Steps.Length b.Events.Length b.Loaded.State.Evidence.Count (Enums.lifecycle.ToWire b.Loaded.State.Lifecycle)
                b.Refusals |> List.iter (fun (i, code, all) -> printfn "  refused step %d as expected: %s (%s)" i code all)
                0
    | [ "historical-build"; root ] -> historicalBuild (Path.GetFullPath root)
    | [ "historical-verify"; root ] -> historicalVerify (Path.GetFullPath root) None
    | [ "historical-verify"; root; report ] -> historicalVerify (Path.GetFullPath root) (Some(Path.GetFullPath report))
    | [ "lock"; root ] ->
        Gate.writeLock (Path.GetFullPath root)
        printfn "wrote %s" (Gate.lockPath (Path.GetFullPath root))
        0
    | "gate" :: root :: rest ->
        let full = Path.GetFullPath root
        let anchored =
            match rest with
            | [ "--anchor"; tag ] -> Gate.anchorFailures full tag
            | _ when File.Exists(Gate.releaseMarker full) ->
                [ { Gate.Rule = "anchor.required"; Gate.Detail = "compatibility/v1.0.0/RELEASE exists: the gate must run with --anchor <release tag>" } ]
            | _ -> []
        let failures = Gate.run full Gate.LoadedByThisProcess @ anchored
        printfn "%s" (Json.canonical (Gate.report failures) |> Result.defaultValue "")
        if failures.IsEmpty then 0 else 1
    | [ "gate-selftest"; root ] ->
        let results = Gate.selfTest (Path.GetFullPath root)
        results |> List.iter (fun (name, ok, detail) -> printfn "%s %s — %s" (if ok then "PASS" else "FAIL") name detail)
        if results |> List.forall (fun (_, ok, _) -> ok) then 0 else 1
    | [ "github-live"; phase; root; runId; fixtureCommit ] -> githubLive phase (Path.GetFullPath root) runId fixtureCommit None
    | [ "github-live"; phase; root; runId; fixtureCommit; id ] -> githubLive phase (Path.GetFullPath root) runId fixtureCommit (Some id)
    | _ ->
        eprintfn "usage: causa (generate|verify|reproduce|historical-build|historical-verify) <repository-root> | template-create|template-verify|workspace-create|workspace-verify|workspace-remote-check|workspace-publish|workspace-application-verify|rehearsal-init [options]"
        2
