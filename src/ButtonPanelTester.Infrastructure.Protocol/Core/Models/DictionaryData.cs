namespace Core.Models;

/// <summary>
/// Dati aggregati del protocollo: indirizzi board e comandi globali.
/// Restituito da IDictionaryProvider.LoadProtocolDataAsync.
/// </summary>
public record DictionaryData(
    IReadOnlyList<ProtocolAddress> Addresses,
    IReadOnlyList<Command> Commands);
