#!/usr/bin/env python3
"""Authoring aid for the Causa storage contract v1.2.0 JSON Schemas.

v1.2.0 is a compatible additive extension of v1.1.0 (contract/v1.2.0/CONTRACT.md section 4). The v1.0.0 definitions
come unchanged from author_schemas.py and the v1.1.0 additions unchanged from author_schemas_v110.py. This script then
applies exactly the 1.2.0 additions and nothing else:

- the version constants become "1.2.0" and every $id moves to contract/v1.2.0/schemas/;
- two payloads, InvestigationTitleRevised and UnknownRevised, are registered (envelope enum and payload rule).

No enumeration, entity or state shape changes: the projection gains no array (CONTRACT section 4.4).

Usage: author_schemas_v120.py <output-directory>
"""
import json
import posixpath
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import author_schemas as v1  # noqa: E402
import author_schemas_v110 as v110  # noqa: E402  (imports apply the 1.1.0 enumeration extension to v1)

V120 = "1.2.0"
v1.BASE = "https://github.com/kemiller2002/causa-data/contract/v1.2.0/schemas/"
c = v1.c


def define_payloads_v120():
    v110.define_payloads_v110()
    v1.payload("InvestigationTitleRevised",
               {"title": c("text"), "reason": c("nullableText")},
               "1.2.0: revises the investigation's title. The title is kept verbatim; reason is why, or null. A revision is a command group of its own.")
    v1.payload("UnknownRevised",
               {"unknownId": c("uuid"), "question": c("text")},
               "1.2.0: revises an open unknown's question verbatim. The unknown keeps its id, state, basis, materiality and provenance. A revision is a command group of its own.")


def retarget(value):
    """Version constants and descriptive paths of v1.0.0 and v1.1.0 become 1.2.0."""
    if isinstance(value, dict):
        out = {}
        for k, v in value.items():
            if k == "const" and v in ("1.0.0", "1.1.0"):
                out[k] = V120
            else:
                out[k] = retarget(v)
        return out
    if isinstance(value, list):
        return [retarget(v) for v in value]
    if isinstance(value, str):
        return value.replace("contract/v1.0.0/", "contract/v1.2.0/").replace("fixtures/v1.0.0/", "fixtures/v1.2.0/").replace(
            "No domain packs exist in v1.0.0.", "No domain packs exist in v1.2.0.")
    return value


def main():
    out = Path(sys.argv[1])
    define_payloads_v120()
    v110.define_entities_v110()
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
