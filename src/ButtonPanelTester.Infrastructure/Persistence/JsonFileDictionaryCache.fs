namespace Stem.ButtonPanelTester.Infrastructure.Persistence

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading
open System.Threading.Tasks
open Stem.ButtonPanelTester.Core.Dictionary

/// Lazy reader for the embedded seed bytes used by
/// `ExtractSeedIfMissingAsync`. Returns the raw cache-file JSON
/// bytes that the seed extractor writes verbatim when no cache
/// exists yet, or `None` when no seed is available to the host
/// (e.g. an `InMemory` test wiring with no embedded resource).
///
/// Production binding lands in T031 (`EmbeddedSeedExtractor`) and
/// reads the manifest resource
/// `Stem.ButtonPanelTester.GUI.Assets.dictionary.seed.json` from
/// the GUI assembly via `Assembly.GetManifestResourceStream`. The
/// composition root (T033) wires that reader into this adapter.
type SeedBytesReader = CancellationToken -> Task<byte[] option>

/// On-disk cache envelope, per
/// `specs/001-fetch-dictionary/contracts/cache-format.md` §"`dictionary.json` shape".
///
/// `contentHash` extends the sample shape in the contract with the
/// wire-side `ButtonPanelDictionary.ContentHash` so a write→read
/// round-trip preserves the dictionary value byte-for-byte under
/// F# structural equality. The Lean theorem
/// `cache_memory_equal_post_first_success` (T027) and FR-010 both
/// require that the cache slot and memory slot carry the same
/// `Dict` value after a write — recomputing the hash on read from
/// a deserialised payload would require a deterministic canonical
/// form that callers (T049 `HttpDictionaryProvider`) and this
/// adapter would have to agree on. Transporting the hash through
/// the cache instead keeps the adapter's contract independent of
/// any canonicalisation policy.
///
/// The `.sha256` sidecar covers a different concern: tamper
/// detection on the cache file bytes themselves. The two hashes
/// are distinct by design — `contentHash` identifies the logical
/// dictionary content, the sidecar identifies the cache file's
/// on-disk integrity.
[<CLIMutable>]
type private CacheFile = {
    SchemaVersion: int
    ContentHash:   string
    FetchedAt:     DateTimeOffset option
    SeededAt:      DateTimeOffset option
    PanelTypes:    PanelType list
}

/// Production adapter for `IDictionaryCache`, per
/// `specs/001-fetch-dictionary/contracts/ports.md` §IDictionaryCache
/// and `cache-format.md`. Manages the
/// `dictionary.json` + `dictionary.json.sha256` pair under the
/// supplied `cacheDirectory` with atomic temp+rename writes.
///
/// Constructor parameters:
///   - `cacheDirectory` — the directory holding both files. The
///     adapter creates it on first write (idempotent
///     `Directory.CreateDirectory`). Production binding is
///     `%LOCALAPPDATA%\Stem\ButtonPanelTester\cache\` per STEM
///     `APP_DATA.md` (v1.9.0), wired via `StemAppData.cacheDir ()`
///     at the composition root; tests pass a temp directory.
///   - `seedReader` — lazy source for the embedded seed bytes
///     consulted by `ExtractSeedIfMissingAsync`. See
///     `SeedBytesReader` for the lifecycle.
///
/// Crash safety: each write is temp-file + `File.Move(overwrite)`
/// (atomic per file). The two renames (JSON, then sidecar) are
/// NOT atomic *together*; a kill between them leaves a sidecar
/// referring to a stale JSON (or vice versa), which the read path
/// detects as `Failed(CacheUnreadable, "sidecar hash mismatch")`
/// and the service handles by falling back to the seed
/// (FR-019, exercised in T037).
type JsonFileDictionaryCache(cacheDirectory: string, seedReader: SeedBytesReader) =

    let jsonPath    = Path.Combine(cacheDirectory, "dictionary.json")
    let sidecarPath = Path.Combine(cacheDirectory, "dictionary.json.sha256")

    /// Shared serializer options: camelCase property names to match
    /// the wire shape contracted in `cache-format.md`, plus
    /// `JsonFSharpConverter` for F# records, options, and lists.
    /// `JsonSerializerOptions` caches per-instance converter
    /// resolution state and is not safe to mutate after the first
    /// `Serialize`/`Deserialize` call — one instance per adapter
    /// instance, never reconfigured.
    let serializerOptions =
        let o = JsonSerializerOptions()
        o.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
        o.Converters.Add(JsonFSharpConverter())
        o

    /// Serialise `dict` + `fetchedAt` into the canonical cache-file
    /// envelope bytes. `SeededAt` is `None` for live-fetch writes
    /// (those go through `WriteAsync`); the seed-extraction path
    /// (`ExtractSeedIfMissingAsync`) bypasses this and writes the
    /// pre-built seed bytes verbatim.
    let serializeCacheFile (dict: ButtonPanelDictionary) (fetchedAt: DateTimeOffset) : byte[] =
        let cf = {
            SchemaVersion = 1
            ContentHash   = dict.ContentHash
            FetchedAt     = Some fetchedAt
            SeededAt      = None
            PanelTypes    = dict.PanelTypes
        }
        let json = JsonSerializer.Serialize<CacheFile>(cf, serializerOptions)
        Encoding.UTF8.GetBytes(json)

    /// Reverse direction: reconstruct a `ButtonPanelDictionary` from
    /// the parsed envelope. `ContentHash` is restored verbatim from
    /// the envelope; `PanelTypes` is the same list shape used on
    /// the wire (Core's `ButtonPanelDictionary` / `PanelType` types).
    let toDomain (cf: CacheFile) : ButtonPanelDictionary =
        { ContentHash = cf.ContentHash; PanelTypes = cf.PanelTypes }

    /// Atomic temp+rename of `bytes` to `jsonPath`, plus the
    /// `bytes`-hash sidecar to `sidecarPath`. Both writes use
    /// `<path>.tmp` as the staging name and `File.Move(overwrite=true)`
    /// to commit, so a process kill mid-write leaves either the
    /// previous file in place or no torn final file at the target
    /// path.
    let writeBytesAsync (bytes: byte[]) (ct: CancellationToken) : Task = task {
        Directory.CreateDirectory(cacheDirectory) |> ignore
        let hash = ContentHash.compute bytes
        let sidecarBytes = Encoding.ASCII.GetBytes(hash + "\n")
        let tmpJson    = jsonPath + ".tmp"
        let tmpSidecar = sidecarPath + ".tmp"
        do! File.WriteAllBytesAsync(tmpJson, bytes, ct)
        do! File.WriteAllBytesAsync(tmpSidecar, sidecarBytes, ct)
        File.Move(tmpJson, jsonPath, overwrite = true)
        File.Move(tmpSidecar, sidecarPath, overwrite = true)
    }

    interface IDictionaryCache with

        member _.ExistsAsync _ = task {
            return File.Exists(jsonPath) && File.Exists(sidecarPath)
        }

        member _.ReadAsync(ct: CancellationToken) = task {
            if not (File.Exists(jsonPath)) || not (File.Exists(sidecarPath)) then
                return Failed(CacheAbsent, None)
            else
                try
                    let! bytes = File.ReadAllBytesAsync(jsonPath, ct)
                    let actualHash = ContentHash.compute bytes
                    let! sidecarRaw = File.ReadAllTextAsync(sidecarPath, ct)
                    let expectedHash = sidecarRaw.Trim()
                    if not (String.Equals(actualHash, expectedHash, StringComparison.Ordinal)) then
                        return Failed(CacheUnreadable, Some "sidecar hash mismatch")
                    else
                        // F# 10 strict nullness: System.Text.Json's
                        // Deserialize<T> returns T | null for reference
                        // targets; same DELTA-2 pattern as Core's
                        // DictionaryJson.fromJson (T021 commit body).
                        match JsonSerializer.Deserialize<CacheFile>(bytes, serializerOptions) with
                        | null ->
                            return Failed(CacheUnreadable, Some "cache envelope parsed to JSON null")
                        | cf ->
                            // One of fetchedAt / seededAt must be present per
                            // cache-format.md §"`dictionary.json` shape". The
                            // pair (None, None) is invalid.
                            match cf.FetchedAt, cf.SeededAt with
                            | Some t, _ -> return Success(toDomain cf, t)
                            | None, Some t -> return Success(toDomain cf, t)
                            | None, None ->
                                return Failed(CacheUnreadable, Some "neither fetchedAt nor seededAt set")
                with
                | :? JsonException as ex ->
                    return Failed(CacheUnreadable, Some ex.Message)
                | :? IOException as ex ->
                    return Failed(CacheUnreadable, Some ex.Message)
        }

        member _.WriteAsync(dict: ButtonPanelDictionary, fetchedAt: DateTimeOffset, ct: CancellationToken) : Task = task {
            let bytes = serializeCacheFile dict fetchedAt
            let newSidecar = ContentHash.compute bytes
            // Skip-write optimisation per cache-format.md §"Skip-write
            // optimisation": if the bytes we would write hash to the
            // same value already on disk, the on-disk file is bit-equal
            // and rewriting is needless IO. `fetchedAt` advancement is
            // reflected in `DictionaryService`'s in-memory
            // `DictionarySource.Live(t)`, not in the file.
            let mutable skip = false
            if File.Exists(sidecarPath) then
                let! existingRaw = File.ReadAllTextAsync(sidecarPath, ct)
                if String.Equals(existingRaw.Trim(), newSidecar, StringComparison.Ordinal) then
                    skip <- true
            if not skip then
                do! writeBytesAsync bytes ct
        }

        member this.ExtractSeedIfMissingAsync(ct: CancellationToken) : Task = task {
            // FR-019 recovery: extract whenever `ReadAsync` would
            // fail, not merely when both files are absent. A
            // surviving JSON/sidecar pair whose hash no longer
            // matches (torn write, tampering, partial replace) is
            // overwritten in place by the embedded seed so the
            // service's second `ReadAsync` sees a healthy cache.
            // Callers that need to surface the pre-extract failure
            // reason (e.g. `DictionaryService.InitializeAsync`
            // labelling `Some CacheUnreadable` per FR-019) must
            // call `ReadAsync` themselves before this method.
            let cache = this :> IDictionaryCache
            let! readResult = cache.ReadAsync(ct)
            match readResult with
            | Success _ -> ()
            | Failed _ ->
                let! seedBytesOpt = seedReader ct
                match seedBytesOpt with
                | Some bytes -> do! writeBytesAsync bytes ct
                | None -> ()  // no seed available — caller proceeds with Failed
        }
