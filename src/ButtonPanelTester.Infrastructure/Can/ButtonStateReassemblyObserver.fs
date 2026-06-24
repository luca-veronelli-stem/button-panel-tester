namespace Stem.ButtonPanelTester.Infrastructure.Can

open System
open System.Collections.Generic
open Microsoft.Extensions.Logging
open Services.Protocol                    // PacketReassembler
open Stem.ButtonPanelTester.Core.Can      // ICanFrameStream, RawCanFrame, ButtonStateFrame, ButtonStateObservation, IButtonStateObserver

/// Hot subject: gated immutable observer list whose `Dispose` truly detaches
/// (mirrors `WhoIAmReassemblyObserver`'s `SubjectFanOut`, research R5). The
/// snapshot is taken under the lock; the subscriber callbacks fire with the
/// lock NOT held (`stem-async-discipline`, spec-002/003 precedent).
module private ButtonStateObservable =
    type Subscription(remove: unit -> unit) =
        interface IDisposable with
            member _.Dispose() = remove ()

    type SubjectFanOut<'T>() =
        let gate = obj ()
        let mutable observers: IObserver<'T> list = []
        member _.OnNext(value: 'T) =
            let snapshot = lock gate (fun () -> observers)
            for observer in snapshot do observer.OnNext value
        interface IObservable<'T> with
            member _.Subscribe(observer: IObserver<'T>) =
                lock gate (fun () -> observers <- observer :: observers)
                new Subscription(fun () ->
                    lock gate (fun () ->
                        observers <-
                            observers |> List.filter (fun o -> not (obj.ReferenceEquals(o, observer)))))
                :> IDisposable

/// Production `IButtonStateObserver` (fix #270). A baptized panel is silent on WHO_I_AM
/// (`AAS_STAND_BY`; `CORRECTIONS.md` §C1) and heartbeats its button-state `VAR_WRITE` on a
/// **directed CAN ID** equal to its SP_Address, whose machineType byte (bits 23-16) is the variant.
/// So the observer:
///   1. decodes the frame's CAN ID via `ButtonStateObservation.variantOfDirectedId` and accepts the
///      frame ONLY when it decodes to a known `Marketing` variant — this rejects the broadcast id
///      0x1FFFFFFF (-> Virgin) and the tool SRID 0x00000008 (-> Unknown) for free, with no special
///      casing (the variant decode IS the id filter, T044);
///   2. reassembles segmented SP_APP frames **per source CAN ID** (a `PacketReassembler` per id —
///      different panels never share a fragment buffer) via `PacketReassembler`;
///   3. command-filters on 0x0002 (SP_APP_CMD_ID_VAR_WRITE), filters the variable address to the
///      button-state set {0x8000, 0x803E} (dropping the 0x80FE virgin sentinel and any other address
///      inline — R6, extending the inherited WHO_I_AM stopgap, NO new bypass);
///   4. parses the command-inclusive 5-byte payload with `ButtonStateFrame.parse` and republishes a
///      `ButtonStateObservation` carrying the frame AND the variant decoded from the CAN ID.
/// Reuses `Services.Protocol.PacketReassembler`; does NOT use the dictionary-driven `PacketDecoder`.
/// Every drop axis (non-marketing id / too short / wrong command / virgin / wrong address / wrong
/// length) is a silent non-event (Trace only); the observer has no CAN-status surface. Receive-only.
/// Edge detection is the consumer's job — the observer is stateless w.r.t. press/release. Per
/// `specs/005-button-press-test/contracts/button-state-observer-port.md`.
type ButtonStateReassemblyObserver(frameStream: ICanFrameStream, logger: ILogger<ButtonStateReassemblyObserver>) =

    // Firmware-pinned SP_APP offsets (research R1; mirror PacketDecoder / WhoIAmReassemblyObserver).
    // NOTE: plain `let` bindings, NOT [<Literal>] — literals cannot be instance-scoped inside a class.
    let commandHighIndex = 7
    let commandLowIndex = 8
    let addressHighIndex = 9
    let addressLowIndex = 10
    let varWriteCmdHigh = 0x00uy
    let varWriteCmdLow = 0x02uy
    // The 5-byte command-inclusive payload `ButtonStateFrame.parse` expects:
    // [0x00, 0x02, 0x80, var_low, bitmap] — i.e. the slice starting AT the command byte (index 7),
    // NOT the WHO_I_AM-style payload-after-command slice. parse reads the address at the slice's
    // offset 2-3 and the bitmap at offset 4, so the command MUST be included.
    let buttonStatePayloadLength = 5
    let buttonStateAddressPrimary = 0x8000us
    let buttonStateAddressSecondary = 0x803Eus
    let virginSentinel = 0x80FEus

    let subject = ButtonStateObservable.SubjectFanOut<ButtonStateObservation>()

    // One PacketReassembler per source CAN ID: directed heartbeats from different panels (and the
    // broadcast/SRID streams that never reach here) must not interleave fragments in a shared buffer.
    // Touched only on the single vendored read thread (the inherited single-reassembler assumption),
    // so a plain Dictionary needs no lock — same threading model as WhoIAmReassemblyObserver.
    let reassemblers = Dictionary<uint32, PacketReassembler>()

    let reassemblerFor (canId: uint32) : PacketReassembler =
        match reassemblers.TryGetValue canId with
        | true, existing -> existing
        | false, _ ->
            let fresh = PacketReassembler()
            reassemblers[canId] <- fresh
            fresh

    let emit (variant: MarketingVariant) (merged: byte[]) =
        // Need cmd[7,8] + addr[9,10] + bitmap[11] in range: the byte-7 slice of length 5
        // requires merged.Length >= commandHighIndex + buttonStatePayloadLength = 12.
        if merged.Length < commandHighIndex + buttonStatePayloadLength then
            logger.LogTrace(
                "Dropped reassembled packet: too short, {Length} bytes (reason={Reason})",
                merged.Length,
                "too-short")
        elif merged[commandHighIndex] <> varWriteCmdHigh
             || merged[commandLowIndex] <> varWriteCmdLow then
            logger.LogTrace(
                "Dropped reassembled packet: command {CommandHigh:X2}{CommandLow:X2} != VAR_WRITE 0x0002 (reason={Reason})",
                merged[commandHighIndex],
                merged[commandLowIndex],
                "wrong-command")
        else
            let address =
                (uint16 merged[addressHighIndex] <<< 8) ||| uint16 merged[addressLowIndex]

            if address = virginSentinel then
                logger.LogTrace(
                    "Dropped reassembled VAR_WRITE: virgin sentinel address {Address:X4} (reason={Reason})",
                    address,
                    "virgin")
            elif address <> buttonStateAddressPrimary
                 && address <> buttonStateAddressSecondary then
                logger.LogTrace(
                    "Dropped reassembled VAR_WRITE: address {Address:X4} not a button-state address (reason={Reason})",
                    address,
                    "wrong-address")
            else
                // Byte-7 slice: ButtonStateFrame.parse expects the command-inclusive 5-byte payload
                // [0x00,0x02,0x80,var_low,bitmap], NOT the WHO_I_AM payload-after-command slice.
                let payload =
                    ReadOnlyMemory<byte>(merged, commandHighIndex, buttonStatePayloadLength)

                match ButtonStateFrame.parse payload with
                | Some frame -> subject.OnNext { Frame = frame; Variant = variant }
                | None ->
                    logger.LogTrace(
                        "Dropped reassembled VAR_WRITE: parse rejected {Length}-byte payload (reason={Reason})",
                        buttonStatePayloadLength,
                        "wrong-length")

    let onFrame (frame: RawCanFrame) =
        // The accept rule IS the variant decode of the source CAN ID: a directed SP_App id whose
        // machineType (bits 23-16) decodes to a known Marketing variant is a baptized panel's
        // heartbeat; broadcast (-> Virgin) and the tool SRID (-> Unknown) decode to non-marketing
        // and are dropped here, before any reassembly (T044).
        match ButtonStateObservation.variantOfDirectedId frame.CanId with
        | Marketing variant ->
            // F# strict nullness (FS3261): Accept returns byte[]? — the `null` arm is the
            // buffering/too-short case; `merged` is narrowed non-null in the other arm.
            match (reassemblerFor frame.CanId).Accept(frame.Payload.Span) with
            | null ->
                // The reassembler returns null for EVERY buffered fragment — normal mid-reassembly,
                // NOT a drop. Deliberately silent (#208 precedent).
                ()
            | merged -> emit variant merged
        | Virgin
        | Unknown _ ->
            // HOT PATH: fires for EVERY non-marketing CAN frame on the bus (broadcast 0x1FFFFFFF,
            // the tool SRID 0x00000008, and any other id). Guard with IsEnabled so there is zero
            // arg-boxing overhead when Trace is off (the default). Reason axis "non-marketing-id".
            if logger.IsEnabled(LogLevel.Trace) then
                logger.LogTrace(
                    "Ignored frame {CanId:X8}: machineType not a Marketing variant (reason={Reason})",
                    frame.CanId,
                    "non-marketing-id")

    let subscription = frameStream.RawFramesReceived |> Observable.subscribe onFrame

    do logger.LogDebug("ButtonStateReassemblyObserver subscribed to RawFramesReceived")

    interface IButtonStateObserver with
        member _.ButtonStateObserved = subject :> IObservable<ButtonStateObservation>

    interface IDisposable with
        member _.Dispose() = subscription.Dispose()
