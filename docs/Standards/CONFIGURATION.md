# Standard: CONFIGURATION

> **Stability:** v1.2.0
> **Principle:** every configurable value has a default in code, validation bounds in code, and a typed home in code. Apps bind those types from `Microsoft.Extensions.Configuration` (`IOptions<T>` + `appsettings.json`); libraries take the same types directly from their consumer. Both patterns fail fast — invalid config never silently degrades to undefined behavior.

## The pattern: Constants → Configuration → Service

```
COMPILE-TIME                            RUNTIME
                                                         (in service)
{Component}Constants    {Component}Configuration       _component
┌──────────────────┐    ┌──────────────────────┐     ┌──────────────┐
│ DEFAULT_*        │───▶│ Property = DEFAULT_*  │───▶│ private field │
│ MAX_*  / MIN_*   │    │ Validate()            │    │ (immutable    │
│ XML-doc'd        │    │                       │    │  post-init)   │
└──────────────────┘    └──────────────────────┘     └──────────────┘
```

The same three-layer shape applies to libraries and apps; only the *delivery* of the Configuration object to the Service differs.

## Two delivery mechanisms — by archetype

### Archetype B (library) — typed config, no `IConfiguration`

A library never takes a dependency on `Microsoft.Extensions.Configuration`. The consumer constructs a `{Component}Configuration` and passes it explicitly:

```csharp
public sealed class FrameDecoder
{
    private readonly int _maxFrameSize;
    private readonly TimeSpan _timeout;

    public FrameDecoder(FrameDecoderConfiguration? config = null)
    {
        config ??= new FrameDecoderConfiguration();
        config.Validate();

        _maxFrameSize = config.MaxFrameSize;
        _timeout      = config.Timeout;
    }
}
```

The library is responsible for `Validate()`-ing what it received. Don't trust the consumer to have validated.

### Archetype A (app) — `IOptions<T>` + `appsettings.json`

Apps use `Microsoft.Extensions.Configuration` and the Options pattern. Bind in the composition root with fail-fast validation:

```csharp
services
    .AddOptions<TelemetryConfiguration>()
    .Bind(configuration.GetSection("Telemetry"))
    .ValidateDataAnnotations()
    .Validate(c => c.IsValid(), "Telemetry configuration is invalid")
    .ValidateOnStart();
```

```csharp
public sealed class TelemetryService(IOptions<TelemetryConfiguration> options)
{
    private readonly TelemetryConfiguration _config = options.Value;
    // _config is treated as immutable
}
```

Use `IOptionsSnapshot<T>` for per-request reload semantics, `IOptionsMonitor<T>` only if the service genuinely needs change tokens. Default to `IOptions<T>` — every other variant adds complexity.

`ValidateOnStart()` makes startup fail loudly when the JSON is wrong; without it, the first call to `.Value` is where you discover the bug, hours into a session.

## Constants class

```csharp
public static class FrameDecoderConstants
{
    // ─── Defaults ───────────────────────────────────────────────
    public const int DEFAULT_MAX_FRAME_SIZE = 1100;
    public const int DEFAULT_TIMEOUT_MS     = 1000;

    // ─── Validation bounds ──────────────────────────────────────
    public const int MIN_FRAME_SIZE = 64;
    public const int MAX_FRAME_SIZE = 65535;
    public const int MIN_TIMEOUT_MS = 100;
    public const int MAX_TIMEOUT_MS = 30000;
}
```

Two sections, separated by a comment band: defaults (for the Configuration class to point at) and bounds (for `Validate()` to check against). Every constant has an XML doc.

`SCREAMING_SNAKE_CASE` for `const` / `static readonly` is acceptable in protocol/interop contexts; `PascalCase` is also fine. Pick one per repo and stay consistent — the standard doesn't legislate the case style.

## Configuration class

```csharp
public sealed class FrameDecoderConfiguration
{
    [Range(FrameDecoderConstants.MIN_FRAME_SIZE, FrameDecoderConstants.MAX_FRAME_SIZE)]
    public int MaxFrameSize { get; set; } = FrameDecoderConstants.DEFAULT_MAX_FRAME_SIZE;

    [Range(FrameDecoderConstants.MIN_TIMEOUT_MS, FrameDecoderConstants.MAX_TIMEOUT_MS)]
    public int TimeoutMs { get; set; } = FrameDecoderConstants.DEFAULT_TIMEOUT_MS;

    public TimeSpan Timeout => TimeSpan.FromMilliseconds(TimeoutMs);

    /// <summary>Validates this configuration. Throws on out-of-range or invalid values.</summary>
    public void Validate()
    {
        if (MaxFrameSize < FrameDecoderConstants.MIN_FRAME_SIZE ||
            MaxFrameSize > FrameDecoderConstants.MAX_FRAME_SIZE)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxFrameSize),
                MaxFrameSize,
                $"Must be between {FrameDecoderConstants.MIN_FRAME_SIZE} " +
                $"and {FrameDecoderConstants.MAX_FRAME_SIZE}.");
        }

        if (TimeoutMs < FrameDecoderConstants.MIN_TIMEOUT_MS ||
            TimeoutMs > FrameDecoderConstants.MAX_TIMEOUT_MS)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TimeoutMs),
                TimeoutMs,
                $"Must be between {FrameDecoderConstants.MIN_TIMEOUT_MS} " +
                $"and {FrameDecoderConstants.MAX_TIMEOUT_MS}.");
        }
    }
}
```

Two validation strategies, used together:
- **`[Range]` / `[Required]` DataAnnotations** for simple per-field rules — picked up by `.ValidateDataAnnotations()` in the Options pattern.
- **`Validate()` method** for cross-field invariants and complex rules — called explicitly in libraries; wired via `.Validate(c => …)` in apps.

Both reference `*Constants` so the bounds aren't duplicated.

## Immutability post-init

Once a service reads its configuration, the values are immutable for that service's lifetime:

```csharp
private readonly int _maxFrameSize;  // copied from config; never reassigned
```

If the app needs reload semantics, switch the consuming service to `IOptionsMonitor<T>` and write the change-token plumbing explicitly. Mutating a stored `_config` field is forbidden.

## `appsettings.json` shape (apps only)

```json
{
  "Telemetry": {
    "MaxFrameSize": 2048,
    "TimeoutMs": 5000
  }
}
```

Section name matches the type name (less the `Configuration` suffix). One section per consuming service; no nested-everything godconfigs. Layer environment overrides via `appsettings.{Environment}.json` and user secrets in development; no secrets ever in `appsettings.json`.

## F#

F# records bind cleanly:

```fsharp
type FrameDecoderConfiguration = {
    [<Range(64, 65535)>] mutable MaxFrameSize : int
    [<Range(100, 30000)>] mutable TimeoutMs   : int
} with
    static member Default = {
        MaxFrameSize = FrameDecoderConstants.DEFAULT_MAX_FRAME_SIZE
        TimeoutMs    = FrameDecoderConstants.DEFAULT_TIMEOUT_MS
    }
    member this.Validate () =
        if this.MaxFrameSize < FrameDecoderConstants.MIN_FRAME_SIZE then
            invalidArg "MaxFrameSize" "out of range"
        // ...
```

`mutable` fields are required for `IConfiguration` to bind via reflection. For libraries that don't need binding, prefer immutable F# records and a separate `validate` function.

## What this means in practice

- **New service with configurable parameters:**
  - Define `{Component}Constants` with `DEFAULT_*` and `MAX_*`/`MIN_*` sections.
  - Define `{Component}Configuration` with properties defaulting from constants, plus a `Validate()` method.
  - In a **library**, the service ctor takes the Configuration and validates.
  - In an **app**, register via `AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`; the service depends on `IOptions<T>`.
- **Reviewing:** flag hardcoded magic numbers that should be `*Constants`, missing `Validate()` calls, configurations without bounds, mutated configuration fields after init, and apps that read `IConfiguration` directly instead of going through `IOptions<T>`.
- **Adoption:** repos with no configuration today (e.g. `spark-log-analyzer`) don't have to retrofit — adopt the pattern when the first configurable value appears.
