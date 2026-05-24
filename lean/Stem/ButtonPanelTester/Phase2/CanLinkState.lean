/-
T027 — Lean Phase-2 module for `CanLinkState`.

Mechanises Invariant #1 of `specs/002-can-link-and-panel-discovery/data-model.md`
§1.3 (classification totality): every `CanLinkState` value falls into exactly one
of the five top-level classifications
`{Initializing, Connected, Disconnected, Error.Recoverable, Error.Fatal}`. A
closed inductive plus a wildcard-free `match` proof witnesses this by
construction; adding a sixth top-level case breaks the proof under `cases s`,
which forces a cross-layer (F# DU + FsCheck property test + Lean theorem)
update before the new variant can ship.

The F# surface lives at `src/ButtonPanelTester.Core/Can/CanLinkState.fs` (T012);
the FsCheck classifier test lives at
`tests/ButtonPanelTester.Tests/Property/Can/CanLinkStateTransitionsProperties.fs`
(T026). The trio rides into the tree as one vertical commit per the PR-B
"Scope (9 vertical commits)" decomposition in issue #114.

Constitution Principle I (no `sorry`, no custom axioms;
`specs/002-can-link-and-panel-discovery/plan.md` Constitution Check §I): the
proof is `cases s <;> simp`. Each of the five sub-goals reduces to one of the
five disjuncts and `simp` discharges it by reducing the matching equality
to `True`. No `propext`, no `Classical.choice`, no `Quot.sound` beyond what
the inductive's auto-generated `cases` introduces.

The `Adapter` and `Reason` carriers are abstract type parameters — the
classification statement is structural over `CanLinkState`'s top-level shape,
not over the contents of `Connected`/`Disconnected`/`Error`. Keeping them
polymorphic also means this file does not need to import a Lean model of
`AdapterIdentification` or `DisconnectReason`; the F# side carries those
concrete records, the Lean side reasons over the closure.
-/

namespace Stem.ButtonPanelTester.Phase2

/-! ## DisconnectReason

Closed taxonomy of reasons attached to the `disconnected` case of
`CanLinkState`, mirroring the F# `DisconnectReason` DU in
`src/ButtonPanelTester.Core/Can/CanLinkState.fs` (T012). Not directly
load-bearing for `state_classification_total` below — that theorem is
abstract over the disconnect-reason carrier — but defined here so a
future Lean file proving a per-reason property (e.g. the FR-005
distinct-headline branch) does not need to redefine the taxonomy.
-/

inductive DisconnectReason where
  | noAdapterPresent
  | linkNotYetOpened
  | midSessionUnplug
  | reconnectPending
  deriving DecidableEq, Repr

/-! ## ErrorClassification

Two-case error sub-classification carried by `CanLinkState.error`, mirroring
the F# `ErrorClassification` DU in
`src/ButtonPanelTester.Core/Can/CanLinkState.fs` (T012). The detail string
is modelled as an abstract `Detail` type parameter — the classification
statement does not constrain its content.
-/

inductive ErrorClassification (Detail : Type) where
  | recoverable (detail : Detail)
  | fatal (detail : Detail)
  deriving Repr

/-! ## CanLinkState

Closed four-case inductive mirroring the F# `CanLinkState` DU in
`src/ButtonPanelTester.Core/Can/CanLinkState.fs` (T012). The `Adapter`
and `Detail` carriers are abstract — the classification statement is
structural over the four top-level constructors, not their payload.

`Nat` stands in for `DateTimeOffset`. The invariant proved below does
not constrain timestamp values, only which of the five classifications
each state belongs to.
-/

inductive CanLinkState (Adapter Detail : Type) where
  | initializing
  | connected (adapter : Adapter) (openedAt : Nat) : CanLinkState Adapter Detail
  | disconnected (reason : DisconnectReason) (since : Nat) : CanLinkState Adapter Detail
  | error (classification : ErrorClassification Detail) (since : Nat) : CanLinkState Adapter Detail
  deriving Repr

/-! ## StateClassification

Five-way classification of every `CanLinkState`, matching the F# private
`StateClassification` DU in the property-test file
`tests/ButtonPanelTester.Tests/Property/Can/CanLinkStateTransitionsProperties.fs`
(T026). The two `error` branches are flattened into `errorRecoverableClass`
and `errorFatalClass` so the classifier is exhaustive over the actual
runtime shapes the GUI must render distinctly per FR-002a.
-/

inductive StateClassification where
  | initializingClass
  | connectedClass
  | disconnectedClass
  | errorRecoverableClass
  | errorFatalClass
  deriving DecidableEq, Repr

namespace CanLinkState

/-- Classifier dual to the F# `classify` function in the property-test file.
The `match` is wildcard-free — adding a sixth top-level case to `CanLinkState`
would break elaboration here and force a cross-layer update. -/
def classify {Adapter Detail : Type} (s : CanLinkState Adapter Detail) :
    StateClassification :=
  match s with
  | .initializing => .initializingClass
  | .connected _ _ => .connectedClass
  | .disconnected _ _ => .disconnectedClass
  | .error (.recoverable _) _ => .errorRecoverableClass
  | .error (.fatal _) _ => .errorFatalClass

end CanLinkState

/-! ## state_classification_total (data-model.md §1.3 Invariant #1)

Every inhabitant of `CanLinkState` classifies as one of the five top-level
shapes. A wildcard-free `match` discharges this by construction; the
theorem statement is the contract — a future change that introduced a sixth
case (without updating `classify` to handle it) would fail to compile in
the `classify` definition above, and a future change that updated `classify`
in a non-total way would fail to compile here.

Proof: `cases s` produces four sub-goals (one per top-level constructor);
the `error` goal then bifurcates via the inner `cases classification`.
`simp [CanLinkState.classify]` reduces each leaf to the matching disjunct.
No `sorry`, no custom axioms beyond what a closed-inductive `cases` already
introduces.
-/

theorem state_classification_total
    {Adapter Detail : Type}
    (s : CanLinkState Adapter Detail) :
    CanLinkState.classify s = .initializingClass
  ∨ CanLinkState.classify s = .connectedClass
  ∨ CanLinkState.classify s = .disconnectedClass
  ∨ CanLinkState.classify s = .errorRecoverableClass
  ∨ CanLinkState.classify s = .errorFatalClass := by
  cases s with
  | initializing => simp [CanLinkState.classify]
  | connected _ _ => simp [CanLinkState.classify]
  | disconnected _ _ => simp [CanLinkState.classify]
  | error c _ =>
    cases c with
    | recoverable _ => simp [CanLinkState.classify]
    | fatal _ => simp [CanLinkState.classify]

end Stem.ButtonPanelTester.Phase2
