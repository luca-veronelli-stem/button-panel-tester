namespace Stem.ButtonPanelTester.Core.Can

/// One accepted button-state heartbeat from the panel under test: the decoded
/// `VAR_WRITE` frame plus the `MarketingVariant` derived from the directed CAN
/// ID it arrived on, per
/// `specs/005-button-press-test/contracts/button-state-observer-port.md` and the
/// Session-2026-06-24 clarification (fix #270).
///
/// A baptized panel is silent on WHO_I_AM (`AAS_STAND_BY`; `CORRECTIONS.md` §C1)
/// and instead heartbeats its button-state on a **directed CAN ID** whose
/// machineType byte (bits 23-16) identifies the variant. So the observation
/// carries the variant the panel was baptized as **without any baptism plumbing
/// or discovery** — it is read off the frame's own CAN ID. The consumer keys
/// observability, panel-loss, and the prompt schema off this envelope.
///
/// `Variant` is already filtered to a known `MarketingVariant`: the observer
/// only emits an observation when `variantOfDirectedId` decodes the CAN ID to
/// `Marketing _` (broadcast / virgin / the tool SRID decode to non-marketing and
/// are dropped at the observer boundary). Plain record, not a closed taxonomy —
/// `MarketingVariant`'s closure triple lives with that DU
/// (`PanelObservation.fs`); this envelope adds none of its own.
type ButtonStateObservation =
    { Frame: ButtonStateFrame
      Variant: MarketingVariant }

module ButtonStateObservation =

    /// Decode the marketing variant a **directed** SP_App CAN ID belongs to:
    /// extract the machineType byte at bits 23-16 (`(CanId >>> 16) &&& 0xFF`,
    /// the `network<<24 | machineType<<16 | (fwType&0x3FF)<<6 | board` layout,
    /// `research.md` §R1) and run the shared total decoder
    /// `VariantDecoder.decode`. Returns the full `VariantIdentity`: the observer
    /// accepts only `Marketing _` and drops `Virgin` (broadcast `0x1FFFFFFF` →
    /// `0xFF`) / `Unknown` (the tool SRID `0x00000008` → `0x00`).
    ///
    /// The Lean theorems `machine_type_at_bits_23_16` and
    /// `non_marketing_ids_rejected` in `Phase4/ButtonStateObservation.lean`
    /// (T044) mechanise the extraction + rejection; the FsCheck properties in
    /// `Tests/Property/Can/ButtonStateObservationProperties.fs` (T045) witness
    /// them at the value level.
    let variantOfDirectedId (canId: uint32) : VariantIdentity =
        VariantDecoder.decode (MachineTypeByte(byte ((canId >>> 16) &&& 0xFFu)))
