using Npgsql;
using KetoanMini.Api.Services;

namespace KetoanMini.Api.Security;

public static class ProductionSecurityValidator
{
    private static readonly string[] PlaceholderFragments =
    [
        "admin123", "changeit", "dat_mat_khau", "thay_secret", "doi-chuoi-bi-mat"
    ];

    public static void Validate(WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsProduction()) return;

        var config = builder.Configuration;
        var errors = new List<string>();
        RequireSecret(config["Jwt:Key"], "Jwt:Key", 32, errors);
        RequireBase64Key(config["Security:FieldEncryptionKey"], "Security:FieldEncryptionKey", 32, errors);

        var connectionString = config.GetConnectionString("KetoanMini");
        if (string.IsNullOrWhiteSpace(connectionString))
            errors.Add("ConnectionStrings:KetoanMini is empty");
        else
        {
            try
            {
                var cs = new NpgsqlConnectionStringBuilder(connectionString);
                RequireSecret(cs.Password, "database password", 12, errors);
            }
            catch (ArgumentException)
            {
                errors.Add("ConnectionStrings:KetoanMini is invalid");
            }
        }

        var adminUser = config["Bootstrap:AdminUsername"]?.Trim();
        var adminPassword = config["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(adminUser) || adminUser.Equals("admin", StringComparison.OrdinalIgnoreCase))
            errors.Add("Bootstrap:AdminUsername is empty or still uses 'admin'");
        RequireSecret(adminPassword, "Bootstrap:AdminPassword", 14, errors);

        var turnKeyId = config["Turn:Cloudflare:KeyId"];
        var turnToken = config["Turn:Cloudflare:ApiToken"];
        if (!string.IsNullOrWhiteSpace(turnKeyId) || !string.IsNullOrWhiteSpace(turnToken))
        {
            if (string.IsNullOrWhiteSpace(turnKeyId) || string.IsNullOrWhiteSpace(turnToken))
                errors.Add("Cloudflare TURN KeyId/ApiToken must both be configured");
        }

        if (errors.Count > 0)
            throw new InvalidOperationException("Production security validation failed: " + string.Join("; ", errors));
    }

    /// <summary>
    /// Production must never accept face attendance with a degraded or missing anti-spoof stack.
    /// Resolving the singleton at startup intentionally loads the models now, so a bad deployment
    /// fails before it can receive traffic instead of discovering the problem on the first scan.
    /// </summary>
    public static void ValidateFaceEngine(IHostEnvironment environment, IFaceEngine engine)
    {
        if (!environment.IsProduction()) return;
        if (engine.AntiSpoof.Level == AntiSpoofLevel.Full) return;

        throw new InvalidOperationException(
            $"Production security validation failed: Face anti-spoof must be Full, " +
            $"but is {engine.AntiSpoof.Level} ({engine.AntiSpoof.Detail}).");
    }

    private static void RequireSecret(string? value, string name, int minLength, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{name} is empty");
            return;
        }

        if (value.Length < minLength || PlaceholderFragments.Any(p => value.Contains(p, StringComparison.OrdinalIgnoreCase)))
            errors.Add($"{name} is default, placeholder, or too short");
    }

    private static void RequireBase64Key(string? value, string name, int requiredBytes, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{name} is empty");
            return;
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(value.Trim());
        }
        catch (FormatException)
        {
            errors.Add($"{name} must be valid Base64 encoding exactly {requiredBytes} bytes");
            return;
        }

        try
        {
            if (decoded.Length != requiredBytes)
                errors.Add($"{name} must decode to exactly {requiredBytes} bytes");
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(decoded);
        }
    }
}
