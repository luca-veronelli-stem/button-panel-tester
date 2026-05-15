/-
T027 — Lean Phase-1 module for cache-and-memory consistency.

Mechanises FR-010 of `specs/001-fetch-dictionary/spec.md` line 111:
"System MUST keep the on-disk local copy and the in-memory dictionary
byte-equal at all times after the first successful fetch in a session."

The F# enforcement is the pair `JsonFileDictionaryCache.WriteAsync`
(T029, atomic temp+rename + sidecar hash per `contracts/cache-format.md`
lines 80-95) and the in-memory snapshot update inside `DictionaryService`
(T032 / T030+); both writes happen under the same successful-fetch
transition, so the two slots see the same `ButtonPanelDictionary`
value simultaneously.

This file models the consistency at the spec level: a `Session` carries
the two slots (`memory`, `cache`), each an `Option Dict`. The
`recordSuccess` transition is *defined* to put the same dictionary
value into both slots, and the theorem locks that contract in: a
future change to `recordSuccess` that put differing values in the
two slots would fail to compile here.

Constitution Principle I gate per `specs/001-fetch-dictionary/plan.md`
line 35-37 — no `sorry`, no custom axioms.

Polymorphism in `Dict` keeps T027 independent from T024
(`Phase1/DictionarySource.lean`, 5d5b2d0), T025
(`Phase1/FetchFailureReason.lean`, cdcb234), and T026
(`Phase1/DictionaryProvider.lean`, 34cb58c). All four files carry the
`[P]` parallel marker in `tasks.md:77,78,79,80`. This file does NOT
`import` T024, T025, or T026; the F# layer instantiates
`Dict = Core.Dictionary.ButtonPanelDictionary`.
-/

namespace Stem.ButtonPanelTester.Phase1

/-! ## Session

The operational view of an in-flight dictionary session: two slots,
`memory : Option Dict` and `cache : Option Dict`, both initially
empty. `memory` models the in-memory snapshot held on
`IDictionaryService.Snapshot` (T017, `data-model.md` §3); `cache`
models the on-disk pair (`dictionary.json` + `.sha256` sidecar) per
`contracts/cache-format.md`.

Polymorphism in `Dict` keeps T027 independent from T024-T026 per the
`[P]` parallel markers in `tasks.md:77,78,79,80`. The F# layer
instantiates `Dict = ButtonPanelDictionary`; the proof below does
not depend on the instantiation.
-/

structure Session (Dict : Type) where
  memory : Option Dict
  cache  : Option Dict
  deriving Repr

namespace Session

/-! ## recordSuccess transition

`recordSuccess s d` is the formal counterpart of the F# successful-
live-fetch transition: `JsonFileDictionaryCache.WriteAsync` writes
`d` to the on-disk cache file (atomic temp+rename + sidecar hash
per `contracts/cache-format.md` lines 80-95), and `DictionaryService`
updates `Snapshot` to carry the same `d` (T030+). Both writes happen
under the same transition, so the model puts the same `d` into both
slots simultaneously.

The function ignores the predecessor session — by FR-010 the
post-first-success state depends only on the freshly-fetched value,
not on whatever stale or seed data was in the slots before. The
embedded-seed extraction path (T031) writes through the same
`WriteAsync`, so any prior seed content is overwritten cleanly.
-/

def recordSuccess {Dict : Type} (_ : Session Dict) (d : Dict) : Session Dict :=
  { memory := some d, cache := some d }

end Session

/-! ## cache_memory_equal_post_first_success (spec.md FR-010, plan.md line 35-37)

The mechanised statement of FR-010 at the type level: starting from
ANY session `s` (initial, post-prior-failure, post-prior-success),
applying `recordSuccess d` yields a session whose `memory` slot equals
its `cache` slot — both carry the same `some d`.

The proof is `rfl` because `recordSuccess` is *defined* to put `d`
into both slots. The theorem statement is the design contract: a
future change to `recordSuccess` (e.g. omitting the cache slot, or
using different values in the two slots) would fail to compile here.
The "at every observable point" phrasing of FR-010 in `spec.md` line
111 is captured operationally: the only transition that writes the
two slots writes them in lockstep, and any read-only operation on
the session preserves the equality trivially.

Same proof style as T024's `source_data_preserved` (the design
contract is encoded in the function definition; `rfl` locks it in
at the spec level). Axiom audit verified empty via
`mcp__lean-lsp__lean_verify`.
-/

theorem cache_memory_equal_post_first_success
    {Dict : Type}
    (s : Session Dict)
    (d : Dict) :
    (s.recordSuccess d).memory = (s.recordSuccess d).cache := by
  rfl

end Stem.ButtonPanelTester.Phase1
