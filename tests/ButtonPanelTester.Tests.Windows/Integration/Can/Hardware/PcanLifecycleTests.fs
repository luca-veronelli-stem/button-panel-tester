module Stem.ButtonPanelTester.Tests.Windows.Integration.Can.Hardware.PcanLifecycleTests

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
open Peak.Can.Basic.BackwardCompatibility
open Core.Interfaces
open Infrastructure.Protocol.Hardware
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Infrastructure.Can

/// Hardware-marked integration tests for `PcanCanLink` per
/// `specs/002-can-link-and-panel-discovery/tasks.md` T043. All
/// cases require a real PEAK PCAN-USB adapter on the host.
/// Tracked by [#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112).
///
/// Carry both `[<Trait("Category", "Hardware")>]` AND `Skip` because the
/// upstream standards v1.9.0 reusable workflow runs `dotnet test` without
/// a `--filter` argument, so the Trait alone does not exclude these from
/// default CI (the local pre-push gate applies the filter, the CI gate
/// does not — drift tracked for a standards-repo PR).
///
/// Run locally with:
///   dotnet test tests/ButtonPanelTester.Tests.Windows --filter "Category=Hardware"
/// — and remove the `Skip` arguments on the relevant `[<Fact>]`s for the
/// run, since `xunit` honours `Skip` ahead of the filter.
///
/// Coverage:
///   - `OpenAsync 250000` succeeds within 2 s and surfaces
///     `Connected` with a non-empty `AdapterIdentification`.
///   - `CloseAsync` followed by `OpenAsync` succeeds.
///   - Physical unplug surfaces `Disconnected(MidSessionUnplug, _)`
///     within 5 s — manual: the runner must unplug the adapter when
///     the test prompts.

// --- fixtures ---

let private baudrateBps = 250_000

let private connectTimeout = TimeSpan.FromSeconds(2.0)

let private unplugObservationTimeout = TimeSpan.FromSeconds(5.0)

/// Builds a `PcanCanLink` over a factory that constructs the
/// PEAK stack (`PCANManager` + `CanPort`) at 250 kbps on first
/// `OpenAsync`. Returns the link + a single `IDisposable` that
/// tears down the link, the port, and the driver in reverse
/// construction order — so the test fixture can `use` it and
/// guarantee the PEAK channel + background tasks shut down
/// between test cases. The captured driver / port may be `None`
/// if a test never invokes `OpenAsync`; the cleanup is a no-op
/// in that case.
let private buildLink () : PcanCanLink * IDisposable =
    let mutable createdDriver: PCANManager option = None
    let mutable createdPort: CanPort option = None

    let portFactory () : ICommunicationPort =
        let driver = new PCANManager(TPCANBaudrate.PCAN_BAUD_250K)
        let port = new CanPort(driver :> IPcanDriver)
        createdDriver <- Some driver
        createdPort <- Some port
        port :> ICommunicationPort

    let link = PcanCanLink(portFactory, NullLogger<PcanCanLink>.Instance)

    let cleanup =
        { new IDisposable with
            member _.Dispose() =
                (link :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()

                createdPort |> Option.iter (fun p -> p.Dispose())

                createdDriver
                |> Option.iter (fun d ->
                    (d :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()) }

    link, cleanup

let private collectStates (link: PcanCanLink) : List<CanLinkState> =
    let collected = List<CanLinkState>()

    let _ =
        (link :> ICanLink).LinkStateChanged
        |> Observable.subscribe (fun state -> lock collected (fun () -> collected.Add state))

    collected

let private waitForState
    (collected: List<CanLinkState>)
    (predicate: CanLinkState -> bool)
    (timeout: TimeSpan)
    : CanLinkState option =
    let deadline = DateTime.UtcNow + timeout

    let rec spin () =
        let observed =
            lock collected (fun () ->
                collected
                |> Seq.tryFind predicate)

        match observed with
        | Some _ -> observed
        | None when DateTime.UtcNow >= deadline -> None
        | None ->
            Thread.Sleep(50)
            spin ()

    spin ()

let private isConnected (state: CanLinkState) =
    match state with
    | Connected _ -> true
    | _ -> false

let private isMidSessionUnplug (state: CanLinkState) =
    match state with
    | Disconnected(MidSessionUnplug, _) -> true
    | _ -> false

// --- T043.1: open succeeds + Connected with non-empty identification ---

[<Trait("Category", "Hardware")>]
[<Fact(Skip = "Hardware required; runs locally only — tracked by #112. CI runs dotnet test without a Category filter at standards v1.9.0, so the [<Trait>] alone does not exclude this case.")>]
let OpenAsync_RealAdapter_SurfacesConnectedWithIdentification () =
    let link, cleanup = buildLink ()
    use _ = cleanup

    let observed = collectStates link

    (link :> ICanLink).OpenAsync(baudrateBps, CancellationToken.None)
        .GetAwaiter().GetResult()

    match waitForState observed isConnected connectTimeout with
    | Some(Connected(adapter, _)) ->
        Assert.False(String.IsNullOrWhiteSpace adapter.ChannelName)
        Assert.False(String.IsNullOrWhiteSpace adapter.DeviceId)
        Assert.Equal(baudrateBps, adapter.BaudrateBps)
    | Some other -> Assert.Fail(sprintf "expected Connected, observed %A" other)
    | None ->
        Assert.Fail(
            sprintf
                "no Connected within %.1f s — verify the PEAK adapter is plugged in and the driver is installed"
                connectTimeout.TotalSeconds
        )

// --- T043.2: CloseAsync followed by OpenAsync succeeds ---

[<Trait("Category", "Hardware")>]
[<Fact(Skip = "Hardware required; runs locally only — tracked by #112. CI runs dotnet test without a Category filter at standards v1.9.0, so the [<Trait>] alone does not exclude this case.")>]
let CloseAsyncThenOpenAsync_RealAdapter_SecondOpenReachesConnected () =
    let link, cleanup = buildLink ()
    use _ = cleanup

    let observed = collectStates link
    let canLink = link :> ICanLink

    canLink.OpenAsync(baudrateBps, CancellationToken.None).GetAwaiter().GetResult()
    Assert.NotNull(waitForState observed isConnected connectTimeout |> Option.toObj)

    canLink.CloseAsync(CancellationToken.None).GetAwaiter().GetResult()

    // Snapshot the count BEFORE the second open so we can verify a
    // FRESH Connected lands (rather than the one from the first open
    // still being in the list).
    let preReopenCount = lock observed (fun () -> observed.Count)

    canLink.OpenAsync(baudrateBps, CancellationToken.None).GetAwaiter().GetResult()

    let secondConnected =
        let deadline = DateTime.UtcNow + connectTimeout

        let rec spin () =
            let found =
                lock observed (fun () ->
                    observed
                    |> Seq.skip preReopenCount
                    |> Seq.tryFind isConnected)

            match found with
            | Some _ -> found
            | None when DateTime.UtcNow >= deadline -> None
            | None ->
                Thread.Sleep(50)
                spin ()

        spin ()

    match secondConnected with
    | Some(Connected _) -> ()
    | Some other -> Assert.Fail(sprintf "expected second Connected, observed %A" other)
    | None -> Assert.Fail("no second Connected within 2 s after CloseAsync + OpenAsync")

// --- T043.3: physical unplug surfaces Disconnected(MidSessionUnplug, _) ---

[<Trait("Category", "Hardware")>]
[<Fact(Skip = "Manual: requires the operator to unplug the adapter when prompted")>]
let PhysicalUnplug_AfterConnected_SurfacesMidSessionUnplug () =
    // Skip-by-default because there is no way to script a physical
    // USB unplug inside the test process. Remove the Skip argument
    // and run interactively when validating the lifecycle on a
    // bench rig:
    //   1. Plug the adapter, run the test, wait for the prompt.
    //   2. Within 5 s, physically unplug the adapter.
    //   3. Assert observes Disconnected(MidSessionUnplug, _).
    let link, cleanup = buildLink ()
    use _ = cleanup

    let observed = collectStates link

    (link :> ICanLink).OpenAsync(baudrateBps, CancellationToken.None)
        .GetAwaiter().GetResult()

    Assert.NotNull(waitForState observed isConnected connectTimeout |> Option.toObj)

    // Interactive prompt — the operator unplugs the adapter HERE.
    Console.WriteLine(
        sprintf "Unplug the PEAK adapter within %.1f s…" unplugObservationTimeout.TotalSeconds
    )

    match waitForState observed isMidSessionUnplug unplugObservationTimeout with
    | Some _ -> ()
    | None ->
        Assert.Fail(
            sprintf
                "no Disconnected(MidSessionUnplug, _) within %.1f s of the prompt"
                unplugObservationTimeout.TotalSeconds
        )
