module Causa.Workspace.Tests

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Threading.Tasks
open Xunit
open IndyInitRootCauseInvestigator.Engine
open Causa.Contract
open Causa.GitHub
open Causa.Tool

let private repoRoot =
    let rec up (directory: DirectoryInfo) =
        if File.Exists(Path.Combine(directory.FullName, "engine.lock.json")) then directory.FullName
        else up (directory.Parent |> nonNull)
    up (DirectoryInfo(AppContext.BaseDirectory))

let private copyDirectory source destination =
    Directory.CreateDirectory destination |> ignore
    for file in Directory.GetFiles(source, "*", SearchOption.AllDirectories) do
        let target = Path.Combine(destination, Path.GetRelativePath(source, file))
        Directory.CreateDirectory(Path.GetDirectoryName target |> nonNull) |> ignore
        File.Copy(file, target)

let private withRoot includeTemplate action =
    let root = Path.Combine(Path.GetTempPath(), $"causa-workspace-tests-{Guid.NewGuid():N}")
    Directory.CreateDirectory root |> ignore
    copyDirectory (Path.Combine(repoRoot, "contract")) (Path.Combine(root, "contract"))
    if includeTemplate then copyDirectory (Path.Combine(repoRoot, "templates")) (Path.Combine(root, "templates"))
    try action root
    finally if Directory.Exists root then Directory.Delete(root, true)

let private schemas root = Store.loadSchemas (Layout.schemas root) |> Result.defaultWith (fun error -> failwith error.Detail)

let private directoryDigest path =
    Directory.GetFiles(path, "*", SearchOption.AllDirectories)
    |> Array.sort
    |> Array.map (fun file -> Path.GetRelativePath(path, file), Json.sha256Hex(File.ReadAllBytes file))
    |> Array.toList

let private writeFiles directory (files: InvestigationFiles) =
    File.WriteAllBytes(Path.Combine(directory, "manifest.json"), files.Manifest)
    File.WriteAllBytes(Path.Combine(directory, "events.ndjson"), files.Events)
    File.WriteAllBytes(Path.Combine(directory, "snapshot.json"), files.Snapshot)

let private runGit directory arguments =
    let start = ProcessStartInfo("git")
    start.WorkingDirectory <- directory
    start.RedirectStandardOutput <- true
    start.RedirectStandardError <- true
    arguments |> List.iter start.ArgumentList.Add
    use child = Process.Start start |> nonNull
    let output = child.StandardOutput.ReadToEnd()
    let error = child.StandardError.ReadToEnd()
    child.WaitForExit()
    if child.ExitCode <> 0 then failwith $"git {String.Join(' ', arguments)} failed: {output}{error}"

let private disposableRemote root =
    let remote = Path.Combine(root, "publication-remote.git")
    let seed = Path.Combine(root, "publication-seed")
    Directory.CreateDirectory seed |> ignore
    runGit root [ "init"; "--bare"; "-q"; remote ]
    runGit seed [ "init"; "-q" ]
    File.WriteAllText(Path.Combine(seed, "README.md"), "# disposable publication test\n", Encoding.UTF8)
    runGit seed [ "add"; "README.md" ]
    runGit seed [ "-c"; "user.name=causa-test"; "-c"; "user.email=causa-test@example.invalid"; "commit"; "-q"; "-m"; "seed" ]
    runGit seed [ "branch"; "-M"; "main" ]
    runGit seed [ "remote"; "add"; "origin"; remote ]
    runGit seed [ "push"; "-q"; "-u"; "origin"; "main" ]
    remote

[<Fact>]
let ``historical template is deterministic exact and engine-valid`` () =
    let verified = Workspace.verifyTemplate repoRoot Workspace.templateId |> Result.defaultWith (fun error -> failwith error.Detail)
    Assert.Equal("fbc776c2f4aa27d0cf2d6825ebf45b1c0c2c5219186179638818007d0b116425", verified.Digest)
    Assert.Equal(3, verified.Investigations.Length)
    for investigation in verified.Investigations do
        for file in Store.fileNames do
            let source = Path.Combine(repoRoot, Workspace.sourceDirectory, investigation.InvestigationId, file)
            let template = Path.Combine(repoRoot, "templates", Workspace.templateId, "investigations", investigation.InvestigationId, file)
            Assert.Equal<byte>(File.ReadAllBytes source, File.ReadAllBytes template)

[<Fact>]
let ``one-time template creation preserves every source byte`` () =
    withRoot false (fun root ->
        copyDirectory (Path.Combine(repoRoot, Workspace.sourceDirectory)) (Path.Combine(root, Workspace.sourceDirectory))
        let created = Workspace.createHistoricalTemplate root |> Result.defaultWith (fun error -> failwith error.Detail)
        Assert.Equal(3, created.Investigations.Length)
        Assert.True(Result.isError (Workspace.createHistoricalTemplate root), "an existing immutable template was replaced")
        let sourceDigest = directoryDigest (Path.Combine(root, Workspace.sourceDirectory))
        let templateDigest = directoryDigest (Path.Combine(root, "templates", Workspace.templateId, "investigations"))
        let identical = sourceDigest = templateDigest
        Assert.True(identical, "template bytes differ from the canonical historical workspace"))

[<Fact>]
let ``invalid template is rejected before workspace staging`` () =
    withRoot true (fun root ->
        let manifest = Path.Combine(root, "templates", Workspace.templateId, "template-manifest.json")
        File.AppendAllText(manifest, " ")
        Assert.True(Result.isError (Workspace.createWorkspace root "rehearsal-901" Workspace.templateId))
        let workspaceRoot = Path.Combine(root, "workspaces", "demo")
        if Directory.Exists workspaceRoot then Assert.Empty(Directory.GetFileSystemEntries workspaceRoot))

[<Fact>]
let ``workspace creation is exact rejects an existing destination and remains retry-safe`` () =
    withRoot true (fun root ->
        Assert.True(Result.isOk (Workspace.checkLocalAbsent root "development-001"))
        let created = Workspace.createWorkspace root "development-001" Workspace.templateId |> Result.defaultWith (fun error -> failwith error.Detail)
        Assert.Equal(3, created.InvestigationIds.Length)
        Assert.True(Result.isError (Workspace.checkLocalAbsent root "development-001"))
        let before = directoryDigest (Path.Combine(root, "workspaces", "demo", "development-001"))
        Assert.True(Result.isError (Workspace.createWorkspace root "development-001" Workspace.templateId))
        let after = directoryDigest (Path.Combine(root, "workspaces", "demo", "development-001"))
        let unchanged = before = after
        Assert.True(unchanged, "existing workspace changed after a rejected retry")
        Assert.True(Result.isOk (Workspace.verifyWorkspace root "development-001" Workspace.templateId WorkspaceVerificationMode.Initialized)))

[<Fact>]
let ``workspace identifiers reject emptiness traversal and separators`` () =
    withRoot true (fun root ->
        [ ""; "../escape"; "escape/child"; "Escape"; "%2e%2e" ]
        |> List.iter (fun name -> Assert.True(Result.isError (Workspace.createWorkspace root name Workspace.templateId), name)))

[<Fact>]
let ``workspace creation rejects a symbolic-link escape`` () =
    withRoot true (fun root ->
        let outside = Path.Combine(Path.GetTempPath(), $"causa-workspace-outside-{Guid.NewGuid():N}")
        Directory.CreateDirectory outside |> ignore
        try
            Directory.CreateSymbolicLink(Path.Combine(root, "workspaces"), outside) |> ignore
            Assert.True(Result.isError (Workspace.createWorkspace root "rehearsal-902" Workspace.templateId))
            Assert.False(Directory.Exists(Path.Combine(outside, "demo", "rehearsal-902")))
        finally
            if Directory.Exists outside then Directory.Delete(outside, true))

[<Fact>]
let ``two workspaces isolate an appended event from the template and canonical source`` () =
    withRoot true (fun root ->
        let templateBefore = directoryDigest (Path.Combine(root, "templates", Workspace.templateId))
        let canonicalBefore = directoryDigest (Path.Combine(repoRoot, Workspace.sourceDirectory))
        Workspace.createWorkspace root "isolation-001" Workspace.templateId |> Result.defaultWith (fun error -> failwith error.Detail) |> ignore
        Workspace.createWorkspace root "isolation-002" Workspace.templateId |> Result.defaultWith (fun error -> failwith error.Detail) |> ignore
        let id = "1784da3c-7141-59ee-a495-1d5d44bf2284"
        let directory name = Path.Combine(root, "workspaces", "demo", name, "investigations", id)
        let secondBefore = directoryDigest (Path.Combine(root, "workspaces", "demo", "isolation-002"))
        let currentFiles = Store.readDirectory (directory "isolation-001") |> Result.defaultWith (fun error -> failwith error.Detail)
        let current = Store.validate (schemas root) currentFiles |> Result.defaultWith (fun error -> failwith error.Detail)
        let envelope: CommandEnvelope =
            { CommandId = Guid.NewGuid()
              Investigation = Id.create current.InvestigationId
              ExpectedVersion = Some current.State.Version
              Actor = Actor.human "workspace-isolation-test"
              RequestedAt = Timestamp.truncateToMillisecond ((List.last current.Events).At.AddMinutes 1.0)
              Command = RecordObservation(Id.create(Guid.NewGuid()), "Workspace isolation append.", None) }
        let appended = Decide.execute (Some current.State) envelope |> Result.defaultWith (fun errors -> failwith (String.Join("; ", errors |> List.map _.Code)))
        let updated, _ = Store.append (schemas root) current current.State.Version appended |> Result.defaultWith (fun error -> failwith error.Detail)
        writeFiles (directory "isolation-001") updated
        Assert.True(Result.isOk (Workspace.verifyWorkspace root "isolation-001" Workspace.templateId WorkspaceVerificationMode.Modified))
        Assert.True(Result.isError (Workspace.verifyWorkspace root "isolation-001" Workspace.templateId WorkspaceVerificationMode.Initialized))
        Assert.True(Result.isOk (Workspace.verifyWorkspace root "isolation-002" Workspace.templateId WorkspaceVerificationMode.Initialized))
        let secondUnchanged = secondBefore = directoryDigest (Path.Combine(root, "workspaces", "demo", "isolation-002"))
        let templateUnchanged = templateBefore = directoryDigest (Path.Combine(root, "templates", Workspace.templateId))
        let canonicalUnchanged = canonicalBefore = directoryDigest (Path.Combine(repoRoot, Workspace.sourceDirectory))
        Assert.True(secondUnchanged, "second workspace changed")
        Assert.True(templateUnchanged, "immutable template changed")
        Assert.True(canonicalUnchanged, "canonical historical workspace changed"))

[<Fact>]
let ``modified verification rejects replacement of a historical event`` () =
    withRoot true (fun root ->
        Workspace.createWorkspace root "corruption-001" Workspace.templateId |> Result.defaultWith (fun error -> failwith error.Detail) |> ignore
        let id = "1784da3c-7141-59ee-a495-1d5d44bf2284"
        let events = Path.Combine(root, "workspaces", "demo", "corruption-001", "investigations", id, "events.ndjson")
        let bytes = File.ReadAllBytes events
        bytes[50] <- bytes[50] ^^^ 1uy
        File.WriteAllBytes(events, bytes)
        Assert.True(Result.isError (Workspace.verifyWorkspace root "corruption-001" Workspace.templateId WorkspaceVerificationMode.Modified)))

[<Fact>]
let ``rehearsal naming is explicit normalized and bounded`` () =
    Assert.Equal(Ok "rehearsal-006", Workspace.rehearsalName "006")
    Assert.Equal(Ok "rehearsal-007", Workspace.rehearsalName "7")
    Assert.True(Result.isError (Workspace.rehearsalName "0"))
    Assert.True(Result.isError (Workspace.rehearsalName "six"))

[<Fact>]
let ``publication is atomic remotely verified and refuses an existing destination`` () =
    withRoot true (fun root ->
        Workspace.createWorkspace root "publication-001" Workspace.templateId |> Result.defaultWith (fun error -> failwith error.Detail) |> ignore
        let remote = disposableRemote root
        let backend = GitBackend.create (Path.Combine(root, "publisher.git")) remote "main" |> _.GetAwaiter().GetResult()
        let receipt =
            Workspace.publishWithBackend root "publication-001" Workspace.templateId "local/disposable" "main" backend
            |> _.GetAwaiter().GetResult()
            |> Result.defaultWith (fun error -> failwith error.Detail)
        Assert.Matches("^[0-9a-f]{40}$", receipt.Commit)
        let correctIds = Workspace.historicalInvestigationIds = receipt.InvestigationIds
        Assert.True(correctIds, "publication receipt investigation order changed")
        let reread =
            Workspace.verifyPublishedWithBackend root "publication-001" Workspace.templateId "local/disposable" "main" receipt.Commit backend
            |> _.GetAwaiter().GetResult()
        Assert.True(Result.isOk reread, "published workspace could not be independently read back")
        let second = Workspace.publishWithBackend root "publication-001" Workspace.templateId "local/disposable" "main" backend |> _.GetAwaiter().GetResult()
        Assert.True(Result.isError second, "an existing remote workspace was accepted"))

[<Fact>]
let ``publication detects concurrent branch creation without a forced update`` () =
    withRoot true (fun root ->
        Workspace.createWorkspace root "publication-002" Workspace.templateId |> Result.defaultWith (fun error -> failwith error.Detail) |> ignore
        let remote = disposableRemote root
        let primary = GitBackend.create (Path.Combine(root, "primary.git")) remote "main" |> _.GetAwaiter().GetResult()
        let competitor = GitBackend.create (Path.Combine(root, "competitor.git")) remote "main" |> _.GetAwaiter().GetResult()
        let racing =
            { primary with
                Publish =
                    fun stop token prefix files message ->
                        task {
                            let! current = competitor.Head()
                            let current = current |> Result.defaultWith (fun failure -> failwith (string failure))
                            let! advanced = competitor.Publish (fun _ -> false) current "concurrent/" [ "concurrent/marker.txt", Encoding.UTF8.GetBytes "advance\n" ] "concurrent test advance"
                            advanced |> Result.defaultWith (fun failure -> failwith (string failure)) |> ignore
                            return! primary.Publish stop token prefix files message
                        } }
        let outcome = Workspace.publishWithBackend root "publication-002" Workspace.templateId "local/disposable" "main" racing |> _.GetAwaiter().GetResult()
        match outcome with
        | Ok _ -> failwith "a non-fast-forward publication was accepted"
        | Error error -> Assert.Contains("concurrent branch update", error.Detail))

[<Fact>]
let ``publication failures redact provider credential text`` () =
    let secret = "github_pat_must_not_escape"
    let unavailable: Backend =
        { Name = "credential-redaction-test"
          Head = fun () -> Task.FromResult(Error(Auth secret))
          ListUnder = fun _ _ -> Task.FromResult(Error(Auth secret))
          Blob = fun _ -> Task.FromResult(Error(Auth secret))
          TreeOf = fun _ -> Task.FromResult(Error(Auth secret))
          ChangedPaths = fun _ _ -> Task.FromResult(Error(Auth secret))
          Publish = fun _ _ _ _ _ -> Task.FromResult(Error(Auth secret)) }
    let outcome = Workspace.checkRemoteAbsentWithBackend unavailable "publication-003" |> _.GetAwaiter().GetResult()
    match outcome with
    | Ok _ -> failwith "authentication failure was accepted"
    | Error error ->
        Assert.Contains("authentication failed", error.Detail)
        Assert.DoesNotContain(secret, error.Detail)
