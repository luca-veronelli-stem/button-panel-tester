# Standard: CANCELLATION

> **Stability:** v1.2.0
> **Principle:** every async operation must be cancellable. The token flows from the caller through every awaited call to the underlying IO. Never substitute `CancellationToken.None` for a caller's token; never swallow `OperationCanceledException` to continue work.

## Method signatures

Every public async method takes `CancellationToken cancellationToken = default` as its **last** parameter:

```csharp
public Task<bool> InitializeAsync(
    LayerConfiguration? config = null,
    CancellationToken cancellationToken = default);
```

- Parameter name: `cancellationToken` (Microsoft canon — never `ct` in public signatures).
- Default value: `= default` on public APIs. Internal/private methods may make the parameter required when the call site is the one place a token always exists.
- Position: last (after any `out` parameters).

The same rule applies to `protected`, `internal`, and any non-trivial `private` async method that performs cancellable work. A `private static` helper that does no IO and never blocks doesn't need one.

## Propagation

The token reaches everything awaited inside:

```csharp
public async Task<LayerResult> ProcessAsync(byte[] data, CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();

    var encrypted = await _crypto.EncryptAsync(data, cancellationToken);
    await Task.Delay(_pacingDelay, cancellationToken);
    return await _lower.ProcessAsync(encrypted, cancellationToken);
}
```

Every one of `Task.Delay`, `Channel.Reader.ReadAsync`, `SemaphoreSlim.WaitAsync`, `Stream.ReadAsync`, `HttpClient.SendAsync`, `DbContext.SaveChangesAsync` takes a `CancellationToken` overload — use it.

### `CancellationToken.None` is a bypass

Passing `CancellationToken.None` when a caller's token is in scope is a smell. It means "I'm ignoring your cancellation request." Allowed only when the operation is genuinely caller-independent (a background sweeper, a fire-and-forget cleanup outliving the request) and the call site has a one-line comment saying so.

## Loops

Long-running loops check the token between iterations:

```csharp
foreach (var chunk in chunks)
{
    cancellationToken.ThrowIfCancellationRequested();
    await ProcessAsync(chunk, cancellationToken);
}
```

For tight inner loops where the call already awaits a token-aware operation, the inner `await` is the cancellation point — no extra `ThrowIfCancellationRequested` needed.

## Timeouts via linked CTS

To layer a timeout on top of the caller's token, link them:

```csharp
public async Task<byte[]?> ReadWithTimeoutAsync(TimeSpan timeout, CancellationToken cancellationToken)
{
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    cts.CancelAfter(timeout);

    try
    {
        return await _driver.ReadAsync(cts.Token);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        // internal timeout — not the caller's cancellation
        _logger?.LogDebug("Read timeout after {Timeout}", timeout);
        return null;
    }
}
```

The `when (!cancellationToken.IsCancellationRequested)` clause distinguishes "caller cancelled" (rethrow) from "we hit our own timeout" (handle). Always `using` the CTS — they hold a `Timer` and leak otherwise.

## Catching `OperationCanceledException`

- **Catch `OperationCanceledException`**, not `TaskCanceledException`. The latter is a TPL-internal subtype; catching the parent covers both and is the canonical choice.
- **Do not swallow** OCE to continue an operation. Cancellation is terminal — the caller asked for it.
- A `catch when (...)` filter that distinguishes timeout-vs-caller (above) is the only legitimate handling pattern.

```csharp
// ❌
catch (OperationCanceledException) { /* keep going */ }

// ❌
catch (OperationCanceledException ex) { throw new MyDomainException("...", ex); }
//   converting OCE breaks await-style cancellation

// ✅
catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
{
    return TimeoutResult();
}
```

## `IAsyncDisposable.DisposeAsync()` carve-out

```csharp
public async ValueTask DisposeAsync()  // no CT — the interface forbids one
```

The interface signature has no `CancellationToken`. Implementations must not block indefinitely. If cleanup is non-trivial, store a "disposing" CTS as a field and observe it during async work, or use a short internal timeout. Don't add a custom `DisposeAsync(CancellationToken)` overload as the public face — it confuses the `await using` contract.

## `ConfigureAwait`

See THREAD_SAFETY for the policy by archetype (B requires `.ConfigureAwait(false)`; A allows but doesn't require it). This standard does not repeat the rules.

## F#

`task { ... }` and `async { ... }` workflows accept a `CancellationToken` and propagate it through `let!` bindings:

```fsharp
type FrameDecoder(driver: IDriver) =
    member _.ReadAsync(cancellationToken: CancellationToken) =
        task {
            cancellationToken.ThrowIfCancellationRequested()
            let! frame = driver.ReadAsync cancellationToken
            return decode frame
        }
```

For F# `Async<'T>` workflows, run with `Async.StartAsTask(work, cancellationToken = ct)` from the C# boundary; inside the workflow, `Async` cancellation is automatic at every `let!` and `do!`.

## What this means in practice

- **New async method:** add `CancellationToken cancellationToken = default` last; propagate through every `await` inside.
- **Reviewing:** flag missing CT params, `CancellationToken.None` substitutions, `catch (OperationCanceledException) { ... }` without a `when` filter, swallowed cancellations, and `catch (TaskCanceledException)`.
- **Adoption:** when bumping a repo to v1.2.0, run a sweep for async methods missing CT and `CancellationToken.None` substitutions; add or document each.
