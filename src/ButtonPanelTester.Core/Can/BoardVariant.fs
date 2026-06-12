namespace Stem.ButtonPanelTester.Core.Can

/// Encode side of the variant/machine-identity-byte mapping, per
/// `specs/004-baptism-workflow/data-model.md` §1: spec-004's
/// BoardVariant rides the shipped `MarketingVariant` DU and adds the
/// byte-producing inverse of `VariantDecoder.decode` for the
/// WHO_ARE_YOU TX path.
module BoardVariant =

    /// Variant → machine-identity byte, per `data-model.md` §1 and the
    /// audited firmware constants (CORRECTIONS.md §"Items unchanged"):
    ///   * `EdenXp`    → `0x03`
    ///   * `OptimusXp` → `0x0A`
    ///   * `R3LXp`     → `0x0B`
    ///   * `EdenBs8`   → `0x0C`
    /// Total on the four `MarketingVariant` cases by construction —
    /// the wildcard-free `match` is load-bearing: a fifth variant
    /// breaks this function AND the Lean proof together. Partial
    /// inverse of the shipped total decoder: the Lean theorem
    /// `encode_decode_inverse` in
    /// `lean/Stem/ButtonPanelTester/Phase3/WhoAreYouFrame.lean` (T002)
    /// mechanises `decode (encode v) = Marketing v`, witnessed at the
    /// value level by the FsCheck property `VariantEncodeDecodeInverse`
    /// in `Tests/Property/Can/BoardVariantProperties.fs` (T008).
    let encode (variant: MarketingVariant) : byte =
        match variant with
        | EdenXp -> 0x03uy
        | OptimusXp -> 0x0Auy
        | R3LXp -> 0x0Buy
        | EdenBs8 -> 0x0Cuy

    /// Virgin identity marker `0xFF`, per `data-model.md` §1 and
    /// FR-008: the reset target ONLY, never a BoardVariant — it is
    /// deliberately outside `encode`'s range, and the baptize picker
    /// never offers it.
    let virginMarker = 0xFFuy
