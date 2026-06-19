# Standard: CLIENT_REGISTRATION

> **Stability:** v1.16.0
> **Principle:** an app that needs a per-installation API credential obtains it **once**, through a bootstrap-token exchange against an unauthenticated `POST /register`, then stores it **encrypted-at-rest behind a port** and replays it as an `X-Api-Key` header on every authenticated call. Machine and user identifiers are **hashed** before they cross an organizational boundary. The credential plaintext is never logged at any level, never displayed after first capture, and never written anywhere but the encrypted store.

## When this applies

A client app authenticates to a STEM-operated HTTP service with a credential that is **per installation** and therefore cannot be shipped in source or `appsettings.json`. The credential is provisioned at first launch from a short-lived bootstrap token a technician supplies, exchanged for a long-lived installation credential.

Three consumers drove this standard:

| Repo | Status | `clientApp` |
| --- | --- | --- |
| `button-panel-tester` | **reference implementation** — `specs/001-fetch-dictionary` | `ButtonPanelTester` |
| `stem-device-manager` | [#94](https://github.com/luca-veronelli-stem/stem-device-manager/issues/94) — currently a gitignored-`appsettings.Production.json` stopgap this standard replaces | — |
| `telemetry-manager` | planned greenfield CLI host | `TelemetryManager` |

This standard covers the **registration exchange + credential storage + authenticated-call** mechanics. The observable seed→cache→live *resource* fallback (degraded-mode-as-visible-state) that the reference implementation pairs with registration is an **adjacent concern with a single consumer so far** and is deliberately *not* specified here — it has not yet cleared the cross-repo bar (see [`MIGRATION.md`](./MIGRATION.md) → "Choosing the bump level"). When a second consumer earns it, it gets its own standard.

## The shape

```
bootstrap token (technician)
   │  POST /register   (unauthenticated, one-shot, not idempotent)
   ▼
installation credential  ──►  ICredentialStore  (encrypted-at-rest, indefinite)
                                     │
                                     │ loaded per request
                                     ▼
                              X-Api-Key handler  ──►  authenticated data calls
```

Each arrow is a port (`IRegistrationClient`, `ICredentialStore`, `IInstallationDescriptorProvider`); the live HTTP and OS-crypto adapters are the only types that name the wire or the platform.

## The bootstrap exchange — `POST /register`

```
POST {Service:BaseUrl}/register
```

- **Unauthenticated.** The path is `/register`, **not** `/api/register` — it predates and sits outside the `X-Api-Key` middleware. It is the only unauthenticated call the client makes.
- **Request headers:** `Content-Type: application/json; charset=utf-8`, `Accept: application/json`, `User-Agent: <Stem.AppName>/<assemblyVersion>`. No `X-Api-Key` (the whole point is that we don't have one yet).
- **Request body:** the bootstrap token plus the installation descriptor.

  ```json
  {
    "bootstrapToken": "<opaque, validated client-side only for trim + non-empty>",
    "descriptor": {
      "clientApp":   "<fixed per-consumer key the server's policy registry looks up>",
      "osUserId":    "<SHA-256 hex of the OS user identifier>",
      "machineId":   "<SHA-256 hex of the machine identifier>",
      "installGuid": "<per-installation GUID, non-zero>",
      "appVersion":  "<SemVer 2.0, optional>"
    }
  }
  ```

- **Successful response (`200`):** `{ installationId, apiCredential, issuedAt }`. The client takes `apiCredential` **verbatim** (the server is the sole authority on its shape — no client-side validation), hands it to the credential store for encryption, and never touches the plaintext again. `installationId` / `issuedAt` are forward-compat surface, not consumed in v1.
- **Timeout:** a single client-side timeout, **uniform** with the app's authenticated data calls (one user expectation for "the service is slow"). The reference uses a linked-CTS 90 s timeout sized to absorb cold-start latency on a Free-tier App Service.
- **No retries.** A technician is at the keyboard; retry is a second button press, not a wire-client decision.
- **Not idempotent.** A second `POST /register` with the same token returns `409`. The first `200` is the only chance to capture the plaintext credential; mid-call network failure is recovered operator-side (admin revokes any created installation and mints a fresh token).

## Installation descriptor — privacy posture

The descriptor identifies the installation without leaking raw machine or user identity.

- **Hash before crossing an organizational boundary.** `osUserId` and `machineId` are the **lowercase SHA-256 hex digest of the UTF-8 bytes** of the raw value (64 chars, `[0-9a-f]`). Hashing is **mandatory** whenever the client and server sit on opposite sides of an org boundary (a supplier-deployed client reporting to STEM); it is the recommended default everywhere else. A raw Windows SID or raw machine UUID **MUST NOT** cross the wire. The hash's collision resistance is sufficient as a per-machine / per-user fingerprint for revocation and forensics.
- **Identifier sources (Windows adapter):** the OS user from `WindowsIdentity.GetCurrent().User`; the machine identity from `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid` (preferred over `Win32_ComputerSystemProduct.UUID` because it avoids the `System.Management`/WMI dependency). Both are hashed at construction — the raw values never leave the provider.
- **`installGuid`** is a non-zero per-installation `Guid`, generated on first launch and persisted as an `install.guid` sidecar next to the credential (under the `credentials/` folder per [`APP_DATA.md`](./APP_DATA.md)). It is **paired with the credential and rotated together** by any re-register flow, so the provider re-reads the sidecar on every descriptor build (host facts stay cached; only the GUID is re-read).
- **`appVersion`** is optional and, when present, a SemVer 2.0 string from `AssemblyInformationalVersionAttribute`.
- **`clientApp`** is a fixed per-consumer key. The descriptor *type* does not enforce the hashed form — enforcement lives in the provider so tests construct deterministic stubs with literal digests.

## Error taxonomy — closed, typed, surfaced

Registration failure is modelled as a **closed discriminated union returned as `Result`**, never thrown for expected failures and never swallowed. The HTTP status — not the response body — selects the case:

| HTTP / wire outcome | Error case | Meaning |
| --- | --- | --- |
| `200` | `Ok credential` | success |
| `400` | `DescriptorRejected detail` | malformed / missing-field / zero-`installGuid` / empty-token — a **client bug**; no technician action helps. `detail` is the server's body, for **logs only**. |
| `401` | `TokenInvalid` | token unknown **or** scope mismatch **or** `clientApp` policy-lookup miss — deliberately conflated server-side (don't try to disambiguate). |
| `409` | `TokenAlreadyUsed` | token already consumed; re-register needs admin revocation + a fresh token. |
| `410` | `TokenExpired` | token TTL elapsed. |
| `423` | `TokenRevoked` | token administratively revoked. |
| other `5xx` | `ServerError httpStatus` | tolerate the broad range to avoid lock-step coupling with the server's status set. |
| `HttpRequestException` | `Network Unreachable` | off-the-wire failure. |
| client timeout | `Network Timeout` | the uniform client-side timeout fired. |

- **The server's `error` body is never shown to the user.** It is a developer hint, not a token-validity oracle. The UI renders a **curated, per-case message**; the body goes to logs only.
- **The 401 conflation is intentional** (defense-in-depth — it hides which apps a token was scoped to; the token's entropy makes enumeration infeasible regardless). The honest consequence is that `TokenInvalid` is genuinely ambiguous, and the user-facing copy should match that ("token not accepted — check it or contact us") rather than over-claim a cause.

This taxonomy follows [`ERROR_HANDLING.md`](./ERROR_HANDLING.md): the failures are routine, contract-level outcomes the caller branches on, so they are a return value, not an exception.

## Credential storage

Behind the `ICredentialStore` port:

- **Per-user, per-machine encryption-at-rest.** On Windows: `ProtectedData.Protect` with `DataProtectionScope.CurrentUser` and `optionalEntropy: null`. The ciphertext decrypts only for the same Windows account on the same machine; the app stores **zero** key material (the OS owns the master key). A non-Windows adapter substitutes the platform-equivalent per-user keystore behind the same port.
- **Atomic writes:** write ciphertext to a `.tmp` sibling, then `File.Move(overwrite: true)`. A crash mid-write leaves the prior file intact.
- **Existence is the registration flag.** File present ⇒ registered; absent ⇒ first launch (show the registration affordance).
- **Decrypt failure is treated as absent.** A `CryptographicException` (corrupt file, or copied to another user/machine) is logged at `Warning` and surfaced as "no credential" — the normal first-launch path — never thrown.
- **Indefinite validity.** No expiry, no client-side rotation key. The credential lives until a re-register replaces it; a live `401` on a data call is what signals "re-register".
- **Location:** the `credentials/` sub-folder of the app's `APP_DATA` root — see [`APP_DATA.md`](./APP_DATA.md).

## Authenticated calls — the `X-Api-Key` handler

Authenticated data calls carry the credential as a per-request `X-Api-Key` header, injected by a `DelegatingHandler` composed into the named `HttpClient`'s pipeline so the data-fetch adapter never sees the credential:

- The handler loads the credential from the store **per request** (the decrypt is cheap and avoids stale state after a mid-process re-register), and adds the header when present.
- **Missing credential ⇒ forward without the header**, do not short-circuit. The server replies `401`, which the data path maps to a visible "re-register" state. The handler does not own the "no credential ⇒ unauthorized" contract — the server does — and surfacing the live `401` keeps the wire and the in-process state machine in lockstep.

## Ports

The three ports below are the seam between the live system and the rest of the app. Adapters are wired once at the composition root; `Core`/`Services` name only the interfaces, and tests substitute in-memory fakes (no mocking library — per the STEM testing posture).

```fsharp
type ICredentialStore =
    abstract member ExistsAsync : ct: CancellationToken -> Task<bool>
    abstract member LoadAsync   : ct: CancellationToken -> Task<InstallationCredential option>
    abstract member SaveAsync   : credential: InstallationCredential * ct: CancellationToken -> Task
    abstract member DeleteAsync : ct: CancellationToken -> Task

type IRegistrationClient =
    abstract member RegisterAsync :
        token: BootstrapToken * ct: CancellationToken
        -> Task<Result<InstallationCredential, RegistrationError>>

type IInstallationDescriptorProvider =
    abstract member Current          : unit -> InstallationDescriptor
    abstract member ResetInstallGuid : unit -> unit
```

- Async methods take a `CancellationToken` and return `Task<_>` (uniform F#/C# consumption) and observe the token per [`CANCELLATION.md`](./CANCELLATION.md).
- The DPAPI store and the descriptor provider are **Windows-only** and live in a `net10.0-windows` adapter project; the ports and the consuming services stay platform-neutral, per [`PORTABILITY.md`](./PORTABILITY.md) (`MODULE_SEPARATION` keeps the platform calls out of `Core`/`Services`).
- `ResetInstallGuid` deletes the `install.guid` sidecar so the next `RegisterAsync` is treated as a fresh installation server-side; it is idempotent.

## Host shape — GUI vs CLI

The same ports back two front-ends:

- **GUI host (proven — `button-panel-tester`):** a modal first-run dialog collects the bootstrap token, drives one `RegisterAsync`, and renders the per-case error message inline. The dialog stays open on a recoverable failure so the technician can correct the token and retry.
- **CLI host (planned — `telemetry-manager`):** a `register` verb takes the token via argument, stdin, or environment variable; a separate environment-variable **credential override** lets CI/bench runs supply a credential without the interactive exchange. The override is a deliberate, documented escape hatch — it is never a way to ship a credential in source.

Both shapes sit behind `IRegistrationClient` + `ICredentialStore`; only the front-end differs.

## F#

- **Secrets are single-case DUs with a `private` constructor + a smart constructor.** `BootstrapToken` validates (trim + non-empty) via `TryCreate` returning `Result`; `InstallationCredential.Create` is total (the server owns its shape). Both follow [`VISIBILITY.md`](./VISIBILITY.md) (DU-case visibility) and the [`ERROR_HANDLING.md`](./ERROR_HANDLING.md) smart-constructor pattern.
- **Never log a secret DU instance.** F#'s default `ToString` renders `BootstrapToken "<value>"` and would leak the plaintext to any structured sink. Pass only derived metadata (length, hash prefix) when diagnostics are unavoidable — see [`LOGGING.md`](./LOGGING.md).
- **`RegistrationError` is a closed DU.** A closed DU that is load-bearing for behavior warrants a Lean theorem + FsCheck property pairing when implemented (the F# closed-DU discipline; see [`TESTING.md`](./TESTING.md) for the FsCheck side). The reference implementation has not yet added it, so don't cite a proof that doesn't exist — add the pairing with the code, not retroactively in the standard.
- **`task { }` CE, linked-CTS timeout.** The registration call links the caller's token with a timeout CTS; only an `OperationCanceledException` originating from the **caller's** token is allowed to leak (a timeout maps to `Network Timeout`). `reraise()` is unavailable after an `await` inside `task { }` — capture with `ExceptionDispatchInfo` if rethrow is needed (see [`CANCELLATION.md`](./CANCELLATION.md)).

## Forbidden

- A raw OS user identifier or raw machine identifier on the wire.
- The credential plaintext in any log statement at any level, in the UI after the registration surface closes, or on disk in anything but the encrypted store.
- Showing the server's raw `error` body to the user (logs only).
- Throwing for an expected registration failure, or silently swallowing the error DU.
- Retry storms on `POST /register` (one human-driven attempt at a time; not idempotent).
- Shipping a credential in source or `appsettings*.json` — this standard exists to retire that stopgap ([stem-device-manager#94](https://github.com/luca-veronelli-stem/stem-device-manager/issues/94)).

## What this means in practice

- A new consumer implements three adapters (registration client, credential store, descriptor provider), wires them at the composition root, and picks a host shape — it does **not** re-derive the wire contract, the error taxonomy, the privacy posture, or the storage format. Copy the reference with a citation until a shared package earns its keep.
- The credential lives under `APP_DATA`'s `credentials/`; the install-GUID sidecar lives beside it and rotates with it.
- "Degraded mode" for the **data** the credential unlocks (seed/cache/live fallback) is a separate concern, intentionally out of scope here.

## Reference implementation

`button-panel-tester` `main`, feature `specs/001-fetch-dictionary`:

- Contracts: `contracts/registration-api.md`, `contracts/credential-format.md`, `contracts/ports.md`.
- Adapters: `src/ButtonPanelTester.Infrastructure/Http/HttpRegistrationClient.fs`, `.../Http/ApiKeyAuthHandler.fs`, `.../Persistence/DpapiCredentialStore.fs`, `.../Auth/InstallationDescriptorProvider.fs`.
- Types: `src/ButtonPanelTester.Core/Dictionary/RegistrationTypes.fs` (`BootstrapToken`, `InstallationCredential`, `RegistrationError`), `.../InstallationDescriptor.fs`.

The server side of the contract is owned by `stem-dictionaries-manager` (`specs/001-bootstrap-registration/contracts/register.md`); this standard captures the **consumer** contract.
