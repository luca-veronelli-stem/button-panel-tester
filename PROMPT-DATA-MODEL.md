# Resume Phase B — spec-002 queue fan-out (data-model.md onwards)

You're resuming Phase B of the spec-002 (CAN link lifecycle) refresh on `button-panel-tester`. The previous session interviewed Luca on operator usage + bench reality, applied two rounds of amendments to `spec.md`, ran `/speckit-checklist`, and stopped to let Luca read the amended spec + checklist before fanning out the rest of the queue.

```text
Phase A (PR #152, merged)      ─▶  spec-002 split into lifecycle + spec-003 (panel discovery)
Phase B home session 2026-05-27 ─▶  spec.md first pass (commit 8327e05, superseded)
Phase B work session 2026-05-27 ─▶  interview + two amendments + /speckit-checklist (commits f04318e + b99282a)
YOU (Phase B continuation)     ─▶  data-model.md → research.md → plan.md → tasks.md → quickstart.md → migration-map.md → cleanup → PR
```

## Branch you're on

**`docs/002-lifecycle-spec-refresh`**. Tip is `b99282a` (the spec-quality checklist). Sync with:

```powershell
Set-Location C:\Users\LucaV\Source\Repos\button-panel-tester
git fetch github
git checkout docs/002-lifecycle-spec-refresh
git pull github docs/002-lifecycle-spec-refresh
```

## Read order (cold start)

1. **`specs/002-can-link-lifecycle/spec.md`** at `f04318e` — the final amended spec. This is the load-bearing artefact. 17 FRs (FR-001 .. FR-017), 12 SCs (SC-001 .. SC-012), 2 user stories, 6 clarification sessions (2026-05-24 / -25 / -26 / -27 grouped by sub-topic). Includes the new "Operator-initiatable transitions" table that data-model.md / tasks.md cross-reference.
2. **`specs/002-can-link-lifecycle/checklists/spec-quality.md`** — quality gate. 19 / 30 items resolved by `f04318e`. **5 items are deferred to the queue artefacts**: CHK018 (SC-005 CI-compatibility — plan.md), CHK019 (detail affordance render during Opening — data-model.md), CHK024 (logging template specifics — plan.md), CHK028 (hot-plug event traceable to test — plan.md). Honour them as you write each artefact.
3. **`HANDOFF.md`** — original 2026-05-26 work-session notes. **§6.3** is still load-bearing for the F# DU shape (with the candidate-in-Faulted addition from 2026-05-27). **§8** has the artefact-writing plan in commit order. Note: the F# DU sketch in §6.3 needs mental patching — `IdleCause` collapses to `| UserPaused` only (the `AwaitingBoot` case was dropped during the 2026-05-27 interview because spec-001 + CAN are fully decoupled at the domain level).
4. **`HANDOFF-2026-05-27.md`** — home-machine session notes. **§5** has the queue plan with target FR numbers. Cross-check against the post-amendment FR numbers in spec.md (the renumbering may not match §5's expectations if it was written before today's amendments).
5. **`PROMPT-WORK.md`** — the prompt that opened today's work session. Less load-bearing now; read only if you need extra context.

## Where to resume

The queue is six doc-only vertical commits + a cleanup commit + a PR. Each artefact is its own commit. Doc-only — skip the local build / test gate (memory `feedback_skip_local_gate_for_docs.md`).

### 1. `specs/002-can-link-lifecycle/data-model.md`

F# DU per spec.md's Key Entities + §Operator-initiatable transitions + Clarifications 2026-05-27 §FSM shape:

```fsharp
type IdleCause =
    | UserPaused                              // operator-paused only; AwaitingBoot dropped

type SearchAttempt =
    | NoAdapterEnumerated                     // host returns zero PEAK adapters
    | NoCandidateAvailable of count: int      // ≥1 enumerated, all returned busy
    | Polling                                 // scan / event-wait in flight

type FaultCause =
    | BusOff
    | UnexpectedAdapterStatus of code: uint32
    | DriverNotInstalled
    | AdapterHardwareFailure

type CanLinkState =
    | Idle      of cause: IdleCause      * since: DateTimeOffset
    | Searching of attempt: SearchAttempt * since: DateTimeOffset
    | Opening   of candidate: AdapterCandidate * since: DateTimeOffset
    | Open      of adapter: AdapterIdentification * openedAt: DateTimeOffset
    | Faulted   of cause: FaultCause     * candidate: AdapterCandidate option * since: DateTimeOffset
```

Sections to include:

- F# DU (above) with XML doc bodies. Bare `///` style per memory `feedback_fsharp_bare_xml_docs.md`.
- Mermaid state diagram of every FSM edge from spec.md's Edge Cases + Operator-initiatable transitions table. Include the cancellation edge for Stop-during-Opening (FR-006).
- Invariants — at least: `state_classification_total` (5 families exhaustive), `fault_cause_total`, sticky-since (FR-004), exclusivity-on-Open (FR-010), passive observer (FR-013 → no transmit).
- Lean Phase 2 cross-references: theorems that re-prove the substrate's Phase 2 over the new FSM. Three theorems likely needed:
  - `state_classification_total` over five families (was three in substrate).
  - `fault_cause_total` over `FaultCause` (new).
  - `passive_observer_emits_no_transmit` carried forward unchanged.
  - `idle_cause_total` is trivially one case — keep or drop per Luca's preference; recommend keep as a degenerate proof for completeness.
  - Faulted-candidate-option case analysis (the `Some` vs `None` Reconnect bifurcation) — likely a small auxiliary lemma.
- Resolve CHK019 in passing: "the detail affordance renders the current `CanLinkState` value; there is no snapshot freeze during Opening — render follows every emission."

Load the `dotnet` and `lean4` skills before writing.

### 2. `specs/002-can-link-lifecycle/research.md`

Decisions log. Each entry references the spec.md clarifications and/or the interview session it came from:

- Idle inclusion (HANDOFF §4b — operator-paused only after the 2026-05-27 decoupling).
- Drop Recoverable / Fatal severity (HANDOFF §4c).
- Multi-adapter iteration as bench-resilience (HANDOFF §4a + 2026-05-27 confirmation).
- Hot-plug as explicit edge (2026-05-27 §Edges and iteration).
- Searching retry cadence (deferred to plan.md per HANDOFF §6.8).
- v0.1.0 mental baseline (HANDOFF §4e).
- Candidate-in-Faulted (2026-05-27 §FSM shape).
- Truth-and-acknowledge framing mirroring DictionaryStatusRow (2026-05-27 §Framing — interview).
- Adapter exclusivity stance, exclusive driver access (2026-05-27 §Scope refinements — interview).
- Boot order decoupled from dictionary (2026-05-27 §Scope refinements — interview).
- NotificationCenter scrub (interview; deferred to future NC spec, not anticipated here).

### 3. `specs/002-can-link-lifecycle/plan.md`

Honour `/speckit-plan` skill conventions. Sections:

- Constitution Check re-run.
- Project Structure unchanged (archetype A, two-TFM split per memory `project_button_panel_tester_tests_split.md`).
- Lean Phase 2 re-prove plan (theorem count + the new theorems above).
- **Searching retry cadence pinned** — recommend 5 s periodic poll with vendored-stack device-arrived event as the fast path. Pick freely.
- Resolve CHK018 in passing: name SC-005 as bench-only verification; add a CI-compatible surrogate if practical (e.g., a fake exclusive-mode client). If not practical, document the limitation.
- Resolve CHK024 in passing: pin spec-002-specific log message templates (state name, discriminator, since-timestamp as named parameters per `LOGGING.md`); BeginScope conventions for correlation.
- Resolve CHK028 in passing: name the existing hot-plug acceptance test (or flag the gap) and ensure #111's migration plan inherits it.

Load the `speckit-plan` skill before writing.

### 4. `specs/002-can-link-lifecycle/tasks.md`

Renumbered T001 onwards. Lifecycle-only — the substrate's shared Phase 2 foundation tasks (T012..T033) are not retained; those are already merged on main. Forward-looking only. Honour `/speckit-tasks` conventions.

Load the `speckit-tasks` skill before writing.

### 5. `specs/002-can-link-lifecycle/quickstart.md`

Refreshed bench walkthrough. Cover: cold start to Open, Stop/Start path, Reconnect from Faulted(BusOff), driver-missing path, hot-plug recovery, multi-adapter iteration (or "skip if you only have one adapter" note for the supplier bench).

### 6. `specs/002-can-link-lifecycle/migration-map.md`

Old → new state + FR table. Load-bearing for Phase C (impl ↔ refined-spec delta). Format per HANDOFF.md §8. Fill in real post-amendment FR numbers from spec.md `f04318e`.

### 7. Cleanup commit (final)

```powershell
git rm HANDOFF.md PROMPT.md HANDOFF-2026-05-27.md PROMPT-WORK.md PROMPT-DATA-MODEL.md
git commit -m "chore: drop Phase B session handoff artefacts"
git push github docs/002-lifecycle-spec-refresh
```

### 8. PR

Open one PR on github. Title: `docs(spec-002): rewrite lifecycle spec via /speckit (Phase B)`. Label `docs`. Body: link to `migration-map.md`, name the FSM redesign as the structural change, note Phase C impl drift is expected and will follow as separate implementation-PR(s) (F# DU reshape + Lean Phase 2 re-prove are not in this PR's scope).

## Rules to honour (load-bearing, from global CLAUDE.md)

- **bisect-safe** + **vertical-commits** — one artefact per commit.
- **communication** — terse, low-profile.
- **dual-remote** — push to github only; Bitbucket mirrors via Actions on merge to main.
- **no-attribution** — no AI footers anywhere.
- **skill-discipline** — invoke `/speckit-plan`, `/speckit-tasks`, `dotnet`, `lean4` via the Skill tool when relevant. Don't paraphrase from memory.
- **promote-to-llm-settings** — if anything cross-repo durable surfaces, propose promoting to `llm-settings`.
- **doc-only skip** — local build / test gate is skipped for this branch.

## Skills to load proactively

- `workflow` — at the start of the coding session.
- `speckit` + `speckit-plan` + `speckit-tasks` — when reaching those queue items.
- `dotnet` — for the F# DU block in data-model.md.
- `lean4` — for Phase 2 references in data-model.md / research.md / plan.md.

## After the PR opens

Update memories (post-merge, scoped to task #9):

- `spec-002-live-roadmap.md` — bump `Last updated`, add Phase B done entry, note the FSM reshape + Lean re-prove cost (3 theorems plus the Faulted-candidate-option case analysis lemma).
- `spec-003-live-roadmap.md` — note `LinkStateChanged` payload shape changed (5 families) and spec-003 FR-015' needs re-derive.

Both memories live under `~/.claude/projects/C--Users-LucaV-Source-Repos-button-panel-tester/memory/`.

Then Phase C (impl ↔ refined-spec delta report) — bigger than originally scoped because the F# DU + Lean Phase 2 theorems get reshaped. File follow-up issues for genuine drift; the substantial impl refactor is its own implementation-PR track that follows.

## When in doubt

Ask Luca. Don't guess on FR carving, FSM shape, or PR sequencing — those are load-bearing. Mermaid styling, Lean lemma names, Markdown formatting choices — pick sensible defaults and flag in the PR body.
