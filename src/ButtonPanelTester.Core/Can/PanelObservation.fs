namespace Stem.ButtonPanelTester.Core.Can

open System

/// Closed taxonomy of the four marketing variants spec-002 must
/// distinguish, per `specs/003-panel-discovery/data-model.md`
/// §3.1. Each variant corresponds to one `machineType` byte:
///   * `EdenXp`     — `0x03`
///   * `OptimusXp`  — `0x0A`
///   * `R3LXp`      — `0x0B`
///   * `EdenBs8`    — `0x0C`
///
/// Source for the byte-to-variant mapping is the audit in
/// `docs/Context/bpt-rollout/CORRECTIONS.md` §"Items unchanged", which
/// pins each motherboard's `ID_MACHINE_TYPE` constant. The names are
/// the marketing labels rendered in the Panels-on-bus list row
/// (FR-009).
type MarketingVariant =
    | EdenXp
    | OptimusXp
    | R3LXp
    | EdenBs8

/// Three-case decoded identity of a panel observed on the bus, per
/// `data-model.md` §3.1. `Marketing` carries one of the four known
/// production variants; `Virgin` is the unique value emitted by the
/// `0xFF` machineType (pristine panel that has not yet been claimed
/// by a master); `Unknown` carries the raw byte for any other value
/// so the GUI's detail affordance can render it without the decoder
/// losing information (FR-009).
type VariantIdentity =
    | Marketing of MarketingVariant
    | Virgin
    | Unknown of raw: byte

/// Single observation of a panel on the bus, per `data-model.md` §4.1.
/// `LastSeen` is set to `IClock.UtcNow()` at the moment the `WHO_I_AM`
/// frame was received by `CanLinkService` (T036 / T045 fold this in);
/// `VariantByte` is the raw byte from the frame (preserved alongside
/// the decoded `VariantIdentity` so the GUI's detail affordance can
/// render it for the `Unknown` and `Virgin` cases without re-deriving
/// it from the identity).
type PanelObservation =
    { Uuid: PanelUuid
      VariantByte: MachineTypeByte
      VariantIdentity: VariantIdentity
      LastSeen: DateTimeOffset }

module VariantDecoder =

    /// Total decoder mapping every `MachineTypeByte` to a
    /// `VariantIdentity`, per `data-model.md` §3.1 and FR-009.
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
