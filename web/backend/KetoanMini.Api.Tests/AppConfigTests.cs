using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Kiểm thử tích hợp cấu hình điều khiển từ xa /api/app-config — Đợt 1, nhiệm vụ 4:
///  • Công tắc tính năng (vị trí, chấm công offline…) + nội dung onboarding round-trip qua PUT→GET.
///  • Chỉ Admin được ghi (Employee PUT → 403); mọi người đăng nhập đều đọc được.
/// Lưu &amp; khôi phục cấu hình thật để không làm bẩn app đang chạy (test dùng chung DB).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AppConfigTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private const string Admin = "__test_cfg_admin__";
    private const string Emp = "__test_cfg_emp__";
    private string _savedFeatures = "{}";
    private string _savedOnboarding = "{}";

    public AppConfigTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await Upsert(conn, Admin, "Admin");
        await Upsert(conn, Emp, "Employee");
        await using var r = await conn.Cmd(
            "SELECT feature_flags::text AS f, onboarding::text AS o FROM app_config WHERE id=1").ExecuteReaderAsync();
        if (await r.ReadAsync()) { _savedFeatures = r.Str("f"); _savedOnboarding = r.Str("o"); }
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("UPDATE app_config SET feature_flags=@f::jsonb, onboarding=@o::jsonb WHERE id=1")
            .With("@f", _savedFeatures).With("@o", _savedOnboarding).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM app_users WHERE username = ANY(@u)")
            .With("@u", new[] { Admin, Emp }).ExecuteNonQueryAsync();
    }

    private static async Task Upsert(Npgsql.NpgsqlConnection conn, string username, string role) =>
        await conn.Cmd(
            @"INSERT INTO app_users
                 (id, username, full_name, email, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at, is_deleted)
              VALUES (@id, @u, @u, '', @role, @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)
              ON CONFLICT (username) DO UPDATE SET is_active=TRUE, is_deleted=FALSE, role=@role, approval_status='Approved'")
            .With("@id", Guid.NewGuid()).With("@u", username).With("@role", role)
            .With("@ph", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();

    private async Task<HttpClient> ClientAsAsync(string username, string role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        var id = (Guid)(await conn.Cmd("SELECT id FROM app_users WHERE username=@u").With("@u", username).ExecuteScalarAsync())!;
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        var token = tokens.CreateToken(new UserDto(id, username, username, "", role, true, "Approved", DateTime.UtcNow));
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed record Features(bool LocationEnabled, bool OfflineAttendanceEnabled, bool BiometricAttendanceEnabled,
        bool ChatFileTransferEnabled, bool CompanyPortalEnabled);
    private sealed record Onboarding(string CameraReason, string LocationReason, string NotificationReason,
        string MicrophoneReason, string IntroText);
    private sealed record Config(Features Features, Onboarding Onboarding);

    [Fact]
    public async Task Config_EmployeeCannotPut_Returns403()
    {
        var client = await ClientAsAsync(Emp, "Employee");
        var res = await client.PutAsJsonAsync("/api/app-config", new { features = new { locationEnabled = false } });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Config_AdminPut_FeaturesAndOnboarding_RoundTrips()
    {
        var admin = await ClientAsAsync(Admin, "Admin");
        var put = await admin.PutAsJsonAsync("/api/app-config", new
        {
            features = new { locationEnabled = false, offlineAttendanceEnabled = false },
            onboarding = new { cameraReason = "Xin quyền camera để chấm công khuôn mặt." },
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        // Mọi người đăng nhập đều đọc được cấu hình mới.
        var emp = await ClientAsAsync(Emp, "Employee");
        var cfg = await emp.GetFromJsonAsync<Config>("/api/app-config");

        Assert.NotNull(cfg);
        Assert.False(cfg!.Features.LocationEnabled);
        Assert.False(cfg.Features.OfflineAttendanceEnabled);
        Assert.True(cfg.Features.BiometricAttendanceEnabled); // trường không gửi → mặc định bật
        Assert.Equal("Xin quyền camera để chấm công khuôn mặt.", cfg.Onboarding.CameraReason);
    }
}
