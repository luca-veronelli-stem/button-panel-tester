/-
T024 — Lean Phase-1 module for `DictionarySource`.

Mechanises Invariant #2 of `specs/001-fetch-dictionary/data-model.md` §1.2:
"A `Live -> Cached` transition (refresh failed mid-session) preserves the
in-memory `ButtonPanelDictionary` byte-for-byte; only the wrapper changes."

The F# `DictionarySource` is a *label*, not a container — the dictionary
itself is held separately on `IDictionaryService.Snapshot :
(ButtonPanelDictionary * DictionarySource) voption`
(`specs/001-fetch-dictionary/data-model.md` §3). The theorem is therefore
a statement about the pair `(Dict, DictionarySource)`: when the wrapper
changes from `Live t` to `Cached (t', origin, lastFailure)`, the
dictionary component of the pair is unchanged.

Constitution Principle I (no `sorry`, no custom axioms;
`specs/001-fetch-dictionary/plan.md` line 35-36): the proof is by `rfl`,
arising directly from the definition of the re-label function. The
theorem statement is the design contract — the re-label function is
*defined* to leave the dictionary component fixed, and the theorem
locks that contract in at the spec level.

Polymorphism in the failure-reason parameter `Failure` keeps T024 and T025
(`Phase1/FetchFailureReason.lean`) independent — the two files carry the
`[P]` parallel marker in `specs/001-fetch-dictionary/tasks.md`. F# wire-up
instantiates `Failure = Core.Dictionary.FetchFailureReason`.
-/

namespace Stem.ButtonPanelTester.Phase1

/-! ## CacheOrigin

Two-way provenance taxonomy per `data-model.md` §1.2:
  * `fromEmbeddedSeed` — seed shipped inside the binary.
  * `fromLocalFile`    — on-disk cache from a prior successful live fetch.

Structurally equivalent to the F# `Stem.ButtonPanelTester.Core.Dictionary.CacheOrigin`
DU shipped in T012 (`src/ButtonPanelTester.Core/Dictionary/DictionarySource.fs`).
-/

inductive CacheOrigin where
  | fromEmbeddedSeed
  | fromLocalFile
  deriving DecidableEq, Repr

/-! ## DictionarySource

The provenance label that wraps the in-memory dictionary. Two cases,
matching the F# `DictionarySource` DU shipped in T012:

  * `live   t`               — the in-memory dictionary came from a
                               successful live fetch at time `t`.
  * `cached t origin lf`     — the in-memory dictionary came from the
                               cache (origin = embedded seed or local
                               file); `lf` is the most-recent-failure
                               reason for the status-row detail
                               affordance (`None` if no attempt yet
                               failed, `Some r` otherwise).

The `Failure` parameter abstracts over `FetchFailureReason`: at the F# layer
this is the eight-case closed DU mechanised independently in T025
(`Phase1/FetchFailureReason.lean`). Keeping `Failure` polymorphic here means
T024 and T025 do not need to be landed in a particular order — the [P]
marker in `tasks.md:77,78` is respected.

`Nat` stands in for `DateTimeOffset`. The invariant proved below does
not constrain the timestamp value, only the dictionary slot of the pair.
-/

inductive DictionarySource (Failure : Type) where
  | live   (fetchedAt : Nat) : DictionarySource Failure
  | cached (fetchedAt : Nat) (origin : CacheOrigin)
           (lastFailure : Option Failure) : DictionarySource Failure
  deriving Repr

namespace DictionarySource

/-! ## Re-label: `Live -> Cached`

`relabelLiveToCached d t origin lastFailure` takes a `(dictionary,
Live _)` pair and produces a `(dictionary, Cached _ _ _)` pair with the
SAME dictionary in slot 1. The dictionary is opaque (`Dict` parameter):
the theorem is about the slot, not the content.

This is the operational form of Invariant #2: the F# call site that
moves the source label from `Live` to `Cached` on a mid-session refresh
failure does so via a pure relabel — the in-memory dictionary value
flows through unchanged.
-/

def relabelLiveToCached
    {Dict Failure : Type}
    (d : Dict)
    (newFetchedAt : Nat)
    (origin : CacheOrigin)
    (lastFailure : Option Failure) :
    Dict × DictionarySource Failure :=
  (d, DictionarySource.cached newFetchedAt origin lastFailure)

end DictionarySource

/-! ## source_data_preserved (data-model.md §1.2 Invariant #2)

The mechanised statement of Invariant #2: starting from a `Live`-labelled
dictionary pair `(d, live oldFetchedAt)`, applying `relabelLiveToCached`
yields a pair whose dictionary component is byte-identical to the original
dictionary `d`. Only the source wrapper changes.

The proof is `rfl`: `relabelLiveToCached` is *defined* to put `d` in slot 1
of the returned pair. The theorem is the contract — the function shape is
what makes Invariant #2 hold by construction, and the theorem statement
pins that down so a future change to `relabelLiveToCached` that violated
the invariant would fail to compile here.
-/

theorem source_data_preserved
    {Dict Failure : Type}
    (d : Dict)
    (oldFetchedAt newFetchedAt : Nat)
    (origin : CacheOrigin)
    (lastFailure : Option Failure) :
    let liveSrc : Dict × DictionarySource Failure :=
      (d, DictionarySource.live oldFetchedAt)
    let cachedSrc : Dict × DictionarySource Failure :=
      DictionarySource.relabelLiveToCached d newFetchedAt origin lastFailure
    liveSrc.1 = cachedSrc.1 := by
  rfl

end Stem.ButtonPanelTester.Phase1
