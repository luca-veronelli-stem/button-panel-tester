# Contract: Vendor manifest discipline

**Phase 1 output for**: [../plan.md](../plan.md)
**Concerns**: Constitution Principle VI (Stopgap Discipline) — the vendored `Infrastructure.Protocol` C# stack

Defines the discipline for the spec-002 vendor copy of `stem-device-manager` into `src/ButtonPanelTester.Infrastructure.Protocol/`. The vendoring rules below are derived from the discipline originally documented in `docs/Context/bpt-rollout/02-vendoring-plan.md` and adapted to the source-switch decision in CORRECTIONS.md §C4.

## `VENDOR.md`

Lives at `src/ButtonPanelTester.Infrastructure.Protocol/VENDOR.md`. Required structure:

```markdown
# VENDOR.md — Infrastructure.Protocol

**Upstream**: `git@github.com:luca-veronelli-stem/stem-device-manager.git`
**Pinned SHA**: `<40-char commit SHA at vendor time>`
**Vendored on**: `<ISO 8601 date>`
**Vendored by**: `<author>` via `<PR URL>`

## Manifest

| Upstream path                                       | Local path                                          | LOC | Last verified |
|-----------------------------------------------------|-----------------------------------------------------|-----|---------------|
| Core/Interfaces/ICommunicationPort.cs               | Core/Interfaces/ICommunicationPort.cs               | 42  | <date>        |
| Core/Interfaces/IPacketDecoder.cs                   | Core/Interfaces/IPacketDecoder.cs                   | 28  | <date>        |
| Core/Models/Command.cs                              | Core/Models/Command.cs                              | 18  | <date>        |
| ...                                                 | ...                                                 | ... | ...           |

(Full list of ~24 files; total ≈ 2,686 LOC per [../research.md](../research.md) R1.)

## Local modifications

| File                       | Lines     | Why                                                                                                  | Upstream PR  |
|----------------------------|-----------|------------------------------------------------------------------------------------------------------|--------------|
| Hardware/PCANManager.cs    | +18, -2   | Add CancellationTokenSource + IAsyncDisposable for clean shutdown (per CORRECTIONS.md §C4 follow-up)| <URL>        |

## Removal path

Replace this vendored copy with the `Stem.Communication` NuGet once `stem-device-manager`'s
Phase 5 migration completes. Tracking issue: <GitHub issue URL>.
```

## Vendoring rules

1. **Verbatim copy.** Files cross from upstream to local with no edits except those recorded in the "Local modifications" table.
2. **One vendor commit.** All vendored files land in a single commit on the vendor branch. The commit body cites the pinned SHA. Bisect-safe per the standard rule.
3. **Frozen.** No fix-in-place. If upstream gets a fix we want, re-vendor (update pinned SHA, update manifest, second commit). Local modifications are bounded by the manifest's modifications table.
4. **Pre-commit hash check.** A pre-commit hook (or CI job) computes SHA-256 over every file in `src/ButtonPanelTester.Infrastructure.Protocol/` (excluding `VENDOR.md` and `VENDOR.sha256` themselves) and compares against the sidecar `VENDOR.sha256`. Drift fails CI loudly.
5. **No re-namespacing.** Files keep their upstream namespace (`Stem.DeviceManager.Core.Interfaces`, etc.) unless a collision forces a rename — in which case the rename is documented in the manifest. The F# adapters in `ButtonPanelTester.Infrastructure/Can/` import from the upstream namespaces directly.
6. **No vendored tests.** The upstream test suite stays upstream; the tester's coverage of the vendored code is the responsibility of the tester's own integration tests running through the F# ports (`InMemoryCanLink` + `InMemoryCanFrameStream` for CI; hardware E2E for the manual bench check).

## Stopgap waiver

`docs/STOPGAP_VENDORED_PROTOCOL_STACK.md` lands in the same commit as `VENDOR.md`. Required fields per Constitution Principle VI:

- **Violated principle**: STEM **LANGUAGE** standard (F# default).
- **Rationale**: no F#-native CAN stack exists; the legacy `stem-communication` library has 84 open issues (recorded in `CORRECTIONS.md` §C4); re-implementing in F# wastes ~2 years of upstream production hardening.
- **Removal path**: `Stem.Communication` NuGet, once `stem-device-manager` Phase 5 validates the package in production.
- **Tracking issue**: one issue per Constitution Principle VI rule #4 ("one issue per bypass"). Opens with the plan PR.

## Re-vendoring procedure

When upstream lands a fix or spec-002+ needs:

1. Open a `chore/revendor-protocol-stack-<short>` branch.
2. Update `VENDOR.md`'s pinned SHA. Re-copy every file in the manifest from the new upstream SHA.
3. If the upstream API surface changed, update `PcanCanLink` and `PcanCanFrameStream` adapters to match. **Keep the F# port contracts (`ICanLink`, `ICanFrameStream`) stable** — the port surface is what insulates the tester from upstream churn.
4. Regenerate `VENDOR.sha256`.
5. Run the test suite (unit + property + integration; hardware E2E if a real adapter is available).
6. PR with three bisect-safe commits: (a) the re-vendor commit, (b) the adapter delta, (c) the sidecar update + `VENDOR.md` SHA bump.

## Why this exists

Without the manifest discipline, a vendored copy rots in two ways: silent local edits (someone "just fixes a typo" without recording it, then upstream churn makes re-vendoring impossible) and silent drift from upstream (the pinned SHA becomes ancient and the local copy stops resembling anything anyone supports). The pre-commit hash check forecloses silent edits; the manifest forecloses silent drift. Together they keep the stopgap honest until the `Stem.Communication` NuGet migration retires it.
