# Historical investigation candidates v1

## Deliverable

`historical-candidates-v1` contains two data-only investigations encoded with the existing Causa storage contract. Each
candidate is an ordinary investigation directory containing `manifest.json`, `events.ndjson`, and `snapshot.json`; no
application, schema, UI, or competition-execution code is included.

| Case | Investigation ID | Events | Evidence | Lifecycle |
|---|---|---:|---:|---|
| Watergate: The Break-In and Cover-Up, 1972-1974 | `1e315a78-8b46-5583-8ff0-8a36d207c388` | 132 | 30 | `awaitingEvidence` |
| Pike Township, Marion County, Indiana: Financial Audit Findings, 2024-2025 | `e639642d-8046-5221-8f2e-7945fd094ff9` | 153 | 34 | `awaitingEvidence` |

The open lifecycle is intentional. Watergate retains residual unknowns about the recording gap and proceedings that were
not completed. Pike requires follow-up evidence of corrective-action implementation and effectiveness.

## Case descriptions

### Watergate

The case follows the June 1972 DNC break-in, early criminal investigation and public explanations, Dean's testimony,
Butterfield's disclosure of the recording system, the congressional and prosecutorial demands for recordings, the
Saturday Night Massacre, the grand-jury Road Map, *United States v. Nixon*, the House Judiciary Committee's proposed
articles, resignation, pardon, and preservation of the Nixon records. It distinguishes what was asserted at the time from
what later recordings, judicial rulings, and committee findings established. It also preserves minority and dissenting
views and does not turn grand-jury or impeachment material into a criminal conviction.

Research and source manifest: `research/historical/watergate-1972-1974/`.

### Pike Township

The case represents the complete five-report Indiana State Board of Accounts package for calendar years 2024 and 2025:
the two financial-statement reports, both supplemental compliance reports, and the 2025 federal audit report. It captures
the audit scope, recurring internal-control, compensation-authorization, and vehicle-log findings, the $1,022,949 federal
award schedule understatement and material weakness, applicable Indiana and federal criteria, official responses, and
the corrective action plan. Written responses are labeled as unverified auditee statements. The dataset does not infer
theft, fraud, criminal conduct, or intent.

Research and source manifest: `research/historical/pike-township-audit-2024-2025/`.

## Import and validation

The candidates use storage `formatVersion` and `semanticVersion` `1.0.0` and load through the same reader, schema bundle,
history validation, and event replay used for every existing investigation. Validate them with:

```text
causa validate-dir <repository-root> <repository-root>/workspaces/demo/historical-candidates-v1/investigations/1e315a78-8b46-5583-8ff0-8a36d207c388
causa validate-dir <repository-root> <repository-root>/workspaces/demo/historical-candidates-v1/investigations/e639642d-8046-5221-8f2e-7945fd094ff9
```

Their per-evidence provenance indexes are under `catalog/historical-candidates-v1/provenance/`. The committed research
manifests give stable official URLs and locators for every assertion. Validation results are in
`catalog/historical-candidates-v1/verification/RESULTS.md`.

## Frozen-catalog limitation

The existing `catalog-manifest` schema fixes the historical catalog to exactly three case keys, and the frozen competition
requirement fixes the active demonstration catalog to exactly four investigations (the three historical cases plus the
lightbulb case). Consequently, the current catalog format cannot add these two cases to `catalog/active.json` without
changing a frozen requirement, the catalog schema, and its build/verification code.

This limitation is documented rather than worked around. The candidates are versioned import-ready investigation data,
but they are deliberately not listed in an active catalog manifest. Activation must be an explicit post-kickoff decision
made together with the corresponding requirement and schema change. The existing historical and demonstration catalogs
remain byte-for-byte unchanged.

## Demonstration recommendation

Do **not** activate both candidates in the current competition demonstration: doing so would violate the frozen four-case
contract. If kickoff authorizes rotating or replacing a case without expanding the application requirements, prioritize
Watergate for the main demonstration because its chronology and competing evidentiary postures show the investigation
model especially clearly. Keep Pike as a ready optional import or reserve case; it is valuable for demonstrating careful
separation of audit findings, auditee responses, corrective plans, and criminal inferences, but its open follow-up state
and denser administrative framing require more explanation. If a post-kickoff requirements decision permits a larger or
rotating catalog, both datasets are suitable for demonstration without case-specific application behavior.
