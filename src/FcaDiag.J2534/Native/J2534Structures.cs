using System.Runtime.InteropServices;

namespace FcaDiag.J2534.Native;

/// <summary>
/// J2534 PASSTHRU_MSG structure for sending/receiving messages
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PassThruMsg
{
    public uint ProtocolID;
    public uint RxStatus;
    public uint TxFlags;
    public uint Timestamp;
    public uint DataSize;
    public uint ExtraDataIndex;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4128)]
    public byte[] Data;

    public static PassThruMsg Create(J2534Protocol protocol)
    {
        return new PassThruMsg
        {
            ProtocolID = (uint)protocol,
            Data = new byte[4128]
        };
    }

    public static PassThruMsg CreateTx(J2534Protocol protocol, byte[] data, J2534TxFlag flags = J2534TxFlag.NONE)
    {
        var msg = Create(protocol);
        msg.TxFlags = (uint)flags;
        msg.DataSize = (uint)data.Length;
        Array.Copy(data, msg.Data, data.Length);
        return msg;
    }
}

/// <summary>
/// J2534 SCONFIG structure for configuration parameters
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SConfig
{
    public uint Parameter;
    public uint Value;
}

/// <summary>
/// J2534 SCONFIG_LIST structure for multiple config parameters
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SConfigList
{
    public uint NumOfParams;
    public IntPtr ConfigPtr;
}

/// <summary>
/// J2534 SBYTE_ARRAY structure for byte arrays
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SByteArray
{
    public uint NumOfBytes;
    public IntPtr BytePtr;
}

/// <summary>
/// J2534 device info from registry
/// </summary>
public class J2534Device
{
    public required string Name { get; init; }
    public required string Vendor { get; init; }
    public required string DllPath { get; init; }
    public string? ConfigApplication { get; init; }

    public override string ToString() => $"{Vendor} - {Name}";
}
