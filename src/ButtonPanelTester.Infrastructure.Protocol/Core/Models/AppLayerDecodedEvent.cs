using System.Collections.Immutable;

namespace Core.Models;

/// <summary>
/// Evento applicativo decodificato, emesso da <c>IPacketDecoder</c>.
/// Contiene il comando, la variabile opzionale, il payload raw e
/// l'identità del mittente.
/// </summary>
/// <param name="Command">Comando decodificato dal pacchetto.</param>
/// <param name="Variable">Variabile associata (solo per comandi di read/write variabile). Null negli altri casi.</param>
/// <param name="Payload">Byte grezzi del pacchetto. Equality strutturale garantita da <see cref="Equals(AppLayerDecodedEvent)"/>.</param>
/// <param name="SenderDevice">Nome del device mittente (da <see cref="ProtocolAddress.DeviceName"/>). Stringa vuota se non risolto nel dizionario.</param>
/// <param name="SenderBoard">Nome della board mittente (da <see cref="ProtocolAddress.BoardName"/>). Stringa vuota se non risolto nel dizionario.</param>
/// <param name="SenderId">Indirizzo raw (uint) del mittente letto dal Transport Layer. Sempre popolato anche se SenderDevice/Board sono vuoti (per diagnostica).</param>
public sealed record AppLayerDecodedEvent(
    Command Command,
    Variable? Variable,
    ImmutableArray<byte> Payload,
    string SenderDevice,
    string SenderBoard,
    uint SenderId = 0)
{
    /// <summary>True se l'evento è associato a una variabile.</summary>
    public bool IsVariableEvent => Variable is not null;

    /// <summary>Lunghezza del payload.</summary>
    public int PayloadLength => Payload.IsDefault ? 0 : Payload.Length;

    /// <inheritdoc/>
    public bool Equals(AppLayerDecodedEvent? other)
    {
        if (other is null) return false;
        return Command == other.Command
            && Variable == other.Variable
            && SenderDevice == other.SenderDevice
            && SenderBoard == other.SenderBoard
            && SenderId == other.SenderId
            && ImmutableArrayEquality.SequenceEqual(Payload, other.Payload);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(
            Command,
            Variable,
            SenderDevice,
            SenderBoard,
            SenderId,
            ImmutableArrayEquality.SequenceHash(Payload));
}
