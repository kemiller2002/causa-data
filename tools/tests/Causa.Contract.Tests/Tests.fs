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
      "correctiveActionState", WireEnum.values Enums.correctiveActionState; "proposalKind", WireEnum.values Enums.proposalKind
      "lifecycleCommand", Enums.lifecycleCommands |> List.map snd; "hypothesisCommand", WireEnum.values Enums.hypothesisCommand ]
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
