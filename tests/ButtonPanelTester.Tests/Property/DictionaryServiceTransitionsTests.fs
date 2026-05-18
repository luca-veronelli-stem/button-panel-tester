module Stem.ButtonPanelTester.Tests.Property.DictionaryServiceTransitionsTests

open System
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Dictionary

/// FsCheck property suite for `plan.md` Principle II ④
/// "DictionaryServiceTransitions" per `phase-3.md` §T039.
///
/// Models the abstract transition relation
/// `DictionarySource × DictionaryFetchResult → DictionarySource`
/// the dictionary service implements (offline in Phase 3,
/// fully in Phase 5 / US3 via `RefreshAsync` T050). The relation
/// satisfies the invariants the Lean spec mechanises in T024
/// (`source_data_preserved` — Live → Cached re-label preserves
/// the dictionary value) and T027
/// (`cache_memory_equal_post_first_success` — the first Success
/// drives both observable slots to byte-equal state).
///
/// The transition lives in the test file because production
/// `RefreshAsync` is a `NotSupportedException` stub in Phase 3
/// (T032). The properties below pin the contract that T050 will
/// inherit when US3 lands; they double as the executable spec
/// the FsCheck samples can shake out before there is an
/// implementation to drift against.

/// Pure transition function. `RefreshAsync` (T050, US3) must
/// implement this exact behaviour up to the cache-side effects
/// (FR-009, FR-010) that are orthogonal to the in-memory label.
let private transition
    (prior: DictionarySource)
    (result: DictionaryFetchResult)
    : DictionarySource
    =
    match result with
    | Success(_, fetchedAt) -> Live fetchedAt
    | Failed(reason, _) ->
        match prior with
        // FR-011 / FR-012 — failed refresh re-labels `Live` to
        // `Cached` while preserving the prior FetchedAt and the
        // in-memory dictionary (T024 `source_data_preserved`).
        // The newly-cached source is `FromLocalFile` because the
        // dictionary in memory came from a prior live fetch.
        | Live t -> Cached(t, FromLocalFile, Some reason)
        // Failure on an already-cached source only updates the
        // `LastFailureReason`; FetchedAt and Origin are
        // untouched.
        | Cached(t, origin, _) -> Cached(t, origin, Some reason)

[<Property>]
let Transition_SuccessFromAnyPrior_LandsInLiveWithNewTimestamp
    (prior: DictionarySource)
    (dict: ButtonPanelDictionary)
    (fetchedAt: DateTimeOffset)
    =
    let result = Success(dict, fetchedAt)

    match transition prior result with
    | Live t -> t = fetchedAt
    | _ -> false

[<Property>]
let Transition_FailureFromLive_PreservesFetchedAtAndLabelsLocal
    (t: DateTimeOffset)
    (reason: FetchFailureReason)
    =
    let prior = Live t
    let result = Failed(reason, None)

    match transition prior result with
    | Cached(newT, FromLocalFile, Some newReason) ->
        newT = t && newReason = reason
    | _ -> false

[<Property>]
let Transition_FailureFromCached_PreservesAllExceptFailureReason
    (t: DateTimeOffset)
    (origin: CacheOrigin)
    (priorReason: FetchFailureReason option)
    (newReason: FetchFailureReason)
    =
    let prior = Cached(t, origin, priorReason)
    let result = Failed(newReason, None)

    match transition prior result with
    | Cached(newT, newOrigin, Some r) ->
        newT = t && newOrigin = origin && r = newReason
    | _ -> false

[<Property>]
let Transition_AnyPriorAnyResult_LandsInLiveOrCached
    (prior: DictionarySource)
    (result: DictionaryFetchResult)
    =
    // Closure assertion: the F# DU is closed two-way at the
    // type level, but the property documents the invariant
    // explicitly and would fail if the transition function ever
    // produced something exotic (it cannot, but the property is
    // the executable form of the closure contract that pairs
    // with Lean's `source_data_preserved` theorem in T024).
    match transition prior result with
    | Live _
    | Cached _ -> true
