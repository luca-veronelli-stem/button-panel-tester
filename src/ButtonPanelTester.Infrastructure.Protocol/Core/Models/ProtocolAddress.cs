namespace Core.Models;

/// <summary>
/// Indirizzo protocollo di una board (ex ExcelHandler.RowData).
/// Identifica univocamente una board tramite macchina, scheda e indirizzo hex.
/// </summary>
public record ProtocolAddress(
    string DeviceName,
    string BoardName,
    string Address);
