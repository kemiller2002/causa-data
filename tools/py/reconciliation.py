#!/usr/bin/env python3
"""Reconciliation of the implemented F# engine, the candidate contract and the catalog.

The mapping tables below were written by hand from the pinned engine source
(src/engine/Commands.fs EventBody, Model.fs, Codec.fs) and from Indy-init
docs/CAUSA-DATA-CONTRACT-v1.md section 7 and docs/COMMAND-EVENT-CATALOG.md section 27.
The script cross-checks them against the frozen schemas and refuses to emit output
if any schema field is unmapped or any mapping names a field the schema lacks.

  reconciliation.py <schemas-dir> <output-dir>
"""
import json
import sys
from pathlib import Path

ENGINE_COMMIT = "55f49f61c939a3a780cee592f36f732a1328f981"
V = "1.0.0"

# eventType -> list of (wireField, F# constructor element, F# type, semantic meaning)
# "<envelope>" marks engine data carried by the envelope rather than the payload.
ENGINE = {
    "InvestigationStarted": [
        ("title", "title", "NonEmptyText", "Investigation title (decide falls back to the original text when the title is blank)."),
        ("observationId", "observation", "ObservationId", "Id of the initial observation created with the investigation."),
        ("original", "original", "NonEmptyText", "Initial observation, verbatim."),
    ],
    "LifecycleChanged": [
        ("from", "from", "InvestigationLifecycle", "Lifecycle before the change."),
        ("to", "to", "InvestigationLifecycle", "Lifecycle after the change."),
        ("command", "command", "string (engine command name)", "Command that caused the change; distinguishes resolve/reopen/verification-failure."),
        ("reason", "reason", "string option", "Reason or basis; present for markAwaitingEvidence, reopenInvestigation, verificationFailed."),
    ],
    "ObservationRecorded": [
        ("observationId", "Item1", "ObservationId", "Observation id."),
        ("original", "Item2", "NonEmptyText", "Observation, verbatim (R1.2)."),
        ("observedAt", "Item3", "DateTimeOffset option", "When it was observed, if known."),
    ],
    "HypothesisRecorded": [
        ("hypothesisId", "Item1", "HypothesisId", "Hypothesis id."),
        ("statement", "Item2", "NonEmptyText", "Hypothesis statement."),
        ("mechanism", "Item3", "NonEmptyText option", "Proposed mechanism, if stated."),
    ],
    "HypothesisTransitioned": [
        ("hypothesisId", "Item1", "HypothesisId", "Hypothesis id."),
        ("from", "Item2.From", "HypothesisState", "State before."),
        ("to", "Item2.To", "HypothesisState", "State after."),
        ("command", "Item2.Command", "string (HypothesisCommand name)", "Command that caused the change."),
        ("reason", "Item2.Reason", "NonEmptyText", "Why (R2.3)."),
        ("evidenceIds", "Item2.Evidence", "EvidenceId list", "Cited evidence, in citation order."),
        ("<envelope>", "Item2.At", "DateTimeOffset", "Always equals the event time; carried by occurredAt."),
        ("<envelope>", "Item2.Sequence", "int64", "Always equals the event sequence; carried by aggregateVersion."),
    ],
    "EvidenceAdmitted": [
        ("evidenceId", "Item1", "EvidenceId", "Evidence id."),
        ("contentKind", "Item2 (case)", "EvidenceContent", "text (TextEvidence) or artifact (ArtifactEvidence)."),
        ("content", "Item2 (payload)", "NonEmptyText (text) or string (artifact URL)", "Text verbatim, or the absolute http(s) artifact URL."),
        ("category", "Item3", "EvidenceCategory", "Kind of evidence."),
        ("source", "source", "NonEmptyText", "Provenance: where it came from (R3.2)."),
        ("capturedAt", "Item5", "DateTimeOffset option", "When it was captured, if known."),
    ],
    "EvidenceSuperseded": [
        ("evidenceId", "old", "EvidenceId", "Superseded evidence."),
        ("replacementEvidenceId", "replacement", "EvidenceId", "Replacement evidence."),
        ("reason", "reason", "NonEmptyText", "Why it was superseded."),
    ],
    "EvidenceVerificationAssessed": [
        ("evidenceId", "Item1", "EvidenceId", "Evidence id."),
        ("verification", "Item2", "VerificationStatus", "Assessed verification status."),
    ],
    "EvidenceRelated": [
        ("relationshipId", "Item1", "RelationshipId", "Relationship id."),
        ("evidenceId", "Item2", "EvidenceId", "Evidence."),
        ("hypothesisId", "Item3", "HypothesisId", "Hypothesis."),
        ("kind", "Item4", "RelationshipKind", "supports, contradicts, discriminates or insufficient."),
        ("rationale", "Item5", "NonEmptyText option", "Why, if stated."),
    ],
    "UnknownIdentified": [
        ("unknownId", "Item1", "UnknownId", "Unknown id."),
        ("question", "Item2", "NonEmptyText", "Open question."),
        ("materiality", "Item3", "Materiality", "How much it matters."),
    ],
    "UnknownTransitioned": [
        ("unknownId", "Item1", "UnknownId", "Unknown id."),
        ("state", "Item2", "UnknownState", "New state."),
        ("basis", "Item3", "NonEmptyText", "Basis for the change."),
    ],
    "AssumptionRecorded": [
        ("assumptionId", "Item1", "AssumptionId", "Assumption id."),
        ("proposition", "Item2", "NonEmptyText", "Assumed proposition."),
        ("materiality", "Item3", "Materiality", "How much it matters."),
    ],
    "AssumptionTransitioned": [
        ("assumptionId", "Item1", "AssumptionId", "Assumption id."),
        ("state", "Item2", "AssumptionState", "New state."),
    ],
    "ContradictionRecorded": [
        ("contradictionId", "Item1", "ContradictionId", "Contradiction id."),
        ("statement", "Item2", "NonEmptyText", "What contradicts what."),
        ("evidenceIds", "Item3", "EvidenceId list", "Evidence in contradiction."),
        ("materiality", "Item4", "Materiality", "How much it matters."),
    ],
    "ContradictionTransitioned": [
        ("contradictionId", "Item1", "ContradictionId", "Contradiction id."),
        ("state", "Item2", "ContradictionState", "New state."),
        ("basis", "Item3", "NonEmptyText", "Resolution basis."),
    ],
    "PredictionRecorded": [
        ("predictionId", "Item1", "PredictionId", "Prediction id."),
        ("hypothesisId", "Item2", "HypothesisId", "Hypothesis it derives from."),
        ("statement", "Item3", "NonEmptyText", "Predicted observation."),
        ("strength", "Item4", "PredictionStrength", "How strongly it follows."),
        ("timing", "Item5", "PredictionTiming", "As decided by the engine (A11), not as requested."),
    ],
    "ActionRecorded": [
        ("actionId", "Item1", "ActionId", "Action id."),
        ("kind", "Item2", "ActionKind", "Kind of investigative action."),
        ("purpose", "Item3", "NonEmptyText", "Purpose."),
        ("targetHypothesisIds", "Item4", "HypothesisId list", "Hypotheses it tests."),
    ],
    "ActionTransitioned": [
        ("actionId", "Item1", "ActionId", "Action id."),
        ("state", "Item2", "ActionState", "New state."),
    ],
    "FindingRecorded": [
        ("findingId", "Item1", "FindingId", "Finding id."),
        ("actionId", "Item2", "ActionId", "Action that produced it."),
        ("originalResult", "Item3", "NonEmptyText", "Result exactly as entered (R4.4)."),
        ("interpretation", "Item4", "NonEmptyText option", "Interpretation, if stated."),
    ],
    "NegativeKnowledgeEstablished": [
        ("negativeKnowledgeId", "Item1", "NegativeKnowledgeId", "Negative knowledge id."),
        ("statement", "Item2", "NonEmptyText", "What is established not to be the case."),
        ("scope", "Item3", "NonEmptyText", "Where it holds."),
        ("basisEvidenceIds", "Item4", "EvidenceId list (non-empty)", "Evidence that established it (R5.3)."),
    ],
    "CausalAssertionRecorded": [
        ("causeId", "Item1", "CausalAssertionId", "Cause id."),
        ("outcome", "Item2", "NonEmptyText", "Outcome explained."),
        ("members", "Item3", "CausalMember list", "Hypotheses with roles {hypothesisId, role}."),
        ("mechanism", "Item4", "NonEmptyText option", "Causal mechanism, if stated."),
    ],
    "CausalAssertionConfirmed": [
        ("causeId", "Item1", "CausalAssertionId", "Cause id."),
        ("policy", "policy", "string", "Policy id applied by the cause gate (engine Policy.id = core-default/1)."),
    ],
    "CausalAssertionReopened": [
        ("causeId", "Item1", "CausalAssertionId", "Cause id."),
        ("reason", "Item2", "NonEmptyText", "Why it was reopened."),
    ],
    "CorrectiveActionRecorded": [
        ("correctiveActionId", "Item1", "CorrectiveActionId", "Corrective action id."),
        ("kind", "Item2", "CorrectiveActionKind", "mitigation, workaround, correction or prevention (R7.2)."),
        ("description", "Item3", "NonEmptyText", "What will be done."),
        ("targetCauseIds", "Item4", "CausalAssertionId list", "Causes it addresses."),
    ],
    "CorrectiveActionTransitioned": [
        ("correctiveActionId", "Item1", "CorrectiveActionId", "Corrective action id."),
        ("state", "Item2", "CorrectiveActionState", "New state."),
    ],
    "CriterionRecorded": [
        ("criterionId", "Item1", "CriterionId", "Criterion id."),
        ("correctiveActionId", "Item2", "CorrectiveActionId", "Corrective action it verifies."),
        ("expectedOutcome", "Item3", "NonEmptyText", "Success criterion (R7.3)."),
        ("method", "Item4", "NonEmptyText option", "Verification method, if stated."),
    ],
    "VerificationRecorded": [
        ("verificationId", "Item1", "VerificationId", "Verification id."),
        ("criterionId", "Item2", "CriterionId", "Criterion verified."),
        ("outcome", "Item3", "VerificationOutcome", "satisfied, failed or inconclusive."),
        ("evidenceIds", "Item4", "EvidenceId list", "Evidence cited (required when satisfied, R7.4)."),
    ],
    "ConclusionRecorded": [
        ("conclusionId", "Item1", "ConclusionId", "Conclusion id."),
        ("statement", "Item2", "NonEmptyText", "Conclusion."),
        ("strength", "Item3", "ConclusionStrength", "Strength of the conclusion."),
        ("causeIds", "Item4", "CausalAssertionId list", "Causes it rests on."),
        ("residualUncertainty", "Item5", "NonEmptyText option", "Residual uncertainty (R6.4)."),
    ],
    "ProposalRecorded": [
        ("proposalId", "Item1", "ProposalId", "Proposal id."),
        ("kind", "Item2", "ProposalKind", "What is proposed."),
        ("text", "Item3", "NonEmptyText", "Proposed statement/question/purpose; never evidence (R15.2)."),
        ("rationale", "Item4", "NonEmptyText option", "Rationale, if stated."),
    ],
    "ProposalWasAccepted": [
        ("proposalId", "Item1", "ProposalId", "Proposal id."),
        ("targetId", "targetId", "Guid", "Id of the entity the acceptance created."),
    ],
    "ProposalWasRejected": [
        ("proposalId", "Item1", "ProposalId", "Proposal id."),
        ("reason", "Item2", "NonEmptyText", "Why it was rejected."),
    ],
}

ENVELOPE = [
    ("formatVersion", "(none)", "constant", "Wire format version.", "Unspecified field in engine; constant added by contract."),
    ("eventId", "(none)", "derived UUIDv5", "Event identity.", "Engine has no event id; derived (CD-ENV-02)."),
    ("investigationId", "Event.Investigation", "InvestigationId", "Investigation.", "Direct."),
    ("aggregateVersion", "Event.Sequence", "int64", "Logical order.", "Direct."),
    ("eventType", "EventBody case", "union case name", "Discriminator.", "CaseNames.eventType (engine names)."),
    ("occurredAt", "Event.At", "DateTimeOffset", "Time the command was decided.", "Millisecond UTC (CD-WIRE-03)."),
    ("actor", "Event.Actor", "Actor {Id; Kind}", "Who.", "Agent carries provider/model (CD-ENV-04)."),
    ("semanticVersion", "(none)", "constant", "Semantic version.", "Constant added by contract."),
    ("correlationId", "Event.CommandId", "Guid", "Producing command.", "Renamed (CD-ENV-03)."),
    ("payload", "Event.Body", "EventBody", "Event data.", "Per-type, section 7."),
]

# Candidate contract section 7 -> (engine event, field renames, classification, resolution)
CANDIDATE = [
    ("InvestigationStarted", "InvestigationStarted", "Unspecified field",
     "Candidate had title+originalProblem; engine also creates the initial observation. Wire: title, observationId, original."),
    ("InvestigationRenamed", None, "Missing event definition",
     "Engine has no rename command; not registered in v1. Future additive extension (v1.x) if the engine adds it."),
    ("InvestigationActivated", "LifecycleChanged", "Incompatible design decision",
     "Engine emits LifecycleChanged(from,to,command,reason); command=activateInvestigation keeps the distinction. reason is null."),
    ("InvestigationMarkedAwaitingEvidence", "LifecycleChanged", "Incompatible design decision", "command=markAwaitingEvidence; reason required."),
    ("InvestigationMarkedAwaitingVerification", "LifecycleChanged", "Serialization mismatch",
     "command=markAwaitingVerification; engine records no reason (candidate required one): reason is null."),
    ("InvestigationResolved", "LifecycleChanged", "Ambiguous semantic meaning",
     "Candidate required conclusionId and reason; engine resolution records neither. The current conclusion is derived (highest recordedAtVersion). Not invented."),
    ("InvestigationClosed", "LifecycleChanged", "Serialization mismatch", "command=closeInvestigation; reason is null."),
    ("InvestigationReopened", "LifecycleChanged", "Incompatible design decision", "command=reopenInvestigation; reason required."),
    ("InvestigationArchived", None, "Missing event definition",
     "Engine has an Archived state but no command reaching it; not registered. 'archived' kept in the lifecycle enum."),
    ("ObservationRecorded", "ObservationRecorded", "Documentation error", "Names match; observedAt nullable."),
    ("HypothesisProposed", "HypothesisRecorded", "Documentation error", "Engine name is HypothesisRecorded."),
    ("HypothesisStateChanged", "HypothesisTransitioned", "Incompatible design decision",
     "Engine adds the causing command; from/to/reason/evidenceIds kept."),
    ("EvidenceAdmitted", "EvidenceAdmitted", "Documentation error",
     "Candidate field 'verification' does not exist; evidence always starts unverified. contentKind/content map to EvidenceContent."),
    ("EvidenceVerificationChanged", "EvidenceVerificationAssessed", "Serialization mismatch",
     "Engine records only the assessed status (no from, no reason)."),
    ("EvidenceRelatedToHypothesis", "EvidenceRelated", "Documentation error", "Engine name is EvidenceRelated; fields match."),
    ("UnknownIdentified", "UnknownIdentified", "Documentation error", "Match."),
    ("UnknownStateChanged", "UnknownTransitioned", "Serialization mismatch", "Engine records target state + basis only."),
    ("AssumptionRecorded", "AssumptionRecorded", "Documentation error", "Match."),
    ("ContradictionDetected", "ContradictionRecorded", "Documentation error", "Engine name is ContradictionRecorded."),
    ("ContradictionStateChanged", "ContradictionTransitioned", "Serialization mismatch", "Engine records target state + basis only."),
    ("PredictionRecorded", "PredictionRecorded", "Documentation error", "Match; timing is engine-decided (A11)."),
    ("InvestigationActionProposed", "ActionRecorded", "Documentation error", "Engine name is ActionRecorded; fields match."),
    ("InvestigationActionStateChanged", "ActionTransitioned", "Serialization mismatch", "Engine records target state only (no reason)."),
    ("FindingRecorded", "FindingRecorded", "Unspecified field", "Engine also records interpretation (nullable)."),
    ("CausalAssertionProposed", "CausalAssertionRecorded", "Documentation error", "Engine name is CausalAssertionRecorded."),
    ("CausalAssertionStateChanged", "CausalAssertionConfirmed + CausalAssertionReopened", "Incompatible design decision",
     "Engine has distinct confirmation (with policy id) and reopening (with reason) events, matching the catalog's CauseConfirmed."),
    ("CorrectiveActionProposed", "CorrectiveActionRecorded", "Documentation error", "Engine name is CorrectiveActionRecorded."),
    ("CorrectiveActionStateChanged", "CorrectiveActionTransitioned", "Serialization mismatch", "Engine records target state only."),
    ("VerificationCriterionRecorded", "CriterionRecorded", "Documentation error", "Engine name is CriterionRecorded."),
    ("VerificationRecorded", "VerificationRecorded", "Documentation error", "Match; failed/satisfied follow-on events are emitted in the same command."),
    ("ConclusionRecorded", "ConclusionRecorded", "Documentation error", "Match."),
]

NOT_IN_CANDIDATE = {
    "AssumptionTransitioned": "Missing event definition (candidate omitted assumption lifecycle)",
    "EvidenceSuperseded": "Missing event definition (candidate named supersession only in prose)",
    "NegativeKnowledgeEstablished": "Missing event definition (named only in prose)",
    "ProposalRecorded": "Missing event definition (proposal lifecycle named only in prose)",
    "ProposalWasAccepted": "Missing event definition",
    "ProposalWasRejected": "Missing event definition",
}

CATALOG_NOT_IMPLEMENTED = [
    "InvestigationRenamed", "InvestigationArchived", "ClaimRecorded", "InterpretationRecorded", "EvidenceDependencyAdded",
    "AssumptionDependencyAdded", "InvestigationRelationshipAdded", "CausalMemberAdded", "ObligationAccepted", "ReviewRequested",
    "ReviewApproved", "DomainPackActivated", "DomainPackUpgraded", "ExperimentalProfileActivated", "PolicyVersionPinned",
    "ExtensionAttached", "ExtensionMigrationRecorded", "ConclusionReassessed", "ReportIssued", "InvestigationRecoveryRecorded",
    "RedactionRecorded",
]


ENUM_CASES = [
    ("lifecycle", "InvestigationLifecycle", [("Draft", "draft"), ("Active", "active"), ("AwaitingEvidence", "awaitingEvidence"), ("AwaitingVerification", "awaitingVerification"), ("Resolved", "resolved"), ("Closed", "closed"), ("Archived", "archived")]),
    ("hypothesisState", "HypothesisState", [("Proposed", "proposed"), ("Plausible", "plausible"), ("Supported", "supported"), ("Weakened", "weakened"), ("Falsified", "falsified"), ("Superseded", "superseded")]),
    ("unknownState", "UnknownState", [("Identified", "identified"), ("Investigating", "investigating"), ("UnknownResolved", "resolved"), ("AcceptedResidual", "acceptedResidual"), ("UnknownSuperseded", "superseded")]),
    ("materiality", "Materiality", [("Blocking", "blocking"), ("Material", "material"), ("NonMaterial", "nonMaterial"), ("Informational", "informational")]),
    ("assumptionState", "AssumptionState", [("ExplicitUnverified", "explicitUnverified"), ("ProvisionallyAccepted", "provisionallyAccepted"), ("AssumptionVerified", "verified"), ("Rejected", "rejected"), ("Expired", "expired")]),
    ("contradictionState", "ContradictionState", [("Detected", "detected"), ("Triaged", "triaged"), ("ContradictionInvestigating", "investigating"), ("ContradictionResolved", "resolved"), ("ContradictionAcceptedResidual", "acceptedResidual"), ("ContradictionSuperseded", "superseded")]),
    ("actionState", "ActionState", [("ActionProposed", "proposed"), ("ActionApproved", "approved"), ("ActionInProgress", "inProgress"), ("ActionCompleted", "completed"), ("ActionInterpreted", "interpreted"), ("ActionRejected", "rejected"), ("ActionCancelled", "cancelled")]),
    ("actionKind", "ActionKind", [("Test", "test"), ("Inspection", "inspection"), ("Query", "query"), ("Comparison", "comparison"), ("Experiment", "experiment")]),
    ("causeState", "CausalAssertionState", [("CauseProposed", "proposed"), ("CauseConfirmed", "confirmed"), ("CauseReopened", "reopened"), ("CauseSuperseded", "superseded")]),
    ("causeRole", "CauseRole", [("RootCause", "rootCause"), ("ContributingFactor", "contributingFactor"), ("Trigger", "trigger"), ("EnablingCondition", "enablingCondition")]),
    ("correctiveActionState", "CorrectiveActionState", [("CorrectionProposed", "proposed"), ("CorrectionApproved", "approved"), ("CorrectionInProgress", "inProgress"), ("Implemented", "implemented"), ("AwaitingVerificationOfEffect", "awaitingVerificationOfEffect"), ("VerifiedEffective", "verifiedEffective"), ("VerificationFailed", "verificationFailed"), ("ReworkRequired", "reworkRequired"), ("CorrectionCancelled", "cancelled")]),
    ("correctiveActionKind", "CorrectiveActionKind", [("Mitigation", "mitigation"), ("Workaround", "workaround"), ("Correction", "correction"), ("Prevention", "prevention")]),
    ("evidenceCategory", "EvidenceCategory", [("DirectObservation", "directObservation"), ("Measurement", "measurement"), ("Log", "log"), ("Document", "document"), ("ExperimentResult", "experimentResult"), ("Testimony", "testimony"), ("ExternalReference", "externalReference")]),
    ("verificationStatus", "VerificationStatus", [("Unverified", "unverified"), ("PartiallyVerified", "partiallyVerified"), ("Verified", "verified"), ("Invalidated", "invalidated")]),
    ("relationshipKind", "RelationshipKind", [("Supports", "supports"), ("Contradicts", "contradicts"), ("Discriminates", "discriminates"), ("Insufficient", "insufficient")]),
    ("predictionStrength", "PredictionStrength", [("Necessary", "necessary"), ("Expected", "expected"), ("DiscriminatingPrediction", "discriminating"), ("NonDiscriminating", "nonDiscriminating")]),
    ("predictionTiming", "PredictionTiming", [("Prospective", "prospective"), ("Retrospective", "retrospective")]),
    ("verificationOutcome", "VerificationOutcome", [("Satisfied", "satisfied"), ("Failed", "failed"), ("Inconclusive", "inconclusive")]),
    ("conclusionStrength", "ConclusionStrength", [("PlausibleExplanation", "plausibleExplanation"), ("LeadingExplanation", "leadingExplanation"), ("SupportedCause", "supportedCause"), ("ConfirmedCause", "confirmedCause"), ("AcceptedWithResidualUncertainty", "acceptedWithResidualUncertainty")]),
    ("proposalKind", "ProposalKind", [("ProposedHypothesis", "hypothesis"), ("ProposedUnknown", "unknown"), ("ProposedAction", "action")]),
    ("proposalState", "ProposalState", [("ProposalActive", "active"), ("ProposalAccepted", "accepted"), ("ProposalRejected", "rejected")]),
    ("hypothesisCommand", "HypothesisCommand", [("MarkPlausible", "markPlausible"), ("MarkSupported", "markSupported"), ("MarkWeakened", "markWeakened"), ("Falsify", "falsify"), ("Supersede", "supersede"), ("Reopen", "reopen")]),
    ("contentKind", "EvidenceContent", [("TextEvidence", "text"), ("ArtifactEvidence", "artifact")]),
]
LIFECYCLE_COMMANDS = [("ActivateInvestigation", "activateInvestigation"), ("MarkAwaitingEvidence", "markAwaitingEvidence"), ("MarkAwaitingVerification", "markAwaitingVerification"),
                      ("ResolveInvestigation", "resolveInvestigation"), ("CloseInvestigation", "closeInvestigation"), ("ReopenInvestigation", "reopenInvestigation"), ("VerificationFailed", "verificationFailed")]
HYPOTHESIS_COMMANDS = [(a, b) for a, b in ENUM_CASES[21][2]]
ACTOR_KINDS = "Human -> {kind:human}; System -> {kind:system}; Agent(provider, model) -> {kind:agent, provider, model}"


def load_schemas(root):
    return {p.relative_to(root).as_posix(): json.loads(p.read_text()) for p in Path(root).rglob("*.schema.json")}


def main():
    schemas_dir, out = Path(sys.argv[1]), Path(sys.argv[2])
    sys.path.insert(0, str(Path(__file__).parent))
    import render_registry as rr
    docs = load_schemas(schemas_dir)
    problems = []
    rows = []
    for et, fields in ENGINE.items():
        s = docs.get(f"payloads/{et}.schema.json")
        if s is None:
            problems.append(f"no payload schema for {et}")
            continue
        wire = [f for f, *_ in fields if f != "<envelope>"]
        if sorted(wire) != sorted(s["required"]):
            problems.append(f"{et}: mapping {sorted(wire)} != schema {sorted(s['required'])}")
        for f, ctor, ftype, meaning in fields:
            if f == "<envelope>":
                rows.append({"eventType": et, "wireField": None, "fsharpConstructor": f"EventBody.{et}", "fsharpElement": ctor,
                             "fsharpType": ftype, "jsonType": None, "required": None, "nullable": None, "constraints": None,
                             "enumValues": None, "default": None, "semantic": meaning, "versionIntroduced": V,
                             "compatibility": "Not persisted; reconstructed from the envelope (CONTRACT 7.2)."})
                continue
            t, n, cst = rr.describe(s["properties"][f], docs)
            enum_values = None
            if t.startswith("enum "):
                enum_values = docs["common.schema.json"]["$defs"][t.split(" ", 1)[1]]["enum"]
            rows.append({"eventType": et, "wireField": f, "fsharpConstructor": f"EventBody.{et}", "fsharpElement": ctor,
                         "fsharpType": ftype, "jsonType": t, "required": True, "nullable": n, "constraints": cst,
                         "enumValues": enum_values, "default": None, "semantic": meaning, "versionIntroduced": V,
                         "compatibility": "Frozen in v1.0.0; any change requires a new contract version (CONTRACT 13)."})
    registered = docs["event-envelope.schema.json"]["properties"]["eventType"]["enum"]
    if sorted(registered) != sorted(ENGINE):
        problems.append("envelope eventType enum differs from the engine mapping")
    common = docs["common.schema.json"]["$defs"]
    for name, _, pairs in ENUM_CASES:
        if [b for _, b in pairs] != common[name]["enum"]:
            problems.append(f"enum {name}: mapping {[b for _, b in pairs]} != schema {common[name]['enum']}")
    if [b for _, b in LIFECYCLE_COMMANDS] != common["lifecycleCommand"]["enum"]:
        problems.append("lifecycleCommand mapping differs from schema")
    if problems:
        sys.exit("reconciliation incomplete:\n" + "\n".join(problems))

    inventory = rr.inventory(docs)
    reconciliation = {
        "experiment": "EX-CAUSA-STORAGE-2026-001",
        "contractVersion": V,
        "engineCommit": ENGINE_COMMIT,
        "schemaBundleSha256": __import__("hashlib").sha256((schemas_dir / "bundle.json").read_bytes()).hexdigest(),
        "registeredEventTypes": registered,
        "envelope": [{"wireField": a, "fsharpSource": b, "fsharpType": c, "semantic": d, "resolution": e} for a, b, c, d, e in ENVELOPE],
        "envelopeFieldsRemovedFromCandidate": [
            {"field": "recordedAt", "classification": "Serialization mismatch", "resolution": "Engine records one timestamp (Event.At). Removed; occurredAt only."},
            {"field": "policyRefs", "classification": "Unspecified field", "resolution": "Engine has no per-event policy references; the cause gate's policy id is in CausalAssertionConfirmed.policy."},
            {"field": "causationId", "classification": "Unspecified field", "resolution": "Engine has no causation chain; always null would be noise. Removed."},
            {"field": "provenance{source,sourceRef,recordedBy}", "classification": "Unspecified field", "resolution": "Engine provenance is {Actor, RecordedAt}, derived from the envelope. Removed."},
            {"field": "actor.displayName", "classification": "Unspecified field", "resolution": "Engine actor has no display name. Removed."},
            {"field": "actor.kind external|imported", "classification": "Incompatible design decision", "resolution": "Engine ActorKind = Human | Agent(provider, model) | System. Agents carry provider/model."},
        ],
        "payloadFields": rows,
        "candidateRegistryComparison": [
            {"candidateEventType": a, "engineEventType": b, "classification": c, "resolution": d, "blocking": False} for a, b, c, d in CANDIDATE
        ] + [
            {"candidateEventType": None, "engineEventType": k, "classification": v, "resolution": "Registered in v1.0.0 from the engine.", "blocking": False}
            for k, v in NOT_IN_CANDIDATE.items()
        ],
        "catalogEventsNotImplemented": [
            {"catalogEventType": e, "classification": "Missing event definition (not implemented by the engine)",
             "resolution": "Not registered; v1.0.0 readers fail closed on it. Adding it is a compatible additive extension (v1.x).", "blocking": False}
            for e in CATALOG_NOT_IMPLEMENTED
        ],
        "schemaFieldInventory": inventory,
        "counts": {"eventTypes": len(registered), "payloadFields": sum(1 for r in rows if r["wireField"]),
                   "envelopeFields": len(ENVELOPE), "schemas": len(docs), "schemaFieldsTotal": len(inventory),
                   "unresolvedCriticalMappings": 0},
    }
    out.mkdir(parents=True, exist_ok=True)
    (out / "SCHEMA-RECONCILIATION.json").write_text(json.dumps(reconciliation, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")

    md = ["# Field mapping — F# engine ↔ Causa v1.0.0 wire", "",
          f"Engine: product-owned `.engine/src/engine` source (frozen provenance commit `{ENGINE_COMMIT}`, `Commands.fs` `EventBody`). "
          "Generated from a hand-written mapping and cross-checked against the frozen schemas: generation fails if any schema field "
          "or enumeration value is unmapped. Normative alongside CONTRACT.md §6–§7.", "",
          "Actor: " + ACTOR_KINDS + ".", "",
          "Every payload key is required. `nullable` = the F# `option`, encoded as `null` when absent. All fields were introduced in 1.0.0.", "",
          "## Envelope", "", "| wire field | F# source | F# type | resolution |", "|---|---|---|---|"]
    md += [f"| `{a}` | {b} | {c} | {e} |" for a, b, c, d, e in ENVELOPE]
    md += ["", "## Payloads", ""]
    for et in ENGINE:
        md += [f"### `{et}`", "", "| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |", "|---|---|---|---|---|---|---|"]
        def cell(x):
            return str(x).replace("|", "\\|")
        for r in (r for r in rows if r["eventType"] == et):
            wf = f"`{r['wireField']}`" if r["wireField"] else "*(envelope)*"
            md.append(f"| {wf} | `{r['fsharpElement']}` | {cell(r['fsharpType'])} | {cell(r['jsonType'] or '—')} | "
                      f"{'—' if r['nullable'] is None else ('yes' if r['nullable'] else 'no')} | {r['constraints'] or '—'} | {cell(r['semantic'])} |")
        md.append("")
    md += ["## Enumerations (F# union case → wire value)", "",
           "Engine prefixes that exist only to avoid F# name collisions are dropped. The F# case names are those of the "
           "engine's `Model.fs`/`Commands.fs` unions. Bidirectional exhaustiveness is tested against the engine's own case lists "
           "(`Codec.fs` `CaseNames.all*`) by the reference toolkit's unit tests.", "",
           "| enumeration | F# union | F# case → wire value |", "|---|---|---|"]
    md += [f"| `{name}` | `{union}` | " + ", ".join(f"`{a}`→`{b}`" for a, b in pairs) + " |" for name, union, pairs in ENUM_CASES]
    md += ["", "### Engine command strings", "",
           "`LifecycleChanged.command` and `HypothesisTransitioned.command` carry a command name; the engine stores it as a "
           "string (`LifecycleChanged.command`, `HypothesisChange.Command`). These are the exact engine strings:", "",
           "| field | engine string → wire value |", "|---|---|",
           "| `LifecycleChanged.command` | " + ", ".join(f"`\"{a}\"`→`{b}`" for a, b in LIFECYCLE_COMMANDS) + " |",
           "| `HypothesisTransitioned.command` | " + ", ".join(f"`\"{a}\"`→`{b}`" for a, b in HYPOTHESIS_COMMANDS) + " |", "",
           "`\"VerificationFailed\"` is not a user command. The engine emits it as the follow-on `LifecycleChanged(AwaitingVerification, Active, \"VerificationFailed\", Some reason)` "
           "when `RecordVerification` records a failed outcome while the investigation awaits verification. Hypothesis command strings "
           "are `$\"{command}\"` of the `HypothesisCommand` union, i.e. the case name.", ""]
    (out / "FIELD-MAPPING.md").write_text("\n".join(md) + "\n", encoding="utf-8")

    reg = ["# Event registry — Causa storage contract v1.0.0", "",
           f"31 registered event types = the engine `EventBody` cases at `{ENGINE_COMMIT}`, in declaration order. "
           "Unknown types fail closed (`CAUSA.UNKNOWN_EVENT_TYPE`).", "",
           "| # | eventType | payload fields | introduced | candidate name(s) |", "|---|---|---|---|---|"]
    cand_for = {}
    for a, b, *_ in CANDIDATE:
        if b:
            for e in b.split(" + "):
                cand_for.setdefault(e, []).append(a)
    for i, et in enumerate(registered, 1):
        fs = ", ".join(f"`{f}`" for f, *_ in ENGINE[et] if f != "<envelope>")
        reg.append(f"| {i} | `{et}` | {fs} | 1.0.0 | {', '.join(cand_for.get(et, [])) or '— (not in candidate)'} |")
    reg += ["", "## Catalog events not implemented (not registered)", "",
            ", ".join(f"`{e}`" for e in CATALOG_NOT_IMPLEMENTED), "",
            "Each is a future compatible additive extension (new minor contract version). A v1.0.0 reader refuses them.", ""]
    (out / "EVENT-REGISTRY.md").write_text("\n".join(reg) + "\n", encoding="utf-8")
    print(f"ok: {len(registered)} event types, {reconciliation['counts']['payloadFields']} payload fields, {len(inventory)} schema fields")


if __name__ == "__main__":
    main()
