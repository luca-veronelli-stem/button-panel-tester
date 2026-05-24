using Core.Models;

namespace Core.Interfaces;

/// <summary>
/// Astrazione del facade del protocollo STEM. Espone le operazioni
/// orientate al comando (encode TP+CRC+chunking+framing per canale, decode +
/// reassembly + dispatch eventi) e il pattern request/reply.
///
/// Implementazione concreta: <c>Services.Protocol.ProtocolService</c>.
///
/// <para><b>Lifecycle:</b> non è registrato in DI. Viene istanziato a runtime
/// dal consumer (in Phase 3 da un <c>ConnectionManager</c>) quando l'utente
/// sceglie il canale di comunicazione (CAN/BLE/Serial). Estende
/// <see cref="IDisposable"/> per consentire di sganciare correttamente la port
/// sottostante quando il canale viene cambiato.</para>
///
/// <para><b>Why interface:</b> permette ai consumer (TelemetryService,
/// BootService, future Phase 3) di dipendere da un contratto stabile invece
/// che dal tipo concreto, semplificando i mock nei test e l'eventuale swap
/// con l'adapter <c>Stem.Communication</c> in Phase 4.</para>
/// </summary>
public interface IProtocolService : IDisposable
{
    /// <summary>
    /// Indirizzo del mittente STEM (il nostro). Usato dai consumer che devono
    /// riempire campi "destinazione" nel payload applicativo (es.
    /// <c>CMD_CONFIGURE_TELEMETRY</c>).
    /// </summary>
    uint SenderId { get; }

    /// <summary>
    /// Evento applicativo decodificato: emesso per ogni pacchetto completo
    /// riassemblato e riconosciuto dal decoder sottostante.
    /// </summary>
    event EventHandler<AppLayerDecodedEvent>? AppLayerDecoded;

    /// <summary>
    /// Invia un comando senza attendere risposta (fire-and-forget).
    /// Encode TP+CRC, chunking e framing per canale sono gestiti internamente.
    /// </summary>
    Task SendCommandAsync(
        uint recipientId,
        Command command,
        byte[] payload,
        CancellationToken ct = default);

    /// <summary>
    /// Invia un comando e attende la prima risposta che soddisfi
    /// <paramref name="replyValidator"/> entro <paramref name="timeout"/>.
    /// Ritorna <c>true</c> se la risposta arriva in tempo, <c>false</c> su timeout.
    /// </summary>
    Task<bool> SendCommandAndWaitReplyAsync(
        uint recipientId,
        Command command,
        byte[] payload,
        Func<AppLayerDecodedEvent, bool> replyValidator,
        TimeSpan timeout,
        CancellationToken ct = default);
}
