namespace Causa.Tool.Historical

open IndyInitRootCauseInvestigator.Engine
open Causa.Tool.Historical.Kit

/// Space Shuttle Challenger, STS-51-L, 28 January 1986. Research: research/historical/challenger-sts-51l.
/// The physical cause (the right SRM aft field joint, Ch. IV) and the organizational contributing cause (the launch
/// decision, Ch. V) are kept apart by hypothesis, evidence and causal role; see the note at the causal assertion.
module Challenger =
    /// Claims admitted only after the corrective action's verification round opens (freshness, Decide N2/M2).
    let verificationClaims = [ "CH-C080"; "CH-C081"; "CH-C082"; "CH-C101"; "CH-C102"; "CH-C103"; "CH-C104"; "CH-C105"; "CH-C106"; "CH-C107"; "CH-C108"; "CH-C109" ]

    let build (x: Ctx) : Case =
        let g = x.Guid
        let hyp name = Id.create<HypothesisTag> (g "hypothesis" name)
        let hJoint, hTrigger, hIce, hPadIce, hOther = hyp "joint-design", hyp "cold-o-ring-trigger", hyp "ice-in-clevis", hyp "pad-ice-debris", hyp "other-element-or-sabotage"
        let hDecision, hReversal, hContainment, hNormalization, hSafety, hSchedule =
            hyp "flawed-launch-decision", hyp "thiokol-reversal", hyp "internal-containment", hyp "normalized-risk", hyp "silent-safety-program", hyp "schedule-pressure"
        let physical = Id.create<CausalAssertionTag> (g "cause" "loss-of-challenger")
        let caJoint, caSafetyOffice, caLaunchProcess, caFlightRate =
            Id.create<CorrectiveActionTag> (g "corrective" "srm-joint-redesign"), Id.create<CorrectiveActionTag> (g "corrective" "srmqa-office"),
            Id.create<CorrectiveActionTag> (g "corrective" "launch-decision-process"), Id.create<CorrectiveActionTag> (g "corrective" "flight-rate")
        let crFirings, crFlight, crCold = Id.create<CriterionTag> (g "criterion" "full-scale-firings"), Id.create<CriterionTag> (g "criterion" "return-to-flight-joint"), Id.create<CriterionTag> (g "criterion" "cold-certification")
        let unknown name = Id.create<UnknownTag> (g "unknown" name)
        let contradiction name = Id.create<ContradictionTag> (g "contradiction" name)
        let action = Id.create<ActionTag> (g "action" "all-flights-temperature-comparison")
        let a = analyst
        let tr h cmd reason claims = step a (TransitionHypothesis(h, cmd, reason, x.Evs claims))
        let contra name statement claims materiality = step a (RecordContradiction(contradiction name, statement, x.Evs claims, materiality))
        let accept name basis = step a (TransitionContradiction(contradiction name, AcceptContradictionAsResidual, basis))
        let triage name basis = step a (TransitionContradiction(contradiction name, TriageContradiction, basis))
        let resolveC name basis = step a (TransitionContradiction(contradiction name, ResolveContradiction, basis))
        let inv = Id.create<InvestigationTag> (g "investigation" "root")
        { Key = x.Key
          Title = "Space Shuttle Challenger (STS-51-L) loss, 28 January 1986"
          Investigation = inv
          Start = utc (2026, 10, 23, 12, 0)
          ExpectedLifecycle = Resolved
          Research = x.Research
          Steps =
            [ step a (StartInvestigation(inv, "Space Shuttle Challenger (STS-51-L) loss, 28 January 1986", Id.create (g "observation" "initial"),
                                         "Historical investigation recorded from primary sources. Challenger mission 51-L began its flight at 11:38 a.m. EST on 28 January 1986; the flight ended 73 seconds after launch when the External Tank was destroyed and the Orbiter broke up [CH-C001, CH-C002]. Scope: the physical failure and the organizational decision to launch, kept separate as the Rogers Commission keeps them."))
              step a ActivateInvestigation ]
            // Chronology of the launch decision and of the flight, as the sources time it.
            @ ([ "CH-C030"; "CH-C031"; "CH-C032"; "CH-C033"; "CH-C034"; "CH-C037"; "CH-C045"; "CH-C038"; "CH-C046"; "CH-C047"; "CH-C004"
                 "CH-C001"; "CH-C003"; "CH-C005"; "CH-C007"; "CH-C008"; "CH-C009"; "CH-C010"; "CH-C011"; "CH-C012"; "CH-C013"; "CH-C002" ]
               |> List.map x.Observe)
            // Physical hypotheses and the alternatives the Commission excluded.
            @ [ step a (RecordHypothesis(hJoint, "The faulty design of the right Solid Rocket Motor aft field joint caused the pressure seal to fail, letting combustion gas leak and penetrate the External Tank.",
                                         Some "The field joint design was unacceptably sensitive to temperature, physical dimensions, character of materials, reusability, processing and the reaction of the joint to dynamic loading (CH-C016); a leak initiated at or shortly after ignition (CH-C017), reappeared as flame at about 58 s (CH-C026) and impinged on the External Tank (CH-C009, CH-C010)."))
                step a (RecordHypothesis(hTrigger, "Cold O-rings at ignition (estimated 28 +/- 5 degrees F at the coldest point of the joint) did not follow the opening gap quickly enough to seal, so blow-by and erosion began at ignition.",
                                         Some "O-ring resiliency falls with temperature (CH-C021); at the launch temperature the O-ring would probably not be pressure-actuated in time to preclude blow-by (CH-C022); smoke appeared at 0.678 s (CH-C005)."))
                step a (RecordHypothesis(hIce, "Water that froze in the right aft field joint clevis inhibited the secondary seal.", None))
                step a (RecordHypothesis(hPadIce, "Ice on the launch pad or vehicle struck the Orbiter or SRB and caused the failure.", None))
                step a (RecordHypothesis(hOther, "Another Shuttle element, the payload, or sabotage caused or contributed to the joint leak.", None))
                // Organizational hypotheses (the Commission's 'contributing cause').
                step a (RecordHypothesis(hDecision, "The launch decision process was flawed: those who decided to launch did not know the O-ring history, Thiokol's initial written recommendation against launch, or the continuing opposition of Thiokol engineers.",
                                         Some "Commission Ch. V finding and Finding 1 (CH-C027, CH-C040)."))
                step a (RecordHypothesis(hReversal, "Thiokol management reversed its engineers' no-launch recommendation at the urging of Marshall to accommodate a major customer.", None))
                step a (RecordHypothesis(hContainment, "Marshall management contained the O-ring concern within the project instead of communicating it to Levels I and II.", None))
                step a (RecordHypothesis(hNormalization, "NASA and Thiokol had normalized O-ring erosion and blow-by, accepting escalating risk through waived launch constraints and flight-readiness rationales.", None))
                step a (RecordHypothesis(hSafety, "The safety, reliability and quality assurance organization was silent: absent from the launch-decision forums and doing little or no trend analysis of O-ring distress.", None))
                step a (RecordHypothesis(hSchedule, "Pressure to sustain an ambitious flight rate left no margin for unforeseen hardware problems.", None)) ]
            @ x.AdmitAllExcept verificationClaims
            // Physical evidence.
            @ x.Supports hJoint "Commission conclusion on the physical cause." [ "CH-C014"; "CH-C015"; "CH-C016"; "CH-C017"; "CH-C083" ]
            @ x.Supports hJoint "Photographic and telemetry sequence locating the leak at the right SRB aft field joint." [ "CH-C005"; "CH-C006"; "CH-C008"; "CH-C009"; "CH-C010"; "CH-C026" ]
            @ x.Supports hJoint "Joint dimensions from segment reuse are one of the design sensitivities the Commission names." [ "CH-C019"; "CH-C058" ]
            @ x.Supports hTrigger "Temperature and resiliency findings for the 51-L joint." [ "CH-C020"; "CH-C021"; "CH-C022"; "CH-C023"; "CH-C004" ]
            @ x.Supports hTrigger "Flight history: every launch below 61 degrees F showed O-ring distress." [ "CH-C024"; "CH-C052"; "CH-C056" ]
            @ x.Supports hIce "The Commission states water may have been in the clevis and could inhibit the secondary seal." [ "CH-C025" ]
            @ x.Contradicts hPadIce "The Commission found the ice was not a cause of the accident." [ "CH-C049" ]
            @ x.Contradicts hOther "The Commission found no other element or the payload contributed and sabotage was not a factor." [ "CH-C018" ]
            // Organizational evidence.
            @ x.Supports hDecision "Commission findings on the decision to launch." [ "CH-C027"; "CH-C028"; "CH-C040"; "CH-C029"; "CH-C033" ]
            @ x.Supports hReversal "Commission chronology and finding on the Thiokol reversal." [ "CH-C034"; "CH-C035"; "CH-C036"; "CH-C037"; "CH-C039" ]
            @ x.Contradicts hReversal "Mulloy denied directing Thiokol to reconsider its data." [ "CH-C086" ]
            @ x.Supports hContainment "Findings and testimony that Levels I and II were not told of the objections or the launch constraint." [ "CH-C042"; "CH-C043"; "CH-C044"; "CH-C045"; "CH-C046"; "CH-C029" ]
            @ x.Contradicts hContainment "Mulloy testified that the O-ring concern was known through the flight readiness record; the House Committee found no evidence that informing Levels I and II would have changed the outcome." [ "CH-C091"; "CH-C093" ]
            @ x.Supports hNormalization "History of accepted erosion, waived constraints and unheeded warnings." [ "CH-C041"; "CH-C051"; "CH-C053"; "CH-C054"; "CH-C055"; "CH-C057"; "CH-C072"; "CH-C084" ]
            @ x.Supports hSafety "Silent safety program findings." [ "CH-C059"; "CH-C060"; "CH-C071" ]
            @ x.Supports hSchedule "Flight-rate finding." [ "CH-C061" ]
            // A retrospective comparison the Commission performed, recorded as an investigative action with its finding.
            @ [ step a (RecordPrediction(Id.create (g "prediction" "cold-distress"), hTrigger,
                                         "If cold reduces the joint's ability to seal, O-ring distress should be more frequent on cold launches than on warm ones when all flights, not only flights with distress, are compared.",
                                         DiscriminatingPrediction, Retrospective))
                step a (RecordAction(action, Comparison, "Compare O-ring thermal distress against joint temperature across all prior flights, including flights without distress.", [ hTrigger ]))
                step a (TransitionAction(action, ApproveAction))
                step a (TransitionAction(action, StartAction))
                step a (RecordFinding(Id.create (g "finding" "all-flights"), action,
                                      "Rogers Commission (Ch. IV Finding 12; Ch. VI): of 21 launches at 61 degrees F or above only four showed O-ring thermal distress, and every launch below 61 degrees F showed distress; managers on 27 January compared only the flights with distress [CH-C024, CH-C056].",
                                      Some "The comparison the launch-decision meeting did not make supports the cold-temperature trigger."))
                step a (EstablishNegativeKnowledge(Id.create (g "negative" "no-other-element"), "No other STS-51-L element or the payload contributed to the joint leak, and sabotage was not a factor.",
                                                   "STS-51-L, as found by the Rogers Commission (Ch. IV Finding 2)", x.Evs [ "CH-C018" ]))
                step a (EstablishNegativeKnowledge(Id.create (g "negative" "pad-ice"), "Ice on the launch pad was not a cause of the accident (although the decision to launch with it was questionable).",
                                                   "STS-51-L, as found by the Rogers Commission (Ch. V ice finding 2)", x.Evs [ "CH-C049" ]))
                tr hOther Falsify "The Commission found no other element or the payload contributed and sabotage was not a factor." [ "CH-C018" ]
                tr hPadIce Falsify "The Commission found pad ice was not a cause of the accident." [ "CH-C049" ]
                tr hIce MarkPlausible "The Commission states only that water may have been in the clevis." [ "CH-C025" ]
                tr hIce MarkWeakened "Possible, not established: the Commission's finding is conditional ('may have been'), so ice is not part of the established cause." [ "CH-C025" ]
                tr hJoint MarkPlausible "Photographic data place the first smoke at the right SRB aft field joint at 0.678 s." [ "CH-C005"; "CH-C006" ]
                tr hJoint MarkSupported "The Commission and the House Committee both conclude the aft field joint seal of the right SRM failed because of a faulty design." [ "CH-C014"; "CH-C015"; "CH-C016"; "CH-C017"; "CH-C083" ]
                tr hTrigger MarkPlausible "Launch ambient temperature was 15 degrees F colder than any previous launch." [ "CH-C004" ]
                tr hTrigger MarkSupported "Resiliency tests, the joint temperature estimate and the all-flights distress comparison agree." [ "CH-C020"; "CH-C021"; "CH-C022"; "CH-C024" ]
                // Disagreements are preserved, not rewritten into one narrative.
                contra "joint-temperature" "The 51-L joint temperature differs by source and by what is measured: 28 +/- 5 degrees F modelled at the coldest point, 31 degrees F in the Figure 2 table, about 8 degrees F from a pyrometer on the right SRB surface, and 36 degrees F ambient."
                    [ "CH-C020"; "CH-C096"; "CH-C047"; "CH-C004" ] NonMaterial
                accept "joint-temperature" "Every value is far below the 53 degrees F of the coldest previous flight; the causal conclusion does not depend on which estimate is used. The values measure different things and are kept as stated."
                contra "puff-frequency" "The Rogers report gives the smoke puff frequency as about four per second (Ch. III) and about three per second (Ch. IV), and gives slightly different smoke sectors and flame angles." [ "CH-C095"; "CH-C007"; "CH-C026" ] Informational
                accept "puff-frequency" "An internal inconsistency of the report in descriptive detail; it does not bear on the cause."
                contra "secondary-simplification" "A secondary NASA history article says the O-rings 'became brittle' and the External Tank 'exploded'; the Commission's findings describe loss of resiliency and a structural failure with an explosive burn of propellants."
                    [ "CH-C098"; "CH-C015"; "CH-C002" ] Informational
                resolveC "secondary-simplification" "The primary Commission text governs; the secondary wording is a popular simplification and is not used as a finding."
                contra "mulloy-pressure" "Mulloy denied pressing Thiokol to reconsider its data, while the Commission found Thiokol reversed its position at the urging of Marshall." [ "CH-C086"; "CH-C039" ] Material
                triage "mulloy-pressure" "Material to the reversal hypothesis: testimony of a participant conflicts with a Commission finding."
                accept "mulloy-pressure" "The Commission's finding rests on the full record and stands; the conflicting testimony is preserved as residual disagreement about intent and tone, not about the reversal itself."
                contra "information-flow" "Mulloy testified that 'everyone' knew of the O-ring concern via the flight readiness record; the Commission found its seriousness was not communicated to Levels I and II." [ "CH-C091"; "CH-C042"; "CH-C044" ] Material
                accept "information-flow" "Senior officials testified they did not know of Thiokol's objections before launch (CH-C044); the record shows a concern existed but its seriousness and the 27 January objection were not passed on."
                contra "counterfactual" "The Rogers Commission judged that had decision makers known all the facts it is highly unlikely they would have launched; the House Committee found no evidence the outcome would have differed had Levels I and II been informed."
                    [ "CH-C028"; "CH-C093" ] Material
                triage "counterfactual" "Material to the weight of the communication factor, not to the physical cause."
                accept "counterfactual" "The two bodies disagree on a counterfactual that no evidence can settle; both agree the decision process was flawed (CH-C040, CH-C084). Preserved as residual uncertainty."
                contra "thiokol-responsibility" "The House Committee placed weight on Thiokol's failure over many flight readiness reviews to establish a minimum temperature, and on years of poor technical decision-making, rather than on the 27 January communication failure."
                    [ "CH-C094"; "CH-C084"; "CH-C042" ] Material
                accept "thiokol-responsibility" "A difference of emphasis between the two investigating bodies; it is reflected by including normalized risk as an enabling condition rather than choosing one body's weighting."
                contra "teleconference-accounts" "Participants' accounts of the 27 January teleconferences conflict: whether Thiokol recommended delay at 5:45 p.m., Hardy's 'appalled' remark, the 'next April' remark, the tone of the Thiokol caucus and McDonald's interjection."
                    [ "CH-C085"; "CH-C087"; "CH-C088"; "CH-C089"; "CH-C090" ] NonMaterial
                accept "teleconference-accounts" "Recollections given months later; none of them changes the documented sequence of the engineering recommendation, the caucus and the reversal."
                contra "rockwell-ice" "Petrone testified Rockwell concluded it was unsafe to fly because of ice; Lamberth did not interpret Rockwell's position as a no-launch recommendation." [ "CH-C092"; "CH-C048" ] Informational
                accept "rockwell-ice" "The Commission found the Rockwell recommendation ambiguous and the ice not causal; it is outside the causal chain."
                step a (IdentifyUnknown(unknown "reseal", "Was the joint leaking continuously between about 3 s and 58 s, or did oxide deposits reseal it until wind-shear and steering loads disturbed the seal?", NonMaterial))
                step a (TransitionUnknown(unknown "reseal", AcceptUnknownAsResidual, "The Commission leaves the mechanism of the delay open (CH-C026); either path ends in the same joint failure."))
                step a (IdentifyUnknown(unknown "overnight-low", "What was the measured overnight minimum temperature at the pad before launch?", Informational))
                step a (TransitionUnknown(unknown "overnight-low", AcceptUnknownAsResidual, "Only forecasts were found in the retrieved report text (CH-C097)."))
                step a (IdentifyUnknown(unknown "final-telefax", "What is the exact text of Thiokol's final written launch recommendation (Rogers Figure 5.4)?", Informational))
                step a (TransitionUnknown(unknown "final-telefax", AcceptUnknownAsResidual, "The figure is reproduced only as an image and its text was not verified; its existence and timing are documented (CH-C038)."))
                step a (RecordAssumption(Id.create (g "assumption" "gmt-table"), "The Commission's GMT event table (General Purpose Computer time) is the reference for flight event times.", Material))
                step a (TransitionAssumption(Id.create (g "assumption" "gmt-table"), ProvisionallyAcceptAssumption))
                // Organizational hypotheses through the evidence.
                tr hDecision MarkPlausible "Decision makers were unaware of the O-ring history and of the engineers' opposition." [ "CH-C027" ]
                tr hDecision MarkSupported "The Commission found a serious flaw in the decision-making process; the House Committee also found poor technical decision-making." [ "CH-C040"; "CH-C029"; "CH-C084" ]
                tr hReversal MarkPlausible "Thiokol engineers recommended no launch below 53 degrees F; management reversed after a caucus." [ "CH-C033"; "CH-C037" ]
                tr hReversal MarkSupported "Commission Finding 4: the reversal was at Marshall's urging and contrary to the engineers." [ "CH-C039"; "CH-C035" ]
                tr hContainment MarkPlausible "Levels I and II did not know of the launch constraint or its waivers." [ "CH-C029" ]
                tr hContainment MarkSupported "Commission Finding 3 and testimony that senior officials did not know of the objections." [ "CH-C042"; "CH-C043"; "CH-C044" ]
                tr hNormalization MarkPlausible "Erosion was accepted as acceptable risk after 51-C." [ "CH-C053" ]
                tr hNormalization MarkSupported "Waivers, accepted erosion and unheeded warnings over the preceding year." [ "CH-C041"; "CH-C051"; "CH-C055"; "CH-C057" ]
                tr hSafety MarkPlausible "No safety representative was at the teleconference or on the Mission Management Team." [ "CH-C059" ]
                tr hSafety MarkSupported "Little or no trend analysis of O-ring erosion and blow-by was performed." [ "CH-C060"; "CH-C059" ]
                tr hSchedule MarkPlausible "The scheduled flight rate left no margin for unforeseen hardware problems." [ "CH-C061" ]
                tr hSchedule MarkSupported "Commission Ch. VIII finding 4." [ "CH-C061" ]
                // One confirmed causal assertion. The v1 engine's cause gate treats every supported hypothesis outside an
                // assertion as an unaddressed alternative (Decide §15.7), so an independent organizational assertion
                // cannot be confirmed beside the physical one. The separation is carried by the member roles: the
                // physical failure is the root cause and trigger; the decision factors are contributing factors and
                // enabling conditions, which is how the Commission ranks them ("the contributing cause").
                step a (RecordCausalAssertion(physical, "Loss of Challenger 73 seconds after launch on 28 January 1986",
                                              [ { Hypothesis = hJoint; Role = RootCause }
                                                { Hypothesis = hTrigger; Role = Trigger }
                                                { Hypothesis = hDecision; Role = ContributingFactor }
                                                { Hypothesis = hReversal; Role = ContributingFactor }
                                                { Hypothesis = hContainment; Role = ContributingFactor }
                                                { Hypothesis = hSafety; Role = ContributingFactor }
                                                { Hypothesis = hNormalization; Role = EnablingCondition }
                                                { Hypothesis = hSchedule; Role = EnablingCondition } ],
                                              Some "PHYSICAL MECHANISM: a field joint design unacceptably sensitive to temperature, dimensions, materials, reuse, processing and joint rotation (root cause) failed to seal at ignition when cold O-rings could not follow the opening gap (trigger); hot gas leaked, reappeared as a plume at about 58 s, breached the External Tank and led to the structural breakup at about 73 s. ORGANIZATIONAL CONTRIBUTING CAUSE (not part of the physical mechanism): the decision to launch was flawed; Thiokol management reversed its engineers' recommendation at Marshall's urging, the concern was contained below Levels I and II and safety was absent (contributing factors), enabled by a history of accepted erosion, waived launch constraints and schedule pressure (enabling conditions)."))
                step a (ConfirmCause physical)
                // Corrective actions.
                step a (RecordCorrectiveAction(caJoint, Correction, "Redesign the Solid Rocket Motor field joint (Commission Recommendation I): tang capture feature, third O-ring, J-seal insulation and joint heaters that keep seal temperatures at or above 75 degrees F; test and certify it full scale.", [ physical ]))
                step a (RecordCorrectiveAction(caSafetyOffice, Prevention, "Establish an independent Office of Safety, Reliability, Maintainability and Quality Assurance under an Associate Administrator (Recommendation IV).", [ physical ]))
                step a (RecordCorrectiveAction(caLaunchProcess, Prevention, "Strengthen flight readiness and launch-decision communication: recorded FRR and Mission Management Team meetings, crew commander attendance, launch constraints approved and waived only by the Director, NSTS (Recommendation V).", [ physical ]))
                step a (RecordCorrectiveAction(caFlightRate, Prevention, "Set a flight rate consistent with resources and avoid reliance on a single launch capability (Recommendation VIII).", [ physical ]))
                step a (RecordCriterion(crFirings, caJoint, "The redesigned motor completes full-scale, full-duration static test firings before the next flight.", Some "Full-scale static firings reported in NASA return-to-flight documentation"))
                step a (RecordCriterion(crFlight, caJoint, "On the return-to-flight mission the redesigned field joints seal without blow-by or erosion, as shown by a primary post-flight inspection record.", Some "Primary post-flight SRM inspection record for STS-26"))
                step a (RecordCriterion(crCold, caJoint, "The redesigned motor is certified by a full-scale firing under extreme cold conditions before any cold-weather launch.", Some "Cold-conditioned full-scale qualification firing (QM-8)"))
                step a (TransitionCorrection(caJoint, ApproveCorrection))
                step a (TransitionCorrection(caJoint, StartCorrection))
                step a (TransitionCorrection(caJoint, MarkImplemented))
                step a (TransitionCorrection(caSafetyOffice, ApproveCorrection))
                step a (TransitionCorrection(caSafetyOffice, StartCorrection))
                step a (TransitionCorrection(caSafetyOffice, MarkImplemented))
                step a (TransitionCorrection(caLaunchProcess, ApproveCorrection))
                step a (TransitionCorrection(caLaunchProcess, StartCorrection))
                step a (TransitionCorrection(caLaunchProcess, MarkImplemented))
                step a (TransitionCorrection(caFlightRate, ApproveCorrection))
                step a (TransitionCorrection(caJoint, StartVerificationOfEffect))
                step a MarkAwaitingVerification
                // Implementation is not verification (R7.4): resolving before the evidence of effect is refused.
                refuse "RESOLVE.CORRECTION_NOT_VERIFIED" a ResolveInvestigation ]
            @ (verificationClaims |> List.collect x.Admit)
            @ [ step a (RecordVerification(Id.create (g "verification" "firings"), crFirings, Satisfied, x.Evs [ "CH-C080" ]))
                step a (RecordVerification(Id.create (g "verification" "return-to-flight"), crFlight, Satisfied, x.Evs [ "CH-C101"; "CH-C102"; "CH-C103"; "CH-C104" ]))
                // Cold certification was still outstanding when STS-26 flew; QM-8 (20 January 1989, 28 degrees F) closed it.
                step a (RecordVerification(Id.create (g "verification" "cold-certification"), crCold, Satisfied, x.Evs [ "CH-C108"; "CH-C109" ]))
                step a (RecordConclusion(Id.create (g "conclusion" "final"),
                                         "Challenger was lost because the pressure seal in the aft field joint of the right Solid Rocket Motor failed at ignition: a joint design unacceptably sensitive to temperature and other factors could not seal with O-rings at about 28 degrees F. Separately, and as a contributing cause rather than part of the physical mechanism, the decision to launch was flawed: Thiokol's engineering recommendation against launching below 53 degrees F was reversed by management at Marshall's urging and was not communicated to Levels I and II, within a program that had normalized O-ring erosion. The redesigned field joint passed full-scale static firings, sealed without erosion or blow-by on STS-26, and was certified for extreme cold by QM-8.",
                                         ConfirmedCause, [ physical ],
                                         Some "Accepted residuals: the exact joint temperature (estimates of 28 +/- 5, 31 and about 8 degrees F measure different things); whether the leak was continuous or resealed between 3 and 58 s; whether informing Levels I and II would have changed the outcome (Rogers and House disagree); conflicting participant testimony about the teleconference and Marshall's pressure. Verification of the joint redesign rests on one flight (STS-26) plus ground firings; STS-26 also showed water intrusion through vent valves at splashdown (STS-26-M-2), a post-flight condition. The organizational preventions are recorded as implemented, not as verified effective."))
                step a ResolveInvestigation ] }

    let definition : CaseDefinition = { Key = "challenger-sts-51l"; Build = build }
