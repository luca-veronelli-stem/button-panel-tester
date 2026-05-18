module Stem.ButtonPanelTester.Tests.Integration.DictionaryServiceInitializeTests

open System
open System.Threading
open Xunit
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Dictionary
open Stem.ButtonPanelTester.Tests.Fakes

/// Integration tests for `DictionaryService.InitializeAsync` per
/// `phase-3.md` §T037. Lives in the cross-platform `Tests` project
/// (net10.0) because the service and in-memory fakes are
/// net10.0-only.
///
/// Three primary `[<Fact>]`s mirror the cases enumerated in the
/// task spec; two extra `[<Fact>]`s lock additional contract
/// guarantees that fall out for free with this surface.

// --- fixtures ---

let private localDictionary : ButtonPanelDictionary = {
    ContentHash = String.replicate 64 "a"
    PanelTypes = [
        {
            Id = 1
            Name = "Local cache fixture"
            Description = None
            Variables = []
        }
    ]
}

let private seedDictionary : ButtonPanelDictionary = {
    ContentHash = String.replicate 64 "b"
    PanelTypes = [
        {
            Id = 2
            Name = "Embedded seed fixture"
            Description = None
            Variables = []
        }
    ]
}

let private localFetchedAt =
    DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.Zero)

let private seedFetchedAt =
    DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero)

let private clockNow =
    DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero)

let private mkService (cache: InMemoryDictionaryCache) : IDictionaryService =
    let clock = FrozenClock(clockNow)
    let provider = InMemoryDictionaryProvider([])
    DictionaryService(clock, cache, provider) :> IDictionaryService

// --- tests ---

[<Fact>]
let InitializeAsync_EmptyDiskWithAvailableSeed_ExtractsSeedAsEmbeddedOrigin () =
    task {
        let cache = InMemoryDictionaryCache()
        cache.SeedWith(seedDictionary, seedFetchedAt)

        let service = mkService cache
        let! update = service.InitializeAsync(CancellationToken.None)

        match update with
        | Updated(dict, source) ->
            Assert.Equal(seedDictionary.ContentHash, dict.ContentHash)

            match source with
            | Cached(fetchedAt, FromEmbeddedSeed, None) ->
                Assert.Equal(seedFetchedAt, fetchedAt)
            | other ->
                Assert.Fail(
                    sprintf
                        "expected Cached(seedFetchedAt, FromEmbeddedSeed, None), got %A"
                        other
                )
        | NoDictionaryAvailable reason ->
            Assert.Fail(sprintf "expected Updated, got NoDictionaryAvailable %A" reason)
    }

[<Fact>]
let InitializeAsync_PreExistingCache_LabelsAsLocalFile () =
    task {
        let cache = InMemoryDictionaryCache()
        cache.PrePopulate(localDictionary, localFetchedAt)

        let service = mkService cache
        let! update = service.InitializeAsync(CancellationToken.None)

        match update with
        | Updated(dict, source) ->
            Assert.Equal(localDictionary.ContentHash, dict.ContentHash)

            match source with
            | Cached(fetchedAt, FromLocalFile, None) ->
                Assert.Equal(localFetchedAt, fetchedAt)
            | other ->
                Assert.Fail(
                    sprintf
                        "expected Cached(localFetchedAt, FromLocalFile, None), got %A"
                        other
                )
        | NoDictionaryAvailable reason ->
            Assert.Fail(sprintf "expected Updated, got NoDictionaryAvailable %A" reason)
    }

[<Fact>]
let InitializeAsync_CorruptCacheWithAvailableSeed_FallsBackWithCacheUnreadable () =
    // FR-019 regression test: a pre-existing JSON/sidecar pair whose
    // hashes no longer agree must surface as
    // `Cached(seedTime, FromEmbeddedSeed, Some CacheUnreadable)`.
    // Locks in the T029 adapter-contract retrofit and the T032
    // service rewrite together at the integration boundary.
    task {
        let cache = InMemoryDictionaryCache()
        cache.SetCorrupt(Some "sidecar hash mismatch")
        cache.SeedWith(seedDictionary, seedFetchedAt)

        let service = mkService cache
        let! update = service.InitializeAsync(CancellationToken.None)

        match update with
        | Updated(dict, source) ->
            Assert.Equal(seedDictionary.ContentHash, dict.ContentHash)

            match source with
            | Cached(fetchedAt, FromEmbeddedSeed, Some CacheUnreadable) ->
                Assert.Equal(seedFetchedAt, fetchedAt)
            | other ->
                Assert.Fail(
                    sprintf
                        "expected Cached(seedFetchedAt, FromEmbeddedSeed, Some CacheUnreadable), got %A"
                        other
                )
        | NoDictionaryAvailable reason ->
            Assert.Fail(sprintf "expected Updated, got NoDictionaryAvailable %A" reason)
    }

[<Fact>]
let InitializeAsync_AnyUpdatedOutcome_FiresSourceChangedExactlyOnce () =
    task {
        let cache = InMemoryDictionaryCache()
        cache.SeedWith(seedDictionary, seedFetchedAt)

        let service = mkService cache
        let observed = ResizeArray<DictionarySource>()
        service.SourceChanged.Add(observed.Add)

        let! _ = service.InitializeAsync(CancellationToken.None)

        Assert.Equal(1, observed.Count)
    }

[<Fact>]
let InitializeAsync_EmptyDiskNoSeed_ReportsNoDictionaryAvailable () =
    // Neither prior cache nor seed payload — the service surfaces
    // the unrecoverable failure rather than silently masking it.
    task {
        let cache = InMemoryDictionaryCache()
        let service = mkService cache
        let! update = service.InitializeAsync(CancellationToken.None)

        match update with
        | NoDictionaryAvailable CacheAbsent -> ()
        | other ->
            Assert.Fail(
                sprintf "expected NoDictionaryAvailable CacheAbsent, got %A" other
            )
    }
