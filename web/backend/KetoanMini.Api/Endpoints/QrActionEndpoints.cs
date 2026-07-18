using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Realtime;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Giao thức QR do server điều khiển. APK chỉ biết trình bày nội dung, gửi một action ID về server
/// hoặc mở URL HTTPS đã được allowlist; nghiệp vụ cụ thể không còn bị hardcode trong APK.
/// </summary>
public static class QrActionEndpoints
{
    public static void MapQrActions(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/qr")
            .RequireAuthorization()
            .RequireRateLimiting("qr-action")
            // Chặn trước bước JSON binding để body khổng lồ không chiếm RAM dù người gọi đã đăng nhập.
            .WithMetadata(new RequestSizeLimitAttribute(PayloadLimits.MaxQrActionBodyBytes));

        g.MapPost("/resolve", async (QrResolveRequest req, ClaimsPrincipal principal, Database db,
            QrLoginService qrLogin, QrActionTokenService tokens, QrConfiguredActionRegistry configured,
            HttpContext http) =>
        {
            NoStore(http);
            if (req is null || req.ProtocolVersion != QrActionProtocol.Version ||
                string.IsNullOrWhiteSpace(req.Value) || req.Value.Length > QrActionProtocol.MaxQrValueLength ||
                req.Capabilities is { Count: > 16 } ||
                req.Capabilities?.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 64) == true)
                return Results.BadRequest(new { message = "Nội dung hoặc phiên bản giao thức QR không hợp lệ." });

            var username = principal.Username();
            var subjectId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var sid = principal.FindFirstValue("sid");
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(subjectId) ||
                string.IsNullOrWhiteSpace(sid)) return Results.Unauthorized();
            await using var conn = await db.OpenAsync(http.RequestAborted);
            var user = await ReadUserByUsername(conn, username);
            if (user is null || !Guid.TryParse(subjectId, out var claimUserId) || claimUserId != user.Id)
                return Results.Unauthorized();
            if (user.IsPending || !user.IsActive)
                return Results.Json(new { message = "Tài khoản không thể sử dụng chức năng quét QR." }, statusCode: 403);

            var value = req.Value.Trim();
            if (QrLoginService.LooksLikeLoginQr(value))
            {
                if (!Supports(req, QrActionProtocol.ServerDecision))
                    return Results.Ok(Message("Cần cập nhật ứng dụng", "Ứng dụng chưa hỗ trợ xác nhận thao tác QR từ máy chủ.", "warning"));
                if (!await IsWebLoginEnabledAsync(conn, user.Username))
                    return Results.Json(new { message = "Đăng nhập trên web đang bị tắt cho tài khoản này." }, statusCode: 403);

                var scan = qrLogin.ScanDetailed(value, user.Username, user.FullName);
                if (scan.Result == QrLoginScanResult.InvalidOrExpired || scan.ExpiresAt is null)
                    return Results.BadRequest(new { message = "Mã QR đăng nhập không hợp lệ, đã hết hạn hoặc đang được tài khoản khác xử lý." });

                if (scan.Result == QrLoginScanResult.Scanned)
                    await db.RecordAudit(user.Username, "Quét mã đăng nhập QR", "Auth", user.Username,
                        "Đã quét QR web qua giao thức QR server-driven, đang chờ xác nhận trên ứng dụng.");

                var ticket = tokens.CreateUntil(
                    QrActionProtocol.WebLoginHandler,
                    value,
                    subjectId,
                    sid,
                    [QrActionProtocol.ConfirmAction, QrActionProtocol.RejectAction],
                    scan.ExpiresAt.Value);

                return Results.Ok(new QrActionEnvelope(
                    QrActionProtocol.Version,
                    new QrPresentationDto(
                        "Xác nhận đăng nhập web?",
                        $"Tài khoản {(user.FullName.Trim().Length > 0 ? user.FullName : user.Username)} sẽ đăng nhập trên trình duyệt vừa hiển thị mã QR. Chỉ xác nhận nếu chính bạn vừa mở mã này trên máy tính."),
                    [
                        new QrClientActionDto(QrActionProtocol.ConfirmAction, QrActionProtocol.ServerDecision,
                            "Đăng nhập", "primary"),
                        new QrClientActionDto(QrActionProtocol.RejectAction, QrActionProtocol.ServerDecision,
                            "Từ chối", "danger", CloseOnSelect: true)
                    ],
                    DecisionToken: ticket.Value,
                    DismissActionId: QrActionProtocol.RejectAction,
                    ExpiresAt: ticket.ExpiresAt));
            }

            // Phiếu chi: người nhận quét mã để ký nhận đã cầm tiền. Chỉ đúng người ghi trên phiếu mới
            // thấy nội dung; người khác quét chỉ nhận thông báo từ chối (không lộ số tiền/tên ai).
            if (PayoutVoucherEndpoints.LooksLikePayoutQr(value))
            {
                if (!Supports(req, QrActionProtocol.ServerDecision))
                    return Results.Ok(Message("Cần cập nhật ứng dụng", "Ứng dụng chưa hỗ trợ xác nhận thao tác QR từ máy chủ.", "warning"));

                var scan = await PayoutVoucherEndpoints.LookupForScanAsync(conn, value, user.Username);
                if (!scan.Ok) return Results.Ok(Message(scan.Title, scan.Message, "warning"));

                var ticket = tokens.CreateUntil(
                    PayoutVoucherEndpoints.QrHandler, value, subjectId, sid,
                    [PayoutVoucherEndpoints.QrConfirmAction], scan.ExpiresAt);

                var detail = string.IsNullOrWhiteSpace(scan.Reason) ? scan.CategoryName : scan.Reason;
                return Results.Ok(new QrActionEnvelope(
                    QrActionProtocol.Version,
                    new QrPresentationDto(
                        scan.Title,
                        $"Phiếu chi {scan.VoucherNo} · {scan.Amount:#,##0} ₫\n{detail}\n\nChỉ bấm xác nhận khi bạn ĐÃ THỰC SỰ nhận đủ số tiền này từ phòng kế toán."),
                    [
                        new QrClientActionDto(PayoutVoucherEndpoints.QrConfirmAction, QrActionProtocol.ServerDecision,
                            "Đã nhận đủ tiền", "primary"),
                        new QrClientActionDto("dismiss", QrActionProtocol.Dismiss, "Chưa nhận", "secondary",
                            CloseOnSelect: true)
                    ],
                    DecisionToken: ticket.Value,
                    DismissActionId: "dismiss",
                    ExpiresAt: ticket.ExpiresAt));
            }

            var configuredAction = configured.Match(value);
            if (configuredAction is null)
                // Không có nghiệp vụ nào khớp: nhường lại cho ứng dụng tự đọc nội dung mã. Cố tình KHÔNG
                // gửi kèm nội dung đã bóc tách — server không cần biết mã lạ chứa gì.
                return Results.Ok(Message("Chưa hỗ trợ mã QR này",
                    "Máy chủ chưa cấu hình hành động cho mã QR vừa quét.", "warning") with { Unhandled = true });

            var title = QrConfiguredActionRegistry.PlainText(configuredAction.Title, "Thông tin mã QR", 120);
            var message = QrConfiguredActionRegistry.PlainText(configuredAction.Message, "", 1_000);
            if (string.Equals(configuredAction.Kind, "message", StringComparison.OrdinalIgnoreCase))
                return Results.Ok(Message(title, message.Length > 0 ? message : "Đã nhận mã QR."));

            if (string.Equals(configuredAction.Kind, "open_https_url", StringComparison.OrdinalIgnoreCase))
            {
                if (!Supports(req, QrActionProtocol.OpenHttpsUrl) ||
                    !configured.TryGetAllowedHttpsUrl(configuredAction, out var safeUrl))
                    return Results.Ok(Message("Không thể mở liên kết", "Tính năng QR này chưa được cấu hình HTTPS an toàn.", "warning"));

                return Results.Ok(new QrActionEnvelope(
                    QrActionProtocol.Version,
                    new QrPresentationDto(title, message.Length > 0 ? message : "Bạn có muốn mở trang do máy chủ cung cấp không?"),
                    [
                        new QrClientActionDto("open", QrActionProtocol.OpenHttpsUrl,
                            QrConfiguredActionRegistry.PlainText(configuredAction.OpenLabel, "Mở", 40),
                            "primary", safeUrl, CloseOnSelect: true),
                        new QrClientActionDto("dismiss", QrActionProtocol.Dismiss,
                            QrConfiguredActionRegistry.PlainText(configuredAction.CancelLabel, "Đóng", 40),
                            "secondary", CloseOnSelect: true)
                    ],
                    DismissActionId: "dismiss"));
            }

            return Results.Ok(Message("Chưa hỗ trợ hành động QR", "Loại hành động cấu hình trên máy chủ chưa được hỗ trợ.", "warning"));
        });

        g.MapPost("/decision", async (QrDecisionRequest req, ClaimsPrincipal principal, Database db,
            QrLoginService qrLogin, QrActionTokenService tokens, IHubContext<ChangesHub> hub, HttpContext http) =>
        {
            NoStore(http);
            var username = principal.Username();
            var subjectId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var sid = principal.FindFirstValue("sid");
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(subjectId) ||
                string.IsNullOrWhiteSpace(sid)) return Results.Unauthorized();
            if (req is null || string.IsNullOrWhiteSpace(req.ActionId) || req.ActionId.Length > 80 ||
                !tokens.TryRead(req.DecisionToken, subjectId, sid, req.ActionId, out var ticket))
                return Results.BadRequest(new { message = "Lựa chọn QR không hợp lệ hoặc đã hết hạn." });

            await using var conn = await db.OpenAsync(http.RequestAborted);
            var user = await ReadUserByUsername(conn, username);
            if (user is null || !Guid.TryParse(subjectId, out var claimUserId) || claimUserId != user.Id)
                return Results.Unauthorized();
            if (user.IsPending || !user.IsActive)
                return Results.Json(new { message = "Tài khoản không thể thực hiện thao tác QR." }, statusCode: 403);
            if (ticket.Handler.Equals(PayoutVoucherEndpoints.QrHandler, StringComparison.Ordinal))
            {
                if (req.ActionId != PayoutVoucherEndpoints.QrConfirmAction)
                    return Results.BadRequest(new { message = "Lựa chọn QR không được phép." });

                // Ký nhận lại từ đầu trong một câu UPDATE có điều kiện: vé quyết định không tự nó đủ để
                // đổi trạng thái, phiếu vẫn phải đang chờ đúng người này quét và mã chưa hết hạn.
                var signed = await PayoutVoucherEndpoints.ConfirmScanAsync(conn, ticket.Value, user.Username);
                if (signed is null)
                    return Results.BadRequest(new { message = "Phiếu chi không còn chờ ký nhận hoặc mã đã hết hạn." });

                await db.RecordAudit(user.Username, "Ký nhận phiếu chi", "PayoutVoucher", signed.Value.VoucherNo,
                    $"Người nhận quét QR xác nhận đã nhận tiền phiếu {signed.Value.VoucherNo} (app).");
                return Results.Ok(Message("Đã xác nhận",
                    $"Bạn đã ký nhận phiếu chi {signed.Value.VoucherNo}. Kế toán sẽ duyệt chi ngay bây giờ.", "success"));
            }

            if (!ticket.Handler.Equals(QrActionProtocol.WebLoginHandler, StringComparison.Ordinal))
                return Results.BadRequest(new { message = "Máy chủ không còn hỗ trợ hành động QR này." });

            if (req.ActionId == QrActionProtocol.ConfirmAction)
            {
                if (!await IsWebLoginEnabledAsync(conn, user.Username))
                    return Results.Json(new { message = "Đăng nhập trên web đang bị tắt cho tài khoản này." }, statusCode: 403);
                var result = qrLogin.Confirm(ticket.Value, user.Username, user.FullName);
                if (result == QrLoginConfirmResult.InvalidOrExpired)
                    return Results.BadRequest(new { message = "Mã QR không còn chờ xác nhận hoặc đã hết hạn." });
                if (result == QrLoginConfirmResult.Confirmed)
                    await db.RecordAudit(user.Username, "Xác nhận đăng nhập QR", "Auth", user.Username,
                        "Xác nhận qua giao thức QR server-driven cho một phiên đăng nhập web.");
                return Results.Ok(Message("Đã xác nhận", "Trình duyệt sẽ tự động đăng nhập tài khoản của bạn.", "success"));
            }

            if (req.ActionId == QrActionProtocol.RejectAction)
            {
                if (!qrLogin.RejectScan(ticket.Value, user.Username))
                    return Results.BadRequest(new { message = "Mã QR không còn chờ xác nhận." });
                return Results.Ok(Message("Đã từ chối", "Yêu cầu đăng nhập web đã được hủy."));
            }

            return Results.BadRequest(new { message = "Lựa chọn QR không được phép." });
        });
    }

    private static bool Supports(QrResolveRequest req, string capability)
        => req.Capabilities?.Contains(capability, StringComparer.Ordinal) == true;

    private static QrActionEnvelope Message(string title, string message, string severity = "info")
        => new(QrActionProtocol.Version, new QrPresentationDto(title, message, severity), []);

    private static async Task<UserDto?> ReadUserByUsername(NpgsqlConnection conn, string username)
    {
        await using var reader = await conn.Cmd(
            @"SELECT id, username, full_name, email, role, is_active, approval_status, created_at
              FROM app_users WHERE username = @u AND is_deleted = FALSE")
            .With("@u", username).ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new UserDto(
            reader.Guid("id"), reader.Str("username"), reader.Str("full_name"), reader.Str("email"),
            reader.Str("role"), reader.Bool("is_active"), reader.Str("approval_status"), reader.DtNull("created_at"));
    }

    private static async Task<bool> IsWebLoginEnabledAsync(NpgsqlConnection conn, string username)
    {
        await conn.Cmd(
            @"CREATE TABLE IF NOT EXISTS web_login_settings (
                  username varchar(150) PRIMARY KEY,
                  web_login_enabled boolean NOT NULL DEFAULT TRUE,
                  updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP);")
            .ExecuteNonQueryAsync();
        var value = await conn.Cmd("SELECT web_login_enabled FROM web_login_settings WHERE username=@u LIMIT 1")
            .With("@u", username).ExecuteScalarAsync();
        return value is not bool enabled || enabled;
    }

    private static void NoStore(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        http.Response.Headers.Pragma = "no-cache";
    }
}
