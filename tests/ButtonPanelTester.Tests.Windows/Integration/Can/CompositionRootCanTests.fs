module Stem.ButtonPanelTester.Tests.Windows.Integration.Can.CompositionRootCanTests

open System
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Services.Can
open Stem.ButtonPanelTester.Infrastructure.Can
open Stem.ButtonPanelTester.GUI.Composition

/// Composition smoke: the real CompositionRoot CAN graph must RESOLVE without
/// hardware. Because `CanPortShare` builds the port lazily (only on the first
/// `OpenAsync`), resolving the adapters does NOT P/Invoke `pcanbasic.dll`, so this
/// runs in CI with no PEAK driver. Proves the wiring (registrations present, no
/// cycle, lazy build not forced) and that `ICanFrameStream` now resolves to the real
/// `PcanCanFrameStream`, not the placeholder. The real PEAK frame-flow is the bench /
/// Phase-E proof.
[<Fact>]
let Composition_ResolvesCanGraph_BindsRealPcanFrameStream () =
    let services = ServiceCollection()
    let config = ConfigurationBuilder().Build()
    CompositionRoot.configure services config |> ignore
    let sp = services.BuildServiceProvider()
    try
        let frameStream = sp.GetRequiredService<ICanFrameStream>()
        sp.GetRequiredService<ICanLink>() |> ignore
        sp.GetRequiredService<ICanLinkService>() |> ignore
        let observer = sp.GetRequiredService<IWhoIAmObserver>()
        let buttonStateObserver = sp.GetRequiredService<IButtonStateObserver>()
        let ackObserver = sp.GetRequiredService<ISetAddressAckObserver>()
        sp.GetRequiredService<IPanelDiscoveryService>() |> ignore
        let transmitter = sp.GetRequiredService<IMasterSequenceTransmitter>()
        Assert.IsType<PcanCanFrameStream>(frameStream) |> ignore
        Assert.IsType<WhoIAmReassemblyObserver>(observer) |> ignore
        Assert.IsType<ButtonStateReassemblyObserver>(buttonStateObserver) |> ignore
        Assert.IsType<SetAddressAckObserver>(ackObserver) |> ignore
        Assert.IsType<ProtocolMasterSequenceTransmitter>(transmitter) |> ignore
        Assert.IsType<BaptismService>(sp.GetRequiredService<IBaptismService>()) |> ignore
        Assert.IsType<ButtonPressTestService>(sp.GetRequiredService<IButtonPressTestService>()) |> ignore
    finally
        (sp :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()
