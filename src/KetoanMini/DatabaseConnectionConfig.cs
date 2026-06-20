using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace KetoanMini;

internal sealed class DatabaseConnectionSettings
{
    public string ConnectionString { get; set; } = "";
    public string RealtimeHubUrl { get; set; } = "";
}

internal static class DatabaseConnectionConfig
{
    private const string EnvironmentVariableName = "KETOANMINI_CONNECTION_STRING";
    private const string RealtimeHubEnvironmentVariableName = "KETOANMINI_REALTIME_HUB_URL";
    private const string DefaultConnectionString = "Server=localhost\\SQLEXPRESS01;Database=KetoanMini;Trusted_Connection=True;Encrypt=False;TrustServerCertificate=True;";
    private const string DefaultRealtimeHubUrl = "https://localhost:5443/hubs/changes";

    public static string PrimaryConfigPath => Path.Combine(AppContext.BaseDirectory, "config", "database.json");
    public static string UserConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KetoanMini",
        "config",
        "database.json");

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

    public static string LoadRealtimeHubUrl(string connectionString)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(RealtimeHubEnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return NormalizeRealtimeHubUrl(fromEnvironment.Trim());
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

            if (!string.IsNullOrWhiteSpace(settings?.RealtimeHubUrl))
            {
                return NormalizeRealtimeHubUrl(settings.RealtimeHubUrl.Trim());
            }
        }

        return BuildDefaultRealtimeHubUrl(connectionString);
    }

    private static IEnumerable<string> CandidateConfigPaths()
    {
        yield return UserConfigPath;
        yield return PrimaryConfigPath;
        yield return Path.Combine(AppContext.BaseDirectory, "database.json");
        yield return Path.Combine(Environment.CurrentDirectory, "config", "database.json");
        yield return Path.Combine(Environment.CurrentDirectory, "database.json");
    }

    public static void SaveUserConnectionString(string connectionString)
    {
        var directory = Path.GetDirectoryName(UserConfigPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(
            new DatabaseConnectionSettings { ConnectionString = connectionString.Trim() },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(UserConfigPath, json, Encoding.UTF8);
    }

    private static string BuildDefaultRealtimeHubUrl(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            var host = ExtractHost(builder.DataSource);
            if (string.IsNullOrWhiteSpace(host))
            {
                return DefaultRealtimeHubUrl;
            }

            return $"https://{host}:5443/hubs/changes";
        }
        catch
        {
            return DefaultRealtimeHubUrl;
        }
    }

    private static string ExtractHost(string dataSource)
    {
        var host = dataSource.Trim();
        if (host.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            host = host[4..];
        }

        if (host.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase) ||
            host is "." or "(local)")
        {
            return "localhost";
        }

        var slashIndex = host.IndexOf('\\');
        if (slashIndex >= 0)
        {
            host = host[..slashIndex];
        }

        var commaIndex = host.IndexOf(',');
        if (commaIndex >= 0)
        {
            host = host[..commaIndex];
        }

        return string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim();
    }

    private static string NormalizeRealtimeHubUrl(string value)
    {
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value;
        }

        value = value.TrimEnd('/');
        if (value.EndsWith("/hubs/changes", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        if (value.EndsWith("/hubs", StringComparison.OrdinalIgnoreCase))
        {
            return value + "/changes";
        }

        return value + "/hubs/changes";
    }
}
