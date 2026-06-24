module Stem.ButtonPanelTester.Tests.Integration.Can.ButtonPressTestE2ETests

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

/// Example-based integration tests for `ButtonPressTestService` driving the
/// pure FSM (`ButtonPressTest.step`) over the consumed RX observables (spec-005
/// Phase E, T024): the US1 happy path — a baptized OPTIMUS-XP panel, the four
/// active buttons pressed in canonical order, each scored `Pass` within the
/// window, the prompt advancing `Light → Suspension → Up → Down`, and the final
/// grid four `Pass` with `allActivePassed = true` (SC-001/SC-002 logic side).
///
/// Harness (the spec-003/004 integration pattern): a real `CanLinkService`
/// (wrapping `InMemoryCanLink`, driven Connected), the synchronous
/// `InMemoryButtonStateObserver`, and a `FrozenClock` — so the observe → score
/// path is exercised deterministically with no PEAK hardware and no wall-clock
/// sleeps. Panel presence keys off button-state recency (fix #270), so there is
/// no discovery service in the button-press path. The run is launched WITHOUT
/// awaiting; synchronous `Emit`s drive the FSM, and the returned task completes
/// when it reaches a terminal state.

let private fixedNow = DateTimeOffset(2026, 6, 22, 9, 0, 0, TimeSpan.Zero)

let private fixedAdapter: AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"
      DeviceId = "0x01"
      BaudrateBps = 250_000 }

/// A real `CanLinkService` (wrapping `InMemoryCanLink`) driven to `Connected`
/// via `InitializeAsync` (the `BaptismE2ETests` precedent).
let private connectedLink (clock: IClock) : ICanLinkService =
    let link = InMemoryCanLink(seq { (Connected(fixedAdapter, fixedNow), TimeSpan.Zero) })
    let svc = CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)
    (svc :> ICanLinkService).InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    svc :> ICanLinkService

/// The OPTIMUS-XP schema (authoritative): active buttons DOWN(1)/P1(2)/P3(4)/
/// MEM(5), decals Light/Suspension/Up/Down in canonical order.
let private optimus = ButtonSchema.forVariant OptimusXp

let private selectedUuid = PanelUuid(0x177Cu, 0x126Du, 0x7308u)

/// Idle (all released) = all-active-bits-`1` (`0xFF`), the no-buttons-pressed
/// steady state (R2).
let private idle: ButtonStateFrame =
    { Address = VariableAddress 0x8000us
      Bitmap = KeyStateBitmap 0xFFuy }

/// A frame with `bit` pressed — its wire bit CLEARED to `0` against the
/// all-released idle (pressed = `0`, R2).
let private pressedFrame (bit: int) : ButtonStateFrame =
    { Address = VariableAddress 0x8000us
      Bitmap = KeyStateBitmap(0xFFuy &&& ~~~(1uy <<< bit)) }

type private Harness =
    { Clock: FrozenClock
      Buttons: InMemoryButtonStateObserver
      Link: ICanLinkService
      Service: ButtonPressTestService }

let private newHarness () : Harness =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let buttons = InMemoryButtonStateObserver()

    let service =
        new ButtonPressTestService(buttons, link, clock, NullLogger<ButtonPressTestService>.Instance)

    { Clock = clock
      Buttons = buttons
      Link = link
      Service = service }

/// Press a button by its wire bit: release-all (idle) then press the bit. The
/// leading idle frame seeds the press-edge baseline at run start and releases
/// the prior button between presses (so each press is a clean `1 → 0` edge).
let private press (h: Harness) (bit: int) =
    h.Buttons.Emit idle
    h.Buttons.Emit(pressedFrame bit)

// --- happy path: four active buttons pressed in order, all Pass ---

[<Fact>]
let ButtonPressTest_FourActiveButtonsPressedInOrder_AllPassAndAdvancesByDecal () =
    let h = newHarness ()

    // Capture the prompted-button index at every transition.
    let prompts = List<int>()

    use _sub =
        h.Service.StateChanged.Subscribe(fun s ->
            match s with
            | Prompting(index, _, _) -> prompts.Add index
            | _ -> ())

    let task = h.Service.RunAsync(selectedUuid, optimus, CancellationToken.None)

    // The run opens prompting button 0 with a four-slot Pending grid.
    match h.Service.CurrentState with
    | Prompting(0, _, results) -> Assert.Equal<ButtonOutcome[]>([| Pending; Pending; Pending; Pending |], results)
    | s -> failwithf "expected Prompting 0, got %A" s

    // Press each active button in canonical order: DOWN(1) P1(2) P3(4) MEM(5).
    for bit in [ 1; 2; 4; 5 ] do
        press h bit

    let terminal = task.GetAwaiter().GetResult()

    match terminal with
    | Completed results ->
        Assert.Equal<ButtonOutcome[]>([| Pass; Pass; Pass; Pass |], results)
        Assert.True(ButtonPressTest.allActivePassed results)
    | s -> failwithf "expected Completed, got %A" s

    // The prompt advanced through every active button in order, and the decals
    // it walked are exactly Light → Suspension → Up → Down (SC-006 order).
    Assert.Equal<int list>([ 0; 1; 2; 3 ], List.ofSeq prompts)

    let walkedDecals = prompts |> Seq.map (fun i -> optimus.Active.[i].Decal) |> List.ofSeq
    Assert.Equal<string list>([ "Light"; "Suspension"; "Up"; "Down" ], walkedDecals)
