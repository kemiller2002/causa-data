module CleanConsumer.Program

open System
open CleanConsumer.Dataset

let private report (checks: Check list) : int =
    // One line per check: control characters in details are shown escaped.
    let oneLine (s: string) = s.Replace("\r", "\\r").Replace("\n", "\\n")
    checks |> List.iter (fun c -> printfn "%s %s %s" (if c.Passed then "PASS" else "FAIL") c.Name (oneLine c.Detail))
    let failed = checks |> List.filter (fun c -> not c.Passed) |> List.length
    printfn "checks=%d passed=%d failed=%d" checks.Length (checks.Length - failed) failed
    if failed = 0 then 0 else 1

let private usage () =
    eprintfn "usage: clean-consumer verify <repoRoot>"
    eprintfn "       clean-consumer append-demo <repoRoot> <outDir>"
    eprintfn "       clean-consumer validate <investigationDir>"
    2

let private dispatch (argv: string list) : int =
    match argv with
    | [ "verify"; root ] -> verify root |> report
    | [ "append-demo"; root; outDir ] -> AppendDemo.run root outDir |> report
    | [ "validate"; dir ] ->
        // Validate one investigation directory (reader stages 1-16) and print the primary code.
        match loadFileSet dir with
        | Error e -> eprintfn "%s" e; 2
        | Ok files ->
            let r = Verify.validate files
            printfn "%s" ((Verify.describe r).Replace("\n", "\\n"))
            if Result.isOk r then 0 else 1
    | _ -> usage ()

[<EntryPoint>]
let main argv =
    // I/O edge: an unexpected exception is a failure, never a silent success.
    try dispatch (List.ofArray argv)
    with ex ->
        printfn "FAIL internal %s" ex.Message
        printfn "checks=1 passed=0 failed=1"
        1
