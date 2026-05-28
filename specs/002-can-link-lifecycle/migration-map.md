# Migration Map: substrate → Phase B (CAN Link Lifecycle)

**Phase 1 output for**: [plan.md](./plan.md) (Phase B queue item 9).

**Status**: Phase B (2026-05-28). Load-bearing for Phase C: the reconcile PR(s) cite this map row-by-row when reshaping `main`'s substrate code (four-family FSM + `Recoverable / Fatal` severity) to the Phase B five-family FSM. Sourced from substrate code on `main` (`src/ButtonPanelTester.Core/Can/CanLinkState.fs`, `src/ButtonPanelTester.Services/Can/CanLinkService.fs`, `lean/Stem/ButtonPanelTester/Phase2/CanLinkState.lean`), [data-model.md](./data-model.md) §1.1 / §2.1 / §3.1, [research.md](./research.md) §1 + §3, and [tasks.md](./tasks.md) Phase C section.

**Scope**: lifecycle types only. Panel-discovery migration (if any) is spec-003's concern; not covered here.

---

## 1. DU constructor migration — `CanLinkState`

Substrate top-level case → Phase B family + discriminator. The `Adapter` / `Since` payload bookkeeping carries forward by the same name; the sub-discriminator carving is what changes.

| Substrate constructor | Phase B target | Notes | FR cross-references |
|---|---|---|---|
| `CanLinkState.Initializing` | `Searching(Polling, _)` at app launch ([research.md](./research.md) R10 — boot decoupled from dictionary) | The substrate's pre-Open dwell collapses into the same `Polling` discriminator the rest of `Searching` uses. `Idle(AwaitingBoot)` was sketched in HANDOFF §6.3 and dropped — no producer in Phase B. | FR-001, FR-015 |
| `CanLinkState.Connected(adapter, openedAt)` | `Open(adapter, openedAt)` | Same record, renamed family. `AdapterIdentification` shape unchanged ([data-model.md](./data-model.md) §3.1). | FR-001, FR-002, FR-004, FR-005, FR-010 |
| `CanLinkState.Disconnected(reason, since)` | Multiple Phase B targets — split by `DisconnectReason` (see §2.1 below) | The substrate's catch-all `Disconnected` family covered four distinct in-flight situations; Phase B carves them into `Searching(_, _)` outcomes and `Idle(UserPaused, _)` per the substrate reason. | FR-002, FR-006, FR-007, FR-012, FR-015 |
| `CanLinkState.Error(classification, since)` | `Faulted(cause, candidate option, _)` | Severity classifier retired ([research.md](./research.md) §3); the substrate's free-text `detail` collapses into a named `FaultCause` constructor — mapping in §2.2 below. `candidate: AdapterCandidate option` is new payload, populated by `CanLinkService` from the last-attempted candidate (Phase C `T206`). | FR-002, FR-008, FR-014 |

**Family-count net change**: 4 → 5 (gains `Idle` for operator-paused; the substrate's `Initializing` collapses into `Searching(Polling)`). **Sub-discriminator location**: case-payload (Phase B) instead of sibling DU branches (substrate). **Chip-colour projection** (FR-002): family-driven, one-line case match instead of severity-conditional.

---

## 2. Sub-DU migration

### 2.1 `DisconnectReason` → Phase B family + discriminator

The substrate's four-case `DisconnectReason` carved one family into four; Phase B redistributes them across the new families based on what the FSM is actually doing in each.

| Substrate constructor | Phase B target | Rationale |
|---|---|---|
| `DisconnectReason.NoAdapterPresent` | `Searching(NoAdapterEnumerated, _)` | Boot-time absence: the host enumeration returns zero PEAK adapters ([data-model.md](./data-model.md) §1.1, Edge Cases §1). Was an outcome of `Disconnected`; in Phase B is an outcome of `Searching`. |
| `DisconnectReason.LinkNotYetOpened` | `Searching(Polling, _)` | Pre-Open dwell. In substrate this followed `Initializing` before any Open attempt; in Phase B the FSM begins in `Searching(Polling)` at app launch ([research.md](./research.md) R10) and re-enters `Polling` between observation outcomes, so the dedicated "not yet opened" reason has no Phase B analogue distinct from the rest of `Polling`. |
| `DisconnectReason.MidSessionUnplug` | `Searching(Polling, _)` via the `Open → Searching` device-lost edge ([data-model.md](./data-model.md) §1.2) | Mid-session unplug becomes an observation-driven family change rather than a discriminator on a disconnected state. Hot-plug recovery is the explicit edge in [research.md](./research.md) R7. |
| `DisconnectReason.ReconnectPending` | No direct Phase B target — retired | Substrate-only: synthesised by `CloseAsync` so the next `OpenAsync` was observable as a fresh attempt. In Phase B, Stop lands `Idle(UserPaused, _)` and Reconnect transitions directly from `Faulted` to `Opening` or `Searching(Polling)` per FR-008 — no intermediate "pending" state is needed. The substrate's `ReconnectPending` emission becomes a service-internal implementation detail in Phase C `T206`. |

**Producer note**: the substrate's `Disconnected(NoAdapterPresent, _)` was sometimes also synthesised when `Searching` enumerated adapters but all returned busy. Phase B distinguishes that case explicitly as `Searching(NoCandidateAvailable count, _)` — new discriminator, no substrate analogue, supplied by `PcanAdapterEnumeration` ([data-model.md](./data-model.md) §2.1).

### 2.2 `ErrorClassification` → `FaultCause` + candidate

The substrate's `Recoverable | Fatal` severity classifier is **retired** ([research.md](./research.md) §3). The `detail` string is no longer carried free-form — it collapses into one of four named `FaultCause` constructors, with the substrate's `detail` text becoming the source for the `FaultCause` constructor name chosen by `PeakErrorText`.

| Substrate constructor | Phase B target | Rationale |
|---|---|---|
| `ErrorClassification.Recoverable(detail)` (first observation) | `Faulted(<FaultCause from detail>, Some lastCandidate, _)` | First-observation severity flag retired. The cause itself drives the operator's "should I Reconnect?" decision via its self-descriptive name. |
| `ErrorClassification.Fatal(detail)` (second observation after Reconnect) | `Faulted(<same FaultCause from detail>, Some lastCandidate, _)` with refreshed `since` | Second-observation upgrade retired. The repeated arrival into the same `Faulted` family updates `since` per FR-004 invariant rule (c) — "user-initiated round-trip back into the same family via an intervening state updates `since` on the second arrival" ([data-model.md](./data-model.md) §1.1 sticky-since semantics). The operator reads the same cause twice and infers persistence; no hidden counter required. |

**`FaultCause` carving** (from `PeakErrorText.fs` cause strings + Edge Cases §3–§5, §8, §11):

| Substrate `detail` shape (representative) | Phase B `FaultCause` constructor | Phase B headline (FR-003 detail with imperative) |
|---|---|---|
| `"Bus-off detected — try reconnect"` (or equivalent PEAK bus-off status) | `BusOff` | `Faulted · bus-off — try Reconnect` (Edge Cases §3, SC-008) |
| Unrecognised PEAK status code with raw hex (e.g. `"PEAK status 0x4200 — file bug"`) | `UnexpectedAdapterStatus of code: uint32` | `Faulted · PEAK status 0x<HEX> — file bug` (Edge Cases §4) |
| `"PCANBasic native DLL not found — install the PEAK driver"` | `DriverNotInstalled` | `Faulted · PEAK PCANBasic native DLL not found — install the PEAK driver` (Edge Cases §5, §11) |
| `"Adapter unresponsive after driver responded — check cabling / power-cycle"` | `AdapterHardwareFailure` | `Faulted · adapter hardware fault — check cabling / power-cycle` (Edge Cases §8) |

**New payload in Phase B**: `Faulted` carries `candidate: AdapterCandidate option` ([data-model.md](./data-model.md) §1.1, [research.md](./research.md) R6). Substrate `Error` did not carry the candidate — Reconnect-target memory lived in `CanLinkService`'s escalation tracker. Phase C `T206` retires the tracker and lifts last-candidate memory into the state itself. `candidate = None` only for `Faulted(DriverNotInstalled, None, _)` (no enumeration happened before the fault); every other `Faulted` is `Some c` against the last enumerated candidate.

### 2.3 `AdapterCandidate` — new in Phase B

**No substrate analogue.** Substrate `PcanCanLink` interacted with PEAK adapters via a single hard-coded channel; multi-adapter iteration (FR-012) is new in Phase B. `AdapterCandidate` ([data-model.md](./data-model.md) §2.1) is produced by `PcanAdapterEnumeration` (Phase C `T203`) and threaded through `Opening` and `Faulted` payloads.

### 2.4 `AdapterIdentification` — carries forward unchanged

Substrate `AdapterIdentification { ChannelName; DeviceId; BaudrateBps }` is unchanged in Phase B ([data-model.md](./data-model.md) §3.1). The record stays in `Core/Can/CanLinkState.fs` and is referenced from the new `Open(adapter, openedAt)` payload exactly as the substrate `Connected(adapter, openedAt)` referenced it.

---

## 3. Per-consumer touch list

One row per file the Phase C reconcile touches, with the Phase C task ID(s) that reshape each. Cross-reference [tasks.md](./tasks.md) §Phase C for the canonical task descriptions and sequencing.

| Consumer | Phase C task(s) | Action |
|---|---|---|
| `src/ButtonPanelTester.Core/Can/CanLinkState.fs` | `T201` (additive — add `CanLinkStateV2.fs` with `IdleCause`, `SearchAttempt`, `FaultCause`, `AdapterCandidate`, `CanLinkStateV2` DU), `T212` (atomic remove substrate types; rename `CanLinkStateV2.fs` → `CanLinkState.fs`) | The substrate file's `AdapterIdentification` stays (unchanged); the substrate `DisconnectReason`, `ErrorClassification`, and `CanLinkState` DU are removed in `T212`. |
| `lean/Stem/ButtonPanelTester/Phase2/CanLinkState.lean` | `T202` (Phase C.B — re-author) | Retire `transition_reachability_closed` and the substrate four-case inductive (substrate file lines 80–85, classifier lines 97–119, theorem lines 137–153). Add the new five-family inductive + four theorems: `state_classification_total` (over the new five families), `fault_cause_total` ([data-model.md](./data-model.md) §1.3 Invariant #2), `idle_cause_total` (Invariant #3), `faulted_reconnect_target_total` (Invariant #4 — FR-008 Reconnect bifurcation mechanised). "One theorem per file" relaxed per [plan.md](./plan.md) §Constitution Check I + [research.md](./research.md) R16 Phase B note. |
| `lean/Stem/ButtonPanelTester/Phase2/PassiveObserver.lean` | — (no touch) | `observe_emits_no_transmit` carries forward unchanged ([research.md](./research.md) R16) — passive observation is family-agnostic. |
| `src/ButtonPanelTester.Infrastructure/Can/PcanAdapterEnumeration.fs` | `T203` (Phase C.C — NEW file) | New helper that enumerates PEAK adapters via the vendored stack and produces an `AdapterCandidate list` for `PcanCanLinkV2` to iterate (FR-012). No substrate counterpart — multi-adapter iteration is a Phase B addition. |
| `src/ButtonPanelTester.Core/Can/Ports.fs` | `T204` (additive `ICanLinkV2` alongside substrate `ICanLink`), `T212` (rename V2 → canonical) | Port signature (`OpenAsync` / `CloseAsync` / `ReconnectAsync` / `LinkStateChanged` / `CurrentState`) unchanged; only the payload type swaps to the five-family `CanLinkState`. |
| `src/ButtonPanelTester.Infrastructure/Can/PcanCanLink.fs` | `T204` (additive `PcanCanLinkV2.fs`), `T212` (atomic remove + rename) | `PcanCanLinkV2` drives FR-012 iteration over `PcanAdapterEnumeration.enumerate()`, requests exclusive driver-level access on Open (FR-010), emits one of the five families on every transition with sticky-`since` (FR-004), bridges the vendored stack's PnP arrival event into a `Searching(Polling, now)` re-entry ([research.md](./research.md) R7), and propagates `CancellationToken` through the in-flight vendored-driver call (FR-006, ≤ 250 ms budget per [plan.md](./plan.md) §FR-006). |
| `src/ButtonPanelTester.Infrastructure/Can/PcanAdapterIdentity.fs` | — (no touch) | Existing post-Open self-description helper carries forward; produces `AdapterIdentification` for the new `Open(adapter, openedAt)` payload exactly as it produced it for substrate `Connected`. |
| `src/ButtonPanelTester.Infrastructure/Can/PeakErrorText.fs` | `T204` reshape (callers in `PcanCanLinkV2.fs` map PEAK status → `FaultCause`); referenced from `T206`'s logging templates | Mapping from PEAK status code → `FaultCause` constructor replaces the substrate's mapping to a free-text `ErrorClassification.detail` string. The four `FaultCause` constructors above subsume the substrate's status-code coverage; the raw code is preserved in `UnexpectedAdapterStatus code` for diagnostic forensics. |
| `tests/ButtonPanelTester.Tests/Fakes/Can/InMemoryCanLink.fs` | `T205` (additive `InMemoryCanLinkV2.fs`), `T212` (atomic remove + rename) | Scripted V2 sequences (`seq<CanLinkStateV2 * TimeSpan>`); honours `CancellationToken` so FsCheck Stop-during-`Opening` scenarios exercise FR-006 cancellation propagation independent of the PEAK driver. |
| `src/ButtonPanelTester.Services/Can/ICanLinkService.fs` + `CanLinkService.fs` | `T206` (additive `ICanLinkServiceV2.fs` + `CanLinkServiceV2.fs`), `T212` (atomic remove + rename) | Retires the substrate's `Recoverable → Fatal` escalation tracker ([research.md](./research.md) §3). Adds 5-second `PeriodicTimer` polling for `Searching` ([plan.md](./plan.md) §Searching retry policy). FR-014 fan-out, FR-006 cancellation budget, CHK024 logging templates with `BeginScope` correlation keys (`OperatorAction` / `CorrelationId` / `CandidateChannelHandle`). `ILogger<CanLinkServiceV2>` required (archetype A, non-optional). |
| `tests/ButtonPanelTester.Tests/Property/Can/CanLinkStateTransitionsProperties.fs` | `T207` (re-author for five-family shape) | Substrate's `transition_reachability_closed` Lean theorem is retired; this FsCheck property suite replaces it ([plan.md](./plan.md) §Constitution Check II). From any reachable `CanLinkStateV2`, any valid input event lands in another reachable state. Commits RED against `T201`'s stub transition function; turns GREEN after `T206`. |
| `tests/ButtonPanelTester.Tests/Property/Can/CanLinkStickyTimestampProperties.fs` | `T208` (NEW) | FR-004 / Invariant #5: passive re-observation preserves `since`; family or discriminator change updates `since`; user-initiated round-trip via intervening state updates `since` on second arrival. No substrate analogue — the sticky-since rule was operationally enforced but not property-tested in the substrate. |
| `tests/ButtonPanelTester.Tests/Property/Can/LinkStateChangedFamilyExhaustiveProperties.fs` | `T209` (NEW) | FR-014 + FR-002 chip-colour totality: every family appears in some emission; every emission projects to one of `{ green, grey, red }`. No substrate analogue. |
| `src/ButtonPanelTester.GUI/Can/CanStatusRow.fs` | `T210` (re-author for five-family projection) | Chip colour from `CanLinkStateV2` family (green for `Open`, red for `Faulted`, grey for `Idle` / `Searching` / `Opening` — FR-002); headline `<family> · <discriminator detail>` with em-dash convention (FR-003); detail affordance for adapter identification + baud rate + multi-line cause + `since` (FR-005); Start / Stop / Reconnect button visibility per the operator-initiatable affordance map (FR-006 / FR-007 / FR-008); FR-009 click-acknowledge cue (`IsEnabled = false` + `⟳` glyph from `DictionaryStatusRow.fs:151-158`); FR-017 keyboard navigation + screen-reader labels. |
| `tests/ButtonPanelTester.Tests.Windows/Gui/Can/CanStatusRowTests.fs` | `T210` (folded test edits) | Re-author for the five-family shape; load-bearing assertion is SC-010 (Reconnect click → button `IsEnabled = false` AND `Content = "⟳"` from click time through next FSM emission, via `Avalonia.Headless`). |
| `src/ButtonPanelTester.GUI/Composition/CompositionRoot.fs` | `T211` (re-wire + drop dictionary-boot gate) | Register `ICanLinkV2 = PcanCanLinkV2`, `ICanLinkServiceV2 = CanLinkServiceV2`; drop the dictionary-boot gate so the CAN sub-program starts in parallel with the dictionary sub-program ([research.md](./research.md) R10 + R17 Phase B note). Substrate registrations removed in `T212`. |
| `tests/ButtonPanelTester.Tests/Integration/BootOrderTests.fs` | `T211` (re-author as decoupling regression) | Substrate test shipped via [#133](https://github.com/luca-veronelli-stem/button-panel-tester/pull/133) gated CAN start on dictionary-fetch boot completion. Phase B re-authors it to assert the **opposite**: dictionary and CAN sub-programs start in parallel, and the CAN row reaches `Searching(Polling, _)` independent of dictionary-fetch boot completion ([research.md](./research.md) R10, FR-015). |
| `tests/ButtonPanelTester.Tests/Integration/Can/DictionaryIndependenceTests.fs` | `T211` (NEW) | Phase B refresh of the substrate's FR-015 coverage. FsCheck-driven where practical; asserts `IDictionaryService.SourceChanged` emits zero events while `CanLinkServiceV2` walks through `Searching` / `Faulted(cause, _, _)` / `Idle(UserPaused)` paths driven by `InMemoryCanLinkV2`. Covers SC-006 + FR-015. |
| `tests/ButtonPanelTester.Tests/Integration/Can/CanLinkServiceLifecycleTests.fs` | `T206` (folded sub-bullets — additive assertions) | New assertions ride alongside substrate ones until `T212` removes the substrate: `SearchingNoAdapterEnumeratedAppearsWithinOneSecondOfLaunch` (SC-002), `StopDuringOpeningCancelsWithinBudget` (FR-006, ≤ 250 ms), `StopReleasesAdapterHandle` (CHK018 CI surrogate), `ContentionEventEmitsExactlyOneInformationLogEntry` (SC-012). |
| `tests/ButtonPanelTester.Tests/Integration/Can/RecoverableToFatalEscalationTests.fs` | `T212` (DELETE) | The escalation logic this test exercises is retired by Phase B ([research.md](./research.md) §3). No Phase B replacement — the FR-004 sticky-since rule already covers the operator-visible behaviour (repeated arrival into the same `Faulted` family refreshes `since`); the substrate's hidden counter has no analogue. |
| `tests/ButtonPanelTester.Tests.Windows/Integration/Can/Hardware/PcanLifecycleTests.fs` | `T212` (reshape for five-family shape), `T213` (add `HotPlugRecoveryAfterUnplug`) | `T212` reshapes SC-001 / SC-003 / SC-004 / SC-008 / SC-009 / SC-011 assertions against the renamed canonical types (still `Category=Hardware`, excluded from default CI, but MUST compile in the atomic removal commit). `T213` adds the new hot-plug acceptance test that closes [#132](https://github.com/luca-veronelli-stem/button-panel-tester/issues/132): asserts `Open → Searching(Polling, _) → Opening(candidate, _) → Open` driven by unplug + re-seat without operator input, within the SC-004 ≤ 5-second budget. |

---

## 4. FR cross-reference index

Quick lookup: for each FR-NNN, the Phase C task(s) that mechanise the requirement. Use this for traceability when drafting Phase C PR bodies.

| FR | Mechanised by Phase C task(s) | Notes |
|---|---|---|
| FR-001 (five top-level states) | `T201`, `T202`, `T206`, `T212` | Type definition (`T201`), Lean proof (`T202`), service consumer (`T206`), substrate removal (`T212`). |
| FR-002 (chip-colour projection) | `T209`, `T210` | Property-tested exhaustively (`T209`) + GUI implementation (`T210`). |
| FR-003 (`<family> · <discriminator>` headline) | `T210` | GUI rendering. |
| FR-004 (sticky-since) | `T206`, `T208` | Service preserves `since` across passive re-observation (`T206`); FsCheck property mechanises the rule (`T208`). |
| FR-005 (detail affordance content) | `T210` | GUI rendering of `AdapterIdentification`, `BaudrateBps`, full multi-line cause, `since`. |
| FR-006 (Stop affordance + cancellation) | `T204`, `T206`, `T210` | Cancellation propagation through `PcanCanLinkV2` (`T204`); ≤ 250 ms budget asserted in `T206`'s sub-bullets; Stop button visibility in `T210`. |
| FR-007 (Start affordance) | `T206`, `T210` | Synchronous service transition (`T206`); Start button visibility on `Idle(UserPaused, _)` (`T210`). |
| FR-008 (Reconnect affordance) | `T202`, `T206`, `T210` | Lean mechanises the Reconnect bifurcation (`faulted_reconnect_target_total`, `T202`); service implements it (`T206`); button caption reflects `Faulted(_, None, _)` case (`T210`). |
| FR-009 (click-acknowledge cue) | `T210` | GUI `IsEnabled = false` + `⟳` glyph; SC-010 assertion folded into `T210`'s test edits. |
| FR-010 (exclusive driver access) | `T204`, `T212` | Adapter requests exclusivity on Open (`T204`); SC-011 hardware verification reshaped in `T212`. |
| FR-011 (external contention observability) | `T206` | Conditional MUST — service logs Information-level entry per surfaced contention event (CHK024 template); SC-012 assertion folded into `T206`'s sub-bullets. |
| FR-012 (multi-adapter iteration) | `T203`, `T204`, `T206` | Enumeration helper (`T203`); adapter iterates internally (`T204`); service exposes the resulting `LinkStateChanged` stream (`T206`). |
| FR-013 (no transmit) | `T202`, `T219` | Lean `observe_emits_no_transmit` unchanged; re-confirmed at Phase C.G closeout. SC-007 verified externally per `T221`. |
| FR-014 (`LinkStateChanged` fan-out) | `T206`, `T209` | Service fan-out (`T206`); property suite mechanises family-exhaustive coverage (`T209`). |
| FR-015 (dictionary independence) | `T211` | Composition root drops the boot gate; `BootOrderTests` re-authored as decoupling regression; new `DictionaryIndependenceTests` covers SC-006 + FR-015. |
| FR-016 (GUI responsiveness in non-Open states) | `T210`, `T217` | Render-time concern (`T210`); async-discipline audit confirms no blocking calls on the lifecycle path (`T217`). |
| FR-017 (accessibility) | `T210` | Keyboard navigation + screen-reader labels in `CanStatusRow.fs`. |

---

## 5. Retired-substrate-feature index

Phase B explicitly retires the following substrate constructs. Each line lists what is retired and where the retirement is documented in Phase B's design archive.

- **Recoverable → Fatal severity escalation.** Substrate `CanLinkService.fs:40–55` carried a per-cause tracker that flipped `ErrorClassification.Recoverable(detail)` to `ErrorClassification.Fatal(detail)` on second observation across a `ReconnectAsync` call; `RecoverableToFatalEscalationTests.fs` exercised the rule. Retired by [research.md](./research.md) §3 + §1 R3. The repeated-arrival behaviour the operator sees is now FR-004's sticky-since refresh on the second arrival into the same `Faulted` family.
- **`ErrorClassification` DU.** Substrate `CanLinkState.fs:53–55`. Replaced by `FaultCause` ([data-model.md](./data-model.md) §1.1) + the candidate-or-not split carried in the `Faulted` payload.
- **`CanLinkState.Initializing`.** Substrate `CanLinkState.fs:68`. Replaced by `Searching(Polling, _)` at app launch ([research.md](./research.md) R10) — the dictionary-boot gate is dropped, so there is no longer a distinct "still warming up before the first Open attempt" phase.
- **`DisconnectReason.ReconnectPending`.** Substrate `CanLinkState.fs:39`. No Phase B target — Reconnect transitions directly from `Faulted` to `Opening` or `Searching(Polling)` per FR-008; no intermediate emission is needed.
- **`DisconnectReason.LinkNotYetOpened`.** Substrate `CanLinkState.fs:37`. Merges into `Searching(Polling, _)` — the pre-Open dwell is the same shape as every other `Polling` re-entry in Phase B.
- **`transition_reachability_closed` (Lean).** Name as used by [tasks.md](./tasks.md) T202 and [data-model.md](./data-model.md) §4 for the substrate-era reachability theorem. The substrate file `lean/Stem/ButtonPanelTester/Phase2/CanLinkState.lean:137–153` actually ships a `state_classification_total` lemma over the four-family inductive; Phase B retires that four-family classification and substitutes the five-family `state_classification_total` plus three new lemmas (`fault_cause_total`, `idle_cause_total`, `faulted_reconnect_target_total` — [plan.md](./plan.md) §Constitution Check I). Reachability coverage moves out of Lean and into the FsCheck `CanLinkStateTransitionsProperties` suite (`T207`).
- **Dictionary-boot gate in `CompositionRoot.fs`.** Substrate composition-root policy that delayed `CanLinkService` start until `IDictionaryService` finished its first fetch. Retired by [research.md](./research.md) R10 + R17 Phase B note. The two sub-programs run in parallel from app launch in Phase B; spec-001's seed-fallback guarantee (research.md R10) makes the dictionary fully usable from the moment its sub-program starts, so no cross-row interlock is needed.

---

## 6. Sequencing notes

Phase C ships under the **additive-then-remove** pattern from `bisect-safe.md` item 2, structured as sub-phases C.A through C.G in [tasks.md](./tasks.md):

- **C.A (`T201`)** introduces Phase B types under a `V2` suffix alongside the substrate. The commit compiles green because nothing yet imports the new module.
- **C.B (`T202`)** reshapes the Lean Phase 2 module; independent of F# compilation.
- **C.C (`T203` → `T204` → `T205`)** adds the new adapter, port, and fake in additive form. Substrate `ICanLink` and `PcanCanLink` continue to compile and serve the GUI.
- **C.D (`T207` / `T208` / `T209` → `T206`)** lands the three new FsCheck property suites **RED first** (TDD per global `tdd` rule), then `CanLinkServiceV2` fills in the impl that turns them GREEN.
- **C.E (`T210` → `T211`)** rewires the GUI and composition root to the V2 pipeline. Substrate pipeline becomes orphaned but still compiles.
- **C.F (`T212`)** is the atomic substrate-removal commit: every substrate type, fake, and escalation test is removed and the `V2` suffix is dropped from every renamed file. No stubs survive past this commit (per `bisect-safe.md` item 5). `T213` follows with the new `HotPlugRecoveryAfterUnplug` hardware test.
- **C.G (`T214`–`T224`)** is the audit + release-gate sweep (logging audit, CHANGELOG, XML docs, async discipline, Principle V grep, `lake build` re-confirm, VENDOR.sha256 check, SC-007 external capture, plus three orthogonal carry-overs from substrate T-amend-9 / -11 / -12).

Every Phase C commit MUST be independently green for `dotnet build -c Release` and `dotnet test --filter "Category!=Hardware"`. `lake build` is checked at `T202` and re-confirmed at `T219`. See [plan.md](./plan.md) §Phase B queue + §Status §Blockers for the doc-PR-versus-impl-PR boundary, and [tasks.md](./tasks.md) §Dependencies & execution order for the canonical sequencing.

---

## 7. Provenance

- **Substrate constructor names**: read directly from `main` at the SHA `0f720ad` (this branch's tip — `main` has not advanced since Phase A merged). Files: `src/ButtonPanelTester.Core/Can/CanLinkState.fs`, `src/ButtonPanelTester.Core/Can/Ports.fs`, `src/ButtonPanelTester.Services/Can/CanLinkService.fs:40–55`, `lean/Stem/ButtonPanelTester/Phase2/CanLinkState.lean`.
- **Phase B target shapes**: [data-model.md](./data-model.md) §1.1 (`CanLinkState`, `IdleCause`, `SearchAttempt`, `FaultCause`), §2.1 (`AdapterCandidate`), §3.1 (`AdapterIdentification`), §1.3 (invariants), §4 (Lean cross-reference).
- **Retirement decisions**: [research.md](./research.md) §1 R2 (five-family redesign), §1 R3 (severity classifier retirement), §1 R10 (boot decoupling), §3 (retired pre-Phase-B R8 escalation rule).
- **Phase C task IDs**: [tasks.md](./tasks.md) §Phase C (`T201`–`T224`).
