module Stem.ButtonPanelTester.Tests.Property.DictionarySerializationTests

open System
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Dictionary

/// FsCheck `Arbitrary` that filters `NaN`, `Infinity`, and
/// `-Infinity` out of the default `float` generator. Required for
/// the dictionary round-trip property because
/// `Variable.Min` and `Variable.Max` are `float option`, and
/// FsCheck's default float generator emits IEEE non-finite
/// values that:
///   1. The production serialiser rejects on
///      `JsonNumberHandling.Strict` (matching the JSON wire
///      spec, which has no `NaN` / `Infinity`).
///   2. Violate the round-trip equality property even when
///      permitted, because `nan = nan` is `false` under F#
///      structural equality (IEEE 754 semantics inherited from
///      `Double.op_Equality`).
/// Both reasons hold simultaneously: the FsCheck generator is
/// the right place to constrain the input space rather than
/// loosening either the property statement or the production
/// number-handling policy.
type FiniteFloats =
    static member Float () : Arbitrary<float> =
        Arb.filter Double.IsFinite (ArbMap.defaults.ArbFor<float>())

/// FsCheck property covering `plan.md` Principle II ①
/// "DictionarySerialization": for every value
/// `d : ButtonPanelDictionary` over the JSON-representable
/// float subset, deserialising the serialised form yields a
/// value structurally equal to the original. Targets the
/// production serialiser
/// `Stem.ButtonPanelTester.Core.Dictionary.DictionaryJson`, so
/// a regression in either `toJson` or `fromJson` (or in the
/// wire shape contracted by `data-model.md` §1.1) is caught at
/// FsCheck time.
[<Property(Arbitrary = [| typeof<FiniteFloats> |])>]
let Roundtrip_AnyDictionary_PreservesEquality (d: ButtonPanelDictionary) =
    let json = DictionaryJson.toJson d
    match DictionaryJson.fromJson json with
    | Ok parsed -> parsed = d
    | Error _ -> false
