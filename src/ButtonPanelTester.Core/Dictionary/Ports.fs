namespace Stem.ButtonPanelTester.Core.Dictionary

open System
open System.Threading
open System.Threading.Tasks

/// Abstraction over wall-clock time, per
/// `specs/001-fetch-dictionary/contracts/ports.md` §IClock.
/// `DictionaryService` and tests reason deterministically about
/// `FetchedAt` timestamps through this seam; without it every
/// test comparing `DictionarySource.Live(t)` against a fixture
/// would need a time-skew tolerance.
///
/// Production adapter: `Infrastructure.Clock.SystemClock` (T018,
/// wraps `DateTimeOffset.UtcNow`). Test adapter:
/// `Tests.Fakes.FrozenClock` (T019, exposes `Advance` and
/// `SetTo` for scripted time progression).
type IClock =
    /// Current UTC instant. Production implementations wrap
    /// `DateTimeOffset.UtcNow`; test fakes return a scripted value
    /// independent of the system clock.
    abstract member UtcNow: unit -> DateTimeOffset

/// Live dictionary fetch over the wire, per
/// `specs/001-fetch-dictionary/contracts/ports.md` §IDictionaryProvider.
/// Returns either `Success(dict, fetchedAt)` or
/// `Failed(reason, detail)` — never both, never neither. The F#
/// DU `DictionaryFetchResult` (T014) enforces the closure at the
/// type-system level; Lean's `provider_success_xor_failed`
/// theorem in T026 mechanises the same property at the proof
/// level. Implementations MUST translate every expected HTTP /
/// network failure (see `dictionary-api.md` error table) into
/// the `Failed` case rather than throwing; only
/// `OperationCanceledException` is permitted when `ct` is
/// cancelled mid-flight.
///
/// Production adapter: `Infrastructure.Http.HttpDictionaryProvider`
/// (T049, implements the `dictionary-api.md` contract). Test
/// adapter: `Tests.Fakes.InMemoryDictionaryProvider` (T019,
/// scripted-queue of pre-built `DictionaryFetchResult` values).
type IDictionaryProvider =
    /// Fetch the dictionary identified by the adapter's bound
    /// configuration (`Dictionary:Id`). Honours `ct`: cancellation
    /// raises `OperationCanceledException`; every other expected
    /// failure mode is reified as `Failed(reason, detail)`.
    abstract member FetchAsync: ct: CancellationToken -> Task<DictionaryFetchResult>

/// On-disk dictionary cache, per
/// `specs/001-fetch-dictionary/contracts/ports.md` §IDictionaryCache.
/// Manages the JSON-plus-sidecar pair documented in
/// `cache-format.md`:
///   - `ExistsAsync`               — `true` iff the JSON file and
///     its sidecar are both present and consistent enough to
///     attempt a read.
///   - `ReadAsync`                 — `Success(dict, fetchedAt)` if
///     the cache reads cleanly and the sidecar hash matches;
///     otherwise `Failed(CacheAbsent | CacheUnreadable, detail)`
///     per the T013 cache-failure case extension.
///   - `WriteAsync`                — atomic temp+rename, skips
///     the write when the new content's hash equals the
///     existing sidecar.
///   - `ExtractSeedIfMissingAsync` — no-op when `ExistsAsync`
///     is `true`; otherwise extracts the embedded seed via the
///     same atomic path.
///
/// Cache-memory consistency is mechanised by Lean's
/// `cache_memory_equal_post_first_success` theorem in T027.
///
/// Production adapter:
/// `Infrastructure.Persistence.JsonFileDictionaryCache` (T053,
/// implements `cache-format.md`). Test adapter:
/// `Tests.Fakes.InMemoryDictionaryCache` (T019, ref-cell
/// state with a `SeedWith` setup hook).
type IDictionaryCache =
    /// `true` iff both the JSON file and its sidecar are present.
    /// Does not validate the sidecar hash; callers that need full
    /// integrity call `ReadAsync`.
    abstract member ExistsAsync: ct: CancellationToken -> Task<bool>
    /// Read the cache. Returns `Success(dict, fetchedAt)` when the
    /// JSON envelope parses and the sidecar hash matches the JSON
    /// bytes; otherwise `Failed(CacheAbsent, _)` (file missing) or
    /// `Failed(CacheUnreadable, detail)` (hash mismatch, malformed
    /// envelope, IO error).
    abstract member ReadAsync: ct: CancellationToken -> Task<DictionaryFetchResult>
    /// Persist `dict` with the supplied `fetchedAt`. Atomic
    /// temp+rename of both the JSON file and the sidecar; the
    /// skip-write optimisation suppresses the write when the
    /// computed sidecar hash equals the on-disk sidecar.
    abstract member WriteAsync:
        dict: ButtonPanelDictionary *
        fetchedAt: DateTimeOffset *
        ct: CancellationToken ->
            Task
    /// No-op when `ExistsAsync` returns `true`. Otherwise extract
    /// the embedded `Assets/dictionary.seed.json` resource to the
    /// cache directory via the same atomic temp+rename used by
    /// `WriteAsync`.
    abstract member ExtractSeedIfMissingAsync: ct: CancellationToken -> Task

/// Persistent store for the server-issued `InstallationCredential`,
/// per `specs/001-fetch-dictionary/contracts/ports.md`
/// §ICredentialStore.
///   - `ExistsAsync` — file presence, not decryptability.
///   - `LoadAsync`   — `Some credential` on present-and-decrypts;
///                     `None` on absent OR decrypt failure
///                     (failure is logged via the standard
///                     logger but not thrown — see
///                     `credential-format.md`).
///   - `SaveAsync`   — atomic temp+rename. Overwrites any prior
///                     value.
///   - `DeleteAsync` — idempotent; no error if absent.
///
/// The credential's **LOGGING** discipline is inherited from
/// `InstallationCredential` (T015): never log instances of the
/// credential type itself; carry only derived metadata (presence
/// flag, length) on the diagnostic surface. See
/// `docs/Standards/LOGGING.md`.
///
/// Production adapter:
/// `Infrastructure.Persistence.DpapiCredentialStore` (T056,
/// implements `credential-format.md` with DPAPI per-user
/// encryption on Windows). Test adapter:
/// `Tests.Fakes.InMemoryCredentialStore` (T019, ref-cell
/// state).
type ICredentialStore =
    /// `true` iff the credential file is present on disk. Does not
    /// attempt decryption; callers that need a usable credential
    /// call `LoadAsync`.
    abstract member ExistsAsync: ct: CancellationToken -> Task<bool>
    /// Return `Some credential` when the file is present and
    /// decrypts cleanly; `None` when it is absent or decryption
    /// fails. Decrypt failures are logged via the bound `ILogger`
    /// but never thrown.
    abstract member LoadAsync: ct: CancellationToken -> Task<InstallationCredential option>
    /// Persist `credential` via atomic temp+rename. Overwrites any
    /// prior value at the target path.
    abstract member SaveAsync: credential: InstallationCredential * ct: CancellationToken -> Task
    /// Remove the credential file. Idempotent: returns successfully
    /// when the file is already absent.
    abstract member DeleteAsync: ct: CancellationToken -> Task

/// Bootstrap registration exchange against the server, per
/// `specs/001-fetch-dictionary/contracts/ports.md`
/// §IRegistrationClient. Consumes a `BootstrapToken` (T015,
/// trim-then-non-empty-validated) and yields either an
/// `InstallationCredential` (T015, opaque) on success or a
/// typed `RegistrationError` (T015, three closed cases) on
/// any expected HTTP outcome listed in `registration-api.md`.
/// Implementations MUST NOT throw for expected failures; only
/// `OperationCanceledException` is permitted when `ct` is
/// cancelled mid-flight.
///
/// The `Ok` payload is the verbatim `apiCredential` field from
/// the registration response, wrapped via
/// `InstallationCredential.Create` (which is total per T015).
///
/// Production adapter: `Infrastructure.Http.HttpRegistrationClient`
/// (T050, implements `registration-api.md`). Test adapter:
/// `Tests.Fakes.InMemoryRegistrationClient` (T019,
/// `Map<string, Result<_,_>>`-scripted; unscripted tokens
/// default to `Error TokenInvalid`).
type IRegistrationClient =
    /// Exchange `token` for an `InstallationCredential`. `Ok` carries
    /// the verbatim server `apiCredential`; `Error` carries a typed
    /// `RegistrationError` for any expected HTTP outcome. Honours
    /// `ct`: cancellation raises `OperationCanceledException`.
    abstract member RegisterAsync:
        token: BootstrapToken * ct: CancellationToken ->
            Task<Result<InstallationCredential, RegistrationError>>
