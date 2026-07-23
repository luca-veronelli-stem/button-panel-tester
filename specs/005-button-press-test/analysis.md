# Cross-artifact analysis — amendment round 2 (#293 / Phase J), 2026-07-20

`speckit-analyze` run (read-only subagent) against the Phase J amendment set: spec.md §Clarifications
Session 2026-07-20, research.md R1 dual-rate, data-model.md §6a/§6b, the wire-format contract, and
tasks.md Phase J — cross-checked with the pre-existing artifact set, the constitution (v1.0.2), Lean
Phase 4, issue #293, quickstart.md, and `docs/Context/bpt-rollout/`.

**Verdict as returned: loop back to tasks + small spec amendments.** All findings were dispositioned
in the same amendment round (commit alongside this report); Phase J is now ready for implementation.

## Findings and dispositions

| ID | Sev | Finding (file:section) | Disposition |
|---|---|---|---|
| C1 | CRITICAL* | T051 folded the Lean theorem + F# impl into one commit, against Principle I's Lean-first order (every prior theorem-bearing slice — A1, B1, D1, E1, I1 — ships a separate Lean-only commit) | **Fixed**: Phase J restructured — T051 is now a Lean-only commit (J2, mirrors I1); the F# vertical moved to T052 (J3, depends on J2) |
| G1 | HIGH | No Phase J task touched the hardware suite: `ButtonPressTestHardwareTests.fs:87` waits `heartbeatTimeout = 2 s` for the first observation — fails ~84 % of the time against a cold panel's ≈ 12.5 s branch; #253 checklist hooks stale | **Fixed**: new T053 (J4) — raise to 15 s, rewrite the doc, audit the other budget constants, refresh the #253 hooks |
| I1 | HIGH | FR-006 still scored only the press edge, contradicting §6b's unarmed-release rule | **Fixed**: FR-006 amended (armed → press edge; unarmed → first release) with an amendment rider |
| I2 | HIGH | SC-005 still embedded "provisional default ~3 s" | **Fixed**: SC-005 now says ~20 s, firmware-derived |
| I3 | MED | plan.md had no 2026-07-20 amendment; §Performance Goals / §Constraints / the 2026-06-24 amendment's "bench-confirmed" phrasing contradicted the new material | **Fixed**: plan.md §Amendment 2026-07-20 appended |
| G2 | MED | FR/SC coverage matrix not extended for Phase J (nor Phase I) | **Fixed**: 8 affected rows now cite T046/T047 (I) and T050–T053 (J) |
| I4 | MED | Phase I preamble still asserted the 2 s / 3 s defaults and the "~12 s was a different message" mis-attribution with no superseded marker | **Fixed**: superseded-by-Phase-J blockquote added |
| I5 | MED | data-model §2 and the wire-format §Bitmap semantics bullet described the press edge as the whole scoring rule; `KeyStateBitmap.lean` header claims to mechanise them | **Fixed**: §6b cross-references added at both sites; the Lean header update is in T051's task text. (No theorem contradiction: `press_edge_iff_high_to_low` stays true — T052 layers `scored`/armed above an unchanged `pressEdges`) |
| I6 | MED | SC-002 and the held-button edge case were unverifiable/unmeetable for the unarmed first press (the press never reaches the wire) | **Fixed**: unarmed riders added to SC-002 and the held-button edge case |
| R1 | MED | The `reset=1` / `MotherBoardAddress` total-silence trap was "tracked" only inside #293's own body — no standalone tracker once #293 closes | **Fixed**: standalone follow-up issue filed (see #293 comments for the number) |
| S1 | MED | quickstart.md stale: "Select the panel" step (auto-target since #270), no cold-panel ≈ 12.5 s warning, "Pass within ~1 s" wrong for the unarmed first press | **Fixed**: folded into T054 (docs sweep) |
| S2 | LOW | Roadmap spec-007 sketch ("5 s heartbeat timeout / any frame counts") would re-commit this defect at session level | **Fixed**: folded into T054 (annotation) |
| G3 | LOW | T052-CHANGELOG (now T054) had no backing AC; #293 AC-5 had no task | **Accepted**: house discipline covers the CHANGELOG; AC-5 is already landed in the amendment commits — noted in the Phase J preamble |
| A1 | LOW | #293 AC-4 cited the wrong Lean root (`specs/ButtonPanelTester/Phase4/`) | **Fixed**: issue body patched to `lean/Stem/ButtonPanelTester/Phase4/` |

\* Conditional — CRITICAL if same-commit folding does not satisfy Principle I's "before"; the repo's
own precedent (I1 Lean-only) says it does not, so it was treated as CRITICAL and fixed.

## Coverage after disposition

- #293 AC-1/AC-2 → T050; AC-3 → T052; AC-4 → T051 + T052; AC-5 → landed in the amendment commits;
  AC-6 → process. Unmapped task: T054 (docs, house discipline).
- Amended requirements: FR-001/FR-013/SC-005/SC-008 → T050 (+T053 at the bench boundary);
  FR-006/FR-014/SC-001/SC-002 → T051/T052.
- Constitution: PASS (mandatory triple on the arming rule, Lean-first as a separate commit, no new
  stopgap, `test_enabled_iff` parametric and unaffected).

---

# Cross-artifact analysis — amendment round 3 (#296 / Phase K), 2026-07-23

`speckit-analyze` run (read-only subagent) on the Phase K amendment (destination addressing /
variant-from-senderId, commit af199b9), cross-checked with the shipped code seams, the Lean module,
issue #296 ACs, the constitution, and the evidence trace (`bench-logs/pcan/test1.trc` — verified:
all frames on `0x00000008`, senderId bytes `00 0A 01 01`, 186.9 ms ramp then 12.38 s).

**Verdict as returned: loop back for the amendment sweep (H1–H4 + M1) before implementation; the
K1/K2 design itself sound and implementable.** All findings dispositioned in the same round:

| ID | Sev | Finding | Disposition |
|---|---|---|---|
| H1 | HIGH | FR-001's normative text still keyed the variant to the directed CAN ID | **Fixed** — FR-001 amended inline (senderId bits 23–16 + destination note), FR-006 precedent |
| H2 | HIGH | data-model §1a accept bullet + §7 still said "iff `variantOfDirectedId CanId`" | **Fixed** — both re-keyed to the (cmd, addr, senderId) rule |
| H3 | HIGH | observer-port contradicted itself in three places (port doc, adapter table, consumed-surfaces) | **Fixed** — all three re-keyed |
| H4 | HIGH | FR/SC matrix rows FR-001/SC-008 lacked T055/T056 | **Fixed** — rows extended |
| M1 | MED | T056(b) "senderId already parsed by the packet decoder" — wrong seam (the observer hand-indexes; senderId = reassembled bytes 1–4 BE) | **Fixed** — task text re-worded to the real seam |
| M2 | MED | Phase I blockquote's "the re-key design stands" now false for the accept rule | **Fixed** — extended with the Phase K supersession |
| M3 | MED | T055 silent on the old `accepted` def's fate; `arbitration_id_irrelevant` vacuous unless the model carries the arb id | **Fixed** — T055 re-worded (re-document `accepted`; model packet incl. arb id) |
| M4 | MED | Hardware suite's runtime `Assert.Fail` diagnostics (:282/:333/:394) still say "on its directed CAN id" — misleads the operator | **Fixed** — folded into T057 (string-only) |
| M5 | MED | Stale directed-id docs in Ports.fs / ButtonPressTest.fs / App.fs / the fake, covered by no task | **Fixed** — Ports.fs → T056(a); the rest → T057 |
| M6 | MED | Unreleased #270 CHANGELOG entry asserts dropping `0x00000008` | **Fixed** — T057 amends it |
| L1 | LOW | New helper name unpinned | **Fixed** — `variantOfSenderId` pinned in data-model §1a |
| L2 | LOW | research R5 pre-#270 envelope drift | **Fixed** — folded into T057 |
| L3 | LOW | Phase K preamble lacked the AC-5 landed-in-amendment note | **Fixed** — note added |
| L4 | LOW | Reassembler map now allocates per id seen | **Fixed** — commit-body note required by T056 |
| L5 | LOW | "Fakes drive destination-addressed streams" overpromise (fake is post-reassembly) | **Fixed** — clarified doc-only |

Constitution: PASS (0 CRITICAL). Coverage: 100 % with the matrix now current. The RED premise of
T056 was verified real by the analyzer: the shipped observer drops arbitration ID `0x00000008` at
`onFrame` before reassembly.
