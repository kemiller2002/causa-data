module Causa.Contract.Tests

open System
open System.IO
open Xunit
open IndyInitRootCauseInvestigator.Engine
open Causa.Contract
open Causa.Tool

let private repoRoot =
    let rec up (d: DirectoryInfo) = if File.Exists(Path.Combine(d.FullName, "engine.lock.json")) then d.FullName else up (d.Parent |> nonNull)
    up (DirectoryInfo(AppContext.BaseDirectory))

let private schemas = lazy (Store.loadSchemas (Layout.schemas repoRoot) |> Result.defaultWith (fun e -> failwith e.Detail))

/// Engine names <-> wire values, exhaustive in both directions.
let private bijective (e: WireEnum<'T>) (engineAll: 'T list) (engineName: 'T -> string) =
    Assert.Equal<Set<string>>(engineAll |> List.map engineName |> Set.ofList, e.All |> List.map engineName |> Set.ofList)
    let wires = e.All |> List.map e.ToWire
    Assert.Equal(wires.Length, (List.distinct wires).Length)
    e.All |> List.iter (fun v -> Assert.Equal(Some v, WireEnum.tryParse e (e.ToWire v)))

[<Fact>]
let ``every persisted enumeration maps bijectively to the engine union`` () =
    bijective Enums.lifecycle CaseNames.allLifecycles CaseNames.lifecycle
    bijective Enums.hypothesisState CaseNames.allHypothesisStates CaseNames.hypothesisState
    bijective Enums.unknownState CaseNames.allUnknownStates CaseNames.unknownState
    bijective Enums.materiality CaseNames.allMaterialities CaseNames.materiality
    bijective Enums.assumptionState CaseNames.allAssumptionStates CaseNames.assumptionState
    bijective Enums.contradictionState CaseNames.allContradictionStates CaseNames.contradictionState
    bijective Enums.actionState CaseNames.allActionStates CaseNames.actionState
    bijective Enums.actionKind CaseNames.allActionKinds CaseNames.actionKind
    bijective Enums.causeState CaseNames.allCauseStates CaseNames.causeState
    bijective Enums.causeRole CaseNames.allCauseRoles CaseNames.causeRole
    bijective Enums.correctiveActionState CaseNames.allCorrectionStates CaseNames.correctionState
    bijective Enums.correctiveActionKind CaseNames.allCorrectionKinds CaseNames.correctionKind
    bijective Enums.evidenceCategory CaseNames.allEvidenceCategories CaseNames.evidenceCategory
    bijective Enums.verificationStatus CaseNames.allVerificationStatuses CaseNames.verificationStatus
    bijective Enums.relationshipKind CaseNames.allRelationshipKinds CaseNames.relationshipKind
    bijective Enums.predictionStrength CaseNames.allPredictionStrengths CaseNames.predictionStrength
    bijective Enums.predictionTiming CaseNames.allPredictionTimings CaseNames.predictionTiming
    bijective Enums.verificationOutcome CaseNames.allVerificationOutcomes CaseNames.verificationOutcome
    bijective Enums.conclusionStrength CaseNames.allConclusionStrengths CaseNames.conclusionStrength
    bijective Enums.proposalKind CaseNames.allProposalKinds CaseNames.proposalKind
    bijective Enums.proposalState [ ProposalActive; ProposalAccepted; ProposalRejected ] CaseNames.proposalState

[<Fact>]
let ``schema enumerations equal the codec enumerations`` () =
    let common = schemas.Value.Documents[Store.schemaIds.Envelope.Replace("event-envelope", "common")]
    let defs = match Negatives.get [ "$defs" ] common with JObj o -> o | _ -> []
    let values name = match Json.field name defs with Some(JObj d) -> (match Json.field "enum" d with Some(JArr xs) -> xs |> List.map (function JStr s -> s | _ -> "") | _ -> []) | _ -> []
    [ "lifecycle", WireEnum.values Enums.lifecycle; "hypothesisState", WireEnum.values Enums.hypothesisState
      "unknownState", WireEnum.values Enums.unknownState; "materiality", WireEnum.values Enums.materiality
      "assumptionState", WireEnum.values Enums.assumptionState; "contradictionState", WireEnum.values Enums.contradictionState
      "actionState", WireEnum.values Enums.actionState; "actionKind", WireEnum.values Enums.actionKind
      "causeState", WireEnum.values Enums.causeState; "causeRole", WireEnum.values Enums.causeRole
      "correctiveActionState", WireEnum.values Enums.correctiveActionState; "correctiveActionKind", WireEnum.values Enums.correctiveActionKind
      "evidenceCategory", WireEnum.values Enums.evidenceCategory; "verificationStatus", WireEnum.values Enums.verificationStatus
      "relationshipKind", WireEnum.values Enums.relationshipKind; "predictionStrength", WireEnum.values Enums.predictionStrength
      "predictionTiming", WireEnum.values Enums.predictionTiming; "verificationOutcome", WireEnum.values Enums.verificationOutcome
      "conclusionStrength", WireEnum.values Enums.conclusionStrength; "proposalKind", WireEnum.values Enums.proposalKind
      "proposalState", WireEnum.values Enums.proposalState; "lifecycleCommand", Enums.lifecycleCommands |> List.map snd
      "hypothesisCommand", WireEnum.values Enums.hypothesisCommand; "contentKind", Enums.contentKinds ]
    |> List.iter (fun (name, codec) -> Assert.Equal<string list>(codec, values name))

[<Fact>]
let ``RFC 8785 string escaping and key ordering`` () =
    let s = JStr "\u20ac$\u000F\u000aA'\u0042\u0022\u005c\\\"/"
    Assert.Equal(Ok "\"€$\\u000f\\nA'B\\\"\\\\\\\\\\\"/\"", Json.canonical s)
    let o =
        JObj [ "\u20ac", JStr "Euro Sign"; "\r", JStr "Carriage Return"; "\ufb33", JStr "Hebrew Letter Dalet With Dagesh"; "1", JStr "One"
               "\ud83d\ude00", JStr "Emoji: Grinning Face"; "\u0080", JStr "Control"; "\u00f6", JStr "Latin Small Letter O With Diaeresis" ]
    let keys = Json.canonical o |> Result.defaultValue "" |> Json.parse |> Result.defaultValue JNull |> function JObj p -> p |> List.map snd | _ -> []
    Assert.Equal<JValue list>([ JStr "Carriage Return"; JStr "One"; JStr "Control"; JStr "Latin Small Letter O With Diaeresis"; JStr "Euro Sign"; JStr "Emoji: Grinning Face"; JStr "Hebrew Letter Dalet With Dagesh" ], keys)

[<Fact>]
let ``JSON parsing rejects what canonical JSON cannot carry`` () =
    [ "1.5"; "1e3"; "9007199254740992"; "{\"a\":1,\"a\":2}"; "\"\\ud800\""; "[1,]"; "NaN"; "// c\n1" ]
    |> List.iter (fun t -> Assert.True(Result.isError (Json.parse t), t))

[<Fact>]
let ``UUIDv5 matches RFC 4122 and the published namespaces`` () =
    let dns = Guid.Parse "6ba7b810-9dad-11d1-80b4-00c04fd430c8"
    Assert.Equal("886313e1-3b8a-5372-9b90-0c9aee199e5d", Uuid.text (Uuid.v5 dns "python.org"))
    Assert.Equal("241fefd4-c810-5a3a-9cfa-4bbd968ae1cb", Uuid.text Uuid.eventNamespace)
    Assert.Equal(None, Uuid.tryParse "241fefd4-c810-5a3a-9cfa-4bbd968ae1cb\n")
    Assert.Equal(None, Uuid.tryParse "241FEFD4-C810-5A3A-9CFA-4BBD968AE1CB")
    Assert.Equal(None, Uuid.tryParse "00000000-0000-0000-0000-000000000000")

[<Fact>]
let ``timestamps are millisecond UTC only`` () =
    Assert.Equal(None, Timestamp.tryFormat (DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks 1L))
    Assert.Equal(Some "2026-01-01T05:00:00.000Z", Timestamp.tryFormat (DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours -5.0)))
    [ "2026-02-30T00:00:00.000Z"; "2026-01-01T00:00:00Z"; "2026-01-01T00:00:00.000+00:00"; "2026-01-01T00:00:60.000Z"; "2026-01-01T00:00:00.000Z\n" ]
    |> List.iter (fun t -> Assert.Equal(None, Timestamp.tryParse t))

[<Fact>]
let ``schema loading fails closed on unsupported keywords and anchors`` () =
    let bad k = JObj [ "$id", JStr "https://x.invalid/a.schema.json"; k, JStr "x" ]
    Assert.True(Result.isError (SchemaCheck.load [ bad "format" ]))
    Assert.True(Result.isError (SchemaCheck.load [ JObj [ "$id", JStr "https://x.invalid/b.schema.json"; "pattern", JStr "^a$|^b$" ] ]))

let private runs = lazy (Scenarios.all |> List.map (Runner.run >> Result.defaultWith failwith))

[<Fact>]
let ``every event type round-trips through the wire codec`` () =
    let events = runs.Value |> List.collect (fun r -> r.Events)
    events |> List.iter (fun e ->
        let decoded = EventWire.encode e |> Result.bind EventWire.decode |> Result.map (fun d -> d.Event)
        Assert.Equal(Ok e, decoded))
    Assert.Equal(31, events |> List.map (fun e -> CaseNames.eventType e.Body) |> List.distinct |> List.length)

[<Fact>]
let ``writers refuse values with no v1 wire form`` () =
    let e = runs.Value.Head.Events |> List.find (fun e -> match e.Body with ContradictionRecorded _ -> false | _ -> true)
    let atSubMs = { e with At = e.At.AddTicks 1L }
    Assert.Equal(Codes.unrepresentable, (EventWire.encode atSubMs |> Result.mapError (fun x -> x.Code) |> function Error c -> c | Ok _ -> "accepted"))
    let ev = Id.create<EvidenceTag> (Guid.NewGuid())
    let dup = { e with Body = ContradictionRecorded(Id.create (Guid.NewGuid()), NonEmptyText.create "x" |> Result.toOption |> Option.get, [ ev; ev ], Material) }
    Assert.Equal(Codes.unrepresentable, (EventWire.encode dup |> function Error x -> x.Code | Ok _ -> "accepted"))

[<Fact>]
let ``generation is deterministic`` () =
    let build () = Dataset.generate schemas.Value |> Result.defaultWith failwith |> fun g -> g.ByKey |> Map.map (fun _ (f, _) -> f.Events, f.Manifest, f.Snapshot)
    Assert.True((build () = build ()), "two generations differ")

[<Fact>]
let ``frozen dataset verifies`` () =
    let failed = Dataset.verify repoRoot |> List.filter (fun c -> not c.Passed)
    Assert.True(failed.IsEmpty, String.Join("\n", failed |> List.map (fun c -> c.Name + ": " + c.Detail)))

[<Fact>]
let ``schema enumeration coverage is complete`` () =
    let common = schemas.Value.Documents[Store.schemaIds.Envelope.Replace("event-envelope", "common")]
    let defs = match Negatives.get [ "$defs" ] common with JObj o -> o | _ -> []
    // Every plain enumeration in common.schema.json except transition-target subsets is checked above.
    let enumNames = defs |> List.choose (fun (k, v) -> match v with JObj d when (Json.field "enum" d).IsSome && not (k.EndsWith "Target") -> Some k | _ -> None)
    Assert.Equal(24, enumNames.Length)

let private loaded events = Store.build schemas.Value events |> Result.defaultWith (fun e -> failwith e.Detail)

[<Fact>]
let ``rebase drops a pending command that already landed remotely (review M4)`` () =
    let events = runs.Value.Head.Events
    // Split on a command-group boundary (a group must never be cut in half).
    let cut = [ 20 .. events.Length - 3 ] |> List.find (fun i -> (List.item (i - 1) events).CommandId <> (List.item i events).CommandId)
    let baseEvents, rest = List.splitAt cut events
    let ours = rest |> List.takeWhile (fun e -> e.CommandId = rest.Head.CommandId)
    let others = rest |> List.skip ours.Length |> List.takeWhile (fun e -> e.CommandId = (List.item ours.Length rest).CommandId)
    let baseFiles, _ = loaded baseEvents
    let remoteFiles, remoteLoaded = loaded (baseEvents @ ours @ others)
    let remote : Causa.GitHub.Sync.Remote =
        { Token = { HeadCommit = "remote"; TreeSha = "tree" }; Files = Some remoteFiles; Loaded = Some remoteLoaded; BlobShas = [] }
    let local : Causa.GitHub.LocalState =
        { RunId = "unit"; InvestigationId = Uuid.text remoteLoaded.InvestigationId; Synced = Some baseFiles; SyncedToken = None; Pending = ours }
    match Causa.GitHub.Sync.rebase schemas.Value local remote with
    | Ok rebased -> Assert.Empty(rebased.Pending)
    | Error e -> failwithf "rebase failed: %s %s" e.Code e.Detail
    // Same command id remotely but different events: a real conflict, never a silent re-decision.
    let tampered = ours |> List.map (fun e -> { e with Actor = Actor.human "someone-else" })
    match Causa.GitHub.Sync.rebase schemas.Value { local with Pending = tampered } remote with
    | Error e -> Assert.Equal(Codes.correlationReused, e.Code)
    | Ok _ -> failwith "a conflicting command id was accepted"
