namespace Stem.ButtonPanelTester.Core.Dictionary

open System

/// Bootstrap token entered by the operator at registration time,
/// per `specs/001-fetch-dictionary/data-model.md` §1.4. The single-
/// case DU has a `private` constructor so callers cannot fabricate
/// values from arbitrary strings — they must go through
/// `TryCreate`, which trims whitespace and rejects empty input.
/// The trim-then-non-empty invariant is therefore enforced at
/// construction time, not at use time.
///
/// **LOGGING**: do NOT log instances of this type. F#'s default
/// `ToString` renders `BootstrapToken "&lt;value&gt;"`, which would
/// leak the credential to any structured-logging sink. Per the
/// STEM LOGGING standard (`docs/Standards/LOGGING.md`), pass only
/// derived metadata (e.g. length, hash prefix) when diagnostics
/// are required.
type BootstrapToken =
    private BootstrapToken of string
    with
    static member TryCreate(raw: string | null) : Result<BootstrapToken, string> =
        match raw with
        | null -> Error "token is empty"
        | s ->
            let trimmed = s.Trim()
            if String.IsNullOrEmpty trimmed then Error "token is empty"
            else Ok(BootstrapToken trimmed)
    member this.Value =
        let (BootstrapToken s) = this in s

/// Opaque server-issued installation credential, per
/// `specs/001-fetch-dictionary/data-model.md` §1.4. Returned by
/// `IRegistrationClient` after a successful bootstrap exchange
/// and replayed on subsequent authenticated requests. The value
/// is NEVER validated client-side — the server is the only
/// authority on its shape — so `Create` is total and the type
/// is structurally opaque.
///
/// **LOGGING**: same warning as `BootstrapToken` — do NOT log
/// instances. The default `ToString` would expose the secret.
/// Per the STEM LOGGING standard
/// (`docs/Standards/LOGGING.md`), redact at every sink.
type InstallationCredential =
    private InstallationCredential of string
    with
    static member Create(raw: string) = InstallationCredential raw
    member this.Value =
        let (InstallationCredential s) = this in s

/// Closed taxonomy of registration-flow failure modes, aligned with
/// `stem-dictionaries-manager` v0.7.0
/// (`specs/001-bootstrap-registration/contracts/register.md`
/// §"Failure responses") via the consumer-side `registration-api.md`
/// in this repo:
///
///   - `TokenInvalid`               — server returned 401. The
///     server deliberately conflates three causes into this status:
///     token unknown, token-scope mismatch, and `clientApp` policy-
///     lookup miss. The client cannot tell them apart, and shouldn't
///     try (see the threat-model note in `register.md`).
///   - `TokenAlreadyUsed`           — server returned 409. The
///     bootstrap token was consumed by a prior successful
///     registration (this installation, or another). Re-registration
///     requires admin-side revocation + a fresh token.
///   - `TokenExpired`               — server returned 410. TTL on the
///     bootstrap token elapsed.
///   - `TokenRevoked`               — server returned 423. Admin
///     administratively revoked the token.
///   - `DescriptorRejected detail`  — server returned 400. The
///     descriptor portion of the request was malformed, missing a
///     policy-required field, carried a zero `installGuid`, or the
///     bootstrap token was missing/empty. All four server outcomes
///     collapse into this one client case because the dialog UX is
///     identical (client bug — technician cannot fix). `detail`
///     carries the server's `error` body verbatim for log
///     diagnostics.
///   - `RegistrationServerError httpStatus` — any 5xx the server
///     ever produces (today only 500 / `AuditFailure`, but the
///     client tolerates the broader range to avoid lock-step
///     coupling).
///   - `RegistrationNetwork reason` — off-the-wire failure
///     (`NetworkUnreachable` for `HttpRequestException`, `Timeout`
///     for the client-side 10 s timeout). Reuses
///     `FetchFailureReason` (T013) to avoid a parallel
///     `RegistrationNetworkUnreachable | RegistrationTimeout | ...`
///     enumeration.
type RegistrationError =
    | TokenInvalid
    | TokenAlreadyUsed
    | TokenExpired
    | TokenRevoked
    | DescriptorRejected of detail: string
    | RegistrationServerError of httpStatus: int
    | RegistrationNetwork of FetchFailureReason
