namespace Causa.Tool.Historical

open IndyInitRootCauseInvestigator.Engine
open Causa.Tool.Historical.Kit

/// Apollo 1 (AS-204) Command Module 012 fire, 27 January 1967. Research: research/historical/apollo-1.
/// The established fire conditions are kept apart from the unproven exact ignition source: the trigger member is the
/// Board's "most probable" electrical-arc determination, the specific arc locations stay weakened (never confirmed),
/// and the exact source is an accepted residual unknown.
module Apollo1 =
    /// Claims admitted only after the verification rounds open (Decide N2/M2 freshness).
    let verificationClaims =
        [ "A1-C076"; "A1-C077"; "A1-C078"; "A1-C091"; "A1-C092"; "A1-C093"; "A1-C094"; "A1-C095"; "A1-C096"; "A1-C097"
          "A1-C098"; "A1-C099"; "A1-C100"; "A1-C101"; "A1-C102"; "A1-C116"; "A1-C117"; "A1-C119"; "A1-C120"; "A1-C121"; "A1-C122"; "A1-C123" ]

    let build (x: Ctx) : Case =
        let g = x.Guid
        let hyp name = Id.create<HypothesisTag> (g "hypothesis" name)
        let hEnvironment, hArc, hLioh, hJunction, hCoolant = hyp "oxygen-cabin-combustibles", hyp "electrical-arc-trigger", hyp "lioh-door-cable", hyp "junction-box-cover", hyp "coolant-initiation"
        let hNonElectric, hSystem, hSabotage = hyp "non-electrical-ignition", hyp "powered-system-failure", hyp "sabotage"
        let hWiring, hHatch, hEmergency, hManagement = hyp "vulnerable-wiring-workmanship", hyp "inward-hatch", hyp "no-emergency-preparation", hyp "program-management"
        let hIgnitionStrategy = hyp "ignition-elimination-strategy"
        let cause = Id.create<CausalAssertionTag> (g "cause" "cm-012-fire")
        let caFire, caHatch, caSafety =
            Id.create<CorrectiveActionTag> (g "corrective" "fire-hazard-correction"), Id.create<CorrectiveActionTag> (g "corrective" "unified-hatch"), Id.create<CorrectiveActionTag> (g "corrective" "hazardous-test-safety")
        let crFireTests, crFlight, crHatch = Id.create<CriterionTag> (g "criterion" "boilerplate-fire-tests"), Id.create<CriterionTag> (g "criterion" "block-ii-crewed-flight"), Id.create<CriterionTag> (g "criterion" "hatch-egress")
        let unknown name = Id.create<UnknownTag> (g "unknown" name)
        let contradiction name = Id.create<ContradictionTag> (g "contradiction" name)
        let mockup = Id.create<ActionTag> (g "action" "boilerplate-mockup-fire")
        let a = analyst
        let tr h cmd reason claims = step a (TransitionHypothesis(h, cmd, reason, x.Evs claims))
        let contra name statement claims materiality = step a (RecordContradiction(contradiction name, statement, x.Evs claims, materiality))
        let accept name basis = step a (TransitionContradiction(contradiction name, AcceptContradictionAsResidual, basis))
        let resolveC name basis = step a (TransitionContradiction(contradiction name, ResolveContradiction, basis))
        let inv = Id.create<InvestigationTag> (g "investigation" "root")
        { Key = x.Key
          Title = "Apollo 1 (AS-204) Command Module fire, 27 January 1967"
          Investigation = inv
          Start = utc (2026, 10, 23, 2, 0)
          ExpectedLifecycle = AwaitingVerification
          Research = x.Research
          Steps =
            [ step a (StartInvestigation(inv, "Apollo 1 (AS-204) Command Module fire, 27 January 1967", Id.create (g "observation" "initial"),
                                         "Historical investigation recorded from primary sources. During the Plugs-Out Integrated Test of spacecraft 012 at Launch Complex 34 on 27 January 1967, a fire in the 100-percent oxygen cabin killed the crew; the first crew report of fire began at 23:31:04.7 GMT [A1-C001, A1-C006, A1-C011]. Scope: ignition, fire progression, cabin atmosphere, combustibles, escape, emergency response, and corrective actions, distinguishing established conditions from the unproven exact ignition source."))
              step a ActivateInvestigation ]
            @ ([ "A1-C002"; "A1-C001"; "A1-C003"; "A1-C008"; "A1-C007"; "A1-C009"; "A1-C010"; "A1-C011"; "A1-C014"; "A1-C015"; "A1-C017"; "A1-C018"; "A1-C022"; "A1-C023"; "A1-C024" ]
               |> List.map x.Observe)
            @ [ step a (RecordHypothesis(hEnvironment, "The sealed Command Module cabin, pressurized with 100-percent oxygen at 16.7 psia and containing extensive combustible materials near possible ignition sources, turned a minor ignition into a rapidly fatal fire.",
                                         Some "Pure oxygen at above-ambient pressure makes common materials burn intensely (Finding 9, A1-C057); nylon Raschel netting, Velcro and polyurethane foam carried the flame (A1-C025 to A1-C027); the pressure rise ruptured the cabin at a pressure of at least 29 psia (A1-C015). This catalog assigns the root-cause role to this combined hazardous condition; the Board itself lists six conditions without ranking a root cause (A1-C064)."))
                step a (RecordHypothesis(hArc, "The fire was most probably brought about by a minor electrical malfunction or failure of equipment or wire insulation, the Board's most probable initiator being an electrical arc in the lower forward left-hand equipment bay near the Environmental Control Unit; the exact source was not identified.",
                                         Some "Board conclusion and Determination 1 (A1-C029, A1-C031) and Finding 1c (A1-C030). The initiation 'probably coincides' with the 23:30:55 GMT power interruption (A1-C034), but the Board cautions the transients may as easily be an effect of the fire (A1-C036). Overloaded conductors were examined and a few cases could not be exonerated completely (A1-C114). This is a most-probable determination, not a proven mechanism."))
                step a (RecordHypothesis(hLioh, "The specific ignition source was a chafed power cable beneath the lithium hydroxide access door.", None))
                step a (RecordHypothesis(hJunction, "The specific ignition source was an arc at the cover of junction box C-15-1A-52 in the extreme lower-left corner of the lower equipment bay.", None))
                step a (RecordHypothesis(hCoolant, "Spilled water/glycol coolant initiated the fire.", None))
                step a (RecordHypothesis(hNonElectric, "The fire was initiated non-electrically: electrostatic discharge, spontaneous combustion, mechanically produced heat or a struck object.", None))
                step a (RecordHypothesis(hSystem, "A malfunction of an electrically powered spacecraft system or subassembly initiated the fire.", None))
                step a (RecordHypothesis(hSabotage, "The fire was caused by sabotage.", None))
                step a (RecordHypothesis(hWiring, "Deficiencies in Command Module design, workmanship and quality control, including vulnerable Teflon-insulated wiring and coolant plumbing, created multiple credible ignition opportunities (two of the three candidate arcs showed poor engineering and installation) and an unnecessarily hazardous condition.", None))
                step a (RecordHypothesis(hHatch, "The inward-opening, multi-piece hatch could not be opened against the cabin pressure, so the crew could not escape.", None))
                step a (RecordHypothesis(hEmergency, "The test was not classified as hazardous, so no emergency escape, rescue, fire or medical preparations were in place; this contributed to the failed rescue, although in medical opinion the crew could not have been resuscitated by the time the hatches were opened.", None))
                step a (RecordHypothesis(hIgnitionStrategy, "The pure-oxygen fire hazard was known before the fire, and NASA chose to manage it by eliminating ignition sources; that strategy shaped the decisions not to fit a quick-opening hatch or a cabin extinguisher and not to treat the test as hazardous.",
                                         Some "Pre-fire knowledge and design rationale as stated by the Senate committee (A1-C108 to A1-C112): these were deliberate risk trade-offs made at the time, not neglect; recorded to avoid judging 1966 decisions only with 1967 hindsight."))
                step a (RecordHypothesis(hManagement, "Program management problems (engineering orders open at delivery, an incomplete pre-test constraint list, insufficient response to changing program requirements) let deficiencies stand; the Senate committee inferred overconfidence from earlier pure-oxygen success.", None)) ]
            @ x.AdmitAllExcept verificationClaims
            @ x.Supports hEnvironment "Board findings on the oxygen atmosphere and combustible materials." [ "A1-C006"; "A1-C057"; "A1-C025"; "A1-C026"; "A1-C027"; "A1-C028"; "A1-C064" ]
            @ x.Supports hEnvironment "Fire progression: rapid build-up and cabin rupture within seconds." [ "A1-C014"; "A1-C015"; "A1-C016"; "A1-C018"; "A1-C020" ]
            @ x.Supports hArc "Board determination of the most probable initiator." [ "A1-C029"; "A1-C031"; "A1-C032"; "A1-C033"; "A1-C034"; "A1-C009"; "A1-C010"; "A1-C114" ]
            @ [ x.Relate hArc Insufficient "The pre-fire transients may as easily be an effect of the fire as its cause: the timing does not establish the arc." "A1-C036" ]
            @ x.Supports hArc "Panel 5: arcs with all elements needed to cause the fire exist, none positively identified." [ "A1-C041"; "A1-C043" ]
            @ x.Supports hLioh "Fire-damage patterns indicate the region beneath the LiOH access door." [ "A1-C032"; "A1-C043" ]
            @ x.Supports hJunction "Panel 5 identified this candidate region." [ "A1-C042" ]
            @ [ x.Relate hJunction Insufficient "Panel 5 could not eliminate this arc but found little evidence that it was an ignition source." "A1-C044"
                x.Relate hLioh Insufficient "No single source was conclusively identified: absence of proof, not evidence against the LiOH region." "A1-C030" ]
            @ x.Supports hCoolant "Coolant on damaged wiring provides fuel and an ignition mechanism in laboratory tests." [ "A1-C115" ]
            @ x.Contradicts hCoolant "No substantial evidence that coolant was involved in initiating the fire." [ "A1-C039" ]
            @ x.Contradicts hNonElectric "These ignition possibilities were eliminated." [ "A1-C037" ]
            @ x.Contradicts hSystem "None of the electrically powered systems or subassemblies was associated with initiating the fire." [ "A1-C038" ]
            @ x.Contradicts hSabotage "No evidence suggesting sabotage was discovered." [ "A1-C040" ]
            @ x.Supports hWiring "Board findings on design, workmanship and quality control." [ "A1-C058"; "A1-C059"; "A1-C061" ]
            @ x.Supports hHatch "Board findings on the hatch and egress." [ "A1-C047"; "A1-C048"; "A1-C049"; "A1-C050"; "A1-C051"; "A1-C052"; "A1-C118" ]
            @ x.Supports hHatch "Borman's testimony (an opinion, not a Board finding) that a faster outward hatch would have helped." [ "A1-C053" ]
            @ x.Supports hEmergency "Board findings on hazard classification and emergency preparation." [ "A1-C005"; "A1-C054"; "A1-C055" ]
            @ x.Supports hManagement "Board and Senate findings on management and complacency." [ "A1-C062"; "A1-C063"; "A1-C065"; "A1-C066"; "A1-C124" ]
            @ x.Supports hIgnitionStrategy "Pre-fire knowledge and the ignition-elimination design rationale." [ "A1-C108"; "A1-C109"; "A1-C110"; "A1-C111"; "A1-C112"; "A1-C052"; "A1-C103" ]
            @ [ step a (RecordPrediction(Id.create (g "prediction" "mockup"), hEnvironment,
                                         "If a small ignition in this oxygen atmosphere and material configuration explains the fire, a full-scale mock-up ignited in the suspected area reproduces the fire duration and pressure history.",
                                         NonDiscriminating, Retrospective))
                step a (RecordAction(mockup, Experiment, "Apollo 204 Review Board experiment (recorded here, not performed by this investigation): full-scale boilerplate mock-up fire ignited by a hot wire in the suspected ignition area, a consistency check rather than a test between hypotheses.", [ hEnvironment; hArc ]))
                step a (TransitionAction(mockup, ApproveAction))
                step a (TransitionAction(mockup, StartAction))
                step a (RecordFinding(Id.create (g "finding" "mockup"), mockup,
                                      "Apollo 204 Review Board: a full-scale boilerplate mock-up fire ignited by a hot wire in the suspected area closely reproduced the total fire time and pressure history [A1-C046].",
                                      Some "Reproduces the fire's development from a minor ignition; it does not identify which arc ignited the actual fire."))
                step a (EstablishNegativeKnowledge(Id.create (g "negative" "non-electrical"), "Electrostatic discharge, spontaneous combustion, mechanically produced heat and struck-object impact were eliminated as reasonable ignition possibilities.",
                                                   "CM 012 fire, as found by the Apollo 204 Review Board", x.Evs [ "A1-C037" ]))
                step a (EstablishNegativeKnowledge(Id.create (g "negative" "powered-systems"), "None of the electrically powered spacecraft systems or subassemblies was associated with initiating the fire.",
                                                   "CM 012 fire, as found by the Apollo 204 Review Board", x.Evs [ "A1-C038" ]))
                step a (EstablishNegativeKnowledge(Id.create (g "negative" "sabotage"), "No evidence was discovered that suggested sabotage.", "CM 012 fire, Apollo 204 Review Board", x.Evs [ "A1-C040" ]))
                tr hNonElectric Falsify "The Board eliminated these ignition possibilities." [ "A1-C037" ]
                tr hSystem Falsify "The Board found no powered system or subassembly associated with initiating the fire." [ "A1-C038" ]
                tr hSabotage Falsify "No evidence suggesting sabotage was discovered." [ "A1-C040" ]
                tr hCoolant MarkPlausible "Coolant on damaged wiring can provide fuel and ignition, as laboratory tests showed." [ "A1-C115" ]
                tr hCoolant MarkWeakened "The Board found no substantial evidence that coolant initiated the fire, though it can provide fuel." [ "A1-C039" ]
                tr hLioh MarkPlausible "Fire-damage patterns make the region beneath the LiOH access door the most likely start region." [ "A1-C032" ]
                // Not weakened: the Board names this region most likely but unprovable. It is subsumed by the arc determination.
                tr hLioh Supersede "Subsumed by the Board's broader most-probable determination (an arc near the ECU), which includes this region as most likely; the specific cable cannot be proven." []
                tr hJunction MarkPlausible "Panel 5 identified this region and could not eliminate the arc." [ "A1-C042" ]
                tr hJunction MarkWeakened "Panel 5 found little evidence that this arc could have been an ignition source." [ "A1-C044" ]
                step a (RecordContradiction(contradiction "arc-candidates",
                                            "The evidence points to several candidate arcs (beneath the LiOH access door, the C-15-1A-52 cover plate, the ECU instrumentation harness); only one crater appeared closely linked in time to the fire and was later judged low probability; the lingering fire destroyed evidence.",
                                            x.Evs [ "A1-C032"; "A1-C035"; "A1-C042"; "A1-C044"; "A1-C045" ], Material))
                step a (TransitionContradiction(contradiction "arc-candidates", TriageContradiction, "Material to the exact ignition source, not to the arc determination or the fire conditions."))
                accept "arc-candidates" "The Board determined only a most probable initiator and recorded that the exact source will most likely never be positively identified (A1-C029, A1-C030); the candidates are preserved without choosing one."
                contra "first-report" "Interim reports put the first fire detection at 6:31:03 p.m. EST with speaker attributions that the final report revises; the final report gives 23:31:04.7 GMT and attributes utterances only tentatively, with laboratory support; SP-4205 quotes different wording." [ "A1-C079"; "A1-C011"; "A1-C013"; "A1-C084" ] NonMaterial
                accept "first-report" "The final Board report governs the time; the speaker and full wording cannot be determined from the voice record."
                contra "rupture" "Interim reports gave rupture at 6:31:17 EST at about 29 psi and at about 6:31:19 at about 36 psi; the final report gives about 23:31:19 GMT at at least 29 psia; the Senate report gives about 14.7 s after the fire call." [ "A1-C080"; "A1-C015"; "A1-C083" ] NonMaterial
                accept "rupture" "Seconds-level differences between interim and final analyses; the final report governs and the causal account does not depend on them."
                contra "hatch-opening-time" "Hatch opening after the fire report is given as about 5 minutes (Board), 4 min 55 s and 4 min 36 s (Senate report and testimony); firemen and doctor arrival times also differ." [ "A1-C081"; "A1-C082"; "A1-C022"; "A1-C024" ] NonMaterial
                accept "hatch-opening-time" "After telemetry loss times rest on logs and witness estimates (A1-C021); every account places opening at or about the time by which, in medical opinion, resuscitation was impossible."
                contra "report-errata" "The Board report contains internal inconsistencies: a first pressure rise printed as 23:21:11 GMT and a GMT-to-EST conversion printed as 'subtract 17 hours'." [ "A1-C085"; "A1-C086" ] Informational
                resolveC "report-errata" "Typographical inconsistencies resolved by the report's own paired times; recorded so the errors are not propagated."
                contra "rupture-quadrant" "The rupture quadrant reads '+Y, -Z' in the scanned Board report but '-Y, +Z' in NASA's web transcription." [ "A1-C087" ] Informational
                accept "rupture-quadrant" "The scanned original is the authority for its own text; the transcription difference is preserved and not resolved here."
                contra "cause-of-death" "The Senate report says autopsies proved the crew died of asphyxiation and not from burns; Board Finding 3 names thermal burns as a contributory cause of death." [ "A1-C113"; "A1-C020" ] Informational
                resolveC "cause-of-death" "Both agree asphyxia was the cause of death; the Board's finding, which also lists burns as contributory, governs."
                contra "hatch-counterfactual" "Borman testified that a hatch opening in seconds would have let the crew escape; the Board found the crew never capable of egress because of pressurization before rupture and loss of consciousness, and Borman himself estimated 17-18 s for egress even with a 5-s hatch, which Wydler challenged."
                    [ "A1-C053"; "A1-C107"; "A1-C049"; "A1-C104"; "A1-C106" ] Material
                step a (TransitionContradiction(contradiction "hatch-counterfactual", TriageContradiction, "Material to the hatch's role in the deaths."))
                accept "hatch-counterfactual" "The Board's determination governs the finding that egress was impossible; whether a quick-opening hatch would have saved the crew is a counterfactual preserved as unresolved testimony."
                contra "board-size" "Sources differ on the size of the Review Board." [ "A1-C088" ] Informational
                accept "board-size" "Not material to the investigation."
                contra "new-hatch-time" "Opening time of the new unified hatch is given as five seconds (SP-4205) and as seven seconds from inside and ten seconds by the pad crew (Apollo 7 press kit)." [ "A1-C089"; "A1-C071"; "A1-C073" ] Informational
                accept "new-hatch-time" "Both describe an outward-opening hatch openable in seconds independent of cabin overpressure; the primary press kit values are preferred."
                step a (IdentifyUnknown(unknown "exact-ignition", "Which specific arc or failure ignited the fire?", Material))
                step a (TransitionUnknown(unknown "exact-ignition", AcceptUnknownAsResidual, "The Board found no single ignition source and expected it will most likely never be positively identified; the evidence was destroyed by the fire (A1-C029, A1-C030, A1-C045)."))
                step a (IdentifyUnknown(unknown "power-transient", "Was the 23:30:55 GMT power transient the ignition itself or an early effect of the fire?", Material))
                step a (TransitionUnknown(unknown "power-transient", AcceptUnknownAsResidual, "The Board cautions the pre-fire transients may as easily be a result of the fire as its cause (A1-C036)."))
                step a (IdentifyUnknown(unknown "house-report", "What did the House Subcommittee on NASA Oversight's own report conclude?", Informational))
                step a (TransitionUnknown(unknown "house-report", AcceptUnknownAsResidual, "The report was not retrieved; only a hearing excerpt was read. Its conclusions are not used."))
                step a (RecordAssumption(Id.create (g "assumption" "final-report-times"), "The Board's final report times (GMT) supersede the interim reports' times.", Material))
                step a (TransitionAssumption(Id.create (g "assumption" "final-report-times"), ProvisionallyAcceptAssumption))
                tr hArc MarkPlausible "The Board found the most probable initiator was an electrical arc." [ "A1-C031" ]
                tr hArc MarkSupported "Supported only as the Board's most-probable determination: fire-damage patterns point to the ECU region and no non-electrical or system initiator survived scrutiny; the timing link is not relied on (A1-C036). The exact source is a separate accepted unknown." [ "A1-C029"; "A1-C031"; "A1-C033" ]
                tr hEnvironment MarkPlausible "The test was run in 100-percent oxygen at 16.7 psia." [ "A1-C006" ]
                tr hEnvironment MarkSupported "Board findings on atmosphere, combustibles and fire progression, reproduced by the mock-up test." [ "A1-C057"; "A1-C026"; "A1-C064"; "A1-C046" ]
                tr hWiring MarkPlausible "Teflon wiring insulation was vulnerable to abrasion." [ "A1-C061" ]
                tr hWiring MarkSupported "Board Finding 10 on design, workmanship and quality control." [ "A1-C058"; "A1-C059" ]
                tr hHatch MarkPlausible "The inner hatch opened inward against cabin pressure." [ "A1-C048" ]
                tr hHatch MarkSupported "Board Finding 4: the crew was never capable of emergency egress." [ "A1-C049"; "A1-C050" ]
                tr hEmergency MarkPlausible "The test was not classified as hazardous." [ "A1-C005" ]
                tr hEmergency MarkSupported "Board Finding 5 and Determination 5." [ "A1-C054"; "A1-C055" ]
                tr hIgnitionStrategy MarkPlausible "The design objective was to eliminate all sources of ignition." [ "A1-C110" ]
                tr hIgnitionStrategy MarkSupported "The hazard was documented before the fire, and the hatch, extinguisher and test-classification decisions followed the ignition-elimination strategy." [ "A1-C108"; "A1-C111"; "A1-C112" ]
                tr hManagement MarkPlausible "Open engineering orders and an incomplete constraint list at test time." [ "A1-C062" ]
                tr hManagement MarkSupported "Board Finding and Determination 11 and the Board's conclusion on neglected crew-safety questions; the overconfidence element is the Senate committee's inference." [ "A1-C062"; "A1-C063"; "A1-C065" ]
                step a (RecordCausalAssertion(cause, "Fire in Command Module 012 during the Plugs-Out Integrated Test, the death of the crew and the failed rescue",
                                              [ { Hypothesis = hEnvironment; Role = RootCause }
                                                { Hypothesis = hArc; Role = Trigger }
                                                { Hypothesis = hWiring; Role = ContributingFactor }
                                                { Hypothesis = hHatch; Role = ContributingFactor }
                                                { Hypothesis = hEmergency; Role = ContributingFactor }
                                                { Hypothesis = hManagement; Role = EnablingCondition }
                                                { Hypothesis = hIgnitionStrategy; Role = EnablingCondition } ],
                                              Some "A minor electrical fault, most probably an arc near the ECU (trigger; most probable, exact source unidentified), ignited combustibles in a sealed 100-percent oxygen cabin at 16.7 psia (root condition, as assigned by this catalog); the fire spread rapidly, the pressure rise ruptured the cabin at at least 29 psia about 15 s after the first crew report (about 24 s after the 23:30:55 transient) and toxic gases killed the crew. The inward-opening hatch prevented escape; the absence of rescue preparations for a test not classified as hazardous contributed to the failed rescue; deficient wiring and workmanship created credible ignition opportunities. Management problems and an ignition-elimination strategy, a deliberate pre-fire trade-off, let these conditions stand (enabling conditions)."))
                step a (ConfirmCause cause)
                step a (RecordCorrectiveAction(caFire, Correction, "Remove the conditions for a rapid cabin fire: 60/40 oxygen-nitrogen launch-pad cabin atmosphere, restriction and control of combustible materials with material substitution, protective covers over wire bundles, stainless-steel oxygen lines and armored coolant solder joints.", [ cause ]))
                step a (RecordCorrectiveAction(caHatch, Correction, "Replace the two-piece inward-opening hatch with a unified quick-opening, outward-opening hatch that can be opened from inside or outside independent of cabin overpressure.", [ cause ]))
                step a (RecordCorrectiveAction(caSafety, Prevention, "Classify all tests in 100-percent oxygen environments as hazardous, establish an Office of Flight Safety independent of the program office, and provide emergency procedures, equipment and training for pad tests.", [ cause ]))
                step a (RecordCriterion(crFireTests, caFire, "Full-scale boilerplate fire tests in the revised launch-pad atmosphere show fire propagation within acceptable limits.", Some "Boilerplate fire test programme results"))
                step a (RecordCriterion(crFlight, caFire, "Corroboration in flight: the first crewed Block II spacecraft, carrying the changes, flies with no flammability-related anomaly and its systems operating as intended almost without exception (a flight without fire cannot test the fire countermeasures themselves).", Some "Apollo 7 mission report"))
                step a (RecordCriterion(crHatch, caHatch, "The unified hatch is demonstrated to open in seconds from inside and by the pad crew, independent of cabin overpressure.", Some "Hatch opening or egress test record"))
                step a (TransitionCorrection(caFire, ApproveCorrection))
                step a (TransitionCorrection(caFire, StartCorrection))
                step a (TransitionCorrection(caFire, MarkImplemented))
                step a (TransitionCorrection(caHatch, ApproveCorrection))
                step a (TransitionCorrection(caHatch, StartCorrection))
                step a (TransitionCorrection(caHatch, MarkImplemented))
                step a (TransitionCorrection(caSafety, ApproveCorrection))
                step a (TransitionCorrection(caSafety, StartCorrection))
                step a (TransitionCorrection(caSafety, MarkImplemented))
                step a (TransitionCorrection(caFire, StartVerificationOfEffect))
                step a (TransitionCorrection(caHatch, StartVerificationOfEffect))
                step a MarkAwaitingVerification
                refuse "RESOLVE.CORRECTION_NOT_VERIFIED" a ResolveInvestigation ]
            @ (verificationClaims |> List.collect x.Admit)
            @ [ contra "pad-pressure" "Sources give the pre-fire launch-pad cabin pressure as 16.5 psia (TN D-5654) and 16.7 psia (Review Board)." [ "A1-C097"; "A1-C006" ] Informational
                accept "pad-pressure" "A 0.2 psi difference in the stated pressure of the 100-percent oxygen test atmosphere; immaterial to the findings."
                contra "apollo-7-atmosphere" "The Apollo 7 launch cabin atmosphere was measured at about 64/36 oxygen-nitrogen, against the nominal 60/40." [ "A1-C102"; "A1-C074"; "A1-C116" ] Informational
                accept "apollo-7-atmosphere" "The fire tests verified their 60/40 series at 62 +/- 2 percent oxygen (A1-C116); 64 percent is at the upper edge of the tested band. Recorded as stated."
                step a (RecordVerification(Id.create (g "verification" "fire-tests"), crFireTests, Satisfied, x.Evs [ "A1-C091"; "A1-C092"; "A1-C093"; "A1-C094"; "A1-C095"; "A1-C096"; "A1-C076" ]))
                step a (RecordVerification(Id.create (g "verification" "apollo-7"), crFlight, Satisfied, x.Evs [ "A1-C098"; "A1-C101"; "A1-C123"; "A1-C077" ]))
                // Requirements (3 s to open, 30 s egress), a five-vehicle qualification programme and trouble-free flight use are
                // documented, but no retrieved primary record gives a measured opening or egress time: inconclusive.
                step a (RecordVerification(Id.create (g "verification" "hatch-flight"), crHatch, Inconclusive, x.Evs [ "A1-C099"; "A1-C100"; "A1-C117"; "A1-C119"; "A1-C120"; "A1-C121"; "A1-C122" ]))
                step a (RecordConclusion(Id.create (g "conclusion" "final"),
                                         "The Apollo 1 crew died in a fire that spread rapidly through a sealed 100-percent oxygen cabin at 16.7 psia containing extensive combustibles; the fire was most probably ignited by an electrical arc from vulnerable wiring near the Environmental Control Unit, but the exact ignition source was not identified. The inward-opening hatch could not be opened against cabin pressure and the test had not been treated as hazardous. Full-scale mock-up fire tests found the revised atmosphere and materials firesafe, conditional on minor material changes, and Apollo 7 flew the changed spacecraft with its systems operating as intended almost without exception.",
                                         AcceptedWithResidualUncertainty, [ cause ],
                                         Some "The exact ignition source remains unknown and probably unknowable: several candidate arcs exist and the evidence was destroyed; whether the 23:30:55 GMT power transient was cause or effect is undetermined. The root-cause role for the oxygen-and-combustibles condition is this catalog's assignment, not the Board's ranking. The fire tests that verify the atmosphere change are the same series that selected it, and circuit-breaker panels were accepted rather than corrected; Apollo 7 is corroboration, not a fire test. The test-safety measures are documented only by a Senate summary. The unified hatch's opening time has not been verified from a retrieved primary record (requirements, qualification tests and flight use are documented, measured times are not), so the hatch correction remains awaiting verification and the investigation is not resolved. Pre-fire decisions (no quick hatch, no extinguisher, test not hazardous) were deliberate trade-offs under an ignition-elimination strategy."))
                // The hatch criterion is unsatisfied: resolution is refused, and the investigation stays open.
                refuse "RESOLVE.CRITERION_UNSATISFIED" a ResolveInvestigation ] }

    let definition : CaseDefinition = { Key = "apollo-1"; Build = build }
