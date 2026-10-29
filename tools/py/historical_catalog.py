#!/usr/bin/env python3
"""Independent validation of the active historical catalog (catalog/historical-v1, workspaces/demo/historical-v1).

Complements `causa historical-verify` (F#, the authoritative engine) with checks written from the contract text and
research/historical/RESEARCH-INTERFACE.md only:

  verify <root> [--app <path>] [--report <file>]   catalog, contract pipeline (Python reader), provenance, immutability,
                                                   secrets, corrupted-record refusal (both readers), app bundling
  sources <root> [--report <file>]                 network: fetch every source and look for every recorded excerpt;
                                                   anything not confirmed automatically is flagged for human review

Style: pure functions returning check records; I/O at the edges.  Run with `python3 -I`.
"""
from __future__ import annotations

import hashlib
import html
import importlib.util
import json
import re
import shutil
import subprocess
import sys
import tempfile
import unicodedata
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Iterable, Mapping, Sequence

from jsonschema import Draft202012Validator

CASE_KEYS = ("apollo-1", "apollo-11", "challenger-sts-51l")
RUN_ID = "historical-v1"
ALLOWED_ADDITION_WORKSPACES = (RUN_ID, "everyday-v1", "demonstration-v1")
SYNTHETIC_KEYS = ("payment-failure", "intermittent-outage", "conflicting-metrics", "api-latency", "authentication-outage")
FROZEN_PATHS = ("contract/v1.0.0", "fixtures/v1.0.0", "compatibility/v1.0.0", "engine.lock.json", "global.json")
RELEASE_TAG = "causa-storage-v1.0.0"
CLAIM_REF = re.compile(r"\[claim ([A-Z0-9]+-C[0-9]+); source ([A-Z0-9]+-S[0-9]+); (primary|secondary) source; retrieved [0-9-]+\]\Z")
SECRET_PATTERNS = (
    ("github-token", re.compile(r"\b(ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{30,}\b")),
    ("github-pat", re.compile(r"\bgithub_pat_[A-Za-z0-9_]{40,}\b")),
    ("aws-key", re.compile(r"\bAKIA[0-9A-Z]{16}\b")),
    ("private-key", re.compile(r"-----BEGIN [A-Z ]*PRIVATE KEY-----")),
    ("bearer", re.compile(r"\b[Bb]earer\s+[A-Za-z0-9._~+/-]{20,}")),
    ("anthropic-key", re.compile(r"\bsk-ant-[A-Za-z0-9_-]{20,}")),
)


@dataclass(frozen=True)
class Check:
    name: str
    status: str  # PASS | FAIL | REVIEW (needs a human; never counted as a pass)
    detail: str


def passed(name: str, detail: str = "") -> Check:
    return Check(name, "PASS", detail)


def failed(name: str, detail: str) -> Check:
    return Check(name, "FAIL", detail)


def review(name: str, detail: str) -> Check:
    return Check(name, "REVIEW", detail)


def check(name: str, ok: bool, detail: str) -> Check:
    return passed(name, detail) if ok else failed(name, detail)


def sample(xs: Sequence[str], n: int = 12) -> str:
    return "none" if not xs else ", ".join(xs[:n]) + (f" (+{len(xs) - n})" if len(xs) > n else "")


# ---------------------------------------------------------------------------------------------------------------------
# Inputs
# ---------------------------------------------------------------------------------------------------------------------

def load_consumer(root: Path):
    """The repository's independent Python contract consumer, reused as a second reader (stages 1-14, 16-17)."""
    spec = importlib.util.spec_from_file_location("causa_consumer", root / "tools/consumers/python/causa_consumer.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules["causa_consumer"] = module
    spec.loader.exec_module(module)
    return module


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def sha256(b: bytes) -> str:
    return hashlib.sha256(b).hexdigest()


def research(root: Path, key: str) -> tuple[list[dict], list[dict]]:
    d = root / "research" / "historical" / key
    return read_json(d / "sources.json"), read_json(d / "claims.json")


def investigation_raw(directory: Path) -> dict[str, bytes]:
    return {p.name: (p.read_bytes() if p.is_file() else b"") for p in directory.iterdir()}


def events_of(raw: Mapping[str, bytes]) -> list[dict]:
    return [json.loads(line) for line in raw["events.ndjson"].decode("utf-8").split("\n") if line]


# ---------------------------------------------------------------------------------------------------------------------
# Checks
# ---------------------------------------------------------------------------------------------------------------------

def catalog_checks(root: Path, consumer) -> tuple[list[Check], dict | None]:
    active_path, manifest_path = root / "catalog/active.json", root / f"catalog/{RUN_ID}/manifest.json"
    if not manifest_path.exists() or not active_path.exists():
        return [failed("catalog.files", "catalog/active.json or the catalog manifest is missing")], None
    manifest, active = read_json(manifest_path), read_json(active_path)
    schema = read_json(root / "catalog/schemas/catalog-manifest.schema.json")
    errors = sorted(e.message for e in Draft202012Validator(schema).iter_errors(manifest))
    canonical = lambda p: p.read_bytes() == consumer.canonical_file_bytes(read_json(p))
    cases = manifest.get("cases", [])
    ids = [c["investigationId"] for c in cases]
    ws = root / "workspaces" / "demo" / RUN_ID / "investigations"
    dirs = sorted(p.name for p in ws.iterdir()) if ws.exists() else []
    fixture_ids = [i["investigationId"] for i in read_json(root / "fixtures/v1.0.0/manifest.json")["investigations"]]
    bundle_sha = sha256((root / "contract/v1.0.0/schemas/bundle.json").read_bytes())
    return [
        check("catalog.manifest.schema", not errors, sample(errors) if errors else "catalog-manifest.schema.json"),
        check("catalog.manifest.canonical", canonical(manifest_path), "RFC 8785 + LF"),
        check("catalog.active.canonical", canonical(active_path), "RFC 8785 + LF"),
        check("catalog.active.names-demonstration-v1",
              active.get("manifest") == "catalog/demonstration-v1/manifest.json" and active.get("workspace") == "workspaces/demo/demonstration-v1",
              "historical-v1 is a component of the active four-case catalog (tools/py/demonstration_catalog.py); " + json.dumps(active, sort_keys=True)),
        check("catalog.exactly-three-cases", len(cases) == 3, f"{len(cases)} cases"),
        check("catalog.case-identities", tuple(c["caseKey"] for c in cases) == CASE_KEYS, ", ".join(c["caseKey"] for c in cases)),
        check("catalog.workspace-exact", dirs == sorted(ids) and len(set(ids)) == 3, "directories: " + ", ".join(dirs)),
        check("catalog.no-synthetic-cases",
              not set(ids) & set(fixture_ids) and not {c["caseKey"] for c in cases} & set(SYNTHETIC_KEYS),
              "no v1.0.0 synthetic investigation in the active catalog"),
        check("catalog.schema-identity", manifest.get("schemaSha256") == bundle_sha, bundle_sha),
    ], manifest


def pipeline_checks(root: Path, consumer, schemas, entry: Mapping) -> list[Check]:
    key, iid = entry["caseKey"], entry["investigationId"]
    directory = root / entry["path"]
    raw = investigation_raw(directory)
    result = consumer.run_pipeline(iid, raw, schemas)
    stage_fail = [f"stage {s}: {d}" for s, st, d in result.stage_outcomes if st == "FAIL"]
    withheld = [s for s, st, _ in result.stage_outcomes if st == "NOT_EVALUATED"]
    digests = {
        "eventsSha256": sha256(raw["events.ndjson"]),
        "manifestSha256": consumer.json_digest(json.loads(raw["manifest.json"])),
        "snapshotSha256": consumer.json_digest(json.loads(raw["snapshot.json"])),
        "expectedStateSha256": consumer.json_digest(json.loads(raw["snapshot.json"])["state"]),
    }
    drift = [k for k, v in digests.items() if entry.get(k) != v]
    state = json.loads(raw["snapshot.json"])["state"]
    return [
        check(f"{key}.python-reader", result.primary is None and not stage_fail,
              (f"{result.primary.code}: {result.primary.detail}" if result.primary else "stages 1-14 and 16-17 pass")
              + (f"; stage(s) {withheld} need the F# engine (evaluated by `causa historical-verify`)" if withheld else "")),
        check(f"{key}.digests-independent", not drift, "recomputed events/manifest/snapshot/state digests" + (f"; differ: {drift}" if drift else "")),
        check(f"{key}.lifecycle", state["lifecycle"] == entry["expectedLifecycle"] and state["version"] == entry["expectedVersion"],
              f"{state['lifecycle']} at version {state['version']}"),
        check(f"{key}.historical-time-separation",
              all(e["occurredAt"] >= "2026-01-01" for e in events_of(raw)),
              "every envelope occurredAt is a Causa build-clock instant; mission times appear only in observedAt/capturedAt/text"),
    ]


def research_checks(root: Path, key: str, events: Sequence[Mapping]) -> list[Check]:
    sources, claims = research(root, key)
    by_source = {s["sourceId"]: s for s in sources}
    by_claim = {c["claimId"]: c for c in claims}
    admitted = [e["payload"] for e in events if e["eventType"] == "EvidenceAdmitted"]
    refs = [(p["evidenceId"], CLAIM_REF.search(p["source"])) for p in admitted]
    untraceable = [eid for eid, m in refs if not m]
    cited = {m.group(1): eid for eid, m in refs if m}
    unverified_admitted = [c for c in cited if by_claim.get(c, {}).get("verification") == "unverified"]
    dangling = [c for c in cited if c not in by_claim]
    usable = [c["claimId"] for c in claims if c["verification"] != "unverified"]
    not_admitted = [c for c in usable if c not in cited]
    no_locator = [c["claimId"] for c in claims if c["verification"] == "verified" and not (c.get("locator") or "").strip()]
    verified_without_excerpt = [c["claimId"] for c in claims if c["verification"] == "verified" and not c.get("excerpt")]
    unread = [c["claimId"] for c in claims if c["verification"] != "unverified" and by_source.get(c["sourceId"], {}).get("retrievalStatus") != "retrieved"]
    secondary_only = [c["claimId"] for c in claims if c["verification"] != "unverified" and by_source.get(c["sourceId"], {}).get("sourceClass") == "secondary" and c["kind"] == "finding"]
    long_excerpts = [c["claimId"] for c in claims if c.get("excerpt") and len(c["excerpt"].split()) > 60]
    relationship_evidence = {e["payload"]["evidenceId"] for e in events if e["eventType"] == "EvidenceRelated"}
    evidence_claim = {eid: m.group(1) for eid, m in refs if m}
    unrelated_used = [evidence_claim[e] for e in relationship_evidence if e in evidence_claim and by_claim[evidence_claim[e]]["verification"] == "unverified"]
    claim_class = lambda eid: by_source.get(by_claim.get(evidence_claim.get(eid, ""), {}).get("sourceId", ""), {}).get("sourceClass")
    supported = [e["payload"] for e in events if e["eventType"] == "HypothesisTransitioned" and e["payload"]["to"] == "supported"]
    secondary_only_support = [p["hypothesisId"][:8] for p in supported if not any(claim_class(i) == "primary" for i in p["evidenceIds"])]
    satisfied = [e["payload"] for e in events if e["eventType"] == "VerificationRecorded" and e["payload"]["outcome"] == "satisfied"]
    secondary_verification = [evidence_claim.get(i, i) for p in satisfied for i in p["evidenceIds"] if claim_class(i) != "primary"]
    return [
        check(f"{key}.support.primary-basis", not secondary_only_support,
              "every markSupported transition cites at least one primary-source claim" + (f"; secondary-only: {secondary_only_support}" if secondary_only_support else "")),
        check(f"{key}.verification.primary-evidence", not secondary_verification,
              "every satisfied verification cites only primary-source evidence" + (f"; secondary: {sample(secondary_verification)}" if secondary_verification else "")),
        check(f"{key}.sources.complete", all(s.get("url", "").startswith(("https://", "http://")) and s.get("title") and s.get("issuer") for s in sources),
              f"{len(sources)} sources with title, issuer and URL"),
        check(f"{key}.claims.cite-known-sources", all(c["sourceId"] in by_source for c in claims), f"{len(claims)} claims"),
        check(f"{key}.evidence.traceable", not untraceable, "evidence without a claim citation: " + sample(untraceable)),
        check(f"{key}.evidence.citations-resolve", not dangling, "citations naming unknown claims: " + sample(dangling)),
        check(f"{key}.evidence.no-unverified-claims", not unverified_admitted and not unrelated_used,
              "unverified claims admitted or related: " + sample(unverified_admitted + unrelated_used)),
        check(f"{key}.evidence.every-usable-claim-admitted", not not_admitted, "usable claims not admitted: " + sample(not_admitted)),
        check(f"{key}.claims.verified-have-locator", not no_locator, "verified claims without a locator: " + sample(no_locator)),
        check(f"{key}.claims.verified-only-from-retrieved-sources", not unread, "verified claims from unretrieved sources: " + sample(unread)),
        check(f"{key}.claims.findings-from-primary-sources", not secondary_only, "official-finding claims resting on secondary sources: " + sample(secondary_only)),
        check(f"{key}.claims.excerpts-concise", not long_excerpts, "excerpts over 60 words: " + sample(long_excerpts)),
        (passed(f"{key}.claims.verified-have-excerpt", "every verified claim carries an exact excerpt") if not verified_without_excerpt
         else review(f"{key}.claims.verified-have-excerpt", "verified claims without an excerpt (support not machine-checkable): " + sample(verified_without_excerpt))),
    ]


def git(root: Path, *args: str) -> subprocess.CompletedProcess:
    return subprocess.run(["git", "-C", str(root), *args], capture_output=True, text=True)


def immutability_checks(root: Path, base: str) -> list[Check]:
    tag = git(root, "rev-parse", "-q", "--verify", f"refs/tags/{RELEASE_TAG}^{{commit}}")
    frozen = git(root, "diff", "--name-only", f"refs/tags/{RELEASE_TAG}", "--", *FROZEN_PATHS) if tag.returncode == 0 else None
    untracked = git(root, "status", "--porcelain", "--", *FROZEN_PATHS)
    ws = git(root, "diff", "--name-status", base, "--", "workspaces/demo")
    allowed_prefixes = tuple(f"workspaces/demo/{workspace}/" for workspace in ALLOWED_ADDITION_WORKSPACES)
    changed_prior = [l for l in ws.stdout.splitlines() if l and not l.split("\t")[-1].startswith(allowed_prefixes)]
    modified_ws = [l for l in ws.stdout.splitlines() if l and not l.startswith("A")]
    allowed = ", ".join(f"workspaces/demo/{workspace}/" for workspace in ALLOWED_ADDITION_WORKSPACES)
    return [
        (check("immutability.frozen-paths-equal-release-tag", frozen.returncode == 0 and not frozen.stdout.strip() and not untracked.stdout.strip(),
               f"{', '.join(FROZEN_PATHS)} identical to {RELEASE_TAG}" + (f"; differ: {frozen.stdout.split()}" if frozen.stdout.strip() else ""))
         if frozen is not None else failed("immutability.frozen-paths-equal-release-tag", f"tag {RELEASE_TAG} not available locally (git fetch --tags)")),
        check("immutability.prior-workspaces-untouched", not changed_prior and not modified_ws,
              f"only additions under {allowed} relative to {base}" + (f"; other changes: {changed_prior + modified_ws}" if changed_prior or modified_ws else "")),
    ]


def secret_checks(root: Path, base: str) -> list[Check]:
    changed = [l for l in git(root, "diff", "--name-only", "--diff-filter=AM", base).stdout.splitlines() if l]
    untracked = [l for l in git(root, "ls-files", "--others", "--exclude-standard").stdout.splitlines() if l]
    files = [root / p for p in sorted(set(changed + untracked)) if (root / p).is_file()]
    hits = [f"{p.relative_to(root)}: {name}" for p in files for name, rx in SECRET_PATTERNS if rx.search(p.read_text(encoding="utf-8", errors="replace"))]
    return [check("security.no-credentials", not hits, f"{len(files)} added/changed files scanned for {len(SECRET_PATTERNS)} credential patterns" + (f"; hits: {hits}" if hits else ""))]


# Each mutation turns a valid directory into one that a conforming reader must refuse, with the expected stage code.
def _events_lines(raw: dict) -> list[str]:
    return raw["events.ndjson"].decode("utf-8").split("\n")[:-1]


def _with_events(raw: dict, lines: list[str]) -> dict:
    return {**raw, "events.ndjson": ("\n".join(lines) + "\n").encode("utf-8")}


def _replace_in_line(raw: dict, n: int, old: str, new: str) -> dict:
    lines = _events_lines(raw)
    return _with_events(raw, [l.replace(old, new, 1) if i == n else l for i, l in enumerate(lines)])


MUTATIONS: tuple[tuple[str, str, Callable[[dict], dict]], ...] = (
    ("truncated-event-line", "CAUSA.JSON_MALFORMED", lambda r: _with_events(r, _events_lines(r)[:-1] + [_events_lines(r)[-1][:-7]])),
    ("crlf", "CAUSA.ENCODING", lambda r: {**r, "events.ndjson": r["events.ndjson"].replace(b"\n", b"\r\n")}),
    ("future-format-version", "CAUSA.UNSUPPORTED_VERSION", lambda r: _replace_in_line(r, 3, '"formatVersion":"1.0.0"', '"formatVersion":"2.0.0"')),
    ("unknown-event-type", "CAUSA.UNKNOWN_EVENT_TYPE", lambda r: _replace_in_line(r, 3, '"eventType":"', '"eventType":"Forged')),
    ("missing-snapshot", "CAUSA.PATH_INVALID", lambda r: {k: v for k, v in r.items() if k != "snapshot.json"}),
    ("extra-file", "CAUSA.PATH_INVALID", lambda r: {**r, "notes.txt": b"x\n"}),
    ("edited-evidence-without-rehash", "CAUSA.EVENTS_HASH_MISMATCH",
     lambda r: _replace_in_line(r, next(i for i, l in enumerate(_events_lines(r)) if "Paraphrase: " in l), "Paraphrase: ", "Paraphrase: (edited) ")),
    ("dropped-last-event", "CAUSA.MANIFEST_MISMATCH", lambda r: _with_events(r, _events_lines(r)[:-1])),
    ("forged-snapshot", "CAUSA.SNAPSHOT_HASH_MISMATCH", lambda r: {**r, "snapshot.json": r["snapshot.json"].replace(b'"title":"', b'"title":"Forged ', 1)}),
)


def mutation_checks(root: Path, consumer, schemas, entry: Mapping, causa_dll: Path | None) -> list[Check]:
    key, iid = entry["caseKey"], entry["investigationId"]
    raw = investigation_raw(root / entry["path"])

    def fsharp_verdict(files: Mapping[str, bytes]) -> str:
        if causa_dll is None:
            return "NOT_RUN"
        with tempfile.TemporaryDirectory(prefix="causa-mutation-") as tmp:
            d = Path(tmp) / iid
            d.mkdir()
            for name, data in files.items():
                (d / name).write_bytes(data)
            out = subprocess.run(["dotnet", str(causa_dll), "validate-dir", str(root), str(d)], capture_output=True, text=True).stdout.split()
            return out[1].split("/")[0] if len(out) > 1 and out[0] == "INVALID" else ("VALID" if out[:1] == ["VALID"] else "ERROR")

    def one(name: str, expected: str, mutate: Callable[[dict], dict]) -> Check:
        files = mutate(dict(raw))
        if files == raw:
            return failed(f"{key}.refuses.{name}", "mutation did not change the file set (test defect)")
        py = consumer.run_pipeline(iid, files, schemas).primary
        fs = fsharp_verdict(files)
        ok = py is not None and py.code == expected and fs in (expected, "NOT_RUN")
        return check(f"{key}.refuses.{name}", ok, f"expected {expected}; python reader {py.code if py else 'ACCEPTED'}; F# reader {fs}")

    return [one(*m) for m in MUTATIONS]


def app_checks(root: Path, app: Path | None, manifest: Mapping) -> list[Check]:
    if app is None:
        return [review("app.no-bundled-catalog-data", "no --app checkout given; not evaluated")]
    needles = [n for c in manifest["cases"] for n in (c["investigationId"], c["title"], c["eventsSha256"], c["snapshotSha256"], c["expectedStateSha256"])]
    needles += [e["eventId"] for c in manifest["cases"] for e in events_of(investigation_raw(root / c["path"]))]
    tracked = [l for l in subprocess.run(["git", "-C", str(app), "ls-files"], capture_output=True, text=True).stdout.splitlines() if l]
    texts = ((p, (app / p).read_bytes().decode("latin1")) for p in tracked if (app / p).is_file() and (app / p).stat().st_size < 64 * 1048576)
    hits = [f"{p}: {n[:40]}" for p, t in texts for n in needles if n in t]
    return [check("app.no-bundled-catalog-data", not hits, f"{len(tracked)} tracked application files scanned for {len(needles)} catalog identifiers" + (f"; hits: {hits[:10]}" if hits else ""))]


def verify(root: Path, app: Path | None, base: str) -> list[Check]:
    consumer = load_consumer(root)
    schemas_r = consumer.load_schemas(root / "contract/v1.0.0/schemas")
    if not isinstance(schemas_r, consumer.Ok):
        return [failed("schemas.load", str(schemas_r))]
    schemas = schemas_r.value
    head, manifest = catalog_checks(root, consumer)
    if manifest is None:
        return head
    dll = root / "tools/src/Causa.Tool/bin/Release/net10.0/causa.dll"
    causa_dll = dll if dll.exists() and shutil.which("dotnet") else None
    per_case = [
        c
        for entry in manifest["cases"]
        for c in pipeline_checks(root, consumer, schemas, entry)
        + research_checks(root, entry["caseKey"], events_of(investigation_raw(root / entry["path"])))
        + mutation_checks(root, consumer, schemas, entry, causa_dll)
    ]
    return head + per_case + immutability_checks(root, base) + secret_checks(root, base) + app_checks(root, app, manifest)


# ---------------------------------------------------------------------------------------------------------------------
# Source reachability and excerpt confirmation (network)
# ---------------------------------------------------------------------------------------------------------------------

def normalize(text: str) -> str:
    t = unicodedata.normalize("NFKC", html.unescape(text))
    t = t.translate({ord(c): r for c, r in (("‘", "'"), ("’", "'"), ("“", '"'), ("”", '"'), ("–", "-"), ("—", "-"), ("­", ""))})
    t = re.sub(r"-\s*\n\s*", "", t)  # hyphenation across lines
    return re.sub(r"[^a-z0-9]+", " ", t.lower()).strip()


def fetch(url: str, cache: Path) -> tuple[int | None, str]:
    name = hashlib.sha256(url.encode()).hexdigest()
    raw_path = cache / name
    try:
        if not raw_path.exists():
            req = urllib.request.Request(url, headers={"User-Agent": "causa-historical-catalog-verifier/1.0"})
            with urllib.request.urlopen(req, timeout=90) as resp:
                raw_path.write_bytes(resp.read())
                status = resp.status
        else:
            status = 200
        data = raw_path.read_bytes()
        if data[:5] == b"%PDF-":
            txt = subprocess.run(["pdftotext", "-layout", str(raw_path), "-"], capture_output=True).stdout.decode("utf-8", "replace")
        else:
            txt = re.sub(r"<[^>]+>", " ", data.decode("utf-8", "replace"))
        return status, txt
    except Exception as ex:  # network boundary: every failure becomes a reported status
        code = getattr(ex, "code", None)
        return (code if isinstance(code, int) else None), ""


def excerpt_found(excerpt: str, text: str) -> bool:
    e, t = normalize(excerpt), normalize(text)
    if e in t:
        return True
    # OCR text layers of scanned reports often letter-space words ("p e r c e n t"): compare without spaces
    if len(e) >= 20 and e.replace(" ", "") in t.replace(" ", ""):
        return True
    words = e.split()
    # tolerate OCR / line-break noise: every 6-word window of the excerpt must appear
    windows = [" ".join(words[i:i + 6]) for i in range(0, max(1, len(words) - 5), 3)]
    return bool(words) and all(w in t for w in windows)


def source_checks(root: Path) -> list[Check]:
    cache = Path(tempfile.gettempdir()) / "causa-historical-source-cache"
    cache.mkdir(exist_ok=True)

    def per_case(key: str) -> list[Check]:
        sources, claims = research(root, key)
        fetched = {s["sourceId"]: fetch(s["url"], cache) for s in sources}
        reach = [
            (passed if status == 200 and text else review)(f"{key}.source.{s['sourceId']}.reachable", f"HTTP {status}; {len(text)} chars; {s['url']}")
            for s in sources for status, text in [fetched[s["sourceId"]]]
        ]
        excerpts = [
            (passed if excerpt_found(c["excerpt"], fetched[c["sourceId"]][1]) else review)(
                f"{key}.excerpt.{c['claimId']}",
                ("excerpt found in the fetched source text" if excerpt_found(c["excerpt"], fetched[c["sourceId"]][1])
                 else f"excerpt not found automatically in {c['sourceId']} (scanned PDF, dynamic page or mismatch): human review required"))
            for c in claims if c.get("excerpt") and c["verification"] != "unverified"
        ]
        return reach + excerpts

    return [c for key in CASE_KEYS for c in per_case(key)]


# ---------------------------------------------------------------------------------------------------------------------

def report(checks: Sequence[Check], path: Path | None) -> int:
    for c in checks:
        print(f"{c.status} {c.name} — {c.detail}")
    counts = {s: sum(1 for c in checks if c.status == s) for s in ("PASS", "FAIL", "REVIEW")}
    print(f"checks={len(checks)} passed={counts['PASS']} failed={counts['FAIL']} review={counts['REVIEW']}")
    if path is not None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps({"counts": counts, "checks": [c.__dict__ for c in checks]}, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return 0 if counts["FAIL"] == 0 else 1


def option(args: Sequence[str], name: str) -> str | None:
    return args[args.index(name) + 1] if name in args and args.index(name) + 1 < len(args) else None


def main(argv: Sequence[str]) -> int:
    if len(argv) < 2 or argv[0] not in ("verify", "sources"):
        print(__doc__)
        return 2
    root = Path(argv[1]).resolve()
    out = option(argv, "--report")
    if argv[0] == "sources":
        return report(source_checks(root), Path(out) if out else None)
    app = option(argv, "--app")
    base = option(argv, "--base") or "origin/main"
    return report(verify(root, Path(app).resolve() if app else None, base), Path(out) if out else None)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
