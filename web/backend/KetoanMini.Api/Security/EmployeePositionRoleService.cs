using KetoanMini.Api.Data;
using Npgsql;

namespace KetoanMini.Api.Security;

/// <summary>
/// Đồng bộ vai trò tài khoản từ các chức vụ của hồ sơ nhân sự. Với hồ sơ đã có
/// <c>hr_employee_positions</c>, chức vụ là nguồn dữ liệu duy nhất; không giữ vai trò cấp tay song song
/// vì hai nguồn rất dễ lệch nhau.
/// </summary>
public static class EmployeePositionRoleService
{
    public sealed record SyncResult(
        bool AccountFound,
        bool Changed,
        string Username,
        string RolesBefore,
        string RolesAfter);

    public sealed class LastAdministratorException : InvalidOperationException
    {
        public LastAdministratorException()
            : base("Không thể bỏ chức vụ Quản trị hệ thống của quản trị viên hoạt động cuối cùng.") { }
    }

    public static async Task<bool> IsManagedAccountAsync(
        NpgsqlConnection conn, Guid userId, NpgsqlTransaction? tx = null, CancellationToken ct = default)
    {
        await using var cmd = Command(conn, tx, """
            SELECT EXISTS(
                SELECT 1
                FROM hr_employees e
                JOIN hr_employee_positions ep ON ep.employee_id=e.id
                JOIN app_users u ON u.id=@id
                WHERE e.user_id=u.id
                   OR (e.user_id IS NULL AND lower(e.username)=lower(u.username))
            )
            """).With("@id", userId);
        return await cmd.ExecuteScalarAsync(ct) is bool managed && managed;
    }

    public static async Task<IReadOnlyList<string>> LoadDerivedRolesAsync(
        NpgsqlConnection conn, Guid employeeId, NpgsqlTransaction? tx = null, CancellationToken ct = default)
    {
        var roles = new HashSet<string>(StringComparer.Ordinal);
        await using (var r = await Command(conn, tx, """
            SELECT p.default_role
            FROM hr_employee_positions ep
            JOIN hr_job_positions p ON p.id=ep.position_id
            WHERE ep.employee_id=@employee
            ORDER BY ep.is_primary DESC, p.sort_order, p.code
            """).With("@employee", employeeId).ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
                if (AppRoles.Normalize(r.Str("default_role")) is { } role)
                    roles.Add(role);
        }

        // Tương thích hồ sơ cũ được ghi trực tiếp vào position_id sau migration nhưng chưa có hàng nối.
        if (roles.Count == 0)
        {
            var legacyRole = await Command(conn, tx, """
                SELECT p.default_role
                FROM hr_employees e
                JOIN hr_job_positions p ON p.id=e.position_id
                WHERE e.id=@employee
                LIMIT 1
                """).With("@employee", employeeId).ExecuteScalarAsync(ct) as string;
            if (AppRoles.Normalize(legacyRole) is { } normalized)
                roles.Add(normalized);
        }

        if (roles.Count == 0) roles.Add(AppRoles.Employee);
        return roles
            .OrderByDescending(AppRoles.PrimaryPriority)
            .ThenBy(role => role, StringComparer.Ordinal)
            .ToArray();
    }

    public static async Task<SyncResult> SyncAsync(
        NpgsqlConnection conn,
        Guid employeeId,
        string actor,
        string clientIp,
        NpgsqlTransaction? tx = null,
        CancellationToken ct = default,
        bool forceAuthorizationChange = false,
        string? changeReason = null)
    {
        string username;
        string currentPrimary;
        Guid userId;
        await using (var r = await Command(conn, tx, """
            SELECT u.id, u.username, u.role
            FROM hr_employees e
            JOIN app_users u ON u.is_deleted=FALSE
             AND (u.id=e.user_id OR (e.user_id IS NULL AND lower(u.username)=lower(e.username)))
            WHERE e.id=@employee
            ORDER BY (u.id=e.user_id) DESC
            LIMIT 1
            """).With("@employee", employeeId).ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct))
                return new SyncResult(false, false, "", "", "");
            userId = r.Guid("id");
            username = r.Str("username");
            currentPrimary = AppRoles.Normalize(r.Str("role")) ?? AppRoles.Employee;
        }

        var currentExtras = new List<(string Role, DateTime? ExpiresAt)>();
        await using (var r = await Command(conn, tx, """
            SELECT role, expires_at FROM user_roles WHERE username=@username ORDER BY role
            """).With("@username", username).ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                if (AppRoles.Normalize(r.Str("role")) is { } role)
                    currentExtras.Add((role, r.DtNull("expires_at")));

        var desired = (await LoadDerivedRolesAsync(conn, employeeId, tx, ct)).ToArray();
        var desiredPrimary = desired[0];
        var desiredExtras = desired.Skip(1).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var currentExtraRoles = currentExtras.Select(x => x.Role).Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var exact = string.Equals(currentPrimary, desiredPrimary, StringComparison.Ordinal)
                    && currentExtraRoles.SequenceEqual(desiredExtras, StringComparer.Ordinal)
                    && currentExtras.All(x => x.ExpiresAt is null)
                    && currentExtras.Count == currentExtraRoles.Length;

        var before = string.Join(", ", new[] { currentPrimary }.Concat(currentExtraRoles));
        var after = string.Join(", ", desired);
        if (exact && !forceAuthorizationChange)
            return new SyncResult(true, false, username, before, after);

        if (!exact && string.Equals(currentPrimary, AppRoles.Admin, StringComparison.Ordinal)
                   && !string.Equals(desiredPrimary, AppRoles.Admin, StringComparison.Ordinal))
        {
            if (tx is null)
                throw new InvalidOperationException("Giảm quyền Admin phải chạy trong transaction.");
            await Command(conn, tx, "SELECT pg_advisory_xact_lock(823746120031)")
                .ExecuteNonQueryAsync(ct);
            var otherAdmins = Convert.ToInt32(await Command(conn, tx, """
                SELECT COUNT(*) FROM app_users
                WHERE role='Admin' AND is_active=TRUE AND is_deleted=FALSE AND id<>@id
                """).With("@id", userId).ExecuteScalarAsync(ct));
            if (otherAdmins == 0) throw new LastAdministratorException();
        }

        if (!exact)
        {
            await Command(conn, tx, """
                UPDATE app_users
                SET role=@role, authorization_version=COALESCE(authorization_version, 1)+1
                WHERE id=@id
                """).With("@role", desiredPrimary).With("@id", userId).ExecuteNonQueryAsync(ct);
            await Command(conn, tx, "DELETE FROM user_roles WHERE username=@username")
                .With("@username", username).ExecuteNonQueryAsync(ct);
            foreach (var role in desiredExtras)
                await Command(conn, tx, """
                    INSERT INTO user_roles(username, role, granted_by, granted_at, expires_at)
                    VALUES (@username, @role, @actor, CURRENT_TIMESTAMP, NULL)
                    """).With("@username", username).With("@role", role).With("@actor", actor)
                    .ExecuteNonQueryAsync(ct);
        }
        else
        {
            // Role không đổi nhưng phạm vi dữ liệu (staff/phòng ban/địa điểm) đã đổi.
            await Command(conn, tx, """
                UPDATE app_users
                SET authorization_version=COALESCE(authorization_version, 1)+1
                WHERE id=@id
                """).With("@id", userId).ExecuteNonQueryAsync(ct);
        }

        var action = exact ? "Đồng bộ phạm vi theo chức vụ" : "Đồng bộ vai trò theo chức vụ";
        var reason = string.IsNullOrWhiteSpace(changeReason)
            ? (exact
                ? "Phạm vi truy cập tài khoản được suy ra lại từ toàn bộ chức vụ của hồ sơ nhân sự."
                : "Vai trò tài khoản được suy ra từ toàn bộ chức vụ của hồ sơ nhân sự.")
            : changeReason.Trim();
        await Command(conn, tx, """
            INSERT INTO user_role_history
                (username, changed_by, action, roles_before, roles_after, reason, client_ip)
            VALUES (@username, @actor, @action, @before, @after, @reason, @ip)
            """).With("@username", username).With("@actor", actor)
            .With("@action", action).With("@before", before).With("@after", after)
            .With("@reason", reason).With("@ip", clientIp)
            .ExecuteNonQueryAsync(ct);

        return new SyncResult(true, true, username, before, after);
    }

    private static NpgsqlCommand Command(NpgsqlConnection conn, NpgsqlTransaction? tx, string sql)
        => tx is null ? conn.Cmd(sql) : conn.Cmd(sql, tx);
}
