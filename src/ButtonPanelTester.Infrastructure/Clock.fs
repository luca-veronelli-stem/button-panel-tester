namespace Stem.ButtonPanelTester.Infrastructure

open System
open Stem.ButtonPanelTester.Core.Dictionary

/// Production adapter for `IClock`, per
/// `specs/001-fetch-dictionary/contracts/ports.md` §IClock
/// (lines 31-36). Wraps `DateTimeOffset.UtcNow` — a single
/// BCL call, no fields, no state. Registered as a singleton
/// at the composition root
/// (`services.AddSingleton<IClock, SystemClock>()` per
/// ports.md line 195).
///
/// The test seam is `Tests.Fakes.FrozenClock` (T019), which
/// implements the same `IClock` interface with scripted time
/// progression via `Advance` and `SetTo`. The DI registration
/// ensures `DictionaryService` (T030+) reads time only through
/// this port, so every test that needs deterministic
/// timestamps substitutes `FrozenClock` and never reaches the
/// real wall clock.
type SystemClock() =
    interface IClock with
        member _.UtcNow() = DateTimeOffset.UtcNow
