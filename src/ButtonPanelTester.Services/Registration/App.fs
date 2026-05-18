namespace Stem.ButtonPanelTester.Services.Registration

open System.Threading
open System.Threading.Tasks
open Stem.ButtonPanelTester.Core.Dictionary

/// Startup-time orchestration helpers for the registration ceremony,
/// per `specs/001-fetch-dictionary/spec.md` §US2 (FR-014, FR-017).
///
/// The single helper here, `tryRegister`, decides whether the GUI
/// shell should open the registration dialog on launch. Extracted to
/// `Services` (`net10.0`) so the `T048` integration test
/// (`tests/ButtonPanelTester.Tests/Integration/RegistrationFlowTests.fs`,
/// `net10.0`) can exercise the orchestration through
/// `InMemoryCredentialStore` + `InMemoryRegistrationClient` without
/// reaching the `net10.0-windows` GUI shell.
///
/// The GUI side wires the `runDialog` callback to construct a
/// `RegistrationDialogWindow` and `ShowDialog(owner)` it modally; the
/// window's `OutcomeTask` is the `Task<RegistrationOutcome>` returned
/// to the orchestration here.
[<RequireQualifiedAccess>]
module App =

    /// First-launch registration check.
    ///
    ///   - When the credential store already has a credential
    ///     (`ICredentialStore.ExistsAsync` returns `true`), returns
    ///     `RegistrationOutcome.Skipped` immediately and does NOT
    ///     invoke `runDialog` (FR-017 — no re-prompts on subsequent
    ///     launches).
    ///   - Otherwise invokes `runDialog`, which is expected to drive
    ///     the registration UI / wire the bootstrap-token exchange to
    ///     completion and yield a `Task<RegistrationOutcome>`. The
    ///     returned outcome is forwarded verbatim.
    ///
    /// The retry-on-error loop (Failed → fix-and-resubmit) lives
    /// inside the dialog itself; this helper does not re-invoke
    /// `runDialog` after a `Dismissed` outcome. The technician's
    /// dismissal is final for the current launch — the tool proceeds
    /// with the seeded dictionary loaded by US1 (edge case "No
    /// credential, no network" in `spec.md`).
    let tryRegister
        (credentialStore: ICredentialStore)
        (runDialog: unit -> Task<RegistrationOutcome>)
        (ct: CancellationToken)
        : Task<RegistrationOutcome> =
        task {
            let! existing = credentialStore.ExistsAsync(ct)

            if existing then
                return Skipped
            else
                return! runDialog ()
        }
