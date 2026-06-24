module Stem.ButtonPanelTester.Tests.Windows.Gui.Can.ButtonPressTestViewTests

open System
open Avalonia.Controls
open Avalonia.Interactivity
open Avalonia.Styling
open Avalonia.Headless.XUnit
open Avalonia.FuncUI.VirtualDom
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.GUI.Can

/// `Avalonia.Headless.XUnit` tests for `ButtonPressTestView` per spec-005 Phase F
/// (T031 US1 surface). The view is pure: each test materialises the rendered tree
/// through `VirtualDom.create` and walks it the same way `BaptismViewTests` does.
/// The view renders FSM state + the schema's decals; it decides nothing.
///
/// `ButtonPressTestState.Idle` and `InterruptReason.LinkLost` are qualified
/// because the bare cases collide with `BaptismState.Idle` / `BaptismOutcome.LinkLost`
/// in the open `Core.Can` namespace.

// --- helpers (mirrors BaptismViewTests) ---

let private fixedNow = DateTimeOffset(2026, 6, 24, 10, 0, 0, TimeSpan.Zero)

let private optimusSchema = ButtonSchema.forVariant OptimusXp

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
    | :? Button as b -> [ b ]
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

// `TextBlock.Text` is nullable; coalesce to "" so the non-null `Assert` overloads
// accept it (mirrors `BaptismViewTests.outcomeText`).
let private textOf (t: TextBlock) : string =
    match t.Text with
    | null -> ""
    | s -> s

// Full render pinning every input; the convenience wrappers default the
// transient Unexpected bit and the callbacks to the inert pass-through.
let private renderFull
    (enablement: Enablement)
    (state: ButtonPressTestState)
    (schema: ButtonSchema option)
    (now: DateTimeOffset)
    (unexpected: int option)
    (onRun: unit -> unit)
    (onRetry: unit -> unit)
    (onSkip: unit -> unit)
    : Control =
    VirtualDom.create (
        ButtonPressTestView.view enablement state schema now unexpected onRun onRetry onSkip ThemeVariant.Light)

let private render
    (enablement: Enablement)
    (state: ButtonPressTestState)
    (schema: ButtonSchema option)
    (now: DateTimeOffset)
    : Control =
    renderFull enablement state schema now None (fun () -> ()) (fun () -> ()) (fun () -> ())

let private renderWith
    (enablement: Enablement)
    (state: ButtonPressTestState)
    (schema: ButtonSchema option)
    (now: DateTimeOffset)
    (onRun: unit -> unit)
    : Control =
    renderFull enablement state schema now None onRun (fun () -> ()) (fun () -> ())

// (T031-1) the OPTIMUS-XP prompt renders the decal ("Light") with the countdown,
// the firmware name as a secondary diagnostic detail (FR-004 / FR-005 / SC-006).
[<AvaloniaFact>]
let Prompt_RendersDecalWithCountdown () =
    let state = Prompting(0, fixedNow.AddSeconds 8.0, Array.create optimusSchema.Active.Length Pending)
    let root = render Enabled state (Some optimusSchema) fixedNow

    let decal = byName "ButtonPressPromptDecal" root |> List.exactlyOne
    Assert.Contains("Light", textOf decal)

    // Firmware name is the secondary diagnostic detail (FR-004): button 0 of the
    // OPTIMUS-XP active set is DOWN (decal "Light").
    let firmware = byName "ButtonPressPromptFirmware" root |> List.exactlyOne
    Assert.Contains("DOWN", textOf firmware)

    let countdown = byName "ButtonPressCountdown" root |> List.exactlyOne
    Assert.Equal("8 s", textOf countdown)

// (T031-2) the result grid renders the four OPTIMUS-XP active rows in canonical
// order — Light, Suspension, Up, Down (SC-006 / FR-011).
[<AvaloniaFact>]
let ResultGrid_RendersFourActiveRowsInCanonicalOrder () =
    let state = Completed [| Pass; Pass; Pass; Pass |]
    let root = render Enabled state (Some optimusSchema) fixedNow

    let decals = byName "ButtonPressResultDecal" root |> List.map textOf
    Assert.Equal<string list>([ "Light"; "Suspension"; "Up"; "Down" ], decals)

// (T031-3) the all-active-passed indicator is positive ONLY when every active
// button scored Pass (FR-011); never on a mixed grid nor on an interruption.
[<AvaloniaFact>]
let AllActivePassed_PositiveOnlyWhenAllPass () =
    let allPass = render Enabled (Completed [| Pass; Pass; Pass; Pass |]) (Some optimusSchema) fixedNow
    Assert.Equal(1, (byName "ButtonPressAllPassed" allPass).Length)

    let mixed = render Enabled (Completed [| Pass; Missed; Pass; Pass |]) (Some optimusSchema) fixedNow
    Assert.Empty(byName "ButtonPressAllPassed" mixed)

    // An interruption that happens to carry an all-Pass partial never reports
    // all-active-passed (interrupt_excludes_all_passed).
    let interrupted =
        render Enabled (Interrupted(InterruptReason.LinkLost, [| Pass; Pass; Pass; Pass |])) (Some optimusSchema) fixedNow
    Assert.Empty(byName "ButtonPressAllPassed" interrupted)

// (T030 wiring) the Run control is enabled when the test is Enabled, idle, and a
// schema is resolvable; clicking it fires onRun exactly once.
[<AvaloniaFact>]
let Run_EnabledAndFiresOnRun () =
    let mutable calls = 0
    let onRun () = calls <- calls + 1

    let root = renderWith Enabled ButtonPressTestState.Idle (Some optimusSchema) fixedNow onRun
    let run = buttonsNamed "RunButtonPressTest" root |> List.exactlyOne
    Assert.True(run.IsEnabled)

    run.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))
    Assert.Equal(1, calls)

// (SC-008 seed) the Run control is disabled whenever the test is unavailable —
// a Disabled enablement, or no resolvable schema — so the surface never offers a
// run on an unbaptized panel / non-Connected link.
[<AvaloniaFact>]
let Run_DisabledWhenUnavailable () =
    let disabled = render (Disabled "unavailable") ButtonPressTestState.Idle (Some optimusSchema) fixedNow
    Assert.False((buttonsNamed "RunButtonPressTest" disabled |> List.exactlyOne).IsEnabled)

    let noSchema = render Enabled ButtonPressTestState.Idle None fixedNow
    Assert.False((buttonsNamed "RunButtonPressTest" noSchema |> List.exactlyOne).IsEnabled)

// (T030 modality) the Run control is disabled while a run is in flight (Prompting)
// — the GUI-side modal guard mirroring the service's modal RunAsync.
[<AvaloniaFact>]
let Run_DisabledWhileRunning () =
    let state = Prompting(0, fixedNow.AddSeconds 10.0, Array.create optimusSchema.Active.Length Pending)
    let root = render Enabled state (Some optimusSchema) fixedNow
    Assert.False((buttonsNamed "RunButtonPressTest" root |> List.exactlyOne).IsEnabled)

// === F2 (T033): Retry / Skip recovery + transient Unexpected ==================

// (T033-1) a timed-out (Missed) button offers both Retry and Skip (FR-009).
[<AvaloniaFact>]
let TimedOutButton_RendersRetryAndSkip () =
    let state = Prompting(0, fixedNow, [| Missed; Pending; Pending; Pending |])
    let root = render Enabled state (Some optimusSchema) fixedNow

    let retry = buttonsNamed "ButtonPressRetry" root |> List.exactlyOne
    let skip = buttonsNamed "ButtonPressSkip" root |> List.exactlyOne
    Assert.True(retry.IsEnabled)
    Assert.True(skip.IsEnabled)

// (T033-2) clicking Retry invokes the service re-arm callback; the re-armed
// Prompting state re-shows the countdown (FR-009).
[<AvaloniaFact>]
let RetryClick_FiresOnRetry () =
    let mutable calls = 0
    let onRetry () = calls <- calls + 1

    let state = Prompting(0, fixedNow, [| Missed; Pending; Pending; Pending |])
    let root = renderFull Enabled state (Some optimusSchema) fixedNow None (fun () -> ()) onRetry (fun () -> ())

    (buttonsNamed "ButtonPressRetry" root |> List.exactlyOne)
        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent))
    Assert.Equal(1, calls)

    // The countdown is part of the prompt the re-arm restores (FR-005).
    Assert.Equal(1, (byName "ButtonPressCountdown" root).Length)

// (T033-3) clicking Skip invokes the service skip callback (FR-009).
[<AvaloniaFact>]
let SkipClick_FiresOnSkip () =
    let mutable calls = 0
    let onSkip () = calls <- calls + 1

    let state = Prompting(0, fixedNow.AddSeconds 5.0, Array.create optimusSchema.Active.Length Pending)
    let root = renderFull Enabled state (Some optimusSchema) fixedNow None (fun () -> ()) (fun () -> ()) onSkip

    (buttonsNamed "ButtonPressSkip" root |> List.exactlyOne)
        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent))
    Assert.Equal(1, calls)

// (T033-4) an Unexpected press (a wrong but active button — here P1/bit 2,
// "Suspension") surfaces transiently WITHOUT advancing the prompt: the prompt
// still shows button 0 ("Light") (FR-008).
[<AvaloniaFact>]
let UnexpectedPress_SurfacesWithoutAdvancing () =
    let state = Prompting(0, fixedNow.AddSeconds 9.0, Array.create optimusSchema.Active.Length Pending)
    let root = renderFull Enabled state (Some optimusSchema) fixedNow (Some 2) (fun () -> ()) (fun () -> ()) (fun () -> ())

    let notice = byName "ButtonPressUnexpected" root |> List.exactlyOne
    Assert.Contains("Suspension", textOf notice)

    // The prompt is unchanged — still button 0 ("Light"), not advanced.
    let decal = byName "ButtonPressPromptDecal" root |> List.exactlyOne
    Assert.Contains("Light", textOf decal)
