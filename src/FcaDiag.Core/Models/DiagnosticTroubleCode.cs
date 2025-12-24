namespace FcaDiag.Core.Models;

/// <summary>
/// Represents a diagnostic trouble code (DTC)
/// </summary>
public class DiagnosticTroubleCode
{
    /// <summary>
    /// Raw 3-byte DTC value
    /// </summary>
    public required uint Code { get; init; }

    /// <summary>
    /// DTC status byte
    /// </summary>
    public required byte Status { get; init; }

    /// <summary>
    /// Human-readable DTC string (e.g., P0300)
    /// </summary>
    public string DisplayCode => FormatDtcCode(Code);

    /// <summary>
    /// Test failed
    /// </summary>
    public bool TestFailed => (Status & 0x01) != 0;

    /// <summary>
    /// Test failed this operation cycle
    /// </summary>
    public bool TestFailedThisOperationCycle => (Status & 0x02) != 0;

    /// <summary>
    /// Pending DTC
    /// </summary>
    public bool Pending => (Status & 0x04) != 0;

    /// <summary>
    /// Confirmed DTC
    /// </summary>
    public bool Confirmed => (Status & 0x08) != 0;

    /// <summary>
    /// Test not completed since last clear
    /// </summary>
    public bool TestNotCompletedSinceLastClear => (Status & 0x10) != 0;

    /// <summary>
    /// Test failed since last clear
    /// </summary>
    public bool TestFailedSinceLastClear => (Status & 0x20) != 0;

    /// <summary>
    /// Test not completed this operation cycle
    /// </summary>
    public bool TestNotCompletedThisOperationCycle => (Status & 0x40) != 0;

    /// <summary>
    /// Warning indicator requested
    /// </summary>
    public bool WarningIndicatorRequested => (Status & 0x80) != 0;

    private static string FormatDtcCode(uint code)
    {
        var firstByte = (code >> 16) & 0xFF;
        var secondByte = (code >> 8) & 0xFF;
        var thirdByte = code & 0xFF;

        var prefix = ((firstByte >> 6) & 0x03) switch
        {
            0 => 'P',  // Powertrain
            1 => 'C',  // Chassis
            2 => 'B',  // Body
            3 => 'U',  // Network
            _ => '?'
        };

        var digit1 = (firstByte >> 4) & 0x03;
        var digit2 = firstByte & 0x0F;

        return $"{prefix}{digit1}{digit2:X}{secondByte:X2}";
    }

    public override string ToString() => $"{DisplayCode} (Status: 0x{Status:X2})";
}
