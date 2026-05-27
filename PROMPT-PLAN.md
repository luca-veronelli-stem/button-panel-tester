# Resume Phase B — spec-002 queue (plan.md onwards)

You're continuing Phase B of the spec-002 (CAN link lifecycle) refresh on `button-panel-tester`. research.md landed at `55f0fc9` and `/speckit-analyze` confirmed no critical findings (it also closed OPEN-FINDINGS U1 + U2). Next queue item: **`plan.md`** — the largest single artefact of the queue and the load-bearing resolver of most remaining open findings.

```text
Phase A (PR #152, merged)        ─▶  spec-002 split into lifecycle + spec-003
Phase B work 2026-05-27          ─▶  spec.md amended (f04318e), spec-quality checklist (b99282a),
                                     checklist tally fix (f4ddf89), data-model.md rewrite (0be6872),
                                     research.md rewrite (55f0fc9)
YOU                              ─▶  plan.md → tasks.md → quickstart.md → migration-map.md →
                                     cleanup → PR
```

## Branch

**`docs/002-lifecycle-spec-refresh`**, tip `55f0fc9`. Sync:

```powershell
Set-Location C:\Users\LucaV\Source\Repos\button-panel-tester
git fetch github
git checkout docs/002-lifecycle-spec-refresh
git pull github docs/002-lifecycle-spec-refresh
```

## Workflow this session (Luca's gate — load-bearing)

For each remaining queue artefact:

1. Write the artefact.
2. Run `/speckit-analyze` against the new state.
3. Hand the analysis to Luca; **wait for explicit approval**.
4. Commit + push only after approval.

Don't jump step 2.

## Read order (cold start)

1. **`specs/002-can-link-lifecycle/spec.md`** at `f04318e` — final amended spec. 17 FRs, 12 SCs, five-family FSM, 6 clarification sessions. Load-bearing input.
2. **`specs/002-can-link-lifecycle/data-model.md`** at `0be6872` — F# DU, Mermaid covering every spec edge, 7 invariants, 5 Lean theorems cross-referenced.
3. **`specs/002-can-link-lifecycle/research.md`** at `55f0fc9` — 12 Phase B decisions (R1-R12) + 5 carried-forward (R13-R17) + retired (§3). R12 explicitly defers Searching retry cadence to plan.md; R14 leaves the `contracts/can-link-port.md` refresh decision pending plan.md; R16 pins Lean lifecycle modules at 5 theorems / 2 files (with the "one theorem per file" rule explicitly relaxed for `CanLinkState.lean`).
4. **`specs/002-can-link-lifecycle/checklists/spec-quality.md`** at `f4ddf89` — three items deferred to plan.md: CHK018, CHK024, CHK028. **Resolve them in plan.md.**
5. **`OPEN-FINDINGS.md`** at the repo root — running list. Items 6 + 7 (U1, U2) closed by research.md. Items 1, 2, 3, 4, 5, 8, 9 are plan.md's domain — **resolve as many as plan.md can carry** (see "Where to resume" below).
6. **`PROMPT-DATA-MODEL.md`** §3 — original plan.md queue plan. The four CHK resolutions, the Searching cadence pin, and the Lean re-prove plan are the load-bearing items.

## Where to resume

### Queue item 3 — `specs/002-can-link-lifecycle/plan.md`

This artefact resolves the largest cluster of open findings. Per OPEN-FINDINGS and `PROMPT-DATA-MODEL.md §3`, plan.md MUST pin:

**Plan.md MUSTs (from OPEN-FINDINGS + checklists):**

- **OPEN-FINDINGS item 1 — `contracts/can-link-port.md` refresh decision.** Choose: refresh the contract against the five-family `CanLinkState` payload between plan.md and tasks.md (insert a new queue item), OR defer the contract drift to Phase C via `migration-map.md`. **Decide before plan.md commits.** Recommendation: refresh now (it's a small file aligned with research.md R14's Phase B note), but the call is Luca's.
- **OPEN-FINDINGS item 2 (F1) — filename pins.** data-model.md §2.1 / §3.1 forward-reference plan.md §Project Structure for `PcanAdapterEnumeration.fs` (new — enumeration helper producing `AdapterCandidate`) and `PcanAdapterIdentity.fs` (existing — post-Open self-description helper). Pin both under `src/ButtonPanelTester.Infrastructure/Can/`.
- **OPEN-FINDINGS item 3 (F2) — property-suite names.** data-model.md §1.3 Invariant #5 + §4 footer forward-reference plan.md §Constitution Check Principle II. Enumerate at least:
  - sticky-since preservation across passive re-observation (FR-004 / Invariant #5);
  - `LinkStateChanged` totality / exhaustiveness over family (FR-014);
  - reachability replacement for the retired `transition_reachability_closed`.
- **OPEN-FINDINGS item 5 (A1) — FR-006 cancellation budget.** spec.md FR-006 says "the FSM SHOULD land in `Idle(UserPaused)` within the cancellation budget pinned in plan.md". Pin a concrete number — recommend **≤ 250 ms on a normal-load workstation**; verify against PEAK driver typical cancel latency before committing the budget.
- **research.md R12 — Searching retry cadence.** Recommend **5 s periodic poll + vendored-stack device-arrived event as the fast path** (HANDOFF §6.8's default). Justify the choice in a §Searching retry policy sub-section.
- **CHK018** — name SC-005 as bench-only verification. Add a CI-compatible surrogate if practical (e.g., assert adapter handle release via a fake exclusive-mode client). If not practical, document the limitation explicitly.
- **CHK024** — pin spec-002-specific log message templates per STEM `LOGGING.md`. State name, discriminator, since-timestamp as named parameters; BeginScope conventions for correlation IDs.
- **CHK028** — name the existing hot-plug acceptance test (`tests/ButtonPanelTester.Tests.Windows/Gui/Can/HotPlugRecoveryTests.fs`? grep first) or flag the gap; ensure #111's migration plan inherits it. Cross-reference research.md R7.

**Plan.md SHOULDs (style + thoroughness):**

- **OPEN-FINDINGS item 8 (O1) — "active state" definition (CHK010).** spec.md FR-006 uses "active state" = `Searching ∪ Opening ∪ Open ∪ Faulted`. One-line definition in plan.md §Glossary (or footnote) closes CHK010 without needing a spec.md amendment.
- **OPEN-FINDINGS item 9 (A2) — FR-009 sub-perceptual cue.** Single-sentence restate: "no minimum-visibility floor; cue duration matches in-flight call duration; consistent with FR-009 Note."
- **Lean Phase 2 re-prove plan**. data-model.md §4 retires `transition_reachability_closed` and adds 4 new theorems (`state_classification_total` over 5 families, `fault_cause_total`, `idle_cause_total`, `faulted_reconnect_target_total`) plus carries `observe_emits_no_transmit` unchanged. plan.md §Constitution Check Principle I MUST list the new theorems (matches the post-Phase-B count of 5).
- **Standards drift**: substrate plan.md references substrate states (`Connected | Disconnected | Error` with Recoverable/Fatal). The plan.md rewrite touches:
  - §Summary (full rewrite — five families, no severity).
  - §Technical Context (Performance Goals refs SC-001 / SC-005 / SC-008 — those SC numbers shifted in spec.md, verify against current spec.md).
  - §Constitution Check Principle I (Lean module + theorem list — see above).
  - §Constitution Check Principle II (FsCheck property names — see F2 above).
  - §Project Structure (filename pins — see F1 above).
- **Doc-only commits — local build/test gate skipped per project policy** (memory `feedback_skip_local_gate_for_docs.md`). Push directly and let GH CI catch anything.

**Skill loading:** load `speckit-plan` before writing. Also load `stem-logging` if you want the LOGGING.md conventions cached for CHK024.

### Queue items 4-7 — tasks.md / quickstart.md / migration-map.md / cleanup / PR

Per `PROMPT-DATA-MODEL.md §4-§8`. tasks.md rewrite must include the Lean re-prove task (OPEN-FINDINGS F3 — re-author `Phase2/CanLinkState.lean` with the four new theorems).

## Rules to honour (from global CLAUDE.md)

- **bisect-safe** + **vertical-commits** — one artefact per commit.
- **communication** — terse, low-profile.
- **dual-remote** — push to github only; Bitbucket mirrors via Actions on merge to main.
- **no-attribution** — no AI footers anywhere.
- **skill-discipline** — invoke `/speckit-analyze`, `/speckit-plan`, `/speckit-tasks` via the Skill tool when relevant. Don't paraphrase from memory.
- **doc-only skip** — local build / test gate is skipped for this branch.

## Skills to load proactively

- `workflow` — at the start of the coding session.
- `speckit-plan` — for plan.md.
- `stem-logging` — for CHK024's LOGGING.md template conventions.
- `speckit-analyze` — between each queue artefact write and commit.
- `speckit-tasks` — when reaching tasks.md.

## When in doubt

Ask Luca. Don't guess on cancellation budget numbers, cadence values, the contract-refresh decision, or PR sequencing — those are load-bearing. Filename pins, Lean theorem listings, formatting choices — pick sensible defaults from data-model.md / research.md and flag in the PR body if anything material drifts from those.
