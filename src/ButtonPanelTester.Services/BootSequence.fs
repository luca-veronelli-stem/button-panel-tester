module Stem.ButtonPanelTester.Services.BootSequence

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Stem.ButtonPanelTester.Services.Can
open Stem.ButtonPanelTester.Services.Dictionary

/// FR-001 boot-order orchestration: open the CAN adapter ONLY after
/// `IDictionaryService.InitializeAsync` has completed. Lives in
/// `Services` rather than `GUI` because the ordering is a domain
/// invariant; the GUI delegates to it instead of inlining the
/// `let! _ = initTask` + `canLinkService.InitializeAsync` pair.
///
/// The dictionary task is passed in (rather than the
/// `IDictionaryService` itself) so callers can kick off the
/// dictionary fetch before this function runs — e.g. `App.fs`
/// overlaps the registration ceremony with the dictionary boot to
/// keep first-frame latency low. FR-001 constrains only the
/// dict-then-CAN ordering, not when the dictionary task starts.
///
/// `canLinkService.InitializeAsync` failures are caught and logged:
/// the production CAN service surfaces open failures via
/// `LinkStateChanged` so the row chip reflects the error as a state
/// transition. Propagating the exception here would tear down the
/// GUI's `Opened` handler. `OperationCanceledException` during
/// shutdown is treated the same way.
let runBootSequence
    (dictionaryInit: Task<DictionaryStateUpdate>)
    (canLinkService: ICanLinkService)
    (logger: ILogger)
    (cancellationToken: CancellationToken)
    : Task<unit> =
    task {
        let! _ = dictionaryInit

        try
            do! canLinkService.InitializeAsync(cancellationToken)
        with
        | :? OperationCanceledException -> ()
        | ex ->
            logger.LogWarning(
                ex,
                "ICanLinkService.InitializeAsync raised; CAN row will reflect via LinkStateChanged"
            )

        return ()
    }
