namespace Core.Models;

/// <summary>
/// Entry della lista dispositivi visibili nel tab bootloader smart.
/// Rimpiazza i valori hardcoded nei blocchi <c>#if TOPLIFT/EDEN</c> di
/// <c>Form1</c> (vedi <c>Docs/PREPROCESSOR_DIRECTIVES.md</c> blocco #4).
/// </summary>
/// <param name="Address">Indirizzo STEM del dispositivo.</param>
/// <param name="Name">Etichetta visualizzata nella UI.</param>
/// <param name="IsKeyboard">
/// <c>true</c> per le tastiere (selezionabili come opzionali nel flusso smart-boot),
/// <c>false</c> per la scheda madre (obbligatoria).
/// </param>
public sealed record SmartBootDeviceEntry(uint Address, string Name, bool IsKeyboard);
