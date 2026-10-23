# Challenger STS-51-L: adversarial historical review

The reviewer did not build this case. The review covers `sources.json`, `claims.json`, `analysis.md` and `tools/src/Causa.Tool/Historical/Challenger.fs`. Review date: 2026-10-23.

## Method

**Fetching.** All 18 source URLs were fetched again with curl into a new, empty scratch directory (`.../scratchpad/review-ch/`). Every URL returned HTTP 200. HTML was flattened to text. PDFs were converted with `pdftotext -layout`. All processing used `python3 -I`.

**Excerpt check.** Every excerpt (108 claims; CH-C100 has none) was searched, normalised, in the text of its own source:

- 106 matched verbatim.
- CH-C073 and CH-C108 did not match, only because the PDF text is laid out in two columns. Both were confirmed by eye (actions.pdf PDF p. 8, printed p. 2; TWR-17591 Abstract).
- No fabricated or misattributed excerpts were found.

**Statement check.** About 50 claims were then checked in context, statement against the surrounding source text. These covered every timed event, every temperature, all testimony and contradiction claims, and CH-C101 to CH-C109.

**One outside source.** The CAIB Report Vol. I, Ch. 8 was fetched for one later-knowledge finding: `s3.amazonaws.com/akamai.netstorage/anon.nasa-global/CAIB/CAIB_lowres_chapter8.pdf`, HTTP 200.

**Severity.**

- *Critical*: invalidates the conclusion, or a claim rests on a source that does not say it.
- *Major*: a material overstatement, an omission, or a verification gap that a reviewer would act on.
- *Minor*: precision, labelling or hygiene.

**Summary:** 0 critical, 10 major, 14 minor.

## Findings

| id | severity | location | problem | evidence (fetched and seen) | recommended fix |
|---|---|---|---|---|---|
| CH-R01 | major | CH-C027; hDecision statement; `information-flow` contradiction; no claim | **A contradiction is missing.** The case states that decision makers "were unaware of the recent history of problems concerning the O-rings". It does not record that both the House Committee and the Rogers report itself say this history reached Level I / Headquarters. | House Rept 99-1016, p. 5 (PDF p. 13): "Information on the flaws in the joint design and on the problems encountered in missions prior to 51-L was widely available and had been presented to all levels of Shuttle management … at least one detailed briefing at Headquarters". Rogers Ch. VI Finding 5 (p. 148): "The O-ring erosion history presented to Level I at NASA Headquarters in August 1985 was sufficiently detailed to require corrective action prior to the next flight." Rogers Ch. VI also reproduces the 19 Aug 1985 HQ briefing (Fig. 5). | Add claims for House p. 5 and Rogers Ch. VI Finding 5. Record a Material contradiction against CH-C027 / hDecision. Reword hDecision so it separates two things: knowledge of the O-ring history (which was briefed) and knowledge of the 27 January objection (which was not passed on). |
| CH-R02 | major | `tr hDecision MarkSupported` (cites CH-C084); CH-C084 excerpt | **Evidence is used against its own source's meaning.** CH-C084 (House) supports a hypothesis defined by unawareness and communication. Yet the House explicitly rejects that framing. The excerpt cuts off the sentence that says so; it is kept only as a paraphrase in `notes`. | House p. 4: "the underlying problem which led to the Challenger accident was not poor communication or inadequate procedures as implied by the Rogers Commission conclusion. Rather, the fundamental problem was poor technical decision-making over a period of several years". | Extend the CH-C084 excerpt to quote the "not poor communication" sentence. Move CH-C084 from supporting hDecision to supporting hNormalization only. Relate it as contradicting hContainment's weight. |
| CH-R03 | major | hTrigger statement and role; conclusion text "could not seal with O-rings at about 28 degrees F"; missing alternatives | **Single-factor trigger with overstated precision.** The trigger is cold alone. The Commission's own findings make tight gap / high squeeze, the strut-induced gap opening and putty delay co-factors. Its findings also show that cold alone does not explain why *this* joint failed. 28 +/- 5 F is a modelled value at the single coldest point; the sun side was about 50 F. | Ch. IV p. 61 (CH-C023's source): with a 0.004 in gap, sealing is "marginal even in the 40 and 50 degree Fahrenheit temperature range". Ch. IV "Joint Temperature": "all joints had calculated local temperatures as low as 28 +/- 5 degrees". Finding 7: other joints had "similar combinations of tang-to-clevis gap clearance and temperature". Finding 8b: the 310-degree strut enhances gap opening 10-20 % at 300-320 degrees. Finding 11c: putty delay "could be a factor in initial joint failure". Finding 6b: about 50 F on the sun side. | Restate the trigger as cold-reduced resiliency **combined with** a tight 0.004 in gap / full-groove compression at the 300-degree position, plus strut-enhanced opening. Record putty delay as a plausible (not established) co-factor, as ice is. Change the conclusion to "estimated 28 +/- 5 F at the coldest point of the joint". |
| CH-R04 | major | hSchedule `MarkSupported` and role `EnablingCondition` (CH-C061 only) | **Unsupported causal link.** CH-C061 says only that the flight rate "did not accurately reflect the capabilities and resources". It says nothing connecting schedule pressure to the 51-L decision or to the O-ring history. Rogers Ch. VIII hedges any such link. Stronger primary text exists but was not admitted. | Ch. VIII p. 165: "Such considerations may occasionally have obscured engineering concerns." House p. 3 (PDF p. 11): "NASA's drive to achieve a launch schedule of 24 flights per year created pressure throughout the agency that directly contributed to unsafe launch operations." Neither is a claim. | Admit House p. 3 and Rogers p. 165 as claims, and base the Supported transition on them. Otherwise leave hSchedule at Plausible and outside the assertion. Keep the Rogers hedge ("may occasionally") in the hypothesis text. |
| CH-R05 | major | hContainment, hNormalization, hTrigger transitions; conclusion | **Hedged findings become unqualified confirmed causes.** The engine records them at full ConfirmedCause, and the conclusion states them as fact. | Ch. V Finding 3: "troubled by what **appears** to be a propensity". Ch. VI Finding 3: "accepted escalating risk **apparently** because". Ch. IV Findings 9c / 10c: "it is **probable**". Ch. V Finding 1: "it **seems likely** that the launch of 51-L **might** not have occurred when it did" (weaker than the "highly unlikely" in CH-C028). | Carry the source's modality into the hypothesis text and the transition reasons. In the conclusion, mark which elements the Commission stated as probable or apparent. |
| CH-R06 | major | causal assertion roles; acceptance basis of `thiokol-responsibility` | **Role weighting picks a side while claiming not to.** Containment, reversal and safety are ContributingFactor; normalized risk is ranked lower, as EnablingCondition. That is Rogers Ch. V's weighting. Rogers Ch. VI ("An Accident Rooted in History") and the House ("fundamental problem") put the long-term acceptance of erosion first. The acceptance basis says it avoids "choosing one body's weighting", but the roles do choose. | Rogers Ch. VI title and p. 120 (CH-C051). House p. 4-5 (CH-C084). | Give hNormalization at least ContributingFactor. Or state explicitly that the roles follow Rogers Ch. V, and record the House weighting as an accepted residual. |
| CH-R07 | major | `ExpectedLifecycle = Resolved`; corrective actions caSafetyOffice / caLaunchProcess / caFlightRate; conclusion residual | **"Resolved" is justified only for the physical fix.** The organizational preventions have no criterion. caFlightRate is only Approved. Yet the investigation resolves with the organizational causes "confirmed". Later evidence (not knowable in 1988, but relevant to a present-day record) shows they were not fixed. | CAIB Vol. I Ch. 8 (Aug 2003), fetched: "the causes of the institutional failure responsible for Challenger have not been fixed"; §8.1 "Echoes of Challenger". | Label the organizational actions in the conclusion as unverified. Add a later-knowledge residual citing CAIB Ch. 8, marked as post-1988 knowledge. Or add criteria for the organizational actions and leave them unmet. |
| CH-R08 | major | crFirings criterion and its verification (CH-C080); CH-C077; CH-C099 | **The criterion was written so that it passes.** The June 1987 plan committed to one engineering, two developmental and **three qualification** firings before the next flight. Only two QM firings preceded STS-26; the third (QM-8, cold) was deferred until after the flight and a production verification motor was added. The criterion ("completes full-scale, full-duration static test firings") cannot fail. CH-C099 also misstates the gap as "6 vs 5". | v6exsumm (CH-C077): "One engineering test, two developmental, and three qualification full-scale motor test firings will be completed before the next flight." Press kit p. 47: "two development motor tests, two qualification motor (QM) tests, and a production verification motor test. Additionally, one post-STS-26 QM test is scheduled in late December". | Restate crFirings as the committed test set. Record it as partially satisfied, with the QM-8 deferral as an explicit residual: the redesign flew outside its full qualification. Correct CH-C099: the engineering test motor is excluded from the press-kit count, so the real difference is one QM deferred and one PVM added. |
| CH-R09 | major | CH-C109 statement; crCold verification; conclusion "certified for extreme cold by QM-8" | **Misstatement in a claim that a verification rests on.** CH-C109 says QM-8 certified "RSRM seal performance at low temperature". The same abstract says the field joints were heated to 75-130 F before ignition. QM-8 certifies the heater-dependent joint system in a cold-soaked motor; it does not certify cold O-ring sealing. The note on CH-C109 admits this, but the statement and the conclusion drop it. | TWR-17591 Abstract p. ii: "Prior to motor ignition, the field joints were maintained between 75° to 130°F … field joint heaters … controlled at the lower end of their respective limits." | Reword CH-C109 and the conclusion: "certified in a cold-conditioned motor (28 F ambient, 39 F PMBT) with field-joint heaters holding the seals at 75 F or more". Note that cold tolerance of the redesign depends on an active heater system. |
| CH-R10 | major | analysis.md (Scope, Effect, Excluded); CH-C082 notes; CH-C099 notes | **The research record is stale and contradicts the evidence the case relies on.** analysis.md says "No primary post-flight SRM inspection report was retrieved". Its list of primary sources omits CH-S17 and CH-S18. It still excludes "worked as expected" as backed only by secondary sources. The CH-C082 note says the same. The CH-C099 note says "Outcome of the December QM test not retrieved". Yet CH-S17 (TWR-17272) and CH-S18 (TWR-17591) were retrieved, and they carry crFlight and crCold. | sources.json lists CH-S17 and CH-S18 as `retrieved`, HTTP 200, and both were re-fetched. | Update analysis.md (Scope, Verification, Effect, Excluded) and the notes on CH-C082 and CH-C099 to cite CH-C101 to CH-C109. |
| CH-R11 | minor | crFlight verification (CH-C101 to CH-C104) | Evidence of effect is one warm-weather flight: 6 field joints, September conditions, heaters on. This is weak evidence on its own, and it is the "it flew, so it works" inference that the case itself cites Feynman criticising (CH-C057, CH-C072). The source is also the contractor's own assessment. | TWR-17272 §4.9: "The ambient environment was in good agreement with historical September data." | Weight the verification toward the flawed-joint full-scale ground tests (press kit "joint flaws" tests). Keep STS-26 as corroboration only, and say in the residual that the source is a contractor assessment. |
| CH-R12 | minor | CH-C101, CH-C102 ("no damage … on any O-ring"); CH-C105 heaters | The summary wording is contradicted inside the same report. | TWR-17272 §4.12.3: "There was damage to seven vent port plug primary O-rings … The secondary O-ring of the right nozzle-to-case joint vent port plug was also damaged … still under investigation." §3.3 recommends review of a "Field Joint Heater Sensor Placement Problem … sensor misconnection". IFA M-3: fretting, "specific cause under investigation". | Qualify CH-C102: no damage to field-joint O-rings; vent port plug O-ring damage from assembly. Add the heater sensor and fretting items to the residual. |
| CH-R13 | minor | CH-C106; conclusion residual ("a post-flight condition") | The IFA sheet does not limit the vent-valve problem to after the flight. It names a possible pre-launch failure and an ascent threat from water in the joint weather seals, which bears on hIce. | TWR-17272 Table 4.1-2: cause "trapped debris, structural damage, or function failure **prior to launch**"; consequence "precludes threat of ascent flight with water in JPS weatherseals". | Reword: water was found after splashdown, and the valve may have failed before launch. Note the link to the ice-in-joint concern. |
| CH-R14 | minor | RecordAction / RecordFinding "all-flights" | The action compares distress against **joint** temperature. The finding quotes Ch. IV Finding 12, which uses **ambient** temperature (61 F, 21 launches), and mixes it with Ch. VI joint temperatures (63/66 F, 24 flights). The cold sample is 3-4 flights. | Ch. IV Finding 12 ("ambient temperatures of 61 degrees"); Ch. VI p. 146 ("O-ring temperatures at 66 … 63"). | Use one measure; Ch. VI joint temperature fits the stated action. State the n. Keep the ambient statistic as a separate note. |
| CH-R15 | minor | CH-C087 statement | The statement says only "Boisjoly recalled" Hardy being appalled. Four witnesses gave this account, so the contradiction is Hardy against four, not one against one. | Ch. V: Boisjoly (p. 90), Lund (p. 94: "Mr. Hardy said he was appalled that we would make such a recommendation"), Mason (p. 94: "I'm appalled at your recommendation"); chronology p. 108: "reported by both McDonald and Boisjoly". | Name all four in the statement. |
| CH-R16 | minor | `teleconference-accounts` (NonMaterial) and `mulloy-pressure` acceptance basis | **The materiality ratings are inconsistent.** The "appalled" and "next April" remarks are the evidence for the Material "urging of Marshall" finding, yet they are rated NonMaterial. The mulloy-pressure acceptance also rests on authority ("the Commission's finding … stands") rather than on corroborating testimony. | Lund (Ch. V p. 94): Marshall "rejected" the rationale and asked Thiokol to "consider some other thoughts". | Rate the remarks Material, or move them under mulloy-pressure. Cite the Lund testimony as the basis. |
| CH-R17 | minor | CH-C031 statement | The statement asserts as fact that Thiokol gave "an opinion that launch should be delayed" at 5:45 p.m. That point is the contested one in CH-C085 (Reinartz: "I did not perceive it as a recommendation delay"). | Ch. V p. 87 narrative and chronology ("MTI is of the opinion launch should be delayed until Noon or afternoon"), against the Reinartz testimony on the same page. | Attribute the point: "the Commission narrative says …". |
| CH-R18 | minor | `x.Supports hJoint "Joint dimensions…" [CH-C019; CH-C058]` | **Physical and organizational evidence are blurred.** CH-C058 is a Ch. VI organizational finding (an inadequate test and certification programme). It is not about joint dimensions, yet it sits under the physical-design hypothesis with a rationale that does not fit it. | CH-C058 excerpt: "The joint test and certification program was inadequate…" | Move CH-C058 to hNormalization, or to a certification hypothesis. |
| CH-R19 | minor | hSafety supported by CH-C071; CH-C071 / CH-C072 `kind: finding` | Feynman's estimates of loss probability (engineers against management) are not evidence of a silent SR&QA organization. They are also personal observations, not Commission findings, as the claims' own notes say. | v2appf F1 Introduction. | Relate CH-C071 to hNormalization (risk perception) or leave it unrelated. Set `kind` to observation or testimony. |
| CH-R20 | minor | analysis.md "Contemporary record"; hNormalization wording | **Contemporary and later material are confused.** CH-C033, CH-C036 and CH-C047 are post-accident Commission chronology summaries, not records made at the time (the CH-C033 note admits the chart itself was not read). "Normalized" / "normalization" is later framing (Vaughan 1996; CAIB 2003); the word does not appear in any retrieved 1986 source in this sense. | grep of all Rogers chapters, App. F and the House report: no match outside a steel-metallurgy table. CAIB Ch. 8 uses the term. | Re-label CH-C033, CH-C036 and CH-C047 as Commission reconstruction. Phrase hNormalization in 1986 terms ("accepted escalating risk", "acceptable flight risk"), with "normalization" flagged as a later term. |
| CH-R21 | minor | conclusion "failed at ignition"; hJoint "penetrate the External Tank" | Finding 1 says "at or shortly after ignition" and "weakened and/or penetrated". | Ch. IV Finding 1. | Use the source wording. |
| CH-R22 | minor | `accept "joint-temperature"` basis | "Every value is far below the 53 degrees F" ignores the Finding 6b estimate of about 50 F on the sun-facing side of the same joint. That estimate is in the CH-C020 statement. | Ch. IV Finding 6b. | Say "every estimate for the 300-degree region…". |
| CH-R23 | minor | CH-C002 and the StartInvestigation text | The Commission sentence puts "complete structural breakup" next to "All seven crew members perished". That reads as if the crew died at breakup. Later analysis (the Kerwin report on crew cabin and crew remains, 28 July 1986; not retrieved) indicates the crew compartment survived the breakup. | Ch. III p. 19 wording seen. The Kerwin report was not fetched; this point is flagged from general knowledge. | Leave the quote as it is, but do not paraphrase it as a cause of death. Optionally retrieve the Kerwin letter and add it as later analysis. |
| CH-R24 | minor | analysis.md Scope; propagation chain | Scope claims to run "to the range-safety destruct of the SRBs", but no claim covers it. The propagation also skips the lower SRB/ET strut failure and SRB rotation (about 72.20 s), which link the plume to the tank failure. The joint-redesign description cites a "J-seal", which is not in any admitted excerpt. | Ch. III p. 21: "At about 72.20 seconds the lower strut … was severed or pulled away"; event table: "RH SRB RSS destruct 110.250". Press kit p. 45: "J-seal, rather than with putty". | Add claims for 72.20 s, 110.25 s and the J-seal, or narrow the Scope. |

## Checked and found correct

**Excerpts.**

- All 108 excerpts appear in their cited source.
- The locators are consistent with the page markers in the NASA HTML. Checked samples: Ch. IV p. 70-72 findings, Ch. V p. 104 findings, Ch. V chronology p. 106-110, House PDF pp. 12 and 35, actions.pdf PDF p. 8.

**Flight times (CH-C001, CH-C003, CH-C005, CH-C007 to CH-C013).**

- Every MET and GMT value matches the Ch. III event table: 0.678 s / 16:38:00.688; 0.836 s; 58.788 s / 38:58.798 (E207); 59.262 s; 64.660 s; 73.124 s; 73.137 s; 73.618 s / 39:13.628.
- GMT = T0 (16:38:00.010) + MET in every case.
- 11:38 EST = 16:38Z is correct.

**27-28 January times and UTC conversions (EST = UTC-5).** All correct:

- 12:36 p.m. → 17:36Z (CH-C030)
- 5:45 p.m. → 22:45Z (CH-C031)
- 8:45 p.m. → 01:45Z on 28 January (CH-C032)
- 10:30 p.m. → 03:30Z (CH-C034)
- 11:00 p.m. → 04:00Z (CH-C037)
- 11:30 p.m. → 04:30Z (CH-C045)
- 11:45 p.m. → 04:45Z (CH-C038; the "9:45 MST" garble is consistent)
- 5:00 a.m. → 10:00Z (CH-C046)

The order of the script's Observe list matches the chronology. It is consistent with Mulloy's "terminated at approximately 11:15" and with the chronology placing the Aldrich call at 11:30 p.m. to midnight.

**STS-26 times.** 11:37:00 a.m. EDT = 15:37Z (EDT = UTC-4) is correct (CH-C081, CH-C105). Pad 39B is confirmed on the mission page.

**Temperatures and counts.**

- 36 F ambient, "15 degrees colder" (CH-C004).
- 28 +/- 5 F at 300 degrees (CH-C020).
- 5x responsiveness, 75 F against 30 F (CH-C021).
- 55 F sub-scale sealing threshold (CH-C023).
- 51-C at 53 F on 24 January 1985 (CH-C052; Fig. 2 rows also show 53).
- Figure 2 shows 31 F for 51-L (CH-C096).
- Pyrometer readings of 25 F and 8 F (CH-C047).
- Forecasts of 18 F, 22 F and 26 F (CH-C097).
- .171 in erosion against a .070 in prediction (CH-C054).
- QM-8: 20 January 1989, 28 F ambient, 39 F PMBT, cold-conditioned at 20-25 F for 31 days (CH-C108).

**Testimony quotes.** Verbatim and correctly attributed: CH-C035, CH-C043, CH-C044, CH-C045, CH-C085, CH-C086, CH-C088, CH-C089, CH-C090, CH-C091 and CH-C092. The exception is the incompleteness noted in CH-R15.

**House Committee claims.** CH-C083, CH-C093 and CH-C094 are verbatim at the stated pages (Issue 4 Findings 1-2, p. 27).

**Implementation and verification claims.**

- CH-C073 to CH-C079 are verbatim. The engineering test motor firing on 27 May 1987 is confirmed.
- The CH-C101 to CH-C107 summaries in TWR-17272 §3.1.9 / §3.3 exist as quoted. The three SRM IFAs are all listed as closed and not flight constraints.

**Script handling that is sound.**

- hPadIce and hOther are falsified on the correct findings (CH-C049, CH-C018).
- hIce is correctly left Weakened ("may have been").
- The reseal question (Finding 16a/16b) is correctly kept as an open residual.
- The puff-frequency inconsistency between Ch. III (4/s) and Ch. IV (3/s), and between 270-310 and 270-315 degrees, is real.
- Secondary wording ("brittle", "exploded") is correctly kept out of the findings.
- The refusal to resolve before verification is appropriate.
- The conclusion does separate the physical mechanism from the organizational "contributing cause", and does so in the Commission's terms.

## Re-review (round 2)

This round re-checks commit 9765ffe: `REVIEW-RESOLUTION.md`, `CORRECTIONS.md`, `claims.json`, `sources.json` and `analysis.md`, plus the diff of `Challenger.fs`. Only the major findings were re-checked.

The excerpt check was re-run on all 127 claims, including CH-S19 (CAIB Ch. 8). Every new excerpt (CH-C110 to CH-C127) and every corrected excerpt (CH-C084, CH-C099, CH-C109) matches its source. The only non-matches are CH-C073 and CH-C108, as before; both fail only because of the two-column layout.

**Summary:** 9 major findings resolved, 1 partly resolved. The fixes introduced no new critical or major defect; 3 new minor ones are listed below.

### Status of the major findings

| id | status | reason |
|---|---|---|
| CH-R01 | resolved | Two claims were added: CH-C110 (House p. 5) and CH-C111 (Rogers Ch. VI F5). The `history-briefed` contradiction is recorded as Material. hDecision now separates the history, which was briefed, from the 27 January objection, which was not passed on. |
| CH-R02 | resolved | The CH-C084 excerpt now quotes the "not poor communication" sentence. CH-C084 has been removed from hDecision; it now supports hNormalization and contradicts hContainment. |
| CH-R03 | resolved | The trigger now combines cold, the tight gap and the strut ("probably"). Putty (CH-C125) and the other joints (CH-C126) are cited. The conclusion says "modelled at 28 +/- 5 F at the coldest point". |
| CH-R04 | resolved | hSchedule is now Supported on CH-C112 (House "directly contributed") and CH-C113 (Rogers "may occasionally"). Its wording keeps the hedge. |
| CH-R05 | resolved | The source hedges ("probable", "appears", "apparently", "seems likely … might not") now appear in the hypotheses, the transition reasons, the conclusion and the residual. |
| CH-R06 | **partly resolved** | The role is fixed: hNormalization is now ContributingFactor, and the mechanism states which body's weighting is followed. But the `accept "thiokol-responsibility"` basis (Challenger.fs line 132) still says normalized risk is included "as an enabling condition rather than choosing one body's weighting". That text is now false on both counts. It should be reworded. |
| CH-R07 | resolved | The case is now `active`. The institutional criterion is verified Failed and resolution is refused. The organizational measures are no longer presented as effective. (The fairness of using CAIB is assessed below.) |
| CH-R08 | resolved | The criterion now names the committed test set. The deferral of QM-8 is recorded as the Material contradiction `qualification-deferred` and kept as a residual. CH-C099 is corrected. The criterion dropped the plan's "before the next flight". That is acceptable only because the contradiction records the deferral. |
| CH-R09 | resolved | CH-C109, the crCold criterion and the conclusion now say "heated joints in a cold-conditioned motor". |
| CH-R10 | resolved | analysis.md lists CH-S17, CH-S18 and CH-S19. The CH-C082 and CH-C099 notes now cite the retrieved post-flight and QM-8 reports. |

### Is the CAIB-based Failed verification a fair use of later knowledge?

**Verdict: largely yes.** It is a minor defect, not a major one.

**Why it is fair:**

- It is clearly labelled as 2003 knowledge in CH-C115, in analysis.md and in the residual.
- It is not used to judge what the 1986-88 actors knew or did.
- It bears directly on the effectiveness of the measures. The fetched CAIB Ch. 8 also says, specifically about the Flight Readiness Review: "Although the process was altered after Challenger, these changes did not erase the basic problems that were built into the structure of the Flight Readiness Review."

**Weaknesses (new minor findings CH-R25 and CH-R26 below):**

1. **The evidence is general.** CH-C115 is CAIB's general "institutional failure" sentence. The FRR-specific sentence above is a better match for caLaunchProcess (Recommendation V) and should be added.
2. **The criterion was written knowing the outcome.** "The institutional causes … do not recur in later Shuttle operations" has no time bound. A 15-year attribution gap includes intervening causes, such as 1990s budget and workforce cuts. The criterion should name its window and the confounding.
3. **CH-S19 is classed `primary`.** It is primary for Columbia but later analysis for Challenger. Its notes should state this, even though the interface schema allows only primary or secondary.
4. **The other organizational measures are unverified.** caSafetyOffice and caFlightRate are still `implemented` / approved with no criterion. The record should say they are unverified, so that the Failed status on caLaunchProcess is not read as the only organizational verdict.

### New defects introduced by the fixes

| id | severity | location | problem | fix |
|---|---|---|---|---|
| CH-R25 | minor | crInstitutional / RecordVerification `institutional` (CH-C115) | The criterion was written after the outcome was known and has no time window. The evidence is CAIB's general sentence, not its FRR-specific one. | Bound the criterion, for example "through the Shuttle programme as later investigated (CAIB 2003)". Add a claim for the CAIB Ch. 8 FRR sentence and cite it. State the intervening confounders in the residual. |
| CH-R26 | minor | CH-S19 `sourceClass: primary`; caSafetyOffice / caFlightRate | CAIB is later analysis with respect to Challenger. The other two organizational measures have no criterion, so their status is silent. | Note "later analysis (2003), primary for Columbia" in CH-S19 or CH-C115. In the conclusion residual, state that caSafetyOffice and caFlightRate are unverified. |
| CH-R27 | minor | CH-C111 notes | "See also CH-C075 (19 Aug 1985 briefing conclusions, Fig. 5)" points to the wrong claim: CH-C075 is the SRM&QA office. No claim covers Fig. 5. | Remove the cross-reference, or add a claim for Fig. 5. |
