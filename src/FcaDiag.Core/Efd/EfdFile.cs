namespace FcaDiag.Core.Efd;

/// <summary>
/// Represents a CDA6 EFD (EPROM Flash Data) file
/// </summary>
public class EfdFile
{
    /// <summary>
    /// EFD file magic bytes
    /// </summary>
    public static readonly byte[] MagicBytes = [0x1A, 0x45, 0xDF, 0xA3];

    /// <summary>
    /// Original file path
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// File name without path
    /// </summary>
    public string FileName => Path.GetFileName(FilePath ?? "Unknown.efd");

    /// <summary>
    /// Part number from filename (e.g., 68352654ae)
    /// </summary>
    public string PartNumber { get; set; } = string.Empty;

    /// <summary>
    /// Model year
    /// </summary>
    public int ModelYear { get; set; }

    /// <summary>
    /// Drive train type (e.g., FWD/RWD, AWD)
    /// </summary>
    public string DriveTrain { get; set; } = string.Empty;

    /// <summary>
    /// Engine type (e.g., 3.6Phoenix)
    /// </summary>
    public string Engine { get; set; } = string.Empty;

    /// <summary>
    /// Fuel type (e.g., UNLEADED, DIESEL)
    /// </summary>
    public string FuelType { get; set; } = string.Empty;

    /// <summary>
    /// Transmission type (e.g., 948TE)
    /// </summary>
    public string Transmission { get; set; } = string.Empty;

    /// <summary>
    /// Body style code
    /// </summary>
    public string BodyStyle { get; set; } = string.Empty;

    /// <summary>
    /// Emissions standard
    /// </summary>
    public string Emissions { get; set; } = string.Empty;

    /// <summary>
    /// Program identifier
    /// </summary>
    public string Program { get; set; } = string.Empty;

    /// <summary>
    /// Calibration level (SERVICE, ENGINEERING, etc.)
    /// </summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>
    /// Software/calibration version
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Country code
    /// </summary>
    public string Country { get; set; } = string.Empty;

    /// <summary>
    /// Generator software name
    /// </summary>
    public string GeneratorName { get; set; } = string.Empty;

    /// <summary>
    /// Generator software version
    /// </summary>
    public string GeneratorVersion { get; set; } = string.Empty;

    /// <summary>
    /// File creation date if available
    /// </summary>
    public DateTime? CreationDate { get; set; }

    /// <summary>
    /// Raw calibration data blocks
    /// </summary>
    public List<EfdDataBlock> DataBlocks { get; set; } = [];

    /// <summary>
    /// Total size of calibration data in bytes
    /// </summary>
    public long TotalDataSize => DataBlocks.Sum(b => b.Data.Length);

    /// <summary>
    /// Raw file size
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// All metadata as key-value pairs
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>
    /// Target ECU module type
    /// </summary>
    public string TargetModule { get; set; } = "PCM";

    /// <summary>
    /// Gets a display-friendly description of the file
    /// </summary>
    public string Description => $"{ModelYear} {Engine} {Transmission} - {Program}";

    /// <summary>
    /// Gets the file size in a human-readable format
    /// </summary>
    public string FileSizeDisplay
    {
        get
        {
            if (FileSize < 1024) return $"{FileSize} B";
            if (FileSize < 1024 * 1024) return $"{FileSize / 1024.0:F1} KB";
            return $"{FileSize / (1024.0 * 1024.0):F2} MB";
        }
    }
}

/// <summary>
/// Represents a block of calibration data within an EFD file
/// </summary>
public class EfdDataBlock
{
    /// <summary>
    /// Block identifier/name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Start address in ECU memory
    /// </summary>
    public uint StartAddress { get; set; }

    /// <summary>
    /// Block data
    /// </summary>
    public byte[] Data { get; set; } = [];

    /// <summary>
    /// Checksum value
    /// </summary>
    public uint Checksum { get; set; }

    /// <summary>
    /// Block size in bytes
    /// </summary>
    public int Size => Data.Length;
}
