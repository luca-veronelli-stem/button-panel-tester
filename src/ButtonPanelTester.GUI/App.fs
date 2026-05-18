namespace Stem.ButtonPanelTester.GUI

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
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
    let dialogLogger =
        services.GetRequiredService<ILogger<RegistrationDialogWindow>>()
    let cacheFilePath =
        let local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        Path.Combine(local, "Stem.ButtonPanelTester", "dictionary.json")

    let renderInitializing () =
        this.Content <- VirtualDom.create (
            TextBlock.create [
                TextBlock.text "Initializing dictionary…"
            ])

    let renderStatusRow (source: DictionarySource) =
        this.Content <- VirtualDom.create (
            DictionaryStatusRow.view cacheFilePath source)

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

    do
        this.Title <- "Button Panel Tester"
        this.Width <- 600.0
        this.Height <- 400.0
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
