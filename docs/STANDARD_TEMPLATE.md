# Standard: NAME

> **Stability:** vX.Y.0
> **Principle:** one sentence stating the rule that justifies the rest of the document. The reader should be able to predict everything below from this principle alone.

<!--
Authoring notes for new standards under shared/standards/. Delete the
HTML comments before submitting.

House style:
  - English. Prose-driven. Minimal tables; reach for one only when the data
    really tabulates (a decision matrix, a primitive lookup, a coverage grid).
  - No rule codes (TS-001, EA-001, R-NNN). The principle plus the prose
    carries the meaning.
  - No ✅/⚠️/❌/💡 severity markers. Use plain language: "required",
    "allowed", "forbidden", "prefer".
  - No version/date/status block at the top. The file is in git; CHANGELOG.md
    at the repo root tracks the versioning.
  - No embedded changelog section. Same reason.
  - Cross-link to other standards by their short name (TESTING, CANCELLATION),
    not as filesystem paths.
-->

## Optional context paragraph

When the principle is dense enough to need framing, add one short paragraph here naming the audience, the surface area covered, and the surface area explicitly *not* covered. Otherwise jump straight into the rules.

## First load-bearing rule

Body paragraph. State the rule directly; explain the reason in the next sentence. Show what it looks like in code:

```csharp
// minimal, complete example — readers shouldn't have to imagine the surrounding code
public sealed class Example
{
    public void Method() { /* ... */ }
}
```

Anti-pattern in the same shape:

```csharp
// what not to do, and why
public class Example
{
    public void Method() { /* ... */ }
}
```

## Second load-bearing rule

Repeat the structure. Group related rules under one H2 — a rule that needs its own section probably deserves an H2; a clarification stays a paragraph.

## Tables — when they earn their keep

| Shape of input | Recommended choice | Rationale |
| --- | --- | --- |
| Case A | Choice A | one-line reason |
| Case B | Choice B | one-line reason |

A table is right when the rows are genuinely comparable. A table of "rule code → severity → description" rows is just prose with extra cell walls — write it as paragraphs.

## Archetype-specific guidance (when applicable)

If a standard differs by archetype (A — desktop app, B — library, C — meta/config), split with H3:

### Archetype A — app

…

### Archetype B — library

…

Otherwise, omit this section.

## F# (when applicable)

Most .NET standards apply to F# without modification. Add this section only when there's a meaningfully different idiom (a different type, a different attribute, a different pattern). Show one minimal F# example.

```fsharp
// idiomatic F# version of the rule, when it differs
let example = ()
```

## What this means in practice

Three to five bullets, each starting with an action verb, that turn the prose into a checklist a reviewer can apply on a PR:

- **New X:** specific instruction.
- **Existing Y:** specific instruction (often "leave alone unless touching for other reasons").
- **Reviewing:** what to flag.
- **Adoption:** what changes when a repo bumps to this version (if anything).
