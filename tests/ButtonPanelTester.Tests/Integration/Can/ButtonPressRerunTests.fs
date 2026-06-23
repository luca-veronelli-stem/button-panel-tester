module Stem.ButtonPanelTester.Tests.Integration.Can.ButtonPressRerunTests

open System
open System.Collections.Generic
open System.Threading
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Can
open Stem.ButtonPanelTester.Tests.Fakes
open Stem.ButtonPanelTester.Tests.Fakes.Can

/// Integration tests for the US3 surface (spec-005 Phase E, T028): Re-run
/// clearing prior results and starting a fresh sequence (SC-007); a provisional
/// variant (EDEN-XP) driving all-eight prompts with that variant's labels (US3
/// AC-2); and the enablement guard reporting `Disabled` when no baptized panel
/// is selected or the link is down (US3 AC-3 / SC-008 — the service-seam
/// projection of the `testEnablement` predicate). Driven synchronously over the
/// fakes + `FrozenClock`.

let private fixedNow = DateTimeOffset(2026, 6, 22, 9, 0, 0, TimeSpan.Zero)

let private fixedAdapter: AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"
      DeviceId = "0x01"
      BaudrateBps = 250_000 }

let private connectedLink (clock: IClock) : ICanLinkService =
    let link = InMemoryCanLink(seq { (Connected(fixedAdapter, fixedNow), TimeSpan.Zero) })
    let svc = CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)
    (svc :> ICanLinkService).InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    svc :> ICanLinkService

let private optimus = ButtonSchema.forVariant OptimusXp
let private eden = ButtonSchema.forVariant EdenXp

let private selectedUuid = PanelUuid(0x177Cu, 0x126Du, 0x7308u)

let private idle: ButtonStateFrame =
    { Address = VariableAddress 0x8000us
      Bitmap = KeyStateBitmap 0xFFuy }

let private pressedFrame (bit: int) : ButtonStateFrame =
    { Address = VariableAddress 0x8000us
      Bitmap = KeyStateBitmap(0xFFuy &&& ~~~(1uy <<< bit)) }

type private Harness =
    { Clock: FrozenClock
      Buttons: InMemoryButtonStateObserver
      Service: ButtonPressTestService }

let private newHarness () : Harness =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let whoIAm = InMemoryWhoIAmObserver()
    let discovery = new PanelDiscoveryService(whoIAm, link, clock, NullLogger<PanelDiscoveryService>.Instance)
    let buttons = InMemoryButtonStateObserver()

    let service =
        new ButtonPressTestService(buttons, discovery, link, clock, NullLogger<ButtonPressTestService>.Instance)

    { Clock = clock
      Buttons = buttons
      Service = service }

let private press (h: Harness) (bit: int) =
    h.Buttons.Emit idle
    h.Buttons.Emit(pressedFrame bit)

// --- Re-run clears the prior grid and starts a fresh sequence (SC-007) ---

[<Fact>]
let ButtonPressRerun_ClearsPriorResultsAndStartsFresh () =
    let h = newHarness ()
    let task = h.Service.RunAsync(selectedUuid, optimus, CancellationToken.None)

    for bit in [ 1; 2; 4; 5 ] do
        press h bit

    match task.GetAwaiter().GetResult() with
    | Completed results -> Assert.True(ButtonPressTest.allActivePassed results)
    | s -> failwithf "expected Completed all-passed, got %A" s

    // Re-run restarts the SAME panel + schema from a cleared grid and a fresh
    // first prompt (FR-003 / SC-007).
    let _rerun = h.Service.RerunAsync(CancellationToken.None)

    match h.Service.CurrentState with
    | Prompting(0, _, results) -> Assert.Equal<ButtonOutcome[]>([| Pending; Pending; Pending; Pending |], results)
    | s -> failwithf "expected fresh Prompting 0, got %A" s

// --- a provisional variant (EDEN-XP) drives all-eight prompts with its labels ---

[<Fact>]
let ButtonPressRerun_ProvisionalVariant_DrivesAllEightPromptsWithLabels () =
    // EDEN-XP is a provisional, full-eight-button variant (FR-016).
    Assert.True eden.Provisional
    Assert.Equal(8, eden.Active.Length)

    let h = newHarness ()

    let prompts = List<int>()

    use _sub =
        h.Service.StateChanged.Subscribe(fun s ->
            match s with
            | Prompting(index, _, _) -> prompts.Add index
            | _ -> ())

    let task = h.Service.RunAsync(selectedUuid, eden, CancellationToken.None)

    // Press all eight active buttons in canonical order (bits 0..7).
    for bit in 0..7 do
        press h bit

    match task.GetAwaiter().GetResult() with
    | Completed results ->
        Assert.Equal(8, results.Length)
        Assert.True(ButtonPressTest.allActivePassed results)
    | s -> failwithf "expected Completed all-passed, got %A" s

    // The prompt walked all eight buttons, rendering EDEN-XP's (provisional)
    // labels in canonical order (US3 AC-2 — the prompted set tracks the schema).
    Assert.Equal<int list>([ 0; 1; 2; 3; 4; 5; 6; 7 ], List.ofSeq prompts)

    let walkedDecals = prompts |> Seq.map (fun i -> eden.Active.[i].Decal) |> List.ofSeq

    Assert.Equal<string list>(
        [ "HeadUp"; "HeadDown"; "Horizontal"; "Suspension"; "Up"; "Down"; "Stop"; "Lights" ],
        walkedDecals)

// --- the enablement guard reports Disabled when not baptized / link down ---

[<Fact>]
let ButtonPressRerun_EnablementDisabled_WhenNotBaptizedOrLinkDown () =
    let connected = Connected(fixedAdapter, fixedNow)

    // No baptized panel selected (selectedBaptized = false), even on a Connected
    // observable link → Disabled, naming the unmet condition (US3 AC-3 / SC-008).
    match ButtonPressTest.testEnablement connected false true with
    | Disabled explanation -> Assert.Equal(ButtonPressTest.NoBaptizedPanelSelectedExplanation, explanation)
    | Enabled -> failwith "expected Disabled (no baptized panel selected)"

    // Link not Connected → Disabled, naming the link-down condition.
    match ButtonPressTest.testEnablement Initializing true true with
    | Disabled explanation -> Assert.Equal(Baptism.LinkNotConnectedExplanation, explanation)
    | Enabled -> failwith "expected Disabled (link not connected)"

    // All three conjuncts satisfied → Enabled.
    Assert.Equal(Enabled, ButtonPressTest.testEnablement connected true true)
