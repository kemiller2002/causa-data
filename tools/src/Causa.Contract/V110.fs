namespace Causa.Contract

open System
open IndyInitRootCauseInvestigator.Engine

/// DRAFT for owner decision: the causa-storage 1.1.0 reader and writer (contract/v1.1.0/CONTRACT.md).
///
/// 1.1.0 is a compatible additive extension of 1.0.0 (CONTRACT section 13.1): two events (EvidenceRevised,
/// ObservationRevised) and two relationship kinds (consistentWith, inconclusive). A 1.1.0 reader:
/// - reads 1.0.0 file sets with the frozen 1.0.0 reader itself (Store.validate), so they read exactly as before;
/// - reads 1.1.0 file sets through the same 17 stages, with the 1.1.0 schemas, the 33-type registry, the 1.1.0
///   admissibility rules (section 4.6, normative under S-2 B) and the 1.1.0 projection (section 4.7);
/// - refuses every other version, and every investigation whose files mix versions, at stage 5.
[<RequireQualifiedAccess>]
module V110 =
    let version = "1.1.0"
    let revisionTypes = [ "EvidenceRevised"; "ObservationRevised" ]
    let eventTypes = EventWire.eventTypes @ revisionTypes
    let addedKinds = [ "consistentWith"; "inconclusive" ]

    let schemaIds =
        let b = "https://github.com/kemiller2002/causa-data/contract/v1.1.0/schemas/"
        {| Envelope = b + "event-envelope.schema.json"
           Manifest = b + "investigation-manifest.schema.json"
           Snapshot = b + "snapshot.schema.json"
           State = b + "state.schema.json" |}

    type Format =
        | V100
        | V110

    /// A fully verified investigation of either version.
    type Verified =
        { Format: Format
          InvestigationId: Guid
          Lines: JValue list
          StateJson: JValue
          StateSha256: string
          EventsSha256: string
          ManifestSha256: string
          SnapshotSha256: string }

    // ---------------- JSON helpers ----------------

    let field (k: string) (v: JValue) = match v with JObj o -> Json.field k o | _ -> None
    let strField k v = match field k v with Some(JStr s) -> Some s | _ -> None
    let intField k v = match field k v with Some(JInt n) -> Some n | _ -> None

    /// Replaces (or appends) one property; order does not matter in canonical form.
    let setField (k: string) (x: JValue) (v: JValue) =
        match v with
        | JObj o when o |> List.exists (fst >> (=) k) -> JObj(o |> List.map (fun (n, y) -> if n = k then n, x else n, y))
        | JObj o -> JObj(o @ [ k, x ])
        | other -> other

    let private fail code detail : Result<'T, CausaError> = CausaError.fail code detail

    let private inadmissible (domain: string) (detail: string) : Result<'T, CausaError> =
        Error { Code = Codes.historyInadmissible; DomainCode = Some domain; Detail = detail }

    let private withVersion (v: string) (x: JValue) =
        x |> setField "formatVersion" (JStr v) |> setField "semanticVersion" (JStr v)

    let private versionStrings (v: JValue) =
        [ "formatVersion"; "schemaVersion"; "semanticVersion" ] |> List.choose (fun k -> strField k v)

    let private lineName i = "events.ndjson line " + string (i + 1)

    let private schemaCheck (set: SchemaCheck.SchemaSet) (schemaId: string) (name: string) (v: JValue) =
        match SchemaCheck.check set schemaId v with
        | [] -> Ok()
        | e :: rest -> fail Codes.schemaViolation (name + ": " + e + (if rest.IsEmpty then "" else " (+" + string rest.Length + " more)"))

    let private parseFiles (files: InvestigationFiles) =
        let doc name (b: byte[]) =
            Json.decodeUtf8 b |> Result.bind (fun t -> Json.parse (t.Substring(0, t.Length - 1))) |> Result.mapError (fun e -> CausaError.create Codes.jsonMalformed (name + ": " + e))
        result {
            let! manifest = doc "manifest.json" files.Manifest
            let! snapshot = doc "snapshot.json" files.Snapshot
            let! text = Json.decodeUtf8 files.Events |> Result.mapError (CausaError.create Codes.encoding)
            let texts = text.Substring(0, text.Length - 1).Split('\n') |> List.ofArray
            let! lines = texts |> traverse (fun t -> Json.parse t |> Result.mapError (CausaError.create Codes.jsonMalformed))
            return manifest, snapshot, texts, lines
        }

    // ---------------- the core view (section 4.6) ----------------

    let isRevision (line: JValue) = strField "eventType" line |> Option.exists (fun t -> List.contains t revisionTypes)

    let private payloadOf (line: JValue) = field "payload" line |> Option.defaultValue (JObj [])

    /// A 1.0.0 line for the pinned engine: version strings "1.0.0", the given aggregateVersion, and a 1.1.0
    /// relationship kind replaced by `insufficient`, which no engine gate reads (the cause gate counts only supports;
    /// the contradiction gate only contradicts). This is the normative core view of CONTRACT 1.1.0 section 4.6.
    let private coreLine (aggregateVersion: int64) (line: JValue) =
        let line = line |> withVersion "1.0.0" |> setField "aggregateVersion" (JInt aggregateVersion)
        match strField "eventType" line, strField "kind" (payloadOf line) with
        | Some "EvidenceRelated", Some k when List.contains k addedKinds -> line |> setField "payload" (payloadOf line |> setField "kind" (JStr "insufficient"))
        | _ -> line

    /// Engine events of the core view: every line but the revisions; `renumber` numbers them 1..k (for History.validate),
    /// otherwise they keep their aggregateVersion (for the projection, whose versions are the stored ones).
    let private coreEvents (renumber: bool) (lines: JValue list) : Result<Event list, CausaError> =
        lines
        |> List.filter (isRevision >> not)
        |> List.indexed
        |> traverse (fun (i, l) ->
            let v = if renumber then int64 (i + 1) else intField "aggregateVersion" l |> Option.defaultValue 0L
            EventWire.decode (coreLine v l) |> Result.map (fun d -> d.Event))

    // ---------------- admissibility of the 1.1.0 additions (section 4.6) ----------------

    let private openLifecycles = [ "draft"; "active"; "awaitingEvidence"; "awaitingVerification" ]

    /// A source URL is admissible as the engine admits an artifact URL (R3.5): absolute http(s) and well formed.
    let wellFormedUrl (u: string) =
        (u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        && Uri.IsWellFormedUriString(u, UriKind.Absolute)

    type private Facts =
        { Lifecycle: string
          Evidence: Map<string, string * bool> // id -> (source, superseded)
          Observations: Map<string, string> }   // id -> original

    let private groupsOf (lines: (int * JValue) list) =
        lines
        |> List.fold (fun (acc: (int * JValue) list list) (i, l) ->
            match acc with
            | (g :: rest) when strField "correlationId" (snd (List.head g)) = strField "correlationId" l -> (g @ [ i, l ]) :: rest
            | _ -> [ i, l ] :: acc) []
        |> List.rev

    /// The first violation of the 1.1.0 rules, in history order; None when the additions are admissible.
    let private extensionProblem (lines: JValue list) : (string * string) option =
        let note (facts: Facts) (l: JValue) =
            let p = payloadOf l
            let s k = strField k p |> Option.defaultValue ""
            match strField "eventType" l with
            | Some "InvestigationStarted" -> { facts with Lifecycle = "draft"; Observations = facts.Observations.Add(s "observationId", s "original") }
            | Some "LifecycleChanged" -> { facts with Lifecycle = s "to" }
            | Some "ObservationRecorded" -> { facts with Observations = facts.Observations.Add(s "observationId", s "original") }
            | Some "EvidenceAdmitted" -> { facts with Evidence = facts.Evidence.Add(s "evidenceId", (s "source", false)) }
            | Some "EvidenceSuperseded" ->
                match facts.Evidence.TryFind(s "evidenceId") with
                | Some(src, _) -> { facts with Evidence = facts.Evidence.Add(s "evidenceId", (src, true)) }
                | None -> facts
            | _ -> facts
        let revision (facts: Facts) (group: (int * JValue) list) (i: int, l: JValue) : (string * string) option =
            let p = payloadOf l
            let s k = strField k p |> Option.defaultValue ""
            let at = "event " + string (i + 1)
            let creation = group |> List.map snd |> List.tryHead |> Option.filter (fun h -> not (isRevision h))
            if not (List.contains facts.Lifecycle openLifecycles) then Some("INVESTIGATION.NOT_OPEN", at + ": the investigation is " + facts.Lifecycle)
            else
                match strField "eventType" l with
                | Some "EvidenceRevised" ->
                    match facts.Evidence.TryFind(s "evidenceId") with
                    | None -> Some("HISTORY.INADMISSIBLE_EVENT", at + ": EvidenceRevised names evidence that does not exist")
                    | Some(_, true) -> Some("EVIDENCE.SUPERSEDED", at + ": EvidenceRevised names superseded evidence")
                    | Some(source, _) ->
                        match field "sourceUrl" p with
                        | Some(JStr u) when not (wellFormedUrl u) -> Some("EVIDENCE.URL", at + ": sourceUrl is not a well-formed absolute http(s) URL")
                        | _ when creation.IsSome && source <> s "source" ->
                            Some("EVIDENCE.REVISION_MISMATCH", at + ": in its creation group, EvidenceRevised.source differs from EvidenceAdmitted.source")
                        | _ -> None
                | Some "ObservationRevised" ->
                    match facts.Observations.TryFind(s "observationId") with
                    | None -> Some("HISTORY.INADMISSIBLE_EVENT", at + ": ObservationRevised names an observation that does not exist")
                    | Some original when creation.IsSome && original <> s "original" ->
                        Some("OBSERVATION.REVISION_MISMATCH", at + ": in its creation group, ObservationRevised.original differs from ObservationRecorded.original")
                    | Some _ -> None
                | _ -> None
        /// A group holding a revision is one revision alone, or its creation event followed by the revision of the same
        /// id; every event of the group shares occurredAt and actor (CONTRACT section 5.4).
        let shape (group: (int * JValue) list) =
            let ls = group |> List.map snd
            let first = List.head ls
            let at = "event " + string (fst (List.head group) + 1)
            let types = ls |> List.map (strField "eventType" >> Option.defaultValue "")
            let id k (l: JValue) = strField k (payloadOf l)
            if not (ls |> List.exists isRevision) then None
            elif ls |> List.exists (fun l -> field "occurredAt" l <> field "occurredAt" first || field "actor" l <> field "actor" first) then
                Some("HISTORY.INADMISSIBLE_EVENT", at + ": events of one command disagree on actor or time")
            else
                match types, ls with
                | [ "EvidenceRevised" ], _ | [ "ObservationRevised" ], _ -> None
                | [ "EvidenceAdmitted"; "EvidenceRevised" ], [ a; b ] when id "evidenceId" a = id "evidenceId" b -> None
                | [ "ObservationRecorded"; "ObservationRevised" ], [ a; b ] when id "observationId" a = id "observationId" b -> None
                | _ -> Some("HISTORY.INADMISSIBLE_EVENT", at + ": events from here are not what any single 1.1.0 command emits (" + String.Join(", ", types) + ")")
        let start = { Lifecycle = ""; Evidence = Map.empty; Observations = Map.empty }
        lines
        |> List.indexed
        |> groupsOf
        |> List.fold (fun (facts, found) group ->
            match found with
            | Some _ -> facts, found
            | None ->
                match shape group with
                | Some problem -> facts, Some problem
                | None ->
                    group |> List.fold (fun (f, x) (i, l) ->
                        match x with
                        | Some _ -> f, x
                        | None when isRevision l -> f, revision f group (i, l)
                        | None -> note f l, None) (facts, None)) (start, None)
        |> snd

    // ---------------- projection (section 4.7) ----------------

    let private mapArray (k: string) (f: JValue -> JValue) (state: JValue) =
        match field k state with
        | Some(JArr xs) -> state |> setField k (JArr(xs |> List.map f))
        | _ -> state

    let private updateWhere (k: string) (id: string) (f: JValue -> JValue) (state: JValue) =
        state |> mapArray k (fun e -> if strField "id" e = Some id then f e else e)

    /// The 1.1.0 snapshot of a history: the engine's projection of the core view (stored versions), then the
    /// additions: label/sourceUrl on evidence, source on observations, each revision applied in order, the stored
    /// relationship kind, and the investigation's own version and timestamps.
    let snapshotOf (lines: JValue list) : Result<JValue, CausaError> =
        result {
            let! events = coreEvents false lines
            let! state =
                try
                    match Evolve.replay events with
                    | Some s -> Ok s
                    | None -> fail Codes.historyInadmissible "empty history"
                with ex -> fail Codes.historyInadmissible ("replay failed: " + ex.Message)
            let last = List.last lines
            let! lastAt = Fields.parseTs "occurredAt" (strField "occurredAt" last |> Option.defaultValue "")
            let! snap = Projection.snapshot state lastAt
            let n = intField "aggregateVersion" last |> Option.defaultValue 0L
            let baseState =
                field "state" snap |> Option.defaultValue JNull
                |> setField "version" (JInt n)
                |> mapArray "evidence" (fun e -> e |> setField "label" JNull |> setField "sourceUrl" JNull)
                |> mapArray "observations" (fun o -> o |> setField "source" JNull)
            let applied =
                lines
                |> List.fold (fun st l ->
                    let p = payloadOf l
                    let g k = field k p |> Option.defaultValue JNull
                    let s k = strField k p |> Option.defaultValue ""
                    match strField "eventType" l with
                    | Some "EvidenceRevised" ->
                        st |> updateWhere "evidence" (s "evidenceId") (fun e -> e |> setField "label" (g "label") |> setField "source" (g "source") |> setField "sourceUrl" (g "sourceUrl"))
                    | Some "ObservationRevised" ->
                        st |> updateWhere "observations" (s "observationId") (fun o -> o |> setField "original" (g "original") |> setField "source" (g "source"))
                    | Some "EvidenceRelated" when List.contains (s "kind") addedKinds ->
                        st |> updateWhere "relationships" (s "relationshipId") (setField "kind" (g "kind"))
                    | _ -> st) baseState
            return snap |> withVersion version |> setField "aggregateVersion" (JInt n) |> setField "state" applied
        }

    // ---------------- reading ----------------

    /// Stages 6-17 of a file set whose every version string is "1.1.0" (stages 1-5 passed).
    let private validate110 (set: SchemaCheck.SchemaSet) (dirId: Guid) (files: InvestigationFiles) : Result<Verified, CausaError> =
        result {
            let! manifest, snapshot, _, lines = parseFiles files
            // Stage 6
            do! lines |> List.indexed |> traverse (fun (i, l) ->
                    match strField "eventType" l with
                    | Some t when not (List.contains t eventTypes) -> fail Codes.unknownEventType (lineName i + ": event type '" + t + "' is not registered in 1.1.0")
                    | _ -> Ok()) |> Result.map ignore
            // Stage 7
            do! schemaCheck set schemaIds.Manifest "manifest.json" manifest
            do! lines |> List.indexed |> traverse (fun (i, l) -> schemaCheck set schemaIds.Envelope (lineName i) l) |> Result.map ignore
            do! schemaCheck set schemaIds.Snapshot "snapshot.json" snapshot
            // Stages 8-9
            let n = int64 lines.Length
            let m k = strField k manifest |> Option.defaultValue ""
            let mi k = intField k manifest |> Option.defaultValue -1L
            let at (l: JValue) = strField "occurredAt" l |> Option.defaultValue ""
            do! if m "investigationId" = Uuid.text dirId then Ok() else fail Codes.manifestMismatch "manifest investigationId differs from its directory"
            do! if mi "aggregateVersion" = n && mi "eventCount" = n then Ok() else fail Codes.manifestMismatch "manifest aggregateVersion/eventCount differ from the number of events"
            do! if m "createdAt" = at (List.head lines) && m "updatedAt" = at (List.last lines) then Ok() else fail Codes.manifestMismatch "manifest createdAt/updatedAt are not the first/last occurredAt"
            let eventsSha = Json.sha256Hex files.Events
            do! if m "eventsSha256" = eventsSha then Ok() else fail Codes.eventsHashMismatch "manifest eventsSha256 does not match events.ndjson bytes"
            // Stages 10-14
            let g k (l: JValue) = strField k l |> Option.bind Uuid.tryParse |> Option.defaultValue Guid.Empty
            do! match lines |> List.tryFindIndex (fun l -> g "investigationId" l <> dirId) with
                | Some i -> fail Codes.investigationMismatch (lineName i + " names another investigation")
                | None -> Ok()
            do! match lines |> List.countBy (g "eventId") |> List.tryFind (fun (_, c) -> c > 1) with
                | Some(id, _) -> fail Codes.eventIdDuplicate ("eventId " + Uuid.text id + " appears more than once")
                | None -> Ok()
            do! match lines |> List.indexed |> List.tryFind (fun (i, l) -> intField "aggregateVersion" l <> Some(int64 (i + 1))) with
                | Some(i, _) -> fail Codes.sequenceInvalid (lineName i + " does not carry aggregateVersion " + string (i + 1))
                | None -> Ok()
            do! match lines |> List.tryFind (fun l -> g "eventId" l <> Uuid.eventId dirId (intField "aggregateVersion" l |> Option.defaultValue 0L) (g "correlationId" l)) with
                | Some l -> fail Codes.eventIdMismatch ("event " + string (intField "aggregateVersion" l |> Option.defaultValue 0L) + " eventId is not the derived UUIDv5")
                | None -> Ok()
            do! let groups = lines |> List.map (g "correlationId") |> List.fold (fun acc c -> match acc with h :: _ when h = c -> acc | _ -> c :: acc) []
                match groups |> List.countBy id |> List.tryFind (fun (_, c) -> c > 1) with
                | Some(c, _) -> fail Codes.correlationReused ("correlationId " + Uuid.text c + " names more than one command group")
                | None -> Ok()
            // Stage 15: the 1.1.0 rules, then the core view under the pinned engine
            do! match extensionProblem lines with
                | Some(code, detail) -> inadmissible code detail
                | None -> Ok()
            let! core = coreEvents true lines
            do! match History.validate core with
                | [] -> Ok()
                | first :: _ as all -> inadmissible first.Code (String.Join(" | ", all |> List.map (fun e -> e.Code + ": " + e.Message)))
            // Stages 16-17
            let! snapshotSha = Json.digest snapshot |> Result.mapError (CausaError.create Codes.nonCanonical)
            do! if m "snapshotSha256" = snapshotSha then Ok() else fail Codes.snapshotHashMismatch "manifest snapshotSha256 does not match snapshot.json"
            let! expected = snapshotOf lines
            let! expectedText = Json.canonical expected |> Result.mapError (CausaError.create Codes.unrepresentable)
            let! snapshotText = Json.canonical snapshot |> Result.mapError (CausaError.create Codes.nonCanonical)
            do! if expectedText = snapshotText then Ok() else fail Codes.snapshotMismatch "snapshot.json is not the 1.1.0 projection of the replayed event history"
            let stateJson = field "state" expected |> Option.defaultValue JNull
            let! stateSha = Json.digest stateJson |> Result.mapError (CausaError.create Codes.unrepresentable)
            let! manifestSha = Json.digest manifest |> Result.mapError (CausaError.create Codes.nonCanonical)
            return
                { Format = V110; InvestigationId = dirId; Lines = lines; StateJson = stateJson; StateSha256 = stateSha
                  EventsSha256 = eventsSha; ManifestSha256 = manifestSha; SnapshotSha256 = snapshotSha }
        }

    /// The 1.1.0 reader. `v100` and `v110` are the two schema sets (contract/v1.0.0/schemas, contract/v1.1.0/schemas).
    let validate (v100: SchemaCheck.SchemaSet) (v110: SchemaCheck.SchemaSet) (files: InvestigationFiles) : Result<Verified, CausaError> =
        match Store.validate v100 files with
        | Ok l ->
            let lines = l.Events |> List.map (fun e -> EventWire.encode e |> Result.defaultValue JNull)
            Ok { Format = V100; InvestigationId = l.InvestigationId; Lines = lines; StateJson = l.StateJson; StateSha256 = l.StateSha256
                 EventsSha256 = l.EventsSha256; ManifestSha256 = l.ManifestSha256; SnapshotSha256 = l.SnapshotSha256 }
        | Error e when e.Code <> Codes.unsupportedVersion -> Error e
        | Error e ->
            // Stages 1-4 passed (the 1.0.0 reader reached stage 5). Homogeneity: every version string is "1.1.0".
            match parseFiles files with
            | Error pe -> Error pe
            | Ok(manifest, snapshot, _, lines) ->
                let all = versionStrings manifest @ versionStrings snapshot @ (lines |> List.collect versionStrings)
                if all |> List.forall ((=) version) then
                    validate110 v110 (Uuid.tryParse files.DirectoryId |> Option.defaultValue Guid.Empty) files
                elif all |> List.exists ((=) version) then
                    fail Codes.unsupportedVersion ("the investigation mixes versions (" + String.Join(", ", all |> List.distinct) + "); one investigation has exactly one version")
                else Error e

    // ---------------- writing ----------------

    /// Files for 1.1.0 lines (already numbered, identified and versioned) WITHOUT verifying them; every writer then
    /// verifies through `validate`.
    let filesOf (lines: JValue list) : Result<InvestigationFiles, CausaError> =
        result {
            let! texts = lines |> traverse (Json.canonical >> Result.mapError (CausaError.create Codes.unrepresentable))
            let eventsBytes = Json.strictUtf8.GetBytes(String.Join("", texts |> List.map (fun t -> t + "\n")))
            let! snapshot = snapshotOf lines
            let! snapshotSha = Json.digest snapshot |> Result.mapError (CausaError.create Codes.unrepresentable)
            let first, last = List.head lines, List.last lines
            let manifest =
                JObj
                    [ "formatVersion", JStr version
                      "investigationId", field "investigationId" first |> Option.defaultValue JNull
                      "schemaVersion", JStr version
                      "semanticVersion", JStr version
                      "aggregateVersion", JInt(int64 lines.Length)
                      "eventCount", JInt(int64 lines.Length)
                      "eventsSha256", JStr(Json.sha256Hex eventsBytes)
                      "snapshotVersion", JInt Contract.snapshotVersion
                      "snapshotSha256", JStr snapshotSha
                      "domainPacks", JArr []
                      "createdAt", field "occurredAt" first |> Option.defaultValue JNull
                      "updatedAt", field "occurredAt" last |> Option.defaultValue JNull ]
            let! manifestBytes = Json.fileBytes manifest |> Result.mapError (CausaError.create Codes.unrepresentable)
            let! snapshotBytes = Json.fileBytes snapshot |> Result.mapError (CausaError.create Codes.unrepresentable)
            return { DirectoryId = strField "investigationId" first |> Option.defaultValue ""; Manifest = manifestBytes; Events = eventsBytes; Snapshot = snapshotBytes }
        }

    /// Whether a line needs 1.1.0: a revision event, or a 1.1.0 relationship kind.
    let needs110 (line: JValue) =
        isRevision line
        || (strField "eventType" line = Some "EvidenceRelated" && strField "kind" (payloadOf line) |> Option.exists (fun k -> List.contains k addedKinds))

    /// Appending to a verified 1.0.0 investigation (S-6 A): lines that need 1.1.0 are refused whole, before anything is
    /// written, with CAUSA.UNREPRESENTABLE. The pending command is the caller's to keep. A value is never stripped to
    /// make the write succeed; other lines go through the frozen 1.0.0 append.
    let appendToV100 (v100: SchemaCheck.SchemaSet) (current: Loaded) (expectedVersion: int64) (lines: JValue list) =
        match lines |> List.tryFind needs110 with
        | Some l ->
            fail Codes.unrepresentable ((strField "eventType" l |> Option.defaultValue "") + " needs causa-storage 1.1.0, but the investigation is 1.0.0; it is never upgraded in place (CONTRACT 13.2)")
        | None ->
            lines |> traverse (fun l -> EventWire.decode l |> Result.map (fun d -> d.Event))
            |> Result.bind (Store.append v100 current expectedVersion)
