namespace FcaDiag.Core.Enums;

/// <summary>
/// FCA CAN bus networks
/// </summary>
public enum FcaCanNetwork
{
    Unknown = 0,

    /// <summary>
    /// CAN-C: High-speed powertrain network (500 kbps)
    /// </summary>
    CanC,

    /// <summary>
    /// CAN-IHS: Interior high-speed network (500 kbps)
    /// </summary>
    CanIhs,

    /// <summary>
    /// CAN-B: Body/comfort network (125 kbps)
    /// </summary>
    CanB,

    /// <summary>
    /// CAN-D: Diagnostic network
    /// </summary>
    CanD
}
