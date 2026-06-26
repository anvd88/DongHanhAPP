using System.Security.Claims;
using System.Security.Cryptography;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.Data.SqlClient;

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
            var where = "WHERE u.is_deleted = 0";
            if (!string.IsNullOrWhiteSpace(search)) where += " AND (u.username LIKE @s OR u.full_name LIKE @s OR u.email LIKE @s)";
            where += role switch
            {
                "Admin" => " AND u.role = N'Admin'",
                "User" => " AND u.role = N'User'",
                "Pending" => " AND u.approval_status = N'Pending'",
                "Locked" => " AND u.is_active = 0",
                _ => ""
            };

            var list = new List<UserAdminDto>();
            var cmd = conn.Cmd(
                $@"SELECT u.id, u.username, u.full_name, u.email, u.role, u.is_active, u.approval_status, u.created_at,
                          CAST(ISNULL(p.is_online, 0) AS bit) AS is_online,
                          p.last_seen
                   FROM dbo.app_users u
                   OUTER APPLY (
                       SELECT
                           MAX(CASE WHEN us.is_active = 1 AND us.last_seen >= DATEADD(SECOND, -90, SYSDATETIME()) THEN 1 ELSE 0 END) AS is_online,
                           MAX(us.last_seen) AS last_seen
                       FROM dbo.user_sessions us
                       WHERE us.username = u.username
                         AND (us.last_seen >= CONVERT(date, SYSDATETIME()) OR us.is_active = 1)
                   ) p
                   {where}
                   ORDER BY u.created_at DESC");
            if (!string.IsNullOrWhiteSpace(search)) cmd.With("@s", $"%{search}%");
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new UserAdminDto(r.Guid("id"), r.Str("username"), r.Str("full_name"),
                    r.Str("email"), r.Str("role"), r.Bool("is_active"), r.Str("approval_status"), r.DtNull("created_at"),
                    r.Bool("is_online"), r.DtNull("last_seen")));
            return Results.Ok(list);
        });

        g.MapPost("/", async (CreateUserRequest req, ClaimsPrincipal u, Database db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { message = "Vui lòng nhập tên đăng nhập và mật khẩu." });

            await using var conn = await db.OpenAsync();
            var exists = await conn.Cmd("SELECT COUNT(*) FROM dbo.app_users WHERE username=@u AND is_deleted=0")
                .With("@u", req.Username.Trim()).ExecuteScalarAsync();
            if (Convert.ToInt32(exists) > 0)
                return Results.Conflict(new { message = "Tên đăng nhập đã tồn tại." });

            var id = Guid.NewGuid();
            await conn.Cmd(
                @"INSERT INTO dbo.app_users (id, username, full_name, email, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at)
                  VALUES (@id, @u, @fn, @em, @role, @ph, 1, N'Approved', SYSUTCDATETIME(), @by, SYSUTCDATETIME())")
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
                @"UPDATE dbo.app_users SET approval_status=N'Approved', is_active=1, approved_at=SYSUTCDATETIME(), approved_by=@by
                  WHERE id=@id AND is_deleted=0").With("@id", id).With("@by", u.Username()).ExecuteNonQueryAsync();
            if (n > 0) await db.RecordAudit(u.Username(), "Phê duyệt người dùng", "User", id.ToString(), "Phê duyệt tài khoản (web).");
            return n > 0 ? Results.NoContent() : Results.NotFound();
        });

        g.MapPost("/{id:guid}/lock", async (Guid id, SetLockRequest req, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("UPDATE dbo.app_users SET is_active=@active WHERE id=@id AND is_deleted=0")
                .With("@active", !req.Locked).With("@id", id).ExecuteNonQueryAsync();
            if (n > 0) await db.RecordAudit(u.Username(), req.Locked ? "Khóa tài khoản" : "Mở khóa tài khoản", "User", id.ToString(), "(web)");
            return n > 0 ? Results.NoContent() : Results.NotFound();
        });

        g.MapPost("/{id:guid}/reset-password", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            // Tạo mật khẩu tạm ngẫu nhiên, đặt lại cho người dùng và trả về cho admin (giống CodeDisplayWpfWindow).
            var temp = RandomNumberGenerator.GetHexString(8).ToUpperInvariant();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("UPDATE dbo.app_users SET password_hash=@ph WHERE id=@id AND is_deleted=0")
                .With("@ph", PasswordHasher.Hash(temp)).With("@id", id).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await db.RecordAudit(u.Username(), "Đặt lại mật khẩu", "User", id.ToString(), "Admin đặt lại mật khẩu (web).");
            return Results.Ok(new ResetPasswordResponse(temp));
        });

        g.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
            try
            {
                var find = new SqlCommand(
                    "SELECT TOP 1 username FROM dbo.app_users WITH (UPDLOCK, HOLDLOCK) WHERE id=@id AND is_deleted=0",
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
            catch (SqlException ex)
            {
                await tx.RollbackAsync();
                return Results.Json(new { message = "Loi xoa tai khoan: " + ex.Message }, statusCode: 400);
            }
        });
    }

    private static async Task DeleteUserEverywhere(SqlConnection conn, SqlTransaction tx, Guid id, string username)
    {
        var cmd = new SqlCommand(
            @"
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

IF OBJECT_ID(N'dbo.chat_file_offers', N'U') IS NOT NULL
BEGIN
    DELETE o
    FROM dbo.chat_file_offers o
    WHERE o.sender_username = @username
       OR o.receiver_username = @username
       OR o.sender_id = @id
       OR o.receiver_id = @id
       OR EXISTS (
            SELECT 1
            FROM dbo.chat_messages m
            WHERE m.id = o.message_id
              AND (m.sender_username = @username OR m.receiver_username = @username OR m.sender_id = @id OR m.receiver_id = @id)
       )
       OR EXISTS (
            SELECT 1
            FROM dbo.chat_conversations c
            WHERE c.id = o.conversation_id
              AND (c.user_a = @username OR c.user_b = @username OR c.user_a_id = @id OR c.user_b_id = @id)
       );
END;

IF OBJECT_ID(N'dbo.chat_messages', N'U') IS NOT NULL
BEGIN
    DELETE m
    FROM dbo.chat_messages m
    WHERE m.sender_username = @username
       OR m.receiver_username = @username
       OR m.sender_id = @id
       OR m.receiver_id = @id
       OR EXISTS (
            SELECT 1
            FROM dbo.chat_conversations c
            WHERE c.id = m.conversation_id
              AND (c.user_a = @username OR c.user_b = @username OR c.user_a_id = @id OR c.user_b_id = @id)
       );
END;

IF OBJECT_ID(N'dbo.chat_conversations', N'U') IS NOT NULL
BEGIN
    DELETE FROM dbo.chat_conversations
    WHERE user_a = @username
       OR user_b = @username
       OR user_a_id = @id
       OR user_b_id = @id;
END;

IF OBJECT_ID(N'dbo.cham_cong_log', N'U') IS NOT NULL
    DELETE FROM dbo.cham_cong_log WHERE username = @username;

IF OBJECT_ID(N'dbo.cham_cong_face', N'U') IS NOT NULL
BEGIN
    DELETE FROM dbo.cham_cong_face WHERE username = @username;
    UPDATE dbo.cham_cong_face SET created_by = N'' WHERE created_by = @username;
END;

IF OBJECT_ID(N'dbo.user_sessions', N'U') IS NOT NULL
    DELETE FROM dbo.user_sessions WHERE username = @username;

IF OBJECT_ID(N'dbo.work_access_requests', N'U') IS NOT NULL
BEGIN
    DELETE FROM dbo.work_access_requests WHERE username = @username;
    UPDATE dbo.work_access_requests SET approved_by = N'' WHERE approved_by = @username;
END;

IF OBJECT_ID(N'dbo.password_reset_requests', N'U') IS NOT NULL
BEGIN
    DELETE FROM dbo.password_reset_requests WHERE username = @username;
    UPDATE dbo.password_reset_requests SET resolved_by = N'' WHERE resolved_by = @username;
END;

IF OBJECT_ID(N'dbo.registration_codes', N'U') IS NOT NULL
BEGIN
    UPDATE dbo.registration_codes SET created_by = N'' WHERE created_by = @username;
    UPDATE dbo.registration_codes SET used_by = N'' WHERE used_by = @username;
END;

IF OBJECT_ID(N'dbo.app_releases', N'U') IS NOT NULL
    UPDATE dbo.app_releases SET published_by = N'' WHERE published_by = @username;

IF OBJECT_ID(N'dbo.app_settings', N'U') IS NOT NULL
    UPDATE dbo.app_settings SET updated_by = N'' WHERE updated_by = @username;

UPDATE dbo.app_users SET approved_by = N'' WHERE approved_by = @username;

IF OBJECT_ID(N'dbo.audit_logs', N'U') IS NOT NULL
BEGIN
    DELETE FROM dbo.audit_logs
    WHERE user_id = @id
       OR username = @username
       OR (entity = N'User' AND (entity_name = @username OR entity_name = @idText))
       OR (entity = N'ChamCong' AND entity_name = @username);
END;

DELETE FROM dbo.app_users WHERE username = @username;",
            conn, tx);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@idText", id.ToString());
        cmd.Parameters.AddWithValue("@username", username);

        await cmd.ExecuteNonQueryAsync();
    }
}
