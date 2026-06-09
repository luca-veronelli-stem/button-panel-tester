module Stem.ButtonPanelTester.Tests.Windows.Integration.Can.Hardware.DiscoveryHardwareTests

open System
open System.Collections.Generic
open System.Threading
open Microsoft.Extensions.Logging.Abstractions
open Peak.Can.Basic.BackwardCompatibility
open Core.Interfaces
open Infrastructure.Protocol.Hardware
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary // IClock
open Stem.ButtonPanelTester.Infrastructure // SystemClock
open Stem.ButtonPanelTester.Infrastructure.Can // CanPortShare, PcanCanLink, PcanCanFrameStream, WhoIAmReassemblyObserver
open Stem.ButtonPanelTester.Services.Can // CanLinkService, PanelDiscoveryService, IPanelDiscoveryService
open Stem.ButtonPanelTester.Tests.Windows.Fixtures // HardwareFact / ManualHardwareFact

/// Production-path hardware E2E for spec-003 panel discovery — the **live-boundary
/// proof**. The #121 codec bug and the segmented-transport bug both survived for a
/// release because synthetic fixtures + Avalonia.Headless/golden tests stayed green
/// while the real wire path was broken: the read loop never started and WHO_I_AM is a
/// 5-frame segmented SP_APP message, not the single 15-byte frame the unit fixtures
/// modelled. These cases close that gap (see `specs/003-panel-discovery/tasks.md`
/// Phase E). They drive the FULL production chain on a bench rig —
/// read loop → `PcanCanFrameStream` → `WhoIAmReassemblyObserver` →
/// `PanelDiscoveryService` — and read the parsed UUID + variant off the REASSEMBLED
/// real wire message through that chain, so a regression in EITHER the codec OR the
/// segmented transport fails here where the headless suite cannot.
///
/// Tracked by [#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112)
/// (reopened 2026-06-09 as the live cross-spec `Category=Hardware` tracker).
///
/// **Gating.** Every case carries `[<Trait("Category", "Hardware")>]`, so the standards
/// CI category filter (`Category!=Hardware`) excludes the suite at discovery time. The
/// env gate (`[<HardwareFact>]` → `BPT_HARDWARE=1`; `[<ManualHardwareFact>]` →
/// `BPT_HARDWARE_INTERACTIVE=1`) keeps it dormant on adapter-less dev boxes. The bench
/// run is the spec-003 Done gate (T032), not part of CI:
///   $env:BPT_HARDWARE = "1"                # SC-001 (unattended)
///   $env:BPT_HARDWARE_INTERACTIVE = "1"    # + FR-005 prune (attended)
///   dotnet test tests/ButtonPanelTester.Tests.Windows --filter "Category=Hardware"
///
/// **SC-003 / FR-009 (zero tool-originated frames) is NOT an automated assertion here**
/// — it is an ATTENDED bench-capture step. The discovery path is receive-only by
/// construction (no `SendMessageAsync` anywhere in `PcanCanFrameStream` /
/// `WhoIAmReassemblyObserver` / `PanelDiscoveryService`, and `PcanCanLink.OpenAsync`
/// issues no TX), and PCANBasic exposes no TX-frame counter to assert against. To
/// confirm SC-003 on the bench, run PCAN-View (or capture a `.trc`) on a SECOND channel
/// during the SC-001 run and verify zero frames originate from the tool's channel.
/// Record that observation in the T032 bench validation.

// --- fixtures ---

/// SC-001 budget: a powered virgin panel broadcasts WHO_I_AM roughly every ~4 s, so
/// 6 s comfortably covers one full segmented broadcast cycle plus reassembly.
let private discoveryTimeout = TimeSpan.FromSeconds(6.0)

/// FR-005 prune budget after power-off: the real `PanelDiscoveryService` prune timer
/// ticks every 1 s against a 15 s `LastSeen` TTL on `SystemClock`, so the row ages out
/// within ~16 s of the last broadcast. Wait 18 s to leave slack for the final pre-off
/// broadcast still being inside the TTL window when power is cut.
let private pruneTimeout = TimeSpan.FromSeconds(18.0)

/// A `PanelUuid` is "real" when at least one of its three words is non-zero — the
/// all-zero UUID is what a failed/empty parse would surface.
let private isNonZeroUuid (PanelUuid(uuid0, uuid1, uuid2)) =
    uuid0 <> 0u || uuid1 <> 0u || uuid2 <> 0u

/// True when the snapshot carries at least one row decoded as `Virgin` (machineType
/// `0xFF`). On a clean single-panel bench this is exactly one row.
let private hasVirginRow (snapshot: PanelsOnBus) =
    snapshot |> Map.exists (fun _ obs -> obs.VariantIdentity = Virgin)

/// Builds the FULL production discovery chain over the real PEAK stack at 250 kbps and
/// returns the lifecycle service, the discovery service, and a single `IDisposable`
/// that tears everything down in REVERSE construction order (discovery → observer →
/// frame stream → link → port → driver) — mirrors `PcanLifecycleTests.buildLink` and
/// the retired smoke's `finally`. The captured driver / port may be `None` if a test
/// never opens the link; the cleanup is a no-op in that case.
let private buildChain () : ICanLinkService * IPanelDiscoveryService * IDisposable =
    let mutable createdDriver: PCANManager option = None
    let mutable createdPort: CanPort option = None

    let share =
        new CanPortShare(fun () ->
            let driver = new PCANManager(TPCANBaudrate.PCAN_BAUD_250K)
            let port = new CanPort(driver :> IPcanDriver)
            createdDriver <- Some driver
            createdPort <- Some port
            port :> ICommunicationPort)

    let clock = SystemClock() :> IClock
    let link = PcanCanLink((fun () -> share.GetOrBuild()), NullLogger<PcanCanLink>.Instance)
    let frameStream = new PcanCanFrameStream(share, clock, NullLogger<PcanCanFrameStream>.Instance)
    let observer = new WhoIAmReassemblyObserver(frameStream, NullLogger<WhoIAmReassemblyObserver>.Instance)
    let svc = CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)
    let discovery = new PanelDiscoveryService(observer, (svc :> ICanLinkService), clock)

    let cleanup =
        { new IDisposable with
            member _.Dispose() =
                (discovery :> IDisposable).Dispose()
                (observer :> IDisposable).Dispose()
                (frameStream :> IDisposable).Dispose()
                (link :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()

                createdPort |> Option.iter (fun p -> p.Dispose())

                createdDriver
                |> Option.iter (fun d ->
                    (d :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()) }

    (svc :> ICanLinkService), (discovery :> IPanelDiscoveryService), cleanup

/// Subscribe at composition time (the feed is hot — late subscribers do not replay) and
/// accumulate every published snapshot under a lock, mirroring `collectStates`.
let private collectSnapshots (discovery: IPanelDiscoveryService) : List<PanelsOnBus> =
    let collected = List<PanelsOnBus>()

    let _ =
        discovery.PanelsOnBusChanged
        |> Observable.subscribe (fun snapshot -> lock collected (fun () -> collected.Add snapshot))

    collected

/// Bounded deadline spin for the first collected snapshot matching `predicate` (mirrors
/// `PcanLifecycleTests.waitForState`); `None` on timeout — never an unbounded spin.
let private waitForSnapshot
    (collected: List<PanelsOnBus>)
    (predicate: PanelsOnBus -> bool)
    (timeout: TimeSpan)
    : PanelsOnBus option =
    let deadline = DateTime.UtcNow + timeout

    let rec spin () =
        let observed =
            lock collected (fun () -> collected |> Seq.tryFind predicate)

        match observed with
        | Some _ -> observed
        | None when DateTime.UtcNow >= deadline -> None
        | None ->
            Thread.Sleep(50)
            spin ()

    spin ()

// --- SC-001: a powered virgin panel surfaces a Virgin row within 6 s (unattended) ---

[<Trait("Category", "Hardware")>]
[<HardwareFact>]
let Discovery_RealVirginPanel_SurfacesVirginRowWithin6s () =
    let link, discovery, cleanup = buildChain ()
    use _ = cleanup

    let snapshots = collectSnapshots discovery

    // InitializeAsync opens PcanCanLink (→ CanPort.ConnectAsync → StartReading, R1), so
    // frames flow and PanelDiscoveryService coalesces while the link is Connected.
    link.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()

    match waitForSnapshot snapshots hasVirginRow discoveryTimeout with
    | Some snapshot ->
        let virginRow =
            snapshot
            |> Map.toSeq
            |> Seq.tryPick (fun (uuid, obs) ->
                if obs.VariantIdentity = Virgin then Some(uuid, obs) else None)

        match virginRow with
        | Some(uuid, obs) ->
            Assert.True(obs.VariantIdentity = Virgin, "expected the row's VariantIdentity to be Virgin")
            Assert.True(isNonZeroUuid uuid, sprintf "expected a non-zero PanelUuid, observed %A" uuid)
        | None -> Assert.Fail("Virgin row vanished between detection and assertion")
    | None ->
        Assert.Fail(
            sprintf
                "no Virgin Panels-on-bus row within %.1f s — verify a powered VIRGIN panel is on the bus"
                discoveryTimeout.TotalSeconds
        )

// --- FR-005: powering the panel off prunes its row within ~16 s (attended) ---

[<Trait("Category", "Hardware")>]
[<ManualHardwareFact>]
let Discovery_PanelPoweredOff_RowPrunesWithin16s () =
    let link, discovery, cleanup = buildChain ()
    use _ = cleanup

    let snapshots = collectSnapshots discovery

    link.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()

    // Precondition: the Virgin row must be present before we can observe its prune.
    let virginUuid =
        match waitForSnapshot snapshots hasVirginRow discoveryTimeout with
        | Some snapshot ->
            snapshot
            |> Map.toSeq
            |> Seq.tryPick (fun (uuid, obs) -> if obs.VariantIdentity = Virgin then Some uuid else None)
        | None -> None

    match virginUuid with
    | None ->
        Assert.Fail(
            sprintf
                "no Virgin row within %.1f s — power a VIRGIN panel on the bus before the prune leg"
                discoveryTimeout.TotalSeconds
        )
    | Some uuid ->
        // Operator powers the panel OFF here. With no further broadcasts the row's
        // LastSeen ages past the 15 s TTL and the 1 s prune timer drops it; on a clean
        // single-panel bench the map then becomes empty.
        Console.WriteLine("Power OFF the panel now; waiting ~18 s for the row to prune…")

        match waitForSnapshot snapshots Map.isEmpty pruneTimeout with
        | Some _ -> ()
        | None ->
            Assert.Fail(
                sprintf
                    "row for %A still present after the ~16 s prune budget — the FR-005 TTL prune did not fire within %.1f s of power-off"
                    uuid
                    pruneTimeout.TotalSeconds
            )
