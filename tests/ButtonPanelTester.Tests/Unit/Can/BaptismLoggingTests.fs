module Stem.ButtonPanelTester.Tests.Unit.Can.BaptismLoggingTests

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

/// Unit tests for the pure `BaptismLogging` projection helpers (spec-004
/// C6, T026) plus integration-style tests that drive a real
/// `BaptismService` to each terminal outcome and assert it emits EXACTLY
/// ONE structured audit record per attempt (data-model §7) — including the
/// two entry-guard rejections (`LinkLost` / `PanelDisappeared`) that count
/// as attempts but never start the FSM.
///
/// The pure helpers (`outcomeName` / `variantName` / `stepReached` /
/// `uuidText`) are exercised against every shape of the closed DUs they
/// project. The integration tests isolate the audit record by filtering
/// the `RecordingLogger` entries on the `Action` field, which only the
/// audit record carries.

// --- pure: outcomeName (all seven shapes) ---

[<Fact>]
let OutcomeName_AllShapes_RenderStableNames () =
    Assert.Equal("Succeeded", BaptismLogging.outcomeName Succeeded)
    Assert.Equal("WaitTimeout", BaptismLogging.outcomeName WaitTimeout)
    Assert.Equal("UnexpectedVariant", BaptismLogging.outcomeName (UnexpectedVariant(Marketing OptimusXp)))
    Assert.Equal("PanelDisappeared", BaptismLogging.outcomeName PanelDisappeared)
    Assert.Equal("LinkLost", BaptismLogging.outcomeName LinkLost)
    Assert.Equal("TransmissionFailure.ClaimStep", BaptismLogging.outcomeName (TransmissionFailure ClaimStep))
    Assert.Equal("TransmissionFailure.AssignStep", BaptismLogging.outcomeName (TransmissionFailure AssignStep))

// --- pure: variantName (the four marketed names) ---

[<Fact>]
let VariantName_AllVariants_RenderMarketedNames () =
    Assert.Equal("EdenXp", BaptismLogging.variantName EdenXp)
    Assert.Equal("OptimusXp", BaptismLogging.variantName OptimusXp)
    Assert.Equal("R3LXp", BaptismLogging.variantName R3LXp)
    Assert.Equal("EdenBs8", BaptismLogging.variantName EdenBs8)

// --- pure: stepReached ---

[<Fact>]
let StepReached_PreTerminalStates_RenderPhaseNames () =
    Assert.Equal("NotStarted", BaptismLogging.stepReached Idle)
    Assert.Equal("ClaimSent", BaptismLogging.stepReached ClaimSent)
    Assert.Equal(
        "AwaitingAnnounce",
        BaptismLogging.stepReached (AwaitingAnnounce(DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero))))
    Assert.Equal("Assigning", BaptismLogging.stepReached Assigning)

[<Fact>]
let StepReached_Terminal_RenderTerminal () =
    Assert.Equal("Terminal", BaptismLogging.stepReached (Terminal Succeeded))

// --- pure: uuidText ---

[<Fact>]
let UuidText_RendersHexTriple () =
    Assert.Equal("0000000A-000000B0-00000C00", BaptismLogging.uuidText (PanelUuid(0x0Au, 0xB0u, 0xC00u)))
    Assert.Equal("177C126D-00007308-00000001", BaptismLogging.uuidText (PanelUuid(0x177C126Du, 0x7308u, 0x1u)))

// --- integration harness (mirrors BaptismE2ETests, but with a RecordingLogger) ---

let private fixedNow = DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero)

let private fixedAdapter: AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"; DeviceId = "0x01"; BaudrateBps = 250_000 }

let private announcedFwType = 0x000Fus

/// A real `CanLinkService` (wrapping `InMemoryCanLink`) driven to
/// `Connected` via `InitializeAsync` (the `BaptismE2ETests` precedent).
let private connectedLink (clock: IClock) : ICanLinkService =
    let link = InMemoryCanLink(seq { (Connected(fixedAdapter, fixedNow), TimeSpan.Zero) })
    let svc = CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)
    (svc :> ICanLinkService).InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    svc :> ICanLinkService

/// A real `CanLinkService` left UN-initialized, so its `CurrentState` is
/// `Initializing` (not `Connected`) — the entry-guard `LinkLost` fixture.
let private notConnectedLink (clock: IClock) : ICanLinkService =
    let link = InMemoryCanLink(seq { (Connected(fixedAdapter, fixedNow), TimeSpan.Zero) })
    CanLinkService(link, clock, NullLogger<CanLinkService>.Instance) :> ICanLinkService

let private frameOf (machineType: byte) (u0, u1, u2) : WhoIAmFrame =
    { MachineType = MachineTypeByte machineType
      FwType = FwType announcedFwType
      Uuid = PanelUuid(u0, u1, u2) }

type private Harness =
    { Clock: FrozenClock
      Observer: InMemoryWhoIAmObserver
      Discovery: PanelDiscoveryService
      Transmitter: InMemoryMasterSequenceTransmitter
      Logger: RecordingLogger<BaptismService>
      Service: BaptismService }

let private newHarnessWith (link: IClock -> ICanLinkService) : Harness =
    let clock = FrozenClock(fixedNow)
    let canLink = link (clock :> IClock)
    let observer = InMemoryWhoIAmObserver()
    let discovery = new PanelDiscoveryService(observer, canLink, clock, NullLogger<PanelDiscoveryService>.Instance)
    let transmitter = InMemoryMasterSequenceTransmitter(clock :> IClock)
    let logger = RecordingLogger<BaptismService>()

    let service =
        new BaptismService(transmitter, observer, discovery, canLink, clock, logger)

    { Clock = clock
      Observer = observer
      Discovery = discovery
      Transmitter = transmitter
      Logger = logger
      Service = service }

let private newHarness () : Harness = newHarnessWith connectedLink

/// The single audit record for the attempt — the only entry carrying the
/// `Action` field. Asserts exactly one such record and returns its values.
let private auditValues (h: Harness) : Map<string, obj> =
    let records =
        h.Logger.Entries
        |> Seq.filter (fun e -> e.Values.ContainsKey "Action")
        |> List.ofSeq

    Assert.Equal(1, records.Length)
    Assert.Equal(LogLevel.Information, records.[0].Level)
    records.[0].Values

/// Assert the shared audit fields: `Action = "Baptize"`, the outcome,
/// the variant, the uuid hex triple, the furthest step, and the presence
/// of the two ISO instants.
let private assertAudit
    (values: Map<string, obj>)
    (outcome: string)
    (variant: string)
    (uuid: string)
    (step: string)
    =
    Assert.Equal(box "Baptize", values.["Action"])
    Assert.Equal(box outcome, values.["Outcome"])
    Assert.Equal(box variant, values.["Variant"])
    Assert.Equal(box uuid, values.["PanelUuid"])
    Assert.Equal(box step, values.["StepReached"])
    Assert.True(values.ContainsKey "StartedAt")
    Assert.True(values.ContainsKey "CompletedAt")
    // The instants round-trip through DateTimeOffset (ISO-8601 "O").
    Assert.True(DateTimeOffset.TryParse(string values.["StartedAt"]) |> fst)
    Assert.True(DateTimeOffset.TryParse(string values.["CompletedAt"]) |> fst)

// --- Succeeded: one record, StepReached "Assigning" ---

[<Fact>]
let Baptize_Succeeded_EmitsOneRecord () =
    let h = newHarness ()
    let uuid = (0x177Cu, 0x126Du, 0x7308u)
    let variant = EdenXp
    h.Observer.Emit(frameOf 0xFFuy uuid)

    let task = h.Service.BaptizeAsync(PanelUuid uuid, variant, CancellationToken.None)
    h.Observer.Emit(frameOf (BoardVariant.encode variant) uuid)
    Assert.Equal(Succeeded, task.GetAwaiter().GetResult())

    assertAudit (auditValues h) "Succeeded" "EdenXp" "0000177C-0000126D-00007308" "Assigning"

// --- UnexpectedVariant: one record, StepReached "AwaitingAnnounce" ---

[<Fact>]
let Baptize_UnexpectedVariant_EmitsOneRecord () =
    let h = newHarness ()
    let uuid = (0x1u, 0x2u, 0x3u)
    h.Observer.Emit(frameOf 0xFFuy uuid)

    let task = h.Service.BaptizeAsync(PanelUuid uuid, EdenXp, CancellationToken.None)
    h.Observer.Emit(frameOf (BoardVariant.encode OptimusXp) uuid)
    Assert.Equal(UnexpectedVariant(Marketing OptimusXp), task.GetAwaiter().GetResult())

    assertAudit (auditValues h) "UnexpectedVariant" "EdenXp" "00000001-00000002-00000003" "AwaitingAnnounce"

// --- WaitTimeout: one record via RunDeadlineTick, StepReached "AwaitingAnnounce" ---

[<Fact>]
let Baptize_WaitTimeout_EmitsOneRecord () =
    let h = newHarness ()
    let uuid = (0x1u, 0x2u, 0x3u)
    h.Observer.Emit(frameOf 0xFFuy uuid)

    let task = h.Service.BaptizeAsync(PanelUuid uuid, EdenXp, CancellationToken.None)
    h.Clock.SetTo(fixedNow + Baptism.announceBudget)
    h.Service.RunDeadlineTick()
    Assert.Equal(WaitTimeout, task.GetAwaiter().GetResult())

    assertAudit (auditValues h) "WaitTimeout" "EdenXp" "00000001-00000002-00000003" "AwaitingAnnounce"

// --- TransmissionFailure ClaimStep: one record, StepReached "ClaimSent" ---

[<Fact>]
let Baptize_ClaimFault_EmitsOneRecord () =
    let h = newHarness ()
    let uuid = (0x1u, 0x2u, 0x3u)
    h.Observer.Emit(frameOf 0xFFuy uuid)
    h.Transmitter.ScriptFault(1, exn "claim write failed")

    let outcome = h.Service.BaptizeAsync(PanelUuid uuid, EdenXp, CancellationToken.None).GetAwaiter().GetResult()
    Assert.Equal(TransmissionFailure ClaimStep, outcome)

    assertAudit (auditValues h) "TransmissionFailure.ClaimStep" "EdenXp" "00000001-00000002-00000003" "ClaimSent"

// --- TransmissionFailure AssignStep: one record, StepReached "Assigning" ---

[<Fact>]
let Baptize_AssignFault_EmitsOneRecord () =
    let h = newHarness ()
    let uuid = (0x1u, 0x2u, 0x3u)
    let variant = EdenXp
    h.Observer.Emit(frameOf 0xFFuy uuid)
    h.Transmitter.ScriptFault(2, exn "assign write failed")

    let task = h.Service.BaptizeAsync(PanelUuid uuid, variant, CancellationToken.None)
    h.Observer.Emit(frameOf (BoardVariant.encode variant) uuid)
    Assert.Equal(TransmissionFailure AssignStep, task.GetAwaiter().GetResult())

    assertAudit (auditValues h) "TransmissionFailure.AssignStep" "EdenXp" "00000001-00000002-00000003" "Assigning"

// --- entry-guard LinkLost: counts as an attempt, one record, StepReached "NotStarted" ---

[<Fact>]
let Baptize_EntryGuardLinkLost_EmitsOneRecord () =
    let h = newHarnessWith notConnectedLink
    let uuid = (0x1u, 0x2u, 0x3u)

    // Link is not Connected at entry → the attempt is rejected as LinkLost
    // WITHOUT starting the FSM, but the audit record must still be emitted.
    let outcome = h.Service.BaptizeAsync(PanelUuid uuid, EdenXp, CancellationToken.None).GetAwaiter().GetResult()
    Assert.Equal(LinkLost, outcome)

    assertAudit (auditValues h) "LinkLost" "EdenXp" "00000001-00000002-00000003" "NotStarted"

// --- entry-guard PanelDisappeared: selected uuid absent from discovery ---

[<Fact>]
let Baptize_EntryGuardPanelDisappeared_EmitsOneRecord () =
    let h = newHarness ()
    let uuid = (0x1u, 0x2u, 0x3u)

    // The selected uuid was never observed, so it is absent from
    // PanelsOnBus at the entry guard → PanelDisappeared without starting
    // the FSM; the audit record must still be emitted.
    let outcome = h.Service.BaptizeAsync(PanelUuid uuid, EdenXp, CancellationToken.None).GetAwaiter().GetResult()
    Assert.Equal(PanelDisappeared, outcome)

    assertAudit (auditValues h) "PanelDisappeared" "EdenXp" "00000001-00000002-00000003" "NotStarted"
