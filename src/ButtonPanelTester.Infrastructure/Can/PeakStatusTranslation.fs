namespace Stem.ButtonPanelTester.Infrastructure.Can

open System
open System.Text
open Peak.Can.Basic.BackwardCompatibility
open Stem.ButtonPanelTester.Core.Can

/// Translates a raw PEAK `TPCANStatus` code into a user-facing cause +
/// remediation suggestion, then composes it into the
/// `headline\ntechnical` detail string `PcanCanLink` surfaces on the
/// `Error(classification, _)` emission.
///
/// Replaces the legacy single-line `"PEAK adapter reported Error"`
/// literal and the bare status-text passthrough (`PeakErrorText`) with a
/// small curated table that recognises the statuses worth giving the
/// operator an actionable hint for — adapter-busy (#150: the channel is
/// claimed by PCAN-View or another process) and bus-off (#139) — while
/// falling back to the raw PEAK description for everything else.
///
/// **Why match SDK constants, not hex literals.** Issues #150 and #139
/// quote conflicting hex values for the same statuses (`0x4000000` vs
/// `0x40010`); the authoritative source is the named `TPCANStatus`
/// constant in the PEAK SDK, so the table matches on those cast to
/// `uint32` rather than baking a literal that could drift from the SDK.
[<RequireQualifiedAccess>]
module PeakStatusTranslation =

    /// Outcome of translating a raw PEAK status. A **record, not a DU**:
    /// the slice carries no closed taxonomy of causes (those would force
    /// a Lean+FsCheck pairing) — it is a plain bag of presentation fields
    /// the GUI renders. `Fatal` drives the `Recoverable`/`Fatal`
    /// `ErrorClassification` split; `Cause` + `Suggestion` form the
    /// headline; `RawCode` + `RawText` keep the verbatim PEAK diagnostic
    /// for the technical line.
    type TranslatedStatus =
        { Fatal: bool
          Cause: string
          Suggestion: string option
          RawCode: uint32
          RawText: string }

    /// `PCAN_USBBUS1` (`0x51`) — same hardcoded channel as
    /// `PcanAdapterIdentity` / `PeakErrorText` / `PCANManager`.
    let private channel: uint16 = 0x51us

    /// Matches `PeakErrorText`'s buffer ceiling (`MAX_LENGTH_HARDWARE_NAME`
    /// is 256 in the SDK; well above the error-text width).
    let private errorTextBufferLength = 256

    /// First line of `s` (text before the first `\n`), or the whole
    /// string when single-line. Used for the unknown-status fallback so
    /// a multi-line PEAK description still yields a compact headline.
    let private firstLine (s: string) : string =
        if String.IsNullOrEmpty s then
            s
        else
            let idx = s.IndexOf '\n'
            if idx >= 0 then s.Substring(0, idx) else s

    /// Pure lookup table: raw status code → user-facing translation.
    /// Every arm is non-fatal in this slice (the `Fatal` escalation
    /// lives in `CanLinkService` per `research.md` R8). The default arm
    /// keeps the raw PEAK text so the operator always sees *something*
    /// substantive, even for statuses we have not curated a hint for.
    let translate (code: uint32) (rawText: string) : TranslatedStatus =
        // Cast the named SDK constants here (not at module scope) so the
        // table reads as a direct code↔meaning map.
        let busy = uint32 TPCANStatus.PCAN_ERROR_INITIALIZE
        let busOff = uint32 TPCANStatus.PCAN_ERROR_BUSOFF

        if code = busy then
            // Channel already initialised/claimed — typically PCAN-View
            // or another tester instance holding the adapter (#150).
            { Fatal = false
              Cause = "adapter busy"
              Suggestion = Some "close PCAN-View or the competing app, then reconnect"
              RawCode = code
              RawText = rawText }
        elif code = busOff then
            // CAN controller went bus-off (#139); a reconnect re-inits.
            { Fatal = false
              Cause = "bus-off"
              Suggestion = Some "try reconnect"
              RawCode = code
              RawText = rawText }
        else
            // Unknown status: surface the raw PEAK description, no hint.
            { Fatal = false
              Cause = firstLine rawText
              Suggestion = None
              RawCode = code
              RawText = rawText }

    /// Compose the `headline\ntechnical` detail string in the convention
    /// `PcanCanLink.buildFailureState` established: the first line is the
    /// short cause (with the suggestion appended as `-- <suggestion>`
    /// when present); the second line is the verbatim PEAK status.
    let detailText (t: TranslatedStatus) : string =
        let headline =
            t.Cause
            + (t.Suggestion
               |> Option.map (fun s -> " -- " + s)
               |> Option.defaultValue "")

        sprintf "%s\nPEAK status 0x%X: %s" headline t.RawCode t.RawText

    /// Map a translated status to the Core `ErrorClassification`,
    /// carrying the composed detail string.
    let toErrorClassification (t: TranslatedStatus) : ErrorClassification =
        if t.Fatal then Fatal(detailText t) else Recoverable(detailText t)

    /// Best-effort read of the current PEAK status code and its
    /// description for the hardcoded channel. Returns `Some(code, text)`
    /// or `None` when a P/Invoke throws (most likely a missing
    /// `PCANBasic.dll` on the host) — mirrors `PeakErrorText`'s
    /// try/catch discipline so a driver-less host never bubbles an
    /// exception out of the state machine.
    let tryReadCurrentStatus () : (uint32 * string) option =
        try
            let status = PCANBasic.GetStatus channel
            let buffer = StringBuilder(errorTextBufferLength)
            let lookupStatus = PCANBasic.GetErrorText(status, 0us, buffer)
            let statusCode = uint32 status

            let rawText =
                if lookupStatus = TPCANStatus.PCAN_ERROR_OK then
                    let text = buffer.ToString().Trim()
                    if String.IsNullOrWhiteSpace text then sprintf "status 0x%X" statusCode else text
                else
                    sprintf "status 0x%X" statusCode

            Some(statusCode, rawText)
        with _ ->
            None
