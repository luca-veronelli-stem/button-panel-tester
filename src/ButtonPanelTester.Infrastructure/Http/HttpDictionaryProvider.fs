namespace Stem.ButtonPanelTester.Infrastructure.Http

open System
open System.Net.Http
open System.Reflection
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Options
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Dictionary

/// Wire-side variable DTO. Mirrors `Core.Dictionary.Variable`
/// field-for-field but uses CLR-nullable shapes
/// (`string | null`, `Nullable<float>`) rather than F# `option`
/// so missing-or-null JSON values fall out of the default
/// `System.Text.Json` record-binder. `[<CLIMutable>]` adds the
/// parameterless constructor the binder needs; non-private
/// visibility (default `public` in F#) is required because
/// the binder rejects non-public constructors.
[<CLIMutable>]
type VariableDto = {
    Name: string
    AddressHigh: byte
    AddressLow: byte
    DataType: string
    Access: string
    Description: string | null
    Min: System.Nullable<float>
    Max: System.Nullable<float>
    Unit: string | null
    IsStandard: bool
}

/// Wire-side DTO for the `GET /api/dictionaries/{id}/resolved`
/// response, per `contracts/dictionary-api.md` lines 62-77. One
/// `PanelType` on the wire; the consumer wraps it as
/// `PanelTypes = [pt]` to keep `ButtonPanelDictionary.PanelTypes`
/// a list (forward-compat without changing the contract this
/// slice consumes).
[<CLIMutable>]
type DictionaryResolvedDto = {
    Id: int
    Name: string
    Description: string | null
    Variables: VariableDto[] | null
}

/// Production adapter for `IDictionaryProvider`, per
/// `specs/001-fetch-dictionary/contracts/dictionary-api.md` and
/// `research.md` R3 (`ContentHash` canonicalisation). Drives
/// `GET {BaseUrl}/api/dictionaries/{Dictionary:Id}/resolved`, maps
/// the documented HTTP status codes to `DictionaryFetchResult`, and
/// never throws for expected failures (only
/// `OperationCanceledException` from the caller's `ct` is permitted
/// to leak out, per the `IDictionaryProvider` port contract).
///
/// `X-Api-Key` is **not** attached here — the composition root
/// (T051) wires an `ApiKeyAuthHandler` `DelegatingHandler` onto the
/// named `"Dictionary"` `HttpClient` so every request carries the
/// header sourced from `ICredentialStore.LoadAsync`. The
/// integration test (T054) exercises both the handler and this
/// provider in one pipeline so the assertion that `X-Api-Key:
/// <credential>` is on the wire reflects the production path.
///
/// Constructor parameters:
///   - `httpClient` — the `HttpClient` resolved from
///     `IHttpClientFactory` with the `"Dictionary"` name at the
///     composition root (T051). `BaseAddress` is set there to
///     `IOptions<DictionaryOptions>.Value.BaseUrl`; the
///     `ApiKeyAuthHandler` lives in the same client's pipeline.
///   - `options` — `IOptions<DictionaryOptions>` bound to the
///     `"Dictionary"` section of `appsettings.json`. Read for
///     `Dictionary:Id` only; `BaseUrl` flows through the client.
///   - `clock` — `IClock` port; `UtcNow()` stamps `Success(_,
///     fetchedAt)` on every successful fetch.
///   - `logger` — required `ILogger<HttpDictionaryProvider>` per
///     the STEM LOGGING standard for archetype A. Logs successful
///     and failed fetch attempts at `Information` / `Warning`. The
///     `InstallationCredential.Value` never appears at any log
///     level — the handler is the only consumer.
///
/// HTTP outcome → `DictionaryFetchResult` map (from
/// `dictionary-api.md` §"Error responses"):
///
/// | HTTP | Result |
/// |---|---|
/// | 200 | `Success(dict, clock.UtcNow())` |
/// | 400 | `Failed(MalformedPayload, Some detail)` |
/// | 401 | `Failed(Unauthorized, Some detail)` |
/// | 404 | `Failed(NotFound, None)` |
/// | 5xx | `Failed(ServerError, Some httpStatus)` |
/// | other 4xx | `Failed(ServerError, Some httpStatus)` |
/// | `HttpRequestException` | `Failed(NetworkUnreachable, Some ex.Message)` |
/// | client timeout (10 s) | `Failed(Timeout, None)` |
/// | body present but does not deserialise | `Failed(MalformedPayload, Some ex.Message)` |
type HttpDictionaryProvider
    (
        httpClient: HttpClient,
        options: IOptions<DictionaryOptions>,
        clock: IClock,
        logger: ILogger<HttpDictionaryProvider>
    ) =

    /// Wire-side deserialisation options. `CamelCase` matches the
    /// server (lines 38-58 of `dictionary-api.md`); plain
    /// `System.Text.Json` (no `JsonFSharpConverter`) because the
    /// wire DTOs use CLR-nullable shapes (`string | null`,
    /// `Nullable<float>`, `VariableDto[] | null`) rather than F#
    /// `option` — missing-or-null JSON values fall out of the
    /// default record-binder. The `toDomain` mapper translates
    /// the nullable wire shape into the domain `option` types
    /// afterwards. One instance per process — the type caches
    /// converter-resolution state and is not safe to mutate
    /// post-first-use.
    static let serializerOptions =
        let o = JsonSerializerOptions()
        o.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        o.PropertyNameCaseInsensitive <- true
        o

    /// Canonical hashing options — `WriteIndented = false`, no
    /// whitespace, no escaping policy that depends on the host's
    /// locale. Per `research.md` R3 the hash is over the
    /// canonicalised re-serialisation of the deserialised
    /// `PanelType list`, NOT over the server's raw body — so
    /// server-side whitespace and field-order changes do not
    /// perturb the resulting hash. `JsonFSharpConverter` is
    /// required here because the input is the domain
    /// `PanelType list` (F# records with `option` fields).
    static let hashOptions =
        let o = JsonSerializerOptions()
        o.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        o.WriteIndented <- false
        o.Converters.Add(JsonFSharpConverter())
        o

    static let assemblyVersion () =
        let asm = Assembly.GetExecutingAssembly()

        match asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>() with
        | null -> "0.0.0"
        | attr -> attr.InformationalVersion

    static let userAgentValue = "Stem.ButtonPanelTester/" + assemblyVersion ()

    /// Relative URL — `BaseAddress` on the `HttpClient` carries
    /// the scheme/host/port (composition root T051) so the
    /// adapter only contributes the path. `dictionary-api.md`
    /// names `/api/dictionaries/{id}/resolved`; the leading
    /// slash is omitted so `HttpClient.BaseAddress` (which is
    /// required to end with `/`) concatenates correctly.
    let relativeUrl () : string =
        sprintf "api/dictionaries/%d/resolved" options.Value.Id

    /// Compute the canonical content hash over the deserialised
    /// `PanelType list`. The cache (`JsonFileDictionaryCache`)
    /// transports this verbatim through the
    /// `.sha256`-sidecar + cache-file envelope, so the
    /// in-memory and on-disk `ContentHash` round-trip byte-for-byte
    /// — the precondition for `cache_memory_equal_post_first_success`
    /// (T027) and the FR-010 property in T057.
    let computeContentHash (panelTypes: PanelType list) : string =
        let canonicalJson = JsonSerializer.Serialize<PanelType list>(panelTypes, hashOptions)
        let bytes = Encoding.UTF8.GetBytes(canonicalJson)
        ContentHash.compute bytes

    let stringOpt (raw: string | null) : string option =
        match raw with
        | null -> None
        | s -> Some s

    let floatOpt (raw: System.Nullable<float>) : float option =
        if raw.HasValue then Some raw.Value else None

    let toDomainVariable (v: VariableDto) : Variable = {
        Name = v.Name
        AddressHigh = v.AddressHigh
        AddressLow = v.AddressLow
        DataType = v.DataType
        Access = v.Access
        Description = stringOpt v.Description
        Min = floatOpt v.Min
        Max = floatOpt v.Max
        Unit = stringOpt v.Unit
        IsStandard = v.IsStandard
    }

    /// Map the deserialised wire DTO into a domain
    /// `ButtonPanelDictionary`. The wire carries one `PanelType`;
    /// the consumer wraps it as a single-element list per
    /// `dictionary-api.md` line 80.
    let toDomain (dto: DictionaryResolvedDto) : ButtonPanelDictionary =
        let variables =
            match dto.Variables with
            | null -> []
            | arr -> arr |> Array.map toDomainVariable |> Array.toList

        let panel : PanelType = {
            Id = dto.Id
            Name = dto.Name
            Description = stringOpt dto.Description
            Variables = variables
        }
        let panelTypes = [ panel ]
        let hash = computeContentHash panelTypes
        { ContentHash = hash; PanelTypes = panelTypes }

    /// Read the response body as UTF-8 bytes, deserialise via
    /// `JsonFSharpConverter`, and convert to the domain
    /// `ButtonPanelDictionary`. Returns `Failed(MalformedPayload,
    /// Some msg)` on any JsonException, truncated body, or a
    /// literal `null` payload (System.Text.Json's
    /// `Deserialize<T>` returns `T | null` for reference targets;
    /// F# 10 strict nullness surfaces that). Total: never throws
    /// `JsonException` to the caller.
    let parseSuccessBody (response: HttpResponseMessage) (ct: CancellationToken)
        : Task<DictionaryFetchResult> = task {
        try
            let! body = response.Content.ReadAsStringAsync(ct)

            match JsonSerializer.Deserialize<DictionaryResolvedDto>(body, serializerOptions) with
            | null ->
                return Failed(MalformedPayload, Some "response body parsed to JSON null")
            | dto ->
                let dict = toDomain dto
                let fetchedAt = clock.UtcNow()
                return Success(dict, fetchedAt)
        with
        | :? JsonException as ex ->
            return Failed(MalformedPayload, Some ex.Message)
    }

    /// Read up to ~512 bytes of the failure body for diagnostics
    /// and the `Detail` payload on `Failed(_, Some _)`. Bounded
    /// because the server's error bodies are short by contract;
    /// reading the full stream on an arbitrary 5xx wastes bytes
    /// and time. Total: any read failure yields `None`.
    let readErrorDetail (response: HttpResponseMessage) (ct: CancellationToken)
        : Task<string option> = task {
        try
            let! body = response.Content.ReadAsStringAsync(ct)
            let trimmed = if body.Length > 512 then body.Substring(0, 512) else body
            if String.IsNullOrWhiteSpace(trimmed) then return None
            else return Some (trimmed.Trim())
        with _ ->
            return None
    }

    interface IDictionaryProvider with
        member _.FetchAsync(ct: CancellationToken) : Task<DictionaryFetchResult> = task {
            use request = new HttpRequestMessage(HttpMethod.Get, relativeUrl ())
            request.Headers.UserAgent.ParseAdd(userAgentValue)
            request.Headers.Accept.ParseAdd("application/json")

            use timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10.0))
            use linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token)

            try
                use! response = httpClient.SendAsync(request, linkedCts.Token)

                match int response.StatusCode with
                | 200 ->
                    let! result = parseSuccessBody response ct

                    match result with
                    | Success(_, fetchedAt) ->
                        logger.LogInformation(
                            "Dictionary fetch succeeded against {Url}; fetchedAt={FetchedAt:O}.",
                            relativeUrl (),
                            fetchedAt
                        )

                        return result
                    | Failed(reason, detail) ->
                        let detailText =
                            match detail with
                            | Some s -> s
                            | None -> ""

                        logger.LogWarning(
                            "Dictionary fetch returned 200 but body did not deserialise: {Reason} ({Detail}).",
                            reason,
                            detailText
                        )

                        return result
                | 400 ->
                    let! detail = readErrorDetail response ct
                    logger.LogWarning("Dictionary fetch rejected with HTTP 400 (client-side bug): {Detail}.", detail)
                    return Failed(MalformedPayload, detail)
                | 401 ->
                    let! detail = readErrorDetail response ct
                    logger.LogWarning("Dictionary fetch rejected with HTTP 401 (credential invalid or revoked).")
                    return Failed(Unauthorized, detail)
                | 404 ->
                    logger.LogWarning(
                        "Dictionary fetch returned 404 — configured Dictionary:Id={Id} does not exist on this server.",
                        options.Value.Id
                    )
                    return Failed(NotFound, None)
                | status when status >= 500 ->
                    let! detail = readErrorDetail response ct
                    logger.LogWarning("Dictionary fetch failed with HTTP {Status}.", status)
                    let payload =
                        match detail with
                        | Some s -> Some (sprintf "HTTP %d: %s" status s)
                        | None -> Some (sprintf "HTTP %d" status)
                    return Failed(ServerError, payload)
                | status ->
                    // Any other 4xx that isn't 400 / 401 / 404 lands here —
                    // the server documents none today, but `dictionary-api.md`
                    // "any other 4xx/5xx → ServerError" is the catch-all.
                    let! detail = readErrorDetail response ct
                    logger.LogWarning("Dictionary fetch returned unexpected HTTP {Status}.", status)
                    let payload =
                        match detail with
                        | Some s -> Some (sprintf "HTTP %d: %s" status s)
                        | None -> Some (sprintf "HTTP %d" status)
                    return Failed(ServerError, payload)
            with
            // Timeout side of the linked source fired before the caller's
            // `ct` did — surface as `Failed(Timeout, _)` rather than
            // letting the exception escape. When `ct` itself was cancelled
            // by the caller the guard fails and the exception propagates
            // per the `IDictionaryProvider` port contract (only
            // `OperationCanceledException` from the caller's `ct` may
            // leak out).
            | :? OperationCanceledException when not ct.IsCancellationRequested ->
                logger.LogWarning("Dictionary fetch timed out after 10 s.")
                return Failed(Timeout, None)
            | :? HttpRequestException as ex ->
                logger.LogWarning(ex, "Dictionary fetch network failure.")
                return Failed(NetworkUnreachable, Some ex.Message)
        }
