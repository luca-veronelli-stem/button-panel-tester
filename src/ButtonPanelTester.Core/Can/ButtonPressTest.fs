namespace Stem.ButtonPanelTester.Core.Can

open System

/// Per-button verdict in the button-press test, per
/// `specs/005-button-press-test/data-model.md` §4 (FR-006/007/009/011).
/// `Pending` is the initial not-yet-resolved state; `Pass`/`Missed`/`Skipped`
/// are the three resolved outcomes. Closed DU → the mandatory triple
/// (`stem-fp-discipline` §3): closure is witnessed by the Lean theorems in
/// `lean/Stem/ButtonPanelTester/Phase4/ButtonPressTest.lean` (T018) and by the
/// FsCheck properties in `Tests/Property/Can/ButtonPressTestProperties.fs`
/// (T020). Mirrors the Lean inductive `ButtonOutcome` (T018) — same name, same
/// case order. The wildcard-free matches over this DU in `ButtonPressTest.step`
/// are load-bearing: a fifth outcome breaks the F# compile AND the Lean proofs
/// together.
type ButtonOutcome =
    | Pending
    | Pass
    | Missed
    | Skipped

/// Why a button-press-test run ended in `Interrupted`, per `data-model.md` §4
/// (FR-013): the CAN link left `Connected` (`LinkLost`) or the selected panel
/// disappeared from discovery (`PanelLost`). Mirrors the Lean inductive
/// `InterruptReason` (T018) — same name, same case order.
type InterruptReason =
    | LinkLost
    | PanelLost

/// Button-press-test session FSM states, per `data-model.md` §4 and research
/// R7: `Idle → Prompting(index, deadline, results) → terminal Completed |
/// Interrupted`. `Prompting` carries the current button index, that button's
/// countdown deadline, and the per-button results vector (one `ButtonOutcome`
/// per active button); `Completed` carries the final results; `Interrupted`
/// carries the reason and the partial results at the moment of interruption.
/// `Idle` is the pre-`start` state, unreachable inside a run. A terminal state
/// absorbs every further event (Lean `terminal_absorbs`) — the never-flip rule.
/// Mirrors the Lean inductive `ButtonPressTestState` (T018) — same case order;
/// the Lean model's abstract `Nat` deadline refines to `DateTimeOffset` and its
/// `List ButtonOutcome` to `ButtonOutcome[]` here.
type ButtonPressTestState =
    | Idle
    | Prompting of index: int * deadline: DateTimeOffset * results: ButtonOutcome[]
    | Completed of results: ButtonOutcome[]
    | Interrupted of reason: InterruptReason * partial: ButtonOutcome[]

/// The observable FSM inputs, per `data-model.md` §4. `PressEdge bit` is the
/// detector's press edge (the active-masked wire bit that went `1 → 0`, R2);
/// `Tick now` is the clock instant (the pure FSM never reads a clock — the
/// deadline is compared against `now`); `Retry`/`Skip` are technician actions;
/// `LinkChanged`/`PanelPresence` carry the link/discovery guard bits. There is
/// no `Start` event — `ButtonPressTest.start` is a pure constructor. Mirrors the
/// Lean inductive `TestEvent` (T018) — same name, same case order.
type TestEvent =
    | PressEdge of bit: int
    | Tick of now: DateTimeOffset
    | Retry
    | Skip
    | LinkChanged of connected: bool
    | PanelPresence of present: bool

/// The effect channel of `ButtonPressTest.step`: what the service must do after
/// a transition, per `data-model.md` §4. `RecordUnexpected bit` logs a wrong
/// active-button press — NOT counted as a result (FR-008); `AdvancePrompt
/// nextIndex` tells the service to arm the next button's fresh countdown (the
/// pure step carries the deadline forward unchanged — re-arming is the
/// service's job); `FinishCompleted` surfaces the final grid; `Halt reason`
/// tears the run down (FR-013). Mirrors the Lean inductive `TestAction` (T018),
/// with Lean's `noAction` renamed `NoAction` — an F# case named `None` would
/// collide with `Option.None`.
type TestAction =
    | NoAction
    | RecordUnexpected of bit: int
    | AdvancePrompt of nextIndex: int
    | FinishCompleted
    | Halt of InterruptReason

/// The pure button-press-test session FSM, per `data-model.md` §4 and research
/// R7. The arm-for-arm transcription of the Lean model in
/// `lean/Stem/ButtonPanelTester/Phase4/ButtonPressTest.lean` (T018); the
/// governing theorems `test_visits_active_only`, `result_vector_length`,
/// `test_outcome_total`, `pass_requires_press_edge`, `skip_never_pass`,
/// `interrupt_excludes_all_passed` and `terminal_absorbs` are witnessed at the
/// value level by the FsCheck properties in
/// `Tests/Property/Can/ButtonPressTestProperties.fs` (T020).
module ButtonPressTest =

    /// Functional update: a copy of `results` with index `index` set to
    /// `outcome`. Pure (the input array is never mutated) — `step` returns a
    /// fresh results vector on every scoring transition.
    let private withOutcome (index: int) (outcome: ButtonOutcome) (results: ButtonOutcome[]) : ButtonOutcome[] =
        results |> Array.mapi (fun k o -> if k = index then outcome else o)

    /// The pure session-start constructor (`data-model.md` §4): a variant with
    /// no active buttons completes immediately; otherwise the run prompts button
    /// `0` with every result `Pending`. Re-run (FR-003) is a fresh `start` — it
    /// clears all prior results. The `deadline` is supplied by the service
    /// (`clock.Now + budget`); the pure FSM never reads a clock. Mirrors Lean
    /// `start` (T018), `Nat` deadline refined to `DateTimeOffset`.
    let start (schema: ButtonSchema) (deadline: DateTimeOffset) : ButtonPressTestState =
        match schema.Active with
        | [] -> Completed [||]
        | _ -> Prompting(0, deadline, Array.create schema.Active.Length Pending)

    /// The aggregate "all active passed" verdict, per `data-model.md` §4
    /// (FR-011): `true` iff every entry is `Pass` — false whenever any
    /// `Missed`/`Skipped`/`Pending` remains, and unreachable from `Interrupted`
    /// (Lean `interrupt_excludes_all_passed`). Mirrors Lean `allActivePassed`.
    let allActivePassed (results: ButtonOutcome[]) : bool = results |> Array.forall ((=) Pass)

    /// `Prompting` arm of `step` — the session wait at button `index`
    /// (`data-model.md` §4). A `PressEdge` of the prompted button's bit while it
    /// is still `Pending` (in-window) scores `Pass` and advances — `AdvancePrompt`
    /// to the next button, or `FinishCompleted` when the last active button
    /// resolves (FR-006/010/011); another ACTIVE button's bit is `RecordUnexpected`
    /// with NO advance (FR-008); an INACTIVE bit (outside the variant mask) is
    /// `NoAction` (FR-014). A `PressEdge` once the button is no longer `Pending`
    /// (e.g. `Missed`) is a no-op — the never-flip rule (Lean `pass_requires_press_edge`:
    /// a `Pass` needs an in-window press). `Tick now` at/past the deadline while
    /// `Pending` records `Missed`, staying at `index` offering Retry/Skip (FR-007);
    /// `Retry` re-arms a `Missed` button back to `Pending` (FR-009; the service
    /// supplies the fresh deadline); `Skip` records `Skipped` (≠ `Pass`) and
    /// advances (FR-009); `LinkChanged false` / `PanelPresence false` interrupt
    /// the run (FR-013); the connected/present cases self-loop. Wildcard-free and
    /// exhaustive over every event × outcome.
    let private stepPrompting
        (schema: ButtonSchema)
        (index: int)
        (deadline: DateTimeOffset)
        (results: ButtonOutcome[])
        (event: TestEvent)
        : ButtonPressTestState * TestAction =
        let active = schema.Active
        let lastIndex = active.Length - 1

        let advanceOrFinish (results': ButtonOutcome[]) : ButtonPressTestState * TestAction =
            if index = lastIndex then (Completed results', FinishCompleted)
            else (Prompting(index + 1, deadline, results'), AdvancePrompt(index + 1))

        match event with
        | PressEdge bit ->
            match results.[index] with
            | Pending ->
                if bit = active.[index].Bit then advanceOrFinish (withOutcome index Pass results)
                elif active |> List.exists (fun a -> a.Bit = bit) then
                    (Prompting(index, deadline, results), RecordUnexpected bit)
                else
                    (Prompting(index, deadline, results), NoAction)
            | Pass
            | Missed
            | Skipped -> (Prompting(index, deadline, results), NoAction)
        | Tick now ->
            match results.[index] with
            | Pending when deadline <= now -> (Prompting(index, deadline, withOutcome index Missed results), NoAction)
            | Pending
            | Pass
            | Missed
            | Skipped -> (Prompting(index, deadline, results), NoAction)
        | Retry ->
            match results.[index] with
            | Missed -> (Prompting(index, deadline, withOutcome index Pending results), NoAction)
            | Pending
            | Pass
            | Skipped -> (Prompting(index, deadline, results), NoAction)
        | Skip -> advanceOrFinish (withOutcome index Skipped results)
        | LinkChanged connected ->
            if connected then (Prompting(index, deadline, results), NoAction)
            else (Interrupted(LinkLost, results), Halt LinkLost)
        | PanelPresence present ->
            if present then (Prompting(index, deadline, results), NoAction)
            else (Interrupted(PanelLost, results), Halt PanelLost)

    /// Pure TOTAL transition function over (schema, state, event), returning the
    /// next state AND the action to perform — the arm-for-arm transcription of
    /// Lean `step` in `Phase4/ButtonPressTest.lean` (T018), per `data-model.md`
    /// §4. A terminal state (`Completed`/`Interrupted`) absorbs every event
    /// (Lean `terminal_absorbs`); `Idle` is inert (a run starts past it via
    /// `start`); `Prompting` delegates to `stepPrompting`. The pure step NEVER
    /// reads a clock — time arrives as the `Tick now` parameter and the deadline
    /// is compared `deadline <= now`.
    let step (schema: ButtonSchema) (state: ButtonPressTestState) (event: TestEvent) : ButtonPressTestState * TestAction =
        match state with
        | Completed _ -> (state, NoAction)
        | Interrupted _ -> (state, NoAction)
        | Idle -> (state, NoAction)
        | Prompting(index, deadline, results) -> stepPrompting schema index deadline results event

    /// The per-button countdown budget: 10 s, per `data-model.md` §2/§4 and
    /// research R7 (FR-005, default 10 s) — a `research`-config constant in
    /// CODE, NOT a UI setting. The pure FSM never reads a clock; the SERVICE
    /// arms each `Prompting` deadline as `clock.UtcNow() + testBudget` and
    /// re-arms it on every `AdvancePrompt` / `Retry`. Mirrors the baptism
    /// budgets (`Baptism.announceBudget`), `TimeSpan` not UI.
    let testBudget: TimeSpan = TimeSpan.FromSeconds 10.0

    /// The button-state heartbeat observable window (fix #270): a baptized panel
    /// is silent on WHO_I_AM and instead heartbeats its button-state `VAR_WRITE`
    /// on its directed CAN ID, so a button-state frame seen within this window IS
    /// the evidence the panel is present and observable on the bus (the GUI keys
    /// `testEnablement`'s `observable` conjunct off it; `data-model.md` §6). A
    /// provisional bench default of 2 s, comfortably above the ~182 ms idle
    /// refresh cadence measured on the rig (2026-06-24) — to be confirmed on the
    /// bench alongside the press-edge polarity, the single point to retune. A
    /// `research`-config constant in CODE, NOT a UI setting.
    let observableWindow: TimeSpan = TimeSpan.FromSeconds 2.0

    /// The button-state silence threshold that ends a run in `PanelLost` (fix
    /// #270): if no button-state frame arrives for longer than this WHILE a run
    /// is in flight, the panel is declared lost (`Interrupted PanelLost`, FR-013)
    /// — the recency replacement for the old discovery-prune `PanelPresence`
    /// signal. A provisional bench default of 3 s (above `observableWindow`,
    /// derived from the same ~182 ms refresh) — to be confirmed on the rig. A
    /// `research`-config constant in CODE, NOT a UI setting.
    let panelLostThreshold: TimeSpan = TimeSpan.FromSeconds 3.0

    /// Explanation a `Disabled` button-press-test guard carries when a panel
    /// is selected but NOT baptized — or no panel is selected at all (the
    /// selected-and-baptized conjunct, FR-001). The test only runs on an
    /// addressed panel (a baptized panel transmits its button state, R1).
    [<Literal>]
    let NoBaptizedPanelSelectedExplanation =
        "Select a baptized panel before running the button-press test."

    /// Explanation a `Disabled` button-press-test guard carries when the
    /// selected baptized panel is not observable on the bus (the observable
    /// conjunct, FR-001): no button-state frames are arriving from it, so a
    /// run could not score any press.
    [<Literal>]
    let PanelNotObservableExplanation =
        "The selected panel is not observable on the bus; no button-state frames are arriving."

    /// `true` iff the link state is `Connected` — the one bit the enablement
    /// guard reads off the link (`data-model.md` §6). Mirrors `Baptism`'s
    /// private `isConnected` and Lean `isConnected`.
    let private isConnected (link: CanLinkState) : bool =
        match link with
        | Connected _ -> true
        | _ -> false

    /// Button-press-test enablement guard (`data-model.md` §6, FR-001):
    /// `Enabled` IFF the link is `Connected`, a baptized panel is selected, AND
    /// that panel is observable on the bus. Priority-ordered case analysis —
    /// link down / no baptized panel selected / panel not observable — each
    /// `Disabled` branch naming its one unmet conjunct (the link-down text is
    /// the shared `Baptism.LinkNotConnectedExplanation`). Reuses the
    /// `Enablement` DU from `Baptism.fs` (data-model §6 — the test shares the
    /// baptism enablement shape). The Lean theorem `test_enabled_iff` in
    /// `lean/Stem/ButtonPanelTester/Phase4/Enablement.lean` (T021) proves this
    /// ordered analysis equivalent to the flat conjunction; the FsCheck
    /// `TestEnablementGuards` property in
    /// `Tests/Property/Can/ButtonPressTestEnablementProperties.fs` (T022)
    /// witnesses it at the value level — the SC-008 basis (the test is
    /// unavailable, with a reason, on a non-baptized panel or a non-`Connected`
    /// link).
    let testEnablement (link: CanLinkState) (selectedBaptized: bool) (observable: bool) : Enablement =
        if not (isConnected link) then Disabled Baptism.LinkNotConnectedExplanation
        elif not selectedBaptized then Disabled NoBaptizedPanelSelectedExplanation
        elif not observable then Disabled PanelNotObservableExplanation
        else Enabled
