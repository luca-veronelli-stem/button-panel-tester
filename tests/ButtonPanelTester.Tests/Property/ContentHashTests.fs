module Stem.ButtonPanelTester.Tests.Property.ContentHashTests

open FsCheck.FSharp
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Dictionary

/// FsCheck property covering `plan.md` Principle II ② "ContentHash":
/// `ContentHash.compute` is deterministic — the same input bytes
/// produce the same hex string on every invocation. Trivial as a
/// mathematical statement (SHA-256 is a pure function over its input
/// and `Convert.ToHexStringLower` is a pure rendering), but the
/// property guards against an accidental future regression that would
/// inject hidden state (e.g. a salt, a cached random nonce, a static
/// `HashAlgorithm` instance with reset semantics) into the module.
[<Property>]
let Compute_SameBytes_ReturnsSameHash (bytes: byte[]) =
    ContentHash.compute bytes = ContentHash.compute bytes

/// FsCheck property covering `plan.md` Principle II ② "ContentHash":
/// for two distinct byte arrays IN THE GENERATOR'S SAMPLE SPACE,
/// `ContentHash.compute` returns distinct hex strings. Honest
/// framing: SHA-256 collisions exist in theory but FsCheck cannot
/// find one in its bounded random sample space, so this property
/// is a within-sample-space statement rather than a universal one.
/// Equal-input pairs are skipped via the `==>` implication operator
/// (the property is vacuously true when `a = b`).
[<Property>]
let Compute_DistinctBytes_ReturnsDistinctHashes (a: byte[]) (b: byte[]) =
    a <> b ==> (ContentHash.compute a <> ContentHash.compute b)

/// FsCheck property covering `plan.md` Principle II ② "ContentHash":
/// the output is invariably a 64-character lowercase hex string —
/// the invariant the rest of the system (cache `.sha256` sidecar
/// format, log lines, fixture comparisons) relies on by
/// construction. Checked as `Length = 64` plus a per-character
/// membership test over `[0-9a-f]` — cheaper than `Regex.IsMatch`
/// and free of the regex-engine compile cost per FsCheck iteration.
[<Property>]
let Compute_AnyBytes_ProducesLowercaseHex64 (bytes: byte[]) =
    let hash = ContentHash.compute bytes

    let isHexLower c =
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')

    hash.Length = 64 && hash |> Seq.forall isHexLower
