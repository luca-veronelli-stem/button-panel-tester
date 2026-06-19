module Stem.ButtonPanelTester.Tests.Windows.Gui.Can.BaptismViewTests

open System
open System.Threading
open System.Threading.Tasks
open Avalonia.Controls
open Avalonia.Interactivity
open Avalonia.Media
open Avalonia.Styling
open Avalonia.Headless.XUnit
open Avalonia.FuncUI.VirtualDom
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Can
open Stem.ButtonPanelTester.Tests.Fakes.Can
open Stem.ButtonPanelTester.GUI.Can

/// `Avalonia.Headless.XUnit` tests for `BaptismView` per spec-004 Phase E
/// (T034/T035 baptize, T036/T037 reset). The pure helpers are asserted
/// directly; the rendered tree is materialised through `VirtualDom.create`
/// and walked the same way `PanelsOnBusViewTests` / `CanStatusRowTests` do.
///
/// The GUI renders enablement and outcomes; it decides nothing. The baptize
/// surface (T034/T035) and the reset surface (T036/T037) share one `view`.

// --- helpers ---

let private fixedNow = DateTimeOffset(2026, 6, 12, 10, 0, 0, TimeSpan.Zero)

let private fixedAdapter: AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"
      DeviceId = "0x01"
      BaudrateBps = 250_000 }

let private sampleUuid = PanelUuid(0x177C126Du, 0x7308748Fu, 0x16092104u)
let private sampleUuidHex = "177C126D-7308748F-16092104"

// Descend Panel.Children AND ContentControl.Content, like the updated
// PanelsOnBusViewTests `allTextBlocks`.
let rec private allTextBlocks (c: Control) : TextBlock list =
    match box c with
    | :? TextBlock as t -> [ t ]
    | :? Panel as p -> [ for child in p.Children do yield! allTextBlocks child ]
    | :? ContentControl as cc ->
        match cc.Content with
        | :? Control as inner -> allTextBlocks inner
        | _ -> []
    | _ -> []

let rec private allButtons (c: Control) : Button list =
    match box c with
    | :? Button as b ->
        // A Button is also a ContentControl; do NOT descend into its content
        // for the button collector (its content is a label, not a button).
        [ b ]
    | :? Panel as p -> [ for child in p.Children do yield! allButtons child ]
    | :? ContentControl as cc ->
        match cc.Content with
        | :? Control as inner -> allButtons inner
        | _ -> []
    | _ -> []

let private byName (name: string) (root: Control) : TextBlock list =
    allTextBlocks root |> List.filter (fun t -> t.Name = name)

let private buttonsNamed (name: string) (root: Control) : Button list =
    allButtons root |> List.filter (fun b -> b.Name = name)

let private buttonText (b: Button) : string =
    match b.Content with
    | null -> ""
    | :? string as s -> s
    | other ->
        match other.ToString() with
        | null -> ""
        | s -> s

// Default render with sensible defaults; callers override what they pin.
// The reset inputs default to the inert pass-through (`resetEnablement =
// Enabled`, `resetOutcome = None`, `onReset = no-op`) so the baptize tests
// stay focused on the baptize surface.
let private render
    (baptizeEnablement: Enablement)
    (state: BaptismState)
    (selectedVariant: MarketingVariant option)
    (attempt: (MarketingVariant * PanelUuid) option)
    (warning: PanelUuid option)
    : Control =
    VirtualDom.create (
        BaptismView.view
            baptizeEnablement
            Enabled
            state
            selectedVariant
            attempt
            warning
            None
            (fun _ -> ())
            (fun _ -> ())
            (fun () -> ())
            ThemeVariant.Light
    )

let private renderWith
    (baptizeEnablement: Enablement)
    (state: BaptismState)
    (selectedVariant: MarketingVariant option)
    (attempt: (MarketingVariant * PanelUuid) option)
    (warning: PanelUuid option)
    (onVariantSelected: MarketingVariant -> unit)
    (onBaptize: MarketingVariant -> unit)
    : Control =
    VirtualDom.create (
        BaptismView.view
            baptizeEnablement
            Enabled
            state
            selectedVariant
            attempt
            warning
            None
            onVariantSelected
            onBaptize
            (fun () -> ())
            ThemeVariant.Light
    )

// Render the full surface pinning the reset inputs (E3, T037).
let private renderReset
    (resetEnablement: Enablement)
    (state: BaptismState)
    (resetOutcome: ResetOutcome option)
    (onReset: unit -> unit)
    : Control =
    VirtualDom.create (
        BaptismView.view
            Enabled
            resetEnablement
            state
            None
            None
            None
            resetOutcome
            (fun _ -> ())
            (fun _ -> ())
            onReset
            ThemeVariant.Light
    )

// Render a terminal outcome for the failure/success matrix.
let private renderOutcome (v: MarketingVariant) (u: PanelUuid) (o: BaptismOutcome) : Control =
    render Enabled (Terminal o) (Some v) (Some(v, u)) None

let private outcomeText (root: Control) : string =
    match (byName "BaptismOutcome" root |> List.exactlyOne).Text with
    | null -> ""
    | s -> s

// (1) the picker offers exactly the four marketed variants, never the virgin marker
[<AvaloniaFact>]
let Picker_OffersExactlyTheFourMarketedVariants_NeverVirgin () =
    Assert.Equal<MarketingVariant list>(
        [ EdenXp; OptimusXp; R3LXp; EdenBs8 ],
        BaptismView.pickerVariants
    )
    Assert.Equal(4, BaptismView.pickerVariants.Length)

    // Every entry round-trips to a non-Virgin / non-Unknown label.
    for v in BaptismView.pickerVariants do
        let label = BaptismView.variantLabel v
        Assert.NotEqual<string>("Virgin", label)
        Assert.NotEqual<string>("Unknown", label)

    // The rendered VariantOption buttons are exactly the four labels.
    let root = render (Disabled "x") Idle None None None
    let options = buttonsNamed "VariantOption" root
    Assert.Equal(4, options.Length)
    let labels = options |> List.map buttonText
    Assert.Equal<string list>(
        [ "Eden XP"; "Optimus XP"; "R-3L XP"; "Eden BS8" ],
        labels
    )

// (2) FR-002 enable matrix (the SC-005 rendering)
[<AvaloniaFact>]
let Baptize_EnabledWhenEnablementEnabledAndVariantPicked () =
    let root = render Enabled Idle (Some EdenXp) None None
    let baptize = buttonsNamed "BaptizeButton" root |> List.exactlyOne
    Assert.True(baptize.IsEnabled)

[<AvaloniaFact>]
let Baptize_DisabledZeroAnnouncing_RendersExplanation () =
    // Real zero-announcing Disabled value.
    let enablement = Baptism.baptizeEnablement (Connected(fixedAdapter, fixedNow)) 0 None
    let explanation =
        match enablement with
        | Disabled e -> e
        | Enabled -> failwith "expected Disabled for zero announcing"

    let root = render enablement Idle (Some EdenXp) None None
    let baptize = buttonsNamed "BaptizeButton" root |> List.exactlyOne
    Assert.False(baptize.IsEnabled)
    let reason = byName "BaptizeDisabledReason" root |> List.exactlyOne
    Assert.Equal(explanation, reason.Text)

[<AvaloniaFact>]
let Baptize_DisabledTwoOrMoreAnnouncing_RendersExplanation () =
    // Real >=2-announcing Disabled value.
    let enablement =
        Baptism.baptizeEnablement (Connected(fixedAdapter, fixedNow)) 2 (Some sampleUuid)
    let explanation =
        match enablement with
        | Disabled e -> e
        | Enabled -> failwith "expected Disabled for >=2 announcing"

    let root = render enablement Idle (Some EdenXp) None None
    let baptize = buttonsNamed "BaptizeButton" root |> List.exactlyOne
    Assert.False(baptize.IsEnabled)
    let reason = byName "BaptizeDisabledReason" root |> List.exactlyOne
    Assert.Equal(explanation, reason.Text)

// (3) acceptance 1.7 — enabled for any announcing selected panel
[<Fact>]
let Baptize_EnabledForAnyAnnouncingSelectedPanel () =
    Assert.True(BaptismView.baptizeEnabled Enabled (Some OptimusXp) Idle)

// (4) no variant picked -> baptize disabled with a hint
[<AvaloniaFact>]
let NoVariantPicked_BaptizeDisabledWithHint () =
    let root = render Enabled Idle None None None
    let baptize = buttonsNamed "BaptizeButton" root |> List.exactlyOne
    Assert.False(baptize.IsEnabled)
    Assert.NotEmpty(byName "BaptizeDisabledReason" root)

// (5) clicking a variant option fires onVariantSelected with that variant
[<AvaloniaFact>]
let PickingVariant_FiresOnVariantSelected () =
    let mutable picked: MarketingVariant option = None
    let onVariantSelected v = picked <- Some v

    let root =
        renderWith (Disabled "x") Idle None None None onVariantSelected (fun _ -> ())

    // Click the third option (R-3L XP).
    let options = buttonsNamed "VariantOption" root
    let third = options.[2]
    third.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))

    Assert.Equal(Some R3LXp, picked)

// (6) baptize invokes exactly one attempt with the picked variant (no dialog)
[<AvaloniaFact>]
let Baptize_InvokesExactlyOneAttemptWithPickedVariant () =
    let mutable calls: MarketingVariant list = []
    let onBaptize v = calls <- v :: calls

    let root =
        renderWith Enabled Idle (Some R3LXp) None None (fun _ -> ()) onBaptize

    let baptize = buttonsNamed "BaptizeButton" root |> List.exactlyOne
    baptize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))

    // The callback fired synchronously, exactly once, with the picked variant —
    // there is no dialog seam in this view.
    Assert.Equal<MarketingVariant list>([ R3LXp ], calls)

// (7) modal while running: picker and baptize disabled (CHK013)
[<AvaloniaFact>]
let Modal_WhileRunning_PickerAndBaptizeDisabled () =
    let runningStates =
        [ ClaimSent; AwaitingAnnounce fixedNow; Assigning ]

    for state in runningStates do
        Assert.True(BaptismView.isRunning state)
        let root = render Enabled state (Some EdenXp) None None
        let baptize = buttonsNamed "BaptizeButton" root |> List.exactlyOne
        Assert.False(baptize.IsEnabled)
        for opt in buttonsNamed "VariantOption" root do
            Assert.False(opt.IsEnabled)

    Assert.False(BaptismView.isRunning Idle)
    Assert.False(BaptismView.isRunning (Terminal Succeeded))

// (8) success renders the silence explainer (FR-006)
[<AvaloniaFact>]
let Success_RendersSilenceExplainer () =
    let root = render Enabled (Terminal Succeeded) (Some EdenXp) (Some(EdenXp, sampleUuid)) None
    let text = outcomeText root
    Assert.Contains("Eden XP", text)
    Assert.Contains(sampleUuidHex, text)
    Assert.Contains("silent", text)
    Assert.True(
        text.Contains("age out") || text.Contains("ages out"),
        sprintf "expected 'age out'/'ages out' in: %s" text
    )

// (9) failure renderings (FR-005) — step + likely state + next action
[<AvaloniaFact>]
let WaitTimeout_RendersClarification4Guidance () =
    let text = outcomeText (renderOutcome EdenXp sampleUuid WaitTimeout)
    Assert.Contains("re-announce", text)
    Assert.Contains("re-run", text)

[<AvaloniaFact>]
let UnexpectedVariant_NamesAnnouncedIdentity () =
    let text =
        outcomeText (renderOutcome EdenXp sampleUuid (UnexpectedVariant(Marketing OptimusXp)))
    Assert.Contains("Optimus XP", text)

// ClaimNotAdopted (FR-006a / FR-015 / acceptance scenario 10): the deterministic
// guided-recovery rendering. The outcome states the claim did not TAKE — as fact,
// never "likely"/"maybe" — and guides the operator into Reset-to-virgin → re-baptize
// using the existing affordances, naming the attempted variant + panel like the
// sibling outcome arms.
[<AvaloniaFact>]
let ClaimNotAdopted_RendersGuidedRecovery () =
    let text = outcomeText (renderOutcome EdenXp sampleUuid ClaimNotAdopted)
    let lower = text.ToLowerInvariant()
    // Deterministic: states the claim did not take, never hedged.
    Assert.DoesNotContain("likely", lower)
    Assert.DoesNotContain("maybe", lower)
    Assert.Contains("did not take", text)
    // Names the attempted identity + panel, like the sibling arms.
    Assert.Contains("Eden XP", text)
    Assert.Contains(sampleUuidHex, text)
    // Guided recovery: Reset-to-virgin → re-run Baptize via the existing affordances.
    Assert.Contains("Reset", text)
    Assert.True(
        text.Contains("re-run Baptize") || text.Contains("re-baptize"),
        sprintf "ClaimNotAdopted text missing reset → re-baptize recovery: %s" text)

[<AvaloniaFact>]
let PanelDisappeared_NamesStepStateAndNextAction () =
    let text = outcomeText (renderOutcome EdenXp sampleUuid PanelDisappeared)
    Assert.NotEmpty(text)
    let lower = text.ToLowerInvariant()
    Assert.True(
        lower.Contains("left the bus") || lower.Contains("stopped announcing") || lower.Contains("reconnect"),
        sprintf "PanelDisappeared text missing state/next-action: %s" text
    )

[<AvaloniaFact>]
let LinkLost_NamesStepStateAndNextAction () =
    let text = outcomeText (renderOutcome EdenXp sampleUuid LinkLost)
    Assert.Contains("link", text)
    // Case-insensitive: the next-action verb opens a sentence ("Reconnect …").
    Assert.True(
        text.ToLowerInvariant().Contains("reconnect"),
        sprintf "LinkLost text missing next-action: %s" text
    )

[<AvaloniaFact>]
let TransmissionFailureClaim_NamesClaim () =
    let text = outcomeText (renderOutcome EdenXp sampleUuid (TransmissionFailure ClaimStep))
    Assert.Contains("claim", text)

[<AvaloniaFact>]
let TransmissionFailureAssign_NamesAssign () =
    let text = outcomeText (renderOutcome EdenXp sampleUuid (TransmissionFailure AssignStep))
    Assert.True(
        text.Contains("assign") || text.Contains("SET_ADDRESS"),
        sprintf "AssignStep text missing 'assign'/'SET_ADDRESS': %s" text
    )

// (10) FR-007 warning renders when raised
[<AvaloniaFact>]
let Warning_Renders_WhenRaised () =
    let root = render Enabled Idle (Some EdenXp) None (Some sampleUuid)
    let warning = byName "ClaimWarning" root |> List.exactlyOne
    Assert.Contains(sampleUuidHex, warning.Text)
    Assert.Contains("claim", warning.Text)

// (11) no confirmation dialog on baptize — no extra buttons beyond the picker,
// baptize, and the (separate-surface) reset button. The FR-009 confirmation
// for RESET lives in the host's modal dialog (App.fs `confirmReset`), not in
// this in-pane view, so it adds no inline buttons here.
[<AvaloniaFact>]
let NoConfirmationDialogOnBaptize () =
    let root = render Enabled Idle (Some EdenXp) None None
    let other =
        allButtons root
        |> List.filter (fun b ->
            b.Name <> "VariantOption"
            && b.Name <> "BaptizeButton"
            && b.Name <> "ResetButton")
    Assert.Empty(other)

// === E3 (T036 / T037): the RESET-to-virgin surface ===========================
//
// The GUI renders `Baptism.resetEnablement` + the `ResetOutcome` and drives the
// `IBaptismService.ResetAsync` confirmation seam through `BaptismView.runReset`;
// it decides nothing. The wired-service tests assemble the `ResetE2ETests`
// harness (a real `BaptismService` over the in-memory fakes) and assert the
// recorded transmitter sends, exactly as `ResetE2ETests` does.

// A frozen clock for the wired harness (local 2-liner; Wiring.fs is not linked).
let private frozenClock (now: DateTimeOffset) =
    { new IClock with member _.UtcNow() = now }

/// Assembles a real `BaptismService` over a `CanLinkService` driven `Connected`
/// (the `ResetE2ETests.connectedLink` precedent), returning the service AND the
/// recording transmitter so a test can assert `.Sent` on the wired fake.
let private connectedService () : IBaptismService * InMemoryMasterSequenceTransmitter =
    let clock = frozenClock fixedNow
    let link = InMemoryCanLink(seq { (Connected(fixedAdapter, fixedNow), TimeSpan.Zero) })
    let canLink = CanLinkService(link, clock, NullLogger<CanLinkService>.Instance) :> ICanLinkService
    canLink.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    let observer = InMemoryWhoIAmObserver()
    let ackObserver = InMemorySetAddressAckObserver()
    let discovery =
        new PanelDiscoveryService(observer, canLink, clock, NullLogger<PanelDiscoveryService>.Instance)
    let transmitter = InMemoryMasterSequenceTransmitter(clock)

    let service =
        new BaptismService(
            transmitter,
            observer,
            ackObserver,
            discovery,
            canLink,
            clock,
            NullLogger<BaptismService>.Instance)

    (service :> IBaptismService, transmitter)

// (E3-1) FR-008 enable matrix (acceptance 2.3 / 2.4, SC-005): reset needs no
// selection; Enabled at 0/1 announcing, Disabled at >=2; the render mirrors it.
[<AvaloniaFact>]
let Reset_EnableMatrix_FollowsResetEnablement () =
    let connected = Connected(fixedAdapter, fixedNow)

    // Pure predicate at 0 / 1 announcing — Enabled, no list selection needed.
    Assert.True(BaptismView.resetEnabled (Baptism.resetEnablement connected 0) Idle)
    Assert.True(BaptismView.resetEnabled (Baptism.resetEnablement connected 1) Idle)

    // At >=2 the guard is Disabled and reset is inactive.
    let twoEnablement = Baptism.resetEnablement connected 2
    let explanation =
        match twoEnablement with
        | Disabled e -> e
        | Enabled -> failwith "expected Disabled for >=2 announcing"
    Assert.False(BaptismView.resetEnabled twoEnablement Idle)

    // Render: count-2 → ResetButton disabled AND a ResetDisabledReason renders it.
    let disabledRoot = renderReset twoEnablement Idle None (fun () -> ())
    let disabledButton = buttonsNamed "ResetButton" disabledRoot |> List.exactlyOne
    Assert.False(disabledButton.IsEnabled)
    let reason = byName "ResetDisabledReason" disabledRoot |> List.exactlyOne
    Assert.Equal(explanation, reason.Text)

    // Render: count-0 / count-1 → ResetButton enabled.
    for count in [ 0; 1 ] do
        let root = renderReset (Baptism.resetEnablement connected count) Idle None (fun () -> ())
        let button = buttonsNamed "ResetButton" root |> List.exactlyOne
        Assert.True(button.IsEnabled)

// (E3-2) the two surfaces gate each other: reset disabled while a baptize
// attempt runs, even when the reset guard itself says Enabled.
[<Fact>]
let Reset_Disabled_WhileBaptizeAttemptRunning () =
    Assert.False(BaptismView.resetEnabled Enabled ClaimSent)

// (E3-3) FR-009 confirmation wording (identity + broadcast + silent panels).
[<Fact>]
let ResetConfirmation_CarriesFr009Wording () =
    let message = BaptismView.resetConfirmationMessage
    Assert.Contains("identity", message)
    Assert.Contains("every", message)
    Assert.Contains("silent", message)

// (E3-4) confirmed → ResetAsync transmits exactly the dual-fwType pair, Sent.
[<Fact>]
let Reset_Confirmed_InvokesServiceAndBroadcasts () =
    let service, transmitter = connectedService ()

    let outcome =
        BaptismView.runReset (fun () -> Task.FromResult true) service CancellationToken.None
        |> _.GetAwaiter().GetResult()

    Assert.Equal(Sent, outcome)
    Assert.Equal<(MasterSequenceSend * DateTimeOffset) list>(
        [ (WhoAreYouSent(0xFFuy, 0x0004us, true), fixedNow)
          (WhoAreYouSent(0xFFuy, 0x000Fus, true), fixedNow) ],
        transmitter.Sent)

// (E3-5) declined → ResetAsync transmits nothing, Declined (acceptance 2.2).
[<Fact>]
let Reset_Declined_InvokesNothing () =
    let service, transmitter = connectedService ()

    let outcome =
        BaptismView.runReset (fun () -> Task.FromResult false) service CancellationToken.None
        |> _.GetAwaiter().GetResult()

    Assert.Equal(Declined, outcome)
    Assert.Empty(transmitter.Sent)

// (E3-6) honest FR-010 / acceptance-2.5 success message: written to the bus,
// re-announces as virgin within ~6 s, otherwise the list stays empty.
[<AvaloniaFact>]
let ResetOutcome_Sent_RendersHonestMessage () =
    let root = renderReset Enabled Idle (Some Sent) (fun () -> ())
    let text =
        match (byName "ResetOutcome" root |> List.exactlyOne).Text with
        | null -> ""
        | s -> s

    let assertHonest (s: string) =
        Assert.True(
            s.Contains("written") || s.Contains("sent"),
            sprintf "missing written/sent in: %s" s)
        Assert.True(
            s.Contains("re-announce") || s.Contains("re-announces"),
            sprintf "missing re-announce in: %s" s)
        Assert.Contains("virgin", s)
        Assert.Contains("6 s", s)
        Assert.Contains("empty", s)

    assertHonest text
    assertHonest (BaptismView.describeResetOutcome Sent)

// (E3-7) clicking the enabled ResetButton fires onReset exactly once.
[<AvaloniaFact>]
let ResetButtonClick_FiresOnReset () =
    let mutable calls = 0
    let onReset () = calls <- calls + 1

    let root = renderReset Enabled Idle None onReset
    let button = buttonsNamed "ResetButton" root |> List.exactlyOne
    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))

    Assert.Equal(1, calls)
