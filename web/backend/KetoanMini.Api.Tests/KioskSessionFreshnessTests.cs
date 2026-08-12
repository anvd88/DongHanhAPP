using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace KetoanMini.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class KioskSessionFreshnessTests(ApiFactory factory)
{
    private const string KioskKey = "test-kiosk-key-7d64d154";

    [Fact]
    public async Task AuthenticatedKioskRequests_RequireAnActiveSessionOwnedByTheTokenUser()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var username = "kiosk-session-" + suffix;
        var other = "kiosk-other-" + suffix;

        using var app = CreateApp();
        var tokens = await SetupAsync(app.Services, username, other);
        try
        {
            var freshPaths = new[]
            {
                "/api/chamcong/nhandien",
                "/api/chamcong/cham",
                "/api/chamcong/trangthai",
            };
            var invalidTokens = new[]
            {
                tokens.WithoutSid,
                tokens.MissingSession,
                tokens.OtherUsersSession,
                tokens.InactiveSession,
                tokens.RevokedSession,
                tokens.IdleExpiredSession,
            };

            foreach (var token in invalidTokens)
            foreach (var path in freshPaths)
            {
                using var client = Client(app, token, includeKioskKey: true);
                using var response = await CallFreshEndpointAsync(client, path);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }

            // A matching, active, non-revoked and non-idle session is the only JWT path that
            // receives the fresh-account marker used by KioskAccessFilter.
            using (var validClient = Client(app, tokens.Valid))
            {
                using var valid = await validClient.GetAsync("/api/chamcong/trangthai");
                valid.EnsureSuccessStatusCode();
            }

            // Logout only flips is_active=false. That exact session must be rejected everywhere,
            // while an older token with no session row remains compatible outside kiosk endpoints.
            using (var loggedOutClient = Client(app, tokens.InactiveSession))
            using (var loggedOut = await loggedOutClient.GetAsync("/api/auth/devices"))
                Assert.Equal(HttpStatusCode.Unauthorized, loggedOut.StatusCode);
            using (var legacyClient = Client(app, tokens.MissingSession))
            using (var legacy = await legacyClient.GetAsync("/api/auth/devices"))
                legacy.EnsureSuccessStatusCode();

            // Anonymous kiosks keep their existing key-based route. An authenticated but stale
            // JWT cannot use the key as a fallback (covered above).
            using (var anonymous = app.CreateClient())
            {
                using var denied = await anonymous.GetAsync("/api/chamcong/trangthai");
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

                anonymous.DefaultRequestHeaders.Add(KioskAccess.HeaderName, KioskKey);
                using var allowed = await anonymous.GetAsync("/api/chamcong/trangthai");
                allowed.EnsureSuccessStatusCode();
            }
        }
        finally
        {
            await CleanupAsync(app.Services, username, other);
        }
    }

    [Fact]
    public void AnonymousLanKiosk_RemainsAllowedWhenLanAccessIsEnabled()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Security:KioskApiKey"] = KioskKey,
                ["Security:KioskAllowLan"] = "true",
            }).Build();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.10.25");

        Assert.True(KioskAccess.IsAllowed(context, config));
    }

    private WebApplicationFactory<Program> CreateApp() => factory.WithWebHostBuilder(builder =>
    {
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Security:KioskApiKey"] = KioskKey,
                ["Security:KioskAllowLan"] = "false",
                ["Security:SessionIdleDays"] = "1",
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IFaceEngine>();
            services.AddSingleton<IFaceEngine>(new StatusFaceEngine());
        });
    });

    private static HttpClient Client(
        WebApplicationFactory<Program> app, string token, bool includeKioskKey = false)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (includeKioskKey)
            client.DefaultRequestHeaders.Add(KioskAccess.HeaderName, KioskKey);
        return client;
    }

    private static Task<HttpResponseMessage> CallFreshEndpointAsync(HttpClient client, string path)
        => path.EndsWith("/trangthai", StringComparison.Ordinal)
            ? client.GetAsync(path)
            : path.EndsWith("/nhandien", StringComparison.Ordinal)
                ? client.PostAsJsonAsync(path, new { imageBase64 = "AA==" })
                : client.PostAsJsonAsync(path, new { images = new[] { "AA==" } });

    private static async Task<SessionTokens> SetupAsync(
        IServiceProvider services, string username, string other)
    {
        var db = services.GetRequiredService<Database>();
        var tokenService = services.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();

        var userId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await conn.Cmd("""
            INSERT INTO app_users (id, username, full_name, email, role, password_hash, is_active,
                approval_status, approved_at, approved_by, created_at, is_deleted)
            VALUES
                (@id, @u, @u, '', 'Employee', @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE),
                (@otherId, @other, @other, '', 'Employee', @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)
            """)
            .With("@id", userId).With("@u", username)
            .With("@otherId", otherId).With("@other", other)
            .With("@ph", PasswordHasher.Hash("test-pass"))
            .ExecuteNonQueryAsync();

        var validSid = "kiosk-valid-" + suffix();
        var inactiveSid = "kiosk-inactive-" + suffix();
        var revokedSid = "kiosk-revoked-" + suffix();
        var idleSid = "kiosk-idle-" + suffix();
        var otherSid = "kiosk-other-" + suffix();
        await conn.Cmd("""
            INSERT INTO user_sessions
                (session_token, username, machine_name, started_at, last_seen, is_active, client_kind, revoked)
            VALUES
                (@valid, @u, 'test', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, TRUE, 'App', FALSE),
                (@inactive, @u, 'test', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, FALSE, 'App', FALSE),
                (@revoked, @u, 'test', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, TRUE, 'App', TRUE),
                (@idle, @u, 'test', CURRENT_TIMESTAMP - INTERVAL '2 days', CURRENT_TIMESTAMP - INTERVAL '2 days', TRUE, 'App', FALSE),
                (@otherSid, @other, 'test', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, TRUE, 'App', FALSE)
            """)
            .With("@valid", validSid).With("@inactive", inactiveSid)
            .With("@revoked", revokedSid).With("@idle", idleSid)
            .With("@otherSid", otherSid).With("@u", username).With("@other", other)
            .ExecuteNonQueryAsync();

        var user = new UserDto(
            userId, username, username, "", AppRoles.Employee, true, "Approved", DateTime.UtcNow);
        return new SessionTokens(
            tokenService.CreateToken(user, validSid),
            tokenService.CreateToken(user),
            tokenService.CreateToken(user, "kiosk-missing-" + suffix()),
            tokenService.CreateToken(user, otherSid),
            tokenService.CreateToken(user, inactiveSid),
            tokenService.CreateToken(user, revokedSid),
            tokenService.CreateToken(user, idleSid));

        static string suffix() => Guid.NewGuid().ToString("N")[..12];
    }

    private static async Task CleanupAsync(
        IServiceProvider services, string username, string other)
    {
        try
        {
            var db = services.GetRequiredService<Database>();
            await using var conn = await db.OpenAsync();
            await conn.Cmd("DELETE FROM user_sessions WHERE username = ANY(@u)")
                .With("@u", new[] { username, other }).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM app_users WHERE username = ANY(@u)")
                .With("@u", new[] { username, other }).ExecuteNonQueryAsync();
        }
        catch
        {
            // Cleanup is best effort so a primary assertion remains visible.
        }
    }

    private sealed record SessionTokens(
        string Valid,
        string WithoutSid,
        string MissingSession,
        string OtherUsersSession,
        string InactiveSession,
        string RevokedSession,
        string IdleExpiredSession);

    private sealed class StatusFaceEngine : IFaceEngine
    {
        public string Name => "test-status";
        public double MatchThreshold => 0.45;
        public AntiSpoofStatus AntiSpoof => new(AntiSpoofLevel.Full, "test");
        public bool CheckLiveness(byte[] imageBytes) => true;
        public double LivenessProbability(byte[] imageBytes) => 1;
        public double LivenessThreshold => 0.5;
        public float[]? ExtractEmbedding(byte[] imageBytes) => [1, 0, 0];
        public double Compare(float[] a, float[] b) => 1;
        public FaceFrameQuality? AssessFrame(byte[] imageBytes) => null;
    }
}
