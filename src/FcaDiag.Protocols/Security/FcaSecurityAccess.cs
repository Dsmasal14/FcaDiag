namespace FcaDiag.Protocols.Security;

/// <summary>
/// FCA/Stellantis Security Access seed-key calculator
/// Used for UDS Service 0x27 (Security Access) to unlock protected ECU functions
/// </summary>
public static class FcaSecurityAccess
{
    // FCA security constants for different access levels
    private static readonly Dictionary<byte, uint> SecurityConstants = new()
    {
        [0x01] = 0x52316B65,  // Level 1 - Basic diagnostics
        [0x03] = 0x46435365,  // Level 3 - VIN write
        [0x05] = 0x50726F67,  // Level 5 - Programming/Flashing
        [0x07] = 0x456E6765,  // Level 7 - Engineering
        [0x11] = 0x53657276,  // Level 17 - Service
        [0x61] = 0x454F4C21,  // Level 97 - EOL (End of Line)
    };

    /// <summary>
    /// Calculates the security key from a seed using FCA algorithm
    /// </summary>
    /// <param name="seed">The seed bytes received from the ECU</param>
    /// <param name="securityLevel">The security access level (odd number: 0x01, 0x03, etc.)</param>
    /// <returns>The calculated key bytes to send back</returns>
    public static byte[] CalculateKey(byte[] seed, byte securityLevel)
    {
        if (seed == null || seed.Length < 4)
            return [];

        // Convert seed bytes to uint32 (big-endian)
        uint seedValue = (uint)((seed[0] << 24) | (seed[1] << 16) | (seed[2] << 8) | seed[3]);

        // Get the secret constant for this security level
        uint secretConstant = SecurityConstants.TryGetValue(securityLevel, out var constant) ? constant : 0x46434131;

        // Calculate the key using FCA algorithm
        uint keyValue = CalculateKeyInternal(seedValue, secretConstant);

        // Convert back to bytes (big-endian)
        return [
            (byte)(keyValue >> 24),
            (byte)(keyValue >> 16),
            (byte)(keyValue >> 8),
            (byte)(keyValue & 0xFF)
        ];
    }

    /// <summary>
    /// Core seed-key calculation algorithm
    /// </summary>
    private static uint CalculateKeyInternal(uint seed, uint secretConstant)
    {
        uint key = seed;

        // Step 1: Initial XOR Scramble
        // Obfuscate the seed using a static mask
        key = key ^ 0xA5A5A5A5;

        // Step 2: Bitwise Rotation and Arithmetic Mixing
        // Rotate left by 5 bits to shift bit positions
        key = (key << 5) | (key >> 27);
        // Add the secret constant (shared secret)
        key = key + secretConstant;

        // Step 3: Conditional Non-Linearity
        // Apply a transformation only if a specific bit condition is met
        if ((key & 0x00000001) != 0)
        {
            key = key ^ 0xDEADBEEF;
        }
        else
        {
            key = key ^ 0xC0DEBABE;
        }

        return key;
    }

    /// <summary>
    /// Creates a key calculator function for use with UdsClient.SecurityAccessAsync
    /// </summary>
    /// <param name="securityLevel">The security access level</param>
    /// <returns>A function that calculates the key from seed bytes</returns>
    public static Func<byte[], byte[]> GetKeyCalculator(byte securityLevel)
    {
        return seed => CalculateKey(seed, securityLevel);
    }

    /// <summary>
    /// Security level for basic diagnostic operations
    /// </summary>
    public const byte LevelDiagnostic = 0x01;

    /// <summary>
    /// Security level for VIN read/write operations
    /// </summary>
    public const byte LevelVinAccess = 0x03;

    /// <summary>
    /// Security level for ECU programming/flashing
    /// </summary>
    public const byte LevelProgramming = 0x05;

    /// <summary>
    /// Security level for engineering mode
    /// </summary>
    public const byte LevelEngineering = 0x07;

    /// <summary>
    /// Security level for service operations
    /// </summary>
    public const byte LevelService = 0x11;

    /// <summary>
    /// Security level for End-of-Line programming
    /// </summary>
    public const byte LevelEOL = 0x61;
}
