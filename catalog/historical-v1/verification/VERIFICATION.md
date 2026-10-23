# Verification of the active historical catalog `historical-v1`

Data commit verified: `2c14ef956f1e90033f911649831befc366927043` on branch `claude/historical-catalog-v1`. The files in this directory were committed after that data commit; they do not change any catalog input or output, and the reconstruction check excludes this directory.

**Toolchain:**

- .NET SDK 10.0.402 (`global.json`)
- Engine `kemiller2002/indy-init-rehearsal-001@55f49f61c939a3a780cee592f36f732a1328f981`
- Contract v1.0.0, schema identity `0a68af8c93190d2e4d6c27beb2f8cb67c3aebdda586d01beb7122b71af179152`

**Machine-readable results:**

- `fsharp-report.json` (`causa historical-verify`)
- `python-report.json` (`tools/py/historical_catalog.py verify`)
- `sources-report.json` (`… sources`)
- `repository-suite.txt` (the repository's own CI suite)
- `r002-external-load.txt` (Rehearsal 2's reader against GitHub)

## Catalog

| case | investigation id | lifecycle | events | evidence | sources (primary / secondary) | claims (verified / partial / unverified, excluded) |
|---|---|---|---|---|---|---|
| apollo-1 | `c6b26614-fc43-5013-a15c-cf95942ee07b` | awaitingVerification | 432 | 129 | 15 (13 / 2) | 130 (119 / 10 / 1) |
| apollo-11 | `e3adbee5-5096-5a10-bbfb-cecda86a154b` | awaitingEvidence | 431 | 119 | 27 (19 / 8) | 120 (114 / 5 / 1) |
| challenger-sts-51l | `1784da3c-7141-59ee-a495-1d5d44bf2284` | active | 452 | 128 | 19 (17 / 2) | 129 (120 / 8 / 1) |
| **total** | | | **1315** | **376** | **61 (49 / 12)** | **379 (353 / 23 / 3)** |

Every case uses 26 of the 31 registered event types.

## Required tests

| # | requirement | result | evidence |
|---|---|---|---|
| 1 | Exactly three active historical cases | PASS | `catalog.exactly-three-cases` (F#, Python) |
| 2 | Correct case identities | PASS | `catalog.case-identities`, `catalog.workspace-exact` |
| 3 | Schema validation | PASS | Stage 7 of `Store.validate` (F#); the Python reader at stage 7; `catalog.manifest.schema` |
| 4 | Full event-history validation | PASS | Stages 1–17 of `Store.validate` for every case |
| 5 | Event replay through the F# engine | PASS | `History.validate` (stage 15) re-decides every command group; `<case>.validate` |
| 6 | Snapshot and manifest consistency | PASS | Stages 16–17; `<case>.manifest-consistency`; `<case>.digests-independent` (Python) |
| 7 | Deterministic reconstruction | PASS | `catalog.deterministic-reconstruction`: rebuilt through `Decide.execute`, byte-identical |
| 8 | Source-reference completeness | PASS | `provenance.every-evidence-cites-a-claim`, `citations-resolve`, `every-usable-claim-admitted`, `sources.complete` |
| 9 | Unsupported historical claims detection | PASS | `no-unverified-claim-admitted`; `support.primary-basis` (every Supported transition cites a primary claim); `verification.primary-evidence` |
| 10 | No invented or unresolved citation presented as verified | PASS, with 15 for human review | `verified-only-from-read-sources`; `sources-report.json`: 61/61 sources reachable (HTTP 200); 412 of 427 checks pass; **15 excerpts not machine-confirmable** (REVIEW, listed below) |
| 11 | No modification to immutable fixtures | PASS | `immutability.frozen-paths-equal-release-tag` (byte-equal to `causa-storage-v1.0.0`); `causa gate . --anchor causa-storage-v1.0.0` PASS; `prior-workspaces-untouched` |
| 12 | No credentials or secrets | PASS | `security.no-credentials` (6 patterns, all 49 added or changed files) |
| 13 | External repository retrieval compatibility | PASS | `r002-external-load.txt`: Rehearsal 2's own `Session.catalogOf` / `receive` (full contract pipeline plus `History.validate`) read the three investigations from `api.github.com` at the data commit; all VALID, and the state digests equal the manifest |
| 14 | No demonstration data bundled into the application | PASS | `app.no-bundled-catalog-data`: 1,330 catalog identifiers (ids, titles, digests, every event id) against all 338 tracked files of `indy-init-rehearsal-002@c4d5bbf` |
| 15 | Safe refusal of corrupted or incompatible records | PASS | `<case>.refuses.*`: 9 mutations per case, each refused with the expected stage code by both the Python reader and the F# reader (`validate-dir`) |

**Repository suite at the data commit** (`repository-suite.txt`):

- Contract unit tests: 13/13.
- Schema bundle reproducible; `CONTRACT.md` registry matches.
- `causa verify`: 106/106. `causa reproduce`: 113 files, 0 differing.
- Anchored gate: PASS. Gate self-test: PASS.
- Consumers: rehearsal adapter 113/113 and clean F# consumer 222/222. The Python consumer passes 201 checks and fails none; it leaves 41 admissibility checks not evaluated by design, because it has no engine.
- GitHub Actions `causa-storage-v1.0.0 compatibility gate` (including both historical-catalog steps) is green on the data commit.

## Excerpts needing human review (not machine-confirmable)

These excerpts could not be found automatically in the fetched source text. The causes are OCR noise in scanned PDFs, two-column layout, or text extracted from page images. The research agents confirmed them by reading the rendered pages, and the adversarial reviewers spot-checked many of them. **They are not reported as automatic passes.**

- **apollo-1:** A1-C120, C121, C122, C123 (all from A1-S15, JPL TM 33-425 scan).
- **apollo-11:** A11-C017, C019, C054 (A11-S04, 1969 transcript scan); C081, C085, C108 (A11-S01, Mission Report scan); C086 (A11-S17, press kit).
- **challenger-sts-51l:** CH-C073 (CH-S09, two-column PDF); CH-C108 (CH-S18 scan); CH-C115, CH-C128 (CH-S19, CAIB; CH-C128's page number is inferred).

## Adversarial review

An independent reviewer reviewed each case in two rounds (`research/historical/<case>/adversarial-review.md`):

- **Round 1:** 1 critical, 27 major and 43 minor findings in total.
- **Critical finding:** Apollo 11 recorded a fuel cutoff at "5 s" instead of 45 s, an OCR misread. It has been corrected.
- **Round 2:**
  - All majors resolved, except those marked "accepted as disclosed".
  - The Apollo 1 round-2 majors (N1, N2) were then fixed.
  - No critical defect remains.
- **Audit trail:**
  - every disposition is in `REVIEW-RESOLUTION.md`;
  - every in-place research correction is in `CORRECTIONS.md`;
  - every change is in the git history.

## Limitations and unresolved uncertainties

**Engine and contract limitations.** These are recorded, and the contract was not changed.

1. **Every supported hypothesis must sit in the confirmed cause.** The cause gate treats any Supported hypothesis outside a causal assertion as an unaddressed alternative. So physical and organizational factors (Challenger), and anomaly and success factors (Apollo 11), share one confirmed assertion and are distinguished by role. A second, independent assertion cannot be confirmed beside the first.
2. **There is no "plausible but unprovable" state outside a cause.** Apollo 1's most-likely ignition region (LiOH door) is therefore *Weakened*, with that reason stated. It can be reopened.
3. **Every confirmed cause needs a `rootCause` member.** For Apollo 1 the root-cause role is this catalog's assignment; the Board did not rank one. For Apollo 11 it explains the alarms, not the success.
4. **Only system actors appear.** No human reviewer has acted in these histories, and an agent actor may only propose. Every command is issued by deterministic `system` actors (`historical-catalog-builder`, `historical-source-importer`) from AI-researched, AI-reviewed records. A human historian's review is still advisable.
5. **CONTRACT §15 says "synthetic data only".** The historical catalog is a content-policy extension required by `research/HISTORICAL-CASE-CATALOG.md`. It contains public US Government records and short attributed excerpts.

**Historical uncertainties, preserved as accepted residuals or open items:**

- **Apollo 1:**
  - The exact ignition source, and whether the 23:30:55 GMT transient was cause or effect.
  - No measured opening time for the unified hatch was retrieved, so the hatch verification is *inconclusive* and the case is not resolved.
  - Pre-fire decisions are read both as trade-offs and as a failure to understand the danger.
- **Apollo 11:**
  - Responsibility for the radar-switch configuration: procedure or interface specification.
  - The capacity figure (over 10, about 13, up to 15 percent).
  - No single fuel-remaining figure is asserted.
  - The flown effect of Luminary 1B is not in the record, so the case is *awaitingEvidence*.
  - The pairing of individual Go calls with alarms.
- **Challenger:**
  - The joint temperature (estimates measure different things).
  - Whether the leak was continuous or resealed between about 3 s and 58 s.
  - Whether informing Levels I and II would have changed the outcome.
  - The relative weighting given by the Rogers Commission and the House Committee.
  - The physical correction is verified; the institutional correction is verified *Failed* on 2003 CAIB evidence (later knowledge). The case is therefore `active`, not resolved.

**Other limitations:**

- **Unretrieved sources.** Two sources were not retrieved and are used for no established claim: the House NASA Oversight report (Apollo 1), and a legacy ALSJ address (Apollo 11). Apollo 1's NASA SP-48 is known only through a Senate citation.
- **What the integration test covers.** It exercised Rehearsal 2's engine-level reader against the real GitHub REST API, anonymously, against a public repository. It did **not** drive the browser UI. The app requires a credential to connect, and none was available in this session, so the UI connection flow and rendering of these records are untested here.
- **Publication.** The catalog is published on branch `claude/historical-catalog-v1` (pull request #3). It becomes the default-branch catalog only when that pull request is merged.
- **Praxis intake.** This repository documents no Praxis input directory, so none was guessed and no work item was filed here (see `REHEARSAL-EXTERNAL-DATA-BOUNDARY.md`). The application repository's Praxis inbox was not written: no change to it was required, and this session has read-only access to it.
