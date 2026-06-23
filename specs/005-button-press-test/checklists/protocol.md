# Protocol-Framing Requirements Checklist: Button-Press Test (Input Side)

**Purpose**: Validate that the requirements governing the button-state **wire format** — carrier,
command/address identification, bit polarity, reassembly, and edge handling — are complete, clear,
consistent, and measurable before implementation (constitution-recommended for any feature touching
protocol framing).
**Created**: 2026-06-23
**Feature**: [spec.md](../spec.md) · [research.md](../research.md) ·
[contracts/button-state-wire-format.md](../contracts/button-state-wire-format.md)

> "Unit tests for English" — each item tests whether the **requirement is written correctly**, not
> whether code works. `[x]` = the artifact set already satisfies it; `[ ]` = a residual gap/ambiguity
> noted inline.

## Requirement Completeness

- [x] CHK001 Is the wire carrier of a button-state report specified (SP_APP `VAR_WRITE`)? [Completeness, research §R1, contract §App-layer payload]
- [x] CHK002 Is the command code that identifies a button-state frame specified (`0x00:0x02`)? [Completeness, contract §App-layer payload, research §R1]
- [x] CHK003 Is the variable-address set the decoder recognises specified (`{0x8000, 0x803E}`)? [Completeness, contract §App-layer payload, research §R6]
- [x] CHK004 Is the per-bit button assignment (`UP=bit0 … LIGHT=bit7`) documented? [Completeness, spec §Assumptions, research §R3, CORRECTIONS §C3]
- [x] CHK005 Is the transport/reassembly path specified (packetisation → shared `PacketReassembler`)? [Completeness, research §R1/§R5, contract §Transport]
- [x] CHK006 Is the byte offset of the command + address within the reassembled packet pinned? [Completeness, contract §Transport (bytes 7–8 / 9–10)]

## Requirement Clarity / Ambiguity

- [x] CHK007 Is the bit polarity stated unambiguously (pressed = bit `0`, released/idle = bit `1`)? [Clarity, research §R2, contract §Bitmap semantics]
- [x] CHK008 Is "a press is the transition **into** the pressed state" defined as an **edge**, not an absolute level? [Clarity, spec §Clarifications, FR-006, research §R2]
- [x] CHK009 Is the baseline-seeding rule specified (first observed frame; never read an absolute byte as press-state)? [Clarity, research §R2, contract §Bitmap semantics]
- [ ] CHK010 Is the address used by the single-panel bench case unambiguous across all artifacts? [Ambiguity, research §R1 vs §R6] — **residual:** R1 derives keyboard-2 as `0x8073` (`IDBoardNumber−1`) while the recognised set is `{0x8000, 0x803E}`; `0x803E` (legacy) vs `0x8073` (firmware) is unreconciled. Non-blocking — the board-1 bench uses `0x8000`; reconcile as a research.md footnote (see analyze MEDIUM I1).

## Requirement Consistency

- [x] CHK011 Is the firmware-vs-legacy polarity conflict explicitly reconciled (not left contradictory)? [Consistency, research §R2] — legacy set-bit-pressed read the *release* edge; spec-005 reads the *press* edge — two edges of one gesture, not a contradiction.
- [x] CHK012 Is the command code (`0x00:0x02`) consistent across research, contract, and data-model? [Consistency, research §R1/§R6, contract, data-model §1]
- [ ] CHK013 Is the recognised address set consistent across every artifact that names one? [Consistency] — same residual as CHK010 (`0x803E` in R6/contract/data-model vs `0x8073` in R1).

## Coverage / Edge Cases

- [x] CHK014 Is the virgin sentinel (`0x80FE`) explicitly excluded as a test result? [Edge Case, contract §App-layer payload, research §R6]
- [x] CHK015 Is the handling of bits outside the active mask specified (ignored / diagnostic-only)? [Coverage, FR-014, research §R3]
- [x] CHK016 Is the held-button / bouncing case addressed at the wire level (the edge fires once)? [Edge Case, spec §Edge Cases, research §R2]
- [x] CHK017 Is the boot-state caveat documented (`TxTasti` zero-init ⇒ absolute byte is never press-state)? [Edge Case, research §R2, contract §Bitmap semantics]
- [x] CHK018 Is the CRC-validation status documented as an inherited limitation? [Assumption, contract §Transport]

## Measurability / Traceability

- [x] CHK019 Is the protocol framing pinned to a verifiable firmware source (`UserMain.c` line refs)? [Traceability, research §R1/§R2]
- [x] CHK020 Is the inline command/address hardcode's stopgap status documented with a removal path? [Traceability, research §R6, Constitution VI, §C5 fetch ticket #254]
- [x] CHK021 Is the live-boundary confirmation of the wire format + polarity scheduled (not assumed)? [Coverage, quickstart §Polarity confirmation, bench gate #253, tasks T036/T037]

## Notes

- The spec.md *Functional Requirements* stay behaviour-level and technology-agnostic by design; the
  protocol-framing facts live in *Assumptions* + research.md + the wire-format contract (the
  house-style exception recorded in [requirements.md](./requirements.md)). This checklist therefore
  traces mostly to research/contract — that is expected, not a leak.
- **Two open items (CHK010 / CHK013)** are the same single residual: the `0x803E` (legacy) vs
  `0x8073` (firmware) address discrepancy inside the frozen research.md. Non-blocking — OPTIMUS bench
  validation exercises board-1 `0x8000`; the observer hardcodes `{0x8000, 0x803E}` inline (T015) and
  the bench (T037) confirms the live address. Recommended fix: a one-line research.md reconciliation
  footnote when convenient — do **not** re-litigate the frozen artifacts now.
- Everything else passes: the wire carrier, command, polarity, baseline rule, virgin sentinel,
  inactive-bit handling, and the stopgap/removal path are all specified and firmware-traced.
