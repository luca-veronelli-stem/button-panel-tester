namespace Stem.ButtonPanelTester.Core.Dictionary

/// Closed taxonomy of dictionary-fetch failure modes. The six
/// wire-failure cases come from
/// `specs/001-fetch-dictionary/data-model.md` §1.3; the two
/// cache-failure cases extend the closure per
/// `specs/001-fetch-dictionary/contracts/cache-format.md` §74.
/// Closure is witnessed by Lean's `failure_reason_exhaustion`
/// theorem (T025) and by an FsCheck exhaustion property (T023);
/// adding a variant requires updating both.
type FetchFailureReason =
    /// TCP / DNS / TLS handshake failure (`HttpRequestException` at
    /// the adapter boundary).
    | NetworkUnreachable
    /// Client-side 10 s timeout elapsed before the server responded
    /// (`TaskCanceledException` not attributable to caller-supplied
    /// cancellation).
    | Timeout
    /// Server returned HTTP 401 — API key missing or rejected.
    | Unauthorized
    /// Server returned HTTP 404 — configured `Dictionary:Id` does not
    /// exist on this server.
    | NotFound
    /// Response body did not deserialise to the wire DTO (truncated,
    /// non-JSON, schema mismatch, or any other parser failure).
    | MalformedPayload
    /// Server returned HTTP 5xx or any other unexpected outcome the
    /// status-code → reason table did not specifically classify.
    | ServerError
    /// The on-disk cache file is missing (cache-format failure mode;
    /// surfaced by `IDictionaryCache.ReadAsync` per `cache-format.md`
    /// §74).
    | CacheAbsent
    /// The on-disk cache file is present but corrupt (sidecar hash
    /// mismatch, malformed envelope, or IO error during read).
    | CacheUnreadable
