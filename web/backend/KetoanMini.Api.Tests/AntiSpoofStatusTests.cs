using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Chống giả mạo hỏng là kiểu hỏng KHÔNG có triệu chứng: model không nạp được thì
/// <see cref="IFaceEngine.LivenessProbability"/> trả 1 cho mọi ảnh, chấm công vẫn chạy y như bình
/// thường, chỉ là giơ ảnh/màn hình cũng qua. Hai test này là cái chuông cho đúng tình huống đó.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AntiSpoofStatusTests(ApiFactory factory)
{
    /// <summary>
    /// Nếu ai đó làm hỏng việc đóng gói model (đổi luật copy trong .csproj, dọn nhầm thư mục
    /// Models/Face, đổi tên file), test này đỏ NGAY thay vì để hệ thống chạy không có chống giả mạo.
    /// </summary>
    [Fact]
    public void AntiSpoof_IsFullyLoaded_SoAttendanceIsNotSilentlyUnprotected()
    {
        var engine = factory.Services.GetRequiredService<IFaceEngine>();

        Assert.Equal(AntiSpoofLevel.Full, engine.AntiSpoof.Level);
        Assert.NotEmpty(engine.AntiSpoof.Detail);
    }

    /// <summary>
    /// Panel quản trị là nơi DUY NHẤT con người nhìn thấy mức chống giả mạo, nên hình dạng JSON của nó
    /// phải giữ đúng: giao diện web đọc theo tên trường (types.ts viết tay, không sinh tự động).
    /// </summary>
    [Fact]
    public async Task LivenessPanel_ExposesAntiSpoofLevel_ToTheAdminScreen()
    {
        var admin = "as-admin-" + Guid.NewGuid().ToString("N")[..10];
        var token = await MakeAdminAsync(admin);
        try
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync("/api/chamcong/liveness-metrics");
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal("Full", body.GetProperty("antiSpoof").GetProperty("level").GetString());
            Assert.Equal(JsonValueKind.Array, body.GetProperty("metrics").ValueKind);
        }
        finally
        {
            await CleanupAsync(admin);
        }
    }

    private async Task<string> MakeAdminAsync(string username)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();

        var userId = Guid.NewGuid();
        await conn.Cmd("""
            INSERT INTO app_users (id, username, full_name, email, role, password_hash, is_active,
                approval_status, approved_at, approved_by, created_at, is_deleted)
            VALUES (@id, @u, @n, '', 'Admin', @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test',
                CURRENT_TIMESTAMP, FALSE)
            """)
            .With("@id", userId).With("@u", username).With("@n", "Quản trị thử nghiệm")
            .With("@ph", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();

        return tokens.CreateToken(
            new UserDto(userId, username, "Quản trị thử nghiệm", "", "Admin", true, "Approved", DateTime.UtcNow),
            "app:as:" + Guid.NewGuid().ToString("N")[..16]);
    }

    private async Task CleanupAsync(string username)
    {
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            await using var conn = await db.OpenAsync();
            await conn.Cmd("DELETE FROM user_sessions WHERE username=@u").With("@u", username)
                .ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM app_users WHERE username=@u").With("@u", username)
                .ExecuteNonQueryAsync();
        }
        catch { /* dọn dẹp best-effort */ }
    }
}
