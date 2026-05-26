# Stopgap: vendored `Infrastructure.Protocol` C# stack

Per Constitution Principle VI (Stopgap Discipline).

## Violated principle

STEM **LANGUAGE** standard (F# default).

## Rationale

No F#-native CAN stack exists. The legacy `stem-communication` library
has 84 open issues (recorded in `docs/Context/bpt-rollout/CORRECTIONS.md`
section C4). Re-implementing in F# would discard roughly two years of
upstream production hardening in `stem-device-manager`'s
`Infrastructure.Protocol` for no near-term return.

## Removal path

Replace with the `Stem.Communication` NuGet once
`stem-device-manager` Phase 5 validates the package in production.

## Tracking issue

https://github.com/luca-veronelli-stem/button-panel-tester/issues/111

## Vendor manifest

See `src/ButtonPanelTester.Infrastructure.Protocol/VENDOR.md`.

## See also

[`stem-communication` — CAN connection state model](https://github.com/luca-veronelli-stem/stem-communication/blob/main/Docs/CAN-CONNECTION-STATE-MODEL.md):
living design note covering the layered CAN connection model used
across STEM apps, the current divergence between this repo's
`CanLinkState` (presentation layer) and `stem-communication`'s
`ConnectionState` (session layer), and the open questions to resolve
before the migration tracked in
[#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111)
starts.