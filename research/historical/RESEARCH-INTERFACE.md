# Historical research interface (frozen for the research workstreams)

Research workstreams write ONLY under `research/historical/<caseKey>/`. Case keys:
`apollo-1`, `apollo-11`, `challenger-sts-51l`. Claim and source id prefixes: `A1-`, `A11-`, `CH-`.
Construction of Causa investigations is centralized (`tools/src/Causa.Historical`); research files are its only input.
Downloaded documents are never committed (copyright and size); only short attributed excerpts are.

## `sources.json` — JSON array, one object per source

| field | type | rule |
|---|---|---|
| `sourceId` | string | `A1-S01`, `A11-S07`, `CH-S12`, … stable, never reused |
| `title` | string | exact document title |
| `issuer` | string | issuing organization or author |
| `published` | string or null | `YYYY`, `YYYY-MM` or `YYYY-MM-DD` as the document states |
| `url` | string | stable NASA / government / archival URL actually fetched |
| `archivalLocator` | string or null | document number (e.g. `MSC-00171`), NTRS id, volume |
| `retrievedAt` | string | `YYYY-MM-DD` of retrieval |
| `sourceClass` | string | `primary` or `secondary` (secondary = discovery/corroboration only) |
| `retrievalStatus` | string | `retrieved` (content read by the researcher) or `notRetrieved` |
| `httpStatus` | integer or null | final HTTP status observed |
| `rights` | string | redistribution note (e.g. "US Government work, public domain") |

## `claims.json` — JSON array, one object per historical claim

| field | type | rule |
|---|---|---|
| `claimId` | string | `A1-C001`, … stable |
| `statement` | string | researcher's paraphrase; one fact per claim |
| `sourceId` | string | must exist in `sources.json` |
| `locator` | string | page / section / finding / figure / GET timestamp exactly as seen |
| `excerpt` | string or null | exact text copied from the source, at most ~50 words; null if none copied |
| `evidenceCategory` | string | `document`, `testimony`, `measurement`, `log`, `directObservation`, `experimentResult`, `externalReference` |
| `verification` | string | `verified` (source text read; excerpt and locator confirmed), `partiallyVerified` (source read, support is indirect or locator approximate), `unverified` (not confirmed in a retrieved primary source) |
| `historicalTime` | string or null | RFC 3339 UTC, millisecond precision (`1967-01-27T23:31:04.000Z`), only when the source states the time |
| `timeBasis` | string or null | how `historicalTime` was derived (source clock, zone, GET + launch epoch) |
| `topic` | string | short kebab-case tag (`timeline`, `ignition`, `atmosphere`, `alarm-1202`, …) |
| `kind` | string | `fact`, `finding` (an official investigative finding), `recommendation`, `testimony`, `contradiction`, `uncertainty` |
| `notes` | string or null | reliability, uncertainty, disagreement with other sources |

Rules: never invent a citation, page, timestamp, quotation, telemetry value or communication. If a claim cannot be
confirmed, mark it `unverified`; unverified claims are excluded from established findings. Keep contradictory
evidence as separate claims of `kind: contradiction`, citing both sides.

## `analysis.md` — narrative handoff for construction

Sections: Scope; Chronology (claim ids, ordered); Hypotheses and alternatives (with supporting / contradicting claim
ids); Causal structure (role of each factor: root cause, trigger, contributing factor, enabling condition — as the
official findings state them, distinguishing physical from organizational); Contemporary decisions vs retrospective
analysis; Corrective actions and evidence of their effect; Unresolved questions; Excluded / unsupported claims.
