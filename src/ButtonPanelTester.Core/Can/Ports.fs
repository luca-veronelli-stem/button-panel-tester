namespace Stem.ButtonPanelTester.Core.Can

open System
open System.Threading
open System.Threading.Tasks

/// Single raw frame received off the CAN bus, per
/// `specs/003-panel-discovery/contracts/can-frame-stream-port.md` §Port
/// definition. The `[<Struct>]` attribute keeps the value-type carrier
/// allocation-free on the receive thread per `research.md` R3; the
/// `Payload` is a `ReadOnlyMemory<byte>` over the vendored stack's
/// reassembled buffer (valid only for the duration of the `OnNext`
/// callback — see the contract's Threading section).
[<Struct>]
type RawCanFrame =
    { CanId: uint32
      Payload: ReadOnlyMemory<byte>
      ReceivedAt: DateTimeOffset }

/// Lifecycle port for the PEAK CAN adapter, per
/// `contracts/can-link-port.md` §Port definition. Implements FR-001,
/// FR-002, FR-002a, FR-003, FR-004, FR-005, FR-006 (lifecycle slice).
///
/// Production adapter `PcanCanLink` lands in T035 (US1 / PR-C);
/// virtual adapter `InMemoryCanLink` lands in T019 (commit 8 of this
/// PR-B) and drives the property + integration test surface.
///
/// `OpenAsync` is idempotent — calling on an already-open link is a
/// no-op (does NOT fire `LinkStateChanged`). `CloseAsync` mirrors
/// the same idempotence rule for the down state. `ReconnectAsync` is
/// the "close then open" composition the technician triggers from the
/// Reconnect button (FR-003).
type ICanLink =
    /// Opens the configured adapter at the given baud rate (always
    /// `250000` for spec-002). Fires `LinkStateChanged` with
    /// `Connected` on success; with `Disconnected` or `Error` on
    /// failure. Idempotent per lifecycle invariant #1.
    abstract member OpenAsync: baudrateBps: int * cancellationToken: CancellationToken -> Task

    /// Closes the adapter. Fires `LinkStateChanged` with
    /// `Disconnected(ReconnectPending, now)` if the link was up;
    /// otherwise a no-op (lifecycle invariant #2).
    abstract member CloseAsync: cancellationToken: CancellationToken -> Task

    /// Reconnect = Close then Open. Equivalent to the technician
    /// clicking the reconnect button. Always fires at least one
    /// `LinkStateChanged` (lifecycle invariant #3).
    abstract member ReconnectAsync: cancellationToken: CancellationToken -> Task

    /// Hot observable of link-state transitions. Subscribers added
    /// after a transition do NOT replay it — subscribe at composition
    /// time. Events may fire on the vendored stack's read thread;
    /// the GUI layer marshals to the UI thread via FuncUI's
    /// `Cmd.ofSub`.
    abstract member LinkStateChanged: IObservable<CanLinkState>

    /// Current state at the moment of read. Pull-style accessor for
    /// late subscribers, GUI binding, and snapshot tests. Safe to
    /// read from any thread (the production adapter uses an
    /// `Interlocked` read of an atomic reference). Consistent with
    /// the latest `OnNext` on `LinkStateChanged` per lifecycle
    /// invariant #5.
    abstract member CurrentState: CanLinkState

/// Receive port for the raw CAN frame stream, per
/// `contracts/can-frame-stream-port.md` §Port definition. Implements
/// FR-007 (receive side).
///
/// Production adapter `PcanCanFrameStream` lands in T044 (US2 / PR-D);
/// virtual adapter `InMemoryCanFrameStream` lands in T020 (commit 8
/// of this PR-B).
///
/// Frames received while the link is down are dropped silently (no
/// buffering across reconnects, per the contract's Adapter section).
/// All filtering on `CanId` / `Payload.Length` happens in the service
/// layer (`PanelDiscoveryService`); the port is a generic receive surface
/// that later specs reuse for transmit-side responses.
type ICanFrameStream =
    /// Hot observable of every raw CAN frame received while the link
    /// is up. Fires on the vendored stack's read thread; the
    /// `ReadOnlyMemory<byte>` payload is only valid for the duration
    /// of the `OnNext` callback — subscribers that need to retain the
    /// bytes MUST copy (`payload.Span.ToArray()` or `payload.ToArray()`).
    abstract member RawFramesReceived: IObservable<RawCanFrame>

/// Reassembled-WHO_I_AM port, per
/// `specs/003-panel-discovery/contracts/who-i-am-wire-format.md` §Receive + parse
/// contract. The reassembly adapter (Infrastructure, T036) is the only production
/// implementer; `PanelDiscoveryService` (T038) consumes this in place of the raw
/// `ICanFrameStream`. Emits one `WhoIAmFrame` per reassembled + command-matched +
/// parsed WHO_I_AM; every drop axis is a silent non-event (FR-007 — no Error surface).
/// Receive-only (FR-009). (FR-001)
type IWhoIAmObserver =
    /// Hot observable of decoded WHO_I_AM frames. Fires on the vendored read thread;
    /// subscribe at composition time (late subscribers do not replay).
    abstract member WhoIAmObserved: IObservable<WhoIAmFrame>
