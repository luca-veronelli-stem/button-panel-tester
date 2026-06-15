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
