using FcaDiag.Core.Enums;

namespace FcaDiag.Core.Models;

/// <summary>
/// Defines an FCA vehicle module with CAN addressing info
/// </summary>
public class FcaModuleDefinition
{
    /// <summary>
    /// Full module name
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Short module name/abbreviation
    /// </summary>
    public required string ShortName { get; init; }

    /// <summary>
    /// Module type identifier
    /// </summary>
    public required FcaModuleType ModuleType { get; init; }

    /// <summary>
    /// CAN network this module is on
    /// </summary>
    public required FcaCanNetwork Network { get; init; }

    /// <summary>
    /// CAN request/transmit ID (tester to ECU)
    /// </summary>
    public required uint RequestId { get; init; }

    /// <summary>
    /// CAN response/receive ID (ECU to tester)
    /// </summary>
    public required uint ResponseId { get; init; }

    public override string ToString() => $"{ShortName} ({Name})";
}
