namespace Stem.ButtonPanelTester.Core.Can

/// Press-edge detector over consecutive `KeyStateBitmap` frames, per
/// `specs/005-button-press-test/contracts/button-state-wire-format.md`
/// §Bitmap semantics (R2). Firmware ground truth: on the wire
/// **pressed = bit `0`, released/idle = bit `1`** (`UserMain.c:1369,:978`),
/// so a press is an active button's bit transitioning `1 → 0` between two
/// consecutive frames. Bits outside the variant's active mask are ignored
/// (FR-014). The baseline is the first observed frame — no absolute byte is
/// ever read as press-state (`UserMain.c:200` boot caveat).
///
/// `ModuleSuffix` representation: the single-case `KeyStateBitmap` type lives
/// in `ButtonStateFrame.fs`, so this companion module must carry the suffix
/// flag to coexist with the type across the two files (the FSharp.Core
/// `Set` / `List` pattern).
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module KeyStateBitmap =

    /// The bit value a *pressed* key carries on the wire. Firmware clears
    /// the bit on press and sets it on release (`UserMain.c:1369,:1375`),
    /// so pressed = `0`. This is a single named constant on purpose: the
    /// legacy app read the release edge (set-bit), and the press-edge
    /// direction is confirmed on a real OPTIMUS-XP panel in the Hardware
    /// E2E phase — if that bench check surprises, flipping this one
    /// constant flips the detected edge, NOT a redesign.
    [<Literal>]
    let PressedBit = 0uy

    /// Bit value (`0` / `1`) of position `i` (`0..7`) in byte `b`.
    let private bitValue (b: byte) (i: int) : byte = (b >>> i) &&& 1uy

    /// Active-masked bit positions (`0..7`) that transitioned **into
    /// pressed** (`1 → 0`, since pressed = `PressedBit`) between `prior`
    /// and `next`. A position is reported iff it is set in `activeMask`,
    /// was released in `prior`, and is pressed in `next` — the firmware
    /// press edge (R2). Bits outside `activeMask` never appear (FR-014); a
    /// frame compared against itself yields the empty set (no absolute byte
    /// is read as press-state).
    ///
    /// The Lean theorems `press_edge_iff_high_to_low` and
    /// `inactive_bits_ignored` in `Phase4/KeyStateBitmap.lean` (T003)
    /// mechanise these two invariants.
    let pressEdges (activeMask: byte) (prior: KeyStateBitmap) (next: KeyStateBitmap) : Set<int> =
        let (KeyStateBitmap priorByte) = prior
        let (KeyStateBitmap nextByte) = next

        seq { 0..7 }
        |> Seq.filter (fun i ->
            let active = bitValue activeMask i = 1uy
            let priorPressed = bitValue priorByte i = PressedBit
            let nextPressed = bitValue nextByte i = PressedBit
            active && not priorPressed && nextPressed)
        |> Set.ofSeq

    /// Arming update (#293, `data-model.md` §6b): fold an observed bitmap
    /// into the armed state — the bitwise OR of every bitmap observed so far,
    /// baseline frame included. A position is armed once it has been observed
    /// released (bit `1`, R2) in some earlier bitmap. The Lean theorem
    /// `arming_monotonic` in `Phase4/KeyStateBitmap.lean` (T051) mechanises
    /// that the fold never un-arms a position.
    let arm (armed: byte) (observed: KeyStateBitmap) : byte =
        let (KeyStateBitmap observedByte) = observed
        armed ||| observedByte

    /// The §6b scoring rule (#293) layered above `pressEdges` (unchanged): an
    /// active position scores on the press edge (`1 → 0`) when armed —
    /// exactly `pressEdges`, the Lean theorem `armed_scores_on_press_edge` —
    /// and on the release transition (`0 → 1`) when unarmed, because a cold
    /// panel's latched bitmap never transmits a position's FIRST press
    /// (`UserMain.c:1369,:973`) and the release is unambiguous proof of a
    /// completed press (`unarmed_scores_on_first_release`). Scoring against a
    /// frame and then folding it (`arm`) arms the position, so the unarmed
    /// rule fires at most once per position (`no_double_score_after_arming`,
    /// T051; `data-model.md` §6b). Both branches are transitions — no
    /// absolute byte is ever read as press-state.
    let scoredPositions
        (armed: byte)
        (activeMask: byte)
        (prior: KeyStateBitmap)
        (next: KeyStateBitmap)
        : Set<int> =
        let (KeyStateBitmap priorByte) = prior
        let (KeyStateBitmap nextByte) = next

        seq { 0..7 }
        |> Seq.filter (fun i ->
            let active = bitValue activeMask i = 1uy
            let priorPressed = bitValue priorByte i = PressedBit
            let nextPressed = bitValue nextByte i = PressedBit

            active
            && (if bitValue armed i = 1uy then
                    not priorPressed && nextPressed // the press edge, as pressEdges
                else
                    priorPressed && not nextPressed)) // the release transition (0 → 1)
        |> Set.ofSeq
