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

/// Production `IButtonStateObserver` (fix #296, re-keying fix #270). A baptized panel is silent on
/// WHO_I_AM (`AAS_STAND_BY`; `CORRECTIONS.md` §C1) and heartbeats its button-state `VAR_WRITE`
/// **addressed to the master that baptized it**: the CAN arbitration ID is the DESTINATION (the
/// stored `MotherBoardAddress`), which for a panel baptized by THIS tool is the tool's own SRID
/// 0x00000008. The panel's own address rides in the reassembled packet's **senderId** (bytes 1-4,
/// big-endian), whose machineType byte (bits 23-16) is the variant. So the observer:
///   1. reassembles segmented SP_APP frames **per source CAN ID** (a `PacketReassembler` per id —
///      different sources never share a fragment buffer) via `PacketReassembler`, with NO
///      arbitration-ID pre-filter: chunks carry no senderId, so the accept rule can only be applied
///      to a COMPLETED packet;
///   2. command-filters on 0x0002 (SP_APP_CMD_ID_VAR_WRITE) — which alone drops WHO_I_AM traffic
///      (0x0024) whatever id it arrived on — and filters the variable address to the button-state
///      set {0x8000, 0x803E} (dropping the 0x80FE virgin sentinel and any other address inline —
///      R6, extending the inherited WHO_I_AM stopgap, NO new bypass);
///   3. decodes the packet's senderId via `ButtonStateObservation.variantOfSenderId` and accepts
///      ONLY a known `Marketing` variant (the senderId is hand-indexed off the merged array,
///      mirroring `PacketDecoder.ReadSenderIdBigEndian` — this adapter deliberately does NOT use
///      the dictionary-driven `PacketDecoder`);
///   4. parses the command-inclusive 5-byte payload with `ButtonStateFrame.parse` and republishes a
///      `ButtonStateObservation` carrying the frame AND the variant decoded from the senderId.
/// Mechanised by the Lean packet-level theorems `variant_from_sender_id`, `who_i_am_rejected_on_cmd`,
/// `virgin_sentinel_rejected` and `arbitration_id_irrelevant` (T055).
/// Reuses `Services.Protocol.PacketReassembler`; does NOT use the dictionary-driven `PacketDecoder`.
/// Every drop axis (too short / wrong command / virgin / wrong address / non-marketing sender /
/// wrong length) is a silent non-event (Trace only); the observer has no CAN-status surface.
/// Receive-only. Edge detection is the consumer's job — the observer is stateless w.r.t.
/// press/release. Per `specs/005-button-press-test/contracts/button-state-observer-port.md`.
type ButtonStateReassemblyObserver(frameStream: ICanFrameStream, logger: ILogger<ButtonStateReassemblyObserver>) =

    // Firmware-pinned SP_APP offsets (research R1; mirror PacketDecoder / WhoIAmReassemblyObserver).
    // NOTE: plain `let` bindings, NOT [<Literal>] — literals cannot be instance-scoped inside a class.
    // senderId = merged bytes 1-4, BIG-ENDIAN (mirrors PacketDecoder.ReadSenderIdBigEndian; the
    // dictionary-driven decoder itself is deliberately not used here).
    let senderIdIndex = 1
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

    // One PacketReassembler per source CAN ID: streams addressed to different destinations must not
    // interleave fragments in a shared buffer. Since #296 there is no arbitration-ID pre-filter, so
    // this map grows one entry per id seen on the bus (bounded by the bus population — negligible
    // on a bench bus). Touched only on the single vendored read thread (the inherited
    // single-reassembler assumption), so a plain Dictionary needs no lock — same threading model as
    // WhoIAmReassemblyObserver.
    let reassemblers = Dictionary<uint32, PacketReassembler>()

    let reassemblerFor (canId: uint32) : PacketReassembler =
        match reassemblers.TryGetValue canId with
        | true, existing -> existing
        | false, _ ->
            let fresh = PacketReassembler()
            reassemblers[canId] <- fresh
            fresh

    let emit (merged: byte[]) =
        // Need cmd[7,8] + addr[9,10] + bitmap[11] in range: the byte-7 slice of length 5
        // requires merged.Length >= commandHighIndex + buttonStatePayloadLength = 12 — which also
        // covers the senderId at bytes 1-4.
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
                // The panel's OWN address, big-endian at bytes 1-4 — the accept rule's word since
                // #296 (the arbitration id this arrived on is the destination the baptizing master
                // chose, and is never consulted: Lean `arbitration_id_irrelevant`).
                let senderId =
                    (uint32 merged[senderIdIndex] <<< 24)
                    ||| (uint32 merged[senderIdIndex + 1] <<< 16)
                    ||| (uint32 merged[senderIdIndex + 2] <<< 8)
                    ||| uint32 merged[senderIdIndex + 3]

                match ButtonStateObservation.variantOfSenderId senderId with
                | Virgin
                | Unknown _ ->
                    // Per COMPLETED packet, not per frame: this is not the hot path the #270
                    // arbitration-ID pre-filter was, so no IsEnabled guard is warranted.
                    logger.LogTrace(
                        "Dropped reassembled VAR_WRITE: senderId {SenderId:X8} machineType not a Marketing variant (reason={Reason})",
                        senderId,
                        "non-marketing-sender")
                | Marketing variant ->
                    // Byte-7 slice: ButtonStateFrame.parse expects the command-inclusive 5-byte
                    // payload [0x00,0x02,0x80,var_low,bitmap], NOT the WHO_I_AM
                    // payload-after-command slice.
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
        // EVERY frame is reassembled, whatever id it arrived on: the arbitration id is the
        // destination, and the accept rule keys on the senderId — which only exists once the packet
        // is complete. Reassembly stays per source arbitration id (chunks from different sources
        // must not interleave); the accept rule runs in `emit` (T055).
        // F# strict nullness (FS3261): Accept returns byte[]? — the `null` arm is the
        // buffering/too-short case; `merged` is narrowed non-null in the other arm.
        match (reassemblerFor frame.CanId).Accept(frame.Payload.Span) with
        | null ->
            // The reassembler returns null for EVERY buffered fragment — normal mid-reassembly,
            // NOT a drop. Deliberately silent (#208 precedent).
            ()
        | merged -> emit merged

    let subscription = frameStream.RawFramesReceived |> Observable.subscribe onFrame

    do logger.LogDebug("ButtonStateReassemblyObserver subscribed to RawFramesReceived")

    interface IButtonStateObserver with
        member _.ButtonStateObserved = subject :> IObservable<ButtonStateObservation>

    interface IDisposable with
        member _.Dispose() = subscription.Dispose()
