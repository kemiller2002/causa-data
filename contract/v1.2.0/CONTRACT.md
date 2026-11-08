# Causa Storage Contract v1.2.0

| | |
|---|---|
| Status | **Approved by the owner for publication** (INV-034: the title and the primary investigative question can be edited after an investigation has started). Release is the owner's act: merge, then tag `causa-storage-v1.2.0`. |
| Contract version | `1.2.0` (`formatVersion`, `schemaVersion`, `semanticVersion` are all `"1.2.0"`) |
| Change class | Compatible additive extension (v1.0.0 §13.1): two new event types |
| Normative schemas | `contract/v1.2.0/schemas/`; identity = SHA-256 of `schemas/bundle.json` |
| Base | `contract/v1.1.0/CONTRACT.md` (which rests on the frozen `contract/v1.0.0/CONTRACT.md`), unchanged |
| Owner decisions | (1) publish causa-storage 1.2.0; (2) only investigations created in 1.2.0 can have their title or question revised: 1.1.0 (and 1.0.0) investigations keep their version and are never upgraded in place, and a writer refuses such a revision with `CAUSA.UNREPRESENTABLE`; (3) the question is revised, not superseded, so the unknown keeps its id; (4) new investigations are written in 1.2.0. |
| Domain authority | 1.0.0 rules: the engine pinned in `engine.lock.json`, unchanged. 1.1.0 additions: `contract/v1.1.0/CONTRACT.md`. 1.2.0 additions: this document, normatively, with the 1.2.0 negative fixtures as the oracle |

The key words MUST, MUST NOT, SHOULD and MAY are used as in RFC 2119.

## 1. What 1.2.0 is

**Everything in `contract/v1.1.0/CONTRACT.md` applies to 1.2.0 data unchanged, except what this document states.** That
includes everything v1.1.0 inherits from v1.0.0: the wire conventions, layout, envelope and event identity, canonical
form, the error stages, immutability, appends and providers. 1.2.0 adds two event types:

- `InvestigationTitleRevised` (§4.1): revises the investigation's title;
- `UnknownRevised` (§4.2): revises an open unknown's question.

Nothing else changes. No 1.0.0 or 1.1.0 event, field, enumeration value, projection rule or error code changes meaning,
no snapshot array is added, and no 1.0.0 or 1.1.0 file is rewritten. Every 1.0.0 and 1.1.0 schema is byte-identical
to its release; each 1.2.0 schema is its 1.1.0 schema with version strings set to 1.2.0, plus the two payloads and
their registration in `event-envelope.schema.json`.

## 2. Versions

| file | 1.0.0 data | 1.1.0 data | 1.2.0 data |
|---|---|---|---|
| event line `formatVersion`, `semanticVersion` | `"1.0.0"` | `"1.1.0"` | `"1.2.0"` |
| manifest `formatVersion`, `schemaVersion`, `semanticVersion` | `"1.0.0"` | `"1.1.0"` | `"1.2.0"` |
| snapshot `formatVersion`, `semanticVersion` | `"1.0.0"` | `"1.1.0"` | `"1.2.0"` |
| manifest `snapshotVersion`, snapshot `projectionVersion` | `1` | `1` | `1` (the projection gains no array; 1.2.0 is told apart by `formatVersion`) |

**Homogeneity** (v1.1.0 §2.1) is unchanged: every version string in the three files of one investigation is the same,
and an investigation's version is fixed when it is created. A file set whose version strings differ is refused at
stage 5 with `CAUSA.UNSUPPORTED_VERSION`, even when each value is one a reader supports (fixture
`mixed-version-1-1-0-1-2-0`).

## 3. Readers and writers

| reader | 1.0.0 data | 1.1.0 data | 1.2.0 data | any other version, or mixed |
|---|---|---|---|---|
| **1.0.0 reader** (frozen) | as today | `CAUSA.UNSUPPORTED_VERSION` | `CAUSA.UNSUPPORTED_VERSION`, whole; nothing is loaded | `CAUSA.UNSUPPORTED_VERSION` |
| **1.1.0 reader** | as today | as today | `CAUSA.UNSUPPORTED_VERSION`, whole; nothing is loaded | `CAUSA.UNSUPPORTED_VERSION` |
| **1.2.0 reader** | exactly the 1.0.0 reader | exactly the 1.1.0 reader: a `"1.1.0"` line with `InvestigationTitleRevised` or `UnknownRevised` is `CAUSA.UNKNOWN_EVENT_TYPE` (fixture `v110-title-revised`) | the 1.2.0 schemas, the 35-type registry (§4.3), the §4.5 rules and the §4.4 projection | `CAUSA.UNSUPPORTED_VERSION` |

The 1.1.0 negative fixture `unsupported-version-1-2-0` (a 1.1.0 file set stamped `"1.2.0"`) remains the oracle for
1.1.0 readers, which do not support 1.2.0. It is not a 1.2.0 negative: a 1.2.0 reader judges that file set by the
1.2.0 rules.

**Writers.**
1. New investigations are written in 1.2.0, only into new working-copy workspaces (v1.0.0 §13.2). Catalogs are
   read-only.
2. A 1.0.0 or 1.1.0 investigation keeps its version (owner decision 2). A command whose events need 1.2.0 (a title or
   question revision) is refused **before anything is written**, with `CAUSA.UNREPRESENTABLE`; the pending command is
   kept. There is no migration and no upgrade in place. An application SHOULD NOT offer the edit on such an
   investigation, or MUST offer it disabled with that reason.
3. Lengths are not storage rules (v1.1.0 §3.3): INV-020's 200 characters and INV-023's 10,000 are enforced by the UI
   and the command boundary.

## 4. The extension

### 4.1 `InvestigationTitleRevised`

| field | type | nullable | constraint |
|---|---|---|---|
| `title` | string | no | non-blank, verbatim (INV-002) |
| `reason` | string | yes | non-blank, verbatim, or null |

### 4.2 `UnknownRevised`

| field | type | nullable | constraint |
|---|---|---|---|
| `unknownId` | uuid | no | lowercase canonical, non-nil |
| `question` | string | no | non-blank, verbatim (INV-023 when the unknown is the primary question) |

The question is **revised, not superseded** (owner decision 3): the unknown keeps its id.

### 4.3 Registry

35 event types: the 33 of v1.1.0 §4.4, then `InvestigationTitleRevised` and `UnknownRevised`. Unregistered types still
fail closed. `event-envelope.schema.json` accepts both new `eventType` values and binds each to its payload schema.

### 4.4 Projection (state) of a 1.2.0 investigation

The 1.1.0 projection (v1.1.0 §4.7), with these additions. No snapshot array is added.

| event | rule |
|---|---|
| `InvestigationTitleRevised` | state `title ← title` |
| `UnknownRevised` | that unknown's `question ← question`; its `state`, `basis`, `materiality` and `provenance` stay as they are |
| every event | sets `version`; snapshot `aggregateVersion` and `generatedAt` are the last event's, which may be a revision |

History is append-only: the earlier title and question stay in `events.ndjson`, with their provenance (INV-035).

### 4.5 Admissibility of 1.2.0 histories (stage 15; normative)

A 1.2.0 history is admissible iff the v1.1.0 §4.6 rules hold, evaluated as for a 1.1.0 history, **and** the following
rules hold, in history order. If any fails, the primary code is `CAUSA.HISTORY_INADMISSIBLE`, with the domain code given.

| rule | domain code |
|---|---|
| A group that holds `InvestigationTitleRevised` or `UnknownRevised` holds that one event only: each revision is a command group of its own (as `ReviseEvidence` and `ReviseObservation` in v1.1.0 §4.5). | `HISTORY.INADMISSIBLE_EVENT` |
| The investigation is open for recording (`draft`, `active`, `awaitingEvidence`, `awaitingVerification`). | `INVESTIGATION.NOT_OPEN` |
| For `UnknownRevised`: the unknown exists and is `identified` or `investigating`. | `HISTORY.INADMISSIBLE_EVENT` |

The core view of v1.1.0 §4.6 (2), given to the pinned engine, is the history without its 1.1.0 **and** 1.2.0 revision
events, renumbered `1..k`, with every version string `"1.0.0"` and the 1.1.0 kinds given as `insufficient`.

A writer's own domain rules (for example refusing a revision equal to the current value) are not storage rules; a
reader does not judge them.

## 5. Conformance fixtures (`fixtures/v1.2.0/`)

Generated by `tools/v120/build-fixtures.fsx`, deterministically (UUIDv5 ids, a fixed clock). The pinned engine knows no
1.1.0 or 1.2.0 event, so **the 1.2.0 fixture is not decided by the pinned engine**: its commands are decided by the
Causa application's domain kernel (`kemiller2002/causa`, `Causa.Domain`) and written by its causa-storage codec
(`Causa.Persistence`), the first 1.2.0 writer. The negative fixtures are mutations of that valid history, written with
the same codec so that each fails exactly its targeted rule. For every 1.2.0 rule the authority is this document's text
together with the negative fixtures; no fixture is evidence that the pinned engine accepted 1.2.0 data.

`tools/py/v120_fixtures.py verify .` checks the fixtures independently of that codec (python-jsonschema and a separate
reading of §4.4 and §4.5), and that the 1.0.0 Python consumer refuses the 1.2.0 investigation with
`CAUSA.UNSUPPORTED_VERSION`.

- `investigations/<id>/`: one valid investigation, `details-revision`, whose snapshot equals its replay. It covers:
  - both events, each as a group of its own, and a title revision with and without a reason;
  - a revision of the primary question (an informational unknown) and of a second, material unknown, both keeping their ids;
  - a revision of the `InvestigationStarted` observation (the problem statement, 1.1.0 `ObservationRevised`) in 1.2.0 data;
  - verbatim edge cases: leading and trailing spaces, U+200B and an embedded LF.
- `negative/*.json`: 10 negative fixtures in the v1.0.0 negative-fixture shape, each failing exactly its targeted rule:
  - an unregistered type in a 1.1.0 line (`v110-title-revised`: `CAUSA.UNKNOWN_EVENT_TYPE`);
  - mixed 1.1.0/1.2.0 versions (`CAUSA.UNSUPPORTED_VERSION`);
  - a revision of a closed investigation, for each event (`INVESTIGATION.NOT_OPEN`);
  - a revision of a missing and of a resolved unknown, and a revision sharing its group (`HISTORY.INADMISSIBLE_EVENT`);
  - a blank title, a blank question and an unknown field (`CAUSA.SCHEMA_VIOLATION`).
- `index.json`: the list.

## 6. Schemas

`tools/schema-authoring/author_schemas_v120.py` writes `contract/v1.2.0/schemas/` (all but `bundle.json`) from the
1.0.0 and 1.1.0 authoring scripts plus the two payloads, and `python3 tools/py/schema_bundle.py digest
contract/v1.2.0/schemas 1.2.0` writes `bundle.json`.

## 7. Release (owner)

1. Merge.
2. Tag `causa-storage-v1.2.0`.
3. Pin a `compatibility/v1.2.0/lock.json`, as `compatibility/v1.0.0` pins 1.0.0 (not done here: the lock is part of the
   release, and the anchored v1.0.0 gate is specific to 1.0.0).

`contract/v1.0.0`, `contract/v1.1.0`, `fixtures/v1.0.0`, `fixtures/v1.1.0` and `compatibility/v1.0.0` are not touched.
