namespace Stem.ButtonPanelTester.Services.Can

open System
open System.Threading
open System.Threading.Tasks
open Stem.ButtonPanelTester.Core.Can

/// Service-level facade over the `ICanLink` port, per
/// `specs/002-can-link-lifecycle/plan.md` and `data-model.md` §1 / §5.
/// Concrete implementation `CanLinkService` lands in T036 (US1 / PR-C,
/// lifecycle slice). The interface itself ships here so the GUI
/// composition root + downstream test wiring can take a dependency
/// without coupling to a particular implementation.
///
/// Exposes the CAN-link lifecycle only. `CurrentState` is a pull-style
/// accessor for snapshot tests and GUI binding; `LinkStateChanged` is
/// the hot observable FuncUI subscribes through `Cmd.ofSub`.
/// `InitializeAsync` is the FR-001 boot entrypoint called after
/// `IDictionaryService.InitializeAsync` from `App.fs`; `ReconnectAsync`
/// is the FR-003 Reconnect button binding. Panel discovery moved to
/// `IPanelDiscoveryService` (#197).
type ICanLinkService =
    /// Current link state at the moment of read. Pull-style accessor
    /// consistent with the latest `OnNext` on `LinkStateChanged`.
    abstract member CurrentState: CanLinkState

    /// Hot observable of link-state transitions. Subscribers added
    /// after a transition do NOT replay it — subscribe at composition
    /// time. The underlying subject fires on the link's emission
    /// thread (the vendored PCANManager monitor task); FuncUI marshals
    /// to the UI thread.
    abstract member LinkStateChanged: IObservable<CanLinkState>

    /// FR-001 boot entrypoint. Called by `App.fs` after the dictionary
    /// service has initialised; drives the underlying `ICanLink.OpenAsync`.
    abstract member InitializeAsync: cancellationToken: CancellationToken -> Task

    /// FR-003 Reconnect-button binding. Calls the underlying
    /// `ICanLink.ReconnectAsync` and applies the per-cause Recoverable
    /// → Fatal escalation logic per `research.md` R8.
    abstract member ReconnectAsync: cancellationToken: CancellationToken -> Task
