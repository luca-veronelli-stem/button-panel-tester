using Core.Models;

namespace Core.Interfaces;

/// <summary>
/// Configurazione read-only di una variante device. Sostituisce i simboli
/// di preprocessore <c>#if TOPLIFT/EDEN/EGICON</c> con un valore a runtime.
///
/// In Fase 1 espone solo le proprietà base. In Fase 3 saranno aggiunti
/// feature flag booleani — vedi <c>Docs/PREPROCESSOR_DIRECTIVES.md</c>.
/// </summary>
public interface IDeviceVariantConfig
{
    /// <summary>Variante del device.</summary>
    DeviceVariant Variant { get; }

    /// <summary>RecipientId di default. 0 = risolvere per nome a runtime.</summary>
    uint DefaultRecipientId { get; }

    /// <summary>Nome macchina nel dizionario (vuoto se non applicabile).</summary>
    string DeviceName { get; }

    /// <summary>Nome scheda nel dizionario (vuoto se non applicabile).</summary>
    string BoardName { get; }

    /// <summary>
    /// Indirizzo STEM del mittente (il nostro). Usato dal Transport Layer e nei
    /// payload applicativi che richiedono l'indirizzo di destinazione delle
    /// risposte (es. <c>CMD_CONFIGURE_TELEMETRY</c>). Storicamente <c>8</c> per
    /// tutte le varianti; configurabile da <c>appsettings.json</c>
    /// (<c>Device:SenderId</c>).
    /// </summary>
    uint SenderId { get; }

    /// <summary>
    /// Canale di default all'avvio. Usato da <c>ConnectionManager</c> come
    /// valore iniziale; non viene aperto automaticamente (spetta al consumer
    /// chiamare <c>SwitchToAsync</c>). Riflette il legacy:
    /// TOPLIFT → CAN, altre varianti → BLE.
    /// </summary>
    ChannelKind DefaultChannel { get; }

    /// <summary>
    /// Titolo della finestra principale (e label della splash screen).
    /// Rimpiazza il blocco <c>#if TOPLIFT/EDEN/EGICON</c> sul testo della form
    /// (vedi <c>Docs/PREPROCESSOR_DIRECTIVES.md</c> blocco #2). Il caller è
    /// responsabile di appendere la versione software.
    /// </summary>
    string WindowTitle { get; }

    /// <summary>
    /// Lista dispositivi pre-popolati nel tab bootloader smart. Vuota per la
    /// variante <see cref="DeviceVariant.Generic"/> e per <see cref="DeviceVariant.Egicon"/>.
    /// Rimpiazza il blocco <c>#if TOPLIFT/EDEN</c> in <c>Form1</c>
    /// (vedi <c>Docs/PREPROCESSOR_DIRECTIVES.md</c> blocco #4).
    /// </summary>
    IReadOnlyList<SmartBootDeviceEntry> SmartBootDevices { get; }
}
