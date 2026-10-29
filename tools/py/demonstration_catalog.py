"""The active demonstration catalog demonstration-v1: Apollo 1, Apollo 11, Challenger and the burned-out lightbulb.

catalog/active.json names catalog/demonstration-v1/manifest.json and workspaces/demo/demonstration-v1 (Indy-init
EXT-DATA-013, owner decision DEC-R7-006). The three historical investigations are byte-identical copies of
historical-v1 and the lightbulb is a byte-identical copy of everyday-v1; this catalog adds no new record.

  build  <root>                    write catalog/demonstration-v1/manifest.json (derived, RFC 8785 + LF)
  verify <root> [--app <path>] [--report <file>]
                                   pointer, schema, deterministic rebuild, exact four cases, Python reader on every
                                   case, byte identity with historical-v1 / everyday-v1 and the immutable template,
                                   app bundling

Engine replay of the four cases is `causa template-verify --template demonstration-v1` (F#).
Style: pure functions returning check records; I/O at the edges.  Run with `python3 -I`.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any, Mapping, Sequence

from jsonschema import Draft202012Validator

sys.path.insert(0, str(Path(__file__).resolve().parent))
from historical_catalog import (  # noqa: E402  (sibling module; same check vocabulary)
    Check, app_checks, check, failed, investigation_raw, load_consumer, read_json, report, sample, sha256,
)

RUN_ID = "demonstration-v1"
CATALOG_ID = "causa-demonstration"
CATALOG_VERSION = "1.0.0"
CREATED_AT = "2026-10-29T00:00:00.000Z"
SOURCES = (  # caseKey, source workspace
    ("apollo-1", "historical-v1"),
    ("apollo-11", "historical-v1"),
    ("challenger-sts-51l", "historical-v1"),
    ("burned-out-lightbulb", "everyday-v1"),
)
MANIFEST = f"catalog/{RUN_ID}/manifest.json"
WORKSPACE = f"workspaces/demo/{RUN_ID}"
SCHEMA = "catalog/schemas/demonstration-catalog-manifest.schema.json"
ACTIVE = {"activeCatalog": RUN_ID, "catalogId": CATALOG_ID, "catalogVersion": CATALOG_VERSION,
          "manifest": MANIFEST, "workspace": WORKSPACE}


# ---------------------------------------------------------------------------------------------------------------------
# Derivation (pure given the repository contents)
# ---------------------------------------------------------------------------------------------------------------------

def source_ids(root: Path, workspace: str) -> list[str]:
    return sorted(p.name for p in (root / "workspaces/demo" / workspace / "investigations").iterdir())


def case_ids(root: Path) -> dict[str, str]:
    """caseKey -> investigationId: historical keys from catalog/historical-v1, the lightbulb from everyday-v1."""
    historical = {c["caseKey"]: c["investigationId"] for c in read_json(root / "catalog/historical-v1/manifest.json")["cases"]}
    everyday = source_ids(root, "everyday-v1")
    return {**historical, "burned-out-lightbulb": everyday[0]} if len(everyday) == 1 else historical


def entry(root: Path, consumer, key: str, source: str, iid: str) -> dict:
    raw = investigation_raw(root / WORKSPACE / "investigations" / iid)
    state = json.loads(raw["snapshot.json"])["state"]
    return {
        "caseKey": key,
        "investigationId": iid,
        "title": state["title"],
        "path": f"{WORKSPACE}/investigations/{iid}",
        "sourceWorkspace": f"workspaces/demo/{source}",
        "expectedVersion": state["version"],
        "expectedLifecycle": state["lifecycle"],
        "expectedStateSha256": consumer.json_digest(state),
        "eventsSha256": sha256(raw["events.ndjson"]),
        "manifestSha256": consumer.json_digest(json.loads(raw["manifest.json"])),
        "snapshotSha256": consumer.json_digest(json.loads(raw["snapshot.json"])),
    }


def manifest(root: Path, consumer) -> dict:
    ids = case_ids(root)
    template = read_json(root / "templates" / RUN_ID / "template-manifest.json")
    return {
        "catalogId": CATALOG_ID,
        "catalogVersion": CATALOG_VERSION,
        "formatVersion": "1.0.0",
        "semanticVersion": "1.0.0",
        "schemaSha256": sha256((root / "contract/v1.0.0/schemas/bundle.json").read_bytes()),
        "createdAt": CREATED_AT,
        "workspace": WORKSPACE,
        "template": {"templateId": template["templateId"], "templateVersion": template["templateVersion"],
                     "overallTemplateSha256": template["overallTemplateSha256"]},
        "cases": [entry(root, consumer, key, source, ids[key]) for key, source in SOURCES],
        "retainedCompatibilityDataset": {"datasetId": "causa-demo-v1", "manifest": "fixtures/v1.0.0/manifest.json",
                                         "status": "immutable v1.0.0 compatibility fixtures; not part of the active demonstration catalog"},
    }


# ---------------------------------------------------------------------------------------------------------------------
# Checks
# ---------------------------------------------------------------------------------------------------------------------

def same_files(a: Path, b: Path) -> bool:
    names = ("manifest.json", "events.ndjson", "snapshot.json")
    return all((a / n).is_file() and (b / n).is_file() and (a / n).read_bytes() == (b / n).read_bytes() for n in names)


def template_checks(root: Path) -> list[Check]:
    template = read_json(root / "templates" / RUN_ID / "template-manifest.json")
    rows = [(f["path"], f["sha256"]) for inv in template["investigations"] for f in inv["files"]]
    drift = [p for p, digest in rows if sha256((root / WORKSPACE / p).read_bytes()) != digest]
    return [check("demonstration.template-identity", not drift and len(rows) == 12,
                  f"{len(rows)} template file digests checked against {WORKSPACE}" + (f"; differ: {sample(drift)}" if drift else ""))]


def case_checks(root: Path, consumer, schemas, e: Mapping) -> list[Check]:
    key, iid = e["caseKey"], e["investigationId"]
    directory = root / e["path"]
    result = consumer.run_pipeline(iid, investigation_raw(directory), schemas)
    stage_fail = [f"stage {s}: {d}" for s, st, d in result.stage_outcomes if st == "FAIL"]
    return [
        check(f"{key}.python-reader", result.primary is None and not stage_fail,
              f"{result.primary.code}: {result.primary.detail}" if result.primary else "stages 1-14 and 16-17 pass; engine replay is `causa template-verify`"),
        check(f"{key}.byte-identical-to-source", same_files(directory, root / e["sourceWorkspace"] / "investigations" / iid),
              f"manifest, events and snapshot equal {e['sourceWorkspace']}/investigations/{iid}"),
    ]


def catalog_checks(root: Path, consumer) -> tuple[list[Check], dict | None]:
    active_path, manifest_path = root / "catalog/active.json", root / MANIFEST
    if not active_path.exists() or not manifest_path.exists():
        return [failed("demonstration.files", f"catalog/active.json or {MANIFEST} is missing")], None
    committed = read_json(manifest_path)
    errors = sorted(e.message for e in Draft202012Validator(read_json(root / SCHEMA)).iter_errors(committed))
    rebuilt = manifest(root, consumer)
    cases = committed.get("cases", [])
    ids = [c["investigationId"] for c in cases]
    dirs = sorted(p.name for p in (root / WORKSPACE / "investigations").iterdir())
    fixture_ids = [i["investigationId"] for i in read_json(root / "fixtures/v1.0.0/manifest.json")["investigations"]]
    historical = {c["caseKey"]: c for c in read_json(root / "catalog/historical-v1/manifest.json")["cases"]}
    digest_keys = ("investigationId", "title", "expectedVersion", "expectedLifecycle", "expectedStateSha256",
                   "eventsSha256", "manifestSha256", "snapshotSha256")
    historical_drift = [c["caseKey"] for c in cases if c["caseKey"] in historical
                        and any(c[k] != historical[c["caseKey"]][k] for k in digest_keys)]
    return [
        check("demonstration.active-pointer",
              active_path.read_bytes() == consumer.canonical_file_bytes(ACTIVE),
              "catalog/active.json is canonical and names " + MANIFEST + " and " + WORKSPACE),
        check("demonstration.manifest.schema", not errors, sample(errors) if errors else SCHEMA),
        check("demonstration.manifest.reproducible", manifest_path.read_bytes() == consumer.canonical_file_bytes(rebuilt),
              "committed manifest equals `demonstration_catalog.py build` (RFC 8785 + LF)"),
        check("demonstration.exactly-four-cases", tuple(c["caseKey"] for c in cases) == tuple(k for k, _ in SOURCES),
              ", ".join(c["caseKey"] for c in cases)),
        check("demonstration.workspace-exact", dirs == sorted(ids) and len(set(ids)) == 4, "directories: " + ", ".join(dirs)),
        check("demonstration.no-synthetic-cases", not set(ids) & set(fixture_ids), "no v1.0.0 synthetic investigation"),
        check("demonstration.historical-entries-match-historical-v1", not historical_drift,
              "apollo-1, apollo-11, challenger-sts-51l entries equal catalog/historical-v1/manifest.json"
              + (f"; differ: {historical_drift}" if historical_drift else "")),
    ], committed


def verify(root: Path, app: Path | None) -> list[Check]:
    consumer = load_consumer(root)
    schemas_r = consumer.load_schemas(root / "contract/v1.0.0/schemas")
    if not isinstance(schemas_r, consumer.Ok):
        return [failed("schemas.load", str(schemas_r))]
    head, committed = catalog_checks(root, consumer)
    if committed is None:
        return head
    per_case = [c for e in committed["cases"] for c in case_checks(root, consumer, schemas_r.value, e)]
    return head + template_checks(root) + per_case + app_checks(root, app, committed)


# ---------------------------------------------------------------------------------------------------------------------
# Edges
# ---------------------------------------------------------------------------------------------------------------------

def option(args: Sequence[str], name: str) -> str | None:
    return args[args.index(name) + 1] if name in args and args.index(name) + 1 < len(args) else None


def main(argv: Sequence[str]) -> int:
    if len(argv) < 2 or argv[0] not in ("build", "verify"):
        print(__doc__)
        return 2
    root = Path(argv[1]).resolve()
    if argv[0] == "build":
        consumer = load_consumer(root)
        (root / MANIFEST).parent.mkdir(parents=True, exist_ok=True)
        (root / MANIFEST).write_bytes(consumer.canonical_file_bytes(manifest(root, consumer)))
        (root / "catalog/active.json").write_bytes(consumer.canonical_file_bytes(ACTIVE))
        print(f"wrote {MANIFEST} and catalog/active.json")
        return 0
    app, out = option(argv, "--app"), option(argv, "--report")
    return report(verify(root, Path(app).resolve() if app else None), Path(out) if out else None)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
