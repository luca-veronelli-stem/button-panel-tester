namespace Stem.ButtonPanelTester.Core.Can

/// One accepted button-state heartbeat from the panel under test: the decoded
/// `VAR_WRITE` frame plus the `MarketingVariant` derived from the reassembled
/// packet's **senderId**, per
/// `specs/005-button-press-test/contracts/button-state-observer-port.md` and the
/// wire-format section "Destination addressing + variant-from-senderId match
/// rule" (fix #296, superseding the Session-2026-06-24 variant-from-CAN-ID rule
/// of fix #270).
///
/// A baptized panel is silent on WHO_I_AM (`AAS_STAND_BY`; `CORRECTIONS.md` §C1)
/// and instead heartbeats its button-state **addressed to the master that
/// baptized it**: the CAN arbitration ID is the DESTINATION (the stored
/// `MotherBoardAddress`), so for a panel this tool baptized it is the tool's own
/// SRID `0x00000008`. The panel's OWN address rides inside the packet as the
/// senderId, whose machineType byte (bits 23-16) identifies the variant. So the
/// observation still carries the variant the panel was baptized as **without any
/// baptism plumbing or discovery** — it is read off the payload, not off the
/// arbitration id. The consumer keys observability, panel-loss, and the prompt
/// schema off this envelope.
///
/// `Variant` is already filtered to a known `MarketingVariant`: the observer
/// only emits an observation when `variantOfSenderId` decodes the packet's
/// senderId to `Marketing _`. Plain record, not a closed taxonomy —
/// `MarketingVariant`'s closure triple lives with that DU
/// (`PanelObservation.fs`); this envelope adds none of its own.
type ButtonStateObservation =
    { Frame: ButtonStateFrame
      Variant: MarketingVariant }

module ButtonStateObservation =

    /// Decode the marketing variant a word carrying the `SP_App_Calculate_ID`
    /// layout belongs to: extract the machineType byte at bits 23-16
    /// (`(word >>> 16) &&& 0xFF`, the
    /// `network<<24 | machineType<<16 | (fwType&0x3FF)<<6 | board` layout,
    /// `research.md` §R1) and run the shared total decoder
    /// `VariantDecoder.decode`.
    ///
    /// This is the machineType-WORD decode, not an accept rule of its own: under
    /// fix #270 the word was taken to be the frame's arbitration id; since fix
    /// #296 the observer applies the same extraction to the packet's senderId
    /// (`variantOfSenderId`), because the arbitration id is the DESTINATION.
    ///
    /// The Lean definitions `machineTypeByte` / `variantOfDirectedId` and the
    /// theorem `machine_type_at_bits_23_16` in
    /// `Phase4/ButtonStateObservation.lean` (T044, re-documented T055) mechanise
    /// the extraction; the FsCheck properties in
    /// `Tests/Property/Can/ButtonStateObservationProperties.fs` (T045) witness
    /// them at the value level.
    let variantOfDirectedId (canId: uint32) : VariantIdentity =
        VariantDecoder.decode (MachineTypeByte(byte ((canId >>> 16) &&& 0xFFu)))

    /// Decode the marketing variant a reassembled packet's **senderId** belongs
    /// to — the accept rule's variant since fix #296. The senderId (bytes 1-4 of
    /// the transport packet, big-endian) carries the panel's OWN SP address in
    /// the `SP_App_Calculate_ID` layout, so its machineType byte at bits 23-16 is
    /// the variant; the arbitration id the heartbeat arrived on is the
    /// destination and is never consulted. Returns the full `VariantIdentity`:
    /// the observer accepts only `Marketing _`.
    ///
    /// Mechanised by the Lean theorems `variant_from_sender_id` (this extraction
    /// applied to the senderId word) and `arbitration_id_irrelevant` (acceptance
    /// and variant invariant under the arbitration id) in
    /// `Phase4/ButtonStateObservation.lean` (T055), per the wire-format section
    /// "Destination addressing + variant-from-senderId match rule (fix #296)".
    let variantOfSenderId (senderId: uint32) : VariantIdentity =
        variantOfDirectedId senderId
