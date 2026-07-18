using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Realtime;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth");

        g.MapPost("/login", async (LoginRequest req, Database db, TokenService tokens, HttpContext http, IHubContext<ChangesHub> hub, PushService push) =>
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

            // Cờ "tắt đăng nhập trên web": chỉ áp cho trình duyệt web, app native vẫn đăng nhập được.
            if (!IsNativeClient(req.Client) && !await IsWebLoginEnabledAsync(conn, user.Username))
                return Results.Json(new { message = "Đăng nhập trên web đã bị tắt cho tài khoản này. Hãy dùng ứng dụng để đăng nhập." }, statusCode: 403);

            user = user with
            {
                AvatarUrl = await LoadAvatarUrl(conn, user.Id),
                Verified = await LoadVerified(conn, user.Username, user.Role),
                IsDiamond = await LoadDiamond(conn, user.Username, user.Role),
                FaceRegistered = await LoadFaceRegistered(conn, user.Username),
                Roles = await ApiHelpers.LoadAllRolesAsync(conn, user.Username, user.Role)
            };
            // Ghi nhận thiết bị đăng nhập ngay để hiện trong "Quản lý thiết bị" + gắn sid vào token
            // (phục vụ thu hồi từ xa). Đăng nhập mới luôn gỡ cờ thu hồi cũ của chính thiết bị đó.
            var isNative = IsNativeClient(req.Client);
            var clientKind = isNative ? "App" : "Web";
            var sid = WebSessionId(req.Sid, user.Username);
            var knownDevice = Convert.ToInt32(await conn.Cmd("SELECT COUNT(*) FROM user_sessions WHERE username=@u AND session_token=@sid")
                .With("@u",user.Username).With("@sid",sid).ExecuteScalarAsync()) > 0;
            if (!knownDevice)
                await push.SendToUserAsync(user.Username,"Đăng nhập trên thiết bị mới",$"Tài khoản vừa đăng nhập từ {clientKind}: {UserAgentOf(http)}",$"security:{sid}","Settings");
            await RegisterDeviceSessionAsync(conn, user.Username, sid, UserAgentOf(http), clientKind);

            // MỖI TÀI KHOẢN CHỈ 1 APP (nhưng cho phép dùng web song song). Chỉ áp khi đăng nhập từ ỨNG
            // DỤNG: thu hồi các phiên APP khác (KHÔNG đụng phiên Web) → app cũ bị đá (request kế nhận 401);
            // xóa token đẩy cũ để cuộc gọi/thông báo không tới app đã bị đá (app hiện tại tự đăng ký lại).
            // Đăng nhập từ WEB thì không đá ai — web + app dùng đồng thời thoải mái.
            if (isNative)
            {
                await conn.Cmd(
                    @"UPDATE user_sessions
                         SET revoked = TRUE, revoked_at = CURRENT_TIMESTAMP, revoked_by = @u, is_active = FALSE
                       WHERE username = @u AND session_token <> @sid AND client_kind = 'App'")
                    .With("@u", user.Username).With("@sid", sid).ExecuteNonQueryAsync();
                await conn.Cmd("DELETE FROM hr_device_tokens WHERE lower(username) = lower(@u)")
                    .With("@u", user.Username).ExecuteNonQueryAsync();

                // Đá NGAY app cũ đang online qua SignalR: phát "kicked" kèm sid MỚI; app nào có sid khác
                // thì tự đăng xuất tức thì (không chờ heartbeat). Web không nghe sự kiện này nên không sao.
                try { await hub.Clients.User(user.Username).SendAsync("kicked", sid); } catch { /* không có kết nối → bỏ qua */ }
            }

            await db.RecordAudit(user.Username, "Đăng nhập", "Auth", user.Username, $"Đăng nhập ({clientKind}).");

            return Results.Ok(new LoginResponse(tokens.CreateToken(user, sid), user));
        }).AllowAnonymous().RequireRateLimiting("login");

        // ── Đăng nhập web bằng QR ──────────────────────────────────────────────────
        // Trình duyệt tạo phiên 5 phút. QR chỉ chứa scan token cho app; poll token độc lập không xuất
        // hiện trong QR nên người chụp được QR không thể tự nhận JWT từ phía trình duyệt.
        g.MapPost("/qr/start", (QrLoginStartRequest req, HttpContext http, QrLoginService qr) =>
        {
            NoStore(http);
            if (!string.Equals(req.ClientMode?.Trim(), "desktop_qr", StringComparison.Ordinal))
                return Results.BadRequest(new { message = "Chế độ đăng nhập QR desktop không hợp lệ." });
            var sid = string.IsNullOrWhiteSpace(req.Sid)
                ? "web:qr:" + Guid.NewGuid().ToString("N")[..24]
                : WebSessionId(req.Sid, "qr");
            var created = qr.Create(sid, UserAgentOf(http));
            if (created is null)
                return Results.Json(new { message = "Máy chủ đang có quá nhiều phiên QR. Vui lòng thử lại sau." }, statusCode: 503);

            return Results.Ok(new QrLoginStartResponse(created.QrCode, created.PollToken, created.ExpiresAt));
        }).AllowAnonymous().RequireRateLimiting("qr-start");

        // App vừa đọc được QR: gắn tên tài khoản vào phiên để web hiển thị ngay, nhưng CHƯA cấp JWT.
        // Chỉ trình duyệt giữ poll token bí mật mới đọc được tên này.
        g.MapPost("/qr/scan", async (QrLoginConfirmRequest req, ClaimsPrincipal principal, Database db, QrLoginService qr, HttpContext http) =>
        {
            NoStore(http);
            var username = principal.Username();
            if (string.IsNullOrWhiteSpace(username)) return Results.Unauthorized();

            await using var conn = await db.OpenAsync(http.RequestAborted);
            var user = await ReadUserByUsername(conn, username);
            if (user is null || user.IsPending || !user.IsActive)
                return Results.Json(new { message = "Tài khoản không thể quét mã đăng nhập QR." }, statusCode: 403);
            if (!await IsWebLoginEnabledAsync(conn, user.Username))
                return Results.Json(new { message = "Đăng nhập trên web đang bị tắt cho tài khoản này." }, statusCode: 403);

            var result = qr.Scan(req.QrCode, user.Username, user.FullName);
            if (result == QrLoginScanResult.InvalidOrExpired)
                return Results.Json(new { message = "Mã QR không hợp lệ, đã hết hạn hoặc đang được tài khoản khác xử lý." }, statusCode: 400);

            if (result == QrLoginScanResult.Scanned)
                await db.RecordAudit(user.Username, "Quét mã đăng nhập QR", "Auth", user.Username,
                    "Đã quét QR web, đang chờ người dùng xác nhận trên ứng dụng.");

            // App không cần nhận lại hồ sơ của chính nó; web lấy tên qua poll token riêng.
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting("qr-confirm");

        // App đã đăng nhập xác nhận danh tính hiện tại. Kiểm tra lại trạng thái tài khoản và cờ cho
        // phép đăng nhập web trước khi chuyển phiên Scanned -> Confirmed.
        g.MapPost("/qr/confirm", async (QrLoginConfirmRequest req, ClaimsPrincipal principal, Database db, QrLoginService qr, HttpContext http) =>
        {
            NoStore(http);
            var username = principal.Username();
            if (string.IsNullOrWhiteSpace(username)) return Results.Unauthorized();

            await using var conn = await db.OpenAsync(http.RequestAborted);
            var user = await ReadUserByUsername(conn, username);
            if (user is null || user.IsPending || !user.IsActive)
                return Results.Json(new { message = "Tài khoản không thể xác nhận đăng nhập QR." }, statusCode: 403);
            if (!await IsWebLoginEnabledAsync(conn, user.Username))
                return Results.Json(new { message = "Đăng nhập trên web đang bị tắt cho tài khoản này." }, statusCode: 403);

            var result = qr.Confirm(req.QrCode, user.Username, user.FullName);
            if (result == QrLoginConfirmResult.InvalidOrExpired)
                return Results.Json(new { message = "Mã QR không hợp lệ, đã hết hạn hoặc đã được sử dụng." }, statusCode: 400);

            if (result == QrLoginConfirmResult.Confirmed)
                await db.RecordAudit(user.Username, "Xác nhận đăng nhập QR", "Auth", user.Username,
                    "Xác nhận trên ứng dụng cho một phiên đăng nhập web.");

            return Results.Ok(new { message = "Đã xác nhận. Trình duyệt sẽ tự động đăng nhập." });
        }).RequireAuthorization().RequireRateLimiting("qr-confirm");

        // Ảnh đại diện được tải riêng đúng một lần sau khi web thấy trạng thái Scanned. Không lưu
        // data URL ảnh trong session RAM và cũng không kéo ảnh từ PostgreSQL ở từng nhịp poll 3 giây.
        g.MapPost("/qr/account", async (QrLoginPollRequest req, Database db, QrLoginService qr, HttpContext http) =>
        {
            NoStore(http);
            var scanned = qr.GetScannedAccount(req.PollToken);
            if (scanned is null) return Results.NotFound(new { message = "Phiên QR không còn chờ xác nhận." });

            await using var conn = await db.OpenAsync(http.RequestAborted);
            var user = await ReadUserByUsername(conn, scanned.Username);
            if (user is null || user.IsPending || !user.IsActive)
                return Results.NotFound(new { message = "Phiên QR không còn chờ xác nhận." });

            return Results.Ok(new { avatarUrl = await LoadAvatarUrl(conn, user.Id) });
        }).AllowAnonymous().RequireRateLimiting("qr-poll");

        // Người dùng từ chối tại màn xác nhận trên điện thoại: web hiển thị kết quả bị từ chối và yêu
        // cầu tạo QR mới. Chỉ đúng tài khoản đã quét mới thay đổi được trạng thái này.
        g.MapPost("/qr/reject", (QrLoginConfirmRequest req, ClaimsPrincipal principal, QrLoginService qr, HttpContext http) =>
        {
            NoStore(http);
            var username = principal.Username();
            if (string.IsNullOrWhiteSpace(username)) return Results.Unauthorized();
            if (!qr.RejectScan(req.QrCode, username))
                return Results.Json(new { message = "Mã QR không còn chờ xác nhận." }, statusCode: 400);
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting("qr-confirm");

        // Poll trạng thái chỉ đọc RAM khi còn chờ. Chỉ lúc app đã xác nhận mới chạm DB một lần để kiểm
        // tra tài khoản, ghi nhận thiết bị web và cấp JWT theo đúng luồng đăng nhập hiện có.
        g.MapPost("/qr/poll", async (QrLoginPollRequest req, Database db, TokenService tokens, PushService push,
            QrLoginService qr, HttpContext http) =>
        {
            NoStore(http);
            var attempt = qr.BeginConsume(req.PollToken);
            if (attempt.State == QrLoginPollState.Pending)
                return Results.Ok(new { status = "pending", expiresAt = attempt.ExpiresAt });
            if (attempt.State == QrLoginPollState.Scanned && attempt.Account is not null)
                return Results.Ok(new
                {
                    status = "scanned",
                    expiresAt = attempt.ExpiresAt,
                    account = new { username = attempt.Account.Username, fullName = attempt.Account.FullName }
                });
            if (attempt.State == QrLoginPollState.Rejected)
                return Results.Ok(new { status = "rejected", expiresAt = attempt.ExpiresAt });
            if (attempt.State == QrLoginPollState.Expired || attempt.Session is null)
                return Results.Ok(new { status = "expired", expiresAt = attempt.ExpiresAt });

            var session = attempt.Session;
            try
            {
                await using var conn = await db.OpenAsync(http.RequestAborted);
                var user = await ReadUserByUsername(conn, session.Username);
                if (user is null || user.IsPending || !user.IsActive)
                {
                    qr.Invalidate(session);
                    return Results.Json(new { message = "Tài khoản không còn đủ điều kiện đăng nhập." }, statusCode: 403);
                }
                if (!await IsWebLoginEnabledAsync(conn, user.Username))
                {
                    qr.Invalidate(session);
                    return Results.Json(new { message = "Đăng nhập trên web đang bị tắt cho tài khoản này." }, statusCode: 403);
                }

                user = user with
                {
                    AvatarUrl = await LoadAvatarUrl(conn, user.Id),
                    FaceRegistered = await LoadFaceRegistered(conn, user.Username)
                };

                if (!session.AlreadyAuthorized)
                {
                    var knownDevice = Convert.ToInt32(await conn.Cmd(
                            "SELECT COUNT(*) FROM user_sessions WHERE username=@u AND session_token=@sid")
                        .With("@u", user.Username).With("@sid", session.BrowserSid).ExecuteScalarAsync()) > 0;
                    if (!knownDevice)
                        await push.SendToUserAsync(user.Username, "Đăng nhập trên thiết bị mới",
                            $"Tài khoản vừa đăng nhập bằng QR từ Web: {session.UserAgent}",
                            $"security:{session.BrowserSid}", "Settings");

                    await RegisterDeviceSessionAsync(conn, user.Username, session.BrowserSid, session.UserAgent, "Web");
                    await db.RecordAudit(user.Username, "Đăng nhập", "Auth", user.Username, "Đăng nhập web bằng QR.");
                }

                var login = new LoginResponse(tokens.CreateToken(user, session.BrowserSid), user);
                qr.CompleteConsume(session, success: true);
                return Results.Ok(new
                {
                    status = "authenticated",
                    expiresAt = attempt.ExpiresAt,
                    token = login.Token,
                    user = login.User
                });
            }
            catch
            {
                // Lỗi DB/mạng tạm thời không làm mất lượt xác nhận; poll sau có thể thử cấp token lại.
                qr.CompleteConsume(session, success: false);
                throw;
            }
        }).AllowAnonymous().RequireRateLimiting("qr-poll");

        // Trình duyệt xác nhận đã nhận/lưu JWT; nếu response authenticated trước đó bị thất lạc thì
        // chưa có ack và poll vẫn có thể lấy lại kết quả một cách idempotent.
        g.MapPost("/qr/ack", (QrLoginPollRequest req, QrLoginService qr, HttpContext http) =>
        {
            NoStore(http);
            qr.Acknowledge(req.PollToken);
            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting("qr-poll");

        // Đóng hộp thoại hoặc bấm tải lại sẽ hủy phiên cũ ngay, tránh app xác nhận nhầm QR không còn
        // hiển thị. Endpoint idempotent và không tiết lộ token có tồn tại hay không.
        g.MapPost("/qr/cancel", (QrLoginCancelRequest req, QrLoginService qr, HttpContext http) =>
        {
            NoStore(http);
            qr.Cancel(req.PollToken);
            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting("qr-poll");

        // Mobile web -> Android app. This protocol is separate from desktop QR: it uses a
        // different prefix, explicit client mode, dedicated endpoints and channel-bound tokens.
        g.MapPost("/app-login/start", (MobileAppLoginStartRequest req, HttpContext http, QrLoginService logins) =>
        {
            NoStore(http);
            if (!IsMobileAppMode(req.ClientMode))
                return Results.BadRequest(new { message = "Chế độ đăng nhập ứng dụng không hợp lệ." });

            var sid = string.IsNullOrWhiteSpace(req.Sid)
                ? "web:app:" + Guid.NewGuid().ToString("N")[..24]
                : WebSessionId(req.Sid, "app-login");
            var created = logins.Create(sid, UserAgentOf(http), WebLoginChannel.MobileApp);
            if (created is null)
                return Results.Json(new { message = "Máy chủ đang có quá nhiều yêu cầu đăng nhập. Vui lòng thử lại sau." }, statusCode: 503);

            return Results.Ok(new MobileAppLoginStartResponse(
                created.QrCode, created.PollToken, created.ExpiresAt));
        }).AllowAnonymous().RequireRateLimiting("qr-start");

        g.MapPost("/app-login/resolve", async (MobileAppLoginCodeRequest req, ClaimsPrincipal principal,
            Database db, QrLoginService logins, HttpContext http) =>
        {
            NoStore(http);
            if (!IsMobileAppMode(req.ClientMode) || !QrLoginService.LooksLikeMobileAppLogin(req.RequestCode))
                return Results.BadRequest(new { message = "Yêu cầu đăng nhập ứng dụng không hợp lệ." });

            var username = principal.Username();
            if (string.IsNullOrWhiteSpace(username)) return Results.Unauthorized();
            await using var conn = await db.OpenAsync(http.RequestAborted);
            var user = await ReadUserByUsername(conn, username);
            if (user is null || user.IsPending || !user.IsActive)
                return Results.Json(new { message = "Tài khoản không thể xác nhận đăng nhập web." }, statusCode: 403);
            if (!await IsWebLoginEnabledAsync(conn, user.Username))
                return Results.Json(new { message = "Đăng nhập trên web đang bị tắt cho tài khoản này." }, statusCode: 403);

            var scan = logins.ScanDetailed(req.RequestCode, user.Username, user.FullName, WebLoginChannel.MobileApp);
            if (scan.Result == QrLoginScanResult.InvalidOrExpired || scan.ExpiresAt is null)
                return Results.BadRequest(new { message = "Yêu cầu đăng nhập không còn hiệu lực hoặc đã được sử dụng." });

            if (scan.Result == QrLoginScanResult.Scanned)
                await db.RecordAudit(user.Username, "Mở yêu cầu đăng nhập từ web mobile", "Auth", user.Username,
                    "Ứng dụng Nhân sự đã nhận yêu cầu đăng nhập trực tiếp từ trình duyệt mobile.");

            var displayName = string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName.Trim();
            return Results.Ok(new MobileAppLoginChallengeResponse(
                req.RequestCode,
                "Xác nhận đăng nhập web?",
                $"Tài khoản {displayName} sẽ đăng nhập trên trình duyệt mobile vừa mở ứng dụng. Chỉ xác nhận nếu chính bạn vừa bấm Đăng nhập bằng ứng dụng Nhân sự.",
                scan.ExpiresAt.Value));
        }).RequireAuthorization().RequireRateLimiting("qr-confirm");

        g.MapPost("/app-login/confirm", async (MobileAppLoginCodeRequest req, ClaimsPrincipal principal,
            Database db, QrLoginService logins, HttpContext http) =>
        {
            NoStore(http);
            if (!IsMobileAppMode(req.ClientMode))
                return Results.BadRequest(new { message = "Chế độ đăng nhập ứng dụng không hợp lệ." });
            var username = principal.Username();
            if (string.IsNullOrWhiteSpace(username)) return Results.Unauthorized();
            await using var conn = await db.OpenAsync(http.RequestAborted);
            var user = await ReadUserByUsername(conn, username);
            if (user is null || user.IsPending || !user.IsActive)
                return Results.Json(new { message = "Tài khoản không thể xác nhận đăng nhập web." }, statusCode: 403);
            if (!await IsWebLoginEnabledAsync(conn, user.Username))
                return Results.Json(new { message = "Đăng nhập trên web đang bị tắt cho tài khoản này." }, statusCode: 403);

            var result = logins.Confirm(req.RequestCode, user.Username, user.FullName, WebLoginChannel.MobileApp);
            if (result == QrLoginConfirmResult.InvalidOrExpired)
                return Results.BadRequest(new { message = "Yêu cầu đăng nhập không còn hiệu lực hoặc đã được sử dụng." });
            if (result == QrLoginConfirmResult.Confirmed)
                await db.RecordAudit(user.Username, "Xác nhận đăng nhập web mobile", "Auth", user.Username,
                    "Đã xác nhận trong ứng dụng Nhân sự cho phiên đăng nhập web mobile.");
            return Results.Ok(new { message = "Đã xác nhận. Trình duyệt mobile sẽ tự động đăng nhập." });
        }).RequireAuthorization().RequireRateLimiting("qr-confirm");

        g.MapPost("/app-login/reject", (MobileAppLoginCodeRequest req, ClaimsPrincipal principal,
            QrLoginService logins, HttpContext http) =>
        {
            NoStore(http);
            var username = principal.Username();
            if (!IsMobileAppMode(req.ClientMode) || string.IsNullOrWhiteSpace(username))
                return Results.BadRequest(new { message = "Yêu cầu đăng nhập ứng dụng không hợp lệ." });
            if (!logins.RejectScan(req.RequestCode, username, WebLoginChannel.MobileApp))
                return Results.BadRequest(new { message = "Yêu cầu đăng nhập không còn chờ xác nhận." });
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting("qr-confirm");

        g.MapPost("/app-login/poll", async (MobileAppLoginPollRequest req, Database db, TokenService tokens,
            PushService push, QrLoginService logins, HttpContext http) =>
        {
            NoStore(http);
            if (!IsMobileAppMode(req.ClientMode))
                return Results.BadRequest(new { message = "Chế độ đăng nhập ứng dụng không hợp lệ." });
            var attempt = logins.BeginConsume(req.PollToken, WebLoginChannel.MobileApp);
            if (attempt.State == QrLoginPollState.Pending)
                return Results.Ok(new { status = "pending", expiresAt = attempt.ExpiresAt });
            if (attempt.State == QrLoginPollState.Scanned)
                return Results.Ok(new { status = "opened", expiresAt = attempt.ExpiresAt });
            if (attempt.State == QrLoginPollState.Rejected)
                return Results.Ok(new { status = "rejected", expiresAt = attempt.ExpiresAt });
            if (attempt.State == QrLoginPollState.Expired || attempt.Session is null)
                return Results.Ok(new { status = "expired", expiresAt = attempt.ExpiresAt });

            var session = attempt.Session;
            try
            {
                await using var conn = await db.OpenAsync(http.RequestAborted);
                var user = await ReadUserByUsername(conn, session.Username);
                if (user is null || user.IsPending || !user.IsActive)
                {
                    logins.Invalidate(session);
                    return Results.Json(new { message = "Tài khoản không còn đủ điều kiện đăng nhập." }, statusCode: 403);
                }
                if (!await IsWebLoginEnabledAsync(conn, user.Username))
                {
                    logins.Invalidate(session);
                    return Results.Json(new { message = "Đăng nhập trên web đang bị tắt cho tài khoản này." }, statusCode: 403);
                }

                user = user with
                {
                    AvatarUrl = await LoadAvatarUrl(conn, user.Id),
                    FaceRegistered = await LoadFaceRegistered(conn, user.Username)
                };
                if (!session.AlreadyAuthorized)
                {
                    var knownDevice = Convert.ToInt32(await conn.Cmd(
                            "SELECT COUNT(*) FROM user_sessions WHERE username=@u AND session_token=@sid")
                        .With("@u", user.Username).With("@sid", session.BrowserSid).ExecuteScalarAsync()) > 0;
                    if (!knownDevice)
                        await push.SendToUserAsync(user.Username, "Đăng nhập trên thiết bị mới",
                            $"Tài khoản vừa đăng nhập bằng ứng dụng Nhân sự từ Web mobile: {session.UserAgent}",
                            $"security:{session.BrowserSid}", "Settings");

                    await RegisterDeviceSessionAsync(conn, user.Username, session.BrowserSid, session.UserAgent, "Web");
                    await db.RecordAudit(user.Username, "Đăng nhập", "Auth", user.Username,
                        "Đăng nhập web mobile bằng ứng dụng Nhân sự.");
                }

                var login = new LoginResponse(tokens.CreateToken(user, session.BrowserSid), user);
                logins.CompleteConsume(session, success: true);
                return Results.Ok(new
                {
                    status = "authenticated",
                    expiresAt = attempt.ExpiresAt,
                    token = login.Token,
                    user = login.User
                });
            }
            catch
            {
                logins.CompleteConsume(session, success: false);
                throw;
            }
        }).AllowAnonymous().RequireRateLimiting("qr-poll");

        g.MapPost("/app-login/ack", (MobileAppLoginPollRequest req, QrLoginService logins, HttpContext http) =>
        {
            NoStore(http);
            if (IsMobileAppMode(req.ClientMode))
                logins.Acknowledge(req.PollToken, WebLoginChannel.MobileApp);
            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting("qr-poll");

        g.MapPost("/app-login/cancel", (MobileAppLoginPollRequest req, QrLoginService logins, HttpContext http) =>
        {
            NoStore(http);
            if (IsMobileAppMode(req.ClientMode))
                logins.Cancel(req.PollToken, WebLoginChannel.MobileApp);
            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting("qr-poll");

        // Quên mật khẩu bằng khuôn mặt: client nhập username + mật khẩu mới, quét loạt ảnh như
        // chấm công. Server CHỈ so khớp với mẫu đã đăng ký của username đó (1:1 theo tài khoản),
        // không quét toàn bộ nhân viên như luồng đăng nhập khuôn mặt cũ.
        g.MapPost("/forgot-password-face", async (FacePasswordResetRequest req, Database db, IFaceEngine engine, FieldCipher cipher) =>
        {
            var username = (req?.Username ?? "").Trim();
            var newPass = (req?.NewPassword ?? "").Trim();
            if (string.IsNullOrWhiteSpace(username))
                return Results.BadRequest(new { message = "Vui lòng nhập tên đăng nhập." });
            if (newPass.Length < 6)
                return Results.BadRequest(new { message = "Mật khẩu mới cần ít nhất 6 ký tự." });
            if (req?.Images is null || req.Images.Count == 0)
                return Results.BadRequest(new { message = "Thiếu ảnh khuôn mặt." });
            if (req.Images.Count > PayloadLimits.MaxImagesPerRequest)
                return Results.BadRequest(new { message = $"Tối đa {PayloadLimits.MaxImagesPerRequest} ảnh mỗi yêu cầu." });
            if (req.Images.Any(img => !PayloadLimits.TryDecodeImage(img, out _)))
                return Results.BadRequest(new { message = $"Mỗi ảnh phải nhỏ hơn {PayloadLimits.MaxImageBytes / 1024 / 1024} MB." });

            await using var conn = await db.OpenAsync();
            var user = await ReadUserByUsername(conn, username);
            if (user is null)
                return Results.Json(new { message = "Không tìm thấy tài khoản." }, statusCode: 404);
            if (user.IsPending)
                return Results.Json(new { message = "Tài khoản đang chờ quản trị viên phê duyệt." }, statusCode: 403);
            if (!user.IsActive)
                return Results.Json(new { message = "Tài khoản đã bị khóa. Liên hệ quản trị viên." }, statusCode: 403);

            // Lấy sẵn mẫu của đúng username. Đây là điểm tối ưu và đúng nghiệp vụ: reset mật khẩu
            // là xác minh 1:1, không nhận diện 1:N.
            var enrolled = new List<float[]>();
            await using (var r = await conn.Cmd(
                "SELECT embedding FROM cham_cong_face WHERE username = @u ORDER BY created_at DESC")
                .With("@u", user.Username).ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                    enrolled.Add(cipher.DecryptEmbedding((byte[])r["embedding"]));
            }
            if (enrolled.Count == 0)
                return Results.Json(new { message = "Tài khoản này chưa đăng ký khuôn mặt chấm công." }, statusCode: 400);

            // 1) Lấy mọi khung CÓ MẶT, xếp theo chất lượng giảm dần để chọn khung tốt nhất.
            var candidates = new List<(byte[] Bytes, FaceFrameQuality Q)>();
            foreach (var img in req.Images)
            {
                if (!TryDecodeImage(img, out var bytes)) continue;
                if (engine.AssessFrame(bytes) is not { FaceFound: true } q) continue;
                candidates.Add((bytes, q));
            }
            if (candidates.Count == 0)
                return Results.Json(new { message = "Không thấy khuôn mặt rõ ràng. Hãy nhìn thẳng vào camera." }, statusCode: 400);

            candidates.Sort((a, b) => b.Q.Score.CompareTo(a.Q.Score));
            var bestBytes = candidates[0].Bytes;

            // 2) Chống giả mạo: chấm liveness trên vài khung tốt nhất, qua nếu CÓ khung đạt.
            const int livenessFramesToCheck = 5;
            double bestLive = 0;
            foreach (var c in candidates.Take(livenessFramesToCheck))
            {
                bestLive = Math.Max(bestLive, engine.LivenessProbability(c.Bytes));
                if (bestLive >= engine.LivenessThreshold) break;
            }
            if (bestLive < engine.LivenessThreshold)
                return Results.Json(new { message = "Nghi ngờ giả mạo. Hãy nhìn trực tiếp vào camera, không dùng ảnh/màn hình." }, statusCode: 401);

            var probe = engine.ExtractEmbedding(bestBytes);
            if (probe is null)
                return Results.Json(new { message = "Không trích được đặc trưng khuôn mặt. Vui lòng thử lại." }, statusCode: 400);

            // 3) So 1:1 với các mẫu của CHÍNH tài khoản này.
            double bestSim = 0;
            foreach (var emb in enrolled)
            {
                var sim = engine.Compare(probe, emb);
                if (sim > bestSim) bestSim = sim;
            }
            if (bestSim < engine.MatchThreshold)
                return Results.Json(new { message = "Khuôn mặt không khớp với tài khoản này. Vui lòng thử lại." }, statusCode: 401);

            await conn.Cmd("UPDATE app_users SET password_hash = @ph WHERE username = @u AND is_deleted = FALSE")
                .With("@ph", PasswordHasher.Hash(newPass)).With("@u", user.Username).ExecuteNonQueryAsync();
            await db.RecordAudit(user.Username, "Quên mật khẩu (khuôn mặt)", "Auth", user.Username,
                $"Đặt lại mật khẩu bằng xác thực khuôn mặt 1:1 (độ khớp {bestSim:0.000}).");

            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting("face-reset");

        // Khôi phục mật khẩu bằng MÃ do admin cấp (thay cho reset khuôn mặt đã tắt ngoài Development).
        // "Recovery code + xác nhận admin": chính admin là bên cấp mã (một lần, hết hạn 7 ngày) → đây là
        // bước admin chấp thuận. Thành công thì thu hồi mọi phiên đang mở để buộc đăng nhập lại.
        g.MapPost("/reset-with-recovery-code", async (RecoveryResetRequest req, Database db) =>
        {
            var username = (req?.Username ?? "").Trim();
            var code = RecoveryCodes.Normalize(req?.Code);
            var newPass = (req?.NewPassword ?? "").Trim();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(code))
                return Results.BadRequest(new { message = "Vui lòng nhập tên đăng nhập và mã khôi phục." });
            if (newPass.Length < 6)
                return Results.BadRequest(new { message = "Mật khẩu mới cần ít nhất 6 ký tự." });

            await using var conn = await db.OpenAsync();
            var user = await ReadUserByUsername(conn, username);

            // Không tiết lộ tài khoản có tồn tại/khóa/hết hạn hay không: chỉ 1 thông báo lỗi chung.
            long? matchedId = null;
            if (user is not null && user.IsActive && !user.IsPending)
            {
                await using var r = await conn.Cmd(
                    @"SELECT id, code_hash FROM password_recovery_codes
                      WHERE username = @u AND used_at IS NULL
                        AND (expires_at IS NULL OR expires_at > CURRENT_TIMESTAMP)
                      ORDER BY created_at DESC")
                    .With("@u", user.Username).ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    if (PasswordHasher.Verify(code, r.Str("code_hash")))
                    {
                        matchedId = r.GetInt64(0);
                        break;
                    }
                }
            }

            if (matchedId is null)
                return Results.Json(new { message = "Mã khôi phục không đúng hoặc đã hết hạn." }, statusCode: 400);

            await conn.Cmd("UPDATE app_users SET password_hash = @ph WHERE username = @u AND is_deleted = FALSE")
                .With("@ph", PasswordHasher.Hash(newPass)).With("@u", user!.Username).ExecuteNonQueryAsync();
            await conn.Cmd("UPDATE password_recovery_codes SET used_at = CURRENT_TIMESTAMP WHERE id = @id")
                .With("@id", matchedId.Value).ExecuteNonQueryAsync();
            // Thu hồi mọi phiên để mật khẩu cũ (kể cả trên thiết bị khác) không còn dùng được.
            await conn.Cmd(
                @"UPDATE user_sessions SET revoked = TRUE, revoked_at = CURRENT_TIMESTAMP, revoked_by = 'recovery', is_active = FALSE
                  WHERE username = @u AND revoked = FALSE")
                .With("@u", user.Username).ExecuteNonQueryAsync();
            await db.RecordAudit(user.Username, "Khôi phục mật khẩu bằng mã", "Auth", user.Username,
                "Đặt lại mật khẩu bằng mã khôi phục do admin cấp.");

            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting("face-reset");

        g.MapGet("/me", async (ClaimsPrincipal principal, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var user = await ReadUserByUsername(conn, principal.Username());
            if (user is null) return Results.Unauthorized();
            return Results.Ok(user with
            {
                AvatarUrl = await LoadAvatarUrl(conn, user.Id),
                IsDiamond = await LoadDiamond(conn, user.Username, user.Role),
                FaceRegistered = await LoadFaceRegistered(conn, user.Username)
            });
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
            if (!PayloadLimits.TryDecodeImage(dataUrl, out _))
                return Results.BadRequest(new { message = $"Ảnh đại diện phải nhỏ hơn {PayloadLimits.MaxImageBytes / 1024 / 1024} MB." });
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

        // App dùng endpoint chỉ-xác-minh này cho luồng "Quên mã bảo mật ứng dụng". Không đăng nhập
        // lại (tránh đá phiên app hiện tại), không đổi mật khẩu và không bao giờ trả password hash.
        g.MapPost("/verify-password", async (VerifyPasswordRequest req, ClaimsPrincipal principal, Database db, HttpContext http) =>
        {
            NoStore(http);
            if (string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { message = "Vui lòng nhập mật khẩu tài khoản." });

            var username = principal.Username();
            await using var conn = await db.OpenAsync(http.RequestAborted);
            var hash = Convert.ToString(await conn.Cmd(
                    "SELECT password_hash FROM app_users WHERE username = @u AND is_deleted = FALSE AND is_active = TRUE LIMIT 1")
                .With("@u", username).ExecuteScalarAsync()) ?? "";
            if (string.IsNullOrEmpty(hash)) return Results.Unauthorized();
            if (!PasswordHasher.Verify(req.Password, hash))
                return Results.Json(new { message = "Mật khẩu tài khoản không đúng." }, statusCode: 400);

            await db.RecordAudit(username, "Xác minh bảo mật", "User", username,
                "Xác minh mật khẩu để khôi phục mã bảo mật ứng dụng.");
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting("reauth");

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
                      last_seen = CURRENT_TIMESTAMP,
                      is_active = TRUE,
                      ended_at = NULL,
                      end_reason = '',
                      -- GIU NGUYEN machine_name/client_kind da dat luc login (App hay Web); heartbeat
                      -- KHONG ghi de ve Web nua, neu khong phien App se bi hieu nham thanh Web va
                      -- rang buoc 1-app-moi-tai-khoan khong da dung thiet bi.
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

        // ── Quản lý thiết bị đăng nhập ──────────────────────────────────────────────
        // Liệt kê các thiết bị/phiên web của chính người dùng (đánh dấu thiết bị hiện tại).
        g.MapGet("/devices", async (ClaimsPrincipal principal, Database db) =>
        {
            var username = principal.Username();
            if (string.IsNullOrWhiteSpace(username)) return Results.Unauthorized();
            var currentSid = principal.FindFirst("sid")?.Value ?? "";

            await using var conn = await db.OpenAsync();
            var list = new List<DeviceDto>();
            await using var r = await conn.Cmd(
                @"SELECT session_token, machine_name, client_kind, user_agent, started_at, last_seen, is_active, revoked
                  FROM user_sessions
                  WHERE username = @u
                  ORDER BY revoked ASC, last_seen DESC")
                .With("@u", username).ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var sid = r.Str("session_token");
                list.Add(new DeviceDto(sid, r.Str("machine_name"), r.Str("client_kind"), r.Str("user_agent"),
                    r.DtNull("started_at"), r.DtNull("last_seen"), r.Bool("is_active"), r.Bool("revoked"),
                    string.Equals(sid, currentSid, StringComparison.Ordinal)));
            }
            return Results.Ok(list);
        }).RequireAuthorization();

        // Thu hồi (đăng xuất từ xa) một thiết bị của chính mình. Middleware sẽ trả 401 cho token có
        // sid này ở request kế tiếp → thiết bị đó tự đăng xuất.
        g.MapPost("/devices/{sid}/revoke", async (string sid, ClaimsPrincipal principal, Database db) =>
        {
            var username = principal.Username();
            if (string.IsNullOrWhiteSpace(username)) return Results.Unauthorized();

            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd(
                @"UPDATE user_sessions
                     SET revoked = TRUE, revoked_at = CURRENT_TIMESTAMP, revoked_by = @by,
                         is_active = FALSE, ended_at = CURRENT_TIMESTAMP, end_reason = 'Thu hồi thiết bị'
                   WHERE session_token = @t AND username = @u")
                .With("@t", sid).With("@u", username).With("@by", username).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound(new { message = "Không tìm thấy thiết bị." });
            await db.RecordAudit(username, "Thu hồi thiết bị", "Auth", username, $"Thu hồi phiên đăng nhập từ xa (sid={sid}).");
            return Results.NoContent();
        }).RequireAuthorization();

        g.MapPost("/devices/revoke-all", async(ClaimsPrincipal principal,Database db)=>{
            var username=principal.Username();await using var conn=await db.OpenAsync();await conn.Cmd("""
              UPDATE user_sessions SET revoked=TRUE,revoked_at=CURRENT_TIMESTAMP,revoked_by=@u,is_active=FALSE,ended_at=CURRENT_TIMESTAMP,end_reason='Đăng xuất tất cả thiết bị'
              WHERE username=@u AND revoked=FALSE
              """).With("@u",username).ExecuteNonQueryAsync();await conn.Cmd("DELETE FROM hr_device_tokens WHERE lower(username)=lower(@u)").With("@u",username).ExecuteNonQueryAsync();await db.RecordAudit(username,"Đăng xuất tất cả thiết bị","Auth",username,"Thu hồi toàn bộ phiên đăng nhập.");return Results.NoContent();}).RequireAuthorization();

        // ── Cài đặt đăng nhập (bật/tắt đăng nhập trên web cho chính tài khoản) ───────────
        g.MapGet("/account-settings", async (ClaimsPrincipal principal, Database db) =>
        {
            var username = principal.Username();
            if (string.IsNullOrWhiteSpace(username)) return Results.Unauthorized();
            await using var conn = await db.OpenAsync();
            var enabled = await IsWebLoginEnabledAsync(conn, username);
            return Results.Ok(new AccountLoginSettingsDto(enabled));
        }).RequireAuthorization();

        g.MapPut("/account-settings", async (AccountLoginSettingsPatch req, ClaimsPrincipal principal, Database db) =>
        {
            var username = principal.Username();
            if (string.IsNullOrWhiteSpace(username)) return Results.Unauthorized();
            await using var conn = await db.OpenAsync();
            await EnsureLoginSettingsTableAsync(conn);
            await conn.Cmd(
                @"INSERT INTO web_login_settings (username, web_login_enabled, updated_at)
                  VALUES (@u, @e, CURRENT_TIMESTAMP)
                  ON CONFLICT (username) DO UPDATE SET
                      web_login_enabled = EXCLUDED.web_login_enabled,
                      updated_at = CURRENT_TIMESTAMP;")
                .With("@u", username).With("@e", req.WebLoginEnabled).ExecuteNonQueryAsync();
            await db.RecordAudit(username, req.WebLoginEnabled ? "Bật đăng nhập web" : "Tắt đăng nhập web",
                "Auth", username, "Cập nhật cài đặt đăng nhập web từ ứng dụng.");
            return Results.Ok(new AccountLoginSettingsDto(req.WebLoginEnabled));
        }).RequireAuthorization();
    }

    // Xác định client native (app) — client này KHÔNG bị chặn bởi cờ tắt đăng nhập web.
    private static bool IsNativeClient(string? client)
    {
        if (string.IsNullOrWhiteSpace(client)) return false;
        var c = client.Trim().ToLowerInvariant();
        return c is "apk" or "android" or "native" or "app";
    }

    private static bool IsMobileAppMode(string? clientMode)
        => string.Equals(clientMode?.Trim(), "mobile_app", StringComparison.Ordinal);

    // Tạo bảng web-only (không đụng schema dùng chung với app desktop) lưu cờ đăng nhập web mỗi tài khoản.
    private static async Task EnsureLoginSettingsTableAsync(NpgsqlConnection conn)
    {
        await conn.Cmd(
            @"CREATE TABLE IF NOT EXISTS web_login_settings (
                  username varchar(150) PRIMARY KEY,
                  web_login_enabled boolean NOT NULL DEFAULT TRUE,
                  updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP);")
            .ExecuteNonQueryAsync();
    }

    // Mặc định BẬT (chưa có bản ghi = cho phép đăng nhập web); chỉ trả FALSE khi tài khoản đã tắt.
    private static async Task<bool> IsWebLoginEnabledAsync(NpgsqlConnection conn, string username)
    {
        await EnsureLoginSettingsTableAsync(conn);
        var value = await conn.Cmd(
            "SELECT web_login_enabled FROM web_login_settings WHERE username = @u LIMIT 1")
            .With("@u", username).ExecuteScalarAsync();
        return value is not bool b || b;
    }

    // User-Agent của yêu cầu (cắt ngắn để tránh chuỗi bất thường quá dài).
    private static string UserAgentOf(HttpContext http)
    {
        var ua = http.Request.Headers.UserAgent.ToString();
        return ua.Length > 400 ? ua[..400] : ua;
    }

    private static void NoStore(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        http.Response.Headers.Pragma = "no-cache";
    }

    // Ghi/nâng cấp một phiên thiết bị web khi đăng nhập (hiện ngay trong "Quản lý thiết bị") và
    // gỡ mọi cờ thu hồi cũ của đúng thiết bị đó (đăng nhập lại = tin cậy lại thiết bị).
    private static async Task RegisterDeviceSessionAsync(NpgsqlConnection conn, string username, string sid, string userAgent, string clientKind)
    {
        // machine_name để hiển thị trong "Quản lý thiết bị"; client_kind để phân biệt App/Web (dùng cho
        // ràng buộc "1 app/tài khoản" — web không bị đá).
        var machine = clientKind == "App" ? "Ứng dụng" : "Web";
        await conn.Cmd(
            @"INSERT INTO user_sessions
                  (session_token, username, machine_name, user_agent, started_at, last_seen, is_active, client_kind, revoked, revoked_at, revoked_by)
              VALUES (@t, @u, @m, @ua, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, TRUE, @k, FALSE, NULL, '')
              ON CONFLICT (session_token) DO UPDATE SET
                  username = EXCLUDED.username,
                  machine_name = @m,
                  user_agent = EXCLUDED.user_agent,
                  last_seen = CURRENT_TIMESTAMP,
                  started_at = CURRENT_TIMESTAMP,
                  is_active = TRUE,
                  ended_at = NULL,
                  end_reason = '',
                  client_kind = @k,
                  revoked = FALSE,
                  revoked_at = NULL,
                  revoked_by = '';")
            .With("@t", sid).With("@u", username).With("@ua", userAgent).With("@m", machine).With("@k", clientKind)
            .ExecuteNonQueryAsync();
    }

    /// <summary>Giải mã ảnh base64 (chấp nhận cả tiền tố data URL "data:image/...;base64,").</summary>
    private static bool TryDecodeImage(string? b64, out byte[] bytes)
        => PayloadLimits.TryDecodeImage(b64, out bytes);

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
        return dto with
        {
            Verified = await LoadVerified(conn, dto.Username, dto.Role),
            IsDiamond = await LoadDiamond(conn, dto.Username, dto.Role),
            Roles = await ApiHelpers.LoadAllRolesAsync(conn, dto.Username, dto.Role)
        };
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

    private static async Task<bool> LoadDiamond(NpgsqlConnection conn, string username, string role)
    {
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            var r = await conn.Cmd(
                "SELECT 1 FROM web_diamond_members WHERE username = @u LIMIT 1")
                .With("@u", username).ExecuteScalarAsync();
            return r is not null and not DBNull;
        }
        catch { return false; }
    }

    // Đã đăng ký khuôn mặt chưa (có ít nhất 1 mẫu trong cham_cong_face) → app hiện/ẩn banner nhắc.
    private static async Task<bool> LoadFaceRegistered(NpgsqlConnection conn, string username)
    {
        try
        {
            var r = await conn.Cmd(
                "SELECT 1 FROM cham_cong_face WHERE username = @u LIMIT 1")
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
