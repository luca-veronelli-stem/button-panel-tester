# Standard: TESTING

> **Stability:** v1.0.0
> **Stack:** xUnit + FsCheck (property-based) + Avalonia.Headless (GUI). One tests project per repo by default; split when justified.

## One tests project — the default

Each repo has a single `tests/<App>.Tests/` (archetype A) or `tests/Stem.<Lib>.Tests/` (archetype B), targeting `net10.0`, written in F#.

This single project tests every layer of the repo via project references — F# can call into both F# and C# code under test without ceremony.

## When to split into multiple tests projects

Two recognised split shapes, both producing `<App>.Tests` + `<App>.Tests.Windows` (and optionally `<App>.Tests.Linux`):

**TFM split** — when part of the test surface targets `net10.0-windows` and another part stays on `net10.0`. The Windows-only TFM is forced by Avalonia.Headless GUI tests, DPAPI / Registry / WMI integration tests, or any test that depends on a `<App>.Infrastructure` project that itself targets `net10.0-windows`. NuGet rejects a `net10.0` `ProjectReference` to a `net10.0-windows` project (NU1201) — a single F# tests project can't span both worlds. Split is mandatory in this case, not optional.

**Language split** — when a substantial C# layer (legacy `<App>.GUI.Windows` not yet migrated, vendor-mandated C# wrapper) needs its own xUnit fixture conventions or analyzers that conflict with the F# project, AND cross-language refs from a single F# project produce real friction.

Split shape (either case):

```
tests/
├── <App>.Tests/                  net10.0          F#  covers Core, Services, cross-platform Infrastructure
└── <App>.Tests.Windows/          net10.0-windows  F# or C#  covers GUI, Avalonia.Headless, Windows-only Infrastructure
```

The `<App>.Tests.<Platform>` shape (Tests-prefix, then platform suffix) is **load-bearing for CI**: the reusable `dotnet-ci.yml` Linux test leg enumerates `tests/**/*.Tests.{fsproj,csproj}` and skips files matching `*.Tests.Windows.*` or `*.Tests.Linux.*` (see CI standard). Naming a Windows-only test project `<App>.Windows.Tests` instead would break the Linux CI leg (vstest tries to load a `net10.0` output that does not exist on a `net10.0-windows`-only project).

Don't split prophylactically. The single F# tests project handles repos with no Windows-only test surface.

## Naming

- Class: `{ClassUnderTest}Tests` (e.g. `BlePacketDecoderTests`).
- F# module / type: same convention — `[<Tests>] module BlePacketDecoderTests` or a plain F# module.
- Method: `{Method}_{Scenario}_{ExpectedResult}` (e.g. `Decode_TruncatedPayload_ThrowsCrcMismatch`).

For F# the module/function form is common:

```fsharp
module BlePacketDecoderTests

[<Fact>]
let ``Decode truncated payload throws CrcMismatch`` () =
    let raw = Array.zeroCreate 4
    Assert.Throws<CrcMismatchException>(fun () -> BlePacketDecoder.decode raw |> ignore)
    |> ignore
```

Either backtick-style names or PascalCase function names are fine. Stay consistent within a module.

## Unattended-only test suites

The `tests/` project holds **only tests that run to completion with no human intervention**. In an unattended run (CI, a fresh clone, the pre-push gate) every test either asserts and finishes, or is excluded by a runtime gate — never parked waiting on a person. Admissible shapes:

- **Pure logic / fake-seam integration** — run everywhere, every time.
- **Hardware-present-but-unattended** — env-gated *and* `[<Trait("Category", "Hardware")>]`-traited (reference impl: `[<HardwareFact>]`). The device is plugged in; the test drives it and asserts with nobody watching. CI's `Category!=Hardware` filter excludes it on hosted runners — see [CI](./CI.md) → "Hardware-test exclusion".

A test that needs a human to act *during* the run — press a button, unplug a cable, observe a screen — must **not** live in the suite as a `Skip`-by-default case (`[<Fact(Skip = "Manual: unplug the CAN cable")>]`). An unconditional `Skip =` literal is a manual procedure wearing a test's clothing:

- it inflates the *skipped* count and **fakes coverage** — green-adjacent, asserts nothing on any run;
- it **rots** — nobody remembers to remove the `Skip` and perform the dance;
- it can't be gated cleanly — the literal needs a *source edit* to ever run, whereas a bare env gate with no `Skip` would let an unattended run **hang** waiting on the operator.

Resolve it one of two ways:

1. **Automate the human away** with a fixture — a programmable USB power switch / relay for unplug-replug, an input injector for button presses — so the case becomes unattended (`[<HardwareFact>]`).
2. **Demote it to a manual step** in a release / re-vendor **runbook** run against the real application.

The state-machine *logic* such a scenario exercises belongs in fast fake-driven tests **regardless** — only the irreducibly-physical leg is a runbook candidate.

**Exception — attended but env-gated.** When the *only* assertion of some genuinely-physical, non-deterministic behavior can be neither automated nor faithfully captured in prose (e.g. proving a vendored stack's *autonomous* reconnect after a physical replug), keep the test but re-gate it as **attended and env-gated**: `[<ManualHardwareFact>]` keyed on an interactive env var (e.g. `BPT_HARDWARE_INTERACTIVE=1`), tied explicitly to its trigger (a re-vendor guard, a driver bump). This is *not* a `Skip`-by-default case — the env gate is a runtime condition: dormant in every unattended run (CI never hangs), runnable on demand by an operator with **no source edit**. The `[<HardwareFact>]` / `[<ManualHardwareFact>]` attributes are the reference implementation — see [`button-panel-tester#142`](https://github.com/luca-veronelli-stem/button-panel-tester/issues/142).

> **Scope.** This principle governs **attended / human-in-the-loop** tests — those needing a person to act mid-run. Platform gating (a `net10.0-windows`-only test) is an orthogonal concern; hardware gating uses the `Category` trait filter ([CI](./CI.md) → "Hardware-test exclusion"), which already forbids substituting `Skip` for the same exclusion intent.

## Test categories

Organize tests by *what they exercise*, not *who wrote them*:

| Category | Folder | What it tests |
| --- | --- | --- |
| Unit | `Tests/Unit/` | Single function or type, no IO. |
| Property | `Tests/Property/` | FsCheck generators against invariants. |
| Integration | `Tests/Integration/` | Real EF Core (SQLite in-memory), real file IO, etc. |
| GUI | `Tests/Gui/` | Avalonia.Headless against view-models / views. |

A unit test reads a single file. An integration test stands up a fixture. A property test runs 100 random cases by default.

## Property-based testing

FsCheck.Xunit attribute-based:

```fsharp
[<Property>]
let ``Decode is the inverse of Encode`` (payload: byte[]) =
    let encoded = BlePacketEncoder.encode payload
    let decoded = BlePacketDecoder.decode encoded
    decoded = payload
```

Use property tests for:
- Round-trip identities (encode/decode, serialize/deserialize).
- Invariants (e.g. *the result is always within \[0, 100\]*).
- Idempotence (`f (f x) = f x`).

Don't replace example-based unit tests — use both.

## Avalonia GUI testing — headless

`Avalonia.Headless.XUnit` runs Avalonia in-process without a display, so GUI tests work on CI Linux runners.

```fsharp
[<AvaloniaFact>]
let ``Connect button enables when port is selected`` () =
    let window = MainWindow()
    window.Show()
    // simulate selection, assert button enabled state
```

Avoid `[<Fact>]` in GUI tests — `[<AvaloniaFact>]` boots the headless app correctly.

## No mocking libraries

Write **manual fakes** under `tests/<App>.Tests/Fakes/`. A fake is a normal class/module implementing the port. No Moq, NSubstitute, or FakeItEasy.

```fsharp
module Fakes

type FakePersistor() =
    let mutable saved = []
    interface IPersistor with
        member _.Save(item) = saved <- item :: saved
    member _.Saved = saved
```

## Test data

- **Inline** small inputs as `[<Theory; InlineData>]` parameters or as F# literals.
- **Fixture files** larger inputs under `tests/<App>.Tests/Fixtures/` and load via `File.ReadAllBytes` with a path relative to the test assembly.
- **Generators** for property-based tests live next to the property using them, or in `tests/<App>.Tests/Generators.fs`.

Never rely on absolute paths. Never check in production data.

## Running tests

```powershell
dotnet test                                # all tests, default config
dotnet test --filter Category=Unit         # only unit tests
dotnet test --logger "console;verbosity=detailed"
```

CI runs the full suite on both `ubuntu-latest` and `windows-latest` (see CI). A test that only runs on Windows must declare it via xUnit's `[<Fact(Skip=...)>]` or via `RuntimeInformation` checks in the test body.

## Coverage

Not enforced for v1. If we ever add coverage reporting, it'll be `coverlet.collector` + `dorny/test-reporter` action — both already cross-platform. Track this in the MIGRATION standard if it becomes a v2 ask.
