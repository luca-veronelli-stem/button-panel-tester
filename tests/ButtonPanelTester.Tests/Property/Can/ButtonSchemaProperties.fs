module Stem.ButtonPanelTester.Tests.Property.Can.ButtonSchemaProperties

open Xunit
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Can

/// Independent restatement of the canonical firmware order + bit assignment
/// (R3), deliberately NOT reusing `ButtonSchema.canonicalOrder` / `bitOf` so
/// `SchemaActiveOnlyInOrder` is an independent check of the schema's filter
/// rather than a tautology over the schema's own helper.
let private canonicalExpected =
    [ UP, 0; DOWN, 1; P1, 2; P2, 3; P3, 4; MEM, 5; STOP, 6; LIGHT, 7 ]

/// `true` when wire bit `bit` is set in `mask` — a local re-derivation,
/// independent of the schema's private `isActive`.
let private bitSet (mask: byte) (bit: int) = (mask >>> bit) &&& 1uy = 1uy

/// FsCheck property mirroring the Lean theorem `canonical_order_total` in
/// `Phase4/ButtonSchema.lean` (T010): for every variant, `Active` is exactly
/// the canonical firmware order filtered by `ActiveMask` — order preserved,
/// every entry's `Bit` set in the mask (active-only), and no inactive bit
/// present. The expected list is recomputed inline from `canonicalExpected`
/// (NOT from the schema's own `buildActive`/`canonicalOrder`), so the check is
/// independent. FsCheck derives the generator for the closed 4-case
/// `MarketingVariant` union (data-model §3; FR-016).
[<Property>]
let SchemaActiveOnlyInOrder (variant: MarketingVariant) =
    let schema = ButtonSchema.forVariant variant

    let expected =
        canonicalExpected
        |> List.filter (fun (_, bit) -> bitSet schema.ActiveMask bit)
        |> List.map fst

    let actualButtons = schema.Active |> List.map (fun a -> a.Button)

    // order preserved + active-only + no inactive bit (the filtered list itself)
    actualButtons = expected
    // every active entry's Bit is the canonical bit of its Button and is set in the mask
    && schema.Active
       |> List.forall (fun a ->
           List.contains (a.Button, a.Bit) canonicalExpected
           && bitSet schema.ActiveMask a.Bit)

/// OPTIMUS-XP decal exactness (SC-006 / the §C3 correction): the authoritative
/// variant prompts `Light, Suspension, Up, Down` in canonical-filtered order,
/// riding the active buttons `DOWN, P1, P3, MEM`. The singular `"Light"` is the
/// normalized OPTIMUS spelling (research R4).
[<Fact>]
let OptimusXpDecalsAreLightSuspensionUpDownInOrder () =
    let optimus = ButtonSchema.forVariant OptimusXp

    Assert.Equal<FirmwareButton list>(
        [ DOWN; P1; P3; MEM ],
        optimus.Active |> List.map (fun a -> a.Button)
    )

    Assert.Equal<string list>(
        [ "Light"; "Suspension"; "Up"; "Down" ],
        optimus.Active |> List.map (fun a -> a.Decal)
    )

/// FR-016: OPTIMUS-XP is the only authoritative (`Provisional = false`) row;
/// EDEN-XP, R-3L XP, and EDEN-BS8 all carry `Provisional = true` until
/// bench-confirmed. Wildcard-free over the closed `MarketingVariant` union so a
/// fifth variant forces an explicit provisional decision here.
[<Property>]
let ProvisionalFlagSetForEveryNonOptimusVariant (variant: MarketingVariant) =
    let schema = ButtonSchema.forVariant variant

    match variant with
    | OptimusXp -> not schema.Provisional
    | EdenXp
    | R3LXp
    | EdenBs8 -> schema.Provisional
