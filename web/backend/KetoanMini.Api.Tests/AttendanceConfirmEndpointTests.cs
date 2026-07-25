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
/// Bước "Xác nhận" chấm công đi bằng TOKEN thay vì gửi lại cả loạt ảnh. Vì nhánh này CỐ Ý bỏ qua toàn bộ
/// khâu nhận diện, nó phải tự đứng vững: ghi đúng một dòng công cho đúng người, và không cho ai mượn
/// token của người khác hay phát lại một token đã dùng để ghi công hai lần.
///
/// Test bơm thẳng token vào dịch vụ (bước xem trước cần khuôn mặt thật nên không dựng được ở đây) rồi
/// gọi API như client thật, nên nhánh ghi công được chạy hết từ HTTP xuống tận PostgreSQL.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AttendanceConfirmEndpointTests(ApiFactory factory)
{
    [Fact]
    public async Task ConfirmByToken_WritesExactlyOneLog_AndRejectsReplayOrBorrowedToken()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var owner = "cc-owner-" + suffix;
        var other = "cc-other-" + suffix;

        var (ownerToken, otherToken) = await SetupUsersAsync(owner, other);
        try
        {
            var tokens = factory.Services.GetRequiredService<AttendancePreviewTokens>();

            // --- Người khác KHÔNG dùng được token của mình (dù token còn hạn) ---
            var borrowed = tokens.Issue(Pending(owner))!;
            var borrowedResult = await ConfirmAsync(otherToken, borrowed);
            Assert.Equal("expired", borrowedResult.GetProperty("status").GetString());
            Assert.Equal(0, await LogCountAsync(owner));

            // --- Đường chính: xác nhận hợp lệ ghi đúng MỘT dòng công ---
            var granted = tokens.Issue(Pending(owner))!;
            var ok = await ConfirmAsync(ownerToken, granted);
            Assert.Equal("ok", ok.GetProperty("status").GetString());
            Assert.True(ok.GetProperty("matched").GetBoolean());
            Assert.Equal(owner, ok.GetProperty("username").GetString());
            // Lần chấm đầu ngày là giờ Vào (AttendancePolicy tính lại lúc ghi, không lấy từ xem trước).
            Assert.Equal("Vào", ok.GetProperty("loai").GetString());
            Assert.Equal(1, await LogCountAsync(owner));

            // --- Phát lại chính token đó KHÔNG ghi thêm dòng nào ---
            var replay = await ConfirmAsync(ownerToken, granted);
            Assert.Equal("expired", replay.GetProperty("status").GetString());
            Assert.Equal(1, await LogCountAsync(owner));
        }
        finally
        {
            await CleanupAsync(owner, other);
        }
    }

    private static AttendancePreviewTokens.Pending Pending(string username) =>
        new(username, username, "Người Thử Nghiệm", 0.91, 0.7, [0.5f, 0.5f, 0.7071f]);

    private async Task<JsonElement> ConfirmAsync(string bearer, string previewToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        var response = await client.PostAsJsonAsync("/api/chamcong/cham", new { confirmToken = previewToken });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<int> LogCountAsync(string username)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        return Convert.ToInt32(await conn.Cmd("SELECT COUNT(*) FROM cham_cong_log WHERE username=@u")
            .With("@u", username).ExecuteScalarAsync());
    }

    private async Task<(string Owner, string Other)> SetupUsersAsync(string owner, string other)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();
        return (await MakeUserAsync(conn, tokens, owner), await MakeUserAsync(conn, tokens, other));
    }

    private static async Task<string> MakeUserAsync(Npgsql.NpgsqlConnection conn, TokenService tokens,
        string username)
    {
        var userId = Guid.NewGuid();
        await conn.Cmd("""
            INSERT INTO app_users (id, username, full_name, email, role, password_hash, is_active,
                approval_status, approved_at, approved_by, created_at, is_deleted)
            VALUES (@id, @u, @n, '', 'User', @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test',
                CURRENT_TIMESTAMP, FALSE)
            """)
            .With("@id", userId).With("@u", username).With("@n", "Người Thử Nghiệm")
            .With("@ph", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();

        return tokens.CreateToken(
            new UserDto(userId, username, "Người Thử Nghiệm", "", "User", true, "Approved", DateTime.UtcNow),
            "app:cc:" + Guid.NewGuid().ToString("N")[..16]);
    }

    private async Task CleanupAsync(params string[] usernames)
    {
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            await using var conn = await db.OpenAsync();
            await conn.Cmd("DELETE FROM cham_cong_log WHERE username = ANY(@u)").With("@u", usernames)
                .ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM cham_cong_face WHERE username = ANY(@u)").With("@u", usernames)
                .ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM user_sessions WHERE username = ANY(@u)").With("@u", usernames)
                .ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM app_users WHERE username = ANY(@u)").With("@u", usernames)
                .ExecuteNonQueryAsync();
        }
        catch { /* dọn dẹp best-effort */ }
    }
}
