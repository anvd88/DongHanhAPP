using Microsoft.Data.SqlClient;

namespace KetoanMini.Api.Data;

/// <summary>
/// Lớp truy cập SQL Server dùng chung — kết nối tới đúng database KetoanMini
/// mà app desktop đang dùng (không đổi schema).
/// </summary>
public sealed class Database(IConfiguration config)
{
    private readonly string _connectionString =
        config.GetConnectionString("KetoanMini")
        ?? throw new InvalidOperationException("Thiếu ConnectionStrings:KetoanMini trong appsettings.json");

    public SqlConnection Open()
    {
        var conn = new SqlConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}

public static class SqlExtensions
{
    public static SqlCommand Cmd(this SqlConnection conn, string sql)
        => new(sql, conn);

    public static SqlCommand With(this SqlCommand cmd, string name, object? value)
    {
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    public static string Str(this SqlDataReader r, int i) => r.IsDBNull(i) ? string.Empty : r.GetString(i);
    public static string Str(this SqlDataReader r, string col) => r.Str(r.GetOrdinal(col));
    public static decimal Dec(this SqlDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? 0m : r.GetDecimal(i); }
    public static int Int(this SqlDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? 0 : r.GetInt32(i); }
    public static long Long(this SqlDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? 0 : r.GetInt64(i); }
    public static bool Bool(this SqlDataReader r, string col) { var i = r.GetOrdinal(col); return !r.IsDBNull(i) && r.GetBoolean(i); }
    public static Guid Guid(this SqlDataReader r, string col) => r.GetGuid(r.GetOrdinal(col));
    public static DateTime Dt(this SqlDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? default : r.GetDateTime(i); }
    public static DateTime? DtNull(this SqlDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetDateTime(i); }
    public static DateOnly DateOnly(this SqlDataReader r, string col) { var i = r.GetOrdinal(col); return System.DateOnly.FromDateTime(r.GetDateTime(i)); }
}
