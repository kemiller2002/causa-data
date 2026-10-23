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
          "A1-C098"; "A1-C099"; "A1-C100"; "A1-C101"; "A1-C102" ]

    let build (x: Ctx) : Case =
        let g = x.Guid
        let hyp name = Id.create<HypothesisTag> (g "hypothesis" name)
        let hEnvironment, hArc, hLioh, hJunction, hCoolant = hyp "oxygen-cabin-combustibles", hyp "electrical-arc-trigger", hyp "lioh-door-cable", hyp "junction-box-cover", hyp "coolant-initiation"
        let hNonElectric, hSystem, hSabotage = hyp "non-electrical-ignition", hyp "powered-system-failure", hyp "sabotage"
        let hWiring, hHatch, hEmergency, hManagement = hyp "vulnerable-wiring-workmanship", hyp "inward-hatch", hyp "no-emergency-preparation", hyp "program-management"
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
                                         Some "Pure oxygen at above-ambient pressure makes common materials burn intensely (Finding 9, A1-C057); nylon Raschel netting, Velcro and polyurethane foam carried the flame (A1-C025 to A1-C027); the pressure rise ruptured the cabin at about 29 psia (A1-C015). This catalog assigns the root-cause role to this combined hazardous condition; the Board itself lists six conditions without ranking a root cause (A1-C064)."))
                step a (RecordHypothesis(hArc, "The fire was most probably initiated by an electrical arc from vulnerable wiring in the lower forward left-hand equipment bay near the Environmental Control Unit; the exact source was not identified.",
                                         Some "Board Determination 1 and Finding 1c (A1-C030, A1-C031); the initiation probably coincided with the power interruption at 23:30:55 GMT (A1-C034)."))
                step a (RecordHypothesis(hLioh, "The specific ignition source was a chafed power cable beneath the lithium hydroxide access door.", None))
                step a (RecordHypothesis(hJunction, "The specific ignition source was an arc at the cover of junction box C-15-1A-52 in the extreme lower-left corner of the lower equipment bay.", None))
                step a (RecordHypothesis(hCoolant, "Spilled water/glycol coolant initiated the fire.", None))
                step a (RecordHypothesis(hNonElectric, "The fire was initiated non-electrically: electrostatic discharge, spontaneous combustion, mechanically produced heat or a struck object.", None))
                step a (RecordHypothesis(hSystem, "A malfunction of an electrically powered spacecraft system or subassembly initiated the fire.", None))
                step a (RecordHypothesis(hSabotage, "The fire was caused by sabotage.", None))
                step a (RecordHypothesis(hWiring, "Deficiencies in Command Module design, workmanship and quality control, including vulnerable Teflon-insulated wiring and coolant plumbing, created the ignition opportunity.", None))
                step a (RecordHypothesis(hHatch, "The inward-opening, multi-piece hatch could not be opened against the cabin pressure, so the crew could not escape.", None))
                step a (RecordHypothesis(hEmergency, "The test was not classified as hazardous, so no emergency escape, rescue, fire or medical preparations were in place.", None))
                step a (RecordHypothesis(hManagement, "Program management problems and overconfidence from earlier pure-oxygen operations let the hazardous conditions persist unaddressed.", None)) ]
            @ x.AdmitAllExcept verificationClaims
            @ x.Supports hEnvironment "Board findings on the oxygen atmosphere and combustible materials." [ "A1-C006"; "A1-C057"; "A1-C025"; "A1-C026"; "A1-C027"; "A1-C028"; "A1-C064" ]
            @ x.Supports hEnvironment "Fire progression: rapid build-up and cabin rupture within seconds." [ "A1-C014"; "A1-C015"; "A1-C016"; "A1-C018"; "A1-C020" ]
            @ x.Supports hArc "Board determination of the most probable initiator." [ "A1-C029"; "A1-C031"; "A1-C032"; "A1-C033"; "A1-C034"; "A1-C009"; "A1-C010" ]
            @ x.Supports hArc "Panel 5: arcs with all elements needed to cause the fire exist, none positively identified." [ "A1-C041"; "A1-C043" ]
            @ x.Supports hLioh "Fire-damage patterns indicate the region beneath the LiOH access door." [ "A1-C032"; "A1-C043" ]
            @ x.Supports hJunction "Panel 5 could not eliminate the C-15-1A-52 cover-plate arc." [ "A1-C042"; "A1-C044" ]
            @ x.Contradicts hLioh "No single ignition source was conclusively identified." [ "A1-C030" ]
            @ x.Contradicts hJunction "No single ignition source was conclusively identified." [ "A1-C030" ]
            @ x.Supports hCoolant "Glycol residue could ignite Velcro, per Panel 5." [ "A1-C044" ]
            @ x.Contradicts hCoolant "No substantial evidence that coolant was involved in initiating the fire." [ "A1-C039" ]
            @ x.Contradicts hNonElectric "These ignition possibilities were eliminated." [ "A1-C037" ]
            @ x.Contradicts hSystem "None of the electrically powered systems or subassemblies was associated with initiating the fire." [ "A1-C038" ]
            @ x.Contradicts hSabotage "No evidence suggesting sabotage was discovered." [ "A1-C040" ]
            @ x.Supports hWiring "Board findings on design, workmanship and quality control." [ "A1-C058"; "A1-C059"; "A1-C061" ]
            @ x.Supports hHatch "Board findings on the hatch and egress." [ "A1-C047"; "A1-C048"; "A1-C049"; "A1-C050"; "A1-C051"; "A1-C052"; "A1-C053" ]
            @ x.Supports hEmergency "Board findings on hazard classification and emergency preparation." [ "A1-C005"; "A1-C054"; "A1-C055" ]
            @ x.Supports hManagement "Board and Senate findings on management and complacency." [ "A1-C062"; "A1-C063"; "A1-C065"; "A1-C066"; "A1-C067" ]
            @ [ step a (RecordPrediction(Id.create (g "prediction" "mockup"), hEnvironment,
                                         "If a small ignition in this oxygen atmosphere and material configuration explains the fire, a full-scale mock-up ignited in the suspected area reproduces the fire duration and pressure history.",
                                         DiscriminatingPrediction, Retrospective))
                step a (RecordAction(mockup, Experiment, "Full-scale boilerplate mock-up fire ignited by a hot wire in the suspected ignition area.", [ hEnvironment; hArc ]))
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
                tr hCoolant MarkPlausible "Panel 5 notes glycol residue could ignite Velcro." [ "A1-C044" ]
                tr hCoolant MarkWeakened "The Board found no substantial evidence that coolant initiated the fire, though it can provide fuel." [ "A1-C039" ]
                tr hLioh MarkPlausible "Damage patterns point to the region beneath the LiOH access door." [ "A1-C032" ]
                tr hLioh MarkWeakened "Not established: no single ignition source was conclusively identified." [ "A1-C030"; "A1-C041" ]
                tr hJunction MarkPlausible "Panel 5 could not eliminate this arc as a primary ignition source." [ "A1-C044" ]
                tr hJunction MarkWeakened "Not established: no single ignition source was conclusively identified." [ "A1-C030"; "A1-C041" ]
                step a (RecordContradiction(contradiction "arc-candidates",
                                            "The evidence points to several candidate arcs (beneath the LiOH access door, the C-15-1A-52 cover plate, the ECU instrumentation harness); only one crater appeared closely linked in time to the fire and was later judged low probability; the lingering fire destroyed evidence.",
                                            x.Evs [ "A1-C032"; "A1-C035"; "A1-C042"; "A1-C044"; "A1-C045" ], Material))
                step a (TransitionContradiction(contradiction "arc-candidates", TriageContradiction, "Material to the exact ignition source, not to the arc determination or the fire conditions."))
                accept "arc-candidates" "The Board determined only a most probable initiator and recorded that the exact source will most likely never be positively identified (A1-C029, A1-C030); the candidates are preserved without choosing one."
                contra "first-report" "Interim reports put the first fire detection at 6:31:03 p.m. EST and attributed it to a different crew member than the final report, which gives 23:31:04.7 GMT and does not firmly identify the speaker; SP-4205 quotes different wording." [ "A1-C079"; "A1-C011"; "A1-C013"; "A1-C084" ] NonMaterial
                accept "first-report" "The final Board report governs the time; the speaker and full wording cannot be determined from the voice record."
                contra "rupture" "Interim reports gave rupture at 6:31:17 EST at about 29 psi and at about 6:31:19 at about 36 psi; the final report gives about 23:31:19 GMT at at least 29 psia; the Senate report gives about 14.7 s after the fire call." [ "A1-C080"; "A1-C015"; "A1-C083" ] NonMaterial
                accept "rupture" "Seconds-level differences between interim and final analyses; the final report governs and the causal account does not depend on them."
                contra "hatch-opening-time" "Hatch opening after the fire report is given as about 5 minutes (Board), 4 min 55 s and 4 min 36 s (Senate report and testimony); firemen and doctor arrival times also differ." [ "A1-C081"; "A1-C082"; "A1-C022"; "A1-C024" ] NonMaterial
                accept "hatch-opening-time" "After telemetry loss times rest on logs and witness estimates (A1-C021); every account places opening after the Board's estimate that resuscitation was impossible."
                contra "report-errata" "The Board report contains internal inconsistencies: a first pressure rise printed as 23:21:11 GMT, a GMT-to-EST conversion printed as 'subtract 17 hours', and a rupture quadrant read as '+Y, -Z' in the scan but '-Y, +Z' in NASA's transcription." [ "A1-C085"; "A1-C086"; "A1-C087" ] Informational
                resolveC "report-errata" "Each is a typographical inconsistency resolved by the report's own paired times and figures; recorded so the errors are not propagated."
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
                tr hArc MarkSupported "Board determination, fire-damage patterns and timing agree on an arc near the ECU as the most probable initiator; the exact source is a separate accepted unknown." [ "A1-C029"; "A1-C031"; "A1-C033"; "A1-C034" ]
                tr hEnvironment MarkPlausible "The test was run in 100-percent oxygen at 16.7 psia." [ "A1-C006" ]
                tr hEnvironment MarkSupported "Board findings on atmosphere, combustibles and fire progression, reproduced by the mock-up test." [ "A1-C057"; "A1-C026"; "A1-C064"; "A1-C046" ]
                tr hWiring MarkPlausible "Teflon wiring insulation was vulnerable to abrasion." [ "A1-C061" ]
                tr hWiring MarkSupported "Board Finding 10 on design, workmanship and quality control." [ "A1-C058"; "A1-C059" ]
                tr hHatch MarkPlausible "The inner hatch opened inward against cabin pressure." [ "A1-C048" ]
                tr hHatch MarkSupported "Board Finding 4: the crew was never capable of emergency egress." [ "A1-C049"; "A1-C050" ]
                tr hEmergency MarkPlausible "The test was not classified as hazardous." [ "A1-C005" ]
                tr hEmergency MarkSupported "Board Finding 5 and Determination 5." [ "A1-C054"; "A1-C055" ]
                tr hManagement MarkPlausible "Open engineering orders and an incomplete constraint list at test time." [ "A1-C062" ]
                tr hManagement MarkSupported "Board Determination 11 and the Board's conclusion on neglected crew-safety questions." [ "A1-C063"; "A1-C065" ]
                step a (RecordCausalAssertion(cause, "Fire in Command Module 012 during the Plugs-Out Integrated Test and the death of the crew",
                                              [ { Hypothesis = hEnvironment; Role = RootCause }
                                                { Hypothesis = hArc; Role = Trigger }
                                                { Hypothesis = hWiring; Role = ContributingFactor }
                                                { Hypothesis = hHatch; Role = ContributingFactor }
                                                { Hypothesis = hEmergency; Role = ContributingFactor }
                                                { Hypothesis = hManagement; Role = EnablingCondition } ],
                                              Some "A most probable electrical arc near the ECU (trigger; exact source unidentified) ignited combustibles in a sealed 100-percent oxygen cabin at 16.7 psia (root condition, as assigned by this catalog); the fire spread rapidly, the pressure rise ruptured the cabin within about 15 seconds and toxic gases killed the crew. The inward-opening hatch prevented escape and no rescue preparations existed for a test not classified as hazardous; deficient wiring and workmanship provided the ignition opportunity, and management problems let these conditions persist."))
                step a (ConfirmCause cause)
                step a (RecordCorrectiveAction(caFire, Correction, "Remove the conditions for a rapid cabin fire: 60/40 oxygen-nitrogen launch-pad cabin atmosphere, restriction and control of combustible materials with material substitution, protective covers over wire bundles, stainless-steel oxygen lines and armored coolant solder joints.", [ cause ]))
                step a (RecordCorrectiveAction(caHatch, Correction, "Replace the two-piece inward-opening hatch with a unified quick-opening, outward-opening hatch that can be opened from inside or outside independent of cabin overpressure.", [ cause ]))
                step a (RecordCorrectiveAction(caSafety, Prevention, "Classify all tests in 100-percent oxygen environments as hazardous, establish an Office of Flight Safety independent of the program office, and provide emergency procedures, equipment and training for pad tests.", [ cause ]))
                step a (RecordCriterion(crFireTests, caFire, "Full-scale boilerplate fire tests in the revised launch-pad atmosphere show fire propagation within acceptable limits.", Some "Boilerplate fire test programme results"))
                step a (RecordCriterion(crFlight, caFire, "The first crewed Block II spacecraft, carrying the changes, completes its flight with all spacecraft systems operating satisfactorily.", Some "Apollo 7 mission results"))
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
                contra "apollo-7-atmosphere" "The Apollo 7 launch cabin atmosphere was measured at about 64/36 oxygen-nitrogen, against the nominal 60/40." [ "A1-C102"; "A1-C074" ] Informational
                accept "apollo-7-atmosphere" "Within the diluted-atmosphere regime the fire tests qualified; recorded as stated."
                step a (RecordVerification(Id.create (g "verification" "fire-tests"), crFireTests, Satisfied, x.Evs [ "A1-C091"; "A1-C092"; "A1-C093"; "A1-C094"; "A1-C076" ]))
                step a (RecordVerification(Id.create (g "verification" "apollo-7"), crFlight, Satisfied, x.Evs [ "A1-C098"; "A1-C101"; "A1-C077"; "A1-C078" ]))
                // Apollo 7 shows the hatch mechanisms worked and the hatch was used for egress, but no retrieved primary record
                // demonstrates the opening time from inside, by the pad crew, or against overpressure: inconclusive.
                step a (RecordVerification(Id.create (g "verification" "hatch-flight"), crHatch, Inconclusive, x.Evs [ "A1-C099"; "A1-C100" ]))
                step a (RecordConclusion(Id.create (g "conclusion" "final"),
                                         "The Apollo 1 crew died in a fire that spread rapidly through a sealed 100-percent oxygen cabin at 16.7 psia containing extensive combustibles; the fire was most probably ignited by an electrical arc from vulnerable wiring near the Environmental Control Unit, but the exact ignition source was not identified. The inward-opening hatch could not be opened against cabin pressure and the test had not been treated as hazardous. Full-scale mock-up fire tests showed the revised atmosphere and materials were firesafe, and Apollo 7 flew the changed spacecraft with its systems operating as intended.",
                                         AcceptedWithResidualUncertainty, [ cause ],
                                         Some "The exact ignition source remains unknown and probably unknowable: several candidate arcs exist and the evidence was destroyed; whether the 23:30:55 GMT power transient was cause or effect is undetermined. The root-cause role for the oxygen-and-combustibles condition is this catalog's assignment, not the Board's ranking. The unified hatch's opening time has not been verified from a retrieved primary test record (Apollo 7 shows only that its mechanisms operated and it was used for egress), so the hatch correction remains awaiting verification and the investigation is not resolved."))
                // The hatch criterion is unsatisfied: resolution is refused, and the investigation stays open.
                refuse "RESOLVE.CRITERION_UNSATISFIED" a ResolveInvestigation ] }

    let definition : CaseDefinition = { Key = "apollo-1"; Build = build }
