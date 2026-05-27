# Open spec-002 analyze findings (Phase B, post `0be6872`)

Findings flagged during the two `/speckit-analyze` runs of 2026-05-27 that the queue artefacts (`research → plan → tasks → quickstart → migration-map`) might not address explicitly. Re-check at every `/speckit-analyze` cycle; aim to leave only explicitly-accepted entries by the final analyze before the PR opens.

Cross off resolved items as they land. Delete this file in the cleanup commit.

## Not in the queue scope

1. **`contracts/can-link-port.md` is stale.** Encodes the substrate `ICanLink` port (`OpenAsync / CloseAsync / ReconnectAsync`, old DU payload, no `CancellationToken` surface). `PROMPT-DATA-MODEL.md` items 1-8 do not cover a contracts refresh. Options:
   - Insert a queue item between plan.md and tasks.md to refresh `contracts/can-link-port.md` against the 5-family payload and FR-006 cancellation contract.
   - Or document it as Phase C drift in `migration-map.md` and let it land in the impl refactor PR.

   Decide before plan.md commits.

## Probably in the queue but not explicit

2. **F1 — filename pins (plan.md).** data-model.md §2.1 / §3.1 forward-reference plan.md §Project Structure for the concrete filenames of `PcanAdapterEnumeration.fs` (new) and `PcanAdapterIdentity.fs` (existing, kept). plan.md rewrite MUST pin these — `PROMPT-DATA-MODEL.md` doesn't list F1 explicitly.

3. **F2 — property-suite names (plan.md).** data-model.md §1.3 Invariant #5 and §4 footer forward-reference plan.md §Constitution Check Principle II for the FsCheck property names. plan.md rewrite MUST enumerate at least:
   - sticky-since preservation across passive re-observation (FR-004),
   - `LinkStateChanged` total / exhaustive over family (FR-014),
   - reachability replacement for the retired `transition_reachability_closed`.

4. **F3 — Lean theorem retirement (tasks.md).** data-model.md §4 retires `transition_reachability_closed` and adds `state_classification_total` (5-family), `fault_cause_total`, `idle_cause_total`, `faulted_reconnect_target_total`. tasks.md rewrite MUST include a task to re-author `Phase2/CanLinkState.lean`.

5. **A1 — FR-006 cancellation budget.** spec.md FR-006 says "the FSM SHOULD land in `Idle(UserPaused)` within the cancellation budget pinned in plan.md". plan.md rewrite MUST pin a concrete budget (recommend ≤ 250 ms on a normal-load workstation; verify against PEAK driver typical cancel latency).

## Accepted gaps that might silently widen

6. **U1 — spec-001 seed link.** spec.md Assumption §3 references spec-001's seed-fallback for "dictionary always usable at runtime". No artefact explicitly says spec-002 relies on that guarantee. research.md decisions log (PROMPT-DATA-MODEL.md §2 item 10 — "Boot order decoupled from dictionary") is the natural home.

7. **U2 — FR-012 iteration cap.** spec.md does not bound how many candidates `Opening` will iterate before falling to `Searching(NoCandidateAvailable)`. data-model.md §1.2 is loose on this too. plan.md or a data-model.md amendment should pin: "iterate every candidate the vendored stack enumerates; no internal cap."

8. **O1 — "active state" definition (CHK010).** spec.md FR-006 uses "active state" = `Searching ∪ Opening ∪ Open ∪ Faulted`. Not explicitly pinned anywhere; CHK010 deferred to `/speckit-analyze`. One-line addition to data-model.md §1.3 (or §1.1 table footer) would close it. Currently open.

9. **A2 — FR-009 sub-perceptual cue.** Accepted gap (CHK008). Worth a sanity restate in plan.md: "no minimum-visibility floor; the cue duration matches the in-flight call duration; consistent with FR-009 Note."

## Style / wording (LOW)

10. **D1 — truth-and-acknowledge restated 3× in spec.md.** Accepted as intentional reinforcement after CHK013 review. No edit; close at next analyze.

11. **D2 — chip-colour mapping appears in Presentation surfaces table + FR-002.** Accepted as table-normative / FR-requirement. No edit; close at next analyze.

## How to use

1. Read at the start of each Phase B `/speckit-analyze` cycle.
2. Cross off resolved items as artefacts land.
3. The final `/speckit-analyze` before the PR opens should leave only items 6 / 7 / 10 / 11 (accepted), or note the rest in `migration-map.md` for Phase C.
4. Delete this file in the cleanup commit (same step as the `HANDOFF*` / `PROMPT*` files).
