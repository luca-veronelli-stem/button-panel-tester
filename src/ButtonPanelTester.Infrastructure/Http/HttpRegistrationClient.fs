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

/// Wire-side request DTO for `POST /register`. Camel-cased on the
/// wire via the shared `JsonSerializerOptions` below; the F# record
/// field stays PascalCase per `dotnet` style. Kept `private` like
/// `JsonFileDictionaryCache.CacheFile` — `JsonFSharpConverter`
/// (registered on the serializer options) handles F# records without
/// requiring a public parameterless constructor.
[<CLIMutable>]
type private RegistrationRequestDto = { BootstrapToken: string }

/// Wire-side response DTO for `POST /register` 200 OK. Carries the
/// server-issued opaque credential per `contracts/registration-api.md`
/// §"Successful response".
[<CLIMutable>]
type private RegistrationResponseDto = { ApiCredential: string }

/// Production adapter for `IRegistrationClient`, per
/// `specs/001-fetch-dictionary/contracts/ports.md` §IRegistrationClient
/// and `registration-api.md`. Drives `POST {BaseUrl}/register` with
/// the `{ "bootstrapToken": "..." }` body shape, maps the documented
/// HTTP outcomes to `Result<InstallationCredential, RegistrationError>`,
/// and never throws for expected failures (only
/// `OperationCanceledException` when the caller's `ct` is cancelled
/// mid-flight, per the port contract).
///
/// Constructor parameters:
///   - `httpClient` — the `HttpClient` instance (typically resolved
///     from `IHttpClientFactory` at the composition root, T044). The
///     adapter does not configure `BaseAddress` on the client; the
///     `Dictionary:BaseUrl` from `IOptions<DictionaryOptions>` is
///     read per-request so the production wiring and tests can share
///     the same adapter.
///   - `options` — `IOptions<DictionaryOptions>` bound to the
///     `"Dictionary"` section of `appsettings.json` at the GUI
///     composition root. `Value.BaseUrl` supplies the host; `Value.Id`
///     is unused by this adapter (it's the dictionary-fetch URL
///     parameter, consumed by T049 in US3).
///   - `logger` — required `ILogger<HttpRegistrationClient>` per the
///     STEM LOGGING standard for archetype A. Logs successful and
///     failed registration attempts at `Information` and `Warning`
///     respectively. The `BootstrapToken.Value` and `apiCredential`
///     plaintexts never appear at any log level.
///
/// HTTP outcome → `RegistrationError` map (from
/// `registration-api.md` §"Error responses"):
///
/// | HTTP                       | Result                                       |
/// |----------------------------|----------------------------------------------|
/// | 200 OK                     | `Ok (InstallationCredential.Create ...)`      |
/// | 400 Bad Request            | `Error TokenInvalid`                          |
/// | 409 Conflict               | `Error TokenInvalid` (token already consumed) |
/// | any other 4xx              | `Error (RegistrationServerError status)`      |
/// | any 5xx                    | `Error (RegistrationServerError status)`      |
/// | `HttpRequestException`     | `Error (RegistrationNetwork NetworkUnreachable)` |
/// | client timeout (10 s)      | `Error (RegistrationNetwork Timeout)`         |
///
/// Timeout: 10 s, enforced via a linked `CancellationTokenSource`
/// with `CancelAfter`. The caller's `ct` is propagated unmodified;
/// only the timeout side of the linked source produces the `Timeout`
/// error, distinguished from an upstream cancellation by inspecting
/// `ct.IsCancellationRequested` in the catch block.
///
/// Retries: none. The technician is at the keyboard; failed submits
/// surface immediately and the dialog stays open with the inline
/// error message from the `RegistrationError → message` table in the
/// contract.
///
/// Headers:
///   - `Content-Type` is set by `JsonContent.Create` (
///     `application/json; charset=utf-8`).
///   - `Accept: application/json` is added explicitly.
///   - `User-Agent: Stem.ButtonPanelTester/<assemblyVersion>` is
///     added explicitly. `<assemblyVersion>` is read from the
///     `AssemblyInformationalVersionAttribute` on the executing
///     assembly (or `0.0.0` when the attribute is absent — e.g.
///     in fresh local builds without versioning configured).
///   - **No** `X-Api-Key` header. The /register endpoint is the
///     anonymous bootstrap entry point per `registration-api.md`
///     §Endpoint.
type HttpRegistrationClient
    (
        httpClient: HttpClient,
        options: IOptions<DictionaryOptions>,
        logger: ILogger<HttpRegistrationClient>
    ) =

    static let serializerOptions =
        let o = JsonSerializerOptions()
        o.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        o.PropertyNameCaseInsensitive <- true
        // JsonFSharpConverter handles F# records and `[<CLIMutable>]`
        // types regardless of accessor visibility; without it, reflection
        // can't resolve the private parameterless constructor on the
        // wire DTOs.
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

    let buildRequest (token: BootstrapToken) =
        let request = new HttpRequestMessage(HttpMethod.Post, registrationUrl ())
        request.Headers.UserAgent.ParseAdd(userAgentValue)
        request.Headers.Accept.ParseAdd("application/json")
        let body: RegistrationRequestDto = { BootstrapToken = token.Value }
        request.Content <- JsonContent.Create(body, options = serializerOptions)
        request

    interface IRegistrationClient with
        member _.RegisterAsync(token: BootstrapToken, ct: CancellationToken) =
            task {
                use request = buildRequest token

                use timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10.0))
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
                                "Registration succeeded against {Url}.",
                                registrationUrl ()
                            )
                            return Ok(InstallationCredential.Create value.ApiCredential)
                    | 400
                    | 409 ->
                        logger.LogWarning(
                            "Registration rejected with HTTP {Status}.",
                            int response.StatusCode
                        )
                        return Error TokenInvalid
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
                    logger.LogWarning("Registration timed out after 10 s.")
                    return Error(RegistrationNetwork Timeout)
                | :? HttpRequestException as ex ->
                    logger.LogWarning(ex, "Registration network failure.")
                    return Error(RegistrationNetwork NetworkUnreachable)
            }
