using FcaDiag.Core.Enums;
using FcaDiag.Core.Models;

namespace FcaDiag.Protocols.Transport;

/// <summary>
/// FCA module database with CAN IDs and DIDs
/// </summary>
public static class FcaModuleDatabase
{
    /// <summary>
    /// Common FCA module definitions
    /// Note: These are typical values - actual values may vary by year/model
    /// </summary>
    public static readonly List<FcaModuleDefinition> Modules =
    [
        // Powertrain modules (CAN-C)
        new FcaModuleDefinition
        {
            Name = "Powertrain Control Module",
            ShortName = "PCM",
            ModuleType = FcaModuleType.PCM,
            Network = FcaCanNetwork.CanC,
            RequestId = 0x7E0,
            ResponseId = 0x7E8
        },
        new FcaModuleDefinition
        {
            Name = "Transmission Control Module",
            ShortName = "TCM",
            ModuleType = FcaModuleType.TCM,
            Network = FcaCanNetwork.CanC,
            RequestId = 0x7E1,
            ResponseId = 0x7E9
        },
        new FcaModuleDefinition
        {
            Name = "Anti-lock Braking System",
            ShortName = "ABS/ESC",
            ModuleType = FcaModuleType.ABS,
            Network = FcaCanNetwork.CanC,
            RequestId = 0x7E2,
            ResponseId = 0x7EA
        },
        new FcaModuleDefinition
        {
            Name = "Electric Power Steering",
            ShortName = "EPS",
            ModuleType = FcaModuleType.EPS,
            Network = FcaCanNetwork.CanC,
            RequestId = 0x746,
            ResponseId = 0x74E
        },

        // Body modules (CAN-IHS)
        new FcaModuleDefinition
        {
            Name = "Body Control Module",
            ShortName = "BCM",
            ModuleType = FcaModuleType.BCM,
            Network = FcaCanNetwork.CanIhs,
            RequestId = 0x760,
            ResponseId = 0x768
        },
        new FcaModuleDefinition
        {
            Name = "Instrument Panel Cluster",
            ShortName = "IPC",
            ModuleType = FcaModuleType.IPC,
            Network = FcaCanNetwork.CanIhs,
            RequestId = 0x762,
            ResponseId = 0x76A
        },
        new FcaModuleDefinition
        {
            Name = "Radio/Head Unit",
            ShortName = "RADIO",
            ModuleType = FcaModuleType.RADIO,
            Network = FcaCanNetwork.CanIhs,
            RequestId = 0x764,
            ResponseId = 0x76C
        },
        new FcaModuleDefinition
        {
            Name = "HVAC Control Module",
            ShortName = "HVAC",
            ModuleType = FcaModuleType.HVAC,
            Network = FcaCanNetwork.CanIhs,
            RequestId = 0x763,
            ResponseId = 0x76B
        },

        // Safety modules
        new FcaModuleDefinition
        {
            Name = "Airbag Control Module",
            ShortName = "ACSM",
            ModuleType = FcaModuleType.ACSM,
            Network = FcaCanNetwork.CanIhs,
            RequestId = 0x720,
            ResponseId = 0x728
        },
        new FcaModuleDefinition
        {
            Name = "Occupant Classification Module",
            ShortName = "OCM",
            ModuleType = FcaModuleType.OCM,
            Network = FcaCanNetwork.CanIhs,
            RequestId = 0x721,
            ResponseId = 0x729
        },

        // ADAS modules
        new FcaModuleDefinition
        {
            Name = "Adaptive Cruise Control",
            ShortName = "ACC",
            ModuleType = FcaModuleType.ACC,
            Network = FcaCanNetwork.CanC,
            RequestId = 0x700,
            ResponseId = 0x708
        },
        new FcaModuleDefinition
        {
            Name = "Forward Collision Module",
            ShortName = "FCM",
            ModuleType = FcaModuleType.FCM,
            Network = FcaCanNetwork.CanC,
            RequestId = 0x701,
            ResponseId = 0x709
        },
        new FcaModuleDefinition
        {
            Name = "Blind Spot Monitor",
            ShortName = "BSM",
            ModuleType = FcaModuleType.BSM,
            Network = FcaCanNetwork.CanC,
            RequestId = 0x702,
            ResponseId = 0x70A
        },
        new FcaModuleDefinition
        {
            Name = "Park Assist Module",
            ShortName = "PAM",
            ModuleType = FcaModuleType.PAM,
            Network = FcaCanNetwork.CanC,
            RequestId = 0x703,
            ResponseId = 0x70B
        },

        // Security / Network modules
        new FcaModuleDefinition
        {
            Name = "Security Gateway",
            ShortName = "SGW",
            ModuleType = FcaModuleType.SGW,
            Network = FcaCanNetwork.CanIhs,
            RequestId = 0x744,
            ResponseId = 0x74C
        },
        new FcaModuleDefinition
        {
            Name = "Wireless Control Module",
            ShortName = "WCM",
            ModuleType = FcaModuleType.WCM,
            Network = FcaCanNetwork.CanIhs,
            RequestId = 0x740,
            ResponseId = 0x748
        },
        new FcaModuleDefinition
        {
            Name = "TPMS Module",
            ShortName = "TPMS",
            ModuleType = FcaModuleType.TPMS,
            Network = FcaCanNetwork.CanIhs,
            RequestId = 0x742,
            ResponseId = 0x74A
        },

        // Door modules (CAN-B)
        new FcaModuleDefinition
        {
            Name = "Driver Door Module",
            ShortName = "DDM",
            ModuleType = FcaModuleType.DDM,
            Network = FcaCanNetwork.CanB,
            RequestId = 0x780,
            ResponseId = 0x788
        },
        new FcaModuleDefinition
        {
            Name = "Passenger Door Module",
            ShortName = "PDM",
            ModuleType = FcaModuleType.PDM,
            Network = FcaCanNetwork.CanB,
            RequestId = 0x781,
            ResponseId = 0x789
        }
    ];

    /// <summary>
    /// Gets module by type
    /// </summary>
    public static FcaModuleDefinition? GetModule(FcaModuleType type)
    {
        return Modules.FirstOrDefault(m => m.ModuleType == type);
    }

    /// <summary>
    /// Gets modules by network
    /// </summary>
    public static IEnumerable<FcaModuleDefinition> GetModulesByNetwork(FcaCanNetwork network)
    {
        return Modules.Where(m => m.Network == network);
    }

    /// <summary>
    /// Gets module by request ID
    /// </summary>
    public static FcaModuleDefinition? GetModuleByRequestId(uint requestId)
    {
        return Modules.FirstOrDefault(m => m.RequestId == requestId);
    }
}
