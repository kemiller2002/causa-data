namespace Causa.Tool.Historical

open System
open System.IO
open IndyInitRootCauseInvestigator.Engine
open Causa.Contract
open Causa.Tool

/// Verification of the active historical catalog from the committed files only (no builder state), plus a
/// deterministic-reconstruction comparison against a fresh build.
module CatalogVerify =
    type Check = Dataset.Check

    let private check name passed detail : Check = { Name = name; Passed = passed; Detail = detail }

    let private readJson (path: string) =
        try File.ReadAllBytes path |> Json.decodeUtf8 |> Result.bind (fun t -> Json.parse (t.TrimEnd('\n'))) with ex -> Error ex.Message

    let private get (k: string) (v: JValue) = match v with JObj o -> Json.field k o |> Option.defaultValue JNull | _ -> JNull
    let private str k v = match get k v with JStr s -> s | _ -> ""
    let private arr k v = match get k v with JArr xs -> xs | _ -> []

    let private canonicalFile (path: string) =
        match readJson path with
        | Ok v -> (Json.fileBytes v |> Result.defaultValue [||]) = File.ReadAllBytes path
        | Error _ -> false

    let private claimRef = Text.RegularExpressions.Regex(@"\[claim ([A-Z0-9]+-C[0-9]+); source ([A-Z0-9]+-S[0-9]+); (primary|secondary) source; retrieved [0-9-]+\]\z")

    /// Provenance rules for one replayed investigation against its research records.
    let provenanceChecks (key: string) (research: Research) (state: Investigation) : Check list =
        let claims = research.Claims |> List.map (fun c -> c.ClaimId, c) |> Map.ofList
        let evidence = state.Evidence |> Map.toList |> List.map snd
        let refs = evidence |> List.map (fun e -> e, claimRef.Match e.Source.Value)
        let unreferenced = refs |> List.filter (fun (_, m) -> not m.Success) |> List.map (fun (e, _) -> Uuid.text e.Id.Value)
        let cited = refs |> List.filter (fun (_, m) -> m.Success) |> List.map (fun (e, m) -> e, m.Groups[1].Value, m.Groups[2].Value)
        let dangling = cited |> List.filter (fun (_, c, s) -> not (claims.ContainsKey c) || claims[c].SourceId <> s) |> List.map (fun (_, c, _) -> c)
        let admittedUnverified = cited |> List.filter (fun (_, c, _) -> claims |> Map.tryFind c |> Option.exists (Research.usable >> not)) |> List.map (fun (_, c, _) -> c)
        let statusDrift =
            cited
            |> List.filter (fun (e, c, _) -> claims |> Map.tryFind c |> Option.exists (fun cl -> Research.verification cl <> e.Verification))
            |> List.map (fun (_, c, _) -> c)
        let admittedIds = cited |> List.map (fun (_, c, _) -> c) |> Set.ofList
        let notAdmitted = research.Claims |> List.filter (fun c -> Research.usable c && not (admittedIds.Contains c.ClaimId)) |> List.map (fun c -> c.ClaimId)
        let fromUnreadSource =
            research.Claims
            |> List.filter (fun c -> c.Verification <> "unverified" && research.Sources[c.SourceId].RetrievalStatus <> "retrieved")
            |> List.map (fun c -> c.ClaimId)
        let contentMismatch =
            cited
            |> List.filter (fun (e, c, _) ->
                claims |> Map.tryFind c |> Option.exists (fun cl -> (match e.Content with TextEvidence t -> t.Value | ArtifactEvidence u -> u) <> Research.content cl))
            |> List.map (fun (_, c, _) -> c)
        let list (xs: string list) = if xs.IsEmpty then "none" else String.Join(", ", xs |> List.truncate 12) + (if xs.Length > 12 then $" (+{xs.Length - 12})" else "")
        [ check (key + ".provenance.every-evidence-cites-a-claim") unreferenced.IsEmpty ("evidence without a claim/source citation: " + list unreferenced)
          check (key + ".provenance.citations-resolve") dangling.IsEmpty ("citations naming an unknown claim or the wrong source: " + list dangling)
          check (key + ".provenance.no-unverified-claim-admitted") admittedUnverified.IsEmpty ("unverified claims admitted as evidence: " + list admittedUnverified)
          check (key + ".provenance.every-usable-claim-admitted") notAdmitted.IsEmpty ("verified or partially verified claims not admitted: " + list notAdmitted)
          check (key + ".provenance.verification-matches-research") statusDrift.IsEmpty ("evidence whose assessment differs from the research record: " + list statusDrift)
          check (key + ".provenance.content-matches-research") contentMismatch.IsEmpty ("evidence whose text differs from the research record: " + list contentMismatch)
          check (key + ".provenance.verified-only-from-read-sources") fromUnreadSource.IsEmpty ("claims marked verified whose source was not retrieved: " + list fromUnreadSource) ]

    let rec private permutations (xs: 'a list) : 'a list list =
        match xs with
        | [] -> [ [] ]
        | _ -> xs |> List.collect (fun x -> permutations (List.except [ x ] xs) |> List.map (fun rest -> x :: rest))

    /// All cases loaded together in one process, in every order and then the first order again: each replayed state must equal the
    /// catalog digest whatever was loaded before it, and no two cases may share an event id (a reader that indexes the
    /// whole catalog by event id must not see a collision). This proves the data side of EXT-DATA-013 natively; it is
    /// not a substitute for the browser runtime check.
    let combinedChecks (set: SchemaCheck.SchemaSet) (root: string) (cases: JValue list) : Check list =
        let dirOf c = Path.Combine(Catalog.investigations root, str "investigationId" c)
        let load c = Store.readDirectory (dirOf c) |> Result.bind (Store.validate set) |> Result.map (fun l -> str "caseKey" c, l.StateSha256, l.Events.Length)
        let orders = permutations cases
        let results = orders @ List.truncate 1 orders |> List.map (List.map load)
        let loaded = results |> List.concat
        let failures = loaded |> List.choose (function Error e -> Some(e.Code + " " + e.Detail) | Ok _ -> None) |> List.distinct
        let expected = cases |> List.map (fun c -> str "caseKey" c, str "expectedStateSha256" c) |> Map.ofList
        let drifted = loaded |> List.choose (function Ok(k, sha, _) when Map.tryFind k expected <> Some sha -> Some k | _ -> None) |> List.distinct
        let digestsPerCase = loaded |> List.choose Result.toOption |> List.groupBy (fun (k, _, _) -> k) |> List.map (fun (k, xs) -> k, xs |> List.map (fun (_, s, _) -> s) |> List.distinct)
        let unstable = digestsPerCase |> List.filter (fun (_, ds) -> ds.Length <> 1) |> List.map fst
        let eventIds c =
            File.ReadAllLines(Path.Combine(dirOf c, "events.ndjson"))
            |> Array.map (Json.parse >> Result.map (str "eventId") >> Result.defaultValue "")
            |> List.ofArray
        let allIds = cases |> List.collect eventIds
        let shared = allIds |> List.countBy id |> List.filter (fun (_, n) -> n > 1) |> List.map fst
        let total = cases |> List.sumBy (eventIds >> List.length)
        let failureText = if failures.IsEmpty then "none" else String.Join(';', failures)
        [ check "catalog.combined-load" (failures.IsEmpty && loaded.Length = (orders.Length + 1) * cases.Length)
              $"{cases.Length} cases loaded together in {orders.Length} orders plus a repeat ({loaded.Length} loads, {total} events per full load); failures: {failureText}"
          check "catalog.combined-replay-deterministic" (failures.IsEmpty && drifted.IsEmpty && unstable.IsEmpty)
              $"replayed state digests independent of load order and repetition; drifted: {String.Join(',', drifted)}; unstable: {String.Join(',', unstable)}"
          check "catalog.combined-event-ids-disjoint" (shared.IsEmpty && List.forall ((<>) "") allIds)
              $"{allIds.Length} event ids across cases, {shared.Length} shared" ]

    /// Every check on the committed catalog. `rebuilt` is a fresh in-memory build used for the reconstruction check.
    let verify (root: string) (rebuilt: Result<BuiltCase list, string>) : Check list =
        match Store.loadSchemas (Layout.schemas root) with
        | Error e -> [ check "schemas.load" false (e.Code + " " + e.Detail) ]
        | Ok set ->
            let activeOk = File.Exists(Catalog.activePath root) && canonicalFile (Catalog.activePath root)
            let activeV = readJson (Catalog.activePath root) |> Result.defaultValue JNull
            let pointsAtManifest = str "manifest" activeV = "catalog/historical-v1/manifest.json" && str "workspace" activeV = "workspaces/demo/historical-v1"
            match readJson (Catalog.manifestPath root) with
            | Error e -> [ check "catalog.manifest.parse" false e ]
            | Ok m ->
                let cases = arr "cases" m
                let keys = cases |> List.map (str "caseKey")
                let ids = cases |> List.map (str "investigationId")
                let dirs =
                    if Directory.Exists(Catalog.investigations root) then
                        Directory.GetFileSystemEntries(Catalog.investigations root) |> Array.map (fun p -> Path.GetFileName p |> nonNull) |> Array.sort |> List.ofArray
                    else []
                let fixtureIds =
                    readJson (Layout.rootManifest root)
                    |> Result.map (fun rm -> arr "investigations" rm |> List.map (str "investigationId"))
                    |> Result.defaultValue []
                let bundleSha = Json.sha256Hex (File.ReadAllBytes(Layout.bundle root))
                let caseChecks =
                    cases
                    |> List.collect (fun c ->
                        let key = str "caseKey" c
                        let id = str "investigationId" c
                        let dir = Path.Combine(Catalog.investigations root, id)
                        match Store.readDirectory dir |> Result.bind (Store.validate set) with
                        | Error e -> [ check (key + ".validate") false (e.Code + (e.DomainCode |> Option.map (fun d -> "/" + d) |> Option.defaultValue "") + " " + e.Detail) ]
                        | Ok loaded ->
                            let drift =
                                [ "eventsSha256", loaded.EventsSha256; "manifestSha256", loaded.ManifestSha256; "snapshotSha256", loaded.SnapshotSha256
                                  "expectedStateSha256", loaded.StateSha256; "expectedLifecycle", Enums.lifecycle.ToWire loaded.State.Lifecycle; "title", loaded.State.Title.Value
                                  "path", "workspaces/demo/historical-v1/investigations/" + id ]
                                |> List.filter (fun (k, v) -> str k c <> v)
                                |> List.map fst
                                |> fun xs -> if get "expectedVersion" c <> JInt loaded.State.Version then xs @ [ "expectedVersion" ] else xs
                            let researchChecks =
                                match Research.load root key with
                                | Error e -> [ check (key + ".research.load") false e ]
                                | Ok r ->
                                    let researchDrift =
                                        [ "researchSourcesSha256", Json.sha256Hex r.SourcesBytes; "researchClaimsSha256", Json.sha256Hex r.ClaimsBytes ]
                                        |> List.filter (fun (k, v) -> str k c <> v)
                                        |> List.map fst
                                    let primaryRetrieved = r.Sources |> Map.exists (fun _ s -> s.SourceClass = "primary" && s.RetrievalStatus = "retrieved")
                                    let provenanceFile = readJson (Catalog.provenancePath root key)
                                    let provenanceSha = provenanceFile |> Result.bind Json.digest |> Result.defaultValue ""
                                    [ check (key + ".research.load") true $"{r.Sources.Count} sources, {r.Claims.Length} claims"
                                      check (key + ".research.digests") researchDrift.IsEmpty ("manifest research digests differ: " + String.Join(",", researchDrift))
                                      check (key + ".research.primary-source-retrieved") primaryRetrieved "at least one primary source was retrieved and read"
                                      check (key + ".provenance.index")
                                          (canonicalFile (Catalog.provenancePath root key) && provenanceSha = str "provenanceSha256" c)
                                          "provenance index is canonical and matches the manifest digest" ]
                                    @ provenanceChecks key r loaded.State
                            [ check (key + ".validate") true
                                  $"stages 1-17 incl. History.validate: {loaded.Events.Length} events, lifecycle {Enums.lifecycle.ToWire loaded.State.Lifecycle}, {loaded.State.Evidence.Count} evidence, state {loaded.StateSha256}"
                              check (key + ".manifest-consistency") drift.IsEmpty ("catalog entry fields differing from the replayed investigation: " + (if drift.IsEmpty then "none" else String.Join(",", drift)))
                              check (key + ".file-size") ([ "manifest.json"; "events.ndjson"; "snapshot.json" ] |> List.forall (fun f -> FileInfo(Path.Combine(dir, f)).Length <= 4L * 1024L * 1024L))
                                  "every file is at most 4 MiB (Rehearsal 2 reader limit)" ]
                            @ researchChecks)
                let reproduction =
                    match rebuilt with
                    | Error e -> [ check "catalog.deterministic-reconstruction" false ("rebuild failed: " + e) ]
                    | Ok built ->
                        let expected = Catalog.outputs root built
                        let differing =
                            expected |> List.filter (fun (rel, bytes) -> not (File.Exists(Path.Combine(root, rel)) && File.ReadAllBytes(Path.Combine(root, rel)) = bytes)) |> List.map fst
                        let expectedSet = expected |> List.map fst |> Set.ofList
                        let extra =
                            [ Catalog.workspace root; Catalog.catalogDir root ]
                            |> List.filter Directory.Exists
                            |> List.collect (fun d -> Directory.GetFiles(d, "*", SearchOption.AllDirectories) |> List.ofArray)
                            |> List.map (fun p -> Layout.posix (Path.GetRelativePath(root, p)))
                            |> List.filter (fun p -> not (expectedSet.Contains p) && not (p.StartsWith("catalog/historical-v1/verification", StringComparison.Ordinal)))
                        [ check "catalog.deterministic-reconstruction" (differing.IsEmpty && extra.IsEmpty)
                              $"rebuilt {expected.Length} files through Decide.execute; differing: {String.Join(',', differing)}; unexpected: {String.Join(',', extra)}" ]
                [ check "catalog.active-pointer" (activeOk && pointsAtManifest) "catalog/active.json is canonical and names catalog/historical-v1 and workspaces/demo/historical-v1"
                  check "catalog.manifest.canonical" (canonicalFile (Catalog.manifestPath root)) "RFC 8785 + LF"
                  check "catalog.exactly-three-cases" (cases.Length = 3) $"{cases.Length} cases listed"
                  check "catalog.case-identities" (keys = Catalog.caseKeys) ("case keys: " + String.Join(", ", keys))
                  check "catalog.workspace-exact" (dirs = List.sort ids && ids.Length = 3) ("workspace directories: " + String.Join(", ", dirs))
                  check "catalog.no-synthetic-case" (ids |> List.forall (fun i -> not (List.contains i fixtureIds)) && keys |> List.forall (fun k -> not (List.contains k [ "payment-failure"; "intermittent-outage"; "conflicting-metrics"; "api-latency"; "authentication-outage" ])))
                      "no v1.0.0 synthetic investigation id or scenario key in the active catalog"
                  check "catalog.contract-identity" (str "schemaSha256" m = bundleSha && str "formatVersion" m = "1.0.0" && str "semanticVersion" m = "1.0.0") ("schema bundle " + bundleSha) ]
                @ caseChecks
                @ combinedChecks set root cases
                @ reproduction
