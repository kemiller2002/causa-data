# Field mapping — F# engine ↔ Causa v1.0.0 wire

Engine: `kemiller2002/indy-init-rehearsal-001@55f49f61c939a3a780cee592f36f732a1328f981` (`src/engine/Commands.fs` `EventBody`). Generated from a hand-written mapping and cross-checked against the frozen schemas: generation fails if any schema field or enumeration value is unmapped. Normative alongside CONTRACT.md §6–§7.

Actor: Human -> {kind:human}; System -> {kind:system}; Agent(provider, model) -> {kind:agent, provider, model}.

Every payload key is required. `nullable` = the F# `option`, encoded as `null` when absent. All fields were introduced in 1.0.0.

## Envelope

| wire field | F# source | F# type | resolution |
|---|---|---|---|
| `formatVersion` | (none) | constant | Unspecified field in engine; constant added by contract. |
| `eventId` | (none) | derived UUIDv5 | Engine has no event id; derived (CD-ENV-02). |
| `investigationId` | Event.Investigation | InvestigationId | Direct. |
| `aggregateVersion` | Event.Sequence | int64 | Direct. |
| `eventType` | EventBody case | union case name | CaseNames.eventType (engine names). |
| `occurredAt` | Event.At | DateTimeOffset | Millisecond UTC (CD-WIRE-03). |
| `actor` | Event.Actor | Actor {Id; Kind} | Agent carries provider/model (CD-ENV-04). |
| `semanticVersion` | (none) | constant | Constant added by contract. |
| `correlationId` | Event.CommandId | Guid | Renamed (CD-ENV-03). |
| `payload` | Event.Body | EventBody | Per-type, section 7. |

## Payloads

### `InvestigationStarted`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `title` | `title` | NonEmptyText | string | no | non-blank, verbatim | Investigation title (decide falls back to the original text when the title is blank). |
| `observationId` | `observation` | ObservationId | uuid | no | lowercase canonical, non-nil | Id of the initial observation created with the investigation. |
| `original` | `original` | NonEmptyText | string | no | non-blank, verbatim | Initial observation, verbatim. |

### `LifecycleChanged`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `from` | `from` | InvestigationLifecycle | enum lifecycle | no | draft \| active \| awaitingEvidence \| awaitingVerification \| resolved \| closed \| archived | Lifecycle before the change. |
| `to` | `to` | InvestigationLifecycle | enum lifecycleTarget | no | active \| awaitingEvidence \| awaitingVerification \| resolved \| closed | Lifecycle after the change. |
| `command` | `command` | string (engine command name) | enum lifecycleCommand | no | activateInvestigation \| markAwaitingEvidence \| markAwaitingVerification \| resolveInvestigation \| closeInvestigation \| reopenInvestigation \| verificationFailed | Command that caused the change; distinguishes resolve/reopen/verification-failure. |
| `reason` | `reason` | string option | string | yes | non-blank, verbatim | Reason or basis; present for markAwaitingEvidence, reopenInvestigation, verificationFailed. |

### `ObservationRecorded`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `observationId` | `Item1` | ObservationId | uuid | no | lowercase canonical, non-nil | Observation id. |
| `original` | `Item2` | NonEmptyText | string | no | non-blank, verbatim | Observation, verbatim (R1.2). |
| `observedAt` | `Item3` | DateTimeOffset option | timestamp | yes | UTC, millisecond, Z | When it was observed, if known. |

### `HypothesisRecorded`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `hypothesisId` | `Item1` | HypothesisId | uuid | no | lowercase canonical, non-nil | Hypothesis id. |
| `statement` | `Item2` | NonEmptyText | string | no | non-blank, verbatim | Hypothesis statement. |
| `mechanism` | `Item3` | NonEmptyText option | string | yes | non-blank, verbatim | Proposed mechanism, if stated. |

### `HypothesisTransitioned`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `hypothesisId` | `Item1` | HypothesisId | uuid | no | lowercase canonical, non-nil | Hypothesis id. |
| `from` | `Item2.From` | HypothesisState | enum hypothesisState | no | proposed \| plausible \| supported \| weakened \| falsified \| superseded | State before. |
| `to` | `Item2.To` | HypothesisState | enum hypothesisTarget | no | plausible \| supported \| weakened \| falsified \| superseded | State after. |
| `command` | `Item2.Command` | string (HypothesisCommand name) | enum hypothesisCommand | no | markPlausible \| markSupported \| markWeakened \| falsify \| supersede \| reopen | Command that caused the change. |
| `reason` | `Item2.Reason` | NonEmptyText | string | no | non-blank, verbatim | Why (R2.3). |
| `evidenceIds` | `Item2.Evidence` | EvidenceId list | array<uuid> | no | ordered, distinct | Cited evidence, in citation order. |
| *(envelope)* | `Item2.At` | DateTimeOffset | — | — | — | Always equals the event time; carried by occurredAt. |
| *(envelope)* | `Item2.Sequence` | int64 | — | — | — | Always equals the event sequence; carried by aggregateVersion. |

### `EvidenceAdmitted`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `evidenceId` | `Item1` | EvidenceId | uuid | no | lowercase canonical, non-nil | Evidence id. |
| `contentKind` | `Item2 (case)` | EvidenceContent | enum contentKind | no | text \| artifact | text | artifact (TextEvidence | ArtifactEvidence). |
| `content` | `Item2 (payload)` | NonEmptyText | uri string | string | no | non-blank, verbatim | Text verbatim, or the absolute http(s) artifact URL. |
| `category` | `Item3` | EvidenceCategory | enum evidenceCategory | no | directObservation \| measurement \| log \| document \| experimentResult \| testimony \| externalReference | Kind of evidence. |
| `source` | `source` | NonEmptyText | string | no | non-blank, verbatim | Provenance: where it came from (R3.2). |
| `capturedAt` | `Item5` | DateTimeOffset option | timestamp | yes | UTC, millisecond, Z | When it was captured, if known. |

### `EvidenceSuperseded`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `evidenceId` | `old` | EvidenceId | uuid | no | lowercase canonical, non-nil | Superseded evidence. |
| `replacementEvidenceId` | `replacement` | EvidenceId | uuid | no | lowercase canonical, non-nil | Replacement evidence. |
| `reason` | `reason` | NonEmptyText | string | no | non-blank, verbatim | Why it was superseded. |

### `EvidenceVerificationAssessed`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `evidenceId` | `Item1` | EvidenceId | uuid | no | lowercase canonical, non-nil | Evidence id. |
| `verification` | `Item2` | VerificationStatus | enum verificationStatus | no | unverified \| partiallyVerified \| verified \| invalidated | Assessed verification status. |

### `EvidenceRelated`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `relationshipId` | `Item1` | RelationshipId | uuid | no | lowercase canonical, non-nil | Relationship id. |
| `evidenceId` | `Item2` | EvidenceId | uuid | no | lowercase canonical, non-nil | Evidence. |
| `hypothesisId` | `Item3` | HypothesisId | uuid | no | lowercase canonical, non-nil | Hypothesis. |
| `kind` | `Item4` | RelationshipKind | enum relationshipKind | no | supports \| contradicts \| discriminates \| insufficient | supports | contradicts | discriminates | insufficient. |
| `rationale` | `Item5` | NonEmptyText option | string | yes | non-blank, verbatim | Why, if stated. |

### `UnknownIdentified`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `unknownId` | `Item1` | UnknownId | uuid | no | lowercase canonical, non-nil | Unknown id. |
| `question` | `Item2` | NonEmptyText | string | no | non-blank, verbatim | Open question. |
| `materiality` | `Item3` | Materiality | enum materiality | no | blocking \| material \| nonMaterial \| informational | How much it matters. |

### `UnknownTransitioned`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `unknownId` | `Item1` | UnknownId | uuid | no | lowercase canonical, non-nil | Unknown id. |
| `state` | `Item2` | UnknownState | enum unknownTarget | no | investigating \| resolved \| acceptedResidual | New state. |
| `basis` | `Item3` | NonEmptyText | string | no | non-blank, verbatim | Basis for the change. |

### `AssumptionRecorded`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `assumptionId` | `Item1` | AssumptionId | uuid | no | lowercase canonical, non-nil | Assumption id. |
| `proposition` | `Item2` | NonEmptyText | string | no | non-blank, verbatim | Assumed proposition. |
| `materiality` | `Item3` | Materiality | enum materiality | no | blocking \| material \| nonMaterial \| informational | How much it matters. |

### `AssumptionTransitioned`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `assumptionId` | `Item1` | AssumptionId | uuid | no | lowercase canonical, non-nil | Assumption id. |
| `state` | `Item2` | AssumptionState | enum assumptionTarget | no | provisionallyAccepted \| verified \| rejected | New state. |

### `ContradictionRecorded`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `contradictionId` | `Item1` | ContradictionId | uuid | no | lowercase canonical, non-nil | Contradiction id. |
| `statement` | `Item2` | NonEmptyText | string | no | non-blank, verbatim | What contradicts what. |
| `evidenceIds` | `Item3` | EvidenceId list | array<uuid> | no | ordered, distinct | Evidence in contradiction. |
| `materiality` | `Item4` | Materiality | enum materiality | no | blocking \| material \| nonMaterial \| informational | How much it matters. |

### `ContradictionTransitioned`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `contradictionId` | `Item1` | ContradictionId | uuid | no | lowercase canonical, non-nil | Contradiction id. |
| `state` | `Item2` | ContradictionState | enum contradictionTarget | no | triaged \| resolved \| acceptedResidual | New state. |
| `basis` | `Item3` | NonEmptyText | string | no | non-blank, verbatim | Resolution basis. |

### `PredictionRecorded`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `predictionId` | `Item1` | PredictionId | uuid | no | lowercase canonical, non-nil | Prediction id. |
| `hypothesisId` | `Item2` | HypothesisId | uuid | no | lowercase canonical, non-nil | Hypothesis it derives from. |
| `statement` | `Item3` | NonEmptyText | string | no | non-blank, verbatim | Predicted observation. |
| `strength` | `Item4` | PredictionStrength | enum predictionStrength | no | necessary \| expected \| discriminating \| nonDiscriminating | How strongly it follows. |
| `timing` | `Item5` | PredictionTiming | enum predictionTiming | no | prospective \| retrospective | As decided by the engine (A11), not as requested. |

### `ActionRecorded`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `actionId` | `Item1` | ActionId | uuid | no | lowercase canonical, non-nil | Action id. |
| `kind` | `Item2` | ActionKind | enum actionKind | no | test \| inspection \| query \| comparison \| experiment | Kind of investigative action. |
| `purpose` | `Item3` | NonEmptyText | string | no | non-blank, verbatim | Purpose. |
| `targetHypothesisIds` | `Item4` | HypothesisId list | array<uuid> | no | ordered, distinct | Hypotheses it tests. |

### `ActionTransitioned`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `actionId` | `Item1` | ActionId | uuid | no | lowercase canonical, non-nil | Action id. |
| `state` | `Item2` | ActionState | enum actionTarget | no | approved \| rejected \| inProgress \| completed \| cancelled | New state. |

### `FindingRecorded`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `findingId` | `Item1` | FindingId | uuid | no | lowercase canonical, non-nil | Finding id. |
| `actionId` | `Item2` | ActionId | uuid | no | lowercase canonical, non-nil | Action that produced it. |
| `originalResult` | `Item3` | NonEmptyText | string | no | non-blank, verbatim | Result exactly as entered (R4.4). |
| `interpretation` | `Item4` | NonEmptyText option | string | yes | non-blank, verbatim | Interpretation, if stated. |

### `NegativeKnowledgeEstablished`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `negativeKnowledgeId` | `Item1` | NegativeKnowledgeId | uuid | no | lowercase canonical, non-nil | Negative knowledge id. |
| `statement` | `Item2` | NonEmptyText | string | no | non-blank, verbatim | What is established not to be the case. |
| `scope` | `Item3` | NonEmptyText | string | no | non-blank, verbatim | Where it holds. |
| `basisEvidenceIds` | `Item4` | EvidenceId list (non-empty) | array<uuid> | no | ordered, distinct; at least 1 item | Evidence that established it (R5.3). |

### `CausalAssertionRecorded`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `causeId` | `Item1` | CausalAssertionId | uuid | no | lowercase canonical, non-nil | Cause id. |
| `outcome` | `Item2` | NonEmptyText | string | no | non-blank, verbatim | Outcome explained. |
| `members` | `Item3` | CausalMember list | array<object causal-member> | no | min 1, unique | Hypotheses with roles {hypothesisId, role}. |
| `mechanism` | `Item4` | NonEmptyText option | string | yes | non-blank, verbatim | Causal mechanism, if stated. |

### `CausalAssertionConfirmed`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `causeId` | `Item1` | CausalAssertionId | uuid | no | lowercase canonical, non-nil | Cause id. |
| `policy` | `policy` | string | string | no | non-blank, verbatim | Policy id applied by the cause gate (engine Policy.id = core-default/1). |

### `CausalAssertionReopened`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `causeId` | `Item1` | CausalAssertionId | uuid | no | lowercase canonical, non-nil | Cause id. |
| `reason` | `Item2` | NonEmptyText | string | no | non-blank, verbatim | Why it was reopened. |

### `CorrectiveActionRecorded`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `correctiveActionId` | `Item1` | CorrectiveActionId | uuid | no | lowercase canonical, non-nil | Corrective action id. |
| `kind` | `Item2` | CorrectiveActionKind | enum correctiveActionKind | no | mitigation \| workaround \| correction \| prevention | mitigation | workaround | correction | prevention (R7.2). |
| `description` | `Item3` | NonEmptyText | string | no | non-blank, verbatim | What will be done. |
| `targetCauseIds` | `Item4` | CausalAssertionId list | array<uuid> | no | ordered, distinct | Causes it addresses. |

### `CorrectiveActionTransitioned`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `correctiveActionId` | `Item1` | CorrectiveActionId | uuid | no | lowercase canonical, non-nil | Corrective action id. |
| `state` | `Item2` | CorrectiveActionState | enum correctiveActionTarget | no | approved \| cancelled \| inProgress \| implemented \| awaitingVerificationOfEffect \| verificationFailed \| verifiedEffective \| reworkRequired | New state. |

### `CriterionRecorded`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `criterionId` | `Item1` | CriterionId | uuid | no | lowercase canonical, non-nil | Criterion id. |
| `correctiveActionId` | `Item2` | CorrectiveActionId | uuid | no | lowercase canonical, non-nil | Corrective action it verifies. |
| `expectedOutcome` | `Item3` | NonEmptyText | string | no | non-blank, verbatim | Success criterion (R7.3). |
| `method` | `Item4` | NonEmptyText option | string | yes | non-blank, verbatim | Verification method, if stated. |

### `VerificationRecorded`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `verificationId` | `Item1` | VerificationId | uuid | no | lowercase canonical, non-nil | Verification id. |
| `criterionId` | `Item2` | CriterionId | uuid | no | lowercase canonical, non-nil | Criterion verified. |
| `outcome` | `Item3` | VerificationOutcome | enum verificationOutcome | no | satisfied \| failed \| inconclusive | satisfied | failed | inconclusive. |
| `evidenceIds` | `Item4` | EvidenceId list | array<uuid> | no | ordered, distinct | Evidence cited (required when satisfied, R7.4). |

### `ConclusionRecorded`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `conclusionId` | `Item1` | ConclusionId | uuid | no | lowercase canonical, non-nil | Conclusion id. |
| `statement` | `Item2` | NonEmptyText | string | no | non-blank, verbatim | Conclusion. |
| `strength` | `Item3` | ConclusionStrength | enum conclusionStrength | no | plausibleExplanation \| leadingExplanation \| supportedCause \| confirmedCause \| acceptedWithResidualUncertainty | Strength of the conclusion. |
| `causeIds` | `Item4` | CausalAssertionId list | array<uuid> | no | ordered, distinct | Causes it rests on. |
| `residualUncertainty` | `Item5` | NonEmptyText option | string | yes | non-blank, verbatim | Residual uncertainty (R6.4). |

### `ProposalRecorded`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `proposalId` | `Item1` | ProposalId | uuid | no | lowercase canonical, non-nil | Proposal id. |
| `kind` | `Item2` | ProposalKind | enum proposalKind | no | hypothesis \| unknown \| action | What is proposed. |
| `text` | `Item3` | NonEmptyText | string | no | non-blank, verbatim | Proposed statement/question/purpose; never evidence (R15.2). |
| `rationale` | `Item4` | NonEmptyText option | string | yes | non-blank, verbatim | Rationale, if stated. |

### `ProposalWasAccepted`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `proposalId` | `Item1` | ProposalId | uuid | no | lowercase canonical, non-nil | Proposal id. |
| `targetId` | `targetId` | Guid | uuid | no | lowercase canonical, non-nil | Id of the entity the acceptance created. |

### `ProposalWasRejected`

| wire field | F# element | F# type | JSON type | nullable | constraint | meaning |
|---|---|---|---|---|---|---|
| `proposalId` | `Item1` | ProposalId | uuid | no | lowercase canonical, non-nil | Proposal id. |
| `reason` | `Item2` | NonEmptyText | string | no | non-blank, verbatim | Why it was rejected. |

## Enumerations (F# union case → wire value)

Engine prefixes that exist only to avoid F# name collisions are dropped. The F# case names are those of the engine's `Model.fs`/`Commands.fs` unions. Bidirectional exhaustiveness is tested against the engine's own case lists (`Codec.fs` `CaseNames.all*`) by the reference toolkit's unit tests.

| enumeration | F# union | F# case → wire value |
|---|---|---|
| `lifecycle` | `InvestigationLifecycle` | `Draft`→`draft`, `Active`→`active`, `AwaitingEvidence`→`awaitingEvidence`, `AwaitingVerification`→`awaitingVerification`, `Resolved`→`resolved`, `Closed`→`closed`, `Archived`→`archived` |
| `hypothesisState` | `HypothesisState` | `Proposed`→`proposed`, `Plausible`→`plausible`, `Supported`→`supported`, `Weakened`→`weakened`, `Falsified`→`falsified`, `Superseded`→`superseded` |
| `unknownState` | `UnknownState` | `Identified`→`identified`, `Investigating`→`investigating`, `UnknownResolved`→`resolved`, `AcceptedResidual`→`acceptedResidual`, `UnknownSuperseded`→`superseded` |
| `materiality` | `Materiality` | `Blocking`→`blocking`, `Material`→`material`, `NonMaterial`→`nonMaterial`, `Informational`→`informational` |
| `assumptionState` | `AssumptionState` | `ExplicitUnverified`→`explicitUnverified`, `ProvisionallyAccepted`→`provisionallyAccepted`, `AssumptionVerified`→`verified`, `Rejected`→`rejected`, `Expired`→`expired` |
| `contradictionState` | `ContradictionState` | `Detected`→`detected`, `Triaged`→`triaged`, `ContradictionInvestigating`→`investigating`, `ContradictionResolved`→`resolved`, `ContradictionAcceptedResidual`→`acceptedResidual`, `ContradictionSuperseded`→`superseded` |
| `actionState` | `ActionState` | `ActionProposed`→`proposed`, `ActionApproved`→`approved`, `ActionInProgress`→`inProgress`, `ActionCompleted`→`completed`, `ActionInterpreted`→`interpreted`, `ActionRejected`→`rejected`, `ActionCancelled`→`cancelled` |
| `actionKind` | `ActionKind` | `Test`→`test`, `Inspection`→`inspection`, `Query`→`query`, `Comparison`→`comparison`, `Experiment`→`experiment` |
| `causeState` | `CausalAssertionState` | `CauseProposed`→`proposed`, `CauseConfirmed`→`confirmed`, `CauseReopened`→`reopened`, `CauseSuperseded`→`superseded` |
| `causeRole` | `CauseRole` | `RootCause`→`rootCause`, `ContributingFactor`→`contributingFactor`, `Trigger`→`trigger`, `EnablingCondition`→`enablingCondition` |
| `correctiveActionState` | `CorrectiveActionState` | `CorrectionProposed`→`proposed`, `CorrectionApproved`→`approved`, `CorrectionInProgress`→`inProgress`, `Implemented`→`implemented`, `AwaitingVerificationOfEffect`→`awaitingVerificationOfEffect`, `VerifiedEffective`→`verifiedEffective`, `VerificationFailed`→`verificationFailed`, `ReworkRequired`→`reworkRequired`, `CorrectionCancelled`→`cancelled` |
| `correctiveActionKind` | `CorrectiveActionKind` | `Mitigation`→`mitigation`, `Workaround`→`workaround`, `Correction`→`correction`, `Prevention`→`prevention` |
| `evidenceCategory` | `EvidenceCategory` | `DirectObservation`→`directObservation`, `Measurement`→`measurement`, `Log`→`log`, `Document`→`document`, `ExperimentResult`→`experimentResult`, `Testimony`→`testimony`, `ExternalReference`→`externalReference` |
| `verificationStatus` | `VerificationStatus` | `Unverified`→`unverified`, `PartiallyVerified`→`partiallyVerified`, `Verified`→`verified`, `Invalidated`→`invalidated` |
| `relationshipKind` | `RelationshipKind` | `Supports`→`supports`, `Contradicts`→`contradicts`, `Discriminates`→`discriminates`, `Insufficient`→`insufficient` |
| `predictionStrength` | `PredictionStrength` | `Necessary`→`necessary`, `Expected`→`expected`, `DiscriminatingPrediction`→`discriminating`, `NonDiscriminating`→`nonDiscriminating` |
| `predictionTiming` | `PredictionTiming` | `Prospective`→`prospective`, `Retrospective`→`retrospective` |
| `verificationOutcome` | `VerificationOutcome` | `Satisfied`→`satisfied`, `Failed`→`failed`, `Inconclusive`→`inconclusive` |
| `conclusionStrength` | `ConclusionStrength` | `PlausibleExplanation`→`plausibleExplanation`, `LeadingExplanation`→`leadingExplanation`, `SupportedCause`→`supportedCause`, `ConfirmedCause`→`confirmedCause`, `AcceptedWithResidualUncertainty`→`acceptedWithResidualUncertainty` |
| `proposalKind` | `ProposalKind` | `ProposedHypothesis`→`hypothesis`, `ProposedUnknown`→`unknown`, `ProposedAction`→`action` |
| `proposalState` | `ProposalState` | `ProposalActive`→`active`, `ProposalAccepted`→`accepted`, `ProposalRejected`→`rejected` |
| `hypothesisCommand` | `HypothesisCommand` | `MarkPlausible`→`markPlausible`, `MarkSupported`→`markSupported`, `MarkWeakened`→`markWeakened`, `Falsify`→`falsify`, `Supersede`→`supersede`, `Reopen`→`reopen` |
| `contentKind` | `EvidenceContent` | `TextEvidence`→`text`, `ArtifactEvidence`→`artifact` |

### Engine command strings

`LifecycleChanged.command` and `HypothesisTransitioned.command` carry a command name; the engine stores it as a string (`LifecycleChanged.command`, `HypothesisChange.Command`). These are the exact engine strings:

| field | engine string → wire value |
|---|---|
| `LifecycleChanged.command` | `"ActivateInvestigation"`→`activateInvestigation`, `"MarkAwaitingEvidence"`→`markAwaitingEvidence`, `"MarkAwaitingVerification"`→`markAwaitingVerification`, `"ResolveInvestigation"`→`resolveInvestigation`, `"CloseInvestigation"`→`closeInvestigation`, `"ReopenInvestigation"`→`reopenInvestigation`, `"VerificationFailed"`→`verificationFailed` |
| `HypothesisTransitioned.command` | `"MarkPlausible"`→`markPlausible`, `"MarkSupported"`→`markSupported`, `"MarkWeakened"`→`markWeakened`, `"Falsify"`→`falsify`, `"Supersede"`→`supersede`, `"Reopen"`→`reopen` |

`"VerificationFailed"` is not a user command. The engine emits it as the follow-on `LifecycleChanged(AwaitingVerification, Active, "VerificationFailed", Some reason)` when `RecordVerification` records a failed outcome while the investigation awaits verification. Hypothesis command strings are `$"{command}"` of the `HypothesisCommand` union, i.e. the case name.

