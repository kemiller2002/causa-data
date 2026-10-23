/// CONTRACT §8.3 normative fold, implemented directly over WIRE events (JSON), independent of
/// the engine's Investigation record. Produces the §8.2 state object and the §8.1 snapshot.
module CleanConsumer.Projection

open System
open CleanConsumer.Json

type Entity = Map<string, JsonValue>

type State =
    { Header: Map<string, JsonValue>
      /// array name -> entity id -> entity
      Arrays: Map<string, Map<string, Entity>> }

let private get (name: string) (o: JsonValue) = field name o |> Option.defaultValue JNull

let private add (arrayName: string) (entity: (string * JsonValue) list) (provenance: JsonValue) (st: State) =
    let e = Map.ofList (("provenance", provenance) :: entity)
    let key = match e["id"] with JString s -> s | other -> canonical other
    { st with Arrays = st.Arrays |> Map.add arrayName (st.Arrays[arrayName] |> Map.add key e) }

/// Updates to an id that does not exist leave state unchanged (§8.3).
let private update (arrayName: string) (idValue: JsonValue) (f: Entity -> Entity) (st: State) =
    match idValue with
    | JString key ->
        match Map.tryFind key st.Arrays[arrayName] with
        | Some e -> { st with Arrays = st.Arrays |> Map.add arrayName (st.Arrays[arrayName] |> Map.add key (f e)) }
        | None -> st
    | _ -> st

let private set (k: string) (v: JsonValue) (e: Entity) = e |> Map.add k v

let private apply (st: State) (ev: JsonValue) : State =
    let p = get "payload" ev
    let pf k = get k p
    let version = get "aggregateVersion" ev
    let occurredAt = get "occurredAt" ev
    let provenance = JObject [ "actor", get "actor" ev; "recordedAt", occurredAt ]
    let st = { st with Header = st.Header |> Map.add "version" version }
    let addE name fields = add name fields provenance st
    match str "eventType" ev |> Option.defaultValue "" with
    | "InvestigationStarted" -> st // only valid as the first event; handled by `start`
    | "LifecycleChanged" -> { st with Header = st.Header |> Map.add "lifecycle" (pf "to") }
    | "ObservationRecorded" -> addE "observations" [ "id", pf "observationId"; "original", pf "original"; "observedAt", pf "observedAt" ]
    | "HypothesisRecorded" ->
        addE "hypotheses" [ "id", pf "hypothesisId"; "statement", pf "statement"; "mechanism", pf "mechanism"; "state", JString "proposed"; "history", JArray [] ]
    | "HypothesisTransitioned" ->
        let change =
            JObject [ "from", pf "from"; "to", pf "to"; "command", pf "command"; "reason", pf "reason"; "evidenceIds", pf "evidenceIds"
                      "at", occurredAt; "aggregateVersion", version ]
        st
        |> update "hypotheses" (pf "hypothesisId") (fun h ->
            let history = match h["history"] with JArray xs -> xs | _ -> []
            h |> set "state" (pf "to") |> set "history" (JArray(history @ [ change ])))
    | "EvidenceAdmitted" ->
        addE "evidence"
            [ "id", pf "evidenceId"; "contentKind", pf "contentKind"; "content", pf "content"; "category", pf "category"
              "source", pf "source"; "capturedAt", pf "capturedAt"; "verification", JString "unverified"; "supersededBy", JNull
              "admittedAtVersion", version ]
    | "EvidenceSuperseded" -> st |> update "evidence" (pf "evidenceId") (set "supersededBy" (pf "replacementEvidenceId"))
    | "EvidenceVerificationAssessed" -> st |> update "evidence" (pf "evidenceId") (set "verification" (pf "verification"))
    | "EvidenceRelated" ->
        addE "relationships"
            [ "id", pf "relationshipId"; "evidenceId", pf "evidenceId"; "hypothesisId", pf "hypothesisId"; "kind", pf "kind"; "rationale", pf "rationale" ]
    | "UnknownIdentified" ->
        addE "unknowns" [ "id", pf "unknownId"; "question", pf "question"; "materiality", pf "materiality"; "state", JString "identified"; "basis", JNull ]
    | "UnknownTransitioned" -> st |> update "unknowns" (pf "unknownId") (set "state" (pf "state") >> set "basis" (pf "basis"))
    | "AssumptionRecorded" ->
        addE "assumptions" [ "id", pf "assumptionId"; "proposition", pf "proposition"; "materiality", pf "materiality"; "state", JString "explicitUnverified" ]
    | "AssumptionTransitioned" -> st |> update "assumptions" (pf "assumptionId") (set "state" (pf "state"))
    | "ContradictionRecorded" ->
        addE "contradictions"
            [ "id", pf "contradictionId"; "statement", pf "statement"; "evidenceIds", pf "evidenceIds"; "materiality", pf "materiality"
              "state", JString "detected"; "basis", JNull ]
    | "ContradictionTransitioned" -> st |> update "contradictions" (pf "contradictionId") (set "state" (pf "state") >> set "basis" (pf "basis"))
    | "PredictionRecorded" ->
        addE "predictions"
            [ "id", pf "predictionId"; "hypothesisId", pf "hypothesisId"; "statement", pf "statement"; "strength", pf "strength"; "timing", pf "timing" ]
    | "ActionRecorded" ->
        addE "actions" [ "id", pf "actionId"; "kind", pf "kind"; "purpose", pf "purpose"; "targetHypothesisIds", pf "targetHypothesisIds"; "state", JString "proposed" ]
    | "ActionTransitioned" -> st |> update "actions" (pf "actionId") (set "state" (pf "state"))
    | "FindingRecorded" ->
        addE "findings" [ "id", pf "findingId"; "actionId", pf "actionId"; "originalResult", pf "originalResult"; "interpretation", pf "interpretation" ]
    | "NegativeKnowledgeEstablished" ->
        addE "negativeKnowledge" [ "id", pf "negativeKnowledgeId"; "statement", pf "statement"; "scope", pf "scope"; "basisEvidenceIds", pf "basisEvidenceIds" ]
    | "CausalAssertionRecorded" ->
        addE "causes" [ "id", pf "causeId"; "outcome", pf "outcome"; "members", pf "members"; "mechanism", pf "mechanism"; "state", JString "proposed" ]
    | "CausalAssertionConfirmed" -> st |> update "causes" (pf "causeId") (set "state" (JString "confirmed"))
    | "CausalAssertionReopened" -> st |> update "causes" (pf "causeId") (set "state" (JString "reopened"))
    | "CorrectiveActionRecorded" ->
        addE "correctiveActions"
            [ "id", pf "correctiveActionId"; "kind", pf "kind"; "description", pf "description"; "targetCauseIds", pf "targetCauseIds"
              "state", JString "proposed"; "awaitingSinceVersion", JInt 0L ]
    | "CorrectiveActionTransitioned" ->
        st
        |> update "correctiveActions" (pf "correctiveActionId") (fun ca ->
            let ca = ca |> set "state" (pf "state")
            if pf "state" = JString "awaitingVerificationOfEffect" then ca |> set "awaitingSinceVersion" version else ca)
    | "CriterionRecorded" ->
        addE "criteria" [ "id", pf "criterionId"; "correctiveActionId", pf "correctiveActionId"; "expectedOutcome", pf "expectedOutcome"; "method", pf "method" ]
    | "VerificationRecorded" ->
        addE "verifications"
            [ "id", pf "verificationId"; "criterionId", pf "criterionId"; "outcome", pf "outcome"; "evidenceIds", pf "evidenceIds"; "recordedAtVersion", version ]
    | "ConclusionRecorded" ->
        addE "conclusions"
            [ "id", pf "conclusionId"; "statement", pf "statement"; "strength", pf "strength"; "causeIds", pf "causeIds"
              "residualUncertainty", pf "residualUncertainty"; "recordedAtVersion", version ]
    | "ProposalRecorded" ->
        addE "proposals" [ "id", pf "proposalId"; "kind", pf "kind"; "text", pf "text"; "rationale", pf "rationale"; "state", JString "active"; "decision", JNull ]
    | "ProposalWasAccepted" -> st |> update "proposals" (pf "proposalId") (set "state" (JString "accepted"))
    | "ProposalWasRejected" -> st |> update "proposals" (pf "proposalId") (set "state" (JString "rejected") >> set "decision" (pf "reason"))
    | _ -> st

let private start (ev: JsonValue) : Result<State, string> =
    match str "eventType" ev with
    | Some "InvestigationStarted" ->
        let p = get "payload" ev
        let empty = Schema.entityArrays |> List.map (fun a -> a, Map.empty) |> Map.ofList
        let header =
            Map.ofList [ "investigationId", get "investigationId" ev; "title", get "title" p; "lifecycle", JString "draft"
                         "version", get "aggregateVersion" ev; "createdAt", get "occurredAt" ev ]
        let st = { Header = header; Arrays = empty }
        Ok(add "observations" [ "id", get "observationId" p; "original", get "original" p; "observedAt", JNull ]
               (JObject [ "actor", get "actor" ev; "recordedAt", get "occurredAt" ev ]) st)
    | _ -> Error "projection is undefined: the history does not begin with InvestigationStarted"

/// Final state object: every entity array sorted by id with ordinal comparison of the UUID text.
let toJson (st: State) : JsonValue =
    let arrays =
        Schema.entityArrays
        |> List.map (fun name ->
            let items =
                st.Arrays[name] |> Map.toList
                |> List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))
                |> List.map (fun (_, e) -> JObject(Map.toList e))
            name, JArray items)
    JObject((st.Header |> Map.toList) @ arrays)

/// Fold the wire history (already in aggregateVersion order) into the state object.
let project (events: JsonValue list) : Result<JsonValue, string> =
    match events with
    | [] -> Error "empty history"
    | first :: rest -> start first |> Result.map (fun st -> rest |> List.fold apply st |> toJson)

/// §8.1 snapshot envelope; generatedAt = occurredAt of the last applied event.
let snapshotOf (events: JsonValue list) : Result<JsonValue, string> =
    project events
    |> Result.map (fun state ->
        let last = List.last events
        JObject [ "formatVersion", JString "1.0.0"; "investigationId", get "investigationId" (List.head events)
                  "aggregateVersion", get "aggregateVersion" last; "projectionVersion", JInt 1L; "semanticVersion", JString "1.0.0"
                  "generatedAt", get "occurredAt" last; "state", state ])

/// §9.1 manifest for a file set.
let manifestOf (investigationId: string) (events: JsonValue list) (eventsBytes: byte[]) (snapshot: JsonValue) : JsonValue =
    let n = JInt(int64 events.Length)
    JObject [ "formatVersion", JString "1.0.0"; "investigationId", JString investigationId; "schemaVersion", JString "1.0.0"
              "semanticVersion", JString "1.0.0"; "aggregateVersion", n; "eventCount", n
              "eventsSha256", JString(Wire.sha256Hex eventsBytes); "snapshotVersion", JInt 1L
              "snapshotSha256", JString(Wire.canonicalDigest snapshot); "domainPacks", JArray []
              "createdAt", get "occurredAt" (List.head events); "updatedAt", get "occurredAt" (List.last events) ]
