namespace Stem.ButtonPanelTester.Services.Can

open System
open System.Threading
open System.Threading.Tasks
open Stem.ButtonPanelTester.Core.Can

/// Service-level facade over the `ICanLink` + `ICanFrameStream` ports,
/// per `specs/002-can-link-and-panel-discovery/plan.md` and
/// `data-model.md` §1 / §5. Concrete implementation `CanLinkService`
/// lands in T036 (US1 / PR-C, lifecycle slice) with the observation
/// pipeline extension in T045–T047 (US2 / PR-D). The interface itself
/// ships here so the GUI composition root + downstream test wiring
/// can take a dependency without coupling to a particular
/// implementation.
///
/// `CurrentState` / `PanelsOnBus` are pull-style accessors for
/// snapshot tests and GUI binding; `LinkStateChanged` /
/// `PanelsOnBusChanged` are the hot observables FuncUI subscribes
/// through `Cmd.ofSub`. `InitializeAsync` is the FR-001 boot
/// entrypoint called after `IDictionaryService.InitializeAsync` from
/// `App.fs`; `ReconnectAsync` is the FR-003 Reconnect button binding.
type ICanLinkService =
    /// Current link state at the moment of read. Pull-style accessor
    /// consistent with the latest `OnNext` on `LinkStateChanged`.
    abstract member CurrentState: CanLinkState

    /// Current Panels-on-bus snapshot at the moment of read. Returns
    /// `PanelsOnBus.empty` until the link is `Connected` and at least
    /// one WHO_I_AM frame has been observed.
    abstract member PanelsOnBus: PanelsOnBus

    /// Hot observable of link-state transitions. Subscribers added
    /// after a transition do NOT replay it — subscribe at composition
    /// time. The underlying subject fires on the service's pruning-
    /// timer / receive-thread hop chain; FuncUI marshals to the UI
    /// thread.
    abstract member LinkStateChanged: IObservable<CanLinkState>

    /// Hot observable of Panels-on-bus updates. Emits on observe,
    /// prune, and link-loss clear (FR-015).
    abstract member PanelsOnBusChanged: IObservable<PanelsOnBus>

    /// FR-001 boot entrypoint. Called by `App.fs` after the dictionary
    /// service has initialised; drives the underlying `ICanLink.OpenAsync`
    /// and starts the observation pipeline if the open succeeds.
    abstract member InitializeAsync: cancellationToken: CancellationToken -> Task

    /// FR-003 Reconnect-button binding. Calls the underlying
    /// `ICanLink.ReconnectAsync` and applies the per-cause Recoverable
    /// → Fatal escalation logic per `research.md` R8.
    abstract member ReconnectAsync: cancellationToken: CancellationToken -> Task
