# Standard: TESTING

> **Stability:** v1.0.0
> **Stack:** xUnit + FsCheck (property-based) + Avalonia.Headless (GUI). One tests project per repo by default; split when justified.

## One tests project — the default

Each repo has a single `tests/<App>.Tests/` (archetype A) or `tests/Stem.<Lib>.Tests/` (archetype B), targeting `net10.0`, written in F#.

This single project tests every layer of the repo via project references — F# can call into both F# and C# code under test without ceremony.

## When to split into multiple tests projects

Split only when **all** of the following hold:

1. The codebase has a substantial C# layer (e.g. a legacy `<App>.GUI.Windows` not yet migrated, or a vendor-mandated C# wrapper).
2. The C# tests need their own xUnit fixture conventions or analyzers that conflict with the F# project.
3. Cross-language refs from a single F# project are producing real friction (slow build, awkward syntax for C# generics).

Split shape:

```
tests/
├── <App>.Tests/              net10.0  F#  covers Core, Services, Infrastructure
└── <App>.Windows.Tests/      net10.0-windows  C#  covers GUI.Windows
```

Don't split prophylactically. The single F# tests project handles 95% of cases.

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
