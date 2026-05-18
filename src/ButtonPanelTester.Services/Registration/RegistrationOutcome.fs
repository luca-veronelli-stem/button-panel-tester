namespace Stem.ButtonPanelTester.Services.Registration

open Stem.ButtonPanelTester.Core.Dictionary

/// Outcome of the first-launch registration ceremony as observed by
/// the GUI startup orchestration (`App.tryRegister`, T043 / T048).
///
///   - `Completed credential` — the technician submitted a valid
///     bootstrap token, the server issued an installation credential,
///     and `ICredentialStore.SaveAsync` succeeded. The credential is
///     carried alongside so the orchestration helper can verify the
///     persisted state without re-reading the store immediately.
///   - `Skipped` — a credential was already present on launch; no
///     dialog opened, the technician proceeded straight to the main
///     window (FR-017).
///   - `Dismissed` — the dialog appeared and the technician closed it
///     without a successful registration (close button, ESC, or a
///     persistent failure they chose not to retry). The tool continues
///     with the seeded dictionary already loaded by US1 (edge case
///     "No credential, no network" in `spec.md`).
///
/// Lives in `Services` (`net10.0`) so the orchestration helper and
/// the `T048` integration test can both reference the same type
/// without crossing the `net10.0-windows` Infrastructure / GUI
/// boundary.
type RegistrationOutcome =
    | Completed of credential: InstallationCredential
    | Skipped
    | Dismissed
