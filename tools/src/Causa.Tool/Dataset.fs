namespace Causa.Tool

open System
open System.IO
open IndyInitRootCauseInvestigator.Engine
open Causa.Contract

/// Layout of the dataset repository (CONTRACT §3).
module Layout =
    let schemas root = Path.Combine(root, "contract", "v1.0.0", "schemas")
    let bundle root = Path.Combine(schemas root, "bundle.json")
    let golden root = Path.Combine(root, "contract", "v1.0.0", "golden")
    let fixtures root = Path.Combine(root, "fixtures", "v1.0.0")
    let rootManifest root = Path.Combine(fixtures root, "manifest.json")
    let investigations root = Path.Combine(fixtures root, "investigations")
    let negative root = Path.Combine(fixtures root, "negative")

    let posix (path: string) = path.Replace('\\', '/')

    let writeBytes (path: string) (bytes: byte[]) =
        Directory.CreateDirectory(Path.GetDirectoryName path |> nonNull) |> ignore
        File.WriteAllBytes(path, bytes)

    let orFail (r: Result<'T, CausaError>) =
        match r with
        | Ok v -> v
        | Error e -> failwith (e.Code + ": " + e.Detail)

module Bundle =
    /// CONTRACT §10.3: one entry per *.schema.json (path relative to the schemas directory,
    /// '/'-separated, sorted ordinally), digest over exact file bytes.
    let manifest (schemasDir: string) : JValue =
        let entries =
            Directory.GetFiles(schemasDir, "*.schema.json", SearchOption.AllDirectories)
            |> Array.map (fun p -> Layout.posix (Path.GetRelativePath(schemasDir, p)), Json.sha256Hex (File.ReadAllBytes p))
            |> Array.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))
            |> Array.map (fun (p, h) -> JObj [ "path", JStr p; "sha256", JStr h ])
            |> List.ofArray
        JObj
            [ "formatVersion", JStr Contract.formatVersion
              "contractVersion", JStr "1.0.0"
              "algorithm", JStr "sha256"
              "canonicalization", JStr "RFC8785"
              "schemas", JArr entries ]

    let bytes schemasDir = Json.fileBytes (manifest schemasDir) |> Result.mapError (CausaError.create Codes.unrepresentable)

/// Deliberately invalid inputs, each built so that exactly the targeted check is the first
/// to fail in contract order (CONTRACT §11.1).
module Negatives =
    type Negative =
        { Name: string
          Description: string
          Code: string
          DomainCode: string option
          Body: (string * JValue) list }

    let private b64 (bytes: byte[]) = JObj [ "base64", JStr(Convert.ToBase64String bytes) ]

    let private filesJson (f: InvestigationFiles) =
        JObj [ "manifest.json", b64 f.Manifest; "events.ndjson", b64 f.Events; "snapshot.json", b64 f.Snapshot ]

    let private investigationBody (f: InvestigationFiles) = [ "kind", JStr "investigation"; "directoryId", JStr f.DirectoryId; "files", filesJson f ]

    let private utf8 = Json.strictUtf8
    let private parse (bytes: byte[]) = utf8.GetString bytes |> fun t -> t.TrimEnd('\n') |> Json.parse |> Result.mapError (CausaError.create Codes.jsonMalformed) |> Layout.orFail

    let private lines (f: InvestigationFiles) =
        (utf8.GetString f.Events).TrimEnd('\n').Split('\n') |> List.ofArray |> List.map (Json.parse >> Result.mapError (CausaError.create Codes.jsonMalformed) >> Layout.orFail)

    let private ndjson (values: JValue list) =
        values |> List.map (Json.canonical >> Result.mapError (CausaError.create Codes.unrepresentable) >> Layout.orFail >> fun t -> t + "\n") |> String.concat "" |> utf8.GetBytes

    let private file (v: JValue) = Json.fileBytes v |> Result.mapError (CausaError.create Codes.unrepresentable) |> Layout.orFail

    /// Replace the value at a key path inside nested objects.
    let rec set (path: string list) (value: JValue) (v: JValue) =
        match path, v with
        | [ k ], JObj o -> JObj(if o |> List.exists (fst >> (=) k) then o |> List.map (fun (n, x) -> if n = k then n, value else n, x) else o @ [ k, value ])
        | k :: rest, JObj o -> JObj(o |> List.map (fun (n, x) -> if n = k then n, set rest value x else n, x))
        | _ -> v

    let rec get (path: string list) (v: JValue) =
        match path, v with
        | [], x -> x
        | k :: rest, JObj o -> Json.field k o |> Option.map (get rest) |> Option.defaultValue JNull
        | _ -> JNull

    /// Re-point the manifest at new events bytes (and optionally a new snapshot), keeping it self-consistent.
    let private withEvents (f: InvestigationFiles) (events: byte[]) (snapshot: JValue option) (count: int64 option) =
        let m0 = parse f.Manifest
        let m1 = set [ "eventsSha256" ] (JStr(Json.sha256Hex events)) m0
        let m2 = match snapshot with Some s -> set [ "snapshotSha256" ] (JStr(Json.digest s |> Result.defaultValue "")) m1 | None -> m1
        let m3 = match count with Some n -> m2 |> set [ "eventCount" ] (JInt n) |> set [ "aggregateVersion" ] (JInt n) | None -> m2
        { f with
            Events = events
            Manifest = file m3
            Snapshot = snapshot |> Option.map file |> Option.defaultValue f.Snapshot }

    let private lastAt (f: InvestigationFiles) = lines f |> List.last |> get [ "occurredAt" ]

    let private eventIdOf (inv: string) (version: int64) (corr: string) =
        Uuid.text (Uuid.eventId (Guid.Parse inv) version (Guid.Parse corr))

    /// A forged event line (bypassing decide) appended after the history, with a derived eventId.
    let private forgedLine (f: InvestigationFiles) (corrName: string) (eventType: string) (actor: JValue) (payload: JValue) (atMinutesAfterLast: float) =
        let last = lines f |> List.last
        let inv = match get [ "investigationId" ] last with JStr s -> s | _ -> ""
        let version = (match get [ "aggregateVersion" ] last with JInt n -> n | _ -> 0L) + 1L
        let corr = Uuid.text (ScenarioKit.guid "negative" "command" corrName)
        let at =
            match get [ "occurredAt" ] last with
            | JStr s -> (Timestamp.tryParse s |> Option.get).AddMinutes atMinutesAfterLast |> Timestamp.tryFormat |> Option.get
            | _ -> ""
        JObj
            [ "formatVersion", JStr "1.0.0"; "eventId", JStr(eventIdOf inv version corr); "investigationId", JStr inv
              "aggregateVersion", JInt version; "eventType", JStr eventType; "occurredAt", JStr at; "actor", actor
              "semanticVersion", JStr "1.0.0"; "correlationId", JStr corr; "payload", payload ]

    let private human = JObj [ "actorId", JStr "investigator-01"; "kind", JStr "human" ]

    /// Engine-level forgery: decode the forged lines, replay WITHOUT admissibility checks, and
    /// write a fully self-consistent file set (hashes and snapshot agree). Only replay admissibility
    /// can then reject it.
    let private forgedHistory (base': Loaded) (extra: JValue list) =
        let forged = extra |> List.map (EventWire.decode >> Layout.orFail >> fun d -> d.Event)
        Store.buildUnchecked (base'.Events @ forged) |> Layout.orFail

    let private dropEvent (n: int) (vs: JValue list) = vs |> List.indexed |> List.filter (fun (i, _) -> i <> n) |> List.map snd

    let build (byKey: Map<string, InvestigationFiles * Loaded>) : Negative list =
        let filesOf k = fst byKey[k]
        let loadedOf k = snd byKey[k]
        let a, b, c, d, e = filesOf "payment-failure", filesOf "intermittent-outage", filesOf "conflicting-metrics", filesOf "api-latency", filesOf "authentication-outage"
        let neg name code domain description body = { Name = name; Description = description; Code = code; DomainCode = domain; Body = body }
        let inv name code description (f: InvestigationFiles) = neg name code None description (investigationBody f)
        let hypothesisRecordedIndex (f: InvestigationFiles) =
            lines f |> List.findIndex (fun v -> get [ "eventType" ] v = JStr "HypothesisRecorded")
        let mutateLine (f: InvestigationFiles) (index: int) (change: JValue -> JValue) =
            let vs = lines f |> List.mapi (fun i v -> if i = index then change v else v)
            let bytes = ndjson vs
            withEvents f bytes None None
        let hA = hypothesisRecordedIndex a
        let causeId (f: InvestigationFiles) =
            lines f |> List.pick (fun v -> if get [ "eventType" ] v = JStr "CausalAssertionRecorded" then Some(get [ "payload"; "causeId" ] v) else None)
        let unhypothesis =
            lines a |> List.pick (fun v -> if get [ "eventType" ] v = JStr "HypothesisRecorded" && get [ "payload"; "statement" ] v = JStr "Currency rounding in the EU price service produces amounts the processor rejects." then Some(get [ "payload"; "hypothesisId" ] v) else None)
        let evidenceOfA = lines a |> List.pick (fun v -> if get [ "eventType" ] v = JStr "EvidenceAdmitted" then Some(get [ "payload"; "evidenceId" ] v) else None)
        [
          // ---- encoding / syntax / canonical form ----
          inv "bom-in-events" Codes.encoding "events.ndjson starts with a UTF-8 byte order mark."
              { a with Events = Array.append [| 0xEFuy; 0xBBuy; 0xBFuy |] a.Events }
          inv "truncated-event-line" Codes.jsonMalformed "The last events.ndjson line is truncated mid-object."
              (let t = (utf8.GetString a.Events).TrimEnd('\n') in { a with Events = utf8.GetBytes(t.Substring(0, t.Length - 7) + "\n") })
          inv "feff-inside-string" Codes.jsonMalformed "An observation's text contains U+FEFF (escaped as \\ufeff); U+FEFF is forbidden everywhere (CONTRACT §2.3)."
              (let t = (utf8.GetString a.Events).TrimEnd('\n').Split('\n')
               let l2 = t[2].Replace("\"original\":\"", "\"original\":\"\\ufeff")
               { a with Events = utf8.GetBytes(String.Join("\n", Array.concat [ t[0..1]; [| l2 |]; t[3..] ]) + "\n") })
          inv "duplicate-json-key" Codes.jsonMalformed "An event object repeats the key 'eventType' (JSON objects must have unique keys)."
              (let t = (utf8.GetString a.Events).TrimEnd('\n').Split('\n')
               let l0 = t[0].Replace("\"eventType\":\"InvestigationStarted\"", "\"eventType\":\"InvestigationStarted\",\"eventType\":\"InvestigationStarted\"")
               { a with Events = utf8.GetBytes(String.Join("\n", Array.append [| l0 |] t[1..]) + "\n") })
          inv "non-canonical-manifest" Codes.nonCanonical "manifest.json is pretty-printed instead of RFC 8785 canonical form."
              { a with Manifest = utf8.GetBytes((utf8.GetString a.Manifest).TrimEnd('\n').Replace(",", ", ") + "\n") }
          // ---- versions / registry / schema ----
          inv "unsupported-format-version" Codes.unsupportedVersion "An event declares formatVersion 2.0.0."
              (mutateLine a 1 (set [ "formatVersion" ] (JStr "2.0.0")))
          inv "unknown-event-type" Codes.unknownEventType "An event with the unregistered type 'InvestigationDeleted' is appended; readers must fail closed, not skip it."
              (let extra = forgedLine a "unknown-type" "InvestigationDeleted" human (JObj [ "reason", JStr "cleanup" ]) 4.0
               withEvents a (ndjson (lines a @ [ extra ])) None (Some(int64 (lines a).Length + 1L)))
          inv "invalid-field-type" Codes.schemaViolation "A HypothesisRecorded statement is the integer 42 instead of a string."
              (mutateLine a hA (set [ "payload"; "statement" ] (JInt 42L)))
          inv "unexpected-payload-field" Codes.schemaViolation "A HypothesisRecorded payload carries an undeclared field 'confidence' (closed core structure)."
              (mutateLine a hA (set [ "payload"; "confidence" ] (JStr "high")))
          inv "invalid-enum-value" Codes.schemaViolation "An UnknownIdentified materiality is 'critical', outside the materiality enumeration."
              (let i = lines a |> List.findIndex (fun v -> get [ "eventType" ] v = JStr "UnknownIdentified")
               mutateLine a i (set [ "payload"; "materiality" ] (JStr "critical")))
          inv "timestamp-without-milliseconds" Codes.schemaViolation "An occurredAt omits the required three fractional digits."
              (mutateLine a 2 (fun v -> match get [ "occurredAt" ] v with JStr s -> set [ "occurredAt" ] (JStr(s.Replace(".000Z", "Z"))) v | _ -> v))
          inv "identifier-trailing-newline" Codes.schemaViolation "An eventId carries a trailing LF inside the string (ECMA-262 '$' anchors match only at the end)."
              (mutateLine a 2 (fun v -> match get [ "eventId" ] v with JStr s -> set [ "eventId" ] (JStr(s + "\n")) v | _ -> v))
          inv "agent-authored-hypothesis" Codes.schemaViolation "A HypothesisRecorded event is attributed to an AI agent; agents may only record proposals (D-004)."
              (mutateLine a hA (set [ "actor" ] (JObj [ "actorId", JStr "assistant-01"; "kind", JStr "agent"; "provider", JStr "synthetic-provider"; "model", JStr "synthetic-model-1" ])))
          // ---- manifest / integrity ----
          inv "manifest-version-mismatch" Codes.manifestMismatch "The manifest claims aggregateVersion N+1 while events.ndjson holds N events."
              (let m = parse a.Manifest in { a with Manifest = file (set [ "aggregateVersion" ] (JInt(int64 (lines a).Length + 1L)) m) })
          inv "altered-event-without-rehash" Codes.eventsHashMismatch "A prior observation's verbatim text was edited in place; the recorded eventsSha256 no longer matches."
              (let i = lines a |> List.findIndex (fun v -> get [ "eventType" ] v = JStr "ObservationRecorded")
               let vs = lines a |> List.mapi (fun n v -> if n = i then set [ "payload"; "original" ] (JStr "Failures are spread evenly across all clusters.") v else v)
               { a with Events = ndjson vs })
          inv "broken-snapshot-hash" Codes.snapshotHashMismatch "The manifest's snapshotSha256 is not the digest of snapshot.json."
              (let m = parse a.Manifest in { a with Manifest = file (set [ "snapshotSha256" ] (JStr(String('0', 64))) m) })
          inv "forged-snapshot" Codes.snapshotMismatch "snapshot.json claims the api-latency investigation is resolved; hashes are consistent but the snapshot is not the replayed projection."
              (let s = parse d.Snapshot |> set [ "state"; "lifecycle" ] (JStr "resolved")
               withEvents d d.Events (Some s) None)
          // ---- identity / ordering ----
          inv "wrong-investigation-id" Codes.investigationMismatch "One event names a different investigation than its manifest."
              (let other = match get [ "investigationId" ] (lines b |> List.head) with JStr s -> s | _ -> ""
               mutateLine a 3 (set [ "investigationId" ] (JStr other)))
          inv "duplicate-event-id" Codes.eventIdDuplicate "Two events carry the same eventId."
              (let id2 = get [ "eventId" ] (List.item 1 (lines a)) in mutateLine a 2 (set [ "eventId" ] id2))
          inv "sequence-gap" Codes.sequenceInvalid "Event 5 is missing; versions jump from 4 to 6 (the manifest counts are consistent with the remaining lines)."
              (let vs = lines a |> dropEvent 4
               let f = withEvents a (ndjson vs) None (Some(int64 vs.Length))
               let m = parse f.Manifest |> set [ "aggregateVersion" ] (JInt(int64 vs.Length)) |> set [ "updatedAt" ] (lastAt a)
               { f with Manifest = file m })
          inv "duplicate-aggregate-version" Codes.sequenceInvalid "Two consecutive events both claim aggregateVersion 3."
              (mutateLine a 3 (fun v ->
                  let corr = match get [ "correlationId" ] v with JStr s -> s | _ -> ""
                  let invId = match get [ "investigationId" ] v with JStr s -> s | _ -> ""
                  v |> set [ "aggregateVersion" ] (JInt 3L) |> set [ "eventId" ] (JStr(eventIdOf invId 3L corr))))
          inv "correlation-reused" Codes.correlationReused "A later, non-adjacent event reuses the correlationId of an earlier command (its eventId re-derived consistently)."
              (let vs = lines a
               let corr = get [ "correlationId" ] (List.item 2 vs)
               mutateLine a 5 (fun v ->
                  let invId = match get [ "investigationId" ] v with JStr s -> s | _ -> ""
                  let ver = match get [ "aggregateVersion" ] v with JInt n -> n | _ -> 0L
                  let c = match corr with JStr s -> s | _ -> ""
                  v |> set [ "correlationId" ] corr |> set [ "eventId" ] (JStr(eventIdOf invId ver c))))
          inv "manifest-interior-newline" Codes.nonCanonical "manifest.json is valid JSON split across lines (interior LFs are a canonical-form violation, not a framing error)."
              { a with Manifest = utf8.GetBytes((utf8.GetString a.Manifest).TrimEnd('\n').Replace(",\"", ",\n\"") + "\n") }
          inv "forged-event-id" Codes.eventIdMismatch "An eventId is a random UUID rather than the derived UUIDv5."
              (mutateLine a 2 (set [ "eventId" ] (JStr "1b4e28ba-2fa1-41d2-883f-0016d3cca427")))
          // ---- semantic admissibility (engine History.validate) ----
          neg "illegal-hypothesis-transition" Codes.historyInadmissible (Some "HISTORY.INADMISSIBLE_EVENT")
              "A proposed hypothesis jumps straight to supported (markSupported is illegal from proposed). Hashes and snapshot are consistent."
              (investigationBody (forgedHistory (loadedOf "payment-failure")
                  [ forgedLine a "illegal-transition" "HypothesisTransitioned" human
                        (JObj [ "hypothesisId", unhypothesis; "from", JStr "proposed"; "to", JStr "supported"; "command", JStr "markSupported"
                                "reason", JStr "Asserted without the plausible step."; "evidenceIds", JArr [ evidenceOfA ] ]) 4.0 ]))
          neg "forged-resolution" Codes.historyInadmissible (Some "HISTORY.UNGATED_RESOLUTION")
              "The api-latency investigation is marked resolved although its correction is unverified. Hashes and snapshot are consistent."
              (investigationBody (forgedHistory (loadedOf "api-latency")
                  [ forgedLine d "forged-resolution" "LifecycleChanged" human
                        (JObj [ "from", JStr "awaitingVerification"; "to", JStr "resolved"; "command", JStr "resolveInvestigation"; "reason", JNull ]) 4.0 ]))
          neg "forged-cause-confirmation" Codes.historyInadmissible (Some "HISTORY.UNGATED_CONFIRMATION")
              "The conflicting-metrics cause is confirmed despite a weakened member and an open blocking contradiction."
              (investigationBody (forgedHistory (loadedOf "conflicting-metrics")
                  [ forgedLine c "forged-confirmation" "CausalAssertionConfirmed" human (JObj [ "causeId", causeId c; "policy", JStr "core-default/1" ]) 4.0 ]))
          neg "missing-start" Codes.historyInadmissible (Some "HISTORY.NO_START")
              "The first event (InvestigationStarted) is removed and later events renumbered consistently."
              (let vs =
                  lines b |> List.tail
                  |> List.mapi (fun i v ->
                      let corr = match get [ "correlationId" ] v with JStr s -> s | _ -> ""
                      let invId = match get [ "investigationId" ] v with JStr s -> s | _ -> ""
                      v |> set [ "aggregateVersion" ] (JInt(int64 (i + 1))) |> set [ "eventId" ] (JStr(eventIdOf invId (int64 (i + 1)) corr)))
               let f = withEvents b (ndjson vs) None (Some(int64 vs.Length))
               let m = parse f.Manifest |> set [ "createdAt" ] (get [ "occurredAt" ] vs.Head)
               investigationBody { f with Manifest = file m })
          // ---- path safety ----
          neg "path-traversal-directory" Codes.pathInvalid None "The investigation directory name is '../../fixtures' instead of a canonical UUID."
              (investigationBody { a with DirectoryId = "../../fixtures" })
          // ---- provider-level ----
          neg "stale-expected-version" Codes.versionStale None
              "An append to api-latency names expectedAggregateVersion N-1 (a stale base); it must be refused and never overwrite."
              [ "kind", JStr "append"; "baseScenarioKey", JStr "api-latency"; "expectedAggregateVersion", JInt((loadedOf "api-latency").State.Version - 1L)
                "appendedEvents",
                b64 (ndjson [ forgedLine d "stale-append" "ObservationRecorded" human
                                  (JObj [ "observationId", JStr(Uuid.text (ScenarioKit.guid "negative" "observation" "stale")); "original", JStr "p95 is 190 ms this morning."; "observedAt", JNull ]) 4.0 ]) ]
          neg "rewritten-history" Codes.historyRewritten None
              "A successor of payment-failure edits an earlier hypothesis statement and recomputes every hash and the snapshot; it is valid in isolation but does not extend the accepted history."
              [ "kind", JStr "successor"; "baseScenarioKey", JStr "payment-failure"
                "files",
                filesJson (
                    let base' = loadedOf "payment-failure"
                    let edited =
                        base'.Events
                        |> List.map (fun ev ->
                            match ev.Body with
                            | HypothesisRecorded(h, _, m) when Uuid.text h.Value = (match unhypothesis with JStr s -> s | _ -> "") ->
                                { ev with Body = HypothesisRecorded(h, NonEmptyText.create "Currency rounding was never considered." |> Result.toOption |> Option.get, m) }
                            | _ -> ev)
                    Store.buildUnchecked edited |> Layout.orFail) ] ]

    let toJson (n: Negative) : JValue =
        JObj(
            [ "formatVersion", JStr "1.0.0"; "name", JStr n.Name; "description", JStr n.Description; "expectedErrorCode", JStr n.Code
              "expectedDomainCode", (n.DomainCode |> Option.map JStr |> Option.defaultValue JNull) ]
            @ n.Body)

module Dataset =
    let datasetCreatedAt = DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero)

    type Generated =
        { Runs: Runner.Run list
          ByKey: Map<string, InvestigationFiles * Loaded> }

    let private orFail = Layout.orFail

    let private fromBase64 (v: JValue) = match v with JObj o -> (match Json.field "base64" o with Some(JStr s) -> Convert.FromBase64String s | _ -> [||]) | _ -> [||]

    let filesFrom (directoryId: string) (v: JValue) =
        { DirectoryId = directoryId
          Manifest = fromBase64 (Negatives.get [ "manifest.json" ] v)
          Events = fromBase64 (Negatives.get [ "events.ndjson" ] v)
          Snapshot = fromBase64 (Negatives.get [ "snapshot.json" ] v) }

    /// Evaluate a negative fixture; returns the reader's verdict (Ok = wrongly accepted).
    let evaluate (set: SchemaCheck.SchemaSet) (byKey: Map<string, InvestigationFiles * Loaded>) (fixture: JValue) : Result<unit, CausaError> =
        let str k = match Negatives.get [ k ] fixture with JStr s -> s | _ -> ""
        match str "kind" with
        | "investigation" -> Store.validate set (filesFrom (str "directoryId") (Negatives.get [ "files" ] fixture)) |> Result.map ignore
        | "append" ->
            match byKey |> Map.tryFind (str "baseScenarioKey") with
            | None -> CausaError.fail Codes.schemaViolation "unknown base scenario"
            | Some(_, loaded) ->
                let expected = match Negatives.get [ "expectedAggregateVersion" ] fixture with JInt n -> n | _ -> -1L
                Store.appendLines set loaded expected (fromBase64 (Negatives.get [ "appendedEvents" ] fixture)) |> Result.map ignore
        | "successor" ->
            match byKey |> Map.tryFind (str "baseScenarioKey") with
            | None -> CausaError.fail Codes.schemaViolation "unknown base scenario"
            | Some(files, loaded) ->
                Store.successor set loaded files.Events (filesFrom files.DirectoryId (Negatives.get [ "files" ] fixture)) |> Result.map ignore
        | other -> CausaError.fail Codes.schemaViolation ("unknown negative kind '" + other + "'")

    let generate (set: SchemaCheck.SchemaSet) : Result<Generated, string> =
        Scenarios.all
        |> traverse Runner.run
        |> Result.bind (fun runs ->
            runs
            |> traverse (fun r -> Store.build set r.Events |> Result.map (fun built -> r.Scenario.Key, built) |> Result.mapError (fun e -> r.Scenario.Key + ": " + e.Code + " " + e.Detail))
            |> Result.map (fun built -> { Runs = runs; ByKey = Map.ofList built }))

    /// Write the complete dataset under `root`. Only generated outputs are written; schemas are inputs.
    let write (root: string) (set: SchemaCheck.SchemaSet) (g: Generated) =
        let bundleBytes = Bundle.bytes (Layout.schemas root) |> orFail
        Layout.writeBytes (Layout.bundle root) bundleBytes
        let schemaSha = Json.sha256Hex bundleBytes
        let ordered = g.Runs |> List.sortWith (fun a b -> String.CompareOrdinal(Uuid.text a.Scenario.Investigation.Value, Uuid.text b.Scenario.Investigation.Value))
        let entries =
            ordered
            |> List.map (fun r ->
                let files, loaded = g.ByKey[r.Scenario.Key]
                let dir = Path.Combine(Layout.investigations root, files.DirectoryId)
                Layout.writeBytes (Path.Combine(dir, "manifest.json")) files.Manifest
                Layout.writeBytes (Path.Combine(dir, "events.ndjson")) files.Events
                Layout.writeBytes (Path.Combine(dir, "snapshot.json")) files.Snapshot
                Layout.writeBytes (Path.Combine(Layout.golden root, r.Scenario.Key + ".state.json")) (Json.fileBytes loaded.StateJson |> Result.mapError (CausaError.create "X") |> orFail)
                JObj
                    [ "investigationId", JStr files.DirectoryId; "scenarioKey", JStr r.Scenario.Key; "title", JStr r.Scenario.Title
                      "expectedVersion", JInt loaded.State.Version; "expectedLifecycle", JStr(Enums.lifecycle.ToWire loaded.State.Lifecycle)
                      "expectedStateSha256", JStr loaded.StateSha256; "eventsSha256", JStr loaded.EventsSha256
                      "manifestSha256", JStr loaded.ManifestSha256; "snapshotSha256", JStr loaded.SnapshotSha256 ])
        let negatives = Negatives.build g.ByKey |> List.sortWith (fun a b -> String.CompareOrdinal(a.Name, b.Name))
        negatives |> List.iter (fun n -> Layout.writeBytes (Path.Combine(Layout.negative root, n.Name + ".json")) (Json.fileBytes (Negatives.toJson n) |> Result.mapError (CausaError.create "X") |> orFail))
        let rootManifest =
            JObj
                [ "formatVersion", JStr Contract.formatVersion; "datasetId", JStr Contract.datasetId
                  "createdAt", JStr(Timestamp.tryFormat datasetCreatedAt |> Option.get); "schemaSha256", JStr schemaSha
                  "investigations", JArr entries
                  "negativeFixtures", JArr(negatives |> List.map (fun n -> JObj [ "path", JStr("negative/" + n.Name + ".json"); "expectedErrorCode", JStr n.Code ])) ]
        Layout.writeBytes (Layout.rootManifest root) (Json.fileBytes rootManifest |> Result.mapError (CausaError.create "X") |> orFail)
        let scenarios =
            JObj
                [ "formatVersion", JStr "1.0.0"
                  "scenarios",
                  JArr(
                      g.Runs
                      |> List.map (fun r ->
                          let _, loaded = g.ByKey[r.Scenario.Key]
                          JObj
                              [ "scenarioKey", JStr r.Scenario.Key; "investigationId", JStr(Uuid.text r.Scenario.Investigation.Value)
                                "commands", JInt(int64 r.Scenario.Steps.Length); "events", JInt(int64 loaded.Events.Length)
                                "eventTypes", JArr(loaded.Events |> List.map (fun e -> CaseNames.eventType e.Body) |> List.distinct |> List.sort |> List.map JStr)
                                "refusals",
                                JArr(r.Refusals |> List.map (fun (i, code, all) -> JObj [ "step", JInt(int64 i); "expectedCode"
                                                                                          , JStr code; "engineCodes", JStr all ])) ])) ]
        Layout.writeBytes (Path.Combine(Layout.golden root, "scenarios.json")) (Json.fileBytes scenarios |> Result.mapError (CausaError.create "X") |> orFail)

    type Check = { Name: string; Passed: bool; Detail: string }

    let private check name passed detail = { Name = name; Passed = passed; Detail = detail }

    let private readJson (path: string) =
        File.ReadAllBytes path |> Json.decodeUtf8 |> Result.bind (fun t -> Json.parse (t.TrimEnd('\n')))

    /// Full dataset verification from the files on disk only (no generator state).
    let verify (root: string) : Check list =
        match Store.loadSchemas (Layout.schemas root) with
        | Error e -> [ check "schemas.load" false (e.Code + " " + e.Detail) ]
        | Ok set ->
            let bundleOnDisk = File.ReadAllBytes(Layout.bundle root)
            let bundleExpected = Bundle.bytes (Layout.schemas root) |> orFail
            let bundleOk = bundleOnDisk = bundleExpected
            let bundleSchema = readJson (Layout.bundle root) |> Result.map (SchemaCheck.check set Store.schemaIds.Bundle)
            let schemaSha = Json.sha256Hex bundleOnDisk
            match readJson (Layout.rootManifest root) with
            | Error e -> [ check "root-manifest.parse" false e ]
            | Ok rootM ->
                let rootBytes = File.ReadAllBytes(Layout.rootManifest root)
                let rootCanonical = (Json.fileBytes rootM |> Result.defaultValue [||]) = rootBytes
                let rootSchema = SchemaCheck.check set Store.schemaIds.Root rootM
                let entries = match Negatives.get [ "investigations" ] rootM with JArr xs -> xs | _ -> []
                let str k v = match Negatives.get [ k ] v with JStr s -> s | _ -> ""
                let ids = entries |> List.map (str "investigationId")
                let sortedUnique = ids = (ids |> List.distinct |> List.sortWith (fun a b -> String.CompareOrdinal(a, b)))
                let dirs = Directory.GetDirectories(Layout.investigations root) |> Array.map (fun p -> Path.GetFileName p |> nonNull) |> Array.sort |> List.ofArray
                let layoutOk = dirs = (ids |> List.sort) && dirs |> List.forall (fun d -> (Directory.GetFileSystemEntries(Path.Combine(Layout.investigations root, d)) |> Array.map (fun p -> Path.GetFileName p |> nonNull) |> Array.sort |> List.ofArray) = List.sort Store.fileNames)
                let loadedResults =
                    entries
                    |> List.map (fun entry ->
                        let key = str "scenarioKey" entry
                        let id = str "investigationId" entry
                        let files = Store.readDirectory (Path.Combine(Layout.investigations root, id))
                        match Store.validate set files with
                        | Error e -> key, Error(e.Code + " " + e.Detail), None
                        | Ok loaded ->
                            let drift =
                                [ "eventsSha256", loaded.EventsSha256; "manifestSha256", loaded.ManifestSha256; "snapshotSha256", loaded.SnapshotSha256
                                  "expectedStateSha256", loaded.StateSha256; "expectedLifecycle", Enums.lifecycle.ToWire loaded.State.Lifecycle
                                  "title", loaded.State.Title.Value ]
                                |> List.filter (fun (k, v) -> str k entry <> v)
                                |> List.map fst
                                |> fun xs -> if (match Negatives.get [ "expectedVersion" ] entry with JInt n -> n | _ -> -1L) <> loaded.State.Version then xs @ [ "expectedVersion" ] else xs
                            let goldenPath = Path.Combine(Layout.golden root, key + ".state.json")
                            let goldenOk = File.Exists goldenPath && File.ReadAllBytes goldenPath = (Json.fileBytes loaded.StateJson |> Result.defaultValue [||])
                            match drift, goldenOk with
                            | [], true -> key, Ok loaded, Some(files, loaded)
                            | [], false -> key, Error(Codes.goldenMismatch + " golden projection differs from replayed state"), None
                            | ds, _ -> key, Error(Codes.fixtureDrift + " " + String.Join(",", ds)), None)
                let byKey = loadedResults |> List.choose (fun (k, _, x) -> x |> Option.map (fun v -> k, v)) |> Map.ofList
                let negEntries = match Negatives.get [ "negativeFixtures" ] rootM with JArr xs -> xs | _ -> []
                let negFiles = Directory.GetFiles(Layout.negative root, "*.json") |> Array.map (fun p -> "negative/" + (Path.GetFileName p |> nonNull)) |> Array.sort |> List.ofArray
                let listedNeg = negEntries |> List.map (str "path")
                let negativeChecks =
                    negEntries
                    |> List.map (fun n ->
                        let path = str "path" n
                        let expectedCode = str "expectedErrorCode" n
                        match readJson (Path.Combine(Layout.fixtures root, path)) with
                        | Error e -> check ("negative " + path) false e
                        | Ok fixture ->
                            let schemaErrors = SchemaCheck.check set Store.schemaIds.Negative fixture
                            let expectedDomain = match Negatives.get [ "expectedDomainCode" ] fixture with JStr s -> Some s | _ -> None
                            let fileCodeAgrees = str "expectedErrorCode" fixture = expectedCode
                            match schemaErrors, evaluate set byKey fixture with
                            | e :: _, _ -> check ("negative " + path) false ("fixture schema: " + e)
                            | [], Ok() -> check ("negative " + path) false "WRONGLY ACCEPTED"
                            | [], Error err ->
                                let ok = fileCodeAgrees && err.Code = expectedCode && (expectedDomain.IsNone || err.DomainCode = expectedDomain)
                                check ("negative " + path) ok (err.Code + (err.DomainCode |> Option.map (fun d -> "/" + d) |> Option.defaultValue "") + ": " + err.Detail))
                let coverage =
                    byKey |> Map.toList |> List.collect (fun (_, (_, l)) -> l.Events |> List.map (fun e -> CaseNames.eventType e.Body)) |> List.distinct
                let missing = EventWire.eventTypes |> List.filter (fun t -> not (List.contains t coverage))
                let scenariosSchema =
                    readJson (Path.Combine(Layout.golden root, "scenarios.json"))
                    |> Result.map (SchemaCheck.check set (Store.schemaIds.Bundle.Replace("schema-bundle", "golden-scenarios")))
                [ check "golden.scenarios.schema" (scenariosSchema = Ok []) (sprintf "%A" scenariosSchema)
                  check "schema-bundle.reproducible" bundleOk "bundle.json equals the recomputed bundle manifest"
                  check "schema-bundle.schema" (bundleSchema = Ok []) (sprintf "%A" bundleSchema)
                  check "schema-bundle.unresolved-refs" (SchemaCheck.unresolvedReferences set).IsEmpty "every $ref resolves"
                  check "root-manifest.schemaSha256" (str "schemaSha256" rootM = schemaSha) ("bundle sha256 " + schemaSha)
                  check "root-manifest.canonical" rootCanonical "RFC 8785 + LF"
                  check "root-manifest.schema" rootSchema.IsEmpty (String.Join("; ", rootSchema))
                  check "root-manifest.order" sortedUnique "investigations sorted by investigationId, unique"
                  check "layout.exact" layoutOk "investigation directories and files match the index exactly"
                  check "negative.index-complete" (listedNeg = negFiles) "every negative file is listed and every listed file exists"
                  check "coverage.event-types" missing.IsEmpty ("all " + string EventWire.eventTypes.Length + " registered event types appear in valid fixtures" + (if missing.IsEmpty then "" else "; missing " + String.Join(",", missing))) ]
                @ (loadedResults |> List.map (fun (k, r, _) -> match r with Ok l -> check ("investigation " + k) true (sprintf "replayed %d events, lifecycle %s, state %s" l.Events.Length (Enums.lifecycle.ToWire l.State.Lifecycle) l.StateSha256) | Error e -> check ("investigation " + k) false e))
                @ negativeChecks
