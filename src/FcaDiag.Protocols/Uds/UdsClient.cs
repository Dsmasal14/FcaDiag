using FcaDiag.Core.Enums;
using FcaDiag.Core.Interfaces;
using FcaDiag.Core.Models;

namespace FcaDiag.Protocols.Uds;

/// <summary>
/// UDS protocol client for communicating with ECUs
/// </summary>
public class UdsClient : IDiagnosticService
{
    private readonly ICanAdapter _adapter;
    private readonly uint _txId;
    private readonly uint _rxId;
    private readonly int _timeoutMs;

    public UdsClient(ICanAdapter adapter, uint txId, uint rxId, int timeoutMs = 1000)
    {
        _adapter = adapter;
        _txId = txId;
        _rxId = rxId;
        _timeoutMs = timeoutMs;
    }

    public UdsClient(ICanAdapter adapter, FcaModuleDefinition module, int timeoutMs = 1000)
        : this(adapter, module.RequestId, module.ResponseId, timeoutMs)
    {
    }

    public async Task<UdsResponse> StartSessionAsync(DiagnosticSessionType sessionType, CancellationToken cancellationToken = default)
    {
        var request = new byte[] { (byte)UdsServiceId.DiagnosticSessionControl, (byte)sessionType };
        return await SendRequestAsync(UdsServiceId.DiagnosticSessionControl, request, cancellationToken);
    }

    public async Task<UdsResponse> ReadDataByIdentifierAsync(ushort did, CancellationToken cancellationToken = default)
    {
        return await ReadDataByIdentifierAsync([did], cancellationToken);
    }

    public async Task<UdsResponse> ReadDataByIdentifierAsync(ushort[] dids, CancellationToken cancellationToken = default)
    {
        var request = new byte[1 + (dids.Length * 2)];
        request[0] = (byte)UdsServiceId.ReadDataByIdentifier;

        for (int i = 0; i < dids.Length; i++)
        {
            request[1 + (i * 2)] = (byte)(dids[i] >> 8);
            request[2 + (i * 2)] = (byte)(dids[i] & 0xFF);
        }

        return await SendRequestAsync(UdsServiceId.ReadDataByIdentifier, request, cancellationToken);
    }

    public async Task<UdsResponse> WriteDataByIdentifierAsync(ushort did, byte[] data, CancellationToken cancellationToken = default)
    {
        var request = new byte[3 + data.Length];
        request[0] = (byte)UdsServiceId.WriteDataByIdentifier;
        request[1] = (byte)(did >> 8);
        request[2] = (byte)(did & 0xFF);
        Array.Copy(data, 0, request, 3, data.Length);

        return await SendRequestAsync(UdsServiceId.WriteDataByIdentifier, request, cancellationToken);
    }

    public async Task<IReadOnlyList<DiagnosticTroubleCode>> ReadDtcsAsync(CancellationToken cancellationToken = default)
    {
        var request = new byte[] { (byte)UdsServiceId.ReadDtcInformation, 0x02, 0xFF }; // reportDTCByStatusMask, all DTCs
        var response = await SendRequestAsync(UdsServiceId.ReadDtcInformation, request, cancellationToken);

        var dtcs = new List<DiagnosticTroubleCode>();

        if (response.IsPositive && response.Data.Length >= 2)
        {
            // Skip sub-function and status availability mask
            for (int i = 2; i + 3 < response.Data.Length; i += 4)
            {
                var dtcCode = (uint)((response.Data[i] << 16) | (response.Data[i + 1] << 8) | response.Data[i + 2]);
                var status = response.Data[i + 3];

                dtcs.Add(new DiagnosticTroubleCode { Code = dtcCode, Status = status });
            }
        }

        return dtcs;
    }

    public async Task<UdsResponse> ClearDtcsAsync(CancellationToken cancellationToken = default)
    {
        var request = new byte[] { (byte)UdsServiceId.ClearDiagnosticInformation, 0xFF, 0xFF, 0xFF }; // Clear all DTCs
        return await SendRequestAsync(UdsServiceId.ClearDiagnosticInformation, request, cancellationToken);
    }

    public async Task<UdsResponse> SecurityAccessAsync(byte level, Func<byte[], byte[]> keyCalculator, CancellationToken cancellationToken = default)
    {
        // Request seed
        var seedRequest = new byte[] { (byte)UdsServiceId.SecurityAccess, level };
        var seedResponse = await SendRequestAsync(UdsServiceId.SecurityAccess, seedRequest, cancellationToken);

        if (!seedResponse.IsPositive)
            return seedResponse;

        // Extract seed (skip sub-function byte)
        var seed = seedResponse.Data.Length > 1 ? seedResponse.Data[1..] : [];

        // Calculate key
        var key = keyCalculator(seed);

        // Send key
        var keyRequest = new byte[2 + key.Length];
        keyRequest[0] = (byte)UdsServiceId.SecurityAccess;
        keyRequest[1] = (byte)(level + 1); // Send key is odd level
        Array.Copy(key, 0, keyRequest, 2, key.Length);

        return await SendRequestAsync(UdsServiceId.SecurityAccess, keyRequest, cancellationToken);
    }

    public async Task<UdsResponse> StartRoutineAsync(ushort routineId, byte[]? parameters = null, CancellationToken cancellationToken = default)
    {
        var request = new byte[4 + (parameters?.Length ?? 0)];
        request[0] = (byte)UdsServiceId.RoutineControl;
        request[1] = 0x01; // Start routine
        request[2] = (byte)(routineId >> 8);
        request[3] = (byte)(routineId & 0xFF);

        if (parameters != null)
            Array.Copy(parameters, 0, request, 4, parameters.Length);

        return await SendRequestAsync(UdsServiceId.RoutineControl, request, cancellationToken);
    }

    public async Task<UdsResponse> EcuResetAsync(byte resetType, CancellationToken cancellationToken = default)
    {
        var request = new byte[] { (byte)UdsServiceId.EcuReset, resetType };
        return await SendRequestAsync(UdsServiceId.EcuReset, request, cancellationToken);
    }

    public async Task<UdsResponse> TesterPresentAsync(CancellationToken cancellationToken = default)
    {
        var request = new byte[] { (byte)UdsServiceId.TesterPresent, 0x00 };
        return await SendRequestAsync(UdsServiceId.TesterPresent, request, cancellationToken);
    }

    private async Task<UdsResponse> SendRequestAsync(UdsServiceId serviceId, byte[] request, CancellationToken cancellationToken)
    {
        var response = await _adapter.TransactAsync(_txId, _rxId, request, _timeoutMs, cancellationToken);

        if (response == null || response.Length == 0)
        {
            return UdsResponse.Negative(serviceId, NegativeResponseCode.GeneralReject, []);
        }

        // Check for negative response (0x7F)
        if (response[0] == 0x7F && response.Length >= 3)
        {
            var nrc = (NegativeResponseCode)response[2];

            // Handle response pending
            if (nrc == NegativeResponseCode.RequestCorrectlyReceivedResponsePending)
            {
                // Wait for actual response with extended timeout
                response = await _adapter.ReceiveAsync(_rxId, 5000, cancellationToken);
                if (response == null || response.Length == 0)
                    return UdsResponse.Negative(serviceId, NegativeResponseCode.GeneralReject, []);

                if (response[0] == 0x7F && response.Length >= 3)
                    return UdsResponse.Negative(serviceId, (NegativeResponseCode)response[2], response);
            }
            else
            {
                return UdsResponse.Negative(serviceId, nrc, response);
            }
        }

        // Positive response - first byte should be service ID + 0x40
        var expectedResponseId = (byte)serviceId + 0x40;
        if (response[0] != expectedResponseId)
        {
            return UdsResponse.Negative(serviceId, NegativeResponseCode.GeneralReject, response);
        }

        return UdsResponse.Positive(serviceId, response[1..], response);
    }
}
