namespace IndyInitRootCauseInvestigator.Engine

open System
open Aegis

/// R8.3: refusals and invariant violations become Aegis faults at the engine
/// boundary, never hidden exceptions or generic error text (DEC-10).
[<RequireQualifiedAccess>]
module Faults =
    let private config = Operational.aegis

    /// One Aegis fault per domain refusal; the domain error stays the user-facing explanation.
    let ofDomainError (operation: string) (error: DomainError) : Fault =
        let scope = Aegis.scope config operation (Map.ofList [ "rule", ContextValue.Public error.Rule ])
        Aegis.faultOf
            config
            scope
            (FaultCode error.Code)
            FailureCategory.DomainFailure
            FaultSeverity.Warning
            FaultImpact.OperationOnly
            Persistence.Transient
            RecoveryPolicy.NoRecovery
            error.Message
            (InvalidOperationException($"{error.Code}: {error.Message}"))

    let ofDomainErrors operation (errors: DomainError list) = errors |> List.map (ofDomainError operation)
