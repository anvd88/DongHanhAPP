using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace KetoanMini;

internal static class SqlLanConfigurator
{
    public static string Configure(string instanceName, string database, string login, string password, int port, string configPath)
    {
        instanceName = string.IsNullOrWhiteSpace(instanceName) ? "SQLEXPRESS01" : instanceName.Trim();
        database = string.IsNullOrWhiteSpace(database) ? "KetoanMini" : database.Trim();
        login = string.IsNullOrWhiteSpace(login) ? "ketoan_app" : login.Trim();

        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            throw new InvalidOperationException("Mật khẩu SQL phải có ít nhất 8 ký tự.");
        }

        var server = string.Equals(instanceName, "MSSQLSERVER", StringComparison.OrdinalIgnoreCase)
            ? "localhost"
            : $"localhost\\{instanceName}";

        using (var connection = OpenTrustedConnection(server, "master"))
        {
            Execute(connection, $"""
                IF DB_ID(N'{SqlLiteralBody(database)}') IS NULL
                BEGIN
                    CREATE DATABASE {SqlIdentifier(database)};
                END;

                IF SUSER_ID(N'{SqlLiteralBody(login)}') IS NULL
                BEGIN
                    CREATE LOGIN {SqlIdentifier(login)}
                    WITH PASSWORD = N'{SqlLiteralBody(password)}',
                         CHECK_POLICY = OFF,
                         CHECK_EXPIRATION = OFF;
                END
                ELSE
                BEGIN
                    ALTER LOGIN {SqlIdentifier(login)}
                    WITH PASSWORD = N'{SqlLiteralBody(password)}',
                         CHECK_POLICY = OFF,
                         CHECK_EXPIRATION = OFF,
                         ENABLE;
                END;
                """);
        }

        using (var connection = OpenTrustedConnection(server, database))
        {
            Execute(connection, $"""
                IF USER_ID(N'{SqlLiteralBody(login)}') IS NULL
                BEGIN
                    CREATE USER {SqlIdentifier(login)} FOR LOGIN {SqlIdentifier(login)};
                END;

                IF IS_ROLEMEMBER(N'db_owner', N'{SqlLiteralBody(login)}') <> 1
                BEGIN
                    ALTER ROLE db_owner ADD MEMBER {SqlIdentifier(login)};
                END;
                """);
        }

        var ip = PreferredIpv4Address();
        var connectionString = $"Server={ip},{port};Database={database};User Id={login};Password={password};Encrypt=False;TrustServerCertificate=True;";
        Directory.CreateDirectory(Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory);
        File.WriteAllText(
            configPath,
            JsonSerializer.Serialize(new DatabaseConnectionSettings { ConnectionString = connectionString }, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);

        return string.Join(Environment.NewLine, [
            "DONE",
            $"Server={server}",
            $"LanIp={ip}",
            $"Port={port}",
            $"Database={database}",
            $"Login={login}",
            $"Config={configPath}",
            connectionString
        ]);
    }

    private static SqlConnection OpenTrustedConnection(string server, string database)
    {
        var connection = new SqlConnection($"Server={server};Database={database};Trusted_Connection=True;Encrypt=False;TrustServerCertificate=True;Connection Timeout=15;");
        connection.Open();
        return connection;
    }

    private static void Execute(SqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandTimeout = 120;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string SqlIdentifier(string value)
    {
        return "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    private static string SqlLiteralBody(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string PreferredIpv4Address()
    {
        try
        {
            var address = NetworkInterface.GetAllNetworkInterfaces()
                .Where(item => item.OperationalStatus == OperationalStatus.Up)
                .SelectMany(item => item.GetIPProperties().UnicastAddresses)
                .Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(item => item.Address)
                .FirstOrDefault(address => !IPAddress.IsLoopback(address) && !address.ToString().StartsWith("169.254.", StringComparison.Ordinal));

            return address?.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }
}
