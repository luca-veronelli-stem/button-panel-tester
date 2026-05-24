namespace Core.Models;

/// <summary>
/// Variabile dizionario STEM (ex ExcelHandler.VariableData).
/// AddressHigh e AddressLow sono stringhe hex che identificano la variabile.
/// </summary>
public record Variable(
    string Name,
    string AddressHigh,
    string AddressLow,
    string DataType);
