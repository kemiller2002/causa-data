namespace Causa.Tool.Historical

open System
open System.IO
open IndyInitRootCauseInvestigator.Engine
open Causa.Contract
open Causa.Tool

/// A case definition: its key and the script that turns its research records into engine commands.
type CaseDefinition = { Key: string; Build: Kit.Ctx -> Case }

type BuiltCase =
    { Case: Case
      Events: Event list
      Refusals: (int * string * string) list
      Files: InvestigationFiles
      Loaded: Loaded }

/// The active historical catalog (catalog/historical-v1): three investigations under an isolated workspace,
/// built through Decide.execute and verified exactly as a reader would (CONTRACT §9.2, §11).
module Catalog =
    let catalogId = "causa-historical"
    let catalogVersion = "1.0.0"
    let runId = "historical-v1"
    let createdAt = DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero)
    let caseKeys = [ "apollo-1"; "apollo-11"; "challenger-sts-51l" ]

    let workspace (root: string) = Path.Combine(root, "workspaces", "demo", runId)
    let investigations (root: string) = Path.Combine(workspace root, "investigations")
    let catalogDir (root: string) = Path.Combine(root, "catalog", runId)
    let manifestPath (root: string) = Path.Combine(catalogDir root, "manifest.json")
    let activePath (root: string) = Path.Combine(root, "catalog", "active.json")
    let provenancePath (root: string) (key: string) = Path.Combine(catalogDir root, "provenance", key + ".json")

    let private relative (root: string) (path: string) = Layout.posix (Path.GetRelativePath(root, path))

    // ---------------- running ----------------

    /// Command i executes at Start + i minutes on the build clock (millisecond-exact UTC); command ids are derived.
    let timeOf (c: Case) (i: int) = c.Start.AddMinutes(float i)

    let commandId (c: Case) (i: int) = Kit.guid c.Key "command" (string i)

    let run (c: Case) : Result<Event list * (int * string * string) list, string> =
        let codes (errors: DomainError list) = String.Join("; ", errors |> List.map (fun e -> e.Code + " " + e.Message))
        c.Steps
        |> List.indexed
        |> List.fold
            (fun acc (i, st) ->
                acc
                |> Result.bind (fun (state: Investigation option, events: Event list, refusals) ->
                    let env =
                        { CommandId = commandId c i
                          Investigation = c.Investigation
                          ExpectedVersion = state |> Option.map (fun x -> x.Version)
                          Actor = st.Actor
                          RequestedAt = timeOf c i
                          Command = st.Command }
                    match Decide.execute state env, st.Expect with
                    | Ok produced, Accept -> Ok(produced |> List.fold (fun s e -> Some(Evolve.apply s e)) state, events @ produced, refusals)
                    | Error errors, Accept -> Error($"{c.Key} step {i} was refused: {codes errors}")
                    | Ok _, Refuse code -> Error($"{c.Key} step {i} was expected to be refused with {code} but was accepted")
                    | Error errors, Refuse code when errors |> List.exists (fun e -> e.Code = code) ->
                        Ok(state, events, refusals @ [ i, code, String.Join("; ", errors |> List.map (fun e -> e.Code)) ])
                    | Error errors, Refuse code -> Error($"{c.Key} step {i} was refused, but not with {code}: {codes errors}")))
            (Ok(None, [], []))
        |> Result.bind (fun (state, events, refusals) ->
            match state with
            | Some inv when inv.Lifecycle = c.ExpectedLifecycle -> Ok(events, refusals)
            | Some inv -> Error($"{c.Key} ended {inv.Lifecycle}, expected {c.ExpectedLifecycle}")
            | None -> Error(c.Key + " produced no investigation"))

    let private define (root: string) (d: CaseDefinition) : Result<Case, string> =
        Research.load root d.Key
        |> Result.bind (fun research ->
            try Ok(d.Build(Kit.Ctx research)) with CaseDefinitionError message -> Error message)

    let buildCase (set: SchemaCheck.SchemaSet) (root: string) (d: CaseDefinition) : Result<BuiltCase, string> =
        define root d
        |> Result.bind (fun case ->
            run case
            |> Result.bind (fun (events, refusals) ->
                Store.build set events
                |> Result.mapError (fun e -> case.Key + ": " + e.Code + " " + e.Detail)
                |> Result.map (fun (files, loaded) -> { Case = case; Events = events; Refusals = refusals; Files = files; Loaded = loaded })))

    let buildAll (set: SchemaCheck.SchemaSet) (root: string) (defs: CaseDefinition list) = defs |> traverse (buildCase set root)

    // ---------------- derived documents ----------------

    let private claimOf (source: string) =
        let m = Text.RegularExpressions.Regex.Match(source, @"\[claim ([A-Z0-9]+-C[0-9]+); source ([A-Z0-9]+-S[0-9]+); ")
        if m.Success then Some(m.Groups[1].Value, m.Groups[2].Value) else None

    /// Per-evidence provenance index: evidence id -> claim, source, locator, URL, verification (derived, canonical).
    let provenance (b: BuiltCase) : JValue =
        let r = b.Case.Research
        let claims = r.Claims |> List.map (fun c -> c.ClaimId, c) |> Map.ofList
        let entries =
            b.Loaded.State.Evidence
            |> Map.toList
            |> List.map snd
            |> List.sortWith (fun a x -> String.CompareOrdinal(Uuid.text a.Id.Value, Uuid.text x.Id.Value))
            |> List.choose (fun e ->
                claimOf e.Source.Value
                |> Option.bind (fun (cid, _) -> claims |> Map.tryFind cid)
                |> Option.map (fun c ->
                    let s = r.Sources[c.SourceId]
                    JObj
                        [ "evidenceId", JStr(Uuid.text e.Id.Value); "claimId", JStr c.ClaimId; "sourceId", JStr s.SourceId; "title", JStr s.Title
                          "issuer", JStr s.Issuer; "url", JStr s.Url; "locator", JStr c.Locator; "sourceClass", JStr s.SourceClass
                          "researchVerification", JStr c.Verification; "topic", JStr c.Topic; "kind", JStr c.Kind
                          "historicalTime", (c.HistoricalTime |> Option.bind Timestamp.tryFormat |> Option.map JStr |> Option.defaultValue JNull) ]))
        JObj
            [ "caseKey", JStr b.Case.Key; "investigationId", JStr(Uuid.text b.Case.Investigation.Value)
              "excludedUnverifiedClaims", JArr(r.Claims |> List.filter (Research.usable >> not) |> List.map (fun c -> JStr c.ClaimId))
              "evidence", JArr entries ]

    let private sha (bytes: byte[]) = Json.sha256Hex bytes

    let private entry (root: string) (b: BuiltCase) : JValue =
        let r = b.Case.Research
        let usable = r.Claims |> List.filter Research.usable
        JObj
            [ "caseKey", JStr b.Case.Key
              "investigationId", JStr b.Files.DirectoryId
              "title", JStr b.Case.Title
              "path", JStr(relative root (Path.Combine(investigations root, b.Files.DirectoryId)))
              "expectedVersion", JInt b.Loaded.State.Version
              "expectedLifecycle", JStr(Enums.lifecycle.ToWire b.Loaded.State.Lifecycle)
              "expectedStateSha256", JStr b.Loaded.StateSha256
              "eventsSha256", JStr b.Loaded.EventsSha256
              "manifestSha256", JStr b.Loaded.ManifestSha256
              "snapshotSha256", JStr b.Loaded.SnapshotSha256
              "researchSourcesSha256", JStr(sha r.SourcesBytes)
              "researchClaimsSha256", JStr(sha r.ClaimsBytes)
              "provenanceSha256", JStr(Json.digest (provenance b) |> Result.defaultValue "")
              "sourceCount", JInt(int64 r.Sources.Count)
              "primarySourceCount", JInt(r.Sources |> Map.filter (fun _ s -> s.SourceClass = "primary") |> Map.count |> int64)
              "claimCount", JInt(int64 r.Claims.Length)
              "evidenceCount", JInt(int64 b.Loaded.State.Evidence.Count)
              "excludedClaimCount", JInt(int64 (r.Claims.Length - usable.Length)) ]

    let manifest (root: string) (schemaSha: string) (engineCommit: string) (built: BuiltCase list) : JValue =
        JObj
            [ "catalogId", JStr catalogId
              "catalogVersion", JStr catalogVersion
              "formatVersion", JStr Contract.formatVersion
              "semanticVersion", JStr Contract.semanticVersion
              "schemaSha256", JStr schemaSha
              "engineCommit", JStr engineCommit
              "createdAt", JStr(Timestamp.tryFormat createdAt |> Option.get)
              "workspace", JStr(relative root (workspace root))
              "cases", JArr(built |> List.sortBy (fun b -> List.findIndex ((=) b.Case.Key) caseKeys) |> List.map (entry root))
              "retainedCompatibilityDataset",
              JObj
                  [ "datasetId", JStr Contract.datasetId
                    "manifest", JStr "fixtures/v1.0.0/manifest.json"
                    "status", JStr "immutable v1.0.0 compatibility fixtures; not part of the active demonstration catalog" ] ]

    let private engineCommit (root: string) =
        File.ReadAllBytes(Path.Combine(root, "engine.lock.json"))
        |> Json.decodeUtf8
        |> Result.bind Json.parse
        |> Result.map (fun v -> match v with JObj o -> (match Json.field "commit" o with Some(JStr s) -> s | _ -> "") | _ -> "")
        |> Result.defaultValue ""

    let private fileBytes (v: JValue) = Json.fileBytes v |> Result.defaultWith failwith

    /// Every output file (path relative to root, bytes). Pure: writing and reproducing share it.
    let outputs (root: string) (built: BuiltCase list) : (string * byte[]) list =
        let schemaSha = sha (File.ReadAllBytes(Layout.bundle root))
        let investigationFiles =
            built
            |> List.collect (fun b ->
                let dir = Path.Combine(investigations root, b.Files.DirectoryId)
                [ Path.Combine(dir, "manifest.json"), b.Files.Manifest
                  Path.Combine(dir, "events.ndjson"), b.Files.Events
                  Path.Combine(dir, "snapshot.json"), b.Files.Snapshot ])
        let catalogFiles =
            [ manifestPath root, fileBytes (manifest root schemaSha (engineCommit root) built) ]
            @ (built |> List.map (fun b -> provenancePath root b.Case.Key, fileBytes (provenance b)))
        (investigationFiles @ catalogFiles) |> List.map (fun (p, bytes) -> relative root p, bytes)

    let write (root: string) (built: BuiltCase list) =
        outputs root built |> List.iter (fun (rel, bytes) -> Layout.writeBytes (Path.Combine(root, rel)) bytes)
