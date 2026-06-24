module Stem.ButtonPanelTester.Tests.Windows.Integration.Can.Hardware.ButtonPressTestHardwareTests

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
open Peak.Can.Basic.BackwardCompatibility
open Core.Interfaces
open Infrastructure.Protocol.Hardware
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary // IClock
open Stem.ButtonPanelTester.Infrastructure // SystemClock
open Stem.ButtonPanelTester.Infrastructure.Can // CanPortShare, PcanCanLink, PcanCanFrameStream, WhoIAmReassemblyObserver, ButtonStateReassemblyObserver
open Stem.ButtonPanelTester.Services.Can // CanLinkService, PanelDiscoveryService, ButtonPressTestService + their interfaces
open Stem.ButtonPanelTester.Tests.Windows.Fixtures // HardwareFact / ManualHardwareFact

/// Production-path hardware E2E for the spec-005 button-press test — the **live-boundary proof** of the
/// input side, the Phase G **Validation Gate** (`live-boundary-smoke`; the done line CI cannot give).
/// The companion `DiscoveryHardwareTests` proves the RX discovery path and `BaptismHardwareTests` proves
/// the claim/reset path on real silicon; this suite proves the third RX path — a baptized OPTIMUS-XP panel
/// emits the `VAR_WRITE` button-state frames the parser expects, and the tool scores each prompted button
/// on the **press** edge with the firmware-pinned polarity. It drives the SAME production chain the
/// headless integration suite drives, minus nothing on the RX side and minus the TX/baptism transmitter:
/// read loop -> `PcanCanFrameStream` -> `ButtonStateReassemblyObserver` -> `ButtonPressTestService` — so a
/// regression anywhere on that wire path fails here where the `FrozenClock`/`InMemoryButtonStateObserver`
/// suites cannot.
///
/// **Observability is the heartbeat, not WHO_I_AM (fix #270).** A baptized panel is silent on the WHO_I_AM
/// auto-address broadcast (`AAS_STAND_BY`; `CORRECTIONS.md` §C1), so it never appears in discovery. It
/// instead heartbeats its button-state `VAR_WRITE` on a directed CAN ID whose machineType (bits 23-16) is the
/// variant. The precondition is therefore the first `ButtonStateObservation` arriving (whose `Variant`
/// is OPTIMUS-XP), NOT a discovery row — the original WHO_I_AM precondition timed out on real silicon, the
/// defect this PR fixes.
///
/// **RX-only (the whole point of spec-005).** Unlike baptism, this chain has NO transmitter: the technician
/// presses the physical buttons; the tool only observes. `buildButtonPressChain` is the real
/// `PcanCanFrameStream` + `ButtonStateReassemblyObserver` + `ButtonPressTestService` (the system under test),
/// all riding the SAME `CanPortShare`/frame stream, mirroring `CompositionRoot`. Panel presence keys off
/// button-state recency (no discovery dependency).
///
/// **Polarity (R2, the #253 done line).** `KeyStateBitmap.PressedBit = 0uy`: on the wire pressed = bit `0`,
/// so a press is an active bit transitioning `1 -> 0` (`UserMain.c:1369,:978`). The legacy app scored on the
/// `0 -> 1` RELEASE edge and was field-proven only because a technician always releases. The
/// `Run_RealOptimusXpPanel_ScoresOnPressEdgeNotRelease` case makes the press-vs-release distinction OBSERVABLE
/// (press-and-hold; score must fire WHILE held) and asserts the press edge. If a real panel scores on
/// release, the fix is to flip the single `PressedBit` constant and re-run (quickstart §Polarity confirmation)
/// — that is Luca's bench action, NOT a redesign.
///
/// **Gating.** Every case carries `[<Trait("Category", "Hardware")>]`, so the standards CI category filter
/// (`Category!=Hardware`) excludes the suite at discovery time — it COMPILES in CI (the gate build proves it)
/// but never RUNS there. The env gate (`[<HardwareFact>]` -> `BPT_HARDWARE=1`; `[<ManualHardwareFact>]` ->
/// `BPT_HARDWARE_INTERACTIVE=1`) keeps it dormant on adapter-less dev boxes. The bench run is the #253
/// validation gate (T037), not part of CI:
///   $env:BPT_HARDWARE = "1"                # the prompted full run + the polarity confirmation
///   $env:BPT_HARDWARE_INTERACTIVE = "1"    # + the attended recovery / interruption cycle
///   dotnet test tests/ButtonPanelTester.Tests.Windows -c Release --filter "Category=Hardware"
///
/// **Case split (reported for the orchestrator / #253 bench tracker).**
///   * `[<HardwareFact>] Run_RealOptimusXpPanel_AllActiveButtonsScorePassOnPressEdge` — the prompted full
///     OPTIMUS-XP run, all four active buttons pressed in canonical order, terminal `Completed` all-`Pass`
///     (SC-001, SC-006; SC-002 ~1 s feedback is operator-observed, Console-noted).
///   * `[<HardwareFact>] Run_RealOptimusXpPanel_ScoresOnPressEdgeNotRelease` — the R2 polarity confirmation
///     (press-and-hold; score on the `1 -> 0` press edge).
///   * `[<ManualHardwareFact>] Run_RealOptimusXpPanel_InteractiveRecoveryAndInterruption` — the attended
///     cycle a clean run cannot carry: a deliberate Missed (SC-003) + Retry + Skip (FR-007/009), an
///     operator-verified Unexpected (SC-004), and the adapter unplugged mid-run -> `Interrupted LinkLost`,
///     never all-passed (SC-005). The mid-run adapter unplug is the defining `ManualHardwareFact` action
///     (the `BaptismHardwareTests` idiom), which is why SC-005 lands here rather than in the unattended full
///     run.
///
/// **Bench discipline.** This suite is receive-only — it MUTATES nothing on the panel (the technician's
/// presses are the only stimulus). Each case rebuilds its own production chain and tears it down in reverse
/// construction order. xUnit does not guarantee inter-case ordering; the operator keeps one baptized
/// OPTIMUS-XP panel powered on the bus throughout per the quickstart §Bench walkthrough run-sheet.
///
/// Tracked by [#253](https://github.com/luca-veronelli-stem/button-panel-tester/issues/253), the spec-005
/// bench-validation gate (it adds the full-run, Missed-timing, Unexpected, link-loss, and polarity hooks to
/// that checklist).

// --- budgets (deterministic bounded waits — never an unbounded spin) ---

/// First-heartbeat appearance budget (fix #270): a baptized OPTIMUS-XP panel heartbeats its button-state on
/// the ~182 ms idle cadence, so the first `ButtonStateObservation` should arrive within ~2 s of the read
/// loop starting. If none does, the panel is not baptized/heartbeating — a rig precondition, surfaced as a
/// diagnostic `Assert.Fail`.
let private heartbeatTimeout = TimeSpan.FromSeconds 2.0

/// Forensic run-correlation key for the bench run (fix #270): a baptized panel is silent on WHO_I_AM, so its
/// UUID is unavailable in the button-press path; with one panel under test at a time the run scope keys off
/// this sentinel (mirrors the GUI's `buttonPressRunKey`). The variant — the real identity the heartbeat
/// carries — drives the schema.
let private optimusRunKey = PanelUuid(0u, 0u, 0u)

/// Outer bound for the prompted four-button happy-path run: four active buttons, each with a 10 s
/// (`ButtonPressTest.testBudget`) window, plus operator reading/reaction slack. A button pressed too late
/// records `Missed` (non-terminal — the run stalls offering Retry/Skip), so a stall here is the operator
/// pressing outside a window, reported as such rather than spinning unbounded.
let private fullRunTimeout = TimeSpan.FromSeconds 90.0

/// Press-and-hold budget for the polarity case: the operator must press (and hold) the first button within
/// its 10 s window. Generous enough to read the prompt; a `Missed` before the press is distinguished from a
/// genuine polarity failure in the diagnostic.
let private pressHoldTimeout = TimeSpan.FromSeconds 15.0

/// Missed-detection budget: the per-button deadline is 10 s and the service's 250 ms tick records `Missed`
/// just past it on `SystemClock`; 14 s leaves slack for the tick granularity and reassembly.
let private missTimeout = TimeSpan.FromSeconds 14.0

/// Per-press scoring budget: the operator presses the prompted button within its 10 s window and the press
/// edge scores within ~1 s (SC-002); 12 s bounds the press itself.
let private pressTimeout = TimeSpan.FromSeconds 12.0

/// Synchronous-transition budget: `Retry`/`Skip` and a post-`Pass` advance settle on the calling thread, so
/// the next observable state appears within a couple of poll ticks — 3 s is generous slack.
let private settleTimeout = TimeSpan.FromSeconds 3.0

/// Mid-run unplug budget: the operator reads the prompt and physically unplugs the adapter, then
/// `CanLinkService` observes the link leave `Connected` and the FSM halts; 30 s covers the manual step plus
/// detection latency.
let private interruptTimeout = TimeSpan.FromSeconds 30.0

// --- helpers ---

/// Bounded wait for the FIRST button-state `ButtonStateObservation` from the panel under test (fix #270 —
/// a baptized panel is silent on WHO_I_AM, so its directed-id button-state heartbeat, not a discovery row, is
/// the presence + variant signal). Subscribes BEFORE the wait so no early heartbeat is missed, captures into
/// a thread-safe queue (the observer fires on the vendored read thread), and polls for the first one; `None`
/// on timeout, never an unbounded spin. The caller asserts the observation's `Variant` is OPTIMUS-XP.
let private waitForFirstButtonStateObservation
    (observer: IButtonStateObserver)
    (timeout: TimeSpan)
    : ButtonStateObservation option =
    let captured = System.Collections.Concurrent.ConcurrentQueue<ButtonStateObservation>()
    use _sub = observer.ButtonStateObserved |> Observable.subscribe captured.Enqueue
    let deadline = DateTime.UtcNow + timeout

    let rec spin () =
        match captured.TryDequeue() with
        | true, observation -> Some observation
        | false, _ when DateTime.UtcNow >= deadline -> None
        | false, _ ->
            Thread.Sleep 50
            spin ()

    spin ()

/// Bounded poll until `probe` holds; `false` on timeout, never an unbounded spin. The poll cadence (50 ms)
/// matches the discovery wait — fine-grained enough to observe a press edge promptly, coarse enough not to
/// busy-spin.
let private waitUntil (probe: unit -> bool) (timeout: TimeSpan) : bool =
    let deadline = DateTime.UtcNow + timeout

    let rec spin () =
        if probe () then true
        elif DateTime.UtcNow >= deadline then false
        else
            Thread.Sleep 50
            spin ()

    spin ()

/// Await a launched run to its terminal `ButtonPressTestState`, bounded by `timeout` (never an unbounded
/// wait); `None` if it did not complete in time. `CancellationToken.None` is passed at the call sites, so
/// the task completes with a terminal state rather than faulting.
let private awaitState (attempt: Task<ButtonPressTestState>) (timeout: TimeSpan) : ButtonPressTestState option =
    if attempt.Wait timeout then Some attempt.Result else None

/// Bounded poll until active-button `index` in the live `CurrentState` reaches `outcome`. Reads the results
/// vector from whichever non-`Idle` state is current (`Prompting`/`Completed`/`Interrupted` all carry it);
/// a resolved outcome (`Pass`/`Skipped`/`Missed`) persists in the vector after the prompt advances, so this
/// holds across an advance. `false` on timeout.
let private waitForOutcome
    (service: IButtonPressTestService)
    (index: int)
    (outcome: ButtonOutcome)
    (timeout: TimeSpan)
    : bool =
    waitUntil
        (fun () ->
            match service.CurrentState with
            | ButtonPressTestState.Prompting(_, _, results)
            | ButtonPressTestState.Completed results
            | ButtonPressTestState.Interrupted(_, results) -> index < results.Length && results.[index] = outcome
            | ButtonPressTestState.Idle -> false)
        timeout

/// Bounded poll until the live `CurrentState` is `Prompting` at `index` (the prompt has advanced to that
/// button). `false` on timeout.
let private waitForPromptIndex (service: IButtonPressTestService) (index: int) (timeout: TimeSpan) : bool =
    waitUntil
        (fun () ->
            match service.CurrentState with
            | ButtonPressTestState.Prompting(i, _, _) -> i = index
            | ButtonPressTestState.Idle
            | ButtonPressTestState.Completed _
            | ButtonPressTestState.Interrupted _ -> false)
        timeout

/// Builds the FULL production button-press RX chain over the real PEAK stack at 250 kbps and returns the
/// lifecycle service, the button-state observer (the presence + variant source — fix #270), the
/// button-press test service (the SUT), and a single `IDisposable` that tears everything down in REVERSE
/// construction order. It is the real `PcanCanFrameStream` + `ButtonStateReassemblyObserver` (the button-state
/// RX boundary) + `ButtonPressTestService` (RX-only — no transmitter), all riding the SAME `CanPortShare`/frame
/// stream, mirroring `CompositionRoot`. No discovery: a baptized panel is silent on WHO_I_AM, so presence
/// keys off button-state recency. The captured driver / port may be `None` if a test never opens the link;
/// the cleanup is a no-op in that case.
let private buildButtonPressChain
    ()
    : ICanLinkService * IButtonStateObserver * IButtonPressTestService * IDisposable =
    let mutable createdDriver: PCANManager option = None
    let mutable createdPort: CanPort option = None

    let share =
        new CanPortShare(fun () ->
            let driver = new PCANManager(TPCANBaudrate.PCAN_BAUD_250K)
            let port = new CanPort(driver :> IPcanDriver)
            createdDriver <- Some driver
            createdPort <- Some port
            port :> ICommunicationPort)

    let clock = SystemClock() :> IClock
    let link = PcanCanLink((fun () -> share.GetOrBuild()), NullLogger<PcanCanLink>.Instance)
    let frameStream = new PcanCanFrameStream(share, clock, NullLogger<PcanCanFrameStream>.Instance)
    let buttons = new ButtonStateReassemblyObserver(frameStream, NullLogger<ButtonStateReassemblyObserver>.Instance)
    let svc = CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)

    let service =
        new ButtonPressTestService(
            buttons,
            (svc :> ICanLinkService),
            clock,
            NullLogger<ButtonPressTestService>.Instance)

    let cleanup =
        { new IDisposable with
            member _.Dispose() =
                (service :> IDisposable).Dispose()
                (buttons :> IDisposable).Dispose()
                (frameStream :> IDisposable).Dispose()
                (link :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()

                createdPort |> Option.iter (fun p -> p.Dispose())

                createdDriver
                |> Option.iter (fun d ->
                    (d :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()) }

    (svc :> ICanLinkService), (buttons :> IButtonStateObserver), (service :> IButtonPressTestService), cleanup

// --- SC-001/SC-002/SC-006: the prompted four-button OPTIMUS-XP run scores all Pass (unattended-style) ---

[<Trait("Category", "Hardware")>]
[<HardwareFact>]
let Run_RealOptimusXpPanel_AllActiveButtonsScorePassOnPressEdge () =
    let link, observer, service, cleanup = buildButtonPressChain ()
    use _ = cleanup

    // InitializeAsync opens PcanCanLink (-> CanPort.ConnectAsync -> StartReading), so frames flow and
    // discovery coalesces the OPTIMUS-XP row while the link is Connected.
    link.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()

    match waitForFirstButtonStateObservation observer heartbeatTimeout with
    | None ->
        Assert.Fail(
            sprintf
                "no button-state heartbeat within %.1f s — verify a BAPTIZED OPTIMUS-XP panel (machineType 0x0A) is powered and heartbeating its button-state VAR_WRITE on its directed CAN id"
                heartbeatTimeout.TotalSeconds)
    | Some observation ->
        // Variant comes from the directed CAN id of the heartbeat (fix #270), not a discovery row.
        Assert.Equal(OptimusXp, observation.Variant)
        let schema = ButtonSchema.forVariant OptimusXp

        // SC-006: the prompt order is the canonical decals Light -> Suspension -> Up -> Down.
        Console.WriteLine(
            "Press the four OPTIMUS-XP buttons IN ORDER as prompted: Light -> Suspension -> Up -> Down. "
            + "Press each promptly (10 s window) and release; each should score Pass within ~1 s of the press (SC-002).")

        // Fire-and-forget: the run's Task completes only on a terminal state (here, all four buttons scored).
        let run = service.RunAsync(optimusRunKey, schema, CancellationToken.None)

        match awaitState run fullRunTimeout with
        | Some(ButtonPressTestState.Completed results) ->
            // SC-001/SC-006: every active button (Light/Suspension/Up/Down) scored Pass on its press edge.
            Assert.True(
                ButtonPressTest.allActivePassed results,
                sprintf
                    "expected all four active OPTIMUS-XP buttons to score Pass on the press edge, observed %A — a Missed entry means a press came after the 10 s window (press sooner); a Skipped entry means it was skipped"
                    results)
        | Some(ButtonPressTestState.Interrupted(reason, _)) ->
            Assert.Fail(
                sprintf
                    "run interrupted (%A) before completing — for LinkLost verify the adapter stayed plugged; for PanelLost verify the OPTIMUS-XP panel kept announcing WHO_I_AM (a claimed panel that goes silent is pruned from discovery)"
                    reason)
        | Some other -> Assert.Fail(sprintf "run resolved to a non-terminal state %A (a service bug)" other)
        | None ->
            Assert.Fail(
                sprintf
                    "the four-button run did not complete within %.0f s — verify the panel emits VAR_WRITE button-state frames on press (RX wiring) and that you pressed each prompted button inside its window"
                    fullRunTimeout.TotalSeconds)

// --- R2 / SC-002: scoring fires on the PRESS edge (bit 1 -> 0), not on release (unattended-style) ---

[<Trait("Category", "Hardware")>]
[<HardwareFact>]
let Run_RealOptimusXpPanel_ScoresOnPressEdgeNotRelease () =
    let link, observer, service, cleanup = buildButtonPressChain ()
    use _ = cleanup

    link.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()

    match waitForFirstButtonStateObservation observer heartbeatTimeout with
    | None ->
        Assert.Fail(
            sprintf
                "no button-state heartbeat within %.1f s — power a BAPTIZED OPTIMUS-XP panel that heartbeats its button-state before the polarity check"
                heartbeatTimeout.TotalSeconds)
    | Some observation ->
        Assert.Equal(OptimusXp, observation.Variant)
        let schema = ButtonSchema.forVariant OptimusXp
        let firstDecal = schema.Active.[0].Decal // "Light" (DOWN, bit 1)

        // The press-vs-release distinction made OBSERVABLE: press AND HOLD. Scoring on the press edge
        // (PressedBit = 0, the 1 -> 0 transition) scores WHILE the button is still held; scoring on the
        // release edge would not score until the button is let go.
        Console.WriteLine(
            sprintf
                "POLARITY CHECK (R2): when ready, press and HOLD the first button (%s) — do NOT release it until this test reports Pass. Press within ~8 s. Scoring on the PRESS edge (bit 1 -> 0) scores while held."
                firstDecal)

        let run = service.RunAsync(optimusRunKey, schema, CancellationToken.None)

        let scoredWhileHeld =
            waitUntil
                (fun () ->
                    match service.CurrentState with
                    | ButtonPressTestState.Prompting(_, _, results)
                    | ButtonPressTestState.Completed results
                    | ButtonPressTestState.Interrupted(_, results) -> results.Length > 0 && results.[0] = Pass
                    | ButtonPressTestState.Idle -> false)
                pressHoldTimeout

        if not scoredWhileHeld then
            // Distinguish "pressed too late -> Missed" from a genuine release-edge result.
            let diagnosis =
                match service.CurrentState with
                | ButtonPressTestState.Prompting(_, _, results) when results.Length > 0 && results.[0] = Missed ->
                    "the button timed out (Missed) before the press registered — re-run and press within the 10 s window"
                | _ ->
                    "the tool is NOT scoring on the press edge. Flip `PressedBit` (in src/ButtonPanelTester.Core/Can/KeyStateBitmap.fs) and re-run per quickstart §Polarity confirmation — do NOT redesign"

            Assert.Fail(
                sprintf "polarity check: %s did not score Pass while held within %.0f s — %s" firstDecal pressHoldTimeout.TotalSeconds diagnosis)

        Console.WriteLine(
            sprintf "Polarity confirmed: %s scored Pass on the press edge (bit 1 -> 0) while held. You may release now." firstDecal)

        // Drain so the leftover run does not hold the chain mid-prompt while cleanup disposes it.
        ignore run

// --- SC-003/SC-004/SC-005, FR-007/008/009/013: attended recovery + interruption cycle ---

[<Trait("Category", "Hardware")>]
[<ManualHardwareFact>]
let Run_RealOptimusXpPanel_InteractiveRecoveryAndInterruption () =
    let link, observer, service, cleanup = buildButtonPressChain ()
    use _ = cleanup

    link.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()

    match waitForFirstButtonStateObservation observer heartbeatTimeout with
    | None ->
        Assert.Fail(
            sprintf
                "no button-state heartbeat within %.1f s — power a BAPTIZED OPTIMUS-XP panel that heartbeats its button-state before this attended leg"
                heartbeatTimeout.TotalSeconds)
    | Some observation ->
        Assert.Equal(OptimusXp, observation.Variant)
        let schema = ButtonSchema.forVariant OptimusXp
        let decal i = schema.Active.[i].Decal

        Console.WriteLine(
            "Interactive button-press cycle on a baptized OPTIMUS-XP panel. Follow each numbered prompt; do NOT unplug the adapter until step 5 asks you to.")

        let run = service.RunAsync(optimusRunKey, schema, CancellationToken.None)

        // 1) SC-003 / FR-007 — let the first button time out -> Missed.
        Console.WriteLine(sprintf "1) Do NOT press anything. Let the first button (%s) time out (~10 s)." (decal 0))

        if not (waitForOutcome service 0 Missed missTimeout) then
            Assert.Fail(
                sprintf
                    "button 0 (%s) did not reach Missed within %.0f s — verify the per-button countdown is running and you did not press it"
                    (decal 0)
                    missTimeout.TotalSeconds)

        // 2) FR-009 Retry — re-arm the Missed button back to Pending, then press it -> Pass.
        Console.WriteLine(sprintf "2) Missed. Re-arming %s (Retry). When prompted again, press %s within 10 s." (decal 0) (decal 0))
        service.Retry()

        if not (waitForOutcome service 0 Pending settleTimeout) then
            Assert.Fail(sprintf "Retry did not return button 0 (%s) to Pending — FR-009 retry re-arm regressed" (decal 0))

        if not (waitForOutcome service 0 Pass pressTimeout) then
            Assert.Fail(
                sprintf
                    "button 0 (%s) did not score Pass after Retry within %.0f s — press the prompted button promptly on the press edge"
                    (decal 0)
                    pressTimeout.TotalSeconds)

        // 3) SC-004 / FR-008 — a wrong active press is recorded Unexpected with NO advance. The non-advance
        //    is OPERATOR-VERIFIED (a deterministic wrong-press-then-confirm cannot be forced from the test,
        //    the `BaptismHardwareTests` idiom for states a fixture cannot synthesise); the test then waits
        //    for the correct press to advance.
        if not (waitForPromptIndex service 1 settleTimeout) then
            Assert.Fail(sprintf "prompt did not advance to button 1 (%s) after button 0 Pass — FR-010 advance regressed" (decal 1))

        Console.WriteLine(
            sprintf
                "3) %s is prompted. First press a WRONG active button (e.g. %s) ONCE: confirm in the log/GUI it is recorded Unexpected and the prompt STAYS on %s (SC-004, operator-verified). Then press %s to continue."
                (decal 1)
                (decal 2)
                (decal 1)
                (decal 1))

        if not (waitForOutcome service 1 Pass pressTimeout) then
            Assert.Fail(
                sprintf
                    "button 1 (%s) did not score Pass within %.0f s — after the wrong-press check, press the prompted button"
                    (decal 1)
                    pressTimeout.TotalSeconds)

        // 4) FR-009 Skip — skip the next button -> Skipped (never Pass) and advance.
        if not (waitForPromptIndex service 2 settleTimeout) then
            Assert.Fail(sprintf "prompt did not advance to button 2 (%s) after button 1 Pass" (decal 2))

        Console.WriteLine(sprintf "4) %s is prompted. Skipping it (Skip) — it must record Skipped, not Pass." (decal 2))
        service.Skip()

        if not (waitForOutcome service 2 Skipped settleTimeout) then
            Assert.Fail(sprintf "Skip did not record button 2 (%s) as Skipped — FR-009 skip regressed" (decal 2))

        // 5) SC-005 / FR-013 — unplug the adapter mid-run -> Interrupted LinkLost, never all-passed.
        if not (waitForPromptIndex service 3 settleTimeout) then
            Assert.Fail(sprintf "prompt did not advance to the last button (%s) after Skip" (decal 3))

        Console.WriteLine(
            sprintf
                "5) %s is prompted. UNPLUG the PEAK adapter now (do NOT press) — the run must end Interrupted (LinkLost), never all-passed."
                (decal 3))

        match awaitState run interruptTimeout with
        | Some(ButtonPressTestState.Interrupted(InterruptReason.LinkLost, partial)) ->
            // FR-013 / interrupt_excludes_all_passed: a torn-down run never reports all-active-passed.
            Assert.False(
                ButtonPressTest.allActivePassed partial,
                "an Interrupted (LinkLost) run reported all-active-passed — SC-005 / interrupt_excludes_all_passed regressed")
        | Some(ButtonPressTestState.Interrupted(InterruptReason.PanelLost, _)) ->
            Assert.Fail(
                "run ended Interrupted PanelLost, not LinkLost — the panel stopped heartbeating its button-state (silence past the panel-lost threshold) before the unplug; re-run and unplug the adapter promptly when prompted")
        | Some other ->
            Assert.Fail(
                sprintf
                    "run did not end Interrupted LinkLost after the adapter unplug, ended %A — verify you physically unplugged the adapter (CanLinkService must observe the link leave Connected)"
                    other)
        | None ->
            Assert.Fail(
                sprintf
                    "no terminal outcome within %.0f s of the unplug prompt — verify the adapter was physically unplugged"
                    interruptTimeout.TotalSeconds)
