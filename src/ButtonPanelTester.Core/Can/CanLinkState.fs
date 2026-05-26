namespace Stem.ButtonPanelTester.Core.Can

open System

/// Identifies the PEAK adapter underlying a `Connected` `CanLinkState`,
/// per `specs/002-can-link-and-panel-discovery/data-model.md` §6.
///
/// Rendered in the CAN status row's detail affordance (FR-004). Carries
/// the channel name (e.g. `PCAN-USB Pro FD (1)`), the PEAK-reported
/// serial number, and the negotiated bitrate (always `250000` in
/// spec-002 per `quickstart.md`). All three fields are local-only by
/// construction — the type lives in the GUI render path and has no
/// telemetry / serialisation surface (Principle V).
///
/// Defined here, alongside `CanLinkState`, rather than in `Ports.fs`
/// (T017) so the `Connected` case can reference it without a
/// forward-declared abstract carrier — keeps Core self-contained per
/// the T012 note in `tasks.md`.
type AdapterIdentification =
    { ChannelName: string
      DeviceId: string
      BaudrateBps: int }

/// Closed taxonomy of reasons a `CanLinkState.Disconnected` may carry,
/// per `data-model.md` §1.1. Each case is distinguished in the GUI:
///   - `NoAdapterPresent`  — boot-time absence (FR-002, FR-005 hint).
///   - `LinkNotYetOpened`  — `Initializing` follow-on before the first
///                           Open attempt; useful for diagnostics in
///                           the detail affordance.
///   - `MidSessionUnplug`  — Connected → Disconnected via hot-unplug
///                           (FR-005 distinct headline).
///   - `ReconnectPending`  — set by `CloseAsync` / `IAsyncDisposable`
///                           so the next `OpenAsync` is observable as
///                           a fresh attempt rather than a no-op.
type DisconnectReason =
    | NoAdapterPresent
    | LinkNotYetOpened
    | MidSessionUnplug
    | ReconnectPending

/// Two-case error sub-classification carried by `CanLinkState.Error`,
/// per `data-model.md` §1.1 and FR-002a. The detail string is the
/// human-readable PEAK status / cause shown in the status-row detail
/// affordance.
///
/// `Recoverable` is the first observation of a transient PEAK status
/// (e.g. `Bus-off detected — try reconnect`); `Fatal` is the
/// second observation of the same root cause across a reconnect
/// attempt, per the escalation logic that lives in `CanLinkService`
/// (`research.md` R8). The wire is the same; only the GUI's
/// remediation hint differs ("Try reconnect" vs. "Reconnect (unlikely
/// to help)").
type ErrorClassification =
    | Recoverable of detail: string
    | Fatal of detail: string

/// Closed taxonomy of CAN link states, per `data-model.md` §1.1.
/// Mirrors the Lean inductive in `lean/Stem/ButtonPanelTester/Phase2/
/// CanLinkState.lean`; the F# DU is the surface implementation and
/// the Lean theorem `state_classification_total` mechanises the
/// classification totality invariant (Invariant #1).
///
/// Transitions are documented in `data-model.md` §1.2; the operational
/// `Recoverable → Fatal` escalation (Invariant #2) lives in
/// `CanLinkService` rather than here because it is a temporal property
/// across multiple Open attempts, not an algebraic one.
type CanLinkState =
    | Initializing
    | Connected of adapter: AdapterIdentification * openedAt: DateTimeOffset
    | Disconnected of reason: DisconnectReason * since: DateTimeOffset
    | Error of classification: ErrorClassification * since: DateTimeOffset
