module Stem.ButtonPanelTester.Tests.Property.Can.PanelsOnBusProperties

open System
open FsCheck.FSharp
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Can

/// Helper: build a well-formed `WhoIAmFrame` from arbitrary primitives.
/// `fwType` is fixed at `0x04` so the frame is round-trippable through
/// `WhoIAmFrame.parse`; the variant decoder runs against arbitrary
/// `machineType` bytes.
let private mkFrame (machineType: byte) (u0: uint32) (u1: uint32) (u2: uint32) : WhoIAmFrame =
    { MachineType = MachineTypeByte machineType
      FwType = FwType 0x0004us
      Uuid = PanelUuid(u0, u1, u2) }

/// FsCheck property covering `data-model.md` §5.4 (coalescing): for any
/// sequence of `WhoIAmFrame`s observed against a fresh map, the
/// resulting `PanelsOnBus.Count` equals the number of distinct UUIDs
/// in the sequence — i.e. `observe` never produces duplicate rows.
/// Mechanises FR-008 at the value level; the Lean theorem
/// `observe_coalesces_by_uuid` in `Phase2/PanelsOnBus.lean` (T030)
/// mechanises the same invariant at the type level.
[<Property>]
let PanelsOnBusCoalescing (now: DateTimeOffset) (specs: (byte * uint32 * uint32 * uint32) list) =
    let frames =
        specs
        |> List.map (fun (mt, u0, u1, u2) -> mkFrame mt u0 u1 u2)

    let finalMap = frames |> List.fold (fun m f -> PanelsOnBus.observe now f m) PanelsOnBus.empty

    let distinctUuids = frames |> List.map (fun f -> f.Uuid) |> List.distinct |> List.length

    finalMap.Count = distinctUuids

/// FsCheck property covering `data-model.md` §5.4 (monotonic last-seen):
/// per-UUID `LastSeen` is non-decreasing across a sequence of observe
/// calls whose timestamps are themselves non-decreasing. Phrased per-
/// frame: for any two observations of the same UUID, the later
/// `observe` call leaves the row's `LastSeen` ≥ the earlier one.
[<Property>]
let PanelsOnBusLastSeenMonotonic
    (t0: DateTimeOffset)
    (deltaTicks: int)
    (machineType: byte)
    (u0: uint32)
    (u1: uint32)
    (u2: uint32)
    =
    // Property is "later observation has LastSeen ≥ earlier one"; the
    // negative-delta branch lets the test corpus also cover the
    // contrapositive (an earlier-clock re-observation still updates,
    // so equality is the right comparison there).
    let t1 = t0.AddTicks(int64 (abs deltaTicks))
    let frame = mkFrame machineType u0 u1 u2

    let afterFirst = PanelsOnBus.observe t0 frame PanelsOnBus.empty
    let afterSecond = PanelsOnBus.observe t1 frame afterFirst

    match Map.tryFind frame.Uuid afterFirst, Map.tryFind frame.Uuid afterSecond with
    | Some r0, Some r1 -> r1.LastSeen >= r0.LastSeen
    | _ -> false

/// FsCheck property covering `data-model.md` §5.3 (re-derivation): a
/// same-UUID re-observation with a different `machineType` byte
/// updates the row in place, with both `VariantByte` and
/// `VariantIdentity` reflecting the latest frame. Mechanises the
/// "panel power-cycled out of `AAS_STAND_BY`" edge case from §5.3.
[<Property>]
let PanelsOnBusReObservation_UpdatesVariantInPlace
    (now: DateTimeOffset)
    (initialMachineType: byte)
    (updatedMachineType: byte)
    (u0: uint32)
    (u1: uint32)
    (u2: uint32)
    =
    let first = mkFrame initialMachineType u0 u1 u2
    let second = mkFrame updatedMachineType u0 u1 u2

    let afterFirst = PanelsOnBus.observe now first PanelsOnBus.empty
    let afterSecond = PanelsOnBus.observe now second afterFirst

    afterSecond.Count = 1
    && Map.containsKey first.Uuid afterSecond
    && (afterSecond[first.Uuid].VariantByte = MachineTypeByte updatedMachineType)
    && (afterSecond[first.Uuid].VariantIdentity = VariantDecoder.decode(MachineTypeByte updatedMachineType))
