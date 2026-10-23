# Apollo 11: resolution of the adversarial review

Review: `adversarial-review.md` (independent reviewer, 1 critical, 9 major, 13 minor). Research-side corrections: `CORRECTIONS.md`. Modelling changes: `tools/src/Causa.Tool/Historical/Apollo11.fs`.

| finding | disposition |
|---|---|
| A11-R01 (critical) | **Fixed.** A11-C058 re-read from the rendered page: 116 / 45 / 20 / 0 s, cut-off at 45 s. Every other fuel number was re-checked. The new hypothesis hFuelCritical ("seconds to spare") is weakened on this evidence. |
| A11-R02 | **Fixed.** hFuel and the `fuel-framing` acceptance now follow the Mission Report's 45 s / 50 s agreement. The larger slosh margin is labelled as later commentary (A11-C115). |
| A11-R03 | **Fixed.** The workaround is described as a two-change package. The conclusion says "consistent with". The residual says it cannot show the counterfactual. Evidence adds A11-C106. |
| A11-R04 | **Fixed.** A11-C102 and C103 added and observed. Text says it was "a hedged working hypothesis on the surface", with the mechanism established after flight. |
| A11-R05 | **Fixed.** `ascent-fix-timing` attributes the timing only to Martin; Eyles is cited correctly (A11-C105). |
| A11-R06 | **Fixed.** A11-C022 removed from the Go support. A11-C101 is the general Go. `second-alarm-go` records the pairing (A11-C120). |
| A11-R07 | **Fixed.** The hypothesis is now "crew deviation", weakened (no source supports it). The blame contradiction is between procedure and interface specification. |
| A11-R08 | **Fixed.** hAbort is narrowed: backup guidance in monitor mode (A11-C110), with abort options narrowing near the surface (A11-C111, C112 related as `insufficient`). Launch-phase abort modes are no longer cited. |
| A11-R09 | **Fixed.** Commentary claims moved to secondary source records (A11-S24 to S26). A new validator gate requires a primary basis for every `markSupported` transition and every satisfied verification. |
| A11-R10 | **Fixed.** New hypotheses hStateVector (supported; contributes to the landing point) and hFuelCritical (weakened). A11-C062 no longer supports the fuel hypothesis. |
| A11-R11, R12 | **Fixed** (claims). |
| A11-R13 | **Fixed.** hMonitor is limited to P63; the P64 load is cited from A11-C047. |
| A11-R14 | **Fixed.** A11-C106 to C110 added. A11-C107 backs the negative knowledge and the hardware falsification. A11-C109 qualifies "not degraded". |
| A11-R15, R17, R21 | **Fixed** (A11-C116, C117, C118 admitted; the codes are explained in the mechanism). |
| A11-R16 | **Fixed** (A11-C053 dropped from hAutomatic). |
| A11-R18 | **Fixed** ("about half a minute"). |
| A11-R19 | **Fixed** (locator). |
| A11-R20 | **Fixed.** The mechanism and the conclusion state that the roles apply to separate effects. hFuel is recast as propellant monitoring. |
| A11-R22 | **Fixed.** The action finding records the prediction as borne out (A11-C031, with A11-C119 as secondary corroboration). |
| A11-R23 | **Fixed** (A11-C075 dated). |
