# Standard: THREAD_SAFETY

> **Stability:** v1.2.0
> **Principle:** the cheapest concurrency is no concurrency — prefer immutability, `Channel<T>`, or actor patterns to manual locks. When shared mutable state is genuinely needed, pick the primitive that fits and apply it consistently. Sync-over-async is banned outside the composition root.

## Decision order

1. **Can this state be immutable?** Records, `readonly` fields, value snapshots — done.
2. **Is this a producer-consumer pipeline?** Use `System.Threading.Channels` (`Channel<T>`).
3. **Is this an actor / single-writer?** F# `MailboxProcessor`, or a serialized-task queue in C#.
4. **Genuinely shared mutable state?** Pick from the primitive table below.

The table is consulted *after* options 1–3 have been ruled out — most state isn't actually shared and the table doesn't apply.

## Primitive table

| Shape of shared state | Primitive |
| --- | --- |
| Counter / accumulator (long, int) | `Interlocked.Increment` / `Interlocked.Add`; read with `Interlocked.Read` (long) or `Volatile.Read` (int, ref types) |
| Single bool / flag | `Volatile.Read` / `Volatile.Write`; `Interlocked.CompareExchange` for compare-and-swap |
| Time field (DateTime / TimeSpan) | Store as `long Ticks`; `Interlocked.Exchange` to set, `Interlocked.Read` to load. (`Volatile.Read<T>` doesn't accept struct types.) |
| Map / set | `ConcurrentDictionary<TKey, TValue>` |
| FIFO queue | `ConcurrentQueue<T>` |
| Producer-consumer pipeline | `Channel<T>` (`Channel.CreateBounded` / `CreateUnbounded`) |
| Multi-field atomic update | `System.Threading.Lock` (.NET 10) |
| Read-heavy with rare writes | `ReaderWriterLockSlim` |
| Async-aware mutex | `SemaphoreSlim` with `WaitAsync(CancellationToken)` |

## Auto-properties and shared state

`{ get; set; }` is fine for state that isn't concurrently read and written by different threads — i.e. the overwhelming majority of fields. The Volatile/Interlocked discipline kicks in only for state that *is* shared. When in doubt: if multiple threads can touch a field simultaneously, demote the auto-property to an explicit field with the right primitive and expose a method for mutation.

```csharp
// Shared counter — explicit field, Interlocked
private long _packets;
public long Packets => Interlocked.Read(ref _packets);
public void RecordPacket() => Interlocked.Increment(ref _packets);

// Not shared — auto-property is fine
public int LastBatchSize { get; private set; }
```

## `lock` and the .NET 10 `Lock` class

For multi-field atomic updates, prefer `System.Threading.Lock` over `lock(object)`:

```csharp
private readonly Lock _gate = new();

public void Update(int hopCount, double reliability)
{
    lock (_gate)
    {
        _hopCount    = hopCount;
        _reliability = reliability;
    }
}
```

The `Lock` class is a dedicated mutex — it can't be locked accidentally on `this` or a string intern. `lock(this)`, `lock("constant")`, and `lock(typeof(T))` are banned regardless of the lock type.

Locks held across an `await` are also banned: `lock` enters a thread-affine region, and the continuation may run on a different thread. Use `SemaphoreSlim` for async-aware mutual exclusion:

```csharp
private readonly SemaphoreSlim _gate = new(1, 1);

public async Task UpdateAsync(CancellationToken ct)
{
    await _gate.WaitAsync(ct);
    try { /* ... */ }
    finally { _gate.Release(); }
}
```

`WaitAsync` always takes a `CancellationToken` — see CANCELLATION.

## `ConfigureAwait` — by archetype

- **Archetype B (library):** every `await` in non-test code uses `.ConfigureAwait(false)`. Library code shouldn't capture a sync context that doesn't belong to it.
- **Archetype A (app):** modern .NET apps (Avalonia, console, web) usually have no sync context that matters; `ConfigureAwait` is allowed but not required. Don't sweep one in / out — match the file's neighbors.

Roslyn analyzer **CA2007** flags missing `ConfigureAwait` and is appropriate to enable on B projects.

## Sync-over-async — banned

```csharp
var result = SomeAsync().Result;                   // ❌
SomeAsync().Wait();                                // ❌
SomeAsync().GetAwaiter().GetResult();              // ❌
```

These deadlock on captured sync contexts and starve the thread pool. **Carve-out:** the composition root (`Program.Main` returning `int`, top-level startup code) and event-handler entry points where the host signature precludes async (legacy WinForms `event` handlers etc.). Everywhere else, propagate `async` / `Task` up.

CA2012 catches `.Result` on `ValueTask`; the rest is on review.

## Snapshot for complex reads

Don't expose a reference to mutable state. Either return an immutable snapshot or copy the data under a lock:

```csharp
public RouteSnapshot GetSnapshot()
{
    lock (_gate)
    {
        return new RouteSnapshot(_hopCount, _reliability, new DateTime(_lastUpdateTicks));
    }
}

public readonly record struct RouteSnapshot(int HopCount, double Reliability, DateTime LastUpdate);
```

## Forbidden patterns

```csharp
private int _counter;
public void Tick() => _counter++;          // ❌ not atomic

public int Count { get; set; }              // ❌ shared mutable auto-property
Count++;

private DateTime _lastSeen;                 // ❌ Volatile.Read can't help; use long Ticks
```

## F#

F#'s default is immutability; most thread-safety problems don't materialize. When mutable state is genuinely needed:

- Prefer `MailboxProcessor<'T>` for actor-style serialization — every message is processed on the same logical thread, eliminating the need for locks.
- For producer-consumer, use `Channel<'T>` (the C# type works fine in F#).
- For pure async work, use `Async<'T>` workflows or `task { ... }` with `CancellationToken` propagation.
- When `ref cell` or mutable fields appear, the same primitives as C# apply: `Interlocked`, `Volatile`, `Lock`. Reach for them only after the immutable / actor option has been ruled out.

```fsharp
let scanner = MailboxProcessor.Start(fun inbox ->
    let rec loop state = async {
        let! msg = inbox.Receive()
        let state' = handle state msg
        return! loop state'
    }
    loop initialState)
```

## What this means in practice

- **New shared field:** ask "can this be immutable?" first. If not, pick from the table; document with a one-line comment if non-obvious.
- **New async code:** propagate `CancellationToken`; never `.Result`/`.Wait()`/`.GetAwaiter().GetResult()`; in B, `.ConfigureAwait(false)`.
- **Reviewing:** flag sync-over-async, `lock(this)`, locks across `await`, `Volatile.Read` on a struct field, and shared auto-properties without explicit synchronization.
