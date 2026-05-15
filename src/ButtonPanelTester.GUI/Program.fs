namespace Stem.ButtonPanelTester.GUI

open System
open Avalonia
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Stem.ButtonPanelTester.GUI.Composition

/// Entry point for the `ButtonPanelTester.GUI` executable. Builds
/// the configuration root from `appsettings.json` (production) +
/// `appsettings.Development.json` (dev override) + environment
/// variables, wires the MEDI service graph via
/// `CompositionRoot.configure` (T033), then starts Avalonia with
/// `App` as the `Application` and the classic-desktop lifetime
/// (the standard Avalonia entry point on Windows).
///
/// `STAThread` is required by Avalonia on Windows; the F#
/// `[<EntryPoint>]` attribute marks `main` as the assembly entry
/// point so the `<OutputType>WinExe</OutputType>` build target
/// finds it.
module Program =

    let private buildConfiguration () : IConfiguration =
        ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional = false, reloadOnChange = false)
            .AddJsonFile("appsettings.Development.json", optional = true, reloadOnChange = false)
            .Build() :> IConfiguration

    let private buildServices (config: IConfiguration) : IServiceProvider =
        let services = ServiceCollection()
        CompositionRoot.configure services config |> ignore
        services.BuildServiceProvider() :> IServiceProvider

    [<EntryPoint; STAThread>]
    let main (argv: string[]) : int =
        let config = buildConfiguration ()
        let provider = buildServices config
        AppBuilder
            .Configure<App>(fun () -> App(provider))
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(argv)
