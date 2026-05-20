module Stem.ButtonPanelTester.Tests.Unit.WarmUpTests

open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Dictionary

/// Tests for `Services.Dictionary.WarmUp.runAsync` per
/// `specs/001-fetch-dictionary/phases/phase-7.md` (issue #92, slice 2).
/// The warm-up runs once at GUI startup, fire-and-forget, so the Azure
/// App Service Free-tier worker is hot before the technician clicks
/// Refresh. Three contracts pinned here:
///   1. `FetchAsync` is called exactly once per `runAsync` invocation.
///   2. Non-cancellation outcomes (success and any `Failed` reason)
///      are swallowed — the production fetch path is the source of
///      truth, the warm-up just primes the worker.
///   3. `OperationCanceledException` originating from the caller's
///      `ct` propagates so the GUI's shutdown path can stop the
///      in-flight warm-up cleanly.

/// Counting `IDictionaryProvider` stub. Records the number of
/// `FetchAsync` invocations and yields a scripted `DictionaryFetchResult`
/// per call.
type private CountingProvider(scripted: DictionaryFetchResult) =
    let mutable calls = 0

    member _.Calls = calls

    interface IDictionaryProvider with
        member _.FetchAsync(_: CancellationToken) : Task<DictionaryFetchResult> =
            task {
                calls <- calls + 1
                return scripted
            }

/// Cancellation-aware stub. Calls `ct.ThrowIfCancellationRequested()`
/// before yielding — the production adapter's contract is that
/// `OperationCanceledException` may only leak when the caller's `ct`
/// fired (`HttpDictionaryProvider` lines 326-333). This stub
/// simulates that path so the warm-up test can verify propagation
/// without spinning up a real `HttpClient`.
type private CancellableProvider() =
    interface IDictionaryProvider with
        member _.FetchAsync(ct: CancellationToken) : Task<DictionaryFetchResult> =
            task {
                ct.ThrowIfCancellationRequested()
                return Success(
                    { ContentHash = String.replicate 64 "0"; PanelTypes = [] },
                    System.DateTimeOffset.UtcNow
                )
            }

let private aDictionary () : ButtonPanelDictionary = {
    ContentHash = String.replicate 64 "a"
    PanelTypes = []
}

[<Fact>]
let WarmUp_CallsFetchAsyncExactlyOnce () =
    task {
        let provider =
            CountingProvider(Success(aDictionary (), System.DateTimeOffset.UtcNow))

        do!
            WarmUp.runAsync
                (provider :> IDictionaryProvider)
                NullLogger.Instance
                CancellationToken.None

        Assert.Equal(1, provider.Calls)
    }

[<Fact>]
let WarmUp_SwallowsFailedResult () =
    // A `Failed` outcome surfaces in logs but must not raise — the
    // production fetch path retries from the user's Refresh click.
    task {
        let provider =
            CountingProvider(Failed(NetworkUnreachable, Some "warm-up DNS failed"))

        do!
            WarmUp.runAsync
                (provider :> IDictionaryProvider)
                NullLogger.Instance
                CancellationToken.None

        Assert.Equal(1, provider.Calls)
    }

[<Fact>]
let WarmUp_PropagatesCancellationFromCallersCt () =
    task {
        use cts = new CancellationTokenSource()
        cts.Cancel()
        let provider = CancellableProvider() :> IDictionaryProvider

        let act () : Task =
            WarmUp.runAsync provider NullLogger.Instance cts.Token

        let! _ = Assert.ThrowsAnyAsync<System.OperationCanceledException>(act)
        ()
    }
