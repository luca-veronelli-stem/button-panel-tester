namespace Core.Models;

/// <summary>
/// Comando protocollo STEM (ex ExcelHandler.CommandData).
/// CodeHigh e CodeLow sono stringhe hex che identificano il comando.
/// </summary>
public record Command(
    string Name,
    string CodeHigh,
    string CodeLow);
