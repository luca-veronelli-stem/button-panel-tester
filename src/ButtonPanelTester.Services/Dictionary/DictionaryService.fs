namespace Stem.ButtonPanelTester.Services.Dictionary

open System.Threading
open System.Threading.Tasks
open Stem.ButtonPanelTester.Core.Dictionary

/// Production adapter for `IDictionaryService`, per
/// `specs/001-fetch-dictionary/data-model.md` §3. This Phase 3 slice
/// (US1, MVP) implements only the offline path: `InitializeAsync`
/// drives the cache adapter through extract-if-missing → read and
/// labels the result as `Cached(_, FromEmbeddedSeed | FromLocalFile, None)`.
///
/// `RefreshAsync` is a deliberate `notSupported` stub here — the
/// live-fetch coalescing logic per `research.md` R5 (in-flight
/// `TaskCompletionSource` guard, identical-content skip-write
/// optimisation, FR-007 multi-click safety) lands in T050 (US3 /
/// Phase 5) when `HttpDictionaryProvider` (T049) is ready.
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
///   - `clock`   — `IClock` port; held for the future US3 refresh
///     path (live `FetchedAt` stamping). Unused on the offline-only
///     path but injected here so the composition root binding is
///     stable across the three user stories.
///   - `cache`   — `IDictionaryCache` port; the on-disk JSON+sidecar
///     orchestrated by `JsonFileDictionaryCache` in production
///     (T029) and by `InMemoryDictionaryCache` in tests (T019).
///   - `provider` — `IDictionaryProvider` port; unused on the
///     offline path but accepted so the singleton DI registration
///     in `CompositionRoot` (T033) doesn't need to know which
///     phase is wired. The US1 composition binds it to a no-op
///     fake; US3 (T053) replaces it with `HttpDictionaryProvider`.
type DictionaryService(_clock: IClock, cache: IDictionaryCache, _provider: IDictionaryProvider) =

    let sourceChanged = Event<DictionarySource>()
    let mutable snapshot : (ButtonPanelDictionary * DictionarySource) voption = ValueNone

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

        member _.RefreshAsync(_: CancellationToken) =
            // US3 / T050 stub. The full implementation coalesces
            // concurrent callers per FR-007 via a lock-guarded
            // `TaskCompletionSource<DictionaryStateUpdate> voption`
            // (research.md R5) and re-labels `Live → Cached` on
            // transient failure per FR-011, FR-012.
            raise (System.NotSupportedException(
                "DictionaryService.RefreshAsync is not supported in the Phase 3 slice. Live-fetch refresh lands in T050 (US3)."))
