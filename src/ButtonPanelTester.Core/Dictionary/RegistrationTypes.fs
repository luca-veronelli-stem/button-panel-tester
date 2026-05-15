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

/// Closed taxonomy of registration-flow failure modes, per
/// `specs/001-fetch-dictionary/data-model.md` §1.4:
///   - `TokenInvalid`           — the server rejected the
///     `BootstrapToken` with a 4xx specific to bad credentials.
///   - `RegistrationServerError` — any other non-success HTTP
///     status from the registration endpoint (carries the status
///     code for diagnostics).
///   - `RegistrationNetwork`     — the registration flow shares
///     the dictionary-fetch network-failure taxonomy and reuses
///     `FetchFailureReason` (T013); this avoids a parallel
///     `RegistrationNetworkUnreachable | RegistrationTimeout | ...`
///     enumeration that would duplicate the eight cases.
type RegistrationError =
    | TokenInvalid
    | RegistrationServerError of httpStatus: int
    | RegistrationNetwork of FetchFailureReason
