namespace Stem.ButtonPanelTester.GUI

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Platform
open Avalonia.Styling
open Avalonia.Svg.Skia
open Avalonia.Themes.Fluent
open Avalonia.Threading
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Hosts
open Avalonia.FuncUI.Types
open Avalonia.FuncUI.VirtualDom
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services
open Stem.ButtonPanelTester.Services.Can
open Stem.ButtonPanelTester.Services.Dictionary
open Stem.ButtonPanelTester.Services.Registration
open Stem.ButtonPanelTester.GUI.Can
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
    let canLinkService = services.GetRequiredService<ICanLinkService>()
    let panelDiscovery = services.GetRequiredService<IPanelDiscoveryService>()
    let baptismService = services.GetRequiredService<IBaptismService>()
    let buttonPressTestService = services.GetRequiredService<IButtonPressTestService>()
    let buttonStateObserver = services.GetRequiredService<IButtonStateObserver>()
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

    // Host-driven dictionary render state (#179). Starts at
    // `Initializing` (the pre-first-event placeholder); the
    // `SourceChanged` handler advances it to `Ready` on the success
    // path, and the `Opened` handler flips it to `Unavailable` on the
    // catastrophic-init dead-path (`NoDictionaryAvailable`) that
    // `SourceChanged` never covers.
    let mutable dictionaryRender = DictionaryStatusRow.Initializing

    /// Latest `CanLinkState` observed via
    /// `ICanLinkService.LinkStateChanged`. Initial value is
    /// `Initializing` (the DU's zero-information case) — the row
    /// renders an "Initializing…" headline until either the user
    /// clicks Reconnect or `canLinkService.InitializeAsync` resolves
    /// the first concrete transition (FR-001 ordering: this happens
    /// only after dictionary boot completes; see the `Opened`
    /// handler below).
    let mutable lastCanState: CanLinkState = Initializing

    /// Latest Panels-on-bus snapshot observed via
    /// `IPanelDiscoveryService.PanelsOnBusChanged`. Starts empty (the
    /// pre-first-observation value); the subscription below advances it
    /// on every observe / prune / link-loss clear and re-renders.
    let mutable lastPanelsOnBus: PanelsOnBus = PanelsOnBus.empty

    /// The Panels-on-bus row selected for baptism (spec-004 E1, FR-002).
    /// Starts unselected; a row click sets it (see `onSelectPanel`). Cleared
    /// when the selected row prunes out of the snapshot (via
    /// `PanelsOnBusView.pruneSelection` in the `PanelsOnBusChanged` handler)
    /// so a stale selection never reaches `Baptism.baptizeEnablement`. Read
    /// by `renderCombined` to drive the selected-row highlight.
    let mutable selectedPanel: PanelUuid option = None

    /// The variant the technician picked in the baptize surface (spec-004 E2,
    /// FR-002). `None` until a picker button is clicked (see `onSelectVariant`);
    /// feeds `BaptismView.baptizeEnabled` and rides the in-flight attempt config.
    let mutable selectedVariant: MarketingVariant option = None

    /// Latest `IBaptismService.StateChanged` FSM state (spec-004 E2). Starts
    /// `Idle`; the `StateChanged` subscription advances it (claim / await /
    /// assign / terminal) and re-renders so the surface tracks progress + the
    /// terminal outcome rendering.
    let mutable lastBaptismState: BaptismState = Idle

    /// The `(variant, uuid)` of the in-flight / last baptism attempt (spec-004
    /// E2). Set at Baptize-press time so `BaptismView.view` can render the
    /// terminal outcome against the variant + panel that were actually attempted.
    let mutable lastAttempt: (MarketingVariant * PanelUuid) option = None

    /// Latest FR-007 "claim did not take" warning uuid (spec-004 E2), observed
    /// via `IBaptismService.WarningRaised`. Cleared at the start of a new
    /// attempt; rendered by `BaptismView.view` into the operator message.
    let mutable lastBaptismWarning: PanelUuid option = None

    /// The latest reset-to-virgin outcome (spec-004 E3, FR-010). `None` until a
    /// reset completes; set by `onReset` to the `ResetOutcome` returned through
    /// the confirmation seam and rendered by `BaptismView.view` into the honest
    /// success / failure line.
    let mutable lastResetOutcome: ResetOutcome option = None

    /// Latest `IButtonPressTestService.StateChanged` FSM state (spec-005 Phase F).
    /// Starts `Idle` (QUALIFIED — `ButtonPressTestState.Idle` collides with
    /// `BaptismState.Idle`); the `StateChanged` subscription advances it (prompt /
    /// score / terminal) and re-renders so the button-press surface tracks the
    /// prompt + result grid (FR-004/005/011).
    let mutable lastButtonPressState: ButtonPressTestState = ButtonPressTestState.Idle

    /// The `ButtonSchema` of the in-flight / last button-press run (spec-005
    /// Phase F). Set at Run-press time from the heartbeating panel's variant so
    /// `ButtonPressTestView.view` renders the result grid against the schema the
    /// run was actually started with (FR-004/016).
    let mutable lastButtonPressSchema: ButtonSchema option = None

    /// The instant the last button-state heartbeat was observed (spec-005 Phase
    /// F, fix #270), via `IButtonStateObserver`. `None` until the first frame; the
    /// subscription below sets it. A baptized panel is silent on WHO_I_AM, so this
    /// heartbeat — NOT discovery — is the presence signal: the button-press
    /// surface keys observability off its recency (`ButtonPressTest.observableWindow`).
    let mutable lastButtonStateObservedAt: DateTimeOffset option = None

    /// The `MarketingVariant` the last observed heartbeat decoded from its directed
    /// CAN ID (spec-005 Phase F, fix #270). `None` until the first frame; drives
    /// the prompt schema + the baptized/variant enablement conjunct, auto-targeting
    /// the single heartbeating panel under test.
    let mutable lastButtonStateVariant: MarketingVariant option = None

    /// Whether the surface last rendered the panel as observable — so the 1 Hz
    /// idle tick repaints the button-press surface only when heartbeat recency
    /// actually flips (a steady idle state does not repaint every second).
    let mutable lastButtonStateShownObservable = false

    /// Forensic run-correlation key for a button-press run (fix #270): a baptized
    /// panel is silent on WHO_I_AM, so its UUID is unavailable in the button-press
    /// path. With one panel under test at a time the run scope keys off this
    /// sentinel instead of a per-panel UUID; the variant (the real identity the
    /// heartbeat carries) drives the schema.
    let buttonPressRunKey = PanelUuid(0u, 0u, 0u)

    /// `true` when a button-state heartbeat has arrived within
    /// `ButtonPressTest.observableWindow` (spec-005 Phase F, fix #270): the
    /// recency-based observability signal that replaces the discovery list-membership
    /// check. A heartbeat only ever decodes to a known `Marketing` variant (the
    /// observer drops the rest), so an observable heartbeat IS a baptized panel.
    let buttonStateObservable () : bool =
        match lastButtonStateObservedAt with
        | Some seenAt -> clock.UtcNow() - seenAt <= ButtonPressTest.observableWindow
        | None -> false

    // Active Avalonia theme. Initial value read once at construction
    // from `Application.Current.ActualThemeVariant`; every render
    // reads from this cell so a future `ActualThemeVariantChanged`
    // subscription can swap chrome by mutating one field + repainting.
    let mutable currentTheme = Chrome.currentTheme ()

    // No separate `renderInitializing` slot: `renderCombined` below
    // renders the dictionary row from `dictionaryRender`, which starts
    // at `Initializing` ("Initializing dictionary…"), so the CAN status
    // row appears alongside the dictionary placeholder from the first
    // paint (FR-016: the two rows are observable independently).

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

    // Forward-declared mutable holder so the Refresh / Reconnect /
    // CAN-state-changed paths can call back into the combined
    // renderer after toggling state. F# 10 lacks
    // `letrec`-for-callbacks of this shape; the mutable cell is the
    // idiomatic workaround.
    let mutable renderCombined: unit -> unit = fun () -> ()

    let kickoffRefresh () =
        if refreshState = DictionaryStatusRow.Idle then
            refreshState <- DictionaryStatusRow.Refreshing
            renderCombined ()

            let _ : Task = task {
                try
                    let! _ = service.RefreshAsync(CancellationToken.None)
                    ()
                with _ -> ()
                Dispatcher.UIThread.Post(fun () ->
                    refreshState <- DictionaryStatusRow.Idle
                    renderCombined ())
            }
            ()

    /// Reconnect-button callback wired into `CanStatusRow.view`.
    /// Fire-and-forget per FR-003 — `ICanLinkService.ReconnectAsync`
    /// resolves on its own and any state change surfaces via the
    /// `LinkStateChanged` subscription below. Exceptions are
    /// logged + swallowed so a transient failure doesn't crash the
    /// UI; the user can click again.
    let kickoffReconnect () =
        let _ : Task =
            task {
                try
                    do! canLinkService.ReconnectAsync(CancellationToken.None)
                with
                | :? OperationCanceledException -> ()
                | ex ->
                    mainLogger.LogWarning(
                        ex,
                        "ICanLinkService.ReconnectAsync raised; ignored at the UI layer"
                    )
            }

        ()

    /// Row-selection callback wired into `PanelsOnBusView.view` (spec-004 E1,
    /// FR-002). Records the clicked panel as the baptism selection and
    /// re-renders so the row highlight repaints; the GUI decides nothing — the
    /// selection only feeds `Baptism.baptizeEnablement`.
    let onSelectPanel (uuid: PanelUuid) =
        selectedPanel <- Some uuid
        renderCombined ()

    /// Variant-picker callback wired into `BaptismView.view` (spec-004 E2,
    /// FR-002). Records the picked variant and re-renders so the picker
    /// highlight + the Baptize-button enablement repaint; the GUI decides
    /// nothing — the variant only feeds `BaptismView.baptizeEnabled` and the
    /// attempt config.
    let onSelectVariant (v: MarketingVariant) =
        selectedVariant <- Some v
        renderCombined ()

    /// Baptize-button callback wired into `BaptismView.view` (spec-004 E2,
    /// FR-002). Fires ONE attempt against the selected panel for the picked
    /// variant — fire-and-forget, mirroring `kickoffReconnect`: the outcome
    /// surfaces via the `StateChanged` subscription, so the task body only has
    /// to swallow cancellation and log any other fault. No confirmation dialog
    /// (FR-009). A no-op when no panel is selected.
    let onBaptize (v: MarketingVariant) =
        match selectedPanel with
        | None -> ()
        | Some uuid ->
            lastAttempt <- Some(v, uuid)
            lastBaptismWarning <- None

            // Enter the running state synchronously on THIS UI turn so the
            // surface is modal the instant Baptize is pressed — the picker
            // and Baptize disable now, not on the later dispatcher turn when
            // the StateChanged post lands. This closes the re-entrancy window
            // (a second press can no longer reach BaptizeAsync) and clears any
            // stale prior terminal outcome immediately. Mirrors BaptismService
            // entering Idle -> ClaimSent atomically (`fst Baptism.start`); the
            // service's own StateChanged(ClaimSent) is an idempotent repaint.
            lastBaptismState <- fst Baptism.start
            renderCombined ()

            let _ : Task =
                task {
                    try
                        let! _ = baptismService.BaptizeAsync(uuid, v, CancellationToken.None)
                        ()
                    with
                    | :? OperationCanceledException -> ()
                    | ex ->
                        mainLogger.LogWarning(
                            ex,
                            "BaptizeAsync raised; ignored at the UI layer"
                        )
                }

            ()

    /// The FR-009 confirmation SEAM for reset-to-virgin (spec-004 E3): shows a
    /// modal confirmation dialog and resolves to the technician's choice. Mirrors
    /// `runDialog` — a small imperative `Window` backed by a
    /// `TaskCompletionSource<bool>`: the "Reset to virgin" button resolves
    /// `true`, "Cancel" and any other close resolve `false` (closing = decline).
    /// The resolved bool feeds `BaptismView.runReset` → `IBaptismService.ResetAsync`;
    /// the GUI decides nothing, it only relays the confirmation.
    let confirmReset () : Task<bool> =
        let tcs = TaskCompletionSource<bool>()

        let message =
            TextBlock(
                Text = BaptismView.resetConfirmationMessage,
                TextWrapping = TextWrapping.Wrap)

        let confirmButton = Button(Content = "Reset to virgin")
        let cancelButton = Button(Content = "Cancel")

        let buttonRow = StackPanel(Orientation = Orientation.Horizontal, Spacing = 8.0)
        buttonRow.Children.Add(confirmButton)
        buttonRow.Children.Add(cancelButton)

        let panel = StackPanel(Margin = Thickness 20.0, Spacing = 12.0)
        panel.Children.Add(message)
        panel.Children.Add(buttonRow)

        let dialog =
            Window(
                Title = "Reset to virgin?",
                Width = 420.0,
                Height = 200.0,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = panel)

        confirmButton.Click.Add(fun _ ->
            tcs.TrySetResult true |> ignore
            dialog.Close())

        cancelButton.Click.Add(fun _ ->
            tcs.TrySetResult false |> ignore
            dialog.Close())

        // Any close without an explicit choice (window chrome, ESC) declines.
        dialog.Closed.Add(fun _ -> tcs.TrySetResult false |> ignore)

        dialog.ShowDialog(this) |> ignore
        tcs.Task

    /// Reset-button callback wired into `BaptismView.view` (spec-004 E3, FR-008 /
    /// FR-009 / FR-010). Fire-and-forget, mirroring `kickoffReconnect` / `onBaptize`:
    /// drives the confirmation seam (`BaptismView.runReset confirmReset …`), then
    /// posts the resolved `ResetOutcome` onto the UI thread so the honest
    /// success / failure line repaints. Cancellation is swallowed; any other
    /// fault is logged and ignored at the UI layer.
    let onReset () =
        let _ : Task =
            task {
                try
                    let! outcome =
                        BaptismView.runReset confirmReset baptismService CancellationToken.None

                    Dispatcher.UIThread.Post(fun () ->
                        lastResetOutcome <- Some outcome
                        renderCombined ())
                with
                | :? OperationCanceledException -> ()
                | ex ->
                    mainLogger.LogWarning(
                        ex,
                        "ResetAsync raised; ignored at the UI layer"
                    )
            }

        ()

    /// Run-button callback wired into `ButtonPressTestView.view` (spec-005 Phase F,
    /// FR-002/004/005; observability re-keyed in #270, accept rule re-keyed to the
    /// packet senderId in #296). Auto-targets the single heartbeating panel: its
    /// `MarketingVariant` (decoded from the heartbeat packet's senderId,
    /// `lastButtonStateVariant`) resolves its `ButtonSchema`, then starts
    /// ONE modal run via `IButtonPressTestService.RunAsync` — fire-and-forget,
    /// mirroring `onBaptize`: the prompt / score / terminal grid surface via the
    /// `StateChanged` subscription, so the task body only swallows cancellation and
    /// logs any other fault. A no-op when no baptized panel is heartbeating (the Run
    /// control is disabled then, SC-008). No UUID selection — the run scope keys off
    /// `buttonPressRunKey`.
    let onRunButtonPressTest () =
        let resolved =
            if buttonStateObservable () then
                lastButtonStateVariant |> Option.map ButtonSchema.forVariant
            else
                None

        match resolved with
        | None -> ()
        | Some sch ->
            lastButtonPressSchema <- Some sch

            // Enter the running state synchronously on THIS UI turn so the Run
            // control disables the instant it is pressed (modal) — mirrors the
            // baptize re-entrancy close (`fst Baptism.start`). The service's own
            // StateChanged(Prompting 0) is an idempotent repaint.
            lastButtonPressState <- ButtonPressTest.start sch (clock.UtcNow() + ButtonPressTest.testBudget)
            renderCombined ()

            let _ : Task =
                task {
                    try
                        let! _ = buttonPressTestService.RunAsync(buttonPressRunKey, sch, CancellationToken.None)
                        ()
                    with
                    | :? OperationCanceledException -> ()
                    | ex ->
                        mainLogger.LogWarning(
                            ex,
                            "ButtonPressTestService.RunAsync raised; ignored at the UI layer"
                        )
                }

            ()

    /// FR-009 Retry callback (spec-005 Phase F): re-arms the current button with
    /// a fresh countdown via `IButtonPressTestService.Retry` (synchronous, a
    /// no-op when no run is in flight). The re-armed `Prompting` state surfaces
    /// via the `StateChanged` subscription, which repaints the countdown.
    let onRetryButtonPress () = buttonPressTestService.Retry()

    /// FR-009 Skip callback (spec-005 Phase F): records the current button
    /// `Skipped` and advances via `IButtonPressTestService.Skip` (synchronous, a
    /// no-op when no run is in flight). The advance surfaces via `StateChanged`.
    let onSkipButtonPress () = buttonPressTestService.Skip()

    /// FR-003 Re-run callback (spec-005 Phase F): restarts the last run's panel +
    /// schema from a cleared grid via `IButtonPressTestService.RerunAsync` —
    /// fire-and-forget, mirroring `onRunButtonPressTest`. The optimistic fresh
    /// `start` clears the grid + disables the controls on this UI turn; the
    /// terminal grid surfaces via the `StateChanged` subscription. A no-op when no
    /// prior run's schema is known (Re-run is only offered after a run).
    let onRerunButtonPress () =
        match lastButtonPressSchema with
        | None -> ()
        | Some sch ->
            lastButtonPressState <- ButtonPressTest.start sch (clock.UtcNow() + ButtonPressTest.testBudget)
            renderCombined ()

            let _ : Task =
                task {
                    try
                        let! _ = buttonPressTestService.RerunAsync(CancellationToken.None)
                        ()
                    with
                    | :? OperationCanceledException -> ()
                    | ex ->
                        mainLogger.LogWarning(
                            ex,
                            "ButtonPressTestService.RerunAsync raised; ignored at the UI layer"
                        )
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
        renderCombined <- fun () ->
            // Dictionary row (top) — placeholder until SourceChanged
            // fires for the first time. CAN row (middle) — always
            // present, starts in `Initializing`. Panels-on-bus list
            // (bottom) — rendered from `IPanelDiscoveryService`
            // (#197 / spec-003): the third child re-renders on every
            // `PanelsOnBusChanged` emission (observe / prune / link-loss
            // clear) and uses `lastCanState` for the FR-006 empty-state
            // explainer (link-down vs link-up-but-idle).
            let dictionaryRowView : IView =
                DictionaryStatusRow.dictionaryView
                    cacheFilePath
                    dictionaryRender
                    (clock.UtcNow())
                    refreshState
                    kickoffRefresh
                    kickoffReregister

            // Baptize surface (spec-004 E2, FR-002): the enablement verdict is
            // computed in Core from the live link + announcing count + selection
            // (`announcingCount` ranges over the announcing panels — the whole
            // Panels-on-bus map, which only holds announcing panels). The GUI
            // renders that verdict + the latest FSM state / attempt / warning; it
            // decides nothing.
            let baptizeEnablement =
                Baptism.baptizeEnablement lastCanState (Map.count lastPanelsOnBus) selectedPanel

            // Reset surface (spec-004 E3, FR-008): the reset enablement verdict
            // is computed in Core from the live link + announcing count — no
            // selection conjunct (reset is a list-anchor-free broadcast). The
            // GUI renders that verdict + the last `ResetOutcome`; it decides
            // nothing.
            let resetEnablement =
                Baptism.resetEnablement lastCanState (Map.count lastPanelsOnBus)

            // Button-press test surface (spec-005 Phase F, FR-001; observability
            // re-keyed in fix #270): the enablement verdict is computed in Core
            // from the live link + the heartbeating panel's baptized status + its
            // observability — keyed off the button-state HEARTBEAT, not discovery.
            // A baptized panel is silent on WHO_I_AM, so a button-state frame seen
            // within `ButtonPressTest.observableWindow` IS the evidence a baptized
            // panel of a known Marketing variant is present and observable; the
            // tool auto-targets that single heartbeating panel. The schema feeding
            // the decal prompts (FR-004) comes from the observed variant; during a
            // run it is the schema the run was started with (`lastButtonPressSchema`).
            let testObservable = buttonStateObservable ()

            // A heartbeat's senderId only ever decodes to a Marketing variant
            // (the observer drops WHO_I_AM on its command, the virgin sentinel on
            // its address, and non-marketing senders on the senderId — #296), so
            // an observable heartbeat carrying a variant IS a baptized panel.
            let testSelectedBaptized = testObservable && Option.isSome lastButtonStateVariant

            let buttonPressEnablement =
                ButtonPressTest.testEnablement lastCanState testSelectedBaptized testObservable

            let buttonPressSchema =
                match lastButtonPressState with
                | ButtonPressTestState.Idle ->
                    if testObservable then
                        lastButtonStateVariant |> Option.map ButtonSchema.forVariant
                    else
                        None
                | Prompting _
                | ButtonPressTestState.Completed _
                | Interrupted _ -> lastButtonPressSchema

            // Record the observability the surface is rendering so the 1 Hz idle
            // tick repaints only when heartbeat recency flips (fix #270).
            lastButtonStateShownObservable <- testObservable

            let combinedView : IView =
                StackPanel.create [
                    StackPanel.orientation Orientation.Vertical
                    StackPanel.spacing 8.0
                    StackPanel.children [
                        dictionaryRowView
                        CanStatusRow.view lastCanState kickoffReconnect
                        PanelsOnBusView.view lastPanelsOnBus lastCanState selectedPanel onSelectPanel currentTheme
                        BaptismView.view
                            baptizeEnablement
                            resetEnablement
                            lastBaptismState
                            selectedVariant
                            lastAttempt
                            lastBaptismWarning
                            lastResetOutcome
                            onSelectVariant
                            onBaptize
                            onReset
                            currentTheme
                        ButtonPressTestView.view
                            buttonPressEnablement
                            lastButtonPressState
                            buttonPressSchema
                            (clock.UtcNow())
                            // The transient Unexpected-press notice (FR-008) has
                            // no signal on the locked IButtonPressTestService
                            // surface (RecordUnexpected leaves the FSM state
                            // unchanged, so StateChanged never fires for it); the
                            // view capability is proven by the Headless suite,
                            // and surfacing it in-app needs an UnexpectedObserved
                            // service event (out of Phase F scope).
                            None
                            onRunButtonPressTest
                            onRetryButtonPress
                            onSkipButtonPress
                            onRerunButtonPress
                            currentTheme
                    ]
                ]
                :> IView

            this.Content <- Chrome.wrapWithHeader
                currentTheme
                (VirtualDom.create combinedView)

    do
        this.Title <- "Button Panel Tester"
        this.Width <- 600.0
        this.Height <- 400.0
        this.Icon <- Chrome.windowIcon currentTheme
        renderCombined ()

        // SourceChanged → re-render on the UI thread. The event may
        // fire on a background thread (the service awaits IO inside
        // InitializeAsync); marshal back via the Avalonia dispatcher.
        service.SourceChanged.Add(fun s ->
            Dispatcher.UIThread.Post(fun () ->
                dictionaryRender <- DictionaryStatusRow.Ready s
                renderCombined ()))

        // CAN LinkStateChanged → re-render on the UI thread. The
        // service's subject fires on whatever thread the link emits
        // from (the vendored PCANManager monitor task hop chain in
        // production; a synchronous test caller for `InMemoryCanLink`);
        // marshal back to the UI thread the same way as SourceChanged.
        let _ : IDisposable =
            canLinkService.LinkStateChanged
            |> Observable.subscribe (fun state ->
                Dispatcher.UIThread.Post(fun () ->
                    lastCanState <- state
                    renderCombined ()))

        // PanelsOnBusChanged → re-render on the UI thread. The discovery
        // service fires from the CAN read-loop thread (production) or a
        // synchronous test caller; marshal back to the UI thread the same
        // way as LinkStateChanged so the third slot repaints safely.
        let _ : IDisposable =
            panelDiscovery.PanelsOnBusChanged
            |> Observable.subscribe (fun panels ->
                Dispatcher.UIThread.Post(fun () ->
                    lastPanelsOnBus <- panels
                    // Clear the selection if its row pruned out of the snapshot
                    // (spec-004 E1, FR-002) so a stale selection never reaches
                    // `Baptism.baptizeEnablement` before the re-render.
                    selectedPanel <- PanelsOnBusView.pruneSelection panels selectedPanel
                    renderCombined ()))

        // Baptism StateChanged → re-render on the UI thread. The service's
        // subject fires from whatever thread drove the transition (the write
        // continuation / deadline timer in production, a synchronous test
        // caller); marshal back to the UI thread the same way as
        // PanelsOnBusChanged so the baptize surface repaints its progress +
        // terminal-outcome rendering safely (spec-004 E2).
        let _ : IDisposable =
            baptismService.StateChanged
            |> Observable.subscribe (fun st ->
                Dispatcher.UIThread.Post(fun () ->
                    lastBaptismState <- st
                    renderCombined ()))

        // Baptism WarningRaised → re-render on the UI thread (FR-007). Fires the
        // claimed uuid when a just-claimed panel re-announces within the
        // post-success window; marshal back like StateChanged so the
        // claim-did-not-take warning repaints (spec-004 E2).
        let _ : IDisposable =
            baptismService.WarningRaised
            |> Observable.subscribe (fun uuid ->
                Dispatcher.UIThread.Post(fun () ->
                    lastBaptismWarning <- Some uuid
                    renderCombined ()))

        // Button-press StateChanged → re-render on the UI thread (spec-005 Phase
        // F). The service's subject fires from whatever thread drove the
        // transition (a button-state / link / discovery callback, or the deadline
        // timer); marshal back to the UI thread the same way as the baptism
        // StateChanged so the button-press surface repaints its prompt + result
        // grid safely (FR-004/005/011).
        let _ : IDisposable =
            buttonPressTestService.StateChanged
            |> Observable.subscribe (fun st ->
                Dispatcher.UIThread.Post(fun () ->
                    lastButtonPressState <- st
                    renderCombined ()))

        // Button-state heartbeat → track recency + variant on the UI thread
        // (spec-005 Phase F, #270/#296). The observer fires from the vendored read
        // thread (or a synchronous test caller); marshal to the UI thread like the
        // other CAN subscriptions. A button-state heartbeat (variant from its
        // packet senderId, #296) IS the presence + variant signal — the re-key
        // replacement for discovery. Repaint only on a
        // VISIBLE change (the panel newly observable, or its variant changing) so a
        // steady ~5 Hz heartbeat does not rebuild the surface every frame; the
        // observable->stale decay is handled by the 1 Hz tick below.
        let _ : IDisposable =
            buttonStateObserver.ButtonStateObserved
            |> Observable.subscribe (fun observation ->
                Dispatcher.UIThread.Post(fun () ->
                    let rising = not (buttonStateObservable ())
                    let variantChanged = lastButtonStateVariant <> Some observation.Variant
                    lastButtonStateObservedAt <- Some(clock.UtcNow())
                    lastButtonStateVariant <- Some observation.Variant

                    if rising || variantChanged then
                        renderCombined ()))

        // Live per-button countdown (FR-005): the pure view renders the remaining
        // whole seconds from the `Prompting` deadline against the `now` the host
        // threads in, but `StateChanged` only fires on an actual transition — not
        // every second. A 1 Hz UI-thread tick re-renders while a run is
        // `Prompting` so the countdown visibly decrements; a guarded no-op in
        // every other state.
        let countdownTimer = DispatcherTimer(Interval = TimeSpan.FromSeconds 1.0)

        countdownTimer.Tick.Add(fun _ ->
            match lastButtonPressState with
            | Prompting _ -> renderCombined ()
            | ButtonPressTestState.Idle
            | ButtonPressTestState.Completed _
            | Interrupted _ ->
                // Reflect the heartbeat recency decay (fix #270): when the panel
                // falls silent the surface must flip to unavailable (and back when
                // it resumes). Repaint only when observability actually changed so a
                // steady idle state does not rebuild the surface every second.
                if buttonStateObservable () <> lastButtonStateShownObservable then
                    renderCombined ())

        countdownTimer.Start()

        ()

        // ActualThemeVariantChanged → swap title-bar / taskbar icon
        // and re-render the body so the brand-mark `Image.Source`
        // refreshes. The event fires on the UI thread (Avalonia
        // raises it as the OS theme change propagates through the
        // Win32 message pump), so we mutate `currentTheme` and
        // repaint inline. `renderCombined` repaints whatever
        // `dictionaryRender` currently holds (Initializing / Ready /
        // Unavailable).
        match Application.Current with
        | null -> ()
        | app ->
            app.ActualThemeVariantChanged.Add(fun _ ->
                currentTheme <- Chrome.currentTheme ()
                this.Icon <- Chrome.windowIcon currentTheme
                renderCombined ())

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

                // #179 catastrophic-init dead-path: observe the init
                // result the boot sequence otherwise discards. The
                // `NoDictionaryAvailable` arm never fires `SourceChanged`,
                // so without this the dictionary row would sit on
                // "Initializing dictionary…" forever. Await the SAME task
                // `runBootSequence` awaits below — Tasks are
                // multi-await-safe, and FR-001 dict→CAN ordering is
                // preserved because the boot sequence still gates CAN.Open
                // on it — and flip the row to the terminal `Unavailable`
                // view on the dead-path. The `Updated` arm is a no-op: the
                // `SourceChanged` handler already drove the row to `Ready`.
                // Fire-and-forget so a blocked registration dialog or a
                // slow CAN open never delays the failure signal; the
                // continuation marshals back onto the UI thread.
                let _ : Task =
                    task {
                        let! initResult = initTask

                        match DictionaryStatusRow.renderForInitResult initResult with
                        | Some render ->
                            // Field-diagnostic trail for the one failure
                            // that most needs it — live + cache + seed all
                            // failed. Warning, not Error: the app stays
                            // usable (CAN still runs, dictionary degraded),
                            // mirroring the sibling CAN-init LogWarning in
                            // `runBootSequence`. Logged off the mapping
                            // result so `renderForInitResult` stays the
                            // single authority.
                            match render with
                            | DictionaryStatusRow.Unavailable reason ->
                                mainLogger.LogWarning(
                                    "Dictionary unavailable after init — live, cache and seed all failed: {Reason}",
                                    reason)
                            | _ -> ()

                            Dispatcher.UIThread.Post(fun () ->
                                dictionaryRender <- render
                                renderCombined ())
                        | None -> ()
                    }

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

                // FR-001 / SC-001: open the CAN link only AFTER
                // dictionary boot has completed. The ordering is a
                // domain invariant, so it lives in
                // `Services/BootSequence.fs` and the GUI delegates.
                // Failures surface via `LinkStateChanged` (the row
                // chip flips to red / grey); `BootSequence`
                // swallows the exception so this `Opened` handler
                // is not torn down on a transient adapter problem.
                let! _ =
                    BootSequence.runBootSequence
                        initTask
                        canLinkService
                        mainLogger
                        CancellationToken.None

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
