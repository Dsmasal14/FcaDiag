using FcaDiag.Core.Licensing;

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine(@"
╔═══════════════════════════════════════════════════════════════════╗
║           STELLAFLASH LICENSE KEY GENERATOR v1.0                  ║
║              Spot On Auto Diagnostics                             ║
╚═══════════════════════════════════════════════════════════════════╝
");
Console.ResetColor();

while (true)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("Select License Type:");
    Console.ResetColor();
    Console.WriteLine("  1. Trial (30 days)");
    Console.WriteLine("  2. Professional (1 year)");
    Console.WriteLine("  3. Enterprise (1 year)");
    Console.WriteLine("  4. Lifetime");
    Console.WriteLine("  5. Custom expiry date");
    Console.WriteLine("  6. Validate a key");
    Console.WriteLine("  7. Generate batch keys");
    Console.WriteLine("  0. Exit");
    Console.WriteLine();
    Console.Write("Choice: ");

    var choice = Console.ReadLine()?.Trim();

    Console.WriteLine();

    switch (choice)
    {
        case "1":
            GenerateKey(LicenseType.Trial, DateTime.Now.AddDays(30));
            break;
        case "2":
            GenerateKey(LicenseType.Professional, DateTime.Now.AddYears(1));
            break;
        case "3":
            GenerateKey(LicenseType.Enterprise, DateTime.Now.AddYears(1));
            break;
        case "4":
            GenerateKey(LicenseType.Lifetime, null);
            break;
        case "5":
            GenerateCustomKey();
            break;
        case "6":
            ValidateKey();
            break;
        case "7":
            GenerateBatchKeys();
            break;
        case "0":
            return;
        default:
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Invalid choice!");
            Console.ResetColor();
            break;
    }

    Console.WriteLine();
    Console.WriteLine("Press any key to continue...");
    Console.ReadKey(true);
    Console.Clear();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(@"
╔═══════════════════════════════════════════════════════════════════╗
║           STELLAFLASH LICENSE KEY GENERATOR v1.0                  ║
║              Spot On Auto Diagnostics                             ║
╚═══════════════════════════════════════════════════════════════════╝
");
    Console.ResetColor();
}

void GenerateKey(LicenseType type, DateTime? expiry)
{
    var key = LicenseManager.GenerateLicense(type, expiry);

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("═══════════════════════════════════════════════════════════════════");
    Console.WriteLine($"  License Type: {type}");
    Console.WriteLine($"  Expires:      {(expiry.HasValue ? expiry.Value.ToString("yyyy-MM-dd") : "Never (Lifetime)")}");
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine($"  LICENSE KEY:  {key}");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("═══════════════════════════════════════════════════════════════════");
    Console.ResetColor();

    // Verify the generated key
    var validation = LicenseManager.ValidateLicense(key);
    if (validation.IsValid)
    {
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine($"  [Verified: {validation.Message}]");
        Console.ResetColor();
    }
}

void GenerateCustomKey()
{
    Console.Write("Enter license type (Trial/Professional/Enterprise/Lifetime): ");
    var typeStr = Console.ReadLine()?.Trim();

    var type = typeStr?.ToLower() switch
    {
        "trial" or "t" => LicenseType.Trial,
        "professional" or "pro" or "p" => LicenseType.Professional,
        "enterprise" or "ent" or "e" => LicenseType.Enterprise,
        "lifetime" or "lt" or "l" => LicenseType.Lifetime,
        _ => LicenseType.Professional
    };

    DateTime? expiry = null;
    if (type != LicenseType.Lifetime)
    {
        Console.Write("Enter expiry date (yyyy-MM-dd) or days from now (e.g., 365): ");
        var expiryStr = Console.ReadLine()?.Trim();

        if (int.TryParse(expiryStr, out var days))
        {
            expiry = DateTime.Now.AddDays(days);
        }
        else if (DateTime.TryParse(expiryStr, out var date))
        {
            expiry = date;
        }
        else
        {
            expiry = DateTime.Now.AddYears(1);
        }
    }

    GenerateKey(type, expiry);
}

void ValidateKey()
{
    Console.Write("Enter license key to validate: ");
    var key = Console.ReadLine()?.Trim();

    if (string.IsNullOrEmpty(key))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("No key entered!");
        Console.ResetColor();
        return;
    }

    var result = LicenseManager.ValidateLicense(key);

    Console.WriteLine();
    if (result.IsValid)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("═══════════════════════════════════════════════════════════════════");
        Console.WriteLine("  LICENSE VALID");
        Console.WriteLine($"  Type:    {result.Type}");
        Console.WriteLine($"  Expires: {(result.ExpiryDate == DateTime.MaxValue ? "Never (Lifetime)" : result.ExpiryDate.ToString("yyyy-MM-dd"))}");
        Console.WriteLine($"  Status:  {result.Message}");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════");
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("═══════════════════════════════════════════════════════════════════");
        Console.WriteLine("  LICENSE INVALID");
        Console.WriteLine($"  Reason: {result.Message}");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════");
    }
    Console.ResetColor();
}

void GenerateBatchKeys()
{
    Console.Write("Enter license type (Trial/Professional/Enterprise/Lifetime): ");
    var typeStr = Console.ReadLine()?.Trim();

    var type = typeStr?.ToLower() switch
    {
        "trial" or "t" => LicenseType.Trial,
        "professional" or "pro" or "p" => LicenseType.Professional,
        "enterprise" or "ent" or "e" => LicenseType.Enterprise,
        "lifetime" or "lt" or "l" => LicenseType.Lifetime,
        _ => LicenseType.Professional
    };

    Console.Write("Number of keys to generate: ");
    if (!int.TryParse(Console.ReadLine()?.Trim(), out var count) || count < 1)
        count = 5;

    DateTime? expiry = type == LicenseType.Lifetime ? null : DateTime.Now.AddYears(1);
    if (type == LicenseType.Trial)
        expiry = DateTime.Now.AddDays(30);

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"Generating {count} {type} license keys...");
    Console.WriteLine("═══════════════════════════════════════════════════════════════════");
    Console.ResetColor();

    var keys = new List<string>();
    for (int i = 0; i < count; i++)
    {
        var key = LicenseManager.GenerateLicense(type, expiry);
        keys.Add(key);
        Console.WriteLine($"  {i + 1}. {key}");
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("═══════════════════════════════════════════════════════════════════");
    Console.ResetColor();

    // Save to file
    Console.Write("Save to file? (y/n): ");
    if (Console.ReadLine()?.Trim().ToLower() == "y")
    {
        var filename = $"StellaFlash_Keys_{type}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        File.WriteAllLines(filename, keys);
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Keys saved to: {filename}");
        Console.ResetColor();
    }
}
