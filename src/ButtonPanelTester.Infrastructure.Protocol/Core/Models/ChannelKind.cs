namespace Core.Models;

/// <summary>
/// Tipo di canale fisico esposto da una <see cref="Interfaces.ICommunicationPort"/>.
/// Discrimina il framing atteso dal protocollo STEM:
/// <list type="bullet">
/// <item><description><see cref="Can"/> — frame a 8 byte (NetInfo 2 + chunk 6), arbitrationId metadata separato</description></item>
/// <item><description><see cref="Ble"/> — frame fino a 104 byte (NetInfo 2 + recipientId 4 + chunk 98), recipientId in-payload</description></item>
/// <item><description><see cref="Serial"/> — stesso layout di <see cref="Ble"/></description></item>
/// </list>
/// Vedi <c>Docs/PROTOCOL.md</c> §4.3 per le chunk size e §5-6 per le pipeline.
/// </summary>
public enum ChannelKind
{
    Can,
    Ble,
    Serial
}
