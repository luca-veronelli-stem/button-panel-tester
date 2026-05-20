namespace Stem.ButtonPanelTester.Infrastructure.Http

open System
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Stem.ButtonPanelTester.Core.Dictionary

/// Production adapter for `IDictionaryServiceWarmUp`, per
/// `specs/001-fetch-dictionary/phases/phase-7.md` slice 3 (issue #92).
/// Issues a single `GET /health` against the dictionary service's
/// unauthenticated probe endpoint, exposed by
/// `stem-dictionaries-manager` (`Program.cs:86-87`,
/// `ApiKeyMiddleware` allow-list).
///
/// Why `/health`:
///   - **Unauthenticated.** No `X-Api-Key` required — works on fresh,
///     unregistered installs without producing a spurious 401 in
///     the dictionary service's access logs.
///   - **Cold-boot priming.** Azure App Service cold-start is
///     process-level (JIT, DI graph build, `AppDbContext` init).
///     `/health` exercises `AddDbContextCheck<AppDbContext>`, which
///     also primes EF's query plan cache. Any endpoint would warm
///     the worker, but `/health` warms it slightly more thoroughly
///     than a constant-return endpoint would.
///   - **Prior art.** The dictionary service's own deploy workflow
///     uses `/health` for post-deploy cold-start probing.
///
/// Constructor parameters:
///   - `httpClient` — typically the named `"Dictionary"` `HttpClient`
///     from `IHttpClientFactory`. The `ApiKeyAuthHandler` in that
///     pipeline is harmless: registered installs send a meaningless
///     `X-Api-Key` on `/health` (the server ignores it for
///     allow-listed paths); unregistered installs omit the header.
///   - `logger` — required `ILogger<HttpDictionaryServiceWarmUp>`
///     per the STEM LOGGING standard for archetype A.
///
/// Contract:
///   - Returns normally on any HTTP response (2xx, 5xx, `Healthy`,
///     `Unhealthy` — the verdict is irrelevant to cold-boot purposes).
///   - `HttpRequestException` propagates so the orchestration class
///     can decide whether to swallow it.
///   - Internal 90 s timeout (uniform with `HttpDictionaryProvider`
///     and `HttpRegistrationClient`) is enforced via a linked CTS;
///     `OperationCanceledException` propagates when fired by the
///     timeout, distinguishable from caller-`ct` cancellation by the
///     caller via standard token inspection.
type HttpDictionaryServiceWarmUp
    (
        httpClient: HttpClient,
        logger: ILogger<HttpDictionaryServiceWarmUp>
    ) =

    /// Client-side timeout (seconds) for the `/health` probe. Uniform
    /// with the other adapters per the "uniform user expectation"
    /// rule; 90 s absorbs the worst-case cold-boot observed against
    /// `app-dictionaries-manager-prod` (PR #91 diagnostics).
    static member val TimeoutSeconds = 90.0

    interface IDictionaryServiceWarmUp with
        member _.WarmUpAsync(ct: CancellationToken) : Task<unit> = task {
            use request = new HttpRequestMessage(HttpMethod.Get, "health")
            request.Headers.Accept.ParseAdd("application/json")

            use timeoutCts =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(HttpDictionaryServiceWarmUp.TimeoutSeconds)
                )

            use linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token)

            use! response = httpClient.SendAsync(request, linkedCts.Token)
            let status = int response.StatusCode

            logger.LogInformation(
                "Dictionary service /health warm-up returned HTTP {Status}.",
                status
            )

            return ()
        }
