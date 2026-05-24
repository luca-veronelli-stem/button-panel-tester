using Peak.Can.Basic.BackwardCompatibility;
using System.Text;
using TPCANHandle = System.Byte;

namespace Infrastructure.Protocol.Hardware;

// Classe per la gestione degli eventi di ricezione pacchetti
public class CANPacketEventArgs : EventArgs
{
    public uint ArbitrationId { get; }
    public byte[] Data { get; }
    public DateTime Timestamp { get; }

    public CANPacketEventArgs(uint arbitrationId, byte[] data, DateTime timestamp)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArbitrationId = arbitrationId;
        Data = data;
        Timestamp = timestamp;
    }
}

/// <summary>
/// Gestisce la comunicazione con dispositivi PEAK PCAN-USB per il bus CAN.
/// Fornisce funzionalità di connessione, lettura, scrittura e monitoraggio automatico.
/// </summary>
/// <remarks>
/// Questa classe implementa il pattern event-driven per la gestione asincrona
/// dei messaggi CAN e del monitoraggio della connessione.
/// </remarks>
/// <example>
/// <code>
/// var pcan = new PCANManager(TPCANBaudrate.PCAN_BAUD_250K);
/// pcan.PacketReceived += (s, e) => Console.WriteLine($"ID: {e.ArbitrationId:X}");
/// pcan.StartReading();
/// </code>
/// </example>

public class PCANManager : IPcanDriver
{
    private const TPCANHandle Channel = 0x51; // PCAN_USB
    private TPCANBaudrate _currentBaudRate;
    private bool _isConnected;

    // Evento per lo stato della connessione
    /// <summary>
    /// Si verifica quando cambia lo stato della connessione al dispositivo PCAN.
    /// </summary>
    public event EventHandler<bool>? ConnectionStatusChanged;
    /// <summary>
    /// Si verifica quando viene ricevuto un nuovo pacchetto CAN.
    /// </summary>
    public event EventHandler<CANPacketEventArgs>? PacketReceived;
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// Wrapper <see cref="IPcanDriver.SendMessageAsync"/> che adatta
    /// <see cref="TPCANStatus"/> a <see cref="bool"/> (true = OK).
    /// </summary>
    async Task<bool> IPcanDriver.SendMessageAsync(
        uint canId, byte[] data, bool isExtended)
    {
        var status = await SendMessageAsync(canId, data, isExtended);
        return status == TPCANStatus.PCAN_ERROR_OK;
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                ConnectionStatusChanged?.Invoke(this, _isConnected);
            }
        }
    }

    public PCANManager(TPCANBaudrate initialBaudRate = TPCANBaudrate.PCAN_BAUD_100K)
    {
        _currentBaudRate = initialBaudRate;
        Initialize(initialBaudRate);
        StartConnectionMonitoring();
    }

    public bool Initialize(TPCANBaudrate? baudRate = null)
    {
        if (baudRate.HasValue)
            _currentBaudRate = baudRate.Value;

        var result = PCANBasic.Initialize(Channel, _currentBaudRate);
        IsConnected = result == TPCANStatus.PCAN_ERROR_OK;

        return IsConnected;
    }

    private void StartConnectionMonitoring()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var status = PCANBasic.GetStatus(Channel);

                    if (status != TPCANStatus.PCAN_ERROR_OK)
                    {
                        // Perdita di connessione
                        if (IsConnected)
                        {
                            IsConnected = false;
                            StringBuilder errorTextB = new StringBuilder(256);
                            PCANBasic.GetErrorText(status, 0, errorTextB);
                            ErrorOccurred?.Invoke(this, errorTextB.ToString());
                            Disconnect();
                            //    ErrorOccurred?.Invoke(this, "Connessione persa. Tentativo di riconnessione...");
                        }

                        // Tentativo di riconnessione
                        var reconnectResult = PCANBasic.Initialize(Channel, _currentBaudRate);
                        IsConnected = reconnectResult == TPCANStatus.PCAN_ERROR_OK;

                        if (IsConnected)
                        {
                            // Riavvia il ciclo di lettura
                            StartReading();
                            ConnectionStatusChanged?.Invoke(this, true);
                        }
                    }
                    else if (!IsConnected)
                    {
                        // Riconnessione automatica
                        IsConnected = true;
                        StartReading();
                        ConnectionStatusChanged?.Invoke(this, true);
                    }
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, $"Errore nel monitoraggio della connessione: {ex.Message}");
                }

                await Task.Delay(1000); // Controllo dello stato ogni secondo
            }
        });
    }

    /// <summary>
    /// Cambia il baud rate del canale CAN a runtime
    /// </summary>
    /// <param name="newBaudRateKbps">Baud rate in kbps (100, 125, 250, 500)</param>
    /// <returns>True se il cambio è avvenuto con successo, False altrimenti</returns>
    public bool ChangeBaudRate(int newBaudRateKbps)
    {
        TPCANBaudrate newBaudRate;

        // Converti il valore in kbps nell'enum TPCANBaudrate
        switch (newBaudRateKbps)
        {
            case 100000:
                newBaudRate = TPCANBaudrate.PCAN_BAUD_100K;
                break;
            case 125000:
                newBaudRate = TPCANBaudrate.PCAN_BAUD_125K;
                break;
            case 250000:
                newBaudRate = TPCANBaudrate.PCAN_BAUD_250K;
                break;
            case 500000:
                newBaudRate = TPCANBaudrate.PCAN_BAUD_500K;
                break;
            default:
                ErrorOccurred?.Invoke(this, $"Baud rate non supportato: {newBaudRateKbps} kbps. Valori validi: 100, 125, 250, 500");
                return false;
        }

        try
        {
            // Salva lo stato della connessione
            bool wasConnected = _isConnected;

            // Disconnetti temporaneamente
            var uninitResult = PCANBasic.Uninitialize(Channel);
            if (uninitResult != TPCANStatus.PCAN_ERROR_OK)
            {
                StringBuilder errorText = new StringBuilder(256);
                PCANBasic.GetErrorText(uninitResult, 0, errorText);
                ErrorOccurred?.Invoke(this, $"Errore durante la disconnessione: {errorText}");
                return false;
            }

            // Piccolo ritardo per assicurare la completa disconnessione
            System.Threading.Thread.Sleep(100);

            // Inizializza con il nuovo baud rate
            var initResult = PCANBasic.Initialize(Channel, newBaudRate);

            if (initResult == TPCANStatus.PCAN_ERROR_OK)
            {
                _currentBaudRate = newBaudRate;
                IsConnected = true;

                // Se era connesso prima, riavvia la lettura
                if (wasConnected)
                {
                    StartReading();
                }

                return true;
            }
            else
            {
                StringBuilder errorText = new StringBuilder(256);
                PCANBasic.GetErrorText(initResult, 0, errorText);
                ErrorOccurred?.Invoke(this, $"Errore durante la reinizializzazione con baud rate {newBaudRateKbps} kbps: {errorText}");

                // Tenta di ripristinare il baud rate precedente
                var recoveryResult = PCANBasic.Initialize(Channel, _currentBaudRate);
                IsConnected = recoveryResult == TPCANStatus.PCAN_ERROR_OK;

                if (IsConnected && wasConnected)
                {
                    StartReading();
                }

                return false;
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Eccezione durante il cambio del baud rate: {ex.Message}");
            return false;
        }
    }

    // Metodo di utilità per ottenere il baud rate corrente in kbps
    public int GetCurrentBaudRateKbps()
    {
        switch (_currentBaudRate)
        {
            case TPCANBaudrate.PCAN_BAUD_100K:
                return 100;
            case TPCANBaudrate.PCAN_BAUD_125K:
                return 125;
            case TPCANBaudrate.PCAN_BAUD_250K:
                return 250;
            case TPCANBaudrate.PCAN_BAUD_500K:
                return 500;
            default:
                return 0;
        }
    }

    public void StartReading()
    {
        Task.Run(ReadCANMessagesAsync);
    }


    public async Task<TPCANStatus> SendMessageAsync(uint canId, byte[] data, bool isExtended = false)
    {
        if (!_isConnected) return TPCANStatus.PCAN_ERROR_BUSOFF;

        var canMessage = new TPCANMsg
        {
            ID = canId,
            LEN = (byte)Math.Min(data.Length, 8),
            MSGTYPE = isExtended ? TPCANMessageType.PCAN_MESSAGE_EXTENDED : TPCANMessageType.PCAN_MESSAGE_STANDARD,
            DATA = new byte[8]
        };

        Array.Copy(data, canMessage.DATA, canMessage.LEN);
        TPCANStatus result;
        try
        {
            result = PCANBasic.Write(Channel, ref canMessage);

            // Aggiungi un ritardo di 1 ms
            await Task.Delay(5);
        }
        catch (Exception ex)
        {
            throw new IOException(
                $"Failed to write CAN frame (ID 0x{canMessage.ID:X}, {canMessage.LEN} bytes).",
                ex);
        }
        return result;
    }

    //public TPCANStatus SendMessage(uint canId, byte[] data, bool isExtended = false)
    //{
    //    if (!_isConnected) return TPCANStatus.PCAN_ERROR_BUSOFF;

    //    var canMessage = new TPCANMsg
    //    {
    //        ID = canId,
    //        LEN = (byte)Math.Min(data.Length, 8),
    //        MSGTYPE = isExtended ? TPCANMessageType.PCAN_MESSAGE_EXTENDED : TPCANMessageType.PCAN_MESSAGE_STANDARD,
    //        DATA = new byte[8]
    //    };

    //    Array.Copy(data, canMessage.DATA, canMessage.LEN);

    //    var result = PCANBasic.Write(Channel, ref canMessage);
    //    return result;
    //}

    private async Task ReadCANMessagesAsync()
    {
        while (true)
        {
            if (!_isConnected)
            {
                await Task.Delay(500); // Attendi finché non viene ristabilita la connessione
                continue;
            }

            try
            {
                TPCANMsg canMessage;
                TPCANTimestamp canTimestamp;
                var result = PCANBasic.Read(Channel, out canMessage, out canTimestamp);

                if (result == TPCANStatus.PCAN_ERROR_OK)
                {
                    var receivedData = canMessage.DATA.Take(canMessage.LEN).ToArray();
                    var packetEvent = new CANPacketEventArgs(
                        canMessage.ID,
                        receivedData,
                        DateTime.Now
                    );

                    PacketReceived?.Invoke(this, packetEvent);
                }
                else if (result != TPCANStatus.PCAN_ERROR_QRCVEMPTY)
                {
                    // Gestione errori
                    StringBuilder errorTextB = new StringBuilder(256);
                    PCANBasic.GetErrorText(result, 0, errorTextB);
                    ErrorOccurred?.Invoke(this, errorTextB.ToString());
                }

                await Task.Delay(50); // Riduzione carico CPU
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Errore durante la lettura: {ex.Message}");
            }
        }
    }

    public void Disconnect()
    {
        PCANBasic.Uninitialize(Channel);
        _isConnected = false;
    }
}

