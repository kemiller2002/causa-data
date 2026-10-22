/// Dataset-level verification: schema bundle identity (§10.3), root manifest (§4), every frozen
/// investigation, golden projections (§8.4), the scenario summary, and every negative fixture (§12.2).
module CleanConsumer.Dataset

open System
open System.IO
open CleanConsumer.Json
open CleanConsumer.Domain
open CleanConsumer.Verify

type Check = { Name: string; Passed: bool; Detail: string }

let pass name detail = { Name = name; Passed = true; Detail = detail }
let failc name detail = { Name = name; Passed = false; Detail = detail }
let checkOf name (r: Result<string, string>) =
    match r with
    | Ok d -> pass name d
    | Error d -> failc name d

// ---------------------------------------------------------------- I/O edge

let fileName (p: string) : string = match Path.GetFileName p with null -> "" | s -> s
let stemOf (p: string) : string = match Path.GetFileNameWithoutExtension p with null -> "" | s -> s

let readBytes (path: string) : Result<byte[], string> =
    try Ok(File.ReadAllBytes path) with ex -> Error $"cannot read {path}: {ex.Message}"

/// Strict read of one JSON file: framing, syntax, canonical form, then a structural validator.
let readJsonDoc (path: string) (validator: JsonValue -> string list) : Result<JsonValue, string> =
    result {
        let! bytes = readBytes path
        let! text = Wire.jsonFileText bytes |> Result.mapError (fun e -> $"CAUSA.ENCODING: {e}")
        let! doc = Json.parse text |> Result.mapError (fun e -> $"CAUSA.JSON_MALFORMED: {e}")
        do! if canonical doc = text then Ok() else Error "CAUSA.NON_CANONICAL: not RFC 8785 canonical"
        do! match field "formatVersion" doc with
            | Some(JString v) when v <> "1.0.0" -> Error $"CAUSA.UNSUPPORTED_VERSION: formatVersion \"{v}\""
            | _ -> Ok()
        do! match validator doc with
            | [] -> Ok()
            | e :: _ -> Error $"CAUSA.SCHEMA_VIOLATION: {e}"
        return doc
    }

/// Reads an investigation directory. Missing files read as empty and are reported by stage 1
/// through the directory listing (§3.3), so the reader's check order is preserved.
let loadFileSet (dir: string) : Result<FileSet, string> =
    try
        let listing = Directory.GetFileSystemEntries dir |> Array.map fileName |> List.ofArray
        let read name =
            let p = Path.Combine(dir, name)
            if File.Exists p then File.ReadAllBytes p else [||]
        Ok { DirectoryName = fileName (dir.TrimEnd('/')); Listing = listing
             Manifest = read "manifest.json"; Events = read "events.ndjson"; Snapshot = read "snapshot.json" }
    with ex -> Error $"cannot read {dir}: {ex.Message}"

let rel (root: string) (path: string) = Path.GetRelativePath(root, path).Replace('\\', '/')

/// Every file under `dir` with its SHA-256, sorted ordinally by relative path.
let hashTree (dir: string) : (string * string) list =
    Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
    |> Array.map (fun f -> rel dir f, Wire.sha256Hex (File.ReadAllBytes f))
    |> Array.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))
    |> List.ofArray

let private isSortedOrdinal (xs: string list) =
    xs |> List.pairwise |> List.forall (fun (a, b) -> String.CompareOrdinal(a, b) < 0)

/// Command groups = runs of consecutive events sharing correlationId (§5.4).
let commandGroups (events: JsonValue list) =
    events
    |> List.fold (fun (acc: string list) e ->
        let c = str "correlationId" e |> Option.defaultValue ""
        match acc with
        | head :: _ when head = c -> acc
        | _ -> c :: acc) []
    |> List.length

// ---------------------------------------------------------------- schema bundle (§10.3)

let private bundleChecks (contractDir: string) (rootSchemaSha: string option) : Check list =
    let schemasDir = Path.Combine(contractDir, "schemas")
    let bundlePath = Path.Combine(schemasDir, "bundle.json")
    match readJsonDoc bundlePath (Schema.validate "bundle" Schema.schemaBundle) with
    | Error e -> [ failc "schema-bundle.wellformed" e ]
    | Ok bundle ->
        let entries =
            arr "schemas" bundle |> Option.defaultValue []
            |> List.map (fun e -> str "path" e |> Option.defaultValue "", str "sha256" e |> Option.defaultValue "")
        let paths = entries |> List.map fst
        let onDisk =
            Directory.GetFiles(schemasDir, "*.schema.json", SearchOption.AllDirectories)
            |> Array.map (rel schemasDir) |> Set.ofArray
        let identity = readBytes bundlePath |> Result.map Wire.sha256Hex |> Result.defaultValue "?"
        [ pass "schema-bundle.wellformed" "bundle.json is canonical and matches schema-bundle shape"
          (if isSortedOrdinal paths then pass "schema-bundle.sorted" $"{paths.Length} paths sorted ordinally, unique"
           else failc "schema-bundle.sorted" "bundle paths are not sorted ordinally / unique")
          (if Set.ofList paths = onDisk then pass "schema-bundle.complete" $"lists every *.schema.json under schemas/ ({onDisk.Count})"
           else
               let missing = Set.difference onDisk (Set.ofList paths) |> String.concat ","
               let extra = Set.difference (Set.ofList paths) onDisk |> String.concat ","
               failc "schema-bundle.complete" $"missing=[{missing}] extra=[{extra}]") ]
        @ (entries
              |> List.map (fun (p, sha) ->
                  match readBytes (Path.Combine(schemasDir, p)) with
                  | Ok b when Wire.sha256Hex b = sha -> pass $"schema-digest {p}" sha
                  | Ok b -> failc $"schema-digest {p}" $"CAUSA.FIXTURE_DRIFT: bundle says {sha}, file is {Wire.sha256Hex b}"
                  | Error e -> failc $"schema-digest {p}" e))
        @ [ match rootSchemaSha with
            | Some s when s = identity -> pass "schema-identity" $"root manifest schemaSha256 = sha256(bundle.json) = {identity}"
            | Some s -> failc "schema-identity" $"CAUSA.FIXTURE_DRIFT: root manifest schemaSha256 {s}, bundle.json digest {identity}"
            | None -> failc "schema-identity" "root manifest unavailable" ]

// ---------------------------------------------------------------- per-investigation

type Loaded =
    { Key: string
      Entry: JsonValue
      Files: FileSet option
      Result: Result<Valid, Failure> option }

let private roundTrip (v: Valid) =
    let reencoded = v.EngineEvents |> traverse encodeEvent
    match reencoded with
    | Error e -> Error $"engine->wire encoding failed: {e}"
    | Ok wire ->
        let diffs = List.zip wire v.WireEvents |> List.filter (fun (a, b) -> canonical a <> canonical b)
        match diffs with
        | [] -> Ok $"{wire.Length} events decode to engine types and re-encode byte-identically"
        | (a, _) :: _ -> Error(sprintf "%d events differ after round trip; first: %s" diffs.Length (canonical a))

let private investigationChecks (contractDir: string) (fixturesDir: string) (scenarios: JsonValue list) (entry: JsonValue) : Check list * Loaded =
    let id = str "investigationId" entry |> Option.defaultValue "?"
    let key = str "scenarioKey" entry |> Option.defaultValue "?"
    let dir = Path.Combine(fixturesDir, "investigations", id)
    let n name = $"{key} {name}"
    match loadFileSet dir with
    | Error e -> [ failc (n "load") e ], { Key = key; Entry = entry; Files = None; Result = None }
    | Ok files ->
        let listing = Directory.GetFileSystemEntries dir |> Array.map fileName |> Array.sort |> List.ofArray
        let layout =
            if listing = [ "events.ndjson"; "manifest.json"; "snapshot.json" ] then pass (n "layout") "exactly manifest.json, events.ndjson, snapshot.json"
            else failc (n "layout") $"""CAUSA.PATH_INVALID: directory holds [{String.Join(", ", listing)}] (§3.3)"""
        let result = validate files
        let loaded = { Key = key; Entry = entry; Files = Some files; Result = Some result }
        match result with
        | Error f -> [ layout; failc (n "validate") (describe result) ], loaded
        | Ok v ->
            let expect name (want: string option) (got: string) =
                match want with
                | Some w when w = got -> pass (n name) got
                | w -> failc (n name) $"""CAUSA.FIXTURE_DRIFT: root manifest {defaultArg w "?"}, actual {got}"""
            let stateText = canonical v.State
            let goldenPath = Path.Combine(contractDir, "golden", key + ".state.json")
            let golden =
                readJsonDoc goldenPath (Schema.validate "golden" Schema.state)
                |> Result.bind (fun _ -> readBytes goldenPath)
            let scenario = scenarios |> List.tryFind (fun s -> str "scenarioKey" s = Some key)
            let types = v.WireEvents |> List.choose (str "eventType") |> List.distinct |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))
            [ layout
              pass (n "validate") (describe result + " - all 17 reader stages pass, History.validate admissible")
              expect "eventsSha256" (str "eventsSha256" entry) (Wire.sha256Hex files.Events)
              expect "manifestSha256" (str "manifestSha256" entry) (Wire.canonicalDigest v.Manifest)
              expect "snapshotSha256" (str "snapshotSha256" entry) (Wire.canonicalDigest v.Snapshot)
              expect "expectedStateSha256" (str "expectedStateSha256" entry) (Wire.sha256Hex (Wire.utf8 stateText))
              expect "expectedVersion" (int "expectedVersion" entry |> Option.map string) (int "version" v.State |> Option.map string |> Option.defaultValue "?")
              expect "expectedLifecycle" (str "expectedLifecycle" entry) (str "lifecycle" v.State |> Option.defaultValue "?")
              expect "title" (str "title" entry) (str "title" v.State |> Option.defaultValue "?")
              (match golden with
               | Ok bytes when bytes = Wire.jsonFileBytes v.State -> pass (n "golden") $"golden/{key}.state.json byte-identical to projected state ({bytes.Length} bytes)"
               | Ok bytes ->
                   let goldenState = Wire.jsonFileText bytes |> Result.bind Json.parse
                   let schemaNote =
                       match goldenState with
                       | Ok g -> String.Join("; ", Schema.validate "golden" Schema.state g |> List.truncate 2)
                       | Error e -> e
                   failc (n "golden") $"CAUSA.GOLDEN_MISMATCH: golden/{key}.state.json differs from the projection {schemaNote}"
               | Error e -> failc (n "golden") e)
              checkOf (n "engine-roundtrip") (roundTrip v)
              (match scenario with
               | None -> failc (n "scenario-summary") "no entry in golden/scenarios.json"
               | Some s ->
                   let declared = arr "eventTypes" s |> Option.defaultValue [] |> List.choose (function JString x -> Some x | _ -> None)
                   let refusals = arr "refusals" s |> Option.defaultValue [] |> List.length
                   let groups = commandGroups v.WireEvents
                   let problems =
                       [ if declared <> types then $"eventTypes differ: declared {declared.Length}, actual {types.Length}"
                         if int "events" s <> Some(int64 v.WireEvents.Length) then $"""events: declared {int "events" s}, actual {v.WireEvents.Length}"""
                         if str "investigationId" s <> Some id then "investigationId differs"
                         if int "commands" s <> Some(int64 (groups + refusals)) then
                             $"""commands: declared {int "commands" s}, persisted groups {groups} + refusals {refusals}""" ]
                   if problems.IsEmpty then pass (n "scenario-summary") $"{types.Length} event types, {v.WireEvents.Length} events, {groups} command groups + {refusals} refusals"
                   else failc (n "scenario-summary") ("CAUSA.GOLDEN_MISMATCH: " + String.Join("; ", problems))) ],
            loaded

// ---------------------------------------------------------------- negative fixtures (§12.2, §9.2, §9.3)

let private b64 (name: string) (o: JsonValue) : Result<byte[], string> =
    match field name o |> Option.bind (str "base64") with
    | Some s -> (try Ok(Convert.FromBase64String s) with ex -> Error ex.Message)
    | None -> Error $"no base64 for {name}"

let private fileSetOf (dirName: string) (files: JsonValue) : Result<FileSet, string> =
    // `files` may name any entries (§3.3); a missing one reads as empty and stage 1 reports it.
    let listing = match files with JObject ms -> ms |> List.map fst | _ -> []
    let bytesOf name = if List.contains name listing then b64 name files else Ok [||]
    result {
        let! m = bytesOf "manifest.json"
        let! e = bytesOf "events.ndjson"
        let! s = bytesOf "snapshot.json"
        return { DirectoryName = dirName; Listing = listing; Manifest = m; Events = e; Snapshot = s }
    }

type Outcome = { Primary: string; Domain: string option; Detail: string }

let private outcomeOf (r: Result<Valid, Failure>) =
    match r with
    | Ok v -> { Primary = "OK"; Domain = None; Detail = $"accepted ({v.WireEvents.Length} events)" }
    | Error f -> { Primary = f.Code; Domain = f.DomainCode; Detail = f.Detail }

let startsWith (prefix: byte[]) (bytes: byte[]) =
    bytes.Length >= prefix.Length && ReadOnlySpan<byte>(bytes, 0, prefix.Length).SequenceEqual(ReadOnlySpan<byte>(prefix))

/// §9.2: optimistic append. A stale expected version is refused before anything is decided or written.
let appendCandidate (base': FileSet) (baseVersion: int64) (expected: int64) (appended: byte[]) : Result<FileSet, Outcome> =
    if expected <> baseVersion then
        Error { Primary = "CAUSA.VERSION_STALE"; Domain = None; Detail = $"expectedAggregateVersion {expected} but the current aggregateVersion is {baseVersion}; nothing written" }
    else
        Verify.validateAppendedLines (int32 baseVersion + 1) appended
        |> Result.mapError (fun f -> { Primary = f.Code; Domain = f.DomainCode; Detail = f.Detail })
        |> Result.bind (fun _ ->
            // §9.2 step (3): the combined history, with manifest and snapshot rebuilt by the evaluator.
            let events = Array.append base'.Events appended
            let parsed = Wire.ndjsonLines events |> Result.bind (traverse Json.parse) |> Result.toOption
            let derived =
                parsed
                |> Option.bind (fun evs -> Projection.snapshotOf evs |> Result.toOption |> Option.map (fun snap -> evs, snap))
            match derived with
            | Some(evs, snap) ->
                let manifest = Projection.manifestOf base'.DirectoryName evs events snap
                Ok { base' with Events = events; Manifest = Wire.jsonFileBytes manifest; Snapshot = Wire.jsonFileBytes snap }
            | None -> Ok { base' with Events = events })

let private evaluateNegative (byKey: Map<string, Loaded>) (fixture: JsonValue) : Result<Outcome, string> =
    let baseOf () =
        match str "baseScenarioKey" fixture |> Option.bind (fun k -> Map.tryFind k byKey) with
        | Some { Files = Some f; Result = Some(Ok v) } -> Ok(f, v)
        | _ -> Error "base scenario is missing or itself invalid"
    match str "kind" fixture with
    | Some "investigation" ->
        let dirName = str "directoryId" fixture |> Option.defaultValue ""
        field "files" fixture |> Option.map (fileSetOf dirName) |> Option.defaultValue (Error "no files")
        |> Result.map (validate >> outcomeOf)
    | Some "append" ->
        result {
            let! files, v = baseOf ()
            let! appended = b64 "appendedEvents" fixture
            let expected = int "expectedAggregateVersion" fixture |> Option.defaultValue -1L
            let current = int64 v.WireEvents.Length
            return
                match appendCandidate files current expected appended with
                | Error refused -> refused
                | Ok candidate -> validate candidate |> outcomeOf
        }
    | Some "successor" ->
        result {
            let! files, _ = baseOf ()
            let! candidate = field "files" fixture |> Option.map (fileSetOf files.DirectoryName) |> Option.defaultValue (Error "no files")
            let isolated = validate candidate |> outcomeOf
            let extends = startsWith files.Events candidate.Events
            // Order chosen: reader validation of the candidate first, then the §9.3 prefix rule.
            return
                if isolated.Primary <> "OK" then { isolated with Detail = isolated.Detail + (if extends then "" else " (also not a prefix extension)") }
                elif not extends then
                    { Primary = "CAUSA.HISTORY_REWRITTEN"; Domain = None
                      Detail = "candidate is valid in isolation, but its events.ndjson does not start with the accepted events.ndjson bytes" }
                else isolated
        }
    | other -> Error $"unknown negative fixture kind {other}"

let private negativeChecks (fixturesDir: string) (byKey: Map<string, Loaded>) (listed: JsonValue list) : Check list =
    let negDir = Path.Combine(fixturesDir, "negative")
    let onDisk = Directory.GetFiles(negDir, "*.json") |> Array.map (fun f -> "negative/" + fileName f) |> Set.ofArray
    let listedPaths = listed |> List.choose (str "path")
    let coverage =
        if Set.ofList listedPaths = onDisk && listedPaths.Length = onDisk.Count then
            pass "negative-fixtures.listed" $"root manifest lists exactly the {onDisk.Count} files in negative/"
        else failc "negative-fixtures.listed" "root manifest negativeFixtures and negative/*.json differ"
    let each =
        listed
        |> List.map (fun entry ->
            let path = str "path" entry |> Option.defaultValue "?"
            let name = $"negative {stemOf path}"
            match readJsonDoc (Path.Combine(fixturesDir, path)) (Schema.validateNegativeFixture "fixture") with
            | Error e -> failc name $"fixture file itself invalid: {e}"
            | Ok fixture ->
                let expected = str "expectedErrorCode" fixture |> Option.defaultValue "?"
                let expectedDomain = str "expectedDomainCode" fixture
                let listedCode = str "expectedErrorCode" entry
                let stem = stemOf path
                match evaluateNegative byKey fixture with
                | Error e -> failc name e
                | Ok o ->
                    let codeOk = o.Primary = expected
                    let domainOk = expectedDomain.IsNone || expectedDomain = o.Domain
                    let metaOk = listedCode = Some expected && str "name" fixture = Some stem
                    let shown = o.Primary + (match o.Domain with Some d -> "/" + d | None -> "")
                    let want = expected + (match expectedDomain with Some d -> "/" + d | None -> "")
                    let kind = str "kind" fixture |> Option.defaultValue "?"
                    if codeOk && domainOk && metaOk then pass name $"{kind}: got {shown} as expected - {o.Detail}"
                    elif not metaOk then failc name $"""fixture metadata disagrees with root manifest (listed {listedCode}, file {expected}, name {str "name" fixture})"""
                    else failc name $"{kind}: expected {want}, got {shown} - {o.Detail}")
    coverage :: each

// ---------------------------------------------------------------- whole dataset

let verify (repoRoot: string) : Check list =
    let contractDir = Path.Combine(repoRoot, "contract", "v1.0.0")
    let fixturesDir = Path.Combine(repoRoot, "fixtures", "v1.0.0")
    let namespaceCheck =
        if Wire.eventIdNamespace = Wire.eventIdNamespaceStated then
            pass "event-id-namespace" $"UUIDv5(NAMESPACE_URL, \"{Wire.eventIdNamespaceName}\") = {Wire.uuidText Wire.eventIdNamespace} (§5.2)"
        else failc "event-id-namespace" $"computed {Wire.uuidText Wire.eventIdNamespace}, contract states {Wire.uuidText Wire.eventIdNamespaceStated}"
    match readJsonDoc (Path.Combine(fixturesDir, "manifest.json")) (Schema.validate "root" Schema.rootManifest) with
    | Error e -> [ namespaceCheck; failc "root-manifest.wellformed" e ] @ bundleChecks contractDir None
    | Ok root ->
        let investigations = arr "investigations" root |> Option.defaultValue []
        let ids = investigations |> List.choose (str "investigationId")
        let keys = investigations |> List.choose (str "scenarioKey")
        let dirs = Directory.GetDirectories(Path.Combine(fixturesDir, "investigations")) |> Array.map fileName |> Set.ofArray
        let scenariosDoc = readJsonDoc (Path.Combine(contractDir, "golden", "scenarios.json")) (Schema.validate "scenarios" Schema.goldenScenarios)
        let scenarios = scenariosDoc |> Result.toOption |> Option.bind (arr "scenarios") |> Option.defaultValue []
        let rootChecks =
            [ pass "root-manifest.wellformed" "fixtures/v1.0.0/manifest.json is canonical and matches root-manifest shape"
              (if isSortedOrdinal ids then pass "root-manifest.order" $"{ids.Length} investigations ordered by investigationId, unique"
               else failc "root-manifest.order" "investigations not ordered by investigationId / not unique")
              (if (List.distinct keys).Length = keys.Length then pass "root-manifest.unique-keys" (String.Join(",", keys))
               else failc "root-manifest.unique-keys" "duplicate scenarioKey")
              (if Set.ofList ids = dirs then pass "root-manifest.directories" "every investigations/ directory is listed and vice versa"
               else failc "root-manifest.directories" "investigations/ directories differ from the root manifest")
              (match scenariosDoc with
               | Ok _ -> pass "golden/scenarios.json" "canonical JSON matching golden-scenarios.schema.json"
               | Error e -> failc "golden/scenarios.json" e) ]
        let perInvestigation = investigations |> List.map (investigationChecks contractDir fixturesDir scenarios)
        let loaded = perInvestigation |> List.map snd
        let byKey = loaded |> List.map (fun l -> l.Key, l) |> Map.ofList
        let seenTypes =
            loaded
            |> List.collect (fun l -> match l.Result with Some(Ok v) -> v.WireEvents |> List.choose (str "eventType") | _ -> [])
            |> Set.ofList
        let missingTypes = Schema.eventTypes |> List.filter (seenTypes.Contains >> not)
        let coverage =
            if missingTypes.IsEmpty then pass "event-type-coverage" $"all {Schema.eventTypes.Length} registered event types were read and decoded"
            else failc "event-type-coverage" $"""never read: {String.Join(", ", missingTypes)}"""
        [ namespaceCheck ]
        @ bundleChecks contractDir (str "schemaSha256" root)
        @ rootChecks
        @ (perInvestigation |> List.collect fst)
        @ [ coverage ]
        @ negativeChecks fixturesDir byKey (arr "negativeFixtures" root |> Option.defaultValue [])
