#!/usr/bin/env python3
"""Render human-readable registry tables directly from the frozen schema files.

  render_registry.py <schemas-dir> payloads   -> markdown table per event payload
  render_registry.py <schemas-dir> entities   -> markdown table per snapshot entity
  render_registry.py <schemas-dir> enums      -> markdown table of enumerations
  render_registry.py <schemas-dir> inventory  -> JSON field inventory (used by SCHEMA-RECONCILIATION.json)
  render_registry.py <schemas-dir> fill|check <CONTRACT.md> -> refresh / verify the generated sections

Tables are derived from the schemas so documentation cannot drift from the contract.
"""
import json
import sys
from pathlib import Path


def load(root):
    return {p.relative_to(root).as_posix(): json.loads(p.read_text()) for p in Path(root).rglob("*.schema.json")}


def common_defs(docs):
    return docs["common.schema.json"]["$defs"]


def describe(node, docs, depth=0):
    """Return (wireType, nullable, constraint text) for a property schema."""
    defs = common_defs(docs)
    if "$ref" in node:
        ref = node["$ref"]
        target, _, frag = ref.partition("#")
        name = frag.split("/")[-1] if frag else Path(target).name.replace(".schema.json", "")
        if target.endswith("common.schema.json") or (target == "" and name in defs):
            d = defs[name]
            if "enum" in d:
                return "enum " + name, False, " \\| ".join(d["enum"])
            if "oneOf" in d:
                inner = [describe(x, docs, depth + 1) for x in d["oneOf"]]
                non_null = [i for i in inner if i[0] != "null"]
                return non_null[0][0], True, non_null[0][2]
            if "allOf" in d:
                base = describe(d["allOf"][0], docs, depth + 1)
                return base[0], False, base[2] + "; at least 1 item"
            if "const" in d:
                return "string", False, "const \"" + d["const"] + "\""
            return {"uuid": ("uuid", False, "lowercase canonical, non-nil"),
                    "uuidList": ("array<uuid>", False, "ordered, distinct"),
                    "timestamp": ("timestamp", False, "UTC, millisecond, Z"),
                    "text": ("string", False, "non-blank, verbatim"),
                    "httpUrl": ("string", False, "absolute http(s) URL"),
                    "sha256": ("string", False, "64 lowercase hex"),
                    "aggregateVersion": ("integer", False, "1..2^53-1"),
                    "versionOrZero": ("integer", False, "0..2^53-1"),
                    "slug": ("string", False, "^[a-z0-9][a-z0-9-]{0,63}$"),
                    "errorCode": ("string", False, "^CAUSA\\.[A-Z_]+$")}.get(name, (name, False, ""))
        if target.endswith("actor.schema.json"):
            return name, False, "see actor.schema.json"
        return "object " + name, False, "see " + target
    if "oneOf" in node:
        inner = [describe(x, docs, depth + 1) for x in node["oneOf"]]
        non_null = [i for i in inner if i[0] != "null"]
        return non_null[0][0], len(non_null) < len(inner), non_null[0][2]
    if node.get("type") == "null":
        return "null", True, ""
    if "const" in node:
        return type(node["const"]).__name__.replace("str", "string").replace("int", "integer"), False, "const " + json.dumps(node["const"])
    if node.get("type") == "array":
        items = node.get("items", {})
        it = describe(items, docs, depth + 1)[0] if items else "any"
        bits = []
        if "minItems" in node:
            bits.append(f"min {node['minItems']}")
        if "maxItems" in node:
            bits.append(f"max {node['maxItems']}")
        if node.get("uniqueItems"):
            bits.append("unique")
        return f"array<{it}>", False, ", ".join(bits)
    if "enum" in node:
        return "enum", False, " \\| ".join(node["enum"])
    if node.get("type") == "object":
        return "object", False, ""
    return node.get("type", "?"), False, ""


def conditional_rules(schema):
    rules = []
    for rule in schema.get("allOf", []):
        cond = rule.get("if", {}).get("properties", {})
        then = rule.get("then", {}).get("properties", {})
        if not cond or not then:
            continue
        (cfield, cval), = [(k, v.get("const")) for k, v in cond.items()]
        effects = []
        for k, v in then.items():
            if "const" in v:
                effects.append(f"`{k}` = `{json.dumps(v['const'])}`")
            elif v.get("type") == "null":
                effects.append(f"`{k}` is null")
            elif "$ref" in v:
                effects.append(f"`{k}` must be " + v["$ref"].split("/")[-1])
        rules.append(f"`{cfield}` = `{cval}` ⇒ " + ", ".join(effects))
    return rules


def payload_tables(docs):
    out = []
    for path in sorted(p for p in docs if p.startswith("payloads/")):
        s = docs[path]
        name = path.split("/")[1].replace(".schema.json", "")
        out.append(f"#### `{name}`\n\n{s['description']}\n\n| field | type | nullable | constraint |\n|---|---|---|---|")
        for field in s["required"]:
            t, n, cst = describe(s["properties"][field], docs)
            out.append(f"| `{field}` | {t} | {'yes' if n else 'no'} | {cst} |")
        rules = conditional_rules(s)
        if rules:
            out.append("\nInvariants: " + "; ".join(rules) + ".")
        out.append("")
    return "\n".join(out)


def entity_tables(docs):
    out = []
    for path in sorted(p for p in docs if p.startswith("entities/")):
        s = docs[path]
        name = path.split("/")[1].replace(".schema.json", "")
        out.append(f"#### `{name}`\n\n{s['description']}\n\n| field | type | nullable | constraint |\n|---|---|---|---|")
        for field in s["required"]:
            t, n, cst = describe(s["properties"][field], docs)
            out.append(f"| `{field}` | {t} | {'yes' if n else 'no'} | {cst} |")
        out.append("")
    return "\n".join(out)


def enum_table(docs):
    out = ["| enumeration | wire values |", "|---|---|"]
    for k, v in common_defs(docs).items():
        if "enum" in v:
            out.append(f"| `{k}` | " + ", ".join(f"`{x}`" for x in v["enum"]) + " |")
    return "\n".join(out)


def inventory(docs):
    items = []
    for path in sorted(docs):
        s = docs[path]
        if "required" not in s:
            continue
        for field in s["required"]:
            t, n, cst = describe(s["properties"][field], docs)
            items.append({"schema": path, "field": field, "wireType": t, "nullable": n, "required": True, "constraint": cst})
    return items


def filled(text, docs):
    import re
    sections = {"enums": enum_table(docs), "payloads": payload_tables(docs), "entities": entity_tables(docs)}
    for name, body in sections.items():
        pattern = re.compile(r"(<!-- BEGIN GENERATED: " + name + r" -->\n).*?(<!-- END GENERATED: " + name + r" -->)", re.S)
        text = pattern.sub(lambda m: m.group(1) + body.rstrip("\n") + "\n" + m.group(2), text)
    return text


def main():
    docs = load(sys.argv[1])
    mode = sys.argv[2]
    if mode in ("fill", "check"):
        path = Path(sys.argv[3])
        current = path.read_text(encoding="utf-8")
        wanted = filled(current, docs)
        if mode == "fill":
            path.write_text(wanted, encoding="utf-8", newline="\n")
            return
        if current != wanted:
            sys.exit("CONTRACT.md generated sections are out of date with the schemas")
        print("CONTRACT.md generated sections match the schemas")
        return
    if mode == "payloads":
        print(payload_tables(docs))
    elif mode == "entities":
        print(entity_tables(docs))
    elif mode == "enums":
        print(enum_table(docs))
    elif mode == "inventory":
        print(json.dumps(inventory(docs), indent=1))


if __name__ == "__main__":
    main()
