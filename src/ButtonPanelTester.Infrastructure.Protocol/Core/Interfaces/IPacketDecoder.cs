using Core.Models;

namespace Core.Interfaces;

/// <summary>
/// Decoder puro: da <see cref="RawPacket"/> produce un <see cref="AppLayerDecodedEvent"/>.
/// Non ha stato né side effect, deve essere deterministico.
///
/// Implementazione concreta in Fase 2: <c>Services/Protocol/PacketDecoder</c>.
/// </summary>
public interface IPacketDecoder
{
    /// <summary>
    /// Decodifica un pacchetto raw. Restituisce <c>null</c> se il pacchetto
    /// non è riconoscibile o non appartiene a un comando noto.
    /// </summary>
    AppLayerDecodedEvent? Decode(RawPacket packet);

    /// <summary>
    /// Sostituisce atomicamente lo snapshot del dizionario usato per il lookup
    /// di comandi, variabili e mittenti. Chiamato dalla <c>DictionaryCache</c>
    /// ogni volta che i dati dal <see cref="IDictionaryProvider"/> cambiano.
    /// Thread-safe: un <see cref="Decode"/> in corso vede lo snapshot vecchio
    /// o il nuovo, mai uno intermedio.
    /// </summary>
    void UpdateDictionary(
        IReadOnlyList<Command> commands,
        IReadOnlyList<Variable> variables,
        IReadOnlyList<ProtocolAddress> addresses);
}
