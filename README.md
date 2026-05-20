# ButtonPanelTester

[![CI](https://github.com/luca-veronelli-stem/button-panel-tester/actions/workflows/ci.yml/badge.svg)](https://github.com/luca-veronelli-stem/button-panel-tester/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-Proprietary-red)](#license)

> **Bench tool to test STEM button-panel hardware over CAN.**
> **Standard:** v1.5.3 — see [`docs/Standards/`](./docs/Standards/).

---

## Overview

ButtonPanelTester is a bench tool that exercises STEM button-panel hardware over CAN. The user-visible surface today is a single window whose **dictionary status row** reports the provenance, age, and health of the loaded variable dictionary (live, cached, or extracted from an embedded seed) and offers a one-click **Refresh** against a remote `stem-dictionaries-manager`. A first-launch **registration ceremony** swaps a short-lived bootstrap token for a long-lived per-installation API credential stored under DPAPI, so the operator authenticates the tool once per machine and the credential is rotated atomically server-side on any subsequent Re-register. See [`specs/001-fetch-dictionary/quickstart.md`](specs/001-fetch-dictionary/quickstart.md) for the end-to-end operator walkthrough.

## Quick Start

```powershell
dotnet build
dotnet test
dotnet run --project src/ButtonPanelTester.GUI
```

## Solution Structure

```
src/
├── ButtonPanelTester.Core/                domain types + ports
├── ButtonPanelTester.Services/            use cases
├── ButtonPanelTester.Infrastructure/      adapters (EF Core, drivers, IO)
└── ButtonPanelTester.GUI/                 Avalonia + FuncUI
tests/
└── ButtonPanelTester.Tests/               xUnit + FsCheck + Avalonia.Headless
specs/                           Spec-Driven Development (spec-kit) feature folders (optional)
lean/                            Lean 4 workspace (optional — lakefile.lean + lean-toolchain)
docs/                            documentation (Standards/ tracked here)
eng/                             build / release scripts
```

## Documentation

- Dictionary fetch & registration walkthrough: [`specs/001-fetch-dictionary/quickstart.md`](./specs/001-fetch-dictionary/quickstart.md).
- Standards followed: [`docs/Standards/`](./docs/Standards/) — pinned to `v1.5.3`.
- Changelog: [`CHANGELOG.md`](./CHANGELOG.md).
- Repo-specific notes: [`CLAUDE.md`](./CLAUDE.md).

## License

- **Owner:** STEM E.m.s.
- **Author:** Luca Veronelli
- **Creation Date:** 2026
- **License:** Proprietary — All rights reserved.
