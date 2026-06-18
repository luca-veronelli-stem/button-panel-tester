module Stem.ButtonPanelTester.Tests.Windows.Integration.Can.Hardware.BaptismHardwareTests

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
open Peak.Can.Basic.BackwardCompatibility
open Core.Interfaces
open Core.Models // DeviceVariantConfig
open Infrastructure.Protocol.Hardware
open Xunit
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary // IClock
open Stem.ButtonPanelTester.Infrastructure // SystemClock
open Stem.ButtonPanelTester.Infrastructure.Can // CanPortShare, PcanCanLink, PcanCanFrameStream, WhoIAmReassemblyObserver, SetAddressAckObserver, ProtocolMasterSequenceTransmitter
open Stem.ButtonPanelTester.Services.Can // CanLinkService, PanelDiscoveryService, BaptismService + their interfaces
open Stem.ButtonPanelTester.Tests.Windows.Fixtures // HardwareFact / ManualHardwareFact

/// Production-path hardware E2E for the spec-004 baptism workflow — the **live-boundary proof** of
/// the CONFIRMED-ADOPTION criterion (confirmation rework, #232/#212). The companion
/// `DiscoveryHardwareTests` proves the RX discovery path on real silicon; this suite proves the
/// full claim/reset/recovery path, driving the SAME production chain PLUS the master-sequence TX
/// boundary (`ProtocolMasterSequenceTransmitter`) and the `0x25` SET_ADDRESS ACK RX observer
/// (`SetAddressAckObserver`) into `BaptismService` —
/// read loop -> `PcanCanFrameStream` -> { `WhoIAmReassemblyObserver`, `SetAddressAckObserver` } ->
/// `PanelDiscoveryService` + `BaptismService` <- `ProtocolMasterSequenceTransmitter` — so a
/// regression anywhere on that wire path fails here where the headless suites cannot.
///
/// **On the corrected criterion (the whole point of #218 + #232).** `BaptismOutcome.Succeeded` no
/// longer fires on SET_ADDRESS write-completion: the production FSM (`Baptism.step`, Core) only
/// reaches `Succeeded` after it observes the `0x25` application ACK addressed to the tool AND holds
/// broadcast-silence through the full adoption window (`Baptism.adoptionBudget`). So **reaching
/// `Succeeded` end-to-end on real hardware IS the SC-002 confirmed-silence proof** — the service can
/// no longer false-succeed on a panel that kept announcing or whose ACK was dropped (it would land
/// `ClaimNotAdopted` instead). The SC-001 definitive-outcome bound is therefore
/// `announceBudget + adoptionBudget` (the two waits run sequentially), NOT the pre-rework 6 s.
///
/// **First bench validation of the ACK arbId.** `SetAddressAckObserver` keys on
/// `frame.CanId = toolSrid` (`DeviceVariantConfig.DefaultSenderId`, the reverse-engineered directed-
/// reply arbId; see that file's XML doc). This suite is where that filter is validated against real
/// silicon for the FIRST time: if the arbId is wrong, the ACK is never observed and
/// `Baptize_RealVirginPanel_ConfirmedAdoptionWithinCombinedBudget` lands `ClaimNotAdopted`, not
/// `Succeeded` — exactly the finding RW08 is meant to surface. The failure messages call this out.
///
/// **Gating.** Every case carries `[<Trait("Category", "Hardware")>]`, so the standards CI category
/// filter (`Category!=Hardware`) excludes the suite at discovery time. The env gate
/// (`[<HardwareFact>]` -> `BPT_HARDWARE=1`; `[<ManualHardwareFact>]` -> `BPT_HARDWARE_INTERACTIVE=1`)
/// keeps it dormant on adapter-less dev boxes. The bench run is the #218 validation gate (RW08), not
/// part of CI:
///   $env:BPT_HARDWARE = "1"                # the unattended claim/reset legs
///   $env:BPT_HARDWARE_INTERACTIVE = "1"    # + the attended recovery / full-cycle legs
///   dotnet test tests/ButtonPanelTester.Tests.Windows -c Release --filter "Category=Hardware"
///
/// **FR-014 (the tool emits ONLY master-sequence frames) is NOT an automated assertion here** — it
/// is an ATTENDED bench-capture step, exactly as `DiscoveryHardwareTests` documents its SC-003
/// receive-only capture. PCANBasic exposes no TX-frame counter to assert against, so to confirm
/// FR-014 the operator runs PCAN-View (or captures a `.trc`) on a SECOND channel during the claim /
/// reset legs and verifies the tool's channel emits only WHO_ARE_YOU + SET_ADDRESS master-sequence
/// frames (no stray traffic). Record that observation in the RW08 bench validation.
///
/// **State mutation / bench discipline.** Unlike the receive-only discovery suite, these cases MUTATE
/// the panel: the unattended claim leg leaves the panel CLAIMED (silent), and the reset leg returns
/// it to virgin. xUnit does not guarantee inter-case ordering, so the operator manages panel state
/// per the RW08 run-sheet (e.g. reset-to-virgin between unattended claim re-runs); each case rebuilds
/// its own production chain and tears it down in reverse order.
///
/// Tracked by [#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112)
/// (the live cross-spec `Category=Hardware` tracker): this suite adds the claim E2E, the reset E2E,
/// the confirmed-silence verification, and the guided recovery to that checklist.

// --- budgets (deterministic bounded waits — never an unbounded spin) ---

/// Virgin-row appearance budget: a powered virgin panel broadcasts WHO_I_AM roughly every ~4 s, so
/// 6 s comfortably covers one full segmented broadcast cycle plus reassembly (the
/// `DiscoveryHardwareTests` constant).
let private discoveryTimeout = TimeSpan.FromSeconds 6.0

/// SC-001 definitive-outcome bound under the confirmed-adoption criterion. The announce wait
/// (`Baptism.announceBudget`, 6 s) and the adoption-confirmation wait (`Baptism.adoptionBudget`, 6 s)
/// run SEQUENTIALLY, and `Succeeded` only lands on the adoption-deadline tick (silence held), so a
/// confirmed adoption cannot complete before ~announce + adoption and is bounded by their sum. Adds
/// slack for the real re-announce cadence, reassembly, and the 250 ms adoption-deadline tick
/// granularity. This supersedes the pre-rework "within 6 s" (write-completion success is gone).
let private confirmedAdoptionTimeout =
    Baptism.announceBudget + Baptism.adoptionBudget + TimeSpan.FromSeconds 3.0

/// Reset-to-virgin write-completion budget: reset success is CAN-write completion (UNCHANGED by
/// #232 — the firmware never replies, FR-010), so the two dual-fwType broadcasts complete fast; 6 s
/// is generous slack for the PEAK write path.
let private resetWriteTimeout = TimeSpan.FromSeconds 6.0

/// Operator-recovery bound: how many claim rounds a single baptize attempts before surfacing the
/// failure. The spec defines TWO remedies by outcome (see `claimWithRecovery`): a `WaitTimeout` is
/// re-claimed on the still-announcing panel (FR-005), a `ClaimNotAdopted` is Reset-to-virgin then
/// re-baptized (FR-006a/FR-015). RW09 (rig) confirmed that resetting on a `WaitTimeout` discards the
/// panel's late variant-announce and re-races the same fresh-claim timing — so the two outcomes need
/// different remedies. THREE bounded rounds model the operator remedy without ever spinning unbounded.
let private maxClaimAttempts = 3

// --- helpers ---

/// The selected uuid of a Virgin row in a discovery snapshot, if any (machineType `0xFF`). On a clean
/// single-panel bench this is the one physical panel.
let private tryVirginUuid (snapshot: PanelsOnBus) : PanelUuid option =
    snapshot
    |> Map.toSeq
    |> Seq.tryPick (fun (uuid, obs) -> if obs.VariantIdentity = Virgin then Some uuid else None)

/// Bounded poll of the LIVE discovery snapshot (`IPanelDiscoveryService.PanelsOnBus`, the pull
/// accessor — not an accumulated history) for a CURRENTLY-present Virgin row; `None` on timeout,
/// never an unbounded spin. The pull accessor (rather than `DiscoveryHardwareTests`'s
/// snapshot-collector) is the robust primitive across the full-cycle case: a stale historical
/// snapshot would mis-report a panel that has since been claimed/silent.
let private waitForCurrentVirginUuid (discovery: IPanelDiscoveryService) (timeout: TimeSpan) : PanelUuid option =
    let deadline = DateTime.UtcNow + timeout

    let rec spin () =
        match tryVirginUuid discovery.PanelsOnBus with
        | Some uuid -> Some uuid
        | None when DateTime.UtcNow >= deadline -> None
        | None ->
            Thread.Sleep 50
            spin ()

    spin ()

/// Await a launched baptize/reset attempt to its terminal outcome, bounded by `timeout` (never an
/// unbounded wait); `None` if it did not complete in time. `CancellationToken.None` is passed at the
/// call sites, so the task completes with an outcome rather than faulting.
let private awaitOutcome (attempt: Task<'T>) (timeout: TimeSpan) : 'T option =
    if attempt.Wait timeout then Some attempt.Result else None

/// Builds the FULL production baptism chain over the real PEAK stack at 250 kbps and returns the
/// lifecycle service, the discovery service, the baptism service, and a single `IDisposable` that
/// tears everything down in REVERSE construction order. It is `DiscoveryHardwareTests.buildChain`
/// PLUS the real `ProtocolMasterSequenceTransmitter` (the master-sequence TX boundary) and the real
/// `SetAddressAckObserver` (the `0x25` ACK RX boundary), both riding the SAME `CanPortShare` /
/// frame stream the discovery chain taps — one PEAK handle serves both directions, mirroring
/// `CompositionRoot`. The captured driver / port may be `None` if a test never opens the link; the
/// cleanup is a no-op in that case.
let private buildBaptismChain () : ICanLinkService * IPanelDiscoveryService * IBaptismService * IDisposable =
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
    let observer = new WhoIAmReassemblyObserver(frameStream, NullLogger<WhoIAmReassemblyObserver>.Instance)
    let svc = CanLinkService(link, clock, NullLogger<CanLinkService>.Instance)

    let discovery =
        new PanelDiscoveryService(observer, (svc :> ICanLinkService), clock, NullLogger<PanelDiscoveryService>.Instance)

    // Baptism additions over the discovery chain. Both ride the SAME `share`/`frameStream`: the
    // transmitter builds its vendored service lazily via `CanPortShare.OnBuilt` (no eager PEAK
    // P/Invoke), and the ACK observer taps the same reassembled RX stream the WHO_I_AM observer does.
    // `senderId`/`toolSrid` is the vendored `DeviceVariantConfig.DefaultSenderId` — the srid the tool
    // sends under and the directed-reply arbId the ACK is addressed to (CompositionRoot parity).
    let transmitter =
        new ProtocolMasterSequenceTransmitter(
            share,
            DeviceVariantConfig.DefaultSenderId,
            NullLogger<ProtocolMasterSequenceTransmitter>.Instance)

    let setAddressAck =
        new SetAddressAckObserver(
            frameStream,
            DeviceVariantConfig.DefaultSenderId,
            NullLogger<SetAddressAckObserver>.Instance)

    let baptism =
        new BaptismService(
            transmitter,
            observer,
            setAddressAck,
            discovery,
            (svc :> ICanLinkService),
            clock,
            NullLogger<BaptismService>.Instance)

    let cleanup =
        { new IDisposable with
            member _.Dispose() =
                (baptism :> IDisposable).Dispose()
                (setAddressAck :> IDisposable).Dispose()
                (transmitter :> IDisposable).Dispose()
                (discovery :> IDisposable).Dispose()
                (observer :> IDisposable).Dispose()
                (frameStream :> IDisposable).Dispose()
                (link :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()

                createdPort |> Option.iter (fun p -> p.Dispose())

                createdDriver
                |> Option.iter (fun d ->
                    (d :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()) }

    (svc :> ICanLinkService), (discovery :> IPanelDiscoveryService), (baptism :> IBaptismService), cleanup

/// The operator-recovery claim: select the currently-settled Virgin row and baptize it, and when the
/// claim does NOT take, run the spec's PER-OUTCOME remedy up to `maxClaimAttempts` bounded rounds
/// (never an unbounded loop):
///   * `WaitTimeout` (FR-005 / acceptance 3 / the late-re-announcement edge case): the WHO_ARE_YOU
///     took effect and the panel re-announces the variant just past the announce budget, so re-run
///     Baptize on the SAME still-announcing panel WITHOUT a reset — the re-claim catches the late
///     announce. Resetting here (RW09 rig finding) would discard that progress and re-race the same
///     fresh-claim timing, so it can never converge for an announce-edge `WaitTimeout`.
///   * `ClaimNotAdopted` (FR-006a / FR-015): half-baptized (assignment written, panel never went
///     silent) — only a Reset-to-virgin clears it; reset (best-effort), re-acquire the virgin row,
///     then re-baptize.
/// Every other outcome — `UnexpectedVariant`/`PanelDisappeared`/`LinkLost`/`TransmissionFailure`, or a
/// `WaitTimeout`/`ClaimNotAdopted` that survives the final round — returns IMMEDIATELY (a real failure
/// the bench must surface). Returns `Some Succeeded` on confirmed adoption (possibly after a recovery
/// round), `Some <outcome>` for a genuine failure, or `None` when there is no settled Virgin row to
/// claim (initially, or after a reset that did not surface one) / no definitive outcome within budget.
let private claimWithRecovery
    (discovery: IPanelDiscoveryService)
    (baptism: IBaptismService)
    (variant: MarketingVariant)
    (ct: CancellationToken)
    : BaptismOutcome option =
    let rec attempt n uuid =
        match awaitOutcome (baptism.BaptizeAsync(uuid, variant, ct)) confirmedAdoptionTimeout with
        | Some Succeeded -> Some Succeeded
        | Some WaitTimeout when n < maxClaimAttempts ->
            // FR-005: the panel adopted the variant and re-announces it late; re-run Baptize on the SAME
            // still-announcing panel (no reset) — the re-claim catches the late announce within budget.
            Console.WriteLine(
                sprintf
                    "FR-005 recovery: claim of %A as %A ended WaitTimeout (attempt %d/%d) — re-run Baptize on the still-announcing panel (no reset)."
                    uuid
                    variant
                    n
                    maxClaimAttempts)

            attempt (n + 1) uuid
        | Some ClaimNotAdopted when n < maxClaimAttempts ->
            // FR-015: half-baptized — only a Reset clears it. Reset (best-effort), re-acquire the virgin
            // row, then re-baptize. A reset that does not surface a virgin row within budget ends recovery.
            Console.WriteLine(
                sprintf
                    "FR-015 recovery: claim of %A as %A ended ClaimNotAdopted (attempt %d/%d) — Reset-to-virgin then re-baptize."
                    uuid
                    variant
                    n
                    maxClaimAttempts)

            awaitOutcome (baptism.ResetAsync(true, ct)) resetWriteTimeout |> ignore

            (match waitForCurrentVirginUuid discovery discoveryTimeout with
             | Some virginUuid -> attempt (n + 1) virginUuid
             | None -> None)
        | other -> other

    match waitForCurrentVirginUuid discovery discoveryTimeout with
    | None -> None
    | Some uuid -> attempt 1 uuid

// --- SC-001/002, FR-006: a powered virgin panel reaches CONFIRMED adoption (unattended) ---

[<Trait("Category", "Hardware")>]
[<HardwareFact>]
let Baptize_RealVirginPanel_ConfirmedAdoptionWithinCombinedBudget () =
    let link, discovery, baptism, cleanup = buildBaptismChain ()
    use _ = cleanup

    // InitializeAsync opens PcanCanLink (-> CanPort.ConnectAsync -> StartReading), so frames flow and
    // discovery coalesces the virgin row while the link is Connected.
    link.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()

    try
        // Operator path: claim the settled Virgin row, recovering per the spec's outcome-specific remedy
        // (WaitTimeout -> re-claim the still-announcing panel; ClaimNotAdopted -> Reset -> re-baptize) if
        // the claim does not take. Reaching `Succeeded` (possibly after a bounded recovery round) is the
        // SC-002 confirmed-adoption proof, end-to-end on real silicon.
        let variant = EdenXp

        match claimWithRecovery discovery baptism variant CancellationToken.None with
        | Some Succeeded ->
            // SC-002: reaching `Succeeded` under the production FSM means the service observed the
            // 0x25 SET_ADDRESS ACK addressed to the tool AND held broadcast-silence through the full
            // adoption window — the confirmed-adoption proof, end-to-end on real silicon. The bus-
            // capture cross-check (the claimed UUID goes silent / its row ages out within the 15 s
            // prune window) is supplementary, logged for the RW08 capture rather than asserted.
            Console.WriteLine(
                sprintf
                    "Confirmed adoption as %A: the claimed panel is silent by design; its row ages out within the 15 s prune window (SC-002 capture)."
                    variant)
        | Some other ->
            Assert.Fail(
                sprintf
                    "baptism as %A ended %A, not Succeeded even after bounded recovery — on a confirmed-adoption run, ClaimNotAdopted here most likely means the 0x25 ACK was not observed: verify the tool srid (%d) and capture the bus to confirm the directed-reply arbId"
                    variant
                    other
                    DeviceVariantConfig.DefaultSenderId)
        | None ->
            Assert.Fail(
                "no Virgin Panels-on-bus row / no definitive baptism outcome within budget — verify a powered VIRGIN panel is on the bus and re-announcing")
    finally
        // Teardown hygiene: a failed leg can leave the panel half-baptized and pollute the next run, so
        // best-effort Reset-to-virgin before `cleanup` (use _ = cleanup) disposes the chain. Swallowed:
        // a faulting reset here must never mask the test's own assertion.
        try
            awaitOutcome (baptism.ResetAsync(true, CancellationToken.None)) resetWriteTimeout |> ignore
        with _ ->
            ()

// --- SC-003: a claimed panel reset-to-virgin surfaces a Virgin row within ~6 s (unattended) ---

[<Trait("Category", "Hardware")>]
[<HardwareFact>]
let Reset_RealClaimedPanel_VirginRowWithin6s () =
    let link, discovery, baptism, cleanup = buildBaptismChain ()
    use _ = cleanup

    link.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()

    // Reset-to-virgin is a write-completion success (UNCHANGED by #232): the two dual-fwType
    // WHO_ARE_YOU(0xFF, reset=1) broadcasts complete -> `Sent`. The firmware never replies, so write
    // completion is authoritative for the reset side (FR-010).
    match awaitOutcome (baptism.ResetAsync(true, CancellationToken.None)) resetWriteTimeout with
    | Some Sent -> ()
    | Some other ->
        Assert.Fail(
            sprintf
                "reset-to-virgin ended %A, not Sent — verify the link is Connected and a panel is on the bus"
                other)
    | None ->
        Assert.Fail(
            sprintf "reset-to-virgin did not complete its broadcasts within %.1f s" resetWriteTimeout.TotalSeconds)

    // SC-003: a matching (previously claimed/silent) panel re-announces as Virgin within ~6 s of the
    // reset broadcast — the same virgin-row detection the discovery suite uses; with no panel
    // attached the list simply stays empty (quickstart acceptance 2.5).
    match waitForCurrentVirginUuid discovery discoveryTimeout with
    | Some _ -> ()
    | None ->
        Assert.Fail(
            sprintf
                "no Virgin row within %.1f s after reset — verify a CLAIMED (silent) panel was attached before the reset"
                discoveryTimeout.TotalSeconds)

// --- SC-007, FR-015: detect-and-recover a not-adopted panel (Reset -> re-baptize) (attended) ---

[<Trait("Category", "Hardware")>]
[<ManualHardwareFact>]
let Recover_NotAdoptedPanel_ResetThenReBaptize () =
    let link, discovery, baptism, cleanup = buildBaptismChain ()
    use _ = cleanup

    link.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()

    // OPERATOR-VERIFIED (by hand, NOT asserted): the not-adopted state — a half-baptized panel that
    // ACKed but kept announcing, or whose adoption window elapsed without the 0x25 ACK — comes from an
    // F1-style race and cannot be forced deterministically. The operator induces it, confirms in the
    // GUI that the baptism surface shows the FR-015 ClaimNotAdopted guidance (claim did not take ->
    // Reset-to-virgin then re-baptize), then lets this leg drive the recovery and assert the F1 fix.
    Console.WriteLine(
        "Attach a NOT-ADOPTED (half-baptized / claimed) panel and confirm the GUI shows the ClaimNotAdopted recovery guidance; this test now drives Reset -> re-baptize and asserts the F1 fix.")

    // Recovery step 1 — reset-to-virgin. Write completion = Sent (unchanged by #232).
    match awaitOutcome (baptism.ResetAsync(true, CancellationToken.None)) resetWriteTimeout with
    | Some Sent -> ()
    | Some other -> Assert.Fail(sprintf "recovery reset ended %A, not Sent" other)
    | None -> Assert.Fail(sprintf "recovery reset did not complete within %.1f s" resetWriteTimeout.TotalSeconds)

    // Recovery step 2 — re-baptize the post-reset Virgin re-announce RIGHT AWAY. The ASSERTED
    // automatable fact is the F1 fix (`virgin_keeps_waiting`): a transient selected-uuid Virgin
    // re-announce must KEEP WAITING, never false-fail as `UnexpectedVariant`. The full recover-to-
    // `Succeeded` across the GUI depends on the real adoption ACK and is operator-verified (RW08).
    match waitForCurrentVirginUuid discovery discoveryTimeout with
    | None ->
        Assert.Fail(sprintf "no Virgin row within %.1f s after the recovery reset" discoveryTimeout.TotalSeconds)
    | Some uuid ->
        let attempt = baptism.BaptizeAsync(uuid, EdenXp, CancellationToken.None)

        match awaitOutcome attempt confirmedAdoptionTimeout with
        | Some(UnexpectedVariant announced) ->
            Assert.Fail(
                sprintf
                    "re-baptize after reset false-failed as UnexpectedVariant %A — the F1 fix regressed: a transient post-reset Virgin re-announce must keep waiting, never reject"
                    announced)
        | Some outcome ->
            // Not UnexpectedVariant (F1 holds). Succeeded / WaitTimeout / ClaimNotAdopted are all
            // acceptable here for the AUTOMATED assertion; the operator confirms full recovery.
            Console.WriteLine(
                sprintf
                    "Re-baptize after reset ended %A (NOT UnexpectedVariant — F1 holds). Operator: confirm the GUI completed the recovery to a confirmed adoption."
                    outcome)
        | None ->
            Assert.Fail(
                sprintf
                    "no definitive re-baptize outcome within %.1f s after the recovery reset"
                    confirmedAdoptionTimeout.TotalSeconds)

// --- SC-004, FR-013: full cycle across all four variants, zero residual state (attended) ---

[<Trait("Category", "Hardware")>]
[<ManualHardwareFact>]
let FullCycle_FourVariants_ZeroResidualState () =
    let link, discovery, baptism, cleanup = buildBaptismChain ()
    use _ = cleanup

    link.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult()

    Console.WriteLine(
        "Full-cycle bench check: one physical panel re-typed across all four variants. Keep it powered on the bus throughout; the tool resets it to virgin between cycles.")

    try
        // Each cycle is claim (with the spec's per-outcome recovery) -> confirmed adoption ->
        // reset-to-virgin. The tool holds NO state between attempts (FR-013), so the 4th cycle is
        // indistinguishable from the 1st: every cycle must reach `Succeeded` then return the panel to a
        // Virgin row. Uniform success across all four, with no degradation, is the zero-residual-state
        // evidence (SC-004). The selected uuid is the one physical panel; its UUID is stable across
        // resets (a hardware id, not an assigned address).
        for variant in [ EdenXp; OptimusXp; R3LXp; EdenBs8 ] do
            match claimWithRecovery discovery baptism variant CancellationToken.None with
            | Some Succeeded -> ()
            | Some other ->
                Assert.Fail(
                    sprintf "cycle %A: baptism ended %A, not Succeeded even after bounded recovery" variant other)
            | None ->
                Assert.Fail(
                    sprintf
                        "cycle %A: no Virgin row / no confirmed adoption within budget — verify the panel is powered and re-announcing"
                        variant)

            // Per-leg success reset stays: return the panel to virgin before the next variant.
            match awaitOutcome (baptism.ResetAsync(true, CancellationToken.None)) resetWriteTimeout with
            | Some Sent -> ()
            | Some other -> Assert.Fail(sprintf "cycle %A: reset ended %A, not Sent" variant other)
            | None ->
                Assert.Fail(sprintf "cycle %A: reset did not complete within %.1f s" variant resetWriteTimeout.TotalSeconds)
    finally
        // Teardown hygiene: a failed leg can leave the panel half-baptized and pollute the next run, so
        // best-effort Reset-to-virgin before `cleanup` (use _ = cleanup) disposes the chain. Swallowed:
        // a faulting reset here must never mask the test's own assertion.
        try
            awaitOutcome (baptism.ResetAsync(true, CancellationToken.None)) resetWriteTimeout |> ignore
        with _ ->
            ()
