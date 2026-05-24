using System.Buffers.Binary;
using System.Collections.Immutable;
using Core.Interfaces;
using Core.Models;

namespace Infrastructure.Protocol.Hardware;

/// <summary>
/// Adapter CAN che implementa <see cref="ICommunicationPort"/> wrappando un
/// <see cref="IPcanDriver"/> (produzione: <see cref="PCANManager"/>).
///
/// <para><b>Convention payload (opzione A, Fase 2):</b></para>
/// Per mantenere <c>ICommunicationPort</c> uniforme tra CAN/BLE/Serial
/// l'arbitration ID del frame CAN è trasportato <i>in-band</i> come prefisso
/// del payload:
/// <list type="bullet">
/// <item><description><c>SendAsync(payload)</c>: primi 4 byte = arbitrationId
///   <b>little-endian</b>, resto = dati CAN (max 8 byte, extended frame).</description></item>
/// <item><description><c>PacketReceived.RawPacket.Payload</c>: primi 4 byte =
///   arbitrationId LE del mittente, resto = dati CAN ricevuti (≤ 8 byte).</description></item>
/// </list>
/// Questa convention sparirà in Fase 4 quando <c>Stem.Communication</c>
/// fornirà un'API nativa con canId separato.
///
/// <para><b>State machine:</b></para>
/// Il ciclo di connessione è gestito da <see cref="IPcanDriver"/> che esegue
/// auto-reconnect interno. CanPort riflette lo stato via
/// <see cref="IPcanDriver.ConnectionStatusChanged"/>. <see cref="ConnectAsync"/>
/// è un best-effort di sincronizzazione con un breve polling.
/// </summary>
public sealed class CanPort : ICommunicationPort
{
    private const int ArbitrationIdLength = 4;
    private const int MaxCanFrameDataLength = 8;
    private const int ConnectPollAttempts = 20;
    private const int ConnectPollIntervalMs = 100;

    private readonly IPcanDriver _driver;
    private readonly Lock _stateLock = new();
    private int _state;
    private bool _disposed;

    public CanPort(IPcanDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        _driver = driver;
        _state = (int)(driver.IsConnected
            ? ConnectionState.Connected
            : ConnectionState.Disconnected);
        _driver.PacketReceived += OnDriverPacketReceived;
        _driver.ConnectionStatusChanged += OnDriverConnectionChanged;
    }

    /// <inheritdoc/>
    public ChannelKind Kind => ChannelKind.Can;

    /// <inheritdoc/>
    public ConnectionState State => (ConnectionState)Volatile.Read(ref _state);

    /// <inheritdoc/>
    public bool IsConnected => State == ConnectionState.Connected;

    /// <inheritdoc/>
    public event EventHandler<RawPacket>? PacketReceived;

    /// <inheritdoc/>
    public event EventHandler<ConnectionState>? StateChanged;

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (State == ConnectionState.Connected) return;
        Transition(ConnectionState.Connecting);
        for (int attempt = 0; attempt < ConnectPollAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (_driver.IsConnected)
            {
                Transition(ConnectionState.Connected);
                return;
            }
            await Task.Delay(ConnectPollIntervalMs, ct);
        }
        Transition(ConnectionState.Error);
        throw new InvalidOperationException(
            "Timeout: driver PCAN non ha raggiunto lo stato Connected.");
    }

    /// <inheritdoc/>
    public Task DisconnectAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        if (State == ConnectionState.Disconnected) return Task.CompletedTask;
        _driver.Disconnect();
        Transition(ConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        if (State != ConnectionState.Connected)
        {
            throw new InvalidOperationException(
                $"Stato corrente {State}: richiesto Connected per SendAsync.");
        }
        if (payload.Length < ArbitrationIdLength)
        {
            throw new ArgumentException(
                $"Payload troppo corto: servono almeno {ArbitrationIdLength} " +
                "byte di arbitrationId LE come prefisso.",
                nameof(payload));
        }
        int dataLength = payload.Length - ArbitrationIdLength;
        if (dataLength > MaxCanFrameDataLength)
        {
            throw new ArgumentException(
                $"Dati CAN troppo lunghi: {dataLength} byte " +
                $"(max {MaxCanFrameDataLength}).",
                nameof(payload));
        }
        var arbitrationId = BinaryPrimitives.ReadUInt32LittleEndian(
            payload.Span[..ArbitrationIdLength]);
        var data = payload.Span[ArbitrationIdLength..].ToArray();
        await _driver.SendMessageAsync(arbitrationId, data, isExtended: true);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _driver.PacketReceived -= OnDriverPacketReceived;
        _driver.ConnectionStatusChanged -= OnDriverConnectionChanged;
        if (State != ConnectionState.Disconnected)
        {
            try { _driver.Disconnect(); }
            catch { /* best effort cleanup */ }
            Transition(ConnectionState.Disconnected);
        }
    }

    private void OnDriverPacketReceived(object? sender, CANPacketEventArgs e)
    {
        var handler = PacketReceived;
        if (handler is null) return;
        var payload = ImmutableArray.CreateBuilder<byte>(
            ArbitrationIdLength + e.Data.Length);
        Span<byte> arbBytes = stackalloc byte[ArbitrationIdLength];
        BinaryPrimitives.WriteUInt32LittleEndian(arbBytes, e.ArbitrationId);
        for (int i = 0; i < ArbitrationIdLength; i++) payload.Add(arbBytes[i]);
        for (int i = 0; i < e.Data.Length; i++) payload.Add(e.Data[i]);
        handler(this, new RawPacket(payload.MoveToImmutable(), e.Timestamp));
    }

    private void OnDriverConnectionChanged(object? sender, bool isConnected)
    {
        Transition(isConnected
            ? ConnectionState.Connected
            : ConnectionState.Disconnected);
    }

    private void Transition(ConnectionState newState)
    {
        int newInt = (int)newState;
        bool changed;
        lock (_stateLock)
        {
            if (_state == newInt) { changed = false; }
            else
            {
                Volatile.Write(ref _state, newInt);
                changed = true;
            }
        }
        if (changed) StateChanged?.Invoke(this, newState);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
