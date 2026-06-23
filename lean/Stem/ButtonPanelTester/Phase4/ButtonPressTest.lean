/-
T018 — Lean Phase-4 module for the button-press-test session FSM (R7).

Mechanises the session state machine of
`specs/005-button-press-test/data-model.md` §4 and `research.md` R7: a pure,
total `step` over (schema, state, event) returning the next state AND the
action the service must perform. The tool walks a baptized panel's ACTIVE
buttons, one prompt at a time, in canonical firmware order; each prompt has a
per-button countdown; a press-edge in the window scores `Pass` and advances, a
timeout records `Missed`, the technician may `Retry`/`Skip`, and link/panel loss
interrupts the run (never reporting all-passed).

The model mirrors the shipped baptism FSM (`Phase3/BaptismSequence.lean`): the
same pure-state + events + `step → (state, action)` shape, the same `runFrom`/
`run` fold, the same `closingSchedule` + terminal-absorption scaffolding for
totality. Time is abstract `Nat` (the unit is irrelevant to the proofs;
milliseconds is the natural reading), refined to `DateTimeOffset` in F#.

The results vector is `List ButtonOutcome` here (one entry per active button),
refined to `ButtonOutcome[]` in F#. The schema is abstracted to the one datum
the FSM consumes — `TestSchema.activeBits`, the active buttons' wire bits in
canonical order — which is `schema.Active |> List.map (fun a -> a.Bit)` on the F#
`ButtonSchema` (T011). `activeBits.length` is the active count; the prompted bit
at index `i` is `activeBits[i]?`; a pressed bit is "another active button" iff it
is `∈ activeBits` but not the prompted one, and "inactive" iff `∉ activeBits`.

DEADLINE / RE-ARM MODEL (the one genuinely-new design point): the pure `step`
NEVER reads a clock — `Tick (now : Nat)` carries the instant and the deadline is
compared `deadline ≤ now`, exactly as baptism compares its wait budgets. The
deadline is carried UNCHANGED across `Pass`/`Skip`/`Retry` advances; re-arming a
FRESH per-button countdown is the SERVICE's job (Phase E): on an `advancePrompt`
action, or on `Retry`, the service rebuilds the state's deadline as `clock.Now +
budget`. No `step` arm fabricates a deadline (it has no clock to read), so the
seven theorems below quantify over the deadline as an opaque carried value — the
choice of re-arm instant is below the proofs.

START / RE-RUN: `start schema deadline` is a pure state CONSTRUCTOR (there is no
`Start` event): it builds `prompting 0 deadline (replicate n pending)`, or
`completed []` when the variant has no active buttons. Re-run is a fresh `start`
(it clears all results — FR-003). The service calls `start` to begin a run; the
seven theorems quantify over runs driven from `start` (via `run`).

The seven preservation theorems (research R9) ride over this state space:
  * `test_visits_active_only` — every advance steps to exactly the next index
    (`i → i+1`): the active buttons are prompted one at a time, in canonical
    order, no index skipped (FR-002).
  * `result_vector_length` — every reachable state's results vector has length =
    the active count (FR-011): `step` never resizes it.
  * `test_outcome_total` — every run, driven past its pending work by the state's
    `closingSchedule`, terminates in a terminal (`completed`/`interrupted`) and
    that terminal is stable under further events (terminal absorption).
  * `pass_requires_press_edge` — a `Pass` is newly recorded at the prompted index
    ONLY by an in-window matching press-edge (FR-006): no clock, no skip, no
    other event ever writes `Pass`.
  * `skip_never_pass` — a `Skip` records `Skipped` at the prompted index, never
    `Pass` (FR-009).
  * `interrupt_excludes_all_passed` — no reachable `interrupted` run reports
    all-passed (FR-013): the button it was interrupted on is still unresolved.
  * `terminal_absorbs` — a terminal state absorbs every further event, so a late
    press after a reported outcome never flips it (never-flip rule); the
    after-`Missed` facet is carried by `pass_requires_press_edge` (a `Missed`
    button is no longer `pending`, so a press cannot score it `Pass`).

The F# surface lives at `src/ButtonPanelTester.Core/Can/ButtonPressTest.fs`
(T019) and MIRRORS the type names and case order here exactly (stem-fp
discipline §10); the FsCheck properties live at
`tests/.../Property/Can/ButtonPressTestProperties.fs` (T020). This Lean
re-statement lands first, per Constitution Principle I (Lean spec → test → impl).

Constitution Principle I: no `sorry`, no custom axioms.
-/

namespace Stem.ButtonPanelTester.Phase4

/-! ## ButtonOutcome (data-model §4)

The per-button verdict, in the F#-mirror case order. `pending` is the initial
(not-yet-resolved) state; `pass`/`missed`/`skipped` are the three resolved
outcomes. Closed DU → the mandatory triple (closure witnessed by the theorems
below + the FsCheck mirrors). -/

inductive ButtonOutcome where
  | pending
  | pass
  | missed
  | skipped
  deriving DecidableEq, Repr

/-! ## InterruptReason (data-model §4)

Why a run ended in `interrupted`: the CAN link left `Connected` (`linkLost`) or
the selected panel disappeared from discovery (`panelLost`) — FR-013. -/

inductive InterruptReason where
  | linkLost
  | panelLost
  deriving DecidableEq, Repr

/-! ## ButtonPressTestState (data-model §4)

`idle → prompting(index, deadline, results) → terminal completed | interrupted`.
`prompting` carries the current button index, that button's countdown deadline,
and the results vector (one `ButtonOutcome` per active button). `completed`
carries the final results; `interrupted` carries the reason and the partial
results at the moment of interruption. `idle` is the pre-`start` state,
unreachable inside a `run`. -/

inductive ButtonPressTestState where
  | idle
  | prompting (index : Nat) (deadline : Nat) (results : List ButtonOutcome)
  | completed (results : List ButtonOutcome)
  | interrupted (reason : InterruptReason) (partialResults : List ButtonOutcome)
  deriving DecidableEq, Repr

/-! ## TestEvent (data-model §4)

The observable inputs. `pressEdge bit` is the detector's press edge (the
active-masked wire bit that went `1 → 0`); `tick now` is the clock instant;
`retry`/`skip` are technician actions; `linkChanged`/`panelPresence` carry the
link/discovery guard bits. There is no `Start` event — `start` is a pure
constructor. -/

inductive TestEvent where
  | pressEdge (bit : Nat)
  | tick (now : Nat)
  | retry
  | skip
  | linkChanged (connected : Bool)
  | panelPresence (present : Bool)
  deriving DecidableEq, Repr

/-! ## TestAction (data-model §4)

The effect channel of `step`: what the service must do after the transition.
`recordUnexpected bit` logs a wrong active-button press (NOT counted — FR-008);
`advancePrompt nextIndex` arms the next button's countdown; `finishCompleted`
surfaces the final grid; `halt reason` tears the run down. Lean's `none` is
named `noAction` (an F# case named `None` would collide with `Option.None`). -/

inductive TestAction where
  | noAction
  | recordUnexpected (bit : Nat)
  | advancePrompt (nextIndex : Nat)
  | finishCompleted
  | halt (reason : InterruptReason)
  deriving DecidableEq, Repr

/-! ## TestSchema

The FSM-relevant projection of the per-variant `ButtonSchema` (T011):
`activeBits` is the active buttons' wire bits in canonical firmware order — the
prompt sequence. `activeBits.length` is the active count `n`. Mirrors
`schema.Active |> List.map (fun a -> a.Bit)` on the F# side. -/

structure TestSchema where
  activeBits : List Nat

/-! ## allActivePassed (data-model §4, FR-011)

`true` iff every entry is `pass` — false whenever any `missed`/`skipped`/
`pending` remains. The aggregate "all active passed" verdict. -/

def allActivePassed (results : List ButtonOutcome) : Bool :=
  results.all (fun o => decide (o = .pass))

/-! ## stateResults

The results vector a state carries (`idle` carries none → `[]`). Used by the
result-level theorems to read the recorded outcomes out of any state. -/

def stateResults : ButtonPressTestState → List ButtonOutcome
  | .idle => []
  | .prompting _ _ r => r
  | .completed r => r
  | .interrupted _ r => r

/-! ## IsTerminal

A run-ending state — `completed` or `interrupted`. `idle`/`prompting` are not
terminal. -/

def IsTerminal : ButtonPressTestState → Prop
  | .idle => False
  | .prompting _ _ _ => False
  | .completed _ => True
  | .interrupted _ _ => True

/-! ## start (data-model §4)

The pure session-start constructor (Re-run is a fresh `start`): an empty active
set completes immediately; otherwise the run prompts button `0` with all results
`pending`. The `deadline` is supplied by the service (`clock.Now + budget`). -/

def start (schema : TestSchema) (deadline : Nat) : ButtonPressTestState :=
  if schema.activeBits.length = 0 then .completed []
  else .prompting 0 deadline (List.replicate schema.activeBits.length .pending)

/-! ## step (data-model §4 transition table)

Pure total transition function over (schema, state, event). Per-state handling:

  * terminal (`completed`/`interrupted`) — absorbs every event (`terminal_absorbs`);
  * `idle` — inert (a run starts past `idle` via `start`);
  * `prompting i deadline results` — the session wait at button `i`:
    - `pressEdge bit` while `results[i] = pending` (in-window): the prompted bit
      (`activeBits[i]`) scores `pass` and advances — `finishCompleted` if `i` was
      the last active button, else `advancePrompt (i+1)`; another ACTIVE bit is
      `recordUnexpected` with NO advance (FR-008); an INACTIVE bit is `noAction`
      (FR-014). A `pressEdge` once the button is no longer `pending` (e.g.
      `missed`) is a no-op — the never-flip rule;
    - `tick now` with `deadline ≤ now` while `pending` → `missed`, staying at `i`
      offering Retry/Skip (FR-007);
    - `retry` re-arms a `missed` button back to `pending` (the service supplies a
      fresh deadline — see the module header) (FR-009);
    - `skip` records `skipped` (≠ `pass`) and advances (FR-009);
    - `linkChanged false` / `panelPresence false` → `interrupted` + `halt`
      (FR-013); the connected/present cases self-loop. -/

def step (schema : TestSchema) (state : ButtonPressTestState) (event : TestEvent) :
    ButtonPressTestState × TestAction :=
  match state with
  | .completed r => (.completed r, .noAction)
  | .interrupted reason p => (.interrupted reason p, .noAction)
  | .idle => (.idle, .noAction)
  | .prompting i deadline results =>
    match event with
    | .linkChanged connected =>
      if connected then (.prompting i deadline results, .noAction)
      else (.interrupted .linkLost results, .halt .linkLost)
    | .panelPresence present =>
      if present then (.prompting i deadline results, .noAction)
      else (.interrupted .panelLost results, .halt .panelLost)
    | .skip =>
      let results' := results.set i .skipped
      if i + 1 = schema.activeBits.length then (.completed results', .finishCompleted)
      else (.prompting (i + 1) deadline results', .advancePrompt (i + 1))
    | .retry =>
      match results[i]? with
      | some .missed => (.prompting i deadline (results.set i .pending), .noAction)
      | _ => (.prompting i deadline results, .noAction)
    | .tick now =>
      match results[i]? with
      | some .pending =>
        if deadline ≤ now then (.prompting i deadline (results.set i .missed), .noAction)
        else (.prompting i deadline results, .noAction)
      | _ => (.prompting i deadline results, .noAction)
    | .pressEdge bit =>
      match results[i]? with
      | some .pending =>
        if schema.activeBits[i]? = some bit then
          let results' := results.set i .pass
          if i + 1 = schema.activeBits.length then (.completed results', .finishCompleted)
          else (.prompting (i + 1) deadline results', .advancePrompt (i + 1))
        else if bit ∈ schema.activeBits then (.prompting i deadline results, .recordUnexpected bit)
        else (.prompting i deadline results, .noAction)
      | _ => (.prompting i deadline results, .noAction)

/-! ## runFrom / run

A run is a left fold of `step` (state component) over the observed event list —
the same shape as `Phase3/BaptismSequence` and the Phase-2 models. `run` starts
an attempt at `start`'s state. -/

def runFrom (schema : TestSchema) (state : ButtonPressTestState) (events : List TestEvent) :
    ButtonPressTestState :=
  events.foldl (fun s e => (step schema s e).fst) state

def run (schema : TestSchema) (deadline : Nat) (events : List TestEvent) : ButtonPressTestState :=
  runFrom schema (start schema deadline) events

@[simp] theorem runFrom_nil (schema : TestSchema) (s : ButtonPressTestState) :
    runFrom schema s [] = s := rfl

@[simp] theorem runFrom_cons (schema : TestSchema) (s : ButtonPressTestState)
    (e : TestEvent) (es : List TestEvent) :
    runFrom schema s (e :: es) = runFrom schema (step schema s e).fst es := rfl

/-! ## test_visits_active_only (research R9; FR-002)

Every advance steps to exactly the next index: whenever `step` emits
`advancePrompt j` from `prompting i …`, `j = i + 1`. With `start` at index `0`,
this pins the prompt sequence to `0, 1, …, n-1` — the active buttons in
canonical order, one at a time, no index skipped. -/

theorem test_visits_active_only (schema : TestSchema) (i deadline : Nat)
    (results : List ButtonOutcome) (event : TestEvent) (j : Nat) :
    (step schema (.prompting i deadline results) event).snd = .advancePrompt j → j = i + 1 := by
  cases event <;> simp only [step] <;>
    (try split) <;> (try split) <;> (try split) <;> intro h <;> simp_all

/-! ## skip_never_pass (research R9; FR-009)

A `Skip` records `skipped` at the prompted index — never `pass`. Stated as the
safety property (the prompted index is not `pass` after a skip), which holds
whether or not the index is in range (out of range, `set` is a no-op and the
lookup is `none`). -/

theorem skip_never_pass (schema : TestSchema) (i deadline : Nat) (results : List ButtonOutcome) :
    (stateResults (step schema (.prompting i deadline results) .skip).fst)[i]? ≠ some .pass := by
  simp only [step]
  split <;> simp only [stateResults] <;> simp [List.getElem?_set]

/-! ## step_terminal / terminal_absorbs (research R9; never-flip)

A terminal state ignores every further event — the run-level form of the
service's "a terminal state absorbs all events" rule. Once an outcome is
reported, a late press (or any event) leaves it unchanged. The after-`Missed`
facet of never-flip is carried by `pass_requires_press_edge`: a `missed` button
is no longer `pending`, so a press cannot score it `pass`. -/

theorem step_terminal (schema : TestSchema) (state : ButtonPressTestState) (e : TestEvent)
    (h : IsTerminal state) : (step schema state e).fst = state := by
  cases state with
  | idle => exact h.elim
  | prompting _ _ _ => exact h.elim
  | completed r => rfl
  | interrupted reason p => rfl

theorem terminal_absorbs (schema : TestSchema) (state : ButtonPressTestState)
    (events : List TestEvent) (h : IsTerminal state) :
    runFrom schema state events = state := by
  induction events generalizing state with
  | nil => rfl
  | cons e es ih =>
    rw [runFrom_cons, step_terminal schema state e h]
    exact ih state h

/-! ## allActivePassed_false_of_get

A results vector with an in-range non-`pass` entry is not all-passed — the bridge
from the prompting invariant (the current button is unresolved) to
`interrupt_excludes_all_passed`. -/

theorem allActivePassed_false_of_get (r : List ButtonOutcome) (i : Nat)
    (hi : i < r.length) (h : r[i]? ≠ some .pass) : allActivePassed r = false := by
  simp only [allActivePassed, List.all_eq_false]
  refine ⟨r[i], List.getElem_mem hi, ?_⟩
  simp only [decide_eq_true_eq]
  intro he
  exact h (by rw [List.getElem?_eq_getElem hi, he])

/-! ## WF — the structural invariant

`prompting i d r`: the results vector has the active length `n`, `i` is a valid
index, the CURRENT button is not yet `pass` (it is `pending` or `missed`), and
every FUTURE button (index `> i`) is still `pending`. `completed`/`interrupted`
carry length `n`, and `interrupted` additionally is not all-passed. `idle` is
`False` — runs never reach `idle` (they start past it). Preserved by every `step`
(`wf_step`) and holds at `start` (`wf_start`), so it holds for every reachable
state (`wf_run`). The result-level theorems read off this invariant. -/

def WF (n : Nat) : ButtonPressTestState → Prop
  | .idle => False
  | .prompting i _ r =>
    r.length = n ∧ i < n ∧ r[i]? ≠ some .pass ∧ ∀ k, i < k → k < n → r[k]? = some .pending
  | .completed r => r.length = n
  | .interrupted _ r => r.length = n ∧ allActivePassed r = false

theorem wf_start (schema : TestSchema) (deadline : Nat) :
    WF schema.activeBits.length (start schema deadline) := by
  unfold start
  split
  · rename_i h0; simp [WF, h0]
  · rename_i h0
    have hn : 0 < schema.activeBits.length := Nat.pos_of_ne_zero h0
    refine ⟨by simp, hn, ?_, ?_⟩
    · simp [List.getElem?_replicate]
    · intro k _ hkn; simp [hkn]

theorem wf_step (schema : TestSchema) (state : ButtonPressTestState) (event : TestEvent)
    (h : WF schema.activeBits.length state) :
    WF schema.activeBits.length (step schema state event).fst := by
  cases state with
  | idle => exact h.elim
  | completed r => simpa [step] using h
  | interrupted reason p => simpa [step] using h
  | prompting i deadline results =>
    obtain ⟨hlen, hi, hpass, hfut⟩ := h
    cases event with
    | linkChanged connected =>
      cases connected with
      | true => exact ⟨hlen, hi, hpass, hfut⟩
      | false =>
        exact ⟨hlen, allActivePassed_false_of_get results i (hlen ▸ hi) hpass⟩
    | panelPresence present =>
      cases present with
      | true => exact ⟨hlen, hi, hpass, hfut⟩
      | false =>
        exact ⟨hlen, allActivePassed_false_of_get results i (hlen ▸ hi) hpass⟩
    | skip =>
      simp only [step]
      split
      · -- i + 1 = n → completed (results.set i .skipped)
        simp [WF, List.length_set, hlen]
      · -- i + 1 ≠ n → prompting (i+1)
        rename_i hne
        have hi1 : i + 1 < schema.activeBits.length := by omega
        refine ⟨by simp [List.length_set, hlen], hi1, ?_, ?_⟩
        · rw [List.getElem?_set_ne (by omega)]; rw [hfut (i + 1) (by omega) hi1]; simp
        · intro k hk hkn
          rw [List.getElem?_set_ne (by omega)]; exact hfut k (by omega) hkn
    | retry =>
      simp only [step]
      split
      · -- results[i]? = some .missed → prompting i (set i .pending)
        refine ⟨by simp [List.length_set, hlen], hi, ?_, ?_⟩
        · rw [List.getElem?_set_self (by omega)]; simp
        · intro k hk hkn; rw [List.getElem?_set_ne (by omega)]; exact hfut k hk hkn
      · exact ⟨hlen, hi, hpass, hfut⟩
    | tick now =>
      simp only [step]
      split
      · -- results[i]? = some .pending
        split
        · -- deadline ≤ now → prompting i (set i .missed)
          refine ⟨by simp [List.length_set, hlen], hi, ?_, ?_⟩
          · rw [List.getElem?_set_self (by omega)]; simp
          · intro k hk hkn; rw [List.getElem?_set_ne (by omega)]; exact hfut k hk hkn
        · exact ⟨hlen, hi, hpass, hfut⟩
      · exact ⟨hlen, hi, hpass, hfut⟩
    | pressEdge bit =>
      simp only [step]
      split
      · -- results[i]? = some .pending
        split
        · -- activeBits[i]? = some bit → set i .pass, advance
          split
          · -- i + 1 = n → completed
            simp [WF, List.length_set, hlen]
          · -- prompting (i+1)
            rename_i hne
            have hi1 : i + 1 < schema.activeBits.length := by omega
            refine ⟨by simp [List.length_set, hlen], hi1, ?_, ?_⟩
            · rw [List.getElem?_set_ne (by omega)]; rw [hfut (i + 1) (by omega) hi1]; simp
            · intro k hk hkn
              rw [List.getElem?_set_ne (by omega)]; exact hfut k (by omega) hkn
        · -- not matching: recordUnexpected or noAction, state unchanged
          split <;> exact ⟨hlen, hi, hpass, hfut⟩
      · exact ⟨hlen, hi, hpass, hfut⟩

theorem wf_runFrom (schema : TestSchema) (state : ButtonPressTestState) (events : List TestEvent)
    (h : WF schema.activeBits.length state) :
    WF schema.activeBits.length (runFrom schema state events) := by
  induction events generalizing state with
  | nil => exact h
  | cons e es ih =>
    rw [runFrom_cons]
    exact ih (step schema state e).fst (wf_step schema state e h)

theorem wf_run (schema : TestSchema) (deadline : Nat) (events : List TestEvent) :
    WF schema.activeBits.length (run schema deadline events) :=
  wf_runFrom schema (start schema deadline) events (wf_start schema deadline)

/-! ## result_vector_length (research R9; FR-011)

Every reachable state's results vector has length = the active count: `step`
never resizes it. Read off the `WF` invariant. -/

theorem wf_length (n : Nat) (state : ButtonPressTestState) (h : WF n state) :
    (stateResults state).length = n := by
  cases state with
  | idle => exact h.elim
  | prompting i d r => exact h.1
  | completed r => exact h
  | interrupted reason p => exact h.1

theorem result_vector_length (schema : TestSchema) (deadline : Nat) (events : List TestEvent) :
    (stateResults (run schema deadline events)).length = schema.activeBits.length :=
  wf_length _ _ (wf_run schema deadline events)

/-! ## interrupt_excludes_all_passed (research R9; FR-013)

No reachable `interrupted` run reports all-passed — the button the run was
interrupted on is still unresolved, so at least one entry is not `pass`. Read off
the `WF` invariant's `interrupted` conjunct. -/

theorem interrupt_excludes_all_passed (schema : TestSchema) (deadline : Nat)
    (events : List TestEvent) (reason : InterruptReason) (p : List ButtonOutcome)
    (h : run schema deadline events = .interrupted reason p) :
    allActivePassed p = false := by
  have hwf := wf_run schema deadline events
  rw [h] at hwf
  exact hwf.2

/-! ## closingSchedule / test_outcome_total (research R9; totality)

From any reachable (non-`idle`) state, a canonical event suffix drives the run to
a terminal — `prompting` is torn down by a single `linkChanged false`
(`interrupted`); a terminal stays put. `test_outcome_total`: every run, extended
by its reached state's closing schedule, terminates in a terminal that absorbs
all further events (the "exactly one outcome per run" claim). Mirrors baptism's
`baptize_outcome_total`. -/

def closingSchedule : ButtonPressTestState → List TestEvent
  | .idle => []
  | .prompting _ _ _ => [.linkChanged false]
  | .completed _ => []
  | .interrupted _ _ => []

theorem closingSchedule_reaches_terminal (schema : TestSchema) (state : ButtonPressTestState)
    (h : WF schema.activeBits.length state) :
    IsTerminal (runFrom schema state (closingSchedule state)) := by
  cases state with
  | idle => exact h.elim
  | prompting i d r => simp [closingSchedule, runFrom, step, IsTerminal]
  | completed r => simp [closingSchedule, runFrom, IsTerminal]
  | interrupted reason p => simp [closingSchedule, runFrom, IsTerminal]

theorem test_outcome_total (schema : TestSchema) (deadline : Nat) (events : List TestEvent) :
    IsTerminal
        (runFrom schema (run schema deadline events) (closingSchedule (run schema deadline events)))
      ∧ ∀ more : List TestEvent,
          runFrom schema
              (runFrom schema (run schema deadline events)
                (closingSchedule (run schema deadline events)))
              more
            = runFrom schema (run schema deadline events)
                (closingSchedule (run schema deadline events)) := by
  have hwf := wf_run schema deadline events
  have hterm := closingSchedule_reaches_terminal schema (run schema deadline events) hwf
  exact ⟨hterm, fun more => terminal_absorbs schema _ more hterm⟩

/-! ## pass_requires_press_edge (research R9; FR-006)

A `pass` newly recorded at the prompted index `i` comes ONLY from an in-window
matching press-edge: if `i` was not `pass` before the step and is `pass` after,
the event was `pressEdge bit` with `bit` the prompted button's bit
(`activeBits[i]? = some bit`) and the button still `pending` (in-window). No
clock, no `skip`, no other event ever writes `pass`. The after-`Missed` never-flip
is a corollary: a `missed` button is not `pending`, so the `pending`-conclusion
fails — a late press cannot have scored it. -/

theorem pass_requires_press_edge (schema : TestSchema) (i deadline : Nat)
    (results : List ButtonOutcome) (event : TestEvent)
    (hbefore : results[i]? ≠ some .pass)
    (hafter : (stateResults (step schema (.prompting i deadline results) event).fst)[i]? = some .pass) :
    ∃ bit, event = .pressEdge bit ∧ schema.activeBits[i]? = some bit ∧ results[i]? = some .pending := by
  cases event with
  | linkChanged connected =>
    cases connected <;> simp only [step, stateResults] at hafter <;> exact absurd hafter hbefore
  | panelPresence present =>
    cases present <;> simp only [step, stateResults] at hafter <;> exact absurd hafter hbefore
  | skip =>
    simp only [step] at hafter
    split at hafter <;> simp [stateResults, List.getElem?_set] at hafter
  | retry =>
    simp only [step] at hafter
    split at hafter <;> simp_all [stateResults, List.getElem?_set]
  | tick now =>
    simp only [step] at hafter
    split at hafter
    · split at hafter <;> simp_all [stateResults, List.getElem?_set]
    · simp_all [stateResults]
  | pressEdge bit =>
    simp only [step] at hafter
    split at hafter
    · -- results[i]? = some .pending
      rename_i hpend
      split at hafter
      · -- activeBits[i]? = some bit → recorded pass
        rename_i hmatch
        exact ⟨bit, rfl, hmatch, hpend⟩
      · -- not matching: state unchanged, pass would contradict hbefore
        exfalso
        split at hafter <;> (simp only [stateResults] at hafter; exact hbefore hafter)
    · -- results[i]? ≠ some .pending: state unchanged
      exfalso
      simp only [stateResults] at hafter; exact hbefore hafter

end Stem.ButtonPanelTester.Phase4
