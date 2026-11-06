#!/usr/bin/env python3
"""Authoring aid for the Causa storage contract v1.1.0 JSON Schemas (DRAFT for owner decision).

v1.1.0 is a compatible additive extension of v1.0.0 (contract/v1.1.0/CONTRACT.md section 4). The v1.0.0 definitions
come unchanged from author_schemas.py, which reproduces contract/v1.0.0/schemas byte for byte. This script then
applies exactly the 1.1.0 additions and nothing else:

- the version constants become "1.1.0" and every $id moves to contract/v1.1.0/schemas/;
- relationshipKind gains consistentWith and inconclusive;
- two payloads, EvidenceRevised and ObservationRevised, are registered (envelope enum and payload rule);
- the evidence entity gains label and sourceUrl, and the observation entity gains source (all nullable in state).

Usage: author_schemas_v110.py <output-directory>
"""
import json
import posixpath
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import author_schemas as v1  # noqa: E402

V110 = "1.1.0"
v1.BASE = "https://github.com/kemiller2002/causa-data/contract/v1.1.0/schemas/"
v1.ENUMS["relationshipKind"] = v1.ENUMS["relationshipKind"] + ["consistentWith", "inconclusive"]
c = v1.c


def define_payloads_v110():
    v1.define_payloads()
    v1.payload("EvidenceRevised",
               {"evidenceId": c("uuid"), "label": c("text"), "source": c("text"),
                "sourceUrl": {"oneOf": [c("httpUrl"), {"type": "null"}]}},
               "1.1.0: revises the descriptive metadata of evidence (label, source reference, source URL). The admitted content is never revised. In the command group that admits the evidence, source equals EvidenceAdmitted.source byte for byte (creation equality rule).")
    v1.payload("ObservationRevised",
               {"observationId": c("uuid"), "original": c("text"), "source": c("nullableText")},
               "1.1.0: revises an observation's text and source reference. In the command group that records the observation, original equals ObservationRecorded.original byte for byte (creation equality rule).")


def define_entities_v110():
    v1.define_entities()
    ev = v1.ENTITIES["evidence"]
    props = {}
    for k, v in ev["properties"].items():
        props[k] = v
        if k == "content":
            props["label"] = c("nullableText")
        if k == "source":
            props["sourceUrl"] = {"oneOf": [c("httpUrl"), {"type": "null"}]}
    ev["properties"] = props
    ev["required"] = list(props.keys())
    ev["description"] = ev["description"] + " 1.1.0: label and sourceUrl are null until an EvidenceRevised sets them."
    ob = v1.ENTITIES["observation"]
    props = {}
    for k, v in ob["properties"].items():
        props[k] = v
        if k == "original":
            props["source"] = c("nullableText")
    ob["properties"] = props
    ob["required"] = list(props.keys())
    ob["description"] = ob["description"] + " 1.1.0: source is null until an ObservationRevised sets it."


def retarget(value):
    """Version constants and descriptive paths of v1.0.0 become 1.1.0."""
    if isinstance(value, dict):
        out = {}
        for k, v in value.items():
            if k == "const" and v == "1.0.0":
                out[k] = V110
            else:
                out[k] = retarget(v)
        return out
    if isinstance(value, list):
        return [retarget(v) for v in value]
    if isinstance(value, str):
        return value.replace("contract/v1.0.0/", "contract/v1.1.0/").replace("fixtures/v1.0.0/", "fixtures/v1.1.0/").replace(
            "No domain packs exist in v1.0.0.", "No domain packs exist in v1.1.0.")
    return value


def main():
    out = Path(sys.argv[1])
    define_payloads_v110()
    define_entities_v110()
    docs = [v1.common(), v1.actor(), v1.envelope(), v1.state(), v1.snapshot(), v1.investigation_manifest(), v1.root_manifest(),
            v1.negative_fixture(), v1.bundle(), v1.scenarios()]
    docs += v1.payload_docs() + v1.entity_docs()
    for name, body in docs:
        body = retarget(v1.relocate(body, posixpath.dirname(name)))
        path = out / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(body, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(len(docs), "schemas written")


if __name__ == "__main__":
    main()
