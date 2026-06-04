module Stem.ButtonPanelTester.Tests.Windows.Integration.DictionaryRestartPersistenceTests

open System
open System.IO
open System.Collections.Generic
open System.Threading
open Avalonia.Controls
open Avalonia.Headless.XUnit
open Avalonia.FuncUI.VirtualDom
open Xunit
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Dictionary
open Stem.ButtonPanelTester.Infrastructure.Persistence
open Stem.ButtonPanelTester.GUI.Dictionary

/// #191 across-restart regression mapping US3 acceptance #2: a
/// successful refresh that returns byte-identical content must
/// advance the *persisted* "last confirmed live" timestamp, so a
/// later offline relaunch renders the status row with the most
/// recent successful-sync date — not the date the dictionary
/// content last changed.
///
/// These tests exercise the real `JsonFileDictionaryCache` (the
/// second skip-write layer named in the ticket) across two
/// `DictionaryService` instances over the same on-disk cache
/// directory, so they catch the bug at the production persistence
/// boundary that an `InMemoryDictionaryCache` cannot. Lives in
/// `Tests.Windows` because `JsonFileDictionaryCache` ships in
/// `ButtonPanelTester.Infrastructure` (net10.0-windows).

// --- local fakes (Tests.Windows does not reference the net10.0 Fakes project) ---

type private FixedClock(now: DateTimeOffset) =
    interface IClock with
        member _.UtcNow() = now

/// Scripted `IDictionaryProvider`: dequeues the next pre-built
/// result per `FetchAsync`, mirroring the net10.0 `InMemoryDictionaryProvider`.
type private ScriptedProvider(scripted: DictionaryFetchResult seq) =
    let queue = Queue<DictionaryFetchResult>(scripted)

    interface IDictionaryProvider with
        member _.FetchAsync(_: CancellationToken) = task { return queue.Dequeue() }

// --- fixtures ---

let private freshTempDir () =
    let dir =
        Path.Combine(
            Path.GetTempPath(),
            "ButtonPanelTester.RestartPersistenceTests-" + Guid.NewGuid().ToString("N")
        )

    Directory.CreateDirectory(dir) |> ignore
    dir

let private noSeed : SeedBytesReader = fun _ -> task { return None }

let private dictA : ButtonPanelDictionary = {
    ContentHash = String.replicate 64 "a"
    PanelTypes = [
        {
            Id = 1
            Name = "Stable panel"
            Description = None
            Variables = []
        }
    ]
}

// Noon UTC so the local-date projection lands on the same calendar
// day on a UTC CI agent and on Luca's CET/CEST machine.
let private contentChangeDate =
    DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero)

let private lastSyncDate =
    DateTimeOffset(2026, 5, 27, 12, 0, 0, TimeSpan.Zero)

let private mkService (cache: IDictionaryCache) (scripted: DictionaryFetchResult seq) : IDictionaryService =
    // The clock is unused on this path (the provider stamps the
    // success fetchedAt); any fixed instant suffices.
    DictionaryService(FixedClock(lastSyncDate), cache, ScriptedProvider(scripted)) :> IDictionaryService

[<Fact>]
let OfflineRelaunch_AfterIdenticalContentRefresh_PersistsAndReportsLastSyncDate () =
    task {
        let dir = freshTempDir ()

        try
            // Disk state from the session when the content last
            // changed: dictA stamped with the older contentChangeDate.
            let cache = JsonFileDictionaryCache(dir, noSeed) :> IDictionaryCache
            do! cache.WriteAsync(dictA, contentChangeDate, CancellationToken.None)

            // Launch 1: initialise from disk, then a successful refresh
            // returns byte-identical content with a newer fetchedAt.
            let launch1 = mkService cache [ Success(dictA, lastSyncDate) ]
            let! _ = launch1.InitializeAsync(CancellationToken.None)
            let! refreshed = launch1.RefreshAsync(CancellationToken.None)

            match refreshed with
            | Updated(_, Live t) -> Assert.Equal(lastSyncDate, t)
            | other -> Assert.Fail(sprintf "expected Updated(_, Live lastSyncDate), got %A" other)

            // The advanced timestamp is now on disk: a fresh read of the
            // same directory returns dictA @ lastSyncDate.
            let! readBack = cache.ReadAsync(CancellationToken.None)

            match readBack with
            | Success(dict, t) ->
                Assert.Equal(dictA, dict)
                Assert.Equal(lastSyncDate, t)
            | other -> Assert.Fail(sprintf "expected Success(dictA, lastSyncDate), got %A" other)

            // Launch 2 (restart): a brand-new service over the same
            // directory, with the service unreachable. Initialize reads
            // the persisted state; the row reports the last successful
            // sync, not the content-change date.
            let relaunchCache = JsonFileDictionaryCache(dir, noSeed) :> IDictionaryCache
            let launch2 = mkService relaunchCache [ Failed(NetworkUnreachable, None) ]
            let! initResult = launch2.InitializeAsync(CancellationToken.None)

            match initResult with
            | Updated(dict, Cached(t, FromLocalFile, None)) ->
                Assert.Equal(dictA, dict)
                Assert.Equal(lastSyncDate, t)
            | other -> Assert.Fail(sprintf "expected Updated(dictA, Cached(lastSyncDate, FromLocalFile, None)), got %A" other)

            // The offline refresh then re-labels with the failure chip
            // but keeps the persisted last-sync timestamp.
            let! offlineRefresh = launch2.RefreshAsync(CancellationToken.None)

            match offlineRefresh with
            | Updated(dict, Cached(t, FromLocalFile, Some NetworkUnreachable)) ->
                Assert.Equal(dictA, dict)
                Assert.Equal(lastSyncDate, t)
            | other -> Assert.Fail(sprintf "expected Updated(dictA, Cached(lastSyncDate, FromLocalFile, Some NetworkUnreachable)), got %A" other)
        finally
            Directory.Delete(dir, true)
    }

// --- headless render: the persisted timestamp reaches the status row headline ---

let private cacheFilePath =
    @"C:\Users\test\AppData\Local\Stem.ButtonPanelTester\dictionary.json"

let private noop () = ()

let private headlineText (source: DictionarySource) : string =
    let panel =
        VirtualDom.create (
            DictionaryStatusRow.view
                cacheFilePath
                source
                lastSyncDate
                DictionaryStatusRow.Idle
                noop
                noop)
        :?> StackPanel

    panel.Children
    |> Seq.choose (fun c ->
        match box c with
        | :? TextBlock as t when t.Name = "Headline" -> Some t
        | _ -> None)
    |> Seq.exactlyOne
    |> fun t -> match t.Text with | null -> "" | s -> s

[<AvaloniaFact>]
let StatusRow_OfflineAfterIdenticalRefresh_HeadlineShowsLastSyncDate () =
    // The post-restart source carries the advanced fetchedAt; the row
    // headline projects it to the local date. lastSyncDate is noon UTC,
    // so the calendar day is timezone-stable.
    let source = Cached(lastSyncDate, FromLocalFile, Some NetworkUnreachable)

    Assert.Equal("Cached · last synced 2026-05-27", headlineText source)
