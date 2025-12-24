using FcaDiag.Core.Enums;
using FcaDiag.Core.Interfaces;
using FcaDiag.Core.Models;
using FcaDiag.Protocols.Transport;

namespace FcaDiag.Console;

/// <summary>
/// Mock CAN adapter for demo/preview mode - simulates vehicle responses
/// </summary>
public class MockCanAdapter : ICanAdapter
{
    private readonly Random _random = new();
    private readonly HashSet<uint> _respondingModules;
    private DiagnosticSessionType _currentSession = DiagnosticSessionType.Default;

    public bool IsConnected { get; private set; }

    public MockCanAdapter()
    {
        // Simulate these modules responding
        _respondingModules = new HashSet<uint>
        {
            0x7E8,  // PCM
            0x7E9,  // TCM
            0x7EA,  // ABS
            0x768,  // BCM
            0x76A,  // IPC
            0x728,  // ACSM
        };
    }

    public Task<bool> ConnectAsync(ConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        IsConnected = true;
        return Task.FromResult(true);
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task SendAsync(uint canId, byte[] data, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<byte[]?> ReceiveAsync(uint canId, int timeoutMs, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<byte[]?>(null);
    }

    public Task<byte[]?> TransactAsync(uint txId, uint rxId, byte[] data, int timeoutMs, CancellationToken cancellationToken = default)
    {
        // Simulate processing delay
        Thread.Sleep(_random.Next(10, 50));

        // Check if this module "responds"
        if (!_respondingModules.Contains(rxId))
            return Task.FromResult<byte[]?>(null);

        if (data.Length == 0)
            return Task.FromResult<byte[]?>(null);

        var serviceId = (UdsServiceId)data[0];
        var response = GenerateResponse(serviceId, data, rxId);

        return Task.FromResult<byte[]?>(response);
    }

    private byte[]? GenerateResponse(UdsServiceId serviceId, byte[] request, uint moduleId)
    {
        return serviceId switch
        {
            UdsServiceId.TesterPresent => GenerateTesterPresentResponse(),
            UdsServiceId.DiagnosticSessionControl => GenerateSessionResponse(request),
            UdsServiceId.ReadDataByIdentifier => GenerateReadDataResponse(request, moduleId),
            UdsServiceId.ReadDtcInformation => GenerateDtcResponse(moduleId),
            UdsServiceId.ClearDiagnosticInformation => [(byte)(0x40 + (byte)serviceId)],
            _ => null
        };
    }

    private byte[] GenerateTesterPresentResponse()
    {
        return [0x7E, 0x00]; // Positive response
    }

    private byte[] GenerateSessionResponse(byte[] request)
    {
        if (request.Length < 2)
            return [0x7F, (byte)UdsServiceId.DiagnosticSessionControl, 0x13]; // Invalid format

        _currentSession = (DiagnosticSessionType)request[1];
        return [0x50, request[1], 0x00, 0x19, 0x01, 0xF4]; // Session + timing params
    }

    private byte[] GenerateReadDataResponse(byte[] request, uint moduleId)
    {
        if (request.Length < 3)
            return [0x7F, (byte)UdsServiceId.ReadDataByIdentifier, 0x13];

        var did = (ushort)((request[1] << 8) | request[2]);

        return did switch
        {
            FcaDataIdentifiers.VehicleIdentificationNumber => GenerateVinResponse(),
            FcaDataIdentifiers.VehicleManufacturerEcuSoftwareNumber => GenerateSoftwareResponse(moduleId),
            FcaDataIdentifiers.VehicleManufacturerEcuHardwareNumber => GenerateHardwareResponse(moduleId),
            FcaDataIdentifiers.Odometer => GenerateOdometerResponse(),
            FcaDataIdentifiers.BatteryVoltage => GenerateBatteryResponse(),
            FcaDataIdentifiers.EngineRpm => GenerateRpmResponse(),
            FcaDataIdentifiers.CoolantTemperature => GenerateCoolantResponse(),
            FcaDataIdentifiers.VehicleSpeed => GenerateSpeedResponse(),
            _ => [0x7F, (byte)UdsServiceId.ReadDataByIdentifier, 0x31] // Request out of range
        };
    }

    private byte[] GenerateVinResponse()
    {
        // Response: 0x62 + DID + VIN
        var vin = "1C4RJFAG5FC123456"u8.ToArray();
        var response = new byte[3 + vin.Length];
        response[0] = 0x62;
        response[1] = 0xF1;
        response[2] = 0x90;
        Array.Copy(vin, 0, response, 3, vin.Length);
        return response;
    }

    private byte[] GenerateSoftwareResponse(uint moduleId)
    {
        var sw = moduleId switch
        {
            0x7E8 => "PCM_SW_05.12.03"u8.ToArray(),
            0x7E9 => "TCM_SW_02.08.01"u8.ToArray(),
            0x7EA => "ABS_SW_03.04.02"u8.ToArray(),
            0x768 => "BCM_SW_04.11.00"u8.ToArray(),
            _ => "SW_01.00.00"u8.ToArray()
        };
        var response = new byte[3 + sw.Length];
        response[0] = 0x62;
        response[1] = 0xF1;
        response[2] = 0x95;
        Array.Copy(sw, 0, response, 3, sw.Length);
        return response;
    }

    private byte[] GenerateHardwareResponse(uint moduleId)
    {
        var hw = moduleId switch
        {
            0x7E8 => "68249285AC"u8.ToArray(),
            0x7E9 => "68312456AB"u8.ToArray(),
            0x7EA => "68405123AA"u8.ToArray(),
            0x768 => "68512789AD"u8.ToArray(),
            _ => "68000000AA"u8.ToArray()
        };
        var response = new byte[3 + hw.Length];
        response[0] = 0x62;
        response[1] = 0xF1;
        response[2] = 0x91;
        Array.Copy(hw, 0, response, 3, hw.Length);
        return response;
    }

    private byte[] GenerateOdometerResponse()
    {
        // 87,432 km = 0x01557C
        return [0x62, 0x01, 0x00, 0x01, 0x55, 0x7C];
    }

    private byte[] GenerateBatteryResponse()
    {
        // 12.6V = 12600mV = 0x3138
        return [0x62, 0x01, 0x01, 0x31, 0x38];
    }

    private byte[] GenerateRpmResponse()
    {
        // Idle: 750 RPM
        return [0x62, 0x01, 0x04, 0x02, 0xEE];
    }

    private byte[] GenerateCoolantResponse()
    {
        // 92°C (offset 40) = 132
        return [0x62, 0x01, 0x05, 0x84];
    }

    private byte[] GenerateSpeedResponse()
    {
        // 0 km/h (parked)
        return [0x62, 0x01, 0x03, 0x00];
    }

    private byte[] GenerateDtcResponse(uint moduleId)
    {
        // Only PCM has DTCs in demo
        if (moduleId == 0x7E8)
        {
            // P0300, P0171 - both confirmed
            return
            [
                0x59, 0x02, 0xFF,  // Positive response, sub-function, availability mask
                0x03, 0x00, 0x00, 0x08,  // P0300 - Random misfire, confirmed
                0x01, 0x71, 0x00, 0x08   // P0171 - System too lean, confirmed
            ];
        }

        // No DTCs
        return [0x59, 0x02, 0xFF];
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}
