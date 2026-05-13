# Standard: LANGUAGE

> **Stability:** v1.0.0
> **Principle:** use the appropriate language for the feature/layer. Languages exist because they have their use cases. F# is the **default** for new layers — it pays off in domain modelling, immutability, and exhaustive matching. C# remains valid where it earns its keep.

## Layer defaults

| Layer | Default | Rationale |
| --- | --- | --- |
| `Core` / `Abstractions` | F# | Discriminated unions, exhaustive matching, immutable records — F# saves boilerplate that C# can't. |
| `Services` / `Protocol` | F# | Pure functions, easy property-based testing with FsCheck. |
| `Infrastructure` / `Drivers.*` | F# | Adapter logic stays small; mixing languages here adds boundary friction without payoff. C# allowed when an SDK is C#-only and wrapping it would obscure intent. |
| `GUI` | F# | Avalonia + FuncUI (Elmish-MVU). C# allowed for legacy `<App>.GUI.Windows` projects pending Avalonia migration. |
| `DependencyInjection` (B, optional) | C# | MEDI fluent extension methods are idiomatic in C#. F# would add ceremony with no gain. |
| `Tests` | matches code-under-test | Single F# tests project by default; split into a sibling C# tests project only when the C# surface is substantial (see TESTING). |

A single repo can mix F# and C# projects. `dotnet`, MSBuild, and `Directory.{Build,Packages}.props` handle mixed solutions without ceremony.

## Choosing other than the default

When a project adopts a non-default language, the repo's top-level `CLAUDE.md` records a one-sentence justification. This forces deliberate choice and is auditable.

```markdown
## Language choices that deviate from defaults

- `<App>.GUI.Windows`: C# — wraps a vendor's Win32 SDK whose generated bindings are C#-only.
```

## Mixing F# and C# — what to know

- **Project references work both ways.** F# can call into C# (and vice versa) without ceremony as long as types are public.
- **F# compile order matters.** `.fsproj` lists files in compilation order; declare types before they're used.
- **Central Package Management** in `Directory.Packages.props` applies to both languages.
- **`dotnet format`** has separate analyzers per language; both run in CI.
- **F# from C# nuances:** discriminated unions surface as nested classes; option types surface as `FSharpOption<T>`. If a public surface needs to be C#-friendly, expose plain `class` records with explicit nullability rather than DUs.

## Banned APIs are language-agnostic

`BannedSymbols.txt` (see PORTABILITY) is loaded at the project level and applies to F# and C# alike.

## What this means in practice

- **New project:** F# unless you can write the one-line reason for C# in `CLAUDE.md`.
- **Existing C# code:** leave alone unless you'd be touching it for other reasons. The MIGRATION standard tracks the cadence per repo.
- **When adding a feature:** stay in the language of the project you're editing. Don't rewrite a layer just to switch language.
