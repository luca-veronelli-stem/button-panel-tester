namespace Stem.ButtonPanelTester.Tests.Fakes.Can

open System
open System.Collections.Concurrent
open Stem.ButtonPanelTester.Core.Can

/// Test fake for `ISetAddressAckObserver`: `Emit` synchronously pushes one ACK observation
/// (the carried receive timestamp) to every current subscriber on the calling thread. Stands in
/// for the real (windows-only) `SetAddressAckObserver` so the net10.0 service E2E can script the
/// `0x25` ACK fast-positive deterministically (mirrors `InMemoryWhoIAmObserver.Emit`). No
/// hardware, no reassembly. Consumed by the later behavioral slice (RW04) — additive here.
type InMemorySetAddressAckObserver() =
    let observers = ConcurrentBag<IObserver<DateTimeOffset>>()
    member _.Emit(observedAt: DateTimeOffset) =
        for observer in observers do observer.OnNext observedAt
    interface ISetAddressAckObserver with
        member _.SetAddressAckObserved =
            { new IObservable<DateTimeOffset> with
                member _.Subscribe(observer: IObserver<DateTimeOffset>) =
                    observers.Add observer
                    { new IDisposable with member _.Dispose() = () } }
