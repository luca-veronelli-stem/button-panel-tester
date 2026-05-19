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

    /// Environment selector for the optional `appsettings.<env>.json`
    /// overlay. Reads `DOTNET_ENVIRONMENT`, falling back to
    /// `ASPNETCORE_ENVIRONMENT` for compatibility with hosts that
    /// set the ASP.NET name, then defaults to `Production`. Empty
    /// strings are treated as unset.
    let private environmentName () : string =
        let pick (name: string) =
            match Environment.GetEnvironmentVariable(name) with
            | null -> None
            | value ->
                let trimmed = value.Trim()
                if String.IsNullOrEmpty(trimmed) then None else Some trimmed

        pick "DOTNET_ENVIRONMENT"
        |> Option.orElseWith (fun () -> pick "ASPNETCORE_ENVIRONMENT")
        |> Option.defaultValue "Production"

    /// Builds the configuration root from `appsettings.json` plus an
    /// optional `appsettings.<env>.json` overlay (loaded only when the
    /// host sets `DOTNET_ENVIRONMENT` / `ASPNETCORE_ENVIRONMENT`).
    /// Without this gate, a supplier-shipped build that carries a
    /// stray `appsettings.Development.json` from the developer's
    /// machine would silently override the production
    /// `Dictionary:BaseUrl`.
    let private buildConfiguration () : IConfiguration =
        let env = environmentName ()
        let envFile = sprintf "appsettings.%s.json" env

        ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional = false, reloadOnChange = false)
            .AddJsonFile(envFile, optional = true, reloadOnChange = false)
            .Build()
        :> IConfiguration

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
