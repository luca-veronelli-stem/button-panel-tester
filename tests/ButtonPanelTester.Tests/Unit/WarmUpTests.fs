module Stem.ButtonPanelTester.Tests.Unit.WarmUpTests

open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Dictionary

/// Tests for `Services.Dictionary.DictionaryWarmUp.RunAsync` per
/// `specs/001-fetch-dictionary/phases/phase-7.md` slice 3 (issue #92).
/// The warm-up runs once at GUI startup, fire-and-forget, so the
/// Azure App Service Free-tier worker is hot before the technician
/// clicks Refresh or submits the registration token. Three contracts
/// pinned here:
///   1. `WarmUpAsync` is called exactly once per `RunAsync` invocation.
///   2. Non-cancellation outcomes (the adapter throwing a transport
///      exception) are swallowed — the production fetch path remains
///      the source of truth for surfaced errors.
///   3. `OperationCanceledException` originating from the caller's
///      `ct` propagates so the GUI's shutdown path can stop the
///      in-flight warm-up cleanly.
///
/// The adapter shape — `GET /health` against the unauthenticated
/// dictionary-service endpoint — is exercised separately in
/// `tests/ButtonPanelTester.Tests.Windows/Integration/HttpDictionaryServiceWarmUpTests.fs`.

/// Counting `IDictionaryServiceWarmUp` stub. Records the number of
/// `WarmUpAsync` invocations and yields a scripted outcome per call —
/// either returns normally (success) or throws a scripted exception
/// (so the orchestration's swallow-policy and cancellation
/// propagation can be exercised without spinning up an `HttpClient`).
type private CountingWarmUp(scriptedException: exn option) =
    let mutable calls = 0

    member _.Calls = calls

    interface IDictionaryServiceWarmUp with
        member _.WarmUpAsync(ct: CancellationToken) : Task<unit> =
            task {
                calls <- calls + 1
                ct.ThrowIfCancellationRequested()

                match scriptedException with
                | Some ex -> raise ex
                | None -> return ()
            }

[<Fact>]
let RunAsync_CallsWarmUpAsyncExactlyOnce () =
    task {
        let stub = CountingWarmUp(None)

        let warmUp =
            DictionaryWarmUp(
                stub :> IDictionaryServiceWarmUp,
                NullLogger<DictionaryWarmUp>.Instance
            )

        do! warmUp.RunAsync(CancellationToken.None)

        Assert.Equal(1, stub.Calls)
    }

[<Fact>]
let RunAsync_SwallowsAdapterException () =
    // The production fetch path retries from the user's Refresh
    // click; the warm-up's job is just to prime the worker.
    task {
        let stub =
            CountingWarmUp(
                Some(System.Net.Http.HttpRequestException("simulated DNS failure"))
            )

        let warmUp =
            DictionaryWarmUp(
                stub :> IDictionaryServiceWarmUp,
                NullLogger<DictionaryWarmUp>.Instance
            )

        do! warmUp.RunAsync(CancellationToken.None)

        Assert.Equal(1, stub.Calls)
    }

[<Fact>]
let RunAsync_PropagatesCancellationFromCallersCt () =
    task {
        use cts = new CancellationTokenSource()
        cts.Cancel()
        let stub = CountingWarmUp(None)

        let warmUp =
            DictionaryWarmUp(
                stub :> IDictionaryServiceWarmUp,
                NullLogger<DictionaryWarmUp>.Instance
            )

        let act () : Task = warmUp.RunAsync(cts.Token)

        let! _ = Assert.ThrowsAnyAsync<System.OperationCanceledException>(act)
        ()
    }
