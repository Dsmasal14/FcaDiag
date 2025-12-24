using FcaDiag.Core.Interfaces;
using FcaDiag.Core.Models;
using FcaDiag.J2534.Native;

namespace FcaDiag.J2534;

/// <summary>
/// J2534 pass-thru adapter implementation
/// </summary>
public class J2534Adapter : ICanAdapter
{
    private readonly J2534Api _api = new();
    private readonly J2534Device _device;

    private uint _deviceId;
    private uint _channelId;
    private readonly List<uint> _filterIds = [];

    private bool _isConnected;
    private J2534Protocol _protocol;

    public bool IsConnected => _isConnected;

    /// <summary>
    /// Firmware version (available after connect)
    /// </summary>
    public string? FirmwareVersion { get; private set; }

    /// <summary>
    /// DLL version (available after connect)
    /// </summary>
    public string? DllVersion { get; private set; }

    /// <summary>
    /// API version (available after connect)
    /// </summary>
    public string? ApiVersion { get; private set; }

    public J2534Adapter(J2534Device device)
    {
        _device = device;
    }

    public async Task<bool> ConnectAsync(ConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Connect(settings), cancellationToken);
    }

    private bool Connect(ConnectionSettings settings)
    {
        if (_isConnected)
            return true;

        // Load the J2534 DLL
        if (!_api.Load(_device.DllPath))
            return false;

        // Open device
        var result = _api.PassThruOpen(out _deviceId);
        if (result != J2534Error.STATUS_NOERROR)
        {
            _api.Unload();
            return false;
        }

        // Read version info
        var version = _api.PassThruReadVersion(_deviceId);
        if (version.HasValue)
        {
            FirmwareVersion = version.Value.Firmware;
            DllVersion = version.Value.Dll;
            ApiVersion = version.Value.Api;
        }

        // Determine protocol based on adapter type
        _protocol = settings.AdapterType.ToUpperInvariant() switch
        {
            "ISO15765" or "UDS" => J2534Protocol.ISO15765,
            _ => J2534Protocol.CAN
        };

        // Connect to CAN bus
        var flags = J2534ConnectFlag.NONE;
        result = _api.PassThruConnect(_deviceId, _protocol, flags, (uint)settings.Bitrate, out _channelId);
        if (result != J2534Error.STATUS_NOERROR)
        {
            _api.PassThruClose(_deviceId);
            _api.Unload();
            return false;
        }

        // Clear buffers
        _api.PassThruClearTxBuffer(_channelId);
        _api.PassThruClearRxBuffer(_channelId);

        _isConnected = true;
        return true;
    }

    public async Task DisconnectAsync()
    {
        await Task.Run(Disconnect);
    }

    private void Disconnect()
    {
        if (!_isConnected)
            return;

        // Remove all filters
        foreach (var filterId in _filterIds)
        {
            _api.PassThruStopMsgFilter(_channelId, filterId);
        }
        _filterIds.Clear();

        // Disconnect and close
        _api.PassThruDisconnect(_channelId);
        _api.PassThruClose(_deviceId);
        _api.Unload();

        _isConnected = false;
    }

    /// <summary>
    /// Setup a flow control filter for ISO-TP communication
    /// </summary>
    public bool SetupFlowControlFilter(uint txId, uint rxId)
    {
        if (!_isConnected || _protocol != J2534Protocol.ISO15765)
            return false;

        var maskMsg = PassThruMsg.Create(_protocol);
        maskMsg.TxFlags = (uint)J2534TxFlag.ISO15765_FRAME_PAD;
        maskMsg.DataSize = 4;
        maskMsg.Data[0] = 0xFF;
        maskMsg.Data[1] = 0xFF;
        maskMsg.Data[2] = 0xFF;
        maskMsg.Data[3] = 0xFF;

        var patternMsg = PassThruMsg.Create(_protocol);
        patternMsg.TxFlags = (uint)J2534TxFlag.ISO15765_FRAME_PAD;
        patternMsg.DataSize = 4;
        patternMsg.Data[0] = (byte)(rxId >> 24);
        patternMsg.Data[1] = (byte)(rxId >> 16);
        patternMsg.Data[2] = (byte)(rxId >> 8);
        patternMsg.Data[3] = (byte)rxId;

        var flowControlMsg = PassThruMsg.Create(_protocol);
        flowControlMsg.TxFlags = (uint)J2534TxFlag.ISO15765_FRAME_PAD;
        flowControlMsg.DataSize = 4;
        flowControlMsg.Data[0] = (byte)(txId >> 24);
        flowControlMsg.Data[1] = (byte)(txId >> 16);
        flowControlMsg.Data[2] = (byte)(txId >> 8);
        flowControlMsg.Data[3] = (byte)txId;

        var result = _api.PassThruStartMsgFilter(_channelId, J2534FilterType.FLOW_CONTROL_FILTER,
            ref maskMsg, ref patternMsg, ref flowControlMsg, out var filterId);

        if (result == J2534Error.STATUS_NOERROR)
        {
            _filterIds.Add(filterId);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Setup a pass filter for receiving specific CAN IDs
    /// </summary>
    public bool SetupPassFilter(uint canId)
    {
        if (!_isConnected)
            return false;

        var maskMsg = PassThruMsg.Create(_protocol);
        maskMsg.DataSize = 4;
        maskMsg.Data[0] = 0xFF;
        maskMsg.Data[1] = 0xFF;
        maskMsg.Data[2] = 0xFF;
        maskMsg.Data[3] = 0xFF;

        var patternMsg = PassThruMsg.Create(_protocol);
        patternMsg.DataSize = 4;
        patternMsg.Data[0] = (byte)(canId >> 24);
        patternMsg.Data[1] = (byte)(canId >> 16);
        patternMsg.Data[2] = (byte)(canId >> 8);
        patternMsg.Data[3] = (byte)canId;

        var flowControlMsg = PassThruMsg.Create(_protocol);

        var result = _api.PassThruStartMsgFilter(_channelId, J2534FilterType.PASS_FILTER,
            ref maskMsg, ref patternMsg, ref flowControlMsg, out var filterId);

        if (result == J2534Error.STATUS_NOERROR)
        {
            _filterIds.Add(filterId);
            return true;
        }

        return false;
    }

    public async Task SendAsync(uint canId, byte[] data, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => Send(canId, data), cancellationToken);
    }

    private void Send(uint canId, byte[] data)
    {
        if (!_isConnected)
            throw new InvalidOperationException("Not connected");

        // Build message with CAN ID prefix
        var msgData = new byte[4 + data.Length];
        msgData[0] = (byte)(canId >> 24);
        msgData[1] = (byte)(canId >> 16);
        msgData[2] = (byte)(canId >> 8);
        msgData[3] = (byte)canId;
        Array.Copy(data, 0, msgData, 4, data.Length);

        var msg = PassThruMsg.CreateTx(_protocol, msgData,
            _protocol == J2534Protocol.ISO15765 ? J2534TxFlag.ISO15765_FRAME_PAD : J2534TxFlag.NONE);

        uint numMsgs = 1;
        var result = _api.PassThruWriteMsgs(_channelId, ref msg, ref numMsgs, 1000);

        if (result != J2534Error.STATUS_NOERROR)
            throw new IOException($"J2534 write failed: {result}");
    }

    public async Task<byte[]?> ReceiveAsync(uint canId, int timeoutMs, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Receive(canId, timeoutMs), cancellationToken);
    }

    private byte[]? Receive(uint canId, int timeoutMs)
    {
        if (!_isConnected)
            return null;

        var msg = PassThruMsg.Create(_protocol);
        uint numMsgs = 1;

        var result = _api.PassThruReadMsgs(_channelId, ref msg, ref numMsgs, (uint)timeoutMs);

        if (result == J2534Error.ERR_BUFFER_EMPTY || result == J2534Error.ERR_TIMEOUT)
            return null;

        if (result != J2534Error.STATUS_NOERROR || numMsgs == 0)
            return null;

        // Check if this is our expected response
        if (msg.DataSize < 4)
            return null;

        var receivedId = (uint)((msg.Data[0] << 24) | (msg.Data[1] << 16) | (msg.Data[2] << 8) | msg.Data[3]);
        if (receivedId != canId)
            return null;

        // Return data without CAN ID
        var data = new byte[msg.DataSize - 4];
        Array.Copy(msg.Data, 4, data, 0, data.Length);
        return data;
    }

    public async Task<byte[]?> TransactAsync(uint txId, uint rxId, byte[] data, int timeoutMs, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => Transact(txId, rxId, data, timeoutMs), cancellationToken);
    }

    private byte[]? Transact(uint txId, uint rxId, byte[] data, int timeoutMs)
    {
        if (!_isConnected)
            return null;

        // Clear RX buffer before sending
        _api.PassThruClearRxBuffer(_channelId);

        // Send request
        Send(txId, data);

        // Wait for response
        var startTime = Environment.TickCount;
        while (Environment.TickCount - startTime < timeoutMs)
        {
            var response = Receive(rxId, Math.Min(100, timeoutMs));
            if (response != null)
                return response;
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _api.Dispose();
    }
}
