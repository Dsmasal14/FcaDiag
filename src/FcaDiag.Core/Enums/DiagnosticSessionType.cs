namespace FcaDiag.Core.Enums;

/// <summary>
/// UDS diagnostic session types (ISO 14229)
/// </summary>
public enum DiagnosticSessionType : byte
{
    Default = 0x01,
    Programming = 0x02,
    Extended = 0x03,
    SafetySystem = 0x04,

    // FCA specific sessions (0x40-0x5F reserved for vehicle manufacturer)
    FcaEngineering = 0x40,
    FcaEol = 0x41
}
