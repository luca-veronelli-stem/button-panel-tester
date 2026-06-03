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

/// `[<Fact>]` variant for tests that need a **human acting mid-run** (e.g.
/// physically unplugging/replugging the adapter on a prompt), gated behind
/// `BPT_HARDWARE_INTERACTIVE=1`. A strictly stronger precondition than
/// [`HardwareFactAttribute`](#): it implies an attended bench session, so it
/// is kept on its OWN variable — never run these unattended (they would hang
/// waiting for the operator) and never on CI.
///
/// Reserve this for assertions a fixture or a fake cannot make: validating an
/// emergent, undocumented third-party behaviour against real hardware (e.g.
/// the vendored stack's autonomous reconnect on replug). The state-machine
/// *logic* belongs in fake-driven unit tests, not here. See
/// [#142](https://github.com/luca-veronelli-stem/button-panel-tester/issues/142).
type ManualHardwareFactAttribute() as this =
    inherit FactAttribute()

    do
        if Environment.GetEnvironmentVariable("BPT_HARDWARE_INTERACTIVE") <> "1" then
            this.Skip <-
                "Attended hardware test. Set BPT_HARDWARE_INTERACTIVE=1 "
                + "(PowerShell: $env:BPT_HARDWARE_INTERACTIVE=\"1\") and run it yourself "
                + "on a bench rig — it prompts you to unplug/replug the adapter mid-run."
