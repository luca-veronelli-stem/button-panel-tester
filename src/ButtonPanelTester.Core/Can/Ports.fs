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
/// `specs/003-panel-discovery/contracts/can-frame-stream-port.md` §Port
/// definition. Implements FR-001 (receive side).
///
/// Production adapter `PcanCanFrameStream` (Infrastructure, spec-003 T017)
/// translates the vendored stack's reassembled packets into `RawCanFrame`s;
/// the virtual `InMemoryCanFrameStream` fake drives the test surface.
///
/// Frames received while the link is down are dropped silently (no
/// buffering across reconnects, per the contract's Adapter section).
/// WHO_I_AM is a segmented multi-frame message, so `CanId = 0x1FFFFFFF`
/// filtering and reassembly happen downstream in `WhoIAmReassemblyObserver`
/// (Infrastructure, spec-003 T036), not in this port; the port is a generic
/// receive surface that later specs reuse for transmit-side responses.
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

/// Decoded SP_APP VAR_WRITE button-state heartbeats from the panel under test, per
/// `specs/005-button-press-test/contracts/button-state-observer-port.md` (fix #270). Emits one
/// `ButtonStateObservation` per reassembled + command-matched + address-matched + parsed VAR_WRITE
/// arriving on a **directed CAN ID whose machineType (bits 23-16) decodes to a known Marketing
/// variant** — that variant rides in the observation. The broadcast id 0x1FFFFFFF (-> Virgin), the
/// tool SRID 0x00000008 (-> Unknown), the virgin sentinel address 0x80FE, and non-button addresses
/// are dropped (silent non-events). A directed button-state frame IS the evidence that a baptized
/// panel of that variant is present — the consumer keys observability off frame recency, not
/// WHO_I_AM discovery. Edge detection is the consumer's job — the observer is stateless w.r.t.
/// press/release. Receive-only.
type IButtonStateObserver =
    /// Hot observable of decoded button-state observations (frame + variant-from-CAN-ID). Fires on
    /// the vendored read thread; late subscribers do not replay.
    abstract member ButtonStateObserved: IObservable<ButtonStateObservation>

/// Receive port for the SET_ADDRESS application ACK the slave's protocol dispatcher returns to
/// the tool. The dispatcher (`SP_App_ProcessDataRx`, `SP_Application.c:347-360`) ORs `0x80` into
/// the command for every fully-received command whose handler returns true, so `0x80|0x25` is the
/// SET_ADDRESS ACK (`0x80|0x23` the WHO_ARE_YOU ACK — the F6 discriminator). The SET_ADDRESS
/// handler returns true regardless of UUID match, so this ACK is only a *fast positive* that the
/// assignment was received intact — confirmed broadcast-silence stays the authoritative adoption
/// signal (FR-006). The TX port stays fire-and-forget (the slave sends no domain reply); this is
/// the sole RX surface for the ACK, an adoption fast-positive (D1). Receive-only.
///
/// Contract of record:
/// `specs/004-baptism-workflow/contracts/master-sequence-wire-format.md` §SET_ADDRESS §Slave
/// semantics. The confirmation model that consumes it is
/// `specs/004-baptism-workflow/confirmation-rework/data-model.md` §4.3 (the `SetAddressAcked`
/// event observed in `AwaitingAdoption`) — that consumer is a later slice; this port is
/// additive and consumed by nothing yet.
type ISetAddressAckObserver =
    /// Hot observable that fires once per observed SET_ADDRESS ACK addressed to the tool, carrying
    /// the frame's receive timestamp. Fires on the vendored read thread; subscribe at composition
    /// time (late subscribers do not replay). The load-bearing fact is that it fired — the
    /// timestamp is for audit/correlation, not control flow (silence is authoritative).
    abstract member SetAddressAckObserved: IObservable<DateTimeOffset>

/// Transmits the auto-address master-sequence commands this tool is allowed to send
/// (FR-014: claim / reset via WHO_ARE_YOU, address assignment via SET_ADDRESS — nothing
/// else). The product's first CAN transmit port and the single TX entry point — not a
/// general-purpose CAN TX surface: adding any new command to the bus requires a spec that
/// amends FR-014 and the port contract, never a new method here "while we're at it".
///
/// Contract of record:
/// `specs/004-baptism-workflow/contracts/master-sequence-transmitter-port.md`; wire shapes
/// per `specs/004-baptism-workflow/contracts/master-sequence-wire-format.md`.
///
/// Semantics (contract §Semantics):
///   - Write-completion contract: a completed task means the message was written to the
///     bus (driver accepted all frames), NOT that any panel acted on it. Success/failure
///     of the *sequence* is the `BaptismService`'s judgement (FSM), never the port's.
///   - Fault mapping: any adapter exception maps to the service's `TransmissionFailure`
///     outcome, naming the step (FR-005). The port does not retry; the service does not
///     retry.
///   - No queuing/coalescing: each call is one SP_APP message, sent immediately. Callers
///     serialize (the FSM runs at most one attempt; reset's two fwType broadcasts are
///     awaited sequentially).
///   - Cancellation: honors `ct` co-operatively before/between frame writes; cancellation
///     surfaces as `OperationCanceledException`, never a transmission failure.
type IMasterSequenceTransmitter =
    /// Broadcasts WHO_ARE_YOU(machineType, fwType, reset). Completes when the write
    /// has been handed to the bus driver successfully; faults on transmission failure.
    abstract member SendWhoAreYouAsync:
        machineType: byte * fwType: uint16 * reset: bool * ct: CancellationToken -> Task

    /// Broadcasts SET_ADDRESS(uuid, spAddress). The uuid is re-encoded byte-identically
    /// to the announcement it was parsed from (byte-echo invariant). Completes on write
    /// completion; faults on transmission failure. The slave never replies (FR-006/FR-010).
    abstract member SendSetAddressAsync:
        uuid: PanelUuid * spAddress: uint32 * ct: CancellationToken -> Task
