namespace FcaDiag.Core.Models;

/// <summary>
/// CAN bus channel selection
/// </summary>
public enum CanChannel
{
    /// <summary>
    /// Automatically try HS-CAN first, then MS-CAN
    /// </summary>
    Auto,

    /// <summary>
    /// High-Speed CAN (500 kbps) - Primary diagnostic bus
    /// </summary>
    HS_CAN,

    /// <summary>
    /// Medium-Speed CAN (125 kbps) - Body/interior modules
    /// </summary>
    MS_CAN,

    /// <summary>
    /// Single Wire CAN (33.3 kbps) - Some older modules
    /// </summary>
    SW_CAN
}

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
    /// CAN channel to use (Auto, HS-CAN, MS-CAN, SW-CAN)
    /// </summary>
    public CanChannel Channel { get; init; } = CanChannel.Auto;

    /// <summary>
    /// CAN bus bitrate in bps (500000 for HS-CAN, 125000 for MS-CAN)
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

    /// <summary>
    /// Get the bitrate for the specified channel
    /// </summary>
    public static int GetChannelBitrate(CanChannel channel)
    {
        return channel switch
        {
            CanChannel.HS_CAN => 500000,
            CanChannel.MS_CAN => 125000,
            CanChannel.SW_CAN => 33333,
            _ => 500000
        };
    }

    /// <summary>
    /// Get display name for channel
    /// </summary>
    public static string GetChannelName(CanChannel channel)
    {
        return channel switch
        {
            CanChannel.HS_CAN => "HS-CAN (500 kbps)",
            CanChannel.MS_CAN => "MS-CAN (125 kbps)",
            CanChannel.SW_CAN => "SW-CAN (33.3 kbps)",
            CanChannel.Auto => "Auto-detect",
            _ => channel.ToString()
        };
    }
}
