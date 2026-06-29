using System.Security.Claims;
using System.Security.Cryptography;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUsers(this IEndpointRouteBuilder app)
    {
        // Toàn bộ module nhân sự chỉ dành cho Admin.
        var g = app.MapGroup("/api/users").RequireAuthorization(p => p.RequireRole("Admin"));

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
                          (u.role = 'Admin' OR vu.username IS NOT NULL) AS verified
                   FROM app_users u
                   LEFT JOIN web_verified_users vu ON vu.username = u.username
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
                list.Add(new UserAdminDto(r.Guid("id"), r.Str("username"), r.Str("full_name"),
                    r.Str("email"), r.Str("role"), r.Bool("is_active"), r.Str("approval_status"), r.DtNull("created_at"),
                    r.Bool("is_online"), r.DtNull("last_seen"), r.Bool("verified")));
            return Results.Ok(list);
        });

        g.MapPost("/", async (CreateUserRequest req, ClaimsPrincipal u, Database db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { message = "Vui lòng nhập tên đăng nhập và mật khẩu." });

            await using var conn = await db.OpenAsync();
            var exists = await conn.Cmd("SELECT COUNT(*) FROM app_users WHERE username=@u AND is_deleted=FALSE")
                .With("@u", req.Username.Trim()).ExecuteScalarAsync();
            if (Convert.ToInt32(exists) > 0)
                return Results.Conflict(new { message = "Tên đăng nhập đã tồn tại." });

            var id = Guid.NewGuid();
            await conn.Cmd(
                @"INSERT INTO app_users (id, username, full_name, email, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at)
                  VALUES (@id, @u, @fn, @em, @role, @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, @by, CURRENT_TIMESTAMP)")
                .With("@id", id).With("@u", req.Username.Trim()).With("@fn", req.FullName ?? "")
                .With("@em", req.Email ?? "")
                .With("@role", req.Role == "Admin" ? "Admin" : "User")
                .With("@ph", PasswordHasher.Hash(req.Password)).With("@by", u.Username())
                .ExecuteNonQueryAsync();

            await db.RecordAudit(u.Username(), "Tạo người dùng", "User", req.Username, "Admin tạo tài khoản (web).");
            return Results.Ok(new { id });
        });

        g.MapPost("/{id:guid}/approve", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd(
                @"UPDATE app_users SET approval_status='Approved', is_active=TRUE, approved_at=CURRENT_TIMESTAMP, approved_by=@by
                  WHERE id=@id AND is_deleted=FALSE").With("@id", id).With("@by", u.Username()).ExecuteNonQueryAsync();
            if (n > 0) await db.RecordAudit(u.Username(), "Phê duyệt người dùng", "User", id.ToString(), "Phê duyệt tài khoản (web).");
            return n > 0 ? Results.NoContent() : Results.NotFound();
        });

        g.MapPost("/{id:guid}/lock", async (Guid id, SetLockRequest req, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("UPDATE app_users SET is_active=@active WHERE id=@id AND is_deleted=FALSE")
                .With("@active", !req.Locked).With("@id", id).ExecuteNonQueryAsync();
            if (n > 0) await db.RecordAudit(u.Username(), req.Locked ? "Khóa tài khoản" : "Mở khóa tài khoản", "User", id.ToString(), "(web)");
            return n > 0 ? Results.NoContent() : Results.NotFound();
        });

        // Cấp / thu hồi "tích xanh" thủ công cho một tài khoản. Tài khoản Admin luôn có tích xanh
        // (không cần ghi bảng); bảng web_verified_users chỉ lưu các tài khoản thường được nâng cấp.
        g.MapPost("/{id:guid}/verify", async (Guid id, SetVerifiedRequest req, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var username = Convert.ToString(await conn.Cmd(
                "SELECT username FROM app_users WHERE id = @id AND is_deleted = FALSE LIMIT 1")
                .With("@id", id).ExecuteScalarAsync())?.Trim();
            if (string.IsNullOrWhiteSpace(username)) return Results.NotFound();

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
                return Results.NoContent();
            }
            catch (NpgsqlException ex)
            {
                await tx.RollbackAsync();
                return Results.Json(new { message = "Loi xoa tai khoan: " + ex.Message }, statusCode: 400);
            }
        });
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
