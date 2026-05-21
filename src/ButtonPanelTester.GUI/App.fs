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

    let private brandSvgUri =
        "avares://ButtonPanelTester.GUI/Resources/branding/brand-marks/positive/stem-corporate.svg"

    // Multi-frame .ico — Windows picks the matching frame for each surface
    // (16/32/48/256 px). Necessary because Avalonia's `WindowIcon(Bitmap)`
    // overload feeds a single-resolution raster to the title-bar (~16 px)
    // and the taskbar (~32-48 px), and Skia's single-step downsample of
    // the agency 2134×2134 PNG produces visible aliasing on the small
    // surfaces. The same .ico drives the `.exe` shell icon via the
    // MSBuild `<ApplicationIcon>` property — one asset, two delivery
    // mechanisms (PE resource block + avares://).
    let private appIconUri =
        Uri("avares://ButtonPanelTester.GUI/Resources/branding/app-icons/stem-app-icon-positive.ico")

    // Process-wide cache: SvgSource.Load parses and rasterises the SVG
    // once; every SvgImage wrapper after that reuses the same picture.
    let private brandSvgSource = SvgSource.Load(brandSvgUri, null)

    let private brandMargin = Thickness(Spacing.lg, Spacing.sm, Spacing.lg, Spacing.sm)

    /// Inline brand mark for the window header. ~28px high matches the
    /// in-line `h2` type (`Typography.h2 = 20.0`) with breathing room
    /// either side; the `corporate` positive mark is selected via
    /// `Branding.division = Division.None`.
    let brandHeader () : Control =
        let svgImage = SvgImage(Source = brandSvgSource)
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
    let wrapWithHeader (body: Control) : Control =
        let panel = DockPanel()
        let header = brandHeader ()
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
    let windowIcon () : WindowIcon =
        use stream = AssetLoader.Open(appIconUri)
        WindowIcon(stream)

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
    let warmUp = services.GetRequiredService<DictionaryWarmUp>()
    let clock = services.GetRequiredService<IClock>()
    let dialogLogger =
        services.GetRequiredService<ILogger<RegistrationDialogWindow>>()
    let cacheFilePath =
        let local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        Path.Combine(local, "Stem.ButtonPanelTester", "dictionary.json")

    // Local mutable refresh state. T052 / FR-006: a click on the
    // Refresh button kicks off `IDictionaryService.RefreshAsync` and
    // flips `refreshState` to `Refreshing` so the status row's
    // in-flight UX (pulsing pill opacity, spinner glyph, ellipsis
    // headline) renders until the task resolves.
    let mutable refreshState = DictionaryStatusRow.Idle
    let mutable lastSource: DictionarySource option = None

    let renderInitializing () =
        this.Content <- Chrome.wrapWithHeader (VirtualDom.create (
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

    // Re-register: re-open the registration dialog without touching
    // the existing credential. `stem-dictionaries-manager` v0.8.0
    // (#74) handles the server-side atomic rotation: a fresh
    // bootstrap token registered against an existing installation
    // overwrites the prior credential on success, leaving the prior
    // value intact on failure.
    let kickoffReregister () =
        Dispatcher.UIThread.Post(fun () ->
            let _ : Task = task {
                let! _ = runDialog ()
                ()
            }
            ())

    do
        renderStatusRow <- fun (source: DictionarySource) ->
            lastSource <- Some source
            this.Content <- Chrome.wrapWithHeader (VirtualDom.create (
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
        this.Icon <- Chrome.windowIcon ()
        renderInitializing ()

        // SourceChanged → re-render on the UI thread. The event may
        // fire on a background thread (the service awaits IO inside
        // InitializeAsync); marshal back via the Avalonia dispatcher.
        service.SourceChanged.Add(fun s ->
            Dispatcher.UIThread.Post(fun () -> renderStatusRow s))

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
