# Apollo 11 (July 1969): research analysis

## Scope

Apollo 11 **succeeded**. The crew landed on the Moon, did the surface EVA, returned safely, and finished quarantine (A11-C076, A11-C075). The question for this case is why a complex mission succeeded despite operational anomalies. It is not a failure investigation, and no failure is asserted anywhere in the dataset.

The work focuses on the powered descent:
- the 1202/1201 Executive-overflow program alarms;
- the long landing and Armstrong's manual (P66) flight past the West crater boulder field;
- the low-fuel callouts;
- the real-time and post-mission corrective actions.

Launch-to-splashdown milestones are included as a GET-anchored timeline.

Source basis:
- **Primary sources:**
  - NASA *Apollo 11 Mission Report* MSC-00171, November 1969 (A11-S01). Cross-checked against the NTRS scan (A11-S03) and the NTRS record (A11-S02).
  - The 1969 Technical Air-to-Ground Voice Transcription (A11-S04).
  - The Technical Crew Debriefing of 31 July 1969 (A11-S16).
  - The press kit (A11-S17).
  - The Final Flight Mission Rules (A11-S18).
  - The ALSJ/AFJ corrected transcripts (A11-S06 to A11-S15). Their bracketed contributor commentary is retrospective and is labelled as such in `notes`.
- **Secondary (participant) accounts:** Eyles 2004 (A11-S20), Martin 1994 (A11-S21) and Adler 1998 (A11-S22).

Timing conventions:
- GET-derived UTC uses range zero 1969-07-16T13:32:00Z, which the Mission Report states directly (A11-C001).
- Voice-transcript GETs are ground-clock annotations. They lag the computer-determined event times by a few seconds (see A11-C089 and A11-C098).

Excerpts were matched against the fetched text. For the scanned NASA reports, OCR letter-spacing was normalized; the wording was not changed.

## Chronology (claim ids, ordered)

1. Range zero and lift-off, 13:32 UTC, 16 July 1969: A11-C001, A11-C002, A11-C003.
2. Translunar injection at GET 02:44:16.2: A11-C004.
3. Lunar orbit insertion at GET 75:49:50.4: A11-C005.
4. Undocking at GET 100:12:00. The voice report came at 100:18:01, after reacquisition of signal: A11-C006, A11-C007.
5. Descent orbit insertion and powered descent initiation (PDI) at 102:33:05.2. Planned vs actual times: A11-C008, A11-C009.
6. A downrange state-vector error existed at PDI (about 21,000 ft): A11-C010, A11-C011.
7. First 1202 alarm, computer-determined at 102:38:22. Crew calls: A11-C012, A11-C016, A11-C017. Duke's "We're GO on that alarm" at 102:38:53: A11-C018.
8. Second 1202 at 102:39:02. Aldrin links it to the Verb 16 Noun 68 display; the ground answers "Roger. Copy." and offers to monitor Delta-H: A11-C013, A11-C019, A11-C020.
9. P64 at 102:41:32, then the 1201 at 102:42:18. Duke: "We're GO. Same type. We're GO.": A11-C014, A11-C021.
10. Two more 1202s, at 102:42:43 and 102:42:58. Duke: "Roger. 1202. We copy it.": A11-C015, A11-C022, A11-C100.
11. Boulder-field avoidance, manual control, and P66 at 102:43:22. Landing about 1,100 ft beyond the aim point: A11-C049, A11-C050, A11-C051, A11-C052, A11-C053.
12. Low-level light at 102:44:28 or 102:44:30.4. Fuel callouts at "Five percent" (102:44:45), "60 seconds" (102:45:02) and "30 seconds" (102:45:31): A11-C057, A11-C058, A11-C054, A11-C055, A11-C056.
13. Landing at 102:45:39.9 (20:17:39.9 UTC, 20 July). Then "Houston, Tranquility Base here. The Eagle has landed.": A11-C066, A11-C067.
14. First step at 109:24:15. Egress at 109:07:33 and ingress at 111:39:13: A11-C068, A11-C069, A11-C098.
15. Real-time fix: the checklist change keeping the rendezvous radar off for ascent was passed at 121:41:37: A11-C081, A11-C082, A11-C038.
16. Lunar lift-off at 124:22:00.8 and docking at 128:03:00: A11-C070, A11-C071, A11-C083.
17. Transearth injection at 135:23:42.3: A11-C072.
18. Entry and splashdown at 195:18:35 (16:50:35 UTC, 24 July). USS Hornet recovery and the Mobile Quarantine Facility: A11-C073, A11-C074, A11-C099.
19. Quarantine release on 10 August 1969: A11-C075.

## Hypotheses and alternatives

- **H1. The alarms came from a computational overload caused by spurious counter interrupts from the rendezvous radar (RR) coupling data units, which led the Executive to schedule the same job (SERVICER) more than once.**
  - Official finding. Supported by A11-C028 to A11-C034 (Mission Report section 16.2.5) and by A11-C044 to A11-C047 (Eyles).
  - Partly contradicted on mechanism detail: Adler says repeated radar-processing jobs were scheduled (A11-C092). Adler's version is not preferred.
- **H2. The physical root of the interrupts was an 800-Hz excitation that was frequency-locked but not phase-locked between the attitude and translation control assembly (ATCA) and the primary guidance system (PGNS).** It only took effect when the RR mode switch was in AUTO or SLEW.
  - Supported by A11-C032 (Mission Report) and A11-C040 (Eyles, citing the interface control document).
  - A11-C041 adds Eyles's claim that the defect was latent, seen in LM-3 testing and documented but never corrected. This is not in the Mission Report.
- **H3. The cause was a crew or checklist error in the switch setting.**
  - Martin's recollection points this way (A11-C042).
  - Contradicted by Armstrong ("This is the way we've been doing it in all simulations. It was agreed on.", A11-C037), Eyles (A11-C040), Fjeld (A11-C043), and the Mission Report's neutral configuration language (A11-C036).
  - See A11-C093. Only the procedural configuration (AUTO or SLEW) is supported. A procedural error by the crew is not.
- **H4. The low-fuel state was critical.**
  - The real-time indications (A11-C054 to A11-C058) and Armstrong's "We were hitting our abort limit" (A11-C065) support it.
  - Post-flight analysis qualifies it. The Mission Report gives about 45 s indicated and 50 s calculated to depletion at engine cutoff, with 770 lb remaining against 957 lb planned (A11-C059, A11-C060). Retrospective commentary blames slosh for the early low-level light (A11-C062, A11-C064).
  - See A11-C097. No single precise remaining-time number is assigned.
- **H5. The long landing was mainly a manual choice.**
  - Only partly true. The Mission Report attributes the landing-point dispersion primarily to onboard state-vector error (A11-C052, A11-C010, A11-C011).
  - Manual range extension added about 1,100 ft and accounts for the extra propellant use (A11-C051, A11-C061).

## Causal structure

These roles follow the official findings where they exist. Roles taken only from participant accounts are marked.

**Physical factors**
- *Trigger:* counter interrupts from the RR coupling data units (CDUs) took more than 10% of capacity (up to 15% at 12.8 kpps), on top of a load near 90% at peak (A11-C029, A11-C033).
- *Root (official):* the RR/CDU/computer interface configuration, in which the ATCA and PGNS 800-Hz supplies were not phase-locked (A11-C031, A11-C032).
- *Enabling condition:* the RR mode switch was in SLEW or AUTO rather than LGC during descent (A11-C032, A11-C037, A11-C038).
- *Contributing factor:* the extra display load of Verb 16 Noun 68 and the P64 redesignation processing (A11-C019, A11-C045, A11-C047). The 13% figures are participant estimates.
- *Separate physical factors:*
  - The state-vector error that caused the downrange landing point (A11-C010, A11-C011).
  - Propellant slosh that was greater than simulated (A11-C062).

**Organizational factors (participant accounts, not official findings)**
- An interface control document that did not require phase synchronization (A11-C040).
- An LM-3 test observation that was never corrected (A11-C041).
- Procedural training on simulators not wired to a real computer (A11-C042).
- Crew unawareness of which alarms were "Go" (Aldrin's 1991 commentary in A11-S06).

The Mission Report assigns no organizational cause (A11-C036).

### (1) Successful outcome

The landing, the EVA, ascent, rendezvous, transearth injection, entry and recovery were all achieved (A11-C066 to A11-C074). The Mission Report concludes:
- the national objective was met (A11-C076);
- hardware problems did not compromise safety or objectives (A11-C077);
- training showed in the precision of the landing (A11-C078).

### (2) Operational anomalies

- Five Executive-overflow alarms (A11-C012 to A11-C015, A11-C028).
- The downrange state-vector error (A11-C010).
- Slosh and the early low-level light (A11-C062, A11-C063).
- The docking-capture misalignment excursion (noted in A11-C071).
- The wider anomaly list in section 16, which has 28 items including slow cabin decompression (A11-C079, A11-C080).

The momentary S-band dropout at PDI (Mission Report section 5.3) was read but not retained as a claim.

### (3) Contemporary real-time decisions

- Go calls on the alarm reports (A11-C018, A11-C021, A11-C022). For the second alarm the air-to-ground transcript shows only an acknowledgment (A11-C020, A11-C090).
- Aldrin's real-time diagnosis linking the alarms to the 16/68 display (A11-C019).
- Armstrong's decision to fly manually past the crater (A11-C049, A11-C050).
- The RR-off checklist change for ascent (A11-C081, A11-C082).

The stated contemporaneous basis for the Go is the Mission Report's statement that bailout-type alarms "would not prevent continuing the flight", a decision taken before flight, and that navigation was performing properly (A11-C023, A11-C024).

### (4) Retrospective causal analysis

- *Official, post-flight (November 1969):* Mission Report section 16.2.5 (A11-C028 to A11-C034, A11-C084, A11-C085).
- *Participant accounts, later:*
  - Eyles 2004: TLOSS, restart protection, the ICD, LM-3 (A11-C039 to A11-C047).
  - Martin 1994: switch procedure, George Silver (A11-C042, A11-C048).
  - Adler 1998 (A11-C092).
- *Journal commentary:* slosh and fuel (A11-C063, A11-C064), switch controversy (A11-C043).

### (5) Safeguards enabling continuation

- **Priority-driven Executive with restart protection.** It shed or flushed lower-priority work and kept guidance running (A11-C034, A11-C046, A11-C047).
- **Pre-flight alarm disposition and controller judgment.** Supported by A11-C023 and A11-C025. The simulation history comes from A11-C027, which is second-hand. Garman's list is unverified (A11-C026).
- **Mission rule definitions.** An LGC failure is defined as one "which cannot be remedied by crew procedures" (A11-C088). Whether controllers applied that definition here is an inference.
- **Abort capability.**
  - The press kit's abort modes (A11-C086).
  - The Abort Guidance System, aligned to the primary guidance system before PDI (A11-C087).
  - The fuel go/no-go decision point in the descent propellant timeline (A11-C058).
- **Manual control modes** (ATT HOLD, P66) and crew training (A11-C050, A11-C078).

## Contemporary decisions vs retrospective analysis

During descent nobody in Houston or Cambridge knew the cause. Martin recalls MIT "could not figure out, in real time" (A11-C048), and Eyles says events moved too fast for Cambridge to give any input. Controllers continued on the basis of:
- alarm-class rules agreed before flight;
- evidence that the alarms were intermittent and navigation was healthy (A11-C023, A11-C024).

The cause (RR CDU cycle-stealing) was identified only on the surface, before ascent. A mitigation was passed at 121:41:37 (A11-C082). Formal analysis and simulation appear in the November 1969 report (A11-C031).

Participant accounts written 25 to 35 years later disagree with each other and with the real-time record on several points:
- the drain figure (A11-C091);
- the mechanism (A11-C092);
- who was to blame (A11-C093);
- the timing of the fix (A11-C094);
- whether the alarm had been seen in simulation (A11-C095);
- the Go wording on the second alarm (A11-C090).

Where they conflict, real-time transcripts and the Mission Report are preferred.

## Corrective actions and evidence of their effect

1. **Real-time (Apollo 11 ascent).** The rendezvous radar was left off during ascent and the mode-select switch was kept out of PGNS (A11-C081, A11-C082). *Effect:* the ascent was nominal and no ascent alarms are recorded (A11-C083, partially verified).
2. **Software (Luminary 1B, later missions).** The CDUs are zeroed when the RR is unpowered or not in LGC, so the counter interrupts are not generated. Descent monitoring was made cheaper in computer time (A11-C084, A11-C085). *Effect:* no retrieved primary source documents the effect on later flights.
3. **Hardware/procedure (Apollo 12 onward).** The LM bacteria filter was deleted, cutting cabin decompression time from about 5 minutes to under 2 (A11-C080). The effect is stated prospectively only.
4. **Landing accuracy and slosh (participant accounts only).** Eyles's Noun 69 for Apollo 12, and baffles retrofitted from Apollo 14. These were dropped from the established claims because they are secondary only.

## Unresolved questions

1. **The drain.** The Mission Report gives more than 10%, or 15% at maximum rate. Eyles and the Grumman tests give about 13% (13.36%), and Martin recalls 80% speed (A11-C091).
2. **Alarm timing.** ALSJ commentary misattributes Table 5-I times. The Mission Report values are preferred (A11-C089). Voice-call times lag the computer-determined times by a few seconds.
3. **Low-level light time.** The values are 102:44:28, 102:44:30.4 (both Mission Report) and 102:44:31 (ALSJ). A precise "seconds of fuel left" at touchdown should not be asserted. The sources give 45 s indicated and 50 s calculated to depletion at cutoff, with the go/no-go point at 20 s (A11-C096, A11-C097).
4. **Which alarm each later Go answers.** The transcript does not show which of the late 1202s (102:42:43 vs 102:42:58) each acknowledgment answers. The flight-director loop audio was not retrieved (A11-C100).
5. **Who decided, and on what list.** Steve Bales, Jack Garman and the pre-mission alarm review rest only on participant accounts (A11-C025, A11-C026). Kranz's book was not retrieved.
6. **Whether the 800-Hz defect was known and left uncorrected.** This is attested only by Eyles (A11-C041).
7. **Minor timing differences.** Splashdown (A11-C099) and the first step (A11-C098) differ by seconds between sources.

## Excluded / unsupported claims

- **Jack Garman's handwritten alarm list as a documented artifact:** unverified (A11-C026), so excluded from the findings.
- **Any precise "17 seconds of fuel" or similar touchdown figure from popular retellings:** not found in a primary source and not assigned. Aldrin warns in the ALSJ commentary that TV retellings shave seconds off the fuel figures.
- **Kranz's account of the final simulation abort on a 1201:** recorded only as second-hand ALSJ relay (A11-C027, partially verified). The book was not retrieved.
- **Martin's recollection of a last-minute Flight Director instruction to set the RR switch to "Manual" before ascent ignition:** contradicted by the real-time transcript (A11-C094). Not adopted.
- **Adler's "repeated radar data jobs" mechanism, and the core-set and VAC counts:** Adler's account and Eyles's account disagree (seven vs eight core sets). The mechanism is superseded by the Mission Report (A11-C092).
- **Fjeld's claims about Apollo 12's early quantity light and the Apollo 14 baffles:** secondary only and not included as findings.
- **ALSJ index dating of the Apollo 11 mission rules** (September and October 1969; A11-S19): conflicts with the document itself (cover May 16, 1969; Revision B as of July 3, 1969; A11-S18). The index entry is disregarded.
- **Failed or unusable URLs:**
  - Legacy `hq.nasa.gov/alsj` and `history.nasa.gov/alsj` pages redirect to a generic NASA index (A11-S23, notRetrieved).
  - `nasa.gov/.../A11_MissionReport.pdf` returned 404.
  - The ALSJ PAO-transcript OCR HTML returned 404.
  - `a11.eva1.html` returned 404.
  - NTRS citation 19700015747 returned 404; NTRS 19700008096 is the correct record.
