using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using Core.Interfaces;
using Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Services.Protocol;

/// <summary>
/// Facade del protocollo STEM: combina <see cref="ICommunicationPort"/> (I/O
/// byte-level), <see cref="PacketReassembler"/> (aggregazione multi-chunk) e
/// <see cref="IPacketDecoder"/> (decode applicativo) per esporre un'API
/// orientata al comando.
///
/// <para><b>Ruolo:</b></para>
/// <list type="bullet">
/// <item><description>Encode comando → TP (con CRC16 Modbus) → chunk NL → frame wire per canale</description></item>
/// <item><description>Decode chunk wire → strip per-canale → <see cref="PacketReassembler"/> → <see cref="IPacketDecoder"/> → evento <see cref="AppLayerDecoded"/></description></item>
/// <item><description>Pattern request/reply con validatore custom e timeout</description></item>
/// </list>
///
/// <para><b>Thread-safety:</b></para>
/// Le write sullo stato (contatori, reassembler) sono serializzate internamente.
/// <see cref="AppLayerDecoded"/> è firing thread del driver HW sottostante.
///
/// <para><b>Framing per canale</b> (vedi <c>Docs/PROTOCOL.md</c> §5-6 e §7):</para>
/// <list type="bullet">
/// <item><description>CAN — chunk size 6, frame TX = <c>[arbId_LE(4) + NetInfo(2) + chunk(≤6)]</c> (convention A di <see cref="CanPort"/>)</description></item>
/// <item><description>BLE/Serial — chunk size 98, frame TX = <c>[NetInfo(2) + recipientId_LE(4) + chunk(≤98)]</c> pass-through</description></item>
/// </list>
///
/// <para><b>Known gaps (da <c>Docs/PROTOCOL.md</c> §10):</b></para>
/// <list type="bullet">
/// <item><description>CRC16 in RX non validato (parità con legacy).</description></item>
/// <item><description>SenderId byte-swappato (convention mantenuta con il dizionario).</description></item>
/// </list>
/// </summary>
public sealed class ProtocolService : IProtocolService
{
    private const byte DefaultCryptFlag = 0x00;
    private const int DefaultVersion = 1;

    private readonly ICommunicationPort _port;
    private readonly IPacketDecoder _decoder;
    private readonly PacketReassembler _reassembler;
    private readonly ILogger<ProtocolService> _logger;
    private readonly uint _senderId;
    private readonly int _chunkSize;
    private int _nextPacketId;
    private bool _disposed;

    public ProtocolService(
        ICommunicationPort port,
        IPacketDecoder decoder,
        uint senderId,
        ILogger<ProtocolService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(decoder);
        _port = port;
        _decoder = decoder;
        _logger = logger ?? NullLogger<ProtocolService>.Instance;
        _reassembler = new PacketReassembler();
        _senderId = senderId;
        _chunkSize = ChunkSizeFor(port.Kind);
        _port.PacketReceived += OnPortPacketReceived;
    }

    /// <summary>
    /// Evento applicativo decodificato: emesso per ogni pacchetto completo
    /// riassemblato e riconosciuto dal <see cref="IPacketDecoder"/>.
    /// </summary>
    public event EventHandler<AppLayerDecodedEvent>? AppLayerDecoded;

    /// <summary>
    /// Indirizzo del mittente STEM (il nostro). Usato nei payload dei protocolli
    /// applicativi che devono specificare dove ricevere le risposte (es.
    /// <c>CMD_CONFIGURE_TELEMETRY</c>).
    /// </summary>
    public uint SenderId => _senderId;

    /// <summary>
    /// Invia un comando senza attendere risposta (fire-and-forget).
    /// </summary>
    public async Task SendCommandAsync(
        uint recipientId,
        Command command,
        byte[] payload,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(payload);
        ct.ThrowIfCancellationRequested();

        var (cmdInit, cmdOpt) = ParseCommandCode(command);
        var transportPacket = BuildTransportPacket(cmdInit, cmdOpt, payload);
        var chunks = SplitIntoChunks(transportPacket);
        byte packetId = NextPacketId();

        for (int i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var netInfo = new NetInfo(
                RemainingChunks: chunks.Count - i - 1,
                SetLength: i == 0,
                PacketId: packetId,
                Version: DefaultVersion);
            var wireFrame = BuildWireFrame(netInfo, recipientId, chunks[i]);
            await _port.SendAsync(wireFrame, ct);
        }
    }

    /// <summary>
    /// Invia un comando e attende la prima risposta che soddisfi
    /// <paramref name="replyValidator"/> entro <paramref name="timeout"/>.
    /// Ritorna <c>true</c> se la risposta arriva in tempo, <c>false</c> su timeout.
    /// </summary>
    public async Task<bool> SendCommandAndWaitReplyAsync(
        uint recipientId,
        Command command,
        byte[] payload,
        Func<AppLayerDecodedEvent, bool> replyValidator,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(replyValidator);

        // Default TCS (no RunContinuationsAsynchronously): la continuation
        // di WaitAsync può girare inline sul thread che chiama TrySetResult.
        // Nel caso tipico del protocollo STEM questo thread è la receive loop
        // del driver HW — accettabile perché il lavoro post-reply (ritorno del
        // bool e completamento di sendTask) è trascurabile. Ha l'effetto
        // collaterale positivo di evitare una hop su thread pool che, sotto
        // contention, può introdurre latenze artificiali.
        var tcs = new TaskCompletionSource<bool>();

        void Handler(object? sender, AppLayerDecodedEvent evt)
        {
            if (replyValidator(evt)) tcs.TrySetResult(true);
        }

        AppLayerDecoded += Handler;
        try
        {
            await SendCommandAsync(recipientId, command, payload, ct)
                .ConfigureAwait(false);
            try
            {
                return await tcs.Task.WaitAsync(timeout, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return false;
            }
        }
        finally
        {
            AppLayerDecoded -= Handler;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _port.PacketReceived -= OnPortPacketReceived;
        _reassembler.Reset();
    }

    private void OnPortPacketReceived(object? sender, RawPacket raw)
    {
        if (_disposed) return;
        if (raw.IsEmpty)
        {
            _logger.LogWarning(
                "Discarded empty {Channel} frame at {Timestamp:O}",
                _port.Kind, raw.Timestamp);
            return;
        }
        var normalized = StripChannelFraming(raw.Payload.AsSpan(), _port.Kind);
        if (normalized.IsEmpty)
        {
            // Issue #76: a SPARK firmware error response or BLE notification truncated by
            // Plugin.BLE arrives here and gets dropped because it's smaller than the
            // channel framing minimum. Log it so the next bench failure with a malformed
            // reply (e.g. the 1-byte 0xFF observed during the 2026-04-27 SC-006 attempts)
            // is diagnosable in one log pass instead of needing the raw chunk dump.
            _logger.LogWarning(
                "Discarded short {Channel} frame: {Length} bytes (need ≥ {Min} for NetInfo + framing). Bytes: {Hex}",
                _port.Kind, raw.Payload.Length, MinFrameSizeFor(_port.Kind),
                BitConverter.ToString(raw.Payload.AsSpan().ToArray()));
            return;
        }
        var merged = _reassembler.Accept(normalized);
        if (merged is null) return;
        var applicationPacket = new RawPacket(merged.ToImmutableArray(), raw.Timestamp);
        var evt = _decoder.Decode(applicationPacket);
        if (evt is not null)
        {
            AppLayerDecoded?.Invoke(this, evt);
            return;
        }
        // Decoder rejected the frame. Reproduce its null-return conditions so the log
        // distinguishes "too short for the TP+AL header" from "command code not in the
        // dictionary" — the latter is how a missing reply-code registration surfaces
        // (e.g. an API/Excel parity gap silently dropping the device's reply).
        string reason = merged.Length < PacketDecoder.MinPayloadLength
            ? $"length({merged.Length}) < {PacketDecoder.MinPayloadLength}"
            : $"unknown-cmd(0x{merged[PacketDecoder.CommandHighIndex]:X2}{merged[PacketDecoder.CommandLowIndex]:X2})";
        _logger.LogWarning(
            "Decoder dropped {Channel} frame: {Reason} len={Len}",
            _port.Kind, reason, merged.Length);
    }

    /// <summary>
    /// Minimum on-wire frame size before <see cref="StripChannelFraming"/> can produce
    /// a non-empty NetInfo+chunk: 4 bytes for CAN (arbId prefix), 6 for BLE/Serial
    /// (recipient prefix). Used only for the discard-warning text in
    /// <see cref="OnPortPacketReceived"/>.
    /// </summary>
    private static int MinFrameSizeFor(ChannelKind kind) => kind switch
    {
        ChannelKind.Can => 4,
        ChannelKind.Ble or ChannelKind.Serial => 6,
        _ => 0,
    };

    /// <summary>
    /// Normalizza il frame in input a <c>[NetInfo(2) + chunk(N)]</c> secondo
    /// il canale:
    /// <list type="bullet">
    /// <item><description>CAN — rimuove il prefisso <c>arbId_LE(4)</c> della convention A di <see cref="CanPort"/>, resta <c>[NetInfo + chunk]</c>.</description></item>
    /// <item><description>BLE/Serial — rimuove il <c>recipientId_LE(4)</c> a offset [2..5], concatena NetInfo + chunk. Equivalente alla trasformazione di <c>PacketManager.ProcessBLE/SerialPacket</c>.</description></item>
    /// </list>
    /// </summary>
    private static ReadOnlySpan<byte> StripChannelFraming(
        ReadOnlySpan<byte> frame, ChannelKind kind)
    {
        return kind switch
        {
            ChannelKind.Can => frame.Length >= 4 ? frame[4..] : [],
            ChannelKind.Ble or ChannelKind.Serial =>
                frame.Length >= 6 ? StripRecipientId(frame) : [],
            _ => []
        };
    }

    private static byte[] StripRecipientId(ReadOnlySpan<byte> frame)
    {
        // [NetInfo(2) + recipientId(4) + chunk(N)] → [NetInfo(2) + chunk(N)]
        var result = new byte[frame.Length - 4];
        frame[..2].CopyTo(result);
        frame[6..].CopyTo(result.AsSpan(2));
        return result;
    }

    private byte[] BuildWireFrame(NetInfo netInfo, uint recipientId, byte[] chunk)
    {
        var (lo, hi) = netInfo.ToBytes();
        return _port.Kind switch
        {
            ChannelKind.Can => BuildCanWireFrame(recipientId, lo, hi, chunk),
            ChannelKind.Ble or ChannelKind.Serial =>
                BuildSerialWireFrame(recipientId, lo, hi, chunk),
            _ => throw new InvalidOperationException(
                $"ChannelKind {_port.Kind} non supportato.")
        };
    }

    private static byte[] BuildCanWireFrame(
        uint arbId, byte netInfoLo, byte netInfoHi, byte[] chunk)
    {
        // CanPort convention A: [arbId_LE(4) + NetInfo(2) + chunk(≤6)]
        var frame = new byte[4 + 2 + chunk.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), arbId);
        frame[4] = netInfoLo;
        frame[5] = netInfoHi;
        Buffer.BlockCopy(chunk, 0, frame, 6, chunk.Length);
        return frame;
    }

    private static byte[] BuildSerialWireFrame(
        uint recipientId, byte netInfoLo, byte netInfoHi, byte[] chunk)
    {
        // Pass-through [NetInfo(2) + recipientId_LE(4) + chunk(≤98)]
        var frame = new byte[2 + 4 + chunk.Length];
        frame[0] = netInfoLo;
        frame[1] = netInfoHi;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(2, 4), recipientId);
        Buffer.BlockCopy(chunk, 0, frame, 6, chunk.Length);
        return frame;
    }

    private List<byte[]> SplitIntoChunks(byte[] data)
    {
        var chunks = new List<byte[]>();
        for (int offset = 0; offset < data.Length; offset += _chunkSize)
        {
            int size = Math.Min(_chunkSize, data.Length - offset);
            var chunk = new byte[size];
            Buffer.BlockCopy(data, offset, chunk, 0, size);
            chunks.Add(chunk);
        }
        if (chunks.Count == 0) chunks.Add([]);
        return chunks;
    }

    private byte[] BuildTransportPacket(byte cmdInit, byte cmdOpt, byte[] payload)
    {
        // Application Layer: [cmdInit + cmdOpt + payload]
        int alLength = 2 + payload.Length;
        // Transport header: [cryptFlag + senderId_BE(4) + lPack_BE(2)]
        // + trailing CRC_BE(2)
        var packet = new byte[1 + 4 + 2 + alLength + 2];
        packet[0] = DefaultCryptFlag;
        // senderId big-endian (quirk legacy, vedi PROTOCOL.md §3.1)
        packet[1] = (byte)((_senderId >> 24) & 0xFF);
        packet[2] = (byte)((_senderId >> 16) & 0xFF);
        packet[3] = (byte)((_senderId >> 8) & 0xFF);
        packet[4] = (byte)(_senderId & 0xFF);
        // lPack big-endian (lunghezza dell'Application Layer)
        ushort lPack = (ushort)alLength;
        packet[5] = (byte)((lPack >> 8) & 0xFF);
        packet[6] = (byte)(lPack & 0xFF);
        packet[7] = cmdInit;
        packet[8] = cmdOpt;
        Buffer.BlockCopy(payload, 0, packet, 9, payload.Length);
        // CRC16 Modbus su [header + AL] (senza i 2 byte CRC)
        ushort crc = Crc16Modbus(packet.AsSpan(0, packet.Length - 2));
        packet[^2] = (byte)((crc >> 8) & 0xFF);
        packet[^1] = (byte)(crc & 0xFF);
        return packet;
    }

    /// <summary>
    /// CRC16 Modbus (poly 0xA001, init 0xFFFF). Parità con
    /// <c>App.STEMProtocol.TransportLayer.Crc16</c>.
    /// </summary>
    private static ushort Crc16Modbus(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x0001) != 0) crc = (ushort)((crc >> 1) ^ 0xA001);
                else crc >>= 1;
            }
        }
        return crc;
    }

    private byte NextPacketId()
    {
        int raw = Interlocked.Increment(ref _nextPacketId);
        return (byte)(((raw - 1) % 7) + 1);
    }

    private static int ChunkSizeFor(ChannelKind kind) => kind switch
    {
        ChannelKind.Can => 6,
        ChannelKind.Ble => 98,
        ChannelKind.Serial => 98,
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind),
            $"ChannelKind {kind} non supportato.")
    };

    private static (byte CmdInit, byte CmdOpt) ParseCommandCode(Command command)
    {
        byte cmdInit = byte.Parse(
            command.CodeHigh,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);
        byte cmdOpt = byte.Parse(
            command.CodeLow,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);
        return (cmdInit, cmdOpt);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
