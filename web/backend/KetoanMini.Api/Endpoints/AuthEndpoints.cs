using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Npgsql;

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
                  FROM app_users
                  WHERE username = @u AND is_deleted = FALSE")
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
            user = user with { AvatarUrl = await LoadAvatarUrl(conn, user.Id), Verified = await LoadVerified(conn, user.Username, user.Role) };
            await db.RecordAudit(user.Username, "Đăng nhập web", "Auth", user.Username, "Đăng nhập phiên bản web.");

            return Results.Ok(new LoginResponse(tokens.CreateToken(user), user));
        });

        g.MapGet("/me", async (ClaimsPrincipal principal, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var user = await ReadUserByUsername(conn, principal.Username());
            if (user is null) return Results.Unauthorized();
            return Results.Ok(user with { AvatarUrl = await LoadAvatarUrl(conn, user.Id) });
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
            var n = await conn.Cmd("UPDATE app_users SET full_name = @fn, email = @em WHERE username = @u AND is_deleted = FALSE")
                .With("@fn", fullName).With("@em", email).With("@u", username).ExecuteNonQueryAsync();
            if (n == 0) return Results.Unauthorized();

            await db.RecordAudit(username, "Sửa hồ sơ", "User", username, "Đổi tên hiển thị (web).");

            var updated = await ReadUserByUsername(conn, username);
            if (updated is null) return Results.Unauthorized();
            return Results.Ok(updated with { AvatarUrl = await LoadAvatarUrl(conn, updated.Id) });
        }).RequireAuthorization();

        // Lưu ảnh đại diện cho bản web (data URL ảnh đã thu nhỏ/nén ở client). Lưu trong bảng
        // web-only web_user_avatars để KHÔNG đụng schema dùng chung với app desktop.
        g.MapPut("/avatar", async (UpdateAvatarRequest req, ClaimsPrincipal principal, Database db) =>
        {
            var dataUrl = (req.ImageDataUrl ?? "").Trim();
            if (string.IsNullOrWhiteSpace(dataUrl) || !dataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { message = "Ảnh không hợp lệ." });
            if (dataUrl.Length > 1_500_000)
                return Results.BadRequest(new { message = "Ảnh quá lớn. Vui lòng chọn ảnh khác." });

            var username = principal.Username();
            await using var conn = await db.OpenAsync();
            var user = await ReadUserByUsername(conn, username);
            if (user is null) return Results.Unauthorized();

            await EnsureAvatarTableOn(conn);
            await conn.Cmd(@"
                INSERT INTO web_user_avatars (user_id, image_data_url, updated_at)
                VALUES (@id, @v, CURRENT_TIMESTAMP)
                ON CONFLICT (user_id) DO UPDATE SET
                    image_data_url = EXCLUDED.image_data_url,
                    updated_at = EXCLUDED.updated_at;")
                .With("@id", user.Id).With("@v", dataUrl).ExecuteNonQueryAsync();

            await db.RecordAudit(username, "Cập nhật ảnh đại diện", "User", username, "Đổi ảnh đại diện (web).");
            return Results.Ok(user with { AvatarUrl = dataUrl });
        }).RequireAuthorization();

        // Xóa ảnh đại diện → quay lại hiển thị chữ cái đầu.
        g.MapDelete("/avatar", async (ClaimsPrincipal principal, Database db) =>
        {
            var username = principal.Username();
            await using var conn = await db.OpenAsync();
            var user = await ReadUserByUsername(conn, username);
            if (user is null) return Results.Unauthorized();

            await EnsureAvatarTableOn(conn);
            await conn.Cmd("DELETE FROM web_user_avatars WHERE user_id = @id")
                .With("@id", user.Id).ExecuteNonQueryAsync();

            await db.RecordAudit(username, "Xóa ảnh đại diện", "User", username, "Xóa ảnh đại diện (web).");
            return Results.Ok(user with { AvatarUrl = (string?)null });
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
                "SELECT password_hash FROM app_users WHERE username = @u AND is_deleted = FALSE LIMIT 1")
                .With("@u", username).ExecuteScalarAsync()) ?? "";
            if (string.IsNullOrEmpty(hash)) return Results.Unauthorized();

            if (!PasswordHasher.Verify(req.CurrentPassword ?? "", hash))
                return Results.Json(new { message = "Mật khẩu hiện tại không đúng." }, statusCode: 400);

            await conn.Cmd("UPDATE app_users SET password_hash = @ph WHERE username = @u AND is_deleted = FALSE")
                .With("@ph", PasswordHasher.Hash(newPass)).With("@u", username).ExecuteNonQueryAsync();
            await db.RecordAudit(username, "Đổi mật khẩu", "User", username, "Đổi mật khẩu (web).");
            return Results.NoContent();
        }).RequireAuthorization();

        // Nhịp tim hiện diện cho bản web: cập nhật last_seen trong user_sessions để app desktop
        // thấy người dùng "đang online". Mỗi trình duyệt gửi một sid ổn định (lưu localStorage).
        // Phiên này là 'Web' nên KHÔNG bị single-login của desktop kết thúc và cũng không kết
        // thúc phiên desktop. (Nếu tài khoản bị khóa, middleware ở Program.cs đã chặn bằng 401.)
        // Dùng CURRENT_TIMESTAMP để PostgreSQL ghi cùng chuẩn thời gian cho started_at/last_seen.
        g.MapPost("/heartbeat", async (HeartbeatRequest req, ClaimsPrincipal principal, Database db) =>
        {
            var username = principal.Username();
            if (string.IsNullOrWhiteSpace(username)) return Results.Unauthorized();
            var sid = WebSessionId(req?.Sid, username);

            await using var conn = await db.OpenAsync();
            await conn.Cmd(
                @"INSERT INTO user_sessions (session_token, username, machine_name, started_at, last_seen, is_active, client_kind)
                  VALUES (@t, @u, 'Web', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, TRUE, 'Web')
                  ON CONFLICT (session_token) DO UPDATE SET
                      username = EXCLUDED.username,
                      machine_name = 'Web',
                      last_seen = CURRENT_TIMESTAMP,
                      is_active = TRUE,
                      ended_at = NULL,
                      end_reason = '',
                      client_kind = 'Web',
                      started_at = CASE
                          WHEN user_sessions.is_active = FALSE
                            OR user_sessions.last_seen < CURRENT_TIMESTAMP - INTERVAL '90 seconds'
                          THEN CURRENT_TIMESTAMP
                          ELSE user_sessions.started_at
                      END;")
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
                @"UPDATE user_sessions
                     SET is_active = FALSE, ended_at = CURRENT_TIMESTAMP, end_reason = 'Đăng xuất (web)'
                   WHERE session_token = @t AND client_kind = 'Web';")
                .With("@t", sid).ExecuteNonQueryAsync();
            return Results.NoContent();
        }).RequireAuthorization();
    }

    // session_token là varchar(64); dùng sid của trình duyệt, fallback theo username nếu thiếu.
    private static string WebSessionId(string? sid, string username)
    {
        var value = string.IsNullOrWhiteSpace(sid) ? "web:" + username : sid.Trim();
        return value.Length > 64 ? value[..64] : value;
    }

    private static async Task<UserDto?> ReadUserByUsername(NpgsqlConnection conn, string username)
    {
        await using var reader = await conn.Cmd(
            @"SELECT id, username, full_name, email, role, is_active, approval_status, created_at
              FROM app_users WHERE username = @u AND is_deleted = FALSE")
            .With("@u", username).ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var dto = new UserDto(
            reader.Guid("id"), reader.Str("username"), reader.Str("full_name"),
            reader.Str("email"), reader.Str("role"), reader.Bool("is_active"), reader.Str("approval_status"),
            reader.DtNull("created_at"));
        await reader.CloseAsync();
        return dto with { Verified = await LoadVerified(conn, dto.Username, dto.Role) };
    }

    // Tích xanh: Admin luôn có; tài khoản thường thì tra bảng web_verified_users (web-only).
    private static async Task<bool> LoadVerified(NpgsqlConnection conn, string username, string role)
    {
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            var r = await conn.Cmd(
                "SELECT 1 FROM web_verified_users WHERE username = @u LIMIT 1")
                .With("@u", username).ExecuteScalarAsync();
            return r is not null and not DBNull;
        }
        catch { return false; }
    }

    // Đọc data URL ảnh đại diện (web) của một người dùng; null nếu chưa có (hoặc bảng chưa tồn tại).
    private static async Task<string?> LoadAvatarUrl(NpgsqlConnection conn, Guid userId)
    {
        try
        {
            await using var reader = await conn.Cmd(
                "SELECT image_data_url FROM web_user_avatars WHERE user_id = @id")
                .With("@id", userId).ExecuteReaderAsync();
            if (await reader.ReadAsync() && !reader.IsDBNull(0))
            {
                var value = reader.GetString(0);
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        catch { /* ảnh đại diện là phụ — không để lỗi chặn đăng nhập/hồ sơ */ }
        return null;
    }

    /// <summary>Tạo bảng ảnh đại diện web-only nếu chưa có (gọi best-effort lúc khởi động).</summary>
    public static async Task EnsureAvatarTable(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await EnsureAvatarTableOn(conn, ct);
    }

    private static async Task EnsureAvatarTableOn(NpgsqlConnection conn, CancellationToken ct = default)
    {
        await conn.Cmd("""
            CREATE TABLE IF NOT EXISTS web_user_avatars (
                user_id uuid NOT NULL PRIMARY KEY,
                image_data_url text NOT NULL,
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """)
            .ExecuteNonQueryAsync(ct);
    }
}
