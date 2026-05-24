# button-panel-tester — Context for the HP ProBook session

This folder is a self-contained briefing for a Claude session running on
the HP ProBook (Windows 11) that will drive implementation of specs 002
onwards in `button-panel-tester`.

The Linux/NixOS session that produced these docs has no access to the
real PCAN hardware or Windows-only code paths; from here on the actual
spec drafting (`/speckit.specify`), planning, and implementation are
done from the Windows machine where the work skills are installed and
the panels can be plugged in.

## Read order

1. **`01-context.md`** — what the tool is for, the product reality, the
   board hardware and firmware truth, the four board variants and the
   marketing-name ↔ part-number ↔ MachineType mapping.

2. **`02-vendoring-plan.md`** — the STEM protocol stack decision: we
   vendor-copy from `stem-communication` rather than wait for the NuGet
   to ship. Exact files to copy, namespace rules, future-migration
   contract.

3. **`03-roadmap.md`** — six follow-on specs (002 through 007). Each
   one has an `Input:` paragraph ready to feed into `/speckit.specify`,
   locked design decisions, open questions for `/speckit.clarify`, the
   scope cliff, and the hardware-risk story it retires.

## Working assumptions baked into these docs

- **Standards baseline:** v1.9.0 (the version `main` is currently
  bumped to per `d0f8c9c`). Constitution + `docs/Standards/` are the
  source of truth on architectural rules; this folder defers to them
  for anything style-related.

- **Firmware is canonical, tools are reference.** When the protocol
  stack on the .NET side disagrees with the C firmware, the firmware
  wins. Treat `stem-communication` as the implementation pattern but
  spot-check against `~/Downloads/stem-fw-protocollo-seriale-stem-*/`
  for any wire-format question.

- **Single panel on the bus.** All six follow-on specs assume one
  baptized panel at a time. Multi-panel topology (`BoardNumber > 1` in
  the legacy code) is out of scope for the entire roadmap as written.

- **Firmware is one binary.** All four marketed machine variants run
  the same `stem-fw-pac5524-tastiera-can-app` firmware. The variant is
  a tool-side projection of "which subset of the universal board's
  features matter for this machine model" — not a firmware fork.

- **Communication style.** Low-profile commits and PRs, no AI
  attribution anywhere.

## Status snapshot (as of writing)

- spec-001 (dictionary fetch + status row + registration) is landed on
  `main`. Repo is clean. Only open issue is #104 (waiting on an
  external dep).
- Standards v1.9.0 bumped via `d0f8c9c`.
- HP ProBook has Windows 11 fresh-installed, `luca-veronelli-stem`
  llm-settings deployed (incl. `dotnet` + four `stem-*` discipline
  skills + speckit skills). PEAK PCAN-USB IPEH-002022 + 12 pristine
  panels + 2 known-bad panels available on bench.
