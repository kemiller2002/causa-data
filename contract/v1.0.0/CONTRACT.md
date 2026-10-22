# Causa Storage Contract v1.0.0

| | |
|---|---|
| Contract version | `1.0.0` (`formatVersion`, `schemaVersion`, `semanticVersion` are all `"1.0.0"`) |
| Dataset id | `causa-demo-v1` |
| Normative schemas | `contract/v1.0.0/schemas/` (JSON Schema Draft 2020-12), identity = SHA-256 of `schemas/bundle.json` |
| Domain authority | `kemiller2002/indy-init-rehearsal-001` engine at commit `55f49f61c939a3a780cee592f36f732a1328f981` (`engine.lock.json`) |
| Supersedes | the *candidate* `Indy-init/docs/CAUSA-DATA-CONTRACT-v1.md` §1–§10, where reconciliation required a change (see §16) |

The key words MUST, MUST NOT, SHOULD and MAY are used as in RFC 2119.

This document and the schemas together are the wire contract. Neither is derived from F# record
serialization. Where this text and a schema disagree, the disagreement is a contract defect, and readers MUST treat the
input as invalid until it is resolved through §14.

---

## 1. Scope and authority

1. The contract defines how one Causa investigation is persisted: its **authoritative event history**, a **derived
   snapshot**, and a **manifest**. It also defines the frozen demonstration dataset and the negative fixtures.
2. **Events are the only authority.** Snapshots, manifests, golden projections and indexes are derived. They MUST be
   reproducible from the event history and MUST NOT introduce facts that are absent from it.
3. **Domain semantics** (which histories are admissible) are owned by the F# domain engine pinned in
   `engine.lock.json`: `Decide.decide`, `Evolve.apply` and `History.validate`. This contract does not restate the
   decision rules. It names the engine as the semantic oracle (§11.3).
4. **Provider metadata** (Git commit/blob SHA, branch, repository path, ETag, tokens) is not domain data. It MUST NOT
   appear in any file defined here (§9.4).

## 2. Wire conventions (all files)

1. **Encoding.** UTF-8, no byte order mark, LF line endings only. No CR anywhere. Invalid UTF-8 is rejected.
2. **Canonical form.** Every JSON text MUST be exactly its RFC 8785 (JCS) canonical serialization. Keys are sorted by
   UTF-16 code units, there is no insignificant whitespace, and strings use the JCS escaping rules (`\"`, `\\`, `\b`, `\f`,
   `\n`, `\r`, `\t`, other controls as lowercase `\u00xx`, everything else literal). A JSON file (`manifest.json`,
   `snapshot.json`, root manifest, golden, negative fixture, `bundle.json`) is one canonical text followed by exactly one LF.
   `events.ndjson` is one canonical text per line, each terminated by LF, with no blank lines. Non-canonical bytes are rejected
   (`CAUSA.NON_CANONICAL`) even if semantically equal, because digests are over bytes.
   **Framing** (`CAUSA.ENCODING`) covers only: an empty file, a CR anywhere, a missing final LF, and (in
   `events.ndjson`) a blank line. Any other deviation of a JSON file from its exact canonical text (interior or extra
   LFs, spaces) is `CAUSA.NON_CANONICAL`, or `CAUSA.JSON_MALFORMED` if it is not a single JSON text.
   Schema files (`*.schema.json`) are human-authored and exempt from canonical form. Like `bundle.json`, they are
   identified by the digest of their exact bytes (§10.3).
3. **Values.** No comments, NaN, Infinity, or floating-point numbers. Integers are JSON integers in
   `[-(2^53-1), 2^53-1]`. Non-integers, exponents and out-of-range integers are `CAUSA.JSON_MALFORMED`; `-0` parses but is
   `CAUSA.NON_CANONICAL`. Readers MUST accept nesting up to depth 128 and MUST reject deeper documents as
   `CAUSA.JSON_MALFORMED`. Depth counts each object or array on the path, the top-level one being depth 1; scalars
   add nothing. Valid v1 documents nest at most 6 levels. Fixtures `nesting-depth-128` and `nesting-depth-129` pin the
   boundary. Object keys are case-sensitive and MUST be unique (`CAUSA.JSON_MALFORMED`). They are
   lowerCamelCase, except the file-name keys of negative-fixture `files` objects. Unpaired UTF-16 surrogates are
   invalid. U+FEFF is forbidden everywhere: as a leading BOM (`CAUSA.ENCODING`), and anywhere else, raw or escaped as
   `\ufeff`, including inside string values (`CAUSA.JSON_MALFORMED`). Writers refuse text containing it
   (`CAUSA.UNREPRESENTABLE`).
4. **Closed structures.** Every object defined here is closed (`additionalProperties: false`). Unknown fields are
   rejected, never ignored. v1.0.0 defines **no extension namespace** (§13.5).
5. **Required vs nullable.** v1.0.0 has **no optional keys**: every declared key MUST be present. A value that the
   domain models as an `option` is encoded as `null` when absent. `null` is allowed only where a schema says so.
6. **Text.** Domain text (`NonEmptyText` in the engine) is stored **verbatim**: never trimmed, case-folded or
   Unicode-normalized. It MUST contain at least one character outside the engine whitespace set
   `U+0009–U+000D, U+0020, U+0085, U+00A0, U+1680, U+2000–U+200A, U+2028, U+2029, U+202F, U+205F, U+3000`. NFC
   normalization is **not** required (it would rewrite verbatim observations). The test is per UTF-16 code unit, so
   zero-width characters such as U+200B count as non-blank.
7. **Arrays.** Arrays are ordered and the order is significant. Identifier arrays (`evidenceIds`, `causeIds`,
   `targetHypothesisIds`, `targetCauseIds`, `basisEvidenceIds`) MUST NOT contain duplicates and are **not** sorted: they
   preserve the order in which the domain command cited them. Snapshot entity arrays are sorted (§8.3). The engine
   itself accepts a command that cites the same evidence twice, or repeats an identical causal member. Such an event
   has no v1 wire form (`CAUSA.UNREPRESENTABLE`), so the command boundary MUST de-duplicate before deciding.
8. **UUIDs.** Lowercase, hyphenated, 36 characters (`^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$`).
   The nil UUID is invalid. Any UUID version is accepted.
9. **Timestamps.** RFC 3339 UTC with exactly three fractional digits and `Z`: `2026-10-22T12:00:00.000Z`. The value MUST be
   a real proleptic-Gregorian instant in years `0001`–`9999` (no `02-30`, no leap second `60`). Calendar validity is a
   stage-7 rule carried by the `timestamp` definition in `common.schema.json`, for every timestamp in every file,
   even though the schema pattern cannot express it. **The command boundary MUST supply millisecond-truncated UTC**
   (for example, the engine's `RequestedAt`). A runtime clock such as `DateTimeOffset.UtcNow` has 100 ns ticks, and
   the engine accepts those, but a v1 writer refuses them. Histories in the rehearsal's legacy `rci.events/v1` format
   (7-digit fractions) carry sub-millisecond instants; migrating them would change their meaning, so they are not
   migrated silently. Writers MUST refuse instants with sub-millisecond precision
   rather than truncate them (`CAUSA.UNREPRESENTABLE`). Time zone offsets are not preserved. Only the instant is
   semantic.
10. **Enumerations.** Persisted as lowerCamelCase strings from closed sets (§6). There is no fallback `other` value.
11. **Hashes.** SHA-256, lowercase 64-character hex. JSON documents are hashed over their **canonical text without the
    trailing LF**. Three things are hashed over their **exact file bytes** instead: `events.ndjson`, the schema files,
    and `bundle.json` (whose byte digest is the schema identity).
12. **No secrets, credentials, personal data or embedded binaries.** Artifacts are referenced by absolute `http(s)` URL.
13. **Regular expressions** in the schemas have ECMA-262 semantics, as JSON Schema requires. In particular, a final `$`
    matches only at the very end of the string, never before a trailing LF. Consumers whose regex engine differs MUST
    translate: Python `re.search` and .NET both let `$` match before a final `\n`, so use `\Z` or `\z`. A value such
    as `"<uuid>\n"` is invalid (negative fixture `identifier-trailing-newline`).

## 3. Physical layout and paths

```text
contract/v1.0.0/CONTRACT.md
contract/v1.0.0/schemas/**/*.schema.json      normative schemas
contract/v1.0.0/schemas/bundle.json           schema bundle manifest (§10.3)
contract/v1.0.0/golden/<scenarioKey>.state.json  expected projections
contract/v1.0.0/golden/scenarios.json         scenario summary (event types, expected refusals)
fixtures/v1.0.0/manifest.json                 root fixture manifest (§4)
fixtures/v1.0.0/investigations/<investigationId>/{manifest.json,events.ndjson,snapshot.json}
fixtures/v1.0.0/negative/<name>.json          negative fixtures (§12.2)
workspaces/demo/<runId>/investigations/<investigationId>/{manifest.json,events.ndjson,snapshot.json}
compatibility/v1.0.0/lock.json                compatibility gate lock (§14)
```

1. Paths are `/`-separated, relative to the repository root, and **case-sensitive**. Data paths (`fixtures/`,
   `workspaces/`) are lowercase ASCII. Payload schema file names reproduce the case-sensitive event discriminator
   (`payloads/EvidenceAdmitted.schema.json`).
2. `investigationId` directory names are canonical UUIDs (§2.8) and MUST equal the manifest's `investigationId`.
   `runId` matches `^[a-z0-9][a-z0-9-]{0,63}$`. A reader MUST reject any other directory name (`CAUSA.PATH_INVALID`),
   including `.`/`..` segments, separators, upper case or percent-encoding.
3. An investigation directory contains **exactly** the three files above. A missing or extra file is
   `CAUSA.PATH_INVALID`. Every directory entry counts, including hidden files and subdirectories.
4. `fixtures/v1.0.0/` and `contract/v1.0.0/` are **immutable** after the `causa-storage-v1.0.0` tag. Writers MUST NOT
   create, modify or delete anything under them. All demonstration writes go to an isolated
   `workspaces/demo/<runId>/` namespace (§9).

## 4. Root fixture manifest — `fixtures/v1.0.0/manifest.json`

Schema: `root-manifest.schema.json`. Fields: `formatVersion` (`"1.0.0"`), `datasetId` (`"causa-demo-v1"`), `createdAt`
(timestamp), `schemaSha256` (SHA-256 of `schemas/bundle.json` bytes), `investigations`, and `negativeFixtures`.

`investigations[]` is ordered by `investigationId` (ordinal) and has unique ids and keys. Each entry has
`investigationId`, `scenarioKey` (slug), `title`, `expectedVersion`, `expectedLifecycle`, `expectedStateSha256` (digest of
the canonical `state` object, §8), `eventsSha256` (file bytes), `manifestSha256` and `snapshotSha256` (canonical digests).

`negativeFixtures[]` lists `{path, expectedErrorCode}` with paths relative to `fixtures/v1.0.0/`. A negative fixture is
never listed as a valid investigation. The root manifest contains no digest of itself. It is anchored by `compatibility/v1.0.0/lock.json` (which pins its
bytes, §14) and by the immutable release tag.

## 5. Authoritative event envelope — one line of `events.ndjson`

Schema: `event-envelope.schema.json`.

| field | type | rule |
|---|---|---|
| `formatVersion` | string | `"1.0.0"` |
| `eventId` | uuid | derived, §5.2 |
| `investigationId` | uuid | equals the manifest and directory |
| `aggregateVersion` | integer | 1, 2, … N in file order, no gaps or duplicates |
| `eventType` | string | registered discriminator, §7 |
| `occurredAt` | timestamp | the time the producing command was decided (engine `RequestedAt`) |
| `actor` | object | §5.3 |
| `semanticVersion` | string | `"1.0.0"` |
| `correlationId` | uuid | the producing command's id (engine `CommandId`) |
| `payload` | object | strict per-`eventType` shape, §7 |

1. **Ordering** is by `aggregateVersion` only. Clock values never establish order. `InvestigationStarted` MUST be
   `aggregateVersion` 1 and the history MUST begin with it.
2. **Event identity.** `eventId = UUIDv5(N, investigationId + "/" + aggregateVersion + "/" + correlationId)`. Here
   `aggregateVersion` is written in decimal without leading zeros, the UUIDs are lowercase canonical text, the name
   is encoded as UTF-8, and
   `N = UUIDv5(NAMESPACE_URL, "https://github.com/kemiller2002/causa-data/contract/v1/event-id") = 241fefd4-c810-5a3a-9cfa-4bbd968ae1cb`.
   Readers MUST verify the derivation (`CAUSA.EVENT_ID_MISMATCH`) and uniqueness (`CAUSA.EVENT_ID_DUPLICATE`). The
   derivation exists because the domain engine has no event id. It makes ids reproducible and bound to their position
   and command. Divergent histories therefore yield different ids at the same version.
3. **Actor.** `{actorId, kind:"human"|"system"}` or `{actorId, kind:"agent", provider, model}`. All values are non-blank
   text. `actorId` is a stable, non-secret, non-personal identifier. An `agent` actor MAY appear only on
   `ProposalRecorded` (engine rule D-004). The schema enforces this.
4. **Command groups.** One domain command emits one or more consecutive events that share `correlationId`, `occurredAt`
   and `actor`. Readers MUST NOT reorder, split or merge groups. Admissibility (§11.3) is decided per group. A
   `correlationId` names exactly one group: reusing it for a later, non-adjacent group is invalid
   (`CAUSA.CORRELATION_REUSED`), so writers MUST use a fresh command id per command. The engine does not enforce
   this; the contract does. Adjacent events that share a `correlationId` but differ in `occurredAt` or `actor` are not
   one command. The engine's admissibility check refuses them (stage 15, `HISTORY.INADMISSIBLE_EVENT`).
5. **Immutability.** An appended event is never edited, re-ordered or deleted. Corrections are new events
   (for example `EvidenceSuperseded`).
6. **Clock values** are recorded, not validated. `occurredAt` need not increase with `aggregateVersion`, and a reader
   MUST NOT reject or reorder a history because of its clock values.

## 6. Enumerations

Wire values are lowerCamelCase. Engine prefixes that exist only to avoid F# name collisions (`UnknownResolved`,
`ContradictionInvestigating`, `ActionProposed`, `CorrectionCancelled`, `CauseConfirmed`, `AssumptionVerified`,
`ProposalActive`, `DiscriminatingPrediction`, `ProposedHypothesis`, …) are dropped. The bidirectional engine↔wire mapping
is exhaustive and tested in both directions. The complete field-by-field mapping, including the engine command
strings carried by `LifecycleChanged.command` and `HypothesisTransitioned.command`, is published in
`contract/v1.0.0/FIELD-MAPPING.md`.

<!-- BEGIN GENERATED: enums -->
| enumeration | wire values |
|---|---|
| `lifecycle` | `draft`, `active`, `awaitingEvidence`, `awaitingVerification`, `resolved`, `closed`, `archived` |
| `hypothesisState` | `proposed`, `plausible`, `supported`, `weakened`, `falsified`, `superseded` |
| `unknownState` | `identified`, `investigating`, `resolved`, `acceptedResidual`, `superseded` |
| `materiality` | `blocking`, `material`, `nonMaterial`, `informational` |
| `assumptionState` | `explicitUnverified`, `provisionallyAccepted`, `verified`, `rejected`, `expired` |
| `contradictionState` | `detected`, `triaged`, `investigating`, `resolved`, `acceptedResidual`, `superseded` |
| `actionState` | `proposed`, `approved`, `inProgress`, `completed`, `interpreted`, `rejected`, `cancelled` |
| `actionKind` | `test`, `inspection`, `query`, `comparison`, `experiment` |
| `causeState` | `proposed`, `confirmed`, `reopened`, `superseded` |
| `causeRole` | `rootCause`, `contributingFactor`, `trigger`, `enablingCondition` |
| `correctiveActionState` | `proposed`, `approved`, `inProgress`, `implemented`, `awaitingVerificationOfEffect`, `verifiedEffective`, `verificationFailed`, `reworkRequired`, `cancelled` |
| `correctiveActionKind` | `mitigation`, `workaround`, `correction`, `prevention` |
| `evidenceCategory` | `directObservation`, `measurement`, `log`, `document`, `experimentResult`, `testimony`, `externalReference` |
| `verificationStatus` | `unverified`, `partiallyVerified`, `verified`, `invalidated` |
| `relationshipKind` | `supports`, `contradicts`, `discriminates`, `insufficient` |
| `predictionStrength` | `necessary`, `expected`, `discriminating`, `nonDiscriminating` |
| `predictionTiming` | `prospective`, `retrospective` |
| `verificationOutcome` | `satisfied`, `failed`, `inconclusive` |
| `conclusionStrength` | `plausibleExplanation`, `leadingExplanation`, `supportedCause`, `confirmedCause`, `acceptedWithResidualUncertainty` |
| `proposalKind` | `hypothesis`, `unknown`, `action` |
| `proposalState` | `active`, `accepted`, `rejected` |
| `hypothesisCommand` | `markPlausible`, `markSupported`, `markWeakened`, `falsify`, `supersede`, `reopen` |
| `lifecycleCommand` | `activateInvestigation`, `markAwaitingEvidence`, `markAwaitingVerification`, `resolveInvestigation`, `closeInvestigation`, `reopenInvestigation`, `verificationFailed` |
| `contentKind` | `text`, `artifact` |
| `lifecycleTarget` | `active`, `awaitingEvidence`, `awaitingVerification`, `resolved`, `closed` |
| `hypothesisTarget` | `plausible`, `supported`, `weakened`, `falsified`, `superseded` |
| `unknownTarget` | `investigating`, `resolved`, `acceptedResidual` |
| `assumptionTarget` | `provisionallyAccepted`, `verified`, `rejected` |
| `contradictionTarget` | `triaged`, `resolved`, `acceptedResidual` |
| `actionTarget` | `approved`, `rejected`, `inProgress`, `completed`, `cancelled` |
| `correctiveActionTarget` | `approved`, `cancelled`, `inProgress`, `implemented`, `awaitingVerificationOfEffect`, `verificationFailed`, `verifiedEffective`, `reworkRequired` |
<!-- END GENERATED: enums -->

`*Target` enumerations are the transition targets the engine can actually emit (`Decide` transition tables and
follow-ons). States that no v1 command reaches (`archived`, unknown `superseded`, assumption `expired`, contradiction
`superseded`/`investigating`, action `interpreted`, cause `superseded`) are valid in snapshots for completeness, but no
v1 event can produce them.

## 7. Event registry (31 event types)

The registered discriminators are exactly the engine's `EventBody` cases, in this order:
`InvestigationStarted, LifecycleChanged, ObservationRecorded, HypothesisRecorded, HypothesisTransitioned,
EvidenceAdmitted, EvidenceSuperseded, EvidenceVerificationAssessed, EvidenceRelated, UnknownIdentified,
UnknownTransitioned, AssumptionRecorded, AssumptionTransitioned, ContradictionRecorded, ContradictionTransitioned,
PredictionRecorded, ActionRecorded, ActionTransitioned, FindingRecorded, NegativeKnowledgeEstablished,
CausalAssertionRecorded, CausalAssertionConfirmed, CausalAssertionReopened, CorrectiveActionRecorded,
CorrectiveActionTransitioned, CriterionRecorded, VerificationRecorded, ConclusionRecorded, ProposalRecorded,
ProposalWasAccepted, ProposalWasRejected`.

1. **Fail closed.** An unregistered `eventType` makes the whole history invalid (`CAUSA.UNKNOWN_EVENT_TYPE`). It is
   never skipped, preserved-and-ignored, or mapped to a generic event.
2. **No redundant copies.** The engine's `HypothesisChange.At` and `.Sequence` always equal the event's
   `occurredAt` and `aggregateVersion`, so they are not persisted. Readers reconstruct them from the envelope. A
   writer MUST refuse an engine event where they differ (`CAUSA.UNREPRESENTABLE`).
3. **Transition events carry their cause.** `LifecycleChanged.command` and `HypothesisTransitioned.command` name the
   domain command, so semantically distinct transitions (falsify vs supersede, resolve vs reopen) stay distinguishable
   without a generic setter. The per-entity `*Transitioned` events carry only the target `state`, exactly as the engine
   does. The prior state is derivable by replay.
4. **Causal members.** Whole-member duplicates are invalid (`uniqueItems`). The same hypothesis MAY appear twice with
   different roles, because the engine permits it.
5. **Artifact URLs.** The schema checks only for an absolute `http(s)` prefix. Full URL well-formedness is an engine rule
   and fails at the admissibility stage (domain code `HISTORY.INADMISSIBLE_EVENT`).

<!-- BEGIN GENERATED: payloads -->
#### `ActionRecorded`

Proposes an investigative action; starts 'proposed'.

| field | type | nullable | constraint |
|---|---|---|---|
| `actionId` | uuid | no | lowercase canonical, non-nil |
| `kind` | enum actionKind | no | test \| inspection \| query \| comparison \| experiment |
| `purpose` | string | no | non-blank, verbatim |
| `targetHypothesisIds` | array<uuid> | no | ordered, distinct |

#### `ActionTransitioned`

Investigative action state change.

| field | type | nullable | constraint |
|---|---|---|---|
| `actionId` | uuid | no | lowercase canonical, non-nil |
| `state` | enum actionTarget | no | approved \| rejected \| inProgress \| completed \| cancelled |

#### `AssumptionRecorded`

Records an explicit assumption; starts 'explicitUnverified'.

| field | type | nullable | constraint |
|---|---|---|---|
| `assumptionId` | uuid | no | lowercase canonical, non-nil |
| `proposition` | string | no | non-blank, verbatim |
| `materiality` | enum materiality | no | blocking \| material \| nonMaterial \| informational |

#### `AssumptionTransitioned`

Assumption state change.

| field | type | nullable | constraint |
|---|---|---|---|
| `assumptionId` | uuid | no | lowercase canonical, non-nil |
| `state` | enum assumptionTarget | no | provisionallyAccepted \| verified \| rejected |

#### `CausalAssertionConfirmed`

Confirms a cause after the cause gate passed; 'policy' identifies the policy version applied (engine Policy.id).

| field | type | nullable | constraint |
|---|---|---|---|
| `causeId` | uuid | no | lowercase canonical, non-nil |
| `policy` | string | no | non-blank, verbatim |

#### `CausalAssertionRecorded`

Proposes a causal explanation composed of hypotheses with roles; starts 'proposed'.

| field | type | nullable | constraint |
|---|---|---|---|
| `causeId` | uuid | no | lowercase canonical, non-nil |
| `outcome` | string | no | non-blank, verbatim |
| `members` | array<object causal-member> | no | min 1, unique |
| `mechanism` | string | yes | non-blank, verbatim |

#### `CausalAssertionReopened`

Reopens a confirmed cause.

| field | type | nullable | constraint |
|---|---|---|---|
| `causeId` | uuid | no | lowercase canonical, non-nil |
| `reason` | string | no | non-blank, verbatim |

#### `ConclusionRecorded`

A conclusion with explicit residual uncertainty (R6.4).

| field | type | nullable | constraint |
|---|---|---|---|
| `conclusionId` | uuid | no | lowercase canonical, non-nil |
| `statement` | string | no | non-blank, verbatim |
| `strength` | enum conclusionStrength | no | plausibleExplanation \| leadingExplanation \| supportedCause \| confirmedCause \| acceptedWithResidualUncertainty |
| `causeIds` | array<uuid> | no | ordered, distinct |
| `residualUncertainty` | string | yes | non-blank, verbatim |

#### `ContradictionRecorded`

Records a contradiction among evidence; starts 'detected'.

| field | type | nullable | constraint |
|---|---|---|---|
| `contradictionId` | uuid | no | lowercase canonical, non-nil |
| `statement` | string | no | non-blank, verbatim |
| `evidenceIds` | array<uuid> | no | ordered, distinct |
| `materiality` | enum materiality | no | blocking \| material \| nonMaterial \| informational |

#### `ContradictionTransitioned`

Contradiction state change with its basis.

| field | type | nullable | constraint |
|---|---|---|---|
| `contradictionId` | uuid | no | lowercase canonical, non-nil |
| `state` | enum contradictionTarget | no | triaged \| resolved \| acceptedResidual |
| `basis` | string | no | non-blank, verbatim |

#### `CorrectiveActionRecorded`

Proposes a corrective action; mitigation is distinct from correction (R7.2).

| field | type | nullable | constraint |
|---|---|---|---|
| `correctiveActionId` | uuid | no | lowercase canonical, non-nil |
| `kind` | enum correctiveActionKind | no | mitigation \| workaround \| correction \| prevention |
| `description` | string | no | non-blank, verbatim |
| `targetCauseIds` | array<uuid> | no | ordered, distinct |

#### `CorrectiveActionTransitioned`

Corrective action state change.

| field | type | nullable | constraint |
|---|---|---|---|
| `correctiveActionId` | uuid | no | lowercase canonical, non-nil |
| `state` | enum correctiveActionTarget | no | approved \| cancelled \| inProgress \| implemented \| awaitingVerificationOfEffect \| verificationFailed \| verifiedEffective \| reworkRequired |

#### `CriterionRecorded`

A verification success criterion for a corrective action (R7.3).

| field | type | nullable | constraint |
|---|---|---|---|
| `criterionId` | uuid | no | lowercase canonical, non-nil |
| `correctiveActionId` | uuid | no | lowercase canonical, non-nil |
| `expectedOutcome` | string | no | non-blank, verbatim |
| `method` | string | yes | non-blank, verbatim |

#### `EvidenceAdmitted`

Admits evidence. Text content is verbatim; artifacts are referenced by absolute http(s) URL, never embedded (R3.5). Verification starts 'unverified'.

| field | type | nullable | constraint |
|---|---|---|---|
| `evidenceId` | uuid | no | lowercase canonical, non-nil |
| `contentKind` | enum contentKind | no | text \| artifact |
| `content` | string | no | non-blank, verbatim |
| `category` | enum evidenceCategory | no | directObservation \| measurement \| log \| document \| experimentResult \| testimony \| externalReference |
| `source` | string | no | non-blank, verbatim |
| `capturedAt` | timestamp | yes | UTC, millisecond, Z |

Invariants: `contentKind` = `artifact` ⇒ `content` must be httpUrl.

#### `EvidenceRelated`

Relates evidence to a hypothesis.

| field | type | nullable | constraint |
|---|---|---|---|
| `relationshipId` | uuid | no | lowercase canonical, non-nil |
| `evidenceId` | uuid | no | lowercase canonical, non-nil |
| `hypothesisId` | uuid | no | lowercase canonical, non-nil |
| `kind` | enum relationshipKind | no | supports \| contradicts \| discriminates \| insufficient |
| `rationale` | string | yes | non-blank, verbatim |

#### `EvidenceSuperseded`

Marks evidence as superseded by a replacement; the original remains in history.

| field | type | nullable | constraint |
|---|---|---|---|
| `evidenceId` | uuid | no | lowercase canonical, non-nil |
| `replacementEvidenceId` | uuid | no | lowercase canonical, non-nil |
| `reason` | string | no | non-blank, verbatim |

#### `EvidenceVerificationAssessed`

Records an assessment of the evidence's verification status.

| field | type | nullable | constraint |
|---|---|---|---|
| `evidenceId` | uuid | no | lowercase canonical, non-nil |
| `verification` | enum verificationStatus | no | unverified \| partiallyVerified \| verified \| invalidated |

#### `FindingRecorded`

The actual result of an action exactly as entered (R4.4).

| field | type | nullable | constraint |
|---|---|---|---|
| `findingId` | uuid | no | lowercase canonical, non-nil |
| `actionId` | uuid | no | lowercase canonical, non-nil |
| `originalResult` | string | no | non-blank, verbatim |
| `interpretation` | string | yes | non-blank, verbatim |

#### `HypothesisRecorded`

A new hypothesis in state 'proposed'.

| field | type | nullable | constraint |
|---|---|---|---|
| `hypothesisId` | uuid | no | lowercase canonical, non-nil |
| `statement` | string | no | non-blank, verbatim |
| `mechanism` | string | yes | non-blank, verbatim |

#### `HypothesisTransitioned`

Hypothesis state change with its reason and cited evidence (R2.3). The change time and version are the envelope's occurredAt and aggregateVersion.

| field | type | nullable | constraint |
|---|---|---|---|
| `hypothesisId` | uuid | no | lowercase canonical, non-nil |
| `from` | enum hypothesisState | no | proposed \| plausible \| supported \| weakened \| falsified \| superseded |
| `to` | enum hypothesisTarget | no | plausible \| supported \| weakened \| falsified \| superseded |
| `command` | enum hypothesisCommand | no | markPlausible \| markSupported \| markWeakened \| falsify \| supersede \| reopen |
| `reason` | string | no | non-blank, verbatim |
| `evidenceIds` | array<uuid> | no | ordered, distinct |

Invariants: `command` = `markPlausible` ⇒ `to` = `"plausible"`; `command` = `markSupported` ⇒ `to` = `"supported"`; `command` = `markWeakened` ⇒ `to` = `"weakened"`; `command` = `falsify` ⇒ `to` = `"falsified"`; `command` = `supersede` ⇒ `to` = `"superseded"`; `command` = `reopen` ⇒ `to` = `"plausible"`; `command` = `markSupported` ⇒ `evidenceIds` must be nonEmptyUuidList; `command` = `markWeakened` ⇒ `evidenceIds` must be nonEmptyUuidList; `command` = `falsify` ⇒ `evidenceIds` must be nonEmptyUuidList; `command` = `reopen` ⇒ `evidenceIds` must be nonEmptyUuidList.

#### `InvestigationStarted`

Starts the investigation and records its initial observation verbatim. Always aggregateVersion 1.

| field | type | nullable | constraint |
|---|---|---|---|
| `title` | string | no | non-blank, verbatim |
| `observationId` | uuid | no | lowercase canonical, non-nil |
| `original` | string | no | non-blank, verbatim |

#### `LifecycleChanged`

Investigation lifecycle transition, discriminated by the command that caused it.

| field | type | nullable | constraint |
|---|---|---|---|
| `from` | enum lifecycle | no | draft \| active \| awaitingEvidence \| awaitingVerification \| resolved \| closed \| archived |
| `to` | enum lifecycleTarget | no | active \| awaitingEvidence \| awaitingVerification \| resolved \| closed |
| `command` | enum lifecycleCommand | no | activateInvestigation \| markAwaitingEvidence \| markAwaitingVerification \| resolveInvestigation \| closeInvestigation \| reopenInvestigation \| verificationFailed |
| `reason` | string | yes | non-blank, verbatim |

Invariants: `command` = `activateInvestigation` ⇒ `to` = `"active"`, `reason` is null; `command` = `markAwaitingEvidence` ⇒ `to` = `"awaitingEvidence"`, `reason` must be text; `command` = `markAwaitingVerification` ⇒ `to` = `"awaitingVerification"`, `reason` is null; `command` = `resolveInvestigation` ⇒ `to` = `"resolved"`, `reason` is null; `command` = `closeInvestigation` ⇒ `to` = `"closed"`, `reason` is null; `command` = `reopenInvestigation` ⇒ `to` = `"active"`, `reason` must be text; `command` = `verificationFailed` ⇒ `to` = `"active"`, `reason` must be text; `command` = `verificationFailed` ⇒ `from` = `"awaitingVerification"`.

#### `NegativeKnowledgeEstablished`

Records something established NOT to be the case, citing evidence (R5.3).

| field | type | nullable | constraint |
|---|---|---|---|
| `negativeKnowledgeId` | uuid | no | lowercase canonical, non-nil |
| `statement` | string | no | non-blank, verbatim |
| `scope` | string | no | non-blank, verbatim |
| `basisEvidenceIds` | array<uuid> | no | ordered, distinct; at least 1 item |

#### `ObservationRecorded`

An observation, preserved verbatim (R1.2).

| field | type | nullable | constraint |
|---|---|---|---|
| `observationId` | uuid | no | lowercase canonical, non-nil |
| `original` | string | no | non-blank, verbatim |
| `observedAt` | timestamp | yes | UTC, millisecond, Z |

#### `PredictionRecorded`

A prediction derived from a hypothesis. Timing is as decided by the engine (A11), not as requested.

| field | type | nullable | constraint |
|---|---|---|---|
| `predictionId` | uuid | no | lowercase canonical, non-nil |
| `hypothesisId` | uuid | no | lowercase canonical, non-nil |
| `statement` | string | no | non-blank, verbatim |
| `strength` | enum predictionStrength | no | necessary \| expected \| discriminating \| nonDiscriminating |
| `timing` | enum predictionTiming | no | prospective \| retrospective |

#### `ProposalRecorded`

A proposal (typically from an AI agent); never evidence or authoritative state (D-004, R15.2).

| field | type | nullable | constraint |
|---|---|---|---|
| `proposalId` | uuid | no | lowercase canonical, non-nil |
| `kind` | enum proposalKind | no | hypothesis \| unknown \| action |
| `text` | string | no | non-blank, verbatim |
| `rationale` | string | yes | non-blank, verbatim |

#### `ProposalWasAccepted`

A human accepted a proposal; targetId is the id of the entity the acceptance created (preceding event of the same correlationId).

| field | type | nullable | constraint |
|---|---|---|---|
| `proposalId` | uuid | no | lowercase canonical, non-nil |
| `targetId` | uuid | no | lowercase canonical, non-nil |

#### `ProposalWasRejected`

A proposal was rejected with a reason.

| field | type | nullable | constraint |
|---|---|---|---|
| `proposalId` | uuid | no | lowercase canonical, non-nil |
| `reason` | string | no | non-blank, verbatim |

#### `UnknownIdentified`

Identifies an open question; starts 'identified'.

| field | type | nullable | constraint |
|---|---|---|---|
| `unknownId` | uuid | no | lowercase canonical, non-nil |
| `question` | string | no | non-blank, verbatim |
| `materiality` | enum materiality | no | blocking \| material \| nonMaterial \| informational |

#### `UnknownTransitioned`

Unknown state change with its basis.

| field | type | nullable | constraint |
|---|---|---|---|
| `unknownId` | uuid | no | lowercase canonical, non-nil |
| `state` | enum unknownTarget | no | investigating \| resolved \| acceptedResidual |
| `basis` | string | no | non-blank, verbatim |

#### `VerificationRecorded`

A verification of a criterion; a satisfied outcome must cite evidence (R7.4).

| field | type | nullable | constraint |
|---|---|---|---|
| `verificationId` | uuid | no | lowercase canonical, non-nil |
| `criterionId` | uuid | no | lowercase canonical, non-nil |
| `outcome` | enum verificationOutcome | no | satisfied \| failed \| inconclusive |
| `evidenceIds` | array<uuid> | no | ordered, distinct |

Invariants: `outcome` = `satisfied` ⇒ `evidenceIds` must be nonEmptyUuidList.
<!-- END GENERATED: payloads -->

## 8. Snapshot and state projection

### 8.1 Snapshot envelope — `snapshot.json`

Schema: `snapshot.schema.json`. Fields: `formatVersion`, `investigationId`, `aggregateVersion` (the last applied
event), `projectionVersion` (`1`), `semanticVersion`, `generatedAt` (**equal to the `occurredAt` of event
`aggregateVersion`**, which keeps snapshots byte-reproducible), and `state`.

A snapshot is valid only if it is byte-identical to the projection of the replayed history (`CAUSA.SNAPSHOT_MISMATCH`).
A plausible-looking snapshot over an inadmissible history is rejected by §11.3 first. Snapshots MUST NOT be read as a
source of truth when the history is available, and v1 has no events-only or snapshot-only profile.

### 8.2 State object

`state` (schema `state.schema.json`; also the shape of `golden/<scenarioKey>.state.json`) contains
`investigationId`, `title`, `lifecycle`, `version` (= `aggregateVersion`), `createdAt` (= `occurredAt` of event 1),
and 17 entity arrays: `observations`, `hypotheses`, `evidence`, `relationships`, `unknowns`, `assumptions`,
`contradictions`, `predictions`, `actions`, `findings`, `negativeKnowledge`, `causes`, `correctiveActions`, `criteria`,
`verifications`, `conclusions`, `proposals`. Derived UI postures such as "leading" or "next best action" are never persisted.

### 8.3 Projection rules (normative fold)

Start with no state, then apply events in `aggregateVersion` order. Every event sets `version` to its `aggregateVersion`,
including an update that changes nothing else. A
"new entity" gets `provenance = {actor: <event actor>, recordedAt: <event occurredAt>}`, and later updates never
change an entity's provenance. Updates to an id that does not exist leave state unchanged. They cannot occur in an
admissible history. Finally, every entity array is sorted by `id` using **ordinal comparison of the UUID text**.

| event | effect |
|---|---|
| `InvestigationStarted` | creates state: `investigationId`, `title`, `lifecycle: "draft"`, `createdAt: occurredAt`, `observations: [{id: observationId, original, observedAt: null}]`, every other array `[]` |
| `LifecycleChanged` | `lifecycle ← to` |
| `ObservationRecorded` | new observation `{id: observationId, original, observedAt}` |
| `HypothesisRecorded` | new hypothesis `{id: hypothesisId, statement, mechanism, state: "proposed", history: []}` |
| `HypothesisTransitioned` | hypothesis `state ← to`; append to `history` `{from, to, command, reason, evidenceIds, at: occurredAt, aggregateVersion}` |
| `EvidenceAdmitted` | new evidence `{id: evidenceId, contentKind, content, category, source, capturedAt, verification: "unverified", supersededBy: null, admittedAtVersion: aggregateVersion}` |
| `EvidenceSuperseded` | evidence `evidenceId`: `supersededBy ← replacementEvidenceId` |
| `EvidenceVerificationAssessed` | evidence `verification ← verification` |
| `EvidenceRelated` | new relationship `{id: relationshipId, evidenceId, hypothesisId, kind, rationale}` |
| `UnknownIdentified` | new unknown `{id: unknownId, question, materiality, state: "identified", basis: null}` |
| `UnknownTransitioned` | unknown `state ← state`, `basis ← basis` |
| `AssumptionRecorded` | new assumption `{id: assumptionId, proposition, materiality, state: "explicitUnverified"}` |
| `AssumptionTransitioned` | assumption `state ← state` |
| `ContradictionRecorded` | new contradiction `{id: contradictionId, statement, evidenceIds, materiality, state: "detected", basis: null}` |
| `ContradictionTransitioned` | contradiction `state ← state`, `basis ← basis` |
| `PredictionRecorded` | new prediction `{id: predictionId, hypothesisId, statement, strength, timing}` |
| `ActionRecorded` | new action `{id: actionId, kind, purpose, targetHypothesisIds, state: "proposed"}` |
| `ActionTransitioned` | action `state ← state` |
| `FindingRecorded` | new finding `{id: findingId, actionId, originalResult, interpretation}` |
| `NegativeKnowledgeEstablished` | new negativeKnowledge `{id: negativeKnowledgeId, statement, scope, basisEvidenceIds}` |
| `CausalAssertionRecorded` | new cause `{id: causeId, outcome, members, mechanism, state: "proposed"}` |
| `CausalAssertionConfirmed` | cause `state ← "confirmed"` |
| `CausalAssertionReopened` | cause `state ← "reopened"` |
| `CorrectiveActionRecorded` | new correctiveAction `{id: correctiveActionId, kind, description, targetCauseIds, state: "proposed", awaitingSinceVersion: 0}` |
| `CorrectiveActionTransitioned` | correctiveAction `state ← state`; if `state = "awaitingVerificationOfEffect"` then `awaitingSinceVersion ← aggregateVersion` |
| `CriterionRecorded` | new criterion `{id: criterionId, correctiveActionId, expectedOutcome, method}` |
| `VerificationRecorded` | new verification `{id: verificationId, criterionId, outcome, evidenceIds, recordedAtVersion: aggregateVersion}` |
| `ConclusionRecorded` | new conclusion `{id: conclusionId, statement, strength, causeIds, residualUncertainty, recordedAtVersion: aggregateVersion}` |
| `ProposalRecorded` | new proposal `{id: proposalId, kind, text, rationale, state: "active", decision: null}` |
| `ProposalWasAccepted` | proposal `state ← "accepted"` (the entity it created is the preceding event of the same command) |
| `ProposalWasRejected` | proposal `state ← "rejected"`, `decision ← reason` |

Nested entity shapes:

<!-- BEGIN GENERATED: entities -->
#### `action`

Investigative action projection.

| field | type | nullable | constraint |
|---|---|---|---|
| `id` | uuid | no | lowercase canonical, non-nil |
| `kind` | enum actionKind | no | test \| inspection \| query \| comparison \| experiment |
| `purpose` | string | no | non-blank, verbatim |
| `targetHypothesisIds` | array<uuid> | no | ordered, distinct |
| `state` | enum actionState | no | proposed \| approved \| inProgress \| completed \| interpreted \| rejected \| cancelled |
| `provenance` | provenance | no | see actor.schema.json |

#### `assumption`

Assumption projection.

| field | type | nullable | constraint |
|---|---|---|---|
| `id` | uuid | no | lowercase canonical, non-nil |
| `proposition` | string | no | non-blank, verbatim |
| `materiality` | enum materiality | no | blocking \| material \| nonMaterial \| informational |
| `state` | enum assumptionState | no | explicitUnverified \| provisionallyAccepted \| verified \| rejected \| expired |
| `provenance` | provenance | no | see actor.schema.json |

#### `causal-member`

A member of a causal assertion.

| field | type | nullable | constraint |
|---|---|---|---|
| `hypothesisId` | uuid | no | lowercase canonical, non-nil |
| `role` | enum causeRole | no | rootCause \| contributingFactor \| trigger \| enablingCondition |

#### `cause`

Causal assertion projection.

| field | type | nullable | constraint |
|---|---|---|---|
| `id` | uuid | no | lowercase canonical, non-nil |
| `outcome` | string | no | non-blank, verbatim |
| `members` | array<object causal-member> | no | min 1, unique |
| `mechanism` | string | yes | non-blank, verbatim |
| `state` | enum causeState | no | proposed \| confirmed \| reopened \| superseded |
| `provenance` | provenance | no | see actor.schema.json |

#### `conclusion`

Conclusion projection; the current conclusion is the one with the highest recordedAtVersion.

| field | type | nullable | constraint |
|---|---|---|---|
| `id` | uuid | no | lowercase canonical, non-nil |
| `statement` | string | no | non-blank, verbatim |
| `strength` | enum conclusionStrength | no | plausibleExplanation \| leadingExplanation \| supportedCause \| confirmedCause \| acceptedWithResidualUncertainty |
| `causeIds` | array<uuid> | no | ordered, distinct |
| `residualUncertainty` | string | yes | non-blank, verbatim |
| `recordedAtVersion` | integer | no | 1..2^53-1 |
| `provenance` | provenance | no | see actor.schema.json |

#### `contradiction`

Contradiction projection; basis is the latest transition basis.

| field | type | nullable | constraint |
|---|---|---|---|
| `id` | uuid | no | lowercase canonical, non-nil |
| `statement` | string | no | non-blank, verbatim |
| `evidenceIds` | array<uuid> | no | ordered, distinct |
| `materiality` | enum materiality | no | blocking \| material \| nonMaterial \| informational |
| `state` | enum contradictionState | no | detected \| triaged \| investigating \| resolved \| acceptedResidual \| superseded |
| `basis` | string | yes | non-blank, verbatim |
| `provenance` | provenance | no | see actor.schema.json |

#### `corrective-action`

Corrective action projection. awaitingSinceVersion is the aggregateVersion at which the current verification round began (0 = never).

| field | type | nullable | constraint |
|---|---|---|---|
| `id` | uuid | no | lowercase canonical, non-nil |
| `kind` | enum correctiveActionKind | no | mitigation \| workaround \| correction \| prevention |
| `description` | string | no | non-blank, verbatim |
| `targetCauseIds` | array<uuid> | no | ordered, distinct |
| `state` | enum correctiveActionState | no | proposed \| approved \| inProgress \| implemented \| awaitingVerificationOfEffect \| verifiedEffective \| verificationFailed \| reworkRequired \| cancelled |
| `awaitingSinceVersion` | integer | no | 0..2^53-1 |
| `provenance` | provenance | no | see actor.schema.json |

#### `criterion`

Verification criterion projection.

| field | type | nullable | constraint |
|---|---|---|---|
| `id` | uuid | no | lowercase canonical, non-nil |
| `correctiveActionId` | uuid | no | lowercase canonical, non-nil |
| `expectedOutcome` | string | no | non-blank, verbatim |
| `method` | string | yes | non-blank, verbatim |
| `provenance` | provenance | no | see actor.schema.json |

#### `evidence`

Evidence projection. admittedAtVersion is the aggregateVersion that admitted it.

| field | type | nullable | constraint |
|---|---|---|---|
| `id` | uuid | no | lowercase canonical, non-nil |
| `contentKind` | enum contentKind | no | text \| artifact |
| `content` | string | no | non-blank, verbatim |
| `category` | enum evidenceCategory | no | directObservation \| measurement \| log \| document \| experimentResult \| testimony \| externalReference |
| `source` | string | no | non-blank, verbatim |
| `capturedAt` | timestamp | yes | UTC, millisecond, Z |
| `verification` | enum verificationStatus | no | unverified \| partiallyVerified \| verified \| invalidated |
| `supersededBy` | uuid | yes | lowercase canonical, non-nil |
| `admittedAtVersion` | integer | no | 1..2^53-1 |
| `provenance` | provenance | no | see actor.schema.json |

#### `finding`

Finding projection.

| field | type | nullable | constraint |
|---|---|---|---|
| `id` | uuid | no | lowercase canonical, non-nil |
| `actionId` | uuid | no | lowercase canonical, non-nil |
| `originalResult` | string | no | non-blank, verbatim |
| `interpretation` | string | yes | non-blank, verbatim |
| `provenance` | provenance | no | see actor.schema.json |

#### `hypothesis-change`

One entry of a hypothesis's change history, in history order.

| field | type | nullable | constraint |
|---|---|---|---|
| `from` | enum hypothesisState | no | proposed \| plausible \| supported \| weakened \| falsified \| superseded |
| `to` | enum hypothesisTarget | no | plausible \| supported \| weakened \| falsified \| superseded |
| `command` | enum hypothesisCommand | no | markPlausible \| markSupported \| markWeakened \| falsify \| supersede \| reopen |
| `reason` | string | no | non-blank, verbatim |
| `evidenceIds` | array<uuid> | no | ordered, distinct |
| `at` | timestamp | no | UTC, millisecond, Z |
| `aggregateVersion` | integer | no | 1..2^53-1 |

#### `hypothesis`

Hypothesis projection with its full change history.

| field | type | nullable | constraint |
|---|---|---|---|
| `id` | uuid | no | lowercase canonical, non-nil |
| `statement` | string | no | non-blank, verbatim |
| `mechanism` | string | yes | non-blank, verbatim |
| `state` | enum hypothesisState | no | proposed \| plausible \| supported \| weakened \| falsified \| superseded |
| `history` | array<object hypothesis-change> | no |  |
| `provenance` | provenance | no | see actor.schema.json |

#### `negative-knowledge`

Negative knowledge projection.

| field | type | nullable | constraint |
|---|---|---|---|
| `id` | uuid | no | lowercase canonical, non-nil |
| `statement` | string | no | non-blank, verbatim |
| `scope` | string | no | non-blank, verbatim |
| `basisEvidenceIds` | array<uuid> | no | ordered, distinct; at least 1 item |
| `provenance` | provenance | no | see actor.schema.json |

#### `observation`

Observation projection.

| field | type | nullable | constraint |
|---|---|---|---|
| `id` | uuid | no | lowercase canonical, non-nil |
| `original` | string | no | non-blank, verbatim |
| `observedAt` | timestamp | yes | UTC, millisecond, Z |
| `provenance` | provenance | no | see actor.schema.json |

#### `prediction`

Prediction projection.

| field | type | nullable | constraint |
|---|---|---|---|
| `id` | uuid | no | lowercase canonical, non-nil |
| `hypothesisId` | uuid | no | lowercase canonical, non-nil |
| `statement` | string | no | non-blank, verbatim |
| `strength` | enum predictionStrength | no | necessary \| expected \| discriminating \| nonDiscriminating |
| `timing` | enum predictionTiming | no | prospective \| retrospective |
| `provenance` | provenance | no | see actor.schema.json |

#### `proposal`

Proposal projection; decision is the rejection reason when rejected.

| field | type | nullable | constraint |
|---|---|---|---|
| `id` | uuid | no | lowercase canonical, non-nil |
| `kind` | enum proposalKind | no | hypothesis \| unknown \| action |
| `text` | string | no | non-blank, verbatim |
| `rationale` | string | yes | non-blank, verbatim |
| `state` | enum proposalState | no | active \| accepted \| rejected |
| `decision` | string | yes | non-blank, verbatim |
| `provenance` | provenance | no | see actor.schema.json |

#### `relationship`

Evidence-hypothesis relationship projection.

| field | type | nullable | constraint |
|---|---|---|---|
| `id` | uuid | no | lowercase canonical, non-nil |
| `evidenceId` | uuid | no | lowercase canonical, non-nil |
| `hypothesisId` | uuid | no | lowercase canonical, non-nil |
| `kind` | enum relationshipKind | no | supports \| contradicts \| discriminates \| insufficient |
| `rationale` | string | yes | non-blank, verbatim |
| `provenance` | provenance | no | see actor.schema.json |

#### `unknown`

Unknown projection; basis is the latest transition basis.

| field | type | nullable | constraint |
|---|---|---|---|
| `id` | uuid | no | lowercase canonical, non-nil |
| `question` | string | no | non-blank, verbatim |
| `materiality` | enum materiality | no | blocking \| material \| nonMaterial \| informational |
| `state` | enum unknownState | no | identified \| investigating \| resolved \| acceptedResidual \| superseded |
| `basis` | string | yes | non-blank, verbatim |
| `provenance` | provenance | no | see actor.schema.json |

#### `verification`

Verification projection.

| field | type | nullable | constraint |
|---|---|---|---|
| `id` | uuid | no | lowercase canonical, non-nil |
| `criterionId` | uuid | no | lowercase canonical, non-nil |
| `outcome` | enum verificationOutcome | no | satisfied \| failed \| inconclusive |
| `evidenceIds` | array<uuid> | no | ordered, distinct |
| `recordedAtVersion` | integer | no | 1..2^53-1 |
| `provenance` | provenance | no | see actor.schema.json |
<!-- END GENERATED: entities -->

### 8.4 Digests

`expectedStateSha256` = SHA-256 of the canonical `state` object. `snapshotSha256` = SHA-256 of the canonical snapshot.
Equal digests across consumers are the cross-run compatibility criterion (STORAGE-002).

## 9. Investigation manifest, appends and provider behaviour

### 9.1 Manifest — `manifest.json`

Schema: `investigation-manifest.schema.json`. `investigationId` equals the directory name. `aggregateVersion` and `eventCount`
both equal the **number of lines** (stage 8). That the last line carries that version is checked separately, at stage 12. `eventsSha256` covers the `events.ndjson` bytes. `snapshotVersion` is `1`. `snapshotSha256` is the
canonical snapshot digest. `domainPacks` is `[]` (none exist in v1). `createdAt` and `updatedAt` are the `occurredAt` of the
first and last events. The manifest MUST NOT contain a Git SHA, branch, repository path, token or any other provider metadata.

### 9.2 Optimistic append

A semantic command carries the aggregate version the writer observed. If it differs from the current
`aggregateVersion`, the append is refused (`CAUSA.VERSION_STALE`) and nothing is written. The writer MUST keep the
pending command so that it can reload and re-decide. Otherwise the writer appends the events that the domain engine
emitted, recomputes snapshot and manifest, and verifies the result exactly as a reader would (§11) before reporting
success. Evaluation order for an append: (1) the version check (`CAUSA.VERSION_STALE`, before the appended lines are
read); (2) the appended lines through stages 2–7, numbered from current + 1; (3) the combined history through every
reader stage, with manifest and snapshot rebuilt by the evaluator. "Numbered from current + 1" is how lines are
labelled in messages. A wrong `aggregateVersion` on an appended line is caught at stage 12 in step (3).

### 9.3 Successor files

A newer file set for an investigation is acceptable only if its `events.ndjson` starts with the previously accepted
`events.ndjson` bytes (`CAUSA.HISTORY_REWRITTEN` otherwise). This holds even when every hash and the snapshot are
self-consistent. The candidate is first verified on its own (§11.1); the prefix check follows. A byte-identical
successor (no new events) is acceptable and changes nothing.

### 9.4 Remote providers (GitHub)

1. **Atomicity.** The events append, manifest and snapshot of one semantic command MUST become visible together. On
   GitHub, that means one Git commit whose tree contains all three files, published by a non-forced ref update
   (`PATCH /git/refs`, `force: false`). Sequential per-file Contents-API writes do not conform.
2. **Concurrency token.** The provider version token is the branch head commit the writer read. A ref update that is not
   a fast-forward is a conflict. The writer reloads, re-verifies the remote history, and then (a) rebases onto the new
   head when the investigation's files did not change there, or (b) reports `conflict` and keeps its pending commands.
   It MUST NOT overwrite or force-push.
3. **Success** is reported only after the provider confirms the ref update **and** a re-read at the new head returns
   the written bytes. Pending, sending, conflicted and unknown remain distinct states. A network failure is never
   reported as success.
4. **Paths.** Writers only touch `workspaces/demo/<runId>/investigations/<investigationId>/…`. Before publishing, the
   adapter MUST check that the new tree differs from its base only under that prefix.
5. Commit messages are audit metadata, not semantic data. Tokens are sent only in the `Authorization` header and never
   written to any file or message.

## 10. Integrity and schema identity

1. Integrity hashes detect accidental or malicious change. They are not authentication or signatures.
2. Each per-investigation manifest pins its events and snapshot. The root manifest pins every investigation's events,
   manifest, snapshot and state digests, plus the schema bundle.
3. **Schema bundle.** `schemas/bundle.json` (schema `schema-bundle.schema.json`) lists every `*.schema.json` under
   `schemas/` as `{path, sha256}`. Paths are relative and `/`-separated, sorted ordinally. Each digest is over exact file
   bytes. The **schema identity** is the SHA-256 of `bundle.json`'s exact bytes. Compatibility tooling compares this
   identity (§14).
4. **Identities.** Three digests identify a release:
   - **schema identity**: SHA-256 of the exact bytes of `contract/v1.0.0/schemas/bundle.json`;
   - **dataset identity**: SHA-256 of the exact bytes of `fixtures/v1.0.0/manifest.json`, which pins the schema
     identity and every investigation's events, manifest, snapshot and state digests;
   - **release identity**: SHA-256 of the exact bytes of `compatibility/v1.0.0/lock.json`, which pins every frozen
     file (negative fixtures included), the engine assemblies and the SDK.

## 11. Error handling

### 11.1 Check order

A reader validates an investigation directory in this order and reports the **first** failing stage as the primary code.
Conforming readers MUST agree on the primary code for every negative fixture.

| # | code | stage |
|---|---|---|
| 1 | `CAUSA.PATH_INVALID` | directory name / path; exactly the three files |
| 2 | `CAUSA.ENCODING` | UTF-8, BOM, LF framing (all three files) |
| 3 | `CAUSA.JSON_MALFORMED` | JSON syntax, duplicate keys, non-integer numbers, surrogates (all files) |
| 4 | `CAUSA.NON_CANONICAL` | RFC 8785 byte form (manifest, snapshot, each event line) |
| 5 | `CAUSA.UNSUPPORTED_VERSION` | a **string** `formatVersion`/`schemaVersion`/`semanticVersion` ≠ `"1.0.0"` (missing or non-string: stage 7; `snapshotVersion`/`projectionVersion`: stage 7) |
| 6 | `CAUSA.UNKNOWN_EVENT_TYPE` | a **string** `eventType` that is not registered (non-string: stage 7) |
| 7 | `CAUSA.SCHEMA_VIOLATION` | manifest, each event, snapshot against the schemas; calendar-invalid timestamps |
| 8 | `CAUSA.MANIFEST_MISMATCH` | manifest id/directory, counts, created/updated times |
| 9 | `CAUSA.EVENTS_HASH_MISMATCH` | `eventsSha256` |
| 10 | `CAUSA.INVESTIGATION_MISMATCH` | an event names another investigation |
| 11 | `CAUSA.EVENT_ID_DUPLICATE` | duplicate `eventId` |
| 12 | `CAUSA.SEQUENCE_INVALID` | versions not exactly 1..N in file order |
| 13 | `CAUSA.EVENT_ID_MISMATCH` | derivation §5.2 |
| 14 | `CAUSA.CORRELATION_REUSED` | a `correlationId` names two non-adjacent groups (§5.4) |
| 15 | `CAUSA.HISTORY_INADMISSIBLE` | domain admissibility (§11.3); carries a domain code |
| 16 | `CAUSA.SNAPSHOT_HASH_MISMATCH` | `snapshotSha256` |
| 17 | `CAUSA.SNAPSHOT_MISMATCH` | snapshot ≠ projection of replayed history |

Dataset-level codes: `CAUSA.FIXTURE_DRIFT` (any root-manifest entry field — digest, version, lifecycle, title — differs
from the investigation; the root manifest, bundle, golden and negative-fixture files are themselves checked for framing,
canonical form, versions and schema with the stage codes above; a `golden/scenarios.json` that contradicts the
investigations is `CAUSA.GOLDEN_MISMATCH`) and
`CAUSA.GOLDEN_MISMATCH` (the projection differs from the golden file). Write-side codes: `CAUSA.VERSION_STALE` (§9.2),
`CAUSA.HISTORY_REWRITTEN` (§9.3) and `CAUSA.UNREPRESENTABLE` (a domain value has no v1 wire form, §2.9, §7.2).

### 11.2 Behaviour

Readers fail closed: an invalid investigation is not partially loaded, and nothing is "repaired" or rewritten.
Errors carry the code, a human-readable detail and, for stage 15, the domain code.

### 11.3 Semantic admissibility

A history is admissible iff the pinned engine's `History.validate` returns no errors. That function re-decides every
command group against the prior state with the recorded actor and time, and requires exactly the recorded events. It
enforces transition tables, open-investigation rules, id uniqueness, agent authority, cause and resolution gates,
verification freshness, prediction timing and repetition refusal. The domain code is the code of its first error
(`HISTORY.NO_START`, `HISTORY.SEQUENCE`, `HISTORY.INADMISSIBLE_EVENT`, `HISTORY.UNGATED_CONFIRMATION`,
`HISTORY.UNGATED_RESOLUTION`). "First" means the first element of the list that `History.validate` returns: it folds
over command groups in history order, and within a group reports sequence problems before the verdict.
A consumer that cannot evaluate admissibility MUST NOT report a history as valid. It may report "structurally valid,
admissibility not evaluated". For inputs whose expected code comes after stage 15, such a consumer's primary code is
conditional on admissibility, and it MUST say so.

## 12. Frozen dataset

### 12.1 Scenarios

Five synthetic investigations were generated by issuing real commands through `Decide.execute` of the pinned engine.
The generator used a fixed clock (step *i* runs at scenario start + 4*i* minutes) and identifiers
`UUIDv5(ca50f191-616f-5fa1-9812-e7defaac955c, "<scenarioKey>/<kind>/<name>")`. Ids are opaque to readers; the
`<kind>/<name>` vocabulary is a generator detail. Commands that the engine must refuse are recorded in
`golden/scenarios.json` (schema `golden-scenarios.schema.json`, informative, not authoritative), and they are never
persisted. Step numbers there are 0-based. `commands` counts persisted command groups plus refused commands.

| scenarioKey | lifecycle | demonstrates |
|---|---|---|
| `payment-failure` | `active` | competing hypotheses, conflicting evidence, accepted and rejected AI proposals, open causal question |
| `intermittent-outage` | `awaitingEvidence` | observation chronology, evidence provenance and supersession, artifact evidence, actions (completed/rejected/cancelled), negative knowledge, engine-forced retrospective prediction |
| `conflicting-metrics` | `active` | blocking contradiction; refused cause confirmation, refused confirmed-cause conclusion and refused resolution; falsify-then-reopen with new evidence |
| `api-latency` | `awaitingVerification` | confirmed multi-member cause, correction implemented, verification pending; resolution refused |
| `authentication-outage` | `resolved` | confirm/reopen/re-confirm cause, failed then satisfied verification (rework), conclusion, resolve/close/reopen/resolve |

Together the valid fixtures exercise all 31 event types.

### 12.2 Negative fixtures

Each `negative/<name>.json` (schema `negative-fixture.schema.json`) carries base64 file bytes, so invalid encodings
can be represented. It also carries the expected primary code and, for stage 15, the expected domain code. Kinds:
`investigation` (validate the file set), `append` (append NDJSON lines to a frozen scenario at `expectedAggregateVersion`)
and `successor` (candidate successor of a frozen scenario). Each fixture is constructed so that the targeted check is
the first to fail.

## 13. Compatibility and evolution

### 13.1 Change classes

| class | examples | version effect | gate |
|---|---|---|---|
| **Compatible implementation change** | a new consumer, a refactored adapter, a faster validator, new tests | none | same schema identity; frozen hashes and golden digests unchanged |
| **Compatible additive extension** | a new event type, a new optional field, a new enum value, a new snapshot array | new minor contract version `1.x.0` with its own `contract/1.x.0/` tree | v1.0.0 readers MUST reject the new data (fail closed); v1.x readers MUST read all v1.0.0 data unchanged |
| **Explicitly versioned migration** | a renamed field, a changed id derivation, a projection change | new major version `2.0.0` plus a migration | the migration reads v1, writes **new** files in a new namespace, is deterministic, repeatable and independently tested; v1 files are never rewritten |
| **Breaking semantic change** | changing what an existing event means, the engine accepting a history it used to refuse (or the reverse) | new `semanticVersion` and explicit authorization | replay of every frozen fixture under the new engine must be reviewed; any changed digest is a FAIL until authorized |

### 13.2 Reader/writer rules

- A v1.0.0 reader reads exactly v1.0.0 and rejects every other `formatVersion`/`schemaVersion`/`semanticVersion` with
  `CAUSA.UNSUPPORTED_VERSION`. It never "best-effort" reads newer data.
- A v1.x writer MUST NOT emit data that a v1.0.0 reader would accept with a different meaning.
- Writers always write the newest version they implement, but only into new workspaces. They never upgrade a v1.0.0
  investigation in place.

### 13.3 Invariants for every future application release

The same schema identity or an approved compatibility path. The same frozen fixture hashes. The same replay semantics
(identical `expectedStateSha256` for every frozen scenario). No unapproved field changes. No silently ignored unknown
events. No historical mutation. No invalid state accepted (every negative fixture rejected with its expected code). These
invariants are machine-checked by `compatibility/v1.0.0/lock.json` (§14).

### 13.4 Version fields

`formatVersion` versions the wire shapes. `semanticVersion` versions domain meaning. `snapshotVersion` (manifest) and
`projectionVersion` (snapshot) both identify the §8.3 projection rules and are `1` in v1.0.0. Changing the projection is an
explicitly versioned migration (§13.1).

### 13.5 Extensions

v1.0.0 deliberately has no extension namespace. Extensibility is achieved only through the versioned change classes
above. Data that a v1.0.0 reader cannot interpret is refused, never dropped. Migration tooling that encounters it
preserves it byte-for-byte by not rewriting the source.

## 14. Compatibility gate

`compatibility/v1.0.0/lock.json` (with `engine.lock.json` and `global.json` at the repository root) pins:

- the schema identity and the digest of every frozen file;
- every expected state digest, and every negative fixture's expected code;
- the engine commit, plus `engineAssemblies`: the SHA-256 of the **bytes of the engine assemblies**
  (`IndyInitRootCauseInvestigator.Engine.dll`, `Aegis.Core.dll`, `FSharp.Core.dll`). These are produced by a
  **sterile build** of the pinned commit, which:
  - takes the sources from the git object store (`git archive <commit>`), so nothing untracked, ignored or generated
    can enter;
  - builds in a fresh temporary directory, with no `Directory.Build.*`, `Directory.Packages.props`, `NuGet.Config`
    or `global.json` above it (other than the pinned `global.json` it writes itself);
  - replaces the whole environment, keeping only the tool path and network egress settings;
  - disables response files (`-noAutoResponse`);
  - uses a fresh HOME and NuGet package folder, with nuget.org as the only package source;
- the .NET SDK version (`global.json`, `rollForward: disable`).

Every build of the engine, sterile or not, uses the same settings (`CausaEngineBuildProperties` in
`Directory.Build.props`). These are Release, no PDB, source paths mapped and no source-control metadata, so the
assembly bytes do not depend on where the tree lives. This was confirmed between two directories, between a git
checkout and a sterile tree, and between the authoring machine and GitHub's runners.

`causa gate .` recomputes all of the pins and runs full verification; any difference is a failure. It also hashes
the engine assemblies **that the running gate process actually loaded** and requires them to equal the sterile
build (`engine.loaded`). So the gate does not need to know how a build could be steered: a planted
`Directory.Build.targets`, `Directory.Build.rsp`, `*.fsproj.user`, `obj/*.targets`, MSBuild property, environment
variable or substituted NuGet package that changes the compiled engine changes the bytes the gate loads, and the
gate fails. `gate-selftest` plants four of these hooks and checks that each one is caught.

After the freeze, the gate is **anchored to the release tag**: `causa gate . --anchor causa-storage-v1.0.0`
additionally requires the working-tree lock to be byte-identical to the lock in the tag, and every frozen path
(`contract/v1.0.0`, `fixtures/v1.0.0`, `compatibility/v1.0.0`, `engine.lock.json`, `global.json`) to equal the tag.
Regenerating the lock in the same change therefore cannot launder a modification. The anchor must be the release
tag itself (`refs/tags/causa-storage-v1.0.0`); any other ref is refused (`anchor.tag`). From the freeze commit onwards,
`compatibility/v1.0.0/RELEASE` exists, and it is listed among the lock's frozen files. While it exists,
`causa gate .` without `--anchor` fails (`anchor.required`), so a missing tag cannot silently downgrade the gate to
unanchored.

**Trust boundary.** The gate is code in this repository, run by a workflow in this repository. It detects
incompatible changes made by anyone who leaves the gate, the toolkit and the workflow alone. A change that edits
those can make any check pass. That is outside what a self-checking repository can prevent. It is covered by the
hosting side:
- a protected release tag;
- required status checks;
- review of every change to `tools/`, `.github/`, `Directory.Build.props` and `global.json`.

A change that deletes `RELEASE` and the tag together is also outside the gate's reach, for the same reason.

The negative fixtures cover every reader stage rule and, one rule per fixture, the engine's decision rules. Each
engine-rule fixture is the **sole** reason for refusal, which the generator asserts against the pinned engine. So a
more lenient reader or engine accepts at least one negative and fails the gate. A rule that can never fire alone is
covered by the fixtures of the rules it always fires with: relaxing it alone does not change which histories are
admissible. `RESOLVE.NO_CONFIRMED_CAUSE` always fires with `RESOLVE.NO_CORRECTION`, and `RESOLVE.BLOCKING_UNKNOWN` with
`RESOLVE.CAUSE_NO_LONGER_HOLDS`. The coverage list is in the experiment record (`ADVERSARIAL-REVIEW.md`).

## 15. Security

Synthetic data only. No credentials, tokens or personal data in any file. Hashes are integrity checks, not
authentication. Artifact URLs in the fixtures use the reserved `example.invalid` domain and MUST be validated
before use in untrusted UI contexts.

## 16. Reconciliation with the candidate contract

This contract implements the candidate's intent and changes it where the implemented engine required. Every change
is recorded with its rationale in `research-documents/research/experiments/EX-CAUSA-STORAGE-2026-001/COMPATIBILITY-DECISIONS.md`.
In summary:

- The event registry is the engine's 31 events, not the candidate's 31 names. Generic `*StateChanged` events become the
  engine's `*Transitioned`/`LifecycleChanged` events. Lifecycle and hypothesis transitions keep their causing command.
- Envelope fields the engine cannot represent were removed: `recordedAt`, `policyRefs`, `causationId`, the envelope
  `provenance` object, `actor.displayName`, and the actor kinds `external`/`imported`. Agents carry `provider` and `model`.
- `eventId` is derived (§5.2).
- Strings are not NFC-normalized. Identifier arrays are ordered, not sorted. There are no optional keys.
- All files are RFC 8785 canonical. `generatedAt` is deterministic.
- `EvidenceAdmitted` has no `verification` field (evidence always starts `unverified`).
- The candidate's `InvestigationRenamed` and `InvestigationArchived` are not registered: the engine has no such commands.
