module Stem.ButtonPanelTester.Tests.Integration.Can.ButtonPressTimeoutTests

open System
open System.Threading
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Can
open Stem.ButtonPanelTester.Tests.Fakes
open Stem.ButtonPanelTester.Tests.Fakes.Can

/// `FrozenClock`-driven integration tests for the per-button deadline (spec-005
/// Phase E, T025, US2): a button still prompting just under the 10 s window;
/// crossing the deadline scoring `Missed` with Retry/Skip still offered (SC-003);
/// and a matching press AFTER the reported `Missed` never flipping the outcome
/// (the `terminal_absorbs` / never-flip rule). The deadline is stepped through
/// `RunDeadlineTick` against the `FrozenClock` — no wall-clock sleeps.

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

// --- just under the deadline still prompts; crossing it scores Missed ---

[<Fact>]
let ButtonPressTimeout_JustUnderThenCrossDeadline_MissedWithRetrySkipOffered () =
    let h = newHarness ()
    let _task = h.Service.RunAsync(selectedUuid, optimus, CancellationToken.None)

    // Just under the 10 s window (deadline = now + testBudget): still Pending.
    h.Clock.SetTo(fixedNow + TimeSpan.FromSeconds 9.0)
    h.Service.RunDeadlineTick()

    match h.Service.CurrentState with
    | Prompting(0, _, results) -> Assert.Equal(Pending, results.[0])
    | s -> failwithf "expected Prompting 0 Pending, got %A" s

    // Cross the deadline → the prompted button is `Missed`, the run stays at
    // index 0 offering Retry/Skip (it does NOT advance on a timeout).
    h.Clock.SetTo(fixedNow + TimeSpan.FromSeconds 11.0)
    h.Service.RunDeadlineTick()

    match h.Service.CurrentState with
    | Prompting(0, _, results) -> Assert.Equal(Missed, results.[0])
    | s -> failwithf "expected Prompting 0 Missed, got %A" s

// --- a matching press AFTER Missed does NOT flip the outcome (never-flip) ---

[<Fact>]
let ButtonPressTimeout_MatchingPressAfterMissed_DoesNotFlip () =
    let h = newHarness ()
    let _task = h.Service.RunAsync(selectedUuid, optimus, CancellationToken.None)

    // Time out button 0.
    h.Clock.SetTo(fixedNow + TimeSpan.FromSeconds 11.0)
    h.Service.RunDeadlineTick()

    match h.Service.CurrentState with
    | Prompting(0, _, results) -> Assert.Equal(Missed, results.[0])
    | s -> failwithf "expected Prompting 0 Missed, got %A" s

    // The technician presses the prompted button (DOWN, bit 1) anyway: a press
    // edge for an already-`Missed` button is a no-op — `Missed` never flips to
    // `Pass` (Lean `terminal_absorbs` / `pass_requires_press_edge`).
    press h 1

    match h.Service.CurrentState with
    | Prompting(0, _, results) -> Assert.Equal(Missed, results.[0])
    | s -> failwithf "expected Prompting 0 still Missed, got %A" s
