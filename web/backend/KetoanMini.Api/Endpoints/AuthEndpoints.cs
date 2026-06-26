using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;

namespace KetoanMini.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth");

        g.MapPost("/login", async (LoginRequest req, Database db, TokenService tokens) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { message = "Vui lòng nhập tên đăng nhập và mật khẩu." });

            await using var conn = await db.OpenAsync();
            await using var reader = await conn.Cmd(
                @"SELECT id, username, full_name, email, role, password_hash, is_active, approval_status, created_at
                  FROM dbo.app_users
                  WHERE username = @u AND is_deleted = 0")
                .With("@u", req.Username.Trim())
                .ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return Results.Json(new { message = "Sai tên đăng nhập hoặc mật khẩu." }, statusCode: 401);

            var hash = reader.Str("password_hash");
            if (!PasswordHasher.Verify(req.Password, hash))
                return Results.Json(new { message = "Sai tên đăng nhập hoặc mật khẩu." }, statusCode: 401);

            var user = new UserDto(
                reader.Guid("id"), reader.Str("username"), reader.Str("full_name"),
                reader.Str("email"), reader.Str("role"), reader.Bool("is_active"), reader.Str("approval_status"),
                reader.DtNull("created_at"));

            if (user.IsPending)
                return Results.Json(new { message = "Tài khoản đang chờ quản trị viên phê duyệt." }, statusCode: 403);
            if (!user.IsActive)
                return Results.Json(new { message = "Tài khoản đã bị khóa. Liên hệ quản trị viên." }, statusCode: 403);

            await reader.CloseAsync();
            await db.RecordAudit(user.Username, "Đăng nhập web", "Auth", user.Username, "Đăng nhập phiên bản web.");

            return Results.Ok(new LoginResponse(tokens.CreateToken(user), user));
        });

        g.MapGet("/me", async (ClaimsPrincipal principal, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            await using var reader = await conn.Cmd(
                @"SELECT id, username, full_name, email, role, is_active, approval_status, created_at
                  FROM dbo.app_users WHERE username = @u AND is_deleted = 0")
                .With("@u", principal.Username())
                .ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return Results.Unauthorized();

            return Results.Ok(new UserDto(
                reader.Guid("id"), reader.Str("username"), reader.Str("full_name"),
                reader.Str("email"), reader.Str("role"), reader.Bool("is_active"), reader.Str("approval_status"),
                reader.DtNull("created_at")));
        }).RequireAuthorization();

        // Sửa hồ sơ của chính mình (web): đổi tên hiển thị. (Ảnh đại diện trên desktop lưu cục bộ
        // từng máy nên bản web chưa hỗ trợ — header hiển thị bằng chữ cái đầu.)
        g.MapPut("/profile", async (UpdateProfileRequest req, ClaimsPrincipal principal, Database db) =>
        {
            var fullName = (req.FullName ?? "").Trim();
            var email = (req.Email ?? "").Trim();
            if (string.IsNullOrWhiteSpace(fullName))
                return Results.BadRequest(new { message = "Vui lòng nhập tên hiển thị." });

            var username = principal.Username();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("UPDATE dbo.app_users SET full_name = @fn, email = @em WHERE username = @u AND is_deleted = 0")
                .With("@fn", fullName).With("@em", email).With("@u", username).ExecuteNonQueryAsync();
            if (n == 0) return Results.Unauthorized();

            await db.RecordAudit(username, "Sửa hồ sơ", "User", username, "Đổi tên hiển thị (web).");

            await using var reader = await conn.Cmd(
                @"SELECT id, username, full_name, email, role, is_active, approval_status, created_at
                  FROM dbo.app_users WHERE username = @u AND is_deleted = 0")
                .With("@u", username).ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return Results.Unauthorized();
            return Results.Ok(new UserDto(
                reader.Guid("id"), reader.Str("username"), reader.Str("full_name"),
                reader.Str("email"), reader.Str("role"), reader.Bool("is_active"), reader.Str("approval_status"),
                reader.DtNull("created_at")));
        }).RequireAuthorization();

        // Đổi mật khẩu của chính mình (web): xác minh mật khẩu hiện tại rồi đặt mật khẩu mới.
        g.MapPost("/change-password", async (ChangePasswordRequest req, ClaimsPrincipal principal, Database db) =>
        {
            var newPass = (req.NewPassword ?? "").Trim();
            if (string.IsNullOrWhiteSpace(newPass))
                return Results.BadRequest(new { message = "Vui lòng nhập mật khẩu mới." });

            var username = principal.Username();
            await using var conn = await db.OpenAsync();
            var hash = Convert.ToString(await conn.Cmd(
                "SELECT TOP 1 password_hash FROM dbo.app_users WHERE username = @u AND is_deleted = 0")
                .With("@u", username).ExecuteScalarAsync()) ?? "";
            if (string.IsNullOrEmpty(hash)) return Results.Unauthorized();

            if (!PasswordHasher.Verify(req.CurrentPassword ?? "", hash))
                return Results.Json(new { message = "Mật khẩu hiện tại không đúng." }, statusCode: 400);

            await conn.Cmd("UPDATE dbo.app_users SET password_hash = @ph WHERE username = @u AND is_deleted = 0")
                .With("@ph", PasswordHasher.Hash(newPass)).With("@u", username).ExecuteNonQueryAsync();
            await db.RecordAudit(username, "Đổi mật khẩu", "User", username, "Đổi mật khẩu (web).");
            return Results.NoContent();
        }).RequireAuthorization();

        // Nhịp tim hiện diện cho bản web: cập nhật last_seen trong user_sessions để app desktop
        // thấy người dùng "đang online". Mỗi trình duyệt gửi một sid ổn định (lưu localStorage).
        // Phiên này là 'Web' nên KHÔNG bị single-login của desktop kết thúc và cũng không kết
        // thúc phiên desktop. (Nếu tài khoản bị khóa, middleware ở Program.cs đã chặn bằng 401.)
        // Dùng SYSDATETIME() (giờ local của server) — KHÔNG dùng SYSUTCDATETIME() — vì app desktop
        // ghi/so sánh last_seen theo giờ local (DateTime.Now); lệch UTC sẽ làm web không bao giờ "online".
        g.MapPost("/heartbeat", async (HeartbeatRequest req, ClaimsPrincipal principal, Database db) =>
        {
            var username = principal.Username();
            if (string.IsNullOrWhiteSpace(username)) return Results.Unauthorized();
            var sid = WebSessionId(req?.Sid, username);

            await using var conn = await db.OpenAsync();
            await conn.Cmd(
                @"UPDATE dbo.user_sessions
                     SET last_seen = SYSDATETIME(), is_active = 1, username = @u, ended_at = NULL, end_reason = N'',
                         started_at = CASE WHEN is_active = 0 OR last_seen < DATEADD(SECOND, -90, SYSDATETIME())
                                           THEN SYSDATETIME() ELSE started_at END
                   WHERE session_token = @t AND client_kind = N'Web';
                  IF @@ROWCOUNT = 0
                     INSERT INTO dbo.user_sessions (session_token, username, machine_name, started_at, last_seen, is_active, client_kind)
                     VALUES (@t, @u, N'Web', SYSDATETIME(), SYSDATETIME(), 1, N'Web');")
                .With("@u", username).With("@t", sid)
                .ExecuteNonQueryAsync();
            return Results.NoContent();
        }).RequireAuthorization();

        // Đăng xuất chủ động trên web → tắt phiên ngay để ẩn khỏi danh sách online.
        g.MapPost("/logout", async (HeartbeatRequest req, ClaimsPrincipal principal, Database db) =>
        {
            var sid = WebSessionId(req?.Sid, principal.Username());
            await using var conn = await db.OpenAsync();
            await conn.Cmd(
                @"UPDATE dbo.user_sessions
                     SET is_active = 0, ended_at = SYSDATETIME(), end_reason = N'Đăng xuất (web)'
                   WHERE session_token = @t AND client_kind = N'Web';")
                .With("@t", sid).ExecuteNonQueryAsync();
            return Results.NoContent();
        }).RequireAuthorization();
    }

    // session_token là NVARCHAR(64); dùng sid của trình duyệt, fallback theo username nếu thiếu.
    private static string WebSessionId(string? sid, string username)
    {
        var value = string.IsNullOrWhiteSpace(sid) ? "web:" + username : sid.Trim();
        return value.Length > 64 ? value[..64] : value;
    }
}
