# Data Model: Dictionary Fetch and Status Display

**Phase 1 output for**: [plan.md](./plan.md)

This document specifies the F# types, port shapes, and state transitions for feat/001. Types live in `src/ButtonPanelTester.Core/Dictionary/`. Adapters and orchestration that consume these types live in `Services` and `Infrastructure`.

---

## 1. Domain types

### 1.1 `ButtonPanelDictionary` and constituents

```fsharp
namespace Stem.ButtonPanelTester.Core.Dictionary

open System

/// One variable in a panel type. Mirrors stem-dictionaries-manager's
/// ResolvedVariableDto exactly so the wire payload deserialises directly.
type Variable = {
    Name        : string
    AddressHigh : byte
    AddressLow  : byte
    DataType    : string
    Access      : string
    Description : string option
    Min         : float option
    Max         : float option
    Unit        : string option
    IsStandard  : bool
}

/// One panel type (e.g. "Pulsantiera 12 tasti rev A"). Mirrors the API's
/// shape; the dictionary itself is one PanelType in this slice's payload,
/// but the type is a list to leave room for a future API change.
type PanelType = {
    Id          : int
    Name        : string
    Description : string option
    Variables   : Variable list
}

/// The loaded dictionary. ContentHash is computed at receipt by hashing
/// the canonicalised JSON; it is what the cache sidecar carries and what
/// CacheConsistency reasons over.
type ButtonPanelDictionary = {
    ContentHash : string             // 64-char lowercase hex SHA-256
    PanelTypes  : PanelType list
}
```

**Notes**:
- The on-the-wire DTO from `stem-dictionaries-manager` has no `ContentHash`; it is computed client-side at receipt and is not transmitted.
- The legacy F# type carried `SchemaVersion: int`; we deliberately drop it (the API does not provide it; see /speckit.clarify Q3 in `spec.md` and R6 in `research.md`).

---

### 1.2 `DictionarySource` — what the user sees

```fsharp
/// Provenance of the dictionary currently loaded in memory. The status
/// row is a pure function of this value plus the most-recent-failure
/// detail (carried in Cached.LastFailureReason).
type CacheOrigin =
    | FromEmbeddedSeed
    | FromLocalFile

type DictionarySource =
    | Live   of FetchedAt : DateTimeOffset
    | Cached of FetchedAt        : DateTimeOffset
              * Origin           : CacheOrigin
              * LastFailureReason: FetchFailureReason option
```

**Invariants** (proved in `lean/Stem/ButtonPanelTester/Phase1/DictionarySource.lean`):
1. `FetchedAt` is the timestamp of the most recent successful live fetch (or, when `Origin = FromEmbeddedSeed`, the seed's build time).
2. A `Live → Cached` transition (refresh failed mid-session) preserves the in-memory `ButtonPanelDictionary` byte-for-byte; only the wrapper changes.
3. `LastFailureReason` is `Some r` exactly when the most recent refresh attempt failed with `r`; it is `None` when the most recent attempt succeeded or no attempt has been made.

---

### 1.3 `DictionaryFetchResult` and `FetchFailureReason`

```fsharp
type FetchFailureReason =
    | NetworkUnreachable    // TCP / DNS / SSL handshake failure
    | Timeout               // 10 s elapsed (R1)
    | Unauthorized          // 401 from the server
    | NotFound              // 404 from the server (configured Dictionary:Id is wrong)
    | MalformedPayload      // body did not deserialise to DictionaryResolvedDto
    | ServerError           // 5xx, or any other unexpected outcome

type DictionaryFetchResult =
    | Success of ButtonPanelDictionary * FetchedAt: DateTimeOffset
    | Failed  of Reason: FetchFailureReason * Detail: string option
```

The `Detail` is human-readable elaboration for the status row's detail affordance; runtime branching keys off `Reason` only. Closed enum: adding a variant requires a Lean theorem update (`failure_reason_exhaustion`).

---

### 1.4 Registration types

```fsharp
type BootstrapToken =
    private BootstrapToken of string
    static member TryCreate (raw: string) : Result<BootstrapToken, string> =
        let trimmed = if isNull raw then "" else raw.Trim()
        if String.IsNullOrEmpty trimmed then Error "token is empty"
        else Ok (BootstrapToken trimmed)
    member this.Value = let (BootstrapToken s) = this in s

type InstallationCredential =
    private InstallationCredential of string  // opaque server-issued secret
    static member Create (raw: string) =
        InstallationCredential raw
    member this.Value = let (InstallationCredential s) = this in s

type RegistrationError =
    | TokenInvalid          // server rejected the BootstrapToken (4xx)
    | RegistrationServerError of httpStatus: int
    | RegistrationNetwork   of FetchFailureReason   // shares the network failure taxonomy
```

**Notes**:
- `BootstrapToken` and `InstallationCredential` are single-case DUs with private constructors so callers cannot fabricate them from arbitrary strings. The dialog goes through `BootstrapToken.TryCreate`; the registration adapter goes through `InstallationCredential.Create`.
- Neither type derives `IComparable` / `IEquatable` of the underlying string in any way that exposes the value to logging; the F# default `ToString` shows `BootstrapToken "<value>"`. Per the LOGGING standard, do not log instances.

---

## 2. Port interfaces (`Core.Dictionary.Ports`)

```fsharp
namespace Stem.ButtonPanelTester.Core.Dictionary

open System
open System.Threading
open System.Threading.Tasks

type IClock =
    abstract member UtcNow : unit -> DateTimeOffset

type IDictionaryProvider =
    abstract member FetchAsync :
        ct: CancellationToken -> Task<DictionaryFetchResult>

type IDictionaryCache =
    abstract member ExistsAsync : ct: CancellationToken -> Task<bool>

    abstract member ReadAsync   :
        ct: CancellationToken
        -> Task<DictionaryFetchResult>   // Failed CacheUnreadable on integrity failure

    abstract member WriteAsync  :
        dictionary: ButtonPanelDictionary
        * fetchedAt: DateTimeOffset
        * ct: CancellationToken
        -> Task

    abstract member ExtractSeedIfMissingAsync :
        ct: CancellationToken -> Task    // no-op when ExistsAsync returns true

type ICredentialStore =
    abstract member ExistsAsync : ct: CancellationToken -> Task<bool>
    abstract member LoadAsync   : ct: CancellationToken -> Task<InstallationCredential option>
    abstract member SaveAsync   : credential: InstallationCredential
                                  * ct: CancellationToken
                                  -> Task
    abstract member DeleteAsync : ct: CancellationToken -> Task

type IRegistrationClient =
    abstract member RegisterAsync :
        token: BootstrapToken
        * ct: CancellationToken
        -> Task<Result<InstallationCredential, RegistrationError>>
```

**Invariants** (proved in `lean/Stem/ButtonPanelTester/Phase1/DictionaryProvider.lean`):
- `IDictionaryProvider.FetchAsync` returns either `Success` or `Failed`, never both, never neither (the F# DU enforces this; the Lean theorem `provider_success_xor_failed` mechanises the same fact for downstream reasoning).

---

## 3. Service surface (`Services.Dictionary`)

```fsharp
namespace Stem.ButtonPanelTester.Services.Dictionary

open System
open System.Threading
open System.Threading.Tasks
open Stem.ButtonPanelTester.Core.Dictionary

/// Notification carried on every state transition. The GUI subscribes
/// and re-renders the status row.
type DictionaryStateUpdate =
    | Updated              of dictionary: ButtonPanelDictionary
                            * source: DictionarySource
    | NoDictionaryAvailable of reason: FetchFailureReason

/// Single-instance, registered singleton in CompositionRoot.
type IDictionaryService =
    /// Snapshot of the active dictionary + source. ValueNone until the
    /// first successful read (live or cache or seed) lands.
    abstract member Snapshot : (ButtonPanelDictionary * DictionarySource) voption

    /// Fired on every transition that changes the rendered status row.
    /// Suppressed when in-memory dictionary is unchanged AND source
    /// label is unchanged (deduplication).
    [<CLIEvent>]
    abstract member SourceChanged : IEvent<DictionarySource>

    /// Equivalent to RefreshAsync but the name reads better at startup.
    abstract member InitializeAsync :
        ct: CancellationToken -> Task<DictionaryStateUpdate>

    /// Manual refresh (FR-006). Concurrent callers receive the same
    /// in-flight task (FR-007).
    abstract member RefreshAsync :
        ct: CancellationToken -> Task<DictionaryStateUpdate>
```

The orchestration logic (cache-and-memory-in-lockstep + coalescing + Live↔Cached re-labelling) is a single `DictionaryService` class in `Services/Dictionary/DictionaryService.fs` consuming the four ports.

---

## 4. State machine

```mermaid
stateDiagram-v2
    [*] --> NoDictionary: process start

    NoDictionary --> Cached_Seed: ExtractSeedIfMissing<br/>(first launch)
    NoDictionary --> Cached_File: ReadCache<br/>(prior session left a file)
    NoDictionary --> Live: live fetch succeeds<br/>before cache/seed read

    Cached_Seed --> Live: live fetch succeeds<br/>(also overwrites cache)
    Cached_File --> Live: live fetch succeeds<br/>(updates cache if differs)

    Live --> Live: refresh succeeds<br/>(timestamp advances)
    Live --> Cached_File: refresh fails<br/>(re-label only;<br/>dictionary preserved)
    Cached_File --> Cached_File: refresh fails<br/>(LastFailureReason updates)
    Cached_Seed --> Cached_Seed: refresh fails<br/>(LastFailureReason updates)

    state NoDictionary {
        description: SourceChanged not yet fired
    }
    state Live {
        description: DictionarySource.Live(t)
    }
    state Cached_File {
        description: DictionarySource.Cached(t, FromLocalFile, lastErr?)
    }
    state Cached_Seed {
        description: DictionarySource.Cached(seedBuildTime, FromEmbeddedSeed, lastErr?)
    }
```

**Reading the diagram**:
- The two "Cached" states are visually distinct so the seed-vs.-file provenance is clear; in F# they are both `DictionarySource.Cached(...)` differing only in `Origin`.
- Once `Live(t)` is reached in a session, subsequent failures re-label to `Cached(t, …)` but the in-memory dictionary stays — proved by `source_data_preserved`.
- The diagram intentionally excludes the in-flight transient (`refreshing…`) — that is a UX overlay, not a state-machine state.

---

## 5. On-disk artifacts

| Path | Format | Owner | Lifecycle |
|---|---|---|---|
| `%LOCALAPPDATA%\Stem.ButtonPanelTester\dictionary.json` | UTF-8 JSON, canonicalised (no whitespace), schema = `DictionaryResolvedDto` | `JsonFileDictionaryCache` | Created on first launch (from seed) or on first successful fetch; rewritten on every fetch whose result differs from current contents. |
| `%LOCALAPPDATA%\Stem.ButtonPanelTester\dictionary.json.sha256` | UTF-8 ASCII, 64 lowercase hex chars + LF | `JsonFileDictionaryCache` | Always written together with the JSON (atomic temp+rename). |
| `%LOCALAPPDATA%\Stem.ButtonPanelTester\credential.dpapi` | DPAPI ciphertext over the InstallationCredential's `string` value, scope `CurrentUser` | `DpapiCredentialStore` | Written on registration success; deleted only by re-registration's atomic overwrite. |

`%LOCALAPPDATA%` resolves via `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)`. On non-Windows the same SF returns the OS-appropriate path (`~/.local/share/` on Linux); when we port, the credential adapter is the only one whose impl changes.

---

## 6. Validation rules at type boundaries

- **HTTP → `ButtonPanelDictionary`**: deserialise with `System.Text.Json`. On exception: surface as `Failed(MalformedPayload, Some "<exception type>")`.
- **`BootstrapToken.TryCreate`**: trims, rejects null/empty/whitespace. Dialog input runs through this before submit.
- **`InstallationCredential.Create`**: no validation on shape; the server contract dictates what a valid credential looks like.
- **`ContentHash`**: lowercase hex, exactly 64 chars; `JsonFileDictionaryCache` rejects sidecars not matching this format as `CacheUnreadable`.

---

## 7. What this slice does **not** introduce

- No `PanelTypeId`, no `VariableId` smart constructors. Variables and panel types are POCO records this slice; future slices that traverse them (e.g. baptize, run-test) introduce richer types as needed.
- No CAN types. `ButtonPanelTester.Core` adds only the `Dictionary` namespace.
- No EF Core entity. Storage is filesystem-only this slice.
