module Stem.ButtonPanelTester.Tests.Unit.Can.CanLinkLoggingTests

open System
open System.Collections.Generic
open System.Threading
open Microsoft.Extensions.Logging
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Services.Can
open Stem.ButtonPanelTester.Tests.Fakes
open Stem.ButtonPanelTester.Tests.Fakes.Can

/// Unit tests for the pure `CanLinkLogging` helpers (#148) plus one
/// integration-style test that drives `CanLinkService` through a real
/// transition and asserts the service emits exactly one structured log
/// entry carrying the `State` field at the expected level.
///
/// The pure helpers (`levelFor` / `stateName` / `severityName` /
/// `detailString` / `sinceOf`) are exercised against representative
/// states of the four-family `CanLinkState` DU.

// --- fixtures ---

let private since =
    DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero)

let private adapter: AdapterIdentification =
    { ChannelName = "PCAN-USB (1)"
      DeviceId = "0x01"
      BaudrateBps = 250_000 }

// --- levelFor ---

[<Fact>]
let LevelFor_InitializingConnectedDisconnected_AreInformation () =
    Assert.Equal(LogLevel.Information, CanLinkLogging.levelFor Initializing)
    Assert.Equal(LogLevel.Information, CanLinkLogging.levelFor (Connected(adapter, since)))
    Assert.Equal(LogLevel.Information, CanLinkLogging.levelFor (Disconnected(NoAdapterPresent, since)))

[<Fact>]
let LevelFor_ErrorRecoverable_IsWarning () =
    Assert.Equal(LogLevel.Warning, CanLinkLogging.levelFor (Error(Recoverable "bus-off", since)))

[<Fact>]
let LevelFor_ErrorFatal_IsError () =
    Assert.Equal(LogLevel.Error, CanLinkLogging.levelFor (Error(Fatal "driver missing", since)))

// --- stateName ---

[<Fact>]
let StateName_RendersFamilyAndDiscriminator () =
    Assert.Equal("Initializing", CanLinkLogging.stateName Initializing)
    Assert.Equal("Connected", CanLinkLogging.stateName (Connected(adapter, since)))
    Assert.Equal("Disconnected.NoAdapterPresent", CanLinkLogging.stateName (Disconnected(NoAdapterPresent, since)))
    Assert.Equal("Disconnected.LinkNotYetOpened", CanLinkLogging.stateName (Disconnected(LinkNotYetOpened, since)))
    Assert.Equal("Disconnected.MidSessionUnplug", CanLinkLogging.stateName (Disconnected(MidSessionUnplug, since)))
    Assert.Equal("Disconnected.ReconnectPending", CanLinkLogging.stateName (Disconnected(ReconnectPending, since)))
    Assert.Equal("Error.Recoverable", CanLinkLogging.stateName (Error(Recoverable "x", since)))
    Assert.Equal("Error.Fatal", CanLinkLogging.stateName (Error(Fatal "x", since)))

// --- severityName ---

[<Fact>]
let SeverityName_NonError_IsDash () =
    Assert.Equal("-", CanLinkLogging.severityName Initializing)
    Assert.Equal("-", CanLinkLogging.severityName (Connected(adapter, since)))
    Assert.Equal("-", CanLinkLogging.severityName (Disconnected(MidSessionUnplug, since)))

[<Fact>]
let SeverityName_Error_IsClassificationName () =
    Assert.Equal("Recoverable", CanLinkLogging.severityName (Error(Recoverable "x", since)))
    Assert.Equal("Fatal", CanLinkLogging.severityName (Error(Fatal "x", since)))

// --- detailString ---

[<Fact>]
let DetailString_NonError_IsEmpty () =
    Assert.Equal("", CanLinkLogging.detailString Initializing)
    Assert.Equal("", CanLinkLogging.detailString (Connected(adapter, since)))
    Assert.Equal("", CanLinkLogging.detailString (Disconnected(NoAdapterPresent, since)))
    Assert.Equal("", CanLinkLogging.detailString (Disconnected(MidSessionUnplug, since)))

[<Fact>]
let DetailString_Error_IsClassificationDetail () =
    Assert.Equal("bus-off detected", CanLinkLogging.detailString (Error(Recoverable "bus-off detected", since)))
    Assert.Equal("driver missing", CanLinkLogging.detailString (Error(Fatal "driver missing", since)))

// --- sinceOf ---

[<Fact>]
let SinceOf_Initializing_IsNone () =
    Assert.Equal<DateTimeOffset option>(None, CanLinkLogging.sinceOf Initializing)

[<Fact>]
let SinceOf_TimestampedStates_AreSome () =
    Assert.Equal<DateTimeOffset option>(Some since, CanLinkLogging.sinceOf (Connected(adapter, since)))
    Assert.Equal<DateTimeOffset option>(Some since, CanLinkLogging.sinceOf (Disconnected(MidSessionUnplug, since)))
    Assert.Equal<DateTimeOffset option>(Some since, CanLinkLogging.sinceOf (Error(Recoverable "x", since)))
    Assert.Equal<DateTimeOffset option>(Some since, CanLinkLogging.sinceOf (Error(Fatal "x", since)))

// --- recording logger ---

/// One captured `ILogger.Log` call: its level and the structured
/// key/value pairs of the message template (so tests can read the
/// `State` field without parsing the rendered string).
type private LogEntry =
    { Level: LogLevel
      Values: Map<string, obj> }

/// Minimal recording `ILogger<'T>`. The net10.0 Tests project has no
/// recording logger (existing CAN tests use `NullLogger`), so this one
/// captures every `Log` call's level + structured values for assertion.
type private RecordingLogger<'T>() =
    let entries = ResizeArray<LogEntry>()

    member _.Entries = entries

    interface ILogger<'T>

    interface ILogger with
        member _.BeginScope<'TState when 'TState: not null>(_state: 'TState) =
            { new IDisposable with
                member _.Dispose() = () }

        member _.IsEnabled(_level: LogLevel) = true

        member _.Log<'TState>
            (
                level: LogLevel,
                _eventId: EventId,
                state: 'TState,
                ex: exn,
                formatter: Func<'TState, exn, string>
            ) =
            let _ = formatter.Invoke(state, ex)

            let values =
                match box state with
                | :? IReadOnlyList<KeyValuePair<string, obj>> as kvs ->
                    kvs |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
                | _ -> Map.empty

            entries.Add { Level = level; Values = values }

// --- integration-style: exactly one State-bearing log per transition ---

[<Fact>]
let CanLinkService_ScriptedConnected_EmitsExactlyOneStateLogAtInformation () =
    let script =
        seq { (Connected(adapter, since), TimeSpan.Zero) }

    let link = InMemoryCanLink(script)
    let clock = FrozenClock(since)
    let logger = RecordingLogger<CanLinkService>()

    let service = CanLinkService(link, clock, logger)

    (service :> ICanLinkService).InitializeAsync(CancellationToken.None)
        .GetAwaiter().GetResult()

    // The transition log carries the {State} field; InitializeAsync's
    // own lifecycle log does not, so filtering on the key isolates the
    // per-transition emission.
    let transitionLogs =
        logger.Entries
        |> Seq.filter (fun e -> e.Values.ContainsKey "State")
        |> List.ofSeq

    Assert.Equal(1, transitionLogs.Length)
    Assert.Equal(LogLevel.Information, transitionLogs.[0].Level)
    Assert.Equal(box "Connected", transitionLogs.[0].Values.["State"])
