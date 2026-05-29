module Stem.ButtonPanelTester.Tests.Integration.Can.DictionaryIndependenceTests

open System
open System.Collections.Generic
open System.Threading
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Can
open Stem.ButtonPanelTester.Services.Dictionary
open Stem.ButtonPanelTester.Tests.Fakes
open Stem.ButtonPanelTester.Tests.Fakes.Can

/// FR-016 / SC-006 regression per `specs/002-can-link-lifecycle/tasks.md`
/// T059. The CAN link's mid-session failure path
/// (`Connected → Disconnected(MidSessionUnplug)`) MUST NOT perturb the
/// dictionary status row: `DictionaryService` and `CanLinkService` are
/// independent, so `IDictionaryService.SourceChanged` fires ZERO times
/// across the CAN-side transitions.
///
/// Wires a real `DictionaryService` (over the in-memory cache / provider
/// / clock fakes from `Fakes/Wiring.fs`) alongside a real
/// `CanLinkService` (over the scripted `InMemoryCanLink` from
/// `Fakes/Can/`). The dictionary is initialised first — that one
/// legitimate emission proves the `SourceChanged` subscription is live
/// (teeth for the zero-emission assertion) — then the count is
/// snapshotted and the CAN sequence is driven; the post-sequence count
/// must equal the snapshot.

// --- fixtures ---

let private fixedNow =
    DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero)

let private fixedAdapter : AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"
      DeviceId = "0x01"
      BaudrateBps = 250_000 }

let private seedDictionary : ButtonPanelDictionary =
    { ContentHash = String.replicate 64 "a"
      PanelTypes =
        [ { Id = 1
            Name = "Dictionary-independence fixture"
            Description = None
            Variables = [] } ] }

let private seedFetchedAt =
    DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero)

// --- FR-016 / SC-006 ---

[<Fact>]
let CanMidSessionUnplug_DoesNotFireDictionarySourceChanged () =
    task {
        // Dictionary service — entirely independent of CAN.
        let clock = FrozenClock(fixedNow)
        let cache = InMemoryDictionaryCache()
        cache.SeedWith(seedDictionary, seedFetchedAt)
        let provider = InMemoryDictionaryProvider(Seq.empty)

        let dictionary =
            DictionaryService(clock, cache, provider) :> IDictionaryService

        let sourceChanges = List<DictionarySource>()
        dictionary.SourceChanged.Add(sourceChanges.Add)

        // Initialise the dictionary so its one legitimate transition
        // proves the SourceChanged subscription is live — the teeth for
        // the zero-emission assertion at the end.
        let! _ = dictionary.InitializeAsync(CancellationToken.None)
        let baselineDictChanges = sourceChanges.Count
        Assert.Equal(1, baselineDictChanges)

        // CAN link service: scripted Connected → Disconnected(MidSessionUnplug).
        let openedAt = fixedNow
        let unpluggedAt = fixedNow.AddSeconds(5.0)

        let script =
            seq {
                (Connected(fixedAdapter, openedAt), TimeSpan.Zero)
                (Disconnected(MidSessionUnplug, unpluggedAt), TimeSpan.Zero)
            }

        let link = InMemoryCanLink(script)

        let canService =
            CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)
            :> ICanLinkService

        let canStates = List<CanLinkState>()

        let _canSub =
            canService.LinkStateChanged
            |> Observable.subscribe canStates.Add

        // Drive the CAN link across the mid-session unplug. Each
        // lifecycle call advances the `InMemoryCanLink` script by one
        // step (InitializeAsync → Connected; ReconnectAsync → the
        // service synthesises ReconnectPending, then dequeues
        // MidSessionUnplug).
        canService.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()
        canService.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult()

        // Sanity: the CAN side genuinely walked Connected →
        // Disconnected(MidSessionUnplug); otherwise the zero-emission
        // assertion below would be vacuous.
        let sawConnected =
            canStates
            |> Seq.exists (fun s ->
                match s with
                | Connected _ -> true
                | _ -> false)

        let sawMidSessionUnplug =
            canStates
            |> Seq.exists (fun s ->
                match s with
                | Disconnected(MidSessionUnplug, _) -> true
                | _ -> false)

        Assert.True(sawConnected, "CAN link should have emitted Connected")

        Assert.True(
            sawMidSessionUnplug,
            "CAN link should have emitted Disconnected(MidSessionUnplug, _)"
        )

        // FR-016 / SC-006: the dictionary emitted ZERO SourceChanged
        // events across the CAN transitions — the count is unchanged
        // from the baseline taken after the dictionary's own init.
        Assert.Equal(baselineDictChanges, sourceChanges.Count)
    }
