module Stem.ButtonPanelTester.Tests.Property.CacheConsistencyTests

open System
open System.Threading
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Dictionary
open Stem.ButtonPanelTester.Tests.Fakes

/// FsCheck property mirroring `CacheConsistency.lean` (T027): for
/// any sequence of fetch outcomes where at least one is `Success`,
/// after the first `Success` the on-disk JSON's `ContentHash`
/// equals the in-memory `ButtonPanelDictionary.ContentHash` at
/// every observable point (FR-010, SC-007).
///
/// `CacheConsistency.lean` proves the post-`recordSuccess` slot
/// equality by `rfl` against a Lean `Session` model. The F#-side
/// property is the operational counterpart: drive the production
/// `DictionaryService` through a generator-supplied fetch sequence
/// and assert the cache-vs-memory hash equality after each fetch
/// once the first `Success` has landed.
///
/// Lives in `tests/ButtonPanelTester.Tests/` (net10.0) because
/// every producer (`DictionaryService`, `InMemoryDictionaryProvider`,
/// `InMemoryDictionaryCache`, `FrozenClock`) is `net10.0`.

let private clockNow =
    DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero)

/// Hand-crafted small dictionary universe — three distinct
/// payloads keyed by their `ContentHash`. The Lean theorem is
/// polymorphic in `Dict`; here we instantiate with a small
/// concrete family so the property is finite and fast.
let private dictPool : ButtonPanelDictionary[] = [|
    {
        ContentHash = String.replicate 64 "a"
        PanelTypes = [
            {
                Id = 1
                Name = "Panel A"
                Description = None
                Variables = []
            }
        ]
    }
    {
        ContentHash = String.replicate 64 "b"
        PanelTypes = [
            {
                Id = 2
                Name = "Panel B"
                Description = Some "with a description"
                Variables = []
            }
        ]
    }
    {
        ContentHash = String.replicate 64 "c"
        PanelTypes = [
            {
                Id = 3
                Name = "Panel C"
                Description = None
                Variables = []
            }
        ]
    }
|]

let private seedDict : ButtonPanelDictionary = {
    ContentHash = String.replicate 64 "s"
    PanelTypes = [
        {
            Id = 0
            Name = "Seed"
            Description = None
            Variables = []
        }
    ]
}

let private seedFetchedAt =
    DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)

/// Encoded fetch outcome variant. The generator emits these; the
/// property's action loop translates them into
/// `DictionaryFetchResult` values stamped with a deterministic
/// `fetchedAt` derived from the index.
type FetchOutcome =
    | OkPick of dictIndex: int
    | FailNetwork
    | FailServer
    | FailUnauthorized

/// FsCheck `Arbitrary` container — passed to `[<Property>]` via
/// `Arbitrary = [| typeof<FetchOutcomeArb> |]`. The attribute
/// reflects over the type for a static member returning
/// `Arbitrary<_>`, so the surface here matches what FsCheck.Xunit
/// expects (mirrors the `FiniteFloats` pattern in
/// `DictionarySerializationTests`).
type FetchOutcomeArb =
    static member Outcome () : Arbitrary<FetchOutcome> =
        Gen.frequency [
            // Bias toward `OkPick` so the precondition "at least one
            // Success" doesn't bottom-out the sample space.
            3, gen {
                let! i = Gen.choose(0, dictPool.Length - 1)
                return OkPick i
            }
            1, Gen.constant FailNetwork
            1, Gen.constant FailServer
            1, Gen.constant FailUnauthorized
        ]
        |> Arb.fromGen

let private outcomeToResult (idx: int) (o: FetchOutcome) : DictionaryFetchResult =
    let fetchedAt =
        clockNow.AddSeconds(float idx)
    match o with
    | OkPick i ->
        let dict = dictPool[i % dictPool.Length]
        Success(dict, fetchedAt)
    | FailNetwork -> Failed(NetworkUnreachable, None)
    | FailServer -> Failed(ServerError, Some "HTTP 503")
    | FailUnauthorized -> Failed(Unauthorized, Some "API key missing or invalid.")

[<Property(Arbitrary = [| typeof<FetchOutcomeArb> |])>]
let CacheAndMemoryHash_AgreePostFirstSuccess (outcomes: FetchOutcome list) =
    let hasSuccess =
        outcomes |> List.exists (function OkPick _ -> true | _ -> false)
    // Precondition: skip vacuously when the generator emitted no
    // `OkPick`. The implication operator `==>` returns Property
    // semantics that FsCheck handles correctly without coupling
    // the property to a list-length lower bound.
    hasSuccess ==> lazy (
        let cache = InMemoryDictionaryCache()
        cache.SeedWith(seedDict, seedFetchedAt)
        let scripted =
            outcomes
            |> List.mapi outcomeToResult
        let provider = InMemoryDictionaryProvider(scripted)
        let clock = FrozenClock(clockNow)
        let service =
            DictionaryService(clock, cache, provider) :> IDictionaryService

        let work = task {
            let! _ = service.InitializeAsync(CancellationToken.None)

            let mutable seenSuccess = false
            let mutable consistent = true

            for outcome in outcomes do
                let! _ = service.RefreshAsync(CancellationToken.None)

                let isSuccess =
                    match outcome with
                    | OkPick _ -> true
                    | _ -> false
                if isSuccess then seenSuccess <- true

                if seenSuccess then
                    // Hash agreement check: read the cache adapter's
                    // current bytes back and compare to the snapshot
                    // in memory.
                    let! readBack =
                        (cache :> IDictionaryCache).ReadAsync(
                            CancellationToken.None
                        )
                    match readBack, service.Snapshot with
                    | Success(diskDict, _), ValueSome(memDict, _) ->
                        if diskDict.ContentHash <> memDict.ContentHash then
                            consistent <- false
                    | _ ->
                        // Either slot empty after a recorded success
                        // → unreachable by `DictionaryService`'s
                        // contract; treat as a failed invariant.
                        consistent <- false

            return consistent
        }

        work.GetAwaiter().GetResult()
    )
