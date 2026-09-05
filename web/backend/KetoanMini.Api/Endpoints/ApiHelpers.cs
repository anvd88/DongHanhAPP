using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

public static class ApiHelpers
{
    public static string Username(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Name) ?? "";

    /// <summary>
    /// Có QUYỀN cụ thể hay không, theo claim quyền mà middleware dựng lại từ CSDL ở mỗi request
    /// (xem Security/AccessProfileService.cs). Dùng cái này khi handler cần RẼ NHÁNH; còn chốt cửa
    /// endpoint thì dùng <c>.RequirePermission(Permissions.X)</c> để 403 xảy ra trước cả handler.
    /// </summary>
    public static bool Can(this ClaimsPrincipal user, string permission)
        => user.HasClaim(Permissions.ClaimType, permission);

    /// <summary>
    /// Giữ vai trò Admin. CHỈ dùng cho PHẠM VI DỮ LIỆU ("thấy mọi dòng" thay vì "chỉ dòng của mình")
    /// và cho hiển thị. KHÔNG dùng để chốt cửa endpoint — cửa phải chốt bằng quyền
    /// (<c>.RequirePermission</c>) để thêm vai trò mới không phải đi sửa từng handler.
    /// </summary>
    public static bool IsAdmin(this ClaimsPrincipal user)
        => user.IsInRole(AppRoles.Admin);

    /// <summary>Quản trị dữ liệu nhân sự (Admin hoặc Nhân sự) — nay chốt bằng QUYỀN hr.manage.</summary>
    public static bool IsHrManager(this ClaimsPrincipal user)
        => user.Can(Permissions.HrManage);

    /// <summary>Vào được khu kế toán — nay chốt bằng QUYỀN accounting.access (Admin, Kế toán, Kế toán trưởng).</summary>
    public static bool IsAccounting(this ClaimsPrincipal user)
        => user.Can(Permissions.AccountingAccess);

    /// <summary>Danh sách vai trò THỨ HAI (đã chuẩn hóa) của một tài khoản, đọc từ bảng user_roles.</summary>
    public static async Task<List<string>> LoadSecondaryRolesAsync(NpgsqlConnection conn, string username)
    {
        var roles = new List<string>();
        if (string.IsNullOrWhiteSpace(username)) return roles;
        try
        {
            await using var r = await conn.Cmd(
                @"SELECT role FROM user_roles
                  WHERE username = @u AND (expires_at IS NULL OR expires_at > CURRENT_TIMESTAMP)")
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

    /// <summary>Người có thẩm quyền GIAO VIỆC &amp; NGHIỆM THU = có quyền tasks.assign (Admin, Thủ kho,
    /// Trưởng phòng — xem Permissions.RolePermissions). Tính lại từ vai trò trong DB nên cấp/thu quyền
    /// có hiệu lực ngay, không chờ đăng nhập lại.</summary>
    public static async Task<bool> IsTaskAssignerAsync(NpgsqlConnection conn, string username, string primaryRole)
    {
        var roles = await LoadAllRolesAsync(conn, username, primaryRole);
        return Permissions.For(roles).Contains(Permissions.TasksAssign);
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

    /// <summary>
    /// Mandatory audit written through the caller's transaction. Unlike the legacy convenience
    /// overload, failures propagate and roll the business mutation back.
    /// </summary>
    public static async Task RecordAudit(this NpgsqlConnection conn, NpgsqlTransaction tx,
        string username, string action, string entity, string entityName, string details,
        CancellationToken ct = default)
    {
        await conn.Cmd(@"INSERT INTO audit_logs (occurred_at, username, action, entity, entity_name, details)
                         VALUES (CURRENT_TIMESTAMP, @u, @a, @e, @en, @d)", tx)
            .With("@u", username).With("@a", action).With("@e", entity)
            .With("@en", entityName).With("@d", details)
            .ExecuteNonQueryAsync(ct);
    }
}
