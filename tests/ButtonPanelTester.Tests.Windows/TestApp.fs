namespace Stem.ButtonPanelTester.Tests.Windows

open Avalonia
open Avalonia.Headless

/// Minimal Avalonia application for `Avalonia.Headless.XUnit` test
/// runs in this project. Discovered by the
/// `[<assembly: AvaloniaTestApplication>]` declaration below; every
/// `[<AvaloniaFact>]` in the assembly executes against this app.
///
/// No FluentTheme styles are wired in — the headless harness drives
/// the logical tree only, and absent styles avoid the per-test cost
/// of resolving theme resources for assertions that never touch
/// painted pixels.
type TestAppBuilder() =
    static member BuildAvaloniaApp() : AppBuilder =
        AppBuilder
            .Configure<Application>()
            .UseHeadless(AvaloniaHeadlessPlatformOptions())

[<assembly: AvaloniaTestApplication(typeof<TestAppBuilder>)>]
do ()
