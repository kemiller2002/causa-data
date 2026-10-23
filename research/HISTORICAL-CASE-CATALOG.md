# Causa Historical Demonstration Case Catalog

Status: REQUIRED CONTENT SCOPE; research and contract-compliant data entry PENDING. Do not represent this document as completed investigation records.

## Catalog policy
The active demonstration catalog MUST contain exactly three historical cases: **Apollo 1 (AS-204)**, **Apollo 11**, and **Space Shuttle Challenger (STS-51-L)**. No synthetic software incidents may appear in the active demo catalog. Preserve immutable v1.0.0 fixture/tag history and negative contract fixtures for compatibility testing; do not rewrite, delete, or silently relabel them. Keep app repositories free of demo records: load cases from this external data repository only after authorized connection.

## Case A: Apollo 1 (AS-204), January 27, 1967
Investigate the ground-test cabin fire, crew loss, oxygen-rich cabin atmosphere, ignition-source uncertainty, flammable cabin materials, inward-opening hatch, escape constraints, design/process reviews, and corrective actions. Clearly distinguish established contributing factors from any unproven exact ignition source. Primary research: NASA Apollo 204 Review Board report and associated appendices, NASA historical mission records.

## Case B: Apollo 11, July 1969
Investigate successful lunar landing and operational decision-making, not a fictitious catastrophe. Include Saturn V launch, translunar operations, lunar-module separation/descent, guidance computer 1201/1202 executive overflow alarms and their interpretation, landing-target adjustments and manual control, low-fuel/landing margin communications, Eagle landing, EVA, lunar ascent, rendezvous, return, and recovery. Investigate causal resilience: detection, prioritization, crew/mission-control decisions, and why the mission continued successfully. Distinguish real-time transcript, telemetry, retrospective technical analysis, and later popular retellings. Primary research: NASA Apollo 11 Mission Report, Apollo Lunar Surface Journal, Apollo 11 Flight Journal, NASA mission transcripts, guidance-computer technical reports. Do not assign invented precise fuel remaining, timing, or causal claims.

## Case C: Challenger STS-51-L, January 28, 1986
Investigate solid rocket booster field-joint seal/O-ring failure, low-temperature conditions, erosion/blow-by evidence, launch decision discussions, engineering warnings, communication/management decisions, vehicle breakup, and Rogers Commission findings and recommendations. Primary research: Presidential Commission on the Space Shuttle Challenger Accident (Rogers Commission) report, NASA records and contemporary engineering documentation. Distinguish physical cause from organizational contributors.

## Evidence and modeling requirements
1. Every asserted historical fact has an evidence record with document title, issuing body, stable source URL or archival locator, page/section or transcript timestamp where available, quoted/extracted fact, and retrieval date.
2. Preserve source provenance, uncertainty, disagreement, and chronology. No invented dialogue, observations, event times, or telemetry.
3. Create schema-valid Causa investigations through the current authoritative contract, F# command/event semantics, and validation pipeline. No hand-written JSON bypassing admissibility.
4. Model observations, hypotheses, counterevidence, causal assertions, verification, decisions, and outcome distinctly. Apollo 11's outcome is success, with investigated anomalies and safeguards.
5. Store the three active cases under a new versioned catalog/workspace path; never mutate frozen fixtures or historical run workspaces.
6. The active catalog manifest lists exactly these three case identifiers and no others. Separate negative/synthetic test fixtures are not user-visible catalog cases.
7. Verify full event replay, manifest/snapshot/hash integrity, contract version, evidence-source reachability where feasible, and actual external repository read by Rehearsal 2.
8. Security: historical records only, no credentials, private user data, or tokens. Preserve repository permissions.
9. Before implementation, inspect the documented Praxis intake mechanism of each target repo and submit work there, not via GitHub Issues.

## Completion gate
Not complete until all three fully populated, source-cited, contract-valid cases exist, a three-entry catalog manifest passes verification, and the application loads them externally. This file alone is a requirements record, not demonstration data.
