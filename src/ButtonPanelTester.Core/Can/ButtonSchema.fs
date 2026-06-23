namespace Stem.ButtonPanelTester.Core.Can

/// The eight firmware buttons in canonical (= declaration) order, per
/// `specs/005-button-press-test/data-model.md` §3 and research R3. The
/// declaration order IS the canonical prompt order — a variant's active
/// set is the canonical order filtered to its mask, never re-ordered. Bit
/// assignment is uniform across variants (R3, `UserMain.c:215-246`):
/// `UP=0 · DOWN=1 · P1=2 · P2=3 · P3=4 · MEM=5 · STOP=6 · LIGHT=7`.
///
/// Closed 8-case DU → the mandatory triple (`stem-fp-discipline` §3):
/// closure is witnessed by the Lean theorem `canonical_order_total` in
/// `lean/Stem/ButtonPanelTester/Phase4/ButtonSchema.lean` (T010) and by
/// the FsCheck property `SchemaActiveOnlyInOrder` in
/// `Tests/Property/Can/ButtonSchemaProperties.fs` (T012). The wildcard-free
/// matches over this DU (`ButtonSchema.bitOf` + the per-variant decal maps)
/// are load-bearing: a ninth button breaks the F# compile AND the Lean
/// proof together, forcing a cross-layer update.
type FirmwareButton =
    | UP
    | DOWN
    | P1
    | P2
    | P3
    | MEM
    | STOP
    | LIGHT

/// One active button of a variant's schema, per `data-model.md` §3:
/// the firmware/diagnostic `Button`, its wire `Bit` (0..7, R3), and the
/// `Decal` — the name printed on the physical panel, the primary prompt
/// label (FR-004). Built only for buttons whose bit is set in the variant
/// mask, so `Bit` is always an active bit.
type ActiveButton =
    { Button: FirmwareButton
      Bit: int
      Decal: string }

/// Per-variant active-button schema, per `data-model.md` §3 and research
/// R3/R4. `ActiveMask` is the variant's set of active wire bits; `Active`
/// is the canonical firmware order filtered to that mask (computed, never
/// hand-listed — see `ButtonSchema.forVariant`); `Provisional` is `true`
/// for every variant but OPTIMUS-XP (FR-016).
///
/// `Active` being the canonical order filtered by `ActiveMask` is the
/// invariant `test_visits_active_only` (Phase D FSM) rests on; it is
/// mechanised by Lean `canonical_order_total` (T010) and witnessed by
/// FsCheck `SchemaActiveOnlyInOrder` (T012).
type ButtonSchema =
    { Variant: MarketingVariant
      ActiveMask: byte
      Active: ActiveButton list
      Provisional: bool }

/// The per-variant button-schema table, per `data-model.md` §3 and
/// research R3/R4. OPTIMUS-XP is authoritative (`Provisional = false`); the
/// other three rows are seeded from the legacy enums and stay provisional
/// until bench-confirmed (FR-016).
module ButtonSchema =

    /// Wire bit assignment (R3), uniform across variants: `UP=0 … LIGHT=7`.
    /// Total, wildcard-free over the closed `FirmwareButton` DU — mirrors
    /// Lean `FirmwareButton.bit` (`Phase4/ButtonSchema.lean`, T010).
    let bitOf (button: FirmwareButton) : int =
        match button with
        | UP -> 0
        | DOWN -> 1
        | P1 -> 2
        | P2 -> 3
        | P3 -> 4
        | MEM -> 5
        | STOP -> 6
        | LIGHT -> 7

    /// The fixed canonical firmware order (R3) — the prompt order a
    /// variant's active buttons are filtered from. Mirrors Lean
    /// `FirmwareButton.canonicalOrder` (`Phase4/ButtonSchema.lean`, T010).
    let canonicalOrder: FirmwareButton list =
        [ UP; DOWN; P1; P2; P3; MEM; STOP; LIGHT ]

    /// `true` when `button`'s wire bit is set in `mask`.
    let private isActive (mask: byte) (bit: int) : bool =
        (mask >>> bit) &&& 1uy = 1uy

    /// Build a variant's `Active` list: the canonical order filtered to the
    /// bits set in `mask`, each surviving button paired with its bit and the
    /// variant's decal (`decalOf`). `List.filter` preserves order, so the
    /// result is the canonical sub-order — the F# image of Lean
    /// `FirmwareButton.activeButtons` (`canonical_order_total`, T010).
    let private buildActive (mask: byte) (decalOf: FirmwareButton -> string) : ActiveButton list =
        canonicalOrder
        |> List.filter (fun button -> isActive mask (bitOf button))
        |> List.map (fun button ->
            { Button = button
              Bit = bitOf button
              Decal = decalOf button })

    /// OPTIMUS-XP decals (R4, authoritative): `DOWN→Light · P1→Suspension ·
    /// P3→Up · MEM→Down` — the §C3 correction, decals on the physical-panel
    /// names, normalized to the singular `"Light"` (SC-006). The four
    /// inactive positions carry `""` (filtered out by the `0x36` mask, never
    /// surfaced); enumerated, not wildcarded, so a ninth button breaks here.
    let private optimusDecal (button: FirmwareButton) : string =
        match button with
        | DOWN -> "Light"
        | P1 -> "Suspension"
        | P3 -> "Up"
        | MEM -> "Down"
        | UP
        | P2
        | STOP
        | LIGHT -> ""

    /// EDEN-XP / EDEN-BS8 decals (R4, provisional — legacy `EdenButtons`
    /// labels, keeping the legacy plural `"Lights"` until bench-confirmed).
    let private edenDecal (button: FirmwareButton) : string =
        match button with
        | UP -> "HeadUp"
        | DOWN -> "HeadDown"
        | P1 -> "Horizontal"
        | P2 -> "Suspension"
        | P3 -> "Up"
        | MEM -> "Down"
        | STOP -> "Stop"
        | LIGHT -> "Lights"

    /// R-3L XP decals (R4, provisional — legacy `R3LXPButtons` labels,
    /// keeping the legacy plural `"Lights"` until bench-confirmed).
    let private r3lDecal (button: FirmwareButton) : string =
        match button with
        | UP -> "HeadDown"
        | DOWN -> "Down"
        | P1 -> "Up"
        | P2 -> "HeadUp"
        | P3 -> "FeetUp"
        | MEM -> "FeetDown"
        | STOP -> "Stop"
        | LIGHT -> "Lights"

    /// The per-variant schema, total on the four `MarketingVariant` cases by
    /// construction (data-model §3, research R3/R4). The wildcard-free
    /// `match` is load-bearing: a fifth variant breaks this function AND the
    /// Lean `canonical_order_total` proof together. OPTIMUS-XP's mask `0x36`
    /// (bits 1,2,4,5 = DOWN,P1,P3,MEM) is the only `Provisional = false` row;
    /// the three all-8 variants stay provisional (FR-016).
    let forVariant (variant: MarketingVariant) : ButtonSchema =
        match variant with
        | OptimusXp ->
            { Variant = OptimusXp
              ActiveMask = 0x36uy
              Active = buildActive 0x36uy optimusDecal
              Provisional = false }
        | EdenXp ->
            { Variant = EdenXp
              ActiveMask = 0xFFuy
              Active = buildActive 0xFFuy edenDecal
              Provisional = true }
        | R3LXp ->
            { Variant = R3LXp
              ActiveMask = 0xFFuy
              Active = buildActive 0xFFuy r3lDecal
              Provisional = true }
        | EdenBs8 ->
            { Variant = EdenBs8
              ActiveMask = 0xFFuy
              Active = buildActive 0xFFuy edenDecal
              Provisional = true }
