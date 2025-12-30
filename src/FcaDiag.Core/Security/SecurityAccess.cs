using System.Text;
using System.Text.Json;

namespace FcaDiag.Core.Security;

/// <summary>
/// Security Access levels used in UDS (Service 0x27)
/// Odd numbers = Request Seed, Even numbers = Send Key
/// </summary>
public enum SecurityLevel
{
    Level1_RequestSeed = 0x01,
    Level1_SendKey = 0x02,
    Level3_RequestSeed = 0x03,
    Level3_SendKey = 0x04,
    Level5_RequestSeed = 0x05,  // Programming level (FCA)
    Level5_SendKey = 0x06,
    Level7_RequestSeed = 0x07,
    Level7_SendKey = 0x08,
    Level9_RequestSeed = 0x09,
    Level9_SendKey = 0x0A,
    Level11_RequestSeed = 0x0B,  // Extended diagnostic level
    Level11_SendKey = 0x0C
}

/// <summary>
/// Captured seed/key pair for analysis
/// </summary>
public class SeedKeyPair
{
    public DateTime Timestamp { get; set; }
    public string ModuleName { get; set; } = string.Empty;
    public uint ModuleAddress { get; set; }
    public SecurityLevel Level { get; set; }
    public byte[] Seed { get; set; } = [];
    public byte[] Key { get; set; } = [];
    public bool WasAccepted { get; set; }
    public string? Notes { get; set; }

    public string SeedHex => BitConverter.ToString(Seed).Replace("-", " ");
    public string KeyHex => BitConverter.ToString(Key).Replace("-", " ");

    public override string ToString() =>
        $"[{Timestamp:HH:mm:ss}] {ModuleName} (0x{ModuleAddress:X3}) Level {(int)Level}: Seed={SeedHex} Key={KeyHex} Accepted={WasAccepted}";
}

/// <summary>
/// Security Access manager with seed/key logging and algorithm support
/// </summary>
public static class SecurityAccessManager
{
    private static readonly List<SeedKeyPair> _capturedPairs = [];
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "StellaFlash", "seedkey_log.json");

    /// <summary>
    /// Calculate key from seed using FCA algorithm
    /// Currently a placeholder - needs real algorithm implementation
    /// </summary>
    public static byte[] CalculateKey(byte[] seed, SecurityLevel level, uint moduleAddress)
    {
        // FCA Level 5 algorithm placeholder
        // Based on analysis of captured data: Seed CC 55 4A F6 -> Key B5 D9 F5 C6
        // This needs to be reverse engineered from multiple seed/key pairs

        return level switch
        {
            SecurityLevel.Level5_SendKey => CalculateFcaLevel5Key(seed),
            SecurityLevel.Level1_SendKey => CalculateGenericKey(seed, 0x01),
            SecurityLevel.Level3_SendKey => CalculateGenericKey(seed, 0x03),
            _ => CalculateGenericKey(seed, (byte)level)
        };
    }

    /// <summary>
    /// FCA Level 5 key calculation (Programming level)
    /// TODO: Implement actual algorithm when reverse engineered
    /// </summary>
    private static byte[] CalculateFcaLevel5Key(byte[] seed)
    {
        // Known pair from capture:
        // Seed: CC 55 4A F6
        // Key:  B5 D9 F5 C6

        // Analysis of this pair:
        // Possible operations: XOR, ADD/SUB, bit rotation, lookup table
        //
        // XOR analysis:
        // CC ^ B5 = 79
        // 55 ^ D9 = 8C
        // 4A ^ F5 = BF
        // F6 ^ C6 = 30
        //
        // Difference analysis:
        // B5 - CC = -23 (0xE9 unsigned)
        // D9 - 55 = 132 (0x84)
        // F5 - 4A = 171 (0xAB)
        // C6 - F6 = -48 (0xD0 unsigned)
        //
        // Without more pairs, we cannot determine the algorithm
        // Using a placeholder that at least demonstrates the structure

        if (seed.Length != 4)
            return [0x00, 0x00, 0x00, 0x00];

        // Placeholder algorithm - NOT the real FCA algorithm
        // This uses a combination of XOR and rotation as a demonstration
        byte[] key = new byte[4];
        uint seedValue = (uint)((seed[0] << 24) | (seed[1] << 16) | (seed[2] << 8) | seed[3]);

        // Example transformation (placeholder)
        uint constant = 0x5A5A5A5A;  // Would need to find actual constant
        uint transformed = seedValue ^ constant;
        transformed = RotateLeft(transformed, 5);
        transformed ^= 0xA5A5A5A5;

        key[0] = (byte)(transformed >> 24);
        key[1] = (byte)(transformed >> 16);
        key[2] = (byte)(transformed >> 8);
        key[3] = (byte)transformed;

        return key;
    }

    /// <summary>
    /// Generic key calculation for other levels
    /// </summary>
    private static byte[] CalculateGenericKey(byte[] seed, byte level)
    {
        // Simple XOR-based placeholder
        byte[] key = new byte[seed.Length];
        for (int i = 0; i < seed.Length; i++)
        {
            key[i] = (byte)(seed[i] ^ (level + i));
        }
        return key;
    }

    private static uint RotateLeft(uint value, int count)
    {
        return (value << count) | (value >> (32 - count));
    }

    /// <summary>
    /// Log a captured seed/key pair for analysis
    /// </summary>
    public static void LogSeedKeyPair(SeedKeyPair pair)
    {
        pair.Timestamp = DateTime.Now;
        _capturedPairs.Add(pair);
        SaveToFile();
    }

    /// <summary>
    /// Log a seed/key attempt
    /// </summary>
    public static void LogAttempt(string moduleName, uint address, SecurityLevel level,
        byte[] seed, byte[] key, bool accepted, string? notes = null)
    {
        var pair = new SeedKeyPair
        {
            Timestamp = DateTime.Now,
            ModuleName = moduleName,
            ModuleAddress = address,
            Level = level,
            Seed = seed,
            Key = key,
            WasAccepted = accepted,
            Notes = notes
        };
        LogSeedKeyPair(pair);
    }

    /// <summary>
    /// Get all captured pairs
    /// </summary>
    public static IReadOnlyList<SeedKeyPair> GetCapturedPairs() => _capturedPairs.AsReadOnly();

    /// <summary>
    /// Get pairs for a specific security level
    /// </summary>
    public static IEnumerable<SeedKeyPair> GetPairsForLevel(SecurityLevel level) =>
        _capturedPairs.Where(p => p.Level == level);

    /// <summary>
    /// Get accepted pairs only (useful for algorithm analysis)
    /// </summary>
    public static IEnumerable<SeedKeyPair> GetAcceptedPairs() =>
        _capturedPairs.Where(p => p.WasAccepted);

    /// <summary>
    /// Export captured pairs to CSV for external analysis
    /// </summary>
    public static void ExportToCsv(string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,Module,Address,Level,Seed,Key,Accepted,Notes");

        foreach (var pair in _capturedPairs)
        {
            sb.AppendLine($"{pair.Timestamp:yyyy-MM-dd HH:mm:ss}," +
                $"{pair.ModuleName}," +
                $"0x{pair.ModuleAddress:X3}," +
                $"{(int)pair.Level}," +
                $"{pair.SeedHex}," +
                $"{pair.KeyHex}," +
                $"{pair.WasAccepted}," +
                $"\"{pair.Notes ?? ""}\"");
        }

        File.WriteAllText(filePath, sb.ToString());
    }

    /// <summary>
    /// Analyze captured pairs and generate statistics
    /// </summary>
    public static string AnalyzeCapturedPairs()
    {
        if (_capturedPairs.Count == 0)
            return "No seed/key pairs captured yet.";

        var sb = new StringBuilder();
        sb.AppendLine("=== Seed/Key Analysis Report ===");
        sb.AppendLine($"Total captured pairs: {_capturedPairs.Count}");
        sb.AppendLine($"Accepted pairs: {_capturedPairs.Count(p => p.WasAccepted)}");
        sb.AppendLine($"Rejected pairs: {_capturedPairs.Count(p => !p.WasAccepted)}");
        sb.AppendLine();

        // Group by security level
        var byLevel = _capturedPairs.GroupBy(p => p.Level);
        foreach (var group in byLevel)
        {
            sb.AppendLine($"Level {(int)group.Key}: {group.Count()} pairs ({group.Count(p => p.WasAccepted)} accepted)");
        }
        sb.AppendLine();

        // Show accepted pairs for algorithm analysis
        var accepted = _capturedPairs.Where(p => p.WasAccepted).ToList();
        if (accepted.Count > 0)
        {
            sb.AppendLine("=== Accepted Pairs (for algorithm analysis) ===");
            foreach (var pair in accepted)
            {
                sb.AppendLine($"  Seed: {pair.SeedHex}  ->  Key: {pair.KeyHex}  (Level {(int)pair.Level})");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Add known good pair from external capture (like the ASC file)
    /// </summary>
    public static void AddKnownPair(byte[] seed, byte[] key, SecurityLevel level, string moduleName, uint address)
    {
        var pair = new SeedKeyPair
        {
            Timestamp = DateTime.Now,
            ModuleName = moduleName,
            ModuleAddress = address,
            Level = level,
            Seed = seed,
            Key = key,
            WasAccepted = true,
            Notes = "Imported from CAN capture"
        };
        _capturedPairs.Add(pair);
        SaveToFile();
    }

    /// <summary>
    /// Load captured pairs from file
    /// </summary>
    public static void LoadFromFile()
    {
        try
        {
            if (File.Exists(LogFilePath))
            {
                var json = File.ReadAllText(LogFilePath);
                var pairs = JsonSerializer.Deserialize<List<SeedKeyPair>>(json);
                if (pairs != null)
                {
                    _capturedPairs.Clear();
                    _capturedPairs.AddRange(pairs);
                }
            }
        }
        catch
        {
            // Ignore load errors
        }
    }

    /// <summary>
    /// Save captured pairs to file
    /// </summary>
    private static void SaveToFile()
    {
        try
        {
            var directory = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(_capturedPairs, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(LogFilePath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    /// <summary>
    /// Clear all captured pairs
    /// </summary>
    public static void ClearCapturedPairs()
    {
        _capturedPairs.Clear();
        if (File.Exists(LogFilePath))
            File.Delete(LogFilePath);
    }
}

/// <summary>
/// Known FCA security constants and patterns
/// </summary>
public static class FcaSecurityConstants
{
    // Security levels used by FCA/Stellantis
    public const byte ProgrammingLevel = 0x05;
    public const byte ExtendedDiagLevel = 0x0B;
    public const byte ManufacturerLevel = 0x11;

    // Diagnostic session types
    public const byte DefaultSession = 0x01;
    public const byte ProgrammingSession = 0x02;
    public const byte ExtendedSession = 0x03;

    // Common FCA diagnostic CAN IDs
    public static readonly Dictionary<uint, string> DiagnosticIds = new()
    {
        [0x7E0] = "PCM Request",
        [0x7E8] = "PCM Response",
        [0x7E1] = "TCM Request",
        [0x7E9] = "TCM Response",
        [0x760] = "BCM Request",
        [0x768] = "BCM Response",
        [0x762] = "IPC Request",
        [0x76A] = "IPC Response",
        [0x720] = "Radio Request",
        [0x728] = "Radio Response",
        [0x7DF] = "Functional Broadcast"
    };

    // Known capture from ASC file
    public static readonly byte[] KnownSeed = [0xCC, 0x55, 0x4A, 0xF6];
    public static readonly byte[] KnownKey = [0xB5, 0xD9, 0xF5, 0xC6];
}
