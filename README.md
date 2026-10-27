# Causa Data

Canonical datastore for the Causa Root-Cause Investigator.

## Active demonstration catalog: `historical-v1`

The active demonstration catalog contains exactly three historical investigations. Each one is built from primary sources:

| case | investigation | lifecycle | question |
|---|---|---|---|
| `apollo-1` | Apollo 1 (AS-204) Command Module fire, 27 January 1967 | `awaitingVerification` | Why did the fire start, why was it lethal, and did the corrections work? The established fire conditions are kept separate from the unproven exact ignition source. |
| `apollo-11` | Apollo 11 lunar landing, July 1969 | `awaitingEvidence` | Why did a successful descent continue despite five 1202/1201 program alarms? The case concludes success; it is not modelled as a failure. |
| `challenger-sts-51l` | Space Shuttle Challenger (STS-51-L), 28 January 1986 | `active` | What was the physical failure of the field joint, and what were the organizational causes of the decision to launch? The two are kept apart. |

- **Pointer:** `catalog/active.json`
- **Manifest:** `catalog/historical-v1/manifest.json` (three entries, with digests; schema in `catalog/schemas/catalog-manifest.schema.json`)
- **Records:** `workspaces/demo/historical-v1/investigations/<investigationId>/{manifest.json,events.ndjson,snapshot.json}`. These are ordinary Causa storage v1.0.0 file sets in the isolated workspace namespace (CONTRACT §3, §9). An external application reads them by selecting repository `kemiller2002/causa-data`, its branch and workspace `historical-v1`. No record is bundled into any application.
- **Provenance:**
  - `catalog/historical-v1/provenance/<case>.json` maps each evidence item to its claim, source, locator and URL.
  - The research records are under `research/historical/<case>/`: `sources.json`, `claims.json` and `analysis.md`. The format is set by `RESEARCH-INTERFACE.md`.
- **Review:**
  - `adversarial-review.md`: the independent reviewer's findings.
  - `REVIEW-RESOLUTION.md`: what was done about each finding.
  - `CORRECTIONS.md`: every in-place research correction.
- **Verification:** `catalog/historical-v1/verification/`, with a machine-readable report and `VERIFICATION.md`.

### Construction and validation

The investigations are created by issuing commands to the pinned engine (`Decide.execute`). The scripts are `tools/src/Causa.Tool/Historical/*.fs`. Ids are deterministic, from `UUIDv5(…/catalog/historical-v1, "<case>/<kind>/<name>")`, and the clock is fixed, so a rebuild is byte-identical. Mission times appear only in observation `observedAt`, evidence `capturedAt` and text; every envelope `occurredAt` is the 2026 build clock.

```bash
tools/fetch-engine.sh && dotnet build tools/src/Causa.Tool -c Release
dotnet tools/src/Causa.Tool/bin/Release/net10.0/causa.dll historical-build .    # regenerate (byte-identical)
dotnet tools/src/Causa.Tool/bin/Release/net10.0/causa.dll historical-verify .   # replay, admissibility, provenance, reconstruction, all three loaded together
python3 -I tools/py/historical_catalog.py verify . --app <app checkout>         # independent reader, immutability, secrets, refusals, no app bundling
python3 -I tools/py/historical_catalog.py sources .                             # network: source reachability and excerpt confirmation
```

## Retained v1.0.0 compatibility fixtures (not the active catalog)

- **Synthetic fixtures:** the five synthetic investigations in `fixtures/v1.0.0/` (payment-failure, intermittent-outage, conflicting-metrics, api-latency, authentication-outage). They are **retained unchanged**, together with the 90 negative fixtures, the golden states and the contract in `contract/v1.0.0/`.
- **Release:** the frozen release is tagged `causa-storage-v1.0.0`. The compatibility gate (`causa gate . --anchor causa-storage-v1.0.0`) proves these files are byte-identical to the tag.
- **Use:** they remain the contract's test corpus, not demonstration content.
- **Workspaces:** earlier run workspaces under `workspaces/demo/` (`live-*`, `rest-*`) are kept as they were.

## Contract

**Causa storage contract v1.0.0** is frozen. The full specification is `contract/v1.0.0/CONTRACT.md`, and the schemas are in `contract/v1.0.0/schemas/`. The domain authority is the engine pinned in `engine.lock.json`.

Never migrate, overwrite or reshape frozen files. Writes go only to isolated `workspaces/demo/<runId>/` paths.

## Security and data policy

- The historical catalog contains only public historical records: US Government works and short, attributed excerpts.
- There are no credentials, tokens or private personal data. The validator scans for credentials.
- The contract's §15 was written for synthetic fixtures. The historical catalog is a deliberate content-policy extension recorded in `research/HISTORICAL-CASE-CATALOG.md`; it does not change the contract.
