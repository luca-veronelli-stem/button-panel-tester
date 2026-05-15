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
    | NetworkUnreachable
    | Timeout
    | Unauthorized
    | NotFound
    | MalformedPayload
    | ServerError
    | CacheAbsent
    | CacheUnreadable
