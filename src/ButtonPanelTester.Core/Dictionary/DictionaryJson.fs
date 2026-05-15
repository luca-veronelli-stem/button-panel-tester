namespace Stem.ButtonPanelTester.Core.Dictionary

open System.Text.Json
open System.Text.Json.Serialization

/// JSON round-trip surface for `ButtonPanelDictionary`, per
/// `specs/001-fetch-dictionary/data-model.md` §1.1 and the wire
/// shape contracted on
/// `specs/001-fetch-dictionary/contracts/dictionary-api.md` lines
/// 62-77. Pairs with the FsCheck property
/// `Stem.ButtonPanelTester.Tests.Property.DictionarySerializationTests`
/// (T021) — `plan.md` Principle II ① "DictionarySerialization".
///
/// `JsonSerializerOptions` is shared at module scope: the type
/// caches per-instance converter resolution state and is not safe
/// to mutate once any `Serialize`/`Deserialize` call has touched
/// it. One instance per process is the documented pattern.
///
/// `FSharp.SystemTextJson`'s `JsonFSharpConverter` (registered
/// globally on `options.Converters`) handles records, options,
/// and lists in one converter. The bare-`System.Text.Json`
/// alternative is hand-rolling a `JsonConverter<_>` per option
/// type and configuring record-property naming manually —
/// mechanical and noisy.
///
/// Numbers are kept on `JsonNumberHandling.Strict` (the BCL
/// default): the JSON wire spec has no `NaN`, `Infinity`, or
/// `-Infinity`, so a `Variable.Min = Some nan` from a peer
/// would already be a malformed payload. The round-trip
/// property in T021 restricts its float generator to finite
/// values to match this domain.
[<RequireQualifiedAccess>]
module DictionaryJson =
    let private options : JsonSerializerOptions =
        let o = JsonSerializerOptions()
        o.Converters.Add(JsonFSharpConverter())
        o

    /// Serialise `d` to a JSON string. Total: every
    /// `ButtonPanelDictionary` value has a unique JSON
    /// representation under `options`. Paired with `fromJson`
    /// for the round-trip property.
    let toJson (d: ButtonPanelDictionary) : string =
        JsonSerializer.Serialize<ButtonPanelDictionary>(d, options)

    /// Parse a JSON string back into a `ButtonPanelDictionary`.
    /// Returns `Error msg` on malformed JSON, shape mismatch, or
    /// a literal `null` payload rather than throwing, so the
    /// caller (T049 `HttpDictionaryProvider`) can translate the
    /// error into a `FetchFailureReason.MalformedPayload` without
    /// `try`/`with` boilerplate. The `null` branch is required by
    /// F# 10 strict nullness (`Deserialize<T>` returns `T | null`
    /// for reference targets) — same DELTA-2 pattern recorded in
    /// T015's RegistrationTypes.
    let fromJson (json: string) : Result<ButtonPanelDictionary, string> =
        try
            match JsonSerializer.Deserialize<ButtonPanelDictionary>(json, options) with
            | null -> Error "payload parsed to JSON null"
            | d -> Ok d
        with
        | :? JsonException as e -> Error e.Message
