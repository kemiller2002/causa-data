namespace Causa.Tool.Historical

open System
open IndyInitRootCauseInvestigator.Engine
open Causa.Contract
open Causa.Tool

/// A historical case: research records plus the commands a deterministic builder issues to the real engine.
/// Historical mission time lives only in observation `observedAt`, evidence `capturedAt` and the text; the
/// envelope `occurredAt` is the Causa build clock, never a 1960s or 1980s instant (CONTRACT §5).
type Case =
    { Key: string
      Title: string
      Investigation: InvestigationId
      Start: DateTimeOffset
      ExpectedLifecycle: InvestigationLifecycle
      Research: Research
      Steps: Step list }

/// Raised while assembling a case script when it cites a claim that cannot back it; turned into an Error at the
/// catalog boundary, so no file is ever written for a case that refers to missing or unverified research.
exception CaseDefinitionError of string

/// Deterministic identifiers, actors and claim-backed command builders shared by the three cases.
module Kit =
    /// UUIDv5(url, ".../catalog/historical-v1"): every id is `UUIDv5(ns, "<caseKey>/<kind>/<name>")`.
    let catalogNamespace = Uuid.v5 Uuid.urlNamespace "https://github.com/kemiller2002/causa-data/catalog/historical-v1"

    let guid (caseKey: string) (kind: string) (name: string) = Uuid.v5 catalogNamespace (caseKey + "/" + kind + "/" + name)

    /// No human reviewer has acted in these histories: every command is issued by the deterministic builder from the
    /// committed research records, so both actors are `system` (CONTRACT §5.3; an agent actor could only propose).
    let importer : Actor = { Id = "historical-source-importer"; Kind = ActorKind.System }
    let analyst : Actor = { Id = "historical-catalog-builder"; Kind = ActorKind.System }

    let step (actor: Actor) command : Step = { Actor = actor; Command = command; Expect = Accept }
    let refuse code (actor: Actor) command : Step = { Actor = actor; Command = command; Expect = Refuse code }

    let utc (y, mo, d, h, mi) = DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.Zero)

    let fail message = raise (CaseDefinitionError message)

    /// The id-and-claim vocabulary of one case.
    type Ctx(research: Research) =
        let byId = research.Claims |> List.map (fun c -> c.ClaimId, c) |> Map.ofList
        member _.Key = research.CaseKey
        member _.Research = research
        member _.Guid kind name = guid research.CaseKey kind name

        /// A claim that may back established content (source read and confirmed).
        member _.Claim(claimId: string) : Claim =
            match byId |> Map.tryFind claimId with
            | None -> fail (research.CaseKey + ": the case cites claim " + claimId + ", which is not in claims.json")
            | Some c when not (Research.usable c) -> fail (research.CaseKey + ": claim " + claimId + " is unverified and cannot back the investigation")
            | Some c -> c

        member this.Ev(claimId: string) : EvidenceId =
            this.Claim claimId |> ignore
            Id.create (guid research.CaseKey "evidence" claimId)

        member this.Evs(claimIds: string list) = claimIds |> List.map this.Ev

        /// Admit a claim as evidence (by the importer), then record the researcher's verification assessment.
        member this.Admit(claimId: string) : Step list =
            let c = this.Claim claimId
            let s = research.Sources[c.SourceId]
            let capturedAt = match c.Category with "log" | "testimony" | "measurement" | "directObservation" -> c.HistoricalTime | _ -> None
            [ step importer (AdmitEvidence(this.Ev claimId, TextInput(Research.content c), Research.category c, Research.citation s c, capturedAt))
              step importer (VerifyEvidence(this.Ev claimId, Research.verification c)) ]

        /// Admit every usable claim in claims.json order, except those the script admits later (for example
        /// verification evidence, which must postdate the verification round it satisfies).
        member this.AdmitAllExcept(later: string list) : Step list =
            research.Claims
            |> List.filter (fun c -> Research.usable c && not (List.contains c.ClaimId later))
            |> List.collect (fun c -> this.Admit c.ClaimId)

        /// A chronology entry, recorded verbatim from the claim's paraphrase and stamped with the historical time
        /// the source states (null when the source states none).
        member this.Observe(claimId: string) : Step =
            let c = this.Claim claimId
            step analyst (RecordObservation(Id.create (guid research.CaseKey "observation" claimId), c.Statement + " [" + claimId + "]", c.HistoricalTime))

        member this.Relate(hypothesis: HypothesisId) (kind: RelationshipKind) (rationale: string) (claimId: string) : Step =
            step analyst (RelateEvidence(Id.create (guid research.CaseKey "relationship" (claimId + "->" + string hypothesis.Value)), this.Ev claimId, hypothesis, kind, Some rationale))

        member this.Supports hypothesis rationale (claimIds: string list) = claimIds |> List.map (this.Relate hypothesis Supports rationale)
        member this.Contradicts hypothesis rationale (claimIds: string list) = claimIds |> List.map (this.Relate hypothesis Contradicts rationale)

        /// Claims referenced by the script, for the provenance coverage check.
        member _.UsableClaimIds = research.Claims |> List.filter Research.usable |> List.map (fun c -> c.ClaimId)
