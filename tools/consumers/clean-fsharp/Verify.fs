/// Reader validation of one investigation file set in the CONTRACT §11.1 check order.
/// The first failing stage is the primary code; stage 15 also carries the engine's domain code.
module CleanConsumer.Verify

open System
open IndyInitRootCauseInvestigator.Engine
open CleanConsumer.Json
open CleanConsumer.Domain

type Failure =
    { Code: string
      Detail: string
      DomainCode: string option }

let fail code detail : Result<'T, Failure> = Error { Code = code; Detail = detail; DomainCode = None }

type FileSet =
    { DirectoryName: string
      /// Names of every entry in the directory (§3.3: exactly the three files).
      Listing: string list
      Manifest: byte[]
      Events: byte[]
      Snapshot: byte[] }

type Valid =
    { InvestigationId: string
      WireEvents: JsonValue list
      EngineEvents: Event list
      State: JsonValue
      Snapshot: JsonValue
      Manifest: JsonValue }

let private firstError (code: string) (errors: string list) : Result<unit, Failure> =
    match errors with
    | [] -> Ok()
    | e :: rest -> fail code (if rest.IsEmpty then e else sprintf "%s (+%d more)" e rest.Length)

let private parseOrFail what (text: string) =
    Json.parse text |> Result.mapError (fun e -> { Code = "CAUSA.JSON_MALFORMED"; Detail = $"{what}: {e}"; DomainCode = None })

let private versionFields (doc: string) (fields: string list) (v: JsonValue) =
    fields
    |> List.choose (fun f ->
        match field f v with
        // Only a STRING other than "1.0.0" is a version claim; missing or non-string is stage 7 (§11.1).
        | Some(JString v) when v <> "1.0.0" -> Some $"{doc}.{f} = \"{v}\" (this reader supports only \"1.0.0\")"
        | _ -> None)

/// §9.2 step (2): appended NDJSON lines alone, through stages 2–7, numbered from `firstLine` (= current + 1).
let validateAppendedLines (firstLine: int) (bytes: byte[]) : Result<JsonValue list, Failure> =
    result {
        let! lines = Wire.ndjsonLines bytes |> Result.mapError (fun e -> { Code = "CAUSA.ENCODING"; Detail = $"appended lines: {e}"; DomainCode = None })
        let numbered = lines |> List.mapi (fun i l -> firstLine + i, l)
        let! events = numbered |> traverse (fun (n, l) -> parseOrFail $"appended line {n}" l)
        let pairs = List.zip numbered events
        do! pairs |> List.filter (fun ((_, l), e) -> canonical e <> l)
            |> List.map (fun ((n, _), _) -> $"appended line {n} is not its RFC 8785 canonical form")
            |> firstError "CAUSA.NON_CANONICAL"
        do! pairs |> List.collect (fun ((n, _), e) -> versionFields $"appended line {n}" [ "formatVersion"; "semanticVersion" ] e)
            |> firstError "CAUSA.UNSUPPORTED_VERSION"
        do! pairs |> List.choose (fun ((n, _), e) ->
                match field "eventType" e with
                | Some(JString t) when not (List.contains t Schema.eventTypes) -> Some $"appended line {n}: unregistered eventType \"{t}\""
                | _ -> None)
            |> firstError "CAUSA.UNKNOWN_EVENT_TYPE"
        do! pairs |> List.collect (fun ((n, _), e) -> Schema.validateEvent $"appended line {n}" e) |> firstError "CAUSA.SCHEMA_VIOLATION"
        return events
    }

/// Validate an investigation directory's three files. `DirectoryName` is the last path segment.
let validate (files: FileSet) : Result<Valid, Failure> =
    result {
        // 1. PATH_INVALID
        do! if Wire.isCanonicalUuid files.DirectoryName then Ok()
            else fail "CAUSA.PATH_INVALID" $"directory name \"{files.DirectoryName}\" is not a canonical lowercase UUID (§3.2)"
        let listing = files.Listing |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))
        do! if listing = [ "events.ndjson"; "manifest.json"; "snapshot.json" ] then Ok()
            else fail "CAUSA.PATH_INVALID" $"""directory holds [{String.Join(", ", listing)}]; exactly manifest.json, events.ndjson, snapshot.json are required (§3.3)"""

        // 2. ENCODING (all three files)
        let enc what r = r |> Result.mapError (fun e -> { Code = "CAUSA.ENCODING"; Detail = $"{what}: {e}"; DomainCode = None })
        let! manifestText = Wire.jsonFileText files.Manifest |> enc "manifest.json"
        let! lines = Wire.ndjsonLines files.Events |> enc "events.ndjson"
        let! snapshotText = Wire.jsonFileText files.Snapshot |> enc "snapshot.json"

        // 3. JSON_MALFORMED (all files)
        let! manifest = parseOrFail "manifest.json" manifestText
        let! events = lines |> List.mapi (fun i l -> i, l) |> traverse (fun (i, l) -> parseOrFail $"events.ndjson line {i + 1}" l)
        let! snapshot = parseOrFail "snapshot.json" snapshotText

        // 4. NON_CANONICAL (manifest, snapshot, each event line)
        let nonCanonical =
            (if canonical manifest <> manifestText then [ "manifest.json is not its RFC 8785 canonical form" ] else [])
            @ (List.zip lines events
               |> List.mapi (fun i (l, e) -> i, l, e)
               |> List.filter (fun (_, l, e) -> canonical e <> l)
               |> List.map (fun (i, _, _) -> $"events.ndjson line {i + 1} is not its RFC 8785 canonical form"))
            @ (if canonical snapshot <> snapshotText then [ "snapshot.json is not its RFC 8785 canonical form" ] else [])
        do! firstError "CAUSA.NON_CANONICAL" nonCanonical

        // 5. UNSUPPORTED_VERSION
        let versions =
            versionFields "manifest" [ "formatVersion"; "schemaVersion"; "semanticVersion" ] manifest
            @ (events |> List.mapi (fun i e -> versionFields $"event[{i + 1}]" [ "formatVersion"; "semanticVersion" ] e) |> List.concat)
            @ versionFields "snapshot" [ "formatVersion"; "semanticVersion" ] snapshot
        do! firstError "CAUSA.UNSUPPORTED_VERSION" versions

        // 6. UNKNOWN_EVENT_TYPE
        let unknownTypes =
            events
            |> List.mapi (fun i e -> i, field "eventType" e)
            |> List.choose (fun (i, t) ->
                match t with
                | Some(JString s) when not (List.contains s Schema.eventTypes) -> Some $"event line {i + 1}: unregistered eventType \"{s}\" (§7.1 fail closed)"
                | _ -> None)
        do! firstError "CAUSA.UNKNOWN_EVENT_TYPE" unknownTypes

        // 7. SCHEMA_VIOLATION (manifest, each event, snapshot; calendar-invalid timestamps)
        let schema =
            Schema.validate "manifest" Schema.investigationManifest manifest
            @ (events |> List.mapi (fun i e -> Schema.validateEvent $"event[line {i + 1}]" e) |> List.concat)
            @ Schema.validate "snapshot" Schema.snapshot snapshot
        do! firstError "CAUSA.SCHEMA_VIOLATION" schema

        // 8. MANIFEST_MISMATCH (id/directory, counts, created/updated times)
        let n = int64 events.Length
        let mId = str "investigationId" manifest |> Option.defaultValue ""
        let mVersion = int "aggregateVersion" manifest |> Option.defaultValue 0L
        let mCount = int "eventCount" manifest |> Option.defaultValue 0L
        let mismatch =
            [ if mId <> files.DirectoryName then $"manifest investigationId {mId} differs from directory {files.DirectoryName}"
              if mVersion <> n then $"manifest aggregateVersion {mVersion} but events.ndjson has {n} lines"
              if mCount <> n then $"manifest eventCount {mCount} but events.ndjson has {n} lines"
              if str "createdAt" manifest <> str "occurredAt" (List.head events) then "manifest createdAt differs from the first event's occurredAt"
              if str "updatedAt" manifest <> str "occurredAt" (List.last events) then "manifest updatedAt differs from the last event's occurredAt" ]
        do! firstError "CAUSA.MANIFEST_MISMATCH" mismatch

        // 9. EVENTS_HASH_MISMATCH
        let actual = Wire.sha256Hex files.Events
        let recorded = str "eventsSha256" manifest |> Option.defaultValue "?"
        do! if recorded = actual then Ok()
            else fail "CAUSA.EVENTS_HASH_MISMATCH" $"manifest eventsSha256 {recorded} but the file digest is {actual}"

        // 10. INVESTIGATION_MISMATCH
        let foreign =
            events |> List.mapi (fun line e -> line + 1, str "investigationId" e) |> List.choose (fun (line, inv) ->
                match inv with
                | Some i when i <> mId -> Some $"event line {line} names investigation {i}, not {mId}"
                | _ -> None)
        do! firstError "CAUSA.INVESTIGATION_MISMATCH" foreign

        // 11. EVENT_ID_DUPLICATE
        let eventIds = events |> List.choose (str "eventId")
        let dups = eventIds |> List.countBy id |> List.filter (fun (_, c) -> c > 1) |> List.map (fun (e, c) -> $"eventId {e} occurs {c} times")
        do! firstError "CAUSA.EVENT_ID_DUPLICATE" dups

        // 12. SEQUENCE_INVALID (exactly 1..N in file order)
        let sequence =
            events |> List.mapi (fun i e -> int64 (i + 1), int "aggregateVersion" e)
            |> List.choose (fun (want, got) ->
                if got = Some want then None
                else Some(sprintf "line %d has aggregateVersion %d, expected %d" want (Option.defaultValue 0L got) want))
        do! firstError "CAUSA.SEQUENCE_INVALID" sequence

        // 13. EVENT_ID_MISMATCH (§5.2 derivation)
        let derivation =
            events |> List.choose (fun e ->
                let v = int "aggregateVersion" e |> Option.defaultValue 0L
                let inv = str "investigationId" e |> Option.defaultValue ""
                let corr = str "correlationId" e |> Option.defaultValue ""
                let want = Wire.deriveEventId inv v corr
                let got = str "eventId" e |> Option.defaultValue "?"
                if got = want then None else Some $"event {v}: eventId {got} but the §5.2 derivation is {want}")
        do! firstError "CAUSA.EVENT_ID_MISMATCH" derivation

        // 14. CORRELATION_REUSED (§5.4: a correlationId names exactly one group of consecutive events)
        let groupIds =
            events
            |> List.fold (fun (acc: (string * int64) list) e ->
                let c = str "correlationId" e |> Option.defaultValue ""
                match acc with
                | (head, _) :: _ when head = c -> acc
                | _ -> (c, int "aggregateVersion" e |> Option.defaultValue 0L) :: acc) []
            |> List.rev
        let reused =
            groupIds
            |> List.groupBy fst
            |> List.filter (fun (_, gs) -> gs.Length > 1)
            |> List.map (fun (c, gs) ->
                let starts = String.Join(", ", gs |> List.map (snd >> string))
                $"correlationId {c} names {gs.Length} non-adjacent groups (starting at versions {starts})")
        do! firstError "CAUSA.CORRELATION_REUSED" reused

        // 15. HISTORY_INADMISSIBLE (engine History.validate is the oracle, §11.3)
        let! engineEvents =
            events |> traverse decodeEvent
            |> Result.mapError (fun e -> { Code = "CAUSA.SCHEMA_VIOLATION"; Detail = $"cannot decode into engine types: {e}"; DomainCode = None })
        do! match History.validate engineEvents with
            | [] -> Ok()
            | first :: rest ->
                Error { Code = "CAUSA.HISTORY_INADMISSIBLE"
                        Detail = first.Code + ": " + first.Message + (if rest.IsEmpty then "" else sprintf " (+%d more)" rest.Length)
                        DomainCode = Some first.Code }

        // 16. SNAPSHOT_HASH_MISMATCH
        let snapDigest = Wire.canonicalDigest snapshot
        let recordedSnap = str "snapshotSha256" manifest |> Option.defaultValue "?"
        do! if recordedSnap = snapDigest then Ok()
            else fail "CAUSA.SNAPSHOT_HASH_MISMATCH" $"manifest snapshotSha256 {recordedSnap} but the canonical snapshot digest is {snapDigest}"

        // 17. SNAPSHOT_MISMATCH (byte-identical to the projection of the replayed history)
        let! projected = Projection.snapshotOf events |> Result.mapError (fun e -> { Code = "CAUSA.SNAPSHOT_MISMATCH"; Detail = e; DomainCode = None })
        do! if canonical projected = snapshotText then Ok()
            else fail "CAUSA.SNAPSHOT_MISMATCH" "snapshot.json is not byte-identical to the projection of the replayed history"

        let state = field "state" projected |> Option.defaultValue JNull
        return { InvestigationId = mId; WireEvents = events; EngineEvents = engineEvents; State = state; Snapshot = projected; Manifest = manifest }
    }

let describe (r: Result<Valid, Failure>) =
    match r with
    | Ok v -> $"valid ({v.WireEvents.Length} events)"
    | Error f -> f.Code + (match f.DomainCode with Some d -> " [" + d + "]" | None -> "") + ": " + f.Detail
