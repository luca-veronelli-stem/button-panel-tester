using System.Collections.Immutable;
using Core.Interfaces;
using Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Services.Protocol;

/// <summary>
/// Decoder puro di pacchetti applicativi STEM. Da un <see cref="RawPacket"/>
/// già riassemblato produce un <see cref="AppLayerDecodedEvent"/> basandosi
/// su uno snapshot immutabile del dizionario.
///
/// Struttura attesa del payload (post-riassembly):
/// <code>
/// byte 0    : cryptFlag           (Transport Layer)
/// byte 1..4 : senderId BE         (Transport Layer, big-endian sul wire)
/// byte 5..6 : lPack               (Transport Layer, non letto)
/// byte 7    : cmdInit = codeHigh  (Application Layer)
/// byte 8    : cmdOpt  = codeLow   (Application Layer)
/// byte 9..N : payload applicativo
/// byte N+1..N+2 : CRC16 Modbus    (scartato, non validato)
/// </code>
///
/// Lo snapshot è sostituibile atomicamente tramite <see cref="UpdateDictionary"/>:
/// l'accesso avviene via <see cref="Volatile.Read"/>/<see cref="Volatile.Write"/>
/// per garantire coerenza senza lock.
/// </summary>
public sealed class PacketDecoder : IPacketDecoder
{
    internal const int MinPayloadLength = 9;
    private const int CrcTailLength = 2;
    private const int ApplicationPayloadStart = 9;
    internal const int CommandHighIndex = 7;
    internal const int CommandLowIndex = 8;
    private const int VariableHighIndex = 9;
    private const int VariableLowIndex = 10;

    private DictionarySnapshot _snapshot;
    private readonly ILogger<PacketDecoder> _logger;

    public PacketDecoder(
        IReadOnlyList<Command> commands,
        IReadOnlyList<Variable> variables,
        IReadOnlyList<ProtocolAddress> addresses,
        ILogger<PacketDecoder>? logger = null)
    {
        _snapshot = Build(commands, variables, addresses);
        _logger = logger ?? NullLogger<PacketDecoder>.Instance;
    }

    /// <summary>
    /// Sostituisce atomicamente lo snapshot del dizionario.
    /// Thread-safe: un <see cref="Decode"/> in corso vede lo snapshot vecchio
    /// o il nuovo, mai uno intermedio.
    /// </summary>
    public void UpdateDictionary(
        IReadOnlyList<Command> commands,
        IReadOnlyList<Variable> variables,
        IReadOnlyList<ProtocolAddress> addresses)
    {
        Volatile.Write(ref _snapshot, Build(commands, variables, addresses));
    }

    /// <inheritdoc/>
    public AppLayerDecodedEvent? Decode(RawPacket packet)
    {
        if (packet.IsEmpty || packet.Length < MinPayloadLength) return null;
        var snapshot = Volatile.Read(ref _snapshot);
        var senderId = ReadSenderIdBigEndian(packet.Payload);
        var command = ResolveCommand(packet.Payload, snapshot)
            ?? SynthesizeUnknownReplyCommand(packet.Payload, senderId);
        if (command is null) return null;
        var variable = ResolveVariable(command, packet.Payload, snapshot);
        var sender = snapshot.FindSender(senderId);
        var appPayload = ExtractApplicationPayload(packet.Payload);
        return new AppLayerDecodedEvent(
            command,
            variable,
            appPayload,
            sender?.DeviceName ?? "",
            sender?.BoardName ?? "",
            senderId);
    }

    /// <summary>
    /// Defense in depth for incomplete dictionaries: when a reply-shaped command
    /// (high bit set on cmdHigh) is unknown, synthesize a placeholder so the
    /// AppLayerDecoded event still fires and bench observers see the frame
    /// instead of a silent drop. Fire-side unknowns (high bit clear) stay
    /// rejected to avoid masking corrupt frames. See #100.
    /// </summary>
    private Command? SynthesizeUnknownReplyCommand(ImmutableArray<byte> payload, uint senderId)
    {
        byte h = payload[CommandHighIndex];
        byte l = payload[CommandLowIndex];
        if ((h & 0x80) == 0) return null;
        _logger.LogWarning(
            "Unknown reply command 0x{High:X2}{Low:X2} from sender 0x{SenderId:X8}; synthesizing placeholder.",
            h, l, senderId);
        return new Command(
            $"Unknown reply 0x{h:X2}{l:X2}",
            h.ToString("X2"),
            l.ToString("X2"));
    }

    private static DictionarySnapshot Build(
        IReadOnlyList<Command> commands,
        IReadOnlyList<Variable> variables,
        IReadOnlyList<ProtocolAddress> addresses)
    {
        return new DictionarySnapshot(
            commands.ToImmutableArray(),
            variables.ToImmutableArray(),
            addresses.ToImmutableArray());
    }

    private static Command? ResolveCommand(
        ImmutableArray<byte> payload,
        DictionarySnapshot snapshot)
    {
        return snapshot.FindCommand(
            payload[CommandHighIndex],
            payload[CommandLowIndex]);
    }

    private static Variable? ResolveVariable(
        Command command,
        ImmutableArray<byte> payload,
        DictionarySnapshot snapshot)
    {
        if (!IsReadOrWriteVariable(command)) return null;
        if (payload.Length <= VariableLowIndex) return null;
        return snapshot.FindVariable(
            payload[VariableHighIndex],
            payload[VariableLowIndex]);
    }

    /// <summary>
    /// Legge i byte 1..4 del TP come uint big-endian. È la convention wire
    /// trasmessa dal firmware (e specularmente scritta da <c>ProtocolService.BuildTransportPacket</c>).
    /// Il dizionario (API Azure / Excel) contiene gli indirizzi già in formato standard
    /// (es. <c>ProtocolAddress.Address = "0x000A0441"</c>), quindi la lettura BE
    /// produce direttamente il valore per il lookup <c>FindSender</c>.
    /// </summary>
    private static uint ReadSenderIdBigEndian(ImmutableArray<byte> payload)
    {
        return ((uint)payload[1] << 24)
             | ((uint)payload[2] << 16)
             | ((uint)payload[3] << 8)
             | payload[4];
    }

    private static ImmutableArray<byte> ExtractApplicationPayload(
        ImmutableArray<byte> payload)
    {
        var end = payload.Length - CrcTailLength;
        if (end <= ApplicationPayloadStart) return ImmutableArray<byte>.Empty;
        var builder = ImmutableArray.CreateBuilder<byte>(end - ApplicationPayloadStart);
        for (int i = ApplicationPayloadStart; i < end; i++)
        {
            builder.Add(payload[i]);
        }
        return builder.MoveToImmutable();
    }

    private static bool IsReadOrWriteVariable(Command command)
    {
        return (command.CodeHigh == "00" || command.CodeHigh == "80")
            && (command.CodeLow == "01" || command.CodeLow == "02");
    }
}
