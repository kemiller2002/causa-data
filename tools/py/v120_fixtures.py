#!/usr/bin/env python3
"""Independent (non-F#) checks of the causa-storage 1.2.0 conformance fixtures (contract/v1.2.0/CONTRACT.md section 5).

  v120_fixtures.py verify <causa-data root>

The fixtures are written by the Causa application's codec (tools/v120/build-fixtures.fsx). This script checks them
with python-jsonschema and a separate reading of the 1.2.0 rules, without that codec:

- every file of the valid investigation validates against its 1.2.0 schema, every version string is "1.2.0", and the
  manifest digests and counts match the bytes;
- the 1.2.0 admissibility rules (section 4.5) accept the valid history, and the projection rules of section 4.4 give
  the snapshot's title, observation texts and unknown questions (and leave each unknown's state, basis, materiality and
  provenance as recorded);
- every negative fixture validates against the negative-fixture schema, is listed in index.json, and fails exactly the
  kind of rule its code names: a schema violation fails the 1.2.0 envelope schema; a history refusal passes every
  schema and fails the section 4.5 rule with its domain code; an unknown event type is outside the 1.1.0 registry of
  a 1.1.0 line; an unsupported version mixes versions;
- the 1.0.0 Python consumer (a 1.0.0 reader) refuses the 1.2.0 investigation whole with CAUSA.UNSUPPORTED_VERSION.
"""
import base64
import hashlib
import json
import sys
from pathlib import Path

from jsonschema import Draft202012Validator

sys.path.insert(0, str(Path(__file__).resolve().parent))
import schema_bundle  # noqa: E402

FILES = ("manifest.json", "events.ndjson", "snapshot.json")
OPEN = {"draft", "active", "awaitingEvidence", "awaitingVerification"}
OPEN_UNKNOWN = {"identified", "investigating"}
DETAILS = {"InvestigationTitleRevised", "UnknownRevised"}


def validators(schema_dir):
    raw = schema_bundle.load(schema_dir)
    docs = [json.loads(b) for _, b in raw]
    reg = schema_bundle.registry(docs)
    by_path = {path: d for (path, _), d in zip(raw, docs)}
    return lambda path: Draft202012Validator(by_path[path], registry=reg)


def lines(events: bytes):
    return [json.loads(line) for line in events.decode("utf-8").rstrip("\n").split("\n")]


def versions(files):
    m, s = json.loads(files["manifest.json"]), json.loads(files["snapshot.json"])
    vs = [m["formatVersion"], m["schemaVersion"], m["semanticVersion"], s["formatVersion"], s["semanticVersion"]]
    for e in lines(files["events.ndjson"]):
        vs += [e["formatVersion"], e["semanticVersion"]]
    return set(vs)


def schema_problems(v, files):
    problems = []
    if not v("investigation-manifest.schema.json").is_valid(json.loads(files["manifest.json"])):
        problems.append("manifest.json")
    for i, e in enumerate(lines(files["events.ndjson"]), 1):
        if not v("event-envelope.schema.json").is_valid(e):
            problems.append(f"events.ndjson line {i}")
    if not v("snapshot.schema.json").is_valid(json.loads(files["snapshot.json"])):
        problems.append("snapshot.json")
    return problems


def details_rules(events):
    """Section 4.5 for the two 1.2.0 events, in history order: (aggregateVersion, domain code) of the first failure."""
    groups = []
    for e in events:
        if groups and groups[-1][0]["correlationId"] == e["correlationId"]:
            groups[-1].append(e)
        else:
            groups.append([e])
    lifecycle, unknowns = "draft", {}
    for g in groups:
        revisions = [e for e in g if e["eventType"] in DETAILS]
        if revisions:
            first = g[0]
            if len(g) != 1:
                return first["aggregateVersion"], "HISTORY.INADMISSIBLE_EVENT"
            if lifecycle not in OPEN:
                return first["aggregateVersion"], "INVESTIGATION.NOT_OPEN"
            if first["eventType"] == "UnknownRevised" and unknowns.get(first["payload"]["unknownId"]) not in OPEN_UNKNOWN:
                return first["aggregateVersion"], "HISTORY.INADMISSIBLE_EVENT"
        for e in g:
            p = e["payload"]
            if e["eventType"] == "LifecycleChanged":
                lifecycle = p["to"]
            elif e["eventType"] == "UnknownIdentified":
                unknowns.setdefault(p["unknownId"], "identified")
            elif e["eventType"] == "UnknownTransitioned" and p["unknownId"] in unknowns:
                unknowns[p["unknownId"]] = p["state"]
    return None


def projected_details(events):
    """Section 4.4 (and 1.1.0 section 4.7 for observations): title, observation texts and unknowns."""
    title, observations, unknowns = None, {}, {}
    for e in events:
        p, t = e["payload"], e["eventType"]
        prov = {"actor": e["actor"], "recordedAt": e["occurredAt"]}
        if t == "InvestigationStarted":
            title, observations[p["observationId"]] = p["title"], p["original"]
        elif t == "ObservationRecorded":
            observations.setdefault(p["observationId"], p["original"])
        elif t == "ObservationRevised" and p["observationId"] in observations:
            observations[p["observationId"]] = p["original"]
        elif t == "InvestigationTitleRevised":
            title = p["title"]
        elif t == "UnknownIdentified" and p["unknownId"] not in unknowns:
            unknowns[p["unknownId"]] = {"question": p["question"], "materiality": p["materiality"], "state": "identified", "basis": None,
                                        "provenance": prov}
        elif t == "UnknownTransitioned" and p["unknownId"] in unknowns:
            unknowns[p["unknownId"]].update(state=p["state"], basis=p["basis"])
        elif t == "UnknownRevised" and p["unknownId"] in unknowns:
            unknowns[p["unknownId"]]["question"] = p["question"]
    return title, observations, unknowns


def verify(root: Path):
    failures = []
    fail = failures.append
    v120 = validators(root / "contract/v1.2.0/schemas")
    v110 = validators(root / "contract/v1.1.0/schemas")
    fixtures = root / "fixtures/v1.2.0"
    index = json.loads((fixtures / "index.json").read_text("utf-8"))
    if index["formatVersion"] != "1.2.0":
        fail("index.json formatVersion")

    for inv in index["investigations"]:
        d = fixtures / "investigations" / inv["investigationId"]
        if sorted(p.name for p in d.iterdir()) != sorted(FILES):
            fail(f"{d}: not exactly the three files")
            continue
        files = {n: (d / n).read_bytes() for n in FILES}
        for problem in schema_problems(v120, files):
            fail(f"{d.name}: {problem} does not match its 1.2.0 schema")
        if versions(files) != {"1.2.0"}:
            fail(f"{d.name}: version strings {sorted(versions(files))}")
        m, s, events = json.loads(files["manifest.json"]), json.loads(files["snapshot.json"]), lines(files["events.ndjson"])
        if m["eventsSha256"] != hashlib.sha256(files["events.ndjson"]).hexdigest():
            fail(f"{d.name}: eventsSha256")
        if m["snapshotSha256"] != hashlib.sha256(files["snapshot.json"][:-1]).hexdigest():
            fail(f"{d.name}: snapshotSha256")
        if not (m["eventCount"] == m["aggregateVersion"] == s["aggregateVersion"] == len(events) == events[-1]["aggregateVersion"]):
            fail(f"{d.name}: counts and versions")
        if s["state"]["version"] != len(events) or s["generatedAt"] != events[-1]["occurredAt"]:
            fail(f"{d.name}: the state's version and generatedAt are not the last event's")
        if {e["eventType"] for e in events} < DETAILS:
            fail(f"{d.name}: does not hold both 1.2.0 events")
        if (verdict := details_rules(events)) is not None:
            fail(f"{d.name}: the valid history fails section 4.5: {verdict}")
        title, observations, unknowns = projected_details(events)
        state = s["state"]
        if state["title"] != title:
            fail(f"{d.name}: snapshot title {state['title']!r} is not the replayed {title!r}")
        if {o["id"]: o["original"] for o in state["observations"]} != observations:
            fail(f"{d.name}: snapshot observations are not the replayed ones")
        replayed = {u["id"]: {k: u[k] for k in ("question", "materiality", "state", "basis", "provenance")} for u in state["unknowns"]}
        if replayed != unknowns:
            fail(f"{d.name}: snapshot unknowns are not the replayed ones")
        # A 1.0.0 reader refuses 1.2.0 data whole.
        sys.path.insert(0, str(root / "tools/consumers/python"))
        import causa_consumer as consumer  # noqa: E402
        schemas = consumer.load_schemas(root / "contract/v1.0.0/schemas").value
        result = consumer.run_pipeline(d.name, files, schemas)
        code = result.primary.code if result.primary else "ACCEPTED"
        if code != "CAUSA.UNSUPPORTED_VERSION":
            fail(f"{d.name}: the 1.0.0 Python consumer answered {code}, not CAUSA.UNSUPPORTED_VERSION")

    listed = {n["path"]: n["expectedErrorCode"] for n in index["negativeFixtures"]}
    on_disk = {f"negative/{p.name}" for p in (fixtures / "negative").glob("*.json")}
    if set(listed) != on_disk:
        fail(f"index.json and negative/ differ: {sorted(set(listed) ^ on_disk)}")
    for path, code in sorted(listed.items()):
        doc = json.loads((fixtures / path).read_text("utf-8"))
        if not v120("negative-fixture.schema.json").is_valid(doc):
            fail(f"{path}: not a 1.2.0 negative fixture")
        if doc["expectedErrorCode"] != code:
            fail(f"{path}: the index says {code}, the fixture {doc['expectedErrorCode']}")
        files = {n: base64.b64decode(f["base64"]) for n, f in doc["files"].items()}
        events = lines(files["events.ndjson"])
        if code == "CAUSA.SCHEMA_VIOLATION":
            if not schema_problems(v120, files):
                fail(f"{path}: passes every 1.2.0 schema")
        elif code == "CAUSA.HISTORY_INADMISSIBLE":
            if problems := schema_problems(v120, files):
                fail(f"{path}: fails a schema before stage 15: {problems}")
            verdict = details_rules(events)
            if verdict is None or verdict[1] != doc["expectedDomainCode"]:
                fail(f"{path}: section 4.5 gives {verdict}, not {doc['expectedDomainCode']}")
        elif code == "CAUSA.UNKNOWN_EVENT_TYPE":
            registered = v110("event-envelope.schema.json").schema["properties"]["eventType"]["enum"]
            if versions(files) != {"1.1.0"} or all(e["eventType"] in registered for e in events):
                fail(f"{path}: not a 1.1.0 history with an unregistered type")
        elif code == "CAUSA.UNSUPPORTED_VERSION":
            if len(versions(files)) < 2:
                fail(f"{path}: versions are homogeneous")
        else:
            fail(f"{path}: unexpected code {code}")

    for f in failures:
        print(f)
    print(f"v1.2.0 fixtures: investigations={len(index['investigations'])} negatives={len(listed)} failures={len(failures)}")
    return not failures


def main():
    if len(sys.argv) != 3 or sys.argv[1] != "verify":
        sys.exit(__doc__)
    sys.exit(0 if verify(Path(sys.argv[2])) else 1)


if __name__ == "__main__":
    main()
