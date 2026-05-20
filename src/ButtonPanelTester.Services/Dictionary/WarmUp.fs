namespace Stem.ButtonPanelTester.Services.Dictionary

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Stem.ButtonPanelTester.Core.Dictionary

/// Startup warm-up for the dictionary service, per
/// `specs/001-fetch-dictionary/phases/phase-7.md` (issue #92, slice 2).
///
/// The dictionary endpoint is hosted on Azure App Service Free tier
/// with Always-On off — the worker process unloads after ~30 min of
/// idle traffic, and the first request after idle pays cold-boot
/// latency (max observed 89.91 s, mean 42 s; PR #91 diagnostics).
/// The slice-1 timeout raise to 90 s absorbs that wait inside the
/// production fetch path; this warm-up makes the wait invisible to
/// the technician by paying it during app startup, before the first
/// Refresh click.
///
/// Semantics:
///   - Calls `IDictionaryProvider.FetchAsync` exactly once.
///   - Logs the observed wall-clock duration (via
///     `System.Diagnostics.Stopwatch`, monotonic and decoupled from
///     `IClock`) and the result classification at `Information`.
///   - Swallows any non-cancellation outcome — the warm-up is
///     fire-and-forget, and the production fetch path (which the
///     technician triggers via Refresh) is the source of truth for
///     surfaced errors.
///   - Propagates `OperationCanceledException` if the caller's `ct`
///     fired, so the GUI shutdown path can cancel an in-flight
///     warm-up cleanly.
[<RequireQualifiedAccess>]
module WarmUp =

    /// Run one warm-up `FetchAsync` against the supplied provider.
    /// Logs the elapsed duration plus the result classification at
    /// `Information`; returns successfully whether the fetch
    /// succeeded or failed. Cancellation from `ct` propagates.
    let runAsync
        (provider: IDictionaryProvider)
        (logger: ILogger)
        (ct: CancellationToken)
        : Task<unit> =
        task {
            let sw = Stopwatch.StartNew()

            try
                let! result = provider.FetchAsync(ct)
                sw.Stop()

                match result with
                | Success(_, _) ->
                    logger.LogInformation(
                        "Dictionary warm-up succeeded in {ElapsedMilliseconds} ms.",
                        sw.ElapsedMilliseconds
                    )
                | Failed(reason, _) ->
                    logger.LogInformation(
                        "Dictionary warm-up returned {Reason} after {ElapsedMilliseconds} ms (production fetch path handles user-visible retry).",
                        reason,
                        sw.ElapsedMilliseconds
                    )
            with
            // OperationCanceledException is intentionally NOT caught here:
            // it propagates so the GUI shutdown path observes the
            // cancellation deterministically. The `when` guard filters
            // it back out of the swallow path.
            | ex when not (ex :? OperationCanceledException) ->
                if sw.IsRunning then sw.Stop()

                logger.LogWarning(
                    ex,
                    "Dictionary warm-up threw after {ElapsedMilliseconds} ms; ignoring (production fetch path will surface real errors).",
                    sw.ElapsedMilliseconds
                )
        }
