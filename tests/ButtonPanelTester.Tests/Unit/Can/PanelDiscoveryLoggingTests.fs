module Stem.ButtonPanelTester.Tests.Unit.Can.PanelDiscoveryLoggingTests

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

/// Unit tests for `PanelDiscoveryService`'s structured domain-event logging
/// (#204 / T001): a NEW panel logs at `Information` (carrying the `Uuid`
/// field), while a re-broadcast of the same UUID does NOT add an
/// `Information` entry. Drives a real `CanLinkService` (wrapping
/// `InMemoryCanLink`) to `Connected`, then feeds decoded frames through
/// `InMemoryWhoIAmObserver.Emit` against a `RecordingLogger` so the captured
/// level + structured fields can be asserted directly.

// --- fixtures (minimal local copies of the DiscoveryE2ETests drivers) ---

let private fixedNow =
    DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero)

let private fixedAdapter : AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"
      DeviceId = "0x01"
      BaudrateBps = 250_000 }

/// A real `CanLinkService` (wrapping `InMemoryCanLink`) driven to `Connected`.
let private connectedLink (clock: IClock) : ICanLinkService =
    let link =
        InMemoryCanLink(seq { (Connected(fixedAdapter, fixedNow), TimeSpan.Zero) })

    let svc =
        CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)

    (svc :> ICanLinkService).InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
    svc :> ICanLinkService

/// Build a decoded WHO_I_AM frame for the observer feed (virgin machineType,
/// 12 V hardware variant).
let private whoIamFrame (u0, u1, u2) : WhoIAmFrame =
    { MachineType = MachineTypeByte 0xFFuy; FwType = FwType 0x0004us; Uuid = PanelUuid(u0, u1, u2) }

// --- (1) a fresh UUID logs exactly one Information entry carrying Uuid ---

[<Fact>]
let NewPanel_FirstObservation_LogsExactlyOneInformation () =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let observer = InMemoryWhoIAmObserver()
    let logger = RecordingLogger<PanelDiscoveryService>()

    use _svc =
        new PanelDiscoveryService(observer, link, clock, logger)

    observer.Emit(whoIamFrame (0x177Cu, 0x126Du, 0x7308u))

    let infoUuidEntries =
        logger.Entries
        |> Seq.filter (fun e -> e.Level = LogLevel.Information && e.Values.ContainsKey "Uuid")
        |> List.ofSeq

    Assert.Equal(1, infoUuidEntries.Length)

// --- (2) a re-broadcast of the same UUID adds no further Information entry ---

[<Fact>]
let SameUuidReBroadcast_DoesNotLogInformation () =
    let clock = FrozenClock(fixedNow)
    let link = connectedLink (clock :> IClock)
    let observer = InMemoryWhoIAmObserver()
    let logger = RecordingLogger<PanelDiscoveryService>()

    use _svc =
        new PanelDiscoveryService(observer, link, clock, logger)

    let uuid = (0x1u, 0x2u, 0x3u)
    observer.Emit(whoIamFrame uuid)
    observer.Emit(whoIamFrame uuid)

    let informationEntries =
        logger.Entries
        |> Seq.filter (fun e -> e.Level = LogLevel.Information)
        |> List.ofSeq

    Assert.Equal(1, informationEntries.Length)
