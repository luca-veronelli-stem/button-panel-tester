# VENDOR-GUARD — read before touching the C# protocol stack

The F# adapters that will land here (spec-002 PR-B onwards) reference the
**frozen** C# project at
[`../../ButtonPanelTester.Infrastructure.Protocol/`](../../ButtonPanelTester.Infrastructure.Protocol/).
That project is a verbatim vendor copy of `stem-device-manager` pinned to a
specific upstream SHA.

**Do not edit the vendored files in place.** If you need an upstream fix or a
new type from the upstream stack, follow the re-vendoring procedure in
[`specs/002-can-link-lifecycle/contracts/vendor-manifest.md`](../../../specs/002-can-link-lifecycle/contracts/vendor-manifest.md)
section "Re-vendoring procedure":

1. Update the pinned SHA in
   [`ButtonPanelTester.Infrastructure.Protocol/VENDOR.md`](../../ButtonPanelTester.Infrastructure.Protocol/VENDOR.md).
2. Re-run `eng/vendor-protocol-stack.ps1` against the new SHA.
3. Re-apply any rows from the `VENDOR.md` "Local modifications" table that
   are still needed (the script's re-copy wipes them).
4. Regenerate `VENDOR.sha256` (`eng/vendor-protocol-stack.ps1 -RehashOnly`).
5. Update this folder's adapters if the upstream API surface changed — keep
   the F# port contracts (`ICanLink`, `ICanFrameStream`) stable. The port
   surface is what insulates the tester from upstream churn.

The stopgap is tracked by [#111](https://github.com/luca-veronelli-stem/button-panel-tester/issues/111)
and goes away when the `Stem.Communication` NuGet retires the vendored copy.
