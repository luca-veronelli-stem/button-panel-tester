module Stem.ButtonPanelTester.Tests.Property.Can.VariantDecoderProperties

open FsCheck.FSharp
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Can

/// Six-way classification of `VariantIdentity`, mirroring the Lean
/// theorem `variant_decoding_total` in
/// `lean/Stem/ButtonPanelTester/Phase2/PanelObservation.lean` (T029).
/// The classifier covers the closure at the F# value level; the Lean
/// side covers it at the type level. Adding a sixth `MarketingVariant`
/// case would break this `match` (no wildcard arm) AND the Lean `cases`
/// proof, forcing a cross-layer update.
type private VariantClass =
    | EdenXpClass
    | OptimusXpClass
    | R3LXpClass
    | EdenBs8Class
    | VirginClass
    | UnknownClass

let private classify (v: VariantIdentity) : VariantClass =
    match v with
    | Marketing EdenXp -> EdenXpClass
    | Marketing OptimusXp -> OptimusXpClass
    | Marketing R3LXp -> R3LXpClass
    | Marketing EdenBs8 -> EdenBs8Class
    | Virgin -> VirginClass
    | Unknown _ -> UnknownClass

/// FsCheck property covering `data-model.md` §2.2 (totality): for
/// every `byte`, `VariantDecoder.decode` produces exactly one of the
/// six branches per FR-003. The wildcard-free `classify` `match`
/// above is load-bearing — a future seventh `VariantIdentity` case
/// would break elaboration here AND in the Lean theorem
/// `variant_decoding_total`.
[<Property>]
let VariantByteMappingTotal (raw: byte) =
    let identity = VariantDecoder.decode(MachineTypeByte raw)
    let cls = classify identity

    cls = EdenXpClass
    || cls = OptimusXpClass
    || cls = R3LXpClass
    || cls = EdenBs8Class
    || cls = VirginClass
    || cls = UnknownClass

/// FsCheck property covering the four well-known marketing-variant
/// bytes per the audit in `CORRECTIONS.md` §"Items unchanged".
/// Phrased as a per-byte switch so the contract is auditable in the
/// test source: a future change that altered any of the four mappings
/// would fail here even before reaching the totality property above.
[<Property>]
let VariantByteMapping_KnownMarketingBytes_DecodeToTheirVariant (raw: byte) =
    let identity = VariantDecoder.decode(MachineTypeByte raw)

    match raw with
    | 0x03uy -> identity = Marketing EdenXp
    | 0x0Auy -> identity = Marketing OptimusXp
    | 0x0Buy -> identity = Marketing R3LXp
    | 0x0Cuy -> identity = Marketing EdenBs8
    | 0xFFuy -> identity = Virgin
    | other -> identity = Unknown other
