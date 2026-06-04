namespace Stem.ButtonPanelTester.Services.Dictionary

open System
open System.Threading
open System.Threading.Tasks
open Stem.ButtonPanelTester.Core.Dictionary

/// Production adapter for `IDictionaryService`, per
/// `specs/001-fetch-dictionary/data-model.md` §3.
/// `InitializeAsync` (US1) drives the cache adapter through
/// extract-if-missing → read and labels the result as `Cached(_,
/// FromEmbeddedSeed | FromLocalFile, None)`. `RefreshAsync` (US3,
/// T050) drives the live fetch through `IDictionaryProvider`,
/// coalesces concurrent callers per FR-007 via a lock-guarded
/// `TaskCompletionSource<DictionaryStateUpdate> voption`, and
/// re-labels the in-memory dictionary as `Cached(_, FromLocalFile,
/// Some reason)` on transient failure per FR-011 + FR-012 without
/// touching the cache file.
///
/// FR-019 cache-integrity recovery: `InitializeAsync` reads the
/// cache before calling `ExtractSeedIfMissingAsync`, so a corrupt
/// JSON/sidecar pair falls back to the embedded seed and surfaces
/// as `Cached(seedTime, FromEmbeddedSeed, Some CacheUnreadable)`.
/// The cache adapter (T029) was loosened from "extract when files
/// are absent" to "extract when `ReadAsync` would fail" so the
/// same atomic temp+rename overwrites the corrupt pair. T037
/// exercises the three observable cases: empty disk + seed,
/// pre-existing readable cache, and integrity failure.
///
/// Constructor parameters:
///   - `clock`    — `IClock` port; `UtcNow()` stamps
///     `SourceChanged Live(now)` on identical-content live
///     refreshes (where the provider's `fetchedAt` is the same
///     as before) and reads through for the canonical refresh
///     timestamp.
///   - `cache`    — `IDictionaryCache` port; the on-disk
///     JSON+sidecar orchestrated by `JsonFileDictionaryCache` in
///     production (T029) and by `InMemoryDictionaryCache` in
///     tests (T019).
///   - `provider` — `IDictionaryProvider` port. `OfflineDictionaryProvider`
///     in the US1 composition; replaced by
///     `Infrastructure.Http.HttpDictionaryProvider` at T051 (US3).
type DictionaryService(clock: IClock, cache: IDictionaryCache, provider: IDictionaryProvider) =

    let sourceChanged = Event<DictionarySource>()
    let mutable snapshot : (ButtonPanelDictionary * DictionarySource) voption = ValueNone

    // Coalescing guard per `research.md` R5: the second concurrent
    // caller of `RefreshAsync` observes the in-flight TCS and
    // awaits the same Task the first caller created — no second
    // HTTP call, no second cache write, FR-007 satisfied. The
    // `inFlightLock` makes the read-or-create-and-store check
    // atomic.
    let inFlightLock = obj()
    let mutable inFlight : TaskCompletionSource<DictionaryStateUpdate> voption = ValueNone

    interface IDictionaryService with

        member _.Snapshot = snapshot

        [<CLIEvent>]
        member _.SourceChanged = sourceChanged.Publish

        member _.InitializeAsync(ct: CancellationToken) = task {
            // FR-019 cache-integrity recovery:
            //   1. Read the cache first so the pre-extract failure
            //      reason is observable for the `Some CacheUnreadable`
            //      label on the corrupt-cache path below.
            //   2. Extract the seed when the prior read failed (the
            //      adapter loosened in T029 keys off `ReadAsync`).
            //   3. Re-read; the cache is now either the pre-existing
            //      readable file, a freshly-extracted seed, or a
            //      state the seed extractor could not repair.
            let! priorResult = cache.ReadAsync(ct)
            do! cache.ExtractSeedIfMissingAsync(ct)
            let! readResult = cache.ReadAsync(ct)

            match readResult with
            | Success(dict, fetchedAt) ->
                // origin/lastFailure are derived from the pre-extract
                // observation:
                //   - Success      → cache was readable on entry, no
                //                    extraction happened, FromLocalFile.
                //   - CacheAbsent  → cold start, seed extracted, no
                //                    prior failure to report.
                //   - other Failed → corrupt pair was overwritten by
                //                    the seed; carry the original
                //                    failure reason so the status row
                //                    can surface `Cached(_,
                //                    FromEmbeddedSeed, Some _)` per
                //                    FR-019.
                let origin, lastFailure =
                    match priorResult with
                    | Success _ -> FromLocalFile, None
                    | Failed(CacheAbsent, _) -> FromEmbeddedSeed, None
                    | Failed(reason, _) -> FromEmbeddedSeed, Some reason
                let source = Cached(fetchedAt, origin, lastFailure)
                snapshot <- ValueSome(dict, source)
                sourceChanged.Trigger(source)
                return Updated(dict, source)

            | Failed(reason, _) ->
                // Seed extractor could not produce a readable cache —
                // either no embedded resource was wired in (test
                // fakes without a seed scripting hook) or the
                // post-extract read failed again. Surface as
                // `NoDictionaryAvailable`; the GUI renders the
                // "dictionary unavailable" banner.
                return NoDictionaryAvailable reason
        }

        member this.RefreshAsync(ct: CancellationToken) =
            // FR-007 in-flight coalescing per research.md R5:
            // hand the second caller the same Task the first
            // caller is awaiting, so a multi-click on Refresh
            // does not multiply HTTP load. The TCS is cleared
            // before the worker signals it, mirroring the legacy
            // ordering that ensures a third caller arriving
            // exactly at the signal boundary either observes a
            // fresh `inFlight = ValueNone` (and starts a new
            // fetch) or the already-resolved TCS (and skips the
            // fetch entirely).
            let tcs, isLeader =
                lock inFlightLock (fun () ->
                    match inFlight with
                    | ValueSome existing -> existing, false
                    | ValueNone ->
                        let fresh = TaskCompletionSource<DictionaryStateUpdate>()
                        inFlight <- ValueSome fresh
                        fresh, true)

            if isLeader then
                let _ : Task = task {
                    try
                        try
                            let! fetchResult = provider.FetchAsync(ct)

                            let! update =
                                task {
                                    match fetchResult with
                                    | Success(dict, fetchedAt) ->
                                        // FR-001 / FR-012 (#191): persist the advanced
                                        // fetchedAt on EVERY successful fetch — including
                                        // when the content is byte-identical to the
                                        // in-memory copy. The earlier ContentHash
                                        // short-circuit skipped the write on identical
                                        // content, so the on-disk fetchedAt stayed at the
                                        // last content-change date and an offline relaunch
                                        // reported a stale "last synced" timestamp.
                                        // The cache envelope carries FetchedAt, so a fresh
                                        // timestamp changes the bytes and
                                        // JsonFileDictionaryCache.WriteAsync rewrites the
                                        // file; a genuine no-op (same content AND same
                                        // fetchedAt) is still skipped there by its hash
                                        // compare.
                                        do! cache.WriteAsync(dict, fetchedAt, ct)
                                        let newSource = Live(fetchedAt)
                                        snapshot <- ValueSome(dict, newSource)
                                        sourceChanged.Trigger(newSource)
                                        return Updated(dict, newSource)
                                    | Failed(reason, _) ->
                                        match snapshot with
                                        | ValueSome(current, currentSource) ->
                                            // FR-011 + FR-012: keep the in-memory
                                            // dictionary byte-for-byte, re-label as
                                            // Cached with the failure reason chip.
                                            // The cache file is untouched.
                                            // `previousFetchedAt` is the prior
                                            // source's FetchedAt regardless of
                                            // whether it was Live or Cached.
                                            let previousFetchedAt =
                                                match currentSource with
                                                | Live t -> t
                                                | Cached(t, _, _) -> t
                                            let reLabelled = Cached(previousFetchedAt, FromLocalFile, Some reason)
                                            snapshot <- ValueSome(current, reLabelled)
                                            sourceChanged.Trigger(reLabelled)
                                            return Updated(current, reLabelled)
                                        | ValueNone ->
                                            // Refresh called before Initialize
                                            // landed an in-memory dictionary.
                                            // Surface NoDictionaryAvailable rather
                                            // than synthesising a Cached state
                                            // from thin air.
                                            return NoDictionaryAvailable reason
                                }

                            tcs.TrySetResult(update) |> ignore
                        with ex ->
                            tcs.TrySetException(ex) |> ignore
                    finally
                        lock inFlightLock (fun () -> inFlight <- ValueNone)
                }
                ()

            tcs.Task
