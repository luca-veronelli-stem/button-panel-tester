namespace Stem.ButtonPanelTester.Tests.Fakes

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Stem.ButtonPanelTester.Core.Dictionary

/// Deterministic test adapter for `IClock`, per
/// `specs/001-fetch-dictionary/contracts/ports.md` §IClock lines
/// 38-46. Initialised at construction with a fixed instant; the
/// `Advance` and `SetTo` scripting hooks let tests step time
/// forward (relative) or jump to an absolute instant without
/// reaching the real wall clock. Every `UtcNow()` call returns
/// the most recently scripted value.
///
/// Tests register this directly via the test-side wiring in
/// `tests/ButtonPanelTester.Tests/Fakes/Wiring.fs` rather than
/// going through the GUI composition root. The production-side
/// counterpart is `Infrastructure.Clock.SystemClock` (T018),
/// which wraps `DateTimeOffset.UtcNow`.
type FrozenClock(initial: DateTimeOffset) =
    let mutable now = initial

    interface IClock with
        member _.UtcNow() = now

    member _.Advance(span: TimeSpan) = now <- now + span
    member _.SetTo(t: DateTimeOffset) = now <- t


/// In-memory test adapter for `IDictionaryProvider`, per
/// `specs/001-fetch-dictionary/contracts/ports.md`
/// §IDictionaryProvider lines 66-74. Backed by a FIFO queue
/// populated from the `scripted` sequence at construction. Each
/// call to `FetchAsync` dequeues the next pre-built
/// `DictionaryFetchResult`; calling it more times than there are
/// scripted results raises `InvalidOperationException` from
/// `Queue<_>.Dequeue` — that is a test-setup bug (not a SUT
/// bug), per ports.md line 72.
///
/// Tests register this directly via the test-side wiring in
/// `tests/ButtonPanelTester.Tests/Fakes/Wiring.fs` rather than
/// going through the GUI composition root. The production-side
/// counterpart is
/// `Infrastructure.Http.HttpDictionaryProvider` (T049), which
/// drives the wire contract in `dictionary-api.md`.
type InMemoryDictionaryProvider(scripted: DictionaryFetchResult seq) =
    let queue = Queue<DictionaryFetchResult>(scripted)

    interface IDictionaryProvider with
        member _.FetchAsync(_: CancellationToken) = task { return queue.Dequeue() }


/// In-memory test adapter for `IDictionaryCache`, per
/// `specs/001-fetch-dictionary/contracts/ports.md`
/// §IDictionaryCache lines 99-116. State is a single mutable
/// `(ButtonPanelDictionary * DateTimeOffset) option` cell that
/// stands in for the JSON-plus-sidecar pair on disk. The
/// `SeedWith` scripting hook lets tests preload the cache with a
/// dictionary + fetched-at timestamp (equivalent to having an
/// extracted embedded-seed file on disk at startup).
///
/// `ReadAsync` returns `Failed(CacheAbsent, None)` when the
/// cell is empty — the T013 `CacheAbsent` failure-reason case
/// with no human-readable detail. `WriteAsync` overwrites the
/// cell unconditionally; the hash-skip optimisation in the
/// production adapter is irrelevant for the test fake.
/// `ExtractSeedIfMissingAsync` is intentionally a no-op: the
/// scripting hook is `SeedWith`, so the test author preloads
/// the state explicitly when an embedded seed is desired.
///
/// Tests register this directly via the test-side wiring in
/// `tests/ButtonPanelTester.Tests/Fakes/Wiring.fs` rather than
/// going through the GUI composition root. The production-side
/// counterpart is
/// `Infrastructure.Persistence.JsonFileDictionaryCache` (T053),
/// which implements `cache-format.md`.
type InMemoryDictionaryCache() =
    let mutable state: (ButtonPanelDictionary * DateTimeOffset) option = None
    let mutable hasSeed = false

    member _.SeedWith(dict: ButtonPanelDictionary, fetchedAt: DateTimeOffset) =
        hasSeed <- true
        state <- Some(dict, fetchedAt)

    interface IDictionaryCache with
        member _.ExistsAsync(_: CancellationToken) = task { return state.IsSome }

        member _.ReadAsync(_: CancellationToken) =
            task {
                match state with
                | Some(d, t) -> return Success(d, t)
                | None -> return Failed(CacheAbsent, None)
            }

        member _.WriteAsync(d: ButtonPanelDictionary, t: DateTimeOffset, _: CancellationToken) =
            task { state <- Some(d, t) }

        member _.ExtractSeedIfMissingAsync(_: CancellationToken) =
            task {
                if state.IsNone && hasSeed then
                    ()
            }


/// In-memory test adapter for `ICredentialStore`, per
/// `specs/001-fetch-dictionary/contracts/ports.md`
/// §ICredentialStore lines 140-149. Single mutable
/// `InstallationCredential option` cell standing in for the
/// DPAPI-encrypted credential file on disk. No dedicated
/// scripting hook is needed: tests drive the adapter through its
/// own interface (`SaveAsync` to preload, `DeleteAsync` to
/// clear).
///
/// `LoadAsync` returns the current value; production-side
/// decryption failures are out of scope for the fake (the
/// production adapter swallows them and returns `None`, per
/// `credential-format.md`).
///
/// Tests register this directly via the test-side wiring in
/// `tests/ButtonPanelTester.Tests/Fakes/Wiring.fs` rather than
/// going through the GUI composition root. The production-side
/// counterpart is
/// `Infrastructure.Persistence.DpapiCredentialStore` (T056),
/// which implements `credential-format.md` with DPAPI per-user
/// encryption on Windows.
type InMemoryCredentialStore() =
    let mutable value: InstallationCredential option = None

    interface ICredentialStore with
        member _.ExistsAsync(_: CancellationToken) = task { return value.IsSome }
        member _.LoadAsync(_: CancellationToken) = task { return value }

        member _.SaveAsync(c: InstallationCredential, _: CancellationToken) =
            task { value <- Some c }

        member _.DeleteAsync(_: CancellationToken) = task { value <- None }


/// In-memory test adapter for `IRegistrationClient`, per
/// `specs/001-fetch-dictionary/contracts/ports.md`
/// §IRegistrationClient lines 171-183. Backed by a
/// `Map<string, Result<InstallationCredential, RegistrationError>>`
/// keyed by the raw `BootstrapToken.Value`. The `SetResult`
/// scripting hook lets tests register one entry per token at
/// setup time; `RegisterAsync` performs a single map lookup and
/// returns the scripted result. Unscripted tokens default to
/// `Error TokenInvalid` so the empty state of a test bootstrap
/// rejects every bootstrap exchange — explicit setup is required
/// for happy-path tests.
///
/// Tests register this directly via the test-side wiring in
/// `tests/ButtonPanelTester.Tests/Fakes/Wiring.fs` rather than
/// going through the GUI composition root. The production-side
/// counterpart is `Infrastructure.Http.HttpRegistrationClient`
/// (T050), which implements `registration-api.md`.
type InMemoryRegistrationClient() =
    let mutable script: Map<string, Result<InstallationCredential, RegistrationError>> =
        Map.empty

    member _.SetResult(token: string, result: Result<InstallationCredential, RegistrationError>) =
        script <- script.Add(token, result)

    interface IRegistrationClient with
        member _.RegisterAsync(token: BootstrapToken, _: CancellationToken) =
            task {
                match Map.tryFind token.Value script with
                | Some r -> return r
                | None -> return Error TokenInvalid
            }
