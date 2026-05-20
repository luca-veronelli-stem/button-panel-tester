namespace Stem.ButtonPanelTester.Services.Dictionary

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Stem.ButtonPanelTester.Core.Dictionary

/// Startup warm-up orchestration for the dictionary service, per
/// `specs/001-fetch-dictionary/phases/phase-7.md` (issue #92).
///
/// The dictionary endpoint is hosted on Azure App Service Free tier
/// with Always-On off — the worker process unloads after ~30 min of
/// idle traffic, and the first request after idle pays cold-boot
/// latency (max observed 89.91 s, mean 42 s; PR #91 diagnostics).
/// The slice-1 timeout raise to 90 s absorbs that wait inside the
/// production fetch path; this warm-up makes the wait invisible to
/// the technician by paying it during app startup, before the first
/// Refresh click or registration submit.
///
/// Semantics:
///   - Calls `IDictionaryServiceWarmUp.WarmUpAsync` exactly once.
///   - Logs the observed wall-clock duration (via
///     `System.Diagnostics.Stopwatch`, monotonic) at `Information`.
///   - Swallows any non-cancellation outcome — the warm-up is
///     fire-and-forget, and the production fetch path is the
///     source of truth for surfaced errors.
///   - Propagates `OperationCanceledException` if the caller's `ct`
///     fired, so the GUI shutdown path can cancel an in-flight
///     warm-up cleanly.
///
/// The class shape (not a module) satisfies the LOGGING standard's
/// `ILogger<T>` requirement for archetype A types.
type DictionaryWarmUp
    (
        warmUp: IDictionaryServiceWarmUp,
        logger: ILogger<DictionaryWarmUp>
    ) =

    /// Run one warm-up probe against the dictionary service. Logs
    /// the elapsed duration at `Information`; returns successfully
    /// whether the probe succeeded or threw a transport exception.
    /// Cancellation from `ct` propagates.
    member _.RunAsync(ct: CancellationToken) : Task<unit> = task {
        let sw = Stopwatch.StartNew()

        try
            do! warmUp.WarmUpAsync(ct)
            sw.Stop()

            logger.LogInformation(
                "Dictionary warm-up succeeded against /health in {ElapsedMilliseconds} ms.",
                sw.ElapsedMilliseconds
            )
        with
        // OperationCanceledException is intentionally NOT caught here:
        // it propagates so the GUI shutdown path observes the
        // cancellation deterministically. The `when` guard filters
        // it back out of the swallow path.
        | ex when not (ex :? OperationCanceledException) ->
            if sw.IsRunning then sw.Stop()

            logger.LogInformation(
                ex,
                "Dictionary warm-up failed after {ElapsedMilliseconds} ms; ignoring (production fetch path will surface real errors).",
                sw.ElapsedMilliseconds
            )
    }
