using Core.Interfaces;

namespace Core.Models;

/// <summary>
/// Configurazione di default per una <see cref="DeviceVariant"/>.
/// Rimpiazza i blocchi <c>#if TOPLIFT/EDEN/EGICON</c> di <c>Form1.LoadDictionaryDataAsync</c>
/// (vedi <c>Docs/PREPROCESSOR_DIRECTIVES.md</c> blocco #7).
///
/// In Fase 1 sono esposte solo le 4 proprietà base. I feature flag booleani
/// saranno aggiunti in Fase 3.
/// </summary>
/// <param name="Variant">Variante del device.</param>
/// <param name="DefaultRecipientId">RecipientId fisso. 0 = risolvere per nome a runtime.</param>
/// <param name="DeviceName">Nome macchina nel dizionario (vuoto se non applicabile).</param>
/// <param name="BoardName">Nome scheda nel dizionario (vuoto se non applicabile).</param>
/// <param name="SenderId">Indirizzo STEM del mittente. Default <see cref="DefaultSenderId"/>.</param>
/// <param name="DefaultChannel">Canale di default all'avvio.</param>
/// <param name="WindowTitle">Titolo finestra / label splash specifico della variante.</param>
/// <param name="SmartBootDevices">Dispositivi pre-popolati nel bootloader smart.</param>
public sealed record DeviceVariantConfig(
    DeviceVariant Variant,
    uint DefaultRecipientId,
    string DeviceName,
    string BoardName,
    uint SenderId,
    ChannelKind DefaultChannel,
    string WindowTitle,
    IReadOnlyList<SmartBootDeviceEntry> SmartBootDevices) : IDeviceVariantConfig
{
    /// <summary>
    /// SenderId di default usato dalla factory <see cref="Create(DeviceVariant)"/>.
    /// Storicamente hardcoded a 8 per tutte le varianti nel legacy.
    /// </summary>
    public const uint DefaultSenderId = 8u;

    /// <summary>
    /// Canale di default per variante, parità con il legacy:
    /// TOPLIFT → CAN, altre → BLE.
    /// </summary>
    public static ChannelKind DefaultChannelFor(DeviceVariant variant)
        => variant == DeviceVariant.TopLift ? ChannelKind.Can : ChannelKind.Ble;

    /// <summary>
    /// Factory totale: per ogni <see cref="DeviceVariant"/> restituisce la config di default
    /// con <see cref="SenderId"/> = <see cref="DefaultSenderId"/>.
    /// Riflette il comportamento di <c>Form1.LoadDictionaryDataAsync</c>.
    /// </summary>
    public static DeviceVariantConfig Create(DeviceVariant variant)
        => Create(variant, DefaultSenderId);

    /// <summary>
    /// Factory totale con <see cref="SenderId"/> esplicito. Usata dalla DI per
    /// iniettare il valore letto da <c>appsettings.json</c> (<c>Device:SenderId</c>).
    /// Il <see cref="DefaultChannel"/> viene calcolato da
    /// <see cref="DefaultChannelFor(DeviceVariant)"/>.
    /// </summary>
    public static DeviceVariantConfig Create(DeviceVariant variant, uint senderId)
    {
        var channel = DefaultChannelFor(variant);
        var title = WindowTitleFor(variant);
        var smartDevices = SmartBootDevicesFor(variant);
        return variant switch
        {
            DeviceVariant.Generic => new DeviceVariantConfig(variant, 0u,        "",      "",      senderId, channel, title, smartDevices),
            DeviceVariant.TopLift => new DeviceVariantConfig(variant, 0x00080381u, "",     "",      senderId, channel, title, smartDevices),
            DeviceVariant.Eden    => new DeviceVariantConfig(variant, 0u,        "EDEN",  "Madre", senderId, channel, title, smartDevices),
            DeviceVariant.Egicon  => new DeviceVariantConfig(variant, 0u,        "SPARK", "HMI",   senderId, channel, title, smartDevices),
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Variante non supportata.")
        };
    }

    /// <summary>
    /// Titolo della finestra principale per variante. Riflette il legacy
    /// <c>#if TOPLIFT/EDEN/EGICON</c> di <c>Form1</c> (blocco #2 in
    /// <c>PREPROCESSOR_DIRECTIVES.md</c>). Il caller appende la versione.
    /// </summary>
    public static string WindowTitleFor(DeviceVariant variant) => variant switch
    {
        DeviceVariant.TopLift => "STEM Toplift A2 Manager ",
        DeviceVariant.Eden    => "STEM Eden XP Manager ",
        DeviceVariant.Egicon  => "STEM Spark Manager ",
        _                     => "STEM Device Manager "
    };

    // Liste statiche condivise per variante: necessario perché DeviceVariantConfig è
    // un record e l'equality su IReadOnlyList è per-reference, quindi chiamate ripetute
    // a Create(variant) devono restituire la stessa istanza di lista.
    private static readonly IReadOnlyList<SmartBootDeviceEntry> TopLiftSmartDevices =
    [
        new(0x000803C1u, "Keyboard 1",  IsKeyboard: true),
        new(0x000803C2u, "Keyboard 2",  IsKeyboard: true),
        new(0x000803C3u, "Keyboard 3",  IsKeyboard: true),
        new(0x00080381u, "Motherboard", IsKeyboard: false),
    ];

    private static readonly IReadOnlyList<SmartBootDeviceEntry> EdenSmartDevices =
    [
        new(0x00030101u, "Keyboard 1",  IsKeyboard: true),
        new(0x00030102u, "Keyboard 2",  IsKeyboard: true),
        new(0x00030141u, "Motherboard", IsKeyboard: false),
    ];

    private static readonly IReadOnlyList<SmartBootDeviceEntry> EmptySmartDevices = [];

    /// <summary>
    /// Dispositivi pre-popolati nel bootloader smart per variante. Riflette
    /// il legacy <c>#if TOPLIFT/EDEN</c> di <c>Form1</c> (blocco #4 in
    /// <c>PREPROCESSOR_DIRECTIVES.md</c>). Generic / Egicon: lista vuota.
    /// </summary>
    public static IReadOnlyList<SmartBootDeviceEntry> SmartBootDevicesFor(DeviceVariant variant) => variant switch
    {
        DeviceVariant.TopLift => TopLiftSmartDevices,
        DeviceVariant.Eden    => EdenSmartDevices,
        _                     => EmptySmartDevices
    };
}
