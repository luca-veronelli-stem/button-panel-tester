using Core.Models;

namespace Core.Interfaces;

/// <summary>
/// Astrazione su un canale fisico di comunicazione (CAN, BLE, Serial).
/// Implementazioni concrete in Fase 2: <c>Services/Hardware/CanPort</c>,
/// <c>BlePort</c>, <c>SerialPort</c>.
///
/// Contratto:
/// <list type="bullet">
/// <item><see cref="ConnectAsync"/>: apre il canale. Transizione
///   <see cref="ConnectionState.Disconnected"/> → <see cref="ConnectionState.Connecting"/>
///   → <see cref="ConnectionState.Connected"/> oppure <see cref="ConnectionState.Error"/>.</item>
/// <item><see cref="DisconnectAsync"/>: chiude il canale, torna a
///   <see cref="ConnectionState.Disconnected"/>.</item>
/// <item><see cref="SendAsync"/>: invia un pacchetto raw; fallisce con eccezione
///   se lo stato non è <see cref="ConnectionState.Connected"/>.</item>
/// <item><see cref="PacketReceived"/>: emesso per ogni pacchetto ricevuto.</item>
/// <item><see cref="StateChanged"/>: emesso a ogni transizione di stato.</item>
/// </list>
/// </summary>
public interface ICommunicationPort : IDisposable
{
    /// <summary>Tipo di canale fisico (CAN / BLE / Serial). Invariante per la vita dell'adapter.</summary>
    ChannelKind Kind { get; }

    /// <summary>Stato corrente del canale.</summary>
    ConnectionState State { get; }

    /// <summary>True se <see cref="State"/> è <see cref="ConnectionState.Connected"/>.</summary>
    bool IsConnected { get; }

    /// <summary>Pacchetto raw ricevuto dal canale.</summary>
    event EventHandler<RawPacket>? PacketReceived;

    /// <summary>Transizione di stato del canale.</summary>
    event EventHandler<ConnectionState>? StateChanged;

    /// <summary>Apre il canale.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Chiude il canale.</summary>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>Invia un pacchetto raw. Richiede <see cref="IsConnected"/> = true.</summary>
    Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default);
}
