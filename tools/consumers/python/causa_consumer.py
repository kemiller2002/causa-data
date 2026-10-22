#!/usr/bin/env python3
"""Independent (non-.NET) consumer of the Causa Storage Contract v1.0.0.

Written only from contract/v1.0.0/CONTRACT.md, contract/v1.0.0/schemas/**, contract/v1.0.0/golden/** and
fixtures/v1.0.0/**.  Standard library plus `jsonschema` / `referencing`.

Style: pure functions over immutable inputs; validation outcomes are explicit values (`Ok`, `Err`, `Withheld`), never
exceptions.  Exceptions raised by the standard library parser are caught at the boundary and converted to `Err`.

Semantic admissibility (CONTRACT §11.3) needs the pinned F# engine, which this consumer does not have.  It therefore
reports (at stage 15) ADMISSIBILITY_NOT_EVALUATED unless one of the *structurally implementable* rules below (derived only from
contract text) rejects the history.  No history is ever reported as fully valid.

Usage:  python3 causa_consumer.py verify <repoRoot>
"""
from __future__ import annotations

import base64
import hashlib
import json
import re
import sys
import uuid
from dataclasses import dataclass, field, replace
from pathlib import Path
from types import MappingProxyType
from typing import Any, Callable, Iterable, Mapping, Sequence

from jsonschema import Draft202012Validator, validators
from jsonschema.exceptions import ValidationError
from referencing import Registry, Resource

# ---------------------------------------------------------------------------------------------------------------------
# Result values
# ---------------------------------------------------------------------------------------------------------------------


@dataclass(frozen=True)
class Ok:
    value: Any = None


@dataclass(frozen=True)
class Err:
    code: str
    detail: str
    stage: int | None = None
    domain_code: str | None = None


@dataclass(frozen=True)
class Withheld:
    """A stage that could not be evaluated; processing continues, but the input is never reported valid."""

    value: Any
    detail: str


@dataclass(frozen=True)
class Check:
    status: str  # PASS | FAIL | NOT_EVALUATED
    name: str
    detail: str

    def line(self) -> str:
        return f"{self.status} {self.name} {self.detail}".rstrip()


def passed(name: str, detail: str = "") -> Check:
    return Check("PASS", name, detail)


def failed(name: str, detail: str) -> Check:
    return Check("FAIL", name, detail)


def not_evaluated(name: str, detail: str) -> Check:
    return Check("NOT_EVALUATED", name, detail)


def check_of(name: str, outcome: Ok | Err, ok_detail: str = "") -> Check:
    return passed(name, ok_detail) if isinstance(outcome, Ok) else failed(name, f"{outcome.code}: {outcome.detail}")


# ---------------------------------------------------------------------------------------------------------------------
# Constants from the contract
# ---------------------------------------------------------------------------------------------------------------------

V1 = "1.0.0"
MAX_SAFE = 2**53 - 1
UUID_RE = re.compile(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")
NIL_UUID = "00000000-0000-0000-0000-000000000000"
RUN_ID_RE = re.compile(r"[a-z0-9][a-z0-9-]{0,63}")
TIMESTAMP_PATTERN = (
    "^[0-9]{4}-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])T([01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9]\\.[0-9]{3}Z$"
)
TIMESTAMP_RE = re.compile(r"(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})\.(\d{3})Z", re.ASCII)
EVENT_ID_NAMESPACE_URL = "https://github.com/kemiller2002/causa-data/contract/v1/event-id"
EVENT_ID_NAMESPACE_EXPECTED = "241fefd4-c810-5a3a-9cfa-4bbd968ae1cb"
EVENT_ID_NAMESPACE = uuid.uuid5(uuid.NAMESPACE_URL, EVENT_ID_NAMESPACE_URL)
SCHEMA_BASE = "https://github.com/kemiller2002/causa-data/contract/v1.0.0/schemas/"
INVESTIGATION_FILES = ("manifest.json", "events.ndjson", "snapshot.json")

# §7, verbatim order.
EVENT_REGISTRY: tuple[str, ...] = tuple(
    s.strip()
    for s in """InvestigationStarted, LifecycleChanged, ObservationRecorded, HypothesisRecorded, HypothesisTransitioned,
EvidenceAdmitted, EvidenceSuperseded, EvidenceVerificationAssessed, EvidenceRelated, UnknownIdentified,
UnknownTransitioned, AssumptionRecorded, AssumptionTransitioned, ContradictionRecorded, ContradictionTransitioned,
PredictionRecorded, ActionRecorded, ActionTransitioned, FindingRecorded, NegativeKnowledgeEstablished,
CausalAssertionRecorded, CausalAssertionConfirmed, CausalAssertionReopened, CorrectiveActionRecorded,
CorrectiveActionTransitioned, CriterionRecorded, VerificationRecorded, ConclusionRecorded, ProposalRecorded,
ProposalWasAccepted, ProposalWasRejected""".split(",")
)

# §11.1 stage numbers.
STAGES = MappingProxyType(
    {
        1: "CAUSA.PATH_INVALID",
        2: "CAUSA.ENCODING",
        3: "CAUSA.JSON_MALFORMED",
        4: "CAUSA.NON_CANONICAL",
        5: "CAUSA.UNSUPPORTED_VERSION",
        6: "CAUSA.UNKNOWN_EVENT_TYPE",
        7: "CAUSA.SCHEMA_VIOLATION",
        8: "CAUSA.MANIFEST_MISMATCH",
        9: "CAUSA.EVENTS_HASH_MISMATCH",
        10: "CAUSA.INVESTIGATION_MISMATCH",
        11: "CAUSA.EVENT_ID_DUPLICATE",
        12: "CAUSA.SEQUENCE_INVALID",
        13: "CAUSA.EVENT_ID_MISMATCH",
        14: "CAUSA.CORRELATION_REUSED",
        15: "CAUSA.HISTORY_INADMISSIBLE",
        16: "CAUSA.SNAPSHOT_HASH_MISMATCH",
        17: "CAUSA.SNAPSHOT_MISMATCH",
    }
)
ADMISSIBILITY_NOT_EVALUATED = "ADMISSIBILITY_NOT_EVALUATED"


def err(stage: int, detail: str, domain_code: str | None = None) -> Err:
    return Err(STAGES[stage], detail, stage, domain_code)


# ---------------------------------------------------------------------------------------------------------------------
# §2 strict reading: encoding, framing, JSON, canonical form
# ---------------------------------------------------------------------------------------------------------------------

BOM = b"\xef\xbb\xbf"


def decode_utf8(raw: bytes, name: str) -> Ok | Err:
    """§2.1: UTF-8, no BOM, no CR anywhere."""
    if raw.startswith(BOM):
        return err(2, f"{name}: starts with a UTF-8 byte order mark")
    try:
        text = raw.decode("utf-8", errors="strict")  # rejects overlongs, encoded surrogates, truncation
    except UnicodeDecodeError as e:
        return err(2, f"{name}: invalid UTF-8 at byte {e.start}")
    if "\r" in text:
        return err(2, f"{name}: contains CR")
    return Ok(text)


def frame_single(text: str, name: str) -> Ok | Err:
    """§2.2: a JSON file is one canonical text followed by exactly one LF.

    Framing (stage 2) only requires the terminating LF; interior whitespace/LFs are left to the canonical check
    (stage 4).  Revised §2.2: framing covers only an empty file, CR, a missing final LF and NDJSON blank lines.
    """
    if text == "":
        return err(2, f"{name}: empty file")
    if not text.endswith("\n"):
        return err(2, f"{name}: not terminated by LF")
    return Ok(text[:-1])


def frame_ndjson(text: str, name: str) -> Ok | Err:
    """§2.2: one text per line, each terminated by LF, no blank lines."""
    if text == "":
        return err(2, f"{name}: empty file")
    if not text.endswith("\n"):
        return err(2, f"{name}: last line not terminated by LF")
    lines = tuple(text[:-1].split("\n"))
    blank = next((i for i, l in enumerate(lines) if l == ""), None)
    if blank is not None:
        return err(2, f"{name}: blank line {blank + 1}")
    return Ok(lines)


class _Reject(Exception):
    pass


def _pairs(pairs: list[tuple[str, Any]]) -> dict:
    keys = [k for k, _ in pairs]
    if len(set(keys)) != len(keys):
        dup = next(k for k in keys if keys.count(k) > 1)
        raise _Reject(f"duplicate key {dup!r}")
    return dict(pairs)


def _no_float(s: str) -> Any:
    raise _Reject(f"non-integer number {s!r}")


def _no_constant(s: str) -> Any:
    raise _Reject(f"non-JSON constant {s!r}")


def _has_lone_surrogate(s: str) -> bool:
    return any(0xD800 <= ord(c) <= 0xDFFF for c in s)


def _value_problem(v: Any) -> str | None:
    """Integers in safe range, no lone surrogates (in keys or strings)."""
    if isinstance(v, bool) or v is None:
        return None
    if isinstance(v, int):
        return None if -MAX_SAFE <= v <= MAX_SAFE else f"integer {v} outside [-(2^53-1), 2^53-1]"
    if isinstance(v, str):
        if "\ufeff" in v:
            return "U+FEFF inside a string (§2.3)"
        return "unpaired UTF-16 surrogate in string" if _has_lone_surrogate(v) else None
    if isinstance(v, list):
        return next((p for p in map(_value_problem, v) if p), None)
    if isinstance(v, dict):
        return next(
            (
                p
                for k, x in v.items()
                for p in (("unpaired UTF-16 surrogate or U+FEFF in key" if _has_lone_surrogate(k) or "\ufeff" in k else None),
                          _value_problem(x))
                if p
            ),
            None,
        )
    return f"unexpected value type {type(v).__name__}"


def parse_json(text: str, name: str) -> Ok | Err:
    """§2.3 / stage 3: RFC 8259 syntax, unique keys, integers only (safe range), no lone surrogates, no U+FEFF."""
    if "\ufeff" in text:
        return err(3, f"{name}: U+FEFF at char {text.index(chr(0xFEFF))} (only a leading BOM is ENCODING, §2.3)")
    try:
        value = json.loads(
            text, object_pairs_hook=_pairs, parse_float=_no_float, parse_constant=_no_constant, strict=True
        )
    except _Reject as e:
        return err(3, f"{name}: {e}")
    except (ValueError, RecursionError) as e:
        return err(3, f"{name}: {e}")
    problem = _value_problem(value)
    return err(3, f"{name}: {problem}") if problem else Ok(value)


_JCS_ESC = MappingProxyType({'"': '\\"', "\\": "\\\\", "\b": "\\b", "\f": "\\f", "\n": "\\n", "\r": "\\r", "\t": "\\t"})


def _jcs_string(s: str) -> str:
    return '"' + "".join(_JCS_ESC.get(c) or (f"\\u{ord(c):04x}" if ord(c) < 0x20 else c) for c in s) + '"'


def canonical(v: Any) -> str:
    """RFC 8785 for the integer-only value domain of the contract (no floats exist after stage 3)."""
    if v is None:
        return "null"
    if v is True:
        return "true"
    if v is False:
        return "false"
    if isinstance(v, int):
        return str(v)
    if isinstance(v, str):
        return _jcs_string(v)
    if isinstance(v, (list, tuple)):
        return "[" + ",".join(canonical(x) for x in v) + "]"
    if isinstance(v, Mapping):
        keys = sorted(v.keys(), key=lambda k: k.encode("utf-16-be"))  # UTF-16 code unit order
        return "{" + ",".join(_jcs_string(k) + ":" + canonical(v[k]) for k in keys) + "}"
    raise TypeError(f"not a JSON value: {type(v).__name__}")


def canonical_file_bytes(v: Any) -> bytes:
    return (canonical(v) + "\n").encode("utf-8")


def sha256_hex(b: bytes) -> str:
    return hashlib.sha256(b).hexdigest()


def json_digest(v: Any) -> str:
    """§2.11: JSON documents are hashed over canonical text without the trailing LF."""
    return sha256_hex(canonical(v).encode("utf-8"))


def check_canonical(text: str, value: Any, name: str) -> Ok | Err:
    c = canonical(value)
    if c == text:
        return Ok(value)
    pos = next((i for i, (a, b) in enumerate(zip(c, text)) if a != b), min(len(c), len(text)))
    return err(4, f"{name}: not RFC 8785 canonical (first difference at char {pos})")


def read_single_strict(raw: bytes, name: str) -> Ok | Err:
    """Stages 2-4 for a single-document JSON file; returns the parsed value."""
    r = decode_utf8(raw, name)
    if isinstance(r, Err):
        return r
    r = frame_single(r.value, name)
    if isinstance(r, Err):
        return r
    body = r.value
    p = parse_json(body, name)
    if isinstance(p, Err):
        return p
    return check_canonical(body, p.value, name)


# ---------------------------------------------------------------------------------------------------------------------
# §2.9 timestamps
# ---------------------------------------------------------------------------------------------------------------------


def _days_in_month(y: int, m: int) -> int:
    if m == 2:
        return 29 if (y % 4 == 0 and y % 100 != 0) or y % 400 == 0 else 28
    return 30 if m in (4, 6, 9, 11) else 31


def timestamp_problem(s: str) -> str | None:
    """None if `s` is a strict v1 timestamp that names a real calendar instant (proleptic Gregorian, no leap second)."""
    m = TIMESTAMP_RE.fullmatch(s)
    if not m:
        return f"{s!r} is not YYYY-MM-DDTHH:MM:SS.mmmZ"
    y, mo, d, hh, mi, ss, _ = (int(g) for g in m.groups())
    if y < 1:
        return f"{s!r}: year 0000 is outside 0001-9999 (§2.9)"
    if not 1 <= mo <= 12:
        return f"{s!r}: month {mo}"
    if not 1 <= d <= _days_in_month(y, mo):
        return f"{s!r}: day {d} does not exist in {y:04d}-{mo:02d}"
    if hh > 23 or mi > 59 or ss > 59:
        return f"{s!r}: time out of range (leap seconds are invalid)"
    return None


# ---------------------------------------------------------------------------------------------------------------------
# Schemas (stage 7) and bundle identity (§10.3)
# ---------------------------------------------------------------------------------------------------------------------


def _ecma_pattern(p: str) -> str:
    """JSON Schema patterns are ECMA-262.  Without the 'm' flag ECMA '$' matches only at end of input, whereas Python
    '$' also matches before a final '\\n'.  Translate unescaped '$' outside character classes to '\\Z'."""
    out, i, in_class = [], 0, False
    while i < len(p):
        c = p[i]
        if c == "\\" and i + 1 < len(p):
            out.append(p[i : i + 2])
            i += 2
            continue
        if c == "[" and not in_class:
            in_class = True
        elif c == "]" and in_class:
            in_class = False
        out.append("\\Z" if (c == "$" and not in_class) else c)
        i += 1
    return "".join(out)


def _pattern_keyword(validator, patrn, instance, schema):
    if not validator.is_type(instance, "string"):
        return
    if not re.search(_ecma_pattern(patrn), instance):
        yield ValidationError(f"{instance!r} does not match {patrn!r}")
        return
    if patrn == TIMESTAMP_PATTERN:
        problem = timestamp_problem(instance)
        if problem:
            yield ValidationError(f"calendar-invalid timestamp: {problem}")


# `version=` registers the class for the Draft 2020-12 meta-schema URI.  Without it, jsonschema's `evolve()` re-selects
# the stock Draft202012Validator (via `validator_for`) whenever a $ref enters a resource that declares "$schema" (every
# payload/entity schema), and the override silently stops applying inside payloads.
StrictValidator = validators.extend(Draft202012Validator, {"pattern": _pattern_keyword}, version="draft2020-12-causa-strict")


@dataclass(frozen=True)
class SchemaSet:
    files: Mapping[str, bytes]  # relative path -> exact bytes
    contents: Mapping[str, Any]  # relative path -> parsed schema
    registry: Registry

    def validator(self, rel: str):
        return StrictValidator(self.contents[rel], registry=self.registry)


def load_schemas(schema_dir: Path) -> Ok | Err:
    paths = sorted((p.relative_to(schema_dir).as_posix() for p in schema_dir.rglob("*.schema.json")))
    files = {rel: (schema_dir / rel).read_bytes() for rel in paths}
    contents: dict[str, Any] = {}
    problems: list[str] = []
    for rel, raw in files.items():
        r = decode_utf8(raw, rel)
        p = parse_json(r.value, rel) if isinstance(r, Ok) else r
        if isinstance(p, Err):
            problems.append(f"{p.code} {p.detail}")
            continue
        sid = p.value.get("$id") if isinstance(p.value, dict) else None
        if sid != SCHEMA_BASE + rel:
            problems.append(f"{rel}: $id {sid!r} != {SCHEMA_BASE + rel!r}")
        try:
            Draft202012Validator.check_schema(p.value)
        except Exception as e:  # noqa: BLE001 - boundary conversion
            problems.append(f"{rel}: not a valid Draft 2020-12 schema: {e}")
        contents[rel] = p.value
    if problems:
        return Err("SCHEMA_SET", "; ".join(problems))
    registry = Registry().with_resources(
        (c["$id"], Resource.from_contents(c)) for c in contents.values()
    )
    return Ok(SchemaSet(MappingProxyType(files), MappingProxyType(contents), registry))


def schema_errors(schemas: SchemaSet, rel: str, instance: Any) -> tuple[str, ...]:
    v = schemas.validator(rel)
    return tuple(
        f"/{'/'.join(map(str, e.absolute_path))}: {e.message[:200]}"
        for e in sorted(v.iter_errors(instance), key=lambda e: list(map(str, e.absolute_path)))
    )


def recompute_bundle(schemas: SchemaSet) -> dict:
    return {
        "algorithm": "sha256",
        "canonicalization": "RFC8785",
        "contractVersion": V1,
        "formatVersion": V1,
        "schemas": [
            {"path": rel, "sha256": sha256_hex(raw)}
            for rel, raw in sorted(schemas.files.items(), key=lambda kv: kv[0].encode("utf-16-be"))
        ],
    }


# ---------------------------------------------------------------------------------------------------------------------
# §5.2 event identity
# ---------------------------------------------------------------------------------------------------------------------


def derive_event_id(investigation_id: str, aggregate_version: int, correlation_id: str) -> str:
    return str(uuid.uuid5(EVENT_ID_NAMESPACE, f"{investigation_id}/{aggregate_version}/{correlation_id}"))


# ---------------------------------------------------------------------------------------------------------------------
# §8.3 projection fold
# ---------------------------------------------------------------------------------------------------------------------

COLLECTIONS: tuple[str, ...] = (
    "observations", "hypotheses", "evidence", "relationships", "unknowns", "assumptions", "contradictions",
    "predictions", "actions", "findings", "negativeKnowledge", "causes", "correctiveActions", "criteria",
    "verifications", "conclusions", "proposals",
)  # fmt: skip


def _prov(ev: Mapping) -> dict:
    return {"actor": ev["actor"], "recordedAt": ev["occurredAt"]}


def _copy(p: Mapping, *keys: str) -> dict:
    return {k: p[k] for k in keys}


# eventType -> (collection, id key in payload, entity builder(payload, event) without id/provenance)
CREATES: Mapping[str, tuple[str, str, Callable[[Mapping, Mapping], dict]]] = MappingProxyType(
    {
        "ObservationRecorded": ("observations", "observationId", lambda p, e: _copy(p, "original", "observedAt")),
        "HypothesisRecorded": (
            "hypotheses", "hypothesisId",
            lambda p, e: {**_copy(p, "statement", "mechanism"), "state": "proposed", "history": []},
        ),
        "EvidenceAdmitted": (
            "evidence", "evidenceId",
            lambda p, e: {
                **_copy(p, "contentKind", "content", "category", "source", "capturedAt"),
                "verification": "unverified", "supersededBy": None, "admittedAtVersion": e["aggregateVersion"],
            },
        ),
        "EvidenceRelated": (
            "relationships", "relationshipId",
            lambda p, e: _copy(p, "evidenceId", "hypothesisId", "kind", "rationale"),
        ),
        "UnknownIdentified": (
            "unknowns", "unknownId",
            lambda p, e: {**_copy(p, "question", "materiality"), "state": "identified", "basis": None},
        ),
        "AssumptionRecorded": (
            "assumptions", "assumptionId",
            lambda p, e: {**_copy(p, "proposition", "materiality"), "state": "explicitUnverified"},
        ),
        "ContradictionRecorded": (
            "contradictions", "contradictionId",
            lambda p, e: {**_copy(p, "statement", "evidenceIds", "materiality"), "state": "detected", "basis": None},
        ),
        "PredictionRecorded": (
            "predictions", "predictionId",
            lambda p, e: _copy(p, "hypothesisId", "statement", "strength", "timing"),
        ),
        "ActionRecorded": (
            "actions", "actionId",
            lambda p, e: {**_copy(p, "kind", "purpose", "targetHypothesisIds"), "state": "proposed"},
        ),
        "FindingRecorded": (
            "findings", "findingId", lambda p, e: _copy(p, "actionId", "originalResult", "interpretation"),
        ),
        "NegativeKnowledgeEstablished": (
            "negativeKnowledge", "negativeKnowledgeId",
            lambda p, e: _copy(p, "statement", "scope", "basisEvidenceIds"),
        ),
        "CausalAssertionRecorded": (
            "causes", "causeId",
            lambda p, e: {**_copy(p, "outcome", "members", "mechanism"), "state": "proposed"},
        ),
        "CorrectiveActionRecorded": (
            "correctiveActions", "correctiveActionId",
            lambda p, e: {
                **_copy(p, "kind", "description", "targetCauseIds"), "state": "proposed", "awaitingSinceVersion": 0,
            },
        ),
        "CriterionRecorded": (
            "criteria", "criterionId", lambda p, e: _copy(p, "correctiveActionId", "expectedOutcome", "method"),
        ),
        "VerificationRecorded": (
            "verifications", "verificationId",
            lambda p, e: {**_copy(p, "criterionId", "outcome", "evidenceIds"), "recordedAtVersion": e["aggregateVersion"]},
        ),
        "ConclusionRecorded": (
            "conclusions", "conclusionId",
            lambda p, e: {
                **_copy(p, "statement", "strength", "causeIds", "residualUncertainty"),
                "recordedAtVersion": e["aggregateVersion"],
            },
        ),
        "ProposalRecorded": (
            "proposals", "proposalId",
            lambda p, e: {**_copy(p, "kind", "text", "rationale"), "state": "active", "decision": None},
        ),
    }
)  # fmt: skip


def _hyp_change(ent: Mapping, p: Mapping, e: Mapping) -> dict:
    change = {**_copy(p, "from", "to", "command", "reason", "evidenceIds"), "at": e["occurredAt"],
              "aggregateVersion": e["aggregateVersion"]}  # fmt: skip
    return {**ent, "state": p["to"], "history": [*ent["history"], change]}


def _ca_transition(ent: Mapping, p: Mapping, e: Mapping) -> dict:
    since = e["aggregateVersion"] if p["state"] == "awaitingVerificationOfEffect" else ent["awaitingSinceVersion"]
    return {**ent, "state": p["state"], "awaitingSinceVersion": since}


# eventType -> (collection, id key in payload, entity updater(entity, payload, event))
UPDATES: Mapping[str, tuple[str, str, Callable[[Mapping, Mapping, Mapping], dict]]] = MappingProxyType(
    {
        "HypothesisTransitioned": ("hypotheses", "hypothesisId", _hyp_change),
        "EvidenceSuperseded": ("evidence", "evidenceId", lambda x, p, e: {**x, "supersededBy": p["replacementEvidenceId"]}),
        "EvidenceVerificationAssessed": ("evidence", "evidenceId", lambda x, p, e: {**x, "verification": p["verification"]}),
        "UnknownTransitioned": ("unknowns", "unknownId", lambda x, p, e: {**x, "state": p["state"], "basis": p["basis"]}),
        "AssumptionTransitioned": ("assumptions", "assumptionId", lambda x, p, e: {**x, "state": p["state"]}),
        "ContradictionTransitioned": (
            "contradictions", "contradictionId", lambda x, p, e: {**x, "state": p["state"], "basis": p["basis"]},
        ),
        "ActionTransitioned": ("actions", "actionId", lambda x, p, e: {**x, "state": p["state"]}),
        "CausalAssertionConfirmed": ("causes", "causeId", lambda x, p, e: {**x, "state": "confirmed"}),
        "CausalAssertionReopened": ("causes", "causeId", lambda x, p, e: {**x, "state": "reopened"}),
        "CorrectiveActionTransitioned": ("correctiveActions", "correctiveActionId", _ca_transition),
        "ProposalWasAccepted": ("proposals", "proposalId", lambda x, p, e: {**x, "state": "accepted"}),
        "ProposalWasRejected": ("proposals", "proposalId", lambda x, p, e: {**x, "state": "rejected", "decision": p["reason"]}),
    }
)  # fmt: skip

assert set(CREATES) | set(UPDATES) | {"InvestigationStarted", "LifecycleChanged"} == set(EVENT_REGISTRY)


def _with_entity(state: Mapping, coll: str, ent_id: str, ent: Mapping) -> dict:
    return {**state, coll: {**state[coll], ent_id: ent}}


def apply_event(state: Mapping | None, ev: Mapping) -> Mapping | None:
    """One step of the §8.3 fold.  Internal state keeps each collection as {id: entity}; returns a new mapping."""
    t, p, v = ev["eventType"], ev["payload"], ev["aggregateVersion"]
    if t == "InvestigationStarted":
        base = {c: {} for c in COLLECTIONS}
        obs = {"id": p["observationId"], "original": p["original"], "observedAt": None, "provenance": _prov(ev)}
        return {**base, "investigationId": ev["investigationId"], "title": p["title"], "lifecycle": "draft",
                "createdAt": ev["occurredAt"], "version": v, "observations": {obs["id"]: obs}}  # fmt: skip
    if state is None:
        return None  # "Start with no state": nothing to update before InvestigationStarted
    state = {**state, "version": v}
    if t == "LifecycleChanged":
        return {**state, "lifecycle": p["to"]}
    if t in CREATES:
        coll, key, build = CREATES[t]
        return _with_entity(state, coll, p[key], {"id": p[key], **build(p, ev), "provenance": _prov(ev)})
    coll, key, upd = UPDATES[t]
    current = state[coll].get(p[key])
    return state if current is None else _with_entity(state, coll, p[key], upd(current, p, ev))


def finalize_state(state: Mapping) -> dict:
    """Entity arrays sorted by id, ordinal UUID text comparison."""
    head = {k: state[k] for k in ("investigationId", "title", "lifecycle", "version", "createdAt")}
    return {**head, **{c: [state[c][k] for k in sorted(state[c])] for c in COLLECTIONS}}


def project(events: Sequence[Mapping]) -> Mapping | None:
    state: Mapping | None = None
    for ev in sorted(events, key=lambda e: e["aggregateVersion"]):
        state = apply_event(state, ev)
    return None if state is None else finalize_state(state)


def build_snapshot(events: Sequence[Mapping], state: Mapping) -> dict:
    last = max(events, key=lambda e: e["aggregateVersion"])
    return {
        "formatVersion": V1,
        "investigationId": state["investigationId"],
        "aggregateVersion": last["aggregateVersion"],
        "projectionVersion": 1,
        "semanticVersion": V1,
        "generatedAt": last["occurredAt"],
        "state": state,
    }


def build_manifest(investigation_id: str, events_bytes: bytes, events: Sequence[Mapping], snapshot: Mapping) -> dict:
    """§9.1 (used only to build a candidate file set for an `append` fixture, never written to disk)."""
    return {
        "formatVersion": V1, "investigationId": investigation_id, "schemaVersion": V1, "semanticVersion": V1,
        "aggregateVersion": len(events), "eventCount": len(events), "eventsSha256": sha256_hex(events_bytes),
        "snapshotVersion": 1, "snapshotSha256": json_digest(snapshot), "domainPacks": [],
        "createdAt": events[0]["occurredAt"], "updatedAt": events[-1]["occurredAt"],
    }  # fmt: skip


# ---------------------------------------------------------------------------------------------------------------------
# Stage 15: structurally implementable admissibility rules (necessary, NOT sufficient conditions)
# ---------------------------------------------------------------------------------------------------------------------
#
# S1  §5.1  the history begins with InvestigationStarted                              -> HISTORY.NO_START
# S2  §5.4  a command group (adjacent events, same correlationId) shares occurredAt and actor
#           (non-adjacent reuse of a correlationId is stage 14, CAUSA.CORRELATION_REUSED)
# S3  §8.3  updates name an existing entity ("cannot occur in an admissible history")
# S4  §11.3 entity ids are unique (a create never reuses an id of the same collection)
# S5  §7.3  recorded `from` equals the replayed prior state (HypothesisTransitioned, LifecycleChanged)
# S6  §7    ProposalWasAccepted.targetId is the id created by the preceding event of the same correlationId
# Domain codes for S2-S6 are this consumer's best mapping onto the §11.3 code list (HISTORY.INADMISSIBLE_EVENT).

INADMISSIBLE_EVENT = "HISTORY.INADMISSIBLE_EVENT"


def _created_id(ev: Mapping) -> str | None:
    t = ev["eventType"]
    return ev["payload"][CREATES[t][1]] if t in CREATES else None


def structural_admissibility(events: Sequence[Mapping]) -> Ok | Err:
    if not events or events[0]["eventType"] != "InvestigationStarted":
        first = events[0]["eventType"] if events else "nothing"
        return err(15, f"S1: history begins with {first}, not InvestigationStarted (§5.1)", "HISTORY.NO_START")
    state: Mapping | None = None
    prev: Mapping | None = None
    for ev in events:
        t, p, v, cid = ev["eventType"], ev["payload"], ev["aggregateVersion"], ev["correlationId"]
        at = f"aggregateVersion {v} ({t})"
        same_group = prev is not None and prev["correlationId"] == cid
        if same_group and (prev["occurredAt"] != ev["occurredAt"] or prev["actor"] != ev["actor"]):
            return err(15, f"S2: {at}: command group {cid} does not share occurredAt/actor (§5.4)", INADMISSIBLE_EVENT)
        if t == "InvestigationStarted" and state is not None:
            return err(15, f"S4: {at}: second InvestigationStarted", INADMISSIBLE_EVENT)
        if t == "LifecycleChanged" and p["from"] != state["lifecycle"]:
            return err(15, f"S5: {at}: from={p['from']} but replayed lifecycle is {state['lifecycle']}", INADMISSIBLE_EVENT)
        if t in CREATES:
            coll, key, _ = CREATES[t]
            if p[key] in state[coll]:
                return err(15, f"S4: {at}: {coll} id {p[key]} already exists", INADMISSIBLE_EVENT)
        if t in UPDATES:
            coll, key, _ = UPDATES[t]
            current = state[coll].get(p[key])
            if current is None:
                return err(15, f"S3: {at}: no {coll} entity {p[key]}", INADMISSIBLE_EVENT)
            if t == "HypothesisTransitioned" and p["from"] != current["state"]:
                return err(15, f"S5: {at}: from={p['from']} but replayed state is {current['state']}", INADMISSIBLE_EVENT)
        if t == "ProposalWasAccepted":
            created = _created_id(prev) if same_group else None
            if created != p["targetId"]:
                return err(15, f"S6: {at}: targetId {p['targetId']} is not the entity created by the preceding "
                               f"event of command {cid} ({created})", INADMISSIBLE_EVENT)  # fmt: skip
        state = apply_event(state, ev)
        prev = ev
    return Ok(None)


def referential_notes(events: Sequence[Mapping]) -> tuple[str, ...]:
    """Non-normative: cross-references to ids never created.  The contract does not state referential integrity as a
    rule, so these are reported as information only and never change a verdict."""
    known: dict[str, set] = {c: set() for c in COLLECTIONS}
    refs = {
        "EvidenceRelated": (("evidence", "evidenceId"), ("hypotheses", "hypothesisId")),
        "PredictionRecorded": (("hypotheses", "hypothesisId"),),
        "FindingRecorded": (("actions", "actionId"),),
        "CriterionRecorded": (("correctiveActions", "correctiveActionId"),),
        "VerificationRecorded": (("criteria", "criterionId"), ("evidence", "evidenceIds")),
        "ActionRecorded": (("hypotheses", "targetHypothesisIds"),),
        "CorrectiveActionRecorded": (("causes", "targetCauseIds"),),
        "ConclusionRecorded": (("causes", "causeIds"),),
        "NegativeKnowledgeEstablished": (("evidence", "basisEvidenceIds"),),
        "ContradictionRecorded": (("evidence", "evidenceIds"),),
        "HypothesisTransitioned": (("evidence", "evidenceIds"),),
        "EvidenceSuperseded": (("evidence", "replacementEvidenceId"),),
    }
    notes = []
    for ev in events:
        t, p = ev["eventType"], ev["payload"]
        for coll, key in refs.get(t, ()):
            ids = p[key] if isinstance(p[key], list) else [p[key]]
            notes += [f"v{ev['aggregateVersion']} {t}.{key}->{i}" for i in ids if i not in known[coll]]
        if t == "CausalAssertionRecorded":
            notes += [f"v{ev['aggregateVersion']} member->{m['hypothesisId']}" for m in p["members"]
                      if m["hypothesisId"] not in known["hypotheses"]]  # fmt: skip
        cid = _created_id(ev)
        if cid:
            known[CREATES[t][0]].add(cid)
    return tuple(notes)


# ---------------------------------------------------------------------------------------------------------------------
# §11.1 investigation pipeline
# ---------------------------------------------------------------------------------------------------------------------


@dataclass(frozen=True)
class Ctx:
    directory: str
    raw: Mapping[str, bytes]
    texts: Mapping[str, str] = field(default_factory=dict)
    lines: tuple[str, ...] = ()
    manifest: Any = None
    snapshot: Any = None
    events: tuple[Any, ...] = ()
    state: Any = None


@dataclass(frozen=True)
class PipelineResult:
    primary: Err | None
    stage_outcomes: tuple[tuple[int, str, str], ...]  # (stage, PASS|FAIL|NOT_EVALUATED|SKIPPED, detail)
    ctx: Ctx

    @property
    def withheld(self) -> bool:
        return any(s == "NOT_EVALUATED" for _, s, _ in self.stage_outcomes)


def s1_path(c: Ctx, _: SchemaSet) -> Ok | Err:
    d = c.directory
    if not UUID_RE.fullmatch(d) or d == NIL_UUID:
        return err(1, f"directory name {d!r} is not a canonical non-nil UUID (§3.2)")
    if sorted(c.raw) != sorted(INVESTIGATION_FILES):
        return err(1, f"directory holds {sorted(c.raw)}, not exactly {sorted(INVESTIGATION_FILES)} (§3.3)")
    return Ok(c)


def s2_encoding(c: Ctx, _: SchemaSet) -> Ok | Err:
    texts = {}
    for name in INVESTIGATION_FILES:
        r = decode_utf8(c.raw[name], name)
        if isinstance(r, Err):
            return r
        f = frame_ndjson(r.value, name) if name == "events.ndjson" else frame_single(r.value, name)
        if isinstance(f, Err):
            return f
        texts[name] = f.value
    return Ok(replace(c, texts=MappingProxyType({k: v for k, v in texts.items() if k != "events.ndjson"}),
                      lines=texts["events.ndjson"]))  # fmt: skip


def s3_json(c: Ctx, _: SchemaSet) -> Ok | Err:
    m = parse_json(c.texts["manifest.json"], "manifest.json")
    if isinstance(m, Err):
        return m
    evs = []
    for i, line in enumerate(c.lines, 1):
        e = parse_json(line, f"events.ndjson line {i}")
        if isinstance(e, Err):
            return e
        evs.append(e.value)
    s = parse_json(c.texts["snapshot.json"], "snapshot.json")
    if isinstance(s, Err):
        return s
    return Ok(replace(c, manifest=m.value, events=tuple(evs), snapshot=s.value))


def s4_canonical(c: Ctx, _: SchemaSet) -> Ok | Err:
    pairs = [("manifest.json", c.texts["manifest.json"], c.manifest), ("snapshot.json", c.texts["snapshot.json"], c.snapshot)]
    pairs += [(f"events.ndjson line {i}", l, e) for i, (l, e) in enumerate(zip(c.lines, c.events), 1)]
    bad = next((r for r in (check_canonical(t, v, n) for n, t, v in pairs) if isinstance(r, Err)), None)
    return bad or Ok(c)


def _version_problems(doc: Any, name: str, keys: Iterable[str]) -> list[str]:
    if not isinstance(doc, dict):
        return []
    return [f"{name}.{k}={doc[k]!r}" for k in keys if k in doc and doc[k] != V1]


def s5_versions(c: Ctx, _: SchemaSet) -> Ok | Err:
    probs = _version_problems(c.manifest, "manifest", ("formatVersion", "schemaVersion", "semanticVersion"))
    for i, e in enumerate(c.events, 1):
        probs += _version_problems(e, f"event[{i}]", ("formatVersion", "semanticVersion"))
    probs += _version_problems(c.snapshot, "snapshot", ("formatVersion", "semanticVersion"))
    return err(5, "; ".join(probs[:3])) if probs else Ok(c)


def s6_event_types(c: Ctx, _: SchemaSet) -> Ok | Err:
    for i, e in enumerate(c.events, 1):
        t = e.get("eventType") if isinstance(e, dict) else None
        if isinstance(t, str) and t not in EVENT_REGISTRY:
            return err(6, f"events.ndjson line {i}: eventType {t!r} is not registered (§7.1)")
    return Ok(c)


def s7_schema(c: Ctx, schemas: SchemaSet) -> Ok | Err:
    targets = [("manifest.json", "investigation-manifest.schema.json", c.manifest)]
    targets += [(f"events.ndjson line {i}", "event-envelope.schema.json", e) for i, e in enumerate(c.events, 1)]
    targets += [("snapshot.json", "snapshot.schema.json", c.snapshot)]
    for name, rel, inst in targets:
        errs = schema_errors(schemas, rel, inst)
        if errs:
            return err(7, f"{name}: {errs[0]}" + (f" (+{len(errs) - 1} more)" if len(errs) > 1 else ""))
    return Ok(c)


def s8_manifest(c: Ctx, _: SchemaSet) -> Ok | Err:
    m, n = c.manifest, len(c.events)
    probs = []
    if m["investigationId"] != c.directory:
        probs.append(f"investigationId {m['investigationId']} != directory {c.directory}")
    if m["aggregateVersion"] != n or m["eventCount"] != n:
        probs.append(f"aggregateVersion={m['aggregateVersion']} eventCount={m['eventCount']} lines={n}")
    if n and m["createdAt"] != c.events[0]["occurredAt"]:
        probs.append(f"createdAt {m['createdAt']} != first occurredAt {c.events[0]['occurredAt']}")
    if n and m["updatedAt"] != c.events[-1]["occurredAt"]:
        probs.append(f"updatedAt {m['updatedAt']} != last occurredAt {c.events[-1]['occurredAt']}")
    return err(8, "; ".join(probs)) if probs else Ok(c)


def s9_events_hash(c: Ctx, _: SchemaSet) -> Ok | Err:
    actual = sha256_hex(c.raw["events.ndjson"])
    return Ok(c) if actual == c.manifest["eventsSha256"] else err(9, f"eventsSha256 {c.manifest['eventsSha256']} != {actual}")


def s10_investigation(c: Ctx, _: SchemaSet) -> Ok | Err:
    bad = next(((i, e) for i, e in enumerate(c.events, 1) if e["investigationId"] != c.manifest["investigationId"]), None)
    return err(10, f"line {bad[0]} names investigation {bad[1]['investigationId']}") if bad else Ok(c)


def s11_event_id_unique(c: Ctx, _: SchemaSet) -> Ok | Err:
    ids = [e["eventId"] for e in c.events]
    dup = next((x for i, x in enumerate(ids) if x in ids[:i]), None)
    return err(11, f"eventId {dup} appears more than once") if dup else Ok(c)


def s12_sequence(c: Ctx, _: SchemaSet) -> Ok | Err:
    bad = next(((i, e["aggregateVersion"]) for i, e in enumerate(c.events, 1) if e["aggregateVersion"] != i), None)
    return err(12, f"line {bad[0]} has aggregateVersion {bad[1]} (expected {bad[0]})") if bad else Ok(c)


def s13_event_id_derivation(c: Ctx, _: SchemaSet) -> Ok | Err:
    for e in c.events:
        want = derive_event_id(e["investigationId"], e["aggregateVersion"], e["correlationId"])
        if e["eventId"] != want:
            return err(13, f"aggregateVersion {e['aggregateVersion']}: eventId {e['eventId']} != UUIDv5 {want}")
    return Ok(c)


def s14_correlation(c: Ctx, _: SchemaSet) -> Ok | Err:
    """§5.4: a correlationId names exactly one group of adjacent events."""
    seen: set[str] = set()
    prev = None
    for e in c.events:
        cid = e["correlationId"]
        if cid != prev:
            if cid in seen:
                return err(14, f"aggregateVersion {e['aggregateVersion']}: correlationId {cid} reused by a non-adjacent group")
            seen.add(cid)
        prev = cid
    return Ok(c)


def s15_admissibility(c: Ctx, _: SchemaSet) -> Ok | Err | Withheld:
    r = structural_admissibility(c.events)
    if isinstance(r, Err):
        return r
    notes = referential_notes(c.events)
    info = f"; non-normative dangling refs: {len(notes)}" + (f" {notes[:3]}" if notes else "")
    return Withheld(c, f"{ADMISSIBILITY_NOT_EVALUATED}: no engine; structural rules S1-S6 hold{info}")


def s16_snapshot_hash(c: Ctx, _: SchemaSet) -> Ok | Err:
    actual = json_digest(c.snapshot)
    want = c.manifest["snapshotSha256"]
    return Ok(c) if actual == want else err(16, f"snapshotSha256 {want} != {actual}")


def s17_snapshot(c: Ctx, _: SchemaSet) -> Ok | Err:
    state = project(c.events)
    if state is None:
        return err(17, "projection undefined: history has no InvestigationStarted")
    expected = canonical_file_bytes(build_snapshot(c.events, state))
    if expected != c.raw["snapshot.json"]:
        diff = [k for k in sorted(set(c.snapshot) | {"state"}) if k != "state" and c.snapshot.get(k) !=
                build_snapshot(c.events, state).get(k)]  # fmt: skip
        sdiff = [k for k in state if c.snapshot.get("state", {}).get(k) != state[k]]
        return err(17, f"snapshot.json is not the replayed projection (envelope keys {diff}, state keys {sdiff})")
    return Ok(replace(c, state=state))


PIPELINE: tuple[tuple[int, Callable[[Ctx, SchemaSet], Any]], ...] = (
    (1, s1_path), (2, s2_encoding), (3, s3_json), (4, s4_canonical), (5, s5_versions), (6, s6_event_types),
    (7, s7_schema), (8, s8_manifest), (9, s9_events_hash), (10, s10_investigation), (11, s11_event_id_unique),
    (12, s12_sequence), (13, s13_event_id_derivation), (14, s14_correlation), (15, s15_admissibility),
    (16, s16_snapshot_hash), (17, s17_snapshot),
)  # fmt: skip


def run_pipeline(directory: str, raw: Mapping[str, bytes], schemas: SchemaSet) -> PipelineResult:
    """Run §11.1 in order; the first failing stage is primary and stops processing (fail closed, §11.2)."""
    ctx = Ctx(directory=directory, raw=MappingProxyType(dict(raw)))
    outcomes: list[tuple[int, str, str]] = []
    for stage, fn in PIPELINE:
        r = fn(ctx, schemas)
        if isinstance(r, Err):
            outcomes.append((stage, "FAIL", f"{r.code}: {r.detail}"))
            outcomes += [(s, "SKIPPED", "") for s, _ in PIPELINE if s > stage]
            return PipelineResult(r, tuple(outcomes), ctx)
        if isinstance(r, Withheld):
            outcomes.append((stage, "NOT_EVALUATED", r.detail))
            ctx = r.value
        else:
            outcomes.append((stage, "PASS", ""))
            ctx = r.value
    return PipelineResult(None, tuple(outcomes), ctx)


# ---------------------------------------------------------------------------------------------------------------------
# §9.2 append and §9.3 successor
# ---------------------------------------------------------------------------------------------------------------------


@dataclass(frozen=True)
class Frozen:
    investigation_id: str
    scenario_key: str
    raw: Mapping[str, bytes]
    result: PipelineResult


def _appended_stages_2_to_7(appended: bytes, first_version: int, schemas: SchemaSet) -> Ok | Err:
    """§9.2 step (2): the appended lines alone through stages 2-7, numbered from current + 1."""
    r = decode_utf8(appended, "appended events")
    f = frame_ndjson(r.value, "appended events") if isinstance(r, Ok) else r
    if isinstance(f, Err):
        return f
    labels = [f"appended line (version {first_version + i})" for i in range(len(f.value))]
    parsed = []
    for label, line in zip(labels, f.value):
        p = parse_json(line, label)
        if isinstance(p, Err):
            return p
        parsed.append(p.value)
    for label, line, ev in zip(labels, f.value, parsed):
        c = check_canonical(line, ev, label)
        if isinstance(c, Err):
            return c
    for label, ev in zip(labels, parsed):
        probs = _version_problems(ev, label, ("formatVersion", "semanticVersion"))
        if probs:
            return err(5, probs[0])
    for label, ev in zip(labels, parsed):
        t = ev.get("eventType") if isinstance(ev, dict) else None
        if isinstance(t, str) and t not in EVENT_REGISTRY:
            return err(6, f"{label}: eventType {t!r} is not registered")
    for label, ev in zip(labels, parsed):
        e = schema_errors(schemas, "event-envelope.schema.json", ev)
        if e:
            return err(7, f"{label}: {e[0]}")
    return Ok(tuple(parsed))


def evaluate_append(base: Frozen, expected_version: int, appended: bytes, schemas: SchemaSet) -> tuple[Err | None, str, bool]:
    """§9.2 evaluation order.  Returns (primary, extra detail, primary-is-conditional-on-admissibility)."""
    current = base.result.ctx.manifest["aggregateVersion"]
    if expected_version != current:
        return Err("CAUSA.VERSION_STALE", f"expectedAggregateVersion {expected_version} != current {current}; "
                                          f"appended lines not read, nothing written"), "", False  # fmt: skip
    lines = _appended_stages_2_to_7(appended, current + 1, schemas)
    if isinstance(lines, Err):
        return lines, "", False
    events_bytes = base.raw["events.ndjson"] + appended
    events = [*base.result.ctx.events, *lines.value]
    state = project(events)
    if state is None:
        return err(15, "combined history has no InvestigationStarted", "HISTORY.NO_START"), "", False
    snap = build_snapshot(events, state)
    man = build_manifest(base.investigation_id, events_bytes, events, snap)
    cand = {"manifest.json": canonical_file_bytes(man), "events.ndjson": events_bytes, "snapshot.json": canonical_file_bytes(snap)}
    res = run_pipeline(base.investigation_id, cand, schemas)
    extra = "combined candidate: stages 1-14,16,17 PASS, 15 not evaluated" if res.primary is None else "combined candidate"
    return res.primary, extra, res.withheld


def evaluate_successor(base: Frozen, files: Mapping[str, bytes], schemas: SchemaSet) -> tuple[Err | None, str, bool]:
    """§9.3: the candidate is first verified on its own (§11.1); the prefix check follows."""
    res = run_pipeline(base.investigation_id, files, schemas)
    if res.primary is not None:
        return res.primary, "candidate failed on its own", res.withheld
    iso = "candidate on its own: stages 1-14,16,17 PASS, 15 not evaluated"
    if not files["events.ndjson"].startswith(base.raw["events.ndjson"]):
        return Err("CAUSA.HISTORY_REWRITTEN", "events.ndjson does not start with the accepted bytes (§9.3)", 18), iso, res.withheld
    return None, iso, res.withheld


# ---------------------------------------------------------------------------------------------------------------------
# Contract text <-> schema cross-checks (the intro: a disagreement is a contract defect)
# ---------------------------------------------------------------------------------------------------------------------


def _generated_block(text: str, name: str) -> str:
    m = re.search(rf"<!-- BEGIN GENERATED: {name} -->(.*?)<!-- END GENERATED: {name} -->", text, re.S)
    return m.group(1) if m else ""


def _field_tables(block: str) -> dict[str, list[tuple[str, str]]]:
    out: dict[str, list[tuple[str, str]]] = {}
    for sec in re.split(r"^#### ", block, flags=re.M)[1:]:
        name = re.match(r"`([^`]+)`", sec).group(1)
        rows = re.findall(r"^\| `([^`]+)` \| [^|]+ \| (yes|no) \|", sec, re.M)
        out[name] = rows
    return out


def _nullable(prop: Mapping) -> bool:
    ref = prop.get("$ref", "")
    if ref.endswith(("nullableText", "nullableTimestamp")):
        return True
    return any(o.get("type") == "null" for o in prop.get("oneOf", []))


def contract_schema_crosscheck(contract_text: str, schemas: SchemaSet) -> list[Check]:
    checks = []
    common = schemas.contents["common.schema.json"]["$defs"]
    rows = re.findall(r"^\| `(\w+)` \| (.+) \|$", _generated_block(contract_text, "enums"), re.M)
    mism = [n for n, vals in rows if common.get(n, {}).get("enum") != re.findall(r"`(\w+)`", vals)]
    checks.append(check_of("contract.enums-vs-common-schema", err(7, f"differ: {mism}") if mism or not rows else Ok(),
                           f"{len(rows)} enumerations identical"))  # fmt: skip
    env_enum = schemas.contents["event-envelope.schema.json"]["properties"]["eventType"]["enum"]
    payload_files = sorted(r[len("payloads/"):-len(".schema.json")] for r in schemas.files if r.startswith("payloads/"))
    ok = tuple(env_enum) == EVENT_REGISTRY and sorted(EVENT_REGISTRY) == payload_files and len(EVENT_REGISTRY) == 31
    checks.append(check_of("contract.registry-vs-schemas", Ok() if ok else err(7, "§7 list, envelope enum and payload files differ"),
                           "31 event types: §7 text == envelope enum == payloads/*.schema.json"))  # fmt: skip
    for block, prefix in (("payloads", "payloads/"), ("entities", "entities/")):
        tables = _field_tables(_generated_block(contract_text, block))
        probs = []
        for name, rows in tables.items():
            sch = schemas.contents.get(f"{prefix}{name}.schema.json")
            if sch is None:
                probs.append(f"{name}: no schema")
                continue
            props, req = sch.get("properties", {}), sch.get("required", [])
            if [r[0] for r in rows] != list(props) or sorted(req) != sorted(props):
                probs.append(f"{name}: fields {[r[0] for r in rows]} vs schema {list(props)} required {req}")
            probs += [f"{name}.{f}: nullable {n} vs schema" for f, n in rows if f in props and (n == "yes") != _nullable(props[f])]
        n_schema = len([r for r in schemas.files if r.startswith(prefix)])
        if len(tables) != n_schema:
            probs.append(f"{len(tables)} tables vs {n_schema} schema files")
        checks.append(check_of(f"contract.{block}-tables-vs-schemas", err(7, "; ".join(probs)) if probs else Ok(),
                               f"{len(tables)} field tables match properties/required/nullability"))  # fmt: skip
    return checks


# ---------------------------------------------------------------------------------------------------------------------
# Dataset verification
# ---------------------------------------------------------------------------------------------------------------------


def stage_checks(prefix: str, res: PipelineResult) -> list[Check]:
    out = []
    for stage, status, detail in res.stage_outcomes:
        name = f"{prefix}.stage{stage:02d}.{STAGES[stage].split('.')[1].lower()}"
        if status == "PASS":
            out.append(passed(name))
        elif status == "FAIL":
            out.append(failed(name, detail))
        elif status == "NOT_EVALUATED":
            out.append(not_evaluated(name, detail))
        else:
            out.append(not_evaluated(name, "skipped: an earlier stage failed (fail closed)"))
    return out


def verify_negative(name: str, raw: bytes, listed_code: str | None, frozen: Mapping[str, Frozen], schemas: SchemaSet) -> list[Check]:
    cname = f"negative.{name}"
    doc = read_single_strict(raw, f"negative/{name}.json")
    if isinstance(doc, Err):
        return [failed(cname, f"fixture unreadable: {doc.code} {doc.detail}")]
    fx = doc.value
    errs = schema_errors(schemas, "negative-fixture.schema.json", fx)
    if errs:
        return [failed(cname, f"fixture violates negative-fixture.schema.json: {errs[0]}")]
    pre = []
    if fx["name"] != name:
        pre.append(failed(f"{cname}.name", f"name {fx['name']!r} != file name"))
    if listed_code != fx["expectedErrorCode"]:
        pre.append(failed(f"{cname}.root-listing", f"root manifest lists {listed_code} vs fixture {fx['expectedErrorCode']}"))
    expected, exp_domain, kind = fx["expectedErrorCode"], fx["expectedDomainCode"], fx["kind"]
    b64 = lambda o: base64.b64decode(o["base64"], validate=True)  # noqa: E731
    by_key = {f.scenario_key: f for f in frozen.values()}
    extra = ""
    stage15_withheld = False
    if kind == "investigation":
        files = {k: b64(fx["files"][k]) for k in INVESTIGATION_FILES}
        res = run_pipeline(fx["directoryId"], files, schemas)
        got, stage15_withheld = res.primary, res.withheld
    elif kind == "append":
        base = by_key.get(fx["baseScenarioKey"])
        if base is None:
            return pre + [failed(cname, f"unknown baseScenarioKey {fx['baseScenarioKey']}")]
        got, extra, stage15_withheld = evaluate_append(base, fx["expectedAggregateVersion"], b64(fx["appendedEvents"]), schemas)
    else:
        base = by_key.get(fx["baseScenarioKey"])
        if base is None:
            return pre + [failed(cname, f"unknown baseScenarioKey {fx['baseScenarioKey']}")]
        files = {k: b64(fx["files"][k]) for k in INVESTIGATION_FILES}
        got, extra, stage15_withheld = evaluate_successor(base, files, schemas)
    got_s = "accepted-structurally (no error)" if got is None else got.code + (f"[{got.domain_code}]" if got.domain_code else "")
    detail = f"kind={kind} expected={expected}" + (f"[{exp_domain}]" if exp_domain else "") + f" got={got_s}"
    if got is not None:
        detail += f" ({got.detail[:160]})"
    if extra:
        detail += f"; {extra}"
    if expected == "CAUSA.HISTORY_INADMISSIBLE":
        if got is None:
            return pre + [not_evaluated(cname, f"{detail}; {ADMISSIBILITY_NOT_EVALUATED}: acceptance correctly withheld, "
                                               f"but not rejected (no structural rule applies)")]  # fmt: skip
        if got.code == expected and got.domain_code == exp_domain:
            return pre + [passed(cname, f"{detail}; rejected by a structural rule")]
        return pre + [failed(cname, detail)]
    if got is not None and got.code == expected:
        note = ("; CONDITIONAL: primary code holds only if the history is admissible (stage 15 not evaluated, §11.3)"
                if stage15_withheld and got.stage and got.stage > 15 else "")
        return pre + [passed(cname, detail + note)]
    return pre + [failed(cname, detail)]


def verify(repo: Path) -> list[Check]:
    checks: list[Check] = []
    cdir, fdir = repo / "contract/v1.0.0", repo / "fixtures/v1.0.0"

    # §5.2 namespace
    checks.append(check_of("contract.event-id-namespace",
                           Ok() if str(EVENT_ID_NAMESPACE) == EVENT_ID_NAMESPACE_EXPECTED else err(13, f"{EVENT_ID_NAMESPACE}"),
                           f"UUIDv5(NAMESPACE_URL, ...) = {EVENT_ID_NAMESPACE}"))  # fmt: skip

    # Schemas and bundle identity (§10.3)
    ls = load_schemas(cdir / "schemas")
    if isinstance(ls, Err):
        return checks + [failed("schemas.load", ls.detail)]
    schemas: SchemaSet = ls.value
    checks.append(passed("schemas.load", f"{len(schemas.files)} schema files, $id == path, Draft 2020-12 metaschema-valid, registry built"))
    bundle_raw = (cdir / "schemas/bundle.json").read_bytes()
    b = read_single_strict(bundle_raw, "bundle.json")
    checks.append(check_of("schemas.bundle.strict-read", b))
    if isinstance(b, Ok):
        e = schema_errors(schemas, "schema-bundle.schema.json", b.value)
        checks.append(check_of("schemas.bundle.schema", err(7, e[0]) if e else Ok()))
    recomputed = canonical_file_bytes(recompute_bundle(schemas))
    checks.append(check_of("schemas.bundle.recomputed-bytes",
                           Ok() if recomputed == bundle_raw else Err("CAUSA.FIXTURE_DRIFT", "recomputed bundle.json differs"),
                           f"identity sha256(bundle.json)={sha256_hex(bundle_raw)}"))  # fmt: skip
    contract_text = (cdir / "CONTRACT.md").read_text(encoding="utf-8")
    checks += contract_schema_crosscheck(contract_text, schemas)

    # Root manifest (§4)
    rm = read_single_strict((fdir / "manifest.json").read_bytes(), "fixtures/v1.0.0/manifest.json")
    checks.append(check_of("root-manifest.strict-read", rm))
    if isinstance(rm, Err):
        return checks
    root = rm.value
    e = schema_errors(schemas, "root-manifest.schema.json", root)
    checks.append(check_of("root-manifest.schema", err(7, e[0]) if e else Ok()))
    if e:
        return checks
    checks.append(check_of("root-manifest.schemaSha256",
                           Ok() if root["schemaSha256"] == sha256_hex(bundle_raw) else Err("CAUSA.FIXTURE_DRIFT", root["schemaSha256"])))  # fmt: skip
    ids = [i["investigationId"] for i in root["investigations"]]
    keys = [i["scenarioKey"] for i in root["investigations"]]
    ordered = ids == sorted(ids) and len(set(ids)) == len(ids) and len(set(keys)) == len(keys)
    checks.append(check_of("root-manifest.ordering-uniqueness", Ok() if ordered else Err("CAUSA.FIXTURE_DRIFT", "order/uniqueness")))
    on_disk = sorted(p.name for p in (fdir / "investigations").iterdir())
    checks.append(check_of("root-manifest.investigation-directories",
                           Ok() if on_disk == ids else Err("CAUSA.FIXTURE_DRIFT", f"disk {on_disk} vs manifest {ids}")))  # fmt: skip

    # Valid investigations
    frozen: dict[str, Frozen] = {}
    goldens: dict[str, Any] = {}
    for entry in root["investigations"]:
        iid, key = entry["investigationId"], entry["scenarioKey"]
        pre = f"investigation.{key}"
        d = fdir / "investigations" / iid
        present = {p.name: (p.read_bytes() if p.is_file() else b"") for p in d.iterdir()}  # stage 1 checks the set
        res = run_pipeline(iid, present, schemas)
        raw = {n: present.get(n, b"") for n in INVESTIGATION_FILES}
        frozen[iid] = Frozen(iid, key, MappingProxyType(raw), res)
        checks += stage_checks(pre, res)
        if res.primary is not None or res.ctx.state is None:
            checks.append(failed(f"{pre}.verdict", f"rejected: {res.primary.code if res.primary else 'no state'}"))
            continue
        state, ctx = res.ctx.state, res.ctx
        drift = lambda ok, what: Ok() if ok else Err("CAUSA.FIXTURE_DRIFT", what)  # noqa: E731
        state_sha = json_digest(state)
        checks.append(check_of(f"{pre}.root.expectedStateSha256", drift(state_sha == entry["expectedStateSha256"],
                               f"projected {state_sha} != {entry['expectedStateSha256']}"), f"projected state sha256 {state_sha}"))  # fmt: skip
        checks.append(check_of(f"{pre}.root.eventsSha256", drift(sha256_hex(raw["events.ndjson"]) == entry["eventsSha256"], "events")))
        checks.append(check_of(f"{pre}.root.manifestSha256", drift(json_digest(ctx.manifest) == entry["manifestSha256"], "manifest")))
        checks.append(check_of(f"{pre}.root.snapshotSha256", drift(json_digest(ctx.snapshot) == entry["snapshotSha256"]
                               and ctx.manifest["snapshotSha256"] == entry["snapshotSha256"], "snapshot")))  # fmt: skip
        checks.append(check_of(f"{pre}.root.expectedVersion", drift(state["version"] == entry["expectedVersion"], f"{state['version']}")))
        checks.append(check_of(f"{pre}.root.expectedLifecycle", drift(state["lifecycle"] == entry["expectedLifecycle"], state["lifecycle"])))
        checks.append(check_of(f"{pre}.root.title", drift(state["title"] == entry["title"], state["title"])))
        gpath = cdir / "golden" / f"{key}.state.json"
        graw = gpath.read_bytes() if gpath.exists() else b""
        g = read_single_strict(graw, gpath.name)
        if isinstance(g, Ok):
            ge = schema_errors(schemas, "state.schema.json", g.value)
            g = err(7, ge[0]) if ge else g
        checks.append(check_of(f"{pre}.golden.strict-read-and-schema", g))
        checks.append(check_of(f"{pre}.golden.bytes", Ok() if canonical_file_bytes(state) == graw
                               else Err("CAUSA.GOLDEN_MISMATCH", f"projection differs from golden/{key}.state.json")))  # fmt: skip
        goldens[key] = (state, ctx.events)
        checks.append(not_evaluated(f"{pre}.verdict", "structurally valid, admissibility not evaluated (§11.3); NOT reported valid"))

    # golden/scenarios.json (schema golden-scenarios.schema.json; informative, §12.1)
    sc = read_single_strict((cdir / "golden/scenarios.json").read_bytes(), "scenarios.json")
    if isinstance(sc, Ok):
        se = schema_errors(schemas, "golden-scenarios.schema.json", sc.value)
        sc = err(7, f"scenarios.json: {se[0]}") if se else sc
    checks.append(check_of("golden.scenarios.strict-read-and-schema", sc))
    if isinstance(sc, Ok):
        probs, union = [], set()
        for s in sc.value.get("scenarios", []):
            k = s.get("scenarioKey")
            if k not in goldens:
                probs.append(f"{k}: not a frozen scenario")
                continue
            state, evs = goldens[k]
            types = sorted({e["eventType"] for e in evs})
            union |= set(types)
            # §12.1: `commands` counts persisted command groups plus refused commands.
            groups = len({e["correlationId"] for e in evs})
            refused = len(s.get("refusals", []))
            if s.get("eventTypes") != types or s.get("events") != len(evs) or s.get("investigationId") != state["investigationId"] or s.get("commands") != groups + refused:
                probs.append(f"{k}: eventTypes/events/commands/investigationId differ (commands {s.get('commands')} vs groups {groups} + refusals {refused})")
        if union != set(EVENT_REGISTRY):
            probs.append(f"valid fixtures exercise {len(union)} of 31 types: missing {sorted(set(EVENT_REGISTRY) - union)}")
        checks.append(check_of("golden.scenarios.consistency", Err("CAUSA.GOLDEN_MISMATCH", "; ".join(probs)) if probs else Ok(),
                               "event counts, commands (= persisted groups + refusals), type sets match; all 31 types exercised (§12.1)"))  # fmt: skip

    # Negative fixtures (§12.2)
    listed = {n["path"]: n["expectedErrorCode"] for n in root["negativeFixtures"]}
    disk = sorted(f"negative/{p.name}" for p in (fdir / "negative").iterdir())
    checks.append(check_of("root-manifest.negative-listing", Ok() if sorted(listed) == disk and len(listed) == len(root["negativeFixtures"])
                           else Err("CAUSA.FIXTURE_DRIFT", f"listed {sorted(listed)} vs disk {disk}"), f"{len(disk)} fixtures"))  # fmt: skip
    for path in disk:
        checks += verify_negative(path[len("negative/"):-len(".json")], (fdir / path).read_bytes(), listed.get(path), frozen, schemas)
    return checks


def main(argv: Sequence[str]) -> int:
    if len(argv) != 3 or argv[1] != "verify":
        print("usage: causa_consumer.py verify <repoRoot>", file=sys.stderr)
        return 2
    checks = verify(Path(argv[2]))
    for c in checks:
        print(c.line())
    count = lambda s: sum(1 for c in checks if c.status == s)  # noqa: E731
    f = count("FAIL")
    print(f"checks={len(checks)} passed={count('PASS')} failed={f} not_evaluated={count('NOT_EVALUATED')}")
    return 1 if f else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
