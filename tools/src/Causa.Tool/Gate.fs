namespace Causa.Tool

open System
open System.IO
open Causa.Contract

/// The compatibility gate (CONTRACT §13.3, §14). `lock` is written once, before the freeze;
/// `gate` never writes and fails on any difference.
module Gate =
    let lockPath root = Path.Combine(root, "compatibility", "v1.0.0", "lock.json")

    /// Every frozen file: the whole contract/v1.0.0 and fixtures/v1.0.0 trees.
    let frozenFiles (root: string) =
        [ Path.Combine(root, "contract", "v1.0.0"); Path.Combine(root, "fixtures", "v1.0.0") ]
        |> List.collect (fun d -> Directory.GetFiles(d, "*", SearchOption.AllDirectories) |> List.ofArray)
        |> List.map (fun p -> Layout.posix (Path.GetRelativePath(root, p)), Json.sha256Hex (File.ReadAllBytes p))
        |> List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))

    /// Content hash of the engine source actually compiled (CONTRACT §14): every *.fs / *.fsproj under
    /// .engine/src/engine plus the engine's Directory.Build.props, as sorted "path:sha256" lines.
    let engineSourceSha256 (root: string) =
        let engine = Path.Combine(root, ".engine")
        if not (Directory.Exists(Path.Combine(engine, "src", "engine"))) then "missing"
        else
            Directory.GetFiles(Path.Combine(engine, "src", "engine"), "*", SearchOption.AllDirectories)
            |> Array.filter (fun f -> (f.EndsWith ".fs" || f.EndsWith ".fsproj") && not (f.Contains "/bin/" || f.Contains "/obj/"))
            |> Array.append [| Path.Combine(engine, "Directory.Build.props") |]
            |> Array.filter File.Exists
            |> Array.map (fun f -> Layout.posix (Path.GetRelativePath(engine, f)) + ":" + Json.sha256Hex (File.ReadAllBytes f))
            |> Array.sortWith (fun a b -> String.CompareOrdinal(a, b))
            |> String.concat "\n"
            |> Json.strictUtf8.GetBytes
            |> Json.sha256Hex

    let private sdkVersion (root: string) =
        let p = Path.Combine(root, "global.json")
        if not (File.Exists p) then "unpinned"
        else
            match File.ReadAllText p |> Json.parse with
            | Ok v -> (match Negatives.get [ "sdk"; "version" ] v with JStr s -> s | _ -> "unpinned")
            | Error _ -> "unreadable"

    let private engineCommit (root: string) =
        match File.ReadAllText(Path.Combine(root, "engine.lock.json")) |> Json.parse with
        | Ok v -> (match Negatives.get [ "commit" ] v with JStr s -> s | _ -> "")
        | Error _ -> ""

    let private str k v = match Negatives.get [ k ] v with JStr s -> s | _ -> ""

    let compute (root: string) : JValue =
        let rootManifest = File.ReadAllText(Layout.rootManifest root).TrimEnd('\n') |> Json.parse |> Result.defaultWith failwith
        let investigations = match Negatives.get [ "investigations" ] rootManifest with JArr xs -> xs | _ -> []
        let negatives =
            Directory.GetFiles(Layout.negative root, "*.json")
            |> Array.sort
            |> List.ofArray
            |> List.map (fun p ->
                let fx = File.ReadAllText(p).TrimEnd('\n') |> Json.parse |> Result.defaultWith failwith
                JObj [ "name", JStr(str "name" fx); "expectedErrorCode", JStr(str "expectedErrorCode" fx)
                       "expectedDomainCode", (match Negatives.get [ "expectedDomainCode" ] fx with JStr s -> JStr s | _ -> JNull) ])
        JObj
            [ "formatVersion", JStr "1.0.0"
              "contractVersion", JStr "1.0.0"
              "releaseTag", JStr "causa-storage-v1.0.0"
              "engineCommit", JStr(engineCommit root)
              "engineSourceSha256", JStr(engineSourceSha256 root)
              "dotnetSdk", JStr(sdkVersion root)
              "schemaBundleSha256", JStr(Json.sha256Hex (File.ReadAllBytes(Layout.bundle root)))
              "eventTypes", JArr(EventWire.eventTypes |> List.map JStr)
              "expectedStates",
              JArr(investigations |> List.map (fun e -> JObj [ "scenarioKey", JStr(str "scenarioKey" e); "investigationId", JStr(str "investigationId" e); "stateSha256", JStr(str "expectedStateSha256" e) ]))
              "negatives", JArr negatives
              "frozenFiles", JArr(frozenFiles root |> List.map (fun (p, h) -> JObj [ "path", JStr p; "sha256", JStr h ])) ]

    let writeLock (root: string) =
        let path = lockPath root
        Directory.CreateDirectory(Path.GetDirectoryName path |> nonNull) |> ignore
        File.WriteAllBytes(path, Json.fileBytes (compute root) |> Result.defaultWith failwith)

    type Failure = { Rule: string; Detail: string }

    let frozenRoots = [ "contract/v1.0.0"; "fixtures/v1.0.0"; "compatibility/v1.0.0"; "engine.lock.json"; "global.json" ]

    let private git (root: string) (args: string list) =
        let psi = Diagnostics.ProcessStartInfo("git")
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        ("-C" :: root :: args) |> List.iter psi.ArgumentList.Add
        use p = Diagnostics.Process.Start psi |> nonNull
        let out = p.StandardOutput.ReadToEnd()
        p.WaitForExit()
        p.ExitCode, out

    /// After the freeze the lock is anchored to the release tag (review finding M1): the working-tree lock
    /// must be byte-identical to the tagged one, and no frozen path may differ from the tag.
    let anchorFailures (root: string) (anchor: string) : Failure list =
        match git root [ "show"; anchor + ":compatibility/v1.0.0/lock.json" ] with
        | code, _ when code <> 0 -> [ { Rule = "anchor.present"; Detail = "cannot read compatibility/v1.0.0/lock.json at " + anchor } ]
        | _, tagged ->
            let lockDiffers =
                if File.Exists(lockPath root) && File.ReadAllText(lockPath root) = tagged then []
                else [ { Rule = "anchor.lock"; Detail = "compatibility/v1.0.0/lock.json differs from " + anchor } ]
            let changed =
                match git root ([ "diff"; "--name-only"; anchor; "--" ] @ frozenRoots) with
                | 0, out -> out.Split('\n', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray
                | _ -> [ "<git diff failed>" ]
            let untracked =
                match git root ([ "ls-files"; "--others"; "--exclude-standard"; "--" ] @ frozenRoots) with
                | 0, out -> out.Split('\n', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray
                | _ -> []
            lockDiffers @ ((changed @ untracked) |> List.map (fun f -> { Rule = "anchor.frozen-tree"; Detail = f + " differs from " + anchor }))

    /// Compare the tree against the lock, then run full dataset verification.
    let run (root: string) : Failure list =
        if not (File.Exists(lockPath root)) then [ { Rule = "lock.present"; Detail = "compatibility/v1.0.0/lock.json is missing" } ]
        else
            let locked = File.ReadAllText(lockPath root).TrimEnd('\n') |> Json.parse |> Result.defaultWith failwith
            let current =
                try Ok(compute root) with ex -> Error("cannot compute tree digests: " + ex.Message)
            match current with
            | Error e -> [ { Rule = "tree.readable"; Detail = e } ]
            | Ok now ->
                let scalar rule key =
                    if str key locked = str key now then [] else [ { Rule = rule; Detail = key + ": locked " + str key locked + ", found " + str key now } ]
                let listOf key v = match Negatives.get [ key ] v with JArr xs -> xs |> List.map (Json.canonical >> Result.defaultValue "") | _ -> []
                let listRule rule key =
                    let a, b = Set.ofList (listOf key locked), Set.ofList (listOf key now)
                    let missing = Set.difference a b |> Set.toList
                    let extra = Set.difference b a |> Set.toList
                    (missing |> List.map (fun m -> { Rule = rule; Detail = "missing or changed: " + m }))
                    @ (extra |> List.map (fun x -> { Rule = rule; Detail = "unexpected or changed: " + x }))
                let orderRule =
                    if listOf "eventTypes" locked = listOf "eventTypes" now then [] else [ { Rule = "event-registry.order"; Detail = "registered event types changed" } ]
                let verification =
                    try Dataset.verify root |> List.filter (fun c -> not c.Passed) |> List.map (fun c -> { Rule = "verify." + c.Name; Detail = c.Detail })
                    with ex -> [ { Rule = "verify.exception"; Detail = ex.Message } ]
                scalar "engine.pinned" "engineCommit"
                @ scalar "engine.source" "engineSourceSha256"
                @ scalar "toolchain.sdk" "dotnetSdk"
                @ scalar "schema.identity" "schemaBundleSha256"
                @ listRule "event-registry" "eventTypes" @ orderRule
                @ listRule "replay.semantics" "expectedStates"
                @ listRule "negative.expectations" "negatives"
                @ listRule "frozen.bytes" "frozenFiles"
                @ verification

    let report (failures: Failure list) : JValue =
        JObj
            [ "gate", JStr "causa-storage-v1.0.0-compatibility"
              "result", JStr(if failures.IsEmpty then "PASS" else "FAIL")
              "failures", JArr(failures |> List.map (fun f -> JObj [ "rule", JStr f.Rule; "detail", JStr f.Detail ])) ]

    let private copyTree (src: string) (dst: string) =
        [ "contract"; "fixtures"; "compatibility" ]
        |> List.iter (fun d ->
            Directory.GetFiles(Path.Combine(src, d), "*", SearchOption.AllDirectories)
            |> Array.iter (fun f ->
                let target = Path.Combine(dst, Path.GetRelativePath(src, f))
                Directory.CreateDirectory(Path.GetDirectoryName target |> nonNull) |> ignore
                File.Copy(f, target, true)))
        File.Copy(Path.Combine(src, "engine.lock.json"), Path.Combine(dst, "engine.lock.json"), true)
        if File.Exists(Path.Combine(src, "global.json")) then File.Copy(Path.Combine(src, "global.json"), Path.Combine(dst, "global.json"), true)
        Directory.GetFiles(Path.Combine(src, ".engine", "src", "engine"), "*", SearchOption.AllDirectories)
        |> Array.filter (fun f -> not (f.Contains "/bin/" || f.Contains "/obj/"))
        |> Array.append [| Path.Combine(src, ".engine", "Directory.Build.props") |]
        |> Array.iter (fun f ->
            let target = Path.Combine(dst, Path.GetRelativePath(src, f))
            Directory.CreateDirectory(Path.GetDirectoryName target |> nonNull) |> ignore
            File.Copy(f, target, true))

    /// Gate 7: deliberate mutations, each of which MUST make the gate fail; the unmutated copy MUST pass.
    let selfTest (root: string) : (string * bool * string) list =
        let mutations : (string * (string -> unit)) list =
            [ "unmutated copy (must PASS)", ignore
              "schema-breaking: drop 'rationale' from EvidenceRelated payload",
              (fun r ->
                  let p = Path.Combine(r, "contract", "v1.0.0", "schemas", "payloads", "EvidenceRelated.schema.json")
                  File.WriteAllText(p, File.ReadAllText(p).Replace("\"rationale\",", "").Replace(",\n    \"rationale\"", "")))
              "schema-additive: new optional field 'confidence' on HypothesisRecorded",
              (fun r ->
                  let p = Path.Combine(r, "contract", "v1.0.0", "schemas", "payloads", "HypothesisRecorded.schema.json")
                  File.WriteAllText(p, File.ReadAllText(p).Replace("\"properties\": {", "\"properties\": {\n    \"confidence\": { \"type\": \"string\" },")))
              "schema-bundle regenerated after a schema edit (identity changes)",
              (fun r ->
                  let p = Path.Combine(r, "contract", "v1.0.0", "schemas", "common.schema.json")
                  File.WriteAllText(p, File.ReadAllText(p).Replace("\"informational\"", "\"informational\", \"critical\""))
                  File.WriteAllBytes(Layout.bundle r, Bundle.bytes (Layout.schemas r) |> Result.defaultWith (fun e -> failwith e.Detail)))
              "historical mutation: edit one byte of a frozen events.ndjson",
              (fun r ->
                  let p = Directory.GetFiles(Layout.investigations r, "events.ndjson", SearchOption.AllDirectories) |> Array.sort |> Array.head
                  File.WriteAllText(p, File.ReadAllText(p).Replace("synthetic", "Synthetic")))
              "golden drift: change a golden projection",
              (fun r ->
                  let p = Path.Combine(Layout.golden r, "api-latency.state.json")
                  File.WriteAllText(p, File.ReadAllText(p).Replace("\"awaitingVerification\"", "\"resolved\"")))
              "negative weakened: change a negative fixture's expected code",
              (fun r ->
                  let p = Path.Combine(Layout.negative r, "forged-resolution.json")
                  File.WriteAllText(p, File.ReadAllText(p).Replace("CAUSA.HISTORY_INADMISSIBLE", "CAUSA.SCHEMA_VIOLATION")))
              "negative removed: delete a negative fixture",
              (fun r -> File.Delete(Path.Combine(Layout.negative r, "unknown-event-type.json")))
              "engine drift: pin a different engine commit",
              (fun r ->
                  let p = Path.Combine(r, "engine.lock.json")
                  File.WriteAllText(p, File.ReadAllText(p).Replace("55f49f61c939a3a780cee592f36f732a1328f981", "0000000000000000000000000000000000000000")))
              "engine semantics relaxed: delete the CAUSE.NO_MECHANISM rule from the engine source",
              (fun r ->
                  let p = Path.Combine(r, ".engine", "src", "engine", "Decide.fs")
                  File.WriteAllText(p, File.ReadAllText(p).Replace("| None -> [ err \"CAUSE.NO_MECHANISM\" \"§15.8\" \"The causal mechanism is not stated.\" ]", "| None -> []")))
              "anchored relock: weaken CONTRACT.md then regenerate the lock (must fail against the tag)",
              (fun r ->
                  let p = Path.Combine(r, "contract", "v1.0.0", "CONTRACT.md")
                  File.WriteAllText(p, File.ReadAllText(p).Replace("Unknown fields are\n   rejected, never ignored.", "Unknown fields are\n   ignored."))
                  writeLock r)
              "unapproved fixture added: extra investigation directory",
              (fun r ->
                  let d = Path.Combine(Layout.investigations r, "00000000-0000-4000-8000-000000000001")
                  Directory.CreateDirectory d |> ignore
                  File.WriteAllText(Path.Combine(d, "manifest.json"), "{}\n")) ]
        mutations
        |> List.map (fun (name, mutate) ->
            let scratch = Path.Combine(Path.GetTempPath(), "causa-gate-selftest-" + Guid.NewGuid().ToString("N"))
            copyTree root scratch
            let outcome =
                try
                    // Each scratch copy is a git repository tagged at its unmutated state, so the anchor applies.
                    let g args = git scratch args |> ignore
                    g [ "init"; "-q" ]
                    g [ "-c"; "user.name=selftest"; "-c"; "user.email=selftest@invalid"; "add"; "-A" ]
                    g [ "-c"; "user.name=selftest"; "-c"; "user.email=selftest@invalid"; "commit"; "-q"; "-m"; "frozen" ]
                    g [ "tag"; "causa-storage-v1.0.0" ]
                    mutate scratch
                    run scratch @ anchorFailures scratch "causa-storage-v1.0.0"
                with ex -> [ { Rule = "exception"; Detail = ex.Message } ]
            Directory.Delete(scratch, true)
            let shouldPass = name.StartsWith "unmutated"
            let ok = if shouldPass then outcome.IsEmpty else not outcome.IsEmpty
            name, ok, (if outcome.IsEmpty then "gate PASS" else "gate FAIL: " + String.Join(" | ", outcome |> List.truncate 3 |> List.map (fun f -> f.Rule + " " + f.Detail))))
