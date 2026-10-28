namespace Causa.Tool

open System
open System.Diagnostics
open System.IO
open System.Text.RegularExpressions
open System.Threading.Tasks
open Causa.Contract
open Causa.GitHub

type TemplateFile =
    { Path: string
      Sha256: string }

type TemplateInvestigation =
    { InvestigationId: string
      Files: TemplateFile list }

type HistoricalTemplate =
    { TemplateId: string
      TemplateVersion: string
      StorageContractVersion: string
      SourceRepository: string
      SourceCommit: string
      SourceDirectory: string
      Investigations: TemplateInvestigation list
      Digest: string }

[<RequireQualifiedAccess>]
type WorkspaceVerificationMode =
    | Initialized
    | Modified

type WorkspaceVerification =
    { Workspace: string
      Template: HistoricalTemplate
      InvestigationIds: string list
      Mode: WorkspaceVerificationMode }

type PublicationReceipt =
    { Repository: string
      Branch: string
      Workspace: string
      Commit: string
      TemplateDigest: string
      InvestigationIds: string list }

type private WorkspaceResultBuilder() =
    member _.Bind(value: Result<'T, 'Error>, binder: 'T -> Result<'U, 'Error>) = Result.bind binder value
    member _.Return(value: 'T) : Result<'T, 'Error> = Ok value
    member _.ReturnFrom(value: Result<'T, 'Error>) = value
    member _.Zero() : Result<unit, 'Error> = Ok()
    member _.Delay(action: unit -> Result<'T, 'Error>) = action
    member _.Run(action: unit -> Result<'T, 'Error>) = action ()
    member _.Combine(first: Result<unit, 'Error>, second: unit -> Result<'T, 'Error>) = Result.bind second first
    member this.While(guard: unit -> bool, body: unit -> Result<unit, 'Error>) =
        if not (guard ()) then Ok()
        else Result.bind (fun () -> this.While(guard, body)) (body ())
    member this.For(values: seq<'T>, body: 'T -> Result<unit, 'Error>) =
        use iterator = values.GetEnumerator()
        this.While(iterator.MoveNext, fun () -> body iterator.Current)
    member _.TryWith(action: unit -> Result<'T, 'Error>, handler: exn -> Result<'T, 'Error>) =
        try action () with ex -> handler ex
    member _.TryFinally(action: unit -> Result<'T, 'Error>, compensation: unit -> unit) =
        try action () finally compensation ()

/// Immutable historical templates and isolated workspace provisioning. This is the only implementation used by
/// direct users and by Conditor; orchestration layers invoke these operations rather than copying their rules.
[<RequireQualifiedAccess>]
module Workspace =
    let private workspaceResult = WorkspaceResultBuilder()

    let templateId = "historical-v1"
    let templateVersion = "1.0.0"
    let storageContractVersion = "1.0.0"
    let sourceRepository = "kemiller2002/causa-data"
    let sourceCommit = "dcb8cb994eab81d0384b9c8abc5f0b593f398442"
    let sourceDirectory = "workspaces/demo/historical-v1/investigations"

    let historicalInvestigationIds =
        [ "c6b26614-fc43-5013-a15c-cf95942ee07b"
          "e3adbee5-5096-5a10-bbfb-cecda86a154b"
          "1784da3c-7141-59ee-a495-1d5d44bf2284" ]

    let private idPattern = Regex(@"^[a-z0-9][a-z0-9-]{0,63}\z", RegexOptions.CultureInvariant)
    let private shaPattern = Regex(@"^[0-9a-f]{64}\z", RegexOptions.CultureInvariant)
    let private commitPattern = Regex(@"^[0-9a-f]{40}\z", RegexOptions.CultureInvariant)
    let private repositoryPattern = Regex(@"^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9._-]{1,100}\z", RegexOptions.CultureInvariant)

    let private fail code detail = CausaError.fail code detail
    let private posix (path: string) = path.Replace('\\', '/')
    let private full (path: string) = Path.GetFullPath path
    let private templateRoot root name = Path.Combine(root, "templates", name)
    let private manifestPath root name = Path.Combine(templateRoot root name, "template-manifest.json")
    let private templateInvestigations root name = Path.Combine(templateRoot root name, "investigations")
    let private workspaceRoot root name = Path.Combine(root, "workspaces", "demo", name)
    let private workspaceInvestigations root name = Path.Combine(workspaceRoot root name, "investigations")

    let private ensureName kind (value: string) =
        if idPattern.IsMatch value then Ok value
        else fail Codes.pathInvalid $"{kind} '{value}' must match ^[a-z0-9][a-z0-9-]{{0,63}}$"

    let private isLink (path: string) =
        (File.Exists path || Directory.Exists path)
        && (File.GetAttributes(path) &&& FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint

    let private inside (parent: string) (candidate: string) =
        let p = Path.TrimEndingDirectorySeparator(full parent) + string Path.DirectorySeparatorChar
        let c = full candidate
        c.StartsWith(p, StringComparison.Ordinal)

    let private ensureNoLinkedAncestors (root: string) (path: string) =
        let root = full root
        let rec check (current: string) =
            if String.Equals(current, root, StringComparison.Ordinal) then Ok()
            elif not (inside root current) then fail Codes.pathInvalid $"path escapes repository root: {current}"
            else
                let parent = Directory.GetParent(current)
                match parent with
                | null -> fail Codes.pathInvalid $"path has no repository ancestor: {current}"
                | p ->
                    check p.FullName
                    |> Result.bind (fun () ->
                        if (File.Exists current || Directory.Exists current) && isLink current then
                            fail Codes.pathInvalid $"symbolic links are not allowed in a workspace path: {current}"
                        else Ok())
        check (full path)

    let private canonicalFile (path: string) =
        workspaceResult {
            let bytes = File.ReadAllBytes path
            let! text = Json.decodeUtf8 bytes |> Result.mapError (CausaError.create Codes.encoding)
            if not (text.EndsWith "\n") || text.Contains '\r' then
                return! fail Codes.encoding $"{path} must be UTF-8 with LF framing"
            let body = text.Substring(0, text.Length - 1)
            let! json = Json.parse body |> Result.mapError (CausaError.create Codes.jsonMalformed)
            let! canonical = Json.canonical json |> Result.mapError (CausaError.create Codes.nonCanonical)
            if canonical <> body then return! fail Codes.nonCanonical $"{path} is not canonical JSON"
            return json
        }

    let private str name fields =
        match Json.field name fields with
        | Some(JStr value) -> Ok value
        | _ -> fail Codes.schemaViolation $"template manifest field '{name}' must be a string"

    let private parseFile value =
        match value with
        | JObj fields ->
            workspaceResult {
                let! path = str "path" fields
                let! digest = str "sha256" fields
                if Path.IsPathRooted path || path.Contains '\\' || path.Split('/') |> Array.exists (fun segment -> segment = "" || segment = "." || segment = "..") then
                    return! fail Codes.pathInvalid $"template file path is not a canonical relative path: {path}"
                if not (shaPattern.IsMatch digest) then return! fail Codes.schemaViolation $"template file digest is invalid: {path}"
                return { Path = path; Sha256 = digest }
            }
        | _ -> fail Codes.schemaViolation "template file entry must be an object"

    let private parseInvestigation value =
        match value with
        | JObj fields ->
            workspaceResult {
                let! id = str "investigationId" fields
                if Uuid.tryParse id |> Option.map Uuid.text <> Some id then
                    return! fail Codes.pathInvalid $"template investigation id is not a canonical UUID: {id}"
                let! files =
                    match Json.field "files" fields with
                    | Some(JArr values) -> traverse parseFile values
                    | _ -> fail Codes.schemaViolation $"template investigation {id} has no files array"
                let expected = Store.fileNames |> List.map (fun name -> $"investigations/{id}/{name}") |> List.sort
                if files |> List.map _.Path |> List.sort <> expected then
                    return! fail Codes.pathInvalid $"template investigation {id} must contain exactly the three storage files"
                return { InvestigationId = id; Files = files }
            }
        | _ -> fail Codes.schemaViolation "template investigation entry must be an object"

    let private digestProjection fields =
        fields |> List.filter (fst >> (<>) "overallTemplateSha256") |> JObj

    let private parseTemplateJson value =
        match value with
        | JObj fields ->
            workspaceResult {
                let! id = str "templateId" fields
                let! version = str "templateVersion" fields
                let! contract = str "storageContractVersion" fields
                let! repository = str "sourceRepository" fields
                let! commit = str "sourceCommit" fields
                let! directory = str "sourceDirectory" fields
                let! digest = str "overallTemplateSha256" fields
                let! investigations =
                    match Json.field "investigations" fields with
                    | Some(JArr values) -> traverse parseInvestigation values
                    | _ -> fail Codes.schemaViolation "template manifest has no investigations array"
                let! computed = Json.digest (digestProjection fields) |> Result.mapError (CausaError.create Codes.nonCanonical)
                if digest <> computed then return! fail Codes.fixtureDrift $"template digest mismatch: expected {digest}, computed {computed}"
                if id <> templateId || version <> templateVersion || contract <> storageContractVersion then
                    return! fail Codes.unsupportedVersion "the historical-v1 template identity or contract version is unsupported"
                if repository <> sourceRepository || not (commitPattern.IsMatch commit) || directory <> sourceDirectory then
                    return! fail Codes.schemaViolation "the historical-v1 template source identity is invalid"
                let ids = investigations |> List.map _.InvestigationId
                if ids.Length <> 3 || Set.ofList ids <> Set.ofList historicalInvestigationIds then
                    return! fail Codes.fixtureDrift "historical-v1 must contain exactly Apollo 1, Apollo 11 and Challenger"
                return
                    { TemplateId = id
                      TemplateVersion = version
                      StorageContractVersion = contract
                      SourceRepository = repository
                      SourceCommit = commit
                      SourceDirectory = directory
                      Investigations = investigations
                      Digest = digest }
            }
        | _ -> fail Codes.schemaViolation "template manifest must be an object"

    let private expectedTemplateEntries root name =
        let baseDir = templateRoot root name
        [ Path.Combine(baseDir, "template-manifest.json"); Path.Combine(baseDir, "investigations") ] |> List.sort

    let verifyTemplate (root: string) (name: string) : Result<HistoricalTemplate, CausaError> =
        workspaceResult {
            let root = full root
            let! _ = ensureName "template" name
            let template = templateRoot root name
            if not (Directory.Exists template) then return! fail Codes.pathInvalid $"template does not exist: templates/{name}"
            do! ensureNoLinkedAncestors root template
            let entries = Directory.GetFileSystemEntries template |> List.ofArray |> List.sort
            if entries <> expectedTemplateEntries root name then
                return! fail Codes.pathInvalid $"template {name} contains unexpected top-level entries"
            let investigationsDir = templateInvestigations root name
            if isLink investigationsDir then return! fail Codes.pathInvalid "template investigations directory is a symbolic link"
            let! manifest = canonicalFile (manifestPath root name) |> Result.bind parseTemplateJson
            let directories = Directory.GetDirectories investigationsDir |> List.ofArray
            let filesAtRoot = Directory.GetFiles investigationsDir |> List.ofArray
            if not filesAtRoot.IsEmpty then return! fail Codes.pathInvalid "template investigations root contains files"
            if (directories |> List.map (Path.GetFileName >> nonNull) |> Set.ofList) <> (manifest.Investigations |> List.map _.InvestigationId |> Set.ofList) then
                return! fail Codes.fixtureDrift "template investigation directories differ from the manifest"
            let! schemas = Store.loadSchemas (Layout.schemas root)
            for investigation in manifest.Investigations do
                let dir = Path.Combine(investigationsDir, investigation.InvestigationId)
                do! ensureNoLinkedAncestors root dir
                let entries = Directory.GetFileSystemEntries dir |> Array.map (Path.GetFileName >> nonNull) |> Array.sort |> List.ofArray
                if entries <> (Store.fileNames |> List.sort) then
                    return! fail Codes.pathInvalid $"template investigation contains unexpected files: {investigation.InvestigationId}"
                let! files = Store.readDirectory dir
                let! _ = Store.validate schemas files
                for expected in investigation.Files do
                    let path = Path.Combine(template, expected.Path.Replace('/', Path.DirectorySeparatorChar))
                    if isLink path then return! fail Codes.pathInvalid $"template file is a symbolic link: {expected.Path}"
                    let actual = Json.sha256Hex (File.ReadAllBytes path)
                    if actual <> expected.Sha256 then return! fail Codes.fixtureDrift $"template file digest mismatch: {expected.Path}"
            return manifest
        }

    let private investigationJson root id =
        let files =
            Store.fileNames
            |> List.map (fun name ->
                let relative = $"investigations/{id}/{name}"
                let bytes = File.ReadAllBytes(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)))
                JObj [ "path", JStr relative; "sha256", JStr(Json.sha256Hex bytes) ])
        JObj [ "investigationId", JStr id; "files", JArr files ]

    let private manifestJson (templateDir: string) =
        let fields =
            [ "templateId", JStr templateId
              "templateVersion", JStr templateVersion
              "storageContractVersion", JStr storageContractVersion
              "sourceRepository", JStr sourceRepository
              "sourceCommit", JStr sourceCommit
              "sourceDirectory", JStr sourceDirectory
              "investigations", JArr(historicalInvestigationIds |> List.map (investigationJson templateDir)) ]
        let digest = Json.digest (JObj fields) |> Result.defaultWith failwith
        JObj(fields @ [ "overallTemplateSha256", JStr digest ])

    /// One-time creation refuses an existing template and publishes only after every copied investigation validates.
    let createHistoricalTemplate (root: string) : Result<HistoricalTemplate, CausaError> =
        let root = full root
        let destination = templateRoot root templateId
        let staging = Path.Combine(root, "templates", $".{templateId}-staging-{Guid.NewGuid():N}")
        if Directory.Exists destination || File.Exists destination then fail Codes.pathInvalid $"template already exists: templates/{templateId}"
        else
            try
                let source = Path.Combine(root, sourceDirectory.Replace('/', Path.DirectorySeparatorChar))
                Directory.CreateDirectory(Path.Combine(staging, "investigations")) |> ignore
                for id in historicalInvestigationIds do
                    let fromDir = Path.Combine(source, id)
                    let toDir = Path.Combine(staging, "investigations", id)
                    Directory.CreateDirectory toDir |> ignore
                    for name in Store.fileNames do File.Copy(Path.Combine(fromDir, name), Path.Combine(toDir, name), false)
                let manifest = manifestJson staging
                File.WriteAllBytes(Path.Combine(staging, "template-manifest.json"), Json.fileBytes manifest |> Result.defaultWith failwith)
                Directory.Move(staging, destination)
                verifyTemplate root templateId
            with ex ->
                if Directory.Exists staging then Directory.Delete(staging, true)
                fail Codes.pathInvalid $"template creation failed: {ex.Message}"

    let private templateFiles (root: string) (template: HistoricalTemplate) id =
        let dir = Path.Combine(templateInvestigations root template.TemplateId, id)
        Store.readDirectory dir

    let verifyWorkspace (root: string) (name: string) (templateName: string) (mode: WorkspaceVerificationMode) : Result<WorkspaceVerification, CausaError> =
        workspaceResult {
            let root = full root
            let! _ = ensureName "workspace" name
            let! template = verifyTemplate root templateName
            let workspace = workspaceRoot root name
            if not (Directory.Exists workspace) then return! fail Codes.pathInvalid $"workspace does not exist: workspaces/demo/{name}"
            do! ensureNoLinkedAncestors root workspace
            let topEntries = Directory.GetFileSystemEntries workspace |> List.ofArray
            if topEntries <> [ workspaceInvestigations root name ] then
                return! fail Codes.pathInvalid "workspace must contain only the investigations directory"
            let investigationsDir = workspaceInvestigations root name
            let unexpectedFiles = Directory.GetFiles investigationsDir |> List.ofArray
            if not unexpectedFiles.IsEmpty then return! fail Codes.pathInvalid "workspace investigations root contains files"
            let dirs = Directory.GetDirectories investigationsDir |> List.ofArray
            let ids = dirs |> List.map (Path.GetFileName >> nonNull)
            let expectedIds = template.Investigations |> List.map _.InvestigationId
            if Set.ofList ids <> Set.ofList expectedIds || ids.Length <> expectedIds.Length then
                return! fail Codes.fixtureDrift "workspace must contain exactly the template investigations"
            let! schemas = Store.loadSchemas (Layout.schemas root)
            for investigation in template.Investigations do
                let dir = Path.Combine(investigationsDir, investigation.InvestigationId)
                do! ensureNoLinkedAncestors root dir
                let entries = Directory.GetFileSystemEntries dir |> Array.map (Path.GetFileName >> nonNull) |> Array.sort |> List.ofArray
                if entries <> (Store.fileNames |> List.sort) then
                    return! fail Codes.pathInvalid $"workspace investigation contains unexpected files: {investigation.InvestigationId}"
                for path in Directory.GetFiles dir do
                    if isLink path then return! fail Codes.pathInvalid $"workspace file is a symbolic link: {Path.GetFileName path}"
                let! candidateFiles = Store.readDirectory dir
                let! candidate = Store.validate schemas candidateFiles
                let! sourceFiles = templateFiles root template investigation.InvestigationId
                let! source = Store.validate schemas sourceFiles
                match mode with
                | WorkspaceVerificationMode.Initialized ->
                    if candidateFiles.Manifest <> sourceFiles.Manifest || candidateFiles.Events <> sourceFiles.Events || candidateFiles.Snapshot <> sourceFiles.Snapshot then
                        return! fail Codes.fixtureDrift $"initialized workspace differs from template: {investigation.InvestigationId}"
                | WorkspaceVerificationMode.Modified ->
                    let extends = candidateFiles.Events.Length >= sourceFiles.Events.Length && candidateFiles.Events[.. sourceFiles.Events.Length - 1] = sourceFiles.Events
                    if not extends || candidate.Events.Length < source.Events.Length then
                        return! fail Codes.historyRewritten $"workspace replaced or rewrote historical events: {investigation.InvestigationId}"
            return { Workspace = name; Template = template; InvestigationIds = expectedIds; Mode = mode }
        }

    let internal createWorkspaceWithFileCopy (copyFile: string -> string -> unit) (root: string) (name: string) (templateName: string) : Result<WorkspaceVerification, CausaError> =
        let root = full root
        workspaceResult {
            let! _ = ensureName "workspace" name
            let! template = verifyTemplate root templateName
            let approved = Path.Combine(root, "workspaces", "demo")
            do! ensureNoLinkedAncestors root (Path.Combine(root, "workspaces"))
            Directory.CreateDirectory approved |> ignore
            do! ensureNoLinkedAncestors root approved
            let destination = workspaceRoot root name
            if not (inside approved destination) then return! fail Codes.pathInvalid "workspace destination escapes the approved root"
            if Directory.Exists destination || File.Exists destination then return! fail Codes.pathInvalid $"workspace already exists: workspaces/demo/{name}"
            let staging = Path.Combine(approved, $"stg-{Guid.NewGuid():N}")
            let result =
                try
                    workspaceResult {
                        Directory.CreateDirectory(Path.Combine(staging, "investigations")) |> ignore
                        for investigation in template.Investigations do
                            let fromDir = Path.Combine(templateInvestigations root templateName, investigation.InvestigationId)
                            let toDir = Path.Combine(staging, "investigations", investigation.InvestigationId)
                            Directory.CreateDirectory toDir |> ignore
                            for file in Store.fileNames do copyFile (Path.Combine(fromDir, file)) (Path.Combine(toDir, file))
                        let stagingName = Path.GetFileName staging |> nonNull
                        let! verified = verifyWorkspace root stagingName templateName WorkspaceVerificationMode.Initialized
                        if Directory.Exists destination || File.Exists destination then
                            return! fail Codes.pathInvalid $"workspace was created concurrently: workspaces/demo/{name}"
                        Directory.Move(staging, destination)
                        return { verified with Workspace = name }
                    }
                with ex -> fail Codes.pathInvalid $"workspace creation failed: {ex.Message}"
            if Directory.Exists staging then Directory.Delete(staging, true)
            return! result
        }

    let createWorkspace (root: string) (name: string) (templateName: string) : Result<WorkspaceVerification, CausaError> =
        createWorkspaceWithFileCopy (fun source destination -> File.Copy(source, destination, false)) root name templateName

    /// Read-only local uniqueness probe used by orchestrators before creation.
    let checkLocalAbsent (root: string) (name: string) : Result<unit, CausaError> =
        workspaceResult {
            let root = full root
            let! _ = ensureName "workspace" name
            let workspaces = Path.Combine(root, "workspaces")
            do! ensureNoLinkedAncestors root workspaces
            let destination = workspaceRoot root name
            if Directory.Exists destination || File.Exists destination then
                return! fail Codes.pathInvalid $"local workspace already exists: workspaces/demo/{name}"
            return ()
        }

    let private modeText = function WorkspaceVerificationMode.Initialized -> "INITIALIZED" | WorkspaceVerificationMode.Modified -> "MODIFIED"

    let verificationJson (verification: WorkspaceVerification) =
        JObj
            [ "workspaceIdentifier", JStr verification.Workspace
              "templateIdentifier", JStr verification.Template.TemplateId
              "templateVersion", JStr verification.Template.TemplateVersion
              "templateDigest", JStr verification.Template.Digest
              "storageContractVersion", JStr verification.Template.StorageContractVersion
              "investigationCount", JInt(int64 verification.InvestigationIds.Length)
              "investigationIdentifiers", JArr(verification.InvestigationIds |> List.map JStr)
              "verificationMode", JStr(modeText verification.Mode)
              "verificationResult", JStr "VERIFIED" ]

    let rehearsalName (number: string) =
        match Int32.TryParse number with
        | true, value when value > 0 && value <= 999999 -> Ok(sprintf "rehearsal-%03d" value)
        | _ -> fail Codes.pathInvalid $"rehearsal number '{number}' must be a positive integer"

    let rehearsalReceipt (number: string) (verification: WorkspaceVerification) =
        let destination = $"workspaces/demo/{verification.Workspace}"
        JObj
            [ "schemaVersion", JStr "causa.rehearsal-initialization/v1"
              "rehearsalIdentifier", JStr verification.Workspace
              "workspaceIdentifier", JStr verification.Workspace
              "templateIdentifier", JStr verification.Template.TemplateId
              "templateVersion", JStr verification.Template.TemplateVersion
              "templateDigest", JStr verification.Template.Digest
              "investigationCount", JInt(int64 verification.InvestigationIds.Length)
              "investigationIdentifiers", JArr(verification.InvestigationIds |> List.map JStr)
              "storageContractVersion", JStr verification.Template.StorageContractVersion
              "creationResult", JStr "LOCAL_CREATED"
              "verificationResult", JStr "LOCAL_VERIFIED"
              "sourceRepository", JStr verification.Template.SourceRepository
              "sourceCommit", JStr verification.Template.SourceCommit
              "destinationPath", JStr destination
              "publishedCommit", JNull
              "remoteResult", JStr "NOT_PUBLISHED"
              "createdAt", JStr(DateTimeOffset.UtcNow.ToString("O")) ]

    let private publicationFiles root workspace =
        Directory.GetFiles(workspaceInvestigations root workspace, "*", SearchOption.AllDirectories)
        |> Array.toList
        |> List.map (fun path -> posix (Path.GetRelativePath(root, path)), File.ReadAllBytes path)
        |> List.sortBy fst

    // Provider diagnostics can contain credential-bearing URLs or transport headers. Keep the
    // public tool surface stage-specific while deliberately discarding provider-supplied text.
    let private failureSummary = function
        | Failure.Auth _ -> "authentication failed"
        | Failure.Network _ -> "network operation failed"
        | Failure.NotFastForward _ -> "concurrent branch update detected"
        | Failure.Other _ -> "repository operation failed"

    let private remoteVerify (backend: Backend) (schemas: SchemaCheck.SchemaSet) (commit: string) (root: string) (workspace: string) (local: (string * byte[]) list) =
        task {
            match! backend.TreeOf commit with
            | Error failure -> return Error(CausaError.create Codes.fixtureDrift $"published commit tree could not be read: {failureSummary failure}")
            | Ok tree ->
                let prefix = $"workspaces/demo/{workspace}/"
                match! backend.ListUnder tree prefix with
                | Error failure -> return Error(CausaError.create Codes.fixtureDrift $"published workspace could not be listed: {failureSummary failure}")
                | Ok entries ->
                    let expected = local |> List.map fst |> List.sort
                    let actual = entries |> List.map fst |> List.sort
                    if actual <> expected then return Error(CausaError.create Codes.fixtureDrift "published workspace path set differs from the initialization receipt")
                    else
                        let mutable error = None
                        for path, bytes in local do
                            if error.IsNone then
                                let sha = entries |> List.find (fst >> (=) path) |> snd
                                match! backend.Blob sha with
                                | Error failure -> error <- Some(CausaError.create Codes.fixtureDrift $"published file could not be read: {path}: {failureSummary failure}")
                                | Ok remote when remote <> bytes -> error <- Some(CausaError.create Codes.fixtureDrift $"published file differs: {path}")
                                | Ok _ -> ()
                        match error with
                        | Some e -> return Error e
                        | None ->
                            let ids = historicalInvestigationIds
                            let mutable validationError = None
                            for id in ids do
                                if validationError.IsNone then
                                    let file name = local |> List.find (fun (path, _) -> path.EndsWith($"/{id}/{name}", StringComparison.Ordinal)) |> snd
                                    let files = { DirectoryId = id; Manifest = file "manifest.json"; Events = file "events.ndjson"; Snapshot = file "snapshot.json" }
                                    match Store.validate schemas files with Error e -> validationError <- Some e | Ok _ -> ()
                            return validationError |> Option.map Error |> Option.defaultValue (Ok())
        }

    let private validateRemoteTarget (name: string) (repository: string) (branch: string) =
        workspaceResult {
            let! _ = ensureName "workspace" name
            if not (repositoryPattern.IsMatch repository) then
                return! fail Codes.pathInvalid $"repository '{repository}' must be OWNER/NAME"
            if String.IsNullOrWhiteSpace branch || branch.Contains ".." || branch.StartsWith "/" || branch.EndsWith "/" then
                return! fail Codes.pathInvalid $"branch '{branch}' is invalid"
            return ()
        }

    /// Confirm that the target branch exists and has no workspace at the requested path.
    let checkRemoteAbsentWithBackend (backend: Backend) (name: string) : Task<Result<ProviderToken, CausaError>> =
        task {
            match! backend.Head() with
            | Error failure -> return Error(CausaError.create Codes.fixtureDrift $"target repository or branch could not be confirmed: {failureSummary failure}")
            | Ok head ->
                let prefix = $"workspaces/demo/{name}/"
                match! backend.ListUnder head.TreeSha prefix with
                | Error failure -> return Error(CausaError.create Codes.fixtureDrift $"remote destination uniqueness could not be checked: {failureSummary failure}")
                | Ok (_ :: _) -> return Error(CausaError.create Codes.pathInvalid $"remote workspace already exists: {prefix}")
                | Ok [] -> return Ok head
        }

    /// Publish using an injected provider. Tests use a disposable bare repository; production uses GitHub smart HTTP.
    let publishWithBackend (root: string) (name: string) (templateName: string) (repository: string) (branch: string) (backend: Backend) : Task<Result<PublicationReceipt, CausaError>> =
        task {
            match validateRemoteTarget name repository branch with
            | Error e -> return Error e
            | Ok () ->
                match verifyWorkspace root name templateName WorkspaceVerificationMode.Initialized with
                | Error e -> return Error e
                | Ok verified ->
                    match! checkRemoteAbsentWithBackend backend name with
                    | Error e -> return Error e
                    | Ok head ->
                        let prefix = $"workspaces/demo/{name}/"
                        let files = publicationFiles (full root) name
                        match! backend.Publish (fun _ -> false) head prefix files $"causa: initialize {name} from {templateName}" with
                        | Error failure -> return Error(CausaError.create Codes.fixtureDrift $"workspace publication failed: {failureSummary failure}")
                        | Ok commit ->
                            match Store.loadSchemas (Layout.schemas (full root)) with
                            | Error e -> return Error e
                            | Ok schemas ->
                                match! remoteVerify backend schemas commit (full root) name files with
                                | Error e -> return Error e
                                | Ok () ->
                                    return Ok
                                        { Repository = repository
                                          Branch = branch
                                          Workspace = name
                                          Commit = commit
                                          TemplateDigest = verified.Template.Digest
                                          InvestigationIds = verified.InvestigationIds }
        }

    let checkRemoteAbsent (root: string) (name: string) (repository: string) (branch: string) : Task<Result<unit, CausaError>> =
        task {
            match validateRemoteTarget name repository branch with
            | Error e -> return Error e
            | Ok () ->
                let scratch = Path.Combine(full root, ".local", "workspace-publication", $"remote-check-{Guid.NewGuid():N}.git")
                let! backend = GitBackend.create scratch $"https://github.com/{repository}.git" branch
                match! checkRemoteAbsentWithBackend backend name with
                | Error e -> return Error e
                | Ok _ -> return Ok()
        }

    let publishWorkspace (root: string) (name: string) (templateName: string) (repository: string) (branch: string) : Task<Result<PublicationReceipt, CausaError>> =
        task {
            match validateRemoteTarget name repository branch with
            | Error e -> return Error e
            | Ok () ->
                let scratch = Path.Combine(full root, ".local", "workspace-publication", $"{name}-{Guid.NewGuid():N}.git")
                let! backend = GitBackend.create scratch $"https://github.com/{repository}.git" branch
                return! publishWithBackend root name templateName repository branch backend
        }

    /// Re-read a published commit from the intended branch and compare every
    /// path and byte to the locally verified initialization receipt.
    let verifyPublishedWithBackend (root: string) (name: string) (templateName: string) (repository: string) (branch: string) (commit: string) (backend: Backend) : Task<Result<PublicationReceipt, CausaError>> =
        task {
            match validateRemoteTarget name repository branch with
            | Error e -> return Error e
            | Ok () when not (commitPattern.IsMatch commit) -> return Error(CausaError.create Codes.pathInvalid $"published commit '{commit}' is invalid")
            | Ok () ->
                match verifyWorkspace root name templateName WorkspaceVerificationMode.Initialized with
                | Error e -> return Error e
                | Ok verified ->
                    match! backend.Head() with
                    | Error failure -> return Error(CausaError.create Codes.fixtureDrift $"published branch could not be confirmed: {failureSummary failure}")
                    | Ok head when head.HeadCommit <> commit -> return Error(CausaError.create Codes.fixtureDrift $"published commit {commit} is not the current head of the intended branch")
                    | Ok _ ->
                        let files = publicationFiles (full root) name
                        match Store.loadSchemas (Layout.schemas (full root)) with
                        | Error e -> return Error e
                        | Ok schemas ->
                            match! remoteVerify backend schemas commit (full root) name files with
                            | Error e -> return Error e
                            | Ok () ->
                                return Ok
                                    { Repository = repository
                                      Branch = branch
                                      Workspace = name
                                      Commit = commit
                                      TemplateDigest = verified.Template.Digest
                                      InvestigationIds = verified.InvestigationIds }
        }

    let verifyPublishedWorkspace (root: string) (name: string) (templateName: string) (repository: string) (branch: string) (commit: string) : Task<Result<PublicationReceipt, CausaError>> =
        task {
            match validateRemoteTarget name repository branch with
            | Error e -> return Error e
            | Ok () ->
                let scratch = Path.Combine(full root, ".local", "workspace-publication", $"remote-verify-{Guid.NewGuid():N}.git")
                let! backend = GitBackend.create scratch $"https://github.com/{repository}.git" branch
                return! verifyPublishedWithBackend root name templateName repository branch commit backend
        }

    /// Exercise the same resolver used by the Causa provider and then load/replay all investigations.
    let verifyApplicationCompatibility (root: string) (name: string) (templateName: string) =
        workspaceResult {
            let! prefix = Paths.workspace name
            let expected = $"workspaces/demo/{name}/investigations/"
            if prefix <> expected then return! fail Codes.pathInvalid $"application resolver returned '{prefix}', expected '{expected}'"
            return! verifyWorkspace root name templateName WorkspaceVerificationMode.Modified
        }

    let publicationJson receipt =
        JObj
            [ "repository", JStr receipt.Repository
              "branch", JStr receipt.Branch
              "workspaceIdentifier", JStr receipt.Workspace
              "publishedCommit", JStr receipt.Commit
              "templateDigest", JStr receipt.TemplateDigest
              "investigationCount", JInt(int64 receipt.InvestigationIds.Length)
              "investigationIdentifiers", JArr(receipt.InvestigationIds |> List.map JStr)
              "publicationResult", JStr "PUBLISHED"
              "remoteVerificationResult", JStr "REMOTE_VERIFIED" ]

    let remoteVerificationJson receipt =
        JObj
            [ "repository", JStr receipt.Repository
              "branch", JStr receipt.Branch
              "workspaceIdentifier", JStr receipt.Workspace
              "publishedCommit", JStr receipt.Commit
              "templateDigest", JStr receipt.TemplateDigest
              "investigationCount", JInt(int64 receipt.InvestigationIds.Length)
              "investigationIdentifiers", JArr(receipt.InvestigationIds |> List.map JStr)
              "remoteVerificationResult", JStr "REMOTE_VERIFIED" ]

    let applicationJson (verification: WorkspaceVerification) =
        JObj
            [ "workspaceIdentifier", JStr verification.Workspace
              "investigationCount", JInt(int64 verification.InvestigationIds.Length)
              "investigationIdentifiers", JArr(verification.InvestigationIds |> List.map JStr)
              "applicationCompatibilityResult", JStr "APPLICATION_VERIFIED" ]
