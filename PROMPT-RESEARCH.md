# Resume Phase B — spec-002 queue (research.md onwards)

You're continuing Phase B of the spec-002 (CAN link lifecycle) refresh on `button-panel-tester`. The data-model.md rewrite landed at `0be6872`; `/speckit-analyze` was re-run and confirmed the data-model drift is resolved. Next queue item: **`research.md`**.

```text
Phase A (PR #152, merged)        ─▶  spec-002 split into lifecycle + spec-003
Phase B work 2026-05-27          ─▶  spec.md amended (f04318e), spec-quality checklist (b99282a)
Phase B continuation 2026-05-27  ─▶  checklist tally fix (f4ddf89), data-model.md rewrite (0be6872),
                                     /speckit-analyze re-run (delta report in chat history)
YOU                              ─▶  research.md → plan.md → tasks.md → quickstart.md →
                                     migration-map.md → cleanup → PR
```

## Branch

**`docs/002-lifecycle-spec-refresh`**, tip `0be6872`. Sync:

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

I jumped step 2 once during the data-model.md commit; Luca corrected and confirmed this gate going forward. Do not re-jump.

## Read order (cold start)

1. **`specs/002-can-link-lifecycle/spec.md`** at `f04318e` — final amended spec. 5-family FSM, 17 FRs, 12 SCs, 2 user stories, 6 clarification sessions.
2. **`specs/002-can-link-lifecycle/data-model.md`** at `0be6872` — Phase B Phase 1 output. Five-family DU, Mermaid covering every spec.md edge, seven invariants, AdapterCandidate + AdapterIdentification, Lean Phase 2 cross-reference table (5 theorems).
3. **`specs/002-can-link-lifecycle/checklists/spec-quality.md`** at `f4ddf89` — 18/30 resolved, 4 deferred, 8 open. CHK019 + CHK012 are resolvable as of `0be6872`.
4. **`OPEN-FINDINGS.md`** at the repo root — running list of analyze findings the queue might not catch. Re-check at every `/speckit-analyze` cycle.
5. **`PROMPT-DATA-MODEL.md`** — original queue plan. Items 2-8 are still the load-bearing queue order.
6. **`HANDOFF.md`** §6.3 + §8 — substrate FSM brainstorm. §6.3 needs the mental patches noted in `PROMPT-DATA-MODEL.md` (IdleCause = UserPaused only; Faulted carries candidate option).

## Where to resume

### Queue item 2 — `specs/002-can-link-lifecycle/research.md`

Decisions log. Each entry references the spec.md clarification session and/or the interview point it came from. From `PROMPT-DATA-MODEL.md` §2, the eleven decisions to record:

- Idle inclusion (HANDOFF §4b — operator-paused only after the 2026-05-27 decoupling).
- Drop Recoverable / Fatal severity (HANDOFF §4c).
- Multi-adapter iteration as bench-resilience (HANDOFF §4a + 2026-05-27 confirmation).
- Hot-plug as explicit edge (2026-05-27 §Edges and iteration).
- Searching retry cadence (deferred to plan.md per HANDOFF §6.8).
- v0.1.0 mental baseline (HANDOFF §4e).
- Candidate-in-Faulted (2026-05-27 §FSM shape).
- Truth-and-acknowledge framing mirroring DictionaryStatusRow (2026-05-27 §Framing).
- Adapter exclusivity stance / exclusive driver access (2026-05-27 §Scope refinements).
- Boot order decoupled from dictionary (2026-05-27 §Scope refinements). **Address U1 from OPEN-FINDINGS.md here**: link back to spec-001's seed-fallback as the upstream guarantee that makes the decoupling safe.
- NotificationCenter scrub (deferred to future NC spec).

No skill matches `research.md` directly; honour the spec-kit decision-log conventions. Doc-only — skip the local build / test gate (memory `feedback_skip_local_gate_for_docs.md`).

### Queue items 3-8 — see `PROMPT-DATA-MODEL.md` §3-§8

plan.md is the load-bearing next gate after research.md — it resolves the largest cluster of open analyze findings (A1, C1, C2, F1, F2, U2, T1). Read `OPEN-FINDINGS.md` before writing plan.md to fold those in.

**Pending decision before plan.md commits**: whether to insert a `contracts/can-link-port.md` refresh between plan.md and tasks.md, or defer the contract drift to Phase C via `migration-map.md`. See `OPEN-FINDINGS.md` item 1.

## Rules to honour (from global CLAUDE.md)

- **bisect-safe** + **vertical-commits** — one artefact per commit.
- **communication** — terse, low-profile.
- **dual-remote** — push to github only; Bitbucket mirrors via Actions on merge to main.
- **no-attribution** — no AI footers anywhere.
- **skill-discipline** — invoke `/speckit-analyze`, `/speckit-plan`, `/speckit-tasks` via the Skill tool when relevant. Don't paraphrase from memory.
- **doc-only skip** — local build / test gate is skipped for this branch.

## Skills to load proactively

- `workflow` — at the start of the coding session.
- `speckit-analyze` — between each queue artefact write and commit (per Luca's gate above).
- `speckit-plan` / `speckit-tasks` — when reaching those queue items.

## When in doubt

Ask Luca. Don't guess on FR carving, FSM shape, plan budgets, or PR sequencing — those are load-bearing.
