module Stem.ButtonPanelTester.Tests.Integration.ReregisterResetTests

open System
open System.Threading
open Xunit
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Registration
open Stem.ButtonPanelTester.Tests.Fakes

/// Integration tests for `Services.Registration.App.resetForReregister`,
/// the helper that wipes local install state before the Re-Register
/// dialog opens (issue #98). Lives in the `net10.0` test project so
/// the orchestration is exercised through the in-memory adapters; the
/// production sidecar / DPAPI side is covered by the Windows tests.
///
/// The contract under test (per ports.md): credential is deleted
/// first, then the descriptor's `InstallGuid` is rotated. Failure of
/// either step would leave the system in a stuck state — the wipe
/// must run to completion before the dialog opens.

let private stubDescriptor () : InstallationDescriptor = {
    ClientApp = "ButtonPanelTester"
    OsUserId = String.replicate 64 "a"
    MachineId = String.replicate 64 "b"
    InstallGuid = Guid("11111111-2222-3333-4444-555555555555")
    AppVersion = Some "1.0.0"
}

[<Fact>]
let ResetForReregister_DeletesCredentialAndRotatesInstallGuid () =
    task {
        let credentialStore = InMemoryCredentialStore() :> ICredentialStore
        let preExisting = InstallationCredential.Create "stale-credential"
        do! credentialStore.SaveAsync(preExisting, CancellationToken.None)

        let provider =
            InMemoryInstallationDescriptorProvider(stubDescriptor ())

        let beforeGuid = (provider :> IInstallationDescriptorProvider).Current().InstallGuid

        do!
            App.resetForReregister
                credentialStore
                (provider :> IInstallationDescriptorProvider)
                CancellationToken.None

        // Credential side: gone.
        let! stored = credentialStore.LoadAsync(CancellationToken.None)
        Assert.True(stored.IsNone)

        // Descriptor side: ResetInstallGuid was called exactly once,
        // and the resulting InstallGuid differs from the pre-wipe
        // value.
        Assert.Equal(1, provider.ResetCalls)
        let afterGuid =
            (provider :> IInstallationDescriptorProvider).Current().InstallGuid
        Assert.NotEqual(beforeGuid, afterGuid)
        Assert.NotEqual(Guid.Empty, afterGuid)
    }

[<Fact>]
let ResetForReregister_AbsentCredential_IsIdempotent () =
    // The wipe must be safe even when there's no credential on disk
    // (e.g. the user clicked Re-Register after a partial registration
    // that never produced a credential). Mirrors the
    // ICredentialStore.DeleteAsync + IInstallationDescriptorProvider.
    // ResetInstallGuid idempotence contracts in ports.md.
    task {
        let credentialStore = InMemoryCredentialStore() :> ICredentialStore

        let provider =
            InMemoryInstallationDescriptorProvider(stubDescriptor ())

        do!
            App.resetForReregister
                credentialStore
                (provider :> IInstallationDescriptorProvider)
                CancellationToken.None

        let! stored = credentialStore.LoadAsync(CancellationToken.None)
        Assert.True(stored.IsNone)
        Assert.Equal(1, provider.ResetCalls)
    }

[<Fact>]
let ResetForReregister_NextRegisterAsyncWouldCarryRotatedInstallGuid () =
    // End-to-end check of the per-call rebuild contract from the
    // orchestration's perspective: after the wipe, a downstream
    // consumer that reads provider.Current() observes the rotated
    // InstallGuid. Mirrors what HttpRegistrationClient does internally
    // on the next RegisterAsync.
    task {
        let credentialStore = InMemoryCredentialStore() :> ICredentialStore
        let provider = InMemoryInstallationDescriptorProvider(stubDescriptor ())
        let portFacing = provider :> IInstallationDescriptorProvider

        let originalGuid = (stubDescriptor ()).InstallGuid

        do! App.resetForReregister credentialStore portFacing CancellationToken.None

        let rotated = portFacing.Current()
        Assert.NotEqual(originalGuid, rotated.InstallGuid)
        Assert.Equal((stubDescriptor ()).OsUserId, rotated.OsUserId)
        Assert.Equal((stubDescriptor ()).MachineId, rotated.MachineId)
    }
