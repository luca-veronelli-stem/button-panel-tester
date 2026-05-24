namespace Infrastructure.Protocol.Hardware;

/// <summary>
/// Astrazione minima sul driver PCAN, estratta per consentire i test
/// di <see cref="CanPort"/> senza dipendere dalle DLL native Peak.
/// Implementata da <see cref="PCANManager"/> in produzione.
/// </summary>
public interface IPcanDriver
{
    /// <summary>True se il canale CAN è aperto e operativo.</summary>
    bool IsConnected { get; }

    /// <summary>Pacchetto CAN ricevuto dal bus.</summary>
    event EventHandler<CANPacketEventArgs>? PacketReceived;

    /// <summary>Cambio di stato della connessione al driver PCAN.</summary>
    event EventHandler<bool>? ConnectionStatusChanged;

    /// <summary>
    /// Invia un messaggio CAN. Ritorna <c>true</c> se PCAN riporta successo.
    /// </summary>
    Task<bool> SendMessageAsync(uint canId, byte[] data, bool isExtended);

    /// <summary>Chiude il canale PCAN.</summary>
    void Disconnect();
}
