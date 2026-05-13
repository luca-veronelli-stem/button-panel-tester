# Standard: COMMENTS

> **Stability:** v1.2.0
> **Principle:** XML docs describe the contract; inline comments explain *why*. Names do most of the work — a comment that restates the code is noise. English by default; reach for a comment when the reader of the next change couldn't infer the reason from the code alone.

## Language

English by default for every comment and XML doc — same rule as code, identifiers, GUI strings, and CHANGELOG entries (CLAUDE.md). Italian only on an explicit per-artifact request.

Existing Italian XML docs in v1.1.x repos are left in place; rewrite to English when you touch the surrounding code for other reasons. Don't open language-only sweep PRs.

## Coverage by visibility

| Visibility | XML docs | Notes |
| --- | --- | --- |
| `public`, `protected` | **Required** | Part of the contract surface. |
| `internal` | Optional; recommended for non-obvious types | A `<summary>` is enough; deep tag coverage isn't expected. |
| `private` | Rare | Prefer a descriptive name and, where needed, a one-line `//` comment explaining *why*. |

For archetype B (libraries), the required coverage is enforced by enabling `<GenerateDocumentationFile>true</GenerateDocumentationFile>` in `Directory.Build.props`. CS1591 stays a warning (don't promote to error — it produces noise during exploratory work).

For archetype A (apps), `<GenerateDocumentationFile>` is optional. Adopt it only if a project genuinely benefits.

## Required tags

```csharp
/// <summary>
/// Decodes a frame from its raw byte representation.
/// </summary>
/// <param name="raw">the framed bytes, including the CRC trailer.</param>
/// <param name="cancellationToken">cancels the decode if the caller bails.</param>
/// <returns>the decoded frame, or <c>null</c> when the CRC mismatches.</returns>
/// <exception cref="ArgumentNullException">when <paramref name="raw"/> is null.</exception>
public Frame? Decode(byte[] raw, CancellationToken cancellationToken = default);
```

- `<summary>`: every public/protected member; first letter uppercase, ends with a period, one sentence (two at most).
- `<param>`: one per parameter; lowercase first letter (continuation of the summary sentence). Document special values (`null`, `default`).
- `<returns>`: every non-void method. For `bool` returns explain what `true` and `false` mean; for `Task<T>` document `T`, not the task wrapper.
- `<exception>`: for each exception the method throws as part of its contract (not for "any exception from inside").

`<remarks>` adds context the summary can't carry: thread-safety notes, side effects, performance characteristics, references to other types.

`<example>` is valuable on public API entry points. Skip it on internal types.

## `<inheritdoc/>`

Interface implementations and overrides reuse the parent's docs:

```csharp
public interface IDecoder
{
    /// <summary>Decodes a frame from its raw byte representation.</summary>
    Frame? Decode(byte[] raw);
}

public sealed class FrameDecoder : IDecoder
{
    /// <inheritdoc/>
    public Frame? Decode(byte[] raw) => /* ... */;
}
```

Don't paste the same `<summary>` twice. If the implementation needs to add notes, use `<inheritdoc/>` plus a `<remarks>` block:

```csharp
/// <inheritdoc/>
/// <remarks>This implementation is thread-safe.</remarks>
```

## `cref` references

Use a name the compiler can resolve:

```csharp
/// <see cref="LayerStatus.Success"/>           // imported
/// <see cref="MyApp.Core.LayerStatus.Success"/> // not imported
/// <see langword="null"/>                      // for keywords
```

Fully-qualified is safer when the type isn't `using`-imported in the file.

## Inline comments — explain *why*

```csharp
// MTU is capped at 244 because the BLE stack on iOS strips the
// trailing 3 bytes for the L2CAP header — see device-issue#147.
const int EffectiveMtu = 244;
```

A comment that restates the code is noise:

```csharp
// Increment the counter
counter++;                                       // ❌
```

Prefer a clear name; reach for a comment only when the reason behind the code isn't visible from the code itself.

## TODO / FIXME / HACK / NOTE

Tag temporary state with the GitHub issue number — the tracker is the real backlog:

```csharp
// TODO #42 — switch to streaming once the dictionaries API supports paging
// FIXME #58 — race in reconnect path; lock for now, revisit with proper actor model
// HACK — Bluetooth stack on Windows 11 22H2 ignores the timeout; loop manually
// NOTE — protocol byte order is big-endian (matches the C reference firmware)
```

A `TODO` without an issue number rots; an issue number ties it to a tracker entry that can be reviewed.

## Anti-patterns

- **Commented-out code.** Delete it. Git remembers.
- **Changelogs in comments.** Use commit messages and `CHANGELOG.md`.
- **"This method does X" preambles.** The summary already does that.
- **Restating types.** `// the user's name` above `string Name` adds nothing.
- **Stale comments.** When refactoring, update or delete them.

## F#

Same `///` syntax:

```fsharp
/// <summary>Decodes a frame from its raw byte representation.</summary>
/// <param name="raw">the framed bytes including the CRC trailer.</param>
/// <returns>the decoded frame, or <c>None</c> when the CRC mismatches.</returns>
let decode (raw: byte[]) : Frame option = ...
```

For F#-specific docs (records, DUs, modules), the same coverage rules apply — public surface gets `<summary>`; internal modules are optional.

## Enforcement

- **CS1591** as a warning when `<GenerateDocumentationFile>` is on. Acts as a coverage signal.
- **Roslyn / StyleCop analyzers (SA1600+)** are available if a repo wants stronger enforcement. v1.2.0 doesn't mandate them — adopt at the repo's discretion.
- **Code review** is the primary enforcement: flag missing `<summary>`, dead-code comments, comments that restate code, and stale TODOs.

## What this means in practice

- **New public type/member:** write the XML doc as you write the signature; the coverage rule is non-negotiable for B.
- **New private type/member:** name it well; add a one-line `//` only if a future reader couldn't infer the reason.
- **Touching legacy Italian docs:** rewrite to English in the same change.
- **Reviewing:** flag dead code, obvious comments, changelogs-in-comments, and TODOs without issue numbers.
