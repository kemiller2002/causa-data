# Causa Storage Contract v1.1.0 (DRAFT for owner decision)

| | |
|---|---|
| Status | **DRAFT.** Not released, not tagged, not frozen. Release is the owner's act: merge, then tag `causa-storage-v1.1.0` (Indy-init ODR-V416-02 / S-10). |
| Contract version | `1.1.0` (`formatVersion`, `schemaVersion`, `semanticVersion` are all `"1.1.0"`) |
| Change class | Compatible additive extension (v1.0.0 §13.1): two new event types and two new enumeration values |
| Normative schemas | `contract/v1.1.0/schemas/`; identity = SHA-256 of `schemas/bundle.json` |
| Base | `contract/v1.0.0/CONTRACT.md` (frozen, tag `causa-storage-v1.0.0`), unchanged |
| Authority | OD-CR-01 A (Indy-init APR-CR-OWNER-1/-2) narrowed by Indy-init ODR-V416-S1 B. The rules are owner decisions ODR-V416-S2..S9 and S-8. **Every one of these is pending** until the owner records it with `owner-freeze.fsx decide v4.1.6`. |
| Domain authority | 1.0.0 rules: the engine pinned in `engine.lock.json`, unchanged. 1.1.0 additions: this document, normatively (S-2 B), with the 1.1.0 negative fixtures as the oracle |

The key words MUST, MUST NOT, SHOULD and MAY are used as in RFC 2119.

## 1. What 1.1.0 is

**Everything in `contract/v1.0.0/CONTRACT.md` applies to 1.1.0 data unchanged, except what this document states.** This covers the wire conventions (§2), layout (§3), envelope and event identity (§5), canonical form, the error stages (§11), immutability, appends and providers (§9). 1.1.0 adds:

- two event types: `EvidenceRevised` and `ObservationRevised` (§4.1, §4.2);
- two `relationshipKind` values: `consistentWith` and `inconclusive` (§4.3).

Nothing else changes. No v1.0.0 event, field, enumeration value, projection rule or error code changes meaning, and no v1.0.0 file is rewritten. This is the minimal content of OD-CR-01 A (S-1 B). `InvestigationDetailsRevised`, `HypothesisRevised`, `EvidenceRelationshipRevised` and `EvidenceRelationshipWithdrawn` are left to a later 1.2.0.

## 2. Versions (§5.1 of STORAGE-1.1.md)

| file | 1.0.0 data (unchanged) | 1.1.0 data |
|---|---|---|
| event line `formatVersion`, `semanticVersion` | `"1.0.0"` | `"1.1.0"` |
| manifest `formatVersion`, `schemaVersion`, `semanticVersion` | `"1.0.0"` | `"1.1.0"` |
| snapshot `formatVersion`, `semanticVersion` | `"1.0.0"` | `"1.1.0"` |
| manifest `snapshotVersion`, snapshot `projectionVersion` | `1` | `1` (S-5 A: the 1.0.0 projection is unchanged; 1.1.0 is told apart by `formatVersion`) |

1. **Homogeneity.** Every version string in the three files of one investigation is the same. An investigation's version is fixed when it is created. A file set whose version strings differ is refused at stage 5 with `CAUSA.UNSUPPORTED_VERSION`, even when each value is one a reader supports (fixtures `mixed-version-line`, `mixed-version-manifest`).
2. **Event identity** (namespace `…/contract/v1/event-id`), RFC 8785 canonical form, the timestamp, UUID and text rules, and the error stages 1–17 are unchanged.

## 3. Readers and writers (v1.0.0 §13.2 applied)

| reader | 1.0.0 data | 1.1.0 data | any other version (`1.2.0`, `2.0.0`, …) or mixed |
|---|---|---|---|
| **1.0.0 reader** (frozen) | as today | refused whole at stage 5, `CAUSA.UNSUPPORTED_VERSION`; nothing is loaded | `CAUSA.UNSUPPORTED_VERSION` |
| **1.1.0 reader** | **exactly the 1.0.0 reader**: v1.0.0 schemas, 31-type registry, v1.0.0 enums and projection. A `"1.0.0"` line with `EvidenceRevised` is `CAUSA.UNKNOWN_EVENT_TYPE`; one with kind `consistentWith` is `CAUSA.SCHEMA_VIOLATION`. Every digest is unchanged. | the 1.1.0 schemas, the 33-type registry (§4.4), the §4.6 rules and the §4.7 projection | `CAUSA.UNSUPPORTED_VERSION` |

**Writers.**
1. New investigations are written in 1.1.0, only into new working-copy workspaces (§13.2). Catalogs are read-only.
2. A 1.0.0 investigation stays 1.0.0 (S-6 A). A command whose events need 1.1.0 is refused **before anything is written**, with `CAUSA.UNREPRESENTABLE`. Such a command is an evidence label, URL or revision, an observation source or revision, or a 1.1.0 kind. The pending command is kept (§9.2). **A value the user entered is never stripped to make a write succeed.** There is no migration.
3. Lengths are not storage rules (S-4 A). INV-027 ≤200, INV-025/028 ≤10,000 and INV-029 ≤2,000 are enforced by the UI and the command boundary.

## 4. The extension

### 4.1 `EvidenceRevised`

Revises the descriptive metadata of evidence. The admitted `content` is never revisable (OD-CR-06 A).

| field | type | nullable | constraint |
|---|---|---|---|
| `evidenceId` | uuid | no | lowercase canonical, non-nil |
| `label` | string | no | non-blank, verbatim (INV-027; required, S-7 A) |
| `source` | string | no | non-blank, verbatim (INV-028) |
| `sourceUrl` | httpUrl | yes | absolute http(s) URL, verbatim (INV-029); full well-formedness is a stage-15 rule (§4.6) |

### 4.2 `ObservationRevised`

| field | type | nullable | constraint |
|---|---|---|---|
| `observationId` | uuid | no | lowercase canonical, non-nil |
| `original` | string | no | non-blank, verbatim (INV-024) |
| `source` | string | yes | non-blank, verbatim (INV-025) |

### 4.3 Enumeration

`relationshipKind` = `supports`, `contradicts`, `discriminates`, `insufficient`, **`consistentWith`**, **`inconclusive`**. The UI maps INV-031's `consistent-with` to `consistentWith` and `inconclusive` to `inconclusive`. `insufficient` and `discriminates` keep their own meaning and are shown verbatim (OD-HC-07 B). Neither new kind is support.

**Reassessment (S-8 B).** A new `EvidenceRelated` for the same evidence–hypothesis pair is the reassessment, and the latest one is current. This is representable in 1.0.0 already.

### 4.4 Registry

33 event types: the 31 of v1.0.0 §7, then `EvidenceRevised` and `ObservationRevised`. Unregistered types still fail closed.

### 4.5 Writing (command groups)

The entered values travel in a revision event **in the same command group**, which shares `correlationId`, `occurredAt` and `actor`:
- `AdmitEvidence` emits `EvidenceAdmitted{…, source: S}` and then always `EvidenceRevised{evidenceId, label, source: S, sourceUrl}`.
- `RecordObservation` emits `ObservationRecorded{…, original: O}` and then `ObservationRevised{observationId, original: O, source}`, **only** when a source was entered.
- `ReviseEvidence` and `ReviseObservation` each emit one revision event, as a group of their own.

**Creation equality (S-3 A).** In a creation group, `EvidenceRevised.source` equals `EvidenceAdmitted.source`, and `ObservationRevised.original` equals `ObservationRecorded.original`, byte for byte. A writer refuses a difference (`CAUSA.UNREPRESENTABLE`). A reader refuses it at stage 15.

### 4.6 Admissibility of 1.1.0 histories (stage 15; normative, S-2 B)

A 1.1.0 history is admissible iff **both** of the following hold. If either fails, the primary code is `CAUSA.HISTORY_INADMISSIBLE`, with the domain code given below. The 1.1.0 rules (1) are evaluated first; then the core view (2).

1. **The 1.1.0 rules**, in history order:

| rule | domain code |
|---|---|
| A group that holds a revision event is either that revision alone, or the creation event of the same id followed by its revision (`EvidenceAdmitted`+`EvidenceRevised`, `ObservationRecorded`+`ObservationRevised`). Its events share `occurredAt` and `actor`. | `HISTORY.INADMISSIBLE_EVENT` |
| The investigation is open for recording (`draft`, `active`, `awaitingEvidence`, `awaitingVerification`). | `INVESTIGATION.NOT_OPEN` |
| The evidence or observation exists. The observation may be the `InvestigationStarted` one. | `HISTORY.INADMISSIBLE_EVENT` |
| The evidence is not superseded. | `EVIDENCE.SUPERSEDED` |
| `sourceUrl` is null, or starts with `http://`/`https://` (ASCII case-insensitive) and is a well-formed absolute URI (as the engine judges an artifact URL). | `EVIDENCE.URL` |
| The creation equality rule (§4.5) holds. | `EVIDENCE.REVISION_MISMATCH` / `OBSERVATION.REVISION_MISMATCH` |

2. **The core view is admissible under the pinned engine** (`History.validate`, v1.0.0 §11.3). The domain code is the engine's first error. The core view is the history:
   - without its revision events;
   - renumbered `1..k` in order;
   - with every version string `"1.0.0"`;
   - with `consistentWith` and `inconclusive` given to the engine as `insufficient`.

   No engine gate reads `insufficient`: the cause gate counts only `supports`, and the contradiction rules only `contradicts`. So the substitution changes no 1.0.0 rule's verdict, and the stored kind is never changed.

### 4.7 Projection (state) of a 1.1.0 investigation

The 1.0.0 projection (v1.0.0 §8.3) of the core view, at the stored `aggregateVersion`s, with these additions:

| change | rule |
|---|---|
| evidence entity | gains `label` (text \| null) and `sourceUrl` (httpUrl \| null), both null at admission |
| observation entity | gains `source` (text \| null), null at creation |
| `EvidenceRevised` | evidence `label ← label`, `source ← source`, `sourceUrl ← sourceUrl` |
| `ObservationRevised` | observation `original ← original`, `source ← source` |
| `EvidenceRelated` | `kind` is the stored kind (the extended enumeration) |
| `version`, snapshot `aggregateVersion`, `generatedAt` | of the last event, which may be a revision |

Provenance never changes. Earlier values stay in `events.ndjson`: INV-035 says "the earlier text and its provenance remain in the history". No new snapshot array is added.

## 5. Conformance fixtures (`fixtures/v1.1.0/`)

Generated by `causa v110-fixtures .`, and checked byte for byte by `causa v110-verify .`. Every 1.0.0 event in them was decided by the pinned engine (`Decide.execute`). The revision events and kinds are added as §4.5 says a writer adds them.

- `investigations/<id>/`: one valid J2-shaped investigation, `j2-analysis`. It covers:
  - both events and both kinds;
  - a URL with upper-case scheme and percent-encoding, and a null URL;
  - an observation with and without a source;
  - a revision after creation, a revision of the `InvestigationStarted` observation, and a reassessment of a pair (S-8 B);
  - supersession and a conclusion;
  - verbatim edge cases: leading and trailing spaces, U+200B, and an embedded LF.
- `negative/*.json`: 17 negative fixtures in the v1.0.0 negative-fixture shape, each failing exactly its targeted rule:
  - mixed versions (2), `1.2.0`;
  - a `"1.0.0"` history with `EvidenceRevised`, and one with kind `consistentWith`;
  - a null or blank label, and an unknown field;
  - three bad URLs (two failing the schema, one at stage 15);
  - both creation mismatches;
  - revision of missing or superseded evidence;
  - a group that disagrees on time;
  - an unknown kind.
- `index.json`: the list.

## 6. Compatibility tests (`tools/tests/Causa.Contract.Tests/V110Tests.fs`)

| id | test |
|---|---|
| ST-01 | Every 1.0.0 file set in the repository is read by the 1.1.0 reader as 1.0.0, with the same state, events, manifest and snapshot digests as the frozen reader: the 5 fixtures and the historical, everyday and demonstration workspaces and templates. The fixtures keep their `expectedStateSha256`. |
| ST-02 | Every v1.0.0 `investigation` negative fixture is refused by the 1.1.0 reader with the same primary and domain codes. |
| ST-04 | The 1.1.0 fixture verifies, and decoding then encoding reproduces `events.ndjson`, `manifest.json` and `snapshot.json` byte for byte. Every line is canonical. The committed files equal the deterministic rebuild. |
| ST-05 | No entered value is dropped. Every string entered is in the history verbatim, and every current value is in the projected state after a reload. |
| ST-06 | The frozen 1.0.0 reader refuses the 1.1.0 file set whole (`CAUSA.UNSUPPORTED_VERSION`). |
| ST-07/08/10 | Every 1.1.0 negative fixture is refused with its codes. The frozen reader also refuses the mixed-version fixtures. |
| ST-09 | A 1.0.0 investigation refuses a 1.1.0-only line with `CAUSA.UNREPRESENTABLE`, and its files are untouched. |
| ST-11/12 | Verbatim edge cases. Both kinds are stored and never read as support. |

## 7. Release (owner)

1. Record the ODR-V416 decisions (Indy-init).
2. Merge this draft.
3. Tag `causa-storage-v1.1.0`.
4. Pin a `compatibility/v1.1.0/lock.json`, as `compatibility/v1.0.0` pins 1.0.0. **Not done in this draft:** the v1.0.0 gate (`causa gate --anchor causa-storage-v1.0.0`) is specific to 1.0.0, and a 1.1.0 lock is part of the release.

`contract/v1.0.0`, `fixtures/v1.0.0` and `compatibility/v1.0.0` are not touched, and the anchored v1.0.0 gate still passes.
