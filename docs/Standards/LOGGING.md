# Standard: LOGGING

> **Stability:** v1.2.0
> **Principle:** structured logging via `Microsoft.Extensions.Logging.Abstractions` is the only sanctioned channel. Libraries take a logger optionally; apps wire one in the composition root and use it everywhere. Templates + named parameters always — never string interpolation, never `Console.WriteLine` in production paths.

## Reference

`Microsoft.Extensions.Logging.Abstractions` (>= 10.0.0). Pinned via `Directory.Packages.props`.

## Required vs optional — by archetype

### Archetype B — library

A library never forces a logging stack on its consumer. The logger is **optional**:

```csharp
public sealed class FrameDecoder(ILogger<FrameDecoder>? logger = null)
{
    private readonly ILogger<FrameDecoder>? _logger = logger;
}
```

Always invoke through the null-conditional: `_logger?.LogDebug(...)`. The cost of an absent logger is one null check.

### Archetype A — app

An app's composition root wires a concrete provider (Serilog, Console, Debug, etc.). The logger is **required**:

```csharp
public sealed class TelemetryService(ILogger<TelemetryService> logger)
{
    private readonly ILogger<TelemetryService> _logger = logger;
}
```

If `_logger?.` patterns appear in app code, that's a smell — wire the logger or make the type pure.

## Category = type by default

`ILogger<ThisType>`. The category gives ops the filter knob: `MyApp.NetworkLayer:Debug`, `MyApp.FrameDecoder:Warning`.

A tight cluster of helpers within a single feature may share the **parent's** logger (passed in as `ILogger`, not `ILogger<Helper>`) when each helper would otherwise produce a useless one-call category. Don't reach for this until the multi-category log filter actually feels noisy.

## Templates and parameters — always

```csharp
_logger?.LogDebug(
    "Frame received from {SenderId:X8}, length={Length}",
    senderId,
    length);
```

Never string interpolation in the message:

```csharp
_logger?.LogDebug($"Frame received from {senderId:X8}, length={length}");  // ❌
```

The Roslyn analyzer **CA2254** catches this; keep it on (`AnalysisLevel = latest-recommended` per VISIBILITY).

Parameter naming conventions:

| Kind | Form | Example |
| --- | --- | --- |
| ID (hex) | `{Id:X8}` / `{Id:X4}` | `12AB34CD` |
| Size / count | `{Length}`, `{Count}` | `256`, `5` |
| Time | `{LatencyMs}`, `{DurationMs}` | `15.3` |
| State | `{State}` (enum) | `Established` |
| CRC / checksum | `{Crc:X4}` | `A1B2` |

## Banned in production code

- `Console.WriteLine`, `Console.Error.WriteLine`
- `System.Diagnostics.Debug.WriteLine`
- `System.Diagnostics.Trace.WriteLine`

**Carve-outs:** CLI entry points (a console app's `Program.Main` writing user-facing output is fine); test code; dev-only diagnostic scripts.

## Levels

| Level | Use | Example |
| --- | --- | --- |
| `Trace` | Byte-level dev detail | Frame dumps, raw buffer contents |
| `Debug` | Normal internal operations | "Created 5 chunks", "route selected" |
| `Information` | Lifecycle / business events | "Session established", "device connected" |
| `Warning` | Recoverable errors | CRC mismatch + retry, transient timeout |
| `Error` | Operation failed, component still alive | Handler exception, terminal timeout |
| `Critical` | Component unusable | Driver crash, corrupt state |

Pass the exception as the first argument for `Error` and `Critical`:

```csharp
_logger?.LogError(ex, "Upstream failed for {SenderId:X8}", senderId);
```

Never log secrets, credentials, or PII at `Information` or above. For sensitive payloads stripped at the producer side, `Trace`/`Debug` only.

## Where logging does **not** belong

- **Pure functions** (CRC calculator, hash, ID derivation) — no logger field.
- **DTOs / records / value objects** — no logger field.
- **Stateless validators on a hot path** — no logger field. Caller logs the outcome.

A logger field is a smell on these: it suggests the type took on a side-effecting role.

## Correlation with `BeginScope`

For protocol/network/IO code, scope IDs (`sessionId`, `deviceId`, `requestId`) make logs joinable across handlers:

```csharp
using (_logger?.BeginScope(new Dictionary<string, object> {
    ["SessionId"] = sessionId,
    ["DeviceId"]  = deviceId,
}))
{
    // every log inside this block carries the scope
}
```

Use scopes when the same logical operation crosses several log sites. Skip them for one-shot logs.

## Allocation-free hot paths

`[LoggerMessage]` source generators turn a logging call into a precompiled `LoggerMessage.Define`:

```csharp
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Frame {Length} from {SenderId:X8}")]
    public static partial void FrameReceived(ILogger logger, int length, uint senderId);
}
```

Reach for this only when profiling shows allocations on a real hot path — for ordinary logging it's ceremony with no payoff.

## F#

The same abstractions apply; F# uses the C# extension methods directly:

```fsharp
type FrameDecoder(logger: ILogger<FrameDecoder> option) =
    let log = logger |> Option.toObj  // null when absent
    member _.Decode(raw: byte[]) =
        log |> Option.ofObj |> Option.iter (fun l ->
            l.LogDebug("Frame {Length}", raw.Length))
        // ...
```

Or, more idiomatically, a thin wrapper on top of `ILogger`:

```fsharp
let inline logDebug (logger: ILogger) (template: string) ([<ParamArray>] args: obj[]) =
    logger.LogDebug(template, args)
```

Apps require the logger non-optionally (matching archetype A); libraries take `ILogger<'T> option`.

## What this means in practice

- **New B type that needs logging:** `ILogger<TThis>? = null` in the primary constructor; null-conditional everywhere.
- **New A type that needs logging:** `ILogger<TThis>` (required); call directly.
- **Adoption:** when a repo bumps to v1.2.0 it doesn't have to retro-add logging everywhere. Add it where it earns its keep — protocol/IO/diagnostic paths first.
- **Reviewing:** flag string interpolation in log messages, `Console.WriteLine` in production code, and logger fields on pure types.
