module Stem.ButtonPanelTester.Tests.Property.Can.PruningProperties

open System
open FsCheck.FSharp
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Can

let private mkFrame (machineType: byte) (u0: uint32) (u1: uint32) (u2: uint32) : WhoIAmFrame =
    { MachineType = MachineTypeByte machineType
      FwType = FwType 0x0004us
      Uuid = PanelUuid(u0, u1, u2) }

let private positiveMillis (raw: int) : float = float (max 1 (abs raw))

let private positiveSeconds (raw: int) : float = float (max 1 (abs raw % 86400))

/// FsCheck property covering `specs/003-panel-discovery/
/// data-model.md` §4.3 (pruning correctness) / FR-005: post-prune
/// membership iff `now - lastSeen ≤ ttl`. Mechanises the kept-iff-
/// within-window invariant at the value level; the Lean theorem
/// `prune_partitions_by_threshold` in `Phase2/Pruning.lean` (T031)
/// mechanises the same invariant at the type level.
///
/// `t0` carries the observation timestamp, `nowOffsetTicks` is the
/// signed offset between `LastSeen` and the prune `now` clock, and
/// `ttlMillisRaw` is converted to a strictly-positive millisecond
/// `TimeSpan`. The property asserts post-prune membership matches
/// the predicate `now - lastSeen ≤ ttl` exactly.
[<Property>]
let PruningCorrectness
    (t0: DateTimeOffset)
    (nowOffsetTicks: int64)
    (ttlMillisRaw: int)
    (machineType: byte)
    (u0: uint32)
    (u1: uint32)
    (u2: uint32)
    =
    let ttl = TimeSpan.FromMilliseconds(positiveMillis ttlMillisRaw)

    let safeOffset =
        // Constrain the offset so `t0 + offsetTicks` stays inside the
        // representable `DateTimeOffset` range. FsCheck's full `int64`
        // domain otherwise overflows `DateTimeOffset.Ticks`.
        let maxDelta = TimeSpan.FromDays(365.0).Ticks
        max (-maxDelta) (min maxDelta nowOffsetTicks)

    let now = t0.AddTicks safeOffset
    let frame = mkFrame machineType u0 u1 u2
    let mapWithRow = PanelsOnBus.observe t0 frame PanelsOnBus.empty
    let pruned = Pruning.prune ttl now mapWithRow

    let withinWindow = now - t0 <= ttl
    let stillPresent = Map.containsKey frame.Uuid pruned

    stillPresent = withinWindow

/// FsCheck property covering the §4.3 boundary case: a row whose
/// `LastSeen` is exactly `ttl` old survives a prune (≤ ttl, not <
/// ttl). Phrased as a single-row sanity check distinct from
/// `PruningCorrectness` above so a regression that flipped `<=` to
/// `<` would surface here even before the random-corpus test caught
/// it.
[<Property>]
let PruningBoundary_LastSeenEqualToTtl_StillPresent
    (t0: DateTimeOffset)
    (ttlSecondsRaw: int)
    (machineType: byte)
    (u0: uint32)
    (u1: uint32)
    (u2: uint32)
    =
    let ttl = TimeSpan.FromSeconds(positiveSeconds ttlSecondsRaw)
    let now = t0 + ttl
    let frame = mkFrame machineType u0 u1 u2
    let mapWithRow = PanelsOnBus.observe t0 frame PanelsOnBus.empty
    let pruned = Pruning.prune ttl now mapWithRow

    Map.containsKey frame.Uuid pruned

/// FsCheck property covering idempotence: pruning twice with the same
/// `now` is the same as pruning once. Mechanises the operational
/// expectation behind the 1-second pruning timer in `PanelDiscoveryService`
/// — back-to-back ticks at the same clock instant don't drop
/// extra rows.
[<Property>]
let Pruning_IdempotentAtSameNow
    (t0: DateTimeOffset)
    (ttlSecondsRaw: int)
    (offsetSeconds: int)
    (machineType: byte)
    (u0: uint32)
    (u1: uint32)
    (u2: uint32)
    =
    let ttl = TimeSpan.FromSeconds(positiveSeconds ttlSecondsRaw)
    let now = t0.AddSeconds(float (offsetSeconds % 86400))
    let frame = mkFrame machineType u0 u1 u2
    let mapWithRow = PanelsOnBus.observe t0 frame PanelsOnBus.empty
    let oncePruned = Pruning.prune ttl now mapWithRow
    let twicePruned = Pruning.prune ttl now oncePruned

    oncePruned = twicePruned
