#!/usr/bin/env python3
"""Independent (non-F#) checks of the v1.0.0 schema bundle with python-jsonschema.

  schema_bundle.py check   <schemas-dir>   metaschema-validate every schema and resolve every $ref
  schema_bundle.py digest  <schemas-dir>   print the bundle manifest (JCS) for the directory
"""
import hashlib
import json
import sys
from pathlib import Path

from jsonschema import Draft202012Validator
from referencing import Registry, Resource


def load(root):
    files = sorted(p for p in Path(root).rglob("*.schema.json"))
    return [(p.relative_to(root).as_posix(), p.read_bytes()) for p in files]


def registry(docs):
    return Registry().with_resources([(d["$id"], Resource.from_contents(d)) for d in docs])


def refs(value):
    if isinstance(value, dict):
        for k, v in value.items():
            if k == "$ref":
                yield v
            else:
                yield from refs(v)
    elif isinstance(value, list):
        for v in value:
            yield from refs(v)


def check(root):
    raw = load(root)
    docs = [json.loads(b) for _, b in raw]
    problems = []
    for (path, _), d in zip(raw, docs):
        try:
            Draft202012Validator.check_schema(d)
        except Exception as e:  # noqa: BLE001 - report every failure
            problems.append(f"{path}: metaschema: {e.message if hasattr(e, 'message') else e}")
    reg = registry(docs)
    for (path, _), d in zip(raw, docs):
        resolver = reg.resolver(base_uri=d["$id"])
        for r in refs(d):
            try:
                resolver.lookup(r)
            except Exception as e:  # noqa: BLE001
                problems.append(f"{path}: unresolved $ref {r}: {e}")
    return len(docs), problems


def jcs(value):
    # The bundle manifest contains only strings, integers, arrays and objects with ASCII keys,
    # for which json.dumps(sort_keys, compact, ensure_ascii=False) is RFC 8785 output.
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def bundle(root):
    entries = [{"path": path, "sha256": hashlib.sha256(b).hexdigest()} for path, b in load(root)]
    return {"formatVersion": "1.0.0", "contractVersion": "1.0.0", "algorithm": "sha256", "canonicalization": "RFC8785",
            "schemas": sorted(entries, key=lambda e: e["path"])}


def main():
    cmd, root = sys.argv[1], sys.argv[2]
    if cmd == "check":
        n, problems = check(root)
        for p in problems:
            print(p)
        print(f"schemas={n} problems={len(problems)}")
        sys.exit(1 if problems else 0)
    if cmd == "digest":
        sys.stdout.write(jcs(bundle(root)) + "\n")
        return
    sys.exit("unknown command")


if __name__ == "__main__":
    main()
