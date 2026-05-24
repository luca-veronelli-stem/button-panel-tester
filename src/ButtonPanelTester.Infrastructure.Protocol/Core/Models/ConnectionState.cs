namespace Core.Models;

/// <summary>
/// Stato di una <c>ICommunicationPort</c>.
/// </summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error
}
