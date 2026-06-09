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
open NReco.Logging.File
open Peak.Can.Basic.BackwardCompatibility
open Core.Interfaces
open Infrastructure.Protocol.Hardware
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Infrastructure
open Stem.ButtonPanelTester.Infrastructure.Auth
open Stem.ButtonPanelTester.Infrastructure.Can
open Stem.ButtonPanelTester.Infrastructure.Http
open Stem.ButtonPanelTester.Infrastructure.Persistence
open Stem.ButtonPanelTester.Services.Can
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
///                              wired with `%LOCALAPPDATA%\Stem\ButtonPanelTester\cache\`
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
    ///   - `services.AddLogging(...)` wires three providers behind the
    ///     `ILogger<T>` resolutions used by `DpapiCredentialStore`,
    ///     `HttpRegistrationClient`, `HttpDictionaryProvider`,
    ///     `DictionaryWarmUp`, etc.: `AddSimpleConsole()` for terminal-
    ///     launched dev runs, `AddDebug()` for the IDE Output window,
    ///     and `AddFile(...)` (NReco) writing a rolling text log to
    ///     `%LOCALAPPDATA%\Stem\ButtonPanelTester\logs\app.log` per
    ///     STEM `APP_DATA.md` (v1.9.0) — the path
    ///     `specs/001-fetch-dictionary/quickstart.md` Troubleshooting
    ///     tail tells supplier operators to inspect. Default minimum
    ///     level is `Information`; the `Microsoft.*` category is held
    ///     at `Warning` so framework chatter (HTTP, DI scopes) does
    ///     not drown app lines.
    ///   - `services.AddHttpClient()` so `HttpRegistrationClient`
    ///     can resolve an `HttpClient` per request from
    ///     `IHttpClientFactory`.
    ///   - `services.Configure<DictionaryOptions>(config.GetSection
    ///     "Dictionary")` so `IOptions<DictionaryOptions>` binds to
    ///     `appsettings.json`'s `Dictionary:` section.
    ///   - `ICredentialStore → DpapiCredentialStore` (real adapter
    ///     wired to `%LOCALAPPDATA%\Stem\ButtonPanelTester\credentials\`).
    ///   - `IRegistrationClient → HttpRegistrationClient`, replacing
    ///     the US1 `OfflineRegistrationClient` placeholder.
    let configure (services: IServiceCollection) (config: IConfiguration) : IServiceCollection =
        // `StemAppData.logsDir ()` is the per-app `logs/` sub-folder per
        // STEM `APP_DATA.md`; the helper creates the directory on first
        // call via `ensureDir`, so NReco opening `app.log` for append
        // immediately afterwards is safe.
        let logPath = Path.Combine(StemAppData.logsDir (), "app.log")

        services.AddLogging(fun builder ->
            builder
                .SetMinimumLevel(LogLevel.Information)
                // Suppress framework chatter (DI, options, HTTP request
                // start/end) at Information so app lines stay readable.
                // `System.Net.Http` covers the `IHttpClientFactory`
                // pipeline categories (`System.Net.Http.HttpClient.<name>.*`)
                // — they sit outside the `Microsoft.*` prefix.
                .AddFilter("Microsoft", LogLevel.Warning)
                .AddFilter("System.Net.Http", LogLevel.Warning)
                .AddSimpleConsole()
                .AddDebug()
                .AddFile(
                    logPath,
                    fun opts ->
                        opts.Append <- true
                        opts.FileSizeLimitBytes <- 5L * 1024L * 1024L
                        opts.MaxRollingFiles <- 3
                )
            |> ignore)
        |> ignore

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
                let seedReader = EmbeddedSeedExtractor.readSeedBytes (guiAssembly ())
                JsonFileDictionaryCache(StemAppData.cacheDir (), seedReader) :> IDictionaryCache)
            .AddSingleton<ICredentialStore>(fun sp ->
                let logger = sp.GetRequiredService<ILogger<DpapiCredentialStore>>()
                DpapiCredentialStore(StemAppData.credentialsDir (), logger) :> ICredentialStore)
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
            // IInstallationDescriptorProvider caches the hashed Windows
            // SID + machine GUID + AppVersion at construction (immutable
            // host facts) but re-reads the install.guid sidecar on every
            // Current() call so the Re-Register flow (#98) can rotate
            // the InstallGuid between registration attempts within a
            // single process lifetime. Registered as a singleton so the
            // hash cache survives across calls.
            // `install.guid` lives alongside `credential.dpapi` because the
            // Re-Register flow (#98) rotates both together; both are
            // per-installation identity artefacts.
            .AddSingleton<IInstallationDescriptorProvider>(fun _ ->
                InstallationDescriptorProvider(StemAppData.credentialsDir (), guiAssembly ())
                :> IInstallationDescriptorProvider)
            .AddSingleton<IRegistrationClient>(fun sp ->
                let factory = sp.GetRequiredService<IHttpClientFactory>()
                let client = factory.CreateClient()
                let options = sp.GetRequiredService<IOptions<DictionaryOptions>>()
                let descriptorProvider =
                    sp.GetRequiredService<IInstallationDescriptorProvider>()
                let logger = sp.GetRequiredService<ILogger<HttpRegistrationClient>>()
                HttpRegistrationClient(client, options, descriptorProvider, logger)
                :> IRegistrationClient)
            // IDictionaryServiceWarmUp → HttpDictionaryServiceWarmUp,
            // hitting the unauthenticated GET /health endpoint per
            // phase-7.md slice 3. Reuses the named "Dictionary"
            // HttpClient — the ApiKeyAuthHandler in that pipeline is
            // harmless because /health is in the server's unauth
            // allow-list (stem-dictionaries-manager Program.cs:86-87).
            .AddSingleton<IDictionaryServiceWarmUp>(fun sp ->
                let factory = sp.GetRequiredService<IHttpClientFactory>()
                let httpClient = factory.CreateClient("Dictionary")
                let logger = sp.GetRequiredService<ILogger<HttpDictionaryServiceWarmUp>>()
                HttpDictionaryServiceWarmUp(httpClient, logger) :> IDictionaryServiceWarmUp)
            .AddSingleton<DictionaryWarmUp>()
            .AddSingleton<IDictionaryService, DictionaryService>()
            // CAN port + service graph, registered AFTER IDictionaryService so the
            // FR-001 boot order (dictionary first, CAN second) is observable in the
            // DI container's enumeration. Functional ordering is enforced by App.fs's
            // Opened handler awaiting the dictionary InitializeAsync before
            // `ICanLinkService.InitializeAsync`.
            //
            // `CanPortShare` lazily builds ONE `ICommunicationPort` and shares it between
            // the lifecycle adapter (`PcanCanLink`, via `GetOrBuild` as its port factory)
            // and the receive adapter (`PcanCanFrameStream`, via `OnBuilt`). The vendored
            // `PCANManager`'s P/Invoke into `pcanbasic.dll` does NOT execute until the
            // first `OpenAsync` forces `GetOrBuild`, so a host without the PEAK driver
            // (`DllNotFoundException`) surfaces an observable `Error(Fatal ...)` on
            // `LinkStateChanged` rather than crashing the composition root before
            // MainWindow paints. One PEAK handle now serves both lifecycle + receive —
            // the holder is the single consumer of the port.
            .AddSingleton<CanPortShare>(fun _ ->
                new CanPortShare(fun () ->
                    let driver = new PCANManager(TPCANBaudrate.PCAN_BAUD_250K) :> IPcanDriver
                    new CanPort(driver) :> ICommunicationPort))
            .AddSingleton<ICanLink>(fun sp ->
                let share = sp.GetRequiredService<CanPortShare>()
                let logger = sp.GetRequiredService<ILogger<PcanCanLink>>()
                PcanCanLink((fun () -> share.GetOrBuild()), logger) :> ICanLink)
            .AddSingleton<ICanFrameStream>(fun sp ->
                let share = sp.GetRequiredService<CanPortShare>()
                let clock = sp.GetRequiredService<IClock>()
                let logger = sp.GetRequiredService<ILogger<PcanCanFrameStream>>()
                new PcanCanFrameStream(share, clock, logger) :> ICanFrameStream)
            .AddSingleton<ICanLinkService>(fun sp ->
                let link = sp.GetRequiredService<ICanLink>()
                let clock = sp.GetRequiredService<IClock>()
                let logger = sp.GetRequiredService<ILogger<CanLinkService>>()
                CanLinkService(link, clock, logger) :> ICanLinkService)
            // Panel discovery (#197) — split out of CanLinkService so
            // spec-003 owns the discovery pipeline as an independent
            // spec. The live WHO_I_AM ingest pipeline (filter → parse →
            // coalesce → publish) is wired here: the service subscribes
            // to `ICanFrameStream` and gates on `ICanLinkService`'s
            // Connected state. `ICanFrameStream` now binds to the real
            // `PcanCanFrameStream` above (T018), so WHO_I_AM frames flow
            // once the link `OpenAsync` builds the shared PEAK port;
            // nothing renders the surface yet (the third UI slot is
            // Phase D, spec-003 too). The three dependencies are already
            // bound earlier in this graph.
            .AddSingleton<IPanelDiscoveryService>(fun sp ->
                let frameStream = sp.GetRequiredService<ICanFrameStream>()
                let link = sp.GetRequiredService<ICanLinkService>()
                let clock = sp.GetRequiredService<IClock>()
                new PanelDiscoveryService(frameStream, link, clock) :> IPanelDiscoveryService)
