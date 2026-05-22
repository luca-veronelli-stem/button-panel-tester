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


/// On-disk cache state for the in-memory test adapter. Models the
/// three observable shapes the production
/// `JsonFileDictionaryCache` adapter exposes through `ReadAsync`:
///   - `Empty`     — both target files absent (cold start).
///   - `Present`   — JSON + sidecar pair reads cleanly.
///   - `Corrupt`   — pair exists but the sidecar hash does not
///                   match the JSON (FR-019 case (c)).
type private CacheState =
    | Empty
    | Present of ButtonPanelDictionary * DateTimeOffset
    | Corrupt of detail: string option

/// In-memory test adapter for `IDictionaryCache`, per
/// `specs/001-fetch-dictionary/contracts/ports.md`
/// §IDictionaryCache lines 99-116. Models the JSON-plus-sidecar
/// pair as a `CacheState` cell plus an independent seed-payload
/// slot consulted by `ExtractSeedIfMissingAsync`.
///
/// Scripting hooks (each is independent of the others):
///   - `SeedWith`    — register the seed payload that
///                     `ExtractSeedIfMissingAsync` will write when
///                     the cache is `Empty` or `Corrupt`. Does NOT
///                     pre-populate the disk state.
///   - `PrePopulate` — set the disk state to `Present(...)` (simulates a
///                     prior-session cache that survives the cold start).
///   - `SetCorrupt`  — set the disk state to `Corrupt(detail)` so the
///                     next `ReadAsync` returns
///                     `Failed(CacheUnreadable, detail)` — fixture
///                     for FR-019 case (c).
///
/// `WriteAsync` overwrites the cell unconditionally; the hash-skip
/// optimisation in the production adapter is irrelevant for the
/// test fake. `ExtractSeedIfMissingAsync` mirrors the loosened
/// production contract (T029): it overwrites the disk state with
/// the registered seed whenever a `ReadAsync` against the current
/// state would fail (`Empty` or `Corrupt`), staying a no-op only
/// when the disk state is already `Present`.
///
/// Tests register this directly via the test-side wiring in
/// `tests/ButtonPanelTester.Tests/Fakes/Wiring.fs` rather than
/// going through the GUI composition root. The production-side
/// counterpart is
/// `Infrastructure.Persistence.JsonFileDictionaryCache` (T053),
/// which implements `cache-format.md`.
type InMemoryDictionaryCache() =
    let mutable state: CacheState = Empty
    let mutable seedPayload: (ButtonPanelDictionary * DateTimeOffset) option = None
    let mutable writeCount = 0

    /// Register the seed payload `ExtractSeedIfMissingAsync` will
    /// write through to the cache when the disk state is not
    /// `Present`. Independent of `PrePopulate` and `SetCorrupt`.
    member _.SeedWith(dict: ButtonPanelDictionary, fetchedAt: DateTimeOffset) =
        seedPayload <- Some(dict, fetchedAt)

    /// Pre-populate the disk state with a readable cache pair
    /// (simulates a successful prior-session refresh).
    member _.PrePopulate(dict: ButtonPanelDictionary, fetchedAt: DateTimeOffset) =
        state <- Present(dict, fetchedAt)

    /// Put the cache in the integrity-failure state so the next
    /// `ReadAsync` returns `Failed(CacheUnreadable, detail)`.
    member _.SetCorrupt(detail: string option) = state <- Corrupt detail

    /// Count of `WriteAsync` invocations observed since construction.
    /// `DictionaryServiceRefreshTests` (T055) uses this to assert the
    /// `cache-format.md` "Skip-write optimisation": an identical-
    /// content refresh emits `Live(now)` without an extra cache write
    /// (T050 in-memory hash check). Test-only surface — the
    /// production adapter has no equivalent counter.
    member _.WriteCount = writeCount

    interface IDictionaryCache with
        member _.ExistsAsync(_: CancellationToken) =
            task {
                return
                    match state with
                    | Empty -> false
                    | Present _
                    | Corrupt _ -> true
            }

        member _.ReadAsync(_: CancellationToken) =
            task {
                return
                    match state with
                    | Empty -> Failed(CacheAbsent, None)
                    | Corrupt detail -> Failed(CacheUnreadable, detail)
                    | Present(d, t) -> Success(d, t)
            }

        member _.WriteAsync(d: ButtonPanelDictionary, t: DateTimeOffset, _: CancellationToken) =
            task {
                writeCount <- writeCount + 1
                state <- Present(d, t)
            }

        member _.ExtractSeedIfMissingAsync(_: CancellationToken) =
            task {
                match state with
                | Present _ -> ()
                | Empty
                | Corrupt _ ->
                    match seedPayload with
                    | Some(d, t) -> state <- Present(d, t)
                    | None -> ()
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


/// In-memory test adapter for `IInstallationDescriptorProvider`, per
/// `specs/001-fetch-dictionary/contracts/ports.md`
/// §IInstallationDescriptorProvider. Holds a mutable
/// `InstallationDescriptor` cell standing in for the production
/// adapter's hash cache + `install.guid` sidecar; `ResetInstallGuid`
/// rotates the descriptor's `InstallGuid` to a fresh `Guid` so the
/// orchestration's "next Current() yields a new InstallGuid"
/// contract can be observed without touching the filesystem.
///
/// `ResetCalls` exposes the invocation count so tests asserting on
/// the Re-Register wipe order (credential first, then descriptor)
/// can distinguish "reset never happened" from "reset happened but
/// descriptor unchanged" (the latter is the production behaviour
/// when the sidecar was already missing).
///
/// Tests register this directly via the test-side wiring rather than
/// going through the GUI composition root. The production-side
/// counterpart is
/// `Infrastructure.Auth.InstallationDescriptorProvider`.
type InMemoryInstallationDescriptorProvider(initial: InstallationDescriptor) =
    let mutable descriptor = initial
    let mutable resetCalls = 0

    /// Replace the cell verbatim. Test helper for arranging a
    /// pre-rotation state.
    member _.Set(d: InstallationDescriptor) = descriptor <- d

    /// Count of `ResetInstallGuid` invocations observed since
    /// construction. Test-only surface.
    member _.ResetCalls = resetCalls

    interface IInstallationDescriptorProvider with
        member _.Current() = descriptor

        member _.ResetInstallGuid() =
            resetCalls <- resetCalls + 1
            descriptor <- { descriptor with InstallGuid = Guid.NewGuid() }
