using KetoanMini.Api.Data;
using Npgsql;

namespace KetoanMini.Api.Security;

/// <summary>
/// Tra "AI đang giữ quyền X?" — dùng khi một sự kiện phải báo cho cả một VAI TRÒ chứ không cho một
/// người cụ thể (vd. tài xế báo đã giao hàng thì thủ kho, kế toán kho và quản trị viên đều cần biết).
///
/// Vì sao không lưu sẵn danh sách người nhận ở đâu đó: quyền được suy từ vai trò hiện hành trong CSDL
/// (xem <see cref="AccessProfileService"/>), nên chỉ có cách hỏi lại lúc cần mới đúng ngay sau khi
/// admin vừa đổi vai trò cho ai đó. Số tài khoản của một doanh nghiệp nhỏ chỉ vài chục dòng nên đọc
/// cả bảng là rẻ; đừng "tối ưu" thành bảng ánh xạ tĩnh rồi để nó lệch với phân quyền thật.
/// </summary>
public static class PermissionDirectory
{
    /// <summary>
    /// Tài khoản đang hoạt động có ÍT NHẤT MỘT trong các quyền yêu cầu. Trả về username gốc (đúng
    /// hoa/thường như trong app_users) vì các bảng thông báo đều khớp theo tên đó.
    /// </summary>
    public static async Task<List<string>> UsersWithAnyPermissionAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, IReadOnlyCollection<string> permissions,
        CancellationToken ct = default)
    {
        var recipients = new List<string>();
        if (permissions.Count == 0) return recipients;

        var cmd = tx is null ? conn.Cmd(RosterSql) : conn.Cmd(RosterSql, tx);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var roles = AccessProfileService.Combine(r.Str("role"), r.Str("extra"));
            var granted = Permissions.For(roles);
            if (permissions.Any(granted.Contains)) recipients.Add(r.Str("username"));
        }
        return recipients;
    }

    /// <summary>Như trên nhưng tự mở kết nối — dùng sau khi giao dịch nghiệp vụ đã commit.</summary>
    public static async Task<List<string>> UsersWithAnyPermissionAsync(
        Database db, IReadOnlyCollection<string> permissions, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        return await UsersWithAnyPermissionAsync(conn, null, permissions, ct);
    }

    private const string RosterSql = """
        SELECT u.username, u.role,
               COALESCE((SELECT string_agg(ur.role, ',' ORDER BY ur.role)
                         FROM user_roles ur
                         WHERE ur.username = u.username
                           AND (ur.expires_at IS NULL OR ur.expires_at > CURRENT_TIMESTAMP)), '') AS extra
        FROM app_users u
        WHERE u.is_active = TRUE
          AND COALESCE(u.is_deleted, FALSE) = FALSE
          AND u.approval_status = 'Approved'
        """;
}
