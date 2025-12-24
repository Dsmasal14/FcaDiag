using FcaDiag.Core.Enums;

namespace FcaDiag.Core.Models;

/// <summary>
/// Represents a UDS response from an ECU
/// </summary>
public class UdsResponse
{
    /// <summary>
    /// Whether this is a positive response
    /// </summary>
    public bool IsPositive { get; init; }

    /// <summary>
    /// The service ID that was requested
    /// </summary>
    public UdsServiceId ServiceId { get; init; }

    /// <summary>
    /// Negative response code (only valid if IsPositive is false)
    /// </summary>
    public NegativeResponseCode NegativeResponseCode { get; init; }

    /// <summary>
    /// Response data (excluding service ID byte for positive responses)
    /// </summary>
    public byte[] Data { get; init; } = [];

    /// <summary>
    /// Raw response bytes
    /// </summary>
    public byte[] RawData { get; init; } = [];

    public static UdsResponse Positive(UdsServiceId serviceId, byte[] data, byte[] rawData) => new()
    {
        IsPositive = true,
        ServiceId = serviceId,
        Data = data,
        RawData = rawData
    };

    public static UdsResponse Negative(UdsServiceId serviceId, NegativeResponseCode nrc, byte[] rawData) => new()
    {
        IsPositive = false,
        ServiceId = serviceId,
        NegativeResponseCode = nrc,
        RawData = rawData
    };

    public override string ToString() => IsPositive
        ? $"Positive: {ServiceId}, Data[{Data.Length}]"
        : $"Negative: {ServiceId}, NRC: {NegativeResponseCode}";
}
