namespace Causa.RehearsalAdapter

open System
open System.Globalization
open Causa.Contract

/// Translation between the Causa v1.0.0 wire format and the rehearsal's own persisted
/// format `rci.events/v1` (rehearsal Codec.fs, unmodified). This adapter is separate from the
/// rehearsal; it exists because the rehearsal cannot read v1.0.0 directly (CROSS-RUN-COMPATIBILITY.md).
module Translate =
    /// v1 lowerCamel enum values -> rehearsal CaseNames strings, per enumeration.
    let private enums : Map<string, (string * string) list> =
        Map.ofList
            [ "lifecycle", [ "draft", "Draft"; "active", "Active"; "awaitingEvidence", "AwaitingEvidence"; "awaitingVerification", "AwaitingVerification"; "resolved", "Resolved"; "closed", "Closed"; "archived", "Archived" ]
              "hypothesisState", [ "proposed", "Proposed"; "plausible", "Plausible"; "supported", "Supported"; "weakened", "Weakened"; "falsified", "Falsified"; "superseded", "Superseded" ]
              "unknownState", [ "identified", "Identified"; "investigating", "Investigating"; "resolved", "UnknownResolved"; "acceptedResidual", "AcceptedResidual"; "superseded", "UnknownSuperseded" ]
              "materiality", [ "blocking", "Blocking"; "material", "Material"; "nonMaterial", "NonMaterial"; "informational", "Informational" ]
              "assumptionState", [ "explicitUnverified", "ExplicitUnverified"; "provisionallyAccepted", "ProvisionallyAccepted"; "verified", "AssumptionVerified"; "rejected", "Rejected"; "expired", "Expired" ]
              "contradictionState", [ "detected", "Detected"; "triaged", "Triaged"; "investigating", "ContradictionInvestigating"; "resolved", "ContradictionResolved"; "acceptedResidual", "ContradictionAcceptedResidual"; "superseded", "ContradictionSuperseded" ]
              "actionState", [ "proposed", "ActionProposed"; "approved", "ActionApproved"; "inProgress", "ActionInProgress"; "completed", "ActionCompleted"; "interpreted", "ActionInterpreted"; "rejected", "ActionRejected"; "cancelled", "ActionCancelled" ]
              "actionKind", [ "test", "Test"; "inspection", "Inspection"; "query", "Query"; "comparison", "Comparison"; "experiment", "Experiment" ]
              "causeRole", [ "rootCause", "RootCause"; "contributingFactor", "ContributingFactor"; "trigger", "Trigger"; "enablingCondition", "EnablingCondition" ]
              "correctiveActionState", [ "proposed", "CorrectionProposed"; "approved", "CorrectionApproved"; "inProgress", "CorrectionInProgress"; "implemented", "Implemented"; "awaitingVerificationOfEffect", "AwaitingVerificationOfEffect"; "verifiedEffective", "VerifiedEffective"; "verificationFailed", "VerificationFailed"; "reworkRequired", "ReworkRequired"; "cancelled", "CorrectionCancelled" ]
              "correctiveActionKind", [ "mitigation", "Mitigation"; "workaround", "Workaround"; "correction", "Correction"; "prevention", "Prevention" ]
              "evidenceCategory", [ "directObservation", "DirectObservation"; "measurement", "Measurement"; "log", "Log"; "document", "Document"; "experimentResult", "ExperimentResult"; "testimony", "Testimony"; "externalReference", "ExternalReference" ]
              "verificationStatus", [ "unverified", "Unverified"; "partiallyVerified", "PartiallyVerified"; "verified", "Verified"; "invalidated", "Invalidated" ]
              "relationshipKind", [ "supports", "Supports"; "contradicts", "Contradicts"; "discriminates", "Discriminates"; "insufficient", "Insufficient" ]
              "predictionStrength", [ "necessary", "Necessary"; "expected", "Expected"; "discriminating", "DiscriminatingPrediction"; "nonDiscriminating", "NonDiscriminating" ]
              "predictionTiming", [ "prospective", "Prospective"; "retrospective", "Retrospective" ]
              "verificationOutcome", [ "satisfied", "Satisfied"; "failed", "Failed"; "inconclusive", "Inconclusive" ]
              "conclusionStrength", [ "plausibleExplanation", "PlausibleExplanation"; "leadingExplanation", "LeadingExplanation"; "supportedCause", "SupportedCause"; "confirmedCause", "ConfirmedCause"; "acceptedWithResidualUncertainty", "AcceptedWithResidualUncertainty" ]
              "proposalKind", [ "hypothesis", "ProposedHypothesis"; "unknown", "ProposedUnknown"; "action", "ProposedAction" ]
              "hypothesisCommand", [ "markPlausible", "MarkPlausible"; "markSupported", "MarkSupported"; "markWeakened", "MarkWeakened"; "falsify", "Falsify"; "supersede", "Supersede"; "reopen", "Reopen" ]
              "lifecycleCommand", [ "activateInvestigation", "ActivateInvestigation"; "markAwaitingEvidence", "MarkAwaitingEvidence"; "markAwaitingVerification", "MarkAwaitingVerification"; "resolveInvestigation", "ResolveInvestigation"; "closeInvestigation", "CloseInvestigation"; "reopenInvestigation", "ReopenInvestigation"; "verificationFailed", "VerificationFailed" ] ]

    type FieldRule =
        | Same of v1: string * rci: string
        | Enum of v1: string * rci: string * enumName: string
        | Time of v1: string * rci: string

    /// Per event type: how each v1 payload field maps to the rehearsal `data` field.
    let rules : Map<string, FieldRule list> =
        Map.ofList
            [ "InvestigationStarted", [ Same("title", "title"); Same("observationId", "observation"); Same("original", "original") ]
              "LifecycleChanged", [ Enum("from", "from", "lifecycle"); Enum("to", "to", "lifecycle"); Enum("command", "command", "lifecycleCommand"); Same("reason", "reason") ]
              "ObservationRecorded", [ Same("observationId", "observation"); Same("original", "original"); Time("observedAt", "observedAt") ]
              "HypothesisRecorded", [ Same("hypothesisId", "hypothesis"); Same("statement", "statement"); Same("mechanism", "mechanism") ]
              "HypothesisTransitioned", [ Same("hypothesisId", "hypothesis"); Enum("from", "from", "hypothesisState"); Enum("to", "to", "hypothesisState"); Enum("command", "command", "hypothesisCommand"); Same("reason", "reason"); Same("evidenceIds", "evidence") ]
              "EvidenceAdmitted", [ Same("evidenceId", "evidence"); Enum("category", "category", "evidenceCategory"); Same("source", "source"); Time("capturedAt", "capturedAt") ]
              "EvidenceSuperseded", [ Same("evidenceId", "old"); Same("replacementEvidenceId", "replacement"); Same("reason", "reason") ]
              "EvidenceVerificationAssessed", [ Same("evidenceId", "evidence"); Enum("verification", "verification", "verificationStatus") ]
              "EvidenceRelated", [ Same("relationshipId", "relationship"); Same("evidenceId", "evidence"); Same("hypothesisId", "hypothesis"); Enum("kind", "kind", "relationshipKind"); Same("rationale", "rationale") ]
              "UnknownIdentified", [ Same("unknownId", "unknown"); Same("question", "question"); Enum("materiality", "materiality", "materiality") ]
              "UnknownTransitioned", [ Same("unknownId", "unknown"); Enum("state", "state", "unknownState"); Same("basis", "basis") ]
              "AssumptionRecorded", [ Same("assumptionId", "assumption"); Same("proposition", "proposition"); Enum("materiality", "materiality", "materiality") ]
              "AssumptionTransitioned", [ Same("assumptionId", "assumption"); Enum("state", "state", "assumptionState") ]
              "ContradictionRecorded", [ Same("contradictionId", "contradiction"); Same("statement", "statement"); Same("evidenceIds", "evidence"); Enum("materiality", "materiality", "materiality") ]
              "ContradictionTransitioned", [ Same("contradictionId", "contradiction"); Enum("state", "state", "contradictionState"); Same("basis", "basis") ]
              "PredictionRecorded", [ Same("predictionId", "prediction"); Same("hypothesisId", "hypothesis"); Same("statement", "statement"); Enum("strength", "strength", "predictionStrength"); Enum("timing", "timing", "predictionTiming") ]
              "ActionRecorded", [ Same("actionId", "action"); Enum("kind", "kind", "actionKind"); Same("purpose", "purpose"); Same("targetHypothesisIds", "targets") ]
              "ActionTransitioned", [ Same("actionId", "action"); Enum("state", "state", "actionState") ]
              "FindingRecorded", [ Same("findingId", "finding"); Same("actionId", "action"); Same("originalResult", "originalResult"); Same("interpretation", "interpretation") ]
              "NegativeKnowledgeEstablished", [ Same("negativeKnowledgeId", "negativeKnowledge"); Same("statement", "statement"); Same("scope", "scope"); Same("basisEvidenceIds", "basis") ]
              "CausalAssertionRecorded", [ Same("causeId", "cause"); Same("outcome", "outcome"); Same("mechanism", "mechanism") ]
              "CausalAssertionConfirmed", [ Same("causeId", "cause"); Same("policy", "policy") ]
              "CausalAssertionReopened", [ Same("causeId", "cause"); Same("reason", "reason") ]
              "CorrectiveActionRecorded", [ Same("correctiveActionId", "correctiveAction"); Enum("kind", "kind", "correctiveActionKind"); Same("description", "description"); Same("targetCauseIds", "targets") ]
              "CorrectiveActionTransitioned", [ Same("correctiveActionId", "correctiveAction"); Enum("state", "state", "correctiveActionState") ]
              "CriterionRecorded", [ Same("criterionId", "criterion"); Same("correctiveActionId", "correctiveAction"); Same("expectedOutcome", "expectedOutcome"); Same("method", "method") ]
              "VerificationRecorded", [ Same("verificationId", "verification"); Same("criterionId", "criterion"); Enum("outcome", "outcome", "verificationOutcome"); Same("evidenceIds", "evidence") ]
              "ConclusionRecorded", [ Same("conclusionId", "conclusion"); Same("statement", "statement"); Enum("strength", "strength", "conclusionStrength"); Same("causeIds", "causes"); Same("residualUncertainty", "residualUncertainty") ]
              "ProposalRecorded", [ Same("proposalId", "proposal"); Enum("kind", "kind", "proposalKind"); Same("text", "text"); Same("rationale", "rationale") ]
              "ProposalWasAccepted", [ Same("proposalId", "proposal"); Same("targetId", "target") ]
              "ProposalWasRejected", [ Same("proposalId", "proposal"); Same("reason", "reason") ] ]

    /// The rehearsal decoder parses timestamps with the round-trip "O" format only.
    let toRoundTrip (wire: string) =
        DateTimeOffset.ParseExact(wire, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal ||| DateTimeStyles.AdjustToUniversal)
            .ToOffset(TimeSpan.Zero).ToString("O", CultureInfo.InvariantCulture)

    let fromRoundTrip (rt: string) =
        DateTimeOffset.ParseExact(rt, "O", CultureInfo.InvariantCulture, DateTimeStyles.None).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)

    let private field k (o: (string * JValue) list) = Json.field k o |> Option.defaultValue JNull
    let private obj (v: JValue) = match v with JObj o -> o | _ -> []
    let private str (v: JValue) = match v with JStr s -> s | _ -> ""

    let private enumTo (name: string) (v: JValue) =
        match v with
        | JStr s -> enums[name] |> List.tryFind (fst >> (=) s) |> Option.map (snd >> JStr) |> Option.defaultWith (fun () -> failwithf "no rehearsal value for %s '%s'" name s)
        | other -> other

    let private enumFrom (name: string) (v: JValue) =
        match v with
        | JStr s -> enums[name] |> List.tryFind (snd >> (=) s) |> Option.map (fst >> JStr) |> Option.defaultWith (fun () -> failwithf "no v1 value for %s '%s'" name s)
        | other -> other

    let private timeTo (v: JValue) = match v with JStr s -> JStr(toRoundTrip s) | other -> other
    let private timeFrom (v: JValue) = match v with JStr s -> JStr(fromRoundTrip s) | other -> other

    /// One v1 event line -> one rehearsal event node.
    let eventToRehearsal (v: JValue) : JValue =
        let e = obj v
        let t = str (field "eventType" e)
        let p = obj (field "payload" e)
        let mapped =
            rules[t]
            |> List.map (function
                | Same(a, b) -> b, field a p
                | Enum(a, b, n) -> b, enumTo n (field a p)
                | Time(a, b) -> b, timeTo (field a p))
        let extra =
            match t with
            | "EvidenceAdmitted" ->
                let kind = str (field "contentKind" p)
                [ "content", (if kind = "text" then JObj [ "kind", JStr "text"; "text", field "content" p ] else JObj [ "kind", JStr "artifact"; "uri", field "content" p ]) ]
            | "CausalAssertionRecorded" ->
                let ms = match field "members" p with JArr xs -> xs | _ -> []
                [ "members", JArr(ms |> List.map (fun m -> JObj [ "hypothesis", field "hypothesisId" (obj m); "role", enumTo "causeRole" (field "role" (obj m)) ])) ]
            | "HypothesisTransitioned" -> [ "at", timeTo (field "occurredAt" e); "sequence", field "aggregateVersion" e ]
            | _ -> []
        let actor = obj (field "actor" e)
        let rciActor =
            JObj([ "id", field "actorId" actor; "kind", field "kind" actor ]
                 @ (if str (field "kind" actor) = "agent" then [ "provider", field "provider" actor; "model", field "model" actor ] else []))
        JObj
            [ "seq", field "aggregateVersion" e
              "commandId", field "correlationId" e
              "actor", rciActor
              "at", timeTo (field "occurredAt" e)
              "type", JStr t
              "data", JObj(mapped @ extra) ]

    /// The rehearsal's document form for a whole history.
    let documentToRehearsal (investigationId: string) (lines: JValue list) : string =
        JObj [ "schema", JStr "rci.events/v1"; "investigation", JStr investigationId; "events", JArr(lines |> List.map eventToRehearsal) ]
        |> Json.canonical
        |> Result.defaultWith failwith

    /// One rehearsal event node (as produced by the rehearsal's EventCodec.encode) -> v1 line.
    let eventFromRehearsal (investigationId: string) (v: JValue) : JValue =
        let e = obj v
        let t = str (field "type" e)
        let d = obj (field "data" e)
        let mapped =
            rules[t]
            |> List.map (function
                | Same(a, b) -> a, field b d
                | Enum(a, b, n) -> a, enumFrom n (field b d)
                | Time(a, b) -> a, timeFrom (field b d))
        let extra =
            match t with
            | "EvidenceAdmitted" ->
                let c = obj (field "content" d)
                let kind = str (field "kind" c)
                [ "contentKind", JStr kind; "content", (if kind = "text" then field "text" c else field "uri" c) ]
            | "CausalAssertionRecorded" ->
                let ms = match field "members" d with JArr xs -> xs | _ -> []
                [ "members", JArr(ms |> List.map (fun m -> JObj [ "hypothesisId", field "hypothesis" (obj m); "role", enumFrom "causeRole" (field "role" (obj m)) ])) ]
            | _ -> []
        let a = obj (field "actor" e)
        let actor =
            JObj([ "actorId", field "id" a; "kind", field "kind" a ]
                 @ (if str (field "kind" a) = "agent" then [ "provider", field "provider" a; "model", field "model" a ] else []))
        let version = match field "seq" e with JInt n -> n | _ -> 0L
        let corr = str (field "commandId" e)
        JObj
            [ "formatVersion", JStr "1.0.0"
              "eventId", JStr(Uuid.text (Uuid.eventId (Guid.Parse investigationId) version (Guid.Parse corr)))
              "investigationId", JStr investigationId
              "aggregateVersion", JInt version
              "eventType", JStr t
              "occurredAt", timeFrom (field "at" e)
              "actor", actor
              "semanticVersion", JStr "1.0.0"
              "correlationId", JStr corr
              "payload", JObj(mapped @ extra) ]
