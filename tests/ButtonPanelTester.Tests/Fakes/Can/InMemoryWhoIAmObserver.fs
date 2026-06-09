namespace Stem.ButtonPanelTester.Tests.Fakes.Can

open System
open System.Collections.Concurrent
open Stem.ButtonPanelTester.Core.Can

/// Test fake for `IWhoIAmObserver`: `Emit` synchronously pushes one decoded `WhoIAmFrame`
/// to every current subscriber on the calling thread. Stands in for the real (windows-only)
/// WhoIAmReassemblyObserver so the net10.0 service E2E can drive the post-reassembly feed
/// deterministically (mirrors InMemoryCanFrameStream.Emit). No hardware, no reassembly.
type InMemoryWhoIAmObserver() =
    let observers = ConcurrentBag<IObserver<WhoIAmFrame>>()
    member _.Emit(frame: WhoIAmFrame) =
        for observer in observers do observer.OnNext frame
    interface IWhoIAmObserver with
        member _.WhoIAmObserved =
            { new IObservable<WhoIAmFrame> with
                member _.Subscribe(observer: IObserver<WhoIAmFrame>) =
                    observers.Add observer
                    { new IDisposable with member _.Dispose() = () } }
