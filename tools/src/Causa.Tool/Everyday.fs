namespace Causa.Tool

open System
open System.IO
open IndyInitRootCauseInvestigator.Engine
open Causa.Contract

/// Deterministic, engine-authored everyday root-cause demonstrations.
[<RequireQualifiedAccess>]
module Everyday =
    let templateId = "everyday-v1"
    let combinedTemplateId = "demonstration-v1"
    let caseKey = "burned-out-lightbulb"

    let private idNamespace =
        Uuid.v5 Uuid.urlNamespace "https://github.com/kemiller2002/causa-data/templates/everyday-v1"

    let guid kind name = Uuid.v5 idNamespace ($"{caseKey}/{kind}/{name}")
    let investigationId = Uuid.text (guid "investigation" "root")

    let private actor = Actor.human "everyday-demonstration-investigator"
    let private step command : Step = { Actor = actor; Command = command; Expect = Accept }
    let private refuse code command : Step = { Actor = actor; Command = command; Expect = Refuse code }
    let private id<'T> kind name : Id<'T> = Id.create (guid kind name)
    let private at = DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero)

    type Outcome =
        | Illuminates
        | RemainsDark

    let scenario outcome : Scenario =
        let inv = id<InvestigationTag> "investigation" "root"
        let hypothesis name = id<HypothesisTag> "hypothesis" name
        let evidence name = id<EvidenceTag> "evidence" name
        let relationship name = id<RelationshipTag> "relationship" name
        let hBulb = hypothesis "bulb-failed"
        let hSwitch = hypothesis "wall-switch-defective"
        let hPower = hypothesis "fixture-power-unavailable"
        let hFixture = hypothesis "fixture-defective"
        let eDark = evidence "does-not-illuminate"
        let eOtherLights = evidence "other-lights-operate"
        let eSwitch = evidence "switch-operated"
        let eRemoved = evidence "original-bulb-removed-power-off"
        let eReplacement = evidence "known-good-replacement-installed"
        let eResult = evidence (match outcome with Illuminates -> "illuminates-after-replacement" | RemainsDark -> "remains-dark-after-replacement")
        let cause = id<CausalAssertionTag> "cause" "failed-original-bulb"
        let correction = id<CorrectiveActionTag> "corrective-action" "replace-failed-bulb"
        let criterion = id<CriterionTag> "criterion" "post-replacement-illumination"
        let common =
            [ step (StartInvestigation(inv, "Burned-out ceiling lightbulb", id<ObservationTag> "observation" "initial",
                                      "The ceiling light does not illuminate when the wall switch is turned on."))
              step ActivateInvestigation
              step (RecordHypothesis(hBulb, "The bulb has failed.", Some "The original bulb no longer produces light; its exact internal failure mechanism is not known."))
              step (RecordHypothesis(hSwitch, "The wall switch is defective.", None))
              step (RecordHypothesis(hPower, "Power is unavailable to the fixture.", None))
              step (RecordHypothesis(hFixture, "The fixture itself is defective.", None))
              step (TransitionHypothesis(hBulb, MarkPlausible, "A failed bulb is consistent with the initial observation.", []))
              step (TransitionHypothesis(hSwitch, MarkPlausible, "A defective switch is consistent with the initial observation.", []))
              step (TransitionHypothesis(hPower, MarkPlausible, "Unavailable fixture power is consistent with the initial observation.", []))
              step (TransitionHypothesis(hFixture, MarkPlausible, "A defective fixture is consistent with the initial observation.", []))
              step (AdmitEvidence(eDark, TextInput "Observed: the ceiling light does not illuminate when the wall switch is turned on.", DirectObservation,
                                  "controlled household demonstration", Some(at.AddMinutes -10.0)))
              step (VerifyEvidence(eDark, Verified))
              step (AdmitEvidence(eOtherLights, TextInput "Observed: other lights in the room operate normally.", DirectObservation,
                                  "controlled household demonstration", Some(at.AddMinutes -8.0)))
              step (VerifyEvidence(eOtherLights, Verified))
              step (AdmitEvidence(eSwitch, TextInput "Observed: the wall switch is moved to its on position and the ceiling light remains dark.", DirectObservation,
                                  "controlled household demonstration", Some(at.AddMinutes -6.0)))
              step (VerifyEvidence(eSwitch, Verified))
              step (RelateEvidence(relationship "dark-supports-bulb", eDark, hBulb, Supports, Some "A failed bulb can account for the observed darkness."))
              step (RelateEvidence(relationship "dark-supports-switch", eDark, hSwitch, Supports, Some "The initial observation alone does not exclude a switch fault."))
              step (RelateEvidence(relationship "dark-supports-power", eDark, hPower, Supports, Some "The initial observation alone does not exclude loss of power at the fixture."))
              step (RelateEvidence(relationship "dark-supports-fixture", eDark, hFixture, Supports, Some "The initial observation alone does not exclude a fixture fault."))
              step (RelateEvidence(relationship "other-lights-contradict-room-power", eOtherLights, hPower, Contradicts,
                                   Some "Normal operation elsewhere makes a room-wide power loss less likely, but does not prove power is present at this fixture."))
              step (RecordCausalAssertion(cause, "The original bulb failed to illuminate the ceiling light.", [ { Hypothesis = hBulb; Role = RootCause } ],
                                          Some "Replacing only the bulb with a known-good compatible bulb discriminates bulb failure from the switch, power and fixture alternatives; the original bulb's internal failure mechanism is not asserted."))
              step (RecordCorrectiveAction(correction, Correction, "Replace the failed bulb with a compatible replacement.", [ cause ]))
              step (RecordCriterion(criterion, correction, "The ceiling light illuminates when the wall switch is turned on after the compatible replacement bulb is installed.",
                                    Some "Post-replacement functional illumination test"))
              step (TransitionCorrection(correction, ApproveCorrection))
              step (TransitionCorrection(correction, StartCorrection))
              step (AdmitEvidence(eRemoved, TextInput "Observed: after circuit power was switched off, the original bulb was removed.", DirectObservation,
                                  "controlled household demonstration; no live electrical work", Some(at.AddMinutes 4.0)))
              step (VerifyEvidence(eRemoved, Verified))
              step (AdmitEvidence(eReplacement, TextInput "Observed: a known-good, compatible replacement bulb was installed while circuit power remained off.", DirectObservation,
                                  "controlled household demonstration; no live electrical work", Some(at.AddMinutes 6.0)))
              step (VerifyEvidence(eReplacement, Verified))
              step (TransitionCorrection(correction, MarkImplemented))
              step (TransitionCorrection(correction, StartVerificationOfEffect))
              step MarkAwaitingVerification ]
        let result =
            match outcome with
            | Illuminates ->
                [ step (AdmitEvidence(eResult, TextInput "Observed: after power was restored and the wall switch was turned on, the ceiling light illuminated with the replacement bulb.",
                                      ExperimentResult, "post-replacement functional test", Some(at.AddMinutes 12.0)))
                  step (VerifyEvidence(eResult, Verified))
                  step (RelateEvidence(relationship "replacement-supports-bulb", eResult, hBulb, Supports,
                                       Some "Changing only the bulb restored illumination, supporting failure of the original bulb."))
                  step (RelateEvidence(relationship "replacement-contradicts-switch", eResult, hSwitch, Contradicts,
                                       Some "The unchanged wall switch successfully controls the replacement bulb."))
                  step (RelateEvidence(relationship "replacement-contradicts-power", eResult, hPower, Contradicts,
                                       Some "The fixture received enough power to illuminate the replacement bulb."))
                  step (RelateEvidence(relationship "replacement-contradicts-fixture", eResult, hFixture, Contradicts,
                                       Some "The unchanged fixture illuminates the replacement bulb."))
                  step (TransitionHypothesis(hBulb, MarkSupported, "A known-good compatible replacement illuminates in the unchanged circuit and fixture.", [ eReplacement; eResult ]))
                  step (TransitionHypothesis(hSwitch, MarkWeakened, "The unchanged wall switch controls the working replacement bulb.", [ eResult ]))
                  step (TransitionHypothesis(hPower, MarkWeakened, "The replacement bulb illuminates, so power was available during the verification test.", [ eResult ]))
                  step (TransitionHypothesis(hFixture, MarkWeakened, "The unchanged fixture illuminates the replacement bulb.", [ eResult ]))
                  step (ConfirmCause cause)
                  step (RecordVerification(id<VerificationTag> "verification" "post-replacement-illumination", criterion, Satisfied, [ eResult ]))
                  step (RecordConclusion(id<ConclusionTag> "conclusion" "resolved", "The available evidence supports failure of the original bulb as the root cause: a known-good compatible replacement illuminated in the unchanged switch and fixture.",
                                         ConfirmedCause, [ cause ], Some "The exact internal failure mechanism of the original bulb was not determined."))
                  step ResolveInvestigation ]
            | RemainsDark ->
                [ step (AdmitEvidence(eResult, TextInput "Observed: after power was restored and the wall switch was turned on, the ceiling light remained dark with the known-good replacement bulb.",
                                      ExperimentResult, "post-replacement functional test", Some(at.AddMinutes 12.0)))
                  step (VerifyEvidence(eResult, Verified))
                  step (RelateEvidence(relationship "replacement-failure-contradicts-bulb", eResult, hBulb, Contradicts,
                                       Some "Replacing the bulb did not restore illumination, so bulb failure alone is not supported as the resolution."))
                  step (TransitionHypothesis(hBulb, MarkWeakened, "A known-good replacement also remains dark.", [ eResult ]))
                  step (RecordVerification(id<VerificationTag> "verification" "post-replacement-no-illumination", criterion, Failed, [ eResult ]))
                  refuse "CAUSE.MEMBER_NOT_SUPPORTED" (ConfirmCause cause) ]
        { Key = caseKey + (match outcome with Illuminates -> "" | RemainsDark -> "-replacement-fails")
          Title = "Burned-out ceiling lightbulb"
          Investigation = inv
          Start = at
          ExpectedLifecycle = (match outcome with Illuminates -> Resolved | RemainsDark -> Active)
          Steps = common @ result }

    type Built =
        { Run: Runner.Run
          Files: InvestigationFiles
          Loaded: Loaded }

    let build (schemas: SchemaCheck.SchemaSet) outcome =
        Runner.run (scenario outcome)
        |> Result.bind (fun run ->
            Store.build schemas run.Events
            |> Result.mapError (fun error -> $"{error.Code}: {error.Detail}")
            |> Result.map (fun (files, loaded) -> { Run = run; Files = files; Loaded = loaded }))

    let canonicalDirectory root =
        Path.Combine(root, "workspaces", "demo", templateId, "investigations", investigationId)

    let writeCanonical root (built: Built) =
        let directory = canonicalDirectory root
        Directory.CreateDirectory directory |> ignore
        File.WriteAllBytes(Path.Combine(directory, "manifest.json"), built.Files.Manifest)
        File.WriteAllBytes(Path.Combine(directory, "events.ndjson"), built.Files.Events)
        File.WriteAllBytes(Path.Combine(directory, "snapshot.json"), built.Files.Snapshot)
