# Session handoff — Phase B spec-002 lifecycle FSM redesign

**Session**: 2026-05-26 evening (Luca's work machine).
**Branch**: `docs/002-lifecycle-spec-refresh` (cut from `5198a34` on main).
**Status when paused**: Speckit-clarify in flight; final FSM design synthesised; about to start writing seven doc artefacts; Luca paused to commute home.

**This file + `PROMPT.md` are throwaway artefacts**, committed to this throwaway branch only so the home-machine Claude session can pick up cold. Delete both before the eventual real PR.

---

## 1 — Overall plan (the resume prompt that triggered this session)

Phase A landed PR #152 (merged `5198a34`, 2026-05-26): mechanical split of former `specs/002-can-link-and-panel-discovery/` into `specs/002-can-link-lifecycle/` (this spec) + new `specs/003-panel-discovery/`. Luca approved Phase A.

Phase B = deep re-run of speckit on the lifecycle as a top-level concept. Rewrite all seven Phase-1 artefacts from scratch (`spec.md` + `plan.md` + `tasks.md` + `data-model.md` + `research.md` + `quickstart.md` + new `migration-map.md`). Preserve five bench-validated clarification sessions (Q&A 2026-05-24 / -25 / -26 — bench truth). Generate old→new mapping table so Phase C (impl ↔ refined-spec delta) stays tractable.

Phase C = identify spec ↔ impl drift after Phase B lands. Produce delta table (aligned / drift / spec-too-tight / impl-too-permissive / accepted-difference). File follow-up issues. Originally scoped as "small reconciliation"; after Phase B redesign (see §6), it grows substantially because the F# DU + Lean Phase 2 theorems get reshaped.

**Luca's hard rules (active throughout):**
- Tight-renumber FRs from FR-001, document old→new in `migration-map.md`.
- One doc-only PR with vertical commits per artefact. Doc-only rule: skip local build gate, GH CI catches anything that matters.
- Five bench-validated clarification sessions are preserved verbatim as session bullets in the new `spec.md` — they are bench truth, not relitigated.
- Honour: bisect-safe, vertical-commits, communication (terse + low-profile), dual-remote, no-attribution, skill-discipline.

---

## 2 — Substrate state on main (factual input for the rewrite)

Repo: `C:\Users\LucaV\Source\Repos\button-panel-tester`. Archetype A, standards v1.9.0.

**Already shipped** (substrate spec-002 lifecycle, per `spec-002-live-roadmap.md`):
- PR-A [#120] + PR-B [#121] + PR-C [#122] (foundation + MVP shipped to main).
- Phase 3.5 amendments: PRs #133, #134, #135, #138, #141, #145, #147 (FR-001 boot extract; cold-start hang fix; real PEAK status text; sticky `since`; Reconnect click-feedback contract; Reconnect visibility; severity in headline).
- PR #152 — Phase A split (lifecycle vs discovery).

**Open lifecycle queue** (open when Phase B started):
- **#136** — reclassify cold-start poll-exhaust as `Disconnected(NoAdapterPresent, detail)`, widens DisconnectReason arity. Was the NEXT lifecycle work; Phase B redesign supersedes it (see §6).
- **#142** — env-gated `[<HardwareFact>]` xUnit attribute.

**Parked** (Phase B redesign may obsolete some): #139, #140, #143, #148.

**Cross-spec**: spec-003 (panel discovery) is the downstream consumer of `LinkStateChanged`. Spec-002's redesign changes the observable's payload shape — spec-003 FR-015' consumer contract will need a re-derive against the new state names. Tracked in `spec-003-live-roadmap.md`.

---

## 3 — Phase B started: clarify questions asked + answered

I loaded the `speckit-clarify` skill and started the structured clarify queue (max 5 questions, one at a time, recorded into `spec.md` clarifications section as each lands).

### Q1 — Initializing as a 4th DU case

> spec.md FR-002 says "exactly three top-level chip values" but the DU has four cases (Initializing + Connected + Disconnected + Error). How should the refined spec resolve this?

**Answer (recommended): Fold `Initializing` into `Disconnected`.** Three chip families. `Initializing` becomes `Disconnected(NotYetReady, since)`. Aligns DU with FR-002. Lean theorem `state_classification_total` partitions into three cases.

### Q2 — DisconnectReason shape

> With Initializing folded into Disconnected, the Disconnected family also covers transient transitions in flight (`NotYetReady` = boot incomplete; `ReconnectPending` = OpenAsync in flight). Reconnect button is hidden in BOTH. What's the cleanest DisconnectReason shape?

**Answer (recommended): Four reasons — `NotYetReady | NoAdapterPresent | LinkLost | Reconnecting`.** Drop substrate's `LinkNotYetOpened` (dead). Rename `MidSessionUnplug → LinkLost`. `Reconnecting` is the in-flight Reconnect state regardless of source.

⚠ **Superseded by §6.** Luca's pushback on Q3 surfaced state-machine smells that meant Q2's answer was the wrong abstraction.

### Q3 — AdapterBusy placement (and triggered the redesign)

> The "adapter present but in use by another process" case (`PCAN status 0x40010`) doesn't fit the four reasons from Q2. How should the refined spec carve it (and the related cold-start poll-exhaust case from in-flight #136)?

Luca didn't pick an option; he wrote:

> "Uhm, I think that the connection should be better modelled, like maybe two super states, idle (not attempting to connect) and active (actively scans for connections and tries to connect when it finds one, surface something if there are more than one and notifies the chosen one/ask which one to the user), then the fact that another app is using the resource become not important at all if the service is idle, but gains importance when the service is actively trying to connect and the only present adapter is occupied, just suggesting, we are brainstorming here, not yet formalizing and specifying."

This rotated the conversation from "carve the substrate cleaner" to "redesign the FSM from first principles".

---

## 4 — Luca's design brainstorm (verbatim points + my read of them)

After I sketched the Idle/Active superstate model with cost notes, Luca refined:

### 4a — Multi-adapter (point b)

> "For b there is no actual need right now, I only cited that because I want the app to not stop at (edge case) 'one adapter found' but it's busy → giving up without searching for other adapters, the app works with only one adapter at the time but I want it to be resilient if there are two adapters, one occupied and the other not, if it defaults to the first without looking for anything else it might say adapter busy when there is an alternative present."

**Decoded**: no UI for adapter selection ("first wins" stays the rule). But the iteration logic when N adapters are enumerated MUST try each before declaring "all busy". Spec carves a `SearchAttempt.NoCandidateAvailable of count` discriminator that fires only when every enumerated adapter returned 0x40010. Single-adapter bench is the N=1 special case.

### 4b — Idle (point a)

> "About the idle state, it's not strictly necessary, but it enforces a better semantics I think, we can still develop the whole tool as an 'Always trying to stay up' tool, but the behind allows for better versatility."

…then after I pushed back and proposed deferring Idle to a future spec:

> "The only thing that does not sound to me is the absence of the Idle state, because to me it means there is no action the user can do to completely stop the service and restart it when he decides to do so (also if it is just a deterrent to make the user have a turn it off turn it on way for cases of frustration hahah) but if you say that is not needed I trust you, it's just that on professional tools I usually can see a clear disconnect option."

**Decoded**: Idle is in. Two bench-product use cases justify it beyond "frustration relief":
- "Release the adapter so I can use PCAN-View" — symmetric with PCAN-View's own ability to release the resource.
- "Professional tools have a clear Disconnect option" — bench-product convention.

`Idle` gets two sub-causes:
- `IdleCause.AwaitingBoot` — pre-FR-001 (dictionary boot incomplete). Auto-transitions to `Searching` on boot-complete event.
- `IdleCause.UserPaused` — user clicked Stop. Stays put until user clicks Start.

### 4c — Recoverable/Fatal severity (pushback)

> "There is one thing I'd push against a bit, the escalation stuff, it does not make sense that an error becomes Fatal if the reconnect button does not work, the Recoverable Fatal severity is a bit off to me, can we explore error severity a bit better and map it as it should be or just omit it for now?"

**Decoded**: drop the binary Recoverable/Fatal classification. Replace with named fault causes, each carrying its own description. Severity isn't a meaningful FSM dimension when the user can read the cause and infer what to do. The substrate's "Recoverable→Fatal escalation rule" (FR-002a) becomes "user reads the same cause twice and concludes the obvious" — no FSM-level severity flip.

### 4d — NotificationCenter sequencing

> "Also, I'm thinking now that maybe a good spec we could land before this one is the notification center one, since it will take care of some parts that we are having difficulties to map right now (if the status row only surfaces the current status (and intent (Idle ↔ Active)?) of the can link, who should carry the suggestions?)"

**Decoded**: Luca sees that splitting "current state" (row's job) from "suggested action" (notification's job) cleans up several ambiguities. But landing notification-center first is hard because:
- Notification-center's trigger is `LinkStateChanged → severity ≥ warning` — it needs a specified lifecycle as input.
- The shipped lifecycle impl already has the row carrying suggestions; tearing that out preemptively before notification-center is built creates a UX regression window.

**Decision**: lifecycle spec-002 v2 ships with row carrying both `<cause> — <suggestion>` as the transitional convention. A `§Forward design` callout names notification-center (spec-004 or similar, future) as where suggestions migrate. Lifecycle isn't held hostage to notification-center; both can ship in their natural order.

### 4e — v0.1.0 baseline

> "Maybe to have a better focus you can start from tag v0.1.0 instead of the current main?"

**Decoded**: not literally rewind the git history. The mental baseline. `v0.1.0` (`a6d4c0b`, 2026-05-24) tagged before spec-002 existed — feat-001 dictionary only. "Start from v0.1.0" = "specify spec-002 as if greenfield, no substrate-spec gravity, no accumulated Phase 3.5 patchwork pulling design choices in old directions". We're still committing on top of `5198a34` (main); the v0.1.0 framing is purely the writer's posture.

---

## 5 — My response sketch (after Luca confirmed Idle is in)

I drafted the synthesised FSM (see §6) and asked one final greenlight question via `AskUserQuestion`:

> Final FSM design — ready to write spec.md / plan.md / data-model.md / tasks.md / research.md / quickstart.md / migration-map.md against this shape?

Options offered: (a) Go, write all seven artefacts; (b) Go, write spec.md only first; (c) Adjust the FSM first — your feedback to follow.

**Luca didn't pick** — he wanted to clarify, then said:

> "Very nice, still not finished because I cannot review it now since I have to go home, please save all these info plus the context plus all you can to a throwaway branch in button-panel-tester for my home machine, and then add also a file called PROMPT.md which is the prompt I can copy paste to the claude session on the home machine."

So this `HANDOFF.md` + `PROMPT.md` are the handoff artefacts.

---

## 6 — Synthesised FSM design (this is the load-bearing artefact)

### 6.1 — Five top-level states

```text
Idle      ← starts here (IdleCause = AwaitingBoot until dictionary boot completes)
Searching ← Active, looking for an adapter (scan loop)
Opening   ← Active, candidate found, Open call in flight
Open      ← Active, link is up
Faulted   ← Active, non-routine fault from adapter / driver
```

### 6.2 — Transitions

```text
Idle (AwaitingBoot)  ── dictionary boot completes (auto)  ─▶ Searching
Idle (UserPaused)    ── user clicks Start                 ─▶ Searching
Idle (anything)      ── stays put if no trigger fires     ─▶ Idle

Searching ── candidate enumerated AND not all busy        ─▶ Opening (per candidate)
Searching ── periodic retry tick OR vendored-stack event  ─▶ Searching (detail refreshed)
Searching ── user clicks Stop                             ─▶ Idle (UserPaused)

Opening   ── OpenAsync ok                                 ─▶ Open
Opening   ── OpenAsync busy AND more candidates           ─▶ Opening (next candidate)
Opening   ── OpenAsync busy AND no more candidates        ─▶ Searching (NoCandidateAvailable)
Opening   ── OpenAsync failed with non-routine cause      ─▶ Faulted
Opening   ── user clicks Stop                             ─▶ Idle (UserPaused)

Open      ── vendored stack signals device lost           ─▶ Searching (Polling)
Open      ── adapter raises non-routine fault             ─▶ Faulted
Open      ── user clicks Stop                             ─▶ Idle (UserPaused)

Faulted   ── user clicks Reconnect                        ─▶ Opening (same candidate)
Faulted   ── user clicks Stop                             ─▶ Idle (UserPaused)
```

Hot-plug recovery becomes the explicit `Searching (Polling) ── vendored-stack device-arrived event ─▶ Opening` edge. No more "undocumented vendored-stack freebie". The #111 risk note becomes "the future `Stem.Communication` adapter MUST emit a device-arrived event into the port".

### 6.3 — Sub-discriminator payloads (NOT sibling DU cases)

```fsharp
type IdleCause =
    | AwaitingBoot                      // pre-FR-001, auto-promotes on boot complete
    | UserPaused                        // user clicked Stop

type SearchAttempt =
    | NoAdapterEnumerated               // host returns zero PEAK adapters
    | NoCandidateAvailable of count: int  // ≥1 enumerated, all returned busy (after iterating all)
    | Polling                           // scan / event-wait in flight

type FaultCause =
    | BusOff                            // CAN controller bus-off
    | UnexpectedAdapterStatus of code: uint32  // unrecognised PEAK status
    | DriverNotInstalled                // PCANBasic native DLL missing
    | AdapterHardwareFailure            // driver responded then adapter unresponsive

type CanLinkState =
    | Idle      of cause: IdleCause      * since: DateTimeOffset
    | Searching of attempt: SearchAttempt * since: DateTimeOffset
    | Opening   of candidate: AdapterCandidate * since: DateTimeOffset
    | Open      of adapter: AdapterIdentification * openedAt: DateTimeOffset
    | Faulted   of cause: FaultCause     * since: DateTimeOffset
```

**Rationale for payload vs sibling DU**:
- Top-level shape (`Idle | Searching | Opening | Open | Faulted`) is the chip-family carving — it matters for UX projection and pattern-match exhaustiveness on the rendering side. DU branches.
- Sub-discriminators (`FaultCause.BusOff` etc.) are cause-of-the-state-within-its-family — they matter for detail string formatting but not for chip-family decisions. Payload.
- This means Lean theorem `state_classification_total` partitions the 5 top-level cases; the sub-discriminators get separate (smaller) theorems if needed.

### 6.4 — Chip family projection (3 colours, unchanged from substrate)

| Chip colour | State |
|---|---|
| 🟢 Green   | `Open` |
| 🔴 Red     | `Faulted` |
| ⚪ Grey    | `Idle`, `Searching`, `Opening` |

### 6.5 — User affordances (3, at affordance level — button labels are GUI design)

| Affordance | Available in | Effect |
|---|---|---|
| **Stop**      | `Searching`, `Opening`, `Open`, `Faulted` | → `Idle (UserPaused)` |
| **Start**     | `Idle (UserPaused)`                   | → `Searching` |
| **Reconnect** | `Faulted`                             | → `Opening` (same candidate) |

`Idle (AwaitingBoot)` has no enabled affordance — Start is disabled-with-tooltip "waiting for dictionary boot".

### 6.6 — Bench-validated clarifications that carry forward (unchanged in spirit)

- **Chip colour discipline** (substrate session 2026-05-24): three colours — green / grey / red. Carries forward unchanged.
- **Sticky `since` rule** (substrate FR-002b, session 2026-05-25): `since` reflects the moment the underlying root cause was first observed. Carries forward — applies to ALL states (Idle, Searching, Opening, Open, Faulted), not just former Error.
- **Click-feedback contract** (substrate FR-003, session 2026-05-25): on click, the chip MUST transit through the in-flight grey state for the duration of the in-flight call. Carries forward as: clicking Reconnect from `Faulted` transits through `Opening` (grey) before resolving to `Open` (green) or `Faulted` (red).
- **Em-dash detail string SHOULD** (substrate session 2026-05-26): detail SHOULD follow `<cause> — <imperative suggestion>` when an imperative naturally applies. Carries forward — applies to `Searching`, `Opening`, `Faulted` detail strings.
- **Drop "PEAK ·" vendor-tag from cause strings** (substrate session 2026-05-26): unchanged.

### 6.7 — Things removed from the substrate

- `Initializing` as a top-level state → folded into `Idle (AwaitingBoot)`.
- `Disconnected.LinkNotYetOpened` → dead, removed.
- `Disconnected.MidSessionUnplug` → `Searching.Polling` (after vendored-stack device-lost event) OR `Faulted` (if hardware died).
- `Disconnected.ReconnectPending` → `Opening` (no special name; Open in flight, source state doesn't matter).
- `ErrorClassification.Recoverable` / `.Fatal` → gone. Named fault causes carry their own description; user reads it and decides.
- Recoverable→Fatal escalation logic in `CanLinkService` (data-model.md substrate §1.3 Invariant #2, operational and unmechanised) → gone. The state machine is stateless w.r.t. attempt history; the user sees the same `Faulted` cause re-rendered after a failed Reconnect, no FSM flip needed.

### 6.8 — Searching retry policy (deferred to plan.md)

Spec stays at "Searching transitions to Opening when a candidate becomes available, whether via vendored-stack event or periodic re-scan". Plan-level: pin the cadence (5 s periodic poll? event-only? both?). Not load-bearing on the spec contract.

---

## 7 — Open question parked when Luca left

When Luca returns: ask whether to write all seven artefacts at once, or write `spec.md` first as a smaller surface to review. He hadn't picked between those before parking.

Recommended default if no answer: write `spec.md` first. Smaller blast radius if a "wait, that's wrong" lands.

---

## 8 — Artefact-writing plan (when Luca greenlights)

In commit order (vertical, one per concern, all doc-only):

1. **`specs/002-can-link-lifecycle/spec.md`** — clean greenfield write. Two user stories (US1 visibility at start of shift; US2 mid-session resilience including Stop/Start). FRs tight-renumbered FR-001..FR-NN. Bench-validated clarifications preserved as session bullets. Section order matches `.specify/templates/spec-template.md`.
2. **`specs/002-can-link-lifecycle/data-model.md`** — new F# DU (per §6.3), invariants, state-machine diagram (Mermaid per constitution), Lean Phase 2 cross-references.
3. **`specs/002-can-link-lifecycle/research.md`** — decision log: Idle inclusion (§4b), drop Recoverable/Fatal (§4c), multi-adapter iteration (§4a), hot-plug as explicit edge (§6.2), Searching retry cadence (§6.8), notification-center deferred (§4d), v0.1.0 mental baseline (§4e).
4. **`specs/002-can-link-lifecycle/plan.md`** — Constitution Check re-run, Project Structure (unchanged: archetype A, two-TFM split), Lean Phase 2 re-prove plan (two theorems → three or four: `state_classification_total` over five cases, `fault_cause_total`, `idle_cause_total`, `passive_observer_emits_no_transmit` unchanged), Searching retry cadence decision.
5. **`specs/002-can-link-lifecycle/tasks.md`** — renumbered T001 onwards, lifecycle-only (no shared T012–T033 retained — those are Phase 2 foundation already shipped; the new tasks.md is forward-looking only).
6. **`specs/002-can-link-lifecycle/quickstart.md`** — refreshed bench walkthrough against new FSM.
7. **`specs/002-can-link-lifecycle/migration-map.md`** — old→new state names + FR numbers + section refs. Load-bearing for Phase C. Format:

   ```text
   | Old artefact (pre-Phase-B)                                | New artefact (post-Phase-B)        | Status                | Notes |
   |-----------------------------------------------------------|------------------------------------|-----------------------|-------|
   | `CanLinkState.Initializing`                               | `CanLinkState.Idle(AwaitingBoot)`  | renamed + restructured | DU branch dissolved into payload |
   | `CanLinkState.Disconnected(NoAdapterPresent,_)`           | `CanLinkState.Searching(NoAdapterEnumerated,_)` | renamed + restructured | Family changed |
   | `CanLinkState.Disconnected(MidSessionUnplug,_)`           | `CanLinkState.Searching(Polling,_)` after device-lost event | restructured | Hot-plug now explicit |
   | `CanLinkState.Disconnected(ReconnectPending,_)`           | `CanLinkState.Opening(candidate,_)` | renamed | "In-flight Open" generalised |
   | `CanLinkState.Disconnected(LinkNotYetOpened,_)`           | (none)                             | removed               | Dead constructor in substrate |
   | `CanLinkState.Error(Recoverable d, since)`                | `CanLinkState.Faulted(cause matching d, since)` | restructured | Severity dropped |
   | `CanLinkState.Error(Fatal d, since)`                      | `CanLinkState.Faulted(cause matching d, since)` | restructured | Severity dropped |
   | FR-001 (open after dict boot)                             | FR-001 (same, reworded for Idle→Searching transition) | preserved | |
   | FR-002 (three top-level chip values)                      | FR-002 (three chip colours, five FSM states) | reworded | DU/colour split made explicit |
   | FR-002a (Recoverable/Fatal in headline)                   | (none)                             | removed               | Severity dropped |
   | FR-002b (sticky since)                                    | FR-NN (sticky since, applies to all states) | generalised | No longer Error-only |
   | FR-003 (Reconnect visibility + click contract)            | Split into: FR-NN (Stop/Start/Reconnect affordances) + FR-NN (click-feedback contract through `Opening`) | split | |
   | FR-004 (detail affordance contents)                       | FR-NN (detail string convention)   | preserved             | Em-dash SHOULD unchanged |
   | FR-005 (Disconnected sub-classification)                  | (split across SearchAttempt + FaultCause payloads) | restructured | |
   | FR-006 (tool usable without adapter)                      | FR-NN                              | preserved             | |
   | FR-014 (zero CAN frames)                                  | FR-NN                              | preserved             | |
   | FR-015 (LinkStateChanged observable)                      | FR-NN                              | preserved with new payload shape | spec-003 FR-015' consumer re-derive |
   | FR-016 (dictionary row independence)                      | FR-NN                              | preserved             | |
   ```

   (Placeholder numbers; the writer fills them after spec.md fixes the new numbering.)

---

## 9 — Repo / git / dual-remote state

- Working tree clean at session pause (no commits made yet on this branch — only branch creation).
- `main` is at `5198a34`. Branch `docs/002-lifecycle-spec-refresh` cut from there.
- Remote `github` is the active remote. `bitbucket` is push-blocked via `no_push`; mirror via Actions workflow on every push to `main` (the branch won't be mirrored until merge).
- Push this branch to `github` so the home-machine session can fetch.
- One stale worktree `docs/cross-link-can-state-model` exists (branch gone upstream) — leave alone.
- Side errand parked: standards PR #115 (setup-dotnet @v5 → @v5.1.0 pin). Not on this critical path.

---

## 10 — Skills/memory state when paused

Loaded this session: `workflow`, `speckit`, `speckit-specify`, `speckit-clarify`.

Tasks: `#1 [in_progress] Phase B: rewrite spec-002 lifecycle artefacts via speckit`, `#2 [pending] Phase C: impl ↔ refined-spec delta report`.

Memories NOT updated yet (do this when writing finishes):
- `spec-002-live-roadmap.md` — bump `Last updated`, add a §"Phase B redesign" entry noting the FSM reshape and the Lean re-prove cost.
- `spec-003-live-roadmap.md` — note that spec-003 FR-015' will need a re-derive against the new `LinkStateChanged` payload shape once Phase B lands.

---

## 11 — When the rewrite is done

- One PR on github: `docs/002-lifecycle-spec-refresh` → `main`. Label `docs`. Title: `docs(spec-002): rewrite lifecycle spec via /speckit (Phase B)`.
- PR body: link to migration-map.md, name the FSM redesign, note Phase C impl drift is expected and will follow as separate PRs.
- After merge: delete `HANDOFF.md` + `PROMPT.md` (they were throwaway). Update both live-roadmap memories.
- Phase C kicks off: re-read impl on `main` against the refined spec; produce delta table; file follow-up issues for genuine drift.
