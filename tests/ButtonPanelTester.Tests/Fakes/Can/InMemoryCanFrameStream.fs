namespace Stem.ButtonPanelTester.Tests.Fakes.Can

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Stem.ButtonPanelTester.Core.Can

/// Test adapter for `ICanFrameStream` per
/// `specs/003-panel-discovery/contracts/can-frame-stream-port.md`
/// §Adapter contract (virtual). Driven by a scripted sequence of
/// `(RawCanFrame, TimeSpan)` events: `Start` walks the script on a
/// thread-pool worker, emitting each frame through
/// `RawFramesReceived` after the step's `TimeSpan`.
///
/// Used by the integration test surface (T051–T053, T058) to drive
/// `CanLinkService`'s observation pipeline through deterministic
/// frame sequences without touching real PEAK hardware. Concurrent
/// emission on a worker thread is intentional — it exercises the
/// receive-thread → service-thread hop in `CanLinkService`'s
/// observation pipeline (T045–T047).
type InMemoryCanFrameStream(script: seq<RawCanFrame * TimeSpan>) =
    let steps = List<RawCanFrame * TimeSpan>(script)
    let observers = ConcurrentBag<IObserver<RawCanFrame>>()
    let mutable startedCount = 0

    /// Start walking the scripted sequence on a thread-pool worker.
    /// Each step waits the supplied `TimeSpan` before emitting the
    /// frame. Honours `cancellationToken` — cancellation raises
    /// `OperationCanceledException` at the next `Task.Delay`.
    /// Calling `Start` more than once is a test-setup bug and
    /// raises `InvalidOperationException`.
    member _.Start(cancellationToken: CancellationToken) : Task =
        if Interlocked.Increment(&startedCount) <> 1 then
            raise (InvalidOperationException "InMemoryCanFrameStream.Start may only be called once.")

        task {
            for frame, delay in steps do
                if delay > TimeSpan.Zero then
                    do! Task.Delay(delay, cancellationToken)

                for observer in observers do
                    observer.OnNext frame
        }

    interface ICanFrameStream with
        member _.RawFramesReceived =
            { new IObservable<RawCanFrame> with
                member _.Subscribe(observer: IObserver<RawCanFrame>) =
                    observers.Add observer

                    { new IDisposable with
                        member _.Dispose() = () } }
