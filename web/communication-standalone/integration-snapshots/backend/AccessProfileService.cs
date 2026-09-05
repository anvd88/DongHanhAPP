using System.Security.Claims;
using KetoanMini.Api.Data;
using Npgsql;

namespace KetoanMini.Api.Security;

/// <summary>
/// NGUỒN PHÂN QUYỀN THỐNG NHẤT. Mọi câu hỏi "tài khoản này có được làm X không?" phải đi qua đây
/// (hoặc qua policy quyền do đây sinh ra), KHÔNG endpoint nào tự đọc tên vai trò rồi tự quyết định.
///
/// Ba tính chất quan trọng:
///  • Đọc từ CSDL mỗi lần hỏi ⇒ cấp/thu quyền có hiệu lực NGAY từ request kế tiếp, không cần đăng nhập lại.
///  • KHÔNG tin claim vai trò trong JWT (token sống tới 365 ngày, hạ quyền sẽ không kịp).
///  • Không đọc được dữ liệu phân quyền ⇒ trả null ⇒ endpoint đặc quyền TỪ CHỐI (đóng mặc định),
///    thay vì tiếp tục tin vào quyền cũ.
/// </summary>
public sealed class AccessProfileService(Database db, ILogger<AccessProfileService> logger)
{
    /// <summary>Vai trò hiện hành (chính + phụ còn hạn) đọc thẳng từ CSDL. null = không đọc được.</summary>
    public async Task<IReadOnlyList<string>?> LoadRolesAsync(string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        try
        {
            await using var conn = await db.OpenAsync(ct);
            return await LoadRolesAsync(conn, username, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Khong doc duoc vai tro hien hanh cua {User}", username);
            return null;
        }
    }

    /// <summary>Như trên nhưng dùng lại kết nối sẵn có (tránh mở thêm kết nối trong một request).</summary>
    public static async Task<IReadOnlyList<string>?> LoadRolesAsync(
        NpgsqlConnection conn, string username, CancellationToken ct = default,
        NpgsqlTransaction? transaction = null)
    {
        const string sql =
            @"SELECT u.role,
                     COALESCE((SELECT string_agg(ur.role, ',' ORDER BY ur.role)
                               FROM user_roles ur
                               WHERE ur.username = u.username
                                 AND (ur.expires_at IS NULL OR ur.expires_at > CURRENT_TIMESTAMP)), '') AS extra
              FROM app_users u
              WHERE u.username = @u AND u.is_deleted = FALSE
              LIMIT 1";
        await using var command = transaction is null ? conn.Cmd(sql) : conn.Cmd(sql, transaction);
        await using var r = await command.With("@u", username).ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return Combine(r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1));
    }

    /// <summary>Gộp vai trò chính + chuỗi vai trò phụ ngăn cách bằng dấu phẩy, đã chuẩn hóa &amp; loại trùng.
    /// Vai trò chính thiếu/không hợp lệ ⇒ coi là Nhân viên (giống cách TokenService dựng claim).</summary>
    public static List<string> Combine(string? primaryRole, string extraCsv)
    {
        var roles = new List<string> { AppRoles.Normalize(primaryRole) ?? AppRoles.Employee };
        foreach (var extra in (extraCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (AppRoles.Normalize(extra) is { } norm && !roles.Contains(norm)) roles.Add(norm);
        return roles;
    }

    /// <summary>
    /// Hồ sơ truy cập đầy đủ (vai trò + quyền + phạm vi dữ liệu + giao diện mặc định).
    /// null = tài khoản không còn tồn tại hoặc không đọc được phân quyền.
    /// </summary>
    public async Task<AccessProfileDto?> LoadAsync(string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        await using var conn = await db.OpenAsync(ct);

        string primaryRole, fullName, extraCsv;
        int version;
        await using (var r = await conn.Cmd(
            @"SELECT u.role, u.full_name, COALESCE(u.authorization_version, 1) AS av,
                     COALESCE((SELECT string_agg(ur.role, ',' ORDER BY ur.role)
                               FROM user_roles ur
                               WHERE ur.username = u.username
                                 AND (ur.expires_at IS NULL OR ur.expires_at > CURRENT_TIMESTAMP)), '') AS extra
              FROM app_users u
              WHERE u.username = @u AND u.is_deleted = FALSE
              LIMIT 1")
            .With("@u", username).ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return null;
            primaryRole = r.IsDBNull(0) ? "" : r.GetString(0);
            fullName = r.IsDBNull(1) ? "" : r.GetString(1);
            version = r.IsDBNull(2) ? 1 : Convert.ToInt32(r.GetValue(2));
            extraCsv = r.IsDBNull(3) ? "" : r.GetString(3);
        }

        var roles = Combine(primaryRole, extraCsv);
        var permissions = Permissions.For(roles);
        var scope = await ResolveScopeAsync(conn, username, permissions, ct);
        var ui = UiProfileFor(permissions);

        return new AccessProfileDto(
            username, fullName,
            AppRoles.Normalize(primaryRole) ?? AppRoles.Employee,
            roles,
            [.. roles.Select(AppRoles.Label)],
            [.. permissions.OrderBy(p => p, StringComparer.Ordinal)],
            scope.Name, scope.DepartmentId, scope.LocationId,
            ui, LandingPathFor(ui, permissions),
            version);
    }

    /// <summary>
    /// Phạm vi dữ liệu. Quyền hr.manage/users.manage ⇒ toàn bộ; còn lại lấy theo chức vụ trong hồ sơ
    /// nhân sự (access_role): quản lý phòng ban ⇒ phòng mình, quản lý địa điểm ⇒ chi nhánh mình,
    /// mặc định ⇒ chỉ chính mình. Bảng nhân sự chưa có ⇒ chỉ chính mình (đóng mặc định).
    /// </summary>
    private static async Task<AccessScope> ResolveScopeAsync(
        NpgsqlConnection conn, string username, IReadOnlySet<string> permissions, CancellationToken ct)
    {
        if (permissions.Contains(Permissions.UsersManage)
            || permissions.Contains(Permissions.HrManage))
            return new AccessScope(ScopeKind.All, null, null);
        var companyWide = permissions.Contains(Permissions.CompanyScopeAll);
        try
        {
            await using var r = await conn.Cmd(
                """
                SELECT e.access_role, e.department_id, e.location_id
                FROM app_users u
                JOIN hr_employees e
                  ON e.user_id=u.id OR (e.user_id IS NULL AND lower(e.username)=lower(u.username))
                WHERE lower(u.username)=lower(@u) AND u.is_deleted=FALSE
                ORDER BY (e.user_id=u.id) DESC
                LIMIT 1
                """)
                .With("@u", username).ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
                return companyWide ? new AccessScope(ScopeKind.All, null, null) : AccessScope.SelfOnly;
            var accessRole = r.IsDBNull(0) ? "" : r.GetString(0);
            Guid? dept = r.IsDBNull(1) ? null : r.GetGuid(1);
            Guid? loc = r.IsDBNull(2) ? null : r.GetGuid(2);
            return accessRole switch
            {
                "dept_manager" when dept is not null => new AccessScope(ScopeKind.Department, dept, loc),
                "location_manager" when loc is not null => new AccessScope(ScopeKind.Branch, dept, loc),
                _ when companyWide => new AccessScope(ScopeKind.All, null, null),
                _ => new AccessScope(ScopeKind.Self, dept, loc),
            };
        }
        catch (NpgsqlException)
        {
            return AccessScope.SelfOnly;
        }
    }

    /// <summary>Giao diện mặc định: quản trị viên vào khu quản trị, còn lại vào không gian làm việc.</summary>
    public static string UiProfileFor(IReadOnlySet<string> permissions)
    {
        if (permissions.Contains(Permissions.UsersManage)) return "admin";
        if (permissions.Contains(Permissions.CompanyScopeAll)) return "executive";
        if (permissions.Contains(Permissions.AttendanceKiosk) && permissions.Count == 1) return "kiosk";
        if (permissions.Contains(Permissions.HrManage)) return "hr";
        if (permissions.Contains(Permissions.AccountingAccess)) return "accounting";
        return "workspace";
    }

    /// <summary>
    /// Trang đích sau đăng nhập. Chỉ là gợi ý HIỂN THỊ — vào được hay không vẫn do quyền quyết định,
    /// nên trang đích luôn được chọn trong số trang mà tài khoản thực sự có quyền xem.
    /// </summary>
    public static string LandingPathFor(string uiProfile, IReadOnlySet<string> permissions) => uiProfile switch
    {
        "admin" => "/dashboard",
        "executive" => "/dashboard",
        "kiosk" => "/kiosk",
        "hr" => "/quanly-nhansu",
        "accounting" => "/ketoan",
        _ => permissions.Contains(Permissions.HrSelfAccess) ? "/nhan-su" : "/chats",
    };

    /// <summary>Quyền hiện hành gắn vào request (do middleware dựng lại từ CSDL). Rỗng = chưa xác định được.</summary>
    public static IReadOnlySet<string> CurrentPermissions(ClaimsPrincipal user)
        => user.FindAll(Permissions.ClaimType).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>Kiểm tra quyền trên request hiện tại. Dùng khi endpoint cần rẽ nhánh chứ không chỉ chặn cửa.</summary>
    public static bool Can(ClaimsPrincipal user, string permission)
        => user.HasClaim(Permissions.ClaimType, permission);
}
