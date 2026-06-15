namespace Stem.ButtonPanelTester.Services.Can

open System
open System.Threading
open System.Threading.Tasks
open Stem.ButtonPanelTester.Core.Can

/// Service-level facade driving the spec-004 baptism workflow: a modal,
/// single-attempt sequence that claims a selected virgin panel
/// (WHO_ARE_YOU), waits for its re-announcement, and — only on a matching
/// announcement — assigns its address (SET_ADDRESS), per
/// `specs/004-baptism-workflow/plan.md` and `data-model.md` §4. The
/// concrete `BaptismService` drives the pure FSM (`Baptism.step`, Core)
/// over the consumed observables (`IWhoIAmObserver`,
/// `IPanelDiscoveryService`, `ICanLinkService`) and the
/// `IMasterSequenceTransmitter` TX port.
///
/// `BaptizeAsync` is the FR-002 Baptize-button entrypoint; it is modal —
/// a second concurrent attempt while one is running is a programming bug
/// and throws `InvalidOperationException` (CHK013). `StateChanged` is the
/// hot observable the GUI subscribes through `Cmd.ofSub`; `CurrentState`
/// is the pull-style accessor consistent with the latest `StateChanged`
/// emission.
type IBaptismService =
    /// FR-002 Baptize-button entrypoint. Runs ONE attempt to baptize the
    /// `selected` panel as `variant`: re-checks the entry guards against
    /// the current link + discovery snapshot, fires the WHO_ARE_YOU claim
    /// (echoing the panel's announced fwType), waits up to
    /// `Baptism.announceBudget` (6 s, CHK010-anchored at claim-write
    /// completion) for a matching re-announcement, then assigns the
    /// address. Completes with the terminal `BaptismOutcome`. Throws
    /// `InvalidOperationException` if an attempt is already running
    /// (CHK013 modal contract). Cancellation surfaces as
    /// `OperationCanceledException`, never a `TransmissionFailure`.
    abstract member BaptizeAsync:
        selected: PanelUuid * variant: MarketingVariant * cancellationToken: CancellationToken ->
            Task<BaptismOutcome>

    /// Hot observable of FSM-state transitions. Subscribers added after a
    /// transition do NOT replay it — subscribe at composition time. Emits
    /// every non-absorbed transition of the attempt, terminal states
    /// included.
    abstract member StateChanged: IObservable<BaptismState>

    /// Current FSM state at the moment of read, consistent with the latest
    /// `StateChanged` emission. `Idle` between attempts.
    abstract member CurrentState: BaptismState

    /// Hot observable of the FR-007 post-success "claim did not take"
    /// warning, carrying the claimed `PanelUuid`. After an attempt reaches
    /// `Terminal Succeeded` the service watches for the claimed uuid for one
    /// 15 s pruning window (`data-model.md` §4.4): a panel that took the
    /// claim goes silent, so hearing its uuid again WITHIN the window means
    /// the claim did not take and fires this once. The window is anchored at
    /// the success instant; a new attempt or a link loss cancels the watch,
    /// and expiry is silent. Volatile in-memory only (FR-013). Hot —
    /// subscribers added after a raise do NOT replay it; subscribe at
    /// composition time. The GUI (Phase E) renders the uuid into the operator
    /// message.
    abstract member WarningRaised: IObservable<PanelUuid>

    /// FR-008/FR-009/FR-010 Reset-button entrypoint, behind the confirmation
    /// SEAM: the caller supplies the already-decided `confirmed` result (the
    /// GUI confirmation dialog is Phase E). Reset is a LINEAR flow, not an
    /// attempt FSM (`data-model.md` §5):
    ///   * `confirmed = false` → nothing is transmitted; completes `Declined`
    ///     (FR-009). The declined attempt still logs one audit record (SC-006).
    ///   * `confirmed = true` → broadcasts `WHO_ARE_YOU(0xFF, fwType, reset=1)`
    ///     once per known fwType (`0x0004` then `0x000F`, research R2), awaited
    ///     SEQUENTIALLY as one technician action; completes `Sent` when ALL
    ///     writes complete (write completion is the success signal — the
    ///     firmware never replies, FR-010). The link not `Connected` at entry
    ///     or leaving `Connected` between the two broadcasts completes
    ///     `ResetLinkLost`; a write fault completes `ResetTransmissionFailure`
    ///     with NO retry and no further send. There is no announcement wait.
    /// Cancellation surfaces as `OperationCanceledException`, never a
    /// `ResetTransmissionFailure`.
    abstract member ResetAsync: confirmed: bool * cancellationToken: CancellationToken -> Task<ResetOutcome>
