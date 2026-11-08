// causa-storage 1.2.0 conformance fixtures (contract/v1.2.0/CONTRACT.md section 5), deterministic.
//
// The pinned rehearsal engine (engine.lock.json) knows no 1.1.0 or 1.2.0 event, so these fixtures are decided by the
// Causa application's domain kernel (kemiller2002/causa, Causa.Domain: Decide/Evolve) and written by its causa-storage
// codec (Causa.Persistence: StorageCodecs, Wire), the first writer of 1.2.0. Ids are UUIDv5 under the fixture
// namespace and the clock is fixed, so a rebuild is byte-identical. The negative fixtures are mutations of the valid
// history, written with the same codec so that each fails exactly its targeted rule.
//
// Usage, from the causa-data root (.local/ is git-ignored):
//   mkdir -p .local/causa-build
//   cp <causa checkout>/src/Causa.Persistence/bin/Release/net10.0/Causa.{Domain,Persistence}.dll .local/causa-build/
//   dotnet fsi tools/v120/build-fixtures.fsx
#I "../../.local/causa-build"
#r "Causa.Domain.dll"
#r "Causa.Persistence.dll"

open System
open System.IO
open System.Text
open Causa.Domain
open Causa.Persistence

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))
let version = StorageVersion.v120
let ns = EventIdentity.uuidV5 EventIdentity.namespaceUrl "https://github.com/kemiller2002/causa-data/fixtures/v1.2.0"
let uid (name: string) = EventIdentity.uuidV5 ns name
let text (s: string) = NonEmptyText.create s |> Option.get
let investigation = InvestigationId(uid "details-revision/investigation")
let (InvestigationId invGuid) = investigation
let invText = Canonical.guidText invGuid
let actor = Actor.Human(text "causa-operator")
let clock = DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero)

let primaryQuestion = uid "details-revision/unknown/primary-question"
let diskUnknown = uid "details-revision/unknown/disk"
let startObservation = uid "details-revision/observation/start"

/// The commands, in order; each is its own command group at the next fixed minute.
let commands : (string * CoreCommand) list =
    [ "start", StartInvestigation { Title = text "Nightly export stopped"; ObservationId = startObservation; Original = text "The nightly export stopped at 02:10." }
      "primary-question", IdentifyUnknown { UnknownId = primaryQuestion; Question = text "Why did the export stop?"; Materiality = Materiality.Informational }
      "activate", ActivateInvestigation
      "disk-unknown", IdentifyUnknown { UnknownId = diskUnknown; Question = text "Did the export disk fill?"; Materiality = Materiality.Material }
      "disk-observation", RecordObservation { ObservationId = uid "details-revision/observation/disk"; Original = text "Disk usage was 99% at 02:05."; ObservedAt = None; Source = None }
      // verbatim: leading and trailing spaces are kept
      "title-1", ReviseTitle { Title = text "  Nightly export stopped at 02:10  "; Reason = Some(text "The time narrows the question.") }
      // verbatim: an embedded LF
      "question-1", ReviseUnknown { UnknownId = primaryQuestion; Question = text "Why did the nightly export stop at 02:10,\nand why was no alert raised?" }
      "problem-1", ReviseObservation { ObservationId = startObservation; Original = text "The nightly export stopped at 02:10 and wrote no file."; Source = None }
      // verbatim: U+200B; no reason
      "title-2", ReviseTitle { Title = text "Nightly export stopped at 02:10​"; Reason = None }
      "disk-question-1", ReviseUnknown { UnknownId = diskUnknown; Question = text "Did the export disk fill before 02:10?" } ]

let history =
    commands
    |> List.indexed
    |> List.fold
        (fun (state: Investigation option, events: EventEnvelope list) (n, (name, command)) ->
            let commandId = uid ("details-revision/command/" + name)
            let envelope = { CommandId = commandId; Investigation = investigation; ExpectedVersion = state |> Option.map (fun i -> i.Version); Command = Core command }
            match Kernel.kernel.Decide Policies.coreDefault { ActorId = "causa-operator" } state envelope with
            | Allowed payloads ->
                let current = state |> Option.map (fun i -> i.Version) |> Option.defaultValue (AggregateVersion 0L)
                let added = Events.envelopes investigation current commandId (clock.AddMinutes(float (4 * n))) actor version payloads
                let next = added |> List.fold (fun s e -> Kernel.kernel.Evolve s e |> Result.toOption) state
                next, events @ added
            | other -> failwithf "%s: %A" name other)
        (None, [])
    |> snd

let files =
    match StorageCodecs.encodeHistory investigation history with
    | Ok f -> f
    | Error e -> failwith e

// The written files verify (stages 2-17, snapshot = projection of the replay) and replay through the kernel.
match StorageCodecs.codec.Verify (WorkspaceAddress.catalog (Workspace "fixtures/v1.2.0")) (CommitOid "fixture") files with
| Ok _ -> ()
| Error e -> failwithf "the valid fixture does not verify: %A" e

let wire =
    match Wire.verify invText files with
    | Ok w -> w.Events
    | Error e -> failwithf "%A" e

let dir = Path.Combine(root, "fixtures", "v1.2.0")
let invDir = Path.Combine(dir, "investigations", invText)
Directory.CreateDirectory invDir |> ignore
Directory.CreateDirectory(Path.Combine(dir, "negative")) |> ignore
File.WriteAllBytes(Path.Combine(invDir, "manifest.json"), files.Manifest)
File.WriteAllBytes(Path.Combine(invDir, "events.ndjson"), files.Events)
File.WriteAllBytes(Path.Combine(invDir, "snapshot.json"), files.Snapshot)

// ---- negatives ---------------------------------------------------------------------------------------------------

let asNew (e: WireEvent) : NewWireEvent = { EventType = e.EventType; OccurredAt = e.OccurredAt; Actor = e.Actor; CorrelationId = e.CorrelationId; Payload = e.Payload }

/// The valid history's first k events (versions 1..k), re-stamped in the given version.
let prefix k (v: string) = wire |> List.take k |> List.map (fun e -> { e with FormatVersion = v; SemanticVersion = v })

let extra (name: string) (minute: int) (eventType: string) (payload: Json) : NewWireEvent =
    { EventType = eventType
      OccurredAt = Canonical.timestamp (clock.AddMinutes(float minute)) |> Option.get
      Actor = DomainWire.actorJson actor
      CorrelationId = Canonical.guidText (uid ("details-revision/negative/" + name))
      Payload = payload }

let titleRevised title = Json.Object [ "reason", Json.Null; "title", Json.String title ]
let unknownRevised (id: Guid) question = Json.Object [ "question", Json.String question; "unknownId", Json.String(Canonical.guidText id) ]

/// The first five events: start, primary question, activate, the disk unknown, the disk observation (no revision).
let base' = 5

let negative (name: string) (description: string) (code: string) (domainCode: string option) (fs: FileSet) =
    let b64 (b: byte[]) = Json.Object [ "base64", Json.String(Convert.ToBase64String b) ]
    let doc =
        Json.Object
            [ "description", Json.String description
              "directoryId", Json.String invText
              "expectedDomainCode", (match domainCode with Some d -> Json.String d | None -> Json.Null)
              "expectedErrorCode", Json.String code
              "files", Json.Object [ "events.ndjson", b64 fs.Events; "manifest.json", b64 fs.Manifest; "snapshot.json", b64 fs.Snapshot ]
              "formatVersion", Json.String version
              "kind", Json.String "investigation"
              "name", Json.String name ]
    File.WriteAllBytes(Path.Combine(dir, "negative", name + ".json"), Canonical.fileBytes doc)
    name, code, domainCode

let build (v: string) (k: int) (added: NewWireEvent list) = Wire.fileSet v invText (Wire.append v invText (prefix k v) added)

let mixed =
    // The first event line says 1.1.0; every other version string says 1.2.0 (the bytes stay canonical).
    let lines = (Encoding.UTF8.GetString files.Events).Split('\n')
    lines[0] <- lines[0].Replace("\"formatVersion\":\"1.2.0\"", "\"formatVersion\":\"1.1.0\"").Replace("\"semanticVersion\":\"1.2.0\"", "\"semanticVersion\":\"1.1.0\"")
    { files with Events = Encoding.UTF8.GetBytes(String.Join("\n", lines)) }

let negatives =
    [ negative "v110-title-revised" "A 1.1.0 history holds InvestigationTitleRevised, a type the 1.1.0 registry does not have."
          "CAUSA.UNKNOWN_EVENT_TYPE" None
          (build "1.1.0" base' [ extra "v110-title" 20 "InvestigationTitleRevised" (titleRevised "Nightly export stopped at 02:10") ])
      negative "mixed-version-1-1-0-1-2-0" "One event line of a 1.2.0 investigation carries 1.1.0: one investigation has one version."
          "CAUSA.UNSUPPORTED_VERSION" None mixed
      negative "title-revised-closed" "The title is revised after the investigation was closed."
          "CAUSA.HISTORY_INADMISSIBLE" (Some "INVESTIGATION.NOT_OPEN")
          (build version base'
              [ extra "close" 20 "LifecycleChanged" (Json.Object [ "command", Json.String "closeInvestigation"; "from", Json.String "active"; "reason", Json.Null; "to", Json.String "closed" ])
                extra "closed-title" 24 "InvestigationTitleRevised" (titleRevised "Nightly export stopped at 02:10") ])
      negative "unknown-revised-closed" "An unknown's question is revised after the investigation was closed."
          "CAUSA.HISTORY_INADMISSIBLE" (Some "INVESTIGATION.NOT_OPEN")
          (build version base'
              [ extra "close" 20 "LifecycleChanged" (Json.Object [ "command", Json.String "closeInvestigation"; "from", Json.String "active"; "reason", Json.Null; "to", Json.String "closed" ])
                extra "closed-question" 24 "UnknownRevised" (unknownRevised primaryQuestion "Why did the export stop at 02:10?") ])
      negative "unknown-revised-missing" "UnknownRevised names an unknown that does not exist."
          "CAUSA.HISTORY_INADMISSIBLE" (Some "HISTORY.INADMISSIBLE_EVENT")
          (build version base' [ extra "missing-question" 20 "UnknownRevised" (unknownRevised (uid "details-revision/unknown/missing") "Is there a second cause?") ])
      negative "unknown-revised-resolved" "UnknownRevised names an unknown that is resolved; only an identified or investigating unknown is revised."
          "CAUSA.HISTORY_INADMISSIBLE" (Some "HISTORY.INADMISSIBLE_EVENT")
          (build version base'
              [ extra "resolve-disk" 20 "UnknownTransitioned" (Json.Object [ "basis", Json.String "The disk had 1% free; it did not fill."; "state", Json.String "resolved"; "unknownId", Json.String(Canonical.guidText diskUnknown) ])
                extra "resolved-question" 24 "UnknownRevised" (unknownRevised diskUnknown "Did the export disk fill before 02:10?") ])
      negative "title-revised-in-group" "InvestigationTitleRevised shares its command group with another event; a revision is a group of its own."
          "CAUSA.HISTORY_INADMISSIBLE" (Some "HISTORY.INADMISSIBLE_EVENT")
          (let a = extra "shared-group" 20 "InvestigationTitleRevised" (titleRevised "Nightly export stopped at 02:10")
           let b = { extra "shared-group" 20 "UnknownRevised" (unknownRevised primaryQuestion "Why did the export stop at 02:10?") with CorrelationId = a.CorrelationId }
           build version base' [ a; b ])
      negative "title-blank" "InvestigationTitleRevised.title is whitespace only; a title is non-blank text."
          "CAUSA.SCHEMA_VIOLATION" None
          (build version base' [ extra "blank-title" 20 "InvestigationTitleRevised" (titleRevised " \t ") ])
      negative "question-blank" "UnknownRevised.question is whitespace only; a question is non-blank text."
          "CAUSA.SCHEMA_VIOLATION" None
          (build version base' [ extra "blank-question" 20 "UnknownRevised" (unknownRevised primaryQuestion "   ") ])
      negative "title-revised-unknown-field" "InvestigationTitleRevised carries a field the 1.2.0 schema does not define."
          "CAUSA.SCHEMA_VIOLATION" None
          (build version base' [ extra "extra-field" 20 "InvestigationTitleRevised" (Json.Object [ "reason", Json.Null; "subtitle", Json.String "x"; "title", Json.String "Nightly export" ]) ]) ]

// Every negative is refused with its code by the codec that wrote the valid set.
for (name, code, domainCode) in negatives do
    let bytes = File.ReadAllBytes(Path.Combine(dir, "negative", name + ".json"))
    let doc = Json.parse (Encoding.UTF8.GetString bytes) |> Option.get
    let file n = Json.field "files" (Some doc) |> fun f -> Json.field n f |> fun x -> Json.field "base64" x |> Json.text |> Option.get |> Convert.FromBase64String
    let fs = { Manifest = file "manifest.json"; Events = file "events.ndjson"; Snapshot = file "snapshot.json" }
    match Wire.verify invText fs with
    | Error r when r.Code = code && (domainCode.IsNone || Some r.Detail = domainCode) -> ()
    | other -> failwithf "%s: expected %s %A, got %A" name code domainCode other

let index =
    Json.Object
        [ "formatVersion", Json.String version
          "investigations", Json.Array [ Json.Object [ "investigationId", Json.String invText; "scenarioKey", Json.String "details-revision" ] ]
          "negativeFixtures", Json.Array [ for (name, code, _) in negatives -> Json.Object [ "expectedErrorCode", Json.String code; "path", Json.String("negative/" + name + ".json") ] ] ]
File.WriteAllBytes(Path.Combine(dir, "index.json"), Canonical.fileBytes index)
printfn "fixtures/v1.2.0: %s (%d events), %d negatives" invText wire.Length negatives.Length
