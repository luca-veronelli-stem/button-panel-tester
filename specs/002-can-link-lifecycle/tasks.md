---

description: "Task list for docs/002-lifecycle-spec-refresh (Phase B docs + Phase C impl reconcile)"
---

# Tasks: CAN Link Lifecycle

**Input**: Design documents from `specs/002-can-link-lifecycle/`.

**Prerequisites**: [plan.md](./plan.md) `ce2c901`, [spec.md](./spec.md) `f04318e`, [research.md](./research.md) `55f0fc9`, [data-model.md](./data-model.md) `0be6872`, [contracts/can-link-port.md](./contracts/can-link-port.md) `c5b72fb`, [quickstart.md](./quickstart.md) (refresh pending in this queue), [migration-map.md](./migration-map.md) (pending in this queue).

**Status**: Phase B rewrite (2026-05-27). This task list supersedes the substrate task list shipped via PR-A [#120](https://github.com/luca-veronelli-stem/button-panel-tester/pull/120) / PR-B [#121](https://github.com/luca-veronelli-stem/button-panel-tester/pull/121) / PR-C [#122](https://github.com/luca-veronelli-stem/button-panel-tester/pull/122) and amended through the Phase 3.5 fix queue (#133 .. #147). Panel-discovery tasks moved to [`specs/003-panel-discovery/tasks.md`](../003-panel-discovery/tasks.md) via Phase A ([#151](https://github.com/luca-veronelli-stem/button-panel-tester/pull/151), 2026-05-26). Phase B redesigned the FSM from four families + `Recoverable / Fatal` severity to the five-family shape with sub-discriminators in case payloads ([research.md](./research.md) §1 R2 + R3, [data-model.md](./data-model.md) §1.1).

**Scope (forward-looking)**: this list enumerates the **remaining Phase B doc work** (items 7–11 from [plan.md](./plan.md) §Phase B queue) and the **Phase C impl reconcile** that brings `main`'s substrate code to the Phase B shape. Substrate tasks T001..T072 from PR-A/PR-B/PR-C and Phase 3.5 are not retained as forward-looking — they are already on `main`. See **Completed (substrate)** below for the pointer block.

**Phase C scope note (per [plan.md](./plan.md) §Blockers)**: the Phase C plan is its own track; the per-PR / per-commit cut for Phase C is **not** in scope for the Phase B PR. This task list enumerates Phase C reconcile work for traceability so the Phase B PR documents what reconciliation needs. Phase C ships in one or more follow-up PRs after this doc PR lands.

**Tests**: REQUIRED. The plan's Constitution Check (Principles I + II + IV) mandates Lean Phase 2 proofs, FsCheck property suites, integration tests against `InMemoryCanLink`, GUI tests via `Avalonia.Headless`, and a hardware E2E suite gated as `[<Trait("Category", "Hardware")>]` (excluded from default CI, tracked by [#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112)). Per the constitution, the order is Lean spec → xUnit test → F# impl.

## Format: `[ID] [P?] Description`

- **[P]**: Can run in parallel — different `.fsproj` projects or independent files outside the F# compile graph (Lean, JSON fixtures, docs).
- Phase B doc tasks and Phase C reconcile tasks are cross-cutting reconciliation; they serve both US1 and US2 simultaneously because the FSM redesign is a single concept that the user stories project onto. No `[Story]` labels are used in this list.
- File paths are repo-relative from the root.

## Path conventions

Archetype A, two-TFM split (continues from substrate; see [plan.md](./plan.md) §Project Structure):

- `src/ButtonPanelTester.Core/` — `net10.0` F# domain + ports (extended with `Can/`).
- `src/ButtonPanelTester.Services/` — `net10.0` F# use cases (extended with `Can/`).
- `src/ButtonPanelTester.Infrastructure.Protocol/` — `net10.0-windows` C#, vendor copy (shared with spec-003, frozen per [contracts/vendor-manifest.md](./contracts/vendor-manifest.md)).
- `src/ButtonPanelTester.Infrastructure/` — `net10.0-windows` F# adapters (extended with `Can/`).
- `src/ButtonPanelTester.GUI/` — `net10.0-windows` F# Avalonia + FuncUI shell (extended with `Can/`).
- `tests/ButtonPanelTester.Tests/` — `net10.0` F# xUnit + FsCheck.
- `tests/ButtonPanelTester.Tests.Windows/` — `net10.0-windows` F# Avalonia.Headless + Infrastructure tests.
- `lean/Stem/ButtonPanelTester/Phase2/` — Lean 4 Phase 2 modules (shared with spec-003).

---

## Completed (substrate, shipped on `main`)

The substrate scaffolding, foundational types, MVP US1 implementation, and Phase 3.5 amendments are merged. Pointer block only — full task-by-task history lives in git and in [plan.md](./plan.md) §Status §Completed. The substrate's FSM payload shape (four families + `Recoverable / Fatal` severity) is superseded by Phase B; reconciliation is enumerated under **Phase C** below.

- **Phase 1 — Setup (substrate T001..T011)** — vendor C# protocol stack, solution + test partitioning, `lean/lakefile.toml` Phase 2 `[[lean_lib]]`, `VENDOR-GUARD.md`. Shipped via PR-A [#120](https://github.com/luca-veronelli-stem/button-panel-tester/pull/120).
- **Phase 2 — Foundational (substrate T012..T033, shared with spec-003)** — Core/Can domain types, `Ports.fs`, virtual adapters, FsCheck property suites scaffold, six Lean Phase 2 modules. Shipped via PR-B [#121](https://github.com/luca-veronelli-stem/button-panel-tester/pull/121).
- **Phase 3 — US1 MVP (substrate T034..T043)** — `PcanCanLink`, substrate `CanLinkService`, `CompositionRoot` wiring, `CanStatusRow`, integration + GUI + hardware tests. Shipped via PR-C [#122](https://github.com/luca-veronelli-stem/button-panel-tester/pull/122).
- **Phase 3.5 — Fix queue (substrate T-amend-1 .. T-amend-7)** — boot-order regression, cold-start hang, unexpected PEAK status mapping, sticky-`since`, click-feedback contract, Reconnect visibility, severity-in-headline. Shipped via PRs [#133](https://github.com/luca-veronelli-stem/button-panel-tester/pull/133), [#134](https://github.com/luca-veronelli-stem/button-panel-tester/pull/134), [#135](https://github.com/luca-veronelli-stem/button-panel-tester/pull/135), [#138](https://github.com/luca-veronelli-stem/button-panel-tester/pull/138), [#141](https://github.com/luca-veronelli-stem/button-panel-tester/pull/141), [#145](https://github.com/luca-veronelli-stem/button-panel-tester/pull/145), [#147](https://github.com/luca-veronelli-stem/button-panel-tester/pull/147).
- **Phase A — spec-002 / spec-003 split** — panel-discovery extracted to [`specs/003-panel-discovery/`](../003-panel-discovery/). Shipped via PR [#152](https://github.com/luca-veronelli-stem/button-panel-tester/pull/152).

**Substrate carry-overs not retired by Phase B** (still tracked; promoted into Phase C below):

- [#132](https://github.com/luca-veronelli-stem/button-panel-tester/issues/132) — hot-plug regression test (substrate T-amend-10). Addressed in Phase C by `T213`.
- [#136](https://github.com/luca-veronelli-stem/button-panel-tester/issues/136) — cold-start poll-exhaust reclassification (substrate T-amend-8). Superseded by Phase B's five-family redesign; the substrate issue resolves by absorption into Phase C and is closed when `T212` lands.
- [#140](https://github.com/luca-veronelli-stem/button-panel-tester/issues/140) — GUI tooltip test (substrate T-amend-11). Orthogonal to FSM reshape; carries forward as `T223`.
- [#142](https://github.com/luca-veronelli-stem/button-panel-tester/issues/142) — env-gated `[<HardwareFact>]` xUnit attribute (substrate T-amend-9). Orthogonal; carries forward as `T222`.
- [#143](https://github.com/luca-veronelli-stem/button-panel-tester/issues/143) — driver-download remediation link in the `Faulted(DriverNotInstalled, _, _)` chip (substrate T-amend-12). Orthogonal; carries forward as `T224`.

---

## Phase B — Documentation refresh (this PR)

Each task is a single vertical commit (per `bisect-safe` / `vertical-commits`). Items 1–6 already landed; items 7–11 are the remaining scope of `docs/002-lifecycle-spec-refresh` → `main`.

- [X] T101 spec.md rewrite — `f04318e`.
- [X] T102 checklists/spec-quality.md — `b99282a` + tally fix `f4ddf89`.
- [X] T103 data-model.md rewrite — `0be6872`.
- [X] T104 research.md rewrite — `55f0fc9`.
- [X] T105 plan.md rewrite — `ce2c901`.
- [X] T106 contracts/can-link-port.md refresh — `c5b72fb`.
- [ ] T107 tasks.md rewrite — this file.
- [ ] T107b spec-001 [#156](https://github.com/luca-veronelli-stem/button-panel-tester/issues/156) harmonisation — fix-up amendment commit reshaping the Live/Cached precedent citations in landed `spec.md` (Session 2026-05-27 §Framing; Dependencies section) and `research.md` (R8 Rationale) to point at spec-001 #156 Option γ (dictionary chip = sync-subsystem truth; copy-health rendered as decoration via origin marker + stale-seed glyph). The substantive two-layer truth-and-acknowledge precedent (FR-009 — subsystem-truth chip + button-level click cue) is preserved unchanged; only the chip-carving example wording shifts from "Live / Cached duality" to "sync-subsystem state + copy-health decoration". Doc-only. Commit body uses `Refs #156` (not `Closes`/`Fixes` — `github-pr-auto-close` rule: spec-002 does not close spec-001's #156).
- [ ] T108 quickstart.md refresh — rewrite bench walkthrough for the five-family shape: drop substrate `Connected` / `Initializing` / `Disconnected` language, drop the FR-001 boot-gate sentence ([research.md](./research.md) R10), drop the `(Live or Cached chip)` opener (line 48 today) per spec-001 [#156](https://github.com/luca-veronelli-stem/button-panel-tester/issues/156) Option γ — the dictionary chip is sync-subsystem state, not data-source state. Fix the branch name to `docs/002-lifecycle-spec-refresh`. Mirror plan.md's §Searching retry policy + §FR-006 cancellation budget + §CHK028 hot-plug acceptance traceability.
- [ ] T109 migration-map.md — load-bearing for Phase C. Rows: substrate `CanLinkState` constructor → Phase B family + discriminator (with FR cross-references); substrate `DisconnectReason` / `ErrorClassification` → Phase B `IdleCause` / `SearchAttempt` / `FaultCause` / `AdapterCandidate`; per-consumer touch list (`PcanCanLink`, `CanLinkService`, `CanStatusRow`, `CompositionRoot`, `InMemoryCanLink`, `CanLinkStateTransitionsProperties`, `RecoverableToFatalEscalationTests`, `BootOrderTests`, `PcanLifecycleTests`, `Phase2/CanLinkState.lean`) with the Phase C task ID that reshapes each.
- [ ] T110 cleanup — `git rm` Phase B session handoff artefacts (`HANDOFF*.md`, `PROMPT*.md`, `OPEN-FINDINGS.md`, `NEXT-SESSION*.md`).
- [ ] T111 PR — single `docs/002-lifecycle-spec-refresh` → `main` PR on github. Doc-only; local gate skipped per [memory](feedback_skip_local_gate_for_docs); CI on GitHub Actions confirms.

**Phase B checkpoint**: `docs/002-lifecycle-spec-refresh` has landed every artefact in the queue, OPEN-FINDINGS items 1–9 are closed (10, 11 accepted as style/wording), and the PR opens on github.

---

## Phase C — Impl reconcile to Phase B shape

Forward-looking. Reconciles `main`'s substrate code (four-family FSM + `Recoverable / Fatal` severity) to the Phase B five-family shape. **Out of scope for this PR**; enumerated here so the Phase B doc PR documents the reconciliation work the Phase B redesign implies. Phase C ships in one or more follow-up PRs after the Phase B doc PR lands.

**Sequencing pattern (per `bisect-safe.md` item 2 — additive-then-remove)**: substrate types live alongside the new five-family types under temporary `*V2` names through Phase C.A–C.E, GUI rewires to the new pipeline in Phase C.E (substrate now orphaned but still compiling), and Phase C.F atomically removes the substrate types and renames `*V2` → canonical. Stubs are never left at the tip of Phase C.F (`bisect-safe.md` item 5).

Phase C task IDs use the `T2NN` series so they don't collide with substrate `T001..T072`. Per Constitution Principle I + II ordering, Lean theorem is added (Phase C.B) before the F# impl that consumes the new family is wired in (Phase C.D–E); new FsCheck property suites (`T207` / `T208` / `T209`) commit **RED before** `T206` fills in the impl that turns them GREEN (Phase C.D, TDD-first).

### Phase C.A — Additive five-family DU

The Phase B types are added under a `V2` suffix alongside the substrate. No consumers are touched; the commit compiles green because nothing yet imports the new module.

- [ ] T201 Add `src/ButtonPanelTester.Core/Can/CanLinkStateV2.fs` introducing `IdleCause`, `SearchAttempt`, `FaultCause`, `AdapterCandidate`, `AdapterIdentification` (the substrate `AdapterIdentification` record can stay in the existing file if its shape matches [data-model.md](./data-model.md) §3.1; otherwise relocate here), and the new `CanLinkStateV2` DU per [data-model.md](./data-model.md) §1.1 / §2.1 / §3.1. Co-locate `module Bridge` with `toLegacy : CanLinkStateV2 -> CanLinkState option` if the migration commits need a translation seam — otherwise omit. Wire the new file into `src/ButtonPanelTester.Core/ButtonPanelTester.Core.fsproj` immediately after the existing `Can/CanLinkState.fs` entry so the F# compile order is stable.

### Phase C.B — Lean reshape

Independent of F# compilation. Per Constitution Principle I, the Lean spec is re-authored before the F# pipeline that mechanises it is wired up.

- [ ] T202 Re-author `lean/Stem/ButtonPanelTester/Phase2/CanLinkState.lean` to the post-Phase-B theorem set: retire `transition_reachability_closed`; add `state_classification_total` (over the new five-family inductive), `fault_cause_total` ([data-model.md](./data-model.md) §1.3 Invariant #2), `idle_cause_total` (Invariant #3 — degenerate but kept), `faulted_reconnect_target_total` (Invariant #4 — FR-008 Reconnect bifurcation mechanised). The "one theorem per file" rule is relaxed for this file per [plan.md](./plan.md) §Constitution Check I + [research.md](./research.md) R16 Phase B note — the four lemmas all `by cases` over the same `CanLinkState` inductive. `Phase2/PassiveObserver.lean` (`observe_emits_no_transmit`) is unchanged. Confirm `cd lean && lake build` green with no `sorry`, no custom axioms.

### Phase C.C — Adapter rewrite (Infrastructure)

The new port and adapter are added alongside the substrate; both compile coexistent. The GUI still consumes the substrate adapter at this point.

- [ ] T203 Add `src/ButtonPanelTester.Infrastructure/Can/PcanAdapterEnumeration.fs` — new Infrastructure-internal helper that enumerates PEAK adapters via the vendored stack and produces an `AdapterCandidate list` for `PcanCanLinkV2` to iterate (FR-012). Forward-referenced by [data-model.md](./data-model.md) §2.1 + [plan.md](./plan.md) §Project Structure + [contracts/can-link-port.md](./contracts/can-link-port.md) production adapter section. Helper, not a port.
- [ ] T204 Extend `src/ButtonPanelTester.Core/Can/Ports.fs` with `ICanLinkV2` (the five-family-payload port — same five-member signature as substrate `ICanLink`, payload type `CanLinkStateV2`). Add `src/ButtonPanelTester.Infrastructure/Can/PcanCanLinkV2.fs` implementing `ICanLinkV2` per [contracts/can-link-port.md](./contracts/can-link-port.md) production adapter contract: drive FR-012 iteration internally over `PcanAdapterEnumeration.enumerate()`, request exclusive driver-level access on the OpenAsync that lands `Open` (FR-010), emit one of the five families on every transition with sticky-`since` (FR-004), bridge the vendored stack's PnP arrival event into a `Searching(Polling, now)` re-entry ([research.md](./research.md) R7), and propagate `CancellationToken` through the in-flight vendored-driver call so a Stop during `Opening` lands `Idle(UserPaused, now)` within the ≤ 250 ms budget ([plan.md](./plan.md) §FR-006 cancellation budget). No Recoverable / Fatal counter — `PeakErrorText` maps directly to a `FaultCause` constructor ([contracts/can-link-port.md](./contracts/can-link-port.md) production adapter §"No severity classification"). `ILogger<PcanCanLinkV2>` required (archetype A, non-optional per [`stem-logging`](~/.claude/skills/stem-logging/SKILL.md) Step 1).
- [ ] T205 Add `tests/ButtonPanelTester.Tests/Fakes/Can/InMemoryCanLinkV2.fs` — scripted `seq<CanLinkStateV2 * TimeSpan>` virtual adapter per [contracts/can-link-port.md](./contracts/can-link-port.md) virtual adapter contract. Honours `CancellationToken` so the FsCheck Stop-during-`Opening` scenarios exercise FR-006 propagation independent of the PEAK driver. Sequences after `T204` (depends on the `ICanLinkV2` port definition).

### Phase C.D — Property suites RED, then service GREEN

Per the Constitution Principle II + the global `tdd` rule, the three new FsCheck suites commit **before** `T206` (each suite is RED on first commit because the V2 FSM transition function inside `T201` is a stub and `CanLinkServiceV2` does not yet exist). `T206` then fills in the impl that turns them GREEN. Substrate `CanLinkService` is still alive at this point but the GUI does not yet route through V2.

- [ ] T207 [P] Re-author `tests/ButtonPanelTester.Tests/Property/Can/CanLinkStateTransitionsProperties.fs` for the five-family shape: from any reachable `CanLinkStateV2`, applying any valid input event (operator Stop / Start / Reconnect or any observation-driven event from [data-model.md](./data-model.md) §1.2) lands in another reachable state per the same transition graph ([plan.md](./plan.md) §Constitution Check II — replaces the retired `transition_reachability_closed`). Commits RED; passes once `T201`'s transition function + `T206`'s service realise the graph.
- [ ] T208 [P] Add `tests/ButtonPanelTester.Tests/Property/Can/CanLinkStickyTimestampProperties.fs` — new FsCheck suite mechanising FR-004 / Invariant #5: passive re-observation of the same family + discriminator preserves `since`; family change or discriminator change updates `since`; a user-initiated round-trip back into the same family via an intervening state updates `since` on the second arrival ([plan.md](./plan.md) §Constitution Check II, [contracts/can-link-port.md](./contracts/can-link-port.md) lifecycle invariant 4). Commits RED against `InMemoryCanLinkV2` scripts; passes once `T206`'s service preserves `since` across passive re-observation.
- [ ] T209 [P] Add `tests/ButtonPanelTester.Tests/Property/Can/LinkStateChangedFamilyExhaustiveProperties.fs` — new FsCheck suite mechanising FR-014 + the FR-002 chip-colour total projection: over a quantified random event sequence from `Searching(Polling)`, every family in `{ Idle, Searching, Opening, Open, Faulted }` appears in some emission, and the chip-colour projection is total — every emission carries one of `{ green, grey, red }` ([plan.md](./plan.md) §Constitution Check II). Commits RED; passes once `T206`'s service exposes a `LinkStateChanged` stream whose generated traces cover every family.
- [ ] T206 Add `src/ButtonPanelTester.Services/Can/ICanLinkServiceV2.fs` + `src/ButtonPanelTester.Services/Can/CanLinkServiceV2.fs` — lifecycle slice over the new shape; fills in `T201`'s transition function so `T207`/`T208`/`T209` turn GREEN. Retires the substrate's Recoverable→Fatal escalation logic ([research.md](./research.md) §3). Exposes FR-012 multi-adapter iteration through the lifecycle API (the per-candidate iteration runs inside `PcanCanLinkV2`; the service exposes the resulting `LinkStateChanged` stream and the `Start` / `Stop` / `Reconnect` entry points per [contracts/can-link-port.md](./contracts/can-link-port.md) production adapter section). Drives the FR-014 `LinkStateChanged` fan-out. Implements the `Searching` 5-second periodic poll via a single `System.Threading.PeriodicTimer` cancelled by the service's lifetime `CancellationToken` ([plan.md](./plan.md) §Searching retry policy). Honours the FR-006 cancellation budget (≤ 250 ms). `ILogger<CanLinkServiceV2>` required (archetype A); emits structured log templates per [plan.md](./plan.md) §CHK024 (family-agnostic transition log on every `LinkStateChanged.OnNext`; Open / Faulted supersets; FR-011 external-contention Information entry conditional on the vendored stack surfacing the event). Operator-initiated transitions wrap the in-flight call in `_logger.BeginScope` with keys `OperatorAction` (`"Stop" | "Start" | "Reconnect"`), `CorrelationId` (fresh `Guid`), `CandidateChannelHandle` (when known).
  - Sub-bullets (folded test edits): extend `tests/ButtonPanelTester.Tests/Integration/Can/CanLinkServiceLifecycleTests.fs` with the assertions below. Existing substrate assertions continue to run against the substrate pipeline until `T212` removes the substrate.
    - `SearchingNoAdapterEnumeratedAppearsWithinOneSecondOfLaunch` (SC-002) — `InMemoryCanLinkV2` scripted to emit `Searching(NoAdapterEnumerated, _)` immediately after `CanLinkServiceV2` startup; assert within ≤ 1 s of construction. Covers spec.md §SC-002 (no-adapter launch case is bench-independent — the assertion runs against the fake, not real hardware).
    - `StopDuringOpeningCancelsWithinBudget` (FR-006) — asserts the ≤ 250 ms cancellation budget against `InMemoryCanLinkV2` ([plan.md](./plan.md) §FR-006 cancellation budget, [contracts/can-link-port.md](./contracts/can-link-port.md) lifecycle invariant 2).
    - `StopReleasesAdapterHandle` (CHK018 CI surrogate) — fake `IPcanDriver` (vendored stack's injection seam) records the `CAN_Uninitialize` call sequence after a Stop click ([plan.md](./plan.md) §CHK018).
    - `ContentionEventEmitsExactlyOneInformationLogEntry` (SC-012) — captures `ILogger<CanLinkServiceV2>` records via an `InMemoryLoggerProvider` (or `ITestOutputHelper` provider stub), scripts a fake vendored-stack contention surface to raise N events while the FSM is in `Open`, asserts exactly N Information-level entries matching the FR-011 template ([plan.md](./plan.md) §CHK024). The assertion is conditional on the fake surfacing the event — when the production vendored stack is silent, the test passes trivially with zero entries, consistent with FR-011's conditional MUST.

### Phase C.E — GUI rewire + remove the boot gate

The GUI side migrates to the V2 pipeline; the substrate pipeline becomes orphaned but still compiles. The composition root drops the dictionary-boot gate per [research.md](./research.md) R10.

- [ ] T210 Refresh `src/ButtonPanelTester.GUI/Can/CanStatusRow.fs` for the five-family projection: chip colour from `CanLinkStateV2` family (green for `Open`, red for `Faulted`, grey for `Idle` / `Searching` / `Opening` — FR-002); headline `<family> · <discriminator detail>` with FR-003's em-dash convention; detail affordance for adapter identification + baud rate + multi-line cause + `since` (FR-005); Start / Stop / Reconnect button visibility per the [spec.md](./spec.md) §"Operator-initiatable transitions" affordance map (FR-006 / FR-007 / FR-008); FR-009 click-acknowledge cue (`IsEnabled = false` + `⟳` glyph from `DictionaryStatusRow.fs:151-158`) on the clicked button for the duration of the in-flight call ([plan.md](./plan.md) §FR-009 sub-perceptual cue note — no minimum-visibility floor). FR-017 keyboard navigation + screen-reader labels.
  - Sub-bullets (folded test edits): re-author `tests/ButtonPanelTester.Tests.Windows/Gui/Can/CanStatusRowTests.fs` for the five-family shape; the load-bearing assertion is SC-010 (Reconnect click → button `IsEnabled = false` AND `Content = "⟳"` from click time through the next FSM emission, against `Avalonia.Headless`).
- [ ] T211 Rewire `src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs` — register `ICanLinkV2 = PcanCanLinkV2`, `ICanLinkServiceV2 = CanLinkServiceV2`; drop the dictionary-boot gate so the CAN sub-program starts in parallel with the dictionary sub-program ([research.md](./research.md) R10 + R17 Phase B note). Substrate `ICanLink` / `CanLinkService` registrations are removed in `T212`, not here.
  - Sub-bullets (folded test edits):
    - Re-author `tests/ButtonPanelTester.Tests/Integration/BootOrderTests.fs` (shipped via [#133](https://github.com/luca-veronelli-stem/button-panel-tester/pull/133)) as a decoupling regression — assert dictionary and CAN sub-programs start in parallel, and the CAN row reaches `Searching(Polling, _)` independent of dictionary-fetch boot completion ([research.md](./research.md) R10, FR-015).
    - Add `tests/ButtonPanelTester.Tests/Integration/Can/DictionaryIndependenceTests.fs` (Phase B refresh of the substrate file) — FsCheck-driven where practical, asserts `IDictionaryService.SourceChanged` emits zero events while `CanLinkServiceV2` walks through `Searching` / `Faulted(cause, _, _)` / `Idle(UserPaused)` paths driven by `InMemoryCanLinkV2`. Covers SC-006 ("CAN failure does not affect dictionary status row, across 100% of trials") + FR-015.

### Phase C.F — Remove substrate, rename V2 → canonical

Atomic removal commit. Per `bisect-safe.md` item 5, no stubs survive past this commit.

- [ ] T212 Remove the substrate four-family pipeline atomically and rename V2 → canonical:
  - Delete substrate `DisconnectReason`, `ErrorClassification`, `CanLinkState`, substrate `ICanLink`, substrate `PcanCanLink`, substrate `CanLinkService`, substrate `ICanLinkService` from `src/ButtonPanelTester.Core/Can/CanLinkState.fs`, `src/ButtonPanelTester.Core/Can/Ports.fs`, `src/ButtonPanelTester.Infrastructure/Can/PcanCanLink.fs`, `src/ButtonPanelTester.Services/Can/CanLinkService.fs`, `src/ButtonPanelTester.Services/Can/ICanLinkService.fs`.
  - Delete `tests/ButtonPanelTester.Tests/Fakes/Can/InMemoryCanLink.fs` (substrate fake), `tests/ButtonPanelTester.Tests/Integration/Can/RecoverableToFatalEscalationTests.fs` (the retired escalation test).
  - Rename `Core/Can/CanLinkStateV2.fs` → `Core/Can/CanLinkState.fs`; rename `ICanLinkV2` → `ICanLink`; rename `PcanCanLinkV2.fs` → `PcanCanLink.fs`; rename `CanLinkServiceV2.fs` → `CanLinkService.fs`; rename `ICanLinkServiceV2.fs` → `ICanLinkService.fs`; rename `InMemoryCanLinkV2.fs` → `InMemoryCanLink.fs`. Update `.fsproj` Compile entries.
  - Update every consumer (composition root, GUI, tests) to drop the `V2` suffix. The bridge module (if introduced in `T201`) is deleted in this commit.
  - Reshape `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/PcanLifecycleTests.fs` for the five-family shape — adjust SC-001 / SC-003 / SC-004 / SC-008 / SC-009 / SC-011 assertions against the renamed canonical types. `Category=Hardware` excluded from default CI; the file must still compile so it ships in this atomic commit. Closes [#136](https://github.com/luca-veronelli-stem/button-panel-tester/issues/136) by absorption.
- [ ] T213 Add `HotPlugRecoveryAfterUnplug` to `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/PcanLifecycleTests.fs` per [plan.md](./plan.md) §CHK028 — `Category=Hardware`. Asserts the FSM transit `Open → Searching(Polling, _) → Opening(candidate, _) → Open` driven by an unplug followed by re-seat, without operator input, within the SC-004 ≤ 5-second budget. Closes [#132](https://github.com/luca-veronelli-stem/button-panel-tester/issues/132).

### Phase C.G — Polish, audits, and release gating

Cross-cutting concerns; tasks marked `[P]` are independent and parallelisable.

- [ ] T214 Logging audit per [`docs/Standards/LOGGING.md`](../../docs/Standards/LOGGING.md) and the `stem-logging` skill: verify `_logger?.` patterns are absent (archetype A — non-optional); template messages use named parameters not string interpolation (CA2254 via `AnalysisLevel = latest-recommended`); `LogError` / `LogCritical` take the exception as the first argument when available; `BeginScope` correlation keys (`OperatorAction`, `CorrelationId`, `CandidateChannelHandle`) wrap operator-initiated calls per [plan.md](./plan.md) §CHK024; no `Console.WriteLine` / `Debug.WriteLine` / `Trace.WriteLine` in production code paths; no secrets / credentials / PII at `Information+`. Covers `PcanCanLink`, `CanLinkService`, `PeakErrorText`, `CanStatusRow`.
- [ ] T215 [P] Add a `[Unreleased]` entry to `CHANGELOG.md` summarising the spec-002 lifecycle landing — one or two lines per the project's CHANGELOG conventions.
- [ ] T216 [P] XML doc audit per [`docs/Standards/COMMENTS.md`](../../docs/Standards/COMMENTS.md) for every new public type and member: `IdleCause`, `SearchAttempt`, `FaultCause`, `AdapterCandidate`, `AdapterIdentification`, the new `CanLinkState`, `ICanLink`, `ICanLinkService`, `PcanAdapterEnumeration` exports.
- [ ] T217 [P] Async-discipline audit per [`docs/Standards/CANCELLATION.md`](../../docs/Standards/CANCELLATION.md) + [`docs/Standards/THREAD_SAFETY.md`](../../docs/Standards/THREAD_SAFETY.md) and the `stem-async-discipline` skill: `CancellationToken` propagated through every lifecycle entry (`OpenAsync` / `CloseAsync` / `ReconnectAsync`); shared state in `PcanCanLink` uses an actor or primitive (the existing `SemaphoreSlim(1)` lifetime); `ConfigureAwait` policy archetype-A-correct; no sync-over-async in the lifecycle paths.
- [ ] T218 [P] Compliance check for Principle V on the lifecycle path: grep for OS user / machine name / MAC / SID under `src/ButtonPanelTester.Core/Can/`, `src/ButtonPanelTester.Services/Can/`, `src/ButtonPanelTester.Infrastructure/Can/`, `src/ButtonPanelTester.GUI/Can/` — expected zero hits. Confirms [plan.md](./plan.md) §Constitution Check V ("no identity-bearing data on this feature's path"). Append a "Common gotchas" tail entry to [quickstart.md](./quickstart.md) if anything surfaces.
- [ ] T219 `cd lean && lake build` — confirm Phase 2 theorems compile with no `sorry` and no custom axioms after `T202` lands. (`lake build` is SLOW — run once at Phase C.G closeout, not per-commit; if Phase 2 has churned during Phase C the failure surfaces here.)
- [ ] T220 [P] Run `eng/vendor-protocol-stack.ps1 -RehashOnly` and confirm `VENDOR.sha256` is current — drift gate is apparently not wired in standards@v1.9.0 CI per [memory](project_bpt_vendor_sha256_ci_gap). Run manually after any `Infrastructure.Protocol` edit; expected no diff because Phase C does not touch the vendor copy.
- [ ] T221 SC-007 verification with an external CAN bus capture tool (independent of BPT). Confirm zero CAN frames originating from the tool throughout a representative bench session ([plan.md](./plan.md) §Constitution Check IV — passive observer). Attach the capture summary to the Phase C PR.
- [ ] T222 [P] Carry-over from substrate T-amend-9 ([#142](https://github.com/luca-veronelli-stem/button-panel-tester/issues/142)) — env-gated `[<HardwareFact>]` xUnit attribute so `Category=Hardware` tests are skipped (not failed) when the bench is unavailable. Orthogonal to the FSM reshape.
- [ ] T223 [P] Carry-over from substrate T-amend-11 ([#140](https://github.com/luca-veronelli-stem/button-panel-tester/issues/140)) — GUI tooltip test asserting `CanStatusRow` detail renders the `since` / `openedAt` timestamp via Avalonia.Headless. Orthogonal to the FSM reshape; lands in `tests/ButtonPanelTester.Tests.Windows/Gui/Can/CanStatusRowTests.fs`.
- [ ] T224 Carry-over from substrate T-amend-12 ([#143](https://github.com/luca-veronelli-stem/button-panel-tester/issues/143)) — render a driver-download remediation link inside the `Faulted · driver-not-installed — install the PEAK driver` row (FR-008 button caption note, FR-003 detail string). Land in `src/ButtonPanelTester.GUI/Can/CanStatusRow.fs`.

**Phase C exit gate**: `dotnet build -c Release` green; `dotnet test --filter "Category!=Hardware"` green with the three new property suites + integration tests + GUI tests passing; `lake build` green with five theorems; `Category=Hardware` suite (`PcanLifecycleTests` + `HotPlugRecoveryAfterUnplug`) green when run on the bench; bench walkthrough in [quickstart.md](./quickstart.md) reproduces SC-001 / SC-002 / SC-003 / SC-004 / SC-005 / SC-008 / SC-009 / SC-010 / SC-011; external bus capture confirms SC-007.

---

## Dependencies & execution order

### Phase B (this PR)

- `T107 → T107b → T108 → T109 → T110 → T111`. All six are doc-only commits on `docs/002-lifecycle-spec-refresh`; the PR opens at `T111` after the working tree is clean of session handoff artefacts.

### Phase C (follow-up PRs)

- `T201` (Phase C.A) is the entry point; nothing before it.
- `T202` (Phase C.B) is independent of F# compilation; can land in parallel with `T201` but is sequenced after for readability.
- `T203 → T204 → T205` (Phase C.C). Strictly sequential — `T204` introduces the `ICanLinkV2` port that `T205` implements.
- `T207 / T208 / T209 → T206` (Phase C.D, TDD order). The three property suites are `[P]` against each other and land RED on first commit (each suite references `T201`'s types + `T205`'s fake but the FSM transition function is still a stub). `T206` then fills in the impl that turns them GREEN; its integration sub-bullets ride along.
- `T210 → T211` (Phase C.E). `T210` can precede or follow `T211`; the GUI refresh and the composition-root rewire are in different projects.
- `T212` (Phase C.F) is the atomic substrate-removal commit; it can only land after every consumer is migrated to V2 (i.e., after Phase C.E). `T213` follows because it adds a new hardware test against the renamed canonical types.
- `T214`–`T224` (Phase C.G) — `T214` and `T219` sequence after `T212`; the others marked `[P]` can land in any order.

Per `bisect-safe.md`, every commit in Phase C is independently green for `dotnet build -c Release` and `dotnet test --filter "Category!=Hardware"`; `lake build` is checked at `T202` (Phase C.B) and re-confirmed at `T219` (Phase C.G).

### Parallel opportunities

- Phase B: every queue item is sequential (one artefact per commit).
- Phase C: `T207`, `T208`, `T209`, `T215`–`T218`, `T220`, `T222`, `T223` are all `[P]`. The cluster `T207`–`T209` is the largest parallelisable batch — three FsCheck files land in any order before `T206` (RED first per Phase C.D ordering).

---

## Implementation strategy

**Phase B first.** This PR is doc-only. The remaining tasks (`T107`–`T111`) commit on `docs/002-lifecycle-spec-refresh` and land via `gh pr merge --rebase` on github (Bitbucket mirrors via Actions).

**Phase C as one or more follow-up PRs.** Per [plan.md](./plan.md) §Blockers the Phase C plan is its own track. The recommended cut is one PR per sub-phase (C.A through C.G) — each is a small vertical slice and the additive-then-remove discipline keeps every commit bisect-safe. Sub-phases C.A through C.E ship the new pipeline alongside the substrate; C.F is the atomic substrate removal (single bisect-safe commit at the tip of its PR); C.G is the audit + release-gate sweep. Combining adjacent sub-phases into a single PR is acceptable if the diff stays reviewable.

**MVP equivalence**: at Phase C.E checkpoint the GUI consumes the V2 pipeline and the user-facing US1 + US2 outcomes are deliverable end-to-end. At Phase C.F the substrate is gone and the canonical names are restored. The bench-feel acceptance check is the quickstart walkthrough after C.F.

---

## Notes

- `[P]` tasks = different files in different `.fsproj` projects or outside the F# compile graph; no dependencies on incomplete tasks in the same sub-phase.
- Per Constitution Principle I + II ordering: Lean theorem (`T202`) lands before the F# impl that mechanises it consumes the new family; new FsCheck property suites (`T207` / `T208` / `T209`) commit RED **before** `T206` fills in the impl that turns them GREEN.
- Commit after each task (vertical slice, `bisect-safe` + `vertical-commits`).
- Stubs (bridge functions, `*V2` suffixes) introduced for the additive-then-remove pattern are removed atomically at `T212`. No stub survives past Phase C.F.
- Hardware tests (`Category=Hardware`) are excluded from default CI per [#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112). They must still compile after every Phase C commit — `T212` reshapes `PcanLifecycleTests.fs` against the renamed canonical types in the same atomic commit that removes the substrate.
- The single stopgap (`STOPGAP_VENDORED_PROTOCOL_STACK`, [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111)) is unaffected by Phase C. Removal targets the future `Stem.Communication` NuGet; the #111 migration plan inherits the [research.md](./research.md) R7 hot-plug acceptance check.
