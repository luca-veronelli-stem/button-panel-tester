module Stem.ButtonPanelTester.Tests.Integration.RegistrationFlowTests

open System.Threading
open System.Threading.Tasks
open Xunit
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Services.Registration
open Stem.ButtonPanelTester.Tests.Fakes

/// Integration tests for the GUI startup orchestration helper
/// `Stem.ButtonPanelTester.Services.Registration.App.tryRegister`
/// (T043) per `phase-4.md` §T048. Wires the extracted orchestration
/// through `InMemoryCredentialStore` + `InMemoryRegistrationClient`
/// — no real Avalonia window, no DPAPI, no HTTP — so the
/// `RegistrationOutcome` decision tree can be exercised in
/// `net10.0` land.
///
/// Three cases:
///   (a) Empty store on launch → Completed credential + store now
///       contains it.
///   (b) Non-empty store on launch → Skipped (runDialog NOT
///       invoked).
///   (c) Dialog dismissed without success → Dismissed (store stays
///       empty + caller proceeds with seeded data).

let private aToken (raw: string) : BootstrapToken =
    match BootstrapToken.TryCreate raw with
    | Ok t -> t
    | Error msg -> failwithf "test setup: %s" msg

/// Stub `runDialog` that mimics the production dialog's flow:
/// validates a hard-coded token, calls `IRegistrationClient.RegisterAsync`,
/// and on `Ok` persists the issued credential via
/// `ICredentialStore.SaveAsync` then returns
/// `RegistrationOutcome.Completed`. On `Error`, returns
/// `RegistrationOutcome.Dismissed` (the production dialog would loop
/// internally; in tests we collapse retry to a single attempt).
let private dialogSubmitting
    (token: string)
    (client: ICredentialStore)
    (registration: IRegistrationClient)
    () : Task<RegistrationOutcome> =
    task {
        let! result =
            registration.RegisterAsync(aToken token, CancellationToken.None)

        match result with
        | Ok credential ->
            do! client.SaveAsync(credential, CancellationToken.None)
            return Completed credential
        | Error _ -> return Dismissed
    }

/// Stub `runDialog` that mimics the technician closing the dialog
/// without submitting a token.
let private dialogDismissed () : Task<RegistrationOutcome> =
    Task.FromResult Dismissed

/// Tracks invocation count of the runDialog callback so a test can
/// assert that the dialog was NOT shown.
type private CountingDialog(inner: unit -> Task<RegistrationOutcome>) =
    let mutable invocations = 0
    member _.Invocations = invocations

    member _.Run() : Task<RegistrationOutcome> =
        invocations <- invocations + 1
        inner ()


[<Fact>]
let TryRegister_EmptyStoreSubmittedToken_CompletesAndPersists () =
    task {
        let credentialStore = InMemoryCredentialStore() :> ICredentialStore
        let registration = InMemoryRegistrationClient()
        let issued = InstallationCredential.Create "issued-credential-xyz"
        registration.SetResult("TOKEN-1234", Ok issued)

        let runDialog =
            dialogSubmitting "TOKEN-1234" credentialStore registration

        let! outcome =
            App.tryRegister credentialStore runDialog CancellationToken.None

        match outcome with
        | Completed cred -> Assert.Equal("issued-credential-xyz", cred.Value)
        | other -> Assert.Fail(sprintf "expected Completed _, got %A" other)

        let! stored = credentialStore.LoadAsync(CancellationToken.None)

        match stored with
        | Some c -> Assert.Equal("issued-credential-xyz", c.Value)
        | None -> Assert.Fail("expected store to contain the issued credential")
    }

[<Fact>]
let TryRegister_PreExistingCredential_SkippedAndDialogNotInvoked () =
    task {
        let credentialStore = InMemoryCredentialStore() :> ICredentialStore
        let registration = InMemoryRegistrationClient()
        let preExisting = InstallationCredential.Create "already-here"

        do! credentialStore.SaveAsync(preExisting, CancellationToken.None)

        let dialog =
            CountingDialog(dialogSubmitting "TOKEN-1234" credentialStore registration)

        let! outcome =
            App.tryRegister
                credentialStore
                (fun () -> dialog.Run())
                CancellationToken.None

        Assert.Equal(Skipped, outcome)
        Assert.Equal(0, dialog.Invocations)

        // The pre-existing credential MUST remain intact — Skipped
        // means we didn't touch the store at all.
        let! stored = credentialStore.LoadAsync(CancellationToken.None)

        match stored with
        | Some c -> Assert.Equal("already-here", c.Value)
        | None ->
            Assert.Fail("expected pre-existing credential to remain in the store")
    }

[<Fact>]
let TryRegister_EmptyStoreDialogDismissed_DismissedAndStoreUntouched () =
    task {
        let credentialStore = InMemoryCredentialStore() :> ICredentialStore

        let! outcome =
            App.tryRegister credentialStore dialogDismissed CancellationToken.None

        Assert.Equal(Dismissed, outcome)

        let! stored = credentialStore.LoadAsync(CancellationToken.None)
        Assert.True(stored.IsNone)
    }

[<Fact>]
let TryRegister_EmptyStoreRegistrationServerError_DismissedAndStoreUntouched () =
    // The InMemoryRegistrationClient's "no scripted result" default
    // returns Error TokenInvalid. The dialog stub returns Dismissed
    // when the registration call fails (collapsed retry — production
    // dialog would loop). Store must remain empty.
    task {
        let credentialStore = InMemoryCredentialStore() :> ICredentialStore
        let registration = InMemoryRegistrationClient() :> IRegistrationClient

        let runDialog = dialogSubmitting "TOKEN-1234" credentialStore registration

        let! outcome =
            App.tryRegister credentialStore runDialog CancellationToken.None

        Assert.Equal(Dismissed, outcome)

        let! stored = credentialStore.LoadAsync(CancellationToken.None)
        Assert.True(stored.IsNone)
    }
