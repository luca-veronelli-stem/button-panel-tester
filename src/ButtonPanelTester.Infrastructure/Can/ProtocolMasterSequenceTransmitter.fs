namespace Stem.ButtonPanelTester.Infrastructure.Can

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Core.Interfaces
open Core.Models
open Services.Protocol
open Stem.ButtonPanelTester.Core.Can

/// Production `IMasterSequenceTransmitter` per
/// `specs/004-baptism-workflow/contracts/master-sequence-transmitter-port.md` §Adapter
/// pair: encodes the two app payloads via the Core codecs (`WhoAreYouFrame.encode`,
/// `SetAddressFrame.encode`) and delegates packet build / CRC16 / chunking / NetInfo
/// framing / port write to the vendored `IProtocolService.SendCommandAsync`
/// (`ProtocolService.cs:87-114`, research R1 — no new framing code; #111 waiver: the
/// vendored stack is consumed, never modified). Wire shapes per
/// `specs/004-baptism-workflow/contracts/master-sequence-wire-format.md` §Transport:
/// both messages broadcast on arbId `0x1FFFFFFF` — WHO_ARE_YOU = 15 B packet = 3 CAN
/// frames, SET_ADDRESS = 27 B packet = 5 CAN frames.
///
/// The inner vendored service is built LAZILY via `CanPortShare.OnBuilt` (research R5):
/// the adapter never calls `GetOrBuild`, so no PEAK P/Invoke happens before the
/// lifecycle adapter's user-initiated `OpenAsync`. A send before the shared port has
/// ever been built raises `InvalidOperationException` — the `TransmissionFailure` path
/// (FR-014 gates sends on `Connected` anyway, the Phase C service's job). All adapter
/// exceptions propagate unmapped per the port contract §Semantics; cancellation
/// surfaces as `OperationCanceledException`, never a transmission failure.
type ProtocolMasterSequenceTransmitter
    (share: CanPortShare, senderId: uint32, logger: ILogger<ProtocolMasterSequenceTransmitter>) =

    // CAN broadcast arbitration id (29-bit extended) every master-sequence message
    // targets, per the wire-format contract §Transport (mirrors WhoIAmReassemblyObserver).
    let broadcastId = 0x1FFFFFFFu

    // The two built-in vendored `Command` records (firmware enum names; codes per the
    // wire-format contract §Command codes). They extend the existing hardcoded
    // protocol-metadata set — the dictionary-fetch migration is #156, out of scope.
    let whoAreYouCommand = Command("SP_APP_CMD_AA_WHO_ARE_YOU", "00", "23")
    let setAddressCommand = Command("SP_APP_CMD_AA_SET_ADDRESS", "00", "25")

    let gate = obj ()
    let mutable service: IProtocolService option = None

    do
        share.OnBuilt(fun port ->
            // Inner-service logger deliberately omitted (vendored default = NullLogger):
            // the TX path (SendCommandAsync) logs nothing, and the RX-side decode chatter
            // this instance would otherwise emit (it subscribes PacketReceived by
            // construction) is owned by the RX pipeline adapters (PcanCanFrameStream /
            // WhoIAmReassemblyObserver) — duplicating per-frame warnings here would be
            // log noise.
            let decoder =
                PacketDecoder(
                    [ whoAreYouCommand; setAddressCommand ],
                    List.empty<Variable>,
                    List.empty<ProtocolAddress>)

            let built = new ProtocolService(port, decoder, senderId) :> IProtocolService
            lock gate (fun () -> service <- Some built)

            logger.LogDebug(
                "ProtocolMasterSequenceTransmitter built its protocol service over the shared CAN port (senderId={SenderId})",
                senderId))

    /// Snapshot the inner service under the lock — the lock is never held across an
    /// await; `None` means the shared port has never been built.
    let tryService () = lock gate (fun () -> service)

    /// Send core: `ct` first (cancellation is never a transmission failure), then
    /// delegate the whole packet pipeline to the vendored service. Exceptions
    /// propagate — mapping to `TransmissionFailure` is the Phase C service's job.
    let send (command: Command) (payload: byte[]) (ct: CancellationToken) : Task =
        task {
            ct.ThrowIfCancellationRequested()

            match tryService () with
            | Some svc -> do! svc.SendCommandAsync(broadcastId, command, payload, ct)
            | None ->
                raise (
                    InvalidOperationException(
                        "Shared CAN port not built — the link has never been opened, "
                        + "so there is nothing to transmit on."))
        }

    interface IMasterSequenceTransmitter with
        member _.SendWhoAreYouAsync(machineType, fwType, reset, ct) =
            send
                whoAreYouCommand
                (WhoAreYouFrame.encode
                    { MachineType = machineType
                      FwType = fwType
                      Reset = reset })
                ct

        member _.SendSetAddressAsync(uuid, spAddress, ct) =
            send setAddressCommand (SetAddressFrame.encode { Uuid = uuid; SpAddress = spAddress }) ct

    interface IDisposable with
        // Dispose the inner vendored service if built (detaches its PacketReceived
        // handler from the shared port). The share owns the port itself — never
        // disposed here.
        member _.Dispose() =
            match tryService () with
            | Some svc -> svc.Dispose()
            | None -> ()
