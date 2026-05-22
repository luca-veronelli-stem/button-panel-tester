namespace Stem.ButtonPanelTester.GUI

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Layout
open Avalonia.Platform
open Avalonia.Styling
open Avalonia.Svg.Skia
open Avalonia.Themes.Fluent
open Avalonia.Threading
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Hosts
open Avalonia.FuncUI.VirtualDom
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Dictionary
open Stem.ButtonPanelTester.Services.Registration
open Stem.ButtonPanelTester.GUI.Dictionary

/// Chrome wiring for `MainWindow` — the brand-mark header and the
/// taskbar / title-bar icon. Lives at namespace scope so the static
/// `SvgSource` cache binds once for the process: every refresh
/// re-renders the body but reuses the parsed brand-mark picture.
module private Chrome =

    // Asset URIs by theme. Light + Default render the positive brand
    // mark on the navy `stem-app-icon-positive.ico`; Dark renders the
    // negative brand mark on the `stem-app-icon-mono-white.ico` so
    // the title-bar icon stays visible against the dark chrome.
    // `mono-white` is the brand-manual variant for tiny surfaces
    // (16/32/48 px app icon); `negative` is the variant for the
    // full-colour mark on a dark canvas. No new artwork — both ship
    // under `Resources/branding/` already.
    let private positiveBrandSvgUri =
        "avares://ButtonPanelTester.GUI/Resources/branding/brand-marks/positive/stem-corporate.svg"

    let private negativeBrandSvgUri =
        "avares://ButtonPanelTester.GUI/Resources/branding/brand-marks/negative/stem-corporate.svg"

    // Multi-frame .ico — Windows picks the matching frame for each surface
    // (16/32/48/256 px). Necessary because Avalonia's `WindowIcon(Bitmap)`
    // overload feeds a single-resolution raster to the title-bar (~16 px)
    // and the taskbar (~32-48 px), and Skia's single-step downsample of
    // the agency 2134×2134 PNG produces visible aliasing on the small
    // surfaces. The positive .ico also drives the `.exe` shell icon via
    // the MSBuild `<ApplicationIcon>` property — one asset, two delivery
    // mechanisms (PE resource block + avares://). The PE resource block
    // is single-icon by construction; only the avares:// surface
    // theme-swaps.
    let private positiveAppIconUri =
        Uri("avares://ButtonPanelTester.GUI/Resources/branding/app-icons/stem-app-icon-positive.ico")

    let private monoWhiteAppIconUri =
        Uri("avares://ButtonPanelTester.GUI/Resources/branding/app-icons/stem-app-icon-mono-white.ico")

    // Process-wide cache: SvgSource.Load parses and rasterises the SVG
    // once; every SvgImage wrapper after that reuses the same picture.
    // Pre-load both variants so a theme switch swaps a cached reference
    // instead of triggering a fresh parse on the UI thread.
    let private positiveBrandSvgSource = SvgSource.Load(positiveBrandSvgUri, null)
    let private negativeBrandSvgSource = SvgSource.Load(negativeBrandSvgUri, null)

    let private isDark (theme: ThemeVariant) = theme = ThemeVariant.Dark

    let private brandSvgSourceFor (theme: ThemeVariant) =
        if isDark theme then negativeBrandSvgSource else positiveBrandSvgSource

    let private appIconUriFor (theme: ThemeVariant) =
        if isDark theme then monoWhiteAppIconUri else positiveAppIconUri

    let private brandMargin = Thickness(Spacing.lg, Spacing.sm, Spacing.lg, Spacing.sm)

    /// Inline brand mark for the window header. ~28px high matches the
    /// in-line `h2` type (`Typography.h2 = 20.0`) with breathing room
    /// either side; the `corporate` positive mark is selected via
    /// `Branding.division = Division.None`. The `theme` argument picks
    /// the brand-manual treatment (positive on light, negative on
    /// dark).
    let brandHeader (theme: ThemeVariant) : Control =
        let svgImage = SvgImage(Source = brandSvgSourceFor theme)
        let img = Image()
        img.Source <- svgImage
        img.Height <- 28.0
        img.HorizontalAlignment <- HorizontalAlignment.Left
        img.Margin <- brandMargin
        img :> Control

    /// Wraps any body control with the brand-mark header docked at the
    /// top of a `DockPanel`. Both `renderInitializing` and the
    /// `renderStatusRow` slot consume this so the header stays visible
    /// for every render state.
    let wrapWithHeader (theme: ThemeVariant) (body: Control) : Control =
        let panel = DockPanel()
        let header = brandHeader theme
        DockPanel.SetDock(header, Dock.Top)
        panel.Children.Add(header)
        panel.Children.Add(body)
        panel :> Control

    /// `Window.Icon` from the multi-frame .ico. `WindowIcon(Stream)`
    /// hands the bytes to the Win32 backend's `IconImpl`, which parses
    /// the ICONDIR table and exposes every frame for Windows to pick
    /// from — title-bar gets the 16 px frame, taskbar the 32/48 px
    /// frame, Alt-Tab the 256 px frame. Each surface lands on a
    /// pre-rendered raster instead of a single oversize bitmap
    /// scaled at draw time.
    let windowIcon (theme: ThemeVariant) : WindowIcon =
        use stream = AssetLoader.Open(appIconUriFor theme)
        WindowIcon(stream)

    /// Reads the current Avalonia application's `ActualThemeVariant`,
    /// falling back to `Default` when the framework hasn't fully
    /// initialised yet (rare; mainly the unit-test path that
    /// constructs `MainWindow` without a running `Application`).
    let currentTheme () : ThemeVariant =
        match Application.Current with
        | null -> ThemeVariant.Default
        | app -> app.ActualThemeVariant

/// Main window for the US1 offline-launch surface. Hosts the
/// `DictionaryStatusRow` view (T034) docked at top, kicks off
/// `IDictionaryService.InitializeAsync` on `Opened`, and re-renders
/// the status row on every `SourceChanged` event.
///
/// The renderer is imperative — the window holds a single content
/// slot that gets replaced (via FuncUI's `VirtualDom.create`)
/// whenever the service emits a new `DictionarySource`. A FuncUI
/// `Component` hook-based subscription would also work but the
/// FuncUI 1.5 hooks API isn't worth the extra surface for the one
/// reactive value this window carries today. T052 (US3) will likely
/// graduate this to a Component if the refresh-in-flight UX wants
/// more local state.
type MainWindow(services: IServiceProvider) as this =
    inherit HostWindow()

    let service = services.GetRequiredService<IDictionaryService>()
    let credentialStore = services.GetRequiredService<ICredentialStore>()
    let registrationClient = services.GetRequiredService<IRegistrationClient>()
    let descriptorProvider =
        services.GetRequiredService<IInstallationDescriptorProvider>()
    let warmUp = services.GetRequiredService<DictionaryWarmUp>()
    let clock = services.GetRequiredService<IClock>()
    let dialogLogger =
        services.GetRequiredService<ILogger<RegistrationDialogWindow>>()
    let mainLogger = services.GetRequiredService<ILogger<MainWindow>>()
    let cacheFilePath =
        Path.Combine(Composition.StemAppData.cacheDir (), "dictionary.json")

    // Local mutable refresh state. T052 / FR-006: a click on the
    // Refresh button kicks off `IDictionaryService.RefreshAsync` and
    // flips `refreshState` to `Refreshing` so the status row's
    // in-flight UX (pulsing pill opacity, spinner glyph, ellipsis
    // headline) renders until the task resolves.
    let mutable refreshState = DictionaryStatusRow.Idle
    let mutable lastSource: DictionarySource option = None

    // Active Avalonia theme. Initial value read once at construction
    // from `Application.Current.ActualThemeVariant`; every render
    // reads from this cell so a future `ActualThemeVariantChanged`
    // subscription can swap chrome by mutating one field + repainting.
    let mutable currentTheme = Chrome.currentTheme ()

    let renderInitializing () =
        this.Content <- Chrome.wrapWithHeader currentTheme (VirtualDom.create (
            TextBlock.create [
                TextBlock.text "Initializing dictionary…"
            ]))

    /// Production `runDialog` callback for the registration
    /// orchestration: constructs a `RegistrationDialogWindow` with the
    /// DI-resolved ports + logger, `ShowDialog`-s it modally against
    /// this `MainWindow`, and forwards the dialog's `OutcomeTask`
    /// (`Completed credential` on success, `Dismissed` on close
    /// without success). Dispatched on the UI thread because
    /// `Window.ShowDialog` requires the dispatcher.
    let runDialog () : Task<RegistrationOutcome> =
        task {
            let dialog =
                RegistrationDialogWindow(
                    registrationClient,
                    credentialStore,
                    dialogLogger
                )

            do! dialog.ShowDialog(this)
            return! dialog.OutcomeTask
        }

    // Forward-declared mutable holder so the Refresh callback can
    // call back into `renderStatusRow` after toggling state. F# 10
    // lacks `letrec`-for-callbacks of this shape; the mutable cell
    // is the idiomatic workaround.
    let mutable renderStatusRow : DictionarySource -> unit =
        fun _ -> ()

    let kickoffRefresh () =
        if refreshState = DictionaryStatusRow.Idle then
            refreshState <- DictionaryStatusRow.Refreshing
            match lastSource with
            | Some s -> renderStatusRow s
            | None -> ()

            let _ : Task = task {
                try
                    let! _ = service.RefreshAsync(CancellationToken.None)
                    ()
                with _ -> ()
                Dispatcher.UIThread.Post(fun () ->
                    refreshState <- DictionaryStatusRow.Idle
                    match lastSource with
                    | Some s -> renderStatusRow s
                    | None -> ())
            }
            ()

    // Re-register: wipe local install state (credential + install.guid
    // sidecar) so the next /register POST is treated server-side as a
    // fresh installation, then re-open the registration dialog.
    //
    // Issue #98: the prior Re-Register flow assumed
    // `stem-dictionaries-manager` v0.8.0 (#74) atomic rotation — a
    // fresh bootstrap token registered against an existing installation
    // overwrites the prior credential on success. That assumption holds
    // for a Live installation but breaks when an admin has revoked the
    // Installation row server-side: the server matches
    // `(clientApp, installGuid)`, finds `Status = Revoked`, and returns
    // `ExistingInstallationRevoked` (today conflated to HTTP 401, see
    // dictionaries-manager#85 for the status-code follow-up). Wiping
    // `install.guid` here makes the next POST carry a fresh GUID, and
    // the server treats the machine as a clean install.
    let kickoffReregister () =
        Dispatcher.UIThread.Post(fun () ->
            let _ : Task = task {
                try
                    do!
                        App.resetForReregister
                            credentialStore
                            descriptorProvider
                            CancellationToken.None

                    mainLogger.LogInformation(
                        "Re-register requested: wiped local install state; opening dialog."
                    )
                with ex ->
                    // The wipe failing is non-fatal — open the dialog
                    // anyway. The user can still get a fresh credential
                    // if the wipe partially succeeded; if it failed
                    // entirely the registration will surface the same
                    // failure mode the user was already seeing.
                    mainLogger.LogWarning(
                        ex,
                        "Failed to wipe local install state before Re-Register; opening dialog anyway."
                    )

                let! _ = runDialog ()
                ()
            }
            ())

    do
        renderStatusRow <- fun (source: DictionarySource) ->
            lastSource <- Some source
            this.Content <- Chrome.wrapWithHeader currentTheme (VirtualDom.create (
                DictionaryStatusRow.view
                    cacheFilePath
                    source
                    (clock.UtcNow())
                    refreshState
                    kickoffRefresh
                    kickoffReregister))

    do
        this.Title <- "Button Panel Tester"
        this.Width <- 600.0
        this.Height <- 400.0
        this.Icon <- Chrome.windowIcon currentTheme
        renderInitializing ()

        // SourceChanged → re-render on the UI thread. The event may
        // fire on a background thread (the service awaits IO inside
        // InitializeAsync); marshal back via the Avalonia dispatcher.
        service.SourceChanged.Add(fun s ->
            Dispatcher.UIThread.Post(fun () -> renderStatusRow s))

        // ActualThemeVariantChanged → swap title-bar / taskbar icon
        // and re-render the body so the brand-mark `Image.Source`
        // refreshes. The event fires on the UI thread (Avalonia
        // raises it as the OS theme change propagates through the
        // Win32 message pump), so we mutate `currentTheme` and
        // repaint inline. If `lastSource` hasn't arrived yet we
        // repaint the initializing view; otherwise we re-run
        // `renderStatusRow` with the last known source.
        match Application.Current with
        | null -> ()
        | app ->
            app.ActualThemeVariantChanged.Add(fun _ ->
                currentTheme <- Chrome.currentTheme ()
                this.Icon <- Chrome.windowIcon currentTheme
                match lastSource with
                | Some source -> renderStatusRow source
                | None -> renderInitializing ())

        // On first open:
        //   1. Kick off InitializeAsync so the status row paints
        //      populated within 1 s of the first frame (SC-001 /
        //      SC-002 / FR-004). The Task is fire-and-forget — the
        //      SourceChanged handler above is the success path.
        //   2. Run the registration ceremony if needed. Per FR-014
        //      the dialog blocks the main window until completion or
        //      dismissal; per FR-017 it never reopens once a
        //      credential is on disk. The orchestration helper
        //      lives in Services so T048 can exercise it through
        //      the in-memory test adapters.
        this.Opened.Add(fun _ ->
            (task {
                let initTask = service.InitializeAsync(CancellationToken.None)
                // Fire-and-forget dictionary warm-up per phase-7.md:
                // primes the Azure Free-tier worker so the technician's
                // first explicit Refresh click — or registration POST
                // on a fresh install — hits a warm process. Hits the
                // unauthenticated GET /health endpoint so unregistered
                // installs do not produce a spurious 401. Non-cancellation
                // outcomes are swallowed inside `DictionaryWarmUp.RunAsync`;
                // the production fetch path is the source of truth for
                // user-visible errors.
                let _ : Task<unit> = warmUp.RunAsync(CancellationToken.None)
                let! _ =
                    App.tryRegister
                        credentialStore
                        runDialog
                        CancellationToken.None
                let! _ = initTask
                ()
            }) |> ignore)

/// Avalonia `Application` for the GUI host. Owns the style theme and
/// hands control to `MainWindow` on framework startup.
///
/// Constructor takes the DI `IServiceProvider` built by
/// `Program.main` (T035) from `CompositionRoot.configure` (T033) so
/// the application doesn't construct its own DI graph — the
/// composition root is the single source of truth for adapter
/// bindings.
type App(services: IServiceProvider) =
    inherit Application()

    override this.Initialize() =
        this.Styles.Add(FluentTheme())

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            desktop.MainWindow <- MainWindow(services)
        | _ -> ()
        base.OnFrameworkInitializationCompleted()
