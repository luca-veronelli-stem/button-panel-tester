using System.Collections.Immutable;

namespace Core.Models;

/// <summary>
/// Pacchetto raw ricevuto da una <c>ICommunicationPort</c>.
/// Non ancora decodificato a livello applicativo (per quello vedi
/// <see cref="AppLayerDecodedEvent"/>).
/// </summary>
/// <param name="Payload">Byte del pacchetto. Equality strutturale garantita da <see cref="Equals(RawPacket)"/>.</param>
/// <param name="Timestamp">Istante di ricezione.</param>
public sealed record RawPacket(
    ImmutableArray<byte> Payload,
    DateTime Timestamp)
{
    /// <summary>Numero di byte del payload.</summary>
    public int Length => Payload.IsDefault ? 0 : Payload.Length;

    /// <summary>True se il payload è vuoto o default.</summary>
    public bool IsEmpty => Payload.IsDefaultOrEmpty;

    /// <inheritdoc/>
    public bool Equals(RawPacket? other)
    {
        if (other is null) return false;
        return Timestamp == other.Timestamp
            && ImmutableArrayEquality.SequenceEqual(Payload, other.Payload);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(Timestamp, ImmutableArrayEquality.SequenceHash(Payload));
}
