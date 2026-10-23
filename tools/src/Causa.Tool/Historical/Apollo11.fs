namespace Causa.Tool.Historical

open IndyInitRootCauseInvestigator.Engine
open Causa.Tool.Historical.Kit

/// Apollo 11, July 1969: a successful mission. The investigation does not invent a failure; it asks why the lunar
/// descent succeeded despite five executive-overflow program alarms. Research: research/historical/apollo-11.
/// It keeps five things apart: the outcome (observations, conclusion), the anomalies (alarms, fuel indications),
/// the contemporary decisions (Go calls, P66, checklist change, timed from the 1969 transcript), the retrospective
/// analysis (Mission Report anomaly analysis, later participant accounts, as evidence with their class noted), and
/// the safeguards (restart protection, flight rules, abort capability) as causal members.
module Apollo11 =
    let build (x: Ctx) : Case =
        let g = x.Guid
        let hyp name = Id.create<HypothesisTag> (g "hypothesis" name)
        let hRadar, hMonitor, hHardware, hChecklist = hyp "rr-cdu-counter-interrupts", hyp "monitor-display-load", hyp "computer-hardware-failure", hyp "crew-checklist-error"
        let hRestart, hRules, hManual, hFuel, hAbort, hAutomatic =
            hyp "restart-protection", hyp "ground-alarm-rules", hyp "manual-p66-landing", hyp "propellant-margin-monitoring", hyp "abort-capability", hyp "automatic-landing-sufficient"
        let cause = Id.create<CausalAssertionTag> (g "cause" "alarms-and-safe-landing")
        let caSoftware, caChecklist = Id.create<CorrectiveActionTag> (g "corrective" "luminary-1b"), Id.create<CorrectiveActionTag> (g "corrective" "ascent-rr-checklist")
        let crAscent = Id.create<CriterionTag> (g "criterion" "ascent-no-alarms")
        let unknown name = Id.create<UnknownTag> (g "unknown" name)
        let contradiction name = Id.create<ContradictionTag> (g "contradiction" name)
        let analysis = Id.create<ActionTag> (g "action" "postflight-alarm-analysis")
        let a = analyst
        let tr h cmd reason claims = step a (TransitionHypothesis(h, cmd, reason, x.Evs claims))
        let contra name statement claims materiality = step a (RecordContradiction(contradiction name, statement, x.Evs claims, materiality))
        let accept name basis = step a (TransitionContradiction(contradiction name, AcceptContradictionAsResidual, basis))
        let resolveC name basis = step a (TransitionContradiction(contradiction name, ResolveContradiction, basis))
        let inv = Id.create<InvestigationTag> (g "investigation" "root")
        { Key = x.Key
          Title = "Apollo 11 lunar landing, July 1969: why the descent succeeded despite program alarms"
          Investigation = inv
          Start = utc (2026, 10, 23, 7, 0)
          ExpectedLifecycle = AwaitingEvidence
          Research = x.Research
          Steps =
            [ step a (StartInvestigation(inv, "Apollo 11 lunar landing, July 1969: why the descent succeeded despite program alarms", Id.create (g "observation" "initial"),
                                         "Historical investigation recorded from primary sources. Outcome: success. Apollo 11 lifted off at 13:32:00 GMT on 16 July 1969, the lunar module landed at GET 102:45:40 on 20 July after five computer program alarms during powered descent, and the crew returned on 24 July [A11-C001, A11-C066, A11-C073, A11-C076]. This investigation does not posit a failure: it examines why a complex descent continued safely despite operational anomalies."))
              step a ActivateInvestigation ]
            // Mission chronology (GET converted to UTC from the Mission Report's range zero, 13:32:00 GMT 16 July 1969).
            @ ([ "A11-C001"; "A11-C004"; "A11-C005"; "A11-C006"; "A11-C007"; "A11-C008"; "A11-C012"; "A11-C016"; "A11-C017"; "A11-C018"; "A11-C013"; "A11-C019"
                 "A11-C020"; "A11-C014"; "A11-C021"; "A11-C015"; "A11-C022"; "A11-C050"; "A11-C057"; "A11-C054"; "A11-C055"; "A11-C056"; "A11-C066"; "A11-C067"
                 "A11-C068"; "A11-C069"; "A11-C082"; "A11-C038"; "A11-C070"; "A11-C071"; "A11-C072"; "A11-C073"; "A11-C074"; "A11-C075" ]
               |> List.map x.Observe)
            // Why did the alarms occur?
            @ [ step a (RecordHypothesis(hRadar, "The 1202 and 1201 alarms were Executive overflows caused by counter interrupts from the rendezvous radar coupling data units, which stole more than 10 percent of the computer's capacity.",
                                         Some "With the rendezvous radar mode switch in AUTO or SLEW, resolver excitation came from a 28-volt 800-hertz supply frequency-locked but not phase-locked to the guidance reference; the CDUs slewed and sent continuous pulses to the computer (A11-C031, A11-C032, A11-C033). Repetitive jobs were then scheduled again before earlier copies completed, overflowing the Executive (A11-C029, A11-C030, A11-C034)."))
                step a (RecordHypothesis(hMonitor, "Crew monitoring displays (Verb 16 Noun 68) added computer load that reduced the remaining margin when the alarms occurred.", None))
                step a (RecordHypothesis(hHardware, "The alarms indicated a failure of the lunar module guidance computer.", None))
                step a (RecordHypothesis(hChecklist, "The alarms were caused by a crew or checklist error in setting the rendezvous radar switch.", None))
                // Why did the descent continue to a safe landing?
                step a (RecordHypothesis(hRestart, "The computer's restart protection preserved guidance: each overflow triggered a software restart that discarded stale jobs and resumed the priority guidance and control computations.",
                                         Some "Mission Report: when capacity is exceeded the computer generates a software restart and an Executive overflow alarm (A11-C034); the alarms did not degrade any primary guidance or control function (A11-C035); participant analysis describes the restarts shedding load (A11-C046, A11-C047)."))
                step a (RecordHypothesis(hRules, "Flight controllers could judge in real time that the alarms were not a guidance failure, because it had been decided before flight that such alarms would not prevent continuing, and they gave Go.", None))
                step a (RecordHypothesis(hManual, "Armstrong's manual control in P66 extended the range past a boulder field around a large crater to a safe touchdown point.", None))
                step a (RecordHypothesis(hFuel, "The landing was completed inside the propellant margin, monitored in real time by the low-level light and ground callouts; post-flight analysis shows more margin than the real-time indications suggested.", None))
                step a (RecordHypothesis(hAbort, "Abort capability (abort guidance system, abort modes and mission rules) remained available throughout descent, so continuing did not commit the crew without a fallback.", None))
                step a (RecordHypothesis(hAutomatic, "The automatic descent would have reached a safe landing without crew intervention.", None)) ]
            @ x.AdmitAllExcept [ "A11-C083" ]
            @ x.Supports hRadar "Mission Report anomaly analysis (post-flight analyses and simulations)." [ "A11-C028"; "A11-C029"; "A11-C030"; "A11-C031"; "A11-C032"; "A11-C033"; "A11-C034" ]
            @ x.Supports hRadar "The rendezvous radar switch was not in LGC during descent." [ "A11-C037"; "A11-C038" ]
            @ x.Supports hRadar "Participant accounts agree on the radar-CDU mechanism while differing on the percentage." [ "A11-C044"; "A11-C041" ]
            @ x.Supports hMonitor "Aldrin correlated the second alarm with the Verb 16 Noun 68 display; margin fell to 10 percent or less with it." [ "A11-C019"; "A11-C045" ]
            @ x.Contradicts hHardware "Guidance and control performance was satisfactory and navigation functions were performed properly." [ "A11-C028"; "A11-C035"; "A11-C024" ]
            @ x.Supports hChecklist "A participant recollection frames the cause as the switch set per the procedures book." [ "A11-C042" ]
            @ x.Contradicts hChecklist "Armstrong said the configuration was agreed and used in every simulation; the Mission Report does not call it a crew error; Eyles attributes it to the interface documentation." [ "A11-C037"; "A11-C036"; "A11-C040" ]
            @ x.Supports hRestart "Restart and overflow behaviour as documented, and the absence of guidance degradation." [ "A11-C034"; "A11-C035"; "A11-C028"; "A11-C046"; "A11-C047" ]
            @ x.Supports hRules "Pre-flight decision on alarms, real-time Go calls and the mission rule defining a computer failure." [ "A11-C023"; "A11-C024"; "A11-C018"; "A11-C021"; "A11-C022"; "A11-C088"; "A11-C025" ]
            @ x.Supports hManual "Manual takeover, P66 and the landing point." [ "A11-C049"; "A11-C050"; "A11-C051"; "A11-C052"; "A11-C078" ]
            @ x.Supports hFuel "Real-time propellant indications and post-flight propellant accounting." [ "A11-C054"; "A11-C055"; "A11-C056"; "A11-C058"; "A11-C059"; "A11-C060"; "A11-C061"; "A11-C062"; "A11-C064" ]
            @ x.Supports hAbort "Documented abort capability and backup guidance." [ "A11-C086"; "A11-C087"; "A11-C088" ]
            @ x.Contradicts hAutomatic "The automatic descent was heading into a boulder field around a large crater." [ "A11-C049"; "A11-C053" ]
            @ [ step a (RecordPrediction(Id.create (g "prediction" "phase-reproduction"), hRadar,
                                         "If the radar coupling data units caused the overflows, reproducing the unsynchronized 800-hertz excitation in post-flight simulation reproduces the counter-interrupt load and the alarms.",
                                         DiscriminatingPrediction, Retrospective))
                step a (RecordAction(analysis, Test, "Post-flight analyses and simulations of the descent program alarms (Mission Report anomaly analysis).", [ hRadar; hHardware ]))
                step a (TransitionAction(analysis, ApproveAction))
                step a (TransitionAction(analysis, StartAction))
                step a (RecordFinding(Id.create (g "finding" "postflight-analysis"), analysis,
                                      "Apollo 11 Mission Report: analyses and simulations identified the cause of the multiple job scheduling as primarily counter interrupts from the rendezvous radar coupling data unit; more than 10 percent of computational capacity was preempted [A11-C029, A11-C031].",
                                      Some "Supports the radar-CDU explanation and excludes a computer hardware failure."))
                step a (EstablishNegativeKnowledge(Id.create (g "negative" "no-guidance-degradation"), "The program alarms did not indicate a guidance computer failure and did not degrade any primary guidance or control function.",
                                                   "Apollo 11 powered descent, per the Mission Report", x.Evs [ "A11-C028"; "A11-C035" ]))
                tr hHardware Falsify "Guidance and control performance was satisfactory; the alarms were Executive overflows, not a computer failure." [ "A11-C028"; "A11-C035" ]
                tr hChecklist MarkPlausible "One participant recollection frames the cause as the switch setting in the procedures book." [ "A11-C042" ]
                tr hChecklist MarkWeakened "The configuration was agreed and simulated; the Mission Report assigns no crew error and attributes the cause to the interface; blame framings conflict." [ "A11-C037"; "A11-C036"; "A11-C040" ]
                tr hAutomatic MarkPlausible "The automatic system was guiding the descent until manual takeover." [ "A11-C050" ]
                tr hAutomatic MarkWeakened "The automatic descent would have ended in a boulder field around a large crater; whether it would have been survivable is a counterfactual the record does not settle." [ "A11-C049"; "A11-C053" ]
                // Disagreements preserved.
                contra "load-percentage" "The share of computer capacity taken by the radar interrupts is given as more than 10 percent and 15 percent at maximum rate (Mission Report), about 13 percent (Eyles), and as the computer running at 80 percent speed (Martin)." [ "A11-C091"; "A11-C029"; "A11-C033"; "A11-C044" ] NonMaterial
                accept "load-percentage" "All accounts agree the stolen capacity exceeded the margin; the Mission Report figures are recorded as primary and the others as retrospective."
                contra "overflowing-jobs" "Accounts differ on which jobs overflowed: Adler describes repeated jobs processing non-existent radar data, while the Mission Report describes repetitive guidance jobs rescheduled before completion." [ "A11-C092"; "A11-C030" ] NonMaterial
                accept "overflowing-jobs" "The Mission Report description is primary; the secondary account is preserved."
                contra "blame-framing" "The cause is framed as a crew checklist error (Martin), as an agreed configuration used in every simulation (Armstrong), and as faulty interface documentation (Eyles); the Mission Report assigns no blame." [ "A11-C093"; "A11-C042"; "A11-C037"; "A11-C040"; "A11-C036" ] Material
                step a (TransitionContradiction(contradiction "blame-framing", TriageContradiction, "Material to whether the anomaly is classed as crew error; it does not change the physical mechanism."))
                accept "blame-framing" "The physical mechanism is agreed; attribution of responsibility is contested and is not decided here. The crew-error hypothesis is weakened, not falsified."
                contra "second-alarm-go" "Eyles writes that the ground said 'go' again after the second 1202, but the 1969 transcript shows only 'Roger. Copy.' and an offer to monitor Delta-H." [ "A11-C090"; "A11-C020" ] NonMaterial
                resolveC "second-alarm-go" "The contemporaneous transcript governs the wording; the retrospective account is not used for the exchange."
                contra "alarm-table" "ALSJ commentary gives 102:41:32 for the 1201 alarm, but Mission Report Table 5-I lists 102:41:32 as entry into P64 and the 1201 at 102:42:18." [ "A11-C089"; "A11-C014" ] Informational
                resolveC "alarm-table" "The Mission Report table governs; the commentary misread it."
                contra "ascent-fix-timing" "Martin and Eyles recall the radar instruction being passed minutes before lift-off; the transcript shows the checklist change at GET 121:41:37, about 2 h 40 min before lift-off." [ "A11-C094"; "A11-C082"; "A11-C070" ] NonMaterial
                resolveC "ascent-fix-timing" "The transcript time governs; the recollections are preserved as retrospective."
                contra "simulation-familiarity" "Sources differ on prior familiarity with the alarms: ALSJ and Martin say the 1202 had not been seen in simulation or testing, while Kranz's account (relayed in ALSJ) describes a final simulation aborted for a 1201 alarm." [ "A11-C095"; "A11-C048"; "A11-C027" ] NonMaterial
                accept "simulation-familiarity" "Kranz's account is second-hand and not independently verified here; the pre-flight decision recorded in the Mission Report (A11-C023) is the established basis for the Go calls."
                contra "low-level-light" "The low-level light time is 102:44:28 (Table 5-I), 102:44:30.4 (descent propulsion section) and 102:44:31 (ALSJ)." [ "A11-C096"; "A11-C057"; "A11-C058"; "A11-C063" ] Informational
                accept "low-level-light" "A difference of seconds between tables and analyses; no single value is asserted."
                contra "fuel-framing" "Real-time callouts and the low-level light suggested a critical fuel state, while the Mission Report computed about 45 s indicated and 50 s calculated to depletion at engine cutoff, and retrospective commentary attributes the early light to slosh." [ "A11-C097"; "A11-C055"; "A11-C056"; "A11-C059"; "A11-C064"; "A11-C065" ] Material
                step a (TransitionContradiction(contradiction "fuel-framing", TriageContradiction, "Material to the propellant-margin hypothesis: what the crew knew differs from what post-flight analysis found."))
                accept "fuel-framing" "Both are true of their time: the crew and controllers acted on the real-time indication, which post-flight analysis showed to be conservative. No single precise remaining-fuel figure is asserted."
                contra "first-step-time" "The time of the first step and 'That's one small step' differs between the raw transcript, NASA's press figure and the Mission Report." [ "A11-C098"; "A11-C068" ] Informational
                accept "first-step-time" "Seconds-level differences between records; the Mission Report time is recorded as stated."
                contra "splashdown-time" "Splashdown time differs between the Mission Report (195:18:35, 1650 GMT) and the Apollo Flight Journal index." [ "A11-C099"; "A11-C073" ] Informational
                accept "splashdown-time" "The Mission Report is recorded as primary."
                step a (IdentifyUnknown(unknown "fuel-at-touchdown", "How much usable propellant margin remained at touchdown?", Material))
                step a (TransitionUnknown(unknown "fuel-at-touchdown", AcceptUnknownAsResidual, "The record gives 770 lb remaining and 45 s indicated / 50 s calculated to depletion at engine cutoff, with slosh affecting the gauge; no single precise figure is asserted (A11-C059, A11-C060, A11-C064)."))
                step a (IdentifyUnknown(unknown "which-go", "Which individual alarm did each ground acknowledgment during P64 answer?", Informational))
                step a (TransitionUnknown(unknown "which-go", AcceptUnknownAsResidual, "The voice reports and Go calls are attested but their pairing with Table 5-I alarms is uncertain (A11-C100)."))
                step a (IdentifyUnknown(unknown "alarm-list", "Was a handwritten alarm list the controllers' basis for the Go calls, and what did it say?", Informational))
                step a (TransitionUnknown(unknown "alarm-list", AcceptUnknownAsResidual, "Often cited, but no retrieved primary source confirms it; the Mission Report's pre-flight decision is used instead."))
                step a (RecordAssumption(Id.create (g "assumption" "get-epoch"), "Ground elapsed times convert to UTC from range zero, 13:32:00 GMT on 16 July 1969.", Material))
                step a (TransitionAssumption(Id.create (g "assumption" "get-epoch"), VerifyAssumption))
                tr hRadar MarkPlausible "The alarms were Executive overflows with more than 10 percent of capacity preempted." [ "A11-C029" ]
                tr hRadar MarkSupported "Post-flight analyses and simulations identified the radar coupling data unit interrupts and the excitation phasing." [ "A11-C031"; "A11-C032"; "A11-C033" ]
                tr hMonitor MarkPlausible "Aldrin correlated an alarm with the Verb 16 Noun 68 display." [ "A11-C019" ]
                tr hMonitor MarkSupported "Margin fell to 10 percent or less with the monitor display up; the Mission Report later planned a lower-cost monitoring change." [ "A11-C045"; "A11-C085" ]
                tr hRestart MarkPlausible "The computer generates a restart when capacity is exceeded." [ "A11-C034" ]
                tr hRestart MarkSupported "Guidance and control were not degraded through five alarms." [ "A11-C035"; "A11-C028" ]
                tr hRules MarkPlausible "It had been decided before flight that such alarms would not prevent continuing." [ "A11-C023" ]
                tr hRules MarkSupported "The ground gave Go within about 31 seconds of the first alarm, on the basis recorded in the Mission Report." [ "A11-C018"; "A11-C024"; "A11-C021" ]
                tr hManual MarkPlausible "Manual control was assumed to avoid a boulder field." [ "A11-C049" ]
                tr hManual MarkSupported "P66 was entered and the landing point was moved downrange; the report credits the skill of the landing." [ "A11-C050"; "A11-C051"; "A11-C078" ]
                tr hFuel MarkPlausible "Fuel callouts were made and acknowledged in real time." [ "A11-C054"; "A11-C055"; "A11-C056" ]
                tr hFuel MarkSupported "Post-flight accounting shows propellant remained at cutoff, and attributes the higher usage to crater avoidance." [ "A11-C059"; "A11-C060"; "A11-C061" ]
                tr hAbort MarkPlausible "Abort modes were defined for every phase." [ "A11-C086" ]
                tr hAbort MarkSupported "The abort guidance system and mission rules provided a fallback." [ "A11-C087"; "A11-C088" ]
                step a (RecordCausalAssertion(cause, "Five executive-overflow program alarms occurred during Apollo 11's powered descent, and the descent nevertheless continued to a safe landing",
                                              [ { Hypothesis = hRadar; Role = RootCause }
                                                { Hypothesis = hMonitor; Role = ContributingFactor }
                                                { Hypothesis = hRestart; Role = EnablingCondition }
                                                { Hypothesis = hRules; Role = ContributingFactor }
                                                { Hypothesis = hManual; Role = ContributingFactor }
                                                { Hypothesis = hFuel; Role = ContributingFactor }
                                                { Hypothesis = hAbort; Role = EnablingCondition } ],
                                              Some "ANOMALY: counter interrupts from the rendezvous radar coupling data units, caused by unsynchronized 800-hertz excitation with the radar switch out of LGC, stole more than 10 percent of computer capacity (root cause of the alarms); monitoring displays reduced the remaining margin (contributing). CONTINUATION: restart protection kept the priority guidance computations running (enabling condition); controllers applied a pre-flight decision that such alarms did not stop the descent and gave Go; Armstrong flew P66 past a boulder field; propellant was monitored to touchdown inside the margin (contributing factors); abort capability remained available (enabling condition). The single assertion combines the anomaly and its successful handling because the v1 cause gate does not allow a second, independent assertion beside it."))
                step a (ConfirmCause cause)
                // Corrective actions.
                step a (RecordCorrectiveAction(caChecklist, Workaround, "Real-time checklist change for lunar ascent: rendezvous radar off during ascent to prevent computer overload (passed to the crew at GET 121:41:37).", [ cause ]))
                step a (RecordCorrectiveAction(caSoftware, Correction, "Luminary 1B for future missions: do not process rendezvous radar CDU counter interrupts when the radar is unpowered or not in LGC (zero the data units), and reduce the computer time needed to monitor the descent.", [ cause ]))
                step a (RecordCriterion(crAscent, caChecklist, "Lunar ascent with the revised radar configuration completes without executive-overflow program alarms.", Some "Mission Report ascent and alarm records"))
                step a (TransitionCorrection(caChecklist, ApproveCorrection))
                step a (TransitionCorrection(caChecklist, StartCorrection))
                step a (TransitionCorrection(caChecklist, MarkImplemented))
                step a (TransitionCorrection(caChecklist, StartVerificationOfEffect)) ]
            @ x.Admit "A11-C083"
            @ [ step a (RecordVerification(Id.create (g "verification" "ascent"), crAscent, Satisfied, x.Evs [ "A11-C083" ]))
                step a (TransitionCorrection(caSoftware, ApproveCorrection))
                step a (IdentifyUnknown(unknown "luminary-1b-effect", "Was the Luminary 1B change flown on later missions, and did it prevent radar-induced executive overflows?", Material))
                step a (RecordConclusion(Id.create (g "conclusion" "current"),
                                         "Apollo 11 succeeded. The five 1202/1201 alarms during powered descent were Executive overflows caused by rendezvous radar coupling data unit interrupts that stole more than 10 percent of the computer's capacity; they did not degrade guidance or control. The descent continued because restart protection kept the priority guidance running, controllers applied a pre-flight decision that such alarms did not stop the descent and gave Go, Armstrong flew P66 past a boulder field to a safe site inside the propellant margin, and abort capability remained available. The real-time ascent workaround was effective.",
                                         ConfirmedCause, [ cause ],
                                         Some "Who bears responsibility for the radar switch configuration is contested (crew procedure, agreed configuration, or interface documentation). The capacity figure differs by source (over 10, about 13, up to 15 percent). No single precise fuel-remaining figure is asserted; real-time indications were more critical than post-flight analysis. The flown effect of the Luminary 1B software correction is not in the retrieved Apollo 11 record."))
                step a (MarkAwaitingEvidence "The Luminary 1B software correction needs evidence from later missions, outside the retrieved Apollo 11 record; the ascent workaround is verified but is not the root-cause correction.") ] }

    let definition : CaseDefinition = { Key = "apollo-11"; Build = build }
