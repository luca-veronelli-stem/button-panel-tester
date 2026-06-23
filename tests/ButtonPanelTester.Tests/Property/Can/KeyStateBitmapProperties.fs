module Stem.ButtonPanelTester.Tests.Property.Can.KeyStateBitmapProperties

open FsCheck.FSharp
open FsCheck.Xunit
open Stem.ButtonPanelTester.Core.Can

/// Bit value (`0` / `1`) of position `i` in byte `b` — the test's own
/// independent read of the wire, kept separate from the detector under test.
let private bitValue (b: byte) (i: int) : byte = (b >>> i) &&& 1uy

/// Normalise an arbitrary `int` to a wire bit position `0..7`.
let private toBit (raw: int) : int = ((raw % 8) + 8) % 8

/// Mirrors the Lean theorem `press_edge_iff_high_to_low` in
/// `Phase4/KeyStateBitmap.lean` (T003): for every position, it is reported
/// as a press edge **iff** it is active in `activeMask`, was released in
/// `prior` (bit `1`), and is pressed in `next` (bit `0`) — the firmware
/// `1 → 0` press edge (R2). The expected condition hardcodes the concrete
/// `1 → 0` wire polarity (mirroring the Lean theorem
/// `prior.testBit i = true ∧ next.testBit i = false` literally) rather than
/// reading it back through `PressedBit`, so this is an **independent** check
/// on the detector: `PressedBit` stays the detector's one-line-flip point,
/// but pinning the concrete polarity here means a wrong flip is caught
/// (the bench gate #253 confirms this direction).
[<Property>]
let PressEdgeDetectsHighToLow (activeMask: byte) (priorByte: byte) (nextByte: byte) (rawBit: int) =
    let i = toBit rawBit
    let edges = KeyStateBitmap.pressEdges activeMask (KeyStateBitmap priorByte) (KeyStateBitmap nextByte)
    let active = bitValue activeMask i = 1uy
    let priorReleased = bitValue priorByte i = 1uy
    let nextPressed = bitValue nextByte i = 0uy
    Set.contains i edges = (active && priorReleased && nextPressed)

/// Mirrors the Lean theorem `inactive_bits_ignored` (T003): a position
/// outside the active mask is never reported, whatever the two frames carry
/// (FR-014).
[<Property>]
let InactiveBitsIgnored (activeMask: byte) (priorByte: byte) (nextByte: byte) (rawBit: int) =
    let i = toBit rawBit

    (bitValue activeMask i = 0uy)
    ==> (not (
        Set.contains i (KeyStateBitmap.pressEdges activeMask (KeyStateBitmap priorByte) (KeyStateBitmap nextByte))
    ))

/// Baseline property: a single frame compared against itself yields the
/// empty edge set — no absolute byte is ever read as press-state (R2 boot
/// caveat). A bit cannot be both released in `prior` and pressed in `next`
/// when `prior = next`, so a held button or a steady-state heartbeat scores
/// nothing.
[<Property>]
let SelfFrameYieldsNoEdges (activeMask: byte) (frameByte: byte) =
    let bitmap = KeyStateBitmap frameByte
    KeyStateBitmap.pressEdges activeMask bitmap bitmap = Set.empty
