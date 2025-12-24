using FcaDiag.Core.Models;

namespace FcaDiag.Core.Interfaces;

/// <summary>
/// Interface for CAN bus adapters (J2534, SocketCAN, etc.)
/// </summary>
public interface ICanAdapter : IAsyncDisposable
{
    /// <summary>
    /// Whether the adapter is currently connected
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Connect to the adapter
    /// </summary>
    Task<bool> ConnectAsync(ConnectionSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from the adapter
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Send a CAN message
    /// </summary>
    Task SendAsync(uint canId, byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Receive a CAN message with the specified ID
    /// </summary>
    Task<byte[]?> ReceiveAsync(uint canId, int timeoutMs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send and receive (request/response pattern)
    /// </summary>
    Task<byte[]?> TransactAsync(uint txId, uint rxId, byte[] data, int timeoutMs, CancellationToken cancellationToken = default);
}
