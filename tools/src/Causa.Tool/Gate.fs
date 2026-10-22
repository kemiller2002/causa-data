namespace Causa.Tool

open System
open System.IO
open Causa.Contract

/// The compatibility gate (CONTRACT §13.3, §14). `lock` is written once, before the freeze;
/// `gate` never writes and fails on any difference.
module Gate =
    let lockPath root = Path.Combine(root, "compatibility", "v1.0.0", "lock.json")

    /// compatibility/v1.0.0/RELEASE exists from the freeze commit onwards; when present, the anchored
    /// check is mandatory and a missing tag is a failure, never a silent fallback (review residual on M1).
    let releaseMarker root = Path.Combine(root, "compatibility", "v1.0.0", "RELEASE")

    /// Every frozen file: the whole contract/v1.0.0 and fixtures/v1.0.0 trees, plus the release marker.
    let frozenFiles (root: string) =
        [ Path.Combine(root, "contract", "v1.0.0"); Path.Combine(root, "fixtures", "v1.0.0") ]
        |> List.collect (fun d -> Directory.GetFiles(d, "*", SearchOption.AllDirectories) |> List.ofArray)
        |> List.append (if File.Exists(releaseMarker root) then [ releaseMarker root ] else [])
        |> List.map (fun p -> Layout.posix (Path.GetRelativePath(root, p)), Json.sha256Hex (File.ReadAllBytes p))
        |> List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))

    /// Run a process; `env = Some vars` replaces the whole environment (sterile engine build).
    let private execWith (cwd: string option) (env: (string * string) list option) (exe: string) (args: string list) =
        let psi = Diagnostics.ProcessStartInfo(exe)
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        cwd |> Option.iter (fun d -> psi.WorkingDirectory <- d)
        env |> Option.iter (fun vars ->
            psi.Environment.Clear()
            vars |> List.iter (fun (k, v) -> psi.Environment[k] <- v))
        args |> List.iter psi.ArgumentList.Add
        use p = Diagnostics.Process.Start psi |> nonNull
        let out = p.StandardOutput.ReadToEndAsync()
        let err = p.StandardError.ReadToEnd()
        p.WaitForExit()
        p.ExitCode, out.Result + err

    let private exec (exe: string) (args: string list) =
        let code, out = execWith None None exe args
        code, out

    /// The assemblies whose bytes define engine behaviour at run time.
    let engineAssemblies = [ "Aegis.Core.dll"; "FSharp.Core.dll"; "IndyInitRootCauseInvestigator.Engine.dll" ]

    /// The one way every engine build is configured (Directory.Build.props, `CausaEngineBuildProperties`):
    /// Release, no PDB, source paths mapped, so the output bytes do not depend on where the tree lives.
    let engineBuildProperties (engineRoot: string) =
        [ "Configuration", "Release"; "DebugType", "none"; "DebugSymbols", "false"
          "CopyLocalLockFileAssemblies", "true"; "EnableSourceControlManagerQueries", "false"; "EnableSourceLink", "false"
          "IncludeSourceRevisionInInformationalVersion", "false"; "PathMap", engineRoot + "=/_/engine/" ]

    let private hashAssemblies (dir: string) : (string * string) list =
        engineAssemblies
        |> List.map (fun name ->
            let f = Path.Combine(dir, name)
            name, (if File.Exists f then Json.sha256Hex (File.ReadAllBytes f) else "missing"))

    let private outputDir (engineRoot: string) = Path.Combine(engineRoot, "src", "engine", "bin", "Release", "net10.0")

    /// Variables a sterile build keeps from the caller: tool location and network egress only.
    let private passThrough =
        [ "PATH"; "DOTNET_ROOT"; "HTTPS_PROXY"; "https_proxy"; "HTTP_PROXY"; "http_proxy"; "NO_PROXY"; "no_proxy"; "SSL_CERT_FILE"; "SSL_CERT_DIR" ]

    let private buildSteeringFiles =
        [ "Directory.Build.props"; "Directory.Build.targets"; "Directory.Build.rsp"; "Directory.Packages.props"; "NuGet.Config"; "nuget.config"; "global.json" ]

    /// Build the pinned engine commit in isolation (review round 3): sources come from the git object
    /// store, so nothing untracked, ignored or generated can enter; the directory has no build-steering
    /// file above it; the environment is replaced; response files are off; HOME, the NuGet package folder
    /// and the NuGet configuration are fresh, with nuget.org as the only source.
    let private sterileBuild (root: string) (commit: string) : Result<(string * string) list, string> =
        let dir = Path.Combine(Path.GetTempPath(), "causa-engine-sterile-" + Guid.NewGuid().ToString("N"))
        let ancestors =
            Seq.unfold (fun (d: DirectoryInfo | null) -> match d with null -> None | d -> Some(d, d.Parent)) (DirectoryInfo(dir).Parent)
            |> Seq.collect (fun d -> buildSteeringFiles |> Seq.map (fun f -> Path.Combine(d.FullName, f)))
            |> Seq.filter File.Exists
            |> List.ofSeq
        let step (code, out: string) what = if code = 0 then Ok() else Error(what + " failed: " + out.Trim())
        let result =
            if not ancestors.IsEmpty then Error("build-steering files above the sterile directory: " + String.Join(", ", ancestors))
            else
                Directory.CreateDirectory(Path.Combine(dir, "home")) |> ignore
                let tar = Path.Combine(dir, "engine.tar")
                let engineRoot = Path.Combine(dir, "engine") + "/"
                Directory.CreateDirectory engineRoot |> ignore
                step (exec "git" [ "-C"; Path.Combine(root, ".engine"); "archive"; "--format=tar"; "-o"; tar; commit; "Directory.Build.props"; "src/engine" ]) "git archive of the pinned engine commit"
                |> Result.bind (fun () -> step (exec "tar" [ "-xf"; tar; "-C"; engineRoot ]) "tar")
                |> Result.bind (fun () ->
                    File.Copy(Path.Combine(root, "global.json"), Path.Combine(engineRoot, "global.json"))
                    File.WriteAllText(
                        Path.Combine(engineRoot, "NuGet.Config"),
                        "<configuration><packageSources><clear /><add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" /></packageSources></configuration>\n")
                    let home = Path.Combine(dir, "home")
                    let env =
                        (passThrough |> List.choose (fun k -> Environment.GetEnvironmentVariable k |> Option.ofObj |> Option.map (fun v -> k, v)))
                        @ [ "HOME", home; "DOTNET_CLI_HOME", home; "NUGET_PACKAGES", Path.Combine(dir, "packages")
                            "DOTNET_CLI_TELEMETRY_OPTOUT", "1"; "DOTNET_NOLOGO", "1"; "DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1" ]
                    let props = engineBuildProperties engineRoot |> List.map (fun (k, v) -> "-p:" + k + "=" + v)
                    step (execWith (Some engineRoot) (Some env) "dotnet" ([ "build"; "src/engine/App.Engine.fsproj"; "-noAutoResponse"; "-nologo"; "-v"; "q" ] @ props)) "sterile engine build")
                |> Result.map (fun () -> hashAssemblies (outputDir engineRoot))
        try Directory.Delete(dir, true) with _ -> ()
        result

    let private sterileCache = Collections.Concurrent.ConcurrentDictionary<string, Result<(string * string) list, string>>()

    /// Sterile build result per (engine repository, commit); git content is immutable per commit.
    let pinnedEngine (root: string) (commit: string) =
        sterileCache.GetOrAdd(Path.GetFullPath(Path.Combine(root, ".engine")) + "@" + commit, fun _ -> sterileBuild root commit)

    /// Which engine the gate checks against the pin: the one this process actually runs, or (self-test)
    /// the one an ordinary build of a scratch tree produces, build-steering files and all.
    type EngineUnderTest =
        | LoadedByThisProcess
        | BuiltIn of root: string

    let actualEngine (under: EngineUnderTest) : Result<(string * string) list, string> =
        match under with
        | LoadedByThisProcess ->
            typeof<IndyInitRootCauseInvestigator.Engine.EventBody>.Assembly.Location
            |> Path.GetDirectoryName |> nonNull |> hashAssemblies |> Ok
        | BuiltIn root ->
            let engineRoot = Path.Combine(root, ".engine") + "/"
            let props = engineBuildProperties engineRoot |> List.map (fun (k, v) -> "-p:" + k + "=" + v)
            match execWith (Some root) None "dotnet" ([ "build"; Path.Combine(engineRoot, "src", "engine", "App.Engine.fsproj"); "-nologo"; "-v"; "q" ] @ props) with
            | 0, _ -> Ok(hashAssemblies (outputDir engineRoot))
            | _, out -> Error("engine build failed: " + (out.Trim() |> fun s -> if s.Length > 300 then s.Substring(s.Length - 300) else s))

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

    let private assemblyJson (r: Result<(string * string) list, string>) =
        match r with
        | Ok xs -> JArr(xs |> List.map (fun (n, h) -> JObj [ "name", JStr n; "sha256", JStr h ]))
        | Error e -> JStr("unavailable: " + e)

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
              "engineAssemblies", assemblyJson (pinnedEngine root (engineCommit root))
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

    let private git (root: string) (args: string list) = exec "git" ("-C" :: root :: args)

    /// After the freeze the lock is anchored to the release tag (review finding M1): the working-tree lock
    /// must be byte-identical to the tagged one, and no frozen path may differ from the tag.
    let anchorFailures (root: string) (anchor: string) : Failure list =
        let releaseTag = "causa-storage-v1.0.0"
        // Only the release tag itself anchors (review round 3: `--anchor HEAD` passed a relocked tree).
        if anchor <> releaseTag then [ { Rule = "anchor.tag"; Detail = "the anchor must be the release tag " + releaseTag + ", not " + anchor } ]
        elif fst (git root [ "rev-parse"; "-q"; "--verify"; "refs/tags/" + releaseTag ]) <> 0 then
            [ { Rule = "anchor.present"; Detail = "tag " + releaseTag + " does not exist" } ]
        else
        match git root [ "show"; "refs/tags/" + anchor + ":compatibility/v1.0.0/lock.json" ] with
        | code, _ when code <> 0 -> [ { Rule = "anchor.present"; Detail = "cannot read compatibility/v1.0.0/lock.json at " + anchor } ]
        | _, tagged ->
            let lockDiffers =
                if File.Exists(lockPath root) && File.ReadAllText(lockPath root) = tagged then []
                else [ { Rule = "anchor.lock"; Detail = "compatibility/v1.0.0/lock.json differs from " + anchor } ]
            let changed =
                match git root ([ "diff"; "--name-only"; "refs/tags/" + anchor; "--" ] @ frozenRoots) with
                | 0, out -> out.Split('\n', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray
                | _ -> [ "<git diff failed>" ]
            let untracked =
                match git root ([ "ls-files"; "--others"; "--exclude-standard"; "--" ] @ frozenRoots) with
                | 0, out -> out.Split('\n', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray
                | _ -> []
            lockDiffers @ ((changed @ untracked) |> List.map (fun f -> { Rule = "anchor.frozen-tree"; Detail = f + " differs from " + anchor }))

    /// Compare the tree against the lock, then run full dataset verification.
    let run (root: string) (under: EngineUnderTest) : Failure list =
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
                let engineLoaded =
                    let pinned = Json.canonical (Negatives.get [ "engineAssemblies" ] now) |> Result.defaultValue ""
                    match actualEngine under with
                    | Error e -> [ { Rule = "engine.build"; Detail = e } ]
                    | Ok xs ->
                        let actual = Json.canonical (assemblyJson (Ok xs)) |> Result.defaultValue ""
                        if pinned = actual then []
                        else [ { Rule = "engine.loaded"; Detail = "engine assemblies found " + actual + " differ from a sterile build of the pinned commit " + pinned } ]
                scalar "engine.pinned" "engineCommit"
                @ listRule "engine.binary" "engineAssemblies"
                @ engineLoaded
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
        // The whole engine checkout, git metadata included (the sterile build reads the pinned commit from it);
        // directories first, since git needs its empty ones (refs/heads, ...).
        let kept (f: string) = not (f.Contains "/bin/" || f.Contains "/obj/" || f.Contains "/node_modules/" || f.EndsWith "/bin" || f.EndsWith "/obj")
        Directory.GetDirectories(Path.Combine(src, ".engine"), "*", SearchOption.AllDirectories)
        |> Array.filter kept
        |> Array.iter (fun d -> Directory.CreateDirectory(Path.Combine(dst, Path.GetRelativePath(src, d))) |> ignore)
        Directory.GetFiles(Path.Combine(src, ".engine"), "*", SearchOption.AllDirectories)
        |> Array.filter kept
        |> Array.iter (fun f ->
            let target = Path.Combine(dst, Path.GetRelativePath(src, f))
            Directory.CreateDirectory(Path.GetDirectoryName target |> nonNull) |> ignore
            File.Copy(f, target, true))

    /// A build hook that swaps the engine's Decide.fs for a copy without the CAUSE.NO_MECHANISM rule,
    /// keeping the compile order: the review-round-3 attack, planted through different MSBuild entry points.
    let private plantSwap (r: string) =
        let original = File.ReadAllText(Path.Combine(r, ".engine", "src", "engine", "Decide.fs"))
        let relaxed = original.Replace("| None -> [ err \"CAUSE.NO_MECHANISM\" \"§15.8\" \"The causal mechanism is not stated.\" ]", "| None -> []")
        if relaxed = original then failwith "self-test: CAUSE.NO_MECHANISM rule text not found in Decide.fs"
        Directory.CreateDirectory(Path.Combine(r, "tools", "evil")) |> ignore
        File.WriteAllText(Path.Combine(r, "tools", "evil", "Decide.fs"), relaxed)
        "<Project><Target Name=\"CausaSelfTestSwap\" BeforeTargets=\"CoreCompile\"><ItemGroup>"
        + "<_CausaSwapped Include=\"@(Compile->'%(FullPath)'->Replace('.engine/src/engine/Decide.fs', 'tools/evil/Decide.fs'))\" />"
        + "<Compile Remove=\"@(Compile)\" /><Compile Include=\"@(_CausaSwapped)\" /></ItemGroup></Target></Project>\n"

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
              "engine build steered: repo-root Directory.Build.targets swapping Decide.fs",
              (fun r -> File.WriteAllText(Path.Combine(r, "Directory.Build.targets"), plantSwap r))
              "engine build steered: repo-root Directory.Build.rsp importing a .xml targets file",
              (fun r ->
                  let hook = plantSwap r
                  File.WriteAllText(Path.Combine(r, "tools", "evil", "build.xml"), hook)
                  File.WriteAllText(Path.Combine(r, "Directory.Build.rsp"), "-p:CustomAfterMicrosoftCommonTargets=" + Path.Combine(r, "tools", "evil", "build.xml") + "\n"))
              "engine build steered: App.Engine.fsproj.user",
              (fun r -> File.WriteAllText(Path.Combine(r, ".engine", "src", "engine", "App.Engine.fsproj.user"), plantSwap r))
              "engine build steered: obj/App.Engine.fsproj.*.targets",
              (fun r ->
                  let obj = Path.Combine(r, ".engine", "src", "engine", "obj")
                  Directory.CreateDirectory obj |> ignore
                  File.WriteAllText(Path.Combine(obj, "App.Engine.fsproj.causa.targets"), plantSwap r))
              "release marker deleted (or, before the freeze, added)",
              (fun r -> if File.Exists(releaseMarker r) then File.Delete(releaseMarker r) else File.WriteAllText(releaseMarker r, "causa-storage-v1.0.0\n"))
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
        // The rule that must catch each mutation: failing for some other reason does not count (review m7).
        let expectedRule =
            [ "schema-breaking", "frozen.bytes"; "schema-additive", "frozen.bytes"; "schema-bundle", "schema.identity"
              "historical", "frozen.bytes"; "golden", "frozen.bytes"; "negative weakened", "negative.expectations"
              "negative removed", "negative.expectations"; "engine drift", "engine.pinned"; "engine semantics", "engine.loaded"
              "engine build steered", "engine.loaded"; "release marker", "frozen.bytes"; "anchored relock", "anchor.lock"
              "unapproved fixture", "frozen.bytes" ]
        let expectedFor (name: string) =
            expectedRule |> List.tryFind (fun (prefix, _) -> name.StartsWith prefix) |> Option.map snd
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
                    run scratch (BuiltIn scratch) @ anchorFailures scratch "causa-storage-v1.0.0"
                with ex -> [ { Rule = "exception"; Detail = ex.Message } ]
            Directory.Delete(scratch, true)
            let ok =
                match expectedFor name with
                | None -> name.StartsWith "unmutated" && outcome.IsEmpty
                | Some rule -> outcome |> List.exists (fun f -> f.Rule = rule)
            let rules = outcome |> List.map (fun f -> f.Rule) |> List.distinct
            let shown = outcome |> List.truncate 2 |> List.map (fun f -> f.Rule + " " + (if f.Detail.Length > 160 then f.Detail.Substring(0, 160) + "…" else f.Detail))
            let expectation = match expectedFor name with Some r -> "expects " + r + "; " | None -> ""
            name, ok, expectation + (if outcome.IsEmpty then "gate PASS" else "gate FAIL [" + String.Join(", ", rules) + "]: " + String.Join(" | ", shown)))
