# Standard: EVENTARGS

> **Stability:** v1.2.0
> **Principle:** an event payload is a self-describing type — never a primitive, never an unnamed tuple. Two shapes are equally idiomatic: a `sealed class : EventArgs` (heritage / public surface) or a plain `record` (modern / records-first code). Pick one per event, never both.

## Two valid shapes

```csharp
// Heritage shape — public surface, legacy interop, anything that walks the
// classic .NET event pattern (raised by sender, consumed via `+=`).
public sealed class ConnectionStateChangedEventArgs(
    ConnectionState previous,
    ConnectionState current,
    string? reason = null) : EventArgs
{
    public ConnectionState Previous { get; } = previous;
    public ConnectionState Current  { get; } = current;
    public string?         Reason   { get; } = reason;
}

// Modern shape — internal events, F#-friendly payloads, anything where
// records-as-payload is already the surrounding style.
public sealed record DeviceDiscovered(uint DeviceId, BleAddress Address);
```

Both are exposed via the same standard event type:

```csharp
public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
public event EventHandler<DeviceDiscovered>?                DeviceDiscovered;
```

The `where T : EventArgs` constraint on `EventHandler<T>` was lifted in .NET 4.0 — both forms are first-class.

## Banned: primitive and unnamed payloads

```csharp
public event EventHandler<bool>?        Ready;            // ❌ bool means what?
public event EventHandler<int>?         BuffersTimedOut;  // ❌ count? id? bytes?
public event EventHandler<string>?      Status;           // ❌ message? state?
public event EventHandler<byte[]>?      DataReceived;     // ❌ payload only — no provenance
public event EventHandler<(int, int)>?  Progress;         // ❌ unnamed tuple
```

Promote each to a record or `EventArgs` subclass with named members. A one-line record (`public sealed record DataReceived(byte[] Data, DateTime Timestamp)`) costs nothing and earns its keep at every callsite.

## Naming

- Heritage shape: `{Event}EventArgs` (`SessionExpiredEventArgs`).
- Modern shape: `{Event}` past-tense (`SessionExpired`, `DeviceDiscovered`, `RouteAdded`). The `EventArgs` suffix is reserved for the heritage shape — using it on a record misleads readers.

## Immutability and validation

- All members readonly (`{ get; }` on classes, positional record params).
- Initialize everything in the constructor / primary record params.
- Validate cross-member invariants in the constructor (`throw new ArgumentException(...)`); never throw from a property getter.
- `sealed` is the default. Inheritance on event payloads is rarely useful and complicates equality.

## Timestamp — when to include

Add `DateTime Timestamp { get; } = DateTime.UtcNow` (or a positional `DateTime Timestamp` on a record) when the event is correlatable with logs, traces, or other events: protocol exchanges, IO completions, hardware notifications, diagnostic events. Skip it for plain domain notifications (`LanguageChanged`, `ViewSelected`) — it's noise there.

When you do include it, always UTC. Never `DateTime.Now`.

## XML docs

- `<summary>` on the type.
- `<summary>` on each public member (positional record params get `<param>` on the type).
- `<exception>` on the constructor when invariants are validated.

## F# events

The same shape rule applies. F# idiom is `IEvent<_,_>` (or `Event<_>`) with a record payload — never a primitive, never a tuple of unnamed fields:

```fsharp
type DeviceDiscovered = { DeviceId: uint32; Address: BleAddress; Timestamp: DateTime }

type DeviceScanner() =
    let discovered = Event<DeviceDiscovered>()
    [<CLIEvent>]
    member _.DeviceDiscovered = discovered.Publish
    member internal this.Raise(ev) = discovered.Trigger(ev)
```

For interop with C# consumers exposing `EventHandler<T>`, the C# heritage shape on the boundary is fine; internal F# events stay records.

## Internal high-frequency events

A `readonly record struct` is allowed for **internal** events on a hot path (>1000/s) where the GC allocation of a class genuinely matters. Don't reach for it pre-emptively: measure first. Public surface stays `sealed class : EventArgs` or `sealed record` (reference type).

## What this means in practice

- **New event:** named record by default; `sealed class : EventArgs` when the event is on a public API that already uses the heritage shape.
- **Existing primitive payload:** lift to a record or named EventArgs the next time you touch the surrounding code. Don't refactor in isolation.
- **Reviewing:** flag any new `EventHandler<primitive>` or `EventHandler<(...)>`.
