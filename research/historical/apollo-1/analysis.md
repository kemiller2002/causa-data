# Apollo 1 (AS-204) cabin fire, 27 January 1967: research analysis

## Scope

This file covers the Spacecraft 012 (Block I Command Module) fire during the Space Vehicle Plugs-Out Integrated Test at
Launch Complex 34, Kennedy Space Center, on 27 January 1967. The fire killed Grissom, White and Chaffee (A1-C001 to A1-C004).
It runs from the start of the test to crew removal. It also covers the Apollo 204 Review Board findings, the Senate committee
review, the corrective actions in the Block II spacecraft, and the first crewed Block II flight, Apollo 7, in October 1968.

How the sources were read:

- **Board report (A1-S01).** The scanned report PDF and its OCR were read in full for Parts IV to VI, the interim reports
  and the front matter. Exact wording was checked against the NASA web transcription (A1-S02). Page locators use the
  printed page numbers in the scan. Excerpts resolve OCR line-wrap hyphenation.
- **Panel 5 and errata.** Appendix D Panel 5 (A1-S03, origin and propagation of fire) and the Appendix G Part 1 errata
  (A1-S11) were read.
- **Senate report.** Senate Report No. 956 (A1-S04) was read in OCR. Some passages are too corrupted to quote. Those
  claims are marked `partiallyVerified` with `excerpt: null`. Where an excerpt is given, it keeps the OCR artifacts.
- **Secondary history.** NASA SP-4205 *Chariots for Apollo* (A1-S05) is official NASA history but secondary. It is used
  only for corroboration and for contradiction claims.
- **Not covered.** The House Subcommittee on NASA Oversight report was not located (A1-C090). Only a House hearing
  excerpt was read (A1-S09).

Time conventions:

- The Board states all times in GMT. They are treated as UTC.
- EST = UTC-5. The report's own "subtract 17 hours" conversion note is wrong (A1-C086).
- After telemetry loss at 23:31:22.4 GMT, times come from logs and witness estimates (A1-C021).

## Chronology

| UTC (GMT) | Event | Claims |
|---|---|---|
| 12:55 | Power applied; Plugs-Out Test starts | A1-C008 |
| 19:45 | After hatch installation (19:42), cabin purged with oxygen; test runs at 16.7 psia, 100% O2 | A1-C007, A1-C006 |
| 23:30:54.8 | AC Bus 2 voltage transient | A1-C009 |
| 23:30:55 | Momentary power failure (Finding 1a); probable time of ignition | A1-C010, A1-C034 |
| 23:31:04.7 | First crew report of fire. The interim report said 23:31:03 (contradiction). | A1-C011, A1-C012, A1-C013, A1-C079 |
| ~23:31:12 | Fire becomes intense | A1-C014 |
| ~23:31:19 (19.4) | Cabin ruptures at no less than 29 psia. The CM was designed for about 13 psi differential. Interim figures differ (A1-C080). | A1-C015, A1-C016, A1-C080, A1-C083 |
| 23:31:22.4 | All voice and data lost | A1-C017 |
| ~23:31:30 | Cabin atmosphere estimated lethal | A1-C018, A1-C019, A1-C020 |
| ~23:36 | All hatches open, about 5 min after the fire report. Resuscitation already impossible. | A1-C022, A1-C023, A1-C081 |
| ~23:45 | Doctors arrive, by their own estimate | A1-C024, A1-C082 |
| 1968-10-11 15:02:45 | Apollo 7 launches, the first crewed Block II flight | A1-C077, A1-C078 |

Rupture and fire-stage details: A1-C025, A1-C048, A1-C050.

## Hypotheses and alternatives

**H1, the Board's most probable initiator.** An electrical arc started the fire in the -Y/+Z sector, near the floor in
the lower forward left-hand equipment bay. That is where the ECS instrumentation power wiring leads in between the ECU and
the oxygen panel. It probably happened at the 23:30:55 power interruption.

- Supporting claims: A1-C031, A1-C033, A1-C034, A1-C009, A1-C010, A1-C014, A1-C046.
- This is a "most probable" determination, not a proven one. A1-C030 (Finding 1c) and A1-C029 state the specific
  failure "most likely will never be positively identified".

**H1a, chafed power cable under the LiOH access door.** A power cable passing under the LiOH (CO2 absorber) access door was
chafed by the door's lower edge.

- Supporting claims: A1-C032, A1-C043, A1-C027.
- It cannot be proven because the cable and the door edge were destroyed (A1-C032, A1-C045).

**H1b, other arcs.** Several arc craters were found:

- One crater closely linked in time to the fire was downgraded to low probability (A1-C035).
- Panel 5 found three arcs that had all the elements needed to cause the fire, and could not single out any one of them
  (A1-C041).
- Panel 5 kept two candidate regions open (A1-C042). These were the junction box C-15-1A-52 cover plate in the
  lower-left Lower Equipment Bay, and the right-hand portion of the Left-Hand Equipment Bay.
- The C-15-1A-52 arc "cannot be eliminated" (A1-C044).

**Eliminated or not supported:**

- Electrostatic discharge, spontaneous combustion, mechanical heat, struck-object impact, and flowing-oxygen energy
  (A1-C037).
- Malfunction of powered equipment (A1-C038).
- Coolant as the initiator. There was no substantial evidence for it, but it remains a plausible mechanism (A1-C039).
- Sabotage. No evidence was found (A1-C040).

**Caveat on the data.** The pre-fire transients and crew movements may be effects of the fire rather than its cause
(A1-C036). This weakens the time link between the 23:30:55 transient and ignition.

## Causal structure

The Board separates the unproven exact trigger from the established conditions. It lists six conditions (A1-C064) and
does not rank them as "root cause". The roles below follow the Board's language.

**Trigger (physical, not proven).** The probable trigger was an electrical arc from vulnerable wiring near the ECU
(A1-C029 to A1-C034). Treat it as *most probable*, with no single source identified (A1-C030, A1-C041).

**Enabling and contributing physical conditions (established):**

- A sealed cabin pressurized with 100% O2 at 16.7 psia. The Board found these test conditions "extremely hazardous"
  (A1-C006, A1-C057).
- Extensive combustibles: Raschel nylon netting, Velcro, polyurethane foam (A1-C025, A1-C026, A1-C027, A1-C028).
- Vulnerable Teflon-insulated wiring and poor installation (A1-C061, A1-C058).
- Vulnerable coolant plumbing carrying a combustible, corrosive coolant (A1-C058, A1-C039).

**Contributing to the fatal outcome (established):**

- An inward-opening, multi-piece hatch that could not be opened against internal pressure (A1-C047, A1-C048, A1-C049).
- Venting capacity too small to matter (A1-C050).
- Pressure rupture, then a toxic atmosphere; death by asphyxia (A1-C015, A1-C018, A1-C020).

**Organizational (established):**

- The test was not classified as hazardous (A1-C005, A1-C054).
- No emergency procedures, fire teams, rescue teams or medical teams; equipment unsuited to dense smoke (A1-C054, A1-C055).
- Deficiencies in design, workmanship and quality control, and no fire-protection design features (A1-C058, A1-C059).
- Configuration and management problems: open engineering orders, an unsigned constraint list, and uncertified items
  (A1-C062, A1-C063).
- The Board's overall judgement: "mundane but equally vital" safety questions were neglected (A1-C065).
- The Senate committee attributed the failure to complacency from earlier oxygen-test success (A1-C066). It also found
  that no single person was responsible (see A1-S04 Preface).

## Contemporary decisions vs retrospective analysis

**Decisions made before the fire:**

- Quick-opening hatches were rejected because of the risk of accidental release in space (A1-C052).
- Pure-oxygen ground testing was accepted on the strength of prior success (A1-C066).
- Hazard classification was based on propellants, pyrotechnics and similar hazards (A1-C005).

**Retrospective views:**

- The Board's findings (A1-C054 to A1-C065).
- Borman's view that a fast outward hatch would have saved the crew. This is testimony, not a finding (A1-C053).
- Brooke and Percy noted that the Phillips-report deficiencies coincided with CM 012's production, but explicitly
  did not claim a causal link (A1-C067).

**Interim versus final reports.** The interim reports gave times, pressures and speaker attributions that the final
report revised (A1-C079, A1-C080). The final report should be preferred.

## Corrective actions and evidence of their effect

**Board recommendations:**

- Restrict and control combustibles (A1-C026).
- Faster, simpler egress (A1-C049).
- Emergency procedures and equipment (A1-C056).
- ECS, plumbing and wiring fixes, extinguishing means, and emergency breathing oxygen (A1-C060).
- Mock-up fire tests and diluent-gas studies (A1-C057).

**Implemented:**

- 100% O2 tests defined as hazardous, and an independent Office of Flight Safety set up (A1-C068).
- Control of combustibles, which the Senate report called the most significant change (A1-C069).
- A unified, outward-opening quick hatch. The opening time is given as 5 s in A1-C071 and as 7 s from inside or 10 s by
  pad crew in A1-C073; see the contradiction in A1-C089.
- A 60/40 O2/N2 launch-pad atmosphere, recommended and accepted in March 1968 (A1-C072, A1-C074).
- Stainless-steel oxygen lines, armored solder joints, wiring covers, an emergency oxygen system, an extinguisher, and
  material substitution (A1-C070, A1-C075).

**Evidence of effect:**

- Boilerplate fire tests showed acceptable fire propagation in 60/40 at 16 psi, against about 50% unacceptable spread in
  100% O2 at 16 psi (A1-C076).
- Apollo 7 flew about 260 h; all spacecraft systems operated satisfactorily and all but one detailed objective was met
  (A1-C077, A1-C078).

Apollo 7 shows the redesigned spacecraft flew successfully. It does not directly test the fire countermeasures, because
no fire occurred.

## Unresolved questions

1. **The exact ignition source and location.** It is unproven (A1-C030, A1-C041, A1-C042, A1-C044, A1-C045).
2. **Whether the 23:30:54.8-55 transient was the ignition event.** It might instead be an early effect of the fire
   (A1-C034, A1-C036).
3. **Times after telemetry loss.** Hatch opening is variously 4:36, 4:55, ~5:00 or 5:30 after the report. Firemen and
   doctor arrival times also differ between sources (A1-C021, A1-C081, A1-C082).
4. **Who spoke first, and the exact words** (A1-C013, A1-C079, A1-C084).
5. **Transcription and typographical errors in the report.** These include the "23:21:11" typo, the "subtract 17 hours"
   note, and the rupture quadrant ("+Y,-Z" in the scan versus "-Y,+Z" on the web) (A1-C085, A1-C086, A1-C087). There is
   also a board-size discrepancy (A1-C088).
6. **The House subcommittee's findings** are not represented (A1-C090).

## Excluded / unsupported claims

- **A1-C090** is `unverified` and must not support any finding.
- **`partiallyVerified` claims.** Use these as corroboration only: A1-C051, A1-C053, A1-C060, A1-C066, A1-C068, A1-C070
  and A1-C084. They were read in corrupted OCR or as tables, so no excerpt was copied.
- **Not asserted.** Popular accounts make several claims this dataset does not assert:
  - A specific wire, such as the "silver-plated copper wire near the ECU door", as the proven cause.
  - The Baron report, or the Phillips report, as causal. A1-C067 explicitly disclaims causation.
  - Exact last words beyond the Board's transcription.
- **Interim-report figures.** These are kept only as contradiction claims: the 6:31:03 detection time, the 36 psi
  rupture pressure, and Chaffee as first speaker.
- **Lower-precedence secondary figures.** Secondary figures (the five-and-a-half-minute hatch time in A1-S05, the
  16.2 psi pad pressure, and the five-second hatch) give way to primary sources.
