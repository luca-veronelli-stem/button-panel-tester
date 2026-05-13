# Standard: VISIBILITY

> **Stability:** v1.2.0
> **Principle:** the public surface is a liability — every public type is a future migration. Archetype B (libraries) defaults to `internal`; archetype A (apps) has no external consumer and only sweats encapsulation for refactoring's sake. Both seal by default.

## Two regimes

The visibility regime depends on the repo's archetype (declared in `state/repos.md`).

### Archetype B — library

A library has external consumers (NuGet, project refs). Every public type is a contract.

- **Default `internal`.** A type is `public` only when a consumer outside the assembly genuinely needs it.
- **Public types fall into a small kit:**
  - Facades / entry points (the type a consumer constructs to use the library)
  - Interfaces (the consumer-facing abstraction)
  - DTOs / records (data crossing the boundary)
  - Configuration types
  - `EventArgs` and event payload records (per `EVENTARGS`)
  - Constants (`public static class`)
  - Exception types
  - Enums used in any public signature
- **Internal stays internal.** Concrete implementations, helpers, state machines, buffers, parsers — `internal sealed`.
- **Tests reach internals via `InternalsVisibleTo`.** Centralize the declaration in `Directory.Build.props`:

  ```xml
  <ItemGroup>
    <InternalsVisibleTo Include="$(AssemblyName).Tests" />
  </ItemGroup>
  ```

  One line per test assembly. No catch-all wildcards.

### Archetype A — app

An app has no external consumer. Visibility is only an internal-refactoring discipline.

- **Public is the default** for ordinary domain/service/view types — no consumer is reading `public` as a contract.
- **Internal still earns its keep** for clearly local helpers (parsers, buffers, plumbing) where a `public` declaration would invite cross-feature coupling.
- **`InternalsVisibleTo`** can be declared per-csproj where needed; centralization in `Directory.Build.props` is optional.

## Seal by default — both archetypes

`sealed` is the default for any class not designed for inheritance. The .NET SDK enforces this on internal types via **CA1852** ("seal internal types"); enable it as a warning in `Directory.Build.props`:

```xml
<PropertyGroup>
  <AnalysisLevel>latest-recommended</AnalysisLevel>
</PropertyGroup>
```

`latest-recommended` turns on CA1852 alongside other modern recommendations. Don't override it project-by-project.

A class is non-sealed only when:
- It is an **abstract base** that derived types are expected to extend (rare — see below).
- It is an **exception type** that sub-exceptions specialize (`ChannelException` → `ChannelTimeoutException`).
- It is a **record** with required-by-derived semantics (also rare).

Non-sealed types declare their intent in their summary: "Designed for inheritance: derived types must …".

## `public abstract class` — allow, but prefer composition

A `public abstract class` is a public inheritance hook. They earn their keep occasionally (template-method patterns, framework extension points) but most domain abstractions are better expressed as an interface plus a sealed default implementation. Reach for `public abstract class` only after considering the interface alternative.

## Members

- **Instance fields:** `private` (or `private readonly`).
- **Public properties:** part of the contract — design carefully.
- **Helper methods:** `private` until a second caller exists.
- **`protected virtual` hooks:** only on intentional inheritance bases.

The standard does not legislate fields-vs-auto-properties — both express private state correctly. Choose for clarity at the callsite.

## What to do when adopting

When a repo bumps to v1.2.0:

- **Archetype B:** survey public types. Anything that isn't in the public kit above gets demoted to `internal`. CA1852 reports unsealed internals — seal them. Migrate test-only access to `InternalsVisibleTo`.
- **Archetype A:** enable CA1852 (sealed-by-default discipline). Don't perform a public-to-internal sweep — there's no payoff.

The MIGRATION standard's per-version step list captures the concrete commands.

## What this means in practice

- **New B type:** start `internal sealed`. Promote to `public` only when a consumer needs it, and write the one-line reason in the type summary.
- **New A type:** `public sealed` (or whichever access the surrounding code uses) — match the file's neighbors.
- **Reviewing:** flag any `public class` in B that isn't in the kit; flag any non-sealed class that doesn't say in its summary why it's open.
