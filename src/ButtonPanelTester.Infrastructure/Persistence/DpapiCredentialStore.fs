namespace Stem.ButtonPanelTester.Infrastructure.Persistence

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Stem.ButtonPanelTester.Core.Dictionary

/// Production adapter for `ICredentialStore`, per
/// `specs/001-fetch-dictionary/contracts/ports.md` §ICredentialStore
/// and `credential-format.md`. Manages the single
/// `credential.dpapi` file under the supplied
/// `credentialDirectory` with atomic temp+rename writes and per-user
/// DPAPI encryption-at-rest.
///
/// Constructor parameters:
///   - `credentialDirectory` — the directory holding `credential.dpapi`.
///     The adapter creates it on first write (idempotent
///     `Directory.CreateDirectory`). Production binding is
///     `%LOCALAPPDATA%\Stem\ButtonPanelTester\credentials\` per STEM
///     `APP_DATA.md` (v1.9.0), wired via `StemAppData.credentialsDir ()`
///     at the composition root; tests pass a temp directory.
///   - `logger` — required `ILogger<DpapiCredentialStore>` (archetype A
///     per STEM LOGGING standard). Emits `Information` on save and
///     `Warning` (with the exception) on `CryptographicException`
///     during load. The plaintext credential value never appears at
///     any log level — only the file path and the exception itself.
///
/// File format: raw DPAPI ciphertext of the UTF-8 bytes of
/// `InstallationCredential.Value`, produced via
/// `ProtectedData.Protect` with `DataProtectionScope.CurrentUser` and
/// `optionalEntropy: null`. Decrypt is the inverse on read; a
/// `CryptographicException` (corrupt ciphertext, different user, or
/// different machine) surfaces as `None` after a `Warning` log entry,
/// which routes the caller to the registration dialog's normal
/// first-launch path.
///
/// Crash safety: `SaveAsync` writes ciphertext to `credential.dpapi.tmp`
/// then `File.Move(overwrite = true)` to commit (atomic per file). A
/// kill between `Protect` and the move leaves the previous file
/// intact (or no file at all on a clean machine — also fine).
type DpapiCredentialStore
    (credentialDirectory: string, logger: ILogger<DpapiCredentialStore>) =

    let credentialPath = Path.Combine(credentialDirectory, "credential.dpapi")

    interface ICredentialStore with

        member _.ExistsAsync(_: CancellationToken) =
            task { return File.Exists(credentialPath) }

        member _.LoadAsync(ct: CancellationToken) =
            task {
                if not (File.Exists(credentialPath)) then
                    return None
                else
                    try
                        let! ciphertext = File.ReadAllBytesAsync(credentialPath, ct)
                        let plaintext =
                            ProtectedData.Unprotect(
                                ciphertext,
                                null,
                                DataProtectionScope.CurrentUser
                            )
                        let value = Encoding.UTF8.GetString(plaintext)
                        return Some(InstallationCredential.Create value)
                    with :? CryptographicException as ex ->
                        logger.LogWarning(
                            ex,
                            "Failed to decrypt credential at {Path}; treating as absent.",
                            credentialPath
                        )
                        return None
            }

        member _.SaveAsync(credential: InstallationCredential, ct: CancellationToken) : Task =
            task {
                Directory.CreateDirectory(credentialDirectory) |> ignore
                let plaintext = Encoding.UTF8.GetBytes(credential.Value)
                let ciphertext =
                    ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser)
                let tmp = credentialPath + ".tmp"
                do! File.WriteAllBytesAsync(tmp, ciphertext, ct)
                File.Move(tmp, credentialPath, overwrite = true)
                logger.LogInformation(
                    "Saved installation credential to {Path}.",
                    credentialPath
                )
            }

        member _.DeleteAsync(_: CancellationToken) : Task =
            task {
                if File.Exists(credentialPath) then
                    File.Delete(credentialPath)
            }
