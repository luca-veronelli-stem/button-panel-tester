namespace Stem.ButtonPanelTester.Infrastructure.Can

open System.Text
open Peak.Can.Basic.BackwardCompatibility

/// Best-effort reader for the current PEAK PCAN status code and its
/// human-readable description, formatted as a `headline\ntechnical`
/// detail string suitable for the `Error(Recoverable detail, _)`
/// emission `PcanCanLink` surfaces on the `ConnectionState.Error`
/// transition.
///
/// Used by `PcanCanLink.translateState` to replace the legacy
/// hardcoded `"PEAK adapter reported Error"` literal with actionable
/// diagnostic text — see issue #124 and FR-004.
///
/// Parallels `PcanAdapterIdentity` (same channel constant, same
/// try/catch discipline around `PCANBasic.*`) so a missing PEAK
/// driver or an uninitialised channel never bubbles a P/Invoke
/// exception out of the state-machine — the caller falls back to a
/// non-legacy diagnostic string when this helper returns `None`.
[<RequireQualifiedAccess>]
module internal PeakErrorText =

    /// `PCAN_USBBUS1` (`0x51`) — same hardcoded channel as
    /// `PcanAdapterIdentity` and `PCANManager`.
    let private channel: uint16 = 0x51us

    /// `MAX_LENGTH_HARDWARE_NAME` in the PEAK SDK is 256; the error
    /// text buffer is bounded by the same ceiling in `PCANManager`'s
    /// own `GetErrorText` calls. 256 bytes is well above the
    /// contractual width.
    let private errorTextBufferLength = 256

    /// Read the current `TPCANStatus` for the PCAN channel and look
    /// up its human-readable description. Returns the combined detail
    /// in the `headline\ntechnical` shape `PcanCanLink.buildFailureState`
    /// established. Returns `None` when either P/Invoke throws (the
    /// most likely cause is a missing `PCANBasic.dll` on the host) so
    /// the caller can emit a non-legacy fallback.
    let tryReadCurrentErrorDetail () : string option =
        try
            let status = PCANBasic.GetStatus channel
            let buffer = StringBuilder(errorTextBufferLength)
            let lookupStatus = PCANBasic.GetErrorText(status, 0us, buffer)

            let statusCode = uint32 status

            if lookupStatus = TPCANStatus.PCAN_ERROR_OK then
                let text = buffer.ToString().Trim()

                if System.String.IsNullOrWhiteSpace text then
                    Some(sprintf "PEAK · status 0x%X\nPEAK status 0x%X (no description)" statusCode statusCode)
                else
                    Some(sprintf "PEAK · %s\nPEAK status 0x%X" text statusCode)
            else
                Some(
                    sprintf
                        "PEAK · status 0x%X\nPEAK GetErrorText failed: 0x%X"
                        statusCode
                        (uint32 lookupStatus)
                )
        with _ ->
            None
