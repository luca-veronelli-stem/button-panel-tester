module Stem.ButtonPanelTester.Tests.Windows.Integration.HttpDictionaryProviderTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
open Microsoft.Extensions.Options
open Xunit
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Infrastructure.Http
open Stem.ButtonPanelTester.Services.Dictionary

/// Integration tests for the production `IDictionaryProvider` adapter
/// per `specs/001-fetch-dictionary/contracts/dictionary-api.md` and
/// `phase-5.md` §T054. Each `[<Fact>]` constructs the adapter against
/// a stubbed `HttpMessageHandler` chained behind an `ApiKeyAuthHandler`
/// so the request observed at the bottom of the pipeline reflects the
/// production composition (T051 wires the same handler onto the
/// `"Dictionary"` named `HttpClient`). Lives in `Tests.Windows` (per
/// #76) because the provider ships in `ButtonPanelTester.Infrastructure`
/// (net10.0-windows for DPAPI co-location).

// --- stub handler + helpers ---

type private StubHttpHandler(handler: HttpRequestMessage -> Task<HttpResponseMessage>) =
    inherit HttpMessageHandler()

    let mutable lastRequest: HttpRequestMessage option = None
    let mutable lastXApiKey: string option = None
    let mutable callCount = 0

    member _.LastRequest = lastRequest
    member _.LastXApiKey = lastXApiKey
    member _.CallCount = callCount

    override _.SendAsync
        (
            request: HttpRequestMessage,
            _: CancellationToken
        ) : Task<HttpResponseMessage> =
        task {
            callCount <- callCount + 1
            lastRequest <- Some request

            match request.Headers.TryGetValues("X-Api-Key") with
            | true, values ->
                lastXApiKey <-
                    match values with
                    | null -> None
                    | vs -> vs |> Seq.tryHead
            | false, _ -> lastXApiKey <- None

            return! handler request
        }

/// Single-cell stub `ICredentialStore` for tests — production
/// counterpart is `Infrastructure.Persistence.DpapiCredentialStore`;
/// the `InMemoryCredentialStore` shipped in `Tests/Fakes/Wiring.fs`
/// is `net10.0` and not visible to this `net10.0-windows` test
/// project, so the stub is duplicated locally rather than dragged
/// across the TFM boundary.
type private StubCredentialStore(initial: InstallationCredential option) =
    let mutable value = initial

    interface ICredentialStore with
        member _.ExistsAsync(_: CancellationToken) = task { return value.IsSome }
        member _.LoadAsync(_: CancellationToken) = task { return value }
        member _.SaveAsync(c: InstallationCredential, _: CancellationToken) =
            task { value <- Some c }
        member _.DeleteAsync(_: CancellationToken) = task { value <- None }

let private optionsFor (id: int) : IOptions<DictionaryOptions> =
    Options.Create({ BaseUrl = "https://stub.example/"; Id = id })

let private aCredential () =
    InstallationCredential.Create "STEM-BT-DEV-KEY-2026"

let private makeJsonResponse (status: HttpStatusCode) (body: string) =
    let response = new HttpResponseMessage(status)
    response.Content <- new StringContent(body, Encoding.UTF8, "application/json")
    response

let private makeEmptyResponse (status: HttpStatusCode) =
    let response = new HttpResponseMessage(status)
    response.Content <- new StringContent("", Encoding.UTF8, "application/json")
    response

let private statusHandler (status: int) (errorBody: string) =
    fun (_: HttpRequestMessage) ->
        let body = sprintf "{\"error\":\"%s\"}" errorBody
        Task.FromResult(makeJsonResponse (enum<HttpStatusCode> status) body)

let private throwingHandler (ex: exn) =
    fun (_: HttpRequestMessage) -> Task.FromException<HttpResponseMessage>(ex)

/// Build a fully-wired pipeline: `ApiKeyAuthHandler` over
/// `StubHttpHandler`, on an `HttpClient` with the contracted
/// `BaseAddress`. Mirrors the production composition (T051) so a
/// single test exercises both the auth handler and the provider.
let private makeClient (handler: StubHttpHandler) =
    let credentialStore = StubCredentialStore(Some(aCredential ())) :> ICredentialStore
    let authHandler = new ApiKeyAuthHandler(credentialStore, NullLogger<ApiKeyAuthHandler>.Instance)
    authHandler.InnerHandler <- handler
    let httpClient = new HttpClient(authHandler, disposeHandler = false)
    httpClient.BaseAddress <- Uri "https://stub.example/"
    let options = optionsFor 2
    let clock = { new IClock with member _.UtcNow() = DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero) }
    let logger = NullLogger<HttpDictionaryProvider>.Instance

    HttpDictionaryProvider(httpClient, options, clock, logger)
    :> IDictionaryProvider

let private fixtureBody () : string =
    // `Fixtures/DictionaryResolvedDto.json` is the T020 shared fixture;
    // both test projects copy it next to the assembly (`Content Include`
    // with `Link` in the Tests.Windows fsproj).
    let path =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "DictionaryResolvedDto.json")

    File.ReadAllText(path)

// --- tests: success path ---

[<Fact>]
let FetchAsync_200_ReturnsSuccessWithDeserialisedDictionary () =
    task {
        let fixture = fixtureBody ()

        use handler =
            new StubHttpHandler(fun _ ->
                Task.FromResult(makeJsonResponse HttpStatusCode.OK fixture))

        let client = makeClient handler

        let! result = client.FetchAsync(CancellationToken.None)

        match result with
        | Success(dict, fetchedAt) ->
            Assert.Single<PanelType>(dict.PanelTypes) |> ignore
            let panel = List.head dict.PanelTypes
            Assert.Equal(2, panel.Id)
            Assert.Equal("Pulsantiere", panel.Name)
            Assert.Equal(3, List.length panel.Variables)
            // ContentHash is a 64-char lowercase hex string per
            // `ContentHash.compute`; we don't assert the exact
            // value because the canonicalisation policy lives in
            // the provider and pinning the value here would couple
            // the test to that implementation detail.
            Assert.Equal(64, dict.ContentHash.Length)
            Assert.True(dict.ContentHash |> Seq.forall (fun c ->
                (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
            Assert.Equal(DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero), fetchedAt)
        | Failed(reason, detail) ->
            Assert.Fail(sprintf "expected Success, got Failed(%A, %A)" reason detail)
    }

// --- tests: failure-status mapping ---

[<Fact>]
let FetchAsync_400_ReturnsFailedMalformedPayload () =
    task {
        use handler = new StubHttpHandler(statusHandler 400 "Use /api/dictionaries/standard for the standard dictionary.")
        let client = makeClient handler

        let! result = client.FetchAsync(CancellationToken.None)

        match result with
        | Failed(MalformedPayload, _) -> ()
        | other -> Assert.Fail(sprintf "expected Failed(MalformedPayload, _), got %A" other)
    }

[<Fact>]
let FetchAsync_401_ReturnsFailedUnauthorized () =
    task {
        use handler = new StubHttpHandler(statusHandler 401 "API key missing or invalid.")
        let client = makeClient handler

        let! result = client.FetchAsync(CancellationToken.None)

        match result with
        | Failed(Unauthorized, _) -> ()
        | other -> Assert.Fail(sprintf "expected Failed(Unauthorized, _), got %A" other)
    }

[<Fact>]
let FetchAsync_404_ReturnsFailedNotFound () =
    task {
        use handler =
            new StubHttpHandler(fun _ ->
                Task.FromResult(makeEmptyResponse HttpStatusCode.NotFound))

        let client = makeClient handler

        let! result = client.FetchAsync(CancellationToken.None)

        match result with
        | Failed(NotFound, None) -> ()
        | other -> Assert.Fail(sprintf "expected Failed(NotFound, None), got %A" other)
    }

[<Fact>]
let FetchAsync_503_ReturnsFailedServerError () =
    task {
        use handler = new StubHttpHandler(statusHandler 503 "Database unavailable.")
        let client = makeClient handler

        let! result = client.FetchAsync(CancellationToken.None)

        match result with
        | Failed(ServerError, _) -> ()
        | other -> Assert.Fail(sprintf "expected Failed(ServerError, _), got %A" other)
    }

[<Fact>]
let FetchAsync_200_TruncatedBody_ReturnsFailedMalformedPayload () =
    // Server returned 200 but the body is invalid JSON (truncated
    // mid-string). The provider must catch `JsonException` and
    // surface `Failed(MalformedPayload, _)` rather than throwing.
    task {
        let truncated = "{\"id\":2,\"name\":\"Pulsantiere\","
        use handler =
            new StubHttpHandler(fun _ ->
                Task.FromResult(makeJsonResponse HttpStatusCode.OK truncated))

        let client = makeClient handler

        let! result = client.FetchAsync(CancellationToken.None)

        match result with
        | Failed(MalformedPayload, Some _) -> ()
        | other -> Assert.Fail(sprintf "expected Failed(MalformedPayload, Some _), got %A" other)
    }

[<Fact>]
let FetchAsync_NetworkException_ReturnsFailedNetworkUnreachable () =
    task {
        use handler =
            new StubHttpHandler(throwingHandler (HttpRequestException("simulated DNS failure")))

        let client = makeClient handler

        let! result = client.FetchAsync(CancellationToken.None)

        match result with
        | Failed(NetworkUnreachable, Some _) -> ()
        | other -> Assert.Fail(sprintf "expected Failed(NetworkUnreachable, Some _), got %A" other)
    }

[<Fact>]
let FetchAsync_TaskCanceledFromInsideHandler_ReturnsFailedTimeout () =
    // Mirrors the HttpRegistrationClient timeout test (T046): the
    // adapter's internal 10 s timeout is enforced via a linked CTS.
    // The stub simulates that timeout by throwing
    // `TaskCanceledException` directly; the caller's `ct` is never
    // cancelled, so the guarded catch routes the exception to
    // `Failed(Timeout, _)`.
    task {
        use handler =
            new StubHttpHandler(throwingHandler (new TaskCanceledException("simulated timeout")))

        let client = makeClient handler

        let! result = client.FetchAsync(CancellationToken.None)

        match result with
        | Failed(Timeout, None) -> ()
        | other -> Assert.Fail(sprintf "expected Failed(Timeout, None), got %A" other)
    }

// --- tests: wire shape (request carries X-Api-Key + Accept; URL) ---

[<Fact>]
let FetchAsync_RequestCarriesXApiKeyHeader () =
    task {
        let fixture = fixtureBody ()

        use handler =
            new StubHttpHandler(fun _ ->
                Task.FromResult(makeJsonResponse HttpStatusCode.OK fixture))

        let client = makeClient handler

        let! _ = client.FetchAsync(CancellationToken.None)

        Assert.Equal(Some "STEM-BT-DEV-KEY-2026", handler.LastXApiKey)
    }

[<Fact>]
let FetchAsync_RequestCarriesAcceptApplicationJson () =
    task {
        let fixture = fixtureBody ()

        use handler =
            new StubHttpHandler(fun _ ->
                Task.FromResult(makeJsonResponse HttpStatusCode.OK fixture))

        let client = makeClient handler

        let! _ = client.FetchAsync(CancellationToken.None)

        let request =
            match handler.LastRequest with
            | Some r -> r
            | None ->
                Assert.Fail("expected the stub to have observed a request")
                Unchecked.defaultof<_>

        let acceptStrings =
            request.Headers.Accept
            |> Seq.choose (fun a ->
                match a.MediaType with
                | null -> None
                | s -> Some s)
            |> Seq.toList

        Assert.Contains("application/json", acceptStrings)
    }

[<Fact>]
let FetchAsync_GetsToResolvedPathWithConfiguredId () =
    task {
        let fixture = fixtureBody ()

        use handler =
            new StubHttpHandler(fun _ ->
                Task.FromResult(makeJsonResponse HttpStatusCode.OK fixture))

        let client = makeClient handler

        let! _ = client.FetchAsync(CancellationToken.None)

        let request =
            match handler.LastRequest with
            | Some r -> r
            | None ->
                Assert.Fail("expected the stub to have observed a request")
                Unchecked.defaultof<_>

        Assert.Equal(HttpMethod.Get, request.Method)

        let uri =
            match request.RequestUri with
            | null ->
                Assert.Fail("expected a request URI")
                Unchecked.defaultof<_>
            | u -> u

        Assert.Equal("/api/dictionaries/2/resolved", uri.AbsolutePath)
    }

[<Fact>]
let FetchAsync_NoCredentialOnDisk_OmitsXApiKey () =
    // Re-register / cold-start path: ICredentialStore.LoadAsync returns
    // None. The handler must forward the request WITHOUT X-Api-Key
    // rather than short-circuit — the server's 401 is the source of
    // truth for "no credential ⇒ Unauthorized", and the provider's
    // 401 → Unauthorized mapping plus the FR-018 Re-register affordance
    // (T052) reads off that response. This test wires the handler
    // pipeline directly to bypass the helper that auto-seeds a
    // credential.
    task {
        let credentialStore = StubCredentialStore(None) :> ICredentialStore
        let authHandler = new ApiKeyAuthHandler(credentialStore, NullLogger<ApiKeyAuthHandler>.Instance)
        use inner =
            new StubHttpHandler(statusHandler 401 "API key missing or invalid.")
        authHandler.InnerHandler <- inner
        use httpClient = new HttpClient(authHandler, disposeHandler = false)
        httpClient.BaseAddress <- Uri "https://stub.example/"

        let clock = { new IClock with member _.UtcNow() = DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero) }
        let client =
            HttpDictionaryProvider(
                httpClient,
                optionsFor 2,
                clock,
                NullLogger<HttpDictionaryProvider>.Instance
            )
            :> IDictionaryProvider

        let! result = client.FetchAsync(CancellationToken.None)

        Assert.Equal(None, inner.LastXApiKey)

        match result with
        | Failed(Unauthorized, _) -> ()
        | other -> Assert.Fail(sprintf "expected Failed(Unauthorized, _), got %A" other)
    }
