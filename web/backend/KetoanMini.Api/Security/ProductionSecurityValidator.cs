using Npgsql;

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
        RequireSecret(config["Security:FieldEncryptionKey"], "Security:FieldEncryptionKey", 32, errors);

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
}
