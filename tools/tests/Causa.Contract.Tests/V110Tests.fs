/// DRAFT causa-storage 1.1.0 compatibility tests (contract/v1.1.0/CONTRACT.md section 7).
module V110Tests

open System
open System.IO
open Xunit
open Causa.Contract
open Causa.Tool
open IndyInitRootCauseInvestigator.Engine

let private repoRoot =
    let rec up (d: DirectoryInfo) = if File.Exists(Path.Combine(d.FullName, "engine.lock.json")) then d.FullName else up (d.Parent |> nonNull)
    up (DirectoryInfo(AppContext.BaseDirectory))

let private load (dir: string) = Store.loadSchemas (Path.Combine(repoRoot, dir)) |> Result.defaultWith (fun e -> failwith e.Detail)
let private v100 = lazy (load "contract/v1.0.0/schemas")
let private v110 = lazy (load "contract/v1.1.0/schemas")
let private read files = V110.validate v100.Value v110.Value files

let private validLines = lazy (V110Fixtures.valid () |> Result.defaultWith failwith)
let private validFiles = lazy (V110.filesOf validLines.Value |> Result.defaultWith (fun e -> failwith e.Detail))

let private investigationDirs (relative: string) =
    let d = Path.Combine(repoRoot, relative)
    if Directory.Exists d then Directory.GetDirectories d |> Array.sort |> List.ofArray else []

/// Every 1.0.0 file set the repository holds: the frozen fixtures and every catalog and template workspace.
let private v100Sets () =
    [ yield! investigationDirs "fixtures/v1.0.0/investigations"
      for w in [ "historical-v1"; "everyday-v1"; "demonstration-v1" ] do
          yield! investigationDirs ("workspaces/demo/" + w + "/investigations")
      for t in [ "historical-v1"; "everyday-v1"; "demonstration-v1" ] do
          yield! investigationDirs ("templates/" + t + "/investigations") ]

[<Fact>]
let ``ST-01 every 1.0.0 history reads unchanged under the 1.1.0 reader`` () =
    let sets = v100Sets ()
    Assert.True(sets.Length >= 5 + 3 + 1 + 4, $"expected the frozen fixtures and catalogs, found {sets.Length}")
    for dir in sets do
        let files = Store.readDirectory dir |> Result.defaultWith (fun e -> failwith (dir + ": " + e.Detail))
        let frozen = Store.validate v100.Value files |> Result.defaultWith (fun e -> failwith (dir + ": " + e.Code))
        match read files with
        | Ok v ->
            Assert.Equal(V110.V100, v.Format)
            Assert.Equal(frozen.StateSha256, v.StateSha256)
            Assert.Equal(frozen.EventsSha256, v.EventsSha256)
            Assert.Equal(frozen.SnapshotSha256, v.SnapshotSha256)
            Assert.Equal(frozen.ManifestSha256, v.ManifestSha256)
        | Error e -> failwith (dir + ": " + e.Code + " " + e.Detail)

[<Fact>]
let ``ST-01 the frozen fixtures keep their expected state digests`` () =
    let root = Json.decodeUtf8 (File.ReadAllBytes(Path.Combine(repoRoot, "fixtures/v1.0.0/manifest.json"))) |> Result.bind (fun t -> Json.parse (t.TrimEnd '\n')) |> Result.defaultWith failwith
    let entries = match V110.field "investigations" root with Some(JArr xs) -> xs | _ -> []
    Assert.Equal(5, entries.Length)
    for e in entries do
        let id = V110.strField "investigationId" e |> Option.defaultValue ""
        let files = Store.readDirectory (Path.Combine(repoRoot, "fixtures/v1.0.0/investigations", id)) |> Result.defaultWith (fun x -> failwith x.Detail)
        let v = read files |> Result.defaultWith (fun x -> failwith x.Detail)
        Assert.Equal(V110.strField "expectedStateSha256" e, Some v.StateSha256)

[<Fact>]
let ``ST-02 every 1.0.0 investigation negative fixture is refused with the same codes by the 1.1.0 reader`` () =
    let dir = Path.Combine(repoRoot, "fixtures/v1.0.0/negative")
    let mutable checkedCount = 0
    for path in Directory.GetFiles(dir, "*.json") |> Array.sort do
        let doc = Json.decodeUtf8 (File.ReadAllBytes path) |> Result.bind (fun t -> Json.parse (t.TrimEnd '\n')) |> Result.defaultWith failwith
        if V110.strField "kind" doc = Some "investigation" then
            let files =
                match V110.field "files" doc with
                | Some(JObj fs) -> fs |> List.map (fun (n, f) -> n, Convert.FromBase64String(V110.strField "base64" f |> Option.defaultValue "")) |> Map.ofList
                | _ -> Map.empty
            let dirId = V110.strField "directoryId" doc |> Option.defaultValue ""
            let expected = V110.strField "expectedErrorCode" doc
            let expectedDomain = V110.strField "expectedDomainCode" doc
            let outcome = Store.ofFileMap dirId files |> Result.bind read
            match outcome with
            | Ok _ -> failwith ((Path.GetFileName path |> nonNull) + " was accepted by the 1.1.0 reader")
            | Error e ->
                Assert.True((Some e.Code = expected), $"{Path.GetFileName path |> nonNull}: {e.Code}, expected {expected}")
                Assert.True((e.DomainCode = expectedDomain), $"{Path.GetFileName path |> nonNull}: domain {e.DomainCode}, expected {expectedDomain}")
            checkedCount <- checkedCount + 1
    Assert.True(checkedCount > 50, $"only {checkedCount} investigation negatives checked")

[<Fact>]
let ``ST-04 a 1.1.0 history verifies and round-trips byte for byte`` () =
    let files = validFiles.Value
    let v = read files |> Result.defaultWith (fun e -> failwith (e.Code + " " + e.Detail))
    Assert.Equal(V110.V110, v.Format)
    // decode then encode reproduces every file byte for byte
    let again = V110.filesOf v.Lines |> Result.defaultWith (fun e -> failwith e.Detail)
    Assert.Equal<byte[]>(files.Events, again.Events)
    Assert.Equal<byte[]>(files.Manifest, again.Manifest)
    Assert.Equal<byte[]>(files.Snapshot, again.Snapshot)
    let text = Json.decodeUtf8 files.Events |> Result.defaultWith failwith
    for line in text.TrimEnd('\n').Split('\n') do
        Assert.Equal(Ok line, Json.parse line |> Result.bind Json.canonical)
    // the committed fixture is this file set
    let onDisk = Store.readDirectory (Path.Combine(repoRoot, "fixtures/v1.1.0/investigations", files.DirectoryId)) |> Result.defaultWith (fun e -> failwith e.Detail)
    Assert.Equal<byte[]>(files.Events, onDisk.Events)
    Assert.Equal<byte[]>(files.Manifest, onDisk.Manifest)
    Assert.Equal<byte[]>(files.Snapshot, onDisk.Snapshot)

[<Fact>]
let ``ST-04 the 1.1.0 fixture exercises both events, both kinds, a URL, a null URL and a revision after creation`` () =
    let lines = validLines.Value
    let types = lines |> List.choose (V110.strField "eventType")
    Assert.Contains("EvidenceRevised", types)
    Assert.Contains("ObservationRevised", types)
    let kinds = lines |> List.choose (fun l -> if V110.strField "eventType" l = Some "EvidenceRelated" then V110.field "payload" l |> Option.bind (V110.strField "kind") else None)
    Assert.Contains("consistentWith", kinds)
    Assert.Contains("inconclusive", kinds)
    let urls = lines |> List.choose (fun l -> if V110.strField "eventType" l = Some "EvidenceRevised" then V110.field "payload" l |> Option.bind (V110.field "sourceUrl") else None)
    Assert.Contains(JNull, urls)
    Assert.True(urls |> List.exists (function JStr _ -> true | _ -> false))

[<Fact>]
let ``ST-05 no entered value is dropped`` () =
    let v = read validFiles.Value |> Result.defaultWith (fun e -> failwith e.Detail)
    let events = Json.decodeUtf8 validFiles.Value.Events |> Result.defaultWith failwith
    let stateText = Json.canonical v.StateJson |> Result.defaultWith failwith
    let e = V110Fixtures.entered
    let escaped (s: string) = (Json.canonical (JStr s) |> Result.defaultWith failwith).Trim('"')
    // every value entered is in the history verbatim
    for s in [ e.ObservationCardSource; e.LogLabel; e.LogSource; e.LogUrl; e.CacheLabel; e.CacheSource; e.LogLabelRevised; e.LogSourceRevised
               e.ObservationRevised; e.ObservationRevisedSource; e.ConsistentRationale; e.ReassessRationale; e.DraftLabel; "consistentWith"; "inconclusive" ] do
        Assert.True(events.Contains(escaped s), "history lacks " + s)
    // and every current value is in the projected state after reload
    for s in [ e.ObservationCardSource; e.CacheLabel; e.CacheSource; e.LogLabelRevised; e.LogSourceRevised; e.ObservationRevised
               e.ObservationRevisedSource; e.ConsistentRationale; e.ReassessRationale; "consistentWith"; "inconclusive" ] do
        Assert.True(stateText.Contains(escaped s), "state lacks " + s)

[<Fact>]
let ``ST-06 a 1.0.0 reader refuses 1.1.0 data whole`` () =
    match Store.validate v100.Value validFiles.Value with
    | Ok _ -> failwith "the 1.0.0 reader accepted 1.1.0 data"
    | Error e -> Assert.Equal(Codes.unsupportedVersion, e.Code)

[<Fact>]
let ``ST-07 ST-08 ST-10 every 1.1.0 negative fixture is refused with its code`` () =
    let ns = V110Fixtures.negatives validFiles.Value validLines.Value |> Result.defaultWith failwith
    Assert.True(ns.Length >= 15)
    for n in ns do
        match read n.Files with
        | Ok _ -> failwith (n.Name + " was accepted")
        | Error e ->
            Assert.True((e.Code = n.ExpectedErrorCode && e.DomainCode = n.ExpectedDomainCode), $"{n.Name}: {e.Code} {e.DomainCode} ({e.Detail})")
    // the mixed-version cases are refused by the frozen 1.0.0 reader too
    for name in [ "mixed-version-line"; "mixed-version-manifest" ] do
        let n = ns |> List.find (fun x -> x.Name = name)
        match Store.validate v100.Value n.Files with
        | Ok _ -> failwith (name + " accepted by the 1.0.0 reader")
        | Error e -> Assert.Equal(Codes.unsupportedVersion, e.Code)

[<Fact>]
let ``ST-09 a 1.0.0 investigation refuses 1.1.0-only lines before anything is written`` () =
    let dir = investigationDirs "fixtures/v1.0.0/investigations" |> List.head
    let files = Store.readDirectory dir |> Result.defaultWith (fun e -> failwith e.Detail)
    let loaded = Store.validate v100.Value files |> Result.defaultWith (fun e -> failwith e.Detail)
    let revision = validLines.Value |> List.find (fun l -> V110.strField "eventType" l = Some "EvidenceRevised")
    match V110.appendToV100 v100.Value loaded loaded.State.Version [ revision ] with
    | Ok _ -> failwith "a 1.1.0 event was appended to a 1.0.0 investigation"
    | Error e -> Assert.Equal(Codes.unrepresentable, e.Code)
    let kindLine = validLines.Value |> List.find V110.needs110 |> fun l -> l
    Assert.True(V110.needs110 kindLine)
    // the files on disk are untouched
    let after = Store.readDirectory dir |> Result.defaultWith (fun e -> failwith e.Detail)
    Assert.Equal<byte[]>(files.Events, after.Events)

[<Fact>]
let ``ST-11 verbatim edge cases survive a reload`` () =
    let v = read validFiles.Value |> Result.defaultWith (fun e -> failwith e.Detail)
    let evidence = match V110.field "evidence" v.StateJson with Some(JArr xs) -> xs | _ -> []
    let labels = evidence |> List.choose (V110.strField "label")
    Assert.Contains("Token cache metrics​", labels)
    let events = Json.decodeUtf8 validFiles.Value.Events |> Result.defaultWith failwith
    Assert.Contains("\"label\":\"  Gateway handshake log  \"", events)
    Assert.Contains("\"sourceUrl\":\"HTTPS://Example.invalid/%7Elogs/502\"", events)

[<Fact>]
let ``ST-11 NFC and NFD labels and the 200-character boundary are stored and re-read ordinally`` () =
    // CONTRACT 1.1.0 sections 3 and 4.1: text is verbatim (no Unicode normalization), and lengths are not storage
    // rules (S-4 A), so 200 and 201 characters are both stored; INV-027's 200 is the UI's and the command boundary's.
    let nfc = "Caf\u00e9 log"
    let nfd = "Cafe\u0301 log"
    Assert.NotEqual<string>(nfc, nfd)
    Assert.Equal(nfc.Length + 1, nfd.Length)
    let at200 = String.replicate 199 "a" + "\u00e9"
    let at201 = at200 + "b"
    Assert.Equal(200, at200.Length)
    let labels = [ nfc; nfd; at200; at201 ]
    let g n = V110Fixtures.guid "st-11" n
    let evidence = labels |> List.mapi (fun i _ -> (Id.create (g ("evidence-" + string i)) : EvidenceId))
    let steps =
        [ V110Fixtures.Core(StartInvestigation(V110Fixtures.inv, "Label boundaries", Id.create (g "observation"), "Labels are stored verbatim."), None, None)
          V110Fixtures.Core(ActivateInvestigation, None, None)
          yield! List.zip evidence labels |> List.map (fun (e, l) ->
              V110Fixtures.Core(AdmitEvidence(e, TextInput "Content.", Document, "source", None), None,
                                Some(V110Fixtures.EvidenceRev(e, l, "source", None)))) ]
    let lines = V110Fixtures.linesOf steps |> Result.defaultWith failwith
    let files = V110.filesOf lines |> Result.defaultWith (fun e -> failwith e.Detail)
    let v = read files |> Result.defaultWith (fun e -> failwith (e.Code + " " + e.Detail))
    let stored = match V110.field "evidence" v.StateJson with Some(JArr xs) -> xs |> List.choose (V110.strField "label") | _ -> []
    for l in labels do
        Assert.True(stored |> List.exists (fun s -> String.Equals(s, l, StringComparison.Ordinal)), $"label of {l.Length} characters not re-read ordinally")
    let reread = V110.filesOf v.Lines |> Result.defaultWith (fun e -> failwith e.Detail)
    Assert.Equal<byte[]>(files.Events, reread.Events)

/// ST-12 histories: one hypothesis whose only evidence is related with `kind`, carried to a confirmed cause.
/// `engineKind` is what the pinned engine is given when the history is decided; `kind` is what is stored.
let private causeSteps (engineKind: RelationshipKind) (kind: string option) : V110Fixtures.Step list =
    let g k n = V110Fixtures.guid ("st-12-" + k) (defaultArg kind "supports" + "/" + n)
    let h: HypothesisId = Id.create (g "hypothesis" "h")
    let e: EvidenceId = Id.create (g "evidence" "e")
    let cause: CausalAssertionId = Id.create (g "cause" "c")
    let core c = V110Fixtures.Core(c, None, None)
    [ core (StartInvestigation(V110Fixtures.inv, "Checkout latency regression", Id.create (g "observation" "o"), "Checkout p95 latency rose after the deploy."))
      core ActivateInvestigation
      core (RecordHypothesis(h, "The new gateway client opens a connection per request.", None))
      V110Fixtures.Core(AdmitEvidence(e, TextInput "Gateway logs show 1,912 new TLS handshakes per minute.", Log, "gateway access log export", None), None,
                        Some(V110Fixtures.EvidenceRev(e, "Gateway handshake log", "gateway access log export", None)))
      V110Fixtures.Core(RelateEvidence(Id.create (g "relationship" "r"), e, h, engineKind, Some "Per-request handshakes would explain it."), kind, None)
      core (TransitionHypothesis(h, MarkPlausible, "The handshake count fits.", [ e ]))
      core (TransitionHypothesis(h, MarkSupported, "The handshake count is the mechanism.", [ e ]))
      core (RecordCausalAssertion(cause, "Checkout latency regression", [ { Hypothesis = h; Role = RootCause } ], Some "Per-request TLS handshakes."))
      core (ConfirmCause cause) ]

[<Fact>]
let ``ST-12 a writer cannot confirm a cause whose only evidence is consistentWith or inconclusive`` () =
    // The writer decides on the core view (CONTRACT 1.1.0 section 4.6): the engine sees `insufficient`, and its cause
    // gate refuses the confirmation.
    for k in V110.addedKinds do
        match V110Fixtures.linesOf (causeSteps Insufficient (Some k)) with
        | Ok _ -> failwith (k + ": the engine confirmed a cause whose only evidence is " + k)
        | Error e -> Assert.True(e.Contains "CAUSE.NO_SUPPORTING_EVIDENCE", k + ": " + e)

[<Fact>]
let ``ST-12 a stored confirmation supported only by consistentWith or inconclusive is refused by the reader`` () =
    // Control: decided and stored as `supports`, the history is admissible.
    let files (lines: JValue list) = V110.filesOf lines |> Result.defaultWith (fun e -> failwith e.Detail)
    let supported = V110Fixtures.linesOf (causeSteps Supports None) |> Result.defaultWith failwith
    Assert.True(V110.needs110 (supported |> List.find V110.isRevision))
    match read (files supported) with
    | Ok v -> Assert.Equal(V110.V110, v.Format)
    | Error e -> failwith ("control refused: " + e.Code + " " + e.Detail)
    // The same history with the support stored as a 1.1.0 kind: the cause gate of the core view counts only
    // `supports`, so the confirmation is inadmissible.
    for k in V110.addedKinds do
        let forged = V110Fixtures.linesOf (causeSteps Supports (Some k)) |> Result.defaultWith failwith
        match read (files forged) with
        | Ok _ -> failwith (k + " was counted as support for the cause gate")
        | Error e ->
            Assert.Equal(Codes.historyInadmissible, e.Code)
            Assert.True(e.Detail.Contains "CAUSE.NO_SUPPORTING_EVIDENCE", k + ": " + e.Detail)

[<Fact>]
let ``fixtures v1.1.0 on disk are exactly the deterministic rebuild`` () =
    Assert.Empty(V110Fixtures.verify repoRoot v100.Value v110.Value)

/// A 1.0.0 schema value with its version strings rewritten as 1.1.0 writes them.
let rec private as110 (v: JValue) =
    match v with
    | JStr s -> JStr(if s = "1.0.0" then "1.1.0" else s.Replace("v1.0.0", "v1.1.0"))
    | JArr xs -> JArr(List.map as110 xs)
    | JObj ps -> JObj(ps |> List.map (fun (k, x) -> k, as110 x))
    | x -> x

/// Problems that make `newer` more than an additive extension of `older`, at `path`. Additive: every member of an
/// object is kept, and members are added only to a map of names (properties, $defs); every element of an array is
/// kept in order, and elements are added only to a set (enum, required, oneOf, anyOf, allOf); a description only grows.
let rec private notAdditive (path: string) (parent: string) (older: JValue) (newer: JValue) : string list =
    match older, newer with
    | JObj o, JObj n ->
        let added = n |> List.map fst |> List.filter (fun k -> not (o |> List.exists (fst >> (=) k)))
        [ yield! o |> List.collect (fun (k, x) ->
              match n |> List.tryFind (fst >> (=) k) with
              | Some(_, y) -> notAdditive (path + "/" + k) k x y
              | None -> [ path + "/" + k + " removed" ])
          if not added.IsEmpty && not (List.contains parent [ "properties"; "$defs" ]) then
              yield path + " adds " + String.Join(", ", added) ]
    | JArr o, JArr n ->
        let rec kept (os: JValue list) (ns: JValue list) =
            match os, ns with
            | [], _ -> true
            | _, [] -> false
            | x :: os', y :: ns' -> if x = y then kept os' ns' else kept os ns'
        [ if not (kept o n) then yield path + " drops or reorders an element"
          if n.Length > o.Length && not (List.contains parent [ "enum"; "required"; "oneOf"; "anyOf"; "allOf" ]) then yield path + " grows" ]
    | JStr o, JStr n when parent = "description" -> if n.StartsWith o then [] else [ path + " rewritten" ]
    | o, n -> if o = n then [] else [ path + " changed" ]

[<Fact>]
let ``every 1.0.0 schema is byte-identical to the release`` () =
    // The released bundle (compatibility/v1.0.0/lock.json schemaBundleSha256) names every 1.0.0 schema by digest.
    let doc (p: string) = Json.decodeUtf8 (File.ReadAllBytes(Path.Combine(repoRoot, p))) |> Result.bind (fun t -> Json.parse (t.TrimEnd '\n')) |> Result.defaultWith failwith
    let bundlePath = "contract/v1.0.0/schemas/bundle.json"
    Assert.Equal(V110.strField "schemaBundleSha256" (doc "compatibility/v1.0.0/lock.json"), Some(Json.sha256Hex (File.ReadAllBytes(Path.Combine(repoRoot, bundlePath)))))
    let named = match V110.field "schemas" (doc bundlePath) with Some(JArr xs) -> xs | _ -> []
    let onDisk = Directory.GetFiles(Path.Combine(repoRoot, "contract/v1.0.0/schemas"), "*.schema.json", SearchOption.AllDirectories)
    Assert.Equal(onDisk.Length, named.Length)
    for s in named do
        let p = V110.strField "path" s |> Option.defaultValue ""
        Assert.True((V110.strField "sha256" s = Some(Json.sha256Hex (File.ReadAllBytes(Path.Combine(repoRoot, "contract/v1.0.0/schemas", p))))), p + " differs from the release")

[<Fact>]
let ``the 1.1.0 schemas are the 1.0.0 schemas with additions only`` () =
    let names dir = Directory.GetFiles(Path.Combine(repoRoot, dir), "*.schema.json", SearchOption.AllDirectories) |> Array.map (fun f -> Path.GetRelativePath(Path.Combine(repoRoot, dir), f)) |> Set.ofArray
    let added = Set.difference (names "contract/v1.1.0/schemas") (names "contract/v1.0.0/schemas")
    Assert.Equal<Set<string>>(set [ "payloads/EvidenceRevised.schema.json"; "payloads/ObservationRevised.schema.json" ], added)
    Assert.Empty(Set.difference (names "contract/v1.0.0/schemas") (names "contract/v1.1.0/schemas"))
    // Content: each 1.1.0 schema is its 1.0.0 schema, version strings aside, with additions only.
    let doc (p: string) = Json.decodeUtf8 (File.ReadAllBytes p) |> Result.bind (fun t -> Json.parse (t.TrimEnd '\n')) |> Result.defaultWith failwith
    let problems =
        names "contract/v1.0.0/schemas" |> Set.toList |> List.collect (fun n ->
            notAdditive n "" (as110 (doc (Path.Combine(repoRoot, "contract/v1.0.0/schemas", n)))) (doc (Path.Combine(repoRoot, "contract/v1.1.0/schemas", n))))
    Assert.Empty(problems)
