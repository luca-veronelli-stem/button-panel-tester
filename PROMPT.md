# Resume Phase B — spec-002 CAN link lifecycle FSM redesign

You're picking up a mid-flight design conversation between me (Luca) and another Claude session running on my work machine. We paused because I had to commute home. Everything below is what you need to pick up cold.

## What this is

Phase B of a three-phase track on `button-panel-tester` to refine spec-002 (the CAN link lifecycle). Phase A (mechanical split of a combined spec, merged via PR #152) is done. Phase B is the substantive rewrite of all seven Phase-1 artefacts under `specs/002-can-link-lifecycle/`. Phase C is the impl-↔-spec drift report that follows.

The session before yours got through the `/speckit-clarify` step and into a state-machine redesign brainstorm with me. We converged on a final FSM. The work that's queued is to write all seven doc artefacts against that design, doc-only commits, one PR on github.

## Branch you're on

Already cut and ready: **`docs/002-lifecycle-spec-refresh`** (from `5198a34` on `main`). The home machine should:

```powershell
cd C:\Users\LucaV\Source\Repos\button-panel-tester   # or wherever the repo lives on this machine
git fetch github
git checkout docs/002-lifecycle-spec-refresh
git pull github docs/002-lifecycle-spec-refresh
```

If the repo isn't cloned on this machine yet:

```powershell
git clone git@github.com:luca-veronelli-stem/button-panel-tester.git
cd button-panel-tester
git checkout docs/002-lifecycle-spec-refresh
```

The dual-remote setup, Bitbucket mirror, etc. should auto-configure from the standards rollout — no action needed for Phase B since this is a feature branch (only `main` mirrors).

## Read order

1. **`HANDOFF.md`** at the repo root (committed alongside this file). Full session notes, all design context, every Q&A from the work-machine session, the synthesised FSM in §6. This is the load-bearing read.
2. **`~/.claude/projects/C--Users-LucaV-Source-Repos-button-panel-tester/memory/spec-002-live-roadmap.md`** — overall Phase A/B/C plan and queue state.
3. **`~/.claude/projects/C--Users-LucaV-Source-Repos-button-panel-tester/memory/spec-003-live-roadmap.md`** — sister roadmap (panel discovery is downstream of the redesigned `LinkStateChanged` payload, will need a re-derive).
4. **`specs/002-can-link-lifecycle/{spec,plan,tasks,data-model,research,quickstart}.md`** on this branch — the substrate Phase A pruned. **Factual input, NOT the destination.** The new artefacts are written fresh.

## Where I left off

The work-machine session asked a final greenlight question:

> Final FSM design — ready to write spec.md / plan.md / data-model.md / tasks.md / research.md / quickstart.md / migration-map.md against this shape? Five top-level states (Idle | Searching | Opening | Open | Faulted), three sub-discriminator payloads (IdleCause, SearchAttempt, FaultCause), no Recoverable/Fatal, three user affordances (Stop, Start, Reconnect). Greenfield write from v0.1.0 mental baseline.

I didn't pick an option — I needed to leave. Resume by asking me whatever last clarification feels load-bearing on the FSM, then write.

## What to write (when I greenlight)

In this order, each a vertical commit on `docs/002-lifecycle-spec-refresh`:

1. `specs/002-can-link-lifecycle/spec.md` — clean greenfield write. Two user stories (visibility + mid-session resilience). FRs tight-renumbered FR-001 onwards. Preserve the five bench-validated clarification sessions (Q&A 2026-05-24 / -25 / -26) as session bullets — those are bench truth, not relitigated.
2. `specs/002-can-link-lifecycle/data-model.md` — new F# DU per HANDOFF.md §6.3. Mermaid state diagram. Lean Phase 2 cross-refs.
3. `specs/002-can-link-lifecycle/research.md` — decisions log: Idle inclusion, drop Recoverable/Fatal, multi-adapter iteration, hot-plug as explicit edge, Searching retry cadence, NotificationCenter sequencing, v0.1.0 baseline framing.
4. `specs/002-can-link-lifecycle/plan.md` — Constitution Check re-run, Lean Phase 2 re-prove plan.
5. `specs/002-can-link-lifecycle/tasks.md` — renumbered T001 onwards, lifecycle-only.
6. `specs/002-can-link-lifecycle/quickstart.md` — refreshed bench walkthrough.
7. `specs/002-can-link-lifecycle/migration-map.md` — old→new state names + FR numbers. Load-bearing for Phase C.

All commits doc-only. Skip the local build/test gate (doc changes can't break build; memory `feedback_skip_local_gate_for_docs.md`). After all seven land, open one PR on github, label `docs`, title `docs(spec-002): rewrite lifecycle spec via /speckit (Phase B)`. Body links to migration-map.md.

## Rules to honour

Already in your global CLAUDE.md, but a reminder of the load-bearing ones:

- **bisect-safe** + **vertical-commits** — each artefact in its own commit.
- **communication** — terse, low-profile PR body, no AI attribution.
- **dual-remote** — push to `github` only; Bitbucket mirrors via Actions on merge to main.
- **no-attribution** — no "Generated with Claude" footers anywhere.
- **skill-discipline** — invoke `/speckit-plan`, `/speckit-tasks`, `dotnet`, `lean4` via the Skill tool when relevant.
- **promote-to-llm-settings** — if anything cross-repo durable surfaces, propose promoting to `llm-settings` rather than burying in memory.

## Skills to load proactively

`workflow`, `worktrees` (if you cut a sub-branch for any reason), `speckit` + `speckit-plan` + `speckit-tasks`, `dotnet` (for F# DU blocks in data-model.md), `lean4` (for Phase 2 references in research/plan/data-model).

## Throwaway artefacts to delete before the eventual PR

Before opening the spec-002 rewrite PR:

```powershell
git rm HANDOFF.md PROMPT.md
git commit -m "chore: drop Phase B session handoff artefacts"
```

Both files are session-handoff scratch, not PR content.

## After the rewrite lands

- Update `spec-002-live-roadmap.md` memory: bump `Last updated`, add Phase B done entry, note the FSM reshape + Lean re-prove cost.
- Update `spec-003-live-roadmap.md` memory: note that `LinkStateChanged` payload shape changed and spec-003 FR-015' needs re-derive.
- Move to Phase C (impl ↔ refined-spec delta) — bigger than originally scoped because the FSM is substantively redesigned. Plan to file follow-up issues for genuine drift; the substantial impl refactor (F# DU + Lean Phase 2 theorems) is its own implementation-PR track that follows.

## When in doubt

Ask me. Don't guess on FSM shape, FR carving, or PR sequencing — those are load-bearing. Implementation detail (button labels, search retry cadence specifics, file names) you can pick sensible defaults and flag for review in the PR body.
