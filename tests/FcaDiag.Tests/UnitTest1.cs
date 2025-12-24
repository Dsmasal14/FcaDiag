using FcaDiag.Core.Enums;
using FcaDiag.Core.Models;
using FcaDiag.Protocols.Transport;
using FcaDiag.Protocols.Uds;

namespace FcaDiag.Tests;

public class ModuleDatabaseTests
{
    [Fact]
    public void GetModule_ReturnsCorrectModule()
    {
        var pcm = FcaModuleDatabase.GetModule(FcaModuleType.PCM);

        Assert.NotNull(pcm);
        Assert.Equal("PCM", pcm.ShortName);
        Assert.Equal((uint)0x7E0, pcm.RequestId);
        Assert.Equal((uint)0x7E8, pcm.ResponseId);
    }

    [Fact]
    public void GetModulesByNetwork_ReturnsCorrectModules()
    {
        var canCModules = FcaModuleDatabase.GetModulesByNetwork(FcaCanNetwork.CanC).ToList();

        Assert.Contains(canCModules, m => m.ModuleType == FcaModuleType.PCM);
        Assert.Contains(canCModules, m => m.ModuleType == FcaModuleType.TCM);
        Assert.Contains(canCModules, m => m.ModuleType == FcaModuleType.ABS);
    }
}

public class DiagnosticTroubleCodeTests
{
    [Fact]
    public void DisplayCode_FormatsCorrectly()
    {
        // P0300 = 0x0300 in the raw format
        var dtc = new DiagnosticTroubleCode { Code = 0x030000, Status = 0x08 };

        Assert.StartsWith("P", dtc.DisplayCode);
        Assert.True(dtc.Confirmed);
    }

    [Fact]
    public void StatusFlags_ParseCorrectly()
    {
        var dtc = new DiagnosticTroubleCode { Code = 0x010100, Status = 0x2F };

        Assert.True(dtc.TestFailed);
        Assert.True(dtc.TestFailedThisOperationCycle);
        Assert.True(dtc.Pending);
        Assert.True(dtc.Confirmed);
        Assert.True(dtc.TestFailedSinceLastClear);
    }
}

public class IsoTpHandlerTests
{
    [Fact]
    public void SegmentData_SingleFrame_ReturnsOneFrame()
    {
        var data = new byte[] { 0x22, 0xF1, 0x90 }; // Read VIN request
        var frames = IsoTpHandler.SegmentData(data);

        Assert.Single(frames);
        Assert.Equal((byte)0x03, frames[0][0]); // Single frame, length 3
    }

    [Fact]
    public void SegmentData_MultiFrame_ReturnsMultipleFrames()
    {
        var data = new byte[20]; // Larger than single frame
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)i;

        var frames = IsoTpHandler.SegmentData(data);

        Assert.True(frames.Count > 1);
        Assert.Equal((byte)0x10, (byte)(frames[0][0] & 0xF0)); // First frame
        Assert.Equal((byte)0x20, (byte)(frames[1][0] & 0xF0)); // Consecutive frame
    }

    [Fact]
    public void ReassembleData_RoundTrip_PreservesData()
    {
        var original = new byte[20];
        for (int i = 0; i < original.Length; i++)
            original[i] = (byte)(i + 1);

        var frames = IsoTpHandler.SegmentData(original);
        var reassembled = IsoTpHandler.ReassembleData(frames);

        Assert.NotNull(reassembled);
        Assert.Equal(original, reassembled);
    }
}

public class UdsResponseTests
{
    [Fact]
    public void Positive_CreatesPositiveResponse()
    {
        var response = UdsResponse.Positive(
            UdsServiceId.ReadDataByIdentifier,
            [0xF1, 0x90, 0x31, 0x32, 0x33],
            [0x62, 0xF1, 0x90, 0x31, 0x32, 0x33]);

        Assert.True(response.IsPositive);
        Assert.Equal(UdsServiceId.ReadDataByIdentifier, response.ServiceId);
        Assert.Equal(5, response.Data.Length);
    }

    [Fact]
    public void Negative_CreatesNegativeResponse()
    {
        var response = UdsResponse.Negative(
            UdsServiceId.ReadDataByIdentifier,
            NegativeResponseCode.ServiceNotSupported,
            [0x7F, 0x22, 0x11]);

        Assert.False(response.IsPositive);
        Assert.Equal(NegativeResponseCode.ServiceNotSupported, response.NegativeResponseCode);
    }
}
