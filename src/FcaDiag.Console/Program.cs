using FcaDiag.Core.Enums;
using FcaDiag.Core.Models;
using FcaDiag.J2534;
using FcaDiag.Protocols.Transport;
using FcaDiag.Protocols.Uds;

namespace FcaDiag.Console;

class Program
{
    static async Task Main(string[] args)
    {
        System.Console.WriteLine("FCA Diagnostics Tool");
        System.Console.WriteLine("====================\n");

        // Detect J2534 devices
        System.Console.WriteLine("Scanning for J2534 devices...\n");
        var devices = J2534DeviceDiscovery.GetDevices();

        if (devices.Count == 0)
        {
            System.Console.WriteLine("No J2534 devices found.");
            System.Console.WriteLine("Install a J2534-compatible adapter driver to enable vehicle communication.\n");
            ShowModuleInfo();
            return;
        }

        System.Console.WriteLine($"Found {devices.Count} J2534 device(s):\n");
        for (int i = 0; i < devices.Count; i++)
        {
            System.Console.WriteLine($"  [{i + 1}] {devices[i].Vendor} - {devices[i].Name}");
            System.Console.WriteLine($"      DLL: {devices[i].DllPath}");
        }

        System.Console.WriteLine("\n" + new string('-', 60));
        System.Console.Write("\nSelect device (1-{0}) or 0 to exit: ", devices.Count);

        if (!int.TryParse(System.Console.ReadLine(), out int selection) || selection < 0 || selection > devices.Count)
        {
            System.Console.WriteLine("Invalid selection.");
            return;
        }

        if (selection == 0)
            return;

        var selectedDevice = devices[selection - 1];
        System.Console.WriteLine($"\nConnecting to {selectedDevice.Name}...");

        await using var adapter = new J2534Adapter(selectedDevice);
        var settings = new ConnectionSettings
        {
            AdapterType = "ISO15765",
            Bitrate = 500000,
            TimeoutMs = 1000
        };

        if (!await adapter.ConnectAsync(settings))
        {
            System.Console.WriteLine("Failed to connect to device.");
            System.Console.WriteLine("Make sure the device is connected and no other software is using it.");
            return;
        }

        System.Console.WriteLine("Connected!");
        System.Console.WriteLine($"  Firmware: {adapter.FirmwareVersion}");
        System.Console.WriteLine($"  DLL: {adapter.DllVersion}");
        System.Console.WriteLine($"  API: {adapter.ApiVersion}");

        System.Console.WriteLine("\n" + new string('-', 60));
        System.Console.WriteLine("\nAvailable commands:");
        System.Console.WriteLine("  scan    - Scan for responding modules");
        System.Console.WriteLine("  vin     - Read VIN from PCM");
        System.Console.WriteLine("  dtc     - Read DTCs from all modules");
        System.Console.WriteLine("  info    - Show module information");
        System.Console.WriteLine("  exit    - Exit program");

        while (true)
        {
            System.Console.Write("\n> ");
            var command = System.Console.ReadLine()?.Trim().ToLowerInvariant();

            switch (command)
            {
                case "scan":
                    await ScanModulesAsync(adapter);
                    break;
                case "vin":
                    await ReadVinAsync(adapter);
                    break;
                case "dtc":
                    await ReadAllDtcsAsync(adapter);
                    break;
                case "info":
                    ShowModuleInfo();
                    break;
                case "exit":
                case "quit":
                case "q":
                    return;
                default:
                    System.Console.WriteLine("Unknown command. Type 'info' for help.");
                    break;
            }
        }
    }

    static async Task ScanModulesAsync(J2534Adapter adapter)
    {
        System.Console.WriteLine("\nScanning for modules...\n");

        var foundModules = new List<string>();

        foreach (var module in FcaModuleDatabase.Modules)
        {
            // Setup filter for this module
            adapter.SetupFlowControlFilter(module.RequestId, module.ResponseId);

            var client = new UdsClient(adapter, module);

            try
            {
                var response = await client.TesterPresentAsync();
                if (response.IsPositive)
                {
                    foundModules.Add(module.ShortName);
                    System.Console.WriteLine($"  [OK] {module.ShortName,-8} - {module.Name}");
                }
            }
            catch
            {
                // Module didn't respond
            }
        }

        System.Console.WriteLine($"\nFound {foundModules.Count} responding module(s).");
    }

    static async Task ReadVinAsync(J2534Adapter adapter)
    {
        var pcm = FcaModuleDatabase.GetModule(FcaModuleType.PCM);
        if (pcm == null)
        {
            System.Console.WriteLine("PCM module not defined.");
            return;
        }

        adapter.SetupFlowControlFilter(pcm.RequestId, pcm.ResponseId);
        var client = new UdsClient(adapter, pcm);

        System.Console.WriteLine("\nReading VIN from PCM...");

        try
        {
            // Start extended session
            var sessionResponse = await client.StartSessionAsync(DiagnosticSessionType.Extended);
            if (!sessionResponse.IsPositive)
            {
                System.Console.WriteLine($"Failed to start session: {sessionResponse.NegativeResponseCode}");
                return;
            }

            // Read VIN
            var response = await client.ReadDataByIdentifierAsync(FcaDataIdentifiers.VehicleIdentificationNumber);
            if (response.IsPositive && response.Data.Length >= 2)
            {
                // Skip DID bytes
                var vinData = response.Data.Length > 2 ? response.Data[2..] : response.Data;
                var vin = System.Text.Encoding.ASCII.GetString(vinData).Trim('\0');
                System.Console.WriteLine($"\nVIN: {vin}");
            }
            else
            {
                System.Console.WriteLine($"Failed to read VIN: {response.NegativeResponseCode}");
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"Error: {ex.Message}");
        }
    }

    static async Task ReadAllDtcsAsync(J2534Adapter adapter)
    {
        System.Console.WriteLine("\nReading DTCs from all modules...\n");

        var totalDtcs = 0;

        foreach (var module in FcaModuleDatabase.Modules)
        {
            adapter.SetupFlowControlFilter(module.RequestId, module.ResponseId);
            var client = new UdsClient(adapter, module);

            try
            {
                var dtcs = await client.ReadDtcsAsync();
                if (dtcs.Count > 0)
                {
                    System.Console.WriteLine($"{module.ShortName}:");
                    foreach (var dtc in dtcs)
                    {
                        var status = dtc.Confirmed ? "Confirmed" : dtc.Pending ? "Pending" : "Stored";
                        System.Console.WriteLine($"  {dtc.DisplayCode} - {status}");
                    }
                    totalDtcs += dtcs.Count;
                }
            }
            catch
            {
                // Module didn't respond or doesn't support DTC reading
            }
        }

        if (totalDtcs == 0)
            System.Console.WriteLine("No DTCs found in any module.");
        else
            System.Console.WriteLine($"\nTotal: {totalDtcs} DTC(s)");
    }

    static void ShowModuleInfo()
    {
        System.Console.WriteLine("\nAvailable Modules:");
        System.Console.WriteLine("-----------------");

        foreach (var network in Enum.GetValues<FcaCanNetwork>().Where(n => n != FcaCanNetwork.Unknown))
        {
            var modules = FcaModuleDatabase.GetModulesByNetwork(network).ToList();
            if (modules.Count == 0) continue;

            System.Console.WriteLine($"\n{network}:");
            foreach (var module in modules)
            {
                System.Console.WriteLine($"  {module.ShortName,-8} - {module.Name,-30} TX: 0x{module.RequestId:X3} RX: 0x{module.ResponseId:X3}");
            }
        }

        System.Console.WriteLine("\n\nCommon Data Identifiers (DIDs):");
        System.Console.WriteLine("-------------------------------");
        foreach (var did in FcaDataIdentifiers.DidInfo)
        {
            var unit = string.IsNullOrEmpty(did.Value.Unit) ? "" : $" ({did.Value.Unit})";
            System.Console.WriteLine($"  0x{did.Key:X4} - {did.Value.Name}{unit}");
        }
    }
}
