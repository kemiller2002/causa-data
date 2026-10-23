/// Hand-written structural validation of every document the contract defines.
/// Each spec is transcribed from the CONTRACT.md tables (§4, §5, §6, §7, §8, §9.1, §10.3, §12.2);
/// this is not a general JSON Schema engine.
module CleanConsumer.Schema

open System.Text.RegularExpressions
open CleanConsumer.Json

// ---------------------------------------------------------------- §6 enumerations

/// §7: the 31 registered discriminators, in engine order.
let eventTypes =
    [ "InvestigationStarted"; "LifecycleChanged"; "ObservationRecorded"; "HypothesisRecorded"; "HypothesisTransitioned"
      "EvidenceAdmitted"; "EvidenceSuperseded"; "EvidenceVerificationAssessed"; "EvidenceRelated"; "UnknownIdentified"
      "UnknownTransitioned"; "AssumptionRecorded"; "AssumptionTransitioned"; "ContradictionRecorded"
      "ContradictionTransitioned"; "PredictionRecorded"; "ActionRecorded"; "ActionTransitioned"; "FindingRecorded"
      "NegativeKnowledgeEstablished"; "CausalAssertionRecorded"; "CausalAssertionConfirmed"; "CausalAssertionReopened"
      "CorrectiveActionRecorded"; "CorrectiveActionTransitioned"; "CriterionRecorded"; "VerificationRecorded"
      "ConclusionRecorded"; "ProposalRecorded"; "ProposalWasAccepted"; "ProposalWasRejected" ]

let enums: Map<string, string list> =
    Map.ofList
        [ "lifecycle", [ "draft"; "active"; "awaitingEvidence"; "awaitingVerification"; "resolved"; "closed"; "archived" ]
          "hypothesisState", [ "proposed"; "plausible"; "supported"; "weakened"; "falsified"; "superseded" ]
          "unknownState", [ "identified"; "investigating"; "resolved"; "acceptedResidual"; "superseded" ]
          "materiality", [ "blocking"; "material"; "nonMaterial"; "informational" ]
          "assumptionState", [ "explicitUnverified"; "provisionallyAccepted"; "verified"; "rejected"; "expired" ]
          "contradictionState", [ "detected"; "triaged"; "investigating"; "resolved"; "acceptedResidual"; "superseded" ]
          "actionState", [ "proposed"; "approved"; "inProgress"; "completed"; "interpreted"; "rejected"; "cancelled" ]
          "actionKind", [ "test"; "inspection"; "query"; "comparison"; "experiment" ]
          "causeState", [ "proposed"; "confirmed"; "reopened"; "superseded" ]
          "causeRole", [ "rootCause"; "contributingFactor"; "trigger"; "enablingCondition" ]
          "correctiveActionState",
          [ "proposed"; "approved"; "inProgress"; "implemented"; "awaitingVerificationOfEffect"; "verifiedEffective"
            "verificationFailed"; "reworkRequired"; "cancelled" ]
          "correctiveActionKind", [ "mitigation"; "workaround"; "correction"; "prevention" ]
          "evidenceCategory",
          [ "directObservation"; "measurement"; "log"; "document"; "experimentResult"; "testimony"; "externalReference" ]
          "verificationStatus", [ "unverified"; "partiallyVerified"; "verified"; "invalidated" ]
          "relationshipKind", [ "supports"; "contradicts"; "discriminates"; "insufficient" ]
          "predictionStrength", [ "necessary"; "expected"; "discriminating"; "nonDiscriminating" ]
          "predictionTiming", [ "prospective"; "retrospective" ]
          "verificationOutcome", [ "satisfied"; "failed"; "inconclusive" ]
          "conclusionStrength",
          [ "plausibleExplanation"; "leadingExplanation"; "supportedCause"; "confirmedCause"; "acceptedWithResidualUncertainty" ]
          "proposalKind", [ "hypothesis"; "unknown"; "action" ]
          "proposalState", [ "active"; "accepted"; "rejected" ]
          "hypothesisCommand", [ "markPlausible"; "markSupported"; "markWeakened"; "falsify"; "supersede"; "reopen" ]
          "lifecycleCommand",
          [ "activateInvestigation"; "markAwaitingEvidence"; "markAwaitingVerification"; "resolveInvestigation"
            "closeInvestigation"; "reopenInvestigation"; "verificationFailed" ]
          "contentKind", [ "text"; "artifact" ]
          "lifecycleTarget", [ "active"; "awaitingEvidence"; "awaitingVerification"; "resolved"; "closed" ]
          "hypothesisTarget", [ "plausible"; "supported"; "weakened"; "falsified"; "superseded" ]
          "unknownTarget", [ "investigating"; "resolved"; "acceptedResidual" ]
          "assumptionTarget", [ "provisionallyAccepted"; "verified"; "rejected" ]
          "contradictionTarget", [ "triaged"; "resolved"; "acceptedResidual" ]
          "actionTarget", [ "approved"; "rejected"; "inProgress"; "completed"; "cancelled" ]
          "correctiveActionTarget",
          [ "approved"; "cancelled"; "inProgress"; "implemented"; "awaitingVerificationOfEffect"; "verificationFailed"
            "verifiedEffective"; "reworkRequired" ]
          // §5.3: the non-agent half of the actor oneOf.
          "nonAgentActorKind", [ "human"; "system" ]
          "eventType", eventTypes ]

// ---------------------------------------------------------------- the spec language

type Ty =
    | Uuid
    | NullableUuid
    /// ordered, distinct (§2.7)
    | UuidList
    | NonEmptyUuidList
    | Text
    | NullableText
    | Timestamp
    | NullableTimestamp
    | Enum of string
    | IntRange of int64 * int64
    | ConstString of string
    | ConstInt of int64
    | Sha256
    | Slug
    | Pattern of Regex
    | EmptyArray
    | Actor
    | Provenance
    | ArrayOf of item: Ty * unique: bool * minItems: int
    | Obj of ObjSpec
    | NullOr of Ty
    /// An object with arbitrary keys whose values all have the given type.
    | MapOf of Ty
    /// Any object; its content is validated elsewhere (event payload, dispatched on eventType).
    | AnyObject

and ObjSpec =
    { Fields: (string * Ty) list
      /// Cross-field invariants (schema if/then), evaluated only when every field is valid.
      Rules: Map<string, JsonValue> -> string list }

let private noRules (_: Map<string, JsonValue>) : string list = []
let obj fields = Obj { Fields = fields; Rules = noRules }
let objWith rules fields = Obj { Fields = fields; Rules = rules }

let private maxV = maxSafeInteger
let aggregateVersion = IntRange(1L, maxV)
let versionOrZero = IntRange(0L, maxV)
let v100 = ConstString "1.0.0"

let private distinct (xs: JsonValue list) = (xs |> List.map canonical |> List.distinct |> List.length) = xs.Length

let rec validate (path: string) (ty: Ty) (v: JsonValue) : string list =
    let bad what = [ $"{path}: expected {what}, got {typeName v}" ]
    match ty, v with
    | Uuid, JString s -> if Wire.isCanonicalUuid s then [] else [ $"{path}: \"{s}\" is not a lowercase canonical non-nil UUID" ]
    | Uuid, _ -> bad "uuid string"
    | NullableUuid, JNull -> []
    | NullableUuid, _ -> validate path Uuid v
    | UuidList, _ -> validate path (ArrayOf(Uuid, true, 0)) v
    | NonEmptyUuidList, _ -> validate path (ArrayOf(Uuid, true, 1)) v
    | Text, JString s -> if Wire.isNonBlankText s then [] else [ $"{path}: text must contain a non-whitespace character" ]
    | Text, _ -> bad "text string"
    | NullableText, JNull -> []
    | NullableText, _ -> validate path Text v
    | Timestamp, JString s ->
        match Wire.parseTimestamp s with
        | Ok _ -> []
        | Error e -> [ $"{path}: {e}" ]
    | Timestamp, _ -> bad "timestamp string"
    | NullableTimestamp, JNull -> []
    | NullableTimestamp, _ -> validate path Timestamp v
    | Enum name, JString s ->
        if List.contains s enums[name] then [] else [ $"{path}: \"{s}\" is not a {name} value" ]
    | Enum name, _ -> bad $"{name} string"
    | IntRange(lo, hi), JInt n -> if n >= lo && n <= hi then [] else [ $"{path}: {n} outside [{lo}, {hi}]" ]
    | IntRange _, _ -> bad "integer"
    | ConstString c, JString s when s = c -> []
    | ConstString c, _ -> [ $"{path}: must be \"{c}\"" ]
    | ConstInt c, JInt n when n = c -> []
    | ConstInt c, _ -> [ $"{path}: must be {c}" ]
    | Sha256, JString s -> if Wire.sha256Pattern.IsMatch s then [] else [ $"{path}: not a lowercase SHA-256 hex digest" ]
    | Sha256, _ -> bad "sha256 string"
    | Slug, JString s -> if Wire.slugPattern.IsMatch s then [] else [ $"{path}: \"{s}\" is not a slug" ]
    | Slug, _ -> bad "slug string"
    | Pattern rx, JString s -> if rx.IsMatch s then [] else [ $"{path}: \"{s}\" does not match {rx}" ]
    | Pattern _, _ -> bad "string"
    | EmptyArray, JArray [] -> []
    | EmptyArray, _ -> [ $"{path}: must be []" ]
    | Actor, JObject _ ->
        match str "kind" v with
        | Some("human" | "system") -> validate path (obj [ "actorId", Text; "kind", Enum "nonAgentActorKind" ]) v
        | Some "agent" -> validate path (obj [ "actorId", Text; "kind", ConstString "agent"; "provider", Text; "model", Text ]) v
        | _ -> [ $"{path}.kind: must be human, system or agent" ]
    | Actor, _ -> bad "actor object"
    | Provenance, _ -> validate path (obj [ "actor", Actor; "recordedAt", Timestamp ]) v
    | ArrayOf(item, unique, minItems), JArray xs ->
        let items = xs |> List.mapi (fun i x -> validate $"{path}[{i}]" item x) |> List.concat
        let dup = if unique && not (distinct xs) then [ $"{path}: items must be distinct" ] else []
        let size = if xs.Length < minItems then [ $"{path}: needs at least {minItems} item(s)" ] else []
        items @ dup @ size
    | ArrayOf _, _ -> bad "array"
    | MapOf t, JObject ms -> ms |> List.collect (fun (k, x) -> validate $"{path}[\"{k}\"]" t x)
    | MapOf _, _ -> bad "object"
    | NullOr _, JNull -> []
    | NullOr t, _ -> validate path t v
    | AnyObject, JObject _ -> []
    | AnyObject, _ -> bad "object"
    | Obj spec, JObject members ->
        let declared = spec.Fields |> List.map fst |> Set.ofList
        let unknown =
            members |> List.map fst |> List.filter (declared.Contains >> not)
            |> List.map (fun k -> $"{path}: undeclared field \"{k}\" (closed structure, §2.4)")
        let m = Map.ofList members
        let fieldErrors =
            spec.Fields
            |> List.collect (fun (name, fty) ->
                match Map.tryFind name m with
                | None -> [ $"{path}: missing required field \"{name}\" (§2.5: no optional keys)" ]
                | Some fv -> validate $"{path}.{name}" fty fv)
        let ruleErrors = if unknown.IsEmpty && fieldErrors.IsEmpty then spec.Rules m |> List.map (fun e -> $"{path}: {e}") else []
        unknown @ fieldErrors @ ruleErrors
    | Obj _, _ -> bad "object"


// ---------------------------------------------------------------- rule helpers

let private s (m: Map<string, JsonValue>) k = match Map.tryFind k m with Some(JString x) -> Some x | _ -> None
let private isNull (m: Map<string, JsonValue>) k = Map.tryFind k m = Some JNull
let private nonEmptyArray (m: Map<string, JsonValue>) k = match Map.tryFind k m with Some(JArray(_ :: _)) -> true | _ -> false

let private hypothesisRules (m: Map<string, JsonValue>) =
    let targetOf =
        Map.ofList [ "markPlausible", "plausible"; "markSupported", "supported"; "markWeakened", "weakened"
                     "falsify", "falsified"; "supersede", "superseded"; "reopen", "plausible" ]
    let needsEvidence = set [ "markSupported"; "markWeakened"; "falsify"; "reopen" ]
    match s m "command", s m "to" with
    | Some c, Some t ->
        [ if targetOf[c] <> t then $"command {c} requires to = \"{targetOf[c]}\" (got \"{t}\")"
          if needsEvidence.Contains c && not (nonEmptyArray m "evidenceIds") then $"command {c} requires non-empty evidenceIds" ]
    | _ -> []

let private lifecycleRules (m: Map<string, JsonValue>) =
    // command -> (to, reason must be text?)
    let table =
        Map.ofList [ "activateInvestigation", ("active", false); "markAwaitingEvidence", ("awaitingEvidence", true)
                     "markAwaitingVerification", ("awaitingVerification", false); "resolveInvestigation", ("resolved", false)
                     "closeInvestigation", ("closed", false); "reopenInvestigation", ("active", true)
                     "verificationFailed", ("active", true) ]
    match s m "command", s m "to" with
    | Some c, Some t ->
        let target, reasonIsText = table[c]
        [ if target <> t then $"command {c} requires to = \"{target}\" (got \"{t}\")"
          if reasonIsText && isNull m "reason" then $"command {c} requires a reason"
          if not reasonIsText && not (isNull m "reason") then $"command {c} requires reason = null"
          if c = "verificationFailed" && s m "from" <> Some "awaitingVerification" then
              "command verificationFailed requires from = \"awaitingVerification\"" ]
    | _ -> []

let private evidenceRules (m: Map<string, JsonValue>) =
    match s m "contentKind", s m "content" with
    | Some "artifact", Some c when not (Wire.isHttpUrl c) -> [ "contentKind artifact requires content to be an absolute http(s) URL" ]
    | _ -> []

let private verificationRules (m: Map<string, JsonValue>) =
    if s m "outcome" = Some "satisfied" && not (nonEmptyArray m "evidenceIds") then [ "outcome satisfied requires non-empty evidenceIds" ]
    else []

let causalMember = obj [ "hypothesisId", Uuid; "role", Enum "causeRole" ]
let members = ArrayOf(causalMember, true, 1)

// ---------------------------------------------------------------- §7 payloads

let payloads: Map<string, Ty> =
    Map.ofList
        [ "ActionRecorded", obj [ "actionId", Uuid; "kind", Enum "actionKind"; "purpose", Text; "targetHypothesisIds", UuidList ]
          "ActionTransitioned", obj [ "actionId", Uuid; "state", Enum "actionTarget" ]
          "AssumptionRecorded", obj [ "assumptionId", Uuid; "proposition", Text; "materiality", Enum "materiality" ]
          "AssumptionTransitioned", obj [ "assumptionId", Uuid; "state", Enum "assumptionTarget" ]
          "CausalAssertionConfirmed", obj [ "causeId", Uuid; "policy", Text ]
          "CausalAssertionRecorded", obj [ "causeId", Uuid; "outcome", Text; "members", members; "mechanism", NullableText ]
          "CausalAssertionReopened", obj [ "causeId", Uuid; "reason", Text ]
          "ConclusionRecorded",
          obj [ "conclusionId", Uuid; "statement", Text; "strength", Enum "conclusionStrength"; "causeIds", UuidList
                "residualUncertainty", NullableText ]
          "ContradictionRecorded",
          obj [ "contradictionId", Uuid; "statement", Text; "evidenceIds", UuidList; "materiality", Enum "materiality" ]
          "ContradictionTransitioned", obj [ "contradictionId", Uuid; "state", Enum "contradictionTarget"; "basis", Text ]
          "CorrectiveActionRecorded",
          obj [ "correctiveActionId", Uuid; "kind", Enum "correctiveActionKind"; "description", Text; "targetCauseIds", UuidList ]
          "CorrectiveActionTransitioned", obj [ "correctiveActionId", Uuid; "state", Enum "correctiveActionTarget" ]
          "CriterionRecorded", obj [ "criterionId", Uuid; "correctiveActionId", Uuid; "expectedOutcome", Text; "method", NullableText ]
          "EvidenceAdmitted",
          objWith evidenceRules
              [ "evidenceId", Uuid; "contentKind", Enum "contentKind"; "content", Text; "category", Enum "evidenceCategory"
                "source", Text; "capturedAt", NullableTimestamp ]
          "EvidenceRelated",
          obj [ "relationshipId", Uuid; "evidenceId", Uuid; "hypothesisId", Uuid; "kind", Enum "relationshipKind"
                "rationale", NullableText ]
          "EvidenceSuperseded", obj [ "evidenceId", Uuid; "replacementEvidenceId", Uuid; "reason", Text ]
          "EvidenceVerificationAssessed", obj [ "evidenceId", Uuid; "verification", Enum "verificationStatus" ]
          "FindingRecorded", obj [ "findingId", Uuid; "actionId", Uuid; "originalResult", Text; "interpretation", NullableText ]
          "HypothesisRecorded", obj [ "hypothesisId", Uuid; "statement", Text; "mechanism", NullableText ]
          "HypothesisTransitioned",
          objWith hypothesisRules
              [ "hypothesisId", Uuid; "from", Enum "hypothesisState"; "to", Enum "hypothesisTarget"
                "command", Enum "hypothesisCommand"; "reason", Text; "evidenceIds", UuidList ]
          "InvestigationStarted", obj [ "title", Text; "observationId", Uuid; "original", Text ]
          "LifecycleChanged",
          objWith lifecycleRules
              [ "from", Enum "lifecycle"; "to", Enum "lifecycleTarget"; "command", Enum "lifecycleCommand"; "reason", NullableText ]
          "NegativeKnowledgeEstablished",
          obj [ "negativeKnowledgeId", Uuid; "statement", Text; "scope", Text; "basisEvidenceIds", NonEmptyUuidList ]
          "ObservationRecorded", obj [ "observationId", Uuid; "original", Text; "observedAt", NullableTimestamp ]
          "PredictionRecorded",
          obj [ "predictionId", Uuid; "hypothesisId", Uuid; "statement", Text; "strength", Enum "predictionStrength"
                "timing", Enum "predictionTiming" ]
          "ProposalRecorded", obj [ "proposalId", Uuid; "kind", Enum "proposalKind"; "text", Text; "rationale", NullableText ]
          "ProposalWasAccepted", obj [ "proposalId", Uuid; "targetId", Uuid ]
          "ProposalWasRejected", obj [ "proposalId", Uuid; "reason", Text ]
          "UnknownIdentified", obj [ "unknownId", Uuid; "question", Text; "materiality", Enum "materiality" ]
          "UnknownTransitioned", obj [ "unknownId", Uuid; "state", Enum "unknownTarget"; "basis", Text ]
          "VerificationRecorded",
          objWith verificationRules [ "verificationId", Uuid; "criterionId", Uuid; "outcome", Enum "verificationOutcome"; "evidenceIds", UuidList ] ]

// ---------------------------------------------------------------- §5 envelope

let private envelopeSpec =
    obj [ "formatVersion", v100; "eventId", Uuid; "investigationId", Uuid; "aggregateVersion", aggregateVersion
          "eventType", Enum "eventType"; "occurredAt", Timestamp; "actor", Actor; "semanticVersion", v100
          "correlationId", Uuid; "payload", AnyObject ]

let validateEvent (path: string) (event: JsonValue) : string list =
    let envelope = validate path envelopeSpec event
    let payload =
        match str "eventType" event, field "payload" event with
        | Some t, Some p when payloads.ContainsKey t -> validate $"{path}.payload" payloads[t] p
        | _ -> []
    let agentRule =
        match field "actor" event |> Option.bind (str "kind"), str "eventType" event with
        | Some "agent", Some t when t <> "ProposalRecorded" -> [ $"{path}: an agent actor may appear only on ProposalRecorded (§5.3, D-004), not {t}" ]
        | _ -> []
    let startRule =
        match str "eventType" event, int "aggregateVersion" event with
        | Some "InvestigationStarted", Some v when v <> 1L -> [ $"{path}: InvestigationStarted must be aggregateVersion 1 (§5.1)" ]
        | _ -> []
    envelope @ payload @ agentRule @ startRule

// ---------------------------------------------------------------- §8 entities, state, snapshot

let private withProv fields = obj (fields @ [ "provenance", Provenance ])
let private withProvRules rules fields = objWith rules (fields @ [ "provenance", Provenance ])

let private hypothesisChange =
    objWith hypothesisRules
        [ "from", Enum "hypothesisState"; "to", Enum "hypothesisTarget"; "command", Enum "hypothesisCommand"; "reason", Text
          "evidenceIds", UuidList; "at", Timestamp; "aggregateVersion", aggregateVersion ]

let entities: (string * Ty) list =
    [ "observations", withProv [ "id", Uuid; "original", Text; "observedAt", NullableTimestamp ]
      "hypotheses",
      withProv [ "id", Uuid; "statement", Text; "mechanism", NullableText; "state", Enum "hypothesisState"
                 "history", ArrayOf(hypothesisChange, false, 0) ]
      "evidence",
      withProvRules evidenceRules
          [ "id", Uuid; "contentKind", Enum "contentKind"; "content", Text; "category", Enum "evidenceCategory"
            "source", Text; "capturedAt", NullableTimestamp; "verification", Enum "verificationStatus"
            "supersededBy", NullableUuid; "admittedAtVersion", aggregateVersion ]
      "relationships",
      withProv [ "id", Uuid; "evidenceId", Uuid; "hypothesisId", Uuid; "kind", Enum "relationshipKind"; "rationale", NullableText ]
      "unknowns",
      withProv [ "id", Uuid; "question", Text; "materiality", Enum "materiality"; "state", Enum "unknownState"; "basis", NullableText ]
      "assumptions", withProv [ "id", Uuid; "proposition", Text; "materiality", Enum "materiality"; "state", Enum "assumptionState" ]
      "contradictions",
      withProv [ "id", Uuid; "statement", Text; "evidenceIds", UuidList; "materiality", Enum "materiality"
                 "state", Enum "contradictionState"; "basis", NullableText ]
      "predictions",
      withProv [ "id", Uuid; "hypothesisId", Uuid; "statement", Text; "strength", Enum "predictionStrength"
                 "timing", Enum "predictionTiming" ]
      "actions",
      withProv [ "id", Uuid; "kind", Enum "actionKind"; "purpose", Text; "targetHypothesisIds", UuidList; "state", Enum "actionState" ]
      "findings", withProv [ "id", Uuid; "actionId", Uuid; "originalResult", Text; "interpretation", NullableText ]
      "negativeKnowledge", withProv [ "id", Uuid; "statement", Text; "scope", Text; "basisEvidenceIds", NonEmptyUuidList ]
      "causes", withProv [ "id", Uuid; "outcome", Text; "members", members; "mechanism", NullableText; "state", Enum "causeState" ]
      "correctiveActions",
      withProv [ "id", Uuid; "kind", Enum "correctiveActionKind"; "description", Text; "targetCauseIds", UuidList
                 "state", Enum "correctiveActionState"; "awaitingSinceVersion", versionOrZero ]
      "criteria", withProv [ "id", Uuid; "correctiveActionId", Uuid; "expectedOutcome", Text; "method", NullableText ]
      "verifications",
      withProvRules verificationRules
          [ "id", Uuid; "criterionId", Uuid; "outcome", Enum "verificationOutcome"; "evidenceIds", UuidList
            "recordedAtVersion", aggregateVersion ]
      "conclusions",
      withProv [ "id", Uuid; "statement", Text; "strength", Enum "conclusionStrength"; "causeIds", UuidList
                 "residualUncertainty", NullableText; "recordedAtVersion", aggregateVersion ]
      "proposals",
      withProv [ "id", Uuid; "kind", Enum "proposalKind"; "text", Text; "rationale", NullableText; "state", Enum "proposalState"
                 "decision", NullableText ] ]

let entityArrays = entities |> List.map fst

let state =
    obj (
        [ "investigationId", Uuid; "title", Text; "lifecycle", Enum "lifecycle"; "version", aggregateVersion; "createdAt", Timestamp ]
        @ (entities |> List.map (fun (name, ty) -> name, ArrayOf(ty, false, 0)))
    )

let snapshot =
    obj [ "formatVersion", v100; "investigationId", Uuid; "aggregateVersion", aggregateVersion; "projectionVersion", ConstInt 1L
          "semanticVersion", v100; "generatedAt", Timestamp; "state", state ]

// ---------------------------------------------------------------- §9.1, §4, §10.3, §12.2

let investigationManifest =
    obj [ "formatVersion", v100; "investigationId", Uuid; "schemaVersion", v100; "semanticVersion", v100
          "aggregateVersion", aggregateVersion; "eventCount", aggregateVersion; "eventsSha256", Sha256
          "snapshotVersion", ConstInt 1L; "snapshotSha256", Sha256; "domainPacks", EmptyArray; "createdAt", Timestamp
          "updatedAt", Timestamp ]

let rootManifest =
    obj [ "formatVersion", v100; "datasetId", ConstString "causa-demo-v1"; "createdAt", Timestamp; "schemaSha256", Sha256
          "investigations",
          ArrayOf(
              obj [ "investigationId", Uuid; "scenarioKey", Slug; "title", Text; "expectedVersion", aggregateVersion
                    "expectedLifecycle", Enum "lifecycle"; "expectedStateSha256", Sha256; "eventsSha256", Sha256
                    "manifestSha256", Sha256; "snapshotSha256", Sha256 ],
              false,
              1
          )
          "negativeFixtures",
          ArrayOf(
              obj [ "path", Pattern(Wire.ecmaRegex "^negative/[a-z0-9][a-z0-9-]*\\.json$"); "expectedErrorCode", Pattern(Wire.ecmaRegex "^CAUSA\\.[A-Z_]+$") ],
              false,
              0
          ) ]

let schemaBundle =
    obj [ "formatVersion", v100; "contractVersion", ConstString "1.0.0"; "algorithm", ConstString "sha256"
          "canonicalization", ConstString "RFC8785"
          "schemas", ArrayOf(obj [ "path", Pattern(Wire.ecmaRegex "^[a-z0-9][A-Za-z0-9/._-]*\\.schema\\.json$"); "sha256", Sha256 ], false, 1) ]

let private b64 = obj [ "base64", Pattern(Wire.ecmaRegex "^[A-Za-z0-9+/]*={0,2}$") ]
/// Directory entries by file name; any names (§3.3: missing and extra files are representable).
let private fileSet = MapOf b64
let private domainCode = NullOr(Pattern(Wire.ecmaRegex "^[A-Z]+(\\.[A-Z_]+)+$"))

let private negativeCommon =
    [ "formatVersion", v100; "name", Slug; "description", Text; "expectedErrorCode", Pattern(Wire.ecmaRegex "^CAUSA\\.[A-Z_]+$")
      "expectedDomainCode", domainCode ]

let validateNegativeFixture (path: string) (v: JsonValue) : string list =
    let shape extra = validate path (obj (negativeCommon @ extra)) v
    match str "kind" v with
    | Some "investigation" -> shape [ "kind", ConstString "investigation"; "directoryId", Pattern(Wire.ecmaRegex "^[\\s\\S]+$"); "files", fileSet ]
    | Some "append" ->
        shape [ "kind", ConstString "append"; "baseScenarioKey", Slug; "expectedAggregateVersion", versionOrZero
                "appendedEvents", b64 ]
    | Some "successor" -> shape [ "kind", ConstString "successor"; "baseScenarioKey", Slug; "files", fileSet ]
    | _ -> [ $"{path}.kind: must be investigation, append or successor" ]

// ---------------------------------------------------------------- golden/scenarios.json (golden-scenarios.schema.json)

let goldenScenarios =
    obj [ "formatVersion", v100
          "scenarios",
          ArrayOf(
              obj [ "scenarioKey", Slug; "investigationId", Uuid; "commands", IntRange(1L, System.Int64.MaxValue)
                    "events", aggregateVersion; "eventTypes", ArrayOf(Pattern(Wire.ecmaRegex "^[\\s\\S]*$"), true, 0)
                    "refusals",
                    ArrayOf(
                        obj [ "step", versionOrZero; "expectedCode", Pattern(Wire.ecmaRegex "^[A-Z]+(\\.[A-Z_]+)+$")
                              "engineCodes", Pattern(Wire.ecmaRegex "^[\\s\\S]*$") ],
                        false,
                        0
                    ) ],
              false,
              1
          ) ]
