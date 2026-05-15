/-
T025 — Lean Phase-1 module for `FetchFailureReason`.

Closed taxonomy of dictionary-fetch failure modes, mechanising the F#
`Stem.ButtonPanelTester.Core.Dictionary.FetchFailureReason` DU shipped in
T013 (`src/ButtonPanelTester.Core/Dictionary/FetchFailureReason.fs`). The
eight cases in declared order are:

  1. `networkUnreachable`  — wire reason (`data-model.md` §1.3)
  2. `timeout`             — wire reason
  3. `unauthorized`        — wire reason
  4. `notFound`            — wire reason
  5. `malformedPayload`    — wire reason
  6. `serverError`         — wire reason
  7. `cacheAbsent`         — cache reason (`contracts/cache-format.md` line 74-76)
  8. `cacheUnreadable`     — cache reason (`contracts/cache-format.md` line 74-76)

Closure is witnessed by `failure_reason_exhaustion`. Mechanises Constitution
Principle I per `specs/001-fetch-dictionary/plan.md` line 35-37 — no `sorry`,
no custom axioms. Pairs with the FsCheck closure properties shipped in T023
(`tests/ButtonPanelTester.Tests/Property/FetchFailureReasonClosureTests.fs`,
d36bd44): the FsCheck side guards exhaustion at the value level via a
wildcard-free `match`; this Lean theorem guards it at the type level.

Adding a ninth case to `FetchFailureReason` requires updating the F# DU,
the FsCheck `label` function in T023's test file, AND this theorem (the
`cases r` proof breaks under a new variant). Three-layer break is the
intended cross-layer cost of a closure change.

T024 (`Phase1/DictionarySource.lean`, 5d5b2d0) is the sibling Phase-1 file;
both carry the `[P]` parallel marker in `tasks.md:77,78`. T024 leaves the
failure-reason type polymorphic (`Failure : Type`) precisely so the two
files do not impose a landing order on each other. This file does NOT
`import` T024.
-/

namespace Stem.ButtonPanelTester.Phase1

/-! ## FetchFailureReason

Closed eight-case discriminated union mirroring the F# `FetchFailureReason`
DU in T013 (`src/ButtonPanelTester.Core/Dictionary/FetchFailureReason.fs`,
cb56a9b). The six wire cases come from `data-model.md` §1.3; the two
cache cases (`cacheAbsent`, `cacheUnreadable`) extend the closure per
`contracts/cache-format.md` line 74-76. Case order matches the F# source
verbatim (modulo Lean camelCase / F# PascalCase convention).

`deriving DecidableEq` enables `cases r <;> simp` to discharge the closure
proof below. `deriving Repr` aids debugging and matches the convention of
T024's `CacheOrigin` / `DictionarySource` inductives.
-/

inductive FetchFailureReason where
  | networkUnreachable
  | timeout
  | unauthorized
  | notFound
  | malformedPayload
  | serverError
  | cacheAbsent
  | cacheUnreadable
  deriving DecidableEq, Repr

/-! ## failure_reason_exhaustion (plan.md line 35-37, Principle I)

Every inhabitant of `FetchFailureReason` is one of the eight declared
cases. This is the closure statement of `tasks.md:78`: "every observable
HTTP / network / cache outcome maps to exactly one variant". A closed
inductive satisfies this by construction; the theorem's load-bearing
content is structural: adding a ninth case breaks the proof under
`cases r`, which forces a cross-layer (F# DU + FsCheck label + Lean
theorem) update before the new variant can ship.

Proof: `cases r <;> simp`. Each of the eight sub-goals is one of the
eight disjuncts and `simp` discharges it by reducing the matching equality
to `True`. No `sorry`, no custom axioms; `cases` + `simp` over a finite
inductive does not pull in `Classical.choice` or `propext`. Axiom audit
verified empty via `mcp__lean-lsp__lean_verify`.
-/

theorem failure_reason_exhaustion (r : FetchFailureReason) :
    r = .networkUnreachable
  ∨ r = .timeout
  ∨ r = .unauthorized
  ∨ r = .notFound
  ∨ r = .malformedPayload
  ∨ r = .serverError
  ∨ r = .cacheAbsent
  ∨ r = .cacheUnreadable := by
  cases r <;> simp

end Stem.ButtonPanelTester.Phase1
