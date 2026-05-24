namespace Stem.ButtonPanelTester.Core.Can

open System

/// UUID-keyed map of every panel currently observed on the bus, per
/// `specs/002-can-link-and-panel-discovery/data-model.md` §5.1. Keying
/// by `PanelUuid` is what guarantees coalescing (FR-008): re-broadcasts
/// of the same panel update the existing row in place rather than
/// adding a duplicate. The map's value carries the most recent
/// `PanelObservation` for that UUID.
type PanelsOnBus = Map<PanelUuid, PanelObservation>

module PanelsOnBus =

    /// The empty `PanelsOnBus`. Used at construction time inside
    /// `CanLinkService` and as the post-`clear` value for FR-015
    /// link-loss handling.
    let empty: PanelsOnBus = Map.empty

    /// Insert-or-update the row keyed by `f.Uuid` with a fresh
    /// `PanelObservation` derived from the incoming `WhoIAmFrame` and
    /// the receive timestamp `now`. Per `data-model.md` §5.3:
    ///   * existing rows have their `LastSeen` advanced to `now`;
    ///   * `VariantByte` and `VariantIdentity` are re-derived from the
    ///     latest frame so a panel that power-cycled out of
    ///     `AAS_STAND_BY` mid-session is reflected accurately.
    /// The `count` invariant of `data-model.md` §5.4 is mechanised by
    /// the Lean theorem `observe_coalesces_by_uuid` in
    /// `Phase2/PanelsOnBus.lean` (T030).
    let observe (now: DateTimeOffset) (frame: WhoIAmFrame) (m: PanelsOnBus) : PanelsOnBus =
        let observation =
            { Uuid = frame.Uuid
              VariantByte = frame.MachineType
              VariantIdentity = VariantDecoder.decode frame.MachineType
              LastSeen = now }

        Map.add frame.Uuid observation m

    /// Discard every row regardless of `LastSeen`. Used by
    /// `CanLinkService` on the Connected → Disconnected transition
    /// (FR-015) so the GUI's Panels-on-bus list does not display
    /// stale rows after the adapter goes away.
    let clear (_: PanelsOnBus) : PanelsOnBus = empty
