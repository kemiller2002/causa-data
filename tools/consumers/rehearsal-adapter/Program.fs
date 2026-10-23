module Causa.RehearsalAdapter.Program

open System
open System.IO
open IndyInitRootCauseInvestigator.Engine
open Causa.Contract

type Check = { Name: string; Passed: bool; Detail: string }

let private check name passed detail = { Name = name; Passed = passed; Detail = detail }
let private utf8 = Json.strictUtf8

let private linesOf (bytes: byte[]) =
    (utf8.GetString bytes).TrimEnd('\n').Split('\n') |> List.ofArray
let private parse (s: string) = Json.parse s |> Result.defaultWith failwith
let private field k v = match v with JObj o -> Json.field k o |> Option.defaultValue JNull | _ -> JNull
let private str v = match v with JStr s -> s | _ -> ""

let private sha (path: string) = File.ReadAllBytes path |> Json.sha256Hex

/// Decode a v1 history through the rehearsal's own, unmodified EventCodec.
let rehearsalDecode (investigationId: string) (eventsBytes: byte[]) =
    try
        let doc = Translate.documentToRehearsal investigationId (linesOf eventsBytes |> List.map parse)
        EventCodec.decode doc
    with ex -> Error("translation failed: " + ex.Message)

let private fixturesDigest (root: string) =
    Directory.GetFiles(Path.Combine(root, "fixtures"), "*", SearchOption.AllDirectories)
    |> Array.sort
    |> Array.map (fun p -> Path.GetRelativePath(root, p) + ":" + sha p)
    |> String.concat "\n"
    |> utf8.GetBytes
    |> Json.sha256Hex

let verify (root: string) : Check list =
    let set = Store.loadSchemas (Path.Combine(root, "contract", "v1.0.0", "schemas")) |> Result.defaultWith (fun e -> failwith e.Detail)
    let rootManifest = File.ReadAllText(Path.Combine(root, "fixtures", "v1.0.0", "manifest.json")).TrimEnd('\n') |> parse
    let entries = match field "investigations" rootManifest with JArr xs -> xs | _ -> []
    let before = fixturesDigest root
    let investigationChecks, loadedByKey =
        entries
        |> List.map (fun entry ->
            let id = str (field "investigationId" entry)
            let key = str (field "scenarioKey" entry)
            let files = Store.readDirectory (Path.Combine(root, "fixtures", "v1.0.0", "investigations", id)) |> Result.defaultWith (fun e -> failwith (e.Code + " " + e.Detail))
            match Store.validate set files, rehearsalDecode id files.Events with
            | Error e, _ -> [ check ("rehearsal " + key) false ("reference reader refused: " + e.Code) ], None
            | _, Error e -> [ check ("rehearsal " + key) false ("rehearsal EventCodec.decode refused: " + e) ], None
            | Ok loaded, Ok(_, events) ->
                let sameEvents = events = loaded.Events
                let admissible = History.validate events
                let state = Evolve.replay events
                let stateSha = state |> Option.map (Projection.state >> Result.bind (Json.digest >> Result.mapError (CausaError.create "X"))) |> Option.map (Result.defaultValue "") |> Option.defaultValue ""
                // Byte-exact round trip back through the rehearsal encoder.
                let reencoded = EventCodec.encode (Id.create (Guid.Parse id)) events |> parse
                let nodes = match field "events" reencoded with JArr xs -> xs | _ -> []
                let reencodedLines = nodes |> List.map (Translate.eventFromRehearsal id >> Json.canonical >> Result.defaultValue "")
                let roundTrip = (reencodedLines = linesOf files.Events)
                [ check ("rehearsal " + key + " decode") sameEvents (sprintf "rehearsal EventCodec.decode produced %d events; structurally equal to the reference decode: %b" events.Length sameEvents)
                  check ("rehearsal " + key + " admissible") admissible.IsEmpty (sprintf "History.validate errors: %d" admissible.Length)
                  check ("rehearsal " + key + " state") (stateSha = str (field "expectedStateSha256" entry) && state = Some loaded.State)
                      ("replayed state digest " + stateSha + " vs expected " + str (field "expectedStateSha256" entry))
                  check ("rehearsal " + key + " round-trip") roundTrip "rehearsal EventCodec.encode -> adapter -> v1 reproduces events.ndjson byte-for-byte" ],
                Some(key, (files, loaded)))
        |> List.unzip
    let byKey = loadedByKey |> List.choose id |> Map.ofList
    // Every event type read through the rehearsal codec.
    let typesRead = byKey |> Map.toList |> List.collect (fun (_, (_, l)) -> l.Events |> List.map (fun e -> CaseNames.eventType e.Body)) |> List.distinct
    let negDir = Path.Combine(root, "fixtures", "v1.0.0", "negative")
    let negatives =
        Directory.GetFiles(negDir, "*.json")
        |> Array.sort
        |> List.ofArray
        |> List.map (fun path ->
            let fx = File.ReadAllText(path).TrimEnd('\n') |> parse
            let name = str (field "name" fx)
            let expected = str (field "expectedErrorCode" fx)
            let expectedDomain = match field "expectedDomainCode" fx with JStr s -> Some s | _ -> None
            let b64 v = match field "base64" v with JStr s -> Convert.FromBase64String s | _ -> [||]
            let filesOf v dir =
                match v with
                | JObj o -> o |> List.map (fun (k, x) -> k, b64 x) |> Map.ofList |> Store.ofFileMap dir
                | _ -> CausaError.fail Codes.schemaViolation "files must be an object"
            // Adapter verdict: v1 front-end (contract stages 1-13) + rehearsal decode + History.validate.
            let adapterVerdict, rawRehearsal =
                match str (field "kind" fx) with
                | "investigation" ->
                    match filesOf (field "files" fx) (str (field "directoryId" fx)) with
                    | Error e -> Error e, "n/a (file set refused before decoding)"
                    | Ok files ->
                        let raw =
                            match rehearsalDecode files.DirectoryId files.Events with
                            | Error e -> "rejected (" + e.Split(':')[0] + ")"
                            | Ok(_, evs) -> match History.validate evs with [] -> "ACCEPTED" | e :: _ -> "rejected (" + e.Code + ")"
                        (match Store.validate set files with Ok _ -> Ok() | Error e -> Error e), raw
                | "append" ->
                    let files, loaded = byKey[str (field "baseScenarioKey" fx)]
                    let expectedVersion = match field "expectedAggregateVersion" fx with JInt n -> n | _ -> -1L
                    let verdict = Store.appendLines set loaded expectedVersion (b64 (field "appendedEvents" fx)) |> Result.map ignore
                    verdict, "n/a (the rehearsal adapter compares blob SHA only; no aggregate-version token)"
                | _ ->
                    let files, loaded = byKey[str (field "baseScenarioKey" fx)]
                    match filesOf (field "files" fx) files.DirectoryId with
                    | Error e -> Error e, "n/a"
                    | Ok candidate ->
                        let raw =
                            match rehearsalDecode files.DirectoryId candidate.Events with
                            | Ok(_, evs) ->
                                match Reconcile.plan loaded.Events evs with
                                | Reconcile.Diverged _ -> "rejected (Reconcile.Diverged)"
                                | p -> "ACCEPTED (" + string p + ")"
                            | Error e -> "rejected (" + e + ")"
                        (Store.successor set loaded files.Events candidate |> Result.map ignore), raw
            let ok =
                match adapterVerdict with
                | Error e -> e.Code = expected && (expectedDomain.IsNone || e.DomainCode = expectedDomain)
                | Ok() -> false
            let got = match adapterVerdict with Error e -> e.Code + (e.DomainCode |> Option.map ((+) "/") |> Option.defaultValue "") | Ok() -> "ACCEPTED"
            check ("negative " + name) ok ("adapter=" + got + "; rehearsal codec alone=" + rawRehearsal))
    // Append through the rehearsal encoder into an isolated workspace.
    let appendCheck =
        let files, loaded = byKey["api-latency"]
        let env =
            { CommandId = Uuid.v5 Uuid.urlNamespace "rehearsal-adapter/append-demo"
              Investigation = loaded.State.Id
              ExpectedVersion = Some loaded.State.Version
              Actor = Actor.human "rehearsal-adapter"
              RequestedAt = DateTimeOffset(2026, 10, 22, 21, 0, 0, TimeSpan.Zero)
              Command = RecordObservation(Id.create (Uuid.v5 Uuid.urlNamespace "rehearsal-adapter/obs"), "p95 after batching: 172 ms (synthetic).", None) }
        match Decide.execute (Some loaded.State) env with
        | Error e -> check "append via rehearsal encoder" false (sprintf "%A" e)
        | Ok newEvents ->
            let doc = EventCodec.encode loaded.State.Id (loaded.Events @ newEvents) |> parse
            let nodes = match field "events" doc with JArr xs -> xs | _ -> []
            let id = Uuid.text loaded.InvestigationId
            let newLines = nodes |> List.skip loaded.Events.Length |> List.map (Translate.eventFromRehearsal id >> Json.canonical >> Result.defaultValue "")
            let referenceLines = newEvents |> List.map (EventWire.encode >> Result.bind (Json.canonical >> Result.mapError (CausaError.create "X")) >> Result.defaultValue "")
            let appended = utf8.GetBytes(String.Join("", newLines |> List.map (fun l -> l + "\n")))
            match Store.appendLines set loaded loaded.State.Version appended with
            | Error e -> check "append via rehearsal encoder" false (e.Code + " " + e.Detail)
            | Ok(newFiles, newLoaded) ->
                let dir = Path.Combine(Path.GetTempPath(), "causa-rehearsal-workspace", "workspaces", "demo", "rehearsal-adapter", "investigations", id)
                Directory.CreateDirectory dir |> ignore
                File.WriteAllBytes(Path.Combine(dir, "manifest.json"), newFiles.Manifest)
                File.WriteAllBytes(Path.Combine(dir, "events.ndjson"), newFiles.Events)
                File.WriteAllBytes(Path.Combine(dir, "snapshot.json"), newFiles.Snapshot)
                let reloaded = Store.readDirectory dir |> Result.bind (Store.validate set)
                let ok = newLines = referenceLines && (match reloaded with Ok l -> l.State.Version = loaded.State.Version + 1L | _ -> false)
                check "append via rehearsal encoder" ok (sprintf "new event encoded by rehearsal EventCodec.encode -> v1 bytes identical to reference encoder: %b; isolated workspace reload at version %d" (newLines = referenceLines) newLoaded.State.Version)
    let after = fixturesDigest root
    investigationChecks
    |> List.concat
    |> fun xs ->
        xs
        @ [ check "event-types read via rehearsal codec" (typesRead.Length = 31) (sprintf "%d of 31" typesRead.Length) ]
        @ negatives
        @ [ appendCheck
            check "canonical fixtures unchanged" (before = after) ("fixtures tree digest " + after) ]

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    | [ "verify"; root ] ->
        let checks = verify (Path.GetFullPath root)
        checks |> List.iter (fun c -> printfn "%s %s — %s" (if c.Passed then "PASS" else "FAIL") c.Name c.Detail)
        let failed = checks |> List.filter (fun c -> not c.Passed) |> List.length
        printfn "checks=%d passed=%d failed=%d" checks.Length (checks.Length - failed) failed
        if failed = 0 then 0 else 1
    | _ ->
        eprintfn "usage: rehearsal-adapter verify <repository-root>"
        2
