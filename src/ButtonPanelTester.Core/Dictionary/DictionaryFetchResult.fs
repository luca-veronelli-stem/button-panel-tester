namespace Stem.ButtonPanelTester.Core.Dictionary

open System

/// Outcome of a single dictionary-fetch attempt, per
/// `specs/001-fetch-dictionary/data-model.md` §1.3. The DU is
/// closed two-way: every fetch produces exactly one of `Success`
/// or `Failed`, never both, never neither. The Success-xor-Failed
/// exclusion is the port-boundary contract mechanised by Lean's
/// `provider_success_xor_failed` theorem in T026.
///
/// Consumed types:
///   - `ButtonPanelDictionary` (T010 / data-model.md §1.1) — the
///     successfully-loaded payload.
///   - `FetchFailureReason`    (T013 / data-model.md §1.3) — the
///     eight-case closed failure taxonomy.
///
/// Per §1.3 line 98, `Detail` is human-readable elaboration for
/// the status row's detail affordance: runtime branching keys off
/// `Reason` only, never `Detail`. `Detail` is not load-bearing
/// for logic.
type DictionaryFetchResult =
    | Success of ButtonPanelDictionary * FetchedAt: DateTimeOffset
    | Failed  of Reason: FetchFailureReason * Detail: string option
