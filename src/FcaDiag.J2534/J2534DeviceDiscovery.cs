using FcaDiag.J2534.Native;
using Microsoft.Win32;

namespace FcaDiag.J2534;

/// <summary>
/// Discovers installed J2534 pass-thru devices from Windows registry
/// </summary>
public static class J2534DeviceDiscovery
{
    private const string PassThruRegistryPath = @"SOFTWARE\PassThruSupport.04.04";
    private const string PassThruRegistryPath0202 = @"SOFTWARE\PassThruSupport";

    /// <summary>
    /// Get all installed J2534 devices
    /// </summary>
    public static List<J2534Device> GetDevices()
    {
        var devices = new List<J2534Device>();

        // Try J2534-1 v04.04 path first
        devices.AddRange(GetDevicesFromRegistry(PassThruRegistryPath));

        // Also try legacy v02.02 path
        devices.AddRange(GetDevicesFromRegistry(PassThruRegistryPath0202));

        return devices;
    }

    private static IEnumerable<J2534Device> GetDevicesFromRegistry(string basePath)
    {
        var devices = new List<J2534Device>();

        try
        {
            // Try both 32-bit and 64-bit registry views
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var passThruKey = baseKey.OpenSubKey(basePath);

                if (passThruKey == null)
                    continue;

                foreach (var deviceName in passThruKey.GetSubKeyNames())
                {
                    using var deviceKey = passThruKey.OpenSubKey(deviceName);
                    if (deviceKey == null)
                        continue;

                    var dllPath = deviceKey.GetValue("FunctionLibrary") as string;
                    if (string.IsNullOrEmpty(dllPath))
                        continue;

                    var name = deviceKey.GetValue("Name") as string ?? deviceName;
                    var vendor = deviceKey.GetValue("Vendor") as string ?? "Unknown";
                    var configApp = deviceKey.GetValue("ConfigApplication") as string;

                    // Avoid duplicates
                    if (devices.Any(d => d.DllPath.Equals(dllPath, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    devices.Add(new J2534Device
                    {
                        Name = name,
                        Vendor = vendor,
                        DllPath = dllPath,
                        ConfigApplication = configApp
                    });
                }
            }
        }
        catch (Exception)
        {
            // Registry access may fail - return what we have
        }

        return devices;
    }

    /// <summary>
    /// Check if any J2534 devices are installed
    /// </summary>
    public static bool HasDevices() => GetDevices().Count > 0;

    /// <summary>
    /// Get device by name (partial match)
    /// </summary>
    public static J2534Device? FindDevice(string namePattern)
    {
        return GetDevices().FirstOrDefault(d =>
            d.Name.Contains(namePattern, StringComparison.OrdinalIgnoreCase) ||
            d.Vendor.Contains(namePattern, StringComparison.OrdinalIgnoreCase));
    }
}
