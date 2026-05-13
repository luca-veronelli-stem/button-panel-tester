# {ComponentName}

<!--
Per-component README template. One per project / folder that earns
its own README — typically each top-level project in the solution
(Core, Services, Infrastructure.*, GUI.*, Tests, Drivers.*, etc.).

Section legend:
  REQUIRED — every component README has this section.
  OPTIONAL — include only when the section earns its keep.
  B ONLY   — include only in archetype-B (library) repos where this
             component is part of the consumer-facing public surface.

Delete the comment blocks and the "[REQUIRED]" / "[OPTIONAL]" labels
before submitting.
-->

## Overview                                                   [REQUIRED]

One-paragraph summary: what this component is and where it sits in the larger repo. Link to the parent (root README, solution overview) when context matters.

## Responsibilities                                           [REQUIRED]

Three to seven bullets. Each starts with a verb and names a concrete responsibility. Don't list classes — list capabilities.

- {Verb} {what}, via {how-in-one-phrase}.
- {Verb} {what}, with {mechanism}.
- {Verb} {what}, exposing {surface} to {consumer}.

If the component has a clear out-of-scope, add a "Not responsible for" sub-bullet:

- *Not responsible for:* {thing readers might assume but shouldn't}.

## Folder structure                                           [OPTIONAL]

Useful when the project has more than ~5 top-level folders or a non-obvious layout. Skip for small projects.

```
{ComponentName}/
├── {ComponentName}.csproj
├── Subfolder1/
│   ├── TypeA.cs                 — one-line role
│   └── TypeB.cs                 — one-line role
├── Subfolder2/
│   └── TypeC.cs                 — one-line role
└── Internal/
    └── HelperD.cs               — one-line role
```

Use box-drawing characters (`├ │ └ ─`) for the tree; UTF-8 source files.

## Key types                                                  [REQUIRED]

The two to five types a reader needs to know to use or extend this component. For each: type name + one-paragraph role + a minimal usage snippet if it isn't obvious.

### {PrimaryType}

What it is in one sentence. What it does in one paragraph.

```csharp
var x = new PrimaryType(config);
await x.DoThingAsync(cancellationToken);
```

### {SecondaryType}

…

For long type lists, link to the per-type XML docs rather than restating them. The README is the entry point, not a reference.

## Public surface                                             [B ONLY]

Library archetype only. Either:

- A pointer to the per-component `API_SURFACE.md` (preferred when the surface is non-trivial).
- An inline list of public types organized by kind (entry points, interfaces, configuration, exceptions, enums) when the surface is small.

Apps don't have a public surface — omit this section.

## Configuration                                              [OPTIONAL]

Include when the component has a `{ComponentName}Configuration` class (per the CONFIGURATION standard). Show the property table with defaults and bounds — these come from the matching `{ComponentName}Constants` class.

| Property | Type | Default | Min | Max | Description |
| --- | --- | --- | --- | --- | --- |
| `TimeoutMs` | `int` | `1000` | `100` | `30000` | Operation timeout in milliseconds. |

```csharp
var config = new {ComponentName}Configuration { TimeoutMs = 2000 };
config.Validate();
```

For app components (archetype A) using `IOptions<T>`, show the `appsettings.json` shape:

```json
{
  "{ComponentName}": {
    "TimeoutMs": 2000
  }
}
```

## Dependencies                                               [OPTIONAL]

Include when the component depends on non-obvious projects or external packages. Skip if dependencies are limited to BCL + the parent solution's standard set (`Microsoft.Extensions.*`).

| Depends on | Why |
| --- | --- |
| `Core` | shared domain types |
| `Microsoft.Extensions.Logging.Abstractions` | structured logging (LOGGING standard) |

## Testing                                                    [OPTIONAL]

A pointer to the corresponding tests project + a one-line note on coverage shape. Per the TESTING standard, most repos have a single tests project; some have a Windows-only split.

- Tests live in `tests/{ComponentName}.Tests/` (per TESTING).
- Coverage shape: unit + property; no integration tests — this component is pure.

Skip when the testing pattern is the repo default with nothing component-specific to say.

## See also                                                   [OPTIONAL]

Cross-references to closely related components, design docs, or standards that govern this component's shape. Two to four entries; prune ruthlessly.

- [Sibling component](../OtherComponent/README.md) — relationship in one phrase.
- [Standard: NAME](../../shared/standards/NAME.md) — relevance in one phrase.
