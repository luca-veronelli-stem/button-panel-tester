module Stem.ButtonPanelTester.Tests.Property.Can.ButtonPressTestProperties

open System
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Can

/// FsCheck mirrors of the Lean `ButtonPressTest` theorems in
/// `lean/Stem/ButtonPanelTester/Phase4/ButtonPressTest.lean` (T018), per
/// `specs/005-button-press-test/data-model.md` §4 / research R9. The Lean side
/// proves each invariant over the abstract model; every property here exercises
/// the REAL `ButtonPressTest.step` over generator-scripted event sequences,
/// realized into concrete `TestEvent`s threaded through the live FSM state (the
/// deadline-relative ticks cannot be realized up front — the deadline lives in
/// the running state). Each property is an INDEPENDENT check of `step`'s
/// behaviour — it asserts the invariant over the run's states/results/actions,
/// it never restates `step`'s own match.

let private baseInstant = DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero)

/// The first wire bit (0..15) NOT in the variant's active set — a guaranteed
/// INACTIVE press target (FR-014). Exists for every variant: a full `0xFF` mask
/// yields `8`, OPTIMUS-XP's `0x36` yields `0`.
let private inactiveBit (schema: ButtonSchema) : int =
    [ 0..15 ] |> List.find (fun b -> schema.Active |> List.forall (fun a -> a.Bit <> b))

/// The results vector a state carries (`Idle` carries none) — the independent
/// read of the recorded outcomes out of any state, mirror of Lean `stateResults`.
let private stateResults (state: ButtonPressTestState) : ButtonOutcome[] =
    match state with
    | Prompting(_, _, r) -> r
    | Completed r -> r
    | Interrupted(_, r) -> r
    | ButtonPressTestState.Idle -> [||]

/// A run-ending state — mirror of Lean `IsTerminal`.
let private isTerminal (state: ButtonPressTestState) : bool =
    match state with
    | Completed _
    | Interrupted _ -> true
    | ButtonPressTestState.Idle
    | Prompting _ -> false

/// Mirror of Lean `closingSchedule`: the canonical suffix that drives any state
/// to a terminal — a single `LinkChanged false` tears `Prompting` down to
/// `Interrupted`; a terminal needs nothing.
let private closingSchedule (state: ButtonPressTestState) : TestEvent list =
    match state with
    | Prompting _ -> [ TestEvent.LinkChanged false ]
    | ButtonPressTestState.Idle
    | Completed _
    | Interrupted _ -> []

/// Generator-level event script. Realized against the LIVE FSM state by
/// `realizeFrom` (the prompted button's bit and the deadline-relative ticks
/// depend on the running state), mirroring baptism's `ScriptedEvent` pattern.
type ScriptedEvent =
    | PressPrompted // press the currently-prompted button's bit (in-window match → Pass)
    | PressOtherActive // press a different ACTIVE button's bit (→ RecordUnexpected)
    | PressInactive // press a bit outside the variant mask (→ NoAction, FR-014)
    | TickBeforeDeadline
    | TickAtDeadline
    | DoRetry
    | DoSkip
    | LinkDrop // LinkChanged false
    | LinkUp // LinkChanged true
    | PanelGone // PanelPresence false
    | PanelHere // PanelPresence true

/// Realize one scripted event against the live state and clock cursor. The
/// prompted/other/inactive bit and the deadline-relative ticks are read off the
/// current `Prompting` state; outside `Prompting` the event is realized to a
/// harmless inactive press (the terminal/idle state absorbs it anyway).
let private realizeOne (schema: ButtonSchema) (state: ButtonPressTestState) (now: DateTimeOffset) scripted : TestEvent =
    let active = schema.Active

    match scripted with
    | PressPrompted ->
        match state with
        | Prompting(i, _, _) when i < active.Length -> PressEdge active.[i].Bit
        | ButtonPressTestState.Idle
        | Prompting _
        | Completed _
        | Interrupted _ -> PressEdge(inactiveBit schema)
    | PressOtherActive ->
        match state with
        | Prompting(i, _, _) when i < active.Length ->
            let prompted = active.[i].Bit

            match active |> List.tryFind (fun a -> a.Bit <> prompted) with
            | Some other -> PressEdge other.Bit
            | None -> PressEdge(inactiveBit schema)
        | ButtonPressTestState.Idle
        | Prompting _
        | Completed _
        | Interrupted _ -> PressEdge(inactiveBit schema)
    | PressInactive -> PressEdge(inactiveBit schema)
    | TickBeforeDeadline ->
        match state with
        | Prompting(_, deadline, _) -> TestEvent.Tick(deadline.AddMilliseconds -1.0)
        | ButtonPressTestState.Idle
        | Completed _
        | Interrupted _ -> TestEvent.Tick now
    | TickAtDeadline ->
        match state with
        | Prompting(_, deadline, _) -> TestEvent.Tick deadline
        | ButtonPressTestState.Idle
        | Completed _
        | Interrupted _ -> TestEvent.Tick now
    | DoRetry -> Retry
    | DoSkip -> Skip
    | LinkDrop -> TestEvent.LinkChanged false
    | LinkUp -> TestEvent.LinkChanged true
    | PanelGone -> PanelPresence false
    | PanelHere -> PanelPresence true

/// Realize a script into concrete `TestEvent`s, folding the real `step` so
/// deadline-relative ticks land on the intended side of the current button's
/// window. The clock cursor advances 100 ms per event.
let private realizeFrom (schema: ButtonSchema) (start: ButtonPressTestState) (script: ScriptedEvent list) : TestEvent list =
    let folder (state, now: DateTimeOffset, acc) scripted =
        let ev = realizeOne schema state now scripted
        (fst (ButtonPressTest.step schema state ev), now.AddMilliseconds 100.0, ev :: acc)

    let (_, _, evs) = script |> List.fold folder (start, baseInstant, [])
    List.rev evs

let private startState (schema: ButtonSchema) : ButtonPressTestState =
    ButtonPressTest.start schema baseInstant

/// Mirror of Lean `runFrom`: fold the state component of the real `step` over an
/// observed event list.
let private runFrom (schema: ButtonSchema) (state: ButtonPressTestState) (events: TestEvent list) : ButtonPressTestState =
    events |> List.fold (fun s e -> fst (ButtonPressTest.step schema s e)) state

/// Mirror of Lean `run`: a run starts from `start`'s state.
let private run (schema: ButtonSchema) (events: TestEvent list) : ButtonPressTestState =
    runFrom schema (startState schema) events

/// The `(state-before, event, action, state-after)` trace of a run — the
/// transition-level properties key off the state each event was consumed in and
/// the action/next-state it produced. Every component comes from the actual run
/// (the fold computes each `step` once); the properties OBSERVE the trace, they
/// do not recompute `step`.
let private transitions
    (schema: ButtonSchema)
    (events: TestEvent list)
    : (ButtonPressTestState * TestEvent * TestAction * ButtonPressTestState) list =
    let folder (state, acc) ev =
        let next, action = ButtonPressTest.step schema state ev
        (next, (state, ev, action, next) :: acc)

    events |> List.fold folder (startState schema, []) |> snd |> List.rev

/// One generated button-press-test run: the variant schema, the scripted event
/// sequence, and an extra suffix used to assert terminal absorption.
type ButtonPressScenario =
    { Schema: ButtonSchema
      Script: ScriptedEvent list
      More: ScriptedEvent list }

/// Unconstrained scripted-event mix, biased toward `PressPrompted`/`DoSkip` so
/// runs regularly advance (and frequently reach `Completed`), with link/panel
/// loss kept rare so most runs are not interrupted on the first event.
let private anyScripted: Gen<ScriptedEvent> =
    Gen.frequency
        [ 4, Gen.constant PressPrompted
          2, Gen.constant PressOtherActive
          2, Gen.constant PressInactive
          2, Gen.constant TickBeforeDeadline
          2, Gen.constant TickAtDeadline
          2, Gen.constant DoRetry
          3, Gen.constant DoSkip
          1, Gen.constant LinkDrop
          1, Gen.constant LinkUp
          1, Gen.constant PanelGone
          1, Gen.constant PanelHere ]

/// The four shipped variant schemas (OPTIMUS-XP authoritative with 4 active
/// buttons; the three all-8 provisional variants).
let private schemaGen: Gen<ButtonSchema> =
    Gen.elements [ OptimusXp; EdenXp; R3LXp; EdenBs8 ] |> Gen.map ButtonSchema.forVariant

/// FsCheck `Arbitrary` container — passed to `[<Property>]` via
/// `Arbitrary = [| typeof<ButtonPressGenerators> |]` (house pattern, see
/// `BaptismSequenceProperties.BaptismGenerators`).
type ButtonPressGenerators =
    static member Scenario() : Arbitrary<ButtonPressScenario> =
        gen {
            let! schema = schemaGen
            let! script = Gen.listOf anyScripted
            let! more = Gen.listOf anyScripted

            return
                { Schema = schema
                  Script = script
                  More = more }
        }
        |> Arb.fromGen

/// FsCheck property mirroring the Lean theorem `test_visits_active_only`
/// (T018), per FR-002: every advance steps to exactly the next index — the
/// active buttons are prompted one at a time, in canonical order, no index
/// skipped. Independent: scans the run's emitted `AdvancePrompt` actions and
/// asserts each nextIndex is the current prompting index + 1.
[<Property(Arbitrary = [| typeof<ButtonPressGenerators> |])>]
let TestVisitsActiveOnly (scenario: ButtonPressScenario) =
    let schema = scenario.Schema
    let events = realizeFrom schema (startState schema) scenario.Script

    transitions schema events
    |> List.forall (fun (before, _, action, _) ->
        match before, action with
        | Prompting(i, _, _), AdvancePrompt j -> j = i + 1
        | _ -> true)

/// FsCheck property mirroring the Lean theorem `result_vector_length` (T018),
/// per FR-011: every reachable state's results vector has length = the active
/// count — `step` never resizes it. Independent: reads the final state's
/// results length.
[<Property(Arbitrary = [| typeof<ButtonPressGenerators> |])>]
let ResultVectorLength (scenario: ButtonPressScenario) =
    let schema = scenario.Schema
    let events = realizeFrom schema (startState schema) scenario.Script
    (stateResults (run schema events)).Length = schema.Active.Length

/// FsCheck property mirroring the Lean theorem `test_outcome_total` (T018):
/// every run, extended by its reached state's closing schedule, terminates in a
/// terminal that absorbs arbitrary further events (the "exactly one outcome per
/// run" claim). Independent: drives the real `step` through the closing
/// schedule and an arbitrary suffix, asserting terminality + stability.
[<Property(Arbitrary = [| typeof<ButtonPressGenerators> |])>]
let TestOutcomeTotal (scenario: ButtonPressScenario) =
    let schema = scenario.Schema
    let events = realizeFrom schema (startState schema) scenario.Script
    let reached = run schema events
    let closed = runFrom schema reached (closingSchedule reached)
    let more = realizeFrom schema closed scenario.More
    isTerminal closed && runFrom schema closed more = closed

/// FsCheck property mirroring the Lean theorem `pass_requires_press_edge`
/// (T018), per FR-006: a `Pass` newly recorded at the prompted index comes ONLY
/// from an in-window matching press-edge — the event was `PressEdge` of the
/// prompted button's bit and the button was still `Pending`. Independent: scans
/// the trace for transitions that turn the current index into `Pass` and
/// asserts the triggering event + the in-window precondition.
[<Property(Arbitrary = [| typeof<ButtonPressGenerators> |])>]
let PassRequiresPressEdge (scenario: ButtonPressScenario) =
    let schema = scenario.Schema
    let active = schema.Active
    let events = realizeFrom schema (startState schema) scenario.Script

    transitions schema events
    |> List.forall (fun (before, ev, _, after) ->
        match before with
        | Prompting(i, _, rb) when i < rb.Length ->
            let ra = stateResults after

            if i < ra.Length && ra.[i] = Pass && rb.[i] <> Pass then
                ev = PressEdge active.[i].Bit && rb.[i] = Pending
            else
                true
        | _ -> true)

/// FsCheck property mirroring the Lean theorem `skip_never_pass` (T018), per
/// FR-009: a `Skip` records `Skipped` at the prompted index — never `Pass`.
/// Independent: scans the trace for `Skip` transitions and asserts the prompted
/// index becomes `Skipped`.
[<Property(Arbitrary = [| typeof<ButtonPressGenerators> |])>]
let SkipNeverPass (scenario: ButtonPressScenario) =
    let schema = scenario.Schema
    let events = realizeFrom schema (startState schema) scenario.Script

    transitions schema events
    |> List.forall (fun (before, ev, _, after) ->
        match before, ev with
        | Prompting(i, _, _), Skip ->
            let ra = stateResults after
            i < ra.Length && ra.[i] = Skipped
        | _ -> true)

/// FsCheck property mirroring the Lean theorem `interrupt_excludes_all_passed`
/// (T018), per FR-013: no `Interrupted` run reports all-passed — the button it
/// was interrupted on is still unresolved. Independent: when the run ends
/// `Interrupted`, asserts the partial results contain a non-`Pass` entry (the
/// inlined negation of "all active passed").
[<Property(Arbitrary = [| typeof<ButtonPressGenerators> |])>]
let InterruptExcludesAllPassed (scenario: ButtonPressScenario) =
    let schema = scenario.Schema
    let events = realizeFrom schema (startState schema) scenario.Script

    match run schema events with
    | Interrupted(_, partialResults) -> partialResults |> Array.exists (fun o -> o <> Pass)
    | ButtonPressTestState.Idle
    | Prompting _
    | Completed _ -> true

/// FsCheck property mirroring the Lean theorem `terminal_absorbs` (T018): a
/// terminal state absorbs every further event (never-flip), and its
/// after-`Missed` facet — a press for a button already `Missed` does not flip it
/// back. Independent: drives the run to a guaranteed terminal then feeds an
/// arbitrary suffix asserting no change, AND scans the trace asserting a
/// `PressEdge` on a `Missed` button leaves it `Missed`.
[<Property(Arbitrary = [| typeof<ButtonPressGenerators> |])>]
let TerminalAbsorbs (scenario: ButtonPressScenario) =
    let schema = scenario.Schema
    let events = realizeFrom schema (startState schema) scenario.Script
    let reached = run schema events
    let terminal = runFrom schema reached (closingSchedule reached)
    let more = realizeFrom schema terminal scenario.More
    let absorbs = runFrom schema terminal more = terminal

    let missedNeverFlips =
        transitions schema events
        |> List.forall (fun (before, ev, _, after) ->
            match before, ev with
            | Prompting(i, _, rb), PressEdge _ when i < rb.Length && rb.[i] = Missed ->
                let ra = stateResults after
                i < ra.Length && ra.[i] = Missed
            | _ -> true)

    absorbs && missedNeverFlips
