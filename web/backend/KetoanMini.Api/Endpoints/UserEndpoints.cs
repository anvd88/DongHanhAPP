using System.Security.Claims;
using System.Security.Cryptography;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Realtime;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.SignalR;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUsers(this IEndpointRouteBuilder app)
    {
        // Quản lý tài khoản & phân quyền — chốt bằng QUYỀN users.manage (xem Security/Permissions.cs),
        // không phải bằng tên vai trò "Admin".
        var g = app.MapGroup("/api/users").RequirePermission(Permissions.UsersManage);

        // Danh mục vai trò gán được + QUYỀN mỗi vai trò cấp — để UI hiện rõ "vai trò này làm được gì".
        // Chỉ đọc, nguồn là Security/Permissions.cs (không nhân bản mapping ở frontend).
        app.MapGet("/api/roles/catalog", () =>
        {
            string[] roles =
                [AppRoles.Admin, AppRoles.Accounting, AppRoles.ChiefAccountant, AppRoles.Hr, AppRoles.Manager, AppRoles.Warehouse, AppRoles.Employee];
            var catalog = roles.Select(role => new
            {
                role,
                label = AppRoles.Label(role),
                permissions = (Permissions.RolePermissions.TryGetValue(role, out var perms) ? perms : Array.Empty<string>())
                    .Select(p => new { key = p, label = Permissions.Label(p) })
                    .ToArray(),
            }).ToArray();
            return Results.Ok(catalog);
        }).RequirePermission(Permissions.UsersManage);

        g.MapGet("/", async (Database db, string? search, string? role) =>
        {
            await using var conn = await db.OpenAsync();
            var where = "WHERE u.is_deleted = FALSE";
            if (!string.IsNullOrWhiteSpace(search)) where += " AND (u.username ILIKE @s OR u.full_name ILIKE @s OR u.email ILIKE @s)";
            where += role switch
            {
                "Admin" => " AND u.role = 'Admin'",
                "User" => " AND u.role = 'User'",
                "Pending" => " AND u.approval_status = 'Pending'",
                "Locked" => " AND u.is_active = FALSE",
                _ => ""
            };

            var list = new List<UserAdminDto>();
            var cmd = conn.Cmd(
                $@"SELECT u.id, u.username, u.full_name, u.email, u.role, u.is_active, u.approval_status, u.created_at,
                          COALESCE(p.is_online, FALSE) AS is_online,
                          p.last_seen,
                          (u.role = 'Admin' OR vu.username IS NOT NULL) AS verified,
                          (u.role = 'Admin' OR du.username IS NOT NULL) AS is_diamond,
                          COALESCE((SELECT string_agg(ur.role, ',' ORDER BY ur.role)
                                    FROM user_roles ur WHERE ur.username = u.username), '') AS secondary_roles
                   FROM app_users u
                   LEFT JOIN web_verified_users vu ON vu.username = u.username
                   LEFT JOIN web_diamond_members du ON du.username = u.username
                   LEFT JOIN LATERAL (
                       SELECT
                           BOOL_OR(us.is_active = TRUE AND us.last_seen >= CURRENT_TIMESTAMP - INTERVAL '90 seconds') AS is_online,
                           MAX(us.last_seen) AS last_seen
                       FROM user_sessions us
                       WHERE us.username = u.username
                         AND (us.last_seen >= CURRENT_DATE OR us.is_active = TRUE)
                   ) p ON TRUE
                   {where}
                   ORDER BY u.created_at DESC");
            if (!string.IsNullOrWhiteSpace(search)) cmd.With("@s", $"%{search}%");
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var secondary = r.Str("secondary_roles")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(AppRoles.Normalize).OfType<string>().Distinct().ToList();
                list.Add(new UserAdminDto(r.Guid("id"), r.Str("username"), r.Str("full_name"),
                    r.Str("email"), r.Str("role"), r.Bool("is_active"), r.Str("approval_status"), r.DtNull("created_at"),
                    r.Bool("is_online"), r.DtNull("last_seen"), r.Bool("verified"), r.Bool("is_diamond"), secondary));
            }
            return Results.Ok(list);
        });

        g.MapPost("/", async (CreateUserRequest req, ClaimsPrincipal u, Database db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { message = "Vui lòng nhập tên đăng nhập và mật khẩu." });
            var role = AppRoles.Normalize(req.Role);
            if (role is null)
                return Results.BadRequest(new { message = $"Role phải là một trong: {string.Join(", ", AppRoles.All)}." });

            await using var conn = await db.OpenAsync();
            var exists = await conn.Cmd("SELECT COUNT(*) FROM app_users WHERE username=@u AND is_deleted=FALSE")
                .With("@u", req.Username.Trim()).ExecuteScalarAsync();
            if (Convert.ToInt32(exists) > 0)
                return Results.Conflict(new { message = "Tên đăng nhập đã tồn tại." });

            var id = Guid.NewGuid();
            await conn.Cmd(
                @"INSERT INTO app_users (id, username, full_name, email, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at)
                  VALUES (@id, @u, @fn, @em, @role, @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, @by,
                          COALESCE((SELECT (e.hire_date::timestamp + TIME '09:00') AT TIME ZONE 'Asia/Ho_Chi_Minh'
                                    FROM hr_employees e
                                    WHERE lower(e.username)=lower(@u) AND e.hire_date IS NOT NULL LIMIT 1), CURRENT_TIMESTAMP))")
                .With("@id", id).With("@u", req.Username.Trim()).With("@fn", req.FullName ?? "")
                .With("@em", req.Email ?? "")
                .With("@role", role)
                .With("@ph", PasswordHasher.Hash(req.Password)).With("@by", u.Username())
                .ExecuteNonQueryAsync();

            var employeeId = await conn.Cmd("SELECT id FROM hr_employees WHERE lower(username)=lower(@u) LIMIT 1")
                .With("@u", req.Username.Trim()).ExecuteScalarAsync();
            if (employeeId is Guid eid) await HrEndpoints.SyncEmployeeAccountSharedFields(conn, eid);

            await db.RecordAudit(u.Username(), "Tạo người dùng", "User", req.Username, "Admin tạo tài khoản (web).");
            return Results.Ok(new { id });
        });

        // Đổi vai trò CHÍNH của một tài khoản đã có. Không có endpoint này thì role Accounting/HR chỉ đặt
        // được lúc tạo tài khoản — tức là không cấp được quyền kế toán cho người đang làm việc.
        g.MapPost("/{id:guid}/role", async (Guid id, SetRoleRequest req, ClaimsPrincipal u, Database db,
            IHubContext<ChangesHub> hub, HttpContext http) =>
        {
            var role = AppRoles.Normalize(req.Role);
            if (role is null)
                return Results.BadRequest(new { message = $"Role phải là một trong: {string.Join(", ", AppRoles.All)}." });

            await using var conn = await db.OpenAsync();
            var target = await conn.Cmd("SELECT username FROM app_users WHERE id=@id AND is_deleted=FALSE")
                .With("@id", id).ExecuteScalarAsync() as string;
            if (target is null) return Results.NotFound();
            // Tự hạ quyền chính mình là cách nhanh nhất để khoá hết admin ra ngoài hệ thống.
            if (string.Equals(target, u.Username(), StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { message = "Không thể tự đổi vai trò của chính mình." });

            var before = await SnapshotRolesAsync(conn, target);
            await conn.Cmd("UPDATE app_users SET role=@r WHERE id=@id").With("@id", id).With("@r", role)
                .ExecuteNonQueryAsync();
            await AfterAuthorizationChangeAsync(conn, db, hub, http, u.Username(), target,
                "Đổi vai trò chính", before, req.Reason);
            return Results.NoContent();
        });

        // Cấp / thu hồi một VAI TRÒ PHỤ (đa vai trò): Thủ kho, Trưởng phòng, Kế toán trưởng… Không đụng
        // tới vai trò chính — một người có thể vừa là Kế toán vừa được cấp thêm Thủ kho.
        // ExpiresAt cho phép ỦY QUYỀN TẠM (vd trưởng phòng đi vắng): hết hạn tự mất, không cần nhớ thu hồi.
        g.MapPost("/{id:guid}/secondary-role", async (Guid id, SetSecondaryRoleRequest req, ClaimsPrincipal u,
            Database db, IHubContext<ChangesHub> hub, HttpContext http) =>
        {
            var role = AppRoles.Normalize(req.Role);
            // Chỉ cấp được các vai trò nằm trong danh sách vai trò PHỤ hợp lệ. Cố ý không cho cấp
            // Admin/Kiosk kiểu này: nâng lên Admin phải đổi vai trò CHÍNH để còn đối chiếu được audit.
            if (role is null || !AppRoles.Secondary.Contains(role))
                return Results.BadRequest(new
                {
                    message = $"Vai trò phụ phải là một trong: {string.Join(", ", AppRoles.Secondary.Select(AppRoles.Label))}."
                });

            await using var conn = await db.OpenAsync();
            var target = await conn.Cmd("SELECT username FROM app_users WHERE id=@id AND is_deleted=FALSE")
                .With("@id", id).ExecuteScalarAsync() as string;
            if (string.IsNullOrWhiteSpace(target)) return Results.NotFound();

            var before = await SnapshotRolesAsync(conn, target);
            if (req.Grant)
                await conn.Cmd(
                    @"INSERT INTO user_roles (username, role, granted_by, granted_at, expires_at)
                      VALUES (@u, @r, @by, CURRENT_TIMESTAMP, @exp)
                      ON CONFLICT (username, role) DO UPDATE SET
                          granted_by = EXCLUDED.granted_by,
                          granted_at = EXCLUDED.granted_at,
                          expires_at = EXCLUDED.expires_at")
                    .With("@u", target).With("@r", role).With("@by", u.Username())
                    .With("@exp", (object?)req.ExpiresAt ?? DBNull.Value).ExecuteNonQueryAsync();
            else
                await conn.Cmd("DELETE FROM user_roles WHERE username=@u AND role=@r")
                    .With("@u", target).With("@r", role).ExecuteNonQueryAsync();

            var what = $"{(req.Grant ? "Cấp" : "Thu hồi")} vai trò {AppRoles.Label(role)}";
            await AfterAuthorizationChangeAsync(conn, db, hub, http, u.Username(), target, what, before, req.Reason);
            return Results.NoContent();
        });

        g.MapPost("/{id:guid}/approve", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var resigned = await conn.Cmd("""
                SELECT EXISTS(
                    SELECT 1 FROM hr_employees e
                    JOIN app_users au ON au.id=@id AND (au.id=e.user_id OR lower(au.username)=lower(e.username))
                    WHERE lower(e.status)<>'active'
                )
                """).With("@id", id).ExecuteScalarAsync() is bool stopped && stopped;
            if (resigned)
                return Results.BadRequest(new { message = "Nhân viên đã nghỉ làm nên không thể phê duyệt quyền truy cập." });
            var n = await conn.Cmd(
                @"UPDATE app_users SET approval_status='Approved', is_active=TRUE, approved_at=CURRENT_TIMESTAMP, approved_by=@by
                  WHERE id=@id AND is_deleted=FALSE").With("@id", id).With("@by", u.Username()).ExecuteNonQueryAsync();
            if (n > 0) await db.RecordAudit(u.Username(), "Phê duyệt người dùng", "User", id.ToString(), "Phê duyệt tài khoản (web).");
            return n > 0 ? Results.NoContent() : Results.NotFound();
        });

        g.MapPost("/{id:guid}/lock", async (Guid id, SetLockRequest req, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            await using (var reader = await conn.Cmd(
                "SELECT username, role FROM app_users WHERE id=@id AND is_deleted=FALSE LIMIT 1")
                .With("@id", id)
                .ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync()) return Results.NotFound();

                var username = reader.Str("username");
                var role = reader.Str("role");
                if (req.Locked && string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(username, u.Username(), StringComparison.OrdinalIgnoreCase))
                        return Results.BadRequest(new { message = "Không thể tự khóa tài khoản Admin đang dùng." });
                    await reader.CloseAsync();
                    var otherActiveAdmins = Convert.ToInt32(await conn.Cmd(
                        "SELECT COUNT(*) FROM app_users WHERE role='Admin' AND is_active=TRUE AND is_deleted=FALSE AND id<>@id")
                        .With("@id", id).ExecuteScalarAsync());
                    if (otherActiveAdmins == 0)
                        return Results.BadRequest(new { message = "Phải có ít nhất một Admin hoạt động khác trước khi khóa tài khoản này." });
                }

                if (!reader.IsClosed) await reader.CloseAsync();
                if (!req.Locked)
                {
                    var resigned = await conn.Cmd("""
                        SELECT EXISTS(
                            SELECT 1 FROM hr_employees e
                            WHERE lower(e.username)=lower(@u) AND lower(e.status)<>'active'
                        )
                        """).With("@u", username).ExecuteScalarAsync() is bool stopped && stopped;
                    if (resigned)
                        return Results.BadRequest(new { message = "Nhân viên đã nghỉ làm. Hãy chuyển hồ sơ về Đang làm trước khi mở khóa tài khoản." });
                }
                var n = await conn.Cmd("UPDATE app_users SET is_active=@active WHERE id=@id AND is_deleted=FALSE")
                    .With("@active", !req.Locked).With("@id", id).ExecuteNonQueryAsync();
                if (n > 0) await db.RecordAudit(u.Username(), req.Locked ? "Khóa tài khoản" : "Mở khóa tài khoản", "User", username, "(web)");
                return n > 0 ? Results.NoContent() : Results.NotFound();
            }
        });

        // Cấp / thu hồi "tích xanh" thủ công cho một tài khoản. Tài khoản Admin luôn có tích xanh
        // (không cần ghi bảng); bảng web_verified_users chỉ lưu các tài khoản thường được nâng cấp.
        g.MapPost("/{id:guid}/verify", async (Guid id, SetVerifiedRequest req, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            string username;
            string role;
            await using (var reader = await conn.Cmd(
                "SELECT username, role FROM app_users WHERE id = @id AND is_deleted = FALSE LIMIT 1")
                .With("@id", id).ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync()) return Results.NotFound();
                username = reader.Str("username").Trim();
                role = reader.Str("role");
            }
            if (string.IsNullOrWhiteSpace(username)) return Results.NotFound();
            if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { message = "Admin luôn có đầy đủ đặc quyền." });

            await ChatEndpoints.EnsureTables(db);
            if (req.Verified)
                await conn.Cmd(
                    @"INSERT INTO web_verified_users (username, granted_by, granted_at)
                      VALUES (@u, @by, CURRENT_TIMESTAMP)
                      ON CONFLICT (username) DO UPDATE SET
                          granted_by = EXCLUDED.granted_by,
                          granted_at = EXCLUDED.granted_at")
                    .With("@u", username).With("@by", u.Username()).ExecuteNonQueryAsync();
            else
                await conn.Cmd("DELETE FROM web_verified_users WHERE username = @u")
                    .With("@u", username).ExecuteNonQueryAsync();

            await db.RecordAudit(u.Username(), req.Verified ? "Cấp tích xanh" : "Thu hồi tích xanh", "User", username, "(web)");
            return Results.NoContent();
        });

        g.MapPost("/{id:guid}/diamond", async (Guid id, SetDiamondRequest req, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var username = Convert.ToString(await conn.Cmd(
                "SELECT username FROM app_users WHERE id = @id AND is_deleted = FALSE LIMIT 1")
                .With("@id", id).ExecuteScalarAsync())?.Trim();
            if (string.IsNullOrWhiteSpace(username)) return Results.NotFound();

            await ChatEndpoints.EnsureTables(db);
            if (req.IsDiamond)
                await conn.Cmd(
                    @"INSERT INTO web_diamond_members (username, granted_by, granted_at)
                      VALUES (@u, @by, CURRENT_TIMESTAMP)
                      ON CONFLICT (username) DO UPDATE SET
                          granted_by = EXCLUDED.granted_by,
                          granted_at = EXCLUDED.granted_at")
                    .With("@u", username).With("@by", u.Username()).ExecuteNonQueryAsync();
            else
                await conn.Cmd("DELETE FROM web_diamond_members WHERE username = @u")
                    .With("@u", username).ExecuteNonQueryAsync();

            await db.RecordAudit(u.Username(), req.IsDiamond ? "Cap hoi vien kim cuong" : "Thu hoi hoi vien kim cuong", "User", username, "(web)");
            return Results.NoContent();
        });

        g.MapPost("/{id:guid}/reset-password", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            // Tạo mật khẩu tạm ngẫu nhiên, đặt lại cho người dùng và trả về cho admin (giống CodeDisplayWpfWindow).
            var temp = RandomNumberGenerator.GetHexString(8).ToUpperInvariant();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("UPDATE app_users SET password_hash=@ph WHERE id=@id AND is_deleted=FALSE")
                .With("@ph", PasswordHasher.Hash(temp)).With("@id", id).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await db.RecordAudit(u.Username(), "Đặt lại mật khẩu", "User", id.ToString(), "Admin đặt lại mật khẩu (web).");
            return Results.Ok(new ResetPasswordResponse(temp));
        });

        // Cấp MÃ KHÔI PHỤC cho người dùng tự đặt lại mật khẩu (thay cho reset bằng khuôn mặt đã tắt).
        // Admin đưa mã cho nhân viên → nhân viên dùng ở /api/auth/reset-with-recovery-code. Mã một lần,
        // hết hạn sau 7 ngày, và cấp mã mới sẽ vô hiệu hóa mọi mã cũ chưa dùng của tài khoản đó.
        g.MapPost("/{id:guid}/recovery-code", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var username = (await conn.Cmd("SELECT username FROM app_users WHERE id=@id AND is_deleted=FALSE LIMIT 1")
                .With("@id", id).ExecuteScalarAsync()) as string;
            if (string.IsNullOrWhiteSpace(username)) return Results.NotFound();

            var code = RecoveryCodes.Generate();
            await conn.Cmd("UPDATE password_recovery_codes SET used_at=CURRENT_TIMESTAMP WHERE username=@u AND used_at IS NULL")
                .With("@u", username).ExecuteNonQueryAsync();
            await conn.Cmd(
                @"INSERT INTO password_recovery_codes (username, code_hash, created_by, expires_at)
                  VALUES (@u, @h, @by, CURRENT_TIMESTAMP + INTERVAL '7 days')")
                .With("@u", username)
                .With("@h", PasswordHasher.Hash(RecoveryCodes.Normalize(code)))
                .With("@by", u.Username())
                .ExecuteNonQueryAsync();
            await db.RecordAudit(u.Username(), "Cấp mã khôi phục mật khẩu", "User", id.ToString(),
                $"Admin cấp mã khôi phục cho '{username}' (hết hạn sau 7 ngày, web).");
            return Results.Ok(new RecoveryCodeResponse(code));
        });

        g.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            await using var tx = (NpgsqlTransaction)await conn.BeginTransactionAsync();
            try
            {
                var find = new NpgsqlCommand(
                    "SELECT username FROM app_users WHERE id=@id AND is_deleted=FALSE FOR UPDATE LIMIT 1",
                    conn, tx);
                find.Parameters.AddWithValue("@id", id);

                var username = Convert.ToString(await find.ExecuteScalarAsync())?.Trim();
                if (string.IsNullOrWhiteSpace(username))
                {
                    await tx.RollbackAsync();
                    return Results.NotFound();
                }

                await DeleteUserEverywhere(conn, tx, id, username);
                await tx.CommitAsync();
                // Ghi nhật ký hành động xóa (do admin thực hiện) — bản ghi này nằm ngoài phần
                // audit của người bị xóa nên không bị xóa cùng, giữ dấu vết ai đã xóa tài khoản nào.
                await db.RecordAudit(u.Username(), "Xóa tài khoản", "User", username, $"Admin xóa tài khoản (web). id={id}");
                return Results.NoContent();
            }
            catch (NpgsqlException)
            {
                await tx.RollbackAsync();
                return Results.Json(new { message = "Khong xoa duoc tai khoan (du lieu lien quan hoac rang buoc)." }, statusCode: 400);
            }
        });
    }

    /// <summary>Ảnh chụp vai trò hiện tại của một tài khoản, để ghi audit "trước → sau".</summary>
    private static async Task<string> SnapshotRolesAsync(NpgsqlConnection conn, string username)
        => string.Join(", ", await AccessProfileService.LoadRolesAsync(conn, username) ?? []);

    /// <summary>
    /// VIỆC PHẢI LÀM SAU MỖI LẦN ĐỔI QUYỀN — gom về một chỗ để không lần nào quên bước nào:
    ///  1. Tăng authorization_version (client so sánh để biết phải nạp lại hồ sơ truy cập).
    ///  2. Ghi lịch sử phân quyền có "trước → sau", ai đổi, lý do, địa chỉ truy cập.
    ///  3. Ghi audit chung (để tra cùng dòng thời gian với các thao tác khác).
    ///  4. Bắn tín hiệu realtime tới ĐÚNG người bị đổi quyền → web/app nạp lại menu, layout, route ngay.
    ///
    /// Quyền mới thực ra đã có hiệu lực từ request kế tiếp rồi (middleware đọc vai trò từ CSDL mỗi
    /// request); tín hiệu realtime chỉ để GIAO DIỆN khỏi hiển thị sai cho tới lần bấm tiếp theo.
    /// </summary>
    private static async Task AfterAuthorizationChangeAsync(
        NpgsqlConnection conn, Database db, IHubContext<ChangesHub> hub, HttpContext http,
        string actor, string target, string action, string rolesBefore, string? reason)
    {
        await conn.Cmd(
            "UPDATE app_users SET authorization_version = COALESCE(authorization_version, 1) + 1 WHERE username = @u")
            .With("@u", target).ExecuteNonQueryAsync();

        var rolesAfter = await SnapshotRolesAsync(conn, target);
        var note = string.IsNullOrWhiteSpace(reason) ? "" : reason.Trim();
        try
        {
            await conn.Cmd(
                @"INSERT INTO user_role_history (username, changed_by, action, roles_before, roles_after, reason, client_ip)
                  VALUES (@u, @by, @a, @b, @af, @r, @ip)")
                .With("@u", target).With("@by", actor).With("@a", action)
                .With("@b", rolesBefore).With("@af", rolesAfter).With("@r", note)
                .With("@ip", http.Connection.RemoteIpAddress?.ToString() ?? "")
                .ExecuteNonQueryAsync();
        }
        catch (NpgsqlException) { /* bảng lịch sử chưa có → vẫn còn audit_logs bên dưới */ }

        await db.RecordAudit(actor, action, "User", target,
            $"{action}: [{rolesBefore}] → [{rolesAfter}]{(note.Length > 0 ? $". Lý do: {note}" : "")} (web).");

        // Chỉ gửi cho ĐÚNG người bị đổi quyền (không phát tán ra toàn hệ thống).
        try { await hub.Clients.User(target).SendAsync("changed", "access"); } catch { /* không online → thôi */ }
    }

    private static async Task DeleteUserEverywhere(NpgsqlConnection conn, NpgsqlTransaction tx, Guid id, string username)
    {
        var cmd = new NpgsqlCommand(
            @"
DELETE FROM web_chat_messages msg
USING web_chat_conversations c
WHERE c.id = msg.conversation_id
  AND c.is_group = FALSE
  AND EXISTS (SELECT 1 FROM web_chat_members mm WHERE mm.conversation_id = c.id AND mm.username = @username);

DELETE FROM web_chat_messages WHERE sender_username = @username;
DELETE FROM web_chat_members WHERE username = @username;
DELETE FROM web_chat_conversations c
WHERE NOT EXISTS (SELECT 1 FROM web_chat_members mm WHERE mm.conversation_id = c.id);

DELETE FROM web_verified_users WHERE username = @username;
DELETE FROM web_diamond_members WHERE username = @username;
DELETE FROM user_roles WHERE username = @username;
DELETE FROM cham_cong_log WHERE username = @username;
DELETE FROM cham_cong_face WHERE username = @username;
UPDATE cham_cong_face SET created_by = '' WHERE created_by = @username;
DELETE FROM user_sessions WHERE username = @username;
DELETE FROM work_access_requests WHERE username = @username;
UPDATE work_access_requests SET approved_by = '' WHERE approved_by = @username;
DELETE FROM password_reset_requests WHERE username = @username;
UPDATE password_reset_requests SET resolved_by = '' WHERE resolved_by = @username;
UPDATE registration_codes SET created_by = '' WHERE created_by = @username;
UPDATE registration_codes SET used_by = '' WHERE used_by = @username;
UPDATE app_releases SET published_by = '' WHERE published_by = @username;
UPDATE app_settings SET updated_by = '' WHERE updated_by = @username;
UPDATE app_users SET approved_by = '' WHERE approved_by = @username;

DELETE FROM audit_logs
WHERE user_id = @id
   OR username = @username
   OR (entity = 'User' AND (entity_name = @username OR entity_name = @idText))
   OR (entity = 'ChamCong' AND entity_name = @username);

DELETE FROM app_users WHERE username = @username;",
            conn, tx);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@idText", id.ToString());
        cmd.Parameters.AddWithValue("@username", username);

        await cmd.ExecuteNonQueryAsync();
    }
}
