module Causa.Tool.Program

open System
open System.IO
open Causa.Contract

let private schemasOrExit root =
    match Store.loadSchemas (Layout.schemas root) with
    | Ok s -> s
    | Error e ->
        eprintfn "schema bundle unusable: %s %s" e.Code e.Detail
        exit 2

let private generateInto (root: string) =
    let set = schemasOrExit root
    match Dataset.generate set with
    | Error e ->
        eprintfn "generation failed: %s" e
        exit 1
    | Ok g ->
        Dataset.write root set g
        g.Runs |> List.iter (fun r -> printfn "%-22s commands=%d events=%d refusals=%d" r.Scenario.Key r.Scenario.Steps.Length r.Events.Length r.Refusals.Length)

/// Every generated path, relative to the repository root.
let private generatedFiles (root: string) =
    [ Layout.bundle root; Layout.rootManifest root ]
    @ (Directory.GetFiles(Layout.golden root) |> List.ofArray)
    @ (Directory.GetFiles(Layout.investigations root, "*", SearchOption.AllDirectories) |> List.ofArray)
    @ (Directory.GetFiles(Layout.negative root) |> List.ofArray)
    |> List.map (fun p -> Layout.posix (Path.GetRelativePath(root, p)))
    |> List.sort

let private printChecks (checks: Dataset.Check list) =
    checks |> List.iter (fun c -> printfn "%s %s — %s" (if c.Passed then "PASS" else "FAIL") c.Name c.Detail)
    let failed = checks |> List.filter (fun c -> not c.Passed)
    printfn "checks=%d passed=%d failed=%d" checks.Length (checks.Length - failed.Length) failed.Length
    if failed.IsEmpty then 0 else 1

let private copyDir (src: string) (dst: string) =
    Directory.GetFiles(src, "*", SearchOption.AllDirectories)
    |> Array.iter (fun f ->
        let target = Path.Combine(dst, Path.GetRelativePath(src, f))
        Directory.CreateDirectory(Path.GetDirectoryName target |> nonNull) |> ignore
        File.Copy(f, target, true))

/// Regenerate from the committed schemas into a scratch tree and require byte-identical output.
let private reproduce (root: string) =
    let scratch = Path.Combine(Path.GetTempPath(), "causa-reproduce-" + string Environment.ProcessId)
    if Directory.Exists scratch then Directory.Delete(scratch, true)
    copyDir (Layout.schemas root) (Layout.schemas scratch)
    File.Delete(Layout.bundle scratch)
    generateInto scratch
    let committed = generatedFiles root
    let regenerated = generatedFiles scratch
    let diffs =
        (Set.union (Set.ofList committed) (Set.ofList regenerated))
        |> Set.toList
        |> List.filter (fun rel ->
            let a, b = Path.Combine(root, rel), Path.Combine(scratch, rel)
            not (File.Exists a && File.Exists b && File.ReadAllBytes a = File.ReadAllBytes b))
    Directory.Delete(scratch, true)
    diffs |> List.iter (printfn "DIFF %s")
    printfn "reproduce: files=%d differing=%d" committed.Length diffs.Length
    if diffs.IsEmpty then 0 else 1

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    | [ "generate"; root ] -> generateInto (Path.GetFullPath root); 0
    | [ "verify"; root ] -> Dataset.verify (Path.GetFullPath root) |> printChecks
    | [ "reproduce"; root ] -> reproduce (Path.GetFullPath root)
    | _ ->
        eprintfn "usage: causa (generate|verify|reproduce) <repository-root>"
        2
