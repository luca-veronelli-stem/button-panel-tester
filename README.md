# ButtonPanelTester

[![CI](https://github.com/luca-veronelli-stem/button-panel-tester/actions/workflows/ci.yml/badge.svg)](https://github.com/luca-veronelli-stem/button-panel-tester/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-Proprietary-red)](#license)

> **Bench tool to test STEM button-panel hardware over CAN.**
> **Standard:** v1.15.0 — see [`docs/Standards/`](./docs/Standards/).

---

## Overview

ButtonPanelTester is a bench tool that exercises STEM button-panel hardware over CAN. The user-visible surface today is a single window whose **dictionary status row** reports the provenance, age, and health of the loaded variable dictionary (live, cached, or extracted from an embedded seed) and offers a one-click **Refresh** against a remote `stem-dictionaries-manager`. A first-launch **registration ceremony** swaps a short-lived bootstrap token for a long-lived per-installation API credential stored under DPAPI, so the operator authenticates the tool once per machine and the credential is rotated atomically server-side on any subsequent Re-register. See [`specs/001-fetch-dictionary/quickstart.md`](specs/001-fetch-dictionary/quickstart.md) for the end-to-end operator walkthrough.

A second **CAN status row** reports the live state of the PEAK CAN link over a four-family lifecycle (initializing, connected, disconnected — no adapter found or mid-session unplug — or faulted), with a colour-coded chip, a remediation-oriented headline, a detail tooltip, and a manual **Reconnect** control. It opens the configured PEAK PCAN-USB adapter at 250 kbps and surfaces bench realities: no adapter present, mid-session unplug, driver missing, bus-off, and transient PEAK faults (including a one-click driver-download link when the PEAK driver is absent). See [`specs/002-can-link-lifecycle/quickstart.md`](specs/002-can-link-lifecycle/quickstart.md) for the lifecycle walkthrough.

A third **Panels-on-bus list** passively discovers the panels currently on the bus: while the CAN link is Connected it listens for STEM auto-address `WHO_I_AM` broadcasts and shows one row per panel — the UUID, the decoded variant (marketing name, virgin, or unknown with the raw machine-type byte on a detail tooltip), and the last-seen time — coalescing re-broadcasts by UUID, pruning a panel after 15 s of silence, and clearing the list when the link leaves Connected. Discovery is pure observation: the tool transmits no CAN frame. See [`specs/003-panel-discovery/quickstart.md`](specs/003-panel-discovery/quickstart.md) for the discovery walkthrough.

A **Baptize / Reset-to-virgin** workflow turns the Panels-on-bus list from read-only into the tool's first CAN-transmit feature: when exactly one virgin panel is announcing, the operator claims it as one of four BoardVariants (EDEN-XP, OPTIMUS-XP, R-3L XP, EDEN-BS8) via the three-step auto-address master sequence, or resets a claimed panel back to virgin. Success is reported only on **confirmed adoption** — the panel acknowledges the address assignment and goes silent on the broadcast — never on bare write-completion; a claim that does not take is reported as such and guides the operator into the Reset → re-baptize recovery. Baptize and Reset are disabled with an explanation unless exactly one panel is on the bus, and the tool keeps no memory of panels it baptized. See [`specs/004-baptism-workflow/quickstart.md`](specs/004-baptism-workflow/quickstart.md) for the operator and bench walkthrough.

## Quick Start

```powershell
dotnet build
dotnet test
dotnet run --project src/ButtonPanelTester.GUI
```

## Solution Structure

```
src/
├── ButtonPanelTester.Core/                    domain types + ports
├── ButtonPanelTester.Services/                use cases
├── ButtonPanelTester.Infrastructure/          adapters (EF Core, drivers, IO)
├── ButtonPanelTester.Infrastructure.Protocol/ vendored CAN + raw-frame stack (frozen C#)
└── ButtonPanelTester.GUI/                     Avalonia + FuncUI
tests/
└── ButtonPanelTester.Tests/               xUnit + FsCheck + Avalonia.Headless
specs/                           Spec-Driven Development (spec-kit) feature folders (optional)
lean/                            Lean 4 workspace (optional — lakefile.lean + lean-toolchain)
docs/                            documentation (Standards/ tracked here)
eng/                             build / release scripts
```

## Documentation

- Dictionary fetch & registration walkthrough: [`specs/001-fetch-dictionary/quickstart.md`](./specs/001-fetch-dictionary/quickstart.md).
- CAN-link lifecycle walkthrough: [`specs/002-can-link-lifecycle/quickstart.md`](./specs/002-can-link-lifecycle/quickstart.md).
- Passive panel discovery walkthrough: [`specs/003-panel-discovery/quickstart.md`](./specs/003-panel-discovery/quickstart.md).
- Baptism (claim / reset-to-virgin) walkthrough: [`specs/004-baptism-workflow/quickstart.md`](./specs/004-baptism-workflow/quickstart.md).
- Roadmap: [`docs/ROADMAP.md`](./docs/ROADMAP.md) — shipped state, next spec, provisional order, v1.0 definition.
- Standards followed: [`docs/Standards/`](./docs/Standards/) — pinned to `v1.15.0`.
- Changelog: [`CHANGELOG.md`](./CHANGELOG.md).
- Repo-specific notes: [`CLAUDE.md`](./CLAUDE.md).

## License

- **Owner:** STEM E.m.s.
- **Author:** Luca Veronelli
- **Creation Date:** 2026
- **License:** Proprietary — All rights reserved.
