module Stem.ButtonPanelTester.Tests.Unit.Can.ButtonPressTestLoggingTests

open System
open System.Threading
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Can
open Stem.ButtonPanelTester.Tests.Fakes
open Stem.ButtonPanelTester.Tests.Fakes.Can

/// Unit tests for the pure `ButtonPressTestLogging` projections (spec-005 Phase
/// E, T029) plus integration tests that drive a real `ButtonPressTestService`
/// with a `RecordingLogger` and assert the FR-012 forensic trail (R10): a
/// record per prompt / score / timeout / wrong-press / interruption — with the
/// observed bit, at the right level (Information vs Warning), and carrying NO
/// operator-identity field (Principle V).

// --- pure: outcomeName (the four ButtonOutcome shapes) ---

[<Fact>]
let OutcomeName_AllShapes_RenderStableNames () =
    Assert.Equal("Pending", ButtonPressTestLogging.outcomeName Pending)
    Assert.Equal("Pass", ButtonPressTestLogging.outcomeName Pass)
    Assert.Equal("Missed", ButtonPressTestLogging.outcomeName Missed)
    Assert.Equal("Skipped", ButtonPressTestLogging.outcomeName Skipped)

// --- pure: interruptReasonName (both InterruptReason shapes) ---

[<Fact>]
let InterruptReasonName_AllShapes_RenderStableNames () =
    Assert.Equal("LinkLost", ButtonPressTestLogging.interruptReasonName InterruptReason.LinkLost)
    Assert.Equal("PanelLost", ButtonPressTestLogging.interruptReasonName InterruptReason.PanelLost)

// --- pure: uuidText (the canonical hex triple correlation key) ---

[<Fact>]
let UuidText_RendersHexTriple () =
    Assert.Equal("0000177C-0000126D-00007308", ButtonPressTestLogging.uuidText (PanelUuid(0x177Cu, 0x126Du, 0x7308u)))

// --- integration harness (mirrors the E2E suites, with a RecordingLogger) ---

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

let private idle: ButtonStateFrame =
    { Address = VariableAddress 0x8000us
      Bitmap = KeyStateBitmap 0xFFuy }

let private pressedFrame (bit: int) : ButtonStateFrame =
    { Address = VariableAddress 0x8000us
      Bitmap = KeyStateBitmap(0xFFuy &&& ~~~(1uy <<< bit)) }

type private Harness =
    { Clock: FrozenClock
      Buttons: InMemoryButtonStateObserver
      Link: ICanLinkService
      Logger: RecordingLogger<ButtonPressTestService>
      Service: ButtonPressTestService }

let private newHarness (linkFactory: IClock -> ICanLinkService) : Harness =
    let clock = FrozenClock(fixedNow)
    let link = linkFactory (clock :> IClock)
    let whoIAm = InMemoryWhoIAmObserver()
    let discovery = new PanelDiscoveryService(whoIAm, link, clock, NullLogger<PanelDiscoveryService>.Instance)
    let buttons = InMemoryButtonStateObserver()
    let logger = RecordingLogger<ButtonPressTestService>()
    let service = new ButtonPressTestService(buttons, discovery, link, clock, logger)

    { Clock = clock
      Buttons = buttons
      Link = link
      Logger = logger
      Service = service }

let private press (h: Harness) (bit: int) =
    h.Buttons.Emit idle
    h.Buttons.Emit(pressedFrame bit)

/// The forensic records — every entry carrying the `Action` field (the scope's
/// correlation key is captured separately, not in the message values).
let private records (h: Harness) =
    h.Logger.Entries
    |> Seq.filter (fun e -> e.Values.ContainsKey "Action")
    |> List.ofSeq

let private actionsAndLevels (h: Harness) =
    records h |> List.map (fun e -> string e.Values.["Action"], e.Level)

/// The only field keys a forensic record is allowed to carry — NO operator
/// identity / OS-user / machine name (Principle V). `{OriginalFormat}` is the
/// template string the logging infrastructure always attaches.
let private allowedKeys =
    set [ "Action"; "Index"; "Decal"; "Bit"; "At"; "Reason"; "AllPassed"; "{OriginalFormat}" ]

// --- a happy run emits prompt + score per button, all Information ---

[<Fact>]
let HappyRun_EmitsPromptThenScorePerButton_AllInformation () =
    let h = newHarness connectedLink
    let task = h.Service.RunAsync(selectedUuid, optimus, CancellationToken.None)

    for bit in [ 1; 2; 4; 5 ] do
        press h bit

    task.GetAwaiter().GetResult() |> ignore

    // The forensic trail: prompt then Pass for each of the four buttons, then a
    // completion record — every one at Information.
    Assert.Equal<(string * LogLevel) list>(
        [ "Prompt", LogLevel.Information
          "Pass", LogLevel.Information
          "Prompt", LogLevel.Information
          "Pass", LogLevel.Information
          "Prompt", LogLevel.Information
          "Pass", LogLevel.Information
          "Prompt", LogLevel.Information
          "Pass", LogLevel.Information
          "Completed", LogLevel.Information ],
        actionsAndLevels h)

    // The first scored press carries the OBSERVED wire bit (DOWN = bit 1, R10).
    let firstPass = records h |> List.find (fun e -> string e.Values.["Action"] = "Pass")
    Assert.Equal(box 1, firstPass.Values.["Bit"])

// --- a timeout emits a Missed record at Warning ---

[<Fact>]
let Timeout_EmitsMissed_AtWarning () =
    let h = newHarness connectedLink
    let _task = h.Service.RunAsync(selectedUuid, optimus, CancellationToken.None)

    h.Clock.SetTo(fixedNow + TimeSpan.FromSeconds 11.0)
    h.Service.RunDeadlineTick()

    let missed = records h |> List.filter (fun e -> string e.Values.["Action"] = "Missed")
    Assert.Equal(1, missed.Length)
    Assert.Equal(LogLevel.Warning, missed.[0].Level)

// --- a wrong ACTIVE press emits an Unexpected record at Warning, carrying its bit ---

[<Fact>]
let WrongActivePress_EmitsUnexpected_AtWarning () =
    let h = newHarness connectedLink
    let _task = h.Service.RunAsync(selectedUuid, optimus, CancellationToken.None)

    // Button 0 (DOWN, bit 1) is prompted; press P1 (bit 2) — an active button,
    // but not the one prompted.
    press h 2

    let unexpected = records h |> List.filter (fun e -> string e.Values.["Action"] = "Unexpected")
    Assert.Equal(1, unexpected.Length)
    Assert.Equal(LogLevel.Warning, unexpected.[0].Level)
    Assert.Equal(box 2, unexpected.[0].Values.["Bit"])

// --- an interruption emits an Interrupted record at Warning ---

[<Fact>]
let Interruption_EmitsInterrupted_AtWarning () =
    let h = newHarness connectThenDisconnectLink
    let task = h.Service.RunAsync(selectedUuid, optimus, CancellationToken.None)

    h.Link.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()
    task.GetAwaiter().GetResult() |> ignore

    let interrupted = records h |> List.filter (fun e -> string e.Values.["Action"] = "Interrupted")
    Assert.Equal(1, interrupted.Length)
    Assert.Equal(LogLevel.Warning, interrupted.[0].Level)
    Assert.Equal(box "LinkLost", interrupted.[0].Values.["Reason"])

// --- no forensic record carries an operator-identity field (Principle V) ---

[<Fact>]
let NoForensicRecord_CarriesOperatorIdentity () =
    let h = newHarness connectedLink
    let task = h.Service.RunAsync(selectedUuid, optimus, CancellationToken.None)

    for bit in [ 1; 2; 4; 5 ] do
        press h bit

    task.GetAwaiter().GetResult() |> ignore

    // Every emitted record's fields are drawn ONLY from the forensic allowlist —
    // no OS-user, machine name, SID, or any other operator identity anywhere.
    for entry in records h do
        for KeyValue(key, _) in entry.Values do
            Assert.True(allowedKeys.Contains key, $"unexpected forensic field {key}")
