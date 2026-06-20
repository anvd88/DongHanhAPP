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
            if (!string.IsNullOrWhiteSpace(search)) where += " AND (u.username LIKE @s OR u.full_name LIKE @s)";
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
                $@"SELECT u.id, u.username, u.full_name, u.role, u.is_active, u.approval_status, u.created_at,
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
                    r.Str("role"), r.Bool("is_active"), r.Str("approval_status"), r.DtNull("created_at"),
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
                @"INSERT INTO dbo.app_users (id, username, full_name, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at)
                  VALUES (@id, @u, @fn, @role, @ph, 1, N'Approved', SYSUTCDATETIME(), @by, SYSUTCDATETIME())")
                .With("@id", id).With("@u", req.Username.Trim()).With("@fn", req.FullName ?? "")
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
            // Xóa mềm — giống app desktop (is_deleted = 1).
            var n = await conn.Cmd("UPDATE dbo.app_users SET is_deleted=1, deleted_at=SYSUTCDATETIME(), is_active=0 WHERE id=@id AND is_deleted=0")
                .With("@id", id).ExecuteNonQueryAsync();
            if (n > 0) await db.RecordAudit(u.Username(), "Xóa người dùng", "User", id.ToString(), "Xóa mềm tài khoản (web).");
            return n > 0 ? Results.NoContent() : Results.NotFound();
        });
    }
}
