namespace Stem.ButtonPanelTester.Infrastructure.Can

open System
open System.Text
open Peak.Can.Basic.BackwardCompatibility
open Stem.ButtonPanelTester.Core.Can

/// Best-effort reader for the local PEAK adapter's identifying
/// properties, per
/// `specs/002-can-link-and-panel-discovery/data-model.md` §6 and
/// FR-004. Surfaces an `AdapterIdentification` consumed by
/// `PcanCanLink` (T035) to populate the `Connected adapter, openedAt`
/// state the GUI's `CanStatusRow` detail affordance renders.
///
/// Local-only by construction (Principle V): the three fields live in
/// the GUI render path and never travel over HTTP / Azure Table
/// Storage / log telemetry.
///
/// Called by `PcanCanLink` only after the vendored `CanPort` has
/// reported `Connected` — `PCANBasic.GetValue` requires the channel
/// to have been initialised. Returns `None` if either the hardware
/// name or device id query fails so the caller can carry on without
/// blocking the state transition; the GUI then renders the
/// `Connected` chip with a synthesised channel label.
[<RequireQualifiedAccess>]
module PcanAdapterIdentity =

    /// Same hardcoded channel as the vendored
    /// `Infrastructure.Protocol.Hardware.PCANManager`: `PCAN_USBBUS1`
    /// (`0x51`). Lives here as a literal rather than a reference into
    /// the vendored stack so this helper has no dependency on
    /// `PCANManager`'s private surface.
    let private channel: uint16 = 0x51us

    /// `quickstart.md` pins spec-002 to 250 kbps. Carried into the
    /// returned record so the detail affordance can show the
    /// negotiated bitrate without the caller having to thread it
    /// through.
    let private baudrateBps = 250_000

    /// `PCAN_HARDWARE_NAME` returns an ASCII string up to
    /// `MAX_LENGTH_HARDWARE_NAME` bytes (33 in the v5 SDK). A 256-byte
    /// buffer is well above the contractual ceiling.
    let private hardwareNameBufferLength = 256u

    let private tryReadHardwareName () : string option =
        try
            let buffer = StringBuilder(int hardwareNameBufferLength)

            let status =
                PCANBasic.GetValue(
                    channel,
                    TPCANParameter.PCAN_HARDWARE_NAME,
                    buffer,
                    hardwareNameBufferLength
                )

            if status = TPCANStatus.PCAN_ERROR_OK then
                Some(buffer.ToString())
            else
                None
        with _ ->
            None

    let private tryReadDeviceId () : uint32 option =
        try
            let mutable deviceId: uint32 = 0u

            let status =
                PCANBasic.GetValue(
                    channel,
                    TPCANParameter.PCAN_DEVICE_ID,
                    &deviceId,
                    sizeof<uint32> |> uint32
                )

            if status = TPCANStatus.PCAN_ERROR_OK then
                Some deviceId
            else
                None
        with _ ->
            None

    /// Reads `PCAN_HARDWARE_NAME` and `PCAN_DEVICE_ID` from the
    /// currently-initialised PEAK channel and assembles them into an
    /// `AdapterIdentification`. Returns `None` if either query fails
    /// — the caller (PcanCanLink) treats this as "channel is up but
    /// identity not available" and proceeds with the state transition.
    let tryRead () : AdapterIdentification option =
        match tryReadHardwareName (), tryReadDeviceId () with
        | Some name, Some deviceId ->
            Some
                { ChannelName = name
                  SerialNumber = sprintf "%08X" deviceId
                  BaudrateBps = baudrateBps }
        | _ -> None
