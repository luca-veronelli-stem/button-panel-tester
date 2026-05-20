namespace Stem.ButtonPanelTester.Core.Dictionary

open System

/// Provenance of the dictionary currently loaded in memory, per
/// `specs/001-fetch-dictionary/data-model.md` §1.2. The two cases
/// are mutually exclusive and cover every byte-source the runtime
/// can present to the user:
///   - `FromEmbeddedSeed`: the seed shipped inside the binary, used
///     before the first successful live fetch of a session.
///   - `FromLocalFile`:   the on-disk cache (JSON + sidecar) written
///     by the most recent successful live fetch in any prior session.
type CacheOrigin =
    /// Bytes extracted from the embedded `Assets/dictionary.seed.json`
    /// manifest resource; first-launch fallback before any live fetch
    /// has succeeded on the machine.
    | FromEmbeddedSeed
    /// Bytes loaded from the on-disk cache (JSON + sidecar) written by
    /// the most recent successful live fetch in any prior session.
    | FromLocalFile

/// Wrapper around the in-memory `ButtonPanelDictionary` that carries
/// the provenance the status row reads from. Per
/// `specs/001-fetch-dictionary/data-model.md` §1.2 and the three
/// invariants in §75-78 (mechanised by the Lean
/// `source_data_preserved` theorem shipped in T024):
///   1. `FetchedAt` is the timestamp of the most recent successful
///      live fetch — or, when `Origin = FromEmbeddedSeed`, the seed's
///      build time.
///   2. A `Live -> Cached` re-label (refresh failed mid-session)
///      preserves the in-memory `ButtonPanelDictionary` byte-for-byte;
///      only the wrapper changes.
///   3. `LastFailureReason` is `Some r` exactly when the most recent
///      refresh attempt failed with `r`; `None` when the most recent
///      attempt succeeded or no attempt has been made.
type DictionarySource =
    /// The dictionary in memory came from a successful live fetch in
    /// the current session; `FetchedAt` is the server response time.
    | Live   of FetchedAt : DateTimeOffset
    /// The dictionary in memory came from a cache/seed source.
    /// `FetchedAt` is the timestamp of the last successful live fetch
    /// (or seed build time when `Origin = FromEmbeddedSeed`); `Origin`
    /// names the byte-source; `LastFailureReason` carries the most-
    /// recent refresh failure (`None` if no refresh has been attempted).
    | Cached of FetchedAt        : DateTimeOffset
              * Origin           : CacheOrigin
              * LastFailureReason: FetchFailureReason option
