/-
T017 — Lean Phase-3 module for the `BaptismSequence` FSM.

Mechanises the baptism-attempt state machine of
`specs/004-baptism-workflow/data-model.md` §4: states and transitions (§4.1),
the six-case outcome DU (§4.2), and the event inputs (§4.3). One
technician-initiated claim of the selected panel as the chosen variant; the
tool holds no memory between attempts (FR-013).

The model is a PURE TOTAL step function over (attempt config, state, event)
returning the next state AND the action to perform. The action channel is what
makes FR-004 stateable at code level: `sendAssign` (the SET_ADDRESS write) is
produced by exactly one arm — the validated-match transition out of
`awaitingAnnounce` — and `no_assignment_without_match` proves that arm is the
only source. Time is abstract `Nat` (Phase-2 `Pruning` convention: the unit is
irrelevant to the proofs; milliseconds is the natural reading). The 6 s wait
budget (`announceBudget`, research R4 — a settled scope pin) is anchored at
CLAIM-WRITE COMPLETION: the deadline is computed from the `writeCompleted`
instant that enters `awaitingAnnounce` (CHK010).

Normative semantics carried by `step` (data-model §4.1/§4.3):
  * terminal states absorb every event (terminal-state idempotence, §4.3
    thread-safety note) — in particular a matching announcement arriving AFTER
    a reported `waitTimeout` never flips the outcome (FR-005/clarification 4);
  * a foreign-uuid announcement (≠ selected) never transitions ANY state — a
    strict no-op, including in `awaitingAnnounce` (spec edge case; FsCheck
    property `ForeignUuidNeverSatisfiesWait`);
  * link leaving `Connected` ends every non-terminal state in `linkLost`
    (§4.3: `LinkChanged` is consumed in all non-terminal states).

The three §8 theorems ride over this exact state space: `baptize_progress`
(succeeded IFF matching WHO_I_AM within the budget AND both writes complete),
`baptize_outcome_total` (every run driven past its pending write and the
deadline terminates in exactly one of the six outcomes), and
`no_assignment_without_match` (FR-004).

The F# surface lives at `src/ButtonPanelTester.Core/Can/Baptism.fs`
(T019) and MIRRORS the type names and case order here exactly (stem-fp
discipline §10); the FsCheck properties live at
`tests/.../Property/Can/BaptismSequenceProperties.fs` (T020). This Lean
re-statement lands in commit group C1, ahead of the F# surface, per
Constitution Principle I (Lean spec → test → impl).

Constitution Principle I: no `sorry`, no custom axioms.
-/

import Stem.ButtonPanelTester.Phase2.PanelsOnBus
import Stem.ButtonPanelTester.Phase2.PanelObservation

namespace Stem.ButtonPanelTester.Phase3

open Stem.ButtonPanelTester.Phase2 (PanelUuidKey MarketingVariant VariantIdentity)

/- Phase 2 derives only `Repr` for `VariantIdentity`; the variant-match guard in
`step` (and `DecidableEq` on the event/outcome types that embed it) needs
decidable equality, so it is derived here, where the need arises. -/
deriving instance DecidableEq for VariantIdentity

/-! ## announceBudget (research R4, CHK010)

The announce-wait budget: 6 s in abstract `Nat` time units (milliseconds
reading). Firmware re-announce delay is `2000 + (Σ uuid words mod 4000)` ms
∈ [2, 6] s, so the worst-case uuid answers at the very edge — the budget is a
settled scope pin (R4) and FR-005's structured `waitTimeout` covers the tail.
The window is anchored at claim-write completion (CHK010): see the
`claimSent` → `awaitingAnnounce` arm of `step`. No proof below depends on the
numeric value.
-/

def announceBudget : Nat := 6000

/-! ## AttemptConfig

The per-attempt configuration fixed at Baptize-press time (FR-002 guards
ensure exactly one announcing panel is selected): the selected panel's uuid
and the technician-chosen variant. `step` never changes it — one attempt, one
config (FR-013).
-/

structure AttemptConfig where
  selectedUuid : PanelUuidKey
  chosenVariant : MarketingVariant
  deriving DecidableEq, Repr

/-! ## SequenceStep / BaptismOutcome (data-model §4.2)

Exactly six outcomes, in the F#-mirror case order pinned by §4.2 (FR-005).
`unexpectedVariant` carries the announced identity so the GUI names what the
panel actually claimed to be; `transmissionFailure` carries which of the two
writes faulted.
-/

inductive SequenceStep where
  | claimStep
  | assignStep
  deriving DecidableEq, Repr

inductive BaptismOutcome where
  | succeeded
  | waitTimeout
  | unexpectedVariant (announced : VariantIdentity)
  | panelDisappeared
  | linkLost
  | transmissionFailure (step : SequenceStep)
  deriving DecidableEq, Repr

/-! ## BaptismState (data-model §4.1)

`idle → claimSent → awaitingAnnounce → assigning → terminal`. The
`awaitingAnnounce` state CARRIES its deadline (entry instant + budget): the
6 s window is anchored at claim-write completion, the transition that enters
it (CHK010). Terminal states carry the outcome — the six `Failed_*` /
`Succeeded` sinks of the §4.1 diagram collapse into `terminal outcome`.
-/

inductive BaptismState where
  | idle
  | claimSent
  | awaitingAnnounce (deadline : Nat)
  | assigning
  | terminal (outcome : BaptismOutcome)
  deriving DecidableEq, Repr

/-! ## BaptismEvent (data-model §4.3)

The five observable inputs, in the §4.3 row order. `panelsChanged` abstracts
the `IPanelDiscoveryService` snapshot to the one bit the FSM consumes (is the
selected uuid still present); `linkChanged` likewise (is the link still
`Connected`). `tick` and `writeCompleted` carry the clock instant.
-/

inductive BaptismEvent where
  | announcementHeard (uuid : PanelUuidKey) (variant : VariantIdentity)
  | tick (now : Nat)
  | panelsChanged (selectedPresent : Bool)
  | linkChanged (connected : Bool)
  | writeCompleted (now : Nat)
  | writeFaulted
  deriving DecidableEq, Repr

/-! ## BaptismAction

The effect channel of `step`: what the service must transmit after the
transition. `sendClaim` is the WHO_ARE_YOU claim write (produced only by
`start`); `sendAssign` is the SET_ADDRESS write (produced only by the
validated-match arm — `no_assignment_without_match`). Everything else is
`none`.
-/

inductive BaptismAction where
  | none
  | sendClaim
  | sendAssign
  deriving DecidableEq, Repr

/-! ## start (§4.1 `Idle → ClaimSent`)

The Baptize-press transition. The FR-002 enablement guards are upstream scope
(`Phase3/Enablement.lean`, T027): once they pass, the attempt enters
`claimSent` and the service performs the WHO_ARE_YOU claim write.
-/

def start : BaptismState × BaptismAction := (.claimSent, .sendClaim)

/-! ## step (data-model §4.1 transition table)

Pure total transition function. Per-state event handling:

  * `terminal` — absorbs everything (terminal-state idempotence; a late
    matching announcement after `waitTimeout` never flips the outcome);
  * `idle` — inert except link loss (§4.3: `LinkChanged` in all non-terminal
    states); a run (`run`) starts past `idle`, see `start`;
  * `claimSent` — claim write resolves: completion at `now` opens the announce
    window with `deadline = now + announceBudget` (CHK010 anchor); fault ends
    in `transmissionFailure claimStep`;
  * `awaitingAnnounce deadline` — the §4.1 wait: a selected-uuid announcement
    with the chosen variant advances to `assigning` and emits `sendAssign`
    (FR-004); a selected-uuid announcement with any other identity ends in
    `unexpectedVariant`; a FOREIGN uuid is a strict no-op; a tick at/past the
    deadline ends in `waitTimeout` (FR-005); the selected uuid pruned away
    ends in `panelDisappeared`;
  * `assigning` — assign write resolves: completion is `succeeded` (FR-006),
    fault is `transmissionFailure assignStep`.
-/

def step (cfg : AttemptConfig) (state : BaptismState) (event : BaptismEvent) :
    BaptismState × BaptismAction :=
  match state with
  | .terminal outcome => (.terminal outcome, .none)
  | .idle =>
    match event with
    | .linkChanged false => (.terminal .linkLost, .none)
    | _ => (.idle, .none)
  | .claimSent =>
    match event with
    | .writeCompleted now => (.awaitingAnnounce (now + announceBudget), .none)
    | .writeFaulted => (.terminal (.transmissionFailure .claimStep), .none)
    | .linkChanged false => (.terminal .linkLost, .none)
    | _ => (.claimSent, .none)
  | .awaitingAnnounce deadline =>
    match event with
    | .announcementHeard uuid variant =>
      if uuid = cfg.selectedUuid then
        if variant = .marketing cfg.chosenVariant then
          (.assigning, .sendAssign)
        else
          (.terminal (.unexpectedVariant variant), .none)
      else
        (.awaitingAnnounce deadline, .none)
    | .tick now =>
      if deadline ≤ now then (.terminal .waitTimeout, .none)
      else (.awaitingAnnounce deadline, .none)
    | .panelsChanged false => (.terminal .panelDisappeared, .none)
    | .linkChanged false => (.terminal .linkLost, .none)
    | _ => (.awaitingAnnounce deadline, .none)
  | .assigning =>
    match event with
    | .writeCompleted _ => (.terminal .succeeded, .none)
    | .writeFaulted => (.terminal (.transmissionFailure .assignStep), .none)
    | .linkChanged false => (.terminal .linkLost, .none)
    | _ => (.assigning, .none)

/-! ## runFrom / run

A run is a left fold of `step` (state component) over the observed event
list — the same function-shaped, no-Finmap style as Phase 2's models. `run`
starts an attempt at `start`'s state (`claimSent`, the post-press state): the
`idle → claimSent` edge is `start` itself, so `idle` is unreachable inside a
run.
-/

def runFrom (cfg : AttemptConfig) (state : BaptismState)
    (events : List BaptismEvent) : BaptismState :=
  events.foldl (fun s e => (step cfg s e).fst) state

def run (cfg : AttemptConfig) (events : List BaptismEvent) : BaptismState :=
  runFrom cfg start.fst events

@[simp] theorem runFrom_nil (cfg : AttemptConfig) (s : BaptismState) :
    runFrom cfg s [] = s := rfl

@[simp] theorem runFrom_cons (cfg : AttemptConfig) (s : BaptismState)
    (e : BaptismEvent) (es : List BaptismEvent) :
    runFrom cfg s (e :: es) = runFrom cfg (step cfg s e).fst es := rfl

theorem runFrom_append (cfg : AttemptConfig) (s : BaptismState)
    (xs ys : List BaptismEvent) :
    runFrom cfg s (xs ++ ys) = runFrom cfg (runFrom cfg s xs) ys := by
  simp [runFrom, List.foldl_append]

theorem run_eq_runFrom (cfg : AttemptConfig) (events : List BaptismEvent) :
    run cfg events = runFrom cfg .claimSent events := rfl

theorem run_append (cfg : AttemptConfig) (xs ys : List BaptismEvent) :
    run cfg (xs ++ ys) = runFrom cfg (run cfg xs) ys :=
  runFrom_append cfg start.fst xs ys

/-! ## terminal_absorbs (terminal-state idempotence, data-model §4.3)

A terminal state ignores every further event — the run-level form of the
service's "a terminal state ignores all further events" serialization rule.
This is also the formal carrier of FR-005/clarification 4: once `waitTimeout`
(or any outcome) is reported, a late matching announcement is just another
absorbed event — the tool NEVER flips a reported failure to success.
-/

@[simp] theorem terminal_absorbs (cfg : AttemptConfig) (outcome : BaptismOutcome)
    (events : List BaptismEvent) :
    runFrom cfg (.terminal outcome) events = .terminal outcome := by
  induction events with
  | nil => rfl
  | cons e es ih => exact ih

/-! ## foreign_uuid_never_transitions (spec edge case; FsCheck
`ForeignUuidNeverSatisfiesWait`)

An announcement from a foreign uuid (≠ selected) is a strict no-op in EVERY
state — including `awaitingAnnounce`, where only the selected uuid can either
satisfy the wait or end it in `unexpectedVariant`.
-/

theorem foreign_uuid_never_transitions (cfg : AttemptConfig) (state : BaptismState)
    (uuid : PanelUuidKey) (variant : VariantIdentity)
    (h : uuid ≠ cfg.selectedUuid) :
    step cfg state (.announcementHeard uuid variant) = (state, .none) := by
  cases state <;> simp [step, h]

/-! ## closingSchedule

The canonical event suffix that drives any state past its pending work: the
outstanding write resolves (`writeCompleted`), then the clock passes the
deadline (`tick`). This is the model of the operational guarantee that every
attempt ends — the write Tasks always resolve and the clock always ticks. The
`idle` arm (link loss) exists only for totality: `idle` is unreachable inside
a `run` (see `runFrom`/`run`).
-/

def closingSchedule : BaptismState → List BaptismEvent
  | .idle => [.linkChanged false]
  | .claimSent => [.writeCompleted 0, .tick announceBudget]
  | .awaitingAnnounce deadline => [.tick deadline]
  | .assigning => [.writeCompleted 0]
  | .terminal _ => []

/-- From every state, its closing schedule reaches a terminal state. -/
theorem closingSchedule_reaches_terminal (cfg : AttemptConfig) (state : BaptismState) :
    ∃ outcome : BaptismOutcome,
      runFrom cfg state (closingSchedule state) = .terminal outcome := by
  cases state with
  | idle => exact ⟨.linkLost, rfl⟩
  | claimSent => exact ⟨.waitTimeout, by simp [closingSchedule, step]⟩
  | awaitingAnnounce deadline => exact ⟨.waitTimeout, by simp [closingSchedule, step]⟩
  | assigning => exact ⟨.succeeded, rfl⟩
  | terminal outcome => exact ⟨outcome, rfl⟩

/-! ## baptize_outcome_total (data-model §4.2 / §8)

Outcome totality: every run, driven past its pending write and the announce
deadline (its `closingSchedule`), terminates in a terminal state carrying ONE
of the six outcomes — the outcome space is exactly the six-case
`BaptismOutcome` DU (§4.2, FR-005) — and that outcome is stable: no further
event list changes it (`terminal_absorbs`), which is the "exactly one outcome
per attempt" half of the claim.
-/

theorem baptize_outcome_total (cfg : AttemptConfig) (events : List BaptismEvent) :
    ∃ outcome : BaptismOutcome,
      run cfg (events ++ closingSchedule (run cfg events)) = .terminal outcome ∧
      ∀ more : List BaptismEvent,
        run cfg ((events ++ closingSchedule (run cfg events)) ++ more)
          = .terminal outcome := by
  obtain ⟨outcome, h⟩ := closingSchedule_reaches_terminal cfg (run cfg events)
  refine ⟨outcome, ?_, ?_⟩
  · rw [run_append]
    exact h
  · intro more
    rw [run_append, run_append, h, terminal_absorbs]

/-! ## Successful-run decomposition lemmas

Soundness direction of `baptize_progress`: a run that ends `succeeded` must
have crossed the three gate transitions, in order — claim write completed
(opening the window), matching announcement (while the window was still
open), assign write completed. Each lemma peels one phase: the prefix
self-loops in the phase state, and the pivot event is forced, because every
other exit from that state lands in a non-`succeeded` terminal state that
absorbs the rest of the run.
-/

/-- A successful run from `claimSent` decomposes at the claim-write
completion: a prefix that stays in `claimSent`, the `writeCompleted` pivot
opening the window at `claimDoneAt + announceBudget`, and a successful
remainder from `awaitingAnnounce`. -/
theorem succeeded_through_claim_write (cfg : AttemptConfig)
    (events : List BaptismEvent) :
    runFrom cfg .claimSent events = .terminal .succeeded →
    ∃ (pre : List BaptismEvent) (claimDoneAt : Nat) (post : List BaptismEvent),
      events = pre ++ .writeCompleted claimDoneAt :: post ∧
      runFrom cfg .claimSent pre = .claimSent ∧
      runFrom cfg (.awaitingAnnounce (claimDoneAt + announceBudget)) post
        = .terminal .succeeded := by
  induction events with
  | nil => intro h; simp at h
  | cons e es ih =>
    intro h
    cases e with
    | announcementHeard uuid variant =>
      obtain ⟨pre, claimDoneAt, post, heq, hpre, hpost⟩ := ih h
      exact ⟨.announcementHeard uuid variant :: pre, claimDoneAt, post,
        by simp [heq], hpre, hpost⟩
    | tick now =>
      obtain ⟨pre, claimDoneAt, post, heq, hpre, hpost⟩ := ih h
      exact ⟨.tick now :: pre, claimDoneAt, post, by simp [heq], hpre, hpost⟩
    | panelsChanged selectedPresent =>
      obtain ⟨pre, claimDoneAt, post, heq, hpre, hpost⟩ := ih h
      exact ⟨.panelsChanged selectedPresent :: pre, claimDoneAt, post,
        by simp [heq], hpre, hpost⟩
    | linkChanged connected =>
      cases connected with
      | false => simp [step] at h
      | true =>
        obtain ⟨pre, claimDoneAt, post, heq, hpre, hpost⟩ := ih h
        exact ⟨.linkChanged true :: pre, claimDoneAt, post, by simp [heq], hpre, hpost⟩
    | writeCompleted now => exact ⟨[], now, es, rfl, rfl, h⟩
    | writeFaulted => simp [step] at h

/-- A successful run from `awaitingAnnounce` decomposes at the matching
announcement: a prefix that keeps the window open (every tick before the
deadline, panel still present, link up, foreign uuids ignored), the
`announcementHeard (selected, chosen)` pivot (FR-004), and a successful
remainder from `assigning`. -/
theorem succeeded_through_matching_announcement (cfg : AttemptConfig)
    (deadline : Nat) (events : List BaptismEvent) :
    runFrom cfg (.awaitingAnnounce deadline) events = .terminal .succeeded →
    ∃ (pre post : List BaptismEvent),
      events = pre
          ++ .announcementHeard cfg.selectedUuid (.marketing cfg.chosenVariant)
          :: post ∧
      runFrom cfg (.awaitingAnnounce deadline) pre = .awaitingAnnounce deadline ∧
      runFrom cfg .assigning post = .terminal .succeeded := by
  induction events with
  | nil => intro h; simp at h
  | cons e es ih =>
    intro h
    cases e with
    | announcementHeard uuid variant =>
      by_cases huuid : uuid = cfg.selectedUuid
      · by_cases hvariant : variant = .marketing cfg.chosenVariant
        · subst huuid; subst hvariant
          exact ⟨[], es, rfl, rfl, by simpa [step] using h⟩
        · subst huuid; simp [step, hvariant] at h
      · simp only [runFrom_cons,
          foreign_uuid_never_transitions cfg _ uuid variant huuid] at h
        obtain ⟨pre, post, heq, hpre, hpost⟩ := ih h
        refine ⟨.announcementHeard uuid variant :: pre, post, by simp [heq], ?_, hpost⟩
        simp only [runFrom_cons,
          foreign_uuid_never_transitions cfg _ uuid variant huuid]
        exact hpre
    | tick now =>
      by_cases hd : deadline ≤ now
      · simp [step, hd] at h
      · simp only [runFrom_cons] at h
        rw [show step cfg (.awaitingAnnounce deadline) (.tick now)
              = (.awaitingAnnounce deadline, .none) by simp [step, hd]] at h
        obtain ⟨pre, post, heq, hpre, hpost⟩ := ih h
        refine ⟨.tick now :: pre, post, by simp [heq], ?_, hpost⟩
        rw [runFrom_cons,
          show step cfg (.awaitingAnnounce deadline) (.tick now)
            = (.awaitingAnnounce deadline, .none) by simp [step, hd]]
        exact hpre
    | panelsChanged selectedPresent =>
      cases selectedPresent with
      | false => simp [step] at h
      | true =>
        obtain ⟨pre, post, heq, hpre, hpost⟩ := ih h
        exact ⟨.panelsChanged true :: pre, post, by simp [heq], hpre, hpost⟩
    | linkChanged connected =>
      cases connected with
      | false => simp [step] at h
      | true =>
        obtain ⟨pre, post, heq, hpre, hpost⟩ := ih h
        exact ⟨.linkChanged true :: pre, post, by simp [heq], hpre, hpost⟩
    | writeCompleted now =>
      obtain ⟨pre, post, heq, hpre, hpost⟩ := ih h
      exact ⟨.writeCompleted now :: pre, post, by simp [heq], hpre, hpost⟩
    | writeFaulted =>
      obtain ⟨pre, post, heq, hpre, hpost⟩ := ih h
      exact ⟨.writeFaulted :: pre, post, by simp [heq], hpre, hpost⟩

/-- A successful run from `assigning` decomposes at the assign-write
completion: a prefix that stays in `assigning`, then the `writeCompleted`
pivot — the FR-006 success signal. -/
theorem succeeded_through_assign_write (cfg : AttemptConfig)
    (events : List BaptismEvent) :
    runFrom cfg .assigning events = .terminal .succeeded →
    ∃ (pre : List BaptismEvent) (assignDoneAt : Nat) (tail : List BaptismEvent),
      events = pre ++ .writeCompleted assignDoneAt :: tail ∧
      runFrom cfg .assigning pre = .assigning := by
  induction events with
  | nil => intro h; simp at h
  | cons e es ih =>
    intro h
    cases e with
    | announcementHeard uuid variant =>
      obtain ⟨pre, assignDoneAt, tail, heq, hpre⟩ := ih h
      exact ⟨.announcementHeard uuid variant :: pre, assignDoneAt, tail,
        by simp [heq], hpre⟩
    | tick now =>
      obtain ⟨pre, assignDoneAt, tail, heq, hpre⟩ := ih h
      exact ⟨.tick now :: pre, assignDoneAt, tail, by simp [heq], hpre⟩
    | panelsChanged selectedPresent =>
      obtain ⟨pre, assignDoneAt, tail, heq, hpre⟩ := ih h
      exact ⟨.panelsChanged selectedPresent :: pre, assignDoneAt, tail,
        by simp [heq], hpre⟩
    | linkChanged connected =>
      cases connected with
      | false => simp [step] at h
      | true =>
        obtain ⟨pre, assignDoneAt, tail, heq, hpre⟩ := ih h
        exact ⟨.linkChanged true :: pre, assignDoneAt, tail, by simp [heq], hpre⟩
    | writeCompleted now => exact ⟨[], now, es, rfl, rfl⟩
    | writeFaulted => simp [step] at h

/-! ## baptize_progress (data-model §4.1 / §8)

Progress: a run reaches `succeeded` IFF a WHO_I_AM matching the selected uuid
AND the chosen variant is observed within the budget AND both writes
complete. The right-hand side is the canonical successful-trace shape:

  * `claimPending ++ [writeCompleted claimDoneAt]` — the claim write
    completes (first "write completes" conjunct), opening the announce window
    anchored at `claimDoneAt` (CHK010): deadline `claimDoneAt +
    announceBudget`;
  * `waitWindow` keeps the run in `awaitingAnnounce` — operationally "within
    the budget": no tick at/past the deadline, the panel stays present, the
    link stays up, and foreign announcements are no-ops;
  * the matching `announcementHeard` pivot — the FR-004 validated match;
  * `assignPending ++ [writeCompleted assignDoneAt]` — the assign write
    completes (second conjunct, FR-006); `tail` is absorbed by the terminal
    state.
-/

theorem baptize_progress (cfg : AttemptConfig) (events : List BaptismEvent) :
    run cfg events = .terminal .succeeded ↔
      ∃ (claimPending waitWindow assignPending tail : List BaptismEvent)
        (claimDoneAt assignDoneAt : Nat),
        events = claimPending ++ .writeCompleted claimDoneAt
            :: (waitWindow
              ++ .announcementHeard cfg.selectedUuid (.marketing cfg.chosenVariant)
              :: (assignPending ++ .writeCompleted assignDoneAt :: tail)) ∧
        runFrom cfg .claimSent claimPending = .claimSent ∧
        runFrom cfg (.awaitingAnnounce (claimDoneAt + announceBudget)) waitWindow
          = .awaitingAnnounce (claimDoneAt + announceBudget) ∧
        runFrom cfg .assigning assignPending = .assigning := by
  constructor
  · intro h
    obtain ⟨claimPending, claimDoneAt, post₁, heq₁, h1, hpost₁⟩ :=
      succeeded_through_claim_write cfg events (by rwa [run_eq_runFrom] at h)
    obtain ⟨waitWindow, post₂, heq₂, h2, hpost₂⟩ :=
      succeeded_through_matching_announcement cfg (claimDoneAt + announceBudget)
        post₁ hpost₁
    obtain ⟨assignPending, assignDoneAt, tail, heq₃, h3⟩ :=
      succeeded_through_assign_write cfg post₂ hpost₂
    exact ⟨claimPending, waitWindow, assignPending, tail, claimDoneAt, assignDoneAt,
      by rw [heq₁, heq₂, heq₃], h1, h2, h3⟩
  · rintro ⟨claimPending, waitWindow, assignPending, tail, claimDoneAt, assignDoneAt,
      heq, h1, h2, h3⟩
    subst heq
    simp [run_eq_runFrom, runFrom_append, h1, h2, h3, step]

/-! ## no_assignment_without_match (FR-004 / §8)

The `sendAssign` action (the SET_ADDRESS write) is unreachable unless the
event is an `announcementHeard` matching BOTH the selected uuid and the
chosen variant while the FSM sits in `awaitingAnnounce`. Stated over the
action channel of `step`, which is exactly what the service executes — no
state inspection can fake an assignment.
-/

theorem no_assignment_without_match (cfg : AttemptConfig) (state : BaptismState)
    (event : BaptismEvent)
    (h : (step cfg state event).snd = .sendAssign) :
    ∃ deadline : Nat,
      state = .awaitingAnnounce deadline ∧
      event = .announcementHeard cfg.selectedUuid (.marketing cfg.chosenVariant) := by
  cases state with
  | idle =>
    exfalso
    cases event with
    | panelsChanged selectedPresent => cases selectedPresent <;> simp [step] at h
    | linkChanged connected => cases connected <;> simp [step] at h
    | _ => simp [step] at h
  | claimSent =>
    exfalso
    cases event with
    | panelsChanged selectedPresent => cases selectedPresent <;> simp [step] at h
    | linkChanged connected => cases connected <;> simp [step] at h
    | _ => simp [step] at h
  | awaitingAnnounce deadline =>
    cases event with
    | announcementHeard uuid variant =>
      by_cases huuid : uuid = cfg.selectedUuid
      · by_cases hvariant : variant = .marketing cfg.chosenVariant
        · subst huuid; subst hvariant
          exact ⟨deadline, rfl, rfl⟩
        · simp [step, huuid, hvariant] at h
      · simp [step, huuid] at h
    | tick now =>
      exfalso
      by_cases hd : deadline ≤ now <;> simp [step, hd] at h
    | panelsChanged selectedPresent =>
      exfalso; cases selectedPresent <;> simp [step] at h
    | linkChanged connected =>
      exfalso; cases connected <;> simp [step] at h
    | _ => exfalso; simp [step] at h
  | assigning =>
    exfalso
    cases event with
    | panelsChanged selectedPresent => cases selectedPresent <;> simp [step] at h
    | linkChanged connected => cases connected <;> simp [step] at h
    | _ => simp [step] at h
  | terminal outcome => exfalso; simp [step] at h

end Stem.ButtonPanelTester.Phase3
