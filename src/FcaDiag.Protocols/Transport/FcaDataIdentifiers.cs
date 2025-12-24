using System.Text;

namespace FcaDiag.Protocols.Transport;

/// <summary>
/// Common FCA Data Identifiers (DIDs)
/// </summary>
public static class FcaDataIdentifiers
{
    // Standard UDS DIDs (ISO 14229)
    public const ushort VehicleIdentificationNumber = 0xF190;
    public const ushort VehicleManufacturerEcuHardwareNumber = 0xF191;
    public const ushort SupplierManufacturerEcuHardwareNumber = 0xF192;
    public const ushort VehicleManufacturerEcuHardwareVersionNumber = 0xF193;
    public const ushort SupplierManufacturerEcuHardwareVersionNumber = 0xF194;
    public const ushort VehicleManufacturerEcuSoftwareNumber = 0xF195;
    public const ushort SystemName = 0xF197;
    public const ushort RepairShopCode = 0xF198;
    public const ushort ProgrammingDate = 0xF199;
    public const ushort DiagnosticDataIdentifier = 0xF19E;
    public const ushort EcuSerialNumber = 0xF18C;
    public const ushort VehicleManufacturerSparePartNumber = 0xF187;
    public const ushort SystemSupplierIdentifier = 0xF18A;

    // FCA specific DIDs (examples - actual DIDs vary by module)
    public const ushort Odometer = 0x0100;
    public const ushort BatteryVoltage = 0x0101;
    public const ushort IgnitionStatus = 0x0102;
    public const ushort VehicleSpeed = 0x0103;
    public const ushort EngineRpm = 0x0104;
    public const ushort CoolantTemperature = 0x0105;
    public const ushort FuelLevel = 0x0106;
    public const ushort OilPressure = 0x0107;
    public const ushort OilTemperature = 0x0108;

    // Coding/configuration DIDs
    public const ushort ModuleConfiguration = 0xDE00;
    public const ushort VariantCoding = 0xDE01;
    public const ushort AdaptationData = 0xDE02;

    /// <summary>
    /// DID information for display
    /// </summary>
    public static readonly Dictionary<ushort, DidDefinition> DidInfo = new()
    {
        { VehicleIdentificationNumber, new("VIN", "", ParseAsciiString) },
        { VehicleManufacturerEcuHardwareNumber, new("ECU Hardware Number", "", ParseAsciiString) },
        { VehicleManufacturerEcuSoftwareNumber, new("ECU Software Number", "", ParseAsciiString) },
        { SystemName, new("System Name", "", ParseAsciiString) },
        { EcuSerialNumber, new("ECU Serial Number", "", ParseAsciiString) },
        { VehicleManufacturerSparePartNumber, new("Part Number", "", ParseAsciiString) },
        { ProgrammingDate, new("Programming Date", "", ParseBcdDate) },
        { Odometer, new("Odometer", "km", ParseOdometer) },
        { BatteryVoltage, new("Battery Voltage", "V", ParseVoltage) },
        { VehicleSpeed, new("Vehicle Speed", "km/h", ParseSpeed) },
        { EngineRpm, new("Engine RPM", "rpm", ParseRpm) },
        { CoolantTemperature, new("Coolant Temp", "°C", ParseTemperature) },
        { FuelLevel, new("Fuel Level", "%", ParsePercent) }
    };

    private static string ParseAsciiString(byte[] data)
    {
        return Encoding.ASCII.GetString(data).Trim('\0', ' ');
    }

    private static string ParseBcdDate(byte[] data)
    {
        if (data.Length < 4) return "Unknown";
        var year = ((data[0] >> 4) * 10) + (data[0] & 0x0F) + 2000;
        var month = ((data[1] >> 4) * 10) + (data[1] & 0x0F);
        var day = ((data[2] >> 4) * 10) + (data[2] & 0x0F);
        return $"{year:D4}-{month:D2}-{day:D2}";
    }

    private static string ParseOdometer(byte[] data)
    {
        if (data.Length < 3) return "Unknown";
        var km = (data[0] << 16) | (data[1] << 8) | data[2];
        return km.ToString("N0");
    }

    private static string ParseVoltage(byte[] data)
    {
        if (data.Length < 2) return "Unknown";
        var mv = (data[0] << 8) | data[1];
        return (mv / 1000.0).ToString("F2");
    }

    private static string ParseSpeed(byte[] data)
    {
        if (data.Length < 1) return "Unknown";
        return data[0].ToString();
    }

    private static string ParseRpm(byte[] data)
    {
        if (data.Length < 2) return "Unknown";
        var rpm = (data[0] << 8) | data[1];
        return rpm.ToString();
    }

    private static string ParseTemperature(byte[] data)
    {
        if (data.Length < 1) return "Unknown";
        return (data[0] - 40).ToString();
    }

    private static string ParsePercent(byte[] data)
    {
        if (data.Length < 1) return "Unknown";
        return (data[0] * 100.0 / 255.0).ToString("F1");
    }
}

/// <summary>
/// Definition of a data identifier with parsing info
/// </summary>
public record DidDefinition(string Name, string Unit, Func<byte[], string> Parser);
