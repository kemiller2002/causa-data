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

    /// Like forgedHistory, but asserts (at generation time) that the pinned engine refuses the last forged
    /// command group for exactly `refusal` and nothing else, or emits different events when `refusal` = "MISMATCH".
    /// A more lenient engine would then accept the fixture and fail the gate (review finding M2).
    let private soleRefusal (base': Loaded) (extra: JValue list) (refusal: string) =
        let forged = extra |> List.map (EventWire.decode >> Layout.orFail >> fun d -> d.Event)
        let lastCorr = (List.last forged).CommandId
        let before = base'.Events @ (forged |> List.filter (fun e -> e.CommandId <> lastCorr))
        let group = forged |> List.filter (fun e -> e.CommandId = lastCorr)
        let first = group.Head
        let prior = Evolve.replay before
        match History.commandOf first.Investigation (group |> List.map (fun e -> e.Body)) with
        | None -> failwithf "negative for %s: forged group maps to no command" refusal
        | Some command ->
            let env = { CommandId = first.CommandId; Investigation = first.Investigation; ExpectedVersion = None; Actor = first.Actor; RequestedAt = first.At; Command = command }
            match Decide.decide prior env, refusal with
            | Error errors, r when errors |> List.map (fun e -> e.Code) = [ r ] -> ()
            | Ok bodies, "MISMATCH" when bodies <> (group |> List.map (fun e -> e.Body)) -> ()
            | other, r -> failwithf "negative for %s is not a sole refusal: %A" r other
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
          // ---- every framing / syntax / canonical rule (review M2) ----
          inv "events-crlf" Codes.encoding "events.ndjson uses CRLF line endings."
              { a with Events = utf8.GetBytes((utf8.GetString a.Events).Replace("\n", "\r\n")) }
          inv "events-missing-final-lf" Codes.encoding "events.ndjson does not end with LF."
              { a with Events = utf8.GetBytes((utf8.GetString a.Events).TrimEnd('\n')) }
          inv "events-blank-line" Codes.encoding "events.ndjson contains a blank line between events."
              { a with Events = utf8.GetBytes((utf8.GetString a.Events).Replace("}\n{", "}\n\n{")) }
          inv "events-invalid-utf8" Codes.encoding "events.ndjson contains a byte sequence that is not UTF-8 (0xC3 0x28)."
              { a with Events = Array.concat [ Array.sub a.Events 0 40; [| 0xC3uy; 0x28uy |]; Array.sub a.Events 40 (a.Events.Length - 40) ] }
          inv "events-empty" Codes.encoding "events.ndjson is empty." { a with Events = [||] }
          inv "snapshot-missing-final-lf" Codes.encoding "snapshot.json does not end with LF."
              { a with Snapshot = utf8.GetBytes((utf8.GetString a.Snapshot).TrimEnd('\n')) }
          inv "event-line-non-canonical" Codes.nonCanonical "One event line has a space after a colon (valid JSON, not RFC 8785)."
              (let t = (utf8.GetString a.Events).TrimEnd('\n').Split('\n')
               { a with Events = utf8.GetBytes(String.Join("\n", Array.append [| t[0].Replace("\"aggregateVersion\":1", "\"aggregateVersion\": 1") |] t[1..]) + "\n") })
          inv "snapshot-non-canonical" Codes.nonCanonical "snapshot.json has a space after its first colon (valid JSON, not RFC 8785)."
              (let t = utf8.GetString a.Snapshot
               let i = t.IndexOf "\":"
               { a with Snapshot = utf8.GetBytes(t.Substring(0, i + 2) + " " + t.Substring(i + 2)) })
          inv "negative-zero" Codes.nonCanonical "aggregateVersion is written as -0 on the first event."
              (let t = (utf8.GetString a.Events)
               { a with Events = utf8.GetBytes(t.Substring(0, t.IndexOf "\"aggregateVersion\":1,") + "\"aggregateVersion\":-0," + t.Substring(t.IndexOf "\"aggregateVersion\":1," + 21)) })
          inv "float-number" Codes.jsonMalformed "aggregateVersion is written as 1.0 (integers only)."
              (let t = (utf8.GetString a.Events)
               { a with Events = utf8.GetBytes(t.Substring(0, t.IndexOf "\"aggregateVersion\":1,") + "\"aggregateVersion\":1.0," + t.Substring(t.IndexOf "\"aggregateVersion\":1," + 21)) })
          inv "integer-out-of-range" Codes.jsonMalformed "aggregateVersion is 9007199254740992 (outside the safe integer range)."
              (let t = (utf8.GetString a.Events)
               { a with Events = utf8.GetBytes(t.Substring(0, t.IndexOf "\"aggregateVersion\":1,") + "\"aggregateVersion\":9007199254740992," + t.Substring(t.IndexOf "\"aggregateVersion\":1," + 21)) })
          inv "lone-surrogate" Codes.jsonMalformed "An observation contains an unpaired UTF-16 surrogate (escaped \\ud800)."
              (let t = (utf8.GetString a.Events).TrimEnd('\n').Split('\n')
               { a with Events = utf8.GetBytes(String.Join("\n", Array.concat [ t[0..1]; [| t[2].Replace("\"original\":\"", "\"original\":\"\\ud800") |]; t[3..] ]) + "\n") })
          inv "nesting-too-deep" Codes.jsonMalformed "An event nests arrays 130 levels deep (readers MUST NOT accept depth > 128)."
              (let t = (utf8.GetString a.Events).TrimEnd('\n').Split('\n')
               let deep = String.replicate 130 "[" + String.replicate 130 "]"
               { a with Events = utf8.GetBytes(String.Join("\n", Array.append [| t[0].Replace("\"payload\":{", "\"payload\":{\"deep\":" + deep + ",") |] t[1..]) + "\n") })
          // Boundary: event (1) > payload (2) > arrays. 127 arrays = depth 129; 126 arrays = depth 128.
          inv "nesting-depth-129" Codes.jsonMalformed "An event nests to depth 129 (top-level object = depth 1); one level beyond the limit."
              (let t = (utf8.GetString a.Events).TrimEnd('\n').Split('\n')
               let deep = String.replicate 127 "[" + String.replicate 127 "]"
               { a with Events = utf8.GetBytes(String.Join("\n", Array.append [| t[0].Replace("\"payload\":{", "\"payload\":{\"deep\":" + deep + ",") |] t[1..]) + "\n") })
          inv "nesting-depth-128" Codes.schemaViolation "An event nests to exactly depth 128 (the limit): it parses and is canonical, then fails the schema (undeclared payload field 'deep')."
              (let t = (utf8.GetString a.Events).TrimEnd('\n').Split('\n')
               let deep = String.replicate 126 "[" + String.replicate 126 "]"
               { a with Events = utf8.GetBytes(String.Join("\n", Array.append [| t[0].Replace("\"payload\":{", "\"payload\":{\"deep\":" + deep + ",") |] t[1..]) + "\n") })
          // ---- schema-level value rules ----
          inv "calendar-invalid-occurred-at" Codes.schemaViolation "An occurredAt is 2026-02-30 (not a calendar date)."
              (mutateLine a 2 (set [ "occurredAt" ] (JStr "2026-02-30T10:08:00.000Z")))
          inv "calendar-invalid-manifest-created-at" Codes.schemaViolation "The manifest createdAt is 2026-02-30 (not a calendar date)."
              (let m = parse a.Manifest in { a with Manifest = file (set [ "createdAt" ] (JStr "2026-02-30T10:00:00.000Z") m) })
          inv "non-string-format-version" Codes.schemaViolation "An event's formatVersion is the integer 1 (non-string versions are schema violations, not unsupported versions)."
              (mutateLine a 1 (set [ "formatVersion" ] (JInt 1L)))
          inv "nil-uuid" Codes.schemaViolation "An observationId is the nil UUID."
              (mutateLine a 2 (set [ "payload"; "observationId" ] (JStr "00000000-0000-0000-0000-000000000000")))
          inv "uppercase-uuid" Codes.schemaViolation "A hypothesisId is written in upper case."
              (mutateLine a hA (fun v -> match get [ "payload"; "hypothesisId" ] v with JStr s -> set [ "payload"; "hypothesisId" ] (JStr(s.ToUpperInvariant())) v | _ -> v))
          inv "blank-text" Codes.schemaViolation "A hypothesis statement consists only of spaces."
              (mutateLine a hA (set [ "payload"; "statement" ] (JStr "   ")))
          inv "ideographic-space-text" Codes.schemaViolation "A hypothesis statement consists only of U+3000 (engine whitespace)."
              (mutateLine a hA (set [ "payload"; "statement" ] (JStr "\u3000\u3000")))
          inv "artifact-not-http" Codes.schemaViolation "An artifact evidence URL uses ftp:// (only absolute http(s) URLs are allowed)."
              (let i = lines b |> List.findIndex (fun v -> get [ "payload"; "contentKind" ] v = JStr "artifact")
               mutateLine b i (set [ "payload"; "content" ] (JStr "ftp://example.invalid/synthetic/resolver-query-log.txt")))
          // ---- manifest consistency (every stage-8 rule) ----
          inv "manifest-wrong-investigation-id" Codes.manifestMismatch "The manifest names a different investigation than its directory and events."
              (let m = parse a.Manifest in { a with Manifest = file (set [ "investigationId" ] (get [ "investigationId" ] (List.head (lines b))) m) })
          inv "manifest-event-count-mismatch" Codes.manifestMismatch "The manifest eventCount is one less than the number of lines."
              (let m = parse a.Manifest in { a with Manifest = file (set [ "eventCount" ] (JInt(int64 (lines a).Length - 1L)) m) })
          inv "manifest-created-at-mismatch" Codes.manifestMismatch "The manifest createdAt is not the first event's occurredAt."
              (let m = parse a.Manifest in { a with Manifest = file (set [ "createdAt" ] (JStr "2026-09-08T09:59:00.000Z") m) })
          inv "manifest-updated-at-mismatch" Codes.manifestMismatch "The manifest updatedAt is not the last event's occurredAt."
              (let m = parse a.Manifest in { a with Manifest = file (set [ "updatedAt" ] (JStr "2026-09-08T12:00:00.000Z") m) })
          // ---- engine decision rules, each the SOLE reason for refusal (review M2) ----
          neg "engine-reopen-without-new-evidence" Codes.historyInadmissible (Some "HISTORY.INADMISSIBLE_EVENT")
              "A falsified hypothesis is reopened citing only evidence admitted before the falsification (R2.5: HYPOTHESIS.REOPEN_NEEDS_NEW_EVIDENCE)."
              (let h = lines b |> List.pick (fun v -> if get [ "payload"; "to" ] v = JStr "falsified" then Some(get [ "payload"; "hypothesisId" ] v, get [ "payload"; "evidenceIds" ] v) else None)
               investigationBody (soleRefusal (loadedOf "intermittent-outage")
                   [ forgedLine b "reopen-stale" "HypothesisTransitioned" human
                         (JObj [ "hypothesisId", fst h; "from", JStr "falsified"; "to", JStr "plausible"; "command", JStr "reopen"; "reason", JStr "Reconsidering DNS."; "evidenceIds", snd h ]) 4.0 ]
                   "HYPOTHESIS.REOPEN_NEEDS_NEW_EVIDENCE"))
          neg "engine-evidence-supersedes-itself" Codes.historyInadmissible (Some "HISTORY.INADMISSIBLE_EVENT")
              "Evidence is superseded by itself (EVIDENCE.SELF)."
              (investigationBody (soleRefusal (loadedOf "payment-failure")
                   [ forgedLine a "self-supersede" "EvidenceSuperseded" human (JObj [ "evidenceId", evidenceOfA; "replacementEvidenceId", evidenceOfA; "reason", JStr "Self." ]) 4.0 ]
                   "EVIDENCE.SELF"))
          neg "engine-cause-without-mechanism" Codes.historyInadmissible (Some "HISTORY.UNGATED_CONFIRMATION")
              "A second, otherwise fully supported cause without a stated mechanism is confirmed (§15.8: CAUSE.NO_MECHANISM)."
              (let members = lines d |> List.pick (fun v -> if get [ "eventType" ] v = JStr "CausalAssertionRecorded" then Some(get [ "payload"; "members" ] v) else None)
               let cause2 = JStr(Uuid.text (ScenarioKit.guid "negative" "cause" "no-mechanism"))
               let recordLine = forgedLine d "no-mechanism-record" "CausalAssertionRecorded" human (JObj [ "causeId", cause2; "outcome", JStr "Same regression, restated."; "members", members; "mechanism", JNull ]) 4.0
               let confirmLine =
                   let invId = match get [ "investigationId" ] recordLine with JStr s -> s | _ -> ""
                   let corr = Uuid.text (ScenarioKit.guid "negative" "command" "no-mechanism-confirm")
                   JObj [ "formatVersion", JStr "1.0.0"; "eventId", JStr(eventIdOf invId ((match get [ "aggregateVersion" ] recordLine with JInt n -> n | _ -> 0L) + 1L) corr)
                          "investigationId", JStr invId; "aggregateVersion", JInt((match get [ "aggregateVersion" ] recordLine with JInt n -> n | _ -> 0L) + 1L)
                          "eventType", JStr "CausalAssertionConfirmed"
                          "occurredAt", JStr((Timestamp.tryParse (match get [ "occurredAt" ] recordLine with JStr s -> s | _ -> "") |> Option.get).AddMinutes(4.0) |> Timestamp.tryFormat |> Option.get)
                          "actor", human; "semanticVersion", JStr "1.0.0"; "correlationId", JStr corr; "payload", JObj [ "causeId", cause2; "policy", JStr "core-default/1" ] ]
               investigationBody (soleRefusal (loadedOf "api-latency") [ recordLine; confirmLine ] "CAUSE.NO_MECHANISM"))
          neg "engine-verification-evidence-predates-round" Codes.historyInadmissible (Some "HISTORY.INADMISSIBLE_EVENT")
              "A satisfied verification cites evidence admitted before verification started (R7.4: VERIFICATION.EVIDENCE_PREDATES_ROUND)."
              (let crit = lines d |> List.pick (fun v -> if get [ "eventType" ] v = JStr "CriterionRecorded" then Some(get [ "payload"; "criterionId" ] v) else None)
               let ev = lines d |> List.pick (fun v -> if get [ "eventType" ] v = JStr "EvidenceAdmitted" then Some(get [ "payload"; "evidenceId" ] v) else None)
               investigationBody (soleRefusal (loadedOf "api-latency")
                   [ forgedLine d "stale-verification" "VerificationRecorded" human
                         (JObj [ "verificationId", JStr(Uuid.text (ScenarioKit.guid "negative" "verification" "stale")); "criterionId", crit; "outcome", JStr "satisfied"; "evidenceIds", JArr [ ev ] ]) 4.0 ]
                   "VERIFICATION.EVIDENCE_PREDATES_ROUND"))
          neg "engine-prediction-timing-forged" Codes.historyInadmissible (Some "HISTORY.INADMISSIBLE_EVENT")
              "A prediction recorded after a relevant finding claims to be prospective; the engine records it as retrospective (A11)."
              (let h2 = lines b |> List.pick (fun v -> if get [ "eventType" ] v = JStr "PredictionRecorded" then Some(get [ "payload"; "hypothesisId" ] v) else None)
               investigationBody (soleRefusal (loadedOf "intermittent-outage")
                   [ forgedLine b "prospective-claim" "PredictionRecorded" human
                         (JObj [ "predictionId", JStr(Uuid.text (ScenarioKit.guid "negative" "prediction" "forged")); "hypothesisId", h2; "statement", JStr "Rotation predicts the next burst."; "strength", JStr "expected"; "timing", JStr "prospective" ]) 4.0 ]
                   "MISMATCH"))
          neg "engine-agent-repeats-falsified" Codes.historyInadmissible (Some "HISTORY.INADMISSIBLE_EVENT")
              "An AI agent proposes, as a new hypothesis, an explanation that was already falsified (R5.4: PROPOSAL.REPEATS_NEGATIVE_KNOWLEDGE)."
              (let st = lines b |> List.pick (fun v -> if get [ "eventType" ] v = JStr "HypothesisRecorded" then Some(get [ "payload"; "statement" ] v) else None)
               let agent = JObj [ "actorId", JStr "assistant-01"; "kind", JStr "agent"; "provider", JStr "synthetic-provider"; "model", JStr "synthetic-model-1" ]
               investigationBody (soleRefusal (loadedOf "intermittent-outage")
                   [ forgedLine b "repeat-negative" "ProposalRecorded" agent
                         (JObj [ "proposalId", JStr(Uuid.text (ScenarioKit.guid "negative" "proposal" "repeat")); "kind", JStr "hypothesis"; "text", st; "rationale", JNull ]) 4.0 ]
                   "PROPOSAL.REPEATS_NEGATIVE_KNOWLEDGE"))
          neg "engine-content-after-resolution" Codes.historyInadmissible (Some "HISTORY.INADMISSIBLE_EVENT")
              "An observation is recorded in a resolved investigation (D-005: INVESTIGATION.NOT_OPEN)."
              (investigationBody (soleRefusal (loadedOf "authentication-outage")
                   [ forgedLine e "after-resolution" "ObservationRecorded" human
                         (JObj [ "observationId", JStr(Uuid.text (ScenarioKit.guid "negative" "observation" "late")); "original", JStr "Late note."; "observedAt", JNull ]) 4.0 ]
                   "INVESTIGATION.NOT_OPEN"))
          neg "engine-duplicate-entity-id" Codes.historyInadmissible (Some "HISTORY.INADMISSIBLE_EVENT")
              "A hypothesis is recorded with the id of an existing hypothesis (DUPLICATE_ID)."
              (investigationBody (soleRefusal (loadedOf "payment-failure")
                   [ forgedLine a "duplicate-id" "HypothesisRecorded" human (JObj [ "hypothesisId", unhypothesis; "statement", JStr "A different idea under an old id."; "mechanism", JNull ]) 4.0 ]
                   "DUPLICATE_ID"))
          neg "engine-dangling-reference" Codes.historyInadmissible (Some "HISTORY.INADMISSIBLE_EVENT")
              "Evidence is related to a hypothesis that does not exist (NOT_FOUND)."
              (investigationBody (soleRefusal (loadedOf "payment-failure")
                   [ forgedLine a "dangling" "EvidenceRelated" human
                         (JObj [ "relationshipId", JStr(Uuid.text (ScenarioKit.guid "negative" "relationship" "dangling")); "evidenceId", evidenceOfA
                                 "hypothesisId", JStr(Uuid.text (ScenarioKit.guid "negative" "hypothesis" "missing")); "kind", JStr "supports"; "rationale", JNull ]) 4.0 ]
                   "NOT_FOUND"))
          neg "engine-confirmed-conclusion-unconfirmed-cause" Codes.historyInadmissible (Some "HISTORY.INADMISSIBLE_EVENT")
              "A conclusion claims a confirmed cause that was never confirmed (R6.1: CONCLUSION.UNSUPPORTED)."
              (investigationBody (soleRefusal (loadedOf "conflicting-metrics")
                   [ forgedLine c "unsupported-conclusion" "ConclusionRecorded" human
                         (JObj [ "conclusionId", JStr(Uuid.text (ScenarioKit.guid "negative" "conclusion" "unsupported")); "statement", JStr "Sampling caused it."
                                 "strength", JStr "confirmedCause"; "causeIds", JArr [ causeId c ]; "residualUncertainty", JStr "None stated." ]) 4.0 ]
                   "CONCLUSION.UNSUPPORTED"))
          neg "engine-conclusion-without-residual" Codes.historyInadmissible (Some "HISTORY.INADMISSIBLE_EVENT")
              "A conclusion omits residual uncertainty while a blocking unknown is open (R6.4: CONCLUSION.RESIDUAL_REQUIRED)."
              (investigationBody (soleRefusal (loadedOf "conflicting-metrics")
                   [ forgedLine c "no-residual" "ConclusionRecorded" human
                         (JObj [ "conclusionId", JStr(Uuid.text (ScenarioKit.guid "negative" "conclusion" "no-residual")); "statement", JStr "Inconclusive."
                                 "strength", JStr "plausibleExplanation"; "causeIds", JArr []; "residualUncertainty", JNull ]) 4.0 ]
                   "CONCLUSION.RESIDUAL_REQUIRED"))
          neg "engine-awaiting-verification-without-work" Codes.historyInadmissible (Some "HISTORY.INADMISSIBLE_EVENT")
              "The investigation is marked awaiting verification with no implemented corrective action (LIFECYCLE.NO_VERIFICATION_WORK)."
              (investigationBody (soleRefusal (loadedOf "payment-failure")
                   [ forgedLine a "no-verification-work" "LifecycleChanged" human
                         (JObj [ "from", JStr "active"; "to", JStr "awaitingVerification"; "command", JStr "markAwaitingVerification"; "reason", JNull ]) 4.0 ]
                   "LIFECYCLE.NO_VERIFICATION_WORK"))
          neg "engine-finding-for-rejected-action" Codes.historyInadmissible (Some "HISTORY.INADMISSIBLE_EVENT")
              "A finding is recorded for a rejected action (ACTION.NOT_EXECUTABLE)."
              (let rejected = lines b |> List.pick (fun v -> if get [ "payload"; "state" ] v = JStr "rejected" then Some(get [ "payload"; "actionId" ] v) else None)
               investigationBody (soleRefusal (loadedOf "intermittent-outage")
                   [ forgedLine b "finding-rejected" "FindingRecorded" human
                         (JObj [ "findingId", JStr(Uuid.text (ScenarioKit.guid "negative" "finding" "rejected")); "actionId", rejected; "originalResult", JStr "Ran it anyway."; "interpretation", JNull ]) 4.0 ]
                   "ACTION.NOT_EXECUTABLE"))
          neg "engine-malformed-artifact-url" Codes.historyInadmissible (Some "HISTORY.INADMISSIBLE_EVENT")
              "Artifact evidence has an http(s) prefix but is not a well-formed absolute URL; only the engine rule R3.5 rejects it (EVIDENCE.URL)."
              (investigationBody (soleRefusal (loadedOf "payment-failure")
                   [ forgedLine a "bad-url" "EvidenceAdmitted" human
                         (JObj [ "evidenceId", JStr(Uuid.text (ScenarioKit.guid "negative" "evidence" "bad-url")); "contentKind", JStr "artifact"; "content", JStr "https://exa mple.invalid/x"
                                 "category", JStr "log"; "source", JStr "synthetic"; "capturedAt", JNull ]) 4.0 ]
                   "EVIDENCE.URL"))
          neg "engine-group-time-disagreement" Codes.historyInadmissible (Some "HISTORY.INADMISSIBLE_EVENT")
              "The events of one multi-event command (a finding with its action transitions) disagree on occurredAt."
              (let vs = lines b
               let i = vs |> List.findIndex (fun v -> get [ "eventType" ] v = JStr "FindingRecorded")
               let shifted = vs |> List.mapi (fun n v -> if n = i then (match get [ "occurredAt" ] v with JStr s -> set [ "occurredAt" ] (JStr((Timestamp.tryParse s |> Option.get).AddMinutes(1.0) |> Timestamp.tryFormat |> Option.get)) v | _ -> v) else v)
               let evs = shifted |> List.map (EventWire.decode >> Layout.orFail >> fun x -> x.Event)
               investigationBody (Store.buildUnchecked evs |> Layout.orFail))
          // ---- path safety ----
          inv "extra-file-in-directory" Codes.pathInvalid "The investigation directory contains a fourth file (notes.txt)." a
          inv "missing-snapshot-file" Codes.pathInvalid "The investigation directory has no snapshot.json." a
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

    /// File-set negatives: add or remove an entry of the `files` object.
    let private reshapeFiles (n: Negative) =
        let edit f = n.Body |> List.map (fun (k, v) -> if k = "files" then k, f v else k, v)
        match n.Name with
        | "extra-file-in-directory" -> { n with Body = edit (function JObj o -> JObj(o @ [ "notes.txt", b64 (utf8.GetBytes "synthetic note\n") ]) | v -> v) }
        | "missing-snapshot-file" -> { n with Body = edit (function JObj o -> JObj(o |> List.filter (fst >> (<>) "snapshot.json")) | v -> v) }
        | _ -> n

    let toJson (n0: Negative) : JValue =
        let n = reshapeFiles n0
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

    /// A negative fixture's `files` object: any file names, so missing and extra files are representable.
    let filesFrom (directoryId: string) (v: JValue) : Result<InvestigationFiles, CausaError> =
        match v with
        | JObj o -> o |> List.map (fun (k, x) -> k, fromBase64 x) |> Map.ofList |> Store.ofFileMap directoryId
        | _ -> CausaError.fail Codes.schemaViolation "files must be an object"

    /// Evaluate a negative fixture; returns the reader's verdict (Ok = wrongly accepted).
    let evaluate (set: SchemaCheck.SchemaSet) (byKey: Map<string, InvestigationFiles * Loaded>) (fixture: JValue) : Result<unit, CausaError> =
        let str k = match Negatives.get [ k ] fixture with JStr s -> s | _ -> ""
        match str "kind" with
        | "investigation" -> filesFrom (str "directoryId") (Negatives.get [ "files" ] fixture) |> Result.bind (Store.validate set) |> Result.map ignore
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
                filesFrom files.DirectoryId (Negatives.get [ "files" ] fixture) |> Result.bind (Store.successor set loaded files.Events) |> Result.map ignore
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
                        match Store.readDirectory (Path.Combine(Layout.investigations root, id)) |> Result.bind (fun files -> Store.validate set files |> Result.map (fun l -> files, l)) with
                        | Error e -> key, Error(e.Code + " " + e.Detail), None
                        | Ok(files, loaded) ->
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
