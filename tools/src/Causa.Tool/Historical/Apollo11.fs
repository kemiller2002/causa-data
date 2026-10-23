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
        let hStateVector, hFuelCritical = hyp "pdi-state-vector-error", hyp "fuel-critical-popular-framing"
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
                 "A11-C068"; "A11-C069"; "A11-C082"; "A11-C102"; "A11-C103"; "A11-C038"; "A11-C070"; "A11-C071"; "A11-C072"; "A11-C073"; "A11-C074"; "A11-C075" ]
               |> List.map x.Observe)
            // Why did the alarms occur?
            @ [ step a (RecordHypothesis(hRadar, "The 1202 and 1201 alarms were Executive overflows caused by counter interrupts from the rendezvous radar coupling data units, which stole more than 10 percent of the computer's capacity.",
                                         Some "With the rendezvous radar mode switch in AUTO or SLEW (the configuration the procedures called for; whether the procedure or the interface specification was at fault is contested), resolver excitation came from a 28-volt 800-hertz supply frequency-locked but not phase-locked to the guidance reference; the CDUs slewed and sent continuous pulses to the computer (A11-C031, A11-C032, A11-C033). Repetitive jobs were then scheduled again before earlier copies completed, overflowing the Executive (A11-C029, A11-C030, A11-C034): 1202 when no core sets and 1201 when no VAC areas were free, both Executive bailout alarms (A11-C118). The mechanism was established by post-flight analysis and simulation; on the surface it was a hedged working hypothesis (A11-C102)."))
                step a (RecordHypothesis(hMonitor, "In P63 the crew's Verb 16 Noun 68 monitoring display added computer load that reduced the remaining margin; in P64 the essential load plus landing-point redesignation processing left less than 10 percent margin.", None))
                step a (RecordHypothesis(hHardware, "The alarms indicated a failure of the lunar module guidance computer.", None))
                step a (RecordHypothesis(hChecklist, "The alarms were caused by the crew deviating from procedure in setting the rendezvous radar switch.", None))
                // Why did the descent continue to a safe landing?
                step a (RecordHypothesis(hRestart, "The computer's restart protection preserved guidance: each overflow triggered a software restart that discarded stale jobs and resumed the priority guidance and control computations.",
                                         Some "Mission Report: when capacity is exceeded the computer generates a software restart and an Executive overflow alarm (A11-C034); the alarms did not degrade any primary guidance or control function (A11-C035); participant analysis describes the restarts shedding load (A11-C046, A11-C047)."))
                step a (RecordHypothesis(hRules, "Flight controllers could judge in real time that the alarms were not a guidance failure, because it had been decided before flight that such alarms would not prevent continuing while they were intermittent and navigation was good; they gave explicit Go calls for the first 1202 and the 1201 and a general Go before the late 1202s.", None))
                step a (RecordHypothesis(hManual, "Armstrong's manual control in P66 extended the range past a boulder field around a large crater to a safe touchdown point.", None))
                step a (RecordHypothesis(hFuel, "Propellant monitoring and callouts kept the landing inside the margin: the engine was cut off with about 45 s of firing time remaining against a 20 s landing go/no-go point, and post-flight calculation (50 s) agreed with the onboard indication within measurement accuracy.", None))
                step a (RecordHypothesis(hAbort, "A backup abort guidance system ran in monitor mode through descent and mission rules defined when the primary guidance had failed, so continuing after the alarms kept an abort path, although abort options narrowed close to the surface.", None))
                step a (RecordHypothesis(hStateVector, "The landing came long of its target mainly because the onboard state vector carried a downrange error at powered descent initiation, with manual range extension adding to it.",
                                         Some "Doppler residuals showed the guidance estimate about 21,000 ft uprange; the dispersion was caused primarily by state-vector errors before PDI (A11-C010, A11-C011, A11-C052); manual control then extended range about 1100 ft (A11-C051)."))
                step a (RecordHypothesis(hFuelCritical, "Eagle landed with only seconds of usable propellant to spare (a common popular framing).", None))
                step a (RecordHypothesis(hAutomatic, "The automatic descent would have reached a safe landing without crew intervention.", None)) ]
            @ x.AdmitAllExcept [ "A11-C083"; "A11-C106" ]
            @ x.Supports hRadar "Mission Report anomaly analysis (post-flight analyses and simulations)." [ "A11-C028"; "A11-C029"; "A11-C030"; "A11-C031"; "A11-C032"; "A11-C033"; "A11-C034" ]
            @ x.Supports hRadar "The rendezvous radar switch was not in LGC during descent." [ "A11-C037"; "A11-C038" ]
            @ x.Supports hRadar "Participant-cited post-flight simulation reproduced similar alarms (secondary corroboration)." [ "A11-C119" ]
            @ x.Supports hRadar "Participant accounts agree on the radar-CDU mechanism while differing on the percentage." [ "A11-C044"; "A11-C041" ]
            @ x.Supports hMonitor "Aldrin correlated the second alarm with the Verb 16 Noun 68 display; margin fell to 10 percent or less with it; in P64 the essential load alone left under 10 percent." [ "A11-C019"; "A11-C045"; "A11-C047" ]
            @ x.Contradicts hHardware "Guidance and control performance was satisfactory; the alarms are known to be normal for the situation and indicative of proper computer performance." [ "A11-C028"; "A11-C035"; "A11-C024"; "A11-C107" ]
            @ x.Supports hChecklist "A later ALSJ contributor reports post-flight controversy over whether the switch setting was a mistake (secondary)." [ "A11-C043" ]
            @ x.Contradicts hChecklist "The switch was set as the procedures called for: Armstrong said the configuration was agreed and used in every simulation, Martin says the procedures book called for it; the Mission Report assigns no crew error; Eyles attributes the defect to the interface documentation." [ "A11-C037"; "A11-C042"; "A11-C036"; "A11-C040" ]
            @ x.Supports hRestart "Restart and overflow behaviour as documented, and the absence of guidance degradation." [ "A11-C034"; "A11-C035"; "A11-C028"; "A11-C107"; "A11-C046"; "A11-C047" ]
            @ [ x.Relate hRestart Insufficient "Two altitude-difference transients during the alarms are a small real effect qualifying 'not degraded'." "A11-C109" ]
            @ x.Supports hRules "Pre-flight decision on alarms, real-time Go calls and the mission rule defining a computer failure." [ "A11-C023"; "A11-C024"; "A11-C018"; "A11-C021"; "A11-C101"; "A11-C088"; "A11-C025" ]
            @ x.Supports hManual "Manual takeover, P66 and range extension past the boulder field." [ "A11-C049"; "A11-C050"; "A11-C051"; "A11-C078"; "A11-C113" ]
            @ x.Supports hStateVector "Mission Report navigation analysis of the landing dispersion." [ "A11-C010"; "A11-C011"; "A11-C052" ]
            @ x.Supports hFuel "Real-time propellant indications and post-flight propellant accounting." [ "A11-C054"; "A11-C055"; "A11-C056"; "A11-C057"; "A11-C058"; "A11-C059"; "A11-C060"; "A11-C061"; "A11-C114" ]
            @ x.Supports hFuelCritical "Real-time callouts and the crew's own sense of the abort limit." [ "A11-C055"; "A11-C056"; "A11-C065" ]
            @ x.Contradicts hFuelCritical "Engine cutoff came with about 45 s of firing time remaining, 25 s before the 20 s landing go/no-go point; post-flight calculation agrees within measurement accuracy." [ "A11-C058"; "A11-C059" ]
            @ x.Supports hAbort "Backup guidance in monitor mode and the mission-rule definition of a guidance failure." [ "A11-C110"; "A11-C088"; "A11-C087" ]
            @ [ x.Relate hAbort Insufficient "No descent-abort description was found in the retrieved press kit, and near touchdown Armstrong preferred a drop to an abort at that altitude: abort options narrowed." "A11-C112"
                x.Relate hAbort Insufficient "Absence finding: the retrieved press kit describes launch and other aborts, not powered-descent aborts." "A11-C111" ]
            @ x.Contradicts hAutomatic "The automatic descent was heading into a boulder field around a large crater." [ "A11-C049" ]
            @ [ step a (RecordPrediction(Id.create (g "prediction" "phase-reproduction"), hRadar,
                                         "If the radar coupling data units caused the overflows, reproducing the unsynchronized 800-hertz excitation in post-flight simulation reproduces the counter-interrupt load and the alarms.",
                                         DiscriminatingPrediction, Retrospective))
                step a (RecordAction(analysis, Test, "Post-flight analyses and simulations of the descent program alarms (Mission Report anomaly analysis).", [ hRadar; hHardware ]))
                step a (TransitionAction(analysis, ApproveAction))
                step a (TransitionAction(analysis, StartAction))
                step a (RecordFinding(Id.create (g "finding" "postflight-analysis"), analysis,
                                      "Apollo 11 Mission Report: analyses and simulations identified the cause of the multiple job scheduling as primarily counter interrupts from the rendezvous radar coupling data unit; more than 10 percent of computational capacity was preempted [A11-C029, A11-C031]. A participant-cited Grumman test reproduced similar alarms [A11-C119, secondary].",
                                      Some "The prediction is borne out: post-flight simulation reproduced the counter-interrupt load and the alarms. Supports the radar-CDU explanation and excludes a computer hardware failure."))
                step a (EstablishNegativeKnowledge(Id.create (g "negative" "no-guidance-degradation"), "The program alarms did not indicate a guidance computer failure and did not degrade any primary guidance or control function.",
                                                   "Apollo 11 powered descent, per the Mission Report", x.Evs [ "A11-C028"; "A11-C035"; "A11-C107" ]))
                tr hHardware Falsify "Guidance and control performance was satisfactory; the alarms were Executive overflows, not a computer failure." [ "A11-C028"; "A11-C035" ]
                tr hChecklist MarkPlausible "Post-flight controversy existed over whether the switch setting was a mistake." [ "A11-C043" ]
                tr hChecklist MarkWeakened "No source supports a crew deviation: the setting followed the agreed, simulated procedure; what is contested is whether the procedure or the interface specification was faulty." [ "A11-C037"; "A11-C042"; "A11-C036" ]
                tr hFuelCritical MarkPlausible "The 60- and 30-second callouts and the crew's sense of the abort limit made the fuel state appear critical in real time." [ "A11-C055"; "A11-C056"; "A11-C065" ]
                tr hFuelCritical MarkWeakened "The Mission Report places engine cutoff at about 45 s of firing time remaining, with the landing go/no-go point at 20 s; tight, but not seconds from depletion." [ "A11-C058"; "A11-C059" ]
                tr hAutomatic MarkPlausible "The automatic system was guiding the descent until manual takeover." [ "A11-C050" ]
                tr hAutomatic MarkWeakened "The automatic descent would have ended in a boulder field around a large crater; whether it would have been survivable is a counterfactual the record does not settle." [ "A11-C049"; "A11-C053" ]
                // Disagreements preserved.
                contra "load-percentage" "The share of computer capacity taken by the radar interrupts is given as more than 10 percent and 15 percent at maximum rate (Mission Report), about 13 percent (Eyles), and as the computer running at 80 percent speed (Martin)." [ "A11-C091"; "A11-C029"; "A11-C033"; "A11-C044" ] NonMaterial
                accept "load-percentage" "All accounts agree the stolen capacity exceeded the margin; the Mission Report figures are recorded as primary and the others as retrospective."
                contra "overflowing-jobs" "Accounts differ on which jobs overflowed: Adler describes repeated jobs processing non-existent radar data, while the Mission Report describes repetitive guidance jobs rescheduled before completion." [ "A11-C092"; "A11-C030" ] NonMaterial
                accept "overflowing-jobs" "The Mission Report description is primary; the secondary account is preserved."
                contra "blame-framing" "Responsibility is framed as a faulty procedure and training (Martin), as an agreed configuration used in every simulation (Armstrong), as faulty interface documentation (Eyles, who earlier called it a checklist error), and as a possible crew mistake (later ALSJ commentary); the Mission Report assigns no blame." [ "A11-C093"; "A11-C042"; "A11-C037"; "A11-C040"; "A11-C043"; "A11-C036" ] Material
                step a (TransitionContradiction(contradiction "blame-framing", TriageContradiction, "Material to whether the anomaly is classed as crew error; it does not change the physical mechanism."))
                accept "blame-framing" "The physical mechanism is agreed; attribution between procedure and interface specification is contested and is not decided here. The crew-deviation hypothesis is weakened, not falsified."
                contra "second-alarm-go" "Eyles writes that the ground said 'go' again after the second 1202 and called a go in each P64 case, but the 1969 transcript shows only 'Roger. Copy.' after the second 1202, an explicit Go for the 1201, a general Go before the late 1202s, and no acknowledgment of the last." [ "A11-C090"; "A11-C120"; "A11-C020"; "A11-C022"; "A11-C101" ] NonMaterial
                resolveC "second-alarm-go" "The contemporaneous transcript governs the wording; the retrospective account is not used for the exchange."
                contra "alarm-table" "ALSJ commentary gives 102:41:32 for the 1201 alarm, but Mission Report Table 5-I lists 102:41:32 as entry into P64 and the 1201 at 102:42:18." [ "A11-C089"; "A11-C014" ] Informational
                resolveC "alarm-table" "The Mission Report table governs; the commentary misread it."
                contra "ascent-fix-timing" "Martin recalls a radar-switch instruction as the last message just before ascent ignition; the transcript shows the checklist change at GET 121:41:37, about 2 h 40 min before lift-off; Eyles says only that the diagnosis reached MIT's people in Houston less than an hour before lift-off." [ "A11-C094"; "A11-C082"; "A11-C070"; "A11-C105" ] NonMaterial
                resolveC "ascent-fix-timing" "The transcript time governs; the recollections are preserved as retrospective."
                contra "simulation-familiarity" "Sources differ on prior familiarity with the alarms: ALSJ and Martin say the 1202 had not been seen in simulation or testing, while Kranz's account (relayed in ALSJ) describes a final simulation aborted for a 1201 alarm." [ "A11-C095"; "A11-C048"; "A11-C027" ] NonMaterial
                accept "simulation-familiarity" "Kranz's account is second-hand and not independently verified here; the pre-flight decision recorded in the Mission Report (A11-C023) is the established basis for the Go calls."
                contra "low-level-light" "The low-level light time is 102:44:28 (Table 5-I), 102:44:30.4 (descent propulsion section) and 102:44:31 (ALSJ)." [ "A11-C096"; "A11-C057"; "A11-C058"; "A11-C063" ] Informational
                accept "low-level-light" "A difference of seconds between tables and analyses; no single value is asserted."
                contra "fuel-framing" "Real-time callouts and the low-level light suggested a critical fuel state; the Mission Report found the onboard 45 s indication agreed with the post-flight 50 s within measurement accuracy; later ALSJ commentary attributes a larger apparent shortfall to slosh, which the Mission Report does not." [ "A11-C097"; "A11-C055"; "A11-C056"; "A11-C059"; "A11-C064"; "A11-C115"; "A11-C065" ] Material
                step a (TransitionContradiction(contradiction "fuel-framing", TriageContradiction, "Material to the propellant-margin hypothesis: what the crew knew differs from what post-flight analysis found."))
                accept "fuel-framing" "The crew and controllers acted on the real-time indication; the Mission Report confirms it within 5 s. The larger 'hidden margin' is later commentary and is recorded only as such. No single precise remaining-fuel figure is asserted."
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
                tr hMonitor MarkSupported "Aldrin correlated an alarm with the display, and the Mission Report planned a lower-cost descent monitoring change for later missions." [ "A11-C019"; "A11-C085" ]
                tr hRestart MarkPlausible "The computer generates a restart when capacity is exceeded." [ "A11-C034" ]
                tr hRestart MarkSupported "Guidance and control were not degraded through five alarms (apart from two small altitude-difference transients), and the alarms indicated proper computer performance." [ "A11-C035"; "A11-C028"; "A11-C107" ]
                tr hRules MarkPlausible "It had been decided before flight that such alarms would not prevent continuing." [ "A11-C023" ]
                tr hRules MarkSupported "The ground gave Go about half a minute after the first alarm and for the 1201, on the basis recorded in the Mission Report." [ "A11-C018"; "A11-C024"; "A11-C021"; "A11-C023" ]
                tr hManual MarkPlausible "Manual control was assumed to avoid a boulder field." [ "A11-C049" ]
                tr hManual MarkSupported "P66 was entered and range was extended past the boulder field; the report credits the skill of the landing." [ "A11-C050"; "A11-C051"; "A11-C078" ]
                tr hStateVector MarkPlausible "Doppler residuals showed the onboard estimate about 21,000 ft uprange at PDI." [ "A11-C010" ]
                tr hStateVector MarkSupported "The Mission Report attributes the landing dispersion primarily to state-vector errors before PDI." [ "A11-C052"; "A11-C011" ]
                tr hFuel MarkPlausible "Fuel callouts were made and acknowledged in real time." [ "A11-C054"; "A11-C055"; "A11-C056" ]
                tr hFuel MarkSupported "Engine cutoff at about 45 s of firing time remaining, 770 lb remaining against 957 lb planned, higher usage attributed to crater avoidance." [ "A11-C058"; "A11-C059"; "A11-C060"; "A11-C061" ]
                tr hAbort MarkPlausible "The abort guidance system ran in monitor mode during descent." [ "A11-C110" ]
                tr hAbort MarkSupported "Backup guidance in monitor mode and a mission-rule definition of guidance failure kept an abort path open; it narrowed near the surface." [ "A11-C110"; "A11-C088" ]
                step a (RecordCausalAssertion(cause, "Five executive-overflow program alarms occurred during Apollo 11's powered descent, and the descent nevertheless continued to a safe landing, long of its target",
                                              [ { Hypothesis = hRadar; Role = RootCause }
                                                { Hypothesis = hMonitor; Role = ContributingFactor }
                                                { Hypothesis = hRestart; Role = EnablingCondition }
                                                { Hypothesis = hRules; Role = ContributingFactor }
                                                { Hypothesis = hManual; Role = ContributingFactor }
                                                { Hypothesis = hFuel; Role = ContributingFactor }
                                                { Hypothesis = hStateVector; Role = ContributingFactor }
                                                { Hypothesis = hAbort; Role = EnablingCondition } ],
                                              Some "ANOMALY: counter interrupts from the rendezvous radar coupling data units, caused by unsynchronized 800-hertz excitation with the radar switch out of LGC, stole more than 10 percent of computer capacity (root cause of the alarms); monitoring displays reduced the remaining margin (contributing). CONTINUATION: restart protection kept the priority guidance computations running (enabling condition); controllers applied a pre-flight decision that such alarms did not stop the descent and gave Go; Armstrong flew P66 past a boulder field; propellant was monitored to touchdown inside the margin (contributing factors); backup abort guidance remained available though narrowing near the surface (enabling condition). LANDING POINT: a state-vector error at PDI put the descent long, toward the boulder field (contributing factor). The root-cause role refers to the alarms only; the other roles explain the continuation and the landing point. The single assertion combines the anomaly and its successful handling because the v1 cause gate does not allow a second, independent assertion beside it."))
                step a (ConfirmCause cause)
                // Corrective actions.
                step a (RecordCorrectiveAction(caChecklist, Workaround, "Real-time checklist package for lunar ascent, passed at GET 121:41:37 as a hedged working hypothesis: rendezvous radar in SLEW with circuit breakers off, and the mode-select change, to prevent computer overload.", [ cause ]))
                step a (RecordCorrectiveAction(caSoftware, Correction, "Luminary 1B for future missions: do not process rendezvous radar CDU counter interrupts when the radar is unpowered or not in LGC (zero the data units), and reduce the computer time needed to monitor the descent.", [ cause ]))
                step a (RecordCriterion(crAscent, caChecklist, "Lunar ascent with the revised checklist package completes without executive-overflow program alarms.", Some "Mission Report ascent and alarm records"))
                step a (TransitionCorrection(caChecklist, ApproveCorrection))
                step a (TransitionCorrection(caChecklist, StartCorrection))
                step a (TransitionCorrection(caChecklist, MarkImplemented))
                step a (TransitionCorrection(caChecklist, StartVerificationOfEffect)) ]
            @ x.Admit "A11-C083" @ x.Admit "A11-C106"
            @ [ step a (RecordVerification(Id.create (g "verification" "ascent"), crAscent, Satisfied, x.Evs [ "A11-C083"; "A11-C106" ]))
                step a (TransitionCorrection(caSoftware, ApproveCorrection))
                step a (IdentifyUnknown(unknown "luminary-1b-effect", "Was the Luminary 1B change flown on later missions, and did it prevent radar-induced executive overflows?", Material))
                step a (RecordConclusion(Id.create (g "conclusion" "current"),
                                         "Apollo 11 succeeded. The five 1202/1201 alarms during powered descent were Executive overflows caused by rendezvous radar coupling data unit interrupts that stole more than 10 percent of the computer's capacity; they did not degrade guidance or control. The descent continued because restart protection kept the priority guidance running, controllers applied a pre-flight decision that such intermittent alarms did not stop the descent and gave Go, Armstrong flew P66 past a boulder field (the descent had come in long because of a state-vector error at PDI) and cut off the engine with about 45 s of firing time remaining against a 20 s go/no-go point, and backup abort guidance remained available. Lunar ascent with the revised checklist package was alarm-free, consistent with the workaround. The causal roles apply to two effects: the root cause explains the alarms, the other members explain the continuation and the landing point.",
                                         ConfirmedCause, [ cause ],
                                         Some "Whether the procedure or the interface specification was at fault is contested; no source supports a crew deviation. The capacity figure differs by source (over 10, about 13, up to 15 percent). No single precise fuel-remaining figure is asserted; the Mission Report's 45 s / 50 s agreement is primary, the larger slosh margin is later commentary. The alarm-free ascent is consistent with the workaround but two changes were made together and it cannot show what would have happened without them. On the surface the radar cause was a hedged working hypothesis; the mechanism was established after the flight. Pairing of individual Go calls with alarms is partly uncertain. The flown effect of the Luminary 1B software correction is not in the retrieved Apollo 11 record."))
                step a (MarkAwaitingEvidence "The Luminary 1B software correction needs evidence from later missions, outside the retrieved Apollo 11 record; the ascent workaround is verified but is not the root-cause correction.") ] }

    let definition : CaseDefinition = { Key = "apollo-11"; Build = build }
