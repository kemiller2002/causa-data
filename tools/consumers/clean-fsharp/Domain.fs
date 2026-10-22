/// Wire <-> engine mapping, inferred from CONTRACT §5–§7 and the engine's EventBody/Model types.
/// Decoding assumes the event already passed structural validation (Schema.validateEvent).
module CleanConsumer.Domain

open System
open IndyInitRootCauseInvestigator.Engine
open CleanConsumer.Json

// ---------------------------------------------------------------- bidirectional enum tables (§6)

type private Table<'T> = (string * 'T) list

let private toEngine (name: string) (table: Table<'T>) (wire: string) : Result<'T, string> =
    match table |> List.tryFind (fun (w, _) -> w = wire) with
    | Some(_, e) -> Ok e
    | None -> Error $"{name}: no engine value for wire \"{wire}\""

let private toWire (name: string) (table: Table<'T>) (value: 'T) : Result<string, string> =
    match table |> List.tryFind (fun (_, e) -> e = value) with
    | Some(w, _) -> Ok w
    | None -> Error $"{name}: engine value {value} has no v1 wire form (CAUSA.UNREPRESENTABLE)"

let lifecycleT: Table<InvestigationLifecycle> =
    [ "draft", InvestigationLifecycle.Draft; "active", InvestigationLifecycle.Active
      "awaitingEvidence", InvestigationLifecycle.AwaitingEvidence
      "awaitingVerification", InvestigationLifecycle.AwaitingVerification; "resolved", InvestigationLifecycle.Resolved
      "closed", InvestigationLifecycle.Closed; "archived", InvestigationLifecycle.Archived ]

let hypothesisStateT: Table<HypothesisState> =
    [ "proposed", HypothesisState.Proposed; "plausible", HypothesisState.Plausible; "supported", HypothesisState.Supported
      "weakened", HypothesisState.Weakened; "falsified", HypothesisState.Falsified; "superseded", HypothesisState.Superseded ]

let unknownStateT: Table<UnknownState> =
    [ "identified", UnknownState.Identified; "investigating", UnknownState.Investigating
      "resolved", UnknownState.UnknownResolved; "acceptedResidual", UnknownState.AcceptedResidual
      "superseded", UnknownState.UnknownSuperseded ]

let materialityT: Table<Materiality> =
    [ "blocking", Materiality.Blocking; "material", Materiality.Material; "nonMaterial", Materiality.NonMaterial
      "informational", Materiality.Informational ]

let assumptionStateT: Table<AssumptionState> =
    [ "explicitUnverified", AssumptionState.ExplicitUnverified; "provisionallyAccepted", AssumptionState.ProvisionallyAccepted
      "verified", AssumptionState.AssumptionVerified; "rejected", AssumptionState.Rejected; "expired", AssumptionState.Expired ]

let contradictionStateT: Table<ContradictionState> =
    [ "detected", ContradictionState.Detected; "triaged", ContradictionState.Triaged
      "investigating", ContradictionState.ContradictionInvestigating; "resolved", ContradictionState.ContradictionResolved
      "acceptedResidual", ContradictionState.ContradictionAcceptedResidual
      "superseded", ContradictionState.ContradictionSuperseded ]

let actionStateT: Table<ActionState> =
    [ "proposed", ActionState.ActionProposed; "approved", ActionState.ActionApproved; "inProgress", ActionState.ActionInProgress
      "completed", ActionState.ActionCompleted; "interpreted", ActionState.ActionInterpreted
      "rejected", ActionState.ActionRejected; "cancelled", ActionState.ActionCancelled ]

let actionKindT: Table<ActionKind> =
    [ "test", ActionKind.Test; "inspection", ActionKind.Inspection; "query", ActionKind.Query
      "comparison", ActionKind.Comparison; "experiment", ActionKind.Experiment ]

let causeRoleT: Table<CauseRole> =
    [ "rootCause", CauseRole.RootCause; "contributingFactor", CauseRole.ContributingFactor; "trigger", CauseRole.Trigger
      "enablingCondition", CauseRole.EnablingCondition ]

let correctiveStateT: Table<CorrectiveActionState> =
    [ "proposed", CorrectiveActionState.CorrectionProposed; "approved", CorrectiveActionState.CorrectionApproved
      "inProgress", CorrectiveActionState.CorrectionInProgress; "implemented", CorrectiveActionState.Implemented
      "awaitingVerificationOfEffect", CorrectiveActionState.AwaitingVerificationOfEffect
      "verifiedEffective", CorrectiveActionState.VerifiedEffective
      "verificationFailed", CorrectiveActionState.VerificationFailed
      "reworkRequired", CorrectiveActionState.ReworkRequired; "cancelled", CorrectiveActionState.CorrectionCancelled ]

let correctiveKindT: Table<CorrectiveActionKind> =
    [ "mitigation", CorrectiveActionKind.Mitigation; "workaround", CorrectiveActionKind.Workaround
      "correction", CorrectiveActionKind.Correction; "prevention", CorrectiveActionKind.Prevention ]

let categoryT: Table<EvidenceCategory> =
    [ "directObservation", EvidenceCategory.DirectObservation; "measurement", EvidenceCategory.Measurement
      "log", EvidenceCategory.Log; "document", EvidenceCategory.Document
      "experimentResult", EvidenceCategory.ExperimentResult; "testimony", EvidenceCategory.Testimony
      "externalReference", EvidenceCategory.ExternalReference ]

let verificationStatusT: Table<VerificationStatus> =
    [ "unverified", VerificationStatus.Unverified; "partiallyVerified", VerificationStatus.PartiallyVerified
      "verified", VerificationStatus.Verified; "invalidated", VerificationStatus.Invalidated ]

let relationshipKindT: Table<RelationshipKind> =
    [ "supports", RelationshipKind.Supports; "contradicts", RelationshipKind.Contradicts
      "discriminates", RelationshipKind.Discriminates; "insufficient", RelationshipKind.Insufficient ]

let predictionStrengthT: Table<PredictionStrength> =
    [ "necessary", PredictionStrength.Necessary; "expected", PredictionStrength.Expected
      "discriminating", PredictionStrength.DiscriminatingPrediction; "nonDiscriminating", PredictionStrength.NonDiscriminating ]

let predictionTimingT: Table<PredictionTiming> =
    [ "prospective", PredictionTiming.Prospective; "retrospective", PredictionTiming.Retrospective ]

let verificationOutcomeT: Table<VerificationOutcome> =
    [ "satisfied", VerificationOutcome.Satisfied; "failed", VerificationOutcome.Failed
      "inconclusive", VerificationOutcome.Inconclusive ]

let conclusionStrengthT: Table<ConclusionStrength> =
    [ "plausibleExplanation", ConclusionStrength.PlausibleExplanation; "leadingExplanation", ConclusionStrength.LeadingExplanation
      "supportedCause", ConclusionStrength.SupportedCause; "confirmedCause", ConclusionStrength.ConfirmedCause
      "acceptedWithResidualUncertainty", ConclusionStrength.AcceptedWithResidualUncertainty ]

let proposalKindT: Table<ProposalKind> =
    [ "hypothesis", ProposalKind.ProposedHypothesis; "unknown", ProposalKind.ProposedUnknown
      "action", ProposalKind.ProposedAction ]

/// The engine records hypothesis commands as `string HypothesisCommand` (Decide.hypothesisBody).
let hypothesisCommandT: Table<string> =
    [ "markPlausible", "MarkPlausible"; "markSupported", "MarkSupported"; "markWeakened", "MarkWeakened"
      "falsify", "Falsify"; "supersede", "Supersede"; "reopen", "Reopen" ]

/// The engine records lifecycle commands as literal names (Decide.lifecycleCommand; VerificationFailed follow-on).
let lifecycleCommandT: Table<string> =
    [ "activateInvestigation", "ActivateInvestigation"; "markAwaitingEvidence", "MarkAwaitingEvidence"
      "markAwaitingVerification", "MarkAwaitingVerification"; "resolveInvestigation", "ResolveInvestigation"
      "closeInvestigation", "CloseInvestigation"; "reopenInvestigation", "ReopenInvestigation"
      "verificationFailed", "VerificationFailed" ]

// ---------------------------------------------------------------- result helpers

type ResultBuilder() =
    member _.Bind(r, f) = Result.bind f r
    member _.Return v = Ok v
    member _.ReturnFrom r = r

let result = ResultBuilder()

let traverse (f: 'a -> Result<'b, 'e>) (xs: 'a list) : Result<'b list, 'e> =
    List.foldBack (fun x acc -> result { let! y = f x in let! ys = acc in return y :: ys }) xs (Ok [])

// ---------------------------------------------------------------- decoding (wire -> engine)

let private need (name: string) (o: JsonValue) : Result<JsonValue, string> =
    field name o |> Option.map Ok |> Option.defaultValue (Error $"missing field {name}")

let private reqStr name o =
    match field name o with
    | Some(JString s) -> Ok s
    | _ -> Error $"{name}: expected string"

let private optStr name o =
    match field name o with
    | Some(JString s) -> Ok(Some s)
    | Some JNull -> Ok None
    | _ -> Error $"{name}: expected string or null"

let private guid name o : Result<Guid, string> = reqStr name o |> Result.map Guid.Parse
let private id name o : Result<Id<'T>, string> = guid name o |> Result.map Id
let private text name o = reqStr name o |> Result.bind (fun s -> NonEmptyText.create s |> Result.mapError (fun e -> $"{name}: {e}"))

let private optText name o =
    optStr name o
    |> Result.bind (function
        | None -> Ok None
        | Some s -> NonEmptyText.create s |> Result.map Some |> Result.mapError (fun e -> $"{name}: {e}"))

let private ids name o : Result<Id<'T> list, string> =
    match field name o with
    | Some(JArray xs) ->
        xs |> traverse (function JString s -> Ok(Id(Guid.Parse s)) | _ -> Error $"{name}: expected uuid")
    | _ -> Error $"{name}: expected array"

let private enumOf name table o = reqStr name o |> Result.bind (toEngine name table)

let private timeOf (s: string) = Wire.parseTimestamp s
let private optTime name o = optStr name o |> Result.bind (function None -> Ok None | Some s -> timeOf s |> Result.map Some)

let decodeActor (a: JsonValue) : Result<Actor, string> =
    result {
        let! actorId = reqStr "actorId" a
        let! kind = reqStr "kind" a
        match kind with
        | "human" -> return { Id = actorId; Kind = ActorKind.Human }
        | "system" -> return { Id = actorId; Kind = ActorKind.System }
        | "agent" ->
            let! provider = reqStr "provider" a
            let! model = reqStr "model" a
            return { Id = actorId; Kind = ActorKind.Agent(provider, model) }
        | other -> return! Error $"actor kind {other}"
    }

let private decodeBody (eventType: string) (p: JsonValue) (at: DateTimeOffset) (sequence: int64) : Result<EventBody, string> =
    match eventType with
    | "InvestigationStarted" ->
        result { let! t = text "title" p in let! o = id "observationId" p in let! orig = text "original" p in return InvestigationStarted(t, o, orig) }
    | "LifecycleChanged" ->
        result {
            let! f = enumOf "from" lifecycleT p
            let! t = enumOf "to" lifecycleT p
            let! c = enumOf "command" lifecycleCommandT p
            let! r = optStr "reason" p
            return LifecycleChanged(f, t, c, r)
        }
    | "ObservationRecorded" ->
        result { let! o = id "observationId" p in let! t = text "original" p in let! at = optTime "observedAt" p in return ObservationRecorded(o, t, at) }
    | "HypothesisRecorded" ->
        result { let! h = id "hypothesisId" p in let! s = text "statement" p in let! m = optText "mechanism" p in return HypothesisRecorded(h, s, m) }
    | "HypothesisTransitioned" ->
        result {
            let! h = id "hypothesisId" p
            let! f = enumOf "from" hypothesisStateT p
            let! t = enumOf "to" hypothesisStateT p
            let! c = enumOf "command" hypothesisCommandT p
            let! r = text "reason" p
            let! ev = ids "evidenceIds" p
            // §7.2: At and Sequence are reconstructed from the envelope.
            return HypothesisTransitioned(h, { From = f; To = t; Command = c; Reason = r; Evidence = ev; At = at; Sequence = sequence })
        }
    | "EvidenceAdmitted" ->
        result {
            let! e = id "evidenceId" p
            let! kind = reqStr "contentKind" p
            let! content =
                match kind with
                | "text" -> text "content" p |> Result.map TextEvidence
                | "artifact" -> reqStr "content" p |> Result.map ArtifactEvidence
                | k -> Error $"contentKind {k}"
            let! cat = enumOf "category" categoryT p
            let! src = text "source" p
            let! cap = optTime "capturedAt" p
            return EvidenceAdmitted(e, content, cat, src, cap)
        }
    | "EvidenceSuperseded" ->
        result { let! o = id "evidenceId" p in let! r = id "replacementEvidenceId" p in let! why = text "reason" p in return EvidenceSuperseded(o, r, why) }
    | "EvidenceVerificationAssessed" ->
        result { let! e = id "evidenceId" p in let! v = enumOf "verification" verificationStatusT p in return EvidenceVerificationAssessed(e, v) }
    | "EvidenceRelated" ->
        result {
            let! r = id "relationshipId" p
            let! e = id "evidenceId" p
            let! h = id "hypothesisId" p
            let! k = enumOf "kind" relationshipKindT p
            let! why = optText "rationale" p
            return EvidenceRelated(r, e, h, k, why)
        }
    | "UnknownIdentified" ->
        result { let! u = id "unknownId" p in let! q = text "question" p in let! m = enumOf "materiality" materialityT p in return UnknownIdentified(u, q, m) }
    | "UnknownTransitioned" ->
        result { let! u = id "unknownId" p in let! s = enumOf "state" unknownStateT p in let! b = text "basis" p in return UnknownTransitioned(u, s, b) }
    | "AssumptionRecorded" ->
        result { let! a = id "assumptionId" p in let! pr = text "proposition" p in let! m = enumOf "materiality" materialityT p in return AssumptionRecorded(a, pr, m) }
    | "AssumptionTransitioned" ->
        result { let! a = id "assumptionId" p in let! s = enumOf "state" assumptionStateT p in return AssumptionTransitioned(a, s) }
    | "ContradictionRecorded" ->
        result {
            let! c = id "contradictionId" p
            let! s = text "statement" p
            let! ev = ids "evidenceIds" p
            let! m = enumOf "materiality" materialityT p
            return ContradictionRecorded(c, s, ev, m)
        }
    | "ContradictionTransitioned" ->
        result { let! c = id "contradictionId" p in let! s = enumOf "state" contradictionStateT p in let! b = text "basis" p in return ContradictionTransitioned(c, s, b) }
    | "PredictionRecorded" ->
        result {
            let! pr = id "predictionId" p
            let! h = id "hypothesisId" p
            let! s = text "statement" p
            let! st = enumOf "strength" predictionStrengthT p
            let! t = enumOf "timing" predictionTimingT p
            return PredictionRecorded(pr, h, s, st, t)
        }
    | "ActionRecorded" ->
        result {
            let! a = id "actionId" p
            let! k = enumOf "kind" actionKindT p
            let! pu = text "purpose" p
            let! t = ids "targetHypothesisIds" p
            return ActionRecorded(a, k, pu, t)
        }
    | "ActionTransitioned" ->
        result { let! a = id "actionId" p in let! s = enumOf "state" actionStateT p in return ActionTransitioned(a, s) }
    | "FindingRecorded" ->
        result {
            let! f = id "findingId" p
            let! a = id "actionId" p
            let! r = text "originalResult" p
            let! i = optText "interpretation" p
            return FindingRecorded(f, a, r, i)
        }
    | "NegativeKnowledgeEstablished" ->
        result {
            let! n = id "negativeKnowledgeId" p
            let! s = text "statement" p
            let! sc = text "scope" p
            let! b = ids "basisEvidenceIds" p
            return NegativeKnowledgeEstablished(n, s, sc, b)
        }
    | "CausalAssertionRecorded" ->
        result {
            let! c = id "causeId" p
            let! o = text "outcome" p
            let! ms = need "members" p
            let! members =
                match ms with
                | JArray xs ->
                    xs |> traverse (fun m -> result { let! h = id "hypothesisId" m in let! r = enumOf "role" causeRoleT m in return { Hypothesis = h; Role = r } })
                | _ -> Error "members: expected array"
            let! mech = optText "mechanism" p
            return CausalAssertionRecorded(c, o, members, mech)
        }
    | "CausalAssertionConfirmed" ->
        result { let! c = id "causeId" p in let! pol = reqStr "policy" p in return CausalAssertionConfirmed(c, pol) }
    | "CausalAssertionReopened" ->
        result { let! c = id "causeId" p in let! r = text "reason" p in return CausalAssertionReopened(c, r) }
    | "CorrectiveActionRecorded" ->
        result {
            let! c = id "correctiveActionId" p
            let! k = enumOf "kind" correctiveKindT p
            let! d = text "description" p
            let! t = ids "targetCauseIds" p
            return CorrectiveActionRecorded(c, k, d, t)
        }
    | "CorrectiveActionTransitioned" ->
        result { let! c = id "correctiveActionId" p in let! s = enumOf "state" correctiveStateT p in return CorrectiveActionTransitioned(c, s) }
    | "CriterionRecorded" ->
        result {
            let! c = id "criterionId" p
            let! ca = id "correctiveActionId" p
            let! e = text "expectedOutcome" p
            let! m = optText "method" p
            return CriterionRecorded(c, ca, e, m)
        }
    | "VerificationRecorded" ->
        result {
            let! v = id "verificationId" p
            let! c = id "criterionId" p
            let! o = enumOf "outcome" verificationOutcomeT p
            let! ev = ids "evidenceIds" p
            return VerificationRecorded(v, c, o, ev)
        }
    | "ConclusionRecorded" ->
        result {
            let! c = id "conclusionId" p
            let! s = text "statement" p
            let! st = enumOf "strength" conclusionStrengthT p
            let! causes = ids "causeIds" p
            let! r = optText "residualUncertainty" p
            return ConclusionRecorded(c, s, st, causes, r)
        }
    | "ProposalRecorded" ->
        result {
            let! pr = id "proposalId" p
            let! k = enumOf "kind" proposalKindT p
            let! t = text "text" p
            let! r = optText "rationale" p
            return ProposalRecorded(pr, k, t, r)
        }
    | "ProposalWasAccepted" ->
        result { let! pr = id "proposalId" p in let! t = guid "targetId" p in return ProposalWasAccepted(pr, t) }
    | "ProposalWasRejected" ->
        result { let! pr = id "proposalId" p in let! r = text "reason" p in return ProposalWasRejected(pr, r) }
    | other -> Error $"unregistered event type {other}"

/// One wire envelope to one engine Event (eventId has no engine counterpart; it is verified, not decoded).
let decodeEvent (e: JsonValue) : Result<Event, string> =
    result {
        let! eventType = reqStr "eventType" e
        let! seq = int "aggregateVersion" e |> Option.map Ok |> Option.defaultValue (Error "aggregateVersion")
        let! at = reqStr "occurredAt" e |> Result.bind timeOf
        let! inv = guid "investigationId" e
        let! cmd = guid "correlationId" e
        let! actor = need "actor" e |> Result.bind decodeActor
        let! payload = need "payload" e
        let! body = decodeBody eventType payload at seq
        return { Sequence = seq; Investigation = Id inv; CommandId = cmd; Actor = actor; At = at; Body = body }
    }

// ---------------------------------------------------------------- encoding (engine -> wire)

let private s (v: string) = JString v
let private t (v: NonEmptyText) = JString v.Value
let private ot (v: NonEmptyText option) = v |> Option.map t |> Option.defaultValue JNull
let private g (v: Guid) = JString(Wire.uuidText v)
let private i (v: Id<'T>) = g v.Value
let private il (vs: Id<'T> list) = JArray(vs |> List.map i)

let private nonBlank (name: string) (v: string) =
    if Wire.isNonBlankText v then Ok(JString v) else Error $"{name}: blank text has no v1 wire form (CAUSA.UNREPRESENTABLE)"

let private time (v: DateTimeOffset) = Wire.formatTimestamp v |> Result.map JString
let private optTimeW (v: DateTimeOffset option) = match v with None -> Ok JNull | Some x -> time x

let private enc name table v = toWire name table v |> Result.map JString

let encodeActor (a: Actor) : Result<JsonValue, string> =
    result {
        let! actorId = nonBlank "actorId" a.Id
        match a.Kind with
        | ActorKind.Human -> return JObject [ "actorId", actorId; "kind", s "human" ]
        | ActorKind.System -> return JObject [ "actorId", actorId; "kind", s "system" ]
        | ActorKind.Agent(provider, model) ->
            let! p = nonBlank "provider" provider
            let! m = nonBlank "model" model
            return JObject [ "actorId", actorId; "kind", s "agent"; "provider", p; "model", m ]
    }

let private encodeBody (e: Event) : Result<string * JsonValue, string> =
    let o fields = JObject fields
    match e.Body with
    | InvestigationStarted(title, oid, orig) -> Ok("InvestigationStarted", o [ "title", t title; "observationId", i oid; "original", t orig ])
    | LifecycleChanged(f, tgt, cmd, reason) ->
        result {
            let! fw = enc "lifecycle" lifecycleT f
            let! tw = enc "lifecycle" lifecycleT tgt
            let! cw = enc "lifecycleCommand" lifecycleCommandT cmd
            let! rw = match reason with None -> Ok JNull | Some r -> nonBlank "reason" r
            return "LifecycleChanged", o [ "from", fw; "to", tw; "command", cw; "reason", rw ]
        }
    | ObservationRecorded(oid, orig, at) ->
        optTimeW at |> Result.map (fun a -> "ObservationRecorded", o [ "observationId", i oid; "original", t orig; "observedAt", a ])
    | HypothesisRecorded(h, st, m) -> Ok("HypothesisRecorded", o [ "hypothesisId", i h; "statement", t st; "mechanism", ot m ])
    | HypothesisTransitioned(h, c) ->
        result {
            // §7.2: a writer MUST refuse a change whose At/Sequence differ from the envelope.
            do! if c.At <> e.At || c.Sequence <> e.Sequence then Error "HypothesisChange.At/Sequence differ from the envelope (CAUSA.UNREPRESENTABLE)" else Ok()
            let! f = enc "hypothesisState" hypothesisStateT c.From
            let! tw = enc "hypothesisState" hypothesisStateT c.To
            let! cw = enc "hypothesisCommand" hypothesisCommandT c.Command
            return "HypothesisTransitioned", o [ "hypothesisId", i h; "from", f; "to", tw; "command", cw; "reason", t c.Reason; "evidenceIds", il c.Evidence ]
        }
    | EvidenceAdmitted(eid, content, cat, src, cap) ->
        result {
            let kind, body =
                match content with
                | TextEvidence x -> "text", t x
                | ArtifactEvidence u -> "artifact", JString u
            let! c = enc "evidenceCategory" categoryT cat
            let! ct = optTimeW cap
            return "EvidenceAdmitted", o [ "evidenceId", i eid; "contentKind", s kind; "content", body; "category", c; "source", t src; "capturedAt", ct ]
        }
    | EvidenceSuperseded(old, rep, why) -> Ok("EvidenceSuperseded", o [ "evidenceId", i old; "replacementEvidenceId", i rep; "reason", t why ])
    | EvidenceVerificationAssessed(eid, st) ->
        enc "verificationStatus" verificationStatusT st |> Result.map (fun v -> "EvidenceVerificationAssessed", o [ "evidenceId", i eid; "verification", v ])
    | EvidenceRelated(r, eid, h, k, why) ->
        enc "relationshipKind" relationshipKindT k
        |> Result.map (fun kw -> "EvidenceRelated", o [ "relationshipId", i r; "evidenceId", i eid; "hypothesisId", i h; "kind", kw; "rationale", ot why ])
    | UnknownIdentified(u, q, m) ->
        enc "materiality" materialityT m |> Result.map (fun mw -> "UnknownIdentified", o [ "unknownId", i u; "question", t q; "materiality", mw ])
    | UnknownTransitioned(u, st, b) ->
        // unknownTarget excludes identified/superseded
        (match st with
         | UnknownState.Investigating | UnknownState.UnknownResolved | UnknownState.AcceptedResidual -> enc "unknownTarget" unknownStateT st
         | other -> Error $"unknown transition to {other} has no v1 wire form (CAUSA.UNREPRESENTABLE)")
        |> Result.map (fun sw -> "UnknownTransitioned", o [ "unknownId", i u; "state", sw; "basis", t b ])
    | AssumptionRecorded(a, p, m) ->
        enc "materiality" materialityT m |> Result.map (fun mw -> "AssumptionRecorded", o [ "assumptionId", i a; "proposition", t p; "materiality", mw ])
    | AssumptionTransitioned(a, st) ->
        (match st with
         | AssumptionState.ProvisionallyAccepted | AssumptionState.AssumptionVerified | AssumptionState.Rejected -> enc "assumptionTarget" assumptionStateT st
         | other -> Error $"assumption transition to {other} has no v1 wire form (CAUSA.UNREPRESENTABLE)")
        |> Result.map (fun sw -> "AssumptionTransitioned", o [ "assumptionId", i a; "state", sw ])
    | ContradictionRecorded(c, st, ev, m) ->
        enc "materiality" materialityT m
        |> Result.map (fun mw -> "ContradictionRecorded", o [ "contradictionId", i c; "statement", t st; "evidenceIds", il ev; "materiality", mw ])
    | ContradictionTransitioned(c, st, b) ->
        (match st with
         | ContradictionState.Triaged | ContradictionState.ContradictionResolved | ContradictionState.ContradictionAcceptedResidual ->
             enc "contradictionTarget" contradictionStateT st
         | other -> Error $"contradiction transition to {other} has no v1 wire form (CAUSA.UNREPRESENTABLE)")
        |> Result.map (fun sw -> "ContradictionTransitioned", o [ "contradictionId", i c; "state", sw; "basis", t b ])
    | PredictionRecorded(p, h, st, str, tm) ->
        result {
            let! sw = enc "predictionStrength" predictionStrengthT str
            let! tw = enc "predictionTiming" predictionTimingT tm
            return "PredictionRecorded", o [ "predictionId", i p; "hypothesisId", i h; "statement", t st; "strength", sw; "timing", tw ]
        }
    | ActionRecorded(a, k, p, tg) ->
        enc "actionKind" actionKindT k
        |> Result.map (fun kw -> "ActionRecorded", o [ "actionId", i a; "kind", kw; "purpose", t p; "targetHypothesisIds", il tg ])
    | ActionTransitioned(a, st) ->
        (match st with
         | ActionState.ActionProposed | ActionState.ActionInterpreted -> Error $"action transition to {st} has no v1 wire form (CAUSA.UNREPRESENTABLE)"
         | _ -> enc "actionTarget" actionStateT st)
        |> Result.map (fun sw -> "ActionTransitioned", o [ "actionId", i a; "state", sw ])
    | FindingRecorded(f, a, r, interp) ->
        Ok("FindingRecorded", o [ "findingId", i f; "actionId", i a; "originalResult", t r; "interpretation", ot interp ])
    | NegativeKnowledgeEstablished(n, st, sc, b) ->
        if b.IsEmpty then Error "NegativeKnowledgeEstablished with no basis has no v1 wire form (CAUSA.UNREPRESENTABLE)"
        else Ok("NegativeKnowledgeEstablished", o [ "negativeKnowledgeId", i n; "statement", t st; "scope", t sc; "basisEvidenceIds", il b ])
    | CausalAssertionRecorded(c, outcome, ms, mech) ->
        ms
        |> traverse (fun m -> enc "causeRole" causeRoleT m.Role |> Result.map (fun r -> JObject [ "hypothesisId", i m.Hypothesis; "role", r ]))
        |> Result.map (fun mw -> "CausalAssertionRecorded", o [ "causeId", i c; "outcome", t outcome; "members", JArray mw; "mechanism", ot mech ])
    | CausalAssertionConfirmed(c, policy) -> nonBlank "policy" policy |> Result.map (fun pw -> "CausalAssertionConfirmed", o [ "causeId", i c; "policy", pw ])
    | CausalAssertionReopened(c, r) -> Ok("CausalAssertionReopened", o [ "causeId", i c; "reason", t r ])
    | CorrectiveActionRecorded(c, k, d, tg) ->
        enc "correctiveActionKind" correctiveKindT k
        |> Result.map (fun kw -> "CorrectiveActionRecorded", o [ "correctiveActionId", i c; "kind", kw; "description", t d; "targetCauseIds", il tg ])
    | CorrectiveActionTransitioned(c, st) ->
        (match st with
         | CorrectiveActionState.CorrectionProposed -> Error "corrective action transition to proposed has no v1 wire form (CAUSA.UNREPRESENTABLE)"
         | _ -> enc "correctiveActionTarget" correctiveStateT st)
        |> Result.map (fun sw -> "CorrectiveActionTransitioned", o [ "correctiveActionId", i c; "state", sw ])
    | CriterionRecorded(c, ca, ex, m) ->
        Ok("CriterionRecorded", o [ "criterionId", i c; "correctiveActionId", i ca; "expectedOutcome", t ex; "method", ot m ])
    | VerificationRecorded(v, c, outcome, ev) ->
        enc "verificationOutcome" verificationOutcomeT outcome
        |> Result.map (fun ow -> "VerificationRecorded", o [ "verificationId", i v; "criterionId", i c; "outcome", ow; "evidenceIds", il ev ])
    | ConclusionRecorded(c, st, str, causes, r) ->
        enc "conclusionStrength" conclusionStrengthT str
        |> Result.map (fun sw -> "ConclusionRecorded", o [ "conclusionId", i c; "statement", t st; "strength", sw; "causeIds", il causes; "residualUncertainty", ot r ])
    | ProposalRecorded(p, k, txt, r) ->
        enc "proposalKind" proposalKindT k |> Result.map (fun kw -> "ProposalRecorded", o [ "proposalId", i p; "kind", kw; "text", t txt; "rationale", ot r ])
    | ProposalWasAccepted(p, target) -> Ok("ProposalWasAccepted", o [ "proposalId", i p; "targetId", g target ])
    | ProposalWasRejected(p, r) -> Ok("ProposalWasRejected", o [ "proposalId", i p; "reason", t r ])

/// One engine Event to its wire envelope, including the derived eventId (§5.2).
let encodeEvent (e: Event) : Result<JsonValue, string> =
    result {
        let! eventType, payload = encodeBody e
        let! at = time e.At
        let! actor = encodeActor e.Actor
        let inv = Wire.uuidText e.Investigation.Value
        let corr = Wire.uuidText e.CommandId
        return
            JObject
                [ "formatVersion", s "1.0.0"
                  "eventId", s (Wire.deriveEventId inv e.Sequence corr)
                  "investigationId", s inv
                  "aggregateVersion", JInt e.Sequence
                  "eventType", s eventType
                  "occurredAt", at
                  "actor", actor
                  "semanticVersion", s "1.0.0"
                  "correlationId", s corr
                  "payload", payload ]
    }
