namespace Stem.ButtonPanelTester.Infrastructure.Http

open System
open System.Net.Http
open System.Net.Http.Json
open System.Reflection
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Options
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Dictionary

/// Wire-side descriptor sub-object on the request. F# record kept
/// `private` to the file — `JsonFSharpConverter` on the shared
/// serializer options handles the parameterless-constructor visibility
/// concern. Field names are PascalCase here; the serializer's camelCase
/// policy emits the on-wire `clientApp`, `osUserId`, etc.
[<CLIMutable>]
type private RegistrationDescriptorDto = {
    ClientApp: string
    OsUserId: string
    MachineId: string
    InstallGuid: Guid
    AppVersion: string option
}

/// Wire-side request DTO for `POST /register` per
/// `contracts/registration-api.md` "Request body".
[<CLIMutable>]
type private RegistrationRequestDto = {
    BootstrapToken: string
    Descriptor: RegistrationDescriptorDto
}

/// Wire-side response DTO for `POST /register` 200 OK.
/// `InstallationId` and `IssuedAt` are not consumed in v1 but are
/// captured so the deserializer doesn't drop them (forward-compat
/// + observable in logs if needed).
[<CLIMutable>]
type private RegistrationResponseDto = {
    InstallationId: int
    ApiCredential: string
    IssuedAt: DateTimeOffset
}

/// Wire-side failure envelope. `error` is a short developer hint
/// per the server's contract; we read it for the `DescriptorRejected
/// detail` body and for log diagnostics but never display it to the
/// technician. The field is `string | null` because `[<CLIMutable>]`
/// records initialize string fields to `null` if the JSON body
/// omits the property, and F# 10 strict nullness exposes that here.
[<CLIMutable>]
type private RegistrationErrorDto = { Error: string | null }

/// Production adapter for `IRegistrationClient`, per
/// `specs/001-fetch-dictionary/contracts/registration-api.md`
/// (aligned with `stem-dictionaries-manager` v0.7.0). Drives
/// `POST {BaseUrl}/register` with the
/// `{ bootstrapToken, descriptor: { clientApp, osUserId, machineId,
/// installGuid, appVersion? } }` body shape, maps the documented
/// HTTP status codes to `Result<InstallationCredential,
/// RegistrationError>`, and never throws for expected failures (only
/// `OperationCanceledException` from the caller's `ct` is permitted
/// to leak out, per the port contract).
///
/// Constructor parameters:
///   - `httpClient` — the `HttpClient` instance (typically resolved
///     from `IHttpClientFactory` at the composition root, T044).
///     The adapter does not configure `BaseAddress` on the client;
///     the `Dictionary:BaseUrl` from `IOptions<DictionaryOptions>`
///     is read per-request.
///   - `options` — `IOptions<DictionaryOptions>` bound to the
///     `"Dictionary"` section of `appsettings.json`.
///   - `descriptorProvider` — `IInstallationDescriptorProvider`
///     resolved from the composition root (production binding is
///     `Infrastructure.Auth.InstallationDescriptorProvider`). Carries
///     the hashed `osUserId` + `machineId` (FR-020), the persisted
///     `installGuid`, and the SemVer 2.0 `appVersion`. The provider's
///     `Current()` is called once per `RegisterAsync` so the
///     Re-Register flow (issue #98) can rotate the `installGuid`
///     between attempts within a single process lifetime.
///   - `logger` — required `ILogger<HttpRegistrationClient>` per
///     the STEM LOGGING standard for archetype A. Logs successful
///     and failed registration attempts at `Information` /
///     `Warning`. The `BootstrapToken.Value`, the issued
///     `apiCredential` plaintext, and the raw values that produced
///     the hashed descriptor identifiers never appear at any log
///     level.
///
/// HTTP outcome → `RegistrationError` map (from
/// `registration-api.md` §"Status → RegistrationError map"):
///
/// | HTTP | Result |
/// |---|---|
/// | 200 | `Ok (InstallationCredential.Create response.apiCredential)` |
/// | 400 | `Error (DescriptorRejected response.error)` (server's `error` body verbatim for log diagnostics) |
/// | 401 | `Error TokenInvalid` (server-side conflated cause: unknown/scope/policy-miss) |
/// | 409 | `Error TokenAlreadyUsed` |
/// | 410 | `Error TokenExpired` |
/// | 423 | `Error TokenRevoked` |
/// | other 5xx | `Error (RegistrationServerError httpStatus)` |
/// | `HttpRequestException` | `Error (RegistrationNetwork NetworkUnreachable)` |
/// | client timeout (90 s, `TimeoutSeconds`) | `Error (RegistrationNetwork Timeout)` |
///
/// Uniform with `HttpDictionaryProvider.TimeoutSeconds` per the
/// "uniform user expectation" rule in
/// `contracts/registration-api.md` §"Timeout and retries"; raised
/// from 10 s to 90 s in `phases/phase-7.md` (issue #92) to absorb
/// Azure App Service Free-tier cold-start latency. The result
/// mapping is unchanged.
type HttpRegistrationClient
    (
        httpClient: HttpClient,
        options: IOptions<DictionaryOptions>,
        descriptorProvider: IInstallationDescriptorProvider,
        logger: ILogger<HttpRegistrationClient>
    ) =

    static let serializerOptions =
        let o = JsonSerializerOptions()
        o.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        o.PropertyNameCaseInsensitive <- true
        // JsonFSharpConverter handles F# records, options, and
        // `[<CLIMutable>]` types regardless of accessor visibility;
        // without it, reflection cannot resolve the private
        // parameterless constructor on the wire DTOs, and `string option`
        // does not round-trip through System.Text.Json by default.
        o.Converters.Add(JsonFSharpConverter())
        o

    static let assemblyVersion () =
        let asm = Assembly.GetExecutingAssembly()

        match asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>() with
        | null -> "0.0.0"
        | attr -> attr.InformationalVersion

    static let userAgentValue = "Stem.ButtonPanelTester/" + assemblyVersion ()

    let registrationUrl () =
        let baseUrl = options.Value.BaseUrl
        baseUrl.TrimEnd('/') + "/register"

    let descriptorDto () : RegistrationDescriptorDto =
        // Read the descriptor per call so a `ResetInstallGuid()` from
        // the Re-Register flow (issue #98) is observed by the next
        // `RegisterAsync` — the provider's `Current()` re-reads the
        // `install.guid` sidecar each call while keeping the hashed
        // identifiers cached at construction.
        let descriptor = descriptorProvider.Current()
        {
            ClientApp = descriptor.ClientApp
            OsUserId = descriptor.OsUserId
            MachineId = descriptor.MachineId
            InstallGuid = descriptor.InstallGuid
            AppVersion = descriptor.AppVersion
        }

    let buildRequest (token: BootstrapToken) =
        let request = new HttpRequestMessage(HttpMethod.Post, registrationUrl ())
        request.Headers.UserAgent.ParseAdd(userAgentValue)
        request.Headers.Accept.ParseAdd("application/json")
        let body: RegistrationRequestDto = {
            BootstrapToken = token.Value
            Descriptor = descriptorDto ()
        }
        request.Content <- JsonContent.Create(body, options = serializerOptions)
        request

    let readErrorDetail (response: HttpResponseMessage) (ct: CancellationToken) =
        task {
            try
                let! dto =
                    response.Content.ReadFromJsonAsync<RegistrationErrorDto>(
                        serializerOptions,
                        ct
                    )

                match dto with
                | null -> return ""
                | value ->
                    match value.Error with
                    | null -> return ""
                    | s -> return s
            with _ ->
                return ""
        }

    /// Client-side timeout (seconds) for the registration request.
    /// Mirrors `HttpDictionaryProvider.TimeoutSeconds` per the
    /// "uniform user expectation" rule in
    /// `contracts/registration-api.md` §"Timeout and retries".
    static member val TimeoutSeconds = 90.0

    interface IRegistrationClient with
        member _.RegisterAsync(token: BootstrapToken, ct: CancellationToken) =
            task {
                use request = buildRequest token

                use timeoutCts =
                    new CancellationTokenSource(
                        TimeSpan.FromSeconds(HttpRegistrationClient.TimeoutSeconds)
                    )
                use linkedCts =
                    CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token)

                try
                    use! response = httpClient.SendAsync(request, linkedCts.Token)

                    match int response.StatusCode with
                    | 200 ->
                        let! dto =
                            response.Content.ReadFromJsonAsync<RegistrationResponseDto>(
                                serializerOptions,
                                ct
                            )

                        match dto with
                        | null ->
                            logger.LogWarning(
                                "POST /register returned 200 with a body that parsed to JSON null."
                            )
                            return Error(RegistrationServerError 200)
                        | value ->
                            logger.LogInformation(
                                "Registration succeeded against {Url}; installationId={InstallationId}.",
                                registrationUrl (),
                                value.InstallationId
                            )
                            return Ok(InstallationCredential.Create value.ApiCredential)
                    | 400 ->
                        let! detail = readErrorDetail response ct

                        logger.LogWarning(
                            "Registration rejected by server with HTTP 400 (client-side bug): {Detail}",
                            detail
                        )

                        return Error(DescriptorRejected detail)
                    | 401 ->
                        logger.LogWarning(
                            "Registration rejected with HTTP 401 (token unknown OR scope mismatch OR policy-lookup miss)."
                        )

                        return Error TokenInvalid
                    | 409 ->
                        logger.LogWarning(
                            "Registration rejected with HTTP 409: token already used."
                        )

                        return Error TokenAlreadyUsed
                    | 410 ->
                        logger.LogWarning(
                            "Registration rejected with HTTP 410: token expired."
                        )

                        return Error TokenExpired
                    | 423 ->
                        logger.LogWarning(
                            "Registration rejected with HTTP 423: token revoked."
                        )

                        return Error TokenRevoked
                    | status ->
                        logger.LogWarning(
                            "Registration failed with HTTP {Status}.",
                            status
                        )

                        return Error(RegistrationServerError status)
                with
                // Only catch OperationCanceledException when the timeout side
                // of the linked source fired. When `ct` itself was cancelled by
                // the caller, the guard fails and the exception propagates per
                // the port contract (only `OperationCanceledException` from the
                // caller's `ct` is permitted to leak out).
                | :? OperationCanceledException when not ct.IsCancellationRequested ->
                    logger.LogWarning(
                        "Registration timed out after {TimeoutSeconds} s.",
                        HttpRegistrationClient.TimeoutSeconds
                    )
                    return Error(RegistrationNetwork Timeout)
                | :? HttpRequestException as ex ->
                    logger.LogWarning(ex, "Registration network failure.")
                    return Error(RegistrationNetwork NetworkUnreachable)
            }
