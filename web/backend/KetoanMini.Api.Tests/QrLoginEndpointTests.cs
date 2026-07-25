using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KetoanMini.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class QrLoginEndpointTests
{
    private readonly ApiFactory _factory;
    public QrLoginEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task PublicPoll_IgnoresStaleBearerToken_ForMissingUser()
    {
        string staleToken;
        using (var scope = _factory.Services.CreateScope())
        {
            var missingUsername = "__missing_qr_poll_" + Guid.NewGuid().ToString("N");
            staleToken = scope.ServiceProvider.GetRequiredService<TokenService>().CreateToken(
                new UserDto(
                    Guid.NewGuid(), missingUsername, "Missing QR User", "", "Employee",
                    true, "Approved", DateTime.UtcNow),
                "stale:qr-poll:" + Guid.NewGuid().ToString("N"));
        }

        var browser = _factory.CreateBrowserClient();
        var start = await browser.PostAsJsonAsync(
            "/api/auth/qr/start",
            new { sid = "web:qr-stale-token:" + Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var pollToken = (await start.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("pollToken").GetString()!;

        // Trình duyệt có thể còn giữ JWT của tài khoản đã bị xóa/thu hồi. Endpoint QR công khai
        // vẫn phải poll được; middleware phiên đăng nhập không được biến request này thành 401.
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staleToken);
        var poll = await browser.PostAsJsonAsync("/api/auth/qr/poll", new { pollToken });

        Assert.Equal(HttpStatusCode.OK, poll.StatusCode);
        Assert.Equal("pending",
            (await poll.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task FullFlow_RequiresAppAuthentication_AndIssuesTokenOnce()
    {
        var sid = "web:qr-test:" + Guid.NewGuid().ToString("N");
        var username = "__qr_login_" + Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        const string avatarUrl = "data:image/png;base64,iVBORw0KGgo=";
        string appToken;
        using (var setupScope = _factory.Services.CreateScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<Database>();
            await using var setupConn = await setupDb.OpenAsync();
            await setupConn.Cmd(
                    @"INSERT INTO app_users
                         (id, username, full_name, email, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at, is_deleted)
                      VALUES
                         (@id, @u, 'QR Test', '', 'Employee', @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)")
                .With("@id", userId).With("@u", username).With("@ph", PasswordHasher.Hash("test-pass"))
                .ExecuteNonQueryAsync();
            await setupConn.Cmd(
                    "INSERT INTO web_user_avatars (user_id, image_data_url, updated_at) VALUES (@id, @avatar, CURRENT_TIMESTAMP)")
                .With("@id", userId).With("@avatar", avatarUrl).ExecuteNonQueryAsync();
            appToken = setupScope.ServiceProvider.GetRequiredService<TokenService>().CreateToken(
                new UserDto(userId, username, "QR Test", "", "Employee", true, "Approved", DateTime.UtcNow));
        }

        var browser = _factory.CreateBrowserClient();
        var start = await browser.PostAsJsonAsync("/api/auth/qr/start", new { sid });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        using var started = JsonDocument.Parse(await start.Content.ReadAsStringAsync());
        var qrCode = started.RootElement.GetProperty("qrCode").GetString()!;
        var pollToken = started.RootElement.GetProperty("pollToken").GetString()!;

        var pending = await browser.PostAsJsonAsync("/api/auth/qr/poll", new { pollToken });
        Assert.Equal("pending", (await pending.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var unauthorized = await browser.PostAsJsonAsync("/api/auth/qr/scan", new { qrCode });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var app = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        app.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", appToken);
        var scan = await app.PostAsJsonAsync("/api/auth/qr/scan", new { qrCode });
        Assert.Equal(HttpStatusCode.NoContent, scan.StatusCode);

        var scanned = await browser.PostAsJsonAsync("/api/auth/qr/poll", new { pollToken });
        Assert.Equal(HttpStatusCode.OK, scanned.StatusCode);
        var scannedResult = await scanned.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("scanned", scannedResult.GetProperty("status").GetString());
        Assert.Equal(username, scannedResult.GetProperty("account").GetProperty("username").GetString());
        Assert.Equal("QR Test", scannedResult.GetProperty("account").GetProperty("fullName").GetString());
        var account = await browser.PostAsJsonAsync("/api/auth/qr/account", new { pollToken });
        Assert.Equal(HttpStatusCode.OK, account.StatusCode);
        Assert.Equal(avatarUrl, (await account.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("avatarUrl").GetString());

        var confirm = await app.PostAsJsonAsync("/api/auth/qr/confirm", new { qrCode });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        var authenticated = await browser.PostAsJsonAsync("/api/auth/qr/poll", new { pollToken });
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        var result = await authenticated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("authenticated", result.GetProperty("status").GetString());
        Assert.Equal(username, result.GetProperty("user").GetProperty("username").GetString());

        // HỢP ĐỒNG MỚI: phiên của trình duyệt về bằng COOKIE HttpOnly, thân phản hồi KHÔNG có token.
        // Còn trả token ra cho JavaScript thì nó lại nằm trong localStorage và XSS vẫn lấy được —
        // tức là đổi sang cookie mà chẳng được gì. Xem Security/AuthCookies.cs.
        Assert.False(result.TryGetProperty("token", out var leaked) && leaked.ValueKind != JsonValueKind.Null,
            "Đăng nhập QR trên trình duyệt không được trả JWT ra thân phản hồi.");
        var setCookies = authenticated.Headers.GetValues("Set-Cookie").ToArray();
        var authCookie = Assert.Single(setCookies, c => c.StartsWith("km_auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(setCookies, c => c.StartsWith("km_csrf=", StringComparison.Ordinal));

        // Mô phỏng response đầu bị mất: poll lại vẫn nhận authenticated; ack mới xóa phiên khỏi RAM.
        var deliveredAgain = await browser.PostAsJsonAsync("/api/auth/qr/poll", new { pollToken });
        Assert.Equal("authenticated", (await deliveredAgain.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent,
            (await browser.PostAsJsonAsync("/api/auth/qr/ack", new { pollToken })).StatusCode);
        var usedAgain = await browser.PostAsJsonAsync("/api/auth/qr/poll", new { pollToken });
        Assert.Equal("expired", (await usedAgain.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var rejectedStart = await browser.PostAsJsonAsync("/api/auth/qr/start", new { sid = sid + "-reject" });
        var rejectedStarted = await rejectedStart.Content.ReadFromJsonAsync<JsonElement>();
        var rejectedQrCode = rejectedStarted.GetProperty("qrCode").GetString()!;
        var rejectedPollToken = rejectedStarted.GetProperty("pollToken").GetString()!;
        Assert.Equal(HttpStatusCode.NoContent, (await app.PostAsJsonAsync("/api/auth/qr/scan", new { qrCode = rejectedQrCode })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.PostAsJsonAsync("/api/auth/qr/reject", new { qrCode = rejectedQrCode })).StatusCode);
        var rejectedPoll = await browser.PostAsJsonAsync("/api/auth/qr/poll", new { pollToken = rejectedPollToken });
        Assert.Equal("rejected", (await rejectedPoll.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await app.PostAsJsonAsync("/api/auth/qr/confirm", new { qrCode = rejectedQrCode })).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("DELETE FROM user_sessions WHERE session_token=@sid OR username=@u")
            .With("@sid", sid).With("@u", username).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM web_login_settings WHERE username=@u").With("@u", username).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM web_user_avatars WHERE user_id=@id").With("@id", userId).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM app_users WHERE username=@u").With("@u", username).ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task MobileAppFlow_UsesDedicatedEndpointsAndClientMode()
    {
        var sid = "web:app-test:" + Guid.NewGuid().ToString("N");
        var username = "__app_login_" + Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        string appToken;
        using (var setupScope = _factory.Services.CreateScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<Database>();
            await using var setupConn = await setupDb.OpenAsync();
            await setupConn.Cmd(
                    @"INSERT INTO app_users
                         (id, username, full_name, email, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at, is_deleted)
                      VALUES
                         (@id, @u, 'Mobile App Test', '', 'Employee', @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)")
                .With("@id", userId).With("@u", username).With("@ph", PasswordHasher.Hash("test-pass"))
                .ExecuteNonQueryAsync();
            appToken = setupScope.ServiceProvider.GetRequiredService<TokenService>().CreateToken(
                new UserDto(userId, username, "Mobile App Test", "", "Employee", true, "Approved", DateTime.UtcNow));
        }

        var browser = _factory.CreateBrowserClient();
        var invalidMode = await browser.PostAsJsonAsync("/api/auth/app-login/start",
            new { sid, clientMode = "desktop_qr" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidMode.StatusCode);

        var start = await browser.PostAsJsonAsync("/api/auth/app-login/start",
            new { sid, clientMode = "mobile_app" });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var started = await start.Content.ReadFromJsonAsync<JsonElement>();
        var requestCode = started.GetProperty("requestCode").GetString()!;
        var pollToken = started.GetProperty("pollToken").GetString()!;
        Assert.StartsWith("ketoanmini-app-login:", requestCode);
        Assert.Equal("mobile_app", started.GetProperty("clientMode").GetString());

        var unauthorized = await browser.PostAsJsonAsync("/api/auth/app-login/resolve",
            new { requestCode, clientMode = "mobile_app" });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal("expired", (await (await browser.PostAsJsonAsync("/api/auth/qr/poll", new { pollToken }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var app = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        app.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", appToken);
        var resolve = await app.PostAsJsonAsync("/api/auth/app-login/resolve",
            new { requestCode, clientMode = "mobile_app" });
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        Assert.Equal("Xác nhận đăng nhập web?",
            (await resolve.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());

        var opened = await browser.PostAsJsonAsync("/api/auth/app-login/poll",
            new { pollToken, clientMode = "mobile_app" });
        Assert.Equal("opened", (await opened.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var confirm = await app.PostAsJsonAsync("/api/auth/app-login/confirm",
            new { requestCode, clientMode = "mobile_app" });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var authenticated = await browser.PostAsJsonAsync("/api/auth/app-login/poll",
            new { pollToken, clientMode = "mobile_app" });
        var result = await authenticated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("authenticated", result.GetProperty("status").GetString());
        Assert.Equal(username, result.GetProperty("user").GetProperty("username").GetString());
        Assert.Equal(HttpStatusCode.NoContent,
            (await browser.PostAsJsonAsync("/api/auth/app-login/ack",
                new { pollToken, clientMode = "mobile_app" })).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("DELETE FROM user_sessions WHERE session_token=@sid OR username=@u")
            .With("@sid", sid).With("@u", username).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM web_login_settings WHERE username=@u").With("@u", username).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM app_users WHERE username=@u").With("@u", username).ExecuteNonQueryAsync();
    }
}
