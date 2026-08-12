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
public sealed class SessionOwnershipSecurityTests(ApiFactory factory)
{
    [Fact]
    public async Task SidCollisionAndHeartbeat_CannotChangeOwnerOrReviveAnotherJwt()
    {
        // Host riêng giữ quota login của ca này độc lập với các test đăng nhập khác trong full suite.
        using var app = factory.WithWebHostBuilder(_ => { });
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var victim = "sid-victim-" + suffix;
        var attacker = "sid-attacker-" + suffix;
        var sharedSid = "sid-shared-" + suffix;
        var missingSid = "sid-missing-" + suffix;

        var (victimId, _) = await AddUsersAsync(app.Services, victim, attacker, sharedSid);
        var tokenService = app.Services.GetRequiredService<TokenService>();
        var revokedVictimToken = tokenService.CreateToken(
            new UserDto(victimId, victim, victim, "", AppRoles.Employee, true,
                "Approved", DateTime.UtcNow), sharedSid);
        var missingSessionVictimToken = tokenService.CreateToken(
            new UserDto(victimId, victim, victim, "", AppRoles.Employee, true,
                "Approved", DateTime.UtcNow), missingSid);

        try
        {
            // Tài khoản khác cố đăng nhập bằng SID đã thuộc nạn nhân: đăng nhập vẫn dùng được nhưng
            // server phải cấp SID scoped khác, không được đổi chủ/xóa revoked của hàng cũ.
            using var anonymous = app.CreateClient();
            var login = await anonymous.PostAsJsonAsync("/api/auth/login", new
            {
                username = attacker,
                password = "test-pass",
                sid = sharedSid,
                client = "android",
            });
            login.EnsureSuccessStatusCode();
            var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();
            var attackerToken = loginBody.GetProperty("token").GetString();
            Assert.False(string.IsNullOrWhiteSpace(attackerToken));

            await using (var conn = await Db(app.Services).OpenAsync())
            {
                Assert.Equal(victim, Convert.ToString(await conn.Cmd(
                    "SELECT username FROM user_sessions WHERE session_token=@sid")
                    .With("@sid", sharedSid).ExecuteScalarAsync()));
                Assert.True(Convert.ToBoolean(await conn.Cmd(
                    "SELECT revoked FROM user_sessions WHERE session_token=@sid")
                    .With("@sid", sharedSid).ExecuteScalarAsync()));
            }

            // JWT cũ đã thu hồi vẫn bị middleware chặn.
            using (var revokedClient = Client(app, revokedVictimToken))
            {
                var denied = await revokedClient.PostAsJsonAsync(
                    "/api/auth/heartbeat", new { sid = sharedSid });
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            }

            string attackerSid;
            await using (var conn = await Db(app.Services).OpenAsync())
            {
                attackerSid = Convert.ToString(await conn.Cmd(
                    "SELECT session_token FROM user_sessions WHERE username=@u LIMIT 1")
                    .With("@u", attacker).ExecuteScalarAsync())!;
                Assert.NotEqual(sharedSid, attackerSid);
            }

            // JWT không có hàng session không thể dùng SID của người khác trong body để heartbeat
            // UPSERT/đổi chủ như trước đây.
            using (var missingClient = Client(app, missingSessionVictimToken))
            {
                var denied = await missingClient.PostAsJsonAsync(
                    "/api/auth/heartbeat", new { sid = attackerSid });
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            }

            // Cả heartbeat và logout đều lấy SID từ JWT đã ký, bỏ qua SID giả trong body.
            using (var attackerClient = Client(app, attackerToken!))
            {
                var heartbeat = await attackerClient.PostAsJsonAsync(
                    "/api/auth/heartbeat", new { sid = sharedSid });
                Assert.Equal(HttpStatusCode.NoContent, heartbeat.StatusCode);

                var logout = await attackerClient.PostAsJsonAsync(
                    "/api/auth/logout", new { sid = sharedSid });
                Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            }

            await using (var conn = await Db(app.Services).OpenAsync())
            {
                Assert.False(Convert.ToBoolean(await conn.Cmd(
                    "SELECT is_active FROM user_sessions WHERE session_token=@sid")
                    .With("@sid", attackerSid).ExecuteScalarAsync()));
                Assert.Equal(victim, Convert.ToString(await conn.Cmd(
                    "SELECT username FROM user_sessions WHERE session_token=@sid")
                    .With("@sid", sharedSid).ExecuteScalarAsync()));
            }
        }
        finally
        {
            await CleanupAsync(app.Services, victim, attacker);
        }
    }

    private static Database Db(IServiceProvider services)
        => services.GetRequiredService<Database>();

    private static HttpClient Client(WebApplicationFactory<Program> app, string token)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<(Guid Victim, Guid Attacker)> AddUsersAsync(
        IServiceProvider services, string victim, string attacker, string sharedSid)
    {
        var victimId = Guid.NewGuid();
        var attackerId = Guid.NewGuid();
        await using var conn = await Db(services).OpenAsync();
        await conn.Cmd("""
            INSERT INTO app_users (id, username, full_name, email, role, password_hash, is_active,
                approval_status, approved_at, approved_by, created_at, is_deleted)
            VALUES
                (@victimId, @victim, @victim, '', 'Employee', @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE),
                (@attackerId, @attacker, @attacker, '', 'Employee', @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)
            """)
            .With("@victimId", victimId).With("@victim", victim)
            .With("@attackerId", attackerId).With("@attacker", attacker)
            .With("@ph", PasswordHasher.Hash("test-pass"))
            .ExecuteNonQueryAsync();
        await conn.Cmd("""
            INSERT INTO user_sessions
                (session_token, username, machine_name, started_at, last_seen, is_active,
                 client_kind, revoked, revoked_at, revoked_by)
            VALUES (@sid, @victim, 'old-device', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP,
                    FALSE, 'App', TRUE, CURRENT_TIMESTAMP, 'test')
            """)
            .With("@sid", sharedSid).With("@victim", victim).ExecuteNonQueryAsync();
        return (victimId, attackerId);
    }

    private static async Task CleanupAsync(IServiceProvider services, params string[] usernames)
    {
        try
        {
            await using var conn = await Db(services).OpenAsync();
            await conn.Cmd("DELETE FROM audit_logs WHERE username=ANY(@u) OR entity_name=ANY(@u)")
                .With("@u", usernames).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM user_sessions WHERE username=ANY(@u)")
                .With("@u", usernames).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM app_users WHERE username=ANY(@u)")
                .With("@u", usernames).ExecuteNonQueryAsync();
        }
        catch
        {
            // Cleanup best effort so the primary assertion remains visible.
        }
    }
}
