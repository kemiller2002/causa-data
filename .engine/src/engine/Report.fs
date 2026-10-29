namespace IndyInitRootCauseInvestigator.Engine

open System
open System.Globalization

// R12: Folio report content as a pure, flat model. Rendering belongs to another slice.

type ReportItem =
    { Heading: string
      Detail: string
      Status: string }

type ReportSection =
    { Id: string
      Title: string
      Items: ReportItem list }

type ReportModel =
    { Title: string
      InvestigationId: string
      /// "Interim" | "Final"
      Kind: string
      Version: int64
      /// EventCodec.digest of the encoded source history (A31: same snapshot, same report).
      SnapshotDigest: string
      Observation: string
      Sections: ReportSection list
      UnresolvedWork: ReportItem list }

[<RequireQualifiedAccess>]
module Report =
    let private human = CaseNames.humanize
    let private join (parts: string list) = parts |> List.filter (fun p -> not (String.IsNullOrEmpty p)) |> String.concat "; "
    let private item heading detail status = { Heading = heading; Detail = detail; Status = status }
    let private stamp (d: DateTimeOffset) = d.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)
    let private optional (label: string) (t: NonEmptyText option) = t |> Option.map (fun v -> label + ": " + v.Value) |> Option.defaultValue ""

    let private actorLabel (a: Actor) =
        match a.Kind with
        | Human -> a.Id + " (human)"
        | System -> a.Id + " (system)"
        | Agent(provider, model) -> a.Id + " (AI agent " + provider + "/" + model + ")"

    let correctionKindLabel (kind: CorrectiveActionKind) =
        match kind with
        | Mitigation -> "Mitigation (temporary; does not correct the root cause)"
        | Workaround -> "Workaround (temporary; does not correct the root cause)"
        | Correction -> "Correction (root-cause correction)"
        | Prevention -> "Prevention (prevents recurrence)"

    let private roleLabel (role: CauseRole) = human (CaseNames.causeRole role)

    /// The id each recording event introduces, so sections follow history order, not Guid order.
    let private introduces (body: EventBody) : Guid option =
        match body with
        | InvestigationStarted(_, oid, _) -> Some oid.Value
        | ObservationRecorded(id, _, _) -> Some id.Value
        | HypothesisRecorded(id, _, _) -> Some id.Value
        | EvidenceAdmitted(id, _, _, _, _) -> Some id.Value
        | EvidenceRelated(id, _, _, _, _) -> Some id.Value
        | UnknownIdentified(id, _, _) -> Some id.Value
        | AssumptionRecorded(id, _, _) -> Some id.Value
        | ContradictionRecorded(id, _, _, _) -> Some id.Value
        | PredictionRecorded(id, _, _, _, _) -> Some id.Value
        | ActionRecorded(id, _, _, _) -> Some id.Value
        | FindingRecorded(id, _, _, _) -> Some id.Value
        | NegativeKnowledgeEstablished(id, _, _, _) -> Some id.Value
        | CausalAssertionRecorded(id, _, _, _) -> Some id.Value
        | CorrectiveActionRecorded(id, _, _, _) -> Some id.Value
        | CriterionRecorded(id, _, _, _) -> Some id.Value
        | VerificationRecorded(id, _, _, _) -> Some id.Value
        | ConclusionRecorded(id, _, _, _, _) -> Some id.Value
        | ProposalRecorded(id, _, _, _) -> Some id.Value
        | LifecycleChanged _
        | HypothesisTransitioned _
        | EvidenceSuperseded _
        | EvidenceVerificationAssessed _
        | UnknownTransitioned _
        | AssumptionTransitioned _
        | ContradictionTransitioned _
        | ActionTransitioned _
        | CausalAssertionConfirmed _
        | CausalAssertionReopened _
        | CorrectiveActionTransitioned _
        | ProposalWasAccepted _
        | ProposalWasRejected _ -> None

    type private Context =
        { Inv: Investigation
          Order: Map<Guid, int64> }

    let private ordered (ctx: Context) (idOf: 'V -> Id<'T>) (m: Map<Id<'T>, 'V>) : 'V list =
        m
        |> Map.toList
        |> List.map snd
        |> List.sortBy (fun v ->
            let g = (idOf v).Value
            (ctx.Order |> Map.tryFind g |> Option.defaultValue Int64.MaxValue), g)

    let private evidenceLabel (ctx: Context) (eid: EvidenceId) =
        match ctx.Inv.Evidence |> Map.tryFind eid with
        | Some e ->
            match e.Content with
            | TextEvidence t -> t.Value
            | ArtifactEvidence uri -> uri
        | None -> "evidence " + Id.text eid

    let private hypothesisLabel (ctx: Context) (hid: HypothesisId) =
        ctx.Inv.Hypotheses |> Map.tryFind hid |> Option.map (fun h -> h.Statement.Value) |> Option.defaultValue ("hypothesis " + Id.text hid)

    let private causeLabel (ctx: Context) (cid: CausalAssertionId) =
        ctx.Inv.Causes |> Map.tryFind cid |> Option.map (fun c -> c.Outcome.Value) |> Option.defaultValue ("cause " + Id.text cid)

    let private actionLabel (ctx: Context) (aid: ActionId) =
        ctx.Inv.Actions |> Map.tryFind aid |> Option.map (fun a -> a.Purpose.Value) |> Option.defaultValue ("action " + Id.text aid)

    let private correctionLabel (ctx: Context) (caid: CorrectiveActionId) =
        ctx.Inv.CorrectiveActions |> Map.tryFind caid |> Option.map (fun c -> c.Description.Value) |> Option.defaultValue ("corrective action " + Id.text caid)

    let private criterionLabel (ctx: Context) (crid: CriterionId) =
        ctx.Inv.Criteria |> Map.tryFind crid |> Option.map (fun c -> c.ExpectedOutcome.Value) |> Option.defaultValue ("criterion " + Id.text crid)

    let private listOf (label: string) (f: 'T -> string) (xs: 'T list) =
        if xs.IsEmpty then "" else label + ": " + (xs |> List.map f |> String.concat ", ")

    // ---- timeline ----------------------------------------------------------

    let private describe (ctx: Context) (body: EventBody) : string =
        let ev = evidenceLabel ctx
        let hy = hypothesisLabel ctx
        match body with
        | InvestigationStarted(title, _, original) -> join [ "Investigation \"" + title.Value + "\" started"; "Observation: " + original.Value ]
        | LifecycleChanged(from, target, command, reason) ->
            join [ "Lifecycle " + human (CaseNames.lifecycle from) + " → " + human (CaseNames.lifecycle target) + " (" + command + ")"
                   reason |> Option.map (fun r -> "Reason: " + r) |> Option.defaultValue "" ]
        | ObservationRecorded(_, original, at) ->
            join [ "Observation recorded: " + original.Value; at |> Option.map (fun d -> "Observed " + stamp d) |> Option.defaultValue "" ]
        | HypothesisRecorded(_, statement, mechanism) -> join [ "Hypothesis proposed: " + statement.Value; optional "Mechanism" mechanism ]
        | HypothesisTransitioned(hid, c) ->
            join [ "Hypothesis \"" + hy hid + "\" " + human (CaseNames.hypothesisState c.From) + " → " + human (CaseNames.hypothesisState c.To)
                   "Reason: " + c.Reason.Value
                   listOf "Evidence" ev c.Evidence ]
        | EvidenceAdmitted(eid, _, category, source, _) ->
            join [ "Evidence admitted: " + ev eid; "Category: " + human (CaseNames.evidenceCategory category); "Source: " + source.Value ]
        | EvidenceSuperseded(old, replacement, reason) ->
            join [ "Evidence \"" + ev old + "\" superseded by \"" + ev replacement + "\""; "Reason: " + reason.Value ]
        | EvidenceVerificationAssessed(eid, status) -> "Evidence \"" + ev eid + "\" assessed as " + human (CaseNames.verificationStatus status)
        | EvidenceRelated(_, eid, hid, kind, rationale) ->
            join [ "Evidence \"" + ev eid + "\" " + (CaseNames.relationshipKind kind).ToLowerInvariant() + " hypothesis \"" + hy hid + "\""
                   optional "Rationale" rationale ]
        | UnknownIdentified(_, question, m) -> join [ "Unknown identified: " + question.Value; "Materiality: " + human (CaseNames.materiality m) ]
        | UnknownTransitioned(uid, state, basis) ->
            let q = ctx.Inv.Unknowns |> Map.tryFind uid |> Option.map (fun u -> u.Question.Value) |> Option.defaultValue (Id.text uid)
            join [ "Unknown \"" + q + "\" is now " + human (CaseNames.unknownState state); "Basis: " + basis.Value ]
        | AssumptionRecorded(_, prop, m) -> join [ "Assumption recorded: " + prop.Value; "Materiality: " + human (CaseNames.materiality m) ]
        | AssumptionTransitioned(aid, state) ->
            let p = ctx.Inv.Assumptions |> Map.tryFind aid |> Option.map (fun a -> a.Proposition.Value) |> Option.defaultValue (Id.text aid)
            "Assumption \"" + p + "\" is now " + human (CaseNames.assumptionState state)
        | ContradictionRecorded(_, statement, evidence, m) ->
            join [ "Contradiction detected: " + statement.Value; "Materiality: " + human (CaseNames.materiality m); listOf "Evidence" ev evidence ]
        | ContradictionTransitioned(cid, state, basis) ->
            let s = ctx.Inv.Contradictions |> Map.tryFind cid |> Option.map (fun c -> c.Statement.Value) |> Option.defaultValue (Id.text cid)
            join [ "Contradiction \"" + s + "\" is now " + human (CaseNames.contradictionState state); "Basis: " + basis.Value ]
        | PredictionRecorded(_, hid, statement, strength, timing) ->
            join [ "Prediction for \"" + hy hid + "\": " + statement.Value
                   human (CaseNames.predictionStrength strength) + ", " + (CaseNames.predictionTiming timing).ToLowerInvariant() ]
        | ActionRecorded(_, kind, purpose, targets) ->
            join [ human (CaseNames.actionKind kind) + " planned: " + purpose.Value; listOf "Targets" hy targets ]
        | ActionTransitioned(aid, state) -> "Action \"" + actionLabel ctx aid + "\" is now " + human (CaseNames.actionState state)
        | FindingRecorded(_, aid, result, interpretation) ->
            join [ "Finding for \"" + actionLabel ctx aid + "\": " + result.Value; optional "Interpretation" interpretation ]
        | NegativeKnowledgeEstablished(_, statement, scope, basis) ->
            join [ "Eliminated: " + statement.Value; "Scope: " + scope.Value; listOf "Basis" ev basis ]
        | CausalAssertionRecorded(_, outcome, members, mechanism) ->
            join [ "Causal assertion for \"" + outcome.Value + "\""
                   listOf "Members" (fun (m: CausalMember) -> roleLabel m.Role + ": " + hy m.Hypothesis) members
                   optional "Mechanism" mechanism ]
        | CausalAssertionConfirmed(cid, policy) -> "Cause \"" + causeLabel ctx cid + "\" confirmed under policy " + policy
        | CausalAssertionReopened(cid, reason) -> join [ "Cause \"" + causeLabel ctx cid + "\" reopened"; "Reason: " + reason.Value ]
        | CorrectiveActionRecorded(_, kind, description, targets) ->
            join [ correctionKindLabel kind + ": " + description.Value; listOf "Targets" (causeLabel ctx) targets ]
        | CorrectiveActionTransitioned(caid, state) ->
            "Corrective action \"" + correctionLabel ctx caid + "\" is now " + human (CaseNames.correctionState state)
        | CriterionRecorded(_, caid, expected, method) ->
            join [ "Success criterion for \"" + correctionLabel ctx caid + "\": " + expected.Value; optional "Method" method ]
        | VerificationRecorded(_, crid, outcome, evidence) ->
            join [ "Verification of \"" + criterionLabel ctx crid + "\": " + human (CaseNames.verificationOutcome outcome); listOf "Evidence" ev evidence ]
        | ConclusionRecorded(_, statement, strength, _, residual) ->
            join [ "Conclusion: " + statement.Value; "Strength: " + human (CaseNames.conclusionStrength strength); optional "Residual uncertainty" residual ]
        | ProposalRecorded(_, kind, t, rationale) -> join [ human (CaseNames.proposalKind kind) + " (AI proposal): " + t.Value; optional "Rationale" rationale ]
        | ProposalWasAccepted(pid, _) ->
            let t = ctx.Inv.Proposals |> Map.tryFind pid |> Option.map (fun p -> p.Text.Value) |> Option.defaultValue (Id.text pid)
            "Proposal accepted: " + t
        | ProposalWasRejected(pid, reason) ->
            let t = ctx.Inv.Proposals |> Map.tryFind pid |> Option.map (fun p -> p.Text.Value) |> Option.defaultValue (Id.text pid)
            join [ "Proposal rejected: " + t; "Reason: " + reason.Value ]

    let private timeline (ctx: Context) (events: Event list) =
        events
        |> List.map (fun e ->
            item (string e.Sequence + ". " + human (CaseNames.eventType e.Body)) (describe ctx e.Body) (actorLabel e.Actor + " · " + stamp e.At))

    // ---- entity sections ---------------------------------------------------

    let private hypotheses (ctx: Context) =
        ctx.Inv.Hypotheses
        |> ordered ctx (fun (h: Hypothesis) -> h.Id)
        |> List.map (fun h ->
            let history =
                h.History
                |> List.map (fun c ->
                    "#" + string c.Sequence + " " + human (CaseNames.hypothesisState c.From) + " → " + human (CaseNames.hypothesisState c.To)
                    + " (" + c.Command + "): " + c.Reason.Value
                    + (if c.Evidence.IsEmpty then "" else " [evidence: " + (c.Evidence |> List.map (evidenceLabel ctx) |> String.concat ", ") + "]"))
            item h.Statement.Value (join ([ optional "Mechanism" h.Mechanism ] @ history)) (human (CaseNames.hypothesisState h.State)))

    let private evidence (ctx: Context) =
        ctx.Inv.Evidence
        |> ordered ctx (fun (e: Evidence) -> e.Id)
        |> List.map (fun e ->
            let content =
                match e.Content with
                | TextEvidence t -> t.Value
                | ArtifactEvidence uri -> uri
            let superseded = e.SupersededBy |> Option.map (fun r -> "Superseded by: " + evidenceLabel ctx r) |> Option.defaultValue ""
            let detail =
                join [ "Category: " + human (CaseNames.evidenceCategory e.Category)
                       "Source: " + e.Source.Value
                       "Captured: " + (e.CapturedAt |> Option.map stamp |> Option.defaultValue "not recorded")
                       "Verification: " + human (CaseNames.verificationStatus e.Verification)
                       superseded ]
            let status = if e.SupersededBy.IsSome then "Superseded" else human (CaseNames.verificationStatus e.Verification)
            item content detail status)

    let private relationships (ctx: Context) =
        ctx.Inv.Relationships
        |> ordered ctx (fun (r: Relationship) -> r.Id)
        |> List.map (fun r ->
            item
                (evidenceLabel ctx r.Evidence + " → " + hypothesisLabel ctx r.Hypothesis)
                (join [ "Evidence " + (CaseNames.relationshipKind r.Kind).ToLowerInvariant() + " the hypothesis"; optional "Rationale" r.Rationale ])
                (CaseNames.relationshipKind r.Kind))

    let private experiments (ctx: Context) =
        let predictions =
            ctx.Inv.Predictions
            |> ordered ctx (fun (p: Prediction) -> p.Id)
            |> List.map (fun p ->
                item
                    ("Prediction: " + p.Statement.Value)
                    (join [ "Hypothesis: " + hypothesisLabel ctx p.Hypothesis
                            "Strength: " + human (CaseNames.predictionStrength p.Strength)
                            "Timing: " + human (CaseNames.predictionTiming p.Timing) ])
                    "Prediction")
        let actions =
            ctx.Inv.Actions
            |> ordered ctx (fun (a: InvestigationAction) -> a.Id)
            |> List.collect (fun a ->
                let findings =
                    ctx.Inv.Findings
                    |> ordered ctx (fun (f: Finding) -> f.Id)
                    |> List.filter (fun f -> f.Action = a.Id)
                    |> List.map (fun f ->
                        item
                            ("Finding: " + f.OriginalResult.Value)
                            (join [ "Action: " + a.Purpose.Value; "Original result: " + f.OriginalResult.Value; optional "Interpretation" f.Interpretation ])
                            "Finding")
                item
                    (human (CaseNames.actionKind a.Kind) + ": " + a.Purpose.Value)
                    (join [ listOf "Targets" (hypothesisLabel ctx) a.Targets ])
                    (human (CaseNames.actionState a.State))
                :: findings)
        predictions @ actions

    let private unknowns (ctx: Context) =
        let us =
            ctx.Inv.Unknowns
            |> ordered ctx (fun (u: Unknown) -> u.Id)
            |> List.map (fun u ->
                item
                    ("Unknown: " + u.Question.Value)
                    (join [ "Materiality: " + human (CaseNames.materiality u.Materiality); optional "Basis" u.Basis ])
                    (human (CaseNames.unknownState u.State)))
        let assumptions =
            ctx.Inv.Assumptions
            |> ordered ctx (fun (a: Assumption) -> a.Id)
            |> List.map (fun a ->
                item ("Assumption: " + a.Proposition.Value) ("Materiality: " + human (CaseNames.materiality a.Materiality)) (human (CaseNames.assumptionState a.State)))
        let contradictions =
            ctx.Inv.Contradictions
            |> ordered ctx (fun (c: Contradiction) -> c.Id)
            |> List.map (fun c ->
                item
                    ("Contradiction: " + c.Statement.Value)
                    (join [ "Materiality: " + human (CaseNames.materiality c.Materiality); listOf "Evidence" (evidenceLabel ctx) c.Evidence; optional "Basis" c.Basis ])
                    (human (CaseNames.contradictionState c.State)))
        us @ assumptions @ contradictions

    let private negativeKnowledge (ctx: Context) =
        ctx.Inv.NegativeKnowledge
        |> ordered ctx (fun (n: NegativeKnowledge) -> n.Id)
        |> List.map (fun n -> item n.Statement.Value (join [ "Scope: " + n.Scope.Value; listOf "Basis" (evidenceLabel ctx) n.Basis ]) "Eliminated")

    let private causes (ctx: Context) =
        ctx.Inv.Causes
        |> ordered ctx (fun (c: CausalAssertion) -> c.Id)
        |> List.map (fun c ->
            let members = c.Members |> List.map (fun m -> roleLabel m.Role + ": " + hypothesisLabel ctx m.Hypothesis)
            item c.Outcome.Value (join (members @ [ optional "Mechanism" c.Mechanism ])) (human (CaseNames.causeState c.State)))

    let private correctiveActions (ctx: Context) =
        ctx.Inv.CorrectiveActions
        |> ordered ctx (fun (c: CorrectiveAction) -> c.Id)
        |> List.map (fun ca ->
            let criteria =
                ctx.Inv.Criteria
                |> ordered ctx (fun (c: Criterion) -> c.Id)
                |> List.filter (fun c -> c.CorrectiveAction = ca.Id)
                |> List.map (fun c ->
                    let verifications =
                        ctx.Inv.Verifications
                        |> ordered ctx (fun (v: Verification) -> v.Id)
                        |> List.filter (fun v -> v.Criterion = c.Id)
                        |> List.map (fun v ->
                            "verification " + human (CaseNames.verificationOutcome v.Outcome)
                            + (if v.Evidence.IsEmpty then " (no evidence)" else " (evidence: " + (v.Evidence |> List.map (evidenceLabel ctx) |> String.concat ", ") + ")"))
                    let method = c.Method |> Option.map (fun m -> " [method: " + m.Value + "]") |> Option.defaultValue ""
                    let outcome = if verifications.IsEmpty then " — not yet verified" else " — " + String.concat ", " verifications
                    "Criterion: " + c.ExpectedOutcome.Value + method + outcome)
            item
                ca.Description.Value
                (join ([ "Kind: " + correctionKindLabel ca.Kind; listOf "Targets" (causeLabel ctx) ca.Targets ] @ criteria))
                (human (CaseNames.correctionState ca.State)))

    let private conclusion (ctx: Context) =
        match ctx.Inv.Conclusions |> ordered ctx (fun (c: Conclusion) -> c.Id) with
        | [] -> [ item "No conclusion recorded" "The investigation has not reached a conclusion." "Open" ]
        | cs ->
            cs
            |> List.map (fun c ->
                item
                    c.Statement.Value
                    (join [ "Strength: " + human (CaseNames.conclusionStrength c.Strength)
                            listOf "Causes" (causeLabel ctx) c.Causes
                            "Residual uncertainty: " + (c.ResidualUncertainty |> Option.map (fun r -> r.Value) |> Option.defaultValue "none stated") ])
                    (human (CaseNames.conclusionStrength c.Strength)))

    let isFinal (inv: Investigation) =
        match inv.Lifecycle with
        | Resolved | Closed -> true
        | Draft | Active | AwaitingEvidence | AwaitingVerification | Archived -> false

    let build (inv: Investigation) (events: Event list) : ReportModel =
        let ctx =
            { Inv = inv
              Order =
                events
                |> List.choose (fun e -> introduces e.Body |> Option.map (fun g -> g, e.Sequence))
                |> List.rev
                |> Map.ofList }
        let final = isFinal inv
        let observation =
            events
            |> List.tryPick (fun e ->
                match e.Body with
                | InvestigationStarted(_, _, original) -> Some original.Value
                | _ -> None)
            |> Option.orElse (inv.Observations |> ordered ctx (fun (o: Observation) -> o.Id) |> List.tryHead |> Option.map (fun o -> o.Original.Value))
            |> Option.defaultValue ""
        let section id title items = { Id = id; Title = title; Items = items }
        { Title = inv.Title.Value
          InvestigationId = Id.text inv.Id
          Kind = (if final then "Final" else "Interim")
          Version = inv.Version
          SnapshotDigest = EventCodec.digest (EventCodec.encode inv.Id events)
          Observation = observation
          Sections =
            [ section "timeline" "Timeline" (timeline ctx events)
              section "hypotheses" "Hypotheses considered" (hypotheses ctx)
              section "evidence" "Evidence" (evidence ctx)
              section "relationships" "Evidence relationships" (relationships ctx)
              section "experiments" "Experiments and findings" (experiments ctx)
              section "unknowns" "Unknowns, assumptions and contradictions" (unknowns ctx)
              section "negative-knowledge" "Eliminated explanations" (negativeKnowledge ctx)
              section "causes" "Root-cause reasoning" (causes ctx)
              section "corrective-actions" "Corrective actions and verification" (correctiveActions ctx)
              section "conclusion" "Conclusion and residual uncertainty" (conclusion ctx) ]
          UnresolvedWork =
            if final then []
            else Policy.resolutionBlockers inv |> List.map (fun b -> item b.Message ("Rule " + b.Rule) b.Code) }
