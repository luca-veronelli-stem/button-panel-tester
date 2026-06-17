/-
T017 / RW01 — Lean Phase-3 module for the `BaptismSequence` FSM.

Mechanises the baptism-attempt state machine of
`specs/004-baptism-workflow/confirmation-rework/data-model.md` §4 (which
supersedes §4.1/§4.2/§4.3/§4.4 of the parent `data-model.md`): states and
transitions (§4.1), the seven-case outcome DU (§4.2), and the event inputs
(§4.3). One technician-initiated claim of the selected panel as the chosen
variant; the tool holds no memory between attempts (FR-013).

The model is a PURE TOTAL step function over (attempt config, state, event)
returning the next state AND the action to perform. The action channel is what
makes FR-004 stateable at code level: `sendAssign` (the SET_ADDRESS write) is
produced by exactly one arm — the validated-match transition out of
`awaitingAnnounce` — and `no_assignment_without_match` proves that arm is the
only source. Time is abstract `Nat` (Phase-2 `Pruning` convention: the unit is
irrelevant to the proofs; milliseconds is the natural reading). The two wait
budgets (`announceBudget`/`adoptionBudget`, research R4 — settled scope pins)
are anchored at the two WRITE COMPLETIONS: the announce window opens at the
claim-write `writeCompleted` instant (CHK010), the adoption window at the
assign-write `writeCompleted` instant.

The confirmation-model rework (F1 + F6, data-model confirmation-rework
§"Why the shipped model was wrong"):
  * F1 — in `awaitingAnnounce`, a selected-uuid re-announce as `virgin` (0xFF)
    is the panel still mid-cycle, not a rejection: the FSM KEEPS WAITING.
    `unexpectedVariant` now fires only on a different non-virgin variant
    (`marketing other` / `unknown`). Firmware gates the claim on fwType only.
  * F6 — the assign write completing no longer SUCCEEDS the attempt: it opens
    the adoption-confirmation window (`awaitingAdoption`). Success is reached
    only when the `0x25` application ACK was observed (`setAddressAcked` ⇒
    `ackSeen`) AND broadcast silence held (no selected-uuid re-announce) until
    a tick past the adoption deadline. A selected-uuid re-announce inside the
    window, or the deadline elapsing without an ACK, is `claimNotAdopted`
    (FR-006a) — never a false success (D2 strict gate).

Normative semantics carried by `step` (data-model §4.1/§4.3):
  * terminal states absorb every event (terminal-state idempotence, §4.3
    thread-safety note) — in particular a matching announcement arriving AFTER
    a reported `waitTimeout` never flips the outcome (FR-005/clarification 4);
  * a foreign-uuid announcement (≠ selected) never transitions ANY state — a
    strict no-op, including in `awaitingAnnounce` and `awaitingAdoption` (spec
    edge case; FsCheck property `ForeignUuidNeverSatisfiesWait`);
  * link leaving `Connected` ends every non-terminal state in `linkLost`
    (§4.3: `LinkChanged` is consumed in all non-terminal states).

The §8 theorems ride over this exact state space: `baptize_progress`
(succeeded IFF matching WHO_I_AM within the budget AND both writes complete AND
the ACK is observed AND silence holds to the adoption deadline),
`baptize_outcome_total` (every run driven past its pending work and the
deadlines terminates in exactly one of the seven outcomes),
`no_assignment_without_match` (FR-004), plus `virgin_keeps_waiting` (F1) and
`no_success_without_adoption` (the formal carrier of "never a false success on
write-completion", FR-006).

The F# surface lives at `src/ButtonPanelTester.Core/Can/Baptism.fs`
(RW02) and MIRRORS the type names and case order here exactly (stem-fp
discipline §10); the FsCheck properties live at
`tests/.../Property/Can/BaptismSequenceProperties.fs` (RW02). This Lean
re-statement lands first, per Constitution Principle I (Lean spec → test → impl).

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

/-! ## announceBudget / adoptionBudget (research R4, CHK010)

Two 6 s windows in abstract `Nat` time units (milliseconds reading), the two
sequential waits of the corrected model (SC-001 bounds a definitive outcome by
their sum):

  * `announceBudget` — the announce wait, anchored at claim-write completion
    (CHK010): see the `claimSent → awaitingAnnounce` arm of `step`. Firmware
    re-announce delay is `2000 + (Σ uuid words mod 4000)` ms ∈ [2, 6] s, so the
    worst-case uuid answers at the very edge — a settled scope pin (R4),
    FR-005's structured `waitTimeout` covers the tail.
  * `adoptionBudget` — the adoption-confirmation wait, anchored at assign-write
    completion: see the `assigning → awaitingAdoption` arm. One worst-case
    announce period — a panel that adopted is silent immediately; the window
    only has to outlast one announce period to prove the silence is real, not a
    gap between announcements.

No proof below depends on either numeric value.
-/

def announceBudget : Nat := 6000

def adoptionBudget : Nat := 6000

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

Exactly seven outcomes, in the F#-mirror case order pinned by §4.2
(FR-005/FR-006a). `unexpectedVariant` carries the announced identity so the GUI
names what the panel actually claimed to be; `claimNotAdopted` carries no
payload (its FR-015 recovery guidance is fixed); `transmissionFailure` carries
which of the two writes faulted.
-/

inductive SequenceStep where
  | claimStep
  | assignStep
  deriving DecidableEq, Repr

inductive BaptismOutcome where
  | succeeded
  | waitTimeout
  | unexpectedVariant (announced : VariantIdentity)
  | claimNotAdopted
  | panelDisappeared
  | linkLost
  | transmissionFailure (step : SequenceStep)
  deriving DecidableEq, Repr

/-! ## BaptismState (data-model §4.1)

`idle → claimSent → awaitingAnnounce → assigning → awaitingAdoption →
terminal`. Two waiting states CARRY their deadline (entry instant + budget):
`awaitingAnnounce` the announce window anchored at claim-write completion
(CHK010), `awaitingAdoption` the adoption window anchored at assign-write
completion. `awaitingAdoption` also carries `ackSeen` — whether the `0x25`
application ACK (`setAddressAcked`) has been observed yet; success requires it.
Terminal states carry the outcome — the seven `Failed_*` / `Succeeded` sinks of
the §4.1 diagram collapse into `terminal outcome`.
-/

inductive BaptismState where
  | idle
  | claimSent
  | awaitingAnnounce (deadline : Nat)
  | assigning
  | awaitingAdoption (deadline : Nat) (ackSeen : Bool)
  | terminal (outcome : BaptismOutcome)
  deriving DecidableEq, Repr

/-! ## BaptismEvent (data-model §4.3)

The observable inputs, in the §4.3 row order. `panelsChanged` abstracts the
`IPanelDiscoveryService` snapshot to the one bit the FSM consumes (is the
selected uuid still present); `linkChanged` likewise (is the link still
`Connected`). `tick` and `writeCompleted` carry the clock instant.
`setAddressAcked` (NEW) is the RX observation of the `0x25` application ACK
addressed to the tool — the adoption fast-positive consumed in
`awaitingAdoption`.
-/

inductive BaptismEvent where
  | announcementHeard (uuid : PanelUuidKey) (variant : VariantIdentity)
  | tick (now : Nat)
  | panelsChanged (selectedPresent : Bool)
  | linkChanged (connected : Bool)
  | writeCompleted (now : Nat)
  | writeFaulted
  | setAddressAcked
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
    branches three ways on the variant (F1) — the chosen marketing variant
    advances to `assigning` and emits `sendAssign` (FR-004); `virgin` (a panel
    still mid-cycle) KEEPS WAITING; any other non-virgin variant ends in
    `unexpectedVariant`. A FOREIGN uuid is a strict no-op; a tick at/past the
    deadline ends in `waitTimeout` (FR-005); the selected uuid pruned away ends
    in `panelDisappeared`;
  * `assigning` — assign write resolves: completion at `now` opens the adoption
    window with `deadline = now + adoptionBudget`, `ackSeen = false` (F6 — NOT
    `succeeded`); fault is `transmissionFailure assignStep`;
  * `awaitingAdoption deadline ackSeen` — the §4.1 confirmation wait:
    `setAddressAcked` records the ACK (`ackSeen := true`); a selected-uuid
    re-announce means the panel is still announcing, i.e. NOT adopted →
    `claimNotAdopted` (FR-006a); a foreign uuid is a strict no-op;
    `panelsChanged false` is a no-op (the just-matched panel cannot prune
    inside the window — silence is the absence of an announce, not a prune); a
    tick at/past the deadline closes the window — `succeeded` iff `ackSeen`
    (ACK + held silence, FR-006), else `claimNotAdopted` (FR-006a, strict).
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
        match variant with
        | .marketing v =>
          if v = cfg.chosenVariant then
            (.assigning, .sendAssign)
          else
            (.terminal (.unexpectedVariant variant), .none)
        | .virgin => (.awaitingAnnounce deadline, .none)
        | .unknown _ => (.terminal (.unexpectedVariant variant), .none)
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
    | .writeCompleted now => (.awaitingAdoption (now + adoptionBudget) false, .none)
    | .writeFaulted => (.terminal (.transmissionFailure .assignStep), .none)
    | .linkChanged false => (.terminal .linkLost, .none)
    | _ => (.assigning, .none)
  | .awaitingAdoption deadline ackSeen =>
    match event with
    | .setAddressAcked => (.awaitingAdoption deadline true, .none)
    | .announcementHeard uuid _ =>
      if uuid = cfg.selectedUuid then
        (.terminal .claimNotAdopted, .none)
      else
        (.awaitingAdoption deadline ackSeen, .none)
    | .tick now =>
      if deadline ≤ now then
        if ackSeen then (.terminal .succeeded, .none)
        else (.terminal .claimNotAdopted, .none)
      else (.awaitingAdoption deadline ackSeen, .none)
    | .linkChanged false => (.terminal .linkLost, .none)
    | _ => (.awaitingAdoption deadline ackSeen, .none)

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
state — including `awaitingAnnounce` (where only the selected uuid can satisfy
the wait or end it in `unexpectedVariant`) and `awaitingAdoption` (where only
the selected uuid can break the silence into `claimNotAdopted`).
-/

theorem foreign_uuid_never_transitions (cfg : AttemptConfig) (state : BaptismState)
    (uuid : PanelUuidKey) (variant : VariantIdentity)
    (h : uuid ≠ cfg.selectedUuid) :
    step cfg state (.announcementHeard uuid variant) = (state, .none) := by
  cases state <;> simp [step, h]

/-! ## closingSchedule

The canonical event suffix that drives any state past its pending work: the
outstanding writes resolve (`writeCompleted`), then the clocks pass the
deadlines (`tick`). This is the model of the operational guarantee that every
attempt ends — the write Tasks always resolve and the clock always ticks. From
`assigning` the schedule must cross BOTH the assign write AND the adoption
deadline (the assign write lands in `awaitingAdoption`, not a terminal); from
`awaitingAdoption` one tick past the deadline closes it (`succeeded` if
`ackSeen`, else `claimNotAdopted` — either way a terminal). The `idle` arm
(link loss) exists only for totality: `idle` is unreachable inside a `run`.
-/

def closingSchedule : BaptismState → List BaptismEvent
  | .idle => [.linkChanged false]
  | .claimSent => [.writeCompleted 0, .tick announceBudget]
  | .awaitingAnnounce deadline => [.tick deadline]
  | .assigning => [.writeCompleted 0, .tick adoptionBudget]
  | .awaitingAdoption deadline _ => [.tick deadline]
  | .terminal _ => []

/-- From every state, its closing schedule reaches a terminal state. -/
theorem closingSchedule_reaches_terminal (cfg : AttemptConfig) (state : BaptismState) :
    ∃ outcome : BaptismOutcome,
      runFrom cfg state (closingSchedule state) = .terminal outcome := by
  cases state with
  | idle => exact ⟨.linkLost, rfl⟩
  | claimSent => exact ⟨.waitTimeout, by simp [closingSchedule, step]⟩
  | awaitingAnnounce deadline => exact ⟨.waitTimeout, by simp [closingSchedule, step]⟩
  | assigning => exact ⟨.claimNotAdopted, by simp [closingSchedule, step]⟩
  | awaitingAdoption deadline ackSeen =>
    cases ackSeen with
    | true => exact ⟨.succeeded, by simp [closingSchedule, step]⟩
    | false => exact ⟨.claimNotAdopted, by simp [closingSchedule, step]⟩
  | terminal outcome => exact ⟨outcome, rfl⟩

/-! ## baptize_outcome_total (data-model §4.2 / §8)

Outcome totality: every run, driven past its pending writes and the deadlines
(its `closingSchedule`), terminates in a terminal state carrying ONE of the
seven outcomes — the outcome space is exactly the seven-case `BaptismOutcome`
DU (§4.2, FR-005/FR-006a) — and that outcome is stable: no further event list
changes it (`terminal_absorbs`), which is the "exactly one outcome per attempt"
half of the claim.
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
have crossed the gate transitions, in order — claim write completed (opening
the announce window), matching announcement (while the window was still open),
assign write completed (opening the adoption window), the `setAddressAcked`
ACK observed, and a tick past the adoption deadline with the silence unbroken.
Each lemma peels one phase: the prefix self-loops in the phase state, and the
pivot event is forced, because every other exit from that state lands in a
non-`succeeded` terminal state that absorbs the rest of the run.
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
    | setAddressAcked =>
      obtain ⟨pre, claimDoneAt, post, heq, hpre, hpost⟩ := ih h
      exact ⟨.setAddressAcked :: pre, claimDoneAt, post, by simp [heq], hpre, hpost⟩

/-- A successful run from `awaitingAnnounce` decomposes at the matching
announcement: a prefix that keeps the window open (every tick before the
deadline, panel still present, link up, foreign uuids and virgin re-announces
ignored), the `announcementHeard (selected, chosen)` pivot (FR-004), and a
successful remainder from `assigning`. -/
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
      · subst huuid
        cases variant with
        | marketing v =>
          by_cases hv : v = cfg.chosenVariant
          · subst hv
            exact ⟨[], es, rfl, rfl, by simpa [step] using h⟩
          · simp [step, hv] at h
        | virgin =>
          rw [runFrom_cons,
            show step cfg (.awaitingAnnounce deadline)
                (.announcementHeard cfg.selectedUuid .virgin)
                = (.awaitingAnnounce deadline, .none) by simp [step]] at h
          obtain ⟨pre, post, heq, hpre, hpost⟩ := ih h
          refine ⟨.announcementHeard cfg.selectedUuid .virgin :: pre, post,
            by simp [heq], ?_, hpost⟩
          rw [runFrom_cons,
            show step cfg (.awaitingAnnounce deadline)
                (.announcementHeard cfg.selectedUuid .virgin)
                = (.awaitingAnnounce deadline, .none) by simp [step]]
          exact hpre
        | unknown raw => simp [step] at h
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
    | setAddressAcked =>
      obtain ⟨pre, post, heq, hpre, hpost⟩ := ih h
      exact ⟨.setAddressAcked :: pre, post, by simp [heq], hpre, hpost⟩

/-- A successful run from `assigning` decomposes at the assign-write
completion: a prefix that stays in `assigning`, then the `writeCompleted`
pivot — which opens the adoption window (F6: NOT success) — and a successful
remainder from `awaitingAdoption … false`. -/
theorem succeeded_through_assign_write (cfg : AttemptConfig)
    (events : List BaptismEvent) :
    runFrom cfg .assigning events = .terminal .succeeded →
    ∃ (pre : List BaptismEvent) (assignDoneAt : Nat) (tail : List BaptismEvent),
      events = pre ++ .writeCompleted assignDoneAt :: tail ∧
      runFrom cfg .assigning pre = .assigning ∧
      runFrom cfg (.awaitingAdoption (assignDoneAt + adoptionBudget) false) tail
        = .terminal .succeeded := by
  induction events with
  | nil => intro h; simp at h
  | cons e es ih =>
    intro h
    cases e with
    | announcementHeard uuid variant =>
      obtain ⟨pre, assignDoneAt, tail, heq, hpre, hadopt⟩ := ih h
      exact ⟨.announcementHeard uuid variant :: pre, assignDoneAt, tail,
        by simp [heq], hpre, hadopt⟩
    | tick now =>
      obtain ⟨pre, assignDoneAt, tail, heq, hpre, hadopt⟩ := ih h
      exact ⟨.tick now :: pre, assignDoneAt, tail, by simp [heq], hpre, hadopt⟩
    | panelsChanged selectedPresent =>
      obtain ⟨pre, assignDoneAt, tail, heq, hpre, hadopt⟩ := ih h
      exact ⟨.panelsChanged selectedPresent :: pre, assignDoneAt, tail,
        by simp [heq], hpre, hadopt⟩
    | linkChanged connected =>
      cases connected with
      | false => simp [step] at h
      | true =>
        obtain ⟨pre, assignDoneAt, tail, heq, hpre, hadopt⟩ := ih h
        exact ⟨.linkChanged true :: pre, assignDoneAt, tail, by simp [heq], hpre, hadopt⟩
    | writeCompleted now => exact ⟨[], now, es, rfl, rfl, h⟩
    | writeFaulted => simp [step] at h
    | setAddressAcked =>
      obtain ⟨pre, assignDoneAt, tail, heq, hpre, hadopt⟩ := ih h
      exact ⟨.setAddressAcked :: pre, assignDoneAt, tail, by simp [heq], hpre, hadopt⟩

/-- A successful run from `awaitingAdoption … false` decomposes at the ACK:
a prefix that stays in `awaitingAdoption … false` (foreign uuids ignored,
ticks before the deadline, panel-change no-ops — never the selected-uuid
re-announce that would end it `claimNotAdopted`), the `setAddressAcked` pivot
(`ackSeen := true`), and a successful remainder from `awaitingAdoption … true`. -/
theorem succeeded_through_ack (cfg : AttemptConfig) (deadline : Nat)
    (events : List BaptismEvent) :
    runFrom cfg (.awaitingAdoption deadline false) events = .terminal .succeeded →
    ∃ (pre tail : List BaptismEvent),
      events = pre ++ .setAddressAcked :: tail ∧
      runFrom cfg (.awaitingAdoption deadline false) pre
        = .awaitingAdoption deadline false ∧
      runFrom cfg (.awaitingAdoption deadline true) tail = .terminal .succeeded := by
  induction events with
  | nil => intro h; simp at h
  | cons e es ih =>
    intro h
    cases e with
    | setAddressAcked => exact ⟨[], es, rfl, rfl, h⟩
    | announcementHeard uuid variant =>
      by_cases huuid : uuid = cfg.selectedUuid
      · subst huuid; simp [step] at h
      · rw [runFrom_cons,
          foreign_uuid_never_transitions cfg _ uuid variant huuid] at h
        obtain ⟨pre, tail, heq, hpre, htail⟩ := ih h
        refine ⟨.announcementHeard uuid variant :: pre, tail, by simp [heq], ?_, htail⟩
        rw [runFrom_cons, foreign_uuid_never_transitions cfg _ uuid variant huuid]
        exact hpre
    | tick now =>
      by_cases hd : deadline ≤ now
      · simp [step, hd] at h
      · rw [runFrom_cons,
          show step cfg (.awaitingAdoption deadline false) (.tick now)
            = (.awaitingAdoption deadline false, .none) by simp [step, hd]] at h
        obtain ⟨pre, tail, heq, hpre, htail⟩ := ih h
        refine ⟨.tick now :: pre, tail, by simp [heq], ?_, htail⟩
        rw [runFrom_cons,
          show step cfg (.awaitingAdoption deadline false) (.tick now)
            = (.awaitingAdoption deadline false, .none) by simp [step, hd]]
        exact hpre
    | panelsChanged selectedPresent =>
      obtain ⟨pre, tail, heq, hpre, htail⟩ := ih h
      exact ⟨.panelsChanged selectedPresent :: pre, tail, by simp [heq], hpre, htail⟩
    | linkChanged connected =>
      cases connected with
      | false => simp [step] at h
      | true =>
        obtain ⟨pre, tail, heq, hpre, htail⟩ := ih h
        exact ⟨.linkChanged true :: pre, tail, by simp [heq], hpre, htail⟩
    | writeCompleted now =>
      obtain ⟨pre, tail, heq, hpre, htail⟩ := ih h
      exact ⟨.writeCompleted now :: pre, tail, by simp [heq], hpre, htail⟩
    | writeFaulted =>
      obtain ⟨pre, tail, heq, hpre, htail⟩ := ih h
      exact ⟨.writeFaulted :: pre, tail, by simp [heq], hpre, htail⟩

/-- A successful run from `awaitingAdoption … true` decomposes at the closing
tick: a prefix that holds the silence (foreign uuids ignored, ticks before the
deadline, panel-change no-ops — never the selected-uuid re-announce), then the
`tick closeAt` pivot at/past the deadline that, with `ackSeen` already true,
closes the window `succeeded` (FR-006). -/
theorem succeeded_through_silence_tick (cfg : AttemptConfig) (deadline : Nat)
    (events : List BaptismEvent) :
    runFrom cfg (.awaitingAdoption deadline true) events = .terminal .succeeded →
    ∃ (pre tail : List BaptismEvent) (closeAt : Nat),
      events = pre ++ .tick closeAt :: tail ∧
      deadline ≤ closeAt ∧
      runFrom cfg (.awaitingAdoption deadline true) pre
        = .awaitingAdoption deadline true := by
  induction events with
  | nil => intro h; simp at h
  | cons e es ih =>
    intro h
    cases e with
    | tick now =>
      by_cases hd : deadline ≤ now
      · exact ⟨[], es, now, rfl, hd, rfl⟩
      · rw [runFrom_cons,
          show step cfg (.awaitingAdoption deadline true) (.tick now)
            = (.awaitingAdoption deadline true, .none) by simp [step, hd]] at h
        obtain ⟨pre, tail, closeAt, heq, hclose, hpre⟩ := ih h
        refine ⟨.tick now :: pre, tail, closeAt, by simp [heq], hclose, ?_⟩
        rw [runFrom_cons,
          show step cfg (.awaitingAdoption deadline true) (.tick now)
            = (.awaitingAdoption deadline true, .none) by simp [step, hd]]
        exact hpre
    | setAddressAcked =>
      obtain ⟨pre, tail, closeAt, heq, hclose, hpre⟩ := ih h
      exact ⟨.setAddressAcked :: pre, tail, closeAt, by simp [heq], hclose, hpre⟩
    | announcementHeard uuid variant =>
      by_cases huuid : uuid = cfg.selectedUuid
      · subst huuid; simp [step] at h
      · rw [runFrom_cons,
          foreign_uuid_never_transitions cfg _ uuid variant huuid] at h
        obtain ⟨pre, tail, closeAt, heq, hclose, hpre⟩ := ih h
        refine ⟨.announcementHeard uuid variant :: pre, tail, closeAt,
          by simp [heq], hclose, ?_⟩
        rw [runFrom_cons, foreign_uuid_never_transitions cfg _ uuid variant huuid]
        exact hpre
    | panelsChanged selectedPresent =>
      obtain ⟨pre, tail, closeAt, heq, hclose, hpre⟩ := ih h
      exact ⟨.panelsChanged selectedPresent :: pre, tail, closeAt,
        by simp [heq], hclose, hpre⟩
    | linkChanged connected =>
      cases connected with
      | false => simp [step] at h
      | true =>
        obtain ⟨pre, tail, closeAt, heq, hclose, hpre⟩ := ih h
        exact ⟨.linkChanged true :: pre, tail, closeAt, by simp [heq], hclose, hpre⟩
    | writeCompleted now =>
      obtain ⟨pre, tail, closeAt, heq, hclose, hpre⟩ := ih h
      exact ⟨.writeCompleted now :: pre, tail, closeAt, by simp [heq], hclose, hpre⟩
    | writeFaulted =>
      obtain ⟨pre, tail, closeAt, heq, hclose, hpre⟩ := ih h
      exact ⟨.writeFaulted :: pre, tail, closeAt, by simp [heq], hclose, hpre⟩

/-- A successful run from `awaitingAdoption … false` decomposes across the full
adoption window: an ACK-pending prefix (silence held, no ACK yet), the
`setAddressAcked` pivot, a silence-window prefix (ACK seen, silence still held),
and the closing `tick closeAt` at/past the deadline. The composite of
`succeeded_through_ack` and `succeeded_through_silence_tick`. -/
theorem succeeded_through_adoption_window (cfg : AttemptConfig) (deadline : Nat)
    (events : List BaptismEvent) :
    runFrom cfg (.awaitingAdoption deadline false) events = .terminal .succeeded →
    ∃ (ackPending silenceWindow tail : List BaptismEvent) (closeAt : Nat),
      events = ackPending ++ .setAddressAcked
          :: (silenceWindow ++ .tick closeAt :: tail) ∧
      deadline ≤ closeAt ∧
      runFrom cfg (.awaitingAdoption deadline false) ackPending
        = .awaitingAdoption deadline false ∧
      runFrom cfg (.awaitingAdoption deadline true) silenceWindow
        = .awaitingAdoption deadline true := by
  intro h
  obtain ⟨ackPending, tail₁, heq₁, hack, htail₁⟩ :=
    succeeded_through_ack cfg deadline events h
  obtain ⟨silenceWindow, tail, closeAt, heq₂, hclose, hsilence⟩ :=
    succeeded_through_silence_tick cfg deadline tail₁ htail₁
  exact ⟨ackPending, silenceWindow, tail, closeAt, by rw [heq₁, heq₂], hclose,
    hack, hsilence⟩

/-! ## baptize_progress (data-model §4.1 / §8)

Progress: a run reaches `succeeded` IFF a WHO_I_AM matching the selected uuid
AND the chosen variant is observed within the announce budget, both writes
complete, the `0x25` ACK is observed, and the broadcast silence holds until a
tick past the adoption deadline. The right-hand side is the canonical
successful-trace shape:

  * `claimPending ++ [writeCompleted claimDoneAt]` — the claim write completes
    (first conjunct), opening the announce window anchored at `claimDoneAt`
    (CHK010): deadline `claimDoneAt + announceBudget`;
  * `waitWindow` keeps the run in `awaitingAnnounce` — "within the budget": no
    tick at/past the deadline, the panel stays present, the link stays up,
    foreign announcements and virgin re-announces are no-ops;
  * the matching `announcementHeard` pivot — the FR-004 validated match;
  * `assignPending ++ [writeCompleted assignDoneAt]` — the assign write
    completes (F6: opens the adoption window anchored at `assignDoneAt`,
    deadline `assignDoneAt + adoptionBudget`, `ackSeen = false`);
  * `ackPending` keeps the run in `awaitingAdoption … false` (silence held, no
    ACK yet), then the `setAddressAcked` pivot (`ackSeen := true`);
  * `silenceWindow` keeps the run in `awaitingAdoption … true` (silence still
    held), then `tick closeAt` at/past the adoption deadline — the FR-006
    confirmation; `tail` is absorbed by the terminal state.
-/

theorem baptize_progress (cfg : AttemptConfig) (events : List BaptismEvent) :
    run cfg events = .terminal .succeeded ↔
      ∃ (claimPending waitWindow assignPending ackPending silenceWindow tail
          : List BaptismEvent)
        (claimDoneAt assignDoneAt closeAt : Nat),
        events = claimPending ++ .writeCompleted claimDoneAt
            :: (waitWindow
              ++ .announcementHeard cfg.selectedUuid (.marketing cfg.chosenVariant)
              :: (assignPending ++ .writeCompleted assignDoneAt
                :: (ackPending ++ .setAddressAcked
                  :: (silenceWindow ++ .tick closeAt :: tail)))) ∧
        runFrom cfg .claimSent claimPending = .claimSent ∧
        runFrom cfg (.awaitingAnnounce (claimDoneAt + announceBudget)) waitWindow
          = .awaitingAnnounce (claimDoneAt + announceBudget) ∧
        runFrom cfg .assigning assignPending = .assigning ∧
        runFrom cfg (.awaitingAdoption (assignDoneAt + adoptionBudget) false) ackPending
          = .awaitingAdoption (assignDoneAt + adoptionBudget) false ∧
        runFrom cfg (.awaitingAdoption (assignDoneAt + adoptionBudget) true) silenceWindow
          = .awaitingAdoption (assignDoneAt + adoptionBudget) true ∧
        assignDoneAt + adoptionBudget ≤ closeAt := by
  constructor
  · intro h
    obtain ⟨claimPending, claimDoneAt, post₁, heq₁, h1, hpost₁⟩ :=
      succeeded_through_claim_write cfg events (by rwa [run_eq_runFrom] at h)
    obtain ⟨waitWindow, post₂, heq₂, h2, hpost₂⟩ :=
      succeeded_through_matching_announcement cfg (claimDoneAt + announceBudget)
        post₁ hpost₁
    obtain ⟨assignPending, assignDoneAt, post₃, heq₃, h3, hpost₃⟩ :=
      succeeded_through_assign_write cfg post₂ hpost₂
    obtain ⟨ackPending, silenceWindow, tail, closeAt, heq₄, hclose, h4, h5⟩ :=
      succeeded_through_adoption_window cfg (assignDoneAt + adoptionBudget) post₃ hpost₃
    exact ⟨claimPending, waitWindow, assignPending, ackPending, silenceWindow, tail,
      claimDoneAt, assignDoneAt, closeAt,
      by rw [heq₁, heq₂, heq₃, heq₄], h1, h2, h3, h4, h5, hclose⟩
  · rintro ⟨claimPending, waitWindow, assignPending, ackPending, silenceWindow, tail,
      claimDoneAt, assignDoneAt, closeAt, heq, h1, h2, h3, h4, h5, hclose⟩
    subst heq
    simp [run_eq_runFrom, runFrom_append, h1, h2, h3, h4, h5, hclose, step]

/-! ## no_assignment_without_match (FR-004 / §8)

The `sendAssign` action (the SET_ADDRESS write) is unreachable unless the
event is an `announcementHeard` matching BOTH the selected uuid and the
chosen variant while the FSM sits in `awaitingAnnounce`. Stated over the
action channel of `step`, which is exactly what the service executes — no
state inspection can fake an assignment. The new `awaitingAdoption` arm emits
no `sendAssign` (only `none`), so the proof extends cleanly.
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
      · subst huuid
        cases variant with
        | marketing v =>
          by_cases hv : v = cfg.chosenVariant
          · subst hv; exact ⟨deadline, rfl, rfl⟩
          · simp [step, hv] at h
        | virgin => simp [step] at h
        | unknown raw => simp [step] at h
      · simp [step, huuid] at h
    | tick now =>
      exfalso; by_cases hd : deadline ≤ now <;> simp [step, hd] at h
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
  | awaitingAdoption deadline ackSeen =>
    exfalso
    cases event with
    | announcementHeard uuid variant =>
      by_cases huuid : uuid = cfg.selectedUuid <;> simp [step, huuid] at h
    | tick now =>
      by_cases hd : deadline ≤ now <;> cases ackSeen <;> simp [step, hd] at h
    | linkChanged connected => cases connected <;> simp [step] at h
    | _ => simp [step] at h
  | terminal outcome => exfalso; simp [step] at h

/-! ## virgin_keeps_waiting (F1 / §8)

The F1 fix, stated at the transition level: in `awaitingAnnounce`, a
selected-uuid re-announce as `virgin` is a strict no-op (the panel is still
mid-cycle, not rejected), and `unexpectedVariant` fires only on a non-virgin,
non-chosen variant (`marketing other` / `unknown`).
-/

theorem virgin_keeps_waiting (cfg : AttemptConfig) (deadline : Nat) :
    step cfg (.awaitingAnnounce deadline)
        (.announcementHeard cfg.selectedUuid .virgin)
      = (.awaitingAnnounce deadline, .none)
    ∧ ∀ variant : VariantIdentity,
        (step cfg (.awaitingAnnounce deadline)
            (.announcementHeard cfg.selectedUuid variant)).fst
          = .terminal (.unexpectedVariant variant) →
        variant ≠ .virgin ∧ variant ≠ .marketing cfg.chosenVariant := by
  refine ⟨by simp [step], ?_⟩
  intro variant hstep
  cases variant with
  | marketing v =>
    by_cases hv : v = cfg.chosenVariant
    · subst hv; simp [step] at hstep
    · exact ⟨by simp, by simp [hv]⟩
  | virgin => simp [step] at hstep
  | unknown raw => exact ⟨by simp, by simp⟩

/-! ## no_success_without_adoption (F6 / §8)

The formal carrier of "never a false success on write-completion": a run can
reach `succeeded` only if a `setAddressAcked` (the `0x25` ACK) was observed AND
a closing `tick` happened — the two events the corrected gate adds over the
shipped write-completion model. A corollary of `baptize_progress`.
-/

theorem no_success_without_adoption (cfg : AttemptConfig) (events : List BaptismEvent) :
    run cfg events = .terminal .succeeded →
    BaptismEvent.setAddressAcked ∈ events
      ∧ ∃ closeAt : Nat, BaptismEvent.tick closeAt ∈ events := by
  intro h
  obtain ⟨claimPending, waitWindow, assignPending, ackPending, silenceWindow, tail,
    claimDoneAt, assignDoneAt, closeAt, heq, _, _, _, _, _, _⟩ :=
    (baptize_progress cfg events).mp h
  subst heq
  refine ⟨?_, closeAt, ?_⟩ <;> simp [List.mem_append, List.mem_cons]

end Stem.ButtonPanelTester.Phase3
