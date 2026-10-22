namespace Causa.GitHub

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Threading.Tasks

/// Provider backend over git smart-HTTP against the real GitHub remote. Semantics match the
/// REST backend: a single commit per semantic transition, published by a NON-forced push that
/// GitHub rejects unless it is a fast-forward of the current branch head (CONTRACT §9.4.2).
[<RequireQualifiedAccess>]
module GitBackend =
    type GitResult = { Code: int; Out: byte[]; Err: string }

    let private run (dir: string) (env: (string * string) list) (stdin: byte[] option) (args: string list) : Task<GitResult> =
        task {
            let psi = ProcessStartInfo("git")
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            psi.RedirectStandardInput <- stdin.IsSome
            psi.UseShellExecute <- false
            ("-C" :: dir :: args) |> List.iter psi.ArgumentList.Add
            env |> List.iter (fun (k, v) -> psi.Environment[k] <- v)
            use p = Process.Start psi |> nonNull
            match stdin with
            | Some bytes ->
                do! p.StandardInput.BaseStream.WriteAsync(bytes, 0, bytes.Length)
                p.StandardInput.Close()
            | None -> ()
            use ms = new MemoryStream()
            let copy = p.StandardOutput.BaseStream.CopyToAsync ms
            let! err = p.StandardError.ReadToEndAsync()
            do! copy
            do! p.WaitForExitAsync()
            return { Code = p.ExitCode; Out = ms.ToArray(); Err = err }
        }

    let private text (r: GitResult) = Encoding.UTF8.GetString(r.Out).Trim()

    let private classify (r: GitResult) : Failure =
        let all = (text r + "\n" + r.Err).ToLowerInvariant()
        if all.Contains "non-fast-forward" || all.Contains "[rejected]" || all.Contains "fetch first" || all.Contains "stale info" then NotFastForward(r.Err.Trim())
        elif all.Contains "403" || all.Contains "permission" || all.Contains "denied" || all.Contains "authentication" then Auth(r.Err.Trim())
        elif all.Contains "could not resolve" || all.Contains "connection" || all.Contains "timed out" || all.Contains "unable to access" then Network(r.Err.Trim())
        else Other(r.Err.Trim())

    let private ok (r: GitResult) : Result<GitResult, Failure> = if r.Code = 0 then Ok r else Error(classify r)

    let private identity =
        [ "GIT_AUTHOR_NAME", "causa-live-test"; "GIT_AUTHOR_EMAIL", "causa-live-test@users.noreply.github.com"
          "GIT_COMMITTER_NAME", "causa-live-test"; "GIT_COMMITTER_EMAIL", "causa-live-test@users.noreply.github.com" ]

    /// Create (or reuse) a scratch bare repository whose origin is the real GitHub repository.
    let create (dir: string) (remoteUrl: string) (branch: string) : Task<Backend> =
        task {
            if not (Directory.Exists(Path.Combine(dir, "objects"))) then
                Directory.CreateDirectory dir |> ignore
                let! _ = run dir [] None [ "init"; "--bare"; "-q" ]
                let! _ = run dir [] None [ "remote"; "add"; "origin"; remoteUrl ]
                ()
            let git = run dir []
            let fetch (what: string) (into: string) = git None [ "fetch"; "-q"; "--no-tags"; "--depth=1"; "origin"; "+" + what + ":" + into ] |> fun t -> task { let! r = t in return ok r }
            let revParse (rev: string) = task { let! r = git None [ "rev-parse"; "--verify"; rev ] in return ok r |> Result.map text }
            let head () =
                task {
                    match! fetch ("refs/heads/" + branch) "refs/causa/head" with
                    | Error f -> return Error f
                    | Ok _ ->
                        let! c = revParse "refs/causa/head"
                        let! t = revParse "refs/causa/head^{tree}"
                        return (match c, t with
                                | Ok commit, Ok tree -> Ok { HeadCommit = commit; TreeSha = tree }
                                | Error e, _ | _, Error e -> Error e)
                }
            let listUnder (tree: string) (prefix: string) =
                task {
                    let! r = git None [ "ls-tree"; "-r"; "-z"; tree; "--"; prefix ]
                    return
                        ok r
                        |> Result.map (fun r ->
                            Encoding.UTF8.GetString(r.Out).Split('\000', StringSplitOptions.RemoveEmptyEntries)
                            |> List.ofArray
                            |> List.choose (fun line ->
                                match line.Split('\t', 2) with
                                | [| meta; path |] ->
                                    match meta.Split(' ') with
                                    | [| _; "blob"; sha |] when path.StartsWith prefix -> Some(path, sha)
                                    | _ -> None
                                | _ -> None))
                }
            let blob (sha: string) = task { let! r = git None [ "cat-file"; "blob"; sha ] in return ok r |> Result.map (fun r -> r.Out) }
            let treeOf (commit: string) =
                task {
                    match! fetch commit ("refs/causa/probe") with
                    | Error f -> return Error f
                    | Ok _ -> return! revParse (commit + "^{tree}")
                }
            let changed (baseCommit: string) (commit: string) =
                task {
                    let! r = git None [ "diff"; "--name-only"; "--no-renames"; baseCommit; commit ]
                    return ok r |> Result.map (fun r -> (text r).Split('\n', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray)
                }
            let publish (stop: Stage -> bool) (baseToken: ProviderToken) (prefix: string) (files: (string * byte[]) list) (message: string) =
                task {
                    let halt s = if stop s then Some(Error(Network("injected stop " + string s))) else None
                    match halt BeforeBlobs with
                    | Some e -> return e
                    | None ->
                        let! blobs =
                            files
                            |> List.fold
                                (fun (acc: Task<Result<(string * string) list, Failure>>) (path, bytes) ->
                                    task {
                                        match! acc with
                                        | Error e -> return Error e
                                        | Ok done' ->
                                            let! r = git (Some bytes) [ "hash-object"; "-w"; "--stdin" ]
                                            return ok r |> Result.map (fun r -> done' @ [ path, text r ])
                                    })
                                (Task.FromResult(Ok []))
                        match blobs, halt BeforeTree with
                        | Error e, _ -> return Error e
                        | _, Some e -> return e
                        | Ok entries, None ->
                            let index = Path.Combine(dir, "causa-index-" + Guid.NewGuid().ToString("N"))
                            let withIndex = run dir [ "GIT_INDEX_FILE", index ]
                            let! _ = withIndex None [ "read-tree"; baseToken.TreeSha ]
                            // One update for all entries: the index is a single-writer file.
                            let info = entries |> List.map (fun (p, s) -> "100644 " + s + "\t" + p + "\n") |> String.concat ""
                            let! update = withIndex (Some(Encoding.UTF8.GetBytes info)) [ "update-index"; "--add"; "--index-info" ]
                            let! tree = withIndex None [ "write-tree" ]
                            if File.Exists index then File.Delete index
                            match [ update ] |> List.tryFind (fun r -> r.Code <> 0), ok tree, halt BeforeCommit with
                            | Some bad, _, _ -> return Error(classify bad)
                            | _, Error e, _ -> return Error e
                            | _, _, Some e -> return e
                            | None, Ok t, None ->
                                let! commit = run dir identity None [ "commit-tree"; text t; "-p"; baseToken.HeadCommit; "-m"; message ]
                                match ok commit with
                                | Error e -> return Error e
                                | Ok c ->
                                    let commitSha = text c
                                    match! changed baseToken.HeadCommit commitSha with
                                    | Error e -> return Error e
                                    | Ok paths when paths |> List.exists (fun p -> not (p.StartsWith prefix)) ->
                                        return Error(Other("refusing to publish: changes outside " + prefix + ": " + String.Join(",", paths)))
                                    | Ok _ ->
                                        match halt BeforeRefUpdate with
                                        | Some e -> return e
                                        | None ->
                                            // Non-forced: GitHub refuses anything but a fast-forward of the current head.
                                            let! push = git None [ "push"; "--porcelain"; "origin"; commitSha + ":refs/heads/" + branch ]
                                            return ok push |> Result.map (fun _ -> commitSha)
                }
            return
                { Name = "git-smart-http"
                  Head = head
                  ListUnder = listUnder
                  Blob = blob
                  TreeOf = treeOf
                  ChangedPaths = changed
                  Publish = publish }
        }
