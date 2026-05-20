module Stem.ButtonPanelTester.Tests.Windows.Integration.HttpDictionaryServiceWarmUpTests

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Infrastructure.Http

/// Integration tests for the production `IDictionaryServiceWarmUp`
/// adapter per `specs/001-fetch-dictionary/phases/phase-7.md` slice 3.
/// Drives a stubbed `HttpMessageHandler` so the wire shape (URL,
/// method, success / non-200 / exception / timeout paths) is
/// observable without reaching the real `app-dictionaries-manager-prod`
/// Azure App Service. Lives in `Tests.Windows` because the adapter
/// ships in `ButtonPanelTester.Infrastructure` (`net10.0-windows` for
/// DPAPI co-location).

// --- stub handler ---

type private StubHttpHandler(handler: HttpRequestMessage -> Task<HttpResponseMessage>) =
    inherit HttpMessageHandler()

    let mutable lastRequest: HttpRequestMessage option = None
    let mutable callCount = 0

    member _.LastRequest = lastRequest
    member _.CallCount = callCount

    override _.SendAsync
        (
            request: HttpRequestMessage,
            _: CancellationToken
        ) : Task<HttpResponseMessage> =
        task {
            callCount <- callCount + 1
            lastRequest <- Some request
            return! handler request
        }

// --- helpers ---

let private makeJsonResponse (status: HttpStatusCode) (body: string) =
    let response = new HttpResponseMessage(status)
    response.Content <- new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    response

let private healthyHandler () =
    fun (_: HttpRequestMessage) ->
        Task.FromResult(makeJsonResponse HttpStatusCode.OK "{\"status\":\"Healthy\"}")

let private unhealthyHandler () =
    fun (_: HttpRequestMessage) ->
        Task.FromResult(
            makeJsonResponse HttpStatusCode.ServiceUnavailable "{\"status\":\"Unhealthy\"}"
        )

let private throwingHandler (ex: exn) =
    fun (_: HttpRequestMessage) -> Task.FromException<HttpResponseMessage>(ex)

let private makeClient (handler: StubHttpHandler) : HttpClient =
    let client = new HttpClient(handler, disposeHandler = false)
    client.BaseAddress <- Uri "https://stub.example/"
    client

let private makeAdapter (handler: StubHttpHandler) : IDictionaryServiceWarmUp =
    let client = makeClient handler
    let logger = NullLogger<HttpDictionaryServiceWarmUp>.Instance
    HttpDictionaryServiceWarmUp(client, logger) :> IDictionaryServiceWarmUp

// --- tests: success / non-200 ---

[<Fact>]
let WarmUpAsync_Healthy_ReturnsUnit () =
    task {
        use handler = new StubHttpHandler(healthyHandler ())
        let adapter = makeAdapter handler

        do! adapter.WarmUpAsync(CancellationToken.None)

        Assert.Equal(1, handler.CallCount)
    }

[<Fact>]
let WarmUpAsync_Unhealthy_StillReturnsUnit () =
    // From the warm-up's point of view, the server's health verdict
    // is irrelevant — the worker is responsive (the response came
    // back at all), which is the cold-boot property we cared about.
    // The production fetch path is where the actual health of the
    // dictionary service surfaces to the user.
    task {
        use handler = new StubHttpHandler(unhealthyHandler ())
        let adapter = makeAdapter handler

        do! adapter.WarmUpAsync(CancellationToken.None)

        Assert.Equal(1, handler.CallCount)
    }

// --- tests: failure modes ---

[<Fact>]
let WarmUpAsync_NetworkException_Propagates () =
    // Adapter is dumb on the failure side: it lets the underlying
    // exception leak. The orchestration class (`DictionaryWarmUp`)
    // owns the swallow-policy.
    task {
        use handler =
            new StubHttpHandler(throwingHandler (HttpRequestException("simulated DNS failure")))

        let adapter = makeAdapter handler

        let act () : Task = adapter.WarmUpAsync(CancellationToken.None)

        let! _ = Assert.ThrowsAsync<HttpRequestException>(act)
        ()
    }

[<Fact>]
let WarmUpAsync_TaskCanceledFromInsideHandler_MapsToOperationCanceled () =
    // Mirrors `HttpDictionaryProvider.FetchAsync_TaskCanceledFromInsideHandler_ReturnsFailedTimeout`:
    // the adapter's internal 90 s timeout is enforced via a linked
    // CTS. The stub simulates that timeout by throwing
    // `TaskCanceledException` directly; the caller's `ct` is never
    // cancelled, so the adapter surfaces it as
    // `OperationCanceledException` (which the orchestration class
    // will swallow per slice 2 semantics — caller-`ct` cancellation
    // is the only flavour that escapes the warm-up).
    task {
        use handler =
            new StubHttpHandler(throwingHandler (new TaskCanceledException("simulated timeout")))

        let adapter = makeAdapter handler

        let act () : Task = adapter.WarmUpAsync(CancellationToken.None)

        let! _ = Assert.ThrowsAnyAsync<OperationCanceledException>(act)
        ()
    }

// --- tests: wire shape ---

[<Fact>]
let WarmUpAsync_RequestIsGetToHealthRelativeToBaseAddress () =
    task {
        use handler = new StubHttpHandler(healthyHandler ())
        let adapter = makeAdapter handler

        do! adapter.WarmUpAsync(CancellationToken.None)

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

        Assert.Equal("/health", uri.AbsolutePath)
    }

[<Fact>]
let HttpDictionaryServiceWarmUp_TimeoutSeconds_IsNinety () =
    Assert.Equal(90.0, HttpDictionaryServiceWarmUp.TimeoutSeconds)
