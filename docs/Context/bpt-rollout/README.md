# bpt-rollout context

Historical briefing folder prepared on a Linux/NixOS session (no PCAN
hardware available) before any Windows-side spec drafting began. It
was self-contained and intended to bootstrap the receiving Claude
session into specs 002 through 007 of `button-panel-tester`.

Imported into the repository on **2026-05-24** at the start of
spec-002 drafting so the audit trail of "what we thought going in"
versus "what we learned reading the firmware" lives next to the
specs that consume both.

## Files

| File | Purpose |
|---|---|
| [`00-INDEX.md`](./00-INDEX.md) | Original orientation — read order, baked-in assumptions, status snapshot at the time of authoring. |
| [`01-context.md`](./01-context.md) | Original — product, hardware, firmware, board-variant table, references. |
| [`02-vendoring-plan.md`](./02-vendoring-plan.md) | Original — vendoring discipline for the .NET protocol stack. |
| [`03-roadmap.md`](./03-roadmap.md) | Original — six follow-on specs (002–007), each with an input paragraph + locked decisions + open questions. |
| [`CORRECTIONS.md`](./CORRECTIONS.md) | New, dated **2026-05-24**. Records what the firmware/code audit (run immediately before spec-002 drafting) contradicted or replaced in the four original files. The audit covered the panel firmware, the protocol firmware, the four machine motherboard firmwares, and the two candidate .NET vendor sources. |

## How to read this folder

For new readers: read [`CORRECTIONS.md`](./CORRECTIONS.md) **first**.
It tells you which parts of the original briefing are still load-bearing
and which were overturned by the audit. Then read the original files
through that lens.

The original four files are kept verbatim as historical artifacts.
They are not edited to reflect later findings — the constitution's
Sync Impact Report convention applies here too: the record of what we
believed at a given moment is more useful than a quietly-revised
"current" version.

## Authoritative source going forward

For current design decisions on protocol framing, baptism, board
variants, and vendoring, the authoritative source is the per-spec
folder under [`specs/`](../../../specs/), starting with
`specs/002-*/spec.md` once it lands. CORRECTIONS.md is a one-shot
audit log, not a living document.
