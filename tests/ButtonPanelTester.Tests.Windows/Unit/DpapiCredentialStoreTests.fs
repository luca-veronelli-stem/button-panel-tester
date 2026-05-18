module Stem.ButtonPanelTester.Tests.Windows.Unit.DpapiCredentialStoreTests

open System
open System.Collections.Generic
open System.IO
open System.Threading
open Microsoft.Extensions.Logging
open Xunit
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Infrastructure.Persistence

/// Tests for the production `ICredentialStore` adapter per
/// `specs/001-fetch-dictionary/contracts/credential-format.md` and
/// `phase-4.md` §T045. Lives under `Tests.Windows` (net10.0-windows)
/// because `DpapiCredentialStore` is in the
/// `ButtonPanelTester.Infrastructure` project (net10.0-windows for
/// the `System.Security.Cryptography.ProtectedData` dependency).

// --- helpers ---

let private freshTempDir () =
    let dir =
        Path.Combine(
            Path.GetTempPath(),
            "ButtonPanelTester.DpapiCredentialStoreTests-" + Guid.NewGuid().ToString("N")
        )

    Directory.CreateDirectory(dir) |> ignore
    dir

/// In-memory `ILogger<T>` that records every Log call so tests can
/// assert on the level and exception attached. Used in place of a
/// dedicated fake-logger NuGet to keep the dependency surface small.
/// The `Exception | null` annotation matches the F# 10 strict-
/// nullness projection of MEL's `Exception?` parameter.
type private RecordingLogger<'T>() =
    let entries = ResizeArray<LogLevel * (exn option)>()

    member _.Entries: IReadOnlyList<LogLevel * (exn option)> = entries :> _

    interface ILogger<'T> with
        member _.BeginScope<'TState when 'TState: not null>(_: 'TState) =
            { new IDisposable with
                member _.Dispose() = () }

        member _.IsEnabled(_: LogLevel) = true

        member _.Log<'TState>
            (
                level: LogLevel,
                _: EventId,
                _: 'TState,
                ex: Exception | null,
                _: Func<'TState, Exception | null, string>
            ) =
            let exOpt =
                match ex with
                | null -> None
                | e -> Some e

            entries.Add((level, exOpt))

[<Fact>]
let SaveThenLoad_ReturnsSameCredential () =
    task {
        let dir = freshTempDir ()

        try
            let logger = RecordingLogger<DpapiCredentialStore>()
            let store = DpapiCredentialStore(dir, logger) :> ICredentialStore
            let credential = InstallationCredential.Create "abc-123-test-credential"

            do! store.SaveAsync(credential, CancellationToken.None)

            let! loaded = store.LoadAsync(CancellationToken.None)

            match loaded with
            | Some c -> Assert.Equal(credential.Value, c.Value)
            | None -> Assert.Fail("expected Some credential, got None")
        finally
            Directory.Delete(dir, true)
    }

[<Fact>]
let LoadAsync_MissingFile_ReturnsNone () =
    task {
        let dir = freshTempDir ()

        try
            let logger = RecordingLogger<DpapiCredentialStore>()
            let store = DpapiCredentialStore(dir, logger) :> ICredentialStore

            let! loaded = store.LoadAsync(CancellationToken.None)

            Assert.True(loaded.IsNone)
        finally
            Directory.Delete(dir, true)
    }

[<Fact>]
let LoadAsync_TamperedCiphertext_ReturnsNoneAndEmitsWarning () =
    task {
        let dir = freshTempDir ()

        try
            // Write 64 bytes that are not a valid DPAPI ciphertext.
            // `ProtectedData.Unprotect` will throw
            // `CryptographicException`, which the adapter must
            // catch + log at Warning + surface as None.
            File.WriteAllBytes(Path.Combine(dir, "credential.dpapi"), Array.create 64 0xFFuy)

            let logger = RecordingLogger<DpapiCredentialStore>()
            let store = DpapiCredentialStore(dir, logger) :> ICredentialStore

            let! loaded = store.LoadAsync(CancellationToken.None)

            Assert.True(loaded.IsNone)

            let warnings =
                logger.Entries
                |> Seq.filter (fun (level, exOpt) ->
                    level = LogLevel.Warning && exOpt.IsSome)
                |> Seq.toList

            Assert.NotEmpty(warnings)
        finally
            Directory.Delete(dir, true)
    }

[<Fact>]
let DeleteAsync_NoFile_IsIdempotent () =
    task {
        let dir = freshTempDir ()

        try
            let logger = RecordingLogger<DpapiCredentialStore>()
            let store = DpapiCredentialStore(dir, logger) :> ICredentialStore

            // Two consecutive deletes on a fresh directory must not throw.
            do! store.DeleteAsync(CancellationToken.None)
            do! store.DeleteAsync(CancellationToken.None)

            let! exists = store.ExistsAsync(CancellationToken.None)
            Assert.False(exists)
        finally
            Directory.Delete(dir, true)
    }

[<Fact>]
let DeleteAsync_FileExists_RemovesFile () =
    task {
        let dir = freshTempDir ()

        try
            let logger = RecordingLogger<DpapiCredentialStore>()
            let store = DpapiCredentialStore(dir, logger) :> ICredentialStore
            let credential = InstallationCredential.Create "to-delete"

            do! store.SaveAsync(credential, CancellationToken.None)

            let! existsBefore = store.ExistsAsync(CancellationToken.None)
            Assert.True(existsBefore)

            do! store.DeleteAsync(CancellationToken.None)

            let! existsAfter = store.ExistsAsync(CancellationToken.None)
            Assert.False(existsAfter)
        finally
            Directory.Delete(dir, true)
    }
