using FcaDiag.Core.Enums;
using FcaDiag.Core.Models;

namespace FcaDiag.Core.Interfaces;

/// <summary>
/// High-level diagnostic service interface
/// </summary>
public interface IDiagnosticService
{
    /// <summary>
    /// Start a diagnostic session
    /// </summary>
    Task<UdsResponse> StartSessionAsync(DiagnosticSessionType sessionType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read data by identifier (DID)
    /// </summary>
    Task<UdsResponse> ReadDataByIdentifierAsync(ushort did, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read multiple DIDs
    /// </summary>
    Task<UdsResponse> ReadDataByIdentifierAsync(ushort[] dids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Write data by identifier
    /// </summary>
    Task<UdsResponse> WriteDataByIdentifierAsync(ushort did, byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read DTCs
    /// </summary>
    Task<IReadOnlyList<DiagnosticTroubleCode>> ReadDtcsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear DTCs
    /// </summary>
    Task<UdsResponse> ClearDtcsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Security access (request seed and send key)
    /// </summary>
    Task<UdsResponse> SecurityAccessAsync(byte level, Func<byte[], byte[]> keyCalculator, CancellationToken cancellationToken = default);

    /// <summary>
    /// Start a routine
    /// </summary>
    Task<UdsResponse> StartRoutineAsync(ushort routineId, byte[]? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// ECU reset
    /// </summary>
    Task<UdsResponse> EcuResetAsync(byte resetType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tester present (keep session alive)
    /// </summary>
    Task<UdsResponse> TesterPresentAsync(CancellationToken cancellationToken = default);
}
