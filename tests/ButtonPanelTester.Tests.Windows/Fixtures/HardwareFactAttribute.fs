namespace Stem.ButtonPanelTester.Tests.Windows.Fixtures

open System
open Xunit

/// `[<Fact>]` variant that runs only when the environment variable
/// `BPT_HARDWARE=1` is set; otherwise the test is skipped with a clear
/// remediation reason. This makes the hardware E2E suite runnable on a
/// bench rig with no source edits (`$env:BPT_HARDWARE=1; dotnet test …`)
/// while staying dormant on dev boxes and CI where the variable is unset.
///
/// Pair every case with `[<Trait("Category", "Hardware")>]` so the
/// standards `dotnet-ci.yml` category filter (`Category!=Hardware`, the
/// default since standards#113 / `@v1.12.0`) also excludes it at discovery
/// time — the env gate and the trait filter are complementary, not
/// redundant: the trait keeps CI from listing the case, the env gate keeps
/// it from running (and failing) on an adapter-less dev box.
///
/// Tracked by [#112](https://github.com/luca-veronelli-stem/button-panel-tester/issues/112)
/// (bench-config docs) and [#142](https://github.com/luca-veronelli-stem/button-panel-tester/issues/142)
/// (this attribute).
type HardwareFactAttribute() as this =
    inherit FactAttribute()

    do
        if Environment.GetEnvironmentVariable("BPT_HARDWARE") <> "1" then
            this.Skip <-
                "Hardware required. Set BPT_HARDWARE=1 (PowerShell: $env:BPT_HARDWARE=\"1\") "
                + "on a bench rig with a PEAK PCAN-USB adapter plugged in and the driver installed."
