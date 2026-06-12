module Stem.ButtonPanelTester.Tests.Property.Can.BoardVariantProperties

open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Can

/// FsCheck property covering `data-model.md` §1: the spec-004 encode
/// inverse composed with the shipped spec-003 total decoder recovers
/// exactly the originating variant —
/// `VariantDecoder.decode (MachineTypeByte (BoardVariant.encode v)) = Marketing v`.
/// FsCheck derives the generator for the closed 4-case
/// `MarketingVariant` union. Mirrors the Lean theorem
/// `encode_decode_inverse` in `Phase3/WhoAreYouFrame.lean` (T002); the
/// wildcard-free `match` inside `BoardVariant.encode` is load-bearing —
/// a fifth variant breaks the F# compile AND the Lean `cases` proof
/// together, forcing a cross-layer update.
[<Property>]
let VariantEncodeDecodeInverse (variant: MarketingVariant) =
    VariantDecoder.decode(MachineTypeByte(BoardVariant.encode variant)) = Marketing variant
