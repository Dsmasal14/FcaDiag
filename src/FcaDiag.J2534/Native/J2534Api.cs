using System.Runtime.InteropServices;

namespace FcaDiag.J2534.Native;

/// <summary>
/// P/Invoke wrapper for J2534 DLL functions
/// </summary>
public sealed class J2534Api : IDisposable
{
    private IntPtr _libraryHandle;
    private bool _disposed;

    // Function delegates
    private delegate J2534Error PassThruOpenDelegate(IntPtr pName, out uint pDeviceID);
    private delegate J2534Error PassThruCloseDelegate(uint DeviceID);
    private delegate J2534Error PassThruConnectDelegate(uint DeviceID, uint ProtocolID, uint Flags, uint BaudRate, out uint pChannelID);
    private delegate J2534Error PassThruDisconnectDelegate(uint ChannelID);
    private delegate J2534Error PassThruReadMsgsDelegate(uint ChannelID, ref PassThruMsg pMsg, ref uint pNumMsgs, uint Timeout);
    private delegate J2534Error PassThruWriteMsgsDelegate(uint ChannelID, ref PassThruMsg pMsg, ref uint pNumMsgs, uint Timeout);
    private delegate J2534Error PassThruStartMsgFilterDelegate(uint ChannelID, uint FilterType, ref PassThruMsg pMaskMsg, ref PassThruMsg pPatternMsg, ref PassThruMsg pFlowControlMsg, out uint pFilterID);
    private delegate J2534Error PassThruStopMsgFilterDelegate(uint ChannelID, uint FilterID);
    private delegate J2534Error PassThruIoctlDelegate(uint ChannelID, uint IoctlID, IntPtr pInput, IntPtr pOutput);
    private delegate J2534Error PassThruReadVersionDelegate(uint DeviceID, IntPtr pFirmwareVersion, IntPtr pDllVersion, IntPtr pApiVersion);
    private delegate J2534Error PassThruGetLastErrorDelegate(IntPtr pErrorDescription);

    // Function pointers
    private PassThruOpenDelegate? _passThruOpen;
    private PassThruCloseDelegate? _passThruClose;
    private PassThruConnectDelegate? _passThruConnect;
    private PassThruDisconnectDelegate? _passThruDisconnect;
    private PassThruReadMsgsDelegate? _passThruReadMsgs;
    private PassThruWriteMsgsDelegate? _passThruWriteMsgs;
    private PassThruStartMsgFilterDelegate? _passThruStartMsgFilter;
    private PassThruStopMsgFilterDelegate? _passThruStopMsgFilter;
    private PassThruIoctlDelegate? _passThruIoctl;
    private PassThruReadVersionDelegate? _passThruReadVersion;
    private PassThruGetLastErrorDelegate? _passThruGetLastError;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    public bool IsLoaded => _libraryHandle != IntPtr.Zero;

    /// <summary>
    /// Load J2534 DLL from specified path
    /// </summary>
    public bool Load(string dllPath)
    {
        if (_libraryHandle != IntPtr.Zero)
            Unload();

        _libraryHandle = LoadLibrary(dllPath);
        if (_libraryHandle == IntPtr.Zero)
            return false;

        try
        {
            _passThruOpen = GetDelegate<PassThruOpenDelegate>("PassThruOpen");
            _passThruClose = GetDelegate<PassThruCloseDelegate>("PassThruClose");
            _passThruConnect = GetDelegate<PassThruConnectDelegate>("PassThruConnect");
            _passThruDisconnect = GetDelegate<PassThruDisconnectDelegate>("PassThruDisconnect");
            _passThruReadMsgs = GetDelegate<PassThruReadMsgsDelegate>("PassThruReadMsgs");
            _passThruWriteMsgs = GetDelegate<PassThruWriteMsgsDelegate>("PassThruWriteMsgs");
            _passThruStartMsgFilter = GetDelegate<PassThruStartMsgFilterDelegate>("PassThruStartMsgFilter");
            _passThruStopMsgFilter = GetDelegate<PassThruStopMsgFilterDelegate>("PassThruStopMsgFilter");
            _passThruIoctl = GetDelegate<PassThruIoctlDelegate>("PassThruIoctl");
            _passThruReadVersion = GetDelegate<PassThruReadVersionDelegate>("PassThruReadVersion");
            _passThruGetLastError = GetDelegate<PassThruGetLastErrorDelegate>("PassThruGetLastError");

            return true;
        }
        catch
        {
            Unload();
            return false;
        }
    }

    private T GetDelegate<T>(string functionName) where T : Delegate
    {
        var ptr = GetProcAddress(_libraryHandle, functionName);
        if (ptr == IntPtr.Zero)
            throw new EntryPointNotFoundException($"Function {functionName} not found in J2534 DLL");

        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    public void Unload()
    {
        if (_libraryHandle != IntPtr.Zero)
        {
            FreeLibrary(_libraryHandle);
            _libraryHandle = IntPtr.Zero;
        }

        _passThruOpen = null;
        _passThruClose = null;
        _passThruConnect = null;
        _passThruDisconnect = null;
        _passThruReadMsgs = null;
        _passThruWriteMsgs = null;
        _passThruStartMsgFilter = null;
        _passThruStopMsgFilter = null;
        _passThruIoctl = null;
        _passThruReadVersion = null;
        _passThruGetLastError = null;
    }

    // Public API methods

    public J2534Error PassThruOpen(out uint deviceId)
    {
        deviceId = 0;
        return _passThruOpen?.Invoke(IntPtr.Zero, out deviceId) ?? J2534Error.ERR_FAILED;
    }

    public J2534Error PassThruClose(uint deviceId)
    {
        return _passThruClose?.Invoke(deviceId) ?? J2534Error.ERR_FAILED;
    }

    public J2534Error PassThruConnect(uint deviceId, J2534Protocol protocol, J2534ConnectFlag flags, uint baudRate, out uint channelId)
    {
        channelId = 0;
        return _passThruConnect?.Invoke(deviceId, (uint)protocol, (uint)flags, baudRate, out channelId) ?? J2534Error.ERR_FAILED;
    }

    public J2534Error PassThruDisconnect(uint channelId)
    {
        return _passThruDisconnect?.Invoke(channelId) ?? J2534Error.ERR_FAILED;
    }

    public J2534Error PassThruReadMsgs(uint channelId, ref PassThruMsg msg, ref uint numMsgs, uint timeout)
    {
        return _passThruReadMsgs?.Invoke(channelId, ref msg, ref numMsgs, timeout) ?? J2534Error.ERR_FAILED;
    }

    public J2534Error PassThruWriteMsgs(uint channelId, ref PassThruMsg msg, ref uint numMsgs, uint timeout)
    {
        return _passThruWriteMsgs?.Invoke(channelId, ref msg, ref numMsgs, timeout) ?? J2534Error.ERR_FAILED;
    }

    public J2534Error PassThruStartMsgFilter(uint channelId, J2534FilterType filterType,
        ref PassThruMsg maskMsg, ref PassThruMsg patternMsg, ref PassThruMsg flowControlMsg, out uint filterId)
    {
        filterId = 0;
        return _passThruStartMsgFilter?.Invoke(channelId, (uint)filterType,
            ref maskMsg, ref patternMsg, ref flowControlMsg, out filterId) ?? J2534Error.ERR_FAILED;
    }

    public J2534Error PassThruStopMsgFilter(uint channelId, uint filterId)
    {
        return _passThruStopMsgFilter?.Invoke(channelId, filterId) ?? J2534Error.ERR_FAILED;
    }

    public J2534Error PassThruIoctl(uint channelId, J2534Ioctl ioctlId, IntPtr input, IntPtr output)
    {
        return _passThruIoctl?.Invoke(channelId, (uint)ioctlId, input, output) ?? J2534Error.ERR_FAILED;
    }

    public J2534Error PassThruClearTxBuffer(uint channelId)
    {
        return PassThruIoctl(channelId, J2534Ioctl.CLEAR_TX_BUFFER, IntPtr.Zero, IntPtr.Zero);
    }

    public J2534Error PassThruClearRxBuffer(uint channelId)
    {
        return PassThruIoctl(channelId, J2534Ioctl.CLEAR_RX_BUFFER, IntPtr.Zero, IntPtr.Zero);
    }

    public (string Firmware, string Dll, string Api)? PassThruReadVersion(uint deviceId)
    {
        var firmware = Marshal.AllocHGlobal(256);
        var dll = Marshal.AllocHGlobal(256);
        var api = Marshal.AllocHGlobal(256);

        try
        {
            var result = _passThruReadVersion?.Invoke(deviceId, firmware, dll, api);
            if (result != J2534Error.STATUS_NOERROR)
                return null;

            return (
                Marshal.PtrToStringAnsi(firmware) ?? "",
                Marshal.PtrToStringAnsi(dll) ?? "",
                Marshal.PtrToStringAnsi(api) ?? ""
            );
        }
        finally
        {
            Marshal.FreeHGlobal(firmware);
            Marshal.FreeHGlobal(dll);
            Marshal.FreeHGlobal(api);
        }
    }

    public string? GetLastError()
    {
        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            var result = _passThruGetLastError?.Invoke(buffer);
            return result == J2534Error.STATUS_NOERROR ? Marshal.PtrToStringAnsi(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Unload();
            _disposed = true;
        }
    }
}
