/// The application shell: the Limen wire boundary, routing, the UI state and
/// the projection to a Limen ViewState. Pure: `handle` threads an immutable
/// AppState; entropy (ids) and time arrive in the state via `withContext`, set
/// by the single mutable cell at the WASM export. Legality is never decided
/// here: every command goes through Decide.execute, and every "what can I do
/// now" comes from Transitions and Policy.
module IndyInitRootCauseInvestigator.Engine.Shell

open System
open System.Buffers
open System.Globalization
open System.Text.Json
open System.Text.Json.Nodes

// ---------------------------------------------------------------------------
// JSON output (exact) and input (tolerant)
// ---------------------------------------------------------------------------

type J =
    | JStr of string
    | JNum of int64
    | JBool of bool
    | JNull
    | JArr of J list
    | JObj of (string * J) list

[<RequireQualifiedAccess>]
module JsonOut =
    let rec private write (w: Utf8JsonWriter) (j: J) =
        match j with
        | JStr s -> w.WriteStringValue s
        | JNum n -> w.WriteNumberValue n
        | JBool b -> w.WriteBooleanValue b
        | JNull -> w.WriteNullValue()
        | JArr items ->
            w.WriteStartArray()
            items |> List.iter (write w)
            w.WriteEndArray()
        | JObj fields ->
            w.WriteStartObject()
            fields |> List.iter (fun (k, v) -> w.WritePropertyName k; write w v)
            w.WriteEndObject()

    let render (j: J) : string =
        let buffer = ArrayBufferWriter<byte>()
        use w = new Utf8JsonWriter(buffer)
        write w j
        w.Flush()
        Text.Encoding.UTF8.GetString(buffer.WrittenSpan)

[<RequireQualifiedAccess>]
module JsonIn =
    let parse (text: string) : JsonNode option =
        try
            match JsonNode.Parse text with
            | null -> None
            | node -> Some node
        with _ -> None

    let ofNullable (node: JsonNode | null) : JsonNode option =
        match node with
        | null -> None
        | v -> Some v

    let child (name: string) (node: JsonNode option) : JsonNode option =
        match node with
        | Some(:? JsonObject as o) ->
            if o.ContainsKey name then ofNullable o[name] else None
        | _ -> None

    let private kind (node: JsonNode option) =
        match node with
        | Some(:? JsonValue as v) -> Some(v, v.GetValueKind())
        | _ -> None

    let str (node: JsonNode option) : string option =
        match kind node with
        | Some(v, JsonValueKind.String) -> Some(v.GetValue<string>())
        | _ -> None

    let int (node: JsonNode option) : int option =
        match kind node with
        | Some(v, JsonValueKind.Number) ->
            let d = v.GetValue<double>()
            if d = Math.Floor d && abs d < 2147483647.0 then Some(int d) else None
        | _ -> None

    let bool (node: JsonNode option) : bool option =
        match kind node with
        | Some(_, JsonValueKind.True) -> Some true
        | Some(_, JsonValueKind.False) -> Some false
        | _ -> None

    let list (node: JsonNode option) : JsonNode option list =
        match node with
        | Some(:? JsonArray as a) ->
            [ for item in a ->
                  match item with
                  | null -> None
                  | item -> Some item ]
        | _ -> []

    /// Compact text of an arbitrary node (for an Http body delivered as JSON).
    let text (node: JsonNode option) : string option =
        match node with
        | None -> None
        | Some n ->
            match str (Some n) with
            | Some s -> Some s
            | None -> Some(n.ToJsonString())

// ---------------------------------------------------------------------------
// Routes (hash based, relative; SAF-URL)
// ---------------------------------------------------------------------------

type Screen =
    | HomeScreen
    | WorkspaceScreen of InvestigationId
    /// One addressable thing inside an investigation (R9.6): #/i/{id}/{kind}/{entity}.
    | EntityScreen of InvestigationId * EntityKind * Guid
    | ReportScreen of InvestigationId
    | SettingsScreen
    | FactoryScreen
    | MissingScreen of path: string
    /// A link carrying a reserved parameter (token, code, key, session, ...) is never opened (R9.9).
    | RefusedLinkScreen of parameter: string

[<RequireQualifiedAccess>]
module ScreenRoute =
    /// Path segment for each addressable entity kind.
    let entitySegments =
        [ "h", HypothesisEntity
          "e", EvidenceEntity
          "u", UnknownEntity
          "x", ContradictionEntity
          "t", ActionEntity
          "c", CausalAssertionEntity
          "a", CorrectiveActionEntity
          "k", CriterionEntity
          "p", ProposalEntity ]

    /// Limen's reserved parameter names: never accepted in a link (R9.9).
    let reservedParameters =
        [ "token"; "code"; "key"; "session"; "access_token"; "id_token"; "refresh_token"; "password"; "secret"; "state"; "auth" ]

    let private fragmentOf (location: string) =
        if location.StartsWith("#", StringComparison.Ordinal) then location.Substring 1 else location

    let private pathOf (location: string) =
        let fragment = fragmentOf location
        match fragment.IndexOf '?' with
        | -1 -> fragment
        | index -> fragment.Substring(0, index)

    /// Parameter names in the hash query (values are never read).
    let parameterNames (location: string) : string list =
        let fragment = fragmentOf location
        match fragment.IndexOf '?' with
        | -1 -> []
        | index ->
            fragment.Substring(index + 1).Split('&', StringSplitOptions.RemoveEmptyEntries)
            |> Array.map (fun pair -> (match pair.IndexOf '=' with -1 -> pair | i -> pair.Substring(0, i)).Trim().ToLowerInvariant())
            |> List.ofArray

    let private canonicalId<'Tag> (text: string) : Id<'Tag> option =
        match Id.tryParse<'Tag> text with
        | Some id when Id.text id = text -> Some id
        | _ -> None

    /// Pure and total: a location hash to its screen. Unknown paths and
    /// malformed ids are a value (MissingScreen), never an exception.
    let parse (location: string) : Screen =
        match parameterNames location |> List.tryFind (fun n -> List.contains n reservedParameters) with
        | Some reserved -> RefusedLinkScreen reserved
        | None ->
        let path = pathOf location
        let segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray
        let missing () = MissingScreen(if path.StartsWith("/", StringComparison.Ordinal) then path else "/" + path)
        let exact (parts: string list) = path = "/" + String.Join("/", parts)
        match segments with
        | [] -> HomeScreen
        | [ "settings" ] when exact segments -> SettingsScreen
        | [ "factory" ] when exact segments -> FactoryScreen
        | [ "i"; id ] when exact segments ->
            match canonicalId<InvestigationTag> id with
            | Some iid -> WorkspaceScreen iid
            | None -> missing ()
        | [ "i"; id; "report" ] when exact segments ->
            match canonicalId<InvestigationTag> id with
            | Some iid -> ReportScreen iid
            | None -> missing ()
        | [ "i"; id; segment; entity ] when exact segments ->
            match canonicalId<InvestigationTag> id, List.tryFind (fun (s, _) -> s = segment) entitySegments, canonicalId<InvestigationTag> entity with
            | Some iid, Some(_, kind), Some eid -> EntityScreen(iid, kind, eid.Value)
            | _ -> missing ()
        | _ -> missing ()

    /// Pure: the canonical (relative) location hash of a screen.
    let format (screen: Screen) : string =
        match screen with
        | HomeScreen -> "#/"
        | WorkspaceScreen iid -> "#/i/" + Id.text iid
        | EntityScreen(iid, kind, eid) ->
            let segment = entitySegments |> List.tryFind (fun (_, k) -> k = kind) |> Option.map fst |> Option.defaultValue "h"
            "#/i/" + Id.text iid + "/" + segment + "/" + eid.ToString("D")
        | ReportScreen iid -> "#/i/" + Id.text iid + "/report"
        | SettingsScreen -> "#/settings"
        | FactoryScreen -> "#/factory"
        | MissingScreen path -> "#" + path
        | RefusedLinkScreen _ -> "#/"

    /// The route table published as .echelon/routes.json (name, pattern, path params).
    let table =
        [ "home", "/", []
          "workspace", "/i/{investigation}", [ "investigation" ]
          "report", "/i/{investigation}/report", [ "investigation" ]
          "entity", "/i/{investigation}/{kind}/{entity}", [ "investigation"; "kind"; "entity" ]
          "settings", "/settings", []
          "factory", "/factory", []
          "notFound", "/{*rest}", [ "rest" ] ]

// ---------------------------------------------------------------------------
// Limen wire: inbound messages and outbound effects
// ---------------------------------------------------------------------------

[<RequireQualifiedAccess>]
module Contract =
    let major = 1
    let minor = 4
    let unit = "limen.core"
    let version = 1
    let fingerprint = "sha256:2d5e16b7111fc78a319706b9927e4523cfcc519b7a2c9352ca8283ba32d6b71c"
    let lifecycleId = "limen.lifecycle"
    let lifecycleVersion = 1
    let lifecycleFingerprint = "sha256:cadd4037cac232869a486237ec523e667af9490cdcf3e4fb1ba729cbc5c87978"

type CapabilityOffer = { CapId: string; CapVersion: int; CapFingerprint: string }

type HandshakeOffer =
    { Major: int
      Minor: int
      ContractUnit: string
      ContractVersion: int
      ContractFingerprint: string
      Offers: CapabilityOffer list }

type HttpOutcome =
    | HttpSuccess of status: int * body: string
    | HttpFailure of reason: string * status: int option
    | HttpCancelled
    | HttpOutcomeUnknown of reason: string

type Inbound =
    | Initialize of hash: string * baseUrl: string * handshake: HandshakeOffer option
    | UiEvent of name: string * key: string option * value: string option
    | StorageDone of correlation: string * Result<string option, string>
    | HttpDone of correlation: string * HttpOutcome
    | CapabilityDone of correlation: string * online: bool option
    | NavigationDone of correlation: string
    | ClipboardDone of correlation: string * failure: string option
    | LocationChanged of hash: string
    | ConnectivityChanged of online: bool
    | Unrecognized of reason: string

type Effect =
    | StorageGet of correlation: string * key: string
    | StorageSet of correlation: string * key: string * value: string
    | StorageRemove of correlation: string * key: string
    | HttpCall of correlation: string * httpMethod: string * url: string * headers: (string * string) list * body: string option
    | Navigate of correlation: string * url: string
    | SubscribeConnectivity of correlation: string
    | ClipboardWrite of correlation: string * text: string

type HandshakeReply =
    | NoHandshake
    | Accepted of minor: int * offer: HandshakeOffer * selected: CapabilityOffer list
    | Rejected of J

[<RequireQualifiedAccess>]
module Wire =
    let private hashOf (location: JsonNode option) =
        JsonIn.child "hash" location |> JsonIn.str |> Option.defaultValue ""

    let private offerOf (node: JsonNode option) : HandshakeOffer option =
        match node with
        | None -> None
        | Some _ ->
            let protocol = JsonIn.child "protocol" node
            let contract = JsonIn.child "contract" node
            Some
                { Major = JsonIn.child "major" protocol |> JsonIn.int |> Option.defaultValue 0
                  Minor = JsonIn.child "minor" protocol |> JsonIn.int |> Option.defaultValue 0
                  ContractUnit = JsonIn.child "unit" contract |> JsonIn.str |> Option.defaultValue ""
                  ContractVersion = JsonIn.child "version" contract |> JsonIn.int |> Option.defaultValue 0
                  ContractFingerprint = JsonIn.child "fingerprint" contract |> JsonIn.str |> Option.defaultValue ""
                  Offers =
                    JsonIn.child "capabilities" node
                    |> JsonIn.list
                    |> List.choose (fun c ->
                        match JsonIn.child "id" c |> JsonIn.str, JsonIn.child "version" c |> JsonIn.int, JsonIn.child "fingerprint" c |> JsonIn.str with
                        | Some id, Some v, Some f -> Some { CapId = id; CapVersion = v; CapFingerprint = f }
                        | _ -> None) }

    let private httpOutcome (outcome: JsonNode option) =
        match JsonIn.child "kind" outcome |> JsonIn.str with
        | Some "Success" ->
            HttpSuccess(JsonIn.child "status" outcome |> JsonIn.int |> Option.defaultValue 0, JsonIn.child "body" outcome |> JsonIn.text |> Option.defaultValue "")
        | Some "Failure" ->
            HttpFailure(JsonIn.child "reason" outcome |> JsonIn.str |> Option.defaultValue "network", JsonIn.child "status" outcome |> JsonIn.int)
        | Some "Cancelled" -> HttpCancelled
        | _ -> HttpOutcomeUnknown(JsonIn.child "reason" outcome |> JsonIn.str |> Option.defaultValue "unknown")

    let private effectResult (result: JsonNode option) : Inbound =
        let correlation = JsonIn.child "correlationId" result |> JsonIn.str |> Option.defaultValue ""
        let outcome = JsonIn.child "outcome" result
        match JsonIn.child "kind" result |> JsonIn.str with
        | Some "StorageResult" ->
            match JsonIn.child "kind" outcome |> JsonIn.str with
            | Some "Success" -> StorageDone(correlation, Ok(JsonIn.child "value" outcome |> JsonIn.str))
            | _ -> StorageDone(correlation, Error(JsonIn.child "reason" outcome |> JsonIn.str |> Option.defaultValue "unavailable"))
        | Some "HttpResult" -> HttpDone(correlation, httpOutcome outcome)
        | Some "NavigationResult" -> NavigationDone correlation
        | Some "CapabilityResult" ->
            let online =
                match JsonIn.child "kind" outcome |> JsonIn.str with
                | Some "Completed" -> JsonIn.child "result" outcome |> JsonIn.child "state" |> JsonIn.child "online" |> JsonIn.bool
                | _ -> None
            CapabilityDone(correlation, online)
        | Some "ClipboardResult" ->
            match JsonIn.child "kind" outcome |> JsonIn.str with
            | Some "Success" -> ClipboardDone(correlation, None)
            | _ -> ClipboardDone(correlation, Some(JsonIn.child "reason" outcome |> JsonIn.str |> Option.defaultValue "unknown"))
        | other -> Unrecognized("effect result " + Option.defaultValue "?" other)

    /// Tolerant decoding: anything outside what the shell understands is a value.
    let decode (json: string) : Inbound =
        let root = JsonIn.parse json
        match JsonIn.child "kind" root |> JsonIn.str with
        | Some "Initialize" ->
            let location = JsonIn.child "location" root
            let origin = JsonIn.child "origin" location |> JsonIn.str |> Option.defaultValue ""
            let path = JsonIn.child "path" location |> JsonIn.str |> Option.defaultValue "/"
            Initialize(hashOf location, origin + path, offerOf (JsonIn.child "handshake" root))
        | Some "Event" ->
            let e = JsonIn.child "event" root
            UiEvent(JsonIn.child "name" e |> JsonIn.str |> Option.defaultValue "", JsonIn.child "key" e |> JsonIn.str, JsonIn.child "value" e |> JsonIn.str)
        | Some "EffectResult" -> effectResult (JsonIn.child "result" root)
        | Some "LocationChanged" -> LocationChanged(hashOf (JsonIn.child "location" root))
        | Some "CapabilityFact" ->
            let fact = JsonIn.child "fact" root
            match JsonIn.child "capability" root |> JsonIn.str, JsonIn.child "kind" fact |> JsonIn.str with
            | Some id, Some "ConnectivityChanged" when id = Contract.lifecycleId ->
                match JsonIn.child "online" fact |> JsonIn.bool with
                | Some online -> ConnectivityChanged online
                | None -> Unrecognized "connectivity fact without online"
            | _ -> Unrecognized "capability fact"
        | Some other -> Unrecognized other
        | None -> Unrecognized "no kind"

    /// The handshake answer (DF-LIMEN-2026-0001): accept protocol major 1 at
    /// the highest common minor, echo the core contract exactly, and select the
    /// lifecycle capability exactly as offered when it is offered.
    let negotiate (offer: HandshakeOffer option) : HandshakeReply =
        match offer with
        | None -> NoHandshake
        | Some o when o.Major <> Contract.major ->
            Rejected(JObj [ "kind", JStr "ProtocolUnsupported"; "offered", JObj [ "major", JNum(int64 o.Major); "minor", JNum(int64 o.Minor) ] ])
        | Some o when o.ContractUnit <> Contract.unit || o.ContractVersion <> Contract.version || o.ContractFingerprint <> Contract.fingerprint ->
            let identity unit (version: int) (fp: string) = JObj [ "unit", JStr unit; "version", JNum(int64 version); "fingerprint", JStr fp ]
            Rejected(
                JObj
                    [ "kind", JStr "ContractMismatch"
                      "expected", identity Contract.unit Contract.version Contract.fingerprint
                      "offered", identity o.ContractUnit o.ContractVersion o.ContractFingerprint ]
            )
        | Some o ->
            let selected =
                o.Offers
                |> List.filter (fun c -> c.CapId = Contract.lifecycleId && c.CapVersion = Contract.lifecycleVersion && c.CapFingerprint = Contract.lifecycleFingerprint)
                |> List.truncate 1
            Accepted(min o.Minor Contract.minor, o, selected)

    let private effectJson (effect: Effect) : J =
        match effect with
        | StorageGet(c, key) -> JObj [ "kind", JStr "Storage"; "operation", JStr "get"; "correlationId", JStr c; "key", JStr key ]
        | StorageSet(c, key, value) -> JObj [ "kind", JStr "Storage"; "operation", JStr "set"; "correlationId", JStr c; "key", JStr key; "value", JStr value ]
        | StorageRemove(c, key) -> JObj [ "kind", JStr "Storage"; "operation", JStr "remove"; "correlationId", JStr c; "key", JStr key ]
        | HttpCall(c, m, url, headers, body) ->
            JObj(
                [ "kind", JStr "Http"; "correlationId", JStr c; "method", JStr m; "url", JStr url ]
                @ (if headers.IsEmpty then [] else [ "headers", JObj(headers |> List.map (fun (k, v) -> k, JStr v)) ])
                @ (match body with Some b -> [ "body", JStr b ] | None -> [])
                @ [ "timeoutMs", JNum 20000L; "response", JStr "text" ]
            )
        | ClipboardWrite(c, text) -> JObj [ "kind", JStr "Clipboard"; "correlationId", JStr c; "operation", JStr "writeText"; "text", JStr text ]
        | Navigate(c, url) -> JObj [ "kind", JStr "Navigation"; "operation", JStr "push"; "correlationId", JStr c; "url", JStr url ]
        | SubscribeConnectivity c ->
            JObj
                [ "kind", JStr "Capability"
                  "correlationId", JStr c
                  "capability", JStr Contract.lifecycleId
                  "version", JNum(int64 Contract.lifecycleVersion)
                  "request", JObj [ "operation", JStr "subscribe"; "topics", JArr [ JStr "connectivity" ] ] ]

    let private handshakeJson (reply: HandshakeReply) : (string * J) list =
        match reply with
        | NoHandshake -> []
        | Rejected reason -> [ "handshake", JObj [ "kind", JStr "Rejected"; "reason", reason ] ]
        | Accepted(minor, o, selected) ->
            [ "handshake",
              JObj
                  [ "kind", JStr "Accepted"
                    "protocol", JObj [ "major", JNum(int64 Contract.major); "minor", JNum(int64 minor) ]
                    "contract", JObj [ "unit", JStr o.ContractUnit; "version", JNum(int64 o.ContractVersion); "fingerprint", JStr o.ContractFingerprint ]
                    "capabilities",
                    JArr(selected |> List.map (fun c -> JObj [ "id", JStr c.CapId; "version", JNum(int64 c.CapVersion); "fingerprint", JStr c.CapFingerprint ])) ] ]

    /// Exact output: exactly view, effects, cancellations and (on Initialize) handshake.
    let encode (view: (string * J) list) (effects: Effect list) (reply: HandshakeReply) : string =
        JsonOut.render (JObj([ "view", JObj view; "effects", JArr(effects |> List.map effectJson); "cancellations", JArr [] ] @ handshakeJson reply))

// ---------------------------------------------------------------------------
// Plain-language vocabulary (R16.1: no state-machine jargon on screen)
// ---------------------------------------------------------------------------

[<RequireQualifiedAccess>]
module Words =
    let lifecycle (s: InvestigationLifecycle) =
        match s with
        | Draft -> "Draft"
        | Active -> "In progress"
        | AwaitingEvidence -> "Waiting for evidence"
        | AwaitingVerification -> "Waiting for verification"
        | Resolved -> "Resolved"
        | Closed -> "Closed"
        | Archived -> "Archived"

    let hypothesis (s: HypothesisState) =
        match s with
        | Proposed -> "Proposed"
        | Plausible -> "Plausible"
        | Supported -> "Supported by evidence"
        | Weakened -> "Weakened"
        | Falsified -> "Ruled out"
        | Superseded -> "Replaced"

    let hypothesisTone (s: HypothesisState) =
        match s with
        | Supported -> "ok"
        | Proposed | Plausible -> "unknown"
        | Weakened -> "attention"
        | Falsified | Superseded -> "blocked"

    let allHypothesisStates = [ Proposed; Plausible; Supported; Weakened; Falsified; Superseded ]

    /// (event-name suffix, command, button label)
    let hypothesisCommands =
        [ "MarkPlausible", MarkPlausible, "Mark plausible"
          "MarkSupported", MarkSupported, "Mark supported"
          "MarkWeakened", MarkWeakened, "Mark weakened"
          "Falsify", Falsify, "Rule out (falsify)"
          "Supersede", Supersede, "Mark replaced"
          "Reopen", Reopen, "Reopen" ]

    let unknown (s: UnknownState) =
        match s with
        | Identified -> "Open"
        | Investigating -> "Being investigated"
        | UnknownResolved -> "Answered"
        | AcceptedResidual -> "Accepted as remaining uncertainty"
        | UnknownSuperseded -> "Replaced"

    let unknownCommands =
        [ "Start", StartInvestigatingUnknown, "Start investigating"
          "Resolve", ResolveUnknown, "Mark answered"
          "Accept", AcceptUnknownAsResidual, "Accept as remaining uncertainty" ]

    let materiality (m: Materiality) =
        match m with
        | Blocking -> "Blocking"
        | Material -> "Important"
        | NonMaterial -> "Minor"
        | Informational -> "For information"

    let materialities = [ "Blocking", Blocking; "Material", Material; "NonMaterial", NonMaterial; "Informational", Informational ]

    let contradiction (s: ContradictionState) =
        match s with
        | Detected -> "Open"
        | Triaged -> "Reviewed"
        | ContradictionInvestigating -> "Being investigated"
        | ContradictionResolved -> "Explained"
        | ContradictionAcceptedResidual -> "Accepted as remaining uncertainty"
        | ContradictionSuperseded -> "Replaced"

    let contradictionCommands =
        [ "Triage", TriageContradiction, "Mark reviewed"
          "Resolve", ResolveContradiction, "Mark explained"
          "Accept", AcceptContradictionAsResidual, "Accept as remaining uncertainty" ]

    let action (s: ActionState) =
        match s with
        | ActionProposed -> "Proposed"
        | ActionApproved -> "Approved"
        | ActionInProgress -> "In progress"
        | ActionCompleted -> "Done"
        | ActionInterpreted -> "Interpreted"
        | ActionRejected -> "Rejected"
        | ActionCancelled -> "Cancelled"

    let actionCommands =
        [ "Approve", ApproveAction, "Approve"
          "Start", StartAction, "Start"
          "Reject", RejectAction, "Reject"
          "Cancel", CancelAction, "Cancel" ]

    let actionKind (k: ActionKind) =
        match k with
        | Test -> "Test"
        | Inspection -> "Inspection"
        | Query -> "Query"
        | ActionKind.Comparison -> "Comparison"
        | Experiment -> "Experiment"

    let actionKinds = [ "Test", Test; "Inspection", Inspection; "Query", Query; "Comparison", ActionKind.Comparison; "Experiment", Experiment ]

    let cause (s: CausalAssertionState) =
        match s with
        | CauseProposed -> "Proposed"
        | CauseConfirmed -> "Confirmed"
        | CauseReopened -> "Reopened"
        | CauseSuperseded -> "Replaced"

    let role (r: CauseRole) =
        match r with
        | RootCause -> "Root cause"
        | ContributingFactor -> "Contributing factor"
        | Trigger -> "Trigger"
        | EnablingCondition -> "Enabling condition"

    let correction (s: CorrectiveActionState) =
        match s with
        | CorrectionProposed -> "Proposed"
        | CorrectionApproved -> "Approved"
        | CorrectionInProgress -> "Being implemented"
        | Implemented -> "Implemented (not yet verified)"
        | AwaitingVerificationOfEffect -> "Waiting for verification"
        | VerifiedEffective -> "Verified effective"
        | VerificationFailed -> "Verification failed"
        | ReworkRequired -> "Needs rework"
        | CorrectionCancelled -> "Cancelled"

    let correctionCommands =
        [ "Approve", ApproveCorrection, "Approve"
          "Start", StartCorrection, "Start work"
          "Implemented", MarkImplemented, "Mark implemented"
          "Verify", StartVerificationOfEffect, "Start verification"
          "Rework", RequireRework, "Require rework"
          "Cancel", CancelCorrection, "Cancel" ]

    let correctionKind (k: CorrectiveActionKind) =
        match k with
        | Mitigation -> "Temporary mitigation"
        | Workaround -> "Workaround"
        | Correction -> "Root-cause correction"
        | Prevention -> "Prevention"

    let correctionKinds = [ "Correction", Correction; "Mitigation", Mitigation; "Workaround", Workaround; "Prevention", Prevention ]

    let category (c: EvidenceCategory) =
        match c with
        | DirectObservation -> "Direct observation"
        | Measurement -> "Measurement"
        | Log -> "Log"
        | Document -> "Document"
        | ExperimentResult -> "Experiment result"
        | Testimony -> "Testimony"
        | ExternalReference -> "External reference"

    let categories =
        [ "DirectObservation", DirectObservation; "Measurement", Measurement; "Log", Log; "Document", Document
          "ExperimentResult", ExperimentResult; "Testimony", Testimony; "ExternalReference", ExternalReference ]

    let verificationStatus (v: VerificationStatus) =
        match v with
        | Unverified -> "Not checked"
        | PartiallyVerified -> "Partly checked"
        | Verified -> "Checked"
        | Invalidated -> "Shown wrong"

    let relationship (k: RelationshipKind) =
        match k with
        | Supports -> "supports"
        | Contradicts -> "contradicts"
        | Discriminates -> "helps tell apart"
        | Insufficient -> "is not enough to judge"

    let relationships = [ "Supports", Supports; "Contradicts", Contradicts; "Discriminates", Discriminates; "Insufficient", Insufficient ]

    let strength (s: PredictionStrength) =
        match s with
        | Necessary -> "Must happen"
        | Expected -> "Expected"
        | DiscriminatingPrediction -> "Tells hypotheses apart"
        | NonDiscriminating -> "Does not tell apart"

    let strengths = [ "Expected", Expected; "Necessary", Necessary; "DiscriminatingPrediction", DiscriminatingPrediction; "NonDiscriminating", NonDiscriminating ]

    let outcome (o: VerificationOutcome) =
        match o with
        | Satisfied -> "Criterion met"
        | Failed -> "Criterion not met"
        | Inconclusive -> "Inconclusive"

    let outcomes = [ "Satisfied", Satisfied; "Failed", Failed; "Inconclusive", Inconclusive ]

    let conclusion (s: ConclusionStrength) =
        match s with
        | PlausibleExplanation -> "Plausible explanation"
        | LeadingExplanation -> "Leading explanation"
        | SupportedCause -> "Supported cause"
        | ConfirmedCause -> "Confirmed cause"
        | AcceptedWithResidualUncertainty -> "Accepted with remaining uncertainty"

    let conclusions =
        [ "ConfirmedCause", ConfirmedCause; "SupportedCause", SupportedCause; "LeadingExplanation", LeadingExplanation
          "PlausibleExplanation", PlausibleExplanation; "AcceptedWithResidualUncertainty", AcceptedWithResidualUncertainty ]

    let proposal (s: ProposalState) =
        match s with
        | ProposalActive -> "Waiting for your decision"
        | ProposalAccepted -> "Accepted"
        | ProposalRejected -> "Rejected"

    let actor (a: Actor) =
        match a.Kind with
        | Human -> "You"
        | Agent(_, "deterministic-rules") -> "Rule-based helper (not AI)"
        | Agent _ -> "AI suggestion"
        | System -> "System"

    let date (d: DateTimeOffset) = d.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
    let day (d: DateTimeOffset) = d.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)

    let join (sep: string) (items: string list) = String.Join(sep, items)

    let clip (n: int) (s: string) = if s.Length <= n then s else s.Substring(0, n - 1) + "…"

    let states (names: string list) =
        match names with
        | [] -> "never"
        | [ one ] -> one
        | many -> join ", " (List.take (many.Length - 1) many) + " or " + List.last many

/// A guard used by the projection: the lifecycle commands the kernel table knows.
[<RequireQualifiedAccess>]
module LifecycleCommands =
    let all =
        [ "activate", ActivateInvestigation, "Mark in progress"
          "awaitEvidence", MarkAwaitingEvidence "waiting", "Mark waiting for evidence"
          "awaitVerification", MarkAwaitingVerification, "Mark waiting for verification"
          "resolve", ResolveInvestigation, "Resolve investigation"
          "close", CloseInvestigation, "Close"
          "reopen", ReopenInvestigation "reopen", "Reopen investigation" ]

    let legal (state: InvestigationLifecycle) (command: Command) = Transitions.lifecycle state command |> Option.isSome

// ---------------------------------------------------------------------------
// Application state
// ---------------------------------------------------------------------------

/// A refusal as shown to the person: the domain explanation plus its Aegis fault code (R8.2, R8.3).
type Refusal =
    { Code: string
      Message: string
      Rule: string
      FaultCode: string
      Operation: string }

type LocalSave =
    | Saving
    | SavedLocally
    | SaveFailed of reason: string

/// The factory/proof data published with the build (dist/factory.json).
type FactoryRequirement =
    { ReqId: string
      ReqTitle: string
      ReqScope: string
      ReqSlice: string
      /// "tested" | "untested" | "failing" | "" (no test run recorded)
      Coverage: string }

/// Totals of the recorded test run (test-results/traceability.json).
type FactoryTests =
    { RunCommit: string
      Total: int
      Passed: int
      Failed: int
      Tested: int
      Untested: int
      Failing: int
      /// Which suites the recorded run covers (e.g. "engine"); "" when unknown.
      Suites: string }

type FactoryInfo =
    { Label: string
      Commit: string
      BuiltAt: string
      Experiment: string
      Requirements: FactoryRequirement list
      /// id, title, status
      WorkItems: (string * string * string) list
      Tests: FactoryTests option
      /// "passed" | "failing" | "unknown", from the recorded test run.
      BuildStatus: string }

type FactoryState =
    | FactoryNotRequested
    | FactoryLoading
    | FactoryLoaded of FactoryInfo
    | FactoryFailed of reason: string

/// What an outstanding correlation id was for.
type PendingOp =
    | LoadSettings
    | LoadIndex
    | LoadSync
    | LoadInvestigation of InvestigationId
    | SaveInvestigation of InvestigationId
    | SaveIndex
    | SaveSettings
    | SaveSync
    /// The remote identity travels with the request: a result for another
    /// repository (settings changed meanwhile) is never adopted (D-009).
    | RemoteWrite of InvestigationId * remote: string
    | RemoteRead of InvestigationId * remote: string
    | Subscribe
    | NavigationAck
    | CopyLink of url: string
    | FactoryLoad

type AppState =
    { Investigations: Map<InvestigationId, Event list>
      Screen: Screen
      Drafts: Map<string, string>
      Refusals: Refusal list
      Notice: string
      Online: bool
      /// True once the local index and every investigation it names were read.
      Loaded: bool
      Local: Map<InvestigationId, LocalSave>
      Pending: Map<string, PendingOp>
      NextCorrelation: int64
      /// Entropy for new ids, supplied per message at the boundary.
      Entropy: Guid
      IdCounter: int
      Now: DateTimeOffset
      LifecycleNegotiated: bool
      /// origin + path of the page, for absolute share links (never carries a query).
      BaseUrl: string
      Factory: FactoryState
      Sync: Map<InvestigationId, SyncStatus>
      RemoteSha: Map<InvestigationId, string>
      /// Event count included in the write in flight (to detect changes made meanwhile).
      SentLength: Map<InvestigationId, int>
      SyncDetail: Map<InvestigationId, string>
      Remote: RemoteConfig option
      /// Index entries as read from this device, kept even when their investigation
      /// could not be decoded, so a save never drops them (R10.7).
      StoredIndex: Map<InvestigationId, IndexCodec.Entry>
      Unreadable: Map<InvestigationId, string>
      /// The saved index could not be read: it is never overwritten.
      IndexUnreadable: bool
      /// A change happened before the initial load finished; the index is written once it has.
      IndexDirty: bool
      /// The remote identity the saved sync statuses were confirmed against.
      SyncRemote: string option }

let initial: AppState =
    { Investigations = Map.empty
      Screen = HomeScreen
      Drafts = Map.empty
      Refusals = []
      Notice = ""
      Online = true
      Loaded = false
      Local = Map.empty
      Pending = Map.empty
      NextCorrelation = 1L
      Entropy = Guid.Empty
      IdCounter = 0
      Now = DateTimeOffset.UnixEpoch
      LifecycleNegotiated = false
      BaseUrl = ""
      Factory = FactoryNotRequested
      Sync = Map.empty
      RemoteSha = Map.empty
      SentLength = Map.empty
      SyncDetail = Map.empty
      Remote = None
      StoredIndex = Map.empty
      Unreadable = Map.empty
      IndexUnreadable = false
      IndexDirty = false
      SyncRemote = None }

/// The boundary supplies entropy and the clock for one message; handle stays pure.
let withContext (entropy: Guid) (now: DateTimeOffset) (state: AppState) : AppState =
    { state with Entropy = entropy; IdCounter = 0; Now = now }

[<RequireQualifiedAccess>]
module private Ids =
    let next (st: AppState) : Guid * AppState =
        let n = st.IdCounter + 1
        let counter = BitConverter.GetBytes n
        let bytes = st.Entropy.ToByteArray() |> Array.mapi (fun i b -> if i < 4 then b ^^^ counter[i] else b)
        let g = Guid bytes
        // Guid.Empty is not a valid entity id; perturb deterministically.
        let g = if g = Guid.Empty then Guid("00000000-0000-0000-0000-000000000001") else g
        g, { st with IdCounter = n }

    let correlation (st: AppState) (op: PendingOp) : string * AppState =
        let c = "c" + st.NextCorrelation.ToString(CultureInfo.InvariantCulture)
        c, { st with NextCorrelation = st.NextCorrelation + 1L; Pending = st.Pending |> Map.add c op }

[<RequireQualifiedAccess>]
module Storage =
    let indexKey = "rci:index"
    let settingsKey = "rci:settings"
    let syncKey = "rci:sync"
    let investigationKey (iid: InvestigationId) = "rci:inv:" + Id.text iid
// ---------------------------------------------------------------------------
// Reading the current investigation and the drafts
// ---------------------------------------------------------------------------

let private human = Actor.human "local-user"

/// AI proposals are entered by a person on the AI's behalf (no live model).
let private assistant = Actor.agent "ai-assistant" "none" "manual-entry"

/// The "Challenge the leading explanation" helper: fixed rules, no model.
/// Like any agent it can only record proposals (D-004); a person decides.
let private challenger = Actor.agent "challenge-helper" "none" "deterministic-rules"

let investigationOf (st: AppState) (iid: InvestigationId) : Investigation option =
    st.Investigations |> Map.tryFind iid |> Option.bind Evolve.replay

let currentId (st: AppState) : InvestigationId option =
    match st.Screen with
    | WorkspaceScreen iid | ReportScreen iid | EntityScreen(iid, _, _) when st.Investigations.ContainsKey iid -> Some iid
    | _ -> None

let private draft (st: AppState) (name: string) = st.Drafts |> Map.tryFind name |> Option.defaultValue ""

let private optDraft (st: AppState) (name: string) =
    let v = draft st name
    if String.IsNullOrWhiteSpace v then None else Some v

let private guidDraft (st: AppState) (name: string) : Guid option =
    match Guid.TryParse(draft st name) with
    | true, g when g <> Guid.Empty -> Some g
    | _ -> None

let private choice (table: (string * 'T) list) (fallback: 'T) (value: string) =
    table |> List.tryFind (fun (code, _) -> code = value) |> Option.map snd |> Option.defaultValue fallback

let private dateDraft (st: AppState) (name: string) : DateTimeOffset option =
    match DateTimeOffset.TryParseExact(draft st name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal) with
    | true, d -> Some d
    | _ -> None

let private clear (fields: string list) (st: AppState) =
    { st with Drafts = fields |> List.fold (fun d f -> Map.remove f d) st.Drafts }

/// Every draft field the workspace binds (data-bind-value), so the projection is total.
/// The access token is deliberately absent: it is never projected (R9.9, R11.3).
let draftFields =
    [ "title"; "observation"
      "hypStatement"; "hypMechanism"; "hypReason"; "hypEvidence"
      "aiText"; "aiRationale"; "aiReason"
      "evKind"; "evContent"; "evCategory"; "evSource"; "evCaptured"
      "supOld"; "supNew"; "supReason"
      "relEvidence"; "relHypothesis"; "relKind"; "relRationale"
      "unkQuestion"; "unkMateriality"; "unkBasis"
      "conStatement"; "conEvidence"; "conMateriality"; "conBasis"
      "predHypothesis"; "predStatement"; "predStrength"
      "actKind"; "actPurpose"; "actHypothesis"
      "findAction"; "findResult"; "findInterpretation"
      "negStatement"; "negScope"; "negEvidence"
      "causeOutcome"; "causeRoot"; "causeFactor"; "causeMechanism"
      "corrKind"; "corrDescription"; "corrCause"
      "critCorrection"; "critExpected"; "critMethod"
      "verCriterion"; "verOutcome"; "verEvidence"
      "conclStatement"; "conclStrength"; "conclCause"; "conclResidual"
      "reopenBasis"
      "setOwner"; "setRepo"; "setBranch" ]

// ---------------------------------------------------------------------------
// Persistence and sync plumbing (effects only; results arrive later)
// ---------------------------------------------------------------------------

let private entryOf (iid: InvestigationId) (events: Event list) : IndexCodec.Entry option =
    match Evolve.replay events, List.tryLast events with
    | Some inv, Some last -> Some { Id = iid; Title = inv.Title.Value; Lifecycle = Words.lifecycle inv.Lifecycle; Version = inv.Version; UpdatedAt = last.At }
    | _ -> None

let private storageSet (op: PendingOp) (key: string) (value: string) (st: AppState) : AppState * Effect list =
    let c, st = Ids.correlation st op
    st, [ StorageSet(c, key, value) ]

let private syncCode (status: SyncStatus) =
    match status with
    | LocalOnly -> "local"
    | Synced -> "synced"
    | Conflicted _ -> "conflicted"
    // Sending and unknown outcomes restart as pending: never as synced (R10.6, R10.7).
    | Pending | Sending | SyncUnknown -> "pending"

/// Which repository and branch a sync status refers to.
let remoteIdentity (config: RemoteConfig) = config.Owner + "/" + config.Repository + "@" + config.Branch

let private currentIdentity (st: AppState) = st.Remote |> Option.map remoteIdentity |> Option.defaultValue ""

/// "rci:sync": per investigation, the remote status and last known blob sha,
/// plus the repository they were confirmed against.
let syncDocument (st: AppState) : string =
    JsonOut.render (
        JObj
            [ "schema", JStr "rci.sync/v1"
              "remote", JStr(currentIdentity st)
              "investigations",
              JObj
                  [ for KeyValue(iid, status) in st.Sync ->
                        Id.text iid,
                        JObj(
                            [ "status", JStr(syncCode status) ]
                            @ (match status with Conflicted d -> [ "detail", JStr d ] | _ -> [])
                            @ (match Map.tryFind iid st.RemoteSha with Some sha -> [ "sha", JStr sha ] | None -> [])
                        ) ] ]
    )

let private readSync (text: string) (st: AppState) : AppState =
    let root = JsonIn.parse text
    let st = { st with SyncRemote = JsonIn.child "remote" root |> JsonIn.str }
    match JsonIn.child "investigations" root with
    | Some(:? JsonObject as o) ->
        o
        |> Seq.fold
            (fun (s: AppState) (KeyValue(key, value)) ->
                match Id.tryParse<InvestigationTag> key, JsonIn.ofNullable value with
                | Some _, None -> s
                | Some iid, n ->
                    let status =
                        match JsonIn.child "status" n |> JsonIn.str with
                        | Some "synced" -> Synced
                        | Some "conflicted" -> Conflicted(JsonIn.child "detail" n |> JsonIn.str |> Option.defaultValue "Remote history differs.")
                        | Some "local" -> LocalOnly
                        | _ -> Pending
                    let sha = JsonIn.child "sha" n |> JsonIn.str
                    { s with
                        Sync = s.Sync |> Map.add iid status
                        RemoteSha = match sha with Some v -> s.RemoteSha |> Map.add iid v | None -> s.RemoteSha }
                | None, _ -> s)
            st
    | _ -> st

/// "rci:settings" holds the repository only. The access token is never
/// persisted: it lives in engine memory for this session (R11.3).
let settingsDocument (config: RemoteConfig) : string =
    JsonOut.render (JObj [ "owner", JStr config.Owner; "repository", JStr config.Repository; "branch", JStr config.Branch ])

let private readSettings (text: string) : RemoteConfig option =
    let root = JsonIn.parse text
    let field name = JsonIn.child name root |> JsonIn.str
    match field "owner", field "repository" with
    | Some owner, Some repo when owner <> "" && repo <> "" ->
        // A token stored by an older build is ignored, never adopted.
        Some { Owner = owner; Repository = repo; Branch = field "branch" |> Option.defaultValue "main"; Token = "" }
    | _ -> None

let private hasToken (st: AppState) = st.Remote |> Option.exists (fun r -> r.Token <> "")

/// Reset every investigation's remote state: nothing confirmed for another
/// repository (or none) counts here (D-009/I-18).
let private resetRemoteState (st: AppState) : AppState =
    let status = if st.Remote.IsSome then Pending else LocalOnly
    { st with
        Sync = st.Investigations |> Map.map (fun _ _ -> status)
        RemoteSha = Map.empty
        SyncDetail = Map.empty
        SentLength = Map.empty
        SyncRemote = Some(currentIdentity st) }

let private saveSyncState (st: AppState) = storageSet SaveSync Storage.syncKey (syncDocument st) st

let private httpOf (c: string) (request: HttpRequest) = HttpCall(c, request.Method, request.Url, request.Headers, request.Body)

let private put (iid: InvestigationId) (sha: string option) (st: AppState) : AppState * Effect list =
    match st.Remote, Map.tryFind iid st.Investigations with
    | Some config, Some events ->
        let title = investigationOf st iid |> Option.map (fun i -> i.Title.Value) |> Option.defaultValue "investigation"
        let request = GitHub.writeRequest config iid (EventCodec.encode iid events) sha ("Root-cause investigation: " + Words.clip 60 title + " (version " + string events.Length + ")")
        let c, st = Ids.correlation st (RemoteWrite(iid, remoteIdentity config))
        { st with Sync = st.Sync |> Map.add iid Sending; SentLength = st.SentLength |> Map.add iid events.Length }, [ httpOf c request ]
    | _ -> st, []

/// Send a pending investigation when online and configured; otherwise it stays pending.
let private sendIfReady (iid: InvestigationId) (st: AppState) : AppState * Effect list =
    match st.Remote, Map.tryFind iid st.Sync with
    | Some config, Some Pending when st.Online && config.Token <> "" ->
        match Map.tryFind iid st.RemoteSha with
        | Some sha -> put iid (Some sha) st
        | None ->
            let c, st = Ids.correlation st (RemoteRead(iid, remoteIdentity config))
            { st with Sync = st.Sync |> Map.add iid Sending; SentLength = st.SentLength |> Map.add iid 0 }, [ httpOf c (GitHub.readRequest config iid) ]
    | _ -> st, []

let private sendAllPending (st: AppState) : AppState * Effect list =
    st.Sync
    |> Map.toList
    |> List.filter (fun (_, s) -> s = Pending)
    |> List.fold (fun (s, fx) (iid, _) -> let s, more = sendIfReady iid s in s, fx @ more) (st, [])

/// After every successful command: save locally (investigation + index), then mark
/// the remote copy pending and try to send it (R10.5, R10.6).
/// The index to store: what was read from this device (including entries whose
/// investigation could not be decoded) merged with what is in memory. Written
/// only after the initial load, and never over an unreadable index (R10.7).
let private saveIndex (st: AppState) : AppState * Effect list =
    if st.IndexUnreadable then st, []
    elif not st.Loaded then { st with IndexDirty = true }, []
    else
        let entries =
            st.Investigations
            |> Map.fold (fun m i evs -> match entryOf i evs with Some e -> Map.add i e m | None -> m) st.StoredIndex
        let st = { st with StoredIndex = entries; IndexDirty = false }
        storageSet SaveIndex Storage.indexKey (IndexCodec.encode (entries |> Map.toList |> List.map snd)) st

let private persistAfterChange (iid: InvestigationId) (st: AppState) : AppState * Effect list =
    match Map.tryFind iid st.Investigations with
    | None -> st, []
    | Some events ->
        let st = { st with Local = st.Local |> Map.add iid Saving }
        let st, saveInv = storageSet (SaveInvestigation iid) (Storage.investigationKey iid) (EventCodec.encode iid events) st
        let st, saveIndex = saveIndex st
        match st.Remote with
        | None -> st, saveInv @ saveIndex
        | Some _ ->
            let st =
                match Map.tryFind iid st.Sync with
                | Some Sending -> st // the write in flight is followed by another once it lands
                | _ -> { st with Sync = st.Sync |> Map.add iid Pending }
            let st, send = sendIfReady iid st
            let st, saveSync = saveSyncState st
            st, saveInv @ saveIndex @ saveSync @ send

let private saveSettings (config: RemoteConfig option) (st: AppState) : AppState * Effect list =
    let previous = st.Remote
    let st = { st with Remote = config }
    match config with
    | None ->
        let c, st = Ids.correlation st SaveSettings
        let st = resetRemoteState st
        let st, saveSync = saveSyncState st
        st, [ StorageRemove(c, Storage.settingsKey) ] @ saveSync
    | Some cfg ->
        let st, save = storageSet SaveSettings Storage.settingsKey (settingsDocument cfg) st
        let sameRemote = previous |> Option.exists (fun p -> remoteIdentity p = remoteIdentity cfg && p.Token <> "")
        let st =
            if sameRemote then
                // Same repository and branch: what GitHub confirmed still holds; the rest is pending.
                { st with
                    Sync =
                        st.Investigations
                        |> Map.fold
                            (fun m iid _ ->
                                match Map.tryFind iid m with
                                | Some Synced | Some Sending | Some(Conflicted _) -> m
                                | _ -> Map.add iid Pending m)
                            st.Sync }
            else resetRemoteState st
        let st, send = sendAllPending st
        let st, saveSync = saveSyncState st
        st, save @ saveSync @ send

let private retrySync (st: AppState) : AppState * Effect list =
    match currentId st with
    | Some iid ->
        // Re-read GitHub before writing again: a conflict is never overwritten blindly.
        let st =
            match Map.tryFind iid st.Sync with
            | Some(Conflicted _) | Some SyncUnknown | Some Pending -> { st with Sync = st.Sync |> Map.add iid Pending; RemoteSha = st.RemoteSha |> Map.remove iid; SyncDetail = st.SyncDetail |> Map.remove iid }
            | _ -> st
        let st, send = sendIfReady iid st
        let st, save = saveSyncState st
        { st with Notice = (if send.IsEmpty then "Cannot reach GitHub now; changes stay pending." else "Checking GitHub…") }, save @ send
    | None -> st, []

// ---------------------------------------------------------------------------
// Commands: the only path is Decide.execute (stop-the-line rule)
// ---------------------------------------------------------------------------

let private refusalsOf (operation: string) (errors: DomainError list) : Refusal list =
    errors
    |> List.map (fun e ->
        let fault = Faults.ofDomainError operation e
        let (Aegis.FaultCode code) = fault.Code
        { Code = e.Code; Message = e.Message; Rule = e.Rule; FaultCode = code; Operation = operation })

/// Execute one command against an investigation; on success persist and sync.
// ---------------------------------------------------------------------------
// "Challenge the leading explanation": a deterministic, rule-based helper
// (R15.1, R15.3, R4.3). It is not a language model. Its only output is an
// agent proposal through Decide; a person accepts or rejects it.
// ---------------------------------------------------------------------------

[<RequireQualifiedAccess>]
module Challenge =
    let private standing (h: Hypothesis) =
        match h.State with
        | Proposed | Plausible | Weakened -> true
        | Supported | Falsified | Superseded -> false

    /// Rule: one or more hypotheses are Supported while alternatives still stand.
    /// Returns the proposal text and rationale for the first leading hypothesis.
    let suggestion (inv: Investigation) : (string * string) option =
        let all = inv.Hypotheses |> Map.toList |> List.map snd |> List.sortBy (fun h -> h.Provenance.RecordedAt)
        let alternatives = all |> List.filter standing
        match all |> List.tryFind (fun h -> h.State = Supported), alternatives with
        | Some leading, (_ :: _) ->
            let others = Words.join "; " (alternatives |> List.map (fun h -> "\"" + Words.clip 60 h.Statement.Value + "\""))
            Some(
                "What result would falsify \"" + leading.Statement.Value + "\"? Plan a check whose outcome would differ if " + others + " were true instead.",
                "Rule-based helper: a supported explanation with alternatives still standing should face a check that could prove it wrong (R15.3, R4.3)."
            )
        | _ -> None

    /// The same suggestion is not offered twice while it awaits a decision.
    let available (inv: Investigation) =
        match suggestion inv with
        | Some(text, _) -> not (inv.Proposals |> Map.exists (fun _ p -> p.State = ProposalActive && p.Text.Value = text))
        | None -> false

    /// R15.1: questions before conclusions, one per Policy cause blocker.
    let question (e: DomainError) =
        match e.Code with
        | "CAUSE.NO_SUPPORTING_EVIDENCE" -> "Which evidence directly supports this member of the cause? (" + e.Message + ")"
        | "CAUSE.MEMBER_NOT_SUPPORTED" -> "What evidence would move this hypothesis to supported — or rule it out? (" + e.Message + ")"
        | "CAUSE.ALTERNATIVE_NOT_ADDRESSED" -> "What observation would rule the alternative in or out? (" + e.Message + ")"
        | "CAUSE.OPEN_UNKNOWN" -> "Can this open question be answered before concluding? (" + e.Message + ")"
        | "CAUSE.OPEN_CONTRADICTION" -> "What explains the conflicting evidence? (" + e.Message + ")"
        | "CAUSE.UNVERIFIED_ASSUMPTION" -> "How can this assumption be checked? (" + e.Message + ")"
        | "CAUSE.NO_MECHANISM" -> "How, step by step, does the cause produce the problem? (" + e.Message + ")"
        | "CAUSE.NO_ROOT" -> "Which member is the root cause rather than a contributing factor? (" + e.Message + ")"
        | _ -> "Before concluding: " + e.Message

let private run (operation: string) (actor: Actor) (notice: string) (fields: string list) (command: Command) (iid: InvestigationId) (st: AppState) : AppState * Effect list =
    let state = investigationOf st iid
    let commandId, st = Ids.next st
    let env =
        { CommandId = commandId
          Investigation = iid
          ExpectedVersion = state |> Option.map (fun i -> i.Version)
          Actor = actor
          RequestedAt = st.Now
          Command = command }
    match Decide.execute state env with
    | Error errors -> { st with Refusals = refusalsOf operation errors; Notice = "Not done: " + notice }, []
    | Ok events ->
        let history = (st.Investigations |> Map.tryFind iid |> Option.defaultValue []) @ events
        let st = { st with Investigations = st.Investigations |> Map.add iid history; Refusals = []; Notice = notice } |> clear fields
        persistAfterChange iid st

/// A form that cannot yet form a command: explained like any refusal, with its Aegis code.
let private incomplete (st: AppState) (operation: string) (code: string) (rule: string) (message: string) : AppState * Effect list =
    { st with Refusals = refusalsOf operation [ DomainError.create code rule message ] }, []

let private inWorkspace (st: AppState) (f: InvestigationId -> AppState -> AppState * Effect list) =
    match currentId st with
    | Some iid -> f iid st
    | None -> st, []

let private parseKey<'Tag> (key: string option) : Id<'Tag> option = key |> Option.bind Id.tryParse<'Tag>

let private newId<'Tag> (st: AppState) : Id<'Tag> * AppState =
    let g, st = Ids.next st
    Id.create<'Tag> g, st

let private suffix (prefix: string) (name: string) =
    if name.StartsWith(prefix, StringComparison.Ordinal) then Some(name.Substring prefix.Length) else None

let private navigate (screen: Screen) (st: AppState) : AppState * Effect list =
    let c, st = Ids.correlation st NavigationAck
    { st with Screen = screen }, [ Navigate(c, ScreenRoute.format screen) ]

/// The shareable link of a screen: page origin + path + canonical hash (R9.10).
/// It is built from ids only, so it never carries evidence text or secrets (R9.9).
let shareLink (st: AppState) (screen: Screen) = st.BaseUrl + ScreenRoute.format screen

let private copyLink (screen: Screen) (st: AppState) : AppState * Effect list =
    let url = shareLink st screen
    let c, st = Ids.correlation st (CopyLink url)
    st, [ ClipboardWrite(c, url) ]

let private copyEvent (name: string) (key: string option) (st: AppState) : (AppState * Effect list) option =
    match currentId st, name with
    | Some iid, "copy.workspace" -> Some(copyLink (WorkspaceScreen iid) st)
    | Some iid, "copy.report" -> Some(copyLink (ReportScreen iid) st)
    | Some iid, _ when name.StartsWith("copy.", StringComparison.Ordinal) ->
        let segment = name.Substring 5
        match ScreenRoute.entitySegments |> List.tryFind (fun (s, _) -> s = segment), key |> Option.bind Id.tryParse<InvestigationTag> with
        | Some(_, kind), Some eid -> Some(copyLink (EntityScreen(iid, kind, eid.Value)) st)
        | _ -> Some(st, [])
    | _ -> None

let private startInvestigation (st: AppState) : AppState * Effect list =
    let iid, st = newId<InvestigationTag> st
    let oid, st = newId<ObservationTag> st
    let commandId, st = Ids.next st
    let env =
        { CommandId = commandId; Investigation = iid; ExpectedVersion = None; Actor = human; RequestedAt = st.Now
          Command = StartInvestigation(iid, draft st "title", oid, draft st "observation") }
    match Decide.execute None env with
    | Error errors -> { st with Refusals = refusalsOf "StartInvestigation" errors; Notice = "The investigation was not started." }, []
    | Ok events ->
        let st = { st with Investigations = st.Investigations |> Map.add iid events; Refusals = [] } |> clear [ "title"; "observation" ]
        // Starting means working: the kernel decides whether activation is legal.
        let st, saveEffects = run "ActivateInvestigation" human "Investigation started." [] ActivateInvestigation iid st
        let st, navEffects = navigate (WorkspaceScreen iid) st
        st, saveEffects @ navEffects

let private hypothesisCommand (name: string) (key: string option) (st: AppState) =
    match Words.hypothesisCommands |> List.tryFind (fun (n, _, _) -> n = name), parseKey<HypothesisTag> key with
    | Some(_, command, label), Some hid ->
        let evidence = guidDraft st "hypEvidence" |> Option.map Id.create<EvidenceTag> |> Option.toList
        inWorkspace st (run ("TransitionHypothesis." + name) human (label + ": done.") [ "hypReason"; "hypEvidence" ] (TransitionHypothesis(hid, command, draft st "hypReason", evidence)))
    | _ -> st, []

let private itemCommand (table: (string * 'C * string) list) (name: string) (build: 'C -> AppState -> Command option) (operation: string) (fields: string list) (st: AppState) =
    match table |> List.tryFind (fun (n, _, _) -> n = name) with
    | Some(_, command, label) ->
        match build command st with
        | Some cmd -> inWorkspace st (run (operation + "." + name) human (label + ": done.") fields cmd)
        | None -> st, []
    | None -> st, []

let private settingsFromDrafts (st: AppState) : RemoteConfig option =
    match optDraft st "setOwner", optDraft st "setRepo", optDraft st "setToken" with
    | Some owner, Some repo, Some token ->
        Some { Owner = owner.Trim(); Repository = repo.Trim(); Branch = (optDraft st "setBranch" |> Option.defaultValue "main").Trim(); Token = token.Trim() }
    | Some owner, Some repo, None ->
        // No new token: keep the one held in memory for this session (if any).
        let token = st.Remote |> Option.map (fun r -> r.Token) |> Option.defaultValue ""
        Some { Owner = owner.Trim(); Repository = repo.Trim(); Branch = (optDraft st "setBranch" |> Option.defaultValue "main").Trim(); Token = token }
    | _ -> None

/// UI events: field drafts ("f.<field>") and intents.
let private onEvent (name: string) (key: string option) (value: string option) (st: AppState) : AppState * Effect list =
    match suffix "f." name with
    | Some field -> { st with Drafts = st.Drafts |> Map.add field (Option.defaultValue "" value) }, []
    | None ->
    let st = { st with Notice = "" }
    match copyEvent name key st with
    | Some result -> result
    | None ->
    match name with
    | "start" when not st.Loaded ->
        // M6: nothing is created before the saved investigations have loaded.
        incomplete st "StartInvestigation" "LOAD.IN_PROGRESS" "R10.7" "Investigations saved on this device are still loading; try again in a moment."
    | "start" -> startInvestigation st
    | "dismissRefusals" -> { st with Refusals = [] }, []
    | "hyp.add" ->
        let hid, st = newId<HypothesisTag> st
        inWorkspace st (run "RecordHypothesis" human "Hypothesis added." [ "hypStatement"; "hypMechanism" ] (RecordHypothesis(hid, draft st "hypStatement", optDraft st "hypMechanism")))
    | "ai.add" ->
        let pid, st = newId<ProposalTag> st
        inWorkspace st (run "RecordProposal" assistant "AI-suggested hypothesis recorded for your review." [ "aiText"; "aiRationale" ] (RecordProposal(pid, ProposedHypothesis, draft st "aiText", optDraft st "aiRationale")))
    | "challenge" ->
        match currentId st |> Option.bind (investigationOf st) |> Option.bind Challenge.suggestion with
        | Some(text, rationale) ->
            let pid, st = newId<ProposalTag> st
            inWorkspace st (run "RecordProposal" challenger "Rule-based helper suggested a check that could falsify the leading explanation." [] (RecordProposal(pid, ProposedAction, text, Some rationale)))
        | None -> incomplete st "RecordProposal" "CHALLENGE.NOT_APPLICABLE" "R15.3" "There is no supported explanation with alternatives still standing to challenge."
    | "ai.accept" ->
        match parseKey<ProposalTag> key with
        | Some pid ->
            let target, st = Ids.next st
            inWorkspace st (run "AcceptProposal" human "Suggestion accepted as a hypothesis." [] (AcceptProposal(pid, target)))
        | None -> st, []
    | "ai.reject" ->
        match parseKey<ProposalTag> key with
        | Some pid -> inWorkspace st (run "RejectProposal" human "Suggestion rejected." [ "aiReason" ] (RejectProposal(pid, draft st "aiReason")))
        | None -> st, []
    | "ev.add" ->
        let eid, st = newId<EvidenceTag> st
        let content = if draft st "evKind" = "url" then UrlInput((draft st "evContent").Trim()) else TextInput(draft st "evContent")
        let category = choice Words.categories DirectObservation (draft st "evCategory")
        inWorkspace st (run "AdmitEvidence" human "Evidence added." [ "evKind"; "evContent"; "evCategory"; "evSource"; "evCaptured" ] (AdmitEvidence(eid, content, category, draft st "evSource", dateDraft st "evCaptured")))
    | "ev.supersede" ->
        match guidDraft st "supOld", guidDraft st "supNew" with
        | Some o, Some r ->
            inWorkspace st (run "SupersedeEvidence" human "Evidence replaced; the earlier item stays in the record." [ "supOld"; "supNew"; "supReason" ] (SupersedeEvidence(Id.create o, Id.create r, draft st "supReason")))
        | _ -> incomplete st "SupersedeEvidence" "FORM.INCOMPLETE" "form" "Choose the evidence to replace and the evidence that replaces it."
    | "rel.add" ->
        match guidDraft st "relEvidence", guidDraft st "relHypothesis" with
        | Some e, Some h ->
            let rid, st = newId<RelationshipTag> st
            let kind = choice Words.relationships Supports (draft st "relKind")
            inWorkspace st (run "RelateEvidence" human "Evidence linked to the hypothesis." [ "relEvidence"; "relHypothesis"; "relKind"; "relRationale" ] (RelateEvidence(rid, Id.create e, Id.create h, kind, optDraft st "relRationale")))
        | _ -> incomplete st "RelateEvidence" "FORM.INCOMPLETE" "form" "Choose both the evidence and the hypothesis to link."
    | "unk.add" ->
        let uid, st = newId<UnknownTag> st
        inWorkspace st (run "IdentifyUnknown" human "Open question added." [ "unkQuestion"; "unkMateriality" ] (IdentifyUnknown(uid, draft st "unkQuestion", choice Words.materialities Material (draft st "unkMateriality"))))
    | "con.add" ->
        let cid, st = newId<ContradictionTag> st
        let evidence = guidDraft st "conEvidence" |> Option.map Id.create<EvidenceTag> |> Option.toList
        inWorkspace st (run "RecordContradiction" human "Contradiction recorded." [ "conStatement"; "conEvidence"; "conMateriality" ] (RecordContradiction(cid, draft st "conStatement", evidence, choice Words.materialities Material (draft st "conMateriality"))))
    | "pred.add" ->
        match guidDraft st "predHypothesis" with
        | Some h ->
            let pid, st = newId<PredictionTag> st
            inWorkspace st (run "RecordPrediction" human "Prediction recorded." [ "predHypothesis"; "predStatement"; "predStrength" ] (RecordPrediction(pid, Id.create h, draft st "predStatement", choice Words.strengths Expected (draft st "predStrength"), Prospective)))
        | None -> incomplete st "RecordPrediction" "FORM.INCOMPLETE" "form" "Choose the hypothesis the prediction follows from."
    | "act.add" ->
        let aid, st = newId<ActionTag> st
        let targets = guidDraft st "actHypothesis" |> Option.map Id.create<HypothesisTag> |> Option.toList
        inWorkspace st (run "RecordAction" human "Test or experiment planned." [ "actKind"; "actPurpose"; "actHypothesis" ] (RecordAction(aid, choice Words.actionKinds Test (draft st "actKind"), draft st "actPurpose", targets)))
    | "find.add" ->
        match guidDraft st "findAction" with
        | Some a ->
            let fid, st = newId<FindingTag> st
            inWorkspace st (run "RecordFinding" human "Finding recorded." [ "findAction"; "findResult"; "findInterpretation" ] (RecordFinding(fid, Id.create a, draft st "findResult", optDraft st "findInterpretation")))
        | None -> incomplete st "RecordFinding" "FORM.INCOMPLETE" "form" "Choose the test or experiment this finding came from."
    | "neg.add" ->
        let nid, st = newId<NegativeKnowledgeTag> st
        let basis = guidDraft st "negEvidence" |> Option.map Id.create<EvidenceTag> |> Option.toList
        inWorkspace st (run "EstablishNegativeKnowledge" human "Ruled-out knowledge recorded." [ "negStatement"; "negScope"; "negEvidence" ] (EstablishNegativeKnowledge(nid, draft st "negStatement", draft st "negScope", basis)))
    | "cause.add" ->
        let cid, st = newId<CausalAssertionTag> st
        let members =
            (guidDraft st "causeRoot" |> Option.map (fun g -> { Hypothesis = Id.create g; Role = RootCause }) |> Option.toList)
            @ (guidDraft st "causeFactor" |> Option.map (fun g -> { Hypothesis = Id.create g; Role = ContributingFactor }) |> Option.toList)
        inWorkspace st (run "RecordCausalAssertion" human "Cause proposed." [ "causeOutcome"; "causeRoot"; "causeFactor"; "causeMechanism" ] (RecordCausalAssertion(cid, draft st "causeOutcome", members, optDraft st "causeMechanism")))
    | "cause.confirm" ->
        match parseKey<CausalAssertionTag> key with
        | Some cid -> inWorkspace st (run "ConfirmCause" human "Root cause confirmed." [] (ConfirmCause cid))
        | None -> st, []
    | "corr.add" ->
        let caid, st = newId<CorrectiveActionTag> st
        let targets = guidDraft st "corrCause" |> Option.map Id.create<CausalAssertionTag> |> Option.toList
        inWorkspace st (run "RecordCorrectiveAction" human "Corrective action added." [ "corrKind"; "corrDescription"; "corrCause" ] (RecordCorrectiveAction(caid, choice Words.correctionKinds Correction (draft st "corrKind"), draft st "corrDescription", targets)))
    | "crit.add" ->
        match guidDraft st "critCorrection" with
        | Some ca ->
            let crid, st = newId<CriterionTag> st
            inWorkspace st (run "RecordCriterion" human "Success criterion added." [ "critCorrection"; "critExpected"; "critMethod" ] (RecordCriterion(crid, Id.create ca, draft st "critExpected", optDraft st "critMethod")))
        | None -> incomplete st "RecordCriterion" "FORM.INCOMPLETE" "form" "Choose the corrective action this criterion checks."
    | "ver.add" ->
        match guidDraft st "verCriterion" with
        | Some cr ->
            let vid, st = newId<VerificationTag> st
            let evidence = guidDraft st "verEvidence" |> Option.map Id.create<EvidenceTag> |> Option.toList
            inWorkspace st (run "RecordVerification" human "Verification recorded." [ "verCriterion"; "verOutcome"; "verEvidence" ] (RecordVerification(vid, Id.create cr, choice Words.outcomes Satisfied (draft st "verOutcome"), evidence)))
        | None -> incomplete st "RecordVerification" "FORM.INCOMPLETE" "form" "Choose the success criterion being verified."
    | "concl.add" ->
        let cid, st = newId<ConclusionTag> st
        let causes = guidDraft st "conclCause" |> Option.map Id.create<CausalAssertionTag> |> Option.toList
        inWorkspace st (run "RecordConclusion" human "Conclusion recorded." [ "conclStatement"; "conclStrength"; "conclCause"; "conclResidual" ] (RecordConclusion(cid, draft st "conclStatement", choice Words.conclusions SupportedCause (draft st "conclStrength"), causes, optDraft st "conclResidual")))
    | "settings.save" ->
        match settingsFromDrafts st with
        | Some config ->
            let notice =
                if config.Token = "" then "Repository saved. Enter an access token to sync; it is kept in memory for this session only."
                else "GitHub settings saved. The access token is kept in memory for this session only and is never saved on this device."
            saveSettings (Some config) ({ st with Notice = notice } |> clear [ "setToken" ])
        | None -> incomplete st "SaveSettings" "SETTINGS.INCOMPLETE" "settings" "Enter the owner, repository and an access token."
    | "settings.clear" -> saveSettings None ({ st with Notice = "GitHub settings removed. Investigations stay on this device." } |> clear [ "setOwner"; "setRepo"; "setBranch"; "setToken" ])
    | "sync.retry" -> retrySync st
    | _ ->
        match suffix "hyp." name with
        | Some command -> hypothesisCommand command key st
        | None ->
        match suffix "unk." name, parseKey<UnknownTag> key with
        | Some command, Some uid -> itemCommand Words.unknownCommands command (fun c s -> Some(TransitionUnknown(uid, c, draft s "unkBasis"))) "TransitionUnknown" [ "unkBasis" ] st
        | _ ->
        match suffix "con." name, parseKey<ContradictionTag> key with
        | Some command, Some cid -> itemCommand Words.contradictionCommands command (fun c s -> Some(TransitionContradiction(cid, c, draft s "conBasis"))) "TransitionContradiction" [ "conBasis" ] st
        | _ ->
        match suffix "act." name, parseKey<ActionTag> key with
        | Some command, Some aid -> itemCommand Words.actionCommands command (fun c _ -> Some(TransitionAction(aid, c))) "TransitionAction" [] st
        | _ ->
        match suffix "corr." name, parseKey<CorrectiveActionTag> key with
        | Some command, Some caid -> itemCommand Words.correctionCommands command (fun c _ -> Some(TransitionCorrection(caid, c))) "TransitionCorrection" [] st
        | _ ->
        match suffix "life." name with
        | Some command ->
            let build c (s: AppState) =
                match c with
                | ReopenInvestigation _ -> Some(ReopenInvestigation(draft s "reopenBasis"))
                | MarkAwaitingEvidence _ -> Some(MarkAwaitingEvidence "Waiting for evidence.")
                | other -> Some other
            itemCommand LifecycleCommands.all command build "Lifecycle" [ "reopenBasis" ] st
        | None -> st, []

// ---------------------------------------------------------------------------
// Effect results
// ---------------------------------------------------------------------------

/// Pure decoding of dist/factory.json; anything unreadable is an explained failure.
let decodeFactory (text: string) : Result<FactoryInfo, string> =
    match JsonIn.parse text with
    | None -> Error "factory.json is not JSON"
    | Some root ->
        let field name (n: JsonNode option) = JsonIn.child name n |> JsonIn.str |> Option.defaultValue ""
        match JsonIn.child "schema" (Some root) |> JsonIn.str with
        | Some "rci.factory/v1" ->
            let tests =
                match JsonIn.child "tests" (Some root) with
                | Some(:? JsonObject as o) ->
                    let t = Some(o :> JsonNode)
                    let count name = JsonIn.child name t |> JsonIn.int |> Option.defaultValue 0
                    Some
                        { RunCommit = field "commit" t
                          Total = count "tests"
                          Passed = count "passed"
                          Failed = count "failed"
                          Tested = count "tested"
                          Untested = count "untested"
                          Failing = count "failing"
                          Suites = field "suites" t }
                | _ -> None
            Ok
                { Label = field "label" (Some root)
                  Commit = field "commit" (Some root)
                  BuiltAt = field "builtAt" (Some root)
                  Experiment = field "experiment" (Some root)
                  Requirements =
                    JsonIn.child "requirements" (Some root) |> JsonIn.list
                    |> List.map (fun r ->
                        { ReqId = field "id" r; ReqTitle = field "title" r; ReqScope = field "scope" r; ReqSlice = field "slice" r; Coverage = field "coverage" r })
                  WorkItems =
                    JsonIn.child "workItems" (Some root) |> JsonIn.list
                    |> List.map (fun w -> field "id" w, field "title" w, field "status" w)
                  Tests = tests
                  BuildStatus = (match field "buildStatus" (Some root) with "" -> "unknown" | v -> v) }
        | other -> Error("unsupported factory schema " + Option.defaultValue "(none)" other)

/// Entering the factory view requests ./factory.json once (Limen Http effect).
let private ensureFactory (st: AppState) : AppState * Effect list =
    match st.Screen, st.Factory with
    | FactoryScreen, (FactoryNotRequested | FactoryFailed _) ->
        let c, st = Ids.correlation st FactoryLoad
        { st with Factory = FactoryLoading }, [ HttpCall(c, "GET", "./factory.json", [], None) ]
    | _ -> st, []

let private finishLoading (st: AppState) =
    let loading =
        st.Pending |> Map.exists (fun _ op -> match op with LoadIndex | LoadInvestigation _ | LoadSync | LoadSettings -> true | _ -> false)
    if loading || st.Loaded then st, []
    else
        let st = { st with Loaded = true }
        // Statuses confirmed for another repository (or none) do not count now.
        let st = if st.SyncRemote.IsSome && st.SyncRemote <> Some(currentIdentity st) then resetRemoteState st else st
        let st, index = if st.IndexDirty then saveIndex st else st, []
        let st, send = sendAllPending st
        st, index @ send

let private onStorage (op: PendingOp) (result: Result<string option, string>) (st: AppState) : AppState * Effect list =
    match op, result with
    | LoadSettings, Ok(Some text) ->
        let remote = readSettings text
        // A token persisted by an older build is erased from this device (R11.3).
        let st, scrub =
            match remote, JsonIn.child "token" (JsonIn.parse text) with
            | Some cfg, Some _ -> storageSet SaveSettings Storage.settingsKey (settingsDocument cfg) st
            | _ -> st, []
        let st, more = finishLoading { st with Remote = remote }
        st, scrub @ more
    | LoadSync, Ok(Some text) -> finishLoading (readSync text st)
    | LoadIndex, Ok(Some text) ->
        match IndexCodec.decode text with
        | Ok entries ->
            let st = { st with StoredIndex = entries |> List.map (fun e -> e.Id, e) |> Map.ofList }
            let st, effects =
                entries
                |> List.fold
                    (fun (s, fx) (e: IndexCodec.Entry) ->
                        let c, s = Ids.correlation s (LoadInvestigation e.Id)
                        s, fx @ [ StorageGet(c, Storage.investigationKey e.Id) ])
                    (st, [])
            let st, more = finishLoading st
            st, effects @ more
        | Error e -> finishLoading { st with IndexUnreadable = true; Notice = "The saved list of investigations could not be read (" + e + "); it is left untouched." }
    | LoadInvestigation iid, Ok(Some text) ->
        match EventCodec.decode text with
        | Ok(id, events) when id = iid && not events.IsEmpty ->
            // m4: a history is adopted only if the kernel can verify it; the stored data is left untouched.
            match History.validate events with
            | [] -> finishLoading { st with Investigations = st.Investigations |> Map.add iid events; Local = st.Local |> Map.add iid SavedLocally }
            | errors ->
                finishLoading { st with Unreadable = st.Unreadable |> Map.add iid ("it could not be verified (" + Words.join ", " (errors |> List.map (fun e -> e.Code) |> List.distinct) + ")") }
        | Ok _ -> finishLoading { st with Unreadable = st.Unreadable |> Map.add iid "the stored document names a different investigation" }
        | Error e -> finishLoading { st with Unreadable = st.Unreadable |> Map.add iid e }
    | LoadInvestigation iid, Ok None -> finishLoading { st with Unreadable = st.Unreadable |> Map.add iid "its stored document is missing" }
    | (LoadSettings | LoadSync | LoadIndex), Ok None -> finishLoading st
    | (LoadSettings | LoadSync | LoadIndex | LoadInvestigation _), Error reason ->
        finishLoading { st with Notice = "This device's storage is unavailable (" + reason + "); work will not survive a reload." }
    | SaveInvestigation iid, Ok _ -> { st with Local = st.Local |> Map.add iid SavedLocally }, []
    | SaveInvestigation iid, Error reason -> { st with Local = st.Local |> Map.add iid (SaveFailed reason) }, []
    | (SaveIndex | SaveSync | SaveSettings), Error reason -> { st with Notice = "Saving on this device failed (" + reason + ")." }, []
    | _ -> st, []

let private onRemoteRead (iid: InvestigationId) (outcome: HttpOutcome) (st: AppState) : AppState * Effect list =
    let pending detail = { st with Sync = st.Sync |> Map.add iid Pending; SyncDetail = st.SyncDetail |> Map.add iid detail }, []
    let local = Map.tryFind iid st.Investigations |> Option.defaultValue []
    let st, effects =
        match outcome with
        | HttpSuccess(status, body) ->
            match GitHub.interpretRead status body with
            | Ok None -> put iid None { st with RemoteSha = st.RemoteSha |> Map.remove iid }
            | Ok(Some(sha, text)) ->
                let st = { st with RemoteSha = st.RemoteSha |> Map.add iid sha }
                match EventCodec.decode text with
                | Ok(_, remote) ->
                    match Reconcile.plan local remote with
                    | Reconcile.UpToDate -> { st with Sync = st.Sync |> Map.add iid Synced; SyncDetail = st.SyncDetail |> Map.remove iid }, []
                    | Reconcile.PushLocal -> put iid (Some sha) st
                    | Reconcile.AdoptRemote when not (History.validate remote).IsEmpty ->
                        let codes = History.validate remote |> List.map (fun e -> e.Code) |> List.distinct
                        { st with Sync = st.Sync |> Map.add iid (Conflicted("the copy on GitHub could not be verified (" + Words.join ", " codes + ") and was not adopted")) }, []
                    | Reconcile.AdoptRemote ->
                        let st = { st with Investigations = st.Investigations |> Map.add iid remote; Sync = st.Sync |> Map.add iid Synced; Notice = "Newer history from GitHub was loaded." }
                        let st, a = storageSet (SaveInvestigation iid) (Storage.investigationKey iid) (EventCodec.encode iid remote) st
                        let st, b = saveIndex st
                        st, a @ b
                    | Reconcile.Diverged detail -> { st with Sync = st.Sync |> Map.add iid (Conflicted detail) }, []
                | Error e -> { st with Sync = st.Sync |> Map.add iid (Conflicted("the copy on GitHub could not be read: " + e)) }, []
            | Error e -> pending e
        | HttpFailure _ | HttpCancelled | HttpOutcomeUnknown _ -> pending "GitHub could not be reached; changes stay pending and will be sent when back online."
    let st, save = saveSyncState st
    st, effects @ save

let private onRemoteWrite (iid: InvestigationId) (outcome: HttpOutcome) (st: AppState) : AppState * Effect list =
    let pending detail (s: AppState) = { s with Sync = s.Sync |> Map.add iid Pending; SyncDetail = s.SyncDetail |> Map.add iid detail }, []
    let st, effects =
        match outcome with
        | HttpSuccess(status, body) ->
            match GitHub.interpretWrite status body with
            | Written sha ->
                let st = { st with RemoteSha = st.RemoteSha |> Map.add iid sha; SyncDetail = st.SyncDetail |> Map.remove iid }
                let sent = Map.tryFind iid st.SentLength |> Option.defaultValue 0
                let now = Map.tryFind iid st.Investigations |> Option.map List.length |> Option.defaultValue 0
                if now > sent then sendIfReady iid { st with Sync = st.Sync |> Map.add iid Pending }
                else { st with Sync = st.Sync |> Map.add iid Synced }, []
            | RemoteConflict detail -> { st with Sync = st.Sync |> Map.add iid (Conflicted detail) }, []
            | AuthFailed detail -> pending ("GitHub refused the access token (" + detail + "). Changes stay pending.") st
            | WriteFailed detail -> pending ("GitHub did not accept the write (" + detail + "). Changes stay pending.") st
        | HttpFailure _ | HttpCancelled -> pending "GitHub could not be reached; changes stay pending and will be sent when back online." st
        | HttpOutcomeUnknown _ ->
            // The write may or may not have landed: re-read before writing again.
            pending "It is unknown whether GitHub received the last change; it will be checked again." { st with RemoteSha = st.RemoteSha |> Map.remove iid }
    let st, save = saveSyncState st
    st, effects @ save

// ---------------------------------------------------------------------------
// Projection
// ---------------------------------------------------------------------------

type private Labels =
    { Hyp: Map<Guid, string>
      Ev: Map<Guid, string>
      Cause: Map<Guid, string>
      Corr: Map<Guid, string>
      Crit: Map<Guid, string>
      Act: Map<Guid, string> }

let private ordinals (events: Event list) (prefix: string) (pick: EventBody -> Guid option) : Map<Guid, string> =
    events
    |> List.choose (fun e -> pick e.Body)
    |> List.distinct
    |> List.mapi (fun i g -> g, prefix + string (i + 1))
    |> Map.ofList

let private labelsOf (events: Event list) : Labels =
    { Hyp =
        ordinals events "H" (fun b ->
            match b with
            | HypothesisRecorded(h, _, _) -> Some h.Value
            | _ -> None)
      Ev = ordinals events "E" (fun b -> match b with EvidenceAdmitted(e, _, _, _, _) -> Some e.Value | _ -> None)
      Cause = ordinals events "C" (fun b -> match b with CausalAssertionRecorded(c, _, _, _) -> Some c.Value | _ -> None)
      Corr = ordinals events "A" (fun b -> match b with CorrectiveActionRecorded(c, _, _, _) -> Some c.Value | _ -> None)
      Crit = ordinals events "K" (fun b -> match b with CriterionRecorded(c, _, _, _) -> Some c.Value | _ -> None)
      Act = ordinals events "T" (fun b -> match b with ActionRecorded(a, _, _, _) -> Some a.Value | _ -> None) }

let private lbl (m: Map<Guid, string>) (g: Guid) = Map.tryFind g m |> Option.defaultValue "?"

let private evidenceText (e: Evidence) =
    match e.Content with
    | TextEvidence t -> t.Value
    | ArtifactEvidence uri -> uri

/// Plain-language account of one event for the timeline (R14.1).
let private describe (inv: Investigation) (l: Labels) (body: EventBody) : string =
    let hyp (h: HypothesisId) =
        lbl l.Hyp h.Value + " " + (inv.Hypotheses |> Map.tryFind h |> Option.map (fun x -> "\"" + Words.clip 50 x.Statement.Value + "\"") |> Option.defaultValue "")
    let ev (e: EvidenceId) = lbl l.Ev e.Value
    match body with
    | InvestigationStarted(title, _, _) -> "Investigation started: " + title.Value
    | LifecycleChanged(f, t, _, reason) -> "Status changed from " + Words.lifecycle f + " to " + Words.lifecycle t + (reason |> Option.map (fun r -> " (" + r + ")") |> Option.defaultValue "")
    | ObservationRecorded(_, t, _) -> "Observation recorded: " + Words.clip 80 t.Value
    | HypothesisRecorded(h, _, _) -> "Hypothesis added: " + hyp h
    | HypothesisTransitioned(h, c) ->
        hyp h + " changed from " + Words.hypothesis c.From + " to " + Words.hypothesis c.To + " because: " + c.Reason.Value
        + (if c.Evidence.IsEmpty then "" else " (evidence " + Words.join ", " (c.Evidence |> List.map ev) + ")")
    | EvidenceAdmitted(e, _, cat, src, _) -> "Evidence " + ev e + " added (" + Words.category cat + ", source: " + src.Value + ")"
    | EvidenceSuperseded(o, r, why) -> "Evidence " + ev o + " replaced by " + ev r + ": " + why.Value
    | EvidenceVerificationAssessed(e, s) -> "Evidence " + ev e + " checked: " + Words.verificationStatus s
    | EvidenceRelated(_, e, h, k, _) -> "Evidence " + ev e + " " + Words.relationship k + " " + hyp h
    | UnknownIdentified(_, q, m) -> "Open question (" + Words.materiality m + "): " + Words.clip 80 q.Value
    | UnknownTransitioned(u, s, b) ->
        "Open question " + (inv.Unknowns |> Map.tryFind u |> Option.map (fun x -> "\"" + Words.clip 50 x.Question.Value + "\"") |> Option.defaultValue "") + " is now " + Words.unknown s + ": " + b.Value
    | AssumptionRecorded(_, p, _) -> "Assumption recorded: " + Words.clip 80 p.Value
    | AssumptionTransitioned _ -> "Assumption updated"
    | ContradictionRecorded(_, s, _, _) -> "Contradiction recorded: " + Words.clip 80 s.Value
    | ContradictionTransitioned(_, s, b) -> "Contradiction is now " + Words.contradiction s + ": " + b.Value
    | PredictionRecorded(_, h, s, _, _) -> "Prediction for " + hyp h + ": " + Words.clip 80 s.Value
    | ActionRecorded(a, k, p, _) -> Words.actionKind k + " " + lbl l.Act a.Value + " planned: " + Words.clip 80 p.Value
    | ActionTransitioned(a, s) -> "Test " + lbl l.Act a.Value + " is now " + Words.action s
    | FindingRecorded(_, a, r, _) -> "Finding from " + lbl l.Act a.Value + ": " + Words.clip 80 r.Value
    | NegativeKnowledgeEstablished(_, s, _, _) -> "Ruled out: " + Words.clip 80 s.Value
    | CausalAssertionRecorded(c, o, _, _) -> "Cause " + lbl l.Cause c.Value + " proposed for: " + Words.clip 80 o.Value
    | CausalAssertionConfirmed(c, _) -> "Cause " + lbl l.Cause c.Value + " confirmed"
    | CausalAssertionReopened(c, r) -> "Cause " + lbl l.Cause c.Value + " reopened: " + r.Value
    | CorrectiveActionRecorded(c, k, d, _) -> Words.correctionKind k + " " + lbl l.Corr c.Value + " added: " + Words.clip 80 d.Value
    | CorrectiveActionTransitioned(c, s) -> "Corrective action " + lbl l.Corr c.Value + " is now " + Words.correction s
    | CriterionRecorded(c, _, e, _) -> "Success criterion " + lbl l.Crit c.Value + ": " + Words.clip 80 e.Value
    | VerificationRecorded(_, c, o, e) ->
        "Verification of " + lbl l.Crit c.Value + ": " + Words.outcome o + (if e.IsEmpty then "" else " (evidence " + Words.join ", " (e |> List.map ev) + ")")
    | ConclusionRecorded(_, s, st, _, _) -> "Conclusion (" + Words.conclusion st + "): " + Words.clip 80 s.Value
    | ProposalRecorded(_, _, t, _) -> "AI suggestion recorded: " + Words.clip 80 t.Value
    | ProposalWasAccepted _ -> "AI suggestion accepted by a person"
    | ProposalWasRejected(_, r) -> "AI suggestion rejected: " + r.Value

let private item (fields: (string * J) list) = JObj fields
let private s (v: string) = JStr v
let private b (v: bool) = JBool v
let private n (v: int) = JNum(int64 v)

/// Which states a command is legal from, read from the kernel table.
let private legalFrom (table: 'S -> 'C -> 'S option) (states: 'S list) (word: 'S -> string) (command: 'C) =
    states |> List.filter (fun st -> table st command |> Option.isSome) |> List.map word |> Words.states

let private placeholder: Investigation option =
    match NonEmptyText.tryCreate "—" with
    | Some t ->
        let iid = Id.create<InvestigationTag> (Guid("00000000-0000-0000-0000-0000000000ff"))
        Some(Evolve.apply None { Sequence = 0L; Investigation = iid; CommandId = Guid.Empty; Actor = Actor.system; At = DateTimeOffset.UnixEpoch; Body = InvestigationStarted(t, Id.create (Guid.Empty), t) })
    | None -> None

let private remoteText (st: AppState) (iid: InvestigationId) =
    match st.Remote with
    | None -> "Local only — not synced to GitHub", "unknown"
    | Some cfg ->
        let where = " (" + cfg.Owner + "/" + cfg.Repository + ")"
        match Map.tryFind iid st.Sync |> Option.defaultValue Pending with
        | Synced -> "Saved to GitHub" + where, "ok"
        | Pending when cfg.Token = "" -> "Changes pending upload to GitHub (not yet synced) — enter your access token in GitHub settings; it is not kept after a reload", "attention"
        | Pending when not st.Online -> "Offline — changes pending upload to GitHub (not yet synced)", "attention"
        | Pending -> "Changes pending upload to GitHub (not yet synced)", "attention"
        | Sending -> "Uploading to GitHub… (not yet confirmed)", "attention"
        | Conflicted d -> "Conflict with GitHub: " + d + ". Nothing was overwritten.", "blocked"
        | SyncUnknown -> "Unknown whether GitHub has the latest change (not confirmed)", "attention"
        | LocalOnly -> "Local only — not synced to GitHub", "unknown"

let private localText (st: AppState) (iid: InvestigationId) =
    match Map.tryFind iid st.Local with
    | Some SavedLocally -> "Saved on this device"
    | Some Saving -> "Saving on this device…"
    | Some(SaveFailed r) -> "Not saved on this device (" + r + ")"
    // N9: nothing claims a save that has not been recorded.
    | None -> "Not saved yet"

let private workspaceKeys (st: AppState) (inv: Investigation) (events: Event list) (real: bool) : (string * J) list =
    let l = labelsOf events
    let iid = inv.Id
    let hypotheses = inv.Hypotheses |> Map.toList |> List.map snd |> List.sortBy (fun h -> lbl l.Hyp h.Id.Value)
    let evidence = inv.Evidence |> Map.toList |> List.map snd |> List.sortBy (fun e -> e.AdmittedAt)
    let rels = inv.Relationships |> Map.toList |> List.map snd
    let causes = inv.Causes |> Map.toList |> List.map snd |> List.sortBy (fun c -> lbl l.Cause c.Id.Value)
    let corrections = inv.CorrectiveActions |> Map.toList |> List.map snd |> List.sortBy (fun c -> lbl l.Corr c.Id.Value)
    let criteria = inv.Criteria |> Map.toList |> List.map snd |> List.sortBy (fun c -> lbl l.Crit c.Id.Value)
    let actions = inv.Actions |> Map.toList |> List.map snd |> List.sortBy (fun a -> lbl l.Act a.Id.Value)
    let hypName (h: HypothesisId) =
        lbl l.Hyp h.Value + (inv.Hypotheses |> Map.tryFind h |> Option.map (fun x -> ": " + Words.clip 60 x.Statement.Value) |> Option.defaultValue "")
    let evName (e: EvidenceId) =
        lbl l.Ev e.Value + (inv.Evidence |> Map.tryFind e |> Option.map (fun x -> ": " + Words.clip 60 (evidenceText x)) |> Option.defaultValue "")
    let isOpen = Transitions.isOpen inv.Lifecycle
    let observation = inv.Observations |> Map.toList |> List.map snd |> List.sortBy (fun o -> o.Provenance.RecordedAt) |> List.tryHead
    let resolution = if real then Policy.resolutionBlockers inv else []
    let remote, remoteTone = remoteText st iid
    let syncStatus = Map.tryFind iid st.Sync
    let hypothesisItems =
        hypotheses
        |> List.map (fun h ->
            let supporting = rels |> List.filter (fun r -> r.Hypothesis = h.Id && r.Kind = Supports) |> List.map (fun r -> lbl l.Ev r.Evidence.Value)
            let contradicting = rels |> List.filter (fun r -> r.Hypothesis = h.Id && r.Kind = Contradicts) |> List.map (fun r -> lbl l.Ev r.Evidence.Value)
            let unavailable =
                Words.hypothesisCommands
                |> List.filter (fun (_, c, _) -> Transitions.hypothesis h.State c |> Option.isNone)
                |> List.map (fun (_, c, label) -> label + " (only when " + legalFrom Transitions.hypothesis Words.allHypothesisStates Words.hypothesis c + ")")
            let last = h.History |> List.tryLast
            // R6.2: posture is explained by linked evidence and open blockers, never a number or percentage.
            let linked kind =
                rels |> List.filter (fun r -> r.Hypothesis = h.Id && r.Kind = kind && (inv.Evidence |> Map.tryFind r.Evidence |> Option.exists (fun e -> e.SupersededBy.IsNone)))
                |> List.map (fun r -> lbl l.Ev r.Evidence.Value)
            let describeLinks (word: string) (labels: string list) =
                match labels with
                | [] -> "no " + word + " evidence"
                | [ one ] -> "1 " + word + " (" + one + ")"
                | many -> string many.Length + " " + word + " (" + Words.join ", " many + ")"
            let causeBlockersHere =
                if not real then []
                else
                    inv.Causes |> Map.toList |> List.map snd
                    |> List.filter (fun c -> c.Members |> List.exists (fun m -> m.Hypothesis = h.Id))
                    |> List.map (fun c -> Policy.causeBlockers inv c.Id)
                    |> List.filter (fun blockers -> not (blockers |> List.exists (fun e -> e.Code = "CAUSE.STATE")))
                    |> List.concat
            let posture =
                Words.hypothesis h.State + " because of " + describeLinks "supporting" (linked Supports) + ", " + describeLinks "contradicting" (linked Contradicts)
                + ", " + describeLinks "telling-apart" (linked Discriminates) + ", " + describeLinks "inconclusive" (linked Insufficient) + "."
                + (match causeBlockersHere with
                   | [] -> ""
                   | blockers -> " Confirming it as a cause is blocked by: " + Words.join "; " (blockers |> List.map (fun e -> e.Message)))
            item (
                [ "id", s (Id.text h.Id)
                  "href", s (ScreenRoute.format (EntityScreen(iid, HypothesisEntity, h.Id.Value)))
                  "isFocused", b (st.Screen = EntityScreen(iid, HypothesisEntity, h.Id.Value))
                  "label", s (lbl l.Hyp h.Id.Value)
                  "statement", s h.Statement.Value
                  "mechanism", s (h.Mechanism |> Option.map (fun m -> m.Value) |> Option.defaultValue "")
                  "hasMechanism", b h.Mechanism.IsSome
                  "state", s (Words.hypothesis h.State)
                  "tone", s (Words.hypothesisTone h.State)
                  "isRuledOut", b (h.State = Falsified)
                  "posture", s posture
                  "supportedBy", s (if supporting.IsEmpty then "none" else Words.join ", " supporting)
                  "contradictedBy", s (if contradicting.IsEmpty then "none" else Words.join ", " contradicting)
                  "lastChange",
                  s (
                      match last with
                      | Some c -> "Last change: " + Words.hypothesis c.From + " → " + Words.hypothesis c.To + " because: " + c.Reason.Value + (if c.Evidence.IsEmpty then "" else " (evidence " + Words.join ", " (c.Evidence |> List.map (fun e -> lbl l.Ev e.Value)) + ")")
                      | None -> "No changes yet."
                  )
                  "unavailable", s (if unavailable.IsEmpty then "" else "Not available now: " + Words.join "; " unavailable)
                  "by", s (Words.actor h.Provenance.Actor) ]
                @ (Words.hypothesisCommands
                   |> List.map (fun (name, c, _) -> "hide" + name, b (not isOpen || Transitions.hypothesis h.State c |> Option.isNone)))
            ))
    let hypothesisChanges =
        hypotheses
        |> List.collect (fun h ->
            h.History
            |> List.map (fun c ->
                c.Sequence,
                item
                    [ "id", s (Id.text h.Id + "-" + string c.Sequence)
                      "text",
                      s (
                          lbl l.Hyp h.Id.Value + ": " + Words.hypothesis c.From + " → " + Words.hypothesis c.To + " — " + c.Reason.Value
                          + (if c.Evidence.IsEmpty then "" else " (evidence " + Words.join ", " (c.Evidence |> List.map (fun e -> lbl l.Ev e.Value)) + ")")
                      )
                      "at", s (Words.date c.At) ]))
        |> List.sortBy fst
        |> List.map snd
    let option (id: string) (label: string) = item [ "id", s id; "label", s label ]
    let evidenceItems =
        evidence
        |> List.map (fun e ->
            let isUrl = match e.Content with ArtifactEvidence _ -> true | _ -> false
            item
                [ "id", s (Id.text e.Id)
                  "href", s (ScreenRoute.format (EntityScreen(iid, EvidenceEntity, e.Id.Value)))
                  "isFocused", b (st.Screen = EntityScreen(iid, EvidenceEntity, e.Id.Value))
                  "label", s (lbl l.Ev e.Id.Value)
                  "content", s (evidenceText e)
                  "isUrl", b isUrl
                  "isText", b (not isUrl)
                  "url", s (match e.Content with ArtifactEvidence u -> u | _ -> "")
                  "category", s (Words.category e.Category)
                  "source", s e.Source.Value
                  "captured", s (e.CapturedAt |> Option.map Words.day |> Option.defaultValue "not given")
                  "checked", s (Words.verificationStatus e.Verification)
                  "superseded", s (match e.SupersededBy with Some r -> "Replaced by " + lbl l.Ev r.Value | None -> "")
                  "provenance", s ("Added by " + Words.actor e.Provenance.Actor + " on " + Words.date e.Provenance.RecordedAt) ])
    let relationshipItems =
        rels
        |> List.sortBy (fun r -> r.Provenance.RecordedAt, lbl l.Ev r.Evidence.Value)
        |> List.map (fun r ->
            item
                [ "id", s (Id.text r.Id)
                  "text", s (evName r.Evidence + " — " + Words.relationship r.Kind + " — " + hypName r.Hypothesis)
                  "rationale", s (r.Rationale |> Option.map (fun x -> x.Value) |> Option.defaultValue "")
                  "isContradiction", b (r.Kind = Contradicts) ])
    let contradictingLinks =
        rels
        |> List.filter (fun r -> r.Kind = Contradicts)
        |> List.map (fun r -> item [ "id", s (Id.text r.Id); "text", s ("Evidence " + evName r.Evidence + " contradicts hypothesis " + hypName r.Hypothesis) ])
    // m2: a button that needs no typed input is shown only when the kernel would
    // accept that exact command now (decide without emitting anything).
    let accepts (command: Command) =
        real
        && (Decide.probe inv (Actor.human "local-user") st.Now command |> Result.isOk)
    let transitionFlags (table: (string * 'C * string) list) (legal: 'C -> bool) =
        table |> List.map (fun (name, c, _) -> "hide" + name, b (not isOpen || not (legal c)))
    let unknownItems =
        inv.Unknowns
        |> Map.toList
        |> List.map snd
        |> List.sortBy (fun u -> u.Provenance.RecordedAt)
        |> List.map (fun u ->
            item (
                [ "id", s (Id.text u.Id)
                  "question", s u.Question.Value
                  "materiality", s (Words.materiality u.Materiality)
                  "isBlocking", b (u.Materiality = Blocking)
                  "state", s (Words.unknown u.State)
                  "basis", s (u.Basis |> Option.map (fun x -> x.Value) |> Option.defaultValue "") ]
                @ transitionFlags Words.unknownCommands (fun c -> Transitions.unknown u.State c |> Option.isSome)
            ))
    let contradictionItems =
        inv.Contradictions
        |> Map.toList
        |> List.map snd
        |> List.sortBy (fun c -> c.Provenance.RecordedAt)
        |> List.map (fun c ->
            item (
                [ "id", s (Id.text c.Id)
                  "statement", s c.Statement.Value
                  "evidence", s (if c.Evidence.IsEmpty then "none cited" else Words.join ", " (c.Evidence |> List.map evName))
                  "materiality", s (Words.materiality c.Materiality)
                  "state", s (Words.contradiction c.State)
                  "basis", s (c.Basis |> Option.map (fun x -> x.Value) |> Option.defaultValue "") ]
                @ transitionFlags Words.contradictionCommands (fun cmd -> Transitions.contradiction c.State cmd |> Option.isSome)
            ))
    let actionItems =
        actions
        |> List.map (fun a ->
            item (
                [ "id", s (Id.text a.Id)
                  "label", s (lbl l.Act a.Id.Value)
                  "kind", s (Words.actionKind a.Kind)
                  "purpose", s a.Purpose.Value
                  "targets", s (if a.Targets.IsEmpty then "no hypothesis" else Words.join ", " (a.Targets |> List.map hypName))
                  "state", s (Words.action a.State) ]
                @ transitionFlags Words.actionCommands (fun c -> accepts (TransitionAction(a.Id, c)))
            ))
    let findingItems =
        inv.Findings
        |> Map.toList
        |> List.map snd
        |> List.sortBy (fun f -> f.Provenance.RecordedAt)
        |> List.map (fun f ->
            item
                [ "id", s (Id.text f.Id)
                  "action", s (lbl l.Act f.Action.Value)
                  "result", s f.OriginalResult.Value
                  "interpretation", s (f.Interpretation |> Option.map (fun x -> x.Value) |> Option.defaultValue "") ])
    let predictionItems =
        inv.Predictions
        |> Map.toList
        |> List.map snd
        |> List.sortBy (fun p -> p.Provenance.RecordedAt)
        |> List.map (fun p ->
            item
                [ "id", s (Id.text p.Id)
                  "text", s (hypName p.Hypothesis + " predicts: " + p.Statement.Value + " (" + Words.strength p.Strength + ")")
                  "timing",
                  s (
                      match p.Timing with
                      | Prospective -> "Made before the result was known"
                      | Retrospective -> "Made after a relevant finding (retrospective: weaker test)"
                  )
                  // R4.3: checks that tell hypotheses apart are preferred.
                  "isDiscriminating", b (p.Strength = DiscriminatingPrediction) ])
    let negativeItems =
        inv.NegativeKnowledge
        |> Map.toList
        |> List.map snd
        |> List.sortBy (fun x -> x.Provenance.RecordedAt)
        |> List.map (fun x ->
            item
                [ "id", s (Id.text x.Id)
                  "statement", s x.Statement.Value
                  "scope", s x.Scope.Value
                  "basis", s (Words.join ", " (x.Basis |> List.map (fun e -> lbl l.Ev e.Value))) ])
    // Policy decides: a CAUSE.STATE blocker means the cause is not confirmable in its
    // state, so it has no confirmation work to list and no Confirm button.
    let gate = causes |> List.map (fun c -> c, (if real then Policy.causeBlockers inv c.Id else []))
    let confirmable (blockers: DomainError list) = not (blockers |> List.exists (fun e -> e.Code = "CAUSE.STATE"))
    let causeBlockers = gate |> List.map (fun (c, blockers) -> c, (if confirmable blockers then blockers else []))
    let causeItems =
        causeBlockers
        |> List.map (fun (c, blockers) ->
            item
                [ "id", s (Id.text c.Id)
                  "label", s (lbl l.Cause c.Id.Value)
                  "outcome", s c.Outcome.Value
                  "members", s (Words.join "; " (c.Members |> List.map (fun m -> Words.role m.Role + ": " + hypName m.Hypothesis)))
                  "mechanism", s (c.Mechanism |> Option.map (fun x -> x.Value) |> Option.defaultValue "not stated")
                  "state", s (Words.cause c.State)
                  "isConfirmed", b (c.State = CauseConfirmed)
                  "hideConfirm", b (not real || not (Policy.canConfirm inv c.Id))
                  "blockerCount", n blockers.Length
                  "isBlocked", b (not blockers.IsEmpty)
                  "readiness",
                  s (
                      if c.State = CauseConfirmed then "Confirmed."
                      elif blockers.IsEmpty then "Ready to confirm: every condition is met."
                      else "Cannot be confirmed yet — " + string blockers.Length + " reason(s) listed below."
                  ) ])
    let causeBlockerItems =
        causeBlockers
        |> List.collect (fun (c, blockers) ->
            blockers
            |> List.mapi (fun i e -> item [ "id", s (Id.text c.Id + "-" + string i); "cause", s (lbl l.Cause c.Id.Value); "code", s e.Code; "message", s e.Message; "rule", s e.Rule ]))
    let correctionItems =
        corrections
        |> List.map (fun c ->
            item (
                [ "id", s (Id.text c.Id)
                  "label", s (lbl l.Corr c.Id.Value)
                  "kind", s (Words.correctionKind c.Kind)
                  "isMitigation", b (c.Kind <> Correction)
                  "description", s c.Description.Value
                  "targets", s (Words.join ", " (c.Targets |> List.map (fun t -> lbl l.Cause t.Value)))
                  "state", s (Words.correction c.State) ]
                @ transitionFlags Words.correctionCommands (fun cmd -> accepts (TransitionCorrection(c.Id, cmd)))
            ))
    // VERIFICATION.EVIDENCE_PREDATES_ROUND: only evidence admitted after verification of the
    // chosen fix started (and still active) can verify it (Policy.freshEvidence).
    let roundsInPlay =
        match Guid.TryParse(draft st "verCriterion") with
        | true, g -> inv.Criteria |> Map.tryFind (Id.create g) |> Option.map (fun c -> [ Policy.verificationRound inv c.CorrectiveAction ]) |> Option.defaultValue []
        | _ ->
            corrections
            |> List.filter (fun c -> c.State = AwaitingVerificationOfEffect)
            |> List.map (fun c -> Policy.verificationRound inv c.Id)
    let verificationEvidence = evidence |> List.filter (fun e -> roundsInPlay |> List.exists (fun r -> Policy.freshEvidence inv r e.Id))
    let verificationEvidenceNote =
        match evidence.Length - verificationEvidence.Length with
        | 0 -> ""
        | excluded ->
            string excluded + " earlier or replaced evidence item(s) are not offered: verification needs evidence added after verification of the fix started."
    let criterionItems =
        criteria
        |> List.map (fun c ->
            let verifications = inv.Verifications |> Map.toList |> List.map snd |> List.filter (fun v -> v.Criterion = c.Id)
            let met = Policy.criterionSatisfied inv c
            item
                [ "id", s (Id.text c.Id)
                  "label", s (lbl l.Crit c.Id.Value)
                  "expected", s c.ExpectedOutcome.Value
                  "correction", s (lbl l.Corr c.CorrectiveAction.Value)
                  "isMet", b met
                  "status", s (if met then "Met, with evidence" elif verifications.IsEmpty then "Not yet verified" else "Not met yet") ])
    let verificationItems =
        inv.Verifications
        |> Map.toList
        |> List.map snd
        |> List.sortBy (fun v -> v.Provenance.RecordedAt)
        |> List.map (fun v ->
            item
                [ "id", s (Id.text v.Id)
                  "text",
                  s (lbl l.Crit v.Criterion.Value + ": " + Words.outcome v.Outcome + (if v.Evidence.IsEmpty then " (no evidence cited)" else " — evidence " + Words.join ", " (v.Evidence |> List.map evName))) ])
    let conclusionItems =
        inv.Conclusions
        |> Map.toList
        |> List.map snd
        |> List.sortBy (fun c -> c.Sequence)
        |> List.map (fun c ->
            item
                [ "id", s (Id.text c.Id)
                  "statement", s c.Statement.Value
                  "strength", s (Words.conclusion c.Strength)
                  "residual", s (c.ResidualUncertainty |> Option.map (fun x -> x.Value) |> Option.defaultValue "None stated") ])
    let proposalItems =
        inv.Proposals
        |> Map.toList
        |> List.map snd
        |> List.sortBy (fun p -> p.Provenance.RecordedAt)
        |> List.map (fun p ->
            item
                [ "id", s (Id.text p.Id)
                  "text", s p.Text.Value
                  "rationale", s (p.Rationale |> Option.map (fun x -> x.Value) |> Option.defaultValue "")
                  "state", s (Words.proposal p.State)
                  "decision", s (p.Decision |> Option.map (fun x -> x.Value) |> Option.defaultValue "")
                  "isActive", b (p.State = ProposalActive && isOpen)
                  "by", s (Words.actor p.Provenance.Actor)
                  "acceptLabel",
                  s (
                      match p.Kind with
                      | ProposedHypothesis -> "Accept as hypothesis"
                      | ProposedAction -> "Accept as planned check"
                      | ProposedUnknown -> "Accept as open question"
                  ) ])
    let historyItems =
        events
        |> List.rev
        |> List.map (fun e ->
            item
                [ "id", s (string e.Sequence)
                  "sequence", n (int e.Sequence)
                  "at", s (Words.date e.At)
                  "actor", s (Words.actor e.Actor)
                  "text", s (describe inv l e.Body) ])
    // R8.4 outstanding work: only what Policy, the transition tables and the
    // recorded verifications already say; no legality is decided here.
    let outstanding =
        let fromPolicy source (errors: DomainError list) = errors |> List.map (fun e -> source, e.Message, e.Code)
        let unknownWork =
            inv.Unknowns |> Map.toList |> List.map snd
            |> List.filter (fun u -> Transitions.unknown u.State ResolveUnknown |> Option.isSome)
            |> List.map (fun u -> "Open question", "Answer or accept: \"" + u.Question.Value + "\" (" + Words.materiality u.Materiality + ")", "UNKNOWN.OPEN")
        let contradictionWork =
            inv.Contradictions |> Map.toList |> List.map snd
            |> List.filter (fun c -> Transitions.contradiction c.State ResolveContradiction |> Option.isSome)
            |> List.map (fun c -> "Contradiction", "Explain or accept: \"" + c.Statement.Value + "\" (" + Words.materiality c.Materiality + ")", "CONTRADICTION.OPEN")
        let criterionWork =
            criteria
            |> List.filter (fun c -> not (Policy.criterionSatisfied inv c))
            |> List.map (fun c -> "Verification", "Verify with evidence: " + lbl l.Crit c.Id.Value + " \"" + c.ExpectedOutcome.Value + "\"", "CRITERION.UNVERIFIED")
        let causeWork =
            causeBlockers |> List.collect (fun (c, blockers) -> fromPolicy ("Cause " + lbl l.Cause c.Id.Value) blockers)
        let resolveWork = if isOpen then fromPolicy "Resolution" resolution else []
        unknownWork @ contradictionWork @ criterionWork @ causeWork @ resolveWork
        |> List.distinctBy (fun (_, message, _) -> message)
        |> List.mapi (fun i (source, message, code) -> item [ "id", s (string i); "source", s source; "message", s message; "code", s code ])
    let resolutionItems = resolution |> List.mapi (fun i e -> item [ "id", s (string i); "code", s e.Code; "message", s e.Message; "rule", s e.Rule ])
    let lifecycleFlags =
        LifecycleCommands.all
        |> List.map (fun (name, c, _) ->
            match c with
            // Reopening needs a typed basis: structural table plus the refusal explanation.
            | ReopenInvestigation _ -> "lifeHide_" + name, b (not (LifecycleCommands.legal inv.Lifecycle c))
            | _ -> "lifeHide_" + name, b (not (accepts c)))
    [ "wsId", s (Id.text iid)
      "wsTitle", s inv.Title.Value
      "wsObservation", s (observation |> Option.map (fun o -> o.Original.Value) |> Option.defaultValue "")
      "wsObservationBy", s (observation |> Option.map (fun o -> "Recorded by " + Words.actor o.Provenance.Actor + " on " + Words.date o.Provenance.RecordedAt) |> Option.defaultValue "")
      "wsLifecycle", s (Words.lifecycle inv.Lifecycle)
      "wsVersion", n (int inv.Version)
      "wsIsOpen", b isOpen
      "wsIsClosed", b (not isOpen)
      "reportHref", s (ScreenRoute.format (ReportScreen iid))
      "workspaceHref", s (ScreenRoute.format (WorkspaceScreen iid))
      "localStatus", s (localText st iid)
      "remoteStatus", s remote
      "remoteTone", s remoteTone
      "syncDetail", s (Map.tryFind iid st.SyncDetail |> Option.defaultValue "")
      "hasSyncDetail", b (Map.containsKey iid st.SyncDetail)
      "canRetrySync", b (st.Remote.IsSome && (match syncStatus with Some Synced | Some Sending -> false | _ -> true))
      "hypotheses", JArr hypothesisItems
      "hasHypotheses", b (not hypothesisItems.IsEmpty)
      "hypothesisChanges", JArr hypothesisChanges
      "hypothesisOptions", JArr(hypotheses |> List.map (fun h -> option (Id.text h.Id) (hypName h.Id + " (" + Words.hypothesis h.State + ")")))
      "evidence", JArr evidenceItems
      "hasEvidence", b (not evidenceItems.IsEmpty)
      "evidenceOptions", JArr(evidence |> List.map (fun e -> option (Id.text e.Id) (evName e.Id)))
      "verEvidenceOptions", JArr(verificationEvidence |> List.map (fun e -> option (Id.text e.Id) (evName e.Id)))
      "verEvidenceNote", s verificationEvidenceNote
      "relationships", JArr relationshipItems
      "contradictingLinks", JArr contradictingLinks
      "hasContradictingLinks", b (not contradictingLinks.IsEmpty)
      "contradictions", JArr contradictionItems
      "unknowns", JArr unknownItems
      "hasUnknowns", b (not unknownItems.IsEmpty)
      "predictions", JArr predictionItems
      "actions", JArr actionItems
      "actionOptions", JArr(actions |> List.map (fun a -> option (Id.text a.Id) (lbl l.Act a.Id.Value + ": " + Words.clip 60 a.Purpose.Value)))
      "findings", JArr findingItems
      "negative", JArr negativeItems
      "causes", JArr causeItems
      "hasCauses", b (not causeItems.IsEmpty)
      "causeBlockers", JArr causeBlockerItems
      "causeOptions", JArr(causes |> List.map (fun c -> option (Id.text c.Id) (lbl l.Cause c.Id.Value + ": " + Words.clip 60 c.Outcome.Value + " (" + Words.cause c.State + ")")))
      "corrections", JArr correctionItems
      "correctionOptions", JArr(corrections |> List.map (fun c -> option (Id.text c.Id) (lbl l.Corr c.Id.Value + ": " + Words.clip 60 c.Description.Value)))
      "criteria", JArr criterionItems
      "criterionOptions", JArr(criteria |> List.map (fun c -> option (Id.text c.Id) (lbl l.Crit c.Id.Value + ": " + Words.clip 60 c.ExpectedOutcome.Value)))
      "verifications", JArr verificationItems
      "conclusions", JArr conclusionItems
      "proposals", JArr proposalItems
      "hasProposals", b (not proposalItems.IsEmpty)
      "history", JArr historyItems
      "resolutionBlockers", JArr resolutionItems
      "challengeAvailable", b (real && isOpen && Challenge.available inv)
      "challengeHidden", b (not (real && isOpen && Challenge.available inv))
      "challengeStatus",
      s (
          match Challenge.suggestion inv with
          | Some _ when Challenge.available inv -> "A supported explanation still has alternatives standing: ask what would prove it wrong."
          | Some _ -> "The suggested falsifying check is waiting for your decision below."
          | None -> "Nothing to challenge yet: it applies once one explanation is supported while others still stand."
      )
      "challengeQuestions",
      JArr(
          causeBlockers
          |> List.collect (fun (c, blockers) -> blockers |> List.mapi (fun i e -> item [ "id", s (Id.text c.Id + "-q" + string i); "text", s (Challenge.question e) ]))
      )
      "outstanding", JArr outstanding
      "hasOutstanding", b (not outstanding.IsEmpty)
      "noOutstanding", b outstanding.IsEmpty
      "outstandingSummary", s (if outstanding.IsEmpty then "Nothing outstanding." else string outstanding.Length + " thing(s) still need doing.")
      "canResolve", b (resolution.IsEmpty && LifecycleCommands.legal inv.Lifecycle ResolveInvestigation)
      "resolveSummary",
      s (
          if inv.Lifecycle = Resolved || inv.Lifecycle = Closed then "This investigation is " + Words.lifecycle inv.Lifecycle + "."
          elif resolution.IsEmpty then "Ready to resolve: every condition is met."
          else "Cannot be resolved yet — " + string resolution.Length + " thing(s) still need doing:"
      ) ]
    @ lifecycleFlags

let private reportKeys (inv: Investigation) (events: Event list) (real: bool) : (string * J) list =
    match real with
    | false ->
        [ "reportTitle", s inv.Title.Value
          "reportKind", s "Interim report"
          "reportIsFinal", b false
          "reportIsInterim", b true
          "reportDigest", s ""
          "reportVersion", n (int inv.Version)
          "reportObservation", s ""
          "reportRows", JArr []
          "reportUnresolved", JArr []
          "hasUnresolved", b false ]
    | true ->
        let model = Report.build inv events
        let rows =
            model.Sections
            |> List.collect (fun section ->
                match section.Items with
                | [] -> [ item [ "id", s (section.Id + "-empty"); "section", s section.Title; "first", b true; "heading", s "Nothing recorded."; "detail", s ""; "status", s "" ] ]
                | items ->
                    items
                    |> List.mapi (fun i it ->
                        item [ "id", s (section.Id + "-" + string i); "section", s section.Title; "first", b (i = 0); "heading", s it.Heading; "detail", s it.Detail; "status", s it.Status ]))
        let unresolved = model.UnresolvedWork |> List.mapi (fun i it -> item [ "id", s (string i); "heading", s it.Heading; "detail", s it.Detail; "status", s it.Status ])
        let final = model.Kind = "Final"
        [ "reportTitle", s model.Title
          "reportKind", s (if final then "Final report" else "Interim report — work remains unresolved")
          "reportIsFinal", b final
          "reportIsInterim", b (not final)
          "reportDigest", s model.SnapshotDigest
          "reportVersion", n (int model.Version)
          "reportObservation", s model.Observation
          "reportRows", JArr rows
          "reportUnresolved", JArr unresolved
          "hasUnresolved", b (not unresolved.IsEmpty) ]

let private homeKeys (st: AppState) : (string * J) list =
    let rows =
        st.Investigations
        |> Map.toList
        |> List.choose (fun (iid, events) ->
            match Evolve.replay events, List.tryLast events with
            | Some inv, Some last ->
                Some(
                    last.At,
                    item
                        [ "id", s (Id.text iid)
                          "title", s inv.Title.Value
                          "status", s (Words.lifecycle inv.Lifecycle)
                          "updated", s ("Updated " + Words.date last.At)
                          "href", s (ScreenRoute.format (WorkspaceScreen iid))
                          "reportHref", s (ScreenRoute.format (ReportScreen iid))
                          "sync", s (fst (remoteText st iid)) ]
                )
            | _ -> None)
        |> List.sortByDescending fst
        |> List.map snd
    let unreadable =
        st.StoredIndex
        |> Map.toList
        |> List.filter (fun (iid, _) -> st.Loaded && not (st.Investigations.ContainsKey iid))
        |> List.map (fun (iid, e) ->
            item
                [ "id", s (Id.text iid)
                  "title", s e.Title
                  "reason", s ("Could not be read: " + (Map.tryFind iid st.Unreadable |> Option.defaultValue "not loaded") + ". It is kept in the index and was not changed.") ])
    [ "unreadable", JArr unreadable
      "hasUnreadable", b (not unreadable.IsEmpty)
      "investigations", JArr rows; "hasInvestigations", b (not rows.IsEmpty); "noInvestigations", b rows.IsEmpty ]

/// The complete ViewState: every key in every projection (Limen rule).
let private factoryKeys (st: AppState) : (string * J) list =
    let info = match st.Factory with FactoryLoaded f -> Some f | _ -> None
    let status =
        match st.Factory with
        | FactoryNotRequested | FactoryLoading -> "Loading the factory record…"
        | FactoryLoaded f -> f.Label + " — commit " + (if f.Commit.Length > 7 then f.Commit.Substring(0, 7) else f.Commit) + ", built " + f.BuiltAt
        | FactoryFailed reason -> "The factory record could not be loaded: " + reason
    let requirements = info |> Option.map (fun f -> f.Requirements) |> Option.defaultValue []
    let workItems = info |> Option.map (fun f -> f.WorkItems) |> Option.defaultValue []
    let statusOf slice = workItems |> List.tryFind (fun (id, _, _) -> id = slice) |> Option.map (fun (_, _, st) -> st) |> Option.defaultValue "not in the work queue"
    let slices =
        requirements
        |> List.groupBy (fun r -> r.ReqSlice)
        |> List.map (fun (slice, reqs) ->
            item [ "id", s (if slice = "" then "unassigned" else slice); "slice", s (if slice = "" then "No slice" else slice); "count", n reqs.Length; "status", s (statusOf slice) ])
    [ "isFactory", b (st.Screen = FactoryScreen)
      "factoryStatus", s status
      "hasFactory", b info.IsSome
      "factoryCommit", s (info |> Option.map (fun f -> f.Commit) |> Option.defaultValue "")
      "factoryExperiment", s (info |> Option.map (fun f -> f.Experiment) |> Option.defaultValue "")
      "factoryRequirementCount", n requirements.Length
      "factoryTests",
      s (
          match info |> Option.bind (fun f -> f.Tests) with
          | Some t ->
              string t.Total + " tests recorded, " + string t.Passed + " passed, " + string t.Failed + " failed"
              + (if t.RunCommit <> "" then " (run on commit " + (if t.RunCommit.Length > 7 then t.RunCommit.Substring(0, 7) else t.RunCommit) + ")" else "")
              + ". Requirements: " + string t.Tested + " tested, " + string t.Untested + " untested, " + string t.Failing + " failing."
              + (if t.Suites = "engine" then " Covers the engine (xUnit) tests only; the browser end-to-end suite runs in CI and in the deploy acceptance job and is not included here." else "")
          | None -> "Test results were not available when this site was built."
      )
      "factoryBuildStatus",
      s (
          match info |> Option.map (fun f -> f.BuildStatus) with
          | Some "passed" -> "Build status: every recorded test passed (see the suites covered above)"
          | Some "failing" -> "Build status: some recorded tests failed"
          | _ -> "Build status: unknown (no test run was recorded for this build)"
      )
      "factoryRequirements",
      JArr(
          requirements
          |> List.map (fun r ->
              item
                  [ "id", s r.ReqId
                    "title", s r.ReqTitle
                    "scope", s (if r.ReqScope = "scoped-factory" then "Factory" else "Product")
                    "slice", s (if r.ReqSlice = "" then "—" else r.ReqSlice)
                    "status", s (statusOf r.ReqSlice)
                    "coverage", s (if r.Coverage = "" then "no test run recorded" else r.Coverage) ])
      )
      "factorySlices", JArr slices
      "factoryWorkItems", JArr(workItems |> List.map (fun (id, title, status) -> item [ "id", s id; "title", s title; "status", s status ])) ]

/// The addressed entity of an EntityScreen: (kind word, label, text, state).
let private focusOf (inv: Investigation) (events: Event list) (kind: EntityKind) (g: Guid) : (string * string * string * string) option =
    let l = labelsOf events
    match kind with
    | HypothesisEntity -> inv.Hypotheses |> Map.tryFind (Id.create g) |> Option.map (fun h -> "Hypothesis", lbl l.Hyp g, h.Statement.Value, Words.hypothesis h.State)
    | EvidenceEntity -> inv.Evidence |> Map.tryFind (Id.create g) |> Option.map (fun e -> "Evidence", lbl l.Ev g, evidenceText e, "Source: " + e.Source.Value)
    | UnknownEntity -> inv.Unknowns |> Map.tryFind (Id.create g) |> Option.map (fun u -> "Open question", "", u.Question.Value, Words.unknown u.State)
    | ContradictionEntity -> inv.Contradictions |> Map.tryFind (Id.create g) |> Option.map (fun c -> "Contradiction", "", c.Statement.Value, Words.contradiction c.State)
    | ActionEntity -> inv.Actions |> Map.tryFind (Id.create g) |> Option.map (fun a -> "Test or experiment", lbl l.Act g, a.Purpose.Value, Words.action a.State)
    | CausalAssertionEntity -> inv.Causes |> Map.tryFind (Id.create g) |> Option.map (fun c -> "Cause", lbl l.Cause g, c.Outcome.Value, Words.cause c.State)
    | CorrectiveActionEntity -> inv.CorrectiveActions |> Map.tryFind (Id.create g) |> Option.map (fun c -> "Fix", lbl l.Corr g, c.Description.Value, Words.correction c.State)
    | CriterionEntity -> inv.Criteria |> Map.tryFind (Id.create g) |> Option.map (fun c -> "Success criterion", lbl l.Crit g, c.ExpectedOutcome.Value, "")
    | ProposalEntity -> inv.Proposals |> Map.tryFind (Id.create g) |> Option.map (fun p -> "AI suggestion", "", p.Text.Value, Words.proposal p.State)
    | _ -> None

let project (st: AppState) : (string * J) list =
    let screenOk iid = st.Investigations.ContainsKey iid
    let loading =
        match st.Screen with
        | WorkspaceScreen iid | ReportScreen iid | EntityScreen(iid, _, _) -> not st.Loaded && not (screenOk iid)
        | _ -> not st.Loaded
    let current = currentId st |> Option.bind (fun iid -> investigationOf st iid |> Option.map (fun inv -> inv, st.Investigations[iid]))
    let focus =
        match st.Screen, current with
        | EntityScreen(_, kind, g), Some(inv, events) -> focusOf inv events kind g
        | _ -> None
    let isWorkspace, isReport, missing =
        match st.Screen with
        | WorkspaceScreen iid -> screenOk iid, false, st.Loaded && not (screenOk iid)
        | EntityScreen(iid, _, _) -> screenOk iid && focus.IsSome, false, (st.Loaded && not (screenOk iid)) || (screenOk iid && focus.IsNone)
        | ReportScreen iid -> false, screenOk iid, st.Loaded && not (screenOk iid)
        | MissingScreen _ | RefusedLinkScreen _ -> false, false, true
        | HomeScreen | SettingsScreen | FactoryScreen -> false, false, false
    let ws =
        match current, placeholder with
        | Some(inv, events), _ -> workspaceKeys st inv events true @ reportKeys inv events true
        | None, Some p -> workspaceKeys st p [] false @ reportKeys p [] false
        | None, None -> []
    let title =
        match st.Screen, current with
        | HomeScreen, _ -> "Root-Cause Investigator"
        | SettingsScreen, _ -> "GitHub settings · Root-Cause Investigator"
        | WorkspaceScreen _, Some(inv, _) -> inv.Title.Value + " · Root-Cause Investigator"
        | EntityScreen _, Some(inv, _) when isWorkspace -> inv.Title.Value + " · Root-Cause Investigator"
        | FactoryScreen, _ -> "How this app was built · Root-Cause Investigator"
        | ReportScreen _, Some(inv, _) -> "Report: " + inv.Title.Value + " · Root-Cause Investigator"
        | _, _ when loading -> "Loading · Root-Cause Investigator"
        | _ -> "Not found · Root-Cause Investigator"
    let missingText =
        match st.Screen with
        | MissingScreen path -> "There is no page at \"" + Words.clip 80 path + "\"."
        | RefusedLinkScreen parameter ->
            "This link was not opened: it carries the parameter \"" + parameter + "\", which is never allowed in links because it could expose a secret."
        | EntityScreen(iid, _, _) when screenOk iid ->
            "This investigation has no such item. It may have been mistyped; history is never deleted, so nothing was removed."
        | WorkspaceScreen _ | ReportScreen _ | EntityScreen _ -> "This investigation is not on this device. It may have been created in another browser, or the link is wrong."
        | _ -> ""
    let backToInvestigation =
        match st.Screen with
        | EntityScreen(iid, _, _) when screenOk iid -> Some(ScreenRoute.format (WorkspaceScreen iid))
        | _ -> None
    let focusKeys =
        let kind, label, text, state = focus |> Option.defaultValue ("", "", "", "")
        [ "hasFocus", b focus.IsSome
          "focusKind", s kind
          "focusLabel", s label
          "focusText", s text
          "focusState", s state
          "hasMissingBack", b backToInvestigation.IsSome
          "missingBackHref", s (backToInvestigation |> Option.defaultValue "#/") ]
    let refusals =
        st.Refusals
        |> List.mapi (fun i r -> item [ "id", s (string i); "code", s r.Code; "message", s r.Message; "rule", s r.Rule; "fault", s ("Aegis fault " + r.FaultCode + " (domain refusal, " + r.Operation + ")") ])
    let draftValue f =
        match f, st.Remote with
        | "setOwner", Some r when not (st.Drafts.ContainsKey f) -> r.Owner
        | "setRepo", Some r when not (st.Drafts.ContainsKey f) -> r.Repository
        | "setBranch", Some r when not (st.Drafts.ContainsKey f) -> r.Branch
        | "setBranch", None when not (st.Drafts.ContainsKey f) -> "main"
        | "evKind", _ when not (st.Drafts.ContainsKey f) -> "text"
        | _ -> draft st f
    [ "pageTitle", s title
      "booted", b true
      "isHome", b (st.Screen = HomeScreen)
      "isWorkspace", b isWorkspace
      "isReport", b isReport
      "isSettings", b (st.Screen = SettingsScreen)
      "isMissing", b (missing && not loading)
      "isLoading", b (loading && st.Screen <> SettingsScreen)
      "missingText", s missingText
      "notice", s st.Notice
      "refusals", JArr refusals
      "hasRefusals", b (not refusals.IsEmpty)
      "connection", s (if st.Online then "Online" else "Offline — work continues on this device")
      "isOffline", b (not st.Online)
      "remoteConfigured", b st.Remote.IsSome
      "needsToken", b (st.Remote.IsSome && not (hasToken st))
      "startDisabled", b (not st.Loaded)
      "settingsSummary",
      s (
          match st.Remote with
          | Some r when r.Token = "" ->
              "GitHub repository " + r.Owner + "/" + r.Repository + " (branch " + r.Branch + "). No access token in this session: enter it to sync. Tokens are kept in memory only and are asked for again after a reload."
          | Some r -> "Syncing to GitHub repository " + r.Owner + "/" + r.Repository + " (branch " + r.Branch + "). The access token is kept in memory for this session only; it is never saved, shown or put in links."
          | None -> "Not connected to GitHub. Investigations are saved on this device only."
      ) ]
    @ homeKeys st
    @ (draftFields |> List.map (fun f -> "draft_" + f, s (draftValue f)))
    @ ws
    @ focusKeys
    @ factoryKeys st

// ---------------------------------------------------------------------------
// handle: the whole engine, one message at a time
// ---------------------------------------------------------------------------

let private step (message: Inbound) (st: AppState) : AppState * Effect list * HandshakeReply =
    match message with
    | Initialize(hash, baseUrl, offer) ->
        let reply = Wire.negotiate offer
        let lifecycle = match reply with Accepted(_, _, selected) -> not selected.IsEmpty | _ -> false
        let st = { st with Screen = ScreenRoute.parse hash; LifecycleNegotiated = lifecycle; BaseUrl = baseUrl }
        let c1, st = Ids.correlation st LoadSettings
        let c2, st = Ids.correlation st LoadSync
        let c3, st = Ids.correlation st LoadIndex
        let loads = [ StorageGet(c1, Storage.settingsKey); StorageGet(c2, Storage.syncKey); StorageGet(c3, Storage.indexKey) ]
        let st, subscribe =
            if lifecycle then
                let c, st = Ids.correlation st Subscribe
                st, [ SubscribeConnectivity c ]
            else st, []
        let st, factory = ensureFactory st
        st, loads @ subscribe @ factory, reply
    | UiEvent(name, key, value) ->
        let st, fx = onEvent name key value st
        st, fx, NoHandshake
    | StorageDone(c, result) ->
        match Map.tryFind c st.Pending with
        | Some op ->
            let st, fx = onStorage op result { st with Pending = st.Pending |> Map.remove c }
            st, fx, NoHandshake
        | None -> st, [], NoHandshake
    | HttpDone(c, outcome) ->
        match Map.tryFind c st.Pending with
        | Some(RemoteRead(iid, remote)) when remote = currentIdentity st ->
            let st, fx = onRemoteRead iid outcome { st with Pending = st.Pending |> Map.remove c }
            st, fx, NoHandshake
        | Some(RemoteWrite(iid, remote)) when remote = currentIdentity st ->
            let st, fx = onRemoteWrite iid outcome { st with Pending = st.Pending |> Map.remove c }
            st, fx, NoHandshake
        | Some FactoryLoad ->
            let factory =
                match outcome with
                | HttpSuccess(200, body) -> (match decodeFactory body with Ok f -> FactoryLoaded f | Error e -> FactoryFailed e)
                | HttpSuccess(status, _) -> FactoryFailed("the server answered " + string status)
                | HttpFailure(reason, _) -> FactoryFailed("the request failed (" + reason + ")")
                | HttpCancelled -> FactoryFailed "the request was cancelled"
                | HttpOutcomeUnknown reason -> FactoryFailed reason
            { st with Pending = st.Pending |> Map.remove c; Factory = factory }, [], NoHandshake
        | _ -> { st with Pending = st.Pending |> Map.remove c }, [], NoHandshake
    | CapabilityDone(c, online) ->
        let st = { st with Pending = st.Pending |> Map.remove c; Online = Option.defaultValue st.Online online }
        let st, fx = if st.Loaded then sendAllPending st else st, []
        st, fx, NoHandshake
    | NavigationDone c -> { st with Pending = st.Pending |> Map.remove c }, [], NoHandshake
    | ClipboardDone(c, failure) ->
        let notice =
            match Map.tryFind c st.Pending, failure with
            | Some(CopyLink url), None -> "Link copied: " + url
            | Some(CopyLink url), Some "denied" -> "The browser did not allow copying. The link is: " + url
            | Some(CopyLink url), Some _ -> "Copying is not available here. The link is: " + url
            | _ -> st.Notice
        { st with Pending = st.Pending |> Map.remove c; Notice = notice }, [], NoHandshake
    | LocationChanged hash ->
        let st, fx = ensureFactory { st with Screen = ScreenRoute.parse hash; Refusals = []; Notice = "" }
        st, fx, NoHandshake
    | ConnectivityChanged online ->
        let st = { st with Online = online }
        let st, fx = if online && st.Loaded then sendAllPending st else st, []
        st, fx, NoHandshake
    | Unrecognized _ -> st, [], NoHandshake

/// Pure: one Limen message in, the next state and the response JSON out.
let handle (state: AppState) (messageJson: string) : AppState * string =
    let next, effects, reply = step (Wire.decode messageJson) state
    next, Wire.encode (project next) effects reply
