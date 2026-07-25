using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KetoanMini.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class QrActionEndpointTests
{
    private readonly ApiFactory _factory;
    public QrActionEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task GenericProtocol_ResolvesLogin_AndSupportsBoundConfirmAndReject()
    {
        var username = "__qr_action_" + Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var sid = "app:qr-action:" + Guid.NewGuid().ToString("N")[..20];
        string appToken;
        string forgedSubjectToken;
        using (var setupScope = _factory.Services.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<Database>();
            await using var conn = await db.OpenAsync();
            await conn.Cmd(
                    @"INSERT INTO app_users
                         (id, username, full_name, email, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at, is_deleted)
                      VALUES (@id, @u, 'QR Action Test', '', 'Employee', @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)")
                .With("@id", userId).With("@u", username).With("@ph", PasswordHasher.Hash("test-pass"))
                .ExecuteNonQueryAsync();
            var tokenService = setupScope.ServiceProvider.GetRequiredService<TokenService>();
            appToken = tokenService.CreateToken(
                new UserDto(userId, username, "QR Action Test", "", "Employee", true, "Approved", DateTime.UtcNow), sid);
            forgedSubjectToken = tokenService.CreateToken(
                new UserDto(Guid.NewGuid(), username, "QR Action Test", "", "Employee", true, "Approved", DateTime.UtcNow), sid);
        }

        try
        {
            // Trình duyệt: giữ cookie phiên và tự gắn header CSRF, giống hệt lib/api.ts ở frontend.
            var browser = _factory.CreateBrowserClient();
            var app = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            app.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", appToken);

            Assert.Equal(HttpStatusCode.Unauthorized,
                (await browser.PostAsJsonAsync("/api/qr/resolve", ResolveBody("anything"))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await browser.PostAsJsonAsync("/api/qr/decision", new { decisionToken = "x", actionId = "confirm" })).StatusCode);
            var forgedSubject = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            forgedSubject.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forgedSubjectToken);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await forgedSubject.PostAsJsonAsync("/api/qr/resolve", ResolveBody("anything"))).StatusCode);

            var first = await StartAsync(browser, "-confirm");
            var unsupportedClient = await app.PostAsJsonAsync("/api/qr/resolve", new
            {
                value = first.QrCode,
                protocolVersion = 1,
                capabilities = Array.Empty<string>()
            });
            Assert.Equal(HttpStatusCode.OK, unsupportedClient.StatusCode);
            Assert.Empty((await unsupportedClient.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("actions").EnumerateArray());

            var resolved = await app.PostAsJsonAsync("/api/qr/resolve", ResolveBody(first.QrCode));
            Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
            var envelope = await resolved.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, envelope.GetProperty("protocolVersion").GetInt32());
            // QR đăng nhập phải luôn chạy nghiệp vụ xác nhận, không được rơi xuống nhánh app tự đọc.
            Assert.False(envelope.GetProperty("unhandled").GetBoolean());
            var decisionToken = envelope.GetProperty("decisionToken").GetString()!;
            Assert.DoesNotContain(first.QrCode, decisionToken, StringComparison.Ordinal);
            Assert.Equal("reject", envelope.GetProperty("dismissActionId").GetString());
            Assert.Contains(envelope.GetProperty("actions").EnumerateArray(),
                x => x.GetProperty("type").GetString() == "server_decision" && x.GetProperty("id").GetString() == "confirm");

            var scanned = await browser.PostAsJsonAsync("/api/auth/qr/poll", new { pollToken = first.PollToken });
            var scannedJson = await scanned.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("scanned", scannedJson.GetProperty("status").GetString());
            Assert.Equal(username, scannedJson.GetProperty("account").GetProperty("username").GetString());

            var otherApp = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            otherApp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", await _factory.EmployeeTokenAsync("app:qr-other:" + Guid.NewGuid().ToString("N")));
            Assert.Equal(HttpStatusCode.BadRequest,
                (await otherApp.PostAsJsonAsync("/api/qr/decision", new { decisionToken, actionId = "confirm" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await app.PostAsJsonAsync("/api/qr/decision", new { decisionToken = decisionToken + "x", actionId = "confirm" })).StatusCode);

            var confirm = await app.PostAsJsonAsync("/api/qr/decision", new { decisionToken, actionId = "confirm" });
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
            Assert.Empty((await confirm.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("actions").EnumerateArray());
            Assert.Equal(HttpStatusCode.OK,
                (await app.PostAsJsonAsync("/api/qr/decision", new { decisionToken, actionId = "confirm" })).StatusCode);

            var authenticated = await browser.PostAsJsonAsync("/api/auth/qr/poll", new { pollToken = first.PollToken });
            Assert.Equal("authenticated",
                (await authenticated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
            await browser.PostAsJsonAsync("/api/auth/qr/ack", new { pollToken = first.PollToken });

            var second = await StartAsync(browser, "-reject");
            var secondEnvelope = await (await app.PostAsJsonAsync("/api/qr/resolve", ResolveBody(second.QrCode)))
                .Content.ReadFromJsonAsync<JsonElement>();
            var rejectToken = secondEnvelope.GetProperty("decisionToken").GetString()!;
            Assert.Equal(HttpStatusCode.OK,
                (await app.PostAsJsonAsync("/api/qr/decision", new { decisionToken = rejectToken, actionId = "reject" })).StatusCode);
            var rejected = await browser.PostAsJsonAsync("/api/auth/qr/poll", new { pollToken = second.PollToken });
            Assert.Equal("rejected", (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
            Assert.Equal(HttpStatusCode.BadRequest,
                (await app.PostAsJsonAsync("/api/qr/decision", new { decisionToken = rejectToken, actionId = "confirm" })).StatusCode);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            await using var conn = await db.OpenAsync();
            await conn.Cmd("DELETE FROM user_sessions WHERE username=@u").With("@u", username).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM web_login_settings WHERE username=@u").With("@u", username).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM app_users WHERE username=@u").With("@u", username).ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task UnknownQr_IsMarkedUnhandledForLocalReading_AndInvalidInputIsRejected()
    {
        var app = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        app.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.EmployeeTokenAsync("app:qr-unknown:" + Guid.NewGuid().ToString("N")));

        var unknown = await app.PostAsJsonAsync("/api/qr/resolve", ResolveBody("future-feature:" + Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        var envelope = await unknown.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(envelope.GetProperty("actions").EnumerateArray());
        Assert.Contains("chưa cấu hình", envelope.GetProperty("presentation").GetProperty("message").GetString(),
            StringComparison.OrdinalIgnoreCase);
        // Cờ này là thứ cho phép ứng dụng tự đọc nội dung mã. Bản cũ không biết cờ nên vẫn hiện message trên.
        Assert.True(envelope.GetProperty("unhandled").GetBoolean());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await app.PostAsJsonAsync("/api/qr/resolve", ResolveBody(""))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await app.PostAsJsonAsync("/api/qr/resolve", ResolveBody(new string('x', 4_097)))).StatusCode);
        using var oversizedBody = new StringContent(
            JsonSerializer.Serialize(ResolveBody(new string('x', 40_000))), Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,
            (await app.PostAsync("/api/qr/resolve", oversizedBody)).StatusCode);

        var configuredMessage = await app.PostAsJsonAsync("/api/qr/resolve", ResolveBody("__test_qr_message__"));
        var messageJson = await configuredMessage.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Thông báo từ server", messageJson.GetProperty("presentation").GetProperty("title").GetString());
        Assert.Empty(messageJson.GetProperty("actions").EnumerateArray());
        // Mã ĐÃ có nghiệp vụ thì không bao giờ được nhường cho ứng dụng đọc thô: nội dung do server quyết định.
        Assert.False(messageJson.GetProperty("unhandled").GetBoolean());

        var configuredLink = await app.PostAsJsonAsync("/api/qr/resolve", ResolveBody("__test_qr_link__"));
        var linkJson = await configuredLink.Content.ReadFromJsonAsync<JsonElement>();
        var open = linkJson.GetProperty("actions").EnumerateArray()
            .Single(x => x.GetProperty("type").GetString() == "open_https_url");
        Assert.Equal("https://example.com/qr-help", open.GetProperty("url").GetString()?.TrimEnd('/'));
        Assert.True(open.GetProperty("closeOnSelect").GetBoolean());
    }

    private async Task<(string QrCode, string PollToken)> StartAsync(HttpClient browser, string suffix)
    {
        var response = await browser.PostAsJsonAsync("/api/auth/qr/start", new { sid = "web:qr-action:" + suffix + Guid.NewGuid().ToString("N") });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (json.GetProperty("qrCode").GetString()!, json.GetProperty("pollToken").GetString()!);
    }

    private static object ResolveBody(string value) => new
    {
        value,
        protocolVersion = 1,
        capabilities = new[] { "server_decision", "open_https_url", "dismiss" },
        clientVersionCode = 1
    };
}
