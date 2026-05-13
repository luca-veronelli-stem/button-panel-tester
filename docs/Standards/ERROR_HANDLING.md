# Standard: ERROR_HANDLING

> **Stability:** v1.2.0
> **Principle:** exceptions for the unexpected; values for the expected. A method's failure that's part of its normal contract should be visible at the call site as a return value, not as a `try`/`catch` ceremony. The .NET BCL covers most parameter and state errors; reach for a custom exception type only when a domain has at least three related failure modes.

## Choosing the mechanism

```
Is the failure a routine outcome that callers will branch on every time?
├── yes — it's part of the contract → return a Result type
│           (or a Try-pattern for trivially-shaped failures)
└── no  — it indicates a bug, an impossible state, or a parameter the caller
           shouldn't have passed → throw an exception
```

Three shapes, in order of weight:

### Try-pattern — lightest

```csharp
public static bool TryParseFrame(byte[] raw, out Frame frame);
```

Use when the failure has no payload — caller cares only "did it work?". Matches the `int.TryParse` idiom every .NET developer recognizes.

### Result type — when the failure carries information

A discriminated success/failure value:

```csharp
public readonly record struct ParseResult(
    bool             IsSuccess,
    Frame?           Value,
    ParseError?      Error);
```

Or a generic shape per repo (`Result<TValue, TError>`, `LayerResult`, `OperationOutcome` — name and exact members are a per-repo choice). The standard does **not** ship a shared `Result<T,E>` type — each repo defines its own and stays consistent within itself.

Use when:
- A failure path is normal (CRC mismatch, parse failure, validation).
- The caller needs structured information about the failure (an error code, a payload, a message).
- The function is called frequently and exception-throw cost would matter.

### Exception — when the caller shouldn't routinely catch

```csharp
throw new InvalidOperationException("Cannot send on a disconnected channel.");
```

Use for:
- Bugs (unreachable branches, broken invariants).
- Parameter violations the caller could have prevented.
- Unrecoverable state errors.
- IO/system failures the caller will let bubble.

## BCL exceptions — first stop

Most parameter and state errors are already covered by the BCL. Use the .NET 6+ throw helpers:

```csharp
ArgumentNullException.ThrowIfNull(buffer);
ArgumentNullException.ThrowIfNullOrEmpty(name);
ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
ObjectDisposedException.ThrowIf(_disposed, this);
```

Direct `throw` for the rest:

| Exception | Use for |
| --- | --- |
| `ArgumentNullException` | parameter is null and the contract forbids it |
| `ArgumentException` | parameter is non-null but invalid (e.g. wrong shape) |
| `ArgumentOutOfRangeException` | numeric / index parameter outside the allowed range |
| `InvalidOperationException` | object's state doesn't allow this call right now |
| `ObjectDisposedException` | this instance has been disposed |
| `NotSupportedException` | this overload / mode is not supported |
| `NotImplementedException` | placeholder during construction; never in shipped code |

**Never throw** `Exception`, `SystemException`, or `ApplicationException` directly — they carry no information.

## Custom exception types

Add a custom exception type when:
- The same domain has **three or more** related failure modes that callers may want to distinguish, **or**
- A specific failure carries structured payload that doesn't fit a BCL exception.

Otherwise, a BCL exception with a clear message is enough.

When you do add a hierarchy, keep it small:

```csharp
public class ChannelException : Exception
{
    public ChannelException(string message) : base(message) { }
    public ChannelException(string message, Exception inner) : base(message, inner) { }
}

public sealed class ChannelTimeoutException(string message) : ChannelException(message);
public sealed class ConnectionLostException(string message)  : ChannelException(message);
```

Modern .NET (.NET 8+) doesn't need the `SerializationInfo`/`StreamingContext` constructor — binary serialization is obsolete. The two-constructor shape above (`(string)` and `(string, Exception)`) is sufficient.

## `catch` discipline

- **Catch specific types first**, the catch-all last.
- **`catch (Exception)` is allowed only as a final guard** with `LogCritical` and a deliberate next step (rethrow, return a failure result, terminate). Never swallow silently.
- **`throw;` rethrows** preserving the stack. **`throw ex;` resets** the stack — never use it.
- **`OperationCanceledException`** handling — see CANCELLATION.

```csharp
try
{
    await _driver.SendAsync(data, cancellationToken);
}
catch (ChannelTimeoutException ex)
{
    return SendResult.Timeout(ex.Message);
}
catch (ChannelException ex)
{
    _logger?.LogWarning(ex, "Channel error sending {Length} bytes", data.Length);
    return SendResult.ChannelError(ex.Message);
}
catch (Exception ex)
{
    _logger?.LogCritical(ex, "Unexpected error sending {Length} bytes", data.Length);
    throw;
}
```

For dispose paths, swallow narrowly:

```csharp
try { await _resource.DisposeAsync(); }
catch (ObjectDisposedException) { /* already gone — fine */ }
catch (Exception ex) { _logger?.LogWarning(ex, "Cleanup failed"); }
```

## Error codes

The standard does **not** mandate per-domain error-code prefixes. If a repo wants them (for log aggregation or external API stability), it owns the convention. Otherwise the exception type name plus structured-log parameters carry the context.

## F#

F# has BCL `Result<'T, 'TError>` built in:

```fsharp
type ParseError =
    | BufferTooShort
    | LengthMismatch
    | CrcInvalid of expected: uint16 * actual: uint16

let parseFrame (raw: byte[]) : Result<Frame, ParseError> =
    if raw.Length < MinSize then
        Error BufferTooShort
    else
        ...
```

Compose with the `result { ... }` computation expression (FSharp.Core 8.0+) or the railway-oriented operators from `FsToolkit.ErrorHandling`:

```fsharp
let processFrame raw = result {
    let! frame = parseFrame raw
    let! validated = validate frame
    return! handle validated
}
```

For exception-throwing interop with C#, F# code can `throw` the same BCL exceptions as C#. The throw helpers work in F# too.

## What this means in practice

- **New routine failure** (parse, validate, CRC): return a Result type or a Try-pattern.
- **New parameter / state violation:** BCL throw helper or BCL exception.
- **New domain with ≥3 related failures:** consider a small exception hierarchy under one base.
- **Reviewing:** flag bare `throw new Exception(...)`, `throw ex;`, silent `catch (Exception) { }`, `try`/`catch` used as flow control on routine failures.
