module Stem.ButtonPanelTester.Tests.Windows.Unit.Can.ProtocolMasterSequenceTransmitterTests

open System
open System.Buffers.Binary
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Core.Interfaces
open Core.Models
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Infrastructure.Can

/// File-private fake `ICommunicationPort` (mirrors `FakeCommunicationPort` in
/// PcanCanFrameStreamTests — spec-003 Phase-C precedent). `SendAsync` captures
/// every wire frame in call order; `SendFault`, when `Some`, makes `SendAsync`
/// fault with the scripted exception instead of capturing (the port
/// write-failure path).
type private FakeCommunicationPort() =
    let packetReceived = Event<EventHandler<RawPacket>, RawPacket>()
    let stateChanged = Event<EventHandler<ConnectionState>, ConnectionState>()
    let sentFrames = List<byte[]>()

    /// Scripted send fault: when `Some`, every `SendAsync` call faults with it.
    member val SendFault: exn option = None with get, set

    /// Every frame handed to `SendAsync`, in call order (payload copied).
    member _.SentFrames: List<byte[]> = sentFrames

    interface ICommunicationPort with
        member _.Kind = ChannelKind.Can
        member _.State = ConnectionState.Connected
        member _.IsConnected = true
        [<CLIEvent>]
        member _.PacketReceived = packetReceived.Publish
        [<CLIEvent>]
        member _.StateChanged = stateChanged.Publish
        member _.ConnectAsync(_ct: CancellationToken) = Task.CompletedTask
        member _.DisconnectAsync(_ct: CancellationToken) = Task.CompletedTask
        member this.SendAsync(payload: ReadOnlyMemory<byte>, _ct: CancellationToken) =
            match this.SendFault with
            | Some error -> Task.FromException error
            | None ->
                sentFrames.Add(payload.ToArray())
                Task.CompletedTask

    interface IDisposable with
        member _.Dispose() = ()

/// Arbitrary tool-side protocol sender id — the adapter passes it through to the
/// vendored service; composition supplies the real default in slice B3.
let private testSenderId = 0x12345678u

/// CAN broadcast arbitration id every master-sequence frame must carry, per the
/// wire-format contract §Transport.
let private broadcastArbId = 0x1FFFFFFFu

/// Wire the adapter over a fake port through `CanPortShare`, then force the
/// build (mirrors the shipped `wire` helper in PcanCanFrameStreamTests).
let private wire () =
    let fakePort = new FakeCommunicationPort()
    let share = new CanPortShare(fun () -> fakePort :> ICommunicationPort)

    let transmitter =
        new ProtocolMasterSequenceTransmitter(
            share, testSenderId, NullLogger<ProtocolMasterSequenceTransmitter>.Instance)

    share.GetOrBuild() |> ignore
    (fakePort, transmitter)

// --- fixture loading (MasterSequenceFixtureTests.fs pattern; the JSON is the
// --- net10.0 Tests project's file, linked into this project's output by the
// --- fsproj `<Content Link>` item — DictionaryResolvedDto.json precedent) ---

let private fixturePath =
    Path.Combine(AppContext.BaseDirectory, "Fixtures", "Can", "masterSequenceFixtures.json")

let private hexToBytes (hex: string) : byte[] =
    let length = hex.Length / 2
    let bytes = Array.zeroCreate length

    for i in 0 .. length - 1 do
        bytes[i] <- Convert.ToByte(hex.Substring(i * 2, 2), 16)

    bytes

let private requireString (element: JsonElement) (propertyName: string) : string =
    let raw = element.GetProperty(propertyName).GetString()

    match raw with
    | null -> failwithf "Fixture JSON: property %s missing or null" propertyName
    | s -> s

/// Find one named entry in the master-sequence fixture file and apply
/// `project` to it while the backing `JsonDocument` is still alive.
let private loadEntry (name: string) (project: JsonElement -> 'T) : 'T =
    let json = File.ReadAllText fixturePath
    use doc = JsonDocument.Parse json
    let entries = doc.RootElement.GetProperty("fixtures").EnumerateArray()
    let element = entries |> Seq.find (fun e -> requireString e "name" = name)
    project element

/// Normative app-payload bytes of one fixture entry.
let private loadPayload (name: string) : byte[] =
    loadEntry name (fun element -> hexToBytes (requireString element "payload"))

/// SET_ADDRESS fixture inputs: normative payload bytes + the expectedUuid
/// triple + expectedSpAddress (the adapter call inputs the fixture pins).
let private loadSetAddressEntry (name: string) : byte[] * PanelUuid * uint32 =
    loadEntry name (fun element ->
        let payload = hexToBytes (requireString element "payload")

        let words =
            element.GetProperty("expectedUuid").EnumerateArray()
            |> Seq.map (fun w -> w.GetUInt32())
            |> Seq.toArray

        let spAddress = element.GetProperty("expectedSpAddress").GetUInt32()
        (payload, PanelUuid(words[0], words[1], words[2]), spAddress))

// --- frame reassembly (wire-format contract §Transport) ---

/// Reassemble captured wire frames into the transport packet, asserting the
/// broadcast arbId on EVERY frame. Each frame = [arbId LE (4) | NetInfo (2) |
/// chunk (≤6)]; chunks concatenate in capture order into
/// [cryptFlag | senderId BE (4) | lPack (2) | cmdHigh | cmdLow | app payload (N) | CRC16 (2)].
let private reassembleTransportPacket (frames: List<byte[]>) : byte[] =
    let packet = List<byte>()

    for frame in frames do
        Assert.True(frame.Length > 6, $"wire frame too short: {frame.Length} bytes")

        Assert.Equal(
            broadcastArbId,
            BinaryPrimitives.ReadUInt32LittleEndian(ReadOnlySpan<byte>(frame, 0, 4)))

        packet.AddRange(frame[6..])

    packet.ToArray()

/// App-payload slice of a reassembled transport packet (command bytes sit at
/// [7]/[8]; the trailing 2 bytes are the CRC16).
let private appPayloadOf (packet: byte[]) : byte[] = packet[9 .. packet.Length - 3]

// --- tests ---

[<Fact>]
let SendWhoAreYouAsync_ClaimEdenXp12v_BroadcastsThreeFramesWithFixturePayload () =
    task {
        let expected = loadPayload "claim_eden_xp_12v"
        let (fakePort, transmitter) = wire ()
        use _ = transmitter :> IDisposable

        do!
            (transmitter :> IMasterSequenceTransmitter)
                .SendWhoAreYouAsync(0x03uy, 0x0004us, true, CancellationToken.None)

        Assert.Equal(3, fakePort.SentFrames.Count)
        let packet = reassembleTransportPacket fakePort.SentFrames
        Assert.Equal(0x00uy, packet[7])
        Assert.Equal(0x23uy, packet[8])
        Assert.Equal<byte[]>(expected, appPayloadOf packet)
    }

[<Fact>]
let SendWhoAreYouAsync_Reset12v_BroadcastsVirginMarkerFixturePayload () =
    task {
        let expected = loadPayload "reset_12v"
        let (fakePort, transmitter) = wire ()
        use _ = transmitter :> IDisposable

        do!
            (transmitter :> IMasterSequenceTransmitter)
                .SendWhoAreYouAsync(0xFFuy, 0x0004us, true, CancellationToken.None)

        Assert.Equal(3, fakePort.SentFrames.Count)
        let packet = reassembleTransportPacket fakePort.SentFrames
        Assert.Equal(0x00uy, packet[7])
        Assert.Equal(0x23uy, packet[8])
        Assert.Equal<byte[]>(expected, appPayloadOf packet)
    }

[<Fact>]
let SendSetAddressAsync_EdenXp12vBoard1_BroadcastsFiveFramesWithFixturePayload () =
    task {
        let (expected, uuid, spAddress) = loadSetAddressEntry "set_address_eden_xp_12v_board1"
        let (fakePort, transmitter) = wire ()
        use _ = transmitter :> IDisposable

        do!
            (transmitter :> IMasterSequenceTransmitter)
                .SendSetAddressAsync(uuid, spAddress, CancellationToken.None)

        Assert.Equal(5, fakePort.SentFrames.Count)
        let packet = reassembleTransportPacket fakePort.SentFrames
        Assert.Equal(0x00uy, packet[7])
        Assert.Equal(0x25uy, packet[8])
        Assert.Equal<byte[]>(expected, appPayloadOf packet)
    }

[<Fact>]
let SendWhoAreYouAsync_PortWriteFault_PropagatesAsTaskException () =
    task {
        let (fakePort, transmitter) = wire ()
        use _ = transmitter :> IDisposable
        fakePort.SendFault <- Some(IOException "simulated bus write failure")

        let act () : Task =
            (transmitter :> IMasterSequenceTransmitter)
                .SendWhoAreYouAsync(0x03uy, 0x0004us, true, CancellationToken.None)

        let! _ = Assert.ThrowsAsync<IOException>(act)
        Assert.Empty(fakePort.SentFrames)
    }

[<Fact>]
let SendWhoAreYouAsync_ShareNeverBuilt_ThrowsInvalidOperationException () =
    task {
        // No GetOrBuild here: building the PEAK port is the lifecycle adapter's
        // job (user-initiated OpenAsync); a send before that is the failure path.
        let fakePort = new FakeCommunicationPort()
        let share = new CanPortShare(fun () -> fakePort :> ICommunicationPort)

        let transmitter =
            new ProtocolMasterSequenceTransmitter(
                share, testSenderId, NullLogger<ProtocolMasterSequenceTransmitter>.Instance)

        use _ = transmitter :> IDisposable

        let act () : Task =
            (transmitter :> IMasterSequenceTransmitter)
                .SendWhoAreYouAsync(0x03uy, 0x0004us, true, CancellationToken.None)

        let! _ = Assert.ThrowsAsync<InvalidOperationException>(act)
        Assert.Empty(fakePort.SentFrames)
    }

[<Fact>]
let SendSetAddressAsync_PreCancelledToken_ThrowsOperationCanceledException () =
    task {
        let (fakePort, transmitter) = wire ()
        use _ = transmitter :> IDisposable
        use cts = new CancellationTokenSource()
        cts.Cancel()

        let act () : Task =
            (transmitter :> IMasterSequenceTransmitter)
                .SendSetAddressAsync(PanelUuid(1u, 2u, 3u), 0x00030101u, cts.Token)

        let! _ = Assert.ThrowsAnyAsync<OperationCanceledException>(act)
        Assert.Empty(fakePort.SentFrames)
    }
