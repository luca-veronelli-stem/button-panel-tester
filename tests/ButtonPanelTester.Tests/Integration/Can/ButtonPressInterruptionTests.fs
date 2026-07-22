module Stem.ButtonPanelTester.Tests.Integration.Can.ButtonPressInterruptionTests

open System
open System.Threading
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Can
open Stem.ButtonPanelTester.Tests.Fakes
open Stem.ButtonPanelTester.Tests.Fakes.Can

/// Integration tests for the interruption + inactive-bit transitions (spec-005
/// Phase E, T027; observability re-keyed in fix #270): the link leaving
/// `Connected` mid-prompt → `Interrupted LinkLost`, never all-passed; the panel
/// falling silent (no button-state heartbeat for longer than `panelLostThreshold`)
/// mid-prompt → `Interrupted PanelLost`; and a press for an INACTIVE position
/// (outside the variant mask) ignored, never a prompted-button result (FR-013/
/// FR-014; SC-005). Driven synchronously over the fakes + `FrozenClock`.

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

/// A scripted link: Connected, then Disconnected. `InitializeAsync` reaches
/// Connected; a later `ReconnectAsync` dequeues the Disconnected step so the
/// link leaves `Connected` mid-run (the `BaptismE2ETests` precedent).
let private connectThenDisconnectLink (clock: IClock) : ICanLinkService =
    let link =
        InMemoryCanLink(
            seq {
                (Connected(fixedAdapter, fixedNow), TimeSpan.Zero)
                (Disconnected(MidSessionUnplug, fixedNow), TimeSpan.Zero)
            })

    let svc = CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)
    (svc :> ICanLinkService).InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    svc :> ICanLinkService

let private optimus = ButtonSchema.forVariant OptimusXp

let private selectedUuid = PanelUuid(0x177Cu, 0x126Du, 0x7308u)

let private pressedFrame (bit: int) : ButtonStateFrame =
    { Address = VariableAddress 0x8000us
      Bitmap = KeyStateBitmap(0xFFuy &&& ~~~(1uy <<< bit)) }

let private idle: ButtonStateFrame =
    { Address = VariableAddress 0x8000us
      Bitmap = KeyStateBitmap 0xFFuy }

type private Harness =
    { Clock: FrozenClock
      Buttons: InMemoryButtonStateObserver
      Link: ICanLinkService
      Service: ButtonPressTestService }

let private newHarness (linkFactory: IClock -> ICanLinkService) : Harness =
    let clock = FrozenClock(fixedNow)
    let link = linkFactory (clock :> IClock)
    let buttons = InMemoryButtonStateObserver()

    let service =
        new ButtonPressTestService(buttons, link, clock, NullLogger<ButtonPressTestService>.Instance)

    { Clock = clock
      Buttons = buttons
      Link = link
      Service = service }

// --- the link leaving Connected mid-prompt → Interrupted LinkLost ---

[<Fact>]
let ButtonPressInterruption_LinkLeavesConnected_InterruptedLinkLost () =
    let h = newHarness connectThenDisconnectLink
    let task = h.Service.RunAsync(selectedUuid, optimus, CancellationToken.None)

    // The adapter is unplugged mid-run: the link leaves `Connected` → the run
    // halts in `Interrupted LinkLost`, never reporting all-passed (FR-013).
    h.Link.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()

    match task.GetAwaiter().GetResult() with
    | Interrupted(InterruptReason.LinkLost, partial) -> Assert.False(ButtonPressTest.allActivePassed partial)
    | s -> failwithf "expected Interrupted LinkLost, got %A" s

// --- the panel falling silent (no heartbeat) mid-prompt → Interrupted PanelLost ---

[<Fact>]
let ButtonPressInterruption_PanelStopsHeartbeating_InterruptedPanelLost () =
    let h = newHarness connectedLink
    let _task = h.Service.RunAsync(selectedUuid, optimus, CancellationToken.None)

    // The panel is heartbeating (one observed frame), then falls silent: no
    // button-state frame for longer than `panelLostThreshold` (20 s — above the
    // ~12.5 s slow heartbeat branch, #293) during the run, so the next deadline
    // tick halts in `Interrupted PanelLost` (FR-013 — recency, not discovery
    // pruning; fix #270).
    h.Buttons.Emit idle
    h.Clock.SetTo(fixedNow + TimeSpan.FromSeconds 21.0)
    h.Service.RunDeadlineTick()

    match h.Service.CurrentState with
    | Interrupted(InterruptReason.PanelLost, partial) -> Assert.False(ButtonPressTest.allActivePassed partial)
    | s -> failwithf "expected Interrupted PanelLost, got %A" s

// --- the slow-cadence heartbeat (~12.5 s, cold panel) must NOT read as PanelLost ---

[<Fact>]
let ButtonPressInterruption_PanelHeartbeatsAtSlowCadence_RunStaysLive () =
    let h = newHarness connectedLink
    let _task = h.Service.RunAsync(selectedUuid, optimus, CancellationToken.None)

    // A cold panel heartbeats its all-idle button state on the SLOW branch
    // (~12.5 s, TEMPO_CAN_LENTO — UserMain.c:1013-1020; #293): consecutive
    // button-state frames arrive 12.5 s apart, each gap crossed by a deadline
    // tick. The slow cadence is normal firmware behavior, not a lost panel, so
    // the run must stay live — never `Interrupted PanelLost`.
    h.Buttons.Emit idle

    for _ in 1..3 do
        h.Clock.Advance(TimeSpan.FromSeconds 12.5)
        h.Service.RunDeadlineTick()
        h.Buttons.Emit idle

    match h.Service.CurrentState with
    | Prompting _ -> () // still live (button 0 may be Missed on its 10 s budget — that is fine)
    | s -> failwithf "expected the run to stay live in Prompting, got %A" s

// --- a press for an INACTIVE position (outside the variant mask) is ignored ---

[<Fact>]
let ButtonPressInterruption_InactivePositionPress_Ignored () =
    let h = newHarness connectedLink
    let _task = h.Service.RunAsync(selectedUuid, optimus, CancellationToken.None)

    // Bit 0 (UP) is OUTSIDE the OPTIMUS-XP active mask (0x36 = bits 1,2,4,5):
    // the detector never reports it (FR-014), so the prompt is untouched.
    h.Buttons.Emit idle
    h.Buttons.Emit(pressedFrame 0)

    match h.Service.CurrentState with
    | Prompting(0, _, results) -> Assert.Equal<ButtonOutcome[]>([| Pending; Pending; Pending; Pending |], results)
    | s -> failwithf "expected Prompting 0 unchanged, got %A" s
