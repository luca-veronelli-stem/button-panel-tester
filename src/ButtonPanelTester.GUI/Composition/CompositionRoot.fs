namespace Stem.ButtonPanelTester.GUI.Composition

open System
open System.IO
open System.Reflection
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Infrastructure
open Stem.ButtonPanelTester.Infrastructure.Persistence
open Stem.ButtonPanelTester.Services.Dictionary

/// Offline-launch placeholder for `IDictionaryProvider`. Always
/// returns `Failed(NetworkUnreachable, None)` so US1's offline
/// launch path (extract seed → read cache → emit Cached) succeeds
/// without a live HTTP dependency. Replaced by
/// `Infrastructure.Http.HttpDictionaryProvider` in T053 (US3 /
/// Phase 5) when the wire adapter ships.
type internal OfflineDictionaryProvider() =
    interface IDictionaryProvider with
        member _.FetchAsync(_: CancellationToken) =
            Task.FromResult(Failed(NetworkUnreachable, None))

/// Offline-launch placeholder for `IRegistrationClient`. Always
/// returns `Error(RegistrationNetwork NetworkUnreachable)` so US1's
/// composition graph constructs without DPAPI or HTTP dependencies.
/// Replaced by `Infrastructure.Http.HttpRegistrationClient` in T044
/// (US2 / Phase 4) when the registration ceremony ships.
type internal OfflineRegistrationClient() =
    interface IRegistrationClient with
        member _.RegisterAsync(_: BootstrapToken, _: CancellationToken) =
            Task.FromResult(Error(RegistrationNetwork NetworkUnreachable))

/// `Microsoft.Extensions.DependencyInjection` wiring for the
/// `ButtonPanelTester.GUI` host, per
/// `specs/001-fetch-dictionary/contracts/ports.md` §Composition root.
/// This is the **only** place in the codebase that names production
/// adapter types — `Services` and `Core` consume the ports
/// (`IClock`, `IDictionaryCache`, `IDictionaryProvider`,
/// `IRegistrationClient`, `IDictionaryService`) without knowing
/// which concrete instance the container will hand them.
///
/// Phase 3 (US1, MVP) bindings:
///   - `IClock`              → `SystemClock`                  (T018, real).
///   - `IDictionaryCache`    → `JsonFileDictionaryCache`      (T029, real)
///                              wired with `%LOCALAPPDATA%\Stem.ButtonPanelTester\`
///                              as the cache directory and
///                              `EmbeddedSeedExtractor.readSeedBytes`
///                              (T031) reading the GUI assembly's
///                              embedded seed (T030).
///   - `IDictionaryService`  → `DictionaryService`            (T032, real,
///                              offline-only — RefreshAsync raises).
///   - `IDictionaryProvider` → `OfflineDictionaryProvider`    (above,
///                              no-op placeholder — replaced in T053).
///   - `IRegistrationClient` → `OfflineRegistrationClient`    (above,
///                              no-op placeholder — replaced in T044).
///
/// `ICredentialStore` is **deliberately not bound** in this slice.
/// US1 doesn't read or write credentials; binding `ICredentialStore`
/// here would require a no-op placeholder whose only consumer is
/// US2's `RegistrationDialog` (T042). T044's composition update
/// adds the real `DpapiCredentialStore` binding when the
/// registration ceremony lands.
///
/// `IConfiguration` is accepted by the configure function so US2
/// + US3 can introduce `appsettings.json` bindings (Dictionary
/// section, X-Api-Key sourcing) without changing the composition
/// signature. US1 reads no config values today.
[<RequireQualifiedAccess>]
module CompositionRoot =

    /// Returns `%LOCALAPPDATA%\Stem.ButtonPanelTester` as the cache
    /// directory root. `JsonFileDictionaryCache` creates the
    /// directory on first write (`Directory.CreateDirectory` is
    /// idempotent), so this function is total even on a clean
    /// machine.
    let private defaultCacheDirectory () : string =
        let local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        Path.Combine(local, "Stem.ButtonPanelTester")

    /// Returns the GUI assembly — the one carrying the embedded
    /// `dictionary.seed.json` resource. `Assembly.GetExecutingAssembly`
    /// resolves to whichever assembly the calling code is compiled
    /// into; since this module is compiled into
    /// `ButtonPanelTester.GUI.dll`, the returned assembly is the
    /// one whose manifest contains the seed (T030).
    let private guiAssembly () : Assembly =
        Assembly.GetExecutingAssembly()

    /// Register the Phase 3 service graph against `services`. Call
    /// site is `Program.main` (T035); test bootstraps register
    /// in-memory fakes directly without invoking this function.
    let configure (services: IServiceCollection) (_config: IConfiguration) : IServiceCollection =
        services
            .AddSingleton<IClock, SystemClock>()
            .AddSingleton<IDictionaryCache>(fun _sp ->
                let cacheDir = defaultCacheDirectory ()
                let seedReader = EmbeddedSeedExtractor.readSeedBytes (guiAssembly ())
                JsonFileDictionaryCache(cacheDir, seedReader) :> IDictionaryCache)
            .AddSingleton<IDictionaryProvider, OfflineDictionaryProvider>()
            .AddSingleton<IRegistrationClient, OfflineRegistrationClient>()
            .AddSingleton<IDictionaryService, DictionaryService>()
