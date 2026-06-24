module Stem.ButtonPanelTester.Tests.Integration.Can.ButtonPressRecoveryTests

open System
open System.Threading
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Can
open Stem.ButtonPanelTester.Tests.Fakes
open Stem.ButtonPanelTester.Tests.Fakes.Can

/// Integration tests for the US2 recovery transitions (spec-005 Phase E, T026):
/// a wrong ACTIVE button scored `Unexpected` (not counted, prompt stays, FR-008/
/// SC-004); `Retry` re-arming the same button with a fresh countdown (FR-009);
/// `Skip` recording `Skipped` (≠ `Pass`) and advancing (FR-009); and the
/// held-button edge case (a held press registers once — no second score on the
/// same held frame). Driven synchronously over the fakes + `FrozenClock`.

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
    let buttons = InMemoryButtonStateObserver()

    let service =
        new ButtonPressTestService(buttons, link, clock, NullLogger<ButtonPressTestService>.Instance)

    { Clock = clock
      Buttons = buttons
      Service = service }

let private press (h: Harness) (bit: int) =
    h.Buttons.Emit idle
    h.Buttons.Emit(pressedFrame bit)

/// Advance the frozen clock to `at`, emit a keep-alive idle heartbeat (so the
/// panel stays observable under the recency model, fix #270 — a present panel
/// heartbeats ~182 ms), then fire the deadline tick. Without it the 3 s
/// panel-lost threshold would pre-empt the 10 s `Missed`.
let private tickAt (h: Harness) (at: DateTimeOffset) =
    h.Clock.SetTo at
    h.Buttons.Emit idle
    h.Service.RunDeadlineTick()

// --- a wrong ACTIVE button is Unexpected: not counted, prompt stays ---

[<Fact>]
let ButtonPressRecovery_WrongActiveButton_NotCountedPromptStays () =
    let h = newHarness ()
    let _task = h.Service.RunAsync(selectedUuid, optimus, CancellationToken.None)

    // Button 0 (DOWN, bit 1) is prompted; the technician presses P1 (bit 2) —
    // an ACTIVE button, but not the one prompted: `RecordUnexpected`, no advance,
    // button 0 stays `Pending` (FR-008 — logged, not counted).
    press h 2

    match h.Service.CurrentState with
    | Prompting(0, _, results) -> Assert.Equal(Pending, results.[0])
    | s -> failwithf "expected Prompting 0 Pending, got %A" s

// --- Retry re-arms the current button so a fresh press passes ---

[<Fact>]
let ButtonPressRecovery_RetryReArmsCurrentButton () =
    let h = newHarness ()
    let _task = h.Service.RunAsync(selectedUuid, optimus, CancellationToken.None)

    // Time out button 0 → Missed (keep-alive heartbeat keeps the panel observable).
    tickAt h (fixedNow + TimeSpan.FromSeconds 11.0)

    // Retry re-arms the SAME button back to `Pending` with a fresh countdown.
    h.Service.Retry()

    match h.Service.CurrentState with
    | Prompting(0, _, results) -> Assert.Equal(Pending, results.[0])
    | s -> failwithf "expected Prompting 0 Pending after Retry, got %A" s

    // The re-armed button now scores `Pass` on a press and advances.
    press h 1

    match h.Service.CurrentState with
    | Prompting(1, _, results) -> Assert.Equal(Pass, results.[0])
    | s -> failwithf "expected Prompting 1 with button 0 Pass, got %A" s

// --- Skip records Skipped (≠ Pass) and advances ---

[<Fact>]
let ButtonPressRecovery_Skip_RecordsSkippedAndAdvances () =
    let h = newHarness ()
    let _task = h.Service.RunAsync(selectedUuid, optimus, CancellationToken.None)

    h.Service.Skip()

    match h.Service.CurrentState with
    | Prompting(1, _, results) ->
        Assert.Equal(Skipped, results.[0])
        Assert.NotEqual(Pass, results.[0])
    | s -> failwithf "expected Prompting 1 with button 0 Skipped, got %A" s

// --- a held button registers once: the held frame yields no second score ---

[<Fact>]
let ButtonPressRecovery_HeldButton_RegistersOnce () =
    let h = newHarness ()
    let _task = h.Service.RunAsync(selectedUuid, optimus, CancellationToken.None)

    // Press button 0 (DOWN, bit 1) → Pass, advance to button 1.
    press h 1

    match h.Service.CurrentState with
    | Prompting(1, _, results) -> Assert.Equal(Pass, results.[0])
    | s -> failwithf "expected Prompting 1 with button 0 Pass, got %A" s

    // The button is HELD (the same pressed frame is re-observed, no release):
    // there is no new `1 → 0` edge, so nothing scores — the prompt stays at
    // button 1 and button 0's `Pass` is unchanged (registered once).
    h.Buttons.Emit(pressedFrame 1)

    match h.Service.CurrentState with
    | Prompting(1, _, results) -> Assert.Equal<ButtonOutcome[]>([| Pass; Pending; Pending; Pending |], results)
    | s -> failwithf "expected Prompting 1 unchanged, got %A" s
