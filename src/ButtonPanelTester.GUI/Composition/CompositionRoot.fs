namespace Stem.ButtonPanelTester.GUI.Composition

open System
open System.IO
open System.Net.Http
open System.Reflection
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Options
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Infrastructure
open Stem.ButtonPanelTester.Infrastructure.Auth
open Stem.ButtonPanelTester.Infrastructure.Http
open Stem.ButtonPanelTester.Infrastructure.Persistence
open Stem.ButtonPanelTester.Services.Dictionary

// `OfflineDictionaryProvider` (the Phase 3 / US1 no-op placeholder)
// was removed in Phase 5 / T051: the real
// `Infrastructure.Http.HttpDictionaryProvider` is now wired below
// against the named `"Dictionary"` HttpClient, and US1's offline
// launch path no longer goes through `IDictionaryProvider` (it
// reads the cache directly).

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

    /// Register the Phase 3 + Phase 4 service graph against
    /// `services`. Call site is `Program.main` (T035); test
    /// bootstraps register in-memory fakes directly without invoking
    /// this function.
    ///
    /// Phase 4 (US2) additions over Phase 3's bindings:
    ///   - `services.AddLogging()` so `ILogger<T>` resolutions
    ///     succeed for `DpapiCredentialStore`, `HttpRegistrationClient`,
    ///     and `RegistrationDialogWindow`. Without further provider
    ///     calls (e.g. `AddConsole`) MEL provides a no-op
    ///     `NullLoggerFactory`; a real sink is a follow-up.
    ///   - `services.AddHttpClient()` so `HttpRegistrationClient`
    ///     can resolve an `HttpClient` per request from
    ///     `IHttpClientFactory`.
    ///   - `services.Configure<DictionaryOptions>(config.GetSection
    ///     "Dictionary")` so `IOptions<DictionaryOptions>` binds to
    ///     `appsettings.json`'s `Dictionary:` section.
    ///   - `ICredentialStore → DpapiCredentialStore` (real adapter
    ///     wired to `%LOCALAPPDATA%\Stem.ButtonPanelTester\`).
    ///   - `IRegistrationClient → HttpRegistrationClient`, replacing
    ///     the US1 `OfflineRegistrationClient` placeholder.
    let configure (services: IServiceCollection) (config: IConfiguration) : IServiceCollection =
        services.AddLogging() |> ignore
        services.Configure<DictionaryOptions>(config.GetSection("Dictionary")) |> ignore

        // ApiKeyAuthHandler reads ICredentialStore.LoadAsync per
        // outgoing request and injects the X-Api-Key header per
        // contracts/dictionary-api.md. Registered as transient
        // because IHttpMessageHandlerFactory disposes handlers when
        // the named client's lifetime expires (default 2 min);
        // capturing a singleton handler here would leak past that
        // point.
        services.AddTransient<ApiKeyAuthHandler>() |> ignore

        // Unnamed IHttpClientFactory client for HttpRegistrationClient
        // (no DelegatingHandler — /register is anonymous).
        services.AddHttpClient() |> ignore

        // Named "Dictionary" client with BaseAddress + the
        // ApiKeyAuthHandler in its pipeline, per phase-5.md T051 /
        // research.md R5 wiring. HttpDictionaryProvider consumes this
        // exact client below.
        services
            .AddHttpClient(
                "Dictionary",
                System.Action<IServiceProvider, HttpClient>(fun sp client ->
                    let options = sp.GetRequiredService<IOptions<DictionaryOptions>>()
                    let baseUrl =
                        let raw = options.Value.BaseUrl
                        if raw.EndsWith('/') then raw else raw + "/"
                    client.BaseAddress <- Uri(baseUrl))
            )
            .AddHttpMessageHandler<ApiKeyAuthHandler>()
        |> ignore

        services
            .AddSingleton<IClock, SystemClock>()
            .AddSingleton<IDictionaryCache>(fun _sp ->
                let cacheDir = defaultCacheDirectory ()
                let seedReader = EmbeddedSeedExtractor.readSeedBytes (guiAssembly ())
                JsonFileDictionaryCache(cacheDir, seedReader) :> IDictionaryCache)
            .AddSingleton<ICredentialStore>(fun sp ->
                let dir = defaultCacheDirectory ()
                let logger = sp.GetRequiredService<ILogger<DpapiCredentialStore>>()
                DpapiCredentialStore(dir, logger) :> ICredentialStore)
            // IDictionaryProvider → HttpDictionaryProvider against the
            // named "Dictionary" client. Phase 5 / T051: replaces the
            // Phase 3 OfflineDictionaryProvider placeholder.
            .AddSingleton<IDictionaryProvider>(fun sp ->
                let factory = sp.GetRequiredService<IHttpClientFactory>()
                let httpClient = factory.CreateClient("Dictionary")
                let options = sp.GetRequiredService<IOptions<DictionaryOptions>>()
                let clock = sp.GetRequiredService<IClock>()
                let logger = sp.GetRequiredService<ILogger<HttpDictionaryProvider>>()
                HttpDictionaryProvider(httpClient, options, clock, logger) :> IDictionaryProvider)
            // InstallationDescriptor is built once at first resolve via
            // InstallationDescriptorBuilder (Windows SID + machine GUID
            // hashed; install.guid sidecar read or created). Registered
            // as a singleton so every IRegistrationClient call carries
            // the same descriptor across this process's lifetime.
            .AddSingleton<InstallationDescriptor>(fun _ ->
                InstallationDescriptorBuilder.build (defaultCacheDirectory ()) (guiAssembly ()))
            .AddSingleton<IRegistrationClient>(fun sp ->
                let factory = sp.GetRequiredService<IHttpClientFactory>()
                let client = factory.CreateClient()
                let options = sp.GetRequiredService<IOptions<DictionaryOptions>>()
                let descriptor = sp.GetRequiredService<InstallationDescriptor>()
                let logger = sp.GetRequiredService<ILogger<HttpRegistrationClient>>()
                HttpRegistrationClient(client, options, descriptor, logger) :> IRegistrationClient)
            .AddSingleton<IDictionaryService, DictionaryService>()
