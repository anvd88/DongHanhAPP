using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

public static class ApiHelpers
{
    public static string Username(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Name) ?? "";

    public static bool IsAdmin(this ClaimsPrincipal user)
        => user.IsInRole("Admin");

    public static bool IsHrManager(this ClaimsPrincipal user)
        => user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.Hr);

    public static bool IsAccounting(this ClaimsPrincipal user)
        => user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.Accounting);

    /// <summary>Danh sách vai trò THỨ HAI (đã chuẩn hóa) của một tài khoản, đọc từ bảng user_roles.</summary>
    public static async Task<List<string>> LoadSecondaryRolesAsync(NpgsqlConnection conn, string username)
    {
        var roles = new List<string>();
        if (string.IsNullOrWhiteSpace(username)) return roles;
        try
        {
            await using var r = await conn.Cmd("SELECT role FROM user_roles WHERE username = @u")
                .With("@u", username).ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var norm = AppRoles.Normalize(r.Str("role"));
                if (norm is not null && !roles.Contains(norm)) roles.Add(norm);
            }
        }
        catch { /* bảng chưa tồn tại lúc khởi tạo → coi như không có vai trò phụ */ }
        return roles;
    }

    /// <summary>Tập hợp MỌI vai trò của tài khoản (vai trò chính + vai trò phụ), đã chuẩn hóa &amp; loại trùng.</summary>
    public static async Task<List<string>> LoadAllRolesAsync(NpgsqlConnection conn, string username, string primaryRole)
    {
        var all = new List<string>();
        var primary = AppRoles.Normalize(primaryRole);
        if (primary is not null) all.Add(primary);
        foreach (var r in await LoadSecondaryRolesAsync(conn, username))
            if (!all.Contains(r)) all.Add(r);
        return all;
    }

    /// <summary>Người có thẩm quyền GIAO VIỆC &amp; NGHIỆM THU: Admin, hoặc bất kỳ tài khoản nào giữ vai trò Thủ kho
    /// (vai trò chính hoặc vai trò phụ). Chốt bằng dữ liệu DB nên cấp/thu quyền có hiệu lực ngay, không chờ đăng nhập lại.</summary>
    public static async Task<bool> IsTaskAssignerAsync(NpgsqlConnection conn, string username, string primaryRole)
    {
        if (string.Equals(AppRoles.Normalize(primaryRole), AppRoles.Admin, StringComparison.Ordinal)) return true;
        var roles = await LoadAllRolesAsync(conn, username, primaryRole);
        return roles.Contains(AppRoles.Admin) || roles.Contains(AppRoles.Warehouse);
    }

    /// <summary>Ghi nhật ký hoạt động — giống RecordAudit của app desktop.</summary>
    public static async Task RecordAudit(this Database db, string username, string action, string entity, string entityName, string details)
    {
        try
        {
            await using var conn = await db.OpenAsync();
            await conn.Cmd(@"INSERT INTO audit_logs (occurred_at, username, action, entity, entity_name, details)
                             VALUES (CURRENT_TIMESTAMP, @u, @a, @e, @en, @d)")
                .With("@u", username).With("@a", action).With("@e", entity)
                .With("@en", entityName).With("@d", details)
                .ExecuteNonQueryAsync();
        }
        catch { /* không để lỗi audit chặn nghiệp vụ */ }
    }
}
