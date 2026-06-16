using System.Text.Json;

namespace KetoanMini;

internal sealed class DatabaseConnectionSettings
{
    public string ConnectionString { get; set; } = "";
}

internal static class DatabaseConnectionConfig
{
    private const string EnvironmentVariableName = "KETOANMINI_CONNECTION_STRING";
    private const string DefaultConnectionString = "Server=localhost\\SQLEXPRESS01;Database=KetoanMini;Trusted_Connection=True;Encrypt=False;TrustServerCertificate=True;";

    public static string PrimaryConfigPath => Path.Combine(AppContext.BaseDirectory, "config", "database.json");

    public static string LoadConnectionString()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        foreach (var path in CandidateConfigPaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<DatabaseConnectionSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (!string.IsNullOrWhiteSpace(settings?.ConnectionString))
            {
                return settings.ConnectionString.Trim();
            }
        }

        return DefaultConnectionString;
    }

    private static IEnumerable<string> CandidateConfigPaths()
    {
        yield return PrimaryConfigPath;
        yield return Path.Combine(AppContext.BaseDirectory, "database.json");
        yield return Path.Combine(Environment.CurrentDirectory, "config", "database.json");
        yield return Path.Combine(Environment.CurrentDirectory, "database.json");
    }
}
