using System.Collections.Immutable;

namespace Core.Models;

/// <summary>
/// Campione di telemetria raccolto da <c>ITelemetryService</c>.
/// Associa una <see cref="Variable"/> al suo valore raw e al timestamp di campionamento.
///
/// Il valore deve essere interpretato in base a <see cref="Variable.DataType"/>.
///
/// <para><b>Endianness:</b> il RawValue è trasportato così come arriva sul filo.
/// Fast telemetry (<c>CMD_TELEMETRY_DATA</c>) lo invia in little-endian; la risposta
/// a one-shot read (<c>CMD_READ_VARIABLE</c> reply) lo invia in big-endian. Usare
/// <see cref="NumericValueLittleEndian"/> o <see cref="NumericValueBigEndian"/> per
/// decodificare secondo l'origine del campione.</para>
/// </summary>
/// <param name="Variable">Variabile dizionario a cui il campione si riferisce.</param>
/// <param name="RawValue">Valore grezzo. Equality strutturale garantita da <see cref="Equals(TelemetryDataPoint)"/>.</param>
/// <param name="Timestamp">Istante di campionamento.</param>
/// <param name="Source">Origine del campione — determina l'endianness usata da <see cref="NumericValue"/>.</param>
public sealed record TelemetryDataPoint(
    Variable Variable,
    ImmutableArray<byte> RawValue,
    DateTime Timestamp,
    TelemetrySource Source = TelemetrySource.FastStream)
{
    /// <summary>Numero di byte del valore raw.</summary>
    public int RawValueLength => RawValue.IsDefault ? 0 : RawValue.Length;

    /// <summary>
    /// Valore numerico decodificato in base a <see cref="Source"/>:
    /// little-endian per fast telemetry, big-endian per read reply.
    /// Ritorna 0 se il buffer è vuoto o di lunghezza non supportata (1/2/4 byte).
    /// </summary>
    public uint NumericValue => Source switch
    {
        TelemetrySource.FastStream => DecodeLittleEndian(RawValue),
        TelemetrySource.ReadReply  => DecodeBigEndian(RawValue),
        _ => 0
    };

    /// <summary>Decodifica <see cref="RawValue"/> come uint little-endian (1/2/4 byte).</summary>
    public uint NumericValueLittleEndian => DecodeLittleEndian(RawValue);

    /// <summary>Decodifica <see cref="RawValue"/> come uint big-endian (1/2/4 byte).</summary>
    public uint NumericValueBigEndian => DecodeBigEndian(RawValue);

    private static uint DecodeLittleEndian(ImmutableArray<byte> raw)
    {
        if (raw.IsDefault || raw.Length == 0) return 0;
        return raw.Length switch
        {
            1 => raw[0],
            2 => (uint)(raw[0] | (raw[1] << 8)),
            4 => (uint)(raw[0] | (raw[1] << 8) | (raw[2] << 16) | (raw[3] << 24)),
            _ => 0
        };
    }

    private static uint DecodeBigEndian(ImmutableArray<byte> raw)
    {
        if (raw.IsDefault || raw.Length == 0) return 0;
        return raw.Length switch
        {
            1 => raw[0],
            2 => (uint)((raw[0] << 8) | raw[1]),
            4 => (uint)((raw[0] << 24) | (raw[1] << 16) | (raw[2] << 8) | raw[3]),
            _ => 0
        };
    }

    /// <inheritdoc/>
    public bool Equals(TelemetryDataPoint? other)
    {
        if (other is null) return false;
        return Variable == other.Variable
            && Timestamp == other.Timestamp
            && Source == other.Source
            && ImmutableArrayEquality.SequenceEqual(RawValue, other.RawValue);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(Variable, Timestamp, Source, ImmutableArrayEquality.SequenceHash(RawValue));
}

/// <summary>
/// Origine del campione di telemetria. Determina come il <see cref="TelemetryDataPoint.RawValue"/>
/// va interpretato numericamente.
/// </summary>
public enum TelemetrySource
{
    /// <summary>Fast telemetry stream (<c>CMD_TELEMETRY_DATA</c>). Valori in little-endian.</summary>
    FastStream,
    /// <summary>Risposta a one-shot read (<c>CMD_READ_VARIABLE</c> reply). Valori in big-endian.</summary>
    ReadReply
}
