namespace Stem.ButtonPanelTester.Infrastructure.Auth

open System
open System.IO
open System.Reflection
open System.Security.Cryptography
open System.Security.Principal
open System.Text
open Microsoft.Win32
open Stem.ButtonPanelTester.Core.Dictionary

/// Builds the production `InstallationDescriptor` for the
/// `ButtonPanelTester` consumer, per
/// `specs/001-fetch-dictionary/contracts/registration-api.md`
/// "Request body" + "Privacy posture".
///
/// Side-effects (intentional, called once at composition root):
///   - Reads the Windows SID via `WindowsIdentity.GetCurrent()`.
///   - Reads the OS-wide machine GUID from the registry at
///     `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`. This
///     value is set by Windows during OS install and persists
///     across reboots and updates; on a fresh OS reinstall it is
///     regenerated. We prefer it to `Win32_ComputerSystemProduct.UUID`
///     because it does not require the `System.Management` (WMI)
///     dependency.
///   - Reads-or-creates `install.guid` sidecar text file under the
///     supplied install directory so subsequent launches reuse the
///     same per-installation `Guid`. Atomic-enough: a torn write
///     leaves a non-parseable file that this function regenerates.
///   - Reads `AssemblyInformationalVersionAttribute` from the
///     supplied assembly (typically the GUI's). When absent or
///     blank, `AppVersion` is `None`.
///
/// Both `osUserId` and `machineId` are returned as the lowercase
/// SHA-256 hex digest of the UTF-8 bytes of the raw value (64
/// characters, `[0-9a-f]`) — the raw SID and the raw machine GUID
/// never leave this function. This satisfies FR-020 (no raw
/// identifiers cross the supplier-to-STEM data boundary) and the
/// server's *Privacy posture* MUST.
///
/// This module is Windows-only. The two Windows-API calls
/// (`WindowsIdentity.GetCurrent` + `Registry.LocalMachine`) are why
/// `ButtonPanelTester.Infrastructure` targets `net10.0-windows`.
[<RequireQualifiedAccess>]
module InstallationDescriptorBuilder =

    /// Lowercase SHA-256 hex digest of the UTF-8 bytes of `input`.
    /// Pure, deterministic, testable in isolation (exposed for the
    /// property test in `tests/.../Property/InstallationDescriptorHashingTests.fs`).
    let sha256Hex (input: string) : string =
        let bytes = Encoding.UTF8.GetBytes(input)
        let hash = SHA256.HashData(bytes)
        Convert.ToHexString(hash).ToLowerInvariant()

    let private osUserSidRaw () : string =
        use identity = WindowsIdentity.GetCurrent()

        match identity.User with
        | null ->
            invalidOp
                "Cannot read the current Windows user's SID (WindowsIdentity.User was null)."
        | sid -> sid.Value

    let private machineGuidRaw () : string =
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

    let private readOrCreateInstallGuid (installDirectory: string) : Guid =
        Directory.CreateDirectory(installDirectory) |> ignore
        let path = Path.Combine(installDirectory, "install.guid")

        let writeFresh () =
            let g = Guid.NewGuid()
            File.WriteAllText(path, g.ToString("D"))
            g

        if File.Exists(path) then
            let raw = (File.ReadAllText(path)).Trim()

            match Guid.TryParse(raw) with
            | true, g when g <> Guid.Empty -> g
            | _ -> writeFresh ()
        else
            writeFresh ()

    let private appVersion (assembly: Assembly) : string option =
        match
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        with
        | null -> None
        | attr ->
            if String.IsNullOrWhiteSpace(attr.InformationalVersion) then
                None
            else
                Some attr.InformationalVersion

    /// Build the descriptor. Single call per process lifetime — the
    /// composition root registers the result as a singleton.
    ///
    /// `installDirectory` is the same directory used by the cache
    /// and the credential store: `%LOCALAPPDATA%\Stem.ButtonPanelTester\`.
    /// `versionAssembly` is the assembly to read
    /// `AssemblyInformationalVersion` from — typically the GUI's.
    let build (installDirectory: string) (versionAssembly: Assembly) : InstallationDescriptor =
        {
            ClientApp = "ButtonPanelTester"
            OsUserId = sha256Hex (osUserSidRaw ())
            MachineId = sha256Hex (machineGuidRaw ())
            InstallGuid = readOrCreateInstallGuid installDirectory
            AppVersion = appVersion versionAssembly
        }
