using Npgsql;

namespace KetoanMini.Api.Data;

/// <summary>Shared PostgreSQL access layer backed by an Npgsql pooled data source.</summary>
public sealed class Database : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly NpgsqlDataSource _dataSource;

    public Database(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("KetoanMini")
            ?? throw new InvalidOperationException("Thieu ConnectionStrings:KetoanMini trong appsettings.json");
        _dataSource = NpgsqlDataSource.Create(_connectionString);
    }

    public NpgsqlConnection Open() => _dataSource.OpenConnection();

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default)
        => await _dataSource.OpenConnectionAsync(ct);

    public async Task EnsureDatabaseExistsAsync(CancellationToken ct = default)
    {
        var builder = new NpgsqlConnectionStringBuilder(_connectionString);
        var databaseName = builder.Database;
        if (string.IsNullOrWhiteSpace(databaseName))
            return;

        builder.Database = "postgres";
        await using var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync(ct);

        var exists = await conn.Cmd("SELECT 1 FROM pg_database WHERE datname = @name")
            .With("@name", databaseName)
            .ExecuteScalarAsync(ct);
        if (exists is not null and not DBNull)
            return;

        await conn.Cmd($"CREATE DATABASE {QuoteIdentifier(databaseName)}")
            .ExecuteNonQueryAsync(ct);
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    private static string QuoteIdentifier(string value)
        => "\"" + value.Replace("\"", "\"\"") + "\"";
}

public static class SqlExtensions
{
    public static NpgsqlCommand Cmd(this NpgsqlConnection conn, string sql)
        => new(sql, conn);

    public static NpgsqlCommand Cmd(this NpgsqlConnection conn, string sql, NpgsqlTransaction transaction)
        => new(sql, conn, transaction);

    public static NpgsqlCommand With(this NpgsqlCommand cmd, string name, object? value)
    {
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    public static string Str(this NpgsqlDataReader r, int i)
        => r.IsDBNull(i) ? string.Empty : Convert.ToString(r.GetValue(i)) ?? string.Empty;

    public static string Str(this NpgsqlDataReader r, string col) => r.Str(r.GetOrdinal(col));

    public static decimal Dec(this NpgsqlDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? 0m : Convert.ToDecimal(r.GetValue(i));
    }

    public static int Int(this NpgsqlDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));
    }

    public static long Long(this NpgsqlDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? 0 : Convert.ToInt64(r.GetValue(i));
    }

    public static long? LongNull(this NpgsqlDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : Convert.ToInt64(r.GetValue(i));
    }

    public static bool Bool(this NpgsqlDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return !r.IsDBNull(i) && Convert.ToBoolean(r.GetValue(i));
    }

    public static Guid Guid(this NpgsqlDataReader r, string col) => r.GetGuid(r.GetOrdinal(col));

    public static DateTime Dt(this NpgsqlDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? default : r.GetDateTime(i);
    }

    public static DateTime? DtNull(this NpgsqlDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetDateTime(i);
    }

    public static DateOnly DateOnly(this NpgsqlDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        if (r.IsDBNull(i)) return default;

        var value = r.GetValue(i);
        return value switch
        {
            System.DateOnly d => d,
            DateTime dt => System.DateOnly.FromDateTime(dt),
            _ => System.DateOnly.FromDateTime(Convert.ToDateTime(value))
        };
    }
}
