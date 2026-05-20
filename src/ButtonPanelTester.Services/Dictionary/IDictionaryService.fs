namespace Stem.ButtonPanelTester.Services.Dictionary

open System
open System.Threading
open System.Threading.Tasks
open Stem.ButtonPanelTester.Core.Dictionary

/// Notification carried on every state transition of the
/// dictionary service, per
/// `specs/001-fetch-dictionary/data-model.md` §3. The GUI
/// status-row ViewModel subscribes to these updates (via
/// `IDictionaryService.SourceChanged` for the streaming case and
/// via the Task<DictionaryStateUpdate> return of Initialize /
/// Refresh for the imperative case) and re-renders accordingly:
///   - `Updated`               — a usable dictionary is in memory.
///     The `source` field tells the status row whether the
///     dictionary came from a Live fetch or from the Cached
///     fallback (with the most-recent-failure reason carried in
///     the `Cached` payload).
///   - `NoDictionaryAvailable` — every path (live + cache + seed)
///     failed. `reason` carries the most-relevant
///     `FetchFailureReason` (T013). The GUI surfaces this as a
///     "dictionary unavailable" banner.
type DictionaryStateUpdate =
    /// A usable dictionary is in memory. `dictionary` is the current
    /// in-memory copy; `source` tells the status row whether it came
    /// from a Live fetch or from the Cached fallback (with the
    /// most-recent-failure reason carried in the `Cached` payload).
    | Updated of dictionary: ButtonPanelDictionary * source: DictionarySource
    /// Every byte-source (live + cache + seed) failed. `reason`
    /// carries the most-relevant `FetchFailureReason`; the GUI
    /// surfaces this as a "dictionary unavailable" banner.
    | NoDictionaryAvailable of reason: FetchFailureReason

/// Single-instance dictionary orchestration service, per
/// `specs/001-fetch-dictionary/data-model.md` §3. Registered as a
/// singleton in `CompositionRoot`; consumes the four data-path
/// ports from `Core.Dictionary` (`IClock`,
/// `IDictionaryProvider`, `IDictionaryCache`, `ICredentialStore`)
/// and exposes the snapshot + event + refresh surface that the
/// GUI ViewModels bind to. The orchestration logic
/// (cache-and-memory-in-lockstep, in-flight call coalescing per
/// FR-007, Live↔Cached re-labelling on transient failure) lives
/// in the `DictionaryService` production adapter (T030+); test
/// behaviour is exercised by the FsCheck property in T039
/// (DictionaryServiceTransitions) and the integration tests in
/// T040+.
type IDictionaryService =
    /// Snapshot of the active dictionary and its provenance.
    /// `ValueNone` until the first successful read (live or
    /// cache or seed) lands. Reads from the GUI thread must be
    /// considered eventually-consistent — the source-of-truth
    /// for transitions is the `SourceChanged` event below.
    abstract member Snapshot: (ButtonPanelDictionary * DictionarySource) voption

    /// Fired on every transition that changes the rendered
    /// status row. Suppressed when the in-memory dictionary is
    /// unchanged AND the `DictionarySource` label is unchanged
    /// (deduplication). The payload carries only the new
    /// `DictionarySource`; subscribers that need the dictionary
    /// itself read from `Snapshot` after the event fires.
    [<CLIEvent>]
    abstract member SourceChanged: IEvent<DictionarySource>

    /// First-launch / startup entry point. Same orchestration
    /// as `RefreshAsync` (live attempt -> cache fallback -> seed
    /// extraction); named distinctly so call sites read clearly
    /// at the composition root. Returns the resulting
    /// `DictionaryStateUpdate` so the GUI can render an initial
    /// state before any subscription is wired.
    abstract member InitializeAsync: ct: CancellationToken -> Task<DictionaryStateUpdate>

    /// Manual refresh triggered by the user (FR-006). Concurrent
    /// callers MUST receive the same in-flight Task (FR-007):
    /// the production adapter coalesces overlapping requests so
    /// a multi-click on the Refresh button does not multiply
    /// HTTP load. Returns the resulting `DictionaryStateUpdate`
    /// to the immediate caller; subscribers also observe the
    /// transition via `SourceChanged` if the label changed.
    abstract member RefreshAsync: ct: CancellationToken -> Task<DictionaryStateUpdate>
