namespace Stem.ButtonPanelTester.Infrastructure.Can

open System
open System.Buffers.Binary
open System.Runtime.InteropServices
open Microsoft.Extensions.Logging
open Core.Interfaces
open Core.Models
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary // IClock

/// Hot subject for the frame feed: gated immutable observer list whose `Dispose`
/// truly detaches (matches `PanelDiscoveryService`'s `DiscoveryObservable`, R5).
module private FrameObservable =
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

/// Production `ICanFrameStream`. Translates the vendored `CanPort`'s `PacketReceived`
/// (`RawPacket.Payload` = [arbId little-endian, 4 B][CAN data]) into `RawCanFrame`
/// and fans it out on `RawFramesReceived`. Attaches to the shared port's
/// `PacketReceived` only once the port is built (`CanPortShare.OnBuilt`) so it never
/// forces an eager PEAK build. Receive-only (FR-009); allocation-free per frame.
type PcanCanFrameStream(share: CanPortShare, clock: IClock, logger: ILogger<PcanCanFrameStream>) =

    let subject = FrameObservable.SubjectFanOut<RawCanFrame>()
    let mutable attached: (ICommunicationPort * EventHandler<RawPacket>) option = None

    /// Translate one vendored packet and publish it. Arbitration id = first 4 bytes
    /// (little-endian); CAN data = the remainder; payload is a zero-copy view over
    /// the vendored buffer, valid only for this call.
    let onPacket (_sender: obj | null) (packet: RawPacket) =
        let raw = packet.Payload // ImmutableArray<byte> = [arbId LE 4B][data]
        if raw.Length >= 4 then
            let canId = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan().Slice(0, 4))
            let payload =
                // F# strict nullness: `AsArray` returns `byte[] | null`; the `| null`
                // pattern narrows `backing` to non-null in the other arm.
                match ImmutableCollectionsMarshal.AsArray raw with
                | null -> ReadOnlyMemory<byte>.Empty
                | backing -> ReadOnlyMemory<byte>(backing, 4, backing.Length - 4)
            let receivedAt =
                if packet.Timestamp = DateTime.MinValue then clock.UtcNow()
                else DateTimeOffset(DateTime.SpecifyKind(packet.Timestamp, DateTimeKind.Utc))
            subject.OnNext { CanId = canId; Payload = payload; ReceivedAt = receivedAt }

    // Attach to the shared port's PacketReceived the moment it is built (no eager build).
    do
        share.OnBuilt(fun port ->
            let handler = EventHandler<RawPacket>(fun s pkt -> onPacket s pkt)
            port.PacketReceived.AddHandler handler
            attached <- Some(port, handler)
            logger.LogDebug("PcanCanFrameStream attached to the CAN port PacketReceived"))

    interface ICanFrameStream with
        member _.RawFramesReceived = subject :> IObservable<RawCanFrame>

    interface IDisposable with
        member _.Dispose() =
            match attached with
            | Some(port, handler) -> port.PacketReceived.RemoveHandler handler
            | None -> ()
