module Stem.ButtonPanelTester.Tests.Integration.DictionaryServiceRefreshTests

open System
open System.Threading
open System.Threading.Tasks
open Xunit
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Dictionary
open Stem.ButtonPanelTester.Tests.Fakes

/// Integration tests for `DictionaryService.RefreshAsync` per
/// `phase-5.md` §T055. Wires the production service through
/// `InMemoryDictionaryProvider` (scripted result sequences),
/// `InMemoryDictionaryCache`, and `FrozenClock`. Lives in
/// `tests/ButtonPanelTester.Tests/` (net10.0) because every
/// producer is `net10.0` (Services + Core + in-memory fakes).

// --- fixtures ---

let private dictA : ButtonPanelDictionary = {
    ContentHash = String.replicate 64 "a"
    PanelTypes = [
        {
            Id = 1
            Name = "Cached fixture"
            Description = None
            Variables = []
        }
    ]
}

let private dictB : ButtonPanelDictionary = {
    ContentHash = String.replicate 64 "b"
    PanelTypes = [
        {
            Id = 1
            Name = "Live fixture"
            Description = None
            Variables = []
        }
    ]
}

let private cachedFetchedAt =
    DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.Zero)

let private liveFetchedAt =
    DateTimeOffset(2026, 5, 19, 12, 30, 0, TimeSpan.Zero)

let private clockNow =
    DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero)

let private mkService
    (cache: InMemoryDictionaryCache)
    (scripted: DictionaryFetchResult seq) =
    let clock = FrozenClock(clockNow)
    let provider = InMemoryDictionaryProvider(scripted)
    let service = DictionaryService(clock, cache, provider) :> IDictionaryService
    service

let private initialiseFromCache (service: IDictionaryService) = task {
    let! _ = service.InitializeAsync(CancellationToken.None)
    return ()
}

// --- tests ---

/// Test-only `IDictionaryProvider` that blocks `FetchAsync` until
/// the supplied gate task completes. Lets the coalescing test
/// hold the leader's in-flight call open long enough for the
/// second caller to observe the existing TCS instead of starting
/// its own. Tracks the call count so the test can assert the
/// second caller did NOT issue a separate fetch.
type private GatedDictionaryProvider(gate: Task<DictionaryFetchResult>) =
    let mutable callCount = 0
    member _.CallCount = callCount

    interface IDictionaryProvider with
        member _.FetchAsync(_: CancellationToken) =
            callCount <- callCount + 1
            task { return! gate }

[<Fact>]
let RefreshAsync_TwoConcurrentCalls_CoalesceToOneFetch () =
    // FR-007 coalescing: a second concurrent caller observes the
    // same in-flight Task the first caller created — the provider
    // is called exactly once. The gate keeps the leader's fetch
    // pending so the follower's RefreshAsync runs while
    // `inFlight = ValueSome _`.
    task {
        let cache = InMemoryDictionaryCache()
        cache.PrePopulate(dictA, cachedFetchedAt)
        let gateTcs = TaskCompletionSource<DictionaryFetchResult>()
        let provider = GatedDictionaryProvider(gateTcs.Task)
        let clock = FrozenClock(clockNow)
        let service =
            DictionaryService(clock, cache, provider) :> IDictionaryService

        do! initialiseFromCache service

        // Fire both refreshes before releasing the gate. r1 enters
        // FetchAsync first and parks on `gateTcs.Task`; r2 hits
        // the inFlightLock, sees ValueSome, and shares r1's TCS.
        let r1 = service.RefreshAsync(CancellationToken.None)
        let r2 = service.RefreshAsync(CancellationToken.None)

        // Yield briefly so the leader's spawned task reaches
        // `provider.FetchAsync` before we count.
        do! Task.Yield()
        // ConfigureAwait/Yield does not guarantee the leader has
        // entered FetchAsync; loop until it has (or a generous
        // bound elapses).
        let mutable spins = 0
        while provider.CallCount = 0 && spins < 1000 do
            do! Task.Yield()
            spins <- spins + 1

        Assert.Equal(1, provider.CallCount)

        gateTcs.SetResult(Success(dictB, liveFetchedAt))

        let! u1 = r1
        let! u2 = r2

        // Still exactly one fetch — the follower did not issue a
        // second call after the leader resolved.
        Assert.Equal(1, provider.CallCount)

        match u1 with
        | Updated(_, Live t) -> Assert.Equal(liveFetchedAt, t)
        | other -> Assert.Fail(sprintf "expected first Updated(Live), got %A" other)

        match u2 with
        | Updated(_, Live t) -> Assert.Equal(liveFetchedAt, t)
        | other -> Assert.Fail(sprintf "expected second Updated(Live), got %A" other)
    }

[<Fact>]
let RefreshAsync_FailedRefresh_PreservesInMemoryDictionaryAndPreviousFetchedAt () =
    // FR-011 + FR-012 + SC-007: a transient failure does NOT
    // disturb the in-memory dictionary. The status row re-labels
    // to Cached with the prior FetchedAt and the failure reason
    // chip, but the bytes the rest of the app sees are
    // bit-identical to the pre-refresh snapshot.
    //
    // #191 regression (FR-012 "only on success" half): a FAILED
    // refresh must also leave the *persisted* timestamp untouched —
    // no WriteAsync, and a read-back still carries the prior
    // cachedFetchedAt rather than `now`.
    task {
        let cache = InMemoryDictionaryCache()
        cache.PrePopulate(dictA, cachedFetchedAt)
        let scripted = [ Failed(NetworkUnreachable, None) ]
        let service = mkService cache scripted

        do! initialiseFromCache service
        let baselineWriteCount = cache.WriteCount

        let! update = service.RefreshAsync(CancellationToken.None)

        match update with
        | Updated(dict, source) ->
            Assert.Equal(dictA, dict)

            match source with
            | Cached(t, FromLocalFile, Some NetworkUnreachable) ->
                Assert.Equal(cachedFetchedAt, t)
            | other ->
                Assert.Fail(
                    sprintf
                        "expected Cached(cachedFetchedAt, FromLocalFile, Some NetworkUnreachable), got %A"
                        other
                )
        | NoDictionaryAvailable reason ->
            Assert.Fail(sprintf "expected Updated, got NoDictionaryAvailable %A" reason)

        // No write on failure, and the disk timestamp is unchanged.
        Assert.Equal(baselineWriteCount, cache.WriteCount)

        let! readBack = (cache :> IDictionaryCache).ReadAsync(CancellationToken.None)

        match readBack with
        | Success(dict, t) ->
            Assert.Equal(dictA, dict)
            Assert.Equal(cachedFetchedAt, t)
        | other ->
            Assert.Fail(sprintf "expected Success(dictA, cachedFetchedAt) on read-back, got %A" other)
    }

[<Fact>]
let RefreshAsync_IdenticalContentSuccess_PersistsAdvancedFetchedAtEmitsLive () =
    // #191 (FR-001 / FR-012 / spec.md:85 edge case): a successful
    // refresh that returns byte-identical content MUST persist the
    // advanced fetchedAt to disk, so an offline relaunch reports the
    // last *successful sync*, not the last content-change date.
    //
    // The success carries a fresh fetchedAt (the provider stamps it
    // with clock.UtcNow()), so the cache envelope bytes change and
    // the write goes through — the service no longer short-circuits
    // on a matching ContentHash. The in-memory Live(fetchedAt)
    // signal is unchanged (acceptance #4).
    task {
        let cache = InMemoryDictionaryCache()
        cache.PrePopulate(dictA, cachedFetchedAt)
        let scripted = [ Success(dictA, liveFetchedAt) ]
        let service = mkService cache scripted

        do! initialiseFromCache service

        // Initialize does NOT write through (the cache is already
        // populated by PrePopulate); WriteCount starts at 0.
        let baselineWriteCount = cache.WriteCount

        let! update = service.RefreshAsync(CancellationToken.None)

        // The persisted timestamp advances: exactly one write through.
        Assert.Equal(baselineWriteCount + 1, cache.WriteCount)

        match update with
        | Updated(dict, Live t) ->
            Assert.Equal(dictA, dict)
            Assert.Equal(liveFetchedAt, t)
        | other -> Assert.Fail(sprintf "expected Updated(_, Live _), got %A" other)

        // A read-back observes the advanced fetchedAt on the same
        // (byte-identical) dictionary content.
        let! readBack = (cache :> IDictionaryCache).ReadAsync(CancellationToken.None)

        match readBack with
        | Success(dict, t) ->
            Assert.Equal(dictA, dict)
            Assert.Equal(liveFetchedAt, t)
        | other ->
            Assert.Fail(sprintf "expected Success(dictA, liveFetchedAt) on read-back, got %A" other)
    }

[<Fact>]
let RefreshAsync_DifferingContentSuccess_WritesCacheBeforeEmittingLive () =
    // FR-009 + FR-010: differing content goes through cache write,
    // then the in-memory snapshot flips to Live. Asserts the
    // cache state after RefreshAsync reflects the new dictionary
    // (and writeCount incremented).
    task {
        let cache = InMemoryDictionaryCache()
        cache.PrePopulate(dictA, cachedFetchedAt)
        let scripted = [ Success(dictB, liveFetchedAt) ]
        let service = mkService cache scripted

        do! initialiseFromCache service
        let baselineWriteCount = cache.WriteCount

        let! update = service.RefreshAsync(CancellationToken.None)

        Assert.Equal(baselineWriteCount + 1, cache.WriteCount)

        match update with
        | Updated(dict, Live t) ->
            Assert.Equal(dictB, dict)
            Assert.Equal(liveFetchedAt, t)
        | other -> Assert.Fail(sprintf "expected Updated(_, Live _), got %A" other)

        // The cache observably carries the new payload now — a
        // subsequent ReadAsync returns Success(dictB, liveFetchedAt).
        let! readBack = (cache :> IDictionaryCache).ReadAsync(CancellationToken.None)

        match readBack with
        | Success(dict, t) ->
            Assert.Equal(dictB, dict)
            Assert.Equal(liveFetchedAt, t)
        | other -> Assert.Fail(sprintf "expected Success on read-back, got %A" other)
    }

[<Fact>]
let RefreshAsync_401_LabelsCachedSomeUnauthorizedLeavesCredentialUntouched () =
    // FR-018 gating signal: the status row exposes the Re-register
    // affordance only when LastFailureReason = Some Unauthorized
    // (T052). This test asserts the service produces that exact
    // label. The credential store is NOT a DictionaryService
    // dependency, so "leaves the credential file untouched" is
    // a structural property: the service has no path through
    // which it could mutate ICredentialStore. The test wires an
    // InMemoryCredentialStore preloaded with a credential and
    // asserts it still loads after the refresh — a redundancy
    // guard for the future case where someone wires the store
    // into the service.
    task {
        let cache = InMemoryDictionaryCache()
        cache.PrePopulate(dictA, cachedFetchedAt)
        let credentialStore = InMemoryCredentialStore()
        do! (credentialStore :> ICredentialStore).SaveAsync(
            InstallationCredential.Create "preserved",
            CancellationToken.None
        )
        let scripted = [ Failed(Unauthorized, Some "API key missing or invalid.") ]
        let service = mkService cache scripted

        do! initialiseFromCache service

        let! update = service.RefreshAsync(CancellationToken.None)

        match update with
        | Updated(dict, Cached(t, FromLocalFile, Some Unauthorized)) ->
            Assert.Equal(dictA, dict)
            Assert.Equal(cachedFetchedAt, t)
        | other ->
            Assert.Fail(
                sprintf
                    "expected Updated(dictA, Cached(_, FromLocalFile, Some Unauthorized)), got %A"
                    other
            )

        let! credential =
            (credentialStore :> ICredentialStore).LoadAsync(CancellationToken.None)
        match credential with
        | Some c -> Assert.Equal("preserved", c.Value)
        | None -> Assert.Fail("expected credential store to still carry the preloaded value")
    }
