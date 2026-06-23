module Stem.ButtonPanelTester.Tests.Property.Can.ButtonPressTestEnablementProperties

open System
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Can

/// FsCheck mirror of the Lean `test_enabled_iff` theorem in
/// `lean/Stem/ButtonPanelTester/Phase4/Enablement.lean` (T021), per
/// `specs/005-button-press-test/data-model.md` §6 (FR-001). The Lean side
/// proves the priority-ordered guard equals the flat conjunction; every
/// property here exercises the REAL `ButtonPressTest.testEnablement` over
/// arbitrary `(CanLinkState, selectedBaptized, observable)`. Together they are
/// the SC-008 basis: the button-press test is unavailable — with a reason that
/// names the one unmet condition — on a non-`Connected` link, an unselected or
/// non-baptized panel, or a panel not observable on the bus.

let private baseInstant = DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero)

let private fixedAdapter: AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"
      DeviceId = "0x51"
      BaudrateBps = 250_000 }

/// `true` iff the link is `Connected` — the property-side restatement of the
/// one bit the guard reads (mirrors `ButtonPressTest`'s private `isConnected`
/// and Lean `isConnected`), kept local so the property does not depend on Core
/// internals.
let private isConnected (link: CanLinkState) : bool =
    match link with
    | Connected _ -> true
    | _ -> false

/// Generates a `CanLinkState` biased so `Connected` (the only state the guard
/// can enable from) is well represented alongside the three non-`Connected`
/// shapes the guard treats uniformly as "link down".
let private linkGen: Gen<CanLinkState> =
    Gen.frequency
        [ 3, Gen.constant (Connected(fixedAdapter, baseInstant))
          1, Gen.constant Initializing
          1, Gen.constant (Disconnected(MidSessionUnplug, baseInstant))
          1, Gen.constant (Error(Recoverable "bus-off detected", baseInstant)) ]

/// Either boolean, equally weighted — `selectedBaptized` / `observable` are
/// already-computed bits on the service side (is a baptized panel selected; is
/// it observable on the bus), so the unbiased pair exercises every branch.
let private boolGen: Gen<bool> = Gen.elements [ true; false ]

/// One generated guard input: the link state, whether a baptized panel is
/// selected, and whether that panel is observable on the bus.
type TestEnablementInput =
    { Link: CanLinkState
      SelectedBaptized: bool
      Observable: bool }

/// FsCheck `Arbitrary` container, wired into `[<Property>]` via
/// `Arbitrary = [| typeof<TestEnablementGenerators> |]` (house pattern).
type TestEnablementGenerators =
    static member Input() : Arbitrary<TestEnablementInput> =
        gen {
            let! link = linkGen
            let! selectedBaptized = boolGen
            let! observable = boolGen

            return
                { Link = link
                  SelectedBaptized = selectedBaptized
                  Observable = observable }
        }
        |> Arb.fromGen

/// FsCheck property mirroring the Lean theorem `test_enabled_iff` (T021), per
/// data-model §6 / FR-001: `testEnablement` returns `Enabled` IFF the link is
/// `Connected`, a baptized panel is selected, AND that panel is observable on
/// the bus. The iff is the SC-008 guarantee for the button-press-test surface.
[<Property(Arbitrary = [| typeof<TestEnablementGenerators> |])>]
let TestEnablementGuards_TestEnabledIff (input: TestEnablementInput) =
    let enabled =
        ButtonPressTest.testEnablement input.Link input.SelectedBaptized input.Observable = Enabled

    enabled = (isConnected input.Link && input.SelectedBaptized && input.Observable)

/// FsCheck property (data-model §6, the SC-008 second clause): a `Disabled`
/// button-press-test verdict ALWAYS carries a non-empty explanation that names
/// the one unmet conjunct, in the guard's priority order (link down → no
/// baptized panel selected → panel not observable). A `Disabled` whose
/// conjuncts are all satisfied is a contract violation and fails the property.
[<Property(Arbitrary = [| typeof<TestEnablementGenerators> |])>]
let TestEnablementGuards_DisabledNamesUnmetCondition (input: TestEnablementInput) =
    match ButtonPressTest.testEnablement input.Link input.SelectedBaptized input.Observable with
    | Enabled -> true
    | Disabled explanation ->
        explanation <> ""
        && (if not (isConnected input.Link) then
                explanation = Baptism.LinkNotConnectedExplanation
            elif not input.SelectedBaptized then
                explanation = ButtonPressTest.NoBaptizedPanelSelectedExplanation
            elif not input.Observable then
                explanation = ButtonPressTest.PanelNotObservableExplanation
            else
                false)
