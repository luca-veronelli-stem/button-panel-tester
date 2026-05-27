# Resume Phase B — spec-002 CAN link lifecycle FSM redesign (home → work)

You're picking up a mid-flight design conversation between me (Luca) and a previous Claude session running on my home machine. The home session itself was a continuation of an earlier work-machine session that paused on 2026-05-26 evening. The arc:

```text
work session 2026-05-26 evening  ─▶ commute home ─▶ home session 2026-05-27 ─▶ commute back ─▶ YOU (work session, 2026-05-27+)
```

Everything below is what you need to pick up cold.

## What this is

Phase B of a three-phase track on `button-panel-tester` to refine spec-002 (the CAN link lifecycle). Phase A merged via PR #152 (mechanical split of a combined spec into lifecycle + panel-discovery). Phase B is the substantive rewrite of seven Phase-1 artefacts under `specs/002-can-link-lifecycle/`. Phase C is the impl-↔-spec drift report that follows.

The previous work-machine session got through `/speckit-clarify` and synthesised a five-state FSM. The home-machine session wrote **spec.md only** against that FSM (Luca chose "spec.md first, then await review"). The other **six artefacts** are still queued. That's your job.

## Branch you're on

Already cut, two commits in, fully pushed: **`docs/002-lifecycle-spec-refresh`**. Tip is `968a88a`. Work machine should:

```powershell
Set-Location C:\Users\LucaV\Source\Repos\button-panel-tester
git fetch github
git checkout docs/002-lifecycle-spec-refresh
git pull github docs/002-lifecycle-spec-refresh
```

The branch holds three throwaway scratch files at the repo root (`HANDOFF.md`, `PROMPT.md`, `HANDOFF-2026-05-27.md`, and this `PROMPT-WORK.md` once committed). All four get `git rm`d in the final cleanup commit before the PR opens.

## Read order

1. **`HANDOFF-2026-05-27.md`** — home-machine session notes. What landed in `8327e05` (spec.md), the candidate-in-Faulted DU choice it implements, **the five judgment calls home-Claude made without an explicit clarification** (§4 — Luca should sanity-check these before the next commits cross-reference spec.md by FR number), the queue of six artefacts in commit order. **This is your first stop.**
2. **`HANDOFF.md`** — original 2026-05-26 work-session notes. Still load-bearing for the §6 FSM design (the rewrite implements it verbatim) and the §8 artefact plan. **§6.3 needs mental patching**: the DU was sketched without a candidate in `Faulted`; the home session's clarification added `candidate: AdapterCandidate option` to the `Faulted` payload. The current truth is in spec.md's 2026-05-27 clarifications + §4 of `HANDOFF-2026-05-27.md`.
3. **`specs/002-can-link-lifecycle/spec.md`** at `8327e05` — the load-bearing artefact. 15 FRs (FR-001 .. FR-015), 9 SCs, 2 user stories, 6 clarification sessions. The other six artefacts cross-reference this — read it before writing them so FR numbers don't drift.

## Where to resume

Open by asking me whether any of the five judgment calls in `HANDOFF-2026-05-27.md` §4 need amending in-place on `8327e05` before the next six commits fan out. After my answer (amend / leave / mixed), proceed to the queue. The queue is in `HANDOFF-2026-05-27.md` §5 — same shape as the original `HANDOFF.md` §8 but pinned to real FR numbers now that spec.md exists.

If I greenlight without amendments, write the six artefacts in commit order (one vertical commit each, doc-only):

1. `specs/002-can-link-lifecycle/data-model.md` — F# DU (HANDOFF.md §6.3 + candidate-in-Faulted), Mermaid state diagram of §6.2 edges, Lean Phase 2 cross-references.
2. `specs/002-can-link-lifecycle/research.md` — decisions log.
3. `specs/002-can-link-lifecycle/plan.md` — Constitution Check, Lean Phase 2 re-prove plan, Searching retry cadence pinned.
4. `specs/002-can-link-lifecycle/tasks.md` — renumbered T001 onwards, lifecycle-only.
5. `specs/002-can-link-lifecycle/quickstart.md` — refreshed bench walkthrough.
6. `specs/002-can-link-lifecycle/migration-map.md` — old→new state + FR table.

Then the final commit:

7. **Cleanup**: `git rm HANDOFF.md PROMPT.md HANDOFF-2026-05-27.md PROMPT-WORK.md` + commit `chore: drop Phase B session handoff artefacts` + push + open PR on github.

## Rules to honour

Already in your global CLAUDE.md, but a reminder of the load-bearing ones:

- **bisect-safe** + **vertical-commits** — each artefact in its own commit.
- **communication** — terse, low-profile PR body, no AI attribution.
- **dual-remote** — push to `github` only; Bitbucket mirrors via Actions on merge to main.
- **no-attribution** — no "Generated with Claude" footers anywhere.
- **skill-discipline** — invoke `/speckit-plan`, `/speckit-tasks`, `dotnet`, `lean4` via the Skill tool when relevant.
- **promote-to-llm-settings** — if anything cross-repo durable surfaces, propose promoting to `llm-settings` rather than burying in memory.
- **doc-only skip** — local build / test gate is skipped for this branch (memory `feedback_skip_local_gate_for_docs.md`); GH CI catches anything that matters.

## Skills to load proactively

`workflow`, `worktrees` (if you cut a sub-branch for any reason — unlikely), `speckit` + `speckit-plan` + `speckit-tasks`, `dotnet` (for F# DU blocks in data-model.md), `lean4` (for Phase 2 references in research / plan / data-model).

## PR shape when the rewrite lands

One PR on github: `docs/002-lifecycle-spec-refresh` → `main`. Label `docs`. Title: `docs(spec-002): rewrite lifecycle spec via /speckit (Phase B)`. Body links to `migration-map.md`, names the FSM redesign as the structural change, notes Phase C impl drift is expected and will follow as separate implementation-PR(s) (F# DU reshape + Lean Phase 2 re-prove are not in this PR's scope).

## After the rewrite lands

Update memories — both live under `~/.claude/projects/C--Users-LucaV-Source-Repos-button-panel-tester/memory/`:

- `spec-002-live-roadmap.md` — bump `Last updated`, add Phase B done entry, note the FSM reshape + the Lean re-prove cost (now four theorems including `Faulted` candidate-option case analysis).
- `spec-003-live-roadmap.md` — note that `LinkStateChanged` payload shape changed (five families instead of four) and spec-003 FR-015' needs re-derive.

Then move to Phase C (impl ↔ refined-spec delta) — bigger than originally scoped because the F# DU + Lean Phase 2 theorems get reshaped. Plan to file follow-up issues for genuine drift; the substantial impl refactor is its own implementation-PR track that follows.

## When in doubt

Ask me. Don't guess on FR carving, FSM shape, or PR sequencing — those are load-bearing. Implementation detail (Mermaid styling, Lean lemma names, file ordering inside tasks.md) you can pick sensible defaults and flag for review in the PR body.
