namespace FcaDiag.Core.Enums;

/// <summary>
/// UDS service identifiers (ISO 14229)
/// </summary>
public enum UdsServiceId : byte
{
    // Diagnostic and Communication Management
    DiagnosticSessionControl = 0x10,
    EcuReset = 0x11,
    SecurityAccess = 0x27,
    CommunicationControl = 0x28,
    TesterPresent = 0x3E,
    ControlDtcSetting = 0x85,

    // Data Transmission
    ReadDataByIdentifier = 0x22,
    ReadMemoryByAddress = 0x23,
    ReadScalingDataByIdentifier = 0x24,
    ReadDataByPeriodicIdentifier = 0x2A,
    DynamicallyDefineDataIdentifier = 0x2C,
    WriteDataByIdentifier = 0x2E,
    WriteMemoryByAddress = 0x3D,

    // Stored Data Transmission (DTC)
    ClearDiagnosticInformation = 0x14,
    ReadDtcInformation = 0x19,

    // Input/Output Control
    InputOutputControlByIdentifier = 0x2F,

    // Routine Control
    RoutineControl = 0x31,

    // Upload/Download
    RequestDownload = 0x34,
    RequestUpload = 0x35,
    TransferData = 0x36,
    RequestTransferExit = 0x37,
    RequestFileTransfer = 0x38
}
