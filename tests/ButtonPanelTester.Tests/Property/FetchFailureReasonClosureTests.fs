module Stem.ButtonPanelTester.Tests.Property.FetchFailureReasonClosureTests

open System
open FsCheck.FSharp
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Dictionary

/// Local exhaustive label function used by both properties below.
/// Covers `plan.md` Principle II ③ "FetchFailureReasonClosure" by
/// two layered guarantees:
///
///   1. Compile-time exhaustiveness — F# 10's compiler enforces
///      exhaustive pattern matching with no `| _ ->` wildcard. If a
///      future edit to `FetchFailureReason` adds a ninth case
///      without updating this match, the test project will fail to
///      build. The wildcard is deliberately omitted; that is the
///      enforcement mechanism.
///   2. Runtime sanity — FsCheck 3.x's default Arbitrary for a
///      closed F# discriminated union samples every case with
///      non-zero probability, so the two properties below exercise
///      every arm under random generation.
///
/// The function is local to this test file: production has no
/// caller for a `FetchFailureReason -> string` label today. If one
/// arrives later (e.g. log messages) it will land in its own task
/// alongside its consumer, not here.
let private label (reason: FetchFailureReason) : string =
    match reason with
    | NetworkUnreachable -> "NetworkUnreachable"
    | Timeout -> "Timeout"
    | Unauthorized -> "Unauthorized"
    | NotFound -> "NotFound"
    | MalformedPayload -> "MalformedPayload"
    | ServerError -> "ServerError"
    | CacheAbsent -> "CacheAbsent"
    | CacheUnreadable -> "CacheUnreadable"

/// FsCheck property covering `plan.md` Principle II ③
/// "FetchFailureReasonClosure": for every generated
/// `FetchFailureReason`, the exhaustive `label` match returns a
/// non-empty string. Trivial as a mathematical statement (each
/// arm produces a distinct string literal) but the property pins
/// the runtime side of the closure guarantee — the compile-time
/// side is pinned by the missing `| _ ->` wildcard on `label`.
[<Property>]
let Label_AnyFetchFailureReason_ReturnsNonEmptyLabel (reason: FetchFailureReason) =
    let s = label reason
    not (String.IsNullOrEmpty s)

/// FsCheck property covering `plan.md` Principle II ③
/// "FetchFailureReasonClosure": distinct `FetchFailureReason`
/// values produce distinct labels. Guards against an accidental
/// duplicate (two `match` arms returning the same string,
/// e.g. a copy-paste leaving `ServerError` on the wrong arm),
/// which would mask a missing case under property 1's non-empty
/// check. Equal-input pairs are skipped via the `==>` implication
/// operator (vacuously true when `a = b`).
[<Property>]
let Label_DistinctReasons_ProduceDistinctLabels
    (a: FetchFailureReason)
    (b: FetchFailureReason)
    =
    a <> b ==> (label a <> label b)
