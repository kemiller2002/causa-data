/// `append-demo`: copy one frozen investigation into an isolated workspace (§3.4, §9), append a new
/// event decided by the engine (§9.2), write events/manifest/snapshot, and re-verify as a reader.
module CleanConsumer.AppendDemo

open System
open System.IO
open IndyInitRootCauseInvestigator.Engine
open CleanConsumer.Json
open CleanConsumer.Domain
open CleanConsumer.Dataset

let runId = "clean-consumer-run"

/// Namespace for this consumer's own deterministic ids (UUIDv5 under NAMESPACE_URL).
let private demoNamespace = Wire.uuidV5 Wire.namespaceUrl "https://github.com/kemiller2002/causa-data/tools/consumers/clean-fsharp"

let private writeAll (dir: string) (files: Verify.FileSet) : Result<unit, string> =
    try
        Directory.CreateDirectory dir |> ignore
        File.WriteAllBytes(Path.Combine(dir, "events.ndjson"), files.Events)
        File.WriteAllBytes(Path.Combine(dir, "snapshot.json"), files.Snapshot)
        File.WriteAllBytes(Path.Combine(dir, "manifest.json"), files.Manifest)
        Ok()
    with ex -> Error $"write failed: {ex.Message}"

let private resetDir (dir: string) : Result<unit, string> =
    try
        if Directory.Exists dir then Directory.Delete(dir, true)
        Ok()
    with ex -> Error ex.Message

let run (repoRoot: string) (outDir: string) : Check list =
    let fixturesDir = Path.Combine(repoRoot, "fixtures", "v1.0.0")
    let contractDir = Path.Combine(repoRoot, "contract", "v1.0.0")
    let before = hashTree fixturesDir @ hashTree contractDir
    let checks =
        result {
            do! if Wire.slugPattern.IsMatch runId then Ok() else Error "runId does not match ^[a-z0-9][a-z0-9-]{0,63}$"
            let! root = readJsonDoc (Path.Combine(fixturesDir, "manifest.json")) (Schema.validate "root" Schema.rootManifest)
            let! entry =
                arr "investigations" root |> Option.defaultValue []
                |> List.tryFind (fun e -> str "expectedLifecycle" e = Some "awaitingVerification")
                |> Option.map Ok |> Option.defaultValue (Error "no awaitingVerification investigation in the root manifest")
            let id = str "investigationId" entry |> Option.defaultValue ""
            let key = str "scenarioKey" entry |> Option.defaultValue ""
            let! frozen = loadFileSet (Path.Combine(fixturesDir, "investigations", id))
            let! baseValid = Verify.validate frozen |> Result.mapError (fun f -> "frozen base invalid: " + Verify.describe (Error f))
            let target = Path.GetFullPath(Path.Combine(outDir, "workspaces", "demo", runId, "investigations", id))
            let inside (d: string) = target.StartsWith(Path.GetFullPath d + string Path.DirectorySeparatorChar, StringComparison.Ordinal)
            do! if inside fixturesDir || inside contractDir then Error "refusing to write under fixtures/v1.0.0 or contract/v1.0.0 (§3.4)" else Ok()

            // 1. copy the frozen file set byte-for-byte and verify the copy
            do! resetDir target
            do! writeAll target frozen
            let! copied = loadFileSet target
            let copyCheck =
                match Verify.validate copied with
                | Ok v -> pass "append-demo copy" $"{key} copied to {target}; copy verifies ({v.WireEvents.Length} events)"
                | Error f -> failc "append-demo copy" (Verify.describe (Error f))

            // 2. current engine state by replay
            let! inv = Evolve.replay baseValid.EngineEvents |> Option.map Ok |> Option.defaultValue (Error "replay produced no state")
            let current = inv.Version

            // 3. stale append must be refused before anything is written (§9.2)
            let staleCheck =
                match appendCandidate copied current (current - 1L) [||] with
                | Error o when o.Primary = "CAUSA.VERSION_STALE" ->
                    let unchanged = loadFileSet target |> Result.map (fun f -> f.Events = frozen.Events) |> Result.defaultValue false
                    if unchanged then pass "append-demo stale-refused" $"expected {current - 1L} vs current {current}: CAUSA.VERSION_STALE, nothing written"
                    else failc "append-demo stale-refused" "files changed after a stale refusal"
                | _ -> failc "append-demo stale-refused" "stale expected version was not refused"

            // 4. decide a new command with the engine
            let lastAt = (List.last baseValid.EngineEvents).At
            let requestedAt = lastAt.AddMinutes(4.0).AddMilliseconds(123.0)
            let commandId = Wire.uuidV5 demoNamespace $"{key}/command/append-demo-observation"
            let observationId: ObservationId = Id(Wire.uuidV5 demoNamespace $"{key}/observation/append-demo")
            let envelope =
                { CommandId = commandId
                  Investigation = inv.Id
                  ExpectedVersion = Some current
                  Actor = { Id = "clean-consumer-demo"; Kind = ActorKind.Human }
                  RequestedAt = requestedAt
                  Command = RecordObservation(observationId, "p95 latency sampled at 212 ms by the clean consumer append demo.", Some(lastAt.AddMinutes 2.0)) }
            let! decided =
                Decide.execute (Some inv) envelope
                |> Result.mapError (fun errs -> "engine refused: " + String.Join("; ", errs |> List.map (fun e -> e.Code + " " + e.Message)))
            let! wire = decided |> traverse encodeEvent |> Result.mapError (fun e -> $"CAUSA.UNREPRESENTABLE: {e}")
            let appended = wire |> List.map (fun e -> canonical e + "\n") |> String.concat "" |> Wire.utf8

            // 5. writer: append, recompute snapshot and manifest, write the three files together
            let! candidate = appendCandidate copied current current appended |> Result.mapError (fun o -> o.Primary + ": " + o.Detail)
            do! writeAll target candidate

            // 6. re-verify exactly as a reader would, from disk
            let! written = loadFileSet target
            let verifyCheck =
                match Verify.validate written with
                | Ok v ->
                    let types = String.Join(",", wire |> List.choose (str "eventType"))
                    let at = Wire.formatTimestamp requestedAt |> Result.defaultValue "?"
                    pass "append-demo verify" $"re-read from disk: all 17 stages pass; version {current} -> {v.WireEvents.Length}; appended [{types}] correlationId {Wire.uuidText commandId} at {at}"
                | Error f -> failc "append-demo verify" (Verify.describe (Error f))
            let successorCheck =
                if startsWith frozen.Events written.Events && written.Events.Length > frozen.Events.Length then
                    pass "append-demo successor" "new events.ndjson starts with the frozen events.ndjson bytes (§9.3)"
                else failc "append-demo successor" "CAUSA.HISTORY_REWRITTEN"
            let digests =
                pass "append-demo digests"
                    $"eventsSha256={Wire.sha256Hex written.Events} snapshotSha256={Wire.sha256Hex written.Snapshot[.. written.Snapshot.Length - 2]}"
            return [ copyCheck; staleCheck; verifyCheck; successorCheck; digests ]
        }
        |> function
            | Ok cs -> cs
            | Error e -> [ failc "append-demo" e ]
    let after = hashTree fixturesDir @ hashTree contractDir
    let immutability =
        if before = after then pass "append-demo frozen-unchanged" $"{before.Length} files under fixtures/v1.0.0 and contract/v1.0.0 hash-identical before and after"
        else failc "append-demo frozen-unchanged" "frozen files changed!"
    checks @ [ immutability ]
