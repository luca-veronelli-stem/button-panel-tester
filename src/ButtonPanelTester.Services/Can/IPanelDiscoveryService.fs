namespace Stem.ButtonPanelTester.Services.Can

open System
open Stem.ButtonPanelTester.Core.Can

/// Service-level facade exposing the panels-on-bus discovery surface,
/// split out of `ICanLinkService` (#197) so spec-003 can own the
/// discovery pipeline as an independent spec. The CAN lifecycle service
/// (`ICanLinkService`) stays lifecycle-only; this interface carries the
/// discovery snapshot + change feed.
///
/// `PanelsOnBus` is a pull-style accessor for snapshot tests and GUI
/// binding; `PanelsOnBusChanged` is the hot observable the
/// `PanelsOnBusView` subscribes through `Cmd.ofSub` to fill the third UI
/// slot. The concrete `PanelDiscoveryService` runs the live WHO_I_AM
/// ingest / parse / prune pipeline (spec-003), re-sourced onto the
/// reassembled `IWhoIAmObserver` feed.
type IPanelDiscoveryService =
    /// Current Panels-on-bus snapshot at the moment of read. Returns
    /// `PanelsOnBus.empty` until the link is `Connected` and at least
    /// one WHO_I_AM frame has been observed (spec-003).
    abstract member PanelsOnBus: PanelsOnBus

    /// Hot observable of Panels-on-bus updates. Subscribers added after
    /// an update do NOT replay it — subscribe at composition time. Emits
    /// on observe, prune, and link-loss clear (FR-008).
    abstract member PanelsOnBusChanged: IObservable<PanelsOnBus>
