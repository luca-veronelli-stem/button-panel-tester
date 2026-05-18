module Stem.ButtonPanelTester.Tests.Windows.Integration.HttpRegistrationClientTests

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
open Microsoft.Extensions.Options
open Xunit
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Infrastructure.Http
open Stem.ButtonPanelTester.Services.Dictionary

/// Integration tests for the production `IRegistrationClient` adapter
/// per `specs/001-fetch-dictionary/contracts/registration-api.md` and
/// `phase-4.md` §T046. Each `[<Fact>]` constructs the adapter against
/// a stubbed `HttpMessageHandler` that scripts a single response (or
/// throws a scripted exception) and inspects the recorded request to
/// validate the wire shape. Lives in `Tests.Windows` because the
/// adapter ships in `ButtonPanelTester.Infrastructure` (net10.0-windows
/// for DPAPI co-location).

// --- stub handler ---

/// Records the last `HttpRequestMessage` (and its body, eagerly read
/// inside `SendAsync` so the test can assert on it after the request
/// is disposed) and delegates the response to the supplied function.
/// The function returns a `Task<HttpResponseMessage>` so callers can
/// either yield a fully-built response or throw to simulate network
/// faults / timeouts.
type private StubHttpHandler(handler: HttpRequestMessage -> Task<HttpResponseMessage>) =
    inherit HttpMessageHandler()

    let mutable lastRequest: HttpRequestMessage option = None
    let mutable lastBody: string option = None

    member _.LastRequest = lastRequest
    member _.LastBody = lastBody

    override _.SendAsync
        (
            request: HttpRequestMessage,
            cancellationToken: CancellationToken
        ) : Task<HttpResponseMessage> =
        task {
            lastRequest <- Some request

            match request.Content with
            | null -> ()
            | content ->
                let! body = content.ReadAsStringAsync(cancellationToken)
                lastBody <- Some body

            return! handler request
        }

// --- helpers ---

let private optionsFor (baseUrl: string) : IOptions<DictionaryOptions> =
    Options.Create({ BaseUrl = baseUrl; Id = 2 })

let private makeJsonResponse (status: HttpStatusCode) (body: string) =
    let response = new HttpResponseMessage(status)
    response.Content <- new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    response

let private successHandler (apiCredential: string) =
    fun (_: HttpRequestMessage) ->
        let body = sprintf "{\"apiCredential\":\"%s\"}" apiCredential
        Task.FromResult(makeJsonResponse HttpStatusCode.OK body)

let private statusHandler (status: int) =
    fun (_: HttpRequestMessage) ->
        Task.FromResult(makeJsonResponse (enum<HttpStatusCode> status) "{\"error\":\"stub\"}")

let private throwingHandler (ex: exn) =
    fun (_: HttpRequestMessage) -> Task.FromException<HttpResponseMessage>(ex)

let private makeClient (handler: StubHttpHandler) =
    let httpClient = new HttpClient(handler, disposeHandler = false)
    let options = optionsFor "https://stub.example"
    let logger = NullLogger<HttpRegistrationClient>.Instance

    HttpRegistrationClient(httpClient, options, logger) :> IRegistrationClient

let private aToken () =
    match BootstrapToken.TryCreate "TOKEN-1234" with
    | Ok t -> t
    | Error msg -> failwithf "test setup: %s" msg

// --- tests ---

[<Fact>]
let RegisterAsync_200_ReturnsOkWithCredential () =
    task {
        use handler = new StubHttpHandler(successHandler "issued-credential-xyz")
        let client = makeClient handler

        let! result = client.RegisterAsync(aToken (), CancellationToken.None)

        match result with
        | Ok credential -> Assert.Equal("issued-credential-xyz", credential.Value)
        | Error e -> Assert.Fail(sprintf "expected Ok, got Error %A" e)
    }

[<Fact>]
let RegisterAsync_400_ReturnsTokenInvalid () =
    task {
        use handler = new StubHttpHandler(statusHandler 400)
        let client = makeClient handler

        let! result = client.RegisterAsync(aToken (), CancellationToken.None)

        match result with
        | Error TokenInvalid -> ()
        | other -> Assert.Fail(sprintf "expected Error TokenInvalid, got %A" other)
    }

[<Fact>]
let RegisterAsync_409_ReturnsTokenInvalid () =
    task {
        use handler = new StubHttpHandler(statusHandler 409)
        let client = makeClient handler

        let! result = client.RegisterAsync(aToken (), CancellationToken.None)

        match result with
        | Error TokenInvalid -> ()
        | other -> Assert.Fail(sprintf "expected Error TokenInvalid, got %A" other)
    }

[<Fact>]
let RegisterAsync_503_ReturnsRegistrationServerError () =
    task {
        use handler = new StubHttpHandler(statusHandler 503)
        let client = makeClient handler

        let! result = client.RegisterAsync(aToken (), CancellationToken.None)

        match result with
        | Error(RegistrationServerError 503) -> ()
        | other ->
            Assert.Fail(sprintf "expected Error (RegistrationServerError 503), got %A" other)
    }

[<Fact>]
let RegisterAsync_NetworkException_ReturnsNetworkUnreachable () =
    task {
        use handler =
            new StubHttpHandler(throwingHandler (HttpRequestException("simulated DNS failure")))

        let client = makeClient handler

        let! result = client.RegisterAsync(aToken (), CancellationToken.None)

        match result with
        | Error(RegistrationNetwork NetworkUnreachable) -> ()
        | other ->
            Assert.Fail(
                sprintf
                    "expected Error (RegistrationNetwork NetworkUnreachable), got %A"
                    other
            )
    }

[<Fact>]
let RegisterAsync_TaskCanceledFromInsideHandler_ReturnsTimeout () =
    // The adapter's internal 10 s timeout is enforced via a linked
    // CancellationTokenSource. The stub simulates that timeout by
    // throwing TaskCanceledException directly — the caller's ct is
    // never cancelled, so the adapter's guarded catch routes the
    // exception to RegistrationNetwork Timeout per the contract.
    task {
        use handler =
            new StubHttpHandler(throwingHandler (new TaskCanceledException("simulated timeout")))

        let client = makeClient handler

        let! result = client.RegisterAsync(aToken (), CancellationToken.None)

        match result with
        | Error(RegistrationNetwork Timeout) -> ()
        | other ->
            Assert.Fail(sprintf "expected Error (RegistrationNetwork Timeout), got %A" other)
    }

[<Fact>]
let RegisterAsync_RequestBodyIsBootstrapTokenJson () =
    task {
        use handler = new StubHttpHandler(successHandler "irrelevant")
        let client = makeClient handler

        let! _ = client.RegisterAsync(aToken (), CancellationToken.None)

        let body =
            match handler.LastBody with
            | Some b -> b
            | None -> Assert.Fail("expected the stub to have observed a body"); ""

        // Exact string match — the contract specifies the field name
        // and camelCase casing. JsonSerializer emits a compact form
        // when no WriteIndented is set.
        Assert.Equal("{\"bootstrapToken\":\"TOKEN-1234\"}", body)
    }

[<Fact>]
let RegisterAsync_DoesNotSendXApiKeyHeader () =
    task {
        use handler = new StubHttpHandler(successHandler "irrelevant")
        let client = makeClient handler

        let! _ = client.RegisterAsync(aToken (), CancellationToken.None)

        let request =
            match handler.LastRequest with
            | Some r -> r
            | None ->
                Assert.Fail("expected the stub to have observed a request")
                Unchecked.defaultof<_>

        // /register is the anonymous bootstrap entry point — sending
        // X-Api-Key here would either be silently ignored (current
        // behaviour) or surface as a misleading 401 if the server
        // ever adds middleware. Either way: the adapter must not send
        // it.
        Assert.False(request.Headers.Contains("X-Api-Key"))
    }

[<Fact>]
let RegisterAsync_SendsCorrectUserAgentAndAccept () =
    task {
        use handler = new StubHttpHandler(successHandler "irrelevant")
        let client = makeClient handler

        let! _ = client.RegisterAsync(aToken (), CancellationToken.None)

        let request =
            match handler.LastRequest with
            | Some r -> r
            | None ->
                Assert.Fail("expected the stub to have observed a request")
                Unchecked.defaultof<_>

        // User-Agent.ParseAdd validates the format; assert there is
        // exactly one product token starting with "Stem.ButtonPanelTester/".
        let userAgent =
            request.Headers.UserAgent
            |> Seq.map (fun pivh -> pivh.ToString())
            |> String.concat " "

        Assert.StartsWith("Stem.ButtonPanelTester/", userAgent)

        // Accept must contain application/json. `MediaType` is
        // `string | null` under strict nullness; the explicit
        // filter step drops nulls so `Assert.Contains` sees a
        // `string seq`.
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
let RegisterAsync_PostsToRegisterPath () =
    task {
        use handler = new StubHttpHandler(successHandler "irrelevant")
        let client = makeClient handler

        let! _ = client.RegisterAsync(aToken (), CancellationToken.None)

        let request =
            match handler.LastRequest with
            | Some r -> r
            | None ->
                Assert.Fail("expected the stub to have observed a request")
                Unchecked.defaultof<_>

        Assert.Equal(HttpMethod.Post, request.Method)

        let uri =
            match request.RequestUri with
            | null ->
                Assert.Fail("expected a request URI")
                Unchecked.defaultof<_>
            | u -> u

        Assert.Equal("/register", uri.AbsolutePath)
    }
