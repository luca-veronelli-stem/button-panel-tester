module Stem.ButtonPanelTester.Tests.Windows.Unit.InstallationDescriptorProviderTests

open System
open System.IO
open System.Reflection
open Xunit
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Infrastructure.Auth

/// Tests for the production `IInstallationDescriptorProvider` adapter.
/// Lives in `Tests.Windows` because the adapter ships in
/// `ButtonPanelTester.Infrastructure` (net10.0-windows: it reads the
/// current process's Windows SID + the OS-wide `MachineGuid`). The
/// tests exercise the install.guid sidecar lifecycle against a fresh
/// temp directory; the host-derived facts (`OsUserId`, `MachineId`,
/// `AppVersion`) are touched only to assert they stay stable across
/// calls.

let private freshTempDir () : string =
    let d =
        Path.Combine(Path.GetTempPath(), "bpt-installprovider-" + Guid.NewGuid().ToString("N"))

    Directory.CreateDirectory(d) |> ignore
    d

let private cleanup (dir: string) =
    try
        if Directory.Exists(dir) then
            Directory.Delete(dir, recursive = true)
    with _ ->
        ()

let private currentAssembly () : Assembly = Assembly.GetExecutingAssembly()

let private installGuidPath (dir: string) = Path.Combine(dir, "install.guid")

[<Fact>]
let Construction_FreshDirectory_DoesNotCreateInstallGuidUntilCurrentCalled () =
    // The constructor only caches the host-derived facts; the
    // install.guid sidecar is read (or created) on the first Current()
    // call. This pins the lazy-on-Current behaviour: resolving the
    // singleton from the DI container does not touch the filesystem.
    let dir = freshTempDir ()

    try
        let _ = InstallationDescriptorProvider(dir, currentAssembly ())
        Assert.False(File.Exists(installGuidPath dir))
    finally
        cleanup dir

[<Fact>]
let Current_FirstCallOnEmptyDirectory_WritesNewInstallGuid () =
    let dir = freshTempDir ()

    try
        let provider =
            InstallationDescriptorProvider(dir, currentAssembly ())
            :> IInstallationDescriptorProvider

        let descriptor = provider.Current()
        Assert.NotEqual(Guid.Empty, descriptor.InstallGuid)
        Assert.True(File.Exists(installGuidPath dir))

        let onDisk = (File.ReadAllText(installGuidPath dir)).Trim()
        Assert.Equal(descriptor.InstallGuid.ToString("D"), onDisk)
    finally
        cleanup dir

[<Fact>]
let Current_ReReadsInstallGuidEachCall () =
    // After construction reads/creates a GUID, an out-of-band rewrite
    // of the sidecar must be observed by the next Current(). This is
    // the contract HttpRegistrationClient relies on for the per-call
    // rebuild in the Re-Register flow (#98).
    let dir = freshTempDir ()

    try
        let provider =
            InstallationDescriptorProvider(dir, currentAssembly ())
            :> IInstallationDescriptorProvider

        let first = provider.Current()

        // Rewrite the sidecar to a different (non-zero) GUID and
        // verify that the provider picks it up on the next call.
        let rotated = Guid("cccccccc-1234-5678-9abc-def012345678")
        File.WriteAllText(installGuidPath dir, rotated.ToString("D"))

        let second = provider.Current()
        Assert.Equal(rotated, second.InstallGuid)
        Assert.NotEqual(first.InstallGuid, second.InstallGuid)
    finally
        cleanup dir

[<Fact>]
let ResetInstallGuid_DeletesSidecar_AndNextCurrentMintsFreshGuid () =
    let dir = freshTempDir ()

    try
        let provider =
            InstallationDescriptorProvider(dir, currentAssembly ())

        let portFacing = provider :> IInstallationDescriptorProvider

        let first = portFacing.Current()
        Assert.True(File.Exists(installGuidPath dir))

        portFacing.ResetInstallGuid()
        Assert.False(File.Exists(installGuidPath dir))

        let second = portFacing.Current()
        Assert.True(File.Exists(installGuidPath dir))
        Assert.NotEqual(first.InstallGuid, second.InstallGuid)
        Assert.NotEqual(Guid.Empty, second.InstallGuid)
    finally
        cleanup dir

[<Fact>]
let ResetInstallGuid_IsIdempotentWhenSidecarAbsent () =
    // Per the port contract (mirrors ICredentialStore.DeleteAsync),
    // ResetInstallGuid is idempotent — calling it twice (or against
    // a directory with no sidecar) must not raise.
    let dir = freshTempDir ()

    try
        let provider =
            InstallationDescriptorProvider(dir, currentAssembly ())
            :> IInstallationDescriptorProvider

        // Sidecar does not exist yet (Current was never called).
        provider.ResetInstallGuid()
        provider.ResetInstallGuid()

        Assert.False(File.Exists(installGuidPath dir))
    finally
        cleanup dir

[<Fact>]
let Current_HostDerivedFieldsAreStableAcrossCalls () =
    // OsUserId / MachineId / AppVersion are immutable for the
    // process lifetime; the provider caches them at construction so
    // we don't re-hit Windows APIs on every RegisterAsync. Pins the
    // cache contract.
    let dir = freshTempDir ()

    try
        let provider =
            InstallationDescriptorProvider(dir, currentAssembly ())
            :> IInstallationDescriptorProvider

        let first = provider.Current()
        let second = provider.Current()

        Assert.Equal(first.ClientApp, second.ClientApp)
        Assert.Equal(first.OsUserId, second.OsUserId)
        Assert.Equal(first.MachineId, second.MachineId)
        Assert.Equal(first.AppVersion, second.AppVersion)

        // Sanity: the hashed identifiers are 64-char lowercase hex
        // (FR-020) — the same shape the production builder used to
        // produce, kept here to fail fast if the hashing pipeline
        // breaks.
        Assert.Equal(64, first.OsUserId.Length)
        Assert.Equal(64, first.MachineId.Length)
        Assert.Matches("^[0-9a-f]{64}$", first.OsUserId)
        Assert.Matches("^[0-9a-f]{64}$", first.MachineId)
    finally
        cleanup dir

[<Fact>]
let Sha256Hex_IsDeterministicAndLowercase () =
    // The static helper is exposed so the canonical hash form
    // remains testable in isolation; the production code path calls
    // it inside the provider.
    let a = InstallationDescriptorProvider.Sha256Hex("S-1-5-21-stub")
    let b = InstallationDescriptorProvider.Sha256Hex("S-1-5-21-stub")
    Assert.Equal(a, b)
    Assert.Matches("^[0-9a-f]{64}$", a)
