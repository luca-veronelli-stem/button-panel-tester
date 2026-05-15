namespace Stem.ButtonPanelTester.Infrastructure.Persistence

open System.IO
open System.Reflection
open System.Threading
open System.Threading.Tasks

/// Source of the embedded `dictionary.seed.json` bytes that
/// `JsonFileDictionaryCache.ExtractSeedIfMissingAsync` (T029) writes
/// to disk when the on-disk cache is absent. Implementation of the
/// `SeedBytesReader` contract per
/// `specs/001-fetch-dictionary/research.md` R4.
///
/// The seed file is wired as `<EmbeddedResource>` in
/// `ButtonPanelTester.GUI.fsproj` (T030); F# / .NET manifests the
/// resource under the GUI assembly's default resource namespace
/// `Stem.ButtonPanelTester.GUI.Assets.dictionary.seed.json`. The
/// composition root (T033) partial-applies the GUI assembly to
/// `readSeedBytes` and hands the resulting `SeedBytesReader` to
/// `JsonFileDictionaryCache`.
///
/// The "no-op when the cache file already exists" logic from the
/// task description lives in `JsonFileDictionaryCache.ExtractSeedIfMissingAsync`
/// (the `IDictionaryCache` adapter checks `ExistsAsync` before
/// calling the reader). This module is the strict bytes-supplier
/// half: it doesn't know about the cache adapter, the file system,
/// or the existence check — it knows only how to pull the manifest
/// resource out of a given assembly.
[<RequireQualifiedAccess>]
module EmbeddedSeedExtractor =

    /// The manifest resource name produced by the GUI fsproj's
    /// `<EmbeddedResource Include="Assets/dictionary.seed.json" />`
    /// item. The `.` in the resource path comes from the directory
    /// separator; the leading namespace is the GUI assembly's
    /// `RootNamespace` (`Stem.ButtonPanelTester.GUI`).
    [<Literal>]
    let private SeedResourceName =
        "Stem.ButtonPanelTester.GUI.Assets.dictionary.seed.json"

    /// Read the embedded seed bytes from `assembly`'s manifest. Returns
    /// `Some bytes` when the resource is present, `None` when it isn't
    /// — defensive against:
    ///   - Test harnesses that load `Infrastructure.dll` without the
    ///     GUI assembly (e.g. unit tests for the cache adapter in
    ///     `JsonFileDictionaryCache` that don't need the seed).
    ///   - A future runtime where the GUI build was renamed or the
    ///     `<EmbeddedResource>` was inadvertently dropped from the
    ///     fsproj. The composition root logs a warning at startup
    ///     when `ExtractSeedIfMissingAsync` returns without writing
    ///     anything (T033).
    ///
    /// Memory: the resource stream is materialised to a `byte[]` via
    /// `CopyToAsync` against an in-memory `MemoryStream`, then the
    /// array is returned. The seed payload is small (≤ a few kilobytes
    /// per `research.md` R4 sizing) so the eager copy is cheap and
    /// gives the caller a `byte[]` that survives the disposal of the
    /// resource stream.
    let readSeedBytes (assembly: Assembly) (ct: CancellationToken) : Task<byte[] option> = task {
        // F# 10 strict nullness: GetManifestResourceStream returns
        // Stream | null. Pattern-match before the `use` binding —
        // `use` accepts only non-null IDisposable. Same DELTA-2
        // pattern as Core's DictionaryJson.fromJson (T021 commit body).
        match assembly.GetManifestResourceStream(SeedResourceName) with
        | null -> return None
        | stream ->
            use s = stream
            use ms = new MemoryStream()
            do! s.CopyToAsync(ms, ct)
            return Some(ms.ToArray())
    }
