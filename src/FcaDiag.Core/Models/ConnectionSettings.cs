namespace FcaDiag.Core.Models;

/// <summary>
/// Connection settings for diagnostic adapters
/// </summary>
public class ConnectionSettings
{
    /// <summary>
    /// Adapter type (e.g., "J2534", "SocketCAN", "ELM327")
    /// </summary>
    public required string AdapterType { get; init; }

    /// <summary>
    /// Device name or path (e.g., COM port, device path)
    /// </summary>
    public string? DeviceName { get; init; }

    /// <summary>
    /// CAN bus bitrate in bps
    /// </summary>
    public int Bitrate { get; init; } = 500000;

    /// <summary>
    /// Response timeout in milliseconds
    /// </summary>
    public int TimeoutMs { get; init; } = 1000;

    /// <summary>
    /// P2 timeout (time for ECU to respond) in milliseconds
    /// </summary>
    public int P2TimeoutMs { get; init; } = 50;

    /// <summary>
    /// P2* extended timeout in milliseconds
    /// </summary>
    public int P2ExtendedTimeoutMs { get; init; } = 5000;
}
