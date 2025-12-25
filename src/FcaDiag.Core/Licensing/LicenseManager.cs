using System.Security.Cryptography;
using System.Text;

namespace FcaDiag.Core.Licensing;

/// <summary>
/// License key validation and management
/// </summary>
public static class LicenseManager
{
    // Secret key for license generation/validation (keep this private!)
    private const string SecretKey = "ST3LL4FL4SH-SP0T0N-2024";

    // License file path
    private static readonly string LicenseFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "StellaFlash", "license.key");

    /// <summary>
    /// Validates a license key
    /// </summary>
    public static LicenseValidationResult ValidateLicense(string licenseKey)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
            return new LicenseValidationResult(false, "License key is empty");

        // Clean up the key
        licenseKey = licenseKey.Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "");

        // Key format: XXXXX-XXXXX-XXXXX-XXXXX-XXXXX (25 chars without dashes)
        if (licenseKey.Length != 25)
            return new LicenseValidationResult(false, "Invalid license key format");

        // Extract parts
        var datePart = licenseKey[..4];      // First 4 chars encode expiry
        var typePart = licenseKey[4..6];     // 2 chars for license type
        var randomPart = licenseKey[6..20];  // 14 random chars
        var checksumPart = licenseKey[20..]; // Last 5 chars checksum

        // Validate checksum
        var dataToCheck = datePart + typePart + randomPart;
        var expectedChecksum = GenerateChecksum(dataToCheck);

        if (checksumPart != expectedChecksum)
            return new LicenseValidationResult(false, "Invalid license key");

        // Decode license type
        var licenseType = typePart switch
        {
            "PR" => LicenseType.Professional,
            "EN" => LicenseType.Enterprise,
            "TR" => LicenseType.Trial,
            "LT" => LicenseType.Lifetime,
            _ => LicenseType.Invalid
        };

        if (licenseType == LicenseType.Invalid)
            return new LicenseValidationResult(false, "Invalid license type");

        // Decode expiry date (YYMM format, or 9999 for lifetime)
        if (!TryDecodeExpiry(datePart, out var expiryDate))
            return new LicenseValidationResult(false, "Invalid license date");

        // Check if expired (lifetime licenses never expire)
        if (licenseType != LicenseType.Lifetime && expiryDate < DateTime.Now)
            return new LicenseValidationResult(false, $"License expired on {expiryDate:yyyy-MM-dd}");

        return new LicenseValidationResult(true, "License valid", licenseType, expiryDate);
    }

    /// <summary>
    /// Generates a license key
    /// </summary>
    public static string GenerateLicense(LicenseType type, DateTime? expiryDate = null)
    {
        // Encode expiry
        string datePart;
        if (type == LicenseType.Lifetime || expiryDate == null)
        {
            datePart = "9999";
        }
        else
        {
            datePart = EncodeExpiry(expiryDate.Value);
        }

        // Encode type
        var typePart = type switch
        {
            LicenseType.Professional => "PR",
            LicenseType.Enterprise => "EN",
            LicenseType.Trial => "TR",
            LicenseType.Lifetime => "LT",
            _ => "TR"
        };

        // Generate random part
        var randomPart = GenerateRandomString(14);

        // Generate checksum
        var dataToCheck = datePart + typePart + randomPart;
        var checksumPart = GenerateChecksum(dataToCheck);

        // Combine and format
        var fullKey = datePart + typePart + randomPart + checksumPart;
        return FormatLicenseKey(fullKey);
    }

    /// <summary>
    /// Saves license key to file
    /// </summary>
    public static void SaveLicense(string licenseKey)
    {
        var directory = Path.GetDirectoryName(LicenseFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        // Encrypt the license key before saving
        var encrypted = EncryptString(licenseKey);
        File.WriteAllText(LicenseFilePath, encrypted);
    }

    /// <summary>
    /// Loads saved license key
    /// </summary>
    public static string? LoadLicense()
    {
        if (!File.Exists(LicenseFilePath))
            return null;

        try
        {
            var encrypted = File.ReadAllText(LicenseFilePath);
            return DecryptString(encrypted);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if a valid license exists
    /// </summary>
    public static LicenseValidationResult CheckSavedLicense()
    {
        var savedKey = LoadLicense();
        if (string.IsNullOrEmpty(savedKey))
            return new LicenseValidationResult(false, "No license found");

        return ValidateLicense(savedKey);
    }

    /// <summary>
    /// Removes saved license
    /// </summary>
    public static void RemoveLicense()
    {
        if (File.Exists(LicenseFilePath))
            File.Delete(LicenseFilePath);
    }

    private static string GenerateChecksum(string data)
    {
        var combined = data + SecretKey;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        var hash = Convert.ToBase64String(bytes)
            .Replace("+", "X")
            .Replace("/", "Y")
            .Replace("=", "")
            .ToUpperInvariant();
        return hash[..5];
    }

    private static string GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // Removed confusing chars (0,O,1,I)
        var random = new Random();
        return new string(Enumerable.Range(0, length).Select(_ => chars[random.Next(chars.Length)]).ToArray());
    }

    private static string EncodeExpiry(DateTime date)
    {
        // Encode as YYMM
        var yy = (date.Year % 100).ToString("D2");
        var mm = date.Month.ToString("D2");
        return yy + mm;
    }

    private static bool TryDecodeExpiry(string encoded, out DateTime date)
    {
        date = DateTime.MaxValue;

        if (encoded == "9999")
        {
            date = DateTime.MaxValue; // Lifetime
            return true;
        }

        if (encoded.Length != 4 || !int.TryParse(encoded[..2], out var yy) || !int.TryParse(encoded[2..], out var mm))
            return false;

        if (mm < 1 || mm > 12)
            return false;

        var year = 2000 + yy;
        date = new DateTime(year, mm, DateTime.DaysInMonth(year, mm)); // Last day of month
        return true;
    }

    private static string FormatLicenseKey(string key)
    {
        // Format as XXXXX-XXXXX-XXXXX-XXXXX-XXXXX
        var parts = new List<string>();
        for (int i = 0; i < key.Length; i += 5)
        {
            parts.Add(key.Substring(i, Math.Min(5, key.Length - i)));
        }
        return string.Join("-", parts);
    }

    private static string EncryptString(string plainText)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(SecretKey));
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Combine IV + encrypted data
        var result = new byte[aes.IV.Length + encryptedBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

        return Convert.ToBase64String(result);
    }

    private static string DecryptString(string encryptedText)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(SecretKey));
        var fullBytes = Convert.FromBase64String(encryptedText);

        using var aes = Aes.Create();
        aes.Key = key;

        // Extract IV
        var iv = new byte[16];
        Buffer.BlockCopy(fullBytes, 0, iv, 0, 16);
        aes.IV = iv;

        // Extract encrypted data
        var encryptedBytes = new byte[fullBytes.Length - 16];
        Buffer.BlockCopy(fullBytes, 16, encryptedBytes, 0, encryptedBytes.Length);

        using var decryptor = aes.CreateDecryptor();
        var decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

        return Encoding.UTF8.GetString(decryptedBytes);
    }
}

/// <summary>
/// License validation result
/// </summary>
public class LicenseValidationResult
{
    public bool IsValid { get; }
    public string Message { get; }
    public LicenseType Type { get; }
    public DateTime ExpiryDate { get; }

    public LicenseValidationResult(bool isValid, string message, LicenseType type = LicenseType.Invalid, DateTime expiryDate = default)
    {
        IsValid = isValid;
        Message = message;
        Type = type;
        ExpiryDate = expiryDate == default ? DateTime.MaxValue : expiryDate;
    }
}

/// <summary>
/// License types
/// </summary>
public enum LicenseType
{
    Invalid = 0,
    Trial,
    Professional,
    Enterprise,
    Lifetime
}

/// <summary>
/// Features that can be gated by license type
/// </summary>
public enum LicenseFeature
{
    // Basic features (Trial+)
    ScanModules,
    ReadDtcs,
    ClearDtcs,
    NetworkTopology,
    ModuleIdentification,
    PrintDtcReport,
    ExportActivityLog,

    // Professional features
    ReadVin,
    ClearVin,
    WriteVin,
    LoadEfdFile,
    ModuleInfoReport,

    // Enterprise features
    FlashEcu,
    SaveEcuToEfd,
    ReadEprom,
    WriteEprom,
    SwapHardware,
    RebootModule,
    BatchOperations
}

/// <summary>
/// License feature access control
/// </summary>
public static class LicenseFeatures
{
    /// <summary>
    /// Check if a feature is available for the given license type
    /// Trial: Basic diagnostics only
    /// Professional/Enterprise/Lifetime: Full access (difference is expiration only)
    /// </summary>
    public static bool IsFeatureAvailable(LicenseType licenseType, LicenseFeature feature)
    {
        return feature switch
        {
            // Basic features - available to all valid licenses (including Trial)
            LicenseFeature.ScanModules => licenseType >= LicenseType.Trial,
            LicenseFeature.ReadDtcs => licenseType >= LicenseType.Trial,
            LicenseFeature.ClearDtcs => licenseType >= LicenseType.Trial,
            LicenseFeature.NetworkTopology => licenseType >= LicenseType.Trial,
            LicenseFeature.ModuleIdentification => licenseType >= LicenseType.Trial,
            LicenseFeature.PrintDtcReport => licenseType >= LicenseType.Trial,
            LicenseFeature.ExportActivityLog => licenseType >= LicenseType.Trial,

            // Full features - available to Professional, Enterprise, and Lifetime
            // Professional: 1 year license
            // Enterprise: 1 year license
            // Lifetime: Never expires
            LicenseFeature.ReadVin => licenseType >= LicenseType.Professional,
            LicenseFeature.ClearVin => licenseType >= LicenseType.Professional,
            LicenseFeature.WriteVin => licenseType >= LicenseType.Professional,
            LicenseFeature.LoadEfdFile => licenseType >= LicenseType.Professional,
            LicenseFeature.ModuleInfoReport => licenseType >= LicenseType.Professional,
            LicenseFeature.FlashEcu => licenseType >= LicenseType.Professional,
            LicenseFeature.SaveEcuToEfd => licenseType >= LicenseType.Professional,
            LicenseFeature.ReadEprom => licenseType >= LicenseType.Professional,
            LicenseFeature.WriteEprom => licenseType >= LicenseType.Professional,
            LicenseFeature.SwapHardware => licenseType >= LicenseType.Professional,
            LicenseFeature.RebootModule => licenseType >= LicenseType.Professional,
            LicenseFeature.BatchOperations => licenseType >= LicenseType.Professional,

            _ => false
        };
    }

    /// <summary>
    /// Get the minimum license type required for a feature
    /// </summary>
    public static LicenseType GetRequiredLicense(LicenseFeature feature)
    {
        return feature switch
        {
            // Basic features available to Trial
            LicenseFeature.ScanModules or
            LicenseFeature.ReadDtcs or
            LicenseFeature.ClearDtcs or
            LicenseFeature.NetworkTopology or
            LicenseFeature.ModuleIdentification or
            LicenseFeature.PrintDtcReport or
            LicenseFeature.ExportActivityLog => LicenseType.Trial,

            // All other features require Professional or higher
            _ => LicenseType.Professional
        };
    }

    /// <summary>
    /// Get feature description for display
    /// </summary>
    public static string GetFeatureDescription(LicenseFeature feature)
    {
        return feature switch
        {
            LicenseFeature.ScanModules => "Scan Modules",
            LicenseFeature.ReadDtcs => "Read DTCs",
            LicenseFeature.ClearDtcs => "Clear DTCs",
            LicenseFeature.NetworkTopology => "Network Topology",
            LicenseFeature.ModuleIdentification => "Module Identification",
            LicenseFeature.PrintDtcReport => "Print DTC Report",
            LicenseFeature.ExportActivityLog => "Export Activity Log",
            LicenseFeature.ReadVin => "Read VIN",
            LicenseFeature.ClearVin => "Clear VIN",
            LicenseFeature.WriteVin => "Write VIN",
            LicenseFeature.LoadEfdFile => "Load EFD File",
            LicenseFeature.ModuleInfoReport => "Module Info Report",
            LicenseFeature.FlashEcu => "Flash ECU",
            LicenseFeature.SaveEcuToEfd => "Save ECU to EFD",
            LicenseFeature.ReadEprom => "Read EPROM",
            LicenseFeature.WriteEprom => "Write EPROM",
            LicenseFeature.SwapHardware => "Swap Hardware",
            LicenseFeature.RebootModule => "Reboot Module",
            LicenseFeature.BatchOperations => "Batch Operations",
            _ => feature.ToString()
        };
    }
}
