module Stem.ButtonPanelTester.Tests.Windows.Unit.JsonFileDictionaryCacheTests

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Xunit
open Stem.ButtonPanelTester.Core.Dictionary
open Stem.ButtonPanelTester.Infrastructure.Persistence

/// Tests for the production `IDictionaryCache` adapter per
/// `specs/001-fetch-dictionary/contracts/cache-format.md` and
/// `phase-3.md` §T036. Lives under `Tests.Windows` (net10.0-windows)
/// because `JsonFileDictionaryCache` is in the
/// `ButtonPanelTester.Infrastructure` project (net10.0-windows for
/// the DPAPI dependency US2 adds later).

// --- helpers ---

let private freshTempDir () =
    let dir =
        Path.Combine(
            Path.GetTempPath(),
            "ButtonPanelTester.JsonFileCacheTests-" + Guid.NewGuid().ToString("N")
        )

    Directory.CreateDirectory(dir) |> ignore
    dir

let private noSeed : SeedBytesReader = fun _ -> task { return None }

let private seedOf (bytes: byte[]) : SeedBytesReader =
    fun _ -> task { return Some bytes }

let private sampleDictionary : ButtonPanelDictionary = {
    ContentHash = String.replicate 64 "a"
    PanelTypes = [
        {
            Id = 1
            Name = "Sample panel"
            Description = Some "round-trip fixture"
            Variables = [
                {
                    Name = "BTN_X"
                    AddressHigh = 0uy
                    AddressLow = 1uy
                    DataType = "uint8"
                    Access = "read"
                    Description = None
                    Min = None
                    Max = None
                    Unit = None
                    IsStandard = false
                }
            ]
        }
    ]
}

let private sampleFetchedAt =
    DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero)

/// Build a valid seed-bytes payload by priming a throwaway cache with
/// `dict` + `fetchedAt`. The resulting bytes are exactly what
/// `ExtractSeedIfMissingAsync` would write through `writeBytesAsync`.
let private buildSeedBytesFor
    (dict: ButtonPanelDictionary)
    (fetchedAt: DateTimeOffset)
    : Task<byte[]> =
    task {
        let primingDir = freshTempDir ()

        try
            let primingCache =
                JsonFileDictionaryCache(primingDir, noSeed) :> IDictionaryCache

            do! primingCache.WriteAsync(dict, fetchedAt, CancellationToken.None)
            return File.ReadAllBytes(Path.Combine(primingDir, "dictionary.json"))
        finally
            Directory.Delete(primingDir, true)
    }

// --- tests ---

[<Fact>]
let ReadAsync_MissingFiles_ReturnsCacheAbsent () =
    task {
        let dir = freshTempDir ()

        try
            let cache = JsonFileDictionaryCache(dir, noSeed) :> IDictionaryCache
            let! result = cache.ReadAsync(CancellationToken.None)

            match result with
            | Failed(CacheAbsent, _) -> ()
            | other -> Assert.Fail(sprintf "expected Failed(CacheAbsent, _), got %A" other)
        finally
            Directory.Delete(dir, true)
    }

[<Fact>]
let ReadAsync_SidecarHashMismatch_ReturnsCacheUnreadable () =
    task {
        let dir = freshTempDir ()

        try
            let cache = JsonFileDictionaryCache(dir, noSeed) :> IDictionaryCache
            do! cache.WriteAsync(sampleDictionary, sampleFetchedAt, CancellationToken.None)

            // Tamper: overwrite the sidecar with a hash that does not
            // match the JSON file's actual bytes.
            File.WriteAllText(
                Path.Combine(dir, "dictionary.json.sha256"),
                (String.replicate 64 "0") + "\n"
            )

            let! result = cache.ReadAsync(CancellationToken.None)

            match result with
            | Failed(CacheUnreadable, _) -> ()
            | other -> Assert.Fail(sprintf "expected Failed(CacheUnreadable, _), got %A" other)
        finally
            Directory.Delete(dir, true)
    }

[<Fact>]
let WriteThenRead_RoundTrip_PreservesDictionaryAndFetchedAt () =
    task {
        let dir = freshTempDir ()

        try
            let cache = JsonFileDictionaryCache(dir, noSeed) :> IDictionaryCache
            do! cache.WriteAsync(sampleDictionary, sampleFetchedAt, CancellationToken.None)

            let! result = cache.ReadAsync(CancellationToken.None)

            match result with
            | Success(dict, fetchedAt) ->
                Assert.Equal(sampleDictionary.ContentHash, dict.ContentHash)
                Assert.Equal<PanelType list>(sampleDictionary.PanelTypes, dict.PanelTypes)
                Assert.Equal(sampleFetchedAt, fetchedAt)
            | other -> Assert.Fail(sprintf "expected Success, got %A" other)
        finally
            Directory.Delete(dir, true)
    }

[<Fact>]
let ReadAsync_StraleTempFilesAlongsideCommittedPair_IsIgnored () =
    // Models a previous WriteAsync that was killed before either
    // `File.Move(overwrite = true)` committed: the `.tmp` files still
    // sit in the cache directory, but the prior session's committed
    // pair is intact. ReadAsync must look at the target files only.
    task {
        let dir = freshTempDir ()

        try
            let cache = JsonFileDictionaryCache(dir, noSeed) :> IDictionaryCache
            do! cache.WriteAsync(sampleDictionary, sampleFetchedAt, CancellationToken.None)

            File.WriteAllText(Path.Combine(dir, "dictionary.json.tmp"), "stale-garbage")
            File.WriteAllText(Path.Combine(dir, "dictionary.json.sha256.tmp"), "stale-garbage")

            let! result = cache.ReadAsync(CancellationToken.None)

            match result with
            | Success(dict, fetchedAt) ->
                Assert.Equal(sampleDictionary.ContentHash, dict.ContentHash)
                Assert.Equal(sampleFetchedAt, fetchedAt)
            | other -> Assert.Fail(sprintf "expected Success, got %A" other)
        finally
            Directory.Delete(dir, true)
    }

[<Fact>]
let ExtractSeedIfMissingAsync_CacheReadable_IsNoOp () =
    task {
        let dir = freshTempDir ()

        try
            // Build a seed payload whose contentHash differs from the
            // pre-existing cache so a stray overwrite would be
            // detectable via the read-back hash.
            let! seedBytes =
                buildSeedBytesFor
                    { sampleDictionary with ContentHash = String.replicate 64 "d" }
                    sampleFetchedAt

            let cache =
                JsonFileDictionaryCache(dir, seedOf seedBytes) :> IDictionaryCache

            do! cache.WriteAsync(sampleDictionary, sampleFetchedAt, CancellationToken.None)
            do! cache.ExtractSeedIfMissingAsync(CancellationToken.None)

            let! result = cache.ReadAsync(CancellationToken.None)

            match result with
            | Success(dict, fetchedAt) ->
                Assert.Equal(sampleDictionary.ContentHash, dict.ContentHash)
                Assert.Equal(sampleFetchedAt, fetchedAt)
            | other -> Assert.Fail(sprintf "expected Success, got %A" other)
        finally
            Directory.Delete(dir, true)
    }

[<Fact>]
let ExtractSeedIfMissingAsync_CacheAbsent_WritesSeedBytes () =
    task {
        let dir = freshTempDir ()

        try
            let! seedBytes = buildSeedBytesFor sampleDictionary sampleFetchedAt

            let cache =
                JsonFileDictionaryCache(dir, seedOf seedBytes) :> IDictionaryCache

            do! cache.ExtractSeedIfMissingAsync(CancellationToken.None)

            Assert.True(File.Exists(Path.Combine(dir, "dictionary.json")))
            Assert.True(File.Exists(Path.Combine(dir, "dictionary.json.sha256")))

            let! result = cache.ReadAsync(CancellationToken.None)

            match result with
            | Success(dict, fetchedAt) ->
                Assert.Equal(sampleDictionary.ContentHash, dict.ContentHash)
                Assert.Equal(sampleFetchedAt, fetchedAt)
            | other -> Assert.Fail(sprintf "expected Success, got %A" other)
        finally
            Directory.Delete(dir, true)
    }

[<Fact>]
let ExtractSeedIfMissingAsync_CacheCorrupt_OverwritesWithSeed () =
    // FR-019 case (c): the JSON/sidecar pair survives but the hashes
    // no longer agree. The loosened `ExtractSeedIfMissingAsync`
    // contract overwrites the pair with the embedded seed so the
    // service's subsequent `ReadAsync` succeeds.
    task {
        let dir = freshTempDir ()

        try
            let! seedBytes = buildSeedBytesFor sampleDictionary sampleFetchedAt

            let cache =
                JsonFileDictionaryCache(dir, seedOf seedBytes) :> IDictionaryCache

            let corruptDict =
                { sampleDictionary with ContentHash = String.replicate 64 "f" }

            do! cache.WriteAsync(corruptDict, sampleFetchedAt, CancellationToken.None)

            File.WriteAllText(
                Path.Combine(dir, "dictionary.json.sha256"),
                (String.replicate 64 "0") + "\n"
            )

            let! preExtract = cache.ReadAsync(CancellationToken.None)

            match preExtract with
            | Failed(CacheUnreadable, _) -> ()
            | other ->
                Assert.Fail(
                    sprintf "expected pre-extract Failed(CacheUnreadable, _), got %A" other
                )

            do! cache.ExtractSeedIfMissingAsync(CancellationToken.None)

            let! postExtract = cache.ReadAsync(CancellationToken.None)

            match postExtract with
            | Success(dict, _) ->
                Assert.Equal(sampleDictionary.ContentHash, dict.ContentHash)
            | other ->
                Assert.Fail(sprintf "expected post-extract Success, got %A" other)
        finally
            Directory.Delete(dir, true)
    }

[<Fact>]
let ExtractSeedIfMissingAsync_NoSeedAvailable_LeavesCacheAbsent () =
    task {
        let dir = freshTempDir ()

        try
            let cache = JsonFileDictionaryCache(dir, noSeed) :> IDictionaryCache
            do! cache.ExtractSeedIfMissingAsync(CancellationToken.None)

            let! result = cache.ReadAsync(CancellationToken.None)

            match result with
            | Failed(CacheAbsent, _) -> ()
            | other -> Assert.Fail(sprintf "expected Failed(CacheAbsent, _), got %A" other)
        finally
            Directory.Delete(dir, true)
    }
