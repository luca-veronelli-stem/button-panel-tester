using System.Collections.Immutable;
using System.Globalization;
using Core.Models;

namespace Services.Protocol;

/// <summary>
/// Snapshot immutabile del dizionario usato da <see cref="PacketDecoder"/>.
/// Incapsula liste read-only di comandi, variabili e indirizzi protocollo e
/// offre i lookup necessari alla decodifica.
///
/// Lo snapshot è prodotto al costruttore del decoder e sostituito
/// atomicamente tramite <see cref="PacketDecoder.UpdateDictionary"/>.
/// </summary>
public sealed record DictionarySnapshot(
    ImmutableArray<Command> Commands,
    ImmutableArray<Variable> Variables,
    ImmutableArray<ProtocolAddress> Addresses)
{
    /// <summary>Snapshot vuoto, utile per inizializzazioni.</summary>
    public static DictionarySnapshot Empty { get; } = new(
        ImmutableArray<Command>.Empty,
        ImmutableArray<Variable>.Empty,
        ImmutableArray<ProtocolAddress>.Empty);

    /// <summary>
    /// Trova un comando per i byte di codice (codeHigh/codeLow).
    /// Restituisce null se nessun comando combacia.
    /// </summary>
    public Command? FindCommand(byte codeHigh, byte codeLow)
    {
        foreach (var cmd in Commands)
        {
            if (HexByteEquals(cmd.CodeHigh, codeHigh)
                && HexByteEquals(cmd.CodeLow, codeLow))
            {
                return cmd;
            }
        }
        return null;
    }

    /// <summary>
    /// Trova una variabile per i byte di indirizzo (addressHigh/addressLow).
    /// Restituisce null se nessuna variabile combacia.
    /// </summary>
    public Variable? FindVariable(byte addressHigh, byte addressLow)
    {
        foreach (var variable in Variables)
        {
            if (HexByteEquals(variable.AddressHigh, addressHigh)
                && HexByteEquals(variable.AddressLow, addressLow))
            {
                return variable;
            }
        }
        return null;
    }

    /// <summary>
    /// Trova un <see cref="ProtocolAddress"/> dato un senderId a 32 bit.
    /// Restituisce null se nessun indirizzo combacia.
    /// </summary>
    public ProtocolAddress? FindSender(uint senderId)
    {
        foreach (var addr in Addresses)
        {
            if (HexUInt32Equals(addr.Address, senderId))
            {
                return addr;
            }
        }
        return null;
    }

    private static bool HexByteEquals(string hex, byte value)
    {
        if (string.IsNullOrEmpty(hex)) return false;
        return byte.TryParse(
            hex.AsSpan(),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out var parsed) && parsed == value;
    }

    private static bool HexUInt32Equals(string hex, uint value)
    {
        if (string.IsNullOrEmpty(hex)) return false;
        // ProtocolAddress.Address arriva nel formato "0x00080381" (con prefisso).
        // uint.TryParse(NumberStyles.HexNumber) non accetta il prefisso: lo rimuoviamo.
        var span = hex.AsSpan();
        if (span.Length >= 2 && span[0] == '0' && (span[1] == 'x' || span[1] == 'X'))
            span = span[2..];
        return uint.TryParse(
            span,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out var parsed) && parsed == value;
    }
}
