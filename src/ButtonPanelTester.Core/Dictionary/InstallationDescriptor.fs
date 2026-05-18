namespace Stem.ButtonPanelTester.Core.Dictionary

open System

/// Wire-side descriptor sub-object on `POST /register`, per
/// `specs/001-fetch-dictionary/contracts/registration-api.md`
/// "Request body". One instance per installation; constructed at the
/// composition root and held as a singleton so the same value flows
/// to every registration attempt (the descriptor does not vary by
/// bootstrap token).
///
/// All fields are strings on the wire. `osUserId` and `machineId`
/// MUST be the lowercase SHA-256 hex digest of the raw identifier
/// for `ButtonPanelTester` (supplier-deployed — FR-020 + the
/// server's *Privacy posture*). The descriptor type itself does NOT
/// enforce the hashed form: enforcement lives in the builder
/// (`Infrastructure.Auth.InstallationDescriptorBuilder.build`) so
/// tests can construct deterministic stubs with literal digests.
///
/// `installGuid` is a non-zero `Guid` per-installation, persisted
/// in `%LOCALAPPDATA%\Stem.ButtonPanelTester\install.guid` so
/// re-launches and re-registrations carry the same GUID. The
/// builder creates the sidecar file on first launch.
///
/// `appVersion` MUST be SemVer 2.0 when present. Server rejects
/// malformed values with `DescriptorMalformed → 400`. The builder
/// reads `AssemblyInformationalVersionAttribute.InformationalVersion`
/// from the running GUI assembly, which is set to a SemVer string
/// at build time (the .NET SDK default is `1.0.0` when the property
/// is not explicitly set).
type InstallationDescriptor = {
    /// Free-text identifier matching the server's per-`clientApp`
    /// policy-registry key. Fixed to `"ButtonPanelTester"` for this
    /// consumer.
    ClientApp: string

    /// Lowercase SHA-256 hex digest of the UTF-8 bytes of the
    /// Windows SID (`WindowsIdentity.GetCurrent().User.Value`). 64
    /// characters, `[0-9a-f]`. Strict-required per the server's
    /// policy for `ButtonPanelTester`.
    OsUserId: string

    /// Lowercase SHA-256 hex digest of the UTF-8 bytes of the
    /// machine UUID (`Win32_ComputerSystemProduct.UUID`). 64
    /// characters, `[0-9a-f]`. Strict-required per the server's
    /// policy for `ButtonPanelTester`.
    MachineId: string

    /// Per-installation `Guid`, non-zero. Generated client-side and
    /// persisted in `install.guid` sidecar file.
    InstallGuid: Guid

    /// SemVer 2.0 version string of the running GUI assembly, or
    /// `None` when no `AssemblyInformationalVersion` is set (server
    /// accepts the omitted field; the schema marks it optional).
    AppVersion: string option
}
