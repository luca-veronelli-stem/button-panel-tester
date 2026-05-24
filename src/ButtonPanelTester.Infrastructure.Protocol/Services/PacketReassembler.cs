namespace Services.Protocol;

/// <summary>
/// Riassembler stateful di pacchetti applicativi STEM a partire dai chunk del
/// Network Layer. Ogni chunk è composto da <c>[NetInfo(2) + chunkData(N)]</c>;
/// i chunk con lo stesso <see cref="NetInfo.PacketId"/> vengono accumulati
/// finché <see cref="NetInfo.RemainingChunks"/> non raggiunge 0 (ultimo chunk).
///
/// <para><b>Thread-safety:</b></para>
/// Un singolo <see cref="Lock"/> protegge l'intero accesso al buffer. Gli
/// accettatori concorrenti sono serializzati — il driver HW tipicamente chiama
/// <see cref="Accept"/> da un thread dedicato, ma più canali attivi possono
/// richiedere safety.
///
/// <para><b>Isolamento per packetId:</b></para>
/// Il rolling code a 3 bit (1..7) distingue pacchetti applicativi sovrapposti
/// nel tempo (es. una risposta imprevista ricevuta mentre un upload firmware
/// è in corso). Ogni <see cref="NetInfo.PacketId"/> ha un buffer indipendente.
///
/// <para><b>Known gap (da <c>Docs/PROTOCOL.md</c>):</b></para>
/// Nessun timeout sui buffer incompleti. Un pacchetto con chunk mancanti
/// resta in memoria indefinitamente (parità col comportamento originale di
/// <c>PacketManager.packetQueues</c>). Valutare TTL in Fase 3.
/// </summary>
public sealed class PacketReassembler
{
    private const int MinChunkLength = 2; // almeno i 2 byte di NetInfo

    private readonly Lock _lock = new();
    private readonly Dictionary<int, List<byte[]>> _buffers = [];

    /// <summary>
    /// Accetta un chunk dal canale fisico. I primi due byte sono il NetInfo,
    /// il resto è il payload del chunk.
    /// <list type="bullet">
    /// <item><description>Se il chunk è troppo corto (meno di 2 byte) ritorna <c>null</c>.</description></item>
    /// <item><description>Se <see cref="NetInfo.RemainingChunks"/> > 0, bufferizza e ritorna <c>null</c>.</description></item>
    /// <item><description>Se <see cref="NetInfo.RemainingChunks"/> == 0, concatena i chunk accumulati per quel <see cref="NetInfo.PacketId"/>, svuota il buffer e ritorna il payload applicativo completo.</description></item>
    /// </list>
    /// </summary>
    public byte[]? Accept(ReadOnlySpan<byte> chunkWithNetInfo)
    {
        if (chunkWithNetInfo.Length < MinChunkLength) return null;
        var netInfo = NetInfo.Parse(chunkWithNetInfo[0], chunkWithNetInfo[1]);
        var chunkData = chunkWithNetInfo[MinChunkLength..].ToArray();
        return Accept(netInfo, chunkData);
    }

    /// <summary>
    /// Variante di <see cref="Accept(ReadOnlySpan{byte})"/> con NetInfo già
    /// parsato. Utile al caller che abbia bisogno di ispezionare il NetInfo
    /// prima di passare il chunk al riassembler.
    /// </summary>
    public byte[]? Accept(NetInfo netInfo, byte[] chunkData)
    {
        ArgumentNullException.ThrowIfNull(chunkData);
        lock (_lock)
        {
            if (!_buffers.TryGetValue(netInfo.PacketId, out var accumulated))
            {
                accumulated = [];
                _buffers[netInfo.PacketId] = accumulated;
            }
            accumulated.Add(chunkData);

            if (netInfo.RemainingChunks == 0)
            {
                var merged = Merge(accumulated);
                _buffers.Remove(netInfo.PacketId);
                return merged;
            }
            return null;
        }
    }

    /// <summary>
    /// Svuota tutti i buffer in corso. Utile per reset su cambio canale o
    /// per test. Non emette eventi — i pacchetti parziali in coda vengono
    /// scartati silenziosamente.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _buffers.Clear();
        }
    }

    /// <summary>
    /// Numero di pacchetti parzialmente ricevuti (per diagnostica/test).
    /// </summary>
    public int PendingPacketCount
    {
        get { lock (_lock) return _buffers.Count; }
    }

    private static byte[] Merge(List<byte[]> chunks)
    {
        int total = 0;
        foreach (var c in chunks) total += c.Length;
        var merged = new byte[total];
        int offset = 0;
        foreach (var c in chunks)
        {
            Buffer.BlockCopy(c, 0, merged, offset, c.Length);
            offset += c.Length;
        }
        return merged;
    }
}
