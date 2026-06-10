namespace Stem.ButtonPanelTester.Tests.Fakes

open System
open System.Collections.Generic
open Microsoft.Extensions.Logging

/// One captured `ILogger.Log` call: its level and the structured key/value
/// pairs of the message template (so tests can read named fields like "Uuid"
/// without parsing the rendered string).
type LogEntry =
    { Level: LogLevel
      Values: Map<string, obj> }

/// Minimal recording `ILogger<'T>`: captures every `Log` call's level +
/// structured values for assertion. Shared by the CAN + discovery logging tests.
type RecordingLogger<'T>() =
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
