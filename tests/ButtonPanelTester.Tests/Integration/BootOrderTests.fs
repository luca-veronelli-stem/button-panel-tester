module Stem.ButtonPanelTester.Tests.Integration.BootOrderTests

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services
open Stem.ButtonPanelTester.Services.Can
open Stem.ButtonPanelTester.Services.Dictionary

/// Integration tests for `BootSequence.runBootSequence` per
/// `specs/002-can-link-and-panel-discovery/tasks.md` T040b. Covers
/// FR-001: the CAN adapter open MUST follow `IDictionaryService`
/// boot completion, never before. Wired through an in-process spy
/// `ICanLinkService` and a `TaskCompletionSource` standing in for
/// the in-flight dictionary init task — no Avalonia surface, no
/// real adapter.

/// Spy `ICanLinkService` whose `InitializeAsync` appends to a
/// shared `ConcurrentQueue<string>` so the test can reason about
/// call ordering relative to the dictionary task. `failWith`
/// optionally arranges `InitializeAsync` to throw, modelling the
/// "open failed — log and swallow so LinkStateChanged surfaces it"
/// path in `BootSequence`.
type private SpyCanLinkService
    (
        events: ConcurrentQueue<string>,
        ?failWith: exn,
        ?openedSignal: TaskCompletionSource<unit>
    ) =
    let linkSubject = Event<CanLinkState>()
    let panelsSubject = Event<PanelsOnBus>()

    member val InitializeCalls = 0 with get, set

    interface ICanLinkService with
        member _.CurrentState = Initializing
        member _.PanelsOnBus = PanelsOnBus.empty
        member _.LinkStateChanged = linkSubject.Publish
        member _.PanelsOnBusChanged = panelsSubject.Publish

        member this.InitializeAsync(_ct: CancellationToken) : Task =
            this.InitializeCalls <- this.InitializeCalls + 1
            events.Enqueue "can:initialize"

            openedSignal
            |> Option.iter (fun s -> s.TrySetResult() |> ignore)

            match failWith with
            | Some ex -> Task.FromException(ex)
            | None -> Task.CompletedTask

        member _.ReconnectAsync(_ct: CancellationToken) : Task =
            Task.CompletedTask

let private dummyUpdate : DictionaryStateUpdate =
    NoDictionaryAvailable NetworkUnreachable

// --- FR-001 ordering invariant ---

[<Fact>]
let runBootSequence_CanInitializeNotCalledUntilDictionaryTaskCompletes () =
    let events = ConcurrentQueue<string>()
    let dictTcs = TaskCompletionSource<DictionaryStateUpdate>()
    let canOpened = TaskCompletionSource<unit>()

    let spyCan = SpyCanLinkService(events, openedSignal = canOpened)

    let bootTask =
        BootSequence.runBootSequence
            dictTcs.Task
            (spyCan :> ICanLinkService)
            NullLogger.Instance
            CancellationToken.None

    // Boot sequence is awaiting the dictionary task; CAN must not
    // have been touched yet. A small wait guards against a spurious
    // pass if the implementation accidentally scheduled CAN.Init
    // on a continuation that hadn't run yet.
    Assert.False(bootTask.IsCompleted)
    Assert.False(canOpened.Task.Wait(TimeSpan.FromMilliseconds(50.0)))
    Assert.Equal(0, spyCan.InitializeCalls)

    // Releasing the dictionary task must let the boot sequence
    // proceed to CAN.Init.
    events.Enqueue "dict:complete"
    dictTcs.SetResult dummyUpdate

    Assert.True(bootTask.Wait(TimeSpan.FromSeconds(2.0)))
    Assert.Equal(1, spyCan.InitializeCalls)

    let observed = events.ToArray() |> List.ofArray
    Assert.Equal<string list>([ "dict:complete"; "can:initialize" ], observed)

[<Fact>]
let runBootSequence_CanInitializeFailureSurfacesViaLinkStateNotException () =
    let events = ConcurrentQueue<string>()
    let dictTcs = TaskCompletionSource<DictionaryStateUpdate>()

    let spyCan =
        SpyCanLinkService(events, failWith = InvalidOperationException "PEAK driver gone")

    let bootTask =
        BootSequence.runBootSequence
            dictTcs.Task
            (spyCan :> ICanLinkService)
            NullLogger.Instance
            CancellationToken.None

    dictTcs.SetResult dummyUpdate

    // Boot sequence must complete normally even when CAN.Init
    // throws: the failure path in production is observed via
    // `LinkStateChanged`, not via a propagated exception (which
    // would tear down the `Opened` handler).
    Assert.True(bootTask.Wait(TimeSpan.FromSeconds(2.0)))
    Assert.True(bootTask.IsCompletedSuccessfully)
    Assert.Equal(1, spyCan.InitializeCalls)

[<Fact>]
let runBootSequence_CancellationDuringCanOpenIsSwallowed () =
    let events = ConcurrentQueue<string>()
    let dictTcs = TaskCompletionSource<DictionaryStateUpdate>()

    let spyCan =
        SpyCanLinkService(events, failWith = OperationCanceledException())

    let bootTask =
        BootSequence.runBootSequence
            dictTcs.Task
            (spyCan :> ICanLinkService)
            NullLogger.Instance
            CancellationToken.None

    dictTcs.SetResult dummyUpdate

    Assert.True(bootTask.Wait(TimeSpan.FromSeconds(2.0)))
    Assert.True(bootTask.IsCompletedSuccessfully)
