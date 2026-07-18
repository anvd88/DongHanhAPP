using System.Globalization;
using System.Security.Claims;
using System.Text;
using KetoanMini.Api.Data;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Danh bạ &amp; sơ đồ tổ chức — Đợt 2, nhiệm vụ 6. Cung cấp:
///  • Danh bạ toàn công ty: tìm theo tên/chức vụ (TIẾNG VIỆT KHÔNG DẤU), lọc theo phòng ban.
///  • Trạng thái online (từ user_sessions, giống chat).
///  • Sơ đồ tổ chức theo quan hệ quản lý–nhân viên (manager_id) dạng cây.
///  • PHÂN QUYỀN xem số điện thoại &amp; email: Admin/HR xem tất cả; quản lý xem của nhân viên mình;
///    người khác chỉ thấy tên/chức vụ/phòng ban (ẩn liên hệ), riêng bản thân luôn xem được.
/// </summary>
public static class DirectoryEndpoints
{
    public static void MapDirectory(this WebApplication app)
    {
        var g = app.MapGroup("/api/directory").RequireAuthorization();

        // Danh bạ có tìm kiếm không dấu + lọc phòng ban.
        g.MapGet("/", async (ClaimsPrincipal u, Database db, string? search, Guid? departmentId) =>
        {
            var canSeeAll = u.IsHrManager();
            await using var conn = await db.OpenAsync();
            var (myId, _) = await MyEmployee(conn, u.Username());

            var deptFilter = departmentId is not null ? "AND e.department_id = @dept" : "";
            var cmd = conn.Cmd($"""
                SELECT e.id, e.full_name, e.position, e.phone, e.email, e.username, e.manager_id,
                       d.id AS dept_id, d.name AS dept_name, m.full_name AS manager_name,
                       COALESCE(pres.is_online, FALSE) AS is_online
                FROM hr_employees e
                LEFT JOIN hr_departments d ON d.id = e.department_id
                LEFT JOIN hr_employees m ON m.id = e.manager_id
                LEFT JOIN LATERAL (
                    SELECT BOOL_OR(us.is_active = TRUE AND us.last_seen >= CURRENT_TIMESTAMP - INTERVAL '90 seconds') AS is_online
                    FROM user_sessions us WHERE us.username = e.username
                ) pres ON TRUE
                WHERE e.status = 'Active' {deptFilter}
                ORDER BY d.name NULLS LAST, e.full_name
                """);
            if (departmentId is not null) cmd.With("@dept", departmentId.Value);

            var norm = NoAccent(search);
            var list = new List<object>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var id = r.Guid("id");
                var name = r.Str("full_name");
                var position = r.Str("position");
                // Tìm kiếm KHÔNG DẤU trên tên + chức vụ (lọc phía server sau khi bỏ dấu).
                if (norm.Length > 0 && !NoAccent(name).Contains(norm) && !NoAccent(position).Contains(norm))
                    continue;

                var managerId = r.IsDBNull(r.GetOrdinal("manager_id")) ? (Guid?)null : r.Guid("manager_id");
                var mayContact = canSeeAll || id == myId || (myId is not null && managerId == myId);
                list.Add(new
                {
                    id,
                    fullName = name,
                    position,
                    departmentId = r.IsDBNull(r.GetOrdinal("dept_id")) ? (Guid?)null : r.Guid("dept_id"),
                    departmentName = NullIfEmpty(r.Str("dept_name")),
                    managerId,
                    managerName = NullIfEmpty(r.Str("manager_name")),
                    phone = mayContact ? NullIfEmpty(r.Str("phone")) : null,
                    email = mayContact ? NullIfEmpty(r.Str("email")) : null,
                    canSeeContact = mayContact,
                    online = r.Bool("is_online"),
                });
            }
            return Results.Ok(list);
        });

        // Sơ đồ tổ chức dạng cây (theo manager_id). Gốc = người không có quản lý (hoặc quản lý đã nghỉ).
        g.MapGet("/org-chart", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var nodes = new Dictionary<Guid, OrgNode>();
            var managerOf = new Dictionary<Guid, Guid?>();
            await using (var r = await conn.Cmd("""
                SELECT e.id, e.full_name, e.position, e.manager_id, d.name AS dept_name
                FROM hr_employees e
                LEFT JOIN hr_departments d ON d.id = e.department_id
                WHERE e.status = 'Active'
                ORDER BY e.full_name
                """).ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    var id = r.Guid("id");
                    nodes[id] = new OrgNode(id, r.Str("full_name"), r.Str("position"), NullIfEmpty(r.Str("dept_name")), new List<OrgNode>());
                    managerOf[id] = r.IsDBNull(r.GetOrdinal("manager_id")) ? null : r.Guid("manager_id");
                }
            }

            var roots = new List<OrgNode>();
            foreach (var (id, node) in nodes)
            {
                var mgr = managerOf[id];
                if (mgr is Guid m && nodes.TryGetValue(m, out var parent)) parent.Reports.Add(node);
                else roots.Add(node); // không có quản lý hoặc quản lý không còn Active → là gốc
            }
            return Results.Ok(roots);
        });
    }

    private static async Task<(Guid? Id, Guid? ManagerId)> MyEmployee(NpgsqlConnection conn, string username)
    {
        await using var r = await conn.Cmd("SELECT id, manager_id FROM hr_employees WHERE username=@u LIMIT 1")
            .With("@u", username).ExecuteReaderAsync();
        if (!await r.ReadAsync()) return (null, null);
        return (r.Guid("id"), r.IsDBNull(r.GetOrdinal("manager_id")) ? null : r.Guid("manager_id"));
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>Bỏ dấu tiếng Việt + về chữ thường để tìm "nguyen" khớp "Nguyễn", "ke toan" khớp "Kế toán".</summary>
    private static string NoAccent(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var decomposed = s.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant().Replace('đ', 'd').Replace('Đ', 'd');
    }

    public record OrgNode(Guid Id, string FullName, string Position, string? DepartmentName, List<OrgNode> Reports);
}
