module Stem.ButtonPanelTester.Tests.Windows.Unit.Composition.LoggingLevelConfigurationTests

open System
open System.Collections.Generic
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Xunit
open Stem.ButtonPanelTester.GUI.Composition

// Pins #207: the level-config block (SetMinimumLevel + the two framework
// AddFilter calls + AddConfiguration) is extracted into the provider-free
// helper `CompositionRoot.configureLogLevels`, so the effective levels can be
// asserted against an in-memory `IConfiguration` WITHOUT standing up the full
// service graph or the NReco file provider. Each test builds an
// `ILoggerFactory` from the helper and probes `CreateLogger(category).IsEnabled`.

/// A hermetic logging provider whose loggers report enabled at every level and
/// discard messages. A `LoggerFactory` with NO providers always answers
/// `IsEnabled = false` regardless of the configured filter rules, so without a
/// provider these tests could never observe the level policy. Adding this
/// provider makes the configured filter rules the SOLE gate on `IsEnabled` —
/// exactly what is under test. It is not a sink under #207's "keep NReco file +
/// console + debug" constraint: it lives only in the test factory.
type private ProbeLoggerProvider() =
    let logger =
        { new ILogger with
            member _.BeginScope<'TState when 'TState: not null>(_state: 'TState) =
                { new IDisposable with
                    member _.Dispose() = () }

            member _.IsEnabled(_level: LogLevel) = true

            member _.Log<'TState>
                (
                    _level: LogLevel,
                    _eventId: EventId,
                    _state: 'TState,
                    _ex: exn,
                    _formatter: Func<'TState, exn, string>
                ) =
                () }

    interface ILoggerProvider with
        member _.CreateLogger(_categoryName: string) = logger
        member _.Dispose() = ()

/// Build an `ILoggerFactory` from the production level-config helper bound
/// against an in-memory `Logging:LogLevel:*` configuration. The only sink is the
/// hermetic `ProbeLoggerProvider`, so `IsEnabled` reflects exactly the filter
/// rules `configureLogLevels` installs.
let private factoryFor (pairs: (string * string) list) : ILoggerFactory =
    let initial: KeyValuePair<string, string> list =
        pairs |> List.map (fun (k, v) -> KeyValuePair<string, string>(k, v))

    let config =
        ConfigurationBuilder().AddInMemoryCollection(initial).Build() :> IConfiguration

    let services = ServiceCollection()

    services.AddLogging(fun b ->
        CompositionRoot.configureLogLevels config b |> ignore
        b.AddProvider(new ProbeLoggerProvider()) |> ignore)
    |> ignore

    services.BuildServiceProvider().GetRequiredService<ILoggerFactory>()

// Category names the documented appsettings comments point operators at.
let private discoveryCategory =
    "Stem.ButtonPanelTester.Services.Can.PanelDiscoveryService"

let private reassemblyCategory =
    "Stem.ButtonPanelTester.Infrastructure.Can.WhoIAmReassemblyObserver"

// (1) Quiet-by-default (AC#3): with NO Logging section the code defaults hold —
//     app default Information (Debug suppressed), Microsoft and System.Net.Http
//     held at Warning. An absent config section adds no rules.
[<Fact>]
let Defaults_NoLoggingSection_KeepsQuietByDefault () =
    let factory = factoryFor []
    let app = factory.CreateLogger("Anything")
    Assert.True(app.IsEnabled(LogLevel.Information))
    Assert.False(app.IsEnabled(LogLevel.Debug))
    Assert.False(factory.CreateLogger("Microsoft.Foo").IsEnabled(LogLevel.Information))
    Assert.True(factory.CreateLogger("Microsoft.Foo").IsEnabled(LogLevel.Warning))
    Assert.False(factory.CreateLogger("System.Net.Http.HttpClient.X").IsEnabled(LogLevel.Information))

// (2) Per-category override (AC#4): raising one category to Debug raises ONLY
//     that category; unrelated categories keep the Information default and the
//     framework categories keep their Warning floor.
[<Fact>]
let PerCategoryOverride_RaisesOnlyThatCategory () =
    let factory = factoryFor [ $"Logging:LogLevel:{discoveryCategory}", "Debug" ]
    Assert.True(factory.CreateLogger(discoveryCategory).IsEnabled(LogLevel.Debug))
    Assert.False(factory.CreateLogger("Some.Other.Category").IsEnabled(LogLevel.Debug))
    Assert.False(factory.CreateLogger("Microsoft.Foo").IsEnabled(LogLevel.Information))

// (3) Trace override (AC#4): the reassembly observer's drop-axis Trace lines can
//     be turned on per deployment without touching unrelated categories.
[<Fact>]
let TraceOverride_RaisesOnlyReassemblyObserver () =
    let factory = factoryFor [ $"Logging:LogLevel:{reassemblyCategory}", "Trace" ]
    Assert.True(factory.CreateLogger(reassemblyCategory).IsEnabled(LogLevel.Trace))
    Assert.False(factory.CreateLogger("Some.Other.Category").IsEnabled(LogLevel.Trace))

// (4) Config-last precedence (the load-bearing design): an operator rule for the
//     SAME category the code filters (`Microsoft` → Warning) ties on specificity
//     and WINS because AddConfiguration is added last. Proves the override path,
//     not just additive new categories.
[<Fact>]
let ConfigRule_WinsOnTie_AgainstFrameworkFilter () =
    let factory = factoryFor [ "Logging:LogLevel:Microsoft", "Information" ]
    Assert.True(factory.CreateLogger("Microsoft.Foo").IsEnabled(LogLevel.Information))

// (5) Config Default raises the global floor (AC#1/AC#4): a `Default` rule from
//     config governs uncategorised loggers, overriding the code's Information
//     minimum because config rules are added last.
[<Fact>]
let ConfigDefault_RaisesGlobalFloor () =
    let factory = factoryFor [ "Logging:LogLevel:Default", "Debug" ]
    Assert.True(factory.CreateLogger("Anything").IsEnabled(LogLevel.Debug))
