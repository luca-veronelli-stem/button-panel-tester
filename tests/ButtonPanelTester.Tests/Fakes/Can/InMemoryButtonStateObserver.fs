namespace Stem.ButtonPanelTester.Tests.Fakes.Can

open System
open System.Collections.Concurrent
open Stem.ButtonPanelTester.Core.Can

/// Test fake for `IButtonStateObserver`: `Emit` synchronously pushes one decoded
/// `ButtonStateFrame` to every current subscriber on the calling thread. Stands in for the
/// windows-only `ButtonStateReassemblyObserver` so the net10.0 service E2E (Phase E) can drive
/// the post-reassembly feed deterministically. No hardware, no reassembly.
type InMemoryButtonStateObserver() =
    let observers = ConcurrentBag<IObserver<ButtonStateFrame>>()
    member _.Emit(frame: ButtonStateFrame) =
        for observer in observers do observer.OnNext frame
    interface IButtonStateObserver with
        member _.ButtonStateObserved =
            { new IObservable<ButtonStateFrame> with
                member _.Subscribe(observer: IObserver<ButtonStateFrame>) =
                    observers.Add observer
                    { new IDisposable with member _.Dispose() = () } }
