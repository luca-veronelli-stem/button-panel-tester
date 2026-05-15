/-
T026 — Lean Phase-1 module for `IDictionaryProvider`.

Mechanises the port-boundary outcome closure of
`specs/001-fetch-dictionary/data-model.md` §1.3 line 179:
"`IDictionaryProvider.FetchAsync` returns either `Success` or `Failed`,
never both, never neither (the F# DU enforces this; the Lean theorem
`provider_success_xor_failed` mechanises the same fact for downstream
reasoning)."

The F# port interface is declared in T016
(`src/ButtonPanelTester.Core/Dictionary/Ports.fs`, 738147d) per
`specs/001-fetch-dictionary/contracts/ports.md` §`IDictionaryProvider`
(lines 50-77): `FetchAsync : CancellationToken -> Task<DictionaryFetchResult>`.
The result type is the two-case closed DU shipped in T014
(`src/ButtonPanelTester.Core/Dictionary/DictionaryFetchResult.fs`,
b9967e3) per `data-model.md` §1.3 lines 94-95:

    type DictionaryFetchResult =
        | Success of ButtonPanelDictionary * FetchedAt: DateTimeOffset
        | Failed  of Reason: FetchFailureReason * Detail: string option

Constitution Principle I gate per `specs/001-fetch-dictionary/plan.md`
line 35-37 — no `sorry`, no custom axioms. Pairs with the F# DU's
pattern-match exhaustiveness at every consumer call site (no consumers
yet; the first arrives with T017's `IDictionaryService`).

Polymorphism in both the dictionary payload `Dict` and the failure
reason `Failure` keeps T026 independent from T024 (`Phase1/DictionarySource.lean`,
5d5b2d0) and T025 (`Phase1/FetchFailureReason.lean`, cdcb234). All three
files carry the `[P]` parallel marker in `tasks.md:77,78,79`. This file
does NOT `import` T024 or T025; the F# layer instantiates
`Dict = Core.Dictionary.ButtonPanelDictionary` and
`Failure = Core.Dictionary.FetchFailureReason`.
-/

namespace Stem.ButtonPanelTester.Phase1

/-! ## FetchResult

Two-case closed inductive mirroring the F# `DictionaryFetchResult` DU
shipped in T014 (`src/ButtonPanelTester.Core/Dictionary/DictionaryFetchResult.fs`,
b9967e3) per `data-model.md` §1.3 lines 94-95. Two constructors:

  * `success dict fetchedAt` — the live HTTP fetch produced a usable
                                in-memory dictionary; `fetchedAt` is the
                                receipt-time annotation.
  * `failed  reason detail`  — the live HTTP fetch failed; `reason` is the
                                closed eight-case `FetchFailureReason`,
                                `detail` is the optional human-readable
                                elaboration for the status row's detail
                                affordance.

`Nat` stands in for `DateTimeOffset`; `Option String` stands in for the
F# `string option` detail field. Neither is constrained by the XOR
theorem below — the theorem reasons only about the discriminant.

Polymorphism in `Dict` and `Failure` keeps T026 independent from T024
and T025 per the `[P]` parallel markers in `tasks.md:77,78,79`. The F#
layer instantiates `Dict = ButtonPanelDictionary` and
`Failure = FetchFailureReason`; the proof below does not depend on
either instantiation.
-/

inductive FetchResult (Dict Failure : Type) where
  | success (dict : Dict) (fetchedAt : Nat) : FetchResult Dict Failure
  | failed  (reason : Failure) (detail : Option String) : FetchResult Dict Failure
  deriving Repr

namespace FetchResult

/-! ## Discriminant predicates

`isSuccess` / `isFailed` project the constructor tag to a proposition.
Each predicate is `True` on its own arm and `False` on the other —
the XOR statement below is the conjunction of those two definitional
facts.
-/

def isSuccess {Dict Failure : Type} : FetchResult Dict Failure → Prop
  | .success _ _ => True
  | .failed  _ _ => False

def isFailed {Dict Failure : Type} : FetchResult Dict Failure → Prop
  | .success _ _ => False
  | .failed  _ _ => True

end FetchResult

/-! ## provider_success_xor_failed (data-model.md §1.3 line 179, plan.md line 35-37)

The mechanised statement of the port-boundary outcome closure: every
inhabitant of `FetchResult Dict Failure` is either a `success` (and
not a `failed`) or a `failed` (and not a `success`) — never both,
never neither.

The XOR shape is the disjunction of two conjunctions: the left arm
captures "exactly success", the right arm captures "exactly failed".
A future third constructor on `FetchResult` would yield an unmatched
goal under `cases r`, forcing a Lean-side update — that is the
load-bearing structural content. The closure is mirrored at the F#
layer by the two-case closed DU `DictionaryFetchResult` (T014, b9967e3):
adding a third F# case would force a parallel three-way update of the
Lean theorem, the F# DU, and every `match` site on
`DictionaryFetchResult`.

Proof: `cases r <;> simp [FetchResult.isSuccess, FetchResult.isFailed]`.
Each of the two sub-goals after `cases r` reduces to a definitional
conjunction (`True ∧ ¬ False` on the success arm, `True ∧ ¬ False` on
the failed arm via the symmetric disjunct), and `simp` discharges
both. No `sorry`, no custom axioms. Axiom audit verified via
`mcp__lean-lsp__lean_verify`.
-/

theorem provider_success_xor_failed
    {Dict Failure : Type}
    (r : FetchResult Dict Failure) :
    (r.isSuccess ∧ ¬ r.isFailed) ∨ (r.isFailed ∧ ¬ r.isSuccess) := by
  cases r <;> simp [FetchResult.isSuccess, FetchResult.isFailed]

end Stem.ButtonPanelTester.Phase1
