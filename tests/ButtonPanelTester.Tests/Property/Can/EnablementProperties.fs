module Stem.ButtonPanelTester.Tests.Property.Can.EnablementProperties

open System
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Can

/// FsCheck mirrors of the Lean `Enablement` theorems in
/// `lean/Stem/ButtonPanelTester/Phase3/Enablement.lean` (T027), per
/// `specs/004-baptism-workflow/data-model.md` §6. The Lean side proves the
/// priority-ordered guard equals the flat conjunction; every property here
/// exercises the REAL `Baptism.baptizeEnablement` / `Baptism.resetEnablement`
/// over arbitrary `(CanLinkState, announcing count, selection)`. Together
/// `EnablementGuards` is the SC-005 basis: destructive actions are
/// unreachable with ≥ 2 announcing panels, and every `Disabled` verdict
/// names the one unmet condition.

let private baseInstant = DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero)

let private fixedAdapter: AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"; DeviceId = "0x51"; BaudrateBps = 250_000 }

/// `true` iff the link is `Connected` — the property-side restatement of the
/// one bit the guards read (mirrors `Baptism`'s private `isConnected` and
/// Lean `isConnected`), kept local so the property does not depend on Core
/// internals.
let private isConnected (link: CanLinkState) : bool =
    match link with
    | Connected _ -> true
    | _ -> false

/// Generates a `CanLinkState` biased so `Connected` (the only state any guard
/// can enable from) is well represented alongside the three non-`Connected`
/// shapes the guards treat uniformly as "link down".
let private linkGen: Gen<CanLinkState> =
    Gen.frequency
        [ 3, Gen.constant (Connected(fixedAdapter, baseInstant))
          1, Gen.constant Initializing
          1, Gen.constant (Disconnected(MidSessionUnplug, baseInstant))
          1, Gen.constant (Error(Recoverable "bus-off detected", baseInstant)) ]

/// Announcing-panel count: non-negative (a count, mirroring the Lean `Nat`),
/// biased toward the boundary values 0 / 1 / 2 the guards branch on.
let private countGen: Gen<int> =
    Gen.frequency
        [ 2, Gen.constant 0
          2, Gen.constant 1
          2, Gen.constant 2
          1, Gen.choose (3, 8) ]

let private selectionGen: Gen<PanelUuid option> =
    Gen.frequency
        [ 1, Gen.constant None
          2,
          gen {
              let! u0 = Gen.choose (0, 1_000_000)
              let! u1 = Gen.choose (0, 1_000_000)
              let! u2 = Gen.choose (0, 1_000_000)
              return Some(PanelUuid(uint32 u0, uint32 u1, uint32 u2))
          } ]

/// One generated guard input: the link state, the announcing count, and the
/// current selection.
type EnablementInput =
    { Link: CanLinkState
      Count: int
      Selection: PanelUuid option }

/// FsCheck `Arbitrary` container, wired into `[<Property>]` via
/// `Arbitrary = [| typeof<EnablementGenerators> |]` (house pattern).
type EnablementGenerators =
    static member Input() : Arbitrary<EnablementInput> =
        gen {
            let! link = linkGen
            let! count = countGen
            let! selection = selectionGen

            return
                { Link = link
                  Count = count
                  Selection = selection }
        }
        |> Arb.fromGen

/// FsCheck property mirroring the Lean theorem `baptize_enabled_iff`
/// (T027), per data-model §6 / FR-002: `baptizeEnablement` returns `Enabled`
/// IFF the link is `Connected`, exactly one panel is announcing, AND a panel
/// is selected. The iff is the SC-005 guarantee for the Baptize surface.
[<Property(Arbitrary = [| typeof<EnablementGenerators> |])>]
let EnablementGuards_BaptizeEnabledIff (input: EnablementInput) =
    let enabled = Baptism.baptizeEnablement input.Link input.Count input.Selection = Enabled
    enabled = (isConnected input.Link && input.Count = 1 && Option.isSome input.Selection)

/// FsCheck property mirroring the Lean theorem `reset_enabled_iff` (T027),
/// per data-model §6 / FR-008: `resetEnablement` returns `Enabled` IFF the
/// link is `Connected` AND at most one panel is announcing. The iff is the
/// SC-005 guarantee for the Reset surface.
[<Property(Arbitrary = [| typeof<EnablementGenerators> |])>]
let EnablementGuards_ResetEnabledIff (input: EnablementInput) =
    let enabled = Baptism.resetEnablement input.Link input.Count = Enabled
    enabled = (isConnected input.Link && input.Count <= 1)

/// FsCheck property (data-model §6, the SC-005 second clause): a `Disabled`
/// baptize verdict ALWAYS carries a non-empty explanation that names the one
/// unmet conjunct, in the guard's priority order (link down → zero announcing
/// → two-or-more announcing → none selected). A `Disabled` whose conjuncts
/// are all satisfied is a contract violation and fails the property.
[<Property(Arbitrary = [| typeof<EnablementGenerators> |])>]
let EnablementGuards_BaptizeDisabledNamesUnmetCondition (input: EnablementInput) =
    match Baptism.baptizeEnablement input.Link input.Count input.Selection with
    | Enabled -> true
    | Disabled explanation ->
        explanation <> ""
        && (if not (isConnected input.Link) then explanation = Baptism.LinkNotConnectedExplanation
            elif input.Count = 0 then explanation = Baptism.NoPanelAnnouncingExplanation
            elif input.Count >= 2 then explanation = Baptism.MultipleAnnouncingBaptizeExplanation
            elif Option.isNone input.Selection then explanation = Baptism.NoPanelSelectedExplanation
            else false)

/// FsCheck property (data-model §6, the SC-005 second clause): a `Disabled`
/// reset verdict ALWAYS carries a non-empty explanation that names the one
/// unmet conjunct, in priority order (link down → two-or-more announcing).
/// The two-or-more text states the broadcast reaches every panel.
[<Property(Arbitrary = [| typeof<EnablementGenerators> |])>]
let EnablementGuards_ResetDisabledNamesUnmetCondition (input: EnablementInput) =
    match Baptism.resetEnablement input.Link input.Count with
    | Enabled -> true
    | Disabled explanation ->
        explanation <> ""
        && (if not (isConnected input.Link) then explanation = Baptism.LinkNotConnectedExplanation
            elif input.Count >= 2 then explanation = Baptism.MultipleAnnouncingResetExplanation
            else false)
