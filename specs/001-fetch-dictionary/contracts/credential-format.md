# Contract: On-disk credential format

**Owner**: `Stem.ButtonPanelTester.Infrastructure.Persistence.DpapiCredentialStore`
**Consumers**: `DpapiCredentialStore` (read/write/delete).

---

## File

```
%LOCALAPPDATA%\Stem\ButtonPanelTester\credentials\credential.dpapi
```

Single file. Binary. Existence indicates "this installation is registered". Absence indicates "first launch — show the registration dialog". No sidecar.

## Format

The file is the raw DPAPI ciphertext of the UTF-8 bytes of the `InstallationCredential.Value` string, produced by:

```csharp
byte[] ciphertext = ProtectedData.Protect(
    userData: Encoding.UTF8.GetBytes(credential.Value),
    optionalEntropy: null,
    scope: DataProtectionScope.CurrentUser);
File.WriteAllBytes(path, ciphertext);
```

Decryption is the inverse:

```csharp
byte[] plaintext = ProtectedData.Unprotect(
    encryptedData: File.ReadAllBytes(path),
    optionalEntropy: null,
    scope: DataProtectionScope.CurrentUser);
return Encoding.UTF8.GetString(plaintext);
```

## Properties guaranteed by the format

- **Per-user, per-machine confidentiality**: the ciphertext can only be decrypted by the same Windows user account on the same physical machine that wrote it. Attempting `Unprotect` from a different account or on a different machine throws `CryptographicException`.
- **No key management on the application side**: DPAPI uses the OS-managed user master key. The application stores zero key material.
- **Atomic-ish writes**: `File.WriteAllBytes` is "atomic enough" for our purposes (a half-written ciphertext fails `Unprotect` and we treat the file as missing — see read path below).

## Read path

```fsharp
DpapiCredentialStore.LoadAsync ct =
    if not (File.Exists path) then
        return None
    try
        let plaintext = ProtectedData.Unprotect(File.ReadAllBytes path, null, CurrentUser)
        return Some (InstallationCredential.Create (Encoding.UTF8.GetString plaintext))
    with
    | :? CryptographicException as ex ->
        // Could not decrypt — ciphertext is corrupt, or written by a
        // different user / machine. Treat as missing; the registration
        // dialog will appear and the user supplies a fresh BootstrapToken.
        logger.LogWarning(ex, "Failed to decrypt credential.dpapi; treating as absent.")
        return None
```

The `CryptographicException` path is the failure mode for "the user copied their installation directory to a new machine" or "another user account stole the file". In both cases we surface as "no credential" — the system's normal first-launch path.

## Write path

```fsharp
DpapiCredentialStore.SaveAsync (credential, ct) =
    Directory.CreateDirectory (Path.GetDirectoryName path)
    let plaintext = Encoding.UTF8.GetBytes credential.Value
    let ciphertext = ProtectedData.Protect(plaintext, null, CurrentUser)
    let tmp = path + ".tmp"
    File.WriteAllBytes(tmp, ciphertext)
    File.Move(tmp, path, overwrite = true)
```

Temp+rename pattern, same as the cache.

## Delete path

```fsharp
DpapiCredentialStore.DeleteAsync ct =
    if File.Exists path then File.Delete path
```

Used only by re-registration's atomic-overwrite (R11 in research.md): the new credential is saved first; if the save succeeds and overwrites the old file, no separate delete is needed. `DeleteAsync` exists for tests and emergency recovery.

## Logging

- Writes emit `LogInformation("Saved installation credential to {Path}.", path)`.
- Reads emit nothing on success (per LOGGING standard — successful steady-state operations are silent).
- Any `CryptographicException` is `LogWarning` with the exception (sanitised — exception type + message, no stack trace details that include paths the user would care about).
- The plaintext credential **never** appears in any log statement at any verbosity level.

## What this format does **not** provide

- No expiry — the file lives indefinitely until the user re-registers (per FR-022).
- No rotation key — DPAPI's key rotation is OS-managed and transparent.
- No salt — the per-user-per-machine scoping is sufficient for FR-016. We pass `optionalEntropy: null`.
