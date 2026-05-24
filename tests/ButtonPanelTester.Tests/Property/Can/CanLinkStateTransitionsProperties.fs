module Stem.ButtonPanelTester.Tests.Property.Can.CanLinkStateTransitionsProperties

open System
open FsCheck.FSharp
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Can

/// Five-way classification of every reachable `CanLinkState`, mirroring
/// the Lean theorem `state_classification_total` in
/// `lean/Stem/ButtonPanelTester/Phase2/CanLinkState.lean` (T027). The
/// classifier covers the closure at the F# value level; the Lean side
/// covers it at the type level. Adding a sixth `CanLinkState` case
/// would break this `match` (no wildcard arm) AND the Lean `cases`
/// proof, forcing a cross-layer update.
type private StateClassification =
    | InitializingClass
    | ConnectedClass
    | DisconnectedClass
    | ErrorRecoverableClass
    | ErrorFatalClass

let private classify (s: CanLinkState) : StateClassification =
    match s with
    | Initializing -> InitializingClass
    | Connected _ -> ConnectedClass
    | Disconnected _ -> DisconnectedClass
    | Error(Recoverable _, _) -> ErrorRecoverableClass
    | Error(Fatal _, _) -> ErrorFatalClass

/// FsCheck property covering `data-model.md` §1.3 Invariant #1
/// (classification totality): every value of `CanLinkState` falls into
/// exactly one of the five top-level classifications
/// `{Initializing, Connected, Disconnected, Error.Recoverable,
/// Error.Fatal}`. The wildcard-free `classify` `match` above is
/// load-bearing — the F# compiler would reject this file under a
/// future sixth case, forcing the maintainer to also update
/// `CanLinkState.lean`'s `state_classification_total` theorem.
///
/// The property only asserts that `classify` returns *some*
/// classification (i.e. terminates); the value-level proof that the
/// returned classification is the one expected for each case is the
/// wildcard-free `match` itself.
[<Property>]
let Classify_AnyState_ReturnsOneOfTheFiveClasses (state: CanLinkState) =
    let cls = classify state

    cls = InitializingClass
    || cls = ConnectedClass
    || cls = DisconnectedClass
    || cls = ErrorRecoverableClass
    || cls = ErrorFatalClass

/// FsCheck property covering `data-model.md` §1.2 transition
/// semantics: the four state constructors are mutually exclusive —
/// no inhabitant of `CanLinkState` is structurally equal to a value
/// built with a different top-level case. F# discriminated unions
/// satisfy this by construction; the property guards against a
/// hypothetical future change that flattened the DU (e.g. a `Status`
/// record with optional fields) and accidentally collapsed two
/// states into the same value.
[<Property>]
let StateConstructors_DistinctTopLevelCases_ProduceDistinctValues
    (adapter: AdapterIdentification)
    (openedAt: DateTimeOffset)
    (reason: DisconnectReason)
    (since: DateTimeOffset)
    (detail: string)
    =
    let connected = Connected(adapter, openedAt)
    let disconnected = Disconnected(reason, since)
    let errorState = Error(Recoverable detail, since)

    Initializing <> connected
    && Initializing <> disconnected
    && Initializing <> errorState
    && connected <> disconnected
    && connected <> errorState
    && disconnected <> errorState
