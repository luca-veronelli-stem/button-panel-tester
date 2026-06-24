namespace Stem.ButtonPanelTester.Tests.Fakes.Can

open System
open System.Collections.Concurrent
open Stem.ButtonPanelTester.Core.Can

/// Test fake for `IButtonStateObserver`: pushes one decoded `ButtonStateObservation`
/// (frame + variant-from-CAN-ID) synchronously to every current subscriber on the calling thread.
/// Stands in for the windows-only `ButtonStateReassemblyObserver` so the net10.0 service E2E
/// (Phase E / I3) can drive the post-reassembly feed deterministically. No hardware, no reassembly.
type InMemoryButtonStateObserver() =
    let observers = ConcurrentBag<IObserver<ButtonStateObservation>>()

    /// Emit a full observation — the variant rides with the frame, as it does off a real directed
    /// CAN ID. Use this when the test asserts on the observed variant (the fix-#270 path).
    member _.EmitObservation(observation: ButtonStateObservation) =
        for observer in observers do observer.OnNext observation

    /// Convenience: emit a bare `ButtonStateFrame` as an `OptimusXp` observation. The press-edge
    /// path the service scores on reads only `obs.Frame.Bitmap`, so the variant is immaterial to the
    /// scoring suites that drive frames; tests that assert on the variant use `EmitObservation`.
    member this.Emit(frame: ButtonStateFrame) =
        this.EmitObservation { Frame = frame; Variant = OptimusXp }

    interface IButtonStateObserver with
        member _.ButtonStateObserved =
            { new IObservable<ButtonStateObservation> with
                member _.Subscribe(observer: IObserver<ButtonStateObservation>) =
                    observers.Add observer
                    { new IDisposable with member _.Dispose() = () } }
