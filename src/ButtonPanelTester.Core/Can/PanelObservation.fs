namespace Stem.ButtonPanelTester.Core.Can

open System

/// Closed taxonomy of the four marketing variants spec-003 must
/// distinguish, per `specs/003-panel-discovery/data-model.md`
/// §2.1. Each variant corresponds to one `machineType` byte:
///   * `EdenXp`     — `0x03`
///   * `OptimusXp`  — `0x0A`
///   * `R3LXp`      — `0x0B`
///   * `EdenBs8`    — `0x0C`
///
/// Source for the byte-to-variant mapping is the audit in
/// `docs/Context/bpt-rollout/CORRECTIONS.md` §"Items unchanged", which
/// pins each motherboard's `ID_MACHINE_TYPE` constant. The names are
/// the marketing labels rendered in the Panels-on-bus list row
/// (FR-003).
type MarketingVariant =
    | EdenXp
    | OptimusXp
    | R3LXp
    | EdenBs8

/// Three-case decoded identity of a panel observed on the bus, per
/// `data-model.md` §2.1. `Marketing` carries one of the four known
/// production variants; `Virgin` is the unique value emitted by the
/// `0xFF` machineType (pristine panel that has not yet been claimed
/// by a master); `Unknown` carries the raw byte for any other value
/// so the GUI's detail affordance can render it without the decoder
/// losing information (FR-003).
type VariantIdentity =
    | Marketing of MarketingVariant
    | Virgin
    | Unknown of raw: byte

/// Single observation of a panel on the bus, per `data-model.md` §3.1.
/// `LastSeen` is set to `IClock.UtcNow()` at the moment the `WHO_I_AM`
/// frame is observed by `PanelDiscoveryService`;
/// `VariantByte` is the raw byte from the frame (preserved alongside
/// the decoded `VariantIdentity` so the GUI's detail affordance can
/// render it for the `Unknown` and `Virgin` cases without re-deriving
/// it from the identity).
/// `FwType` is the raw announced fwType word carried through from the
/// already-parsed `WhoIAmFrame`, per spec-004 `data-model.md` §3 and
/// research R2: the `WHO_ARE_YOU` claim must echo this value or the
/// slave ignores it. Additive — latest announcement wins under
/// coalescing, same as every other field.
type PanelObservation =
    { Uuid: PanelUuid
      VariantByte: MachineTypeByte
      VariantIdentity: VariantIdentity
      FwType: uint16
      LastSeen: DateTimeOffset }

module VariantDecoder =

    /// Total decoder mapping every `MachineTypeByte` to a
    /// `VariantIdentity`, per `data-model.md` §2.1 and FR-003.
    ///   * `0x03`         → `Marketing EdenXp`
    ///   * `0x0A`         → `Marketing OptimusXp`
    ///   * `0x0B`         → `Marketing R3LXp`
    ///   * `0x0C`         → `Marketing EdenBs8`
    ///   * `0xFF`         → `Virgin`
    ///   * any other byte → `Unknown raw`
    /// Totality is mechanised by the Lean theorem
    /// `variant_decoding_total` in `Phase2/PanelObservation.lean`
    /// (T029) and witnessed at the value level by the FsCheck property
    /// `VariantByteMappingTotal` in
    /// `Tests/Property/Can/VariantDecoderProperties.fs` (T023).
    let decode (MachineTypeByte raw) : VariantIdentity =
        match raw with
        | 0x03uy -> Marketing EdenXp
        | 0x0Auy -> Marketing OptimusXp
        | 0x0Buy -> Marketing R3LXp
        | 0x0Cuy -> Marketing EdenBs8
        | 0xFFuy -> Virgin
        | other -> Unknown other
