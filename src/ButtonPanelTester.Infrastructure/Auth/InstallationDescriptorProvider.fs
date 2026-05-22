namespace Stem.ButtonPanelTester.Infrastructure.Auth

open System
open System.IO
open System.Reflection
open System.Security.Cryptography
open System.Security.Principal
open System.Text
open Microsoft.Win32
open Stem.ButtonPanelTester.Core.Dictionary

/// Production adapter for `IInstallationDescriptorProvider`, per
/// `specs/001-fetch-dictionary/contracts/registration-api.md`
/// "Request body" + "Privacy posture".
///
/// The host-derived facts (`OsUserId`, `MachineId`, `AppVersion`)
/// are immutable for the process lifetime and are computed once at
/// construction:
///   - The Windows SID is read via `WindowsIdentity.GetCurrent()`.
///   - The OS-wide machine GUID is read from
///     `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`. This value
///     is set during OS install and persists across reboots; a fresh
///     OS reinstall regenerates it. We prefer it to
///     `Win32_ComputerSystemProduct.UUID` because it avoids the
///     `System.Management` (WMI) dependency.
///   - `AppVersion` is read from
///     `AssemblyInformationalVersionAttribute` on the supplied
///     assembly (typically the GUI's). When absent or blank,
///     `AppVersion` is `None`.
///
/// `OsUserId` and `MachineId` are returned as the lowercase SHA-256
/// hex digest of the UTF-8 bytes of the raw value (64 characters,
/// `[0-9a-f]`) — the raw SID and the raw machine GUID never leave
/// this type. This satisfies FR-020 (no raw identifiers cross the
/// supplier-to-STEM data boundary) and the server's *Privacy posture*
/// MUST.
///
/// The `InstallGuid` sidecar (`install.guid` under the supplied
/// install directory) is read on every `Current()` call. This is
/// deliberate: the Re-Register flow (issue #98) calls
/// `ResetInstallGuid()` to delete the sidecar before opening the
/// registration dialog, so the next `HttpRegistrationClient.RegisterAsync`
/// must observe a fresh `Guid`. Hashing the host identifiers per call
/// would be wasteful for facts that cannot change while the process
/// is alive; reading the sidecar per call costs a single small file
/// read on the registration path (not a hot path).
///
/// Thread-safety: the host-derived fields are immutable. The sidecar
/// read uses local-file IO that is not atomic across concurrent
/// callers, but the registration ceremony is single-threaded (the
/// dialog drives one `RegisterAsync` at a time), so a `Current()`
/// race is not exercised in practice.
///
/// This adapter is Windows-only. The two Windows-API calls
/// (`WindowsIdentity.GetCurrent` + `Registry.LocalMachine`) are why
/// `ButtonPanelTester.Infrastructure` targets `net10.0-windows`.
type InstallationDescriptorProvider
    (installDirectory: string, versionAssembly: Assembly) =

    static let sha256Hex (input: string) : string =
        let bytes = Encoding.UTF8.GetBytes(input)
        let hash = SHA256.HashData(bytes)
        Convert.ToHexString(hash).ToLowerInvariant()

    static let osUserSidRaw () : string =
        use identity = WindowsIdentity.GetCurrent()

        match identity.User with
        | null ->
            invalidOp
                "Cannot read the current Windows user's SID (WindowsIdentity.User was null)."
        | sid -> sid.Value

    static let machineGuidRaw () : string =
        use key =
            Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography")

        match key with
        | null ->
            invalidOp
                "Cannot read MachineGuid: HKLM\\SOFTWARE\\Microsoft\\Cryptography is absent."
        | k ->
            match k.GetValue("MachineGuid") with
            | :? string as s when not (String.IsNullOrWhiteSpace(s)) -> s
            | _ ->
                invalidOp
                    "Cannot read MachineGuid: value missing or not a non-empty string."

    static let appVersion (assembly: Assembly) : string option =
        match
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        with
        | null -> None
        | attr ->
            if String.IsNullOrWhiteSpace(attr.InformationalVersion) then
                None
            else
                Some attr.InformationalVersion

    let installGuidPath = Path.Combine(installDirectory, "install.guid")

    let readOrCreateInstallGuid () : Guid =
        Directory.CreateDirectory(installDirectory) |> ignore

        let writeFresh () =
            let g = Guid.NewGuid()
            File.WriteAllText(installGuidPath, g.ToString("D"))
            g

        if File.Exists(installGuidPath) then
            let raw = (File.ReadAllText(installGuidPath)).Trim()

            match Guid.TryParse(raw) with
            | true, g when g <> Guid.Empty -> g
            | _ -> writeFresh ()
        else
            writeFresh ()

    let cachedOsUserId = sha256Hex (osUserSidRaw ())
    let cachedMachineId = sha256Hex (machineGuidRaw ())
    let cachedAppVersion = appVersion versionAssembly

    /// Lowercase SHA-256 hex digest of the UTF-8 bytes of `input`.
    /// Pure, deterministic, exposed for the property test in
    /// `tests/.../Property/InstallationDescriptorHashingTests.fs`.
    static member Sha256Hex(input: string) : string = sha256Hex input

    interface IInstallationDescriptorProvider with
        member _.Current() : InstallationDescriptor = {
            ClientApp = "ButtonPanelTester"
            OsUserId = cachedOsUserId
            MachineId = cachedMachineId
            InstallGuid = readOrCreateInstallGuid ()
            AppVersion = cachedAppVersion
        }

        member _.ResetInstallGuid() : unit =
            if File.Exists(installGuidPath) then
                File.Delete(installGuidPath)
