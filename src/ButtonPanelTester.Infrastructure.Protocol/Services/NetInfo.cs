namespace Services.Protocol;

/// <summary>
/// Header a 16 bit presente nei primi due byte di ogni chunk del Network Layer
/// STEM. Codificato little-endian sul wire.
///
/// <para>Bit layout (MSB = bit 15, LSB = bit 0):</para>
/// <list type="bullet">
/// <item><description>bit 15..6: <see cref="RemainingChunks"/> (10 bit, max 1023)</description></item>
/// <item><description>bit 5: <see cref="SetLength"/> (1 bit, 1 = primo chunk)</description></item>
/// <item><description>bit 4..2: <see cref="PacketId"/> (3 bit, rolling code 1..7)</description></item>
/// <item><description>bit 1..0: <see cref="Version"/> (2 bit, protocollo STEM attuale = 1)</description></item>
/// </list>
///
/// Vedi <c>Docs/PROTOCOL.md</c> §4.1 per il dettaglio completo.
/// </summary>
public readonly record struct NetInfo(
    int RemainingChunks,
    bool SetLength,
    int PacketId,
    int Version)
{
    /// <summary>
    /// Parsing dai due byte del chunk (little-endian sul wire):
    /// <c>raw = (hi &lt;&lt; 8) | lo</c>.
    /// </summary>
    public static NetInfo Parse(byte lo, byte hi)
    {
        int raw = (hi << 8) | lo;
        return new NetInfo(
            RemainingChunks: (raw >> 6) & 0x3FF,
            SetLength: ((raw >> 5) & 0x01) == 1,
            PacketId: (raw >> 2) & 0x07,
            Version: raw & 0x03);
    }

    /// <summary>
    /// Codifica inversa: produce i due byte little-endian pronti da
    /// inserire in testa al chunk sul wire.
    /// </summary>
    public (byte Lo, byte Hi) ToBytes()
    {
        int raw = ((RemainingChunks & 0x3FF) << 6)
                | ((SetLength ? 1 : 0) << 5)
                | ((PacketId & 0x07) << 2)
                | (Version & 0x03);
        return ((byte)(raw & 0xFF), (byte)((raw >> 8) & 0xFF));
    }
}
