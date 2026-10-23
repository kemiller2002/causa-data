namespace Causa.Tool.Historical

open System
open System.IO
open IndyInitRootCauseInvestigator.Engine
open Causa.Contract

/// One research source record (research/historical/RESEARCH-INTERFACE.md, `sources.json`).
type Source =
    { SourceId: string
      Title: string
      Issuer: string
      Published: string option
      Url: string
      ArchivalLocator: string option
      RetrievedAt: string
      SourceClass: string
      RetrievalStatus: string
      HttpStatus: int64 option
      Rights: string }

/// One historical claim (`claims.json`). Claims are the only content the construction may admit as evidence.
type Claim =
    { ClaimId: string
      Statement: string
      SourceId: string
      Locator: string
      Excerpt: string option
      Category: string
      Verification: string
      HistoricalTime: DateTimeOffset option
      TimeBasis: string option
      Topic: string
      Kind: string
      Notes: string option }

type Research =
    { CaseKey: string
      Sources: Map<string, Source>
      Claims: Claim list
      SourcesBytes: byte[]
      ClaimsBytes: byte[] }

[<RequireQualifiedAccess>]
module Research =
    let directory (root: string) (caseKey: string) = Path.Combine(root, "research", "historical", caseKey)

    let categories = [ "document"; "testimony"; "measurement"; "log"; "directObservation"; "experimentResult"; "externalReference" ]
    let verifications = [ "verified"; "partiallyVerified"; "unverified" ]
    let kinds = [ "fact"; "finding"; "recommendation"; "testimony"; "contradiction"; "uncertainty" ]

    let private field (o: (string * JValue) list) (k: string) = Json.field k o |> Option.defaultValue JNull

    let private str (where: string) (o: (string * JValue) list) (k: string) : Result<string, string> =
        match field o k with
        | JStr s when not (String.IsNullOrWhiteSpace s) -> Ok s
        | _ -> Error(where + ": '" + k + "' must be a non-blank string")

    let private optStr (where: string) (o: (string * JValue) list) (k: string) : Result<string option, string> =
        match field o k with
        | JNull -> Ok None
        | JStr s when not (String.IsNullOrWhiteSpace s) -> Ok(Some s)
        | _ -> Error(where + ": '" + k + "' must be null or a non-blank string")

    let private optInt (where: string) (o: (string * JValue) list) (k: string) : Result<int64 option, string> =
        match field o k with
        | JNull -> Ok None
        | JInt n -> Ok(Some n)
        | _ -> Error(where + ": '" + k + "' must be null or an integer")

    let private oneOf (where: string) (k: string) (allowed: string list) (v: string) =
        if List.contains v allowed then Ok v else Error(where + ": '" + k + "' value '" + v + "' is not one of " + String.Join(", ", allowed))

    let private objects (name: string) (bytes: byte[]) : Result<(string * JValue) list list, string> =
        Json.decodeUtf8 bytes
        |> Result.bind Json.parse
        |> Result.mapError (fun e -> name + ": " + e)
        |> Result.bind (function
            | JArr xs -> xs |> traverse (function JObj o -> Ok o | _ -> Error(name + ": every entry must be an object"))
            | _ -> Error(name + " must be a JSON array"))

    let private source (o: (string * JValue) list) : Result<Source, string> =
        result {
            let! id = str "source" o "sourceId"
            let w = "source " + id
            let! title = str w o "title"
            let! issuer = str w o "issuer"
            let! published = optStr w o "published"
            let! url = str w o "url"
            let! locator = optStr w o "archivalLocator"
            let! retrieved = str w o "retrievedAt"
            let! cls = str w o "sourceClass" |> Result.bind (oneOf w "sourceClass" [ "primary"; "secondary" ])
            let! status = str w o "retrievalStatus" |> Result.bind (oneOf w "retrievalStatus" [ "retrieved"; "notRetrieved" ])
            let! http = optInt w o "httpStatus"
            let! rights = str w o "rights"
            return
                { SourceId = id; Title = title; Issuer = issuer; Published = published; Url = url; ArchivalLocator = locator; RetrievedAt = retrieved
                  SourceClass = cls; RetrievalStatus = status; HttpStatus = http; Rights = rights }
        }

    let private claim (o: (string * JValue) list) : Result<Claim, string> =
        result {
            let! id = str "claim" o "claimId"
            let w = "claim " + id
            let! statement = str w o "statement"
            let! sourceId = str w o "sourceId"
            let! locator = str w o "locator"
            let! excerpt = optStr w o "excerpt"
            let! category = str w o "evidenceCategory" |> Result.bind (oneOf w "evidenceCategory" categories)
            let! verification = str w o "verification" |> Result.bind (oneOf w "verification" verifications)
            let! timeText = optStr w o "historicalTime"
            let! time =
                match timeText with
                | None -> Ok None
                | Some t ->
                    match Timestamp.tryParse t with
                    | Some d -> Ok(Some d)
                    | None -> Error(w + ": historicalTime '" + t + "' is not an RFC 3339 UTC millisecond timestamp")
            let! basis = optStr w o "timeBasis"
            let! topic = str w o "topic"
            let! kind = str w o "kind" |> Result.bind (oneOf w "kind" kinds)
            let! notes = optStr w o "notes"
            do! if time.IsSome && basis.IsNone then Error(w + ": historicalTime needs a timeBasis") else Ok()
            return
                { ClaimId = id; Statement = statement; SourceId = sourceId; Locator = locator; Excerpt = excerpt; Category = category
                  Verification = verification; HistoricalTime = time; TimeBasis = basis; Topic = topic; Kind = kind; Notes = notes }
        }

    let private unique (what: string) (ids: string list) =
        match ids |> List.countBy id |> List.filter (fun (_, n) -> n > 1) with
        | [] -> Ok()
        | dups -> Error(what + " ids repeat: " + String.Join(", ", dups |> List.map fst))

    /// Load and structurally check one case's research records. Every claim must cite a known source.
    let load (root: string) (caseKey: string) : Result<Research, string> =
        let dir = directory root caseKey
        let read name = try Ok(File.ReadAllBytes(Path.Combine(dir, name))) with ex -> Error(caseKey + "/" + name + ": " + ex.Message)
        result {
            let! sourcesBytes = read "sources.json"
            let! claimsBytes = read "claims.json"
            let! sources = objects (caseKey + "/sources.json") sourcesBytes |> Result.bind (traverse source)
            let! claims = objects (caseKey + "/claims.json") claimsBytes |> Result.bind (traverse claim)
            do! unique "source" (sources |> List.map (fun s -> s.SourceId))
            do! unique "claim" (claims |> List.map (fun c -> c.ClaimId))
            let byId = sources |> List.map (fun s -> s.SourceId, s) |> Map.ofList
            do! match claims |> List.filter (fun c -> not (byId.ContainsKey c.SourceId)) with
                | [] -> Ok()
                | missing -> Error(caseKey + ": claims cite unknown sources: " + String.Join(", ", missing |> List.map (fun c -> c.ClaimId + "->" + c.SourceId)))
            return { CaseKey = caseKey; Sources = byId; Claims = claims; SourcesBytes = sourcesBytes; ClaimsBytes = claimsBytes }
        }

    /// A claim may support established findings only when its source text was read and confirmed.
    let usable (c: Claim) = c.Verification <> "unverified"

    let category (c: Claim) : EvidenceCategory =
        match c.Category with
        | "testimony" -> Testimony
        | "measurement" -> Measurement
        | "log" -> Log
        | "directObservation" -> DirectObservation
        | "experimentResult" -> ExperimentResult
        | "externalReference" -> ExternalReference
        | _ -> Document

    let verification (c: Claim) : VerificationStatus =
        match c.Verification with
        | "verified" -> Verified
        | "partiallyVerified" -> PartiallyVerified
        | _ -> Unverified

    /// Evidence content: the exact excerpt (when one was copied), the researcher's paraphrase and the reliability note.
    let content (c: Claim) =
        [ c.Excerpt |> Option.map (fun e -> "Excerpt: \"" + e + "\"")
          Some("Paraphrase: " + c.Statement)
          c.TimeBasis |> Option.map (fun b -> "Time basis: " + b)
          c.Notes |> Option.map (fun n -> "Note: " + n) ]
        |> List.choose id
        |> String.concat "\n"

    /// Evidence source: a complete, traceable citation.
    let citation (s: Source) (c: Claim) =
        String.concat
            ""
            [ s.Title
              " ("
              s.Issuer
              (s.Published |> Option.map (fun p -> ", " + p) |> Option.defaultValue "")
              (s.ArchivalLocator |> Option.map (fun l -> ", " + l) |> Option.defaultValue "")
              "), "
              c.Locator
              ". "
              s.Url
              " [claim "
              c.ClaimId
              "; source "
              s.SourceId
              "; "
              s.SourceClass
              " source; retrieved "
              s.RetrievedAt
              "]" ]
