namespace Stem.ButtonPanelTester.Infrastructure.Can

open System
open System.Text
open Peak.Can.Basic.BackwardCompatibility
open Stem.ButtonPanelTester.Core.Can

/// Best-effort reader for the local PEAK adapter's identifying
/// properties, per
/// `specs/002-can-link-lifecycle/data-model.md` §6 and
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
    ///
    /// **Device ID format.** Rendered as `0x<HEX>` with 2-digit zero
    /// padding. The PEAK PCAN_DEVICE_ID is a user-settable byte (0x00
    /// to 0xFF, configurable via PCAN-View → Hardware → Device ID), so
    /// the natural width is 2 hex digits. The `0x` prefix disambiguates
    /// from decimal (a value like `0A` reads as either without a
    /// marker). The PEAK convention on the device sticker is the
    /// suffix-`h` style (`0Ah`), but we use `0x` to stay consistent
    /// with the rest of the tooltip vocabulary.
    ///
    /// **Robustness.** `%02X` is a *minimum-width* specifier, not a
    /// truncation — a future PEAK SDK that widened the device ID
    /// beyond a byte would emit additional hex digits, not crash or
    /// silently drop high bits. The underlying `tryReadDeviceId` query
    /// surfaces a `uint32`, so the format is safe across the full
    /// `0x00000000`–`0xFFFFFFFF` range without changes here.
    let tryRead () : AdapterIdentification option =
        match tryReadHardwareName (), tryReadDeviceId () with
        | Some name, Some deviceId ->
            Some
                { ChannelName = name
                  DeviceId = sprintf "0x%02X" deviceId
                  BaudrateBps = baudrateBps }
        | _ -> None
