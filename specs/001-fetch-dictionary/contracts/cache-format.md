# Contract: On-disk dictionary cache format

**Owner**: `Stem.ButtonPanelTester.Infrastructure.Persistence.JsonFileDictionaryCache`
**Consumers**: `JsonFileDictionaryCache` (read/write), `EmbeddedSeedExtractor` (extract-on-first-launch).

---

## Files

| Path | Format | Mandatory pair |
|---|---|---|
| `%LOCALAPPDATA%\Stem\ButtonPanelTester\cache\dictionary.json` | UTF-8 JSON, canonicalised (no whitespace), no BOM | yes |
| `%LOCALAPPDATA%\Stem\ButtonPanelTester\cache\dictionary.json.sha256` | UTF-8 ASCII, 64 lowercase hex chars + LF, no BOM | yes |

Both files are always present together. A lone `dictionary.json` without a sidecar — or vice versa — is treated as `CacheUnreadable`; the cache adapter falls back to the embedded seed (FR-019).

## `dictionary.json` shape

```json
{
  "schemaVersion": 1,
  "fetchedAt": "2026-05-14T08:32:11.123+00:00",
  "seededAt": null,
  "panelTypes": [
    {
      "id": 2,
      "name": "Pulsantiere",
      "description": null,
      "variables": [ /* … */ ]
    }
  ]
}
```

Field semantics:

| Field | Type | Notes |
|---|---|---|
| `schemaVersion` | int (constant `1` in this slice) | Bump on **client-side** cache schema changes (e.g. adding a new field). Independent of any server-side version (the API does not expose one). Bumping forces a re-fetch — readers seeing a higher number than they understand fall back to the seed. |
| `fetchedAt` | ISO 8601 with offset | Timestamp of the live fetch that wrote this file, OR `null` if the file came from the seed. |
| `seededAt` | ISO 8601 with offset \| null | Set when the file was extracted from the embedded seed; `null` after any subsequent live fetch overwrites it. |
| `panelTypes` | `PanelType[]` (per data-model.md) | Always present, never empty for a usable cache. |

Exactly one of `fetchedAt` / `seededAt` is non-null. The pair `(fetchedAt, seededAt) = (null, null)` is invalid and treated as `CacheUnreadable`.

## `dictionary.json.sha256` shape

A single line: 64 lowercase hex characters, an LF, no other content.

```
3b0c2c5e1d4a8f9e2c6b7a8d4e5f6c7d8e9a0b1c2d3e4f5a6b7c8d9e0a1b2c3d
```

Computed as `BitConverter.ToString(SHA256.HashData(File.ReadAllBytes("dictionary.json"))).Replace("-","").ToLowerInvariant()`.

## Read path

```fsharp
JsonFileDictionaryCache.ReadAsync ct =
    if not (File.Exists jsonPath) || not (File.Exists sidecarPath) then
        return Failed(CacheAbsent, None)
    let actual = sha256Hex (File.ReadAllBytes jsonPath)
    let expected = (File.ReadAllText sidecarPath).Trim()
    if not (String.Equals(actual, expected, StringComparison.Ordinal)) then
        return Failed(CacheUnreadable, Some "sidecar hash mismatch")
    let text = File.ReadAllText jsonPath
    try
        let dict = JsonSerializer.Deserialize<CacheFile>(text)
        return Success(toDomain dict, dict.fetchedAt ?? dict.seededAt)
    with ex ->
        return Failed(CacheUnreadable, Some ex.Message)
```

`FetchFailureReason` extensions used here are the existing `MalformedPayload`-family — to be added if not already present in `Core.Dictionary.FetchFailureReason`:

> **Action for implementation**: extend `FetchFailureReason` with `CacheAbsent` and `CacheUnreadable` (in addition to the six wire-failure cases). Update Lean's `failure_reason_exhaustion` accordingly.

## Write path

```fsharp
JsonFileDictionaryCache.WriteAsync (dict, fetchedAt, ct) =
    let payload = serializeCanonical dict fetchedAt
    let hash = sha256Hex payload
    let tmpJson = jsonPath + ".tmp"
    let tmpHash = sidecarPath + ".tmp"
    File.WriteAllBytes(tmpJson, payload)
    File.WriteAllText(tmpHash, hash + "\n")
    File.Move(tmpJson, jsonPath, overwrite = true)
    File.Move(tmpHash, sidecarPath, overwrite = true)
```

Atomic via temp-file + rename. The two renames are not atomic together; if the process is killed between them, the next launch sees a sidecar pointing at an old JSON (or vice versa) and the read path treats this as `CacheUnreadable` — falling back to the seed. This is acceptable: the next successful fetch repairs the state.

## Skip-write optimisation

When `WriteAsync` would produce **bit-identical bytes** to what is already on disk, **the write is skipped** — there is nothing to change. Because the envelope carries `fetchedAt`, this skip fires only on a genuine no-op: the same `ContentHash` **and** the same `fetchedAt`. A refresh that returns identical content with a *newer* `fetchedAt` changes the bytes (and thus the `.sha256` sidecar), so it is **not** skipped — it is persisted.

This is required by FR-001 / FR-012 (#191): the cache file's `fetchedAt` must record the last *confirmed-live* fetch so that an offline relaunch reports the last successful sync, not the date the *content* last changed. `DictionaryService.RefreshAsync` therefore calls `WriteAsync` on every successful fetch and lets this byte-level check absorb the rare true no-op. The in-memory `DictionarySource.Live(t)` and the persisted `fetchedAt` advance together; the cache file's `fetchedAt` is no longer pinned to "when this *content* was first observed live".

## Permissions and crash safety

- File ACLs default to the user-profile permissions; no special handling.
- All writes are temp+rename to survive process kill.
- The cache directory is created on first write with `Directory.CreateDirectory` (idempotent).
