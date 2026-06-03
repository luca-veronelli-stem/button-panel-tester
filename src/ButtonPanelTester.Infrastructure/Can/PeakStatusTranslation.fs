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
/// claimed *exclusively* by another app, e.g. StemDeviceManager) and
/// bus-off (#139) — while falling back to the raw PEAK description for
/// everything else.
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
            // Channel already initialised/claimed by an app that took it
            // *exclusively* — StemDeviceManager is the canonical holder
            // (#150). NOT PCAN-View: PCAN-View shares the channel, so it
            // leaves the channel connectable rather than busy.
            //
            // No "then reconnect" instruction: the vendored
            // `PCANManager` hot-plug monitor re-`Initialize`s the channel
            // every ~1 s (`Hardware/PCANManager.cs` StartConnectionMonitoring),
            // so the link reconnects on its own ~1-2 s after the holder
            // frees it. The status row's Reconnect button is a manual
            // nudge, not a required step.
            { Fatal = false
              // Compose the headline from the Core marker so the
              // Services recognizer (`ErrorClassification.isAutoRecoverable`,
              // #175) and this producer never drift on the cause label.
              Cause = ErrorClassification.AdapterBusyCause
              Suggestion = Some "close the app holding the channel"
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

    /// The PEAK SDK's channel-condition enum
    /// (`Peak.Can.Basic.ChannelCondition`, `UInt32`-backed:
    /// `ChannelUnavailable = 0` / `ChannelAvailable = 1` /
    /// `ChannelOccupied = 2` / `ChannelPCanView = 3`). The
    /// `BackwardCompatibility` surface this module otherwise uses exposes
    /// the condition only as loose `PCAN_CHANNEL_*` int constants read
    /// back through `GetValue(..., PCAN_CHANNEL_CONDITION, out uint, ...)`
    /// — there is no `TPCANChannelCondition` type in this package — so we
    /// re-export the matching named enum for a typed reader (#168).
    type ChannelCondition = Peak.Can.Basic.ChannelCondition

    /// Cold-start probe of the PEAK channel *condition* for the hardcoded
    /// channel — the one query that distinguishes "an adapter is present
    /// but busy (held *exclusively* by another app, e.g.
    /// StemDeviceManager)" from "no adapter present" WITHOUT opening the
    /// channel (#168).
    ///
    /// `GetStatus` (used by `tryReadCurrentStatus`) cannot make that
    /// distinction: it reports *this* process's channel-handle state, so
    /// a never-opened channel always reads `PCAN_ERROR_INITIALIZE`
    /// whether or not hardware is attached.
    /// `GetValue(PCAN_CHANNEL_CONDITION)` reads the driver's global view
    /// of the channel instead.
    ///
    /// Returns `Some condition` on a successful read; `None` when the
    /// query fails — most likely a missing `PCANBasic.dll` on the host —
    /// mirroring `PeakErrorText` / `tryReadCurrentStatus` try/catch
    /// discipline so a driver-less host never bubbles a P/Invoke out of
    /// the state machine. A `None` result preserves the #136 cold-start
    /// derivation (`Disconnected(NoAdapterPresent, _)`).
    let tryReadChannelCondition () : ChannelCondition option =
        try
            let mutable raw = 0u

            let status =
                PCANBasic.GetValue(
                    channel,
                    TPCANParameter.PCAN_CHANNEL_CONDITION,
                    &raw,
                    uint32 sizeof<uint32>
                )

            if status = TPCANStatus.PCAN_ERROR_OK then
                Some(LanguagePrimitives.EnumOfValue raw)
            else
                None
        with _ ->
            None

    /// The #150 adapter-busy classification — `Recoverable` with the
    /// "adapter busy -- close the app holding the channel" headline —
    /// reused verbatim so a *cold-start* busy channel (#168) surfaces the
    /// same message and severity as a mid-session `PCAN_ERROR_INITIALIZE`.
    /// The empty raw text yields the headline only; the technical second
    /// line carries just the status code (no live `GetErrorText` lookup is
    /// needed for a synthesised classification).
    let busyClassification () : ErrorClassification =
        toErrorClassification (translate (uint32 TPCANStatus.PCAN_ERROR_INITIALIZE) "")
