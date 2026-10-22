#!/usr/bin/env python3
"""Authoring aid for the Causa storage contract v1.0.0 JSON Schemas.

Every definition below is written by hand from CONTRACT.md and the reconciliation in
FIELD-MAPPING.md. Nothing here is derived from F# reflection or auto-serialization.
The emitted files under contract/v1.0.0/schemas/ are the frozen, authoritative artifacts;
this script is retained only as provenance and must not be re-run against a frozen tree
(the compatibility gate pins each emitted file's SHA-256).

Usage: author_schemas.py <output-directory>
"""
import json
import posixpath
import sys
from pathlib import Path

BASE = "https://github.com/kemiller2002/causa-data/contract/v1.0.0/schemas/"
DRAFT = "https://json-schema.org/draft/2020-12/schema"

# Characters the engine treats as whitespace (System.Char.IsWhiteSpace: U+0009-U+000D,
# U+0085, Zs, Zl, Zp). A non-blank string contains at least one character outside this set.
WS = "\\t\\n\\u000b\\f\\r \\u0085\\u00a0\\u1680\\u2000-\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000"
NON_BLANK = "[^" + WS + "]"

ENUMS = {
    "lifecycle": ["draft", "active", "awaitingEvidence", "awaitingVerification", "resolved", "closed", "archived"],
    "hypothesisState": ["proposed", "plausible", "supported", "weakened", "falsified", "superseded"],
    "unknownState": ["identified", "investigating", "resolved", "acceptedResidual", "superseded"],
    "materiality": ["blocking", "material", "nonMaterial", "informational"],
    "assumptionState": ["explicitUnverified", "provisionallyAccepted", "verified", "rejected", "expired"],
    "contradictionState": ["detected", "triaged", "investigating", "resolved", "acceptedResidual", "superseded"],
    "actionState": ["proposed", "approved", "inProgress", "completed", "interpreted", "rejected", "cancelled"],
    "actionKind": ["test", "inspection", "query", "comparison", "experiment"],
    "causeState": ["proposed", "confirmed", "reopened", "superseded"],
    "causeRole": ["rootCause", "contributingFactor", "trigger", "enablingCondition"],
    "correctiveActionState": ["proposed", "approved", "inProgress", "implemented", "awaitingVerificationOfEffect",
                              "verifiedEffective", "verificationFailed", "reworkRequired", "cancelled"],
    "correctiveActionKind": ["mitigation", "workaround", "correction", "prevention"],
    "evidenceCategory": ["directObservation", "measurement", "log", "document", "experimentResult", "testimony",
                         "externalReference"],
    "verificationStatus": ["unverified", "partiallyVerified", "verified", "invalidated"],
    "relationshipKind": ["supports", "contradicts", "discriminates", "insufficient"],
    "predictionStrength": ["necessary", "expected", "discriminating", "nonDiscriminating"],
    "predictionTiming": ["prospective", "retrospective"],
    "verificationOutcome": ["satisfied", "failed", "inconclusive"],
    "conclusionStrength": ["plausibleExplanation", "leadingExplanation", "supportedCause", "confirmedCause",
                           "acceptedWithResidualUncertainty"],
    "proposalKind": ["hypothesis", "unknown", "action"],
    "proposalState": ["active", "accepted", "rejected"],
    "hypothesisCommand": ["markPlausible", "markSupported", "markWeakened", "falsify", "supersede", "reopen"],
    "lifecycleCommand": ["activateInvestigation", "markAwaitingEvidence", "markAwaitingVerification",
                         "resolveInvestigation", "closeInvestigation", "reopenInvestigation", "verificationFailed"],
    "contentKind": ["text", "artifact"],
}

# Transition targets reachable through the engine's Transitions tables and decide follow-ons.
REACHABLE = {
    "lifecycleTarget": ["active", "awaitingEvidence", "awaitingVerification", "resolved", "closed"],
    "hypothesisTarget": ["plausible", "supported", "weakened", "falsified", "superseded"],
    "unknownTarget": ["investigating", "resolved", "acceptedResidual"],
    "assumptionTarget": ["provisionallyAccepted", "verified", "rejected"],
    "contradictionTarget": ["triaged", "resolved", "acceptedResidual"],
    "actionTarget": ["approved", "rejected", "inProgress", "completed", "cancelled"],
    "correctiveActionTarget": ["approved", "cancelled", "inProgress", "implemented", "awaitingVerificationOfEffect",
                               "verificationFailed", "verifiedEffective", "reworkRequired"],
}


def ref(path, name=None):
    return {"$ref": path + ("#/$defs/" + name if name else "")}


def c(name):
    return ref("common.schema.json", name)


def closed(props, description=None, extra=None):
    out = {"type": "object", "additionalProperties": False, "required": list(props.keys()), "properties": props}
    if description:
        out["description"] = description
    if extra:
        out.update(extra)
    return out


def doc(file, title, body, description):
    out = {"$schema": DRAFT, "$id": BASE + file, "title": title, "description": description}
    out.update(body)
    return file, out


def common():
    defs = {
        "uuid": {"type": "string", "description": "Lowercase canonical RFC 4122 text, 36 characters, not the nil UUID.",
                 "pattern": "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
                 "not": {"const": "00000000-0000-0000-0000-000000000000"}},
        "uuidList": {"type": "array", "description": "Ordered list of distinct UUIDs; order is significant and preserved.",
                     "uniqueItems": True, "items": {"$ref": "#/$defs/uuid"}},
        "nonEmptyUuidList": {"allOf": [{"$ref": "#/$defs/uuidList"}, {"minItems": 1}]},
        "timestamp": {"type": "string", "description": "RFC 3339 UTC instant with exactly three fractional digits and 'Z'.",
                      "pattern": "^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])T([01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9]\\.[0-9]{3}Z$"},
        "nullableTimestamp": {"oneOf": [{"$ref": "#/$defs/timestamp"}, {"type": "null"}]},
        "text": {"type": "string", "description": "Verbatim text containing at least one non-whitespace character. Never trimmed or normalized.",
                 "minLength": 1, "pattern": NON_BLANK},
        "nullableText": {"oneOf": [{"$ref": "#/$defs/text"}, {"type": "null"}]},
        "httpUrl": {"type": "string", "description": "Absolute http(s) URL (engine rule R3.5).", "pattern": "^[Hh][Tt][Tt][Pp][Ss]?://[^" + WS + "]"},
        "sha256": {"type": "string", "description": "Lowercase hexadecimal SHA-256 digest.", "pattern": "^[0-9a-f]{64}$"},
        "aggregateVersion": {"type": "integer", "minimum": 1, "maximum": 9007199254740991},
        "versionOrZero": {"type": "integer", "minimum": 0, "maximum": 9007199254740991},
        "formatVersion": {"const": "1.0.0", "description": "Wire format version of this contract."},
        "schemaVersion": {"const": "1.0.0"},
        "semanticVersion": {"const": "1.0.0", "description": "Semantic (domain meaning) version; changes only via approved semantic evolution."},
        "slug": {"type": "string", "pattern": "^[a-z0-9][a-z0-9-]{0,63}$"},
        "errorCode": {"type": "string", "pattern": "^CAUSA\\.[A-Z_]+$"},
    }
    defs.update({k: {"enum": v} for k, v in ENUMS.items()})
    defs.update({k: {"enum": v} for k, v in REACHABLE.items()})
    return doc("common.schema.json", "Causa v1 primitive and enumeration definitions", {"$defs": defs},
               "Domain identifiers, primitive types, version references and every persisted enumeration.")


def actor():
    defs = {
        "actor": {"oneOf": [
            closed({"actorId": c("text"), "kind": {"enum": ["human", "system"]}}),
            closed({"actorId": c("text"), "kind": {"const": "agent"}, "provider": c("text"), "model": c("text")}),
        ]},
        "provenance": closed({"actor": {"$ref": "#/$defs/actor"}, "recordedAt": c("timestamp")},
                             "Who created the entity and the occurredAt of the event that created it."),
    }
    return doc("actor.schema.json", "Causa v1 actor and provenance", {"$defs": defs},
               "Actor identity (non-secret, stable) and entity provenance. Agents carry provider and model (engine ActorKind.Agent).")


P = {}  # eventType -> payload schema


def payload(name, props, description, extra=None):
    P[name] = closed(props, description, extra)


def cond(field, value, then):
    return {"if": {"properties": {field: {"const": value}}, "required": [field]}, "then": then}


def define_payloads():
    payload("InvestigationStarted", {"title": c("text"), "observationId": c("uuid"), "original": c("text")},
            "Starts the investigation and records its initial observation verbatim. Always aggregateVersion 1.")
    lifecycle_rules = [
        ("activateInvestigation", "active", "null"), ("markAwaitingEvidence", "awaitingEvidence", "text"),
        ("markAwaitingVerification", "awaitingVerification", "null"), ("resolveInvestigation", "resolved", "null"),
        ("closeInvestigation", "closed", "null"), ("reopenInvestigation", "active", "text"),
        ("verificationFailed", "active", "text"),
    ]
    payload("LifecycleChanged",
            {"from": c("lifecycle"), "to": c("lifecycleTarget"), "command": c("lifecycleCommand"), "reason": c("nullableText")},
            "Investigation lifecycle transition, discriminated by the command that caused it.",
            {"allOf": [cond("command", cmd, {"properties": {"to": {"const": to},
                                                            "reason": c("text") if reason == "text" else {"type": "null"}}})
                       for cmd, to, reason in lifecycle_rules]
                      + [cond("command", "verificationFailed", {"properties": {"from": {"const": "awaitingVerification"}}})]})
    payload("ObservationRecorded", {"observationId": c("uuid"), "original": c("text"), "observedAt": c("nullableTimestamp")},
            "An observation, preserved verbatim (R1.2).")
    payload("HypothesisRecorded", {"hypothesisId": c("uuid"), "statement": c("text"), "mechanism": c("nullableText")},
            "A new hypothesis in state 'proposed'.")
    hyp_rules = [("markPlausible", "plausible"), ("markSupported", "supported"), ("markWeakened", "weakened"),
                 ("falsify", "falsified"), ("supersede", "superseded"), ("reopen", "plausible")]
    payload("HypothesisTransitioned",
            {"hypothesisId": c("uuid"), "from": c("hypothesisState"), "to": c("hypothesisTarget"), "command": c("hypothesisCommand"),
             "reason": c("text"), "evidenceIds": c("uuidList")},
            "Hypothesis state change with its reason and cited evidence (R2.3). The change time and version are the envelope's occurredAt and aggregateVersion.",
            {"allOf": [cond("command", cmd, {"properties": {"to": {"const": to}}}) for cmd, to in hyp_rules]
                      + [cond("command", cmd, {"properties": {"evidenceIds": c("nonEmptyUuidList")}})
                         for cmd in ["markSupported", "markWeakened", "falsify", "reopen"]]})
    payload("EvidenceAdmitted",
            {"evidenceId": c("uuid"), "contentKind": c("contentKind"), "content": c("text"), "category": c("evidenceCategory"),
             "source": c("text"), "capturedAt": c("nullableTimestamp")},
            "Admits evidence. Text content is verbatim; artifacts are referenced by absolute http(s) URL, never embedded (R3.5). Verification starts 'unverified'.",
            {"allOf": [cond("contentKind", "artifact", {"properties": {"content": c("httpUrl")}})]})
    payload("EvidenceSuperseded", {"evidenceId": c("uuid"), "replacementEvidenceId": c("uuid"), "reason": c("text")},
            "Marks evidence as superseded by a replacement; the original remains in history.")
    payload("EvidenceVerificationAssessed", {"evidenceId": c("uuid"), "verification": c("verificationStatus")},
            "Records an assessment of the evidence's verification status.")
    payload("EvidenceRelated",
            {"relationshipId": c("uuid"), "evidenceId": c("uuid"), "hypothesisId": c("uuid"), "kind": c("relationshipKind"),
             "rationale": c("nullableText")}, "Relates evidence to a hypothesis.")
    payload("UnknownIdentified", {"unknownId": c("uuid"), "question": c("text"), "materiality": c("materiality")},
            "Identifies an open question; starts 'identified'.")
    payload("UnknownTransitioned", {"unknownId": c("uuid"), "state": c("unknownTarget"), "basis": c("text")},
            "Unknown state change with its basis.")
    payload("AssumptionRecorded", {"assumptionId": c("uuid"), "proposition": c("text"), "materiality": c("materiality")},
            "Records an explicit assumption; starts 'explicitUnverified'.")
    payload("AssumptionTransitioned", {"assumptionId": c("uuid"), "state": c("assumptionTarget")}, "Assumption state change.")
    payload("ContradictionRecorded",
            {"contradictionId": c("uuid"), "statement": c("text"), "evidenceIds": c("uuidList"), "materiality": c("materiality")},
            "Records a contradiction among evidence; starts 'detected'.")
    payload("ContradictionTransitioned", {"contradictionId": c("uuid"), "state": c("contradictionTarget"), "basis": c("text")},
            "Contradiction state change with its basis.")
    payload("PredictionRecorded",
            {"predictionId": c("uuid"), "hypothesisId": c("uuid"), "statement": c("text"), "strength": c("predictionStrength"),
             "timing": c("predictionTiming")},
            "A prediction derived from a hypothesis. Timing is as decided by the engine (A11), not as requested.")
    payload("ActionRecorded",
            {"actionId": c("uuid"), "kind": c("actionKind"), "purpose": c("text"), "targetHypothesisIds": c("uuidList")},
            "Proposes an investigative action; starts 'proposed'.")
    payload("ActionTransitioned", {"actionId": c("uuid"), "state": c("actionTarget")}, "Investigative action state change.")
    payload("FindingRecorded",
            {"findingId": c("uuid"), "actionId": c("uuid"), "originalResult": c("text"), "interpretation": c("nullableText")},
            "The actual result of an action exactly as entered (R4.4).")
    payload("NegativeKnowledgeEstablished",
            {"negativeKnowledgeId": c("uuid"), "statement": c("text"), "scope": c("text"), "basisEvidenceIds": c("nonEmptyUuidList")},
            "Records something established NOT to be the case, citing evidence (R5.3).")
    payload("CausalAssertionRecorded",
            {"causeId": c("uuid"), "outcome": c("text"),
             "members": {"type": "array", "minItems": 1, "uniqueItems": True, "items": ref("entities/causal-member.schema.json")},
             "mechanism": c("nullableText")},
            "Proposes a causal explanation composed of hypotheses with roles; starts 'proposed'.")
    payload("CausalAssertionConfirmed", {"causeId": c("uuid"), "policy": c("text")},
            "Confirms a cause after the cause gate passed; 'policy' identifies the policy version applied (engine Policy.id).")
    payload("CausalAssertionReopened", {"causeId": c("uuid"), "reason": c("text")}, "Reopens a confirmed cause.")
    payload("CorrectiveActionRecorded",
            {"correctiveActionId": c("uuid"), "kind": c("correctiveActionKind"), "description": c("text"), "targetCauseIds": c("uuidList")},
            "Proposes a corrective action; mitigation is distinct from correction (R7.2).")
    payload("CorrectiveActionTransitioned", {"correctiveActionId": c("uuid"), "state": c("correctiveActionTarget")},
            "Corrective action state change.")
    payload("CriterionRecorded",
            {"criterionId": c("uuid"), "correctiveActionId": c("uuid"), "expectedOutcome": c("text"), "method": c("nullableText")},
            "A verification success criterion for a corrective action (R7.3).")
    payload("VerificationRecorded",
            {"verificationId": c("uuid"), "criterionId": c("uuid"), "outcome": c("verificationOutcome"), "evidenceIds": c("uuidList")},
            "A verification of a criterion; a satisfied outcome must cite evidence (R7.4).",
            {"allOf": [cond("outcome", "satisfied", {"properties": {"evidenceIds": c("nonEmptyUuidList")}})]})
    payload("ConclusionRecorded",
            {"conclusionId": c("uuid"), "statement": c("text"), "strength": c("conclusionStrength"), "causeIds": c("uuidList"),
             "residualUncertainty": c("nullableText")}, "A conclusion with explicit residual uncertainty (R6.4).")
    payload("ProposalRecorded",
            {"proposalId": c("uuid"), "kind": c("proposalKind"), "text": c("text"), "rationale": c("nullableText")},
            "A proposal (typically from an AI agent); never evidence or authoritative state (D-004, R15.2).")
    payload("ProposalWasAccepted", {"proposalId": c("uuid"), "targetId": c("uuid")},
            "A human accepted a proposal; targetId is the id of the entity the acceptance created (preceding event of the same correlationId).")
    payload("ProposalWasRejected", {"proposalId": c("uuid"), "reason": c("text")}, "A proposal was rejected with a reason.")


def payload_docs():
    return [doc("payloads/" + name + ".schema.json", name + " payload", body, body.pop("description"))
            for name, body in P.items()]


def envelope():
    props = {
        "formatVersion": c("formatVersion"), "eventId": c("uuid"), "investigationId": c("uuid"),
        "aggregateVersion": c("aggregateVersion"), "eventType": {"enum": list(P.keys())}, "occurredAt": c("timestamp"),
        "actor": ref("actor.schema.json", "actor"), "semanticVersion": c("semanticVersion"), "correlationId": c("uuid"),
        "payload": {"type": "object"},
    }
    rules = [cond("eventType", name, {"properties": {"payload": ref("payloads/" + name + ".schema.json")}}) for name in P]
    # D-004: an AI agent may only record proposals.
    rules.append({"if": {"properties": {"actor": {"properties": {"kind": {"const": "agent"}}, "required": ["kind"]}}, "required": ["actor"]},
                  "then": {"properties": {"eventType": {"const": "ProposalRecorded"}}}})
    rules.append(cond("eventType", "InvestigationStarted", {"properties": {"aggregateVersion": {"const": 1}}}))
    body = closed(props, extra={"allOf": rules})
    return doc("event-envelope.schema.json", "Causa v1 authoritative event envelope", body,
               "One line of events.ndjson. eventId = UUIDv5(eventNamespace, investigationId/aggregateVersion/correlationId) (CONTRACT §5.2).")


ENTITIES = {}


def entity(file, props, description, extra=None):
    props = dict(props)
    ENTITIES[file] = closed(props, description, extra)


def define_entities():
    prov = ref("actor.schema.json", "provenance")
    hyp_rules = [("markPlausible", "plausible"), ("markSupported", "supported"), ("markWeakened", "weakened"),
                 ("falsify", "falsified"), ("supersede", "superseded"), ("reopen", "plausible")]
    entity("hypothesis-change", {"from": c("hypothesisState"), "to": c("hypothesisTarget"), "command": c("hypothesisCommand"),
                                 "reason": c("text"), "evidenceIds": c("uuidList"), "at": c("timestamp"),
                                 "aggregateVersion": c("aggregateVersion")},
           "One entry of a hypothesis's change history, in history order.",
           {"allOf": [cond("command", cmd, {"properties": {"to": {"const": to}}}) for cmd, to in hyp_rules]
                     + [cond("command", cmd, {"properties": {"evidenceIds": c("nonEmptyUuidList")}})
                        for cmd in ["markSupported", "markWeakened", "falsify", "reopen"]]})
    entity("causal-member", {"hypothesisId": c("uuid"), "role": c("causeRole")}, "A member of a causal assertion.")
    entity("observation", {"id": c("uuid"), "original": c("text"), "observedAt": c("nullableTimestamp"), "provenance": prov},
           "Observation projection.")
    entity("hypothesis", {"id": c("uuid"), "statement": c("text"), "mechanism": c("nullableText"), "state": c("hypothesisState"),
                          "history": {"type": "array", "items": ref("entities/hypothesis-change.schema.json")}, "provenance": prov},
           "Hypothesis projection with its full change history.")
    entity("evidence", {"id": c("uuid"), "contentKind": c("contentKind"), "content": c("text"), "category": c("evidenceCategory"),
                        "source": c("text"), "capturedAt": c("nullableTimestamp"), "verification": c("verificationStatus"),
                        "supersededBy": {"oneOf": [c("uuid"), {"type": "null"}]}, "admittedAtVersion": c("aggregateVersion"),
                        "provenance": prov}, "Evidence projection. admittedAtVersion is the aggregateVersion that admitted it.",
           {"allOf": [cond("contentKind", "artifact", {"properties": {"content": c("httpUrl")}})]})
    entity("relationship", {"id": c("uuid"), "evidenceId": c("uuid"), "hypothesisId": c("uuid"), "kind": c("relationshipKind"),
                            "rationale": c("nullableText"), "provenance": prov}, "Evidence-hypothesis relationship projection.")
    entity("unknown", {"id": c("uuid"), "question": c("text"), "materiality": c("materiality"), "state": c("unknownState"),
                       "basis": c("nullableText"), "provenance": prov}, "Unknown projection; basis is the latest transition basis.")
    entity("assumption", {"id": c("uuid"), "proposition": c("text"), "materiality": c("materiality"), "state": c("assumptionState"),
                          "provenance": prov}, "Assumption projection.")
    entity("contradiction", {"id": c("uuid"), "statement": c("text"), "evidenceIds": c("uuidList"), "materiality": c("materiality"),
                             "state": c("contradictionState"), "basis": c("nullableText"), "provenance": prov},
           "Contradiction projection; basis is the latest transition basis.")
    entity("prediction", {"id": c("uuid"), "hypothesisId": c("uuid"), "statement": c("text"), "strength": c("predictionStrength"),
                          "timing": c("predictionTiming"), "provenance": prov}, "Prediction projection.")
    entity("action", {"id": c("uuid"), "kind": c("actionKind"), "purpose": c("text"), "targetHypothesisIds": c("uuidList"),
                      "state": c("actionState"), "provenance": prov}, "Investigative action projection.")
    entity("finding", {"id": c("uuid"), "actionId": c("uuid"), "originalResult": c("text"), "interpretation": c("nullableText"),
                       "provenance": prov}, "Finding projection.")
    entity("negative-knowledge", {"id": c("uuid"), "statement": c("text"), "scope": c("text"), "basisEvidenceIds": c("nonEmptyUuidList"),
                                  "provenance": prov}, "Negative knowledge projection.")
    entity("cause", {"id": c("uuid"), "outcome": c("text"),
                     "members": {"type": "array", "minItems": 1, "uniqueItems": True, "items": ref("entities/causal-member.schema.json")},
                     "mechanism": c("nullableText"), "state": c("causeState"), "provenance": prov}, "Causal assertion projection.")
    entity("corrective-action", {"id": c("uuid"), "kind": c("correctiveActionKind"), "description": c("text"),
                                 "targetCauseIds": c("uuidList"), "state": c("correctiveActionState"),
                                 "awaitingSinceVersion": c("versionOrZero"), "provenance": prov},
           "Corrective action projection. awaitingSinceVersion is the aggregateVersion at which the current verification round began (0 = never).")
    entity("criterion", {"id": c("uuid"), "correctiveActionId": c("uuid"), "expectedOutcome": c("text"), "method": c("nullableText"),
                         "provenance": prov}, "Verification criterion projection.")
    entity("verification", {"id": c("uuid"), "criterionId": c("uuid"), "outcome": c("verificationOutcome"), "evidenceIds": c("uuidList"),
                            "recordedAtVersion": c("aggregateVersion"), "provenance": prov}, "Verification projection.",
           {"allOf": [cond("outcome", "satisfied", {"properties": {"evidenceIds": c("nonEmptyUuidList")}})]})
    entity("conclusion", {"id": c("uuid"), "statement": c("text"), "strength": c("conclusionStrength"), "causeIds": c("uuidList"),
                          "residualUncertainty": c("nullableText"), "recordedAtVersion": c("aggregateVersion"), "provenance": prov},
           "Conclusion projection; the current conclusion is the one with the highest recordedAtVersion.")
    entity("proposal", {"id": c("uuid"), "kind": c("proposalKind"), "text": c("text"), "rationale": c("nullableText"),
                        "state": c("proposalState"), "decision": c("nullableText"), "provenance": prov},
           "Proposal projection; decision is the rejection reason when rejected.")


def entity_docs():
    return [doc("entities/" + name + ".schema.json", name + " projection entity", body, body.pop("description"))
            for name, body in ENTITIES.items()]


STATE_ARRAYS = [("observations", "observation"), ("hypotheses", "hypothesis"), ("evidence", "evidence"),
                ("relationships", "relationship"), ("unknowns", "unknown"), ("assumptions", "assumption"),
                ("contradictions", "contradiction"), ("predictions", "prediction"), ("actions", "action"),
                ("findings", "finding"), ("negativeKnowledge", "negative-knowledge"), ("causes", "cause"),
                ("correctiveActions", "corrective-action"), ("criteria", "criterion"), ("verifications", "verification"),
                ("conclusions", "conclusion"), ("proposals", "proposal")]


def state():
    props = {"investigationId": c("uuid"), "title": c("text"), "lifecycle": c("lifecycle"), "version": c("aggregateVersion"),
             "createdAt": c("timestamp")}
    props.update({k: {"type": "array", "description": "Sorted by id (ordinal UUID text).",
                      "items": ref("entities/" + e + ".schema.json")} for k, e in STATE_ARRAYS})
    return doc("state.schema.json", "Causa v1 projected investigation state", closed(props),
               "Deterministic projection of replayed events. Also the shape of golden expected projections.")


def snapshot():
    props = {"formatVersion": c("formatVersion"), "investigationId": c("uuid"), "aggregateVersion": c("aggregateVersion"),
             "projectionVersion": {"const": 1}, "semanticVersion": c("semanticVersion"), "generatedAt": c("timestamp"),
             "state": ref("state.schema.json")}
    return doc("snapshot.schema.json", "Causa v1 snapshot envelope", closed(props),
               "Derived, never authoritative. generatedAt equals occurredAt of event aggregateVersion.")


def investigation_manifest():
    props = {"formatVersion": c("formatVersion"), "investigationId": c("uuid"), "schemaVersion": c("schemaVersion"),
             "semanticVersion": c("semanticVersion"), "aggregateVersion": c("aggregateVersion"), "eventCount": c("aggregateVersion"),
             "eventsSha256": c("sha256"), "snapshotVersion": {"const": 1}, "snapshotSha256": c("sha256"),
             "domainPacks": {"type": "array", "maxItems": 0, "description": "No domain packs exist in v1.0.0."},
             "createdAt": c("timestamp"), "updatedAt": c("timestamp")}
    return doc("investigation-manifest.schema.json", "Causa v1 investigation manifest", closed(props),
               "Per-investigation manifest. Contains no provider metadata (no Git SHA, branch, path or token).")


def root_manifest():
    entry = closed({"investigationId": c("uuid"), "scenarioKey": c("slug"), "title": c("text"), "expectedVersion": c("aggregateVersion"),
                    "expectedLifecycle": c("lifecycle"), "expectedStateSha256": c("sha256"), "eventsSha256": c("sha256"),
                    "manifestSha256": c("sha256"), "snapshotSha256": c("sha256")})
    negative = closed({"path": {"type": "string", "pattern": "^negative/[a-z0-9][a-z0-9-]*\\.json$"}, "expectedErrorCode": c("errorCode")})
    props = {"formatVersion": c("formatVersion"), "datasetId": {"const": "causa-demo-v1"}, "createdAt": c("timestamp"),
             "schemaSha256": c("sha256"), "investigations": {"type": "array", "minItems": 1, "items": entry},
             "negativeFixtures": {"type": "array", "items": negative}}
    return doc("root-manifest.schema.json", "Causa v1 root fixture manifest", closed(props),
               "fixtures/v1.0.0/manifest.json. Investigations ordered by investigationId; negative fixtures never listed as valid.")


def negative_fixture():
    file = closed({"base64": {"type": "string", "pattern": "^[A-Za-z0-9+/]*={0,2}$"}})
    files = {"type": "object", "additionalProperties": file,
             "description": "Directory entries by file name; any names, so missing and extra files are representable (CONTRACT §3.3)."}
    common_props = {"formatVersion": c("formatVersion"), "name": c("slug"), "description": c("text"),
                    "expectedErrorCode": c("errorCode"), "expectedDomainCode": {"oneOf": [{"type": "string", "pattern": "^[A-Z]+(\\.[A-Z_]+)+$"}, {"type": "null"}]}}
    inv = closed(dict(common_props, kind={"const": "investigation"}, directoryId={"type": "string", "minLength": 1}, files=files))
    app = closed(dict(common_props, kind={"const": "append"}, baseScenarioKey=c("slug"), expectedAggregateVersion=c("versionOrZero"),
                      appendedEvents=file))
    succ = closed(dict(common_props, kind={"const": "successor"}, baseScenarioKey=c("slug"), files=files))
    return doc("negative-fixture.schema.json", "Causa v1 negative fixture", {"oneOf": [inv, app, succ]},
               "A deliberately invalid input and the error a conforming reader must report. File bytes are base64 so invalid encodings are representable.")


def bundle():
    entry = closed({"path": {"type": "string", "pattern": "^[a-z0-9][A-Za-z0-9/._-]*\\.schema\\.json$"}, "sha256": c("sha256")})
    props = {"formatVersion": c("formatVersion"), "contractVersion": {"const": "1.0.0"}, "algorithm": {"const": "sha256"},
             "canonicalization": {"const": "RFC8785"}, "schemas": {"type": "array", "minItems": 1, "items": entry}}
    return doc("schema-bundle.schema.json", "Causa v1 schema bundle manifest (integrity metadata)", closed(props),
               "contract/v1.0.0/schemas/bundle.json. Each schema's digest is SHA-256 of its exact file bytes; the bundle identity is SHA-256 of the bundle manifest's exact file bytes.")


def relocate(value, doc_dir):
    """Refs are authored relative to the schemas root; rewrite them relative to each document."""
    if isinstance(value, dict):
        return {k: (relative_ref(v, doc_dir) if k == "$ref" else relocate(v, doc_dir)) for k, v in value.items()}
    if isinstance(value, list):
        return [relocate(v, doc_dir) for v in value]
    return value


def relative_ref(reference, doc_dir):
    if reference.startswith("#"):
        return reference
    target, _, fragment = reference.partition("#")
    rel = posixpath.relpath(target, doc_dir or ".")
    return rel + ("#" + fragment if fragment else "")


def scenarios():
    refusal = closed({"step": c("versionOrZero"), "expectedCode": {"type": "string", "pattern": "^[A-Z]+(\\.[A-Z_]+)+$"},
                      "engineCodes": {"type": "string", "description": "Every code the engine returned, joined by '; '."}})
    entry = closed({"scenarioKey": c("slug"), "investigationId": c("uuid"),
                    "commands": {"type": "integer", "minimum": 1, "description": "Commands issued by the generator: persisted command groups plus refused commands."},
                    "events": c("aggregateVersion"),
                    "eventTypes": {"type": "array", "uniqueItems": True, "items": {"type": "string"}, "description": "Distinct event types, sorted ordinally."},
                    "refusals": {"type": "array", "items": refusal, "description": "Commands the engine refused, as expected (never persisted). step is the 0-based generator step index."}})
    props = {"formatVersion": c("formatVersion"), "scenarios": {"type": "array", "minItems": 1, "items": entry}}
    return doc("golden-scenarios.schema.json", "Causa v1 golden scenario summary", closed(props),
               "contract/v1.0.0/golden/scenarios.json: informative generator summary; not authoritative.")


def main():
    out = Path(sys.argv[1])
    define_payloads()
    define_entities()
    docs = [common(), actor(), envelope(), state(), snapshot(), investigation_manifest(), root_manifest(), negative_fixture(), bundle(), scenarios()]
    docs += payload_docs() + entity_docs()
    for name, body in docs:
        body = relocate(body, posixpath.dirname(name))
        path = out / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(body, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(len(docs), "schemas written")


if __name__ == "__main__":
    main()
