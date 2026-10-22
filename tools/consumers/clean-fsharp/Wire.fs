/// Byte-level wire conventions (CONTRACT §2): encoding/framing, digests, UUIDs, timestamps.
module CleanConsumer.Wire

open System
open System.Globalization
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions

// ---------------------------------------------------------------- digests

let sha256Hex (bytes: byte[]) = SHA256.HashData bytes |> Convert.ToHexStringLower

let utf8 (s: string) : byte[] = UTF8Encoding(false).GetBytes s

/// §2.11: JSON documents are hashed over their canonical text WITHOUT the trailing LF.
let canonicalDigest (v: Json.JsonValue) = Json.canonical v |> utf8 |> sha256Hex

/// A JSON file's exact bytes: one canonical text followed by exactly one LF (§2.2).
let jsonFileBytes (v: Json.JsonValue) = utf8 (Json.canonical v + "\n")

// ---------------------------------------------------------------- framing (§2.1, §2.2)

let private strictUtf8 = UTF8Encoding(false, true)

let private decode (bytes: byte[]) : Result<string, string> =
    if bytes.Length >= 3 && bytes[0] = 0xEFuy && bytes[1] = 0xBBuy && bytes[2] = 0xBFuy then
        Error "starts with a UTF-8 byte order mark"
    else
        try
            let text = strictUtf8.GetString bytes
            if text.Contains '\r' then Error "contains a CR (only LF line endings are allowed)"
            elif text.Length > 0 && text[0] = '﻿' then Error "starts with a byte order mark"
            else Ok text
        with :? DecoderFallbackException as ex -> Error $"invalid UTF-8: {ex.Message}"

/// A JSON file: valid UTF-8, no BOM, no CR, terminated by LF. Returns the text before the final LF.
/// (Interior whitespace, including extra LFs, is left to the canonical-form stage.)
let jsonFileText (bytes: byte[]) : Result<string, string> =
    decode bytes
    |> Result.bind (fun text ->
        if text.Length = 0 then Error "empty file"
        elif not (text.EndsWith '\n') then Error "not terminated by LF"
        else Ok(text.Substring(0, text.Length - 1)))

/// events.ndjson: one text per line, each terminated by LF, no blank lines.
let ndjsonLines (bytes: byte[]) : Result<string list, string> =
    decode bytes
    |> Result.bind (fun text ->
        if text.Length = 0 then Error "empty events.ndjson (a history has at least InvestigationStarted)"
        elif not (text.EndsWith '\n') then Error "last line is not terminated by LF"
        else
            let lines = text.Substring(0, text.Length - 1).Split('\n') |> List.ofArray
            match lines |> List.tryFindIndex String.IsNullOrEmpty with
            | Some i -> Error $"blank line at line {i + 1}"
            | None -> Ok lines)

// ---------------------------------------------------------------- regexes (§2.13)

/// Schema patterns have ECMA-262 semantics: a final `$` matches only at the very end of the string.
/// .NET's `$` also matches before a final LF, so a trailing `$` is translated to `\z`.
let ecmaRegex (pattern: string) : Regex =
    let translated = if pattern.EndsWith "$" then pattern.Substring(0, pattern.Length - 1) + "\\z" else pattern
    Regex(translated, RegexOptions.CultureInvariant)

// ---------------------------------------------------------------- UUIDs (§2.8, §5.2)

let uuidPattern = ecmaRegex "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
let nilUuid = "00000000-0000-0000-0000-000000000000"
let isCanonicalUuid (s: string) = uuidPattern.IsMatch s && s <> nilUuid
let uuidText (g: Guid) = g.ToString("D")

/// RFC 4122 §4.3 name-based UUID, SHA-1 (version 5).
let uuidV5 (ns: Guid) (name: string) : Guid =
    let hash = SHA1.HashData(Array.append (ns.ToByteArray(true)) (utf8 name))
    let bytes =
        hash
        |> Array.take 16
        |> Array.mapi (fun i b ->
            match i with
            | 6 -> (b &&& 0x0Fuy) ||| 0x50uy
            | 8 -> (b &&& 0x3Fuy) ||| 0x80uy
            | _ -> b)
    Guid(ReadOnlySpan<byte>(bytes), true)

let namespaceUrl = Guid.Parse "6ba7b811-9dad-11d1-80b4-00c04fd430c8"
let eventIdNamespaceName = "https://github.com/kemiller2002/causa-data/contract/v1/event-id"
let eventIdNamespaceStated = Guid.Parse "241fefd4-c810-5a3a-9cfa-4bbd968ae1cb"
let eventIdNamespace = uuidV5 namespaceUrl eventIdNamespaceName

/// §5.2: eventId = UUIDv5(N, investigationId + "/" + aggregateVersion + "/" + correlationId).
let deriveEventId (investigationId: string) (aggregateVersion: int64) (correlationId: string) =
    uuidV5 eventIdNamespace $"{investigationId}/{aggregateVersion.ToString(CultureInfo.InvariantCulture)}/{correlationId}"
    |> uuidText

// ---------------------------------------------------------------- timestamps (§2.9)

let private timestampPattern =
    ecmaRegex "^([0-9]{4})-([0-9]{2})-([0-9]{2})T([0-9]{2}):([0-9]{2}):([0-9]{2})\\.([0-9]{3})Z$"

/// Parses the only accepted form; rejects non-calendar instants (02-30, hour 24, second 60, year 0000).
let parseTimestamp (s: string) : Result<DateTimeOffset, string> =
    let m = timestampPattern.Match s
    if not m.Success then Error $"\"{s}\" is not RFC 3339 UTC with exactly three fractional digits and Z"
    else
        let g (i: int) = Int32.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture)
        let y, mo, d, h, mi, se, ms = g 1, g 2, g 3, g 4, g 5, g 6, g 7
        if y < 1 then Error $"\"{s}\": year 0000 is not representable"
        elif mo < 1 || mo > 12 then Error $"\"{s}\": month out of range"
        elif d < 1 || d > DateTime.DaysInMonth(y, mo) then Error $"\"{s}\": not a real calendar date"
        elif h > 23 || mi > 59 || se > 59 then Error $"\"{s}\": time of day out of range (no leap seconds)"
        else Ok(DateTimeOffset(DateTime(y, mo, d, h, mi, se, ms, DateTimeKind.Utc)))

/// Writer side: refuse sub-millisecond precision rather than truncate (CAUSA.UNREPRESENTABLE).
let formatTimestamp (t: DateTimeOffset) : Result<string, string> =
    let utc = t.UtcDateTime
    if utc.Ticks % TimeSpan.TicksPerMillisecond <> 0L then
        Error $"instant {t:O} has sub-millisecond precision (CAUSA.UNREPRESENTABLE)"
    else Ok(utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))

// ---------------------------------------------------------------- text (§2.6)

/// The engine whitespace set of §2.6.
let isEngineWhitespace (c: char) =
    (c >= '\u0009' && c <= '\u000D') || c = ' ' || c = '\u0085' || c = ' ' || c = ' '
    || (c >= ' ' && c <= ' ') || c = ' ' || c = ' ' || c = ' ' || c = ' ' || c = '　'

let isNonBlankText (s: string) = s.Length >= 1 && s |> Seq.exists (isEngineWhitespace >> not)

/// common.schema.json httpUrl: ^[Hh][Tt][Tt][Pp][Ss]?://<non-whitespace char>
let isHttpUrl (s: string) =
    let lower = s.ToLowerInvariant()
    let rest =
        if lower.StartsWith "https://" then Some(s.Substring 8)
        elif lower.StartsWith "http://" then Some(s.Substring 7)
        else None
    match rest with
    | Some r -> r.Length > 0 && not (isEngineWhitespace r[0])
    | None -> false

let slugPattern = ecmaRegex "^[a-z0-9][a-z0-9-]{0,63}$"
let sha256Pattern = ecmaRegex "^[0-9a-f]{64}$"
