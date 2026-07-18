using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Endpoints;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Token sống 365 ngày, nên "vai trò trong JWT" và "vai trò trong DB" có thể lệch nhau rất lâu.
/// Các test này chốt rằng QUYỀN LUÔN ĐƯỢC CHẤM THEO DB: hạ quyền có hiệu lực ngay ở request kế tiếp
/// mà không cần đăng xuất, và cấp quyền cũng vậy.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TokenRoleFreshnessTests
{
    private readonly ApiFactory _factory;
    public TokenRoleFreshnessTests(ApiFactory factory) => _factory = factory;

    private HttpClient NewClient(string token)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Dựng JWT mang vai trò TÙY Ý cho tài khoản test trong khi DB vẫn ghi Employee — mô phỏng
    /// đúng tình huống "token được cấp lúc còn là Admin, sau đó bị hạ quyền": chữ ký hợp lệ, còn hạn.</summary>
    private async Task<string> TokenClaimingRoleAsync(string role)
    {
        await _factory.EmployeeTokenAsync(); // đảm bảo tài khoản tồn tại và role trong DB = Employee
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        var id = (Guid)(await conn.Cmd("SELECT id FROM app_users WHERE username = @u")
            .With("@u", _factory.EmpUser).ExecuteScalarAsync())!;
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        return tokens.CreateToken(
            new UserDto(id, _factory.EmpUser, "Test Employee", "", role, true, "Approved", DateTime.UtcNow));
    }

    private async Task SetDbRoleAsync(string role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("UPDATE app_users SET role = @r WHERE username = @u")
            .With("@r", role).With("@u", _factory.EmpUser).ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task StaleAdminClaim_IsIgnored_WhenDbSaysEmployee()
    {
        // Token nói Admin, DB nói Employee → phải bị chặn. Nếu test này rớt nghĩa là admin bị hạ quyền
        // vẫn dùng được endpoint Admin-only cho tới khi token hết hạn (tối đa 365 ngày).
        using var client = NewClient(await TokenClaimingRoleAsync(AppRoles.Admin));
        var res = await client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task PromotionInDb_TakesEffect_WithoutReissuingToken()
    {
        // Chiều ngược lại: token cũ nói Employee, DB đã nâng lên Admin → phải vào được ngay, không cần
        // đăng nhập lại. Đây là điều kiện để "cấp quyền có hiệu lực ngay" mà không làm gián đoạn người dùng.
        var employeeToken = await TokenClaimingRoleAsync(AppRoles.Employee);
        try
        {
            await SetDbRoleAsync(AppRoles.Admin);
            using var client = NewClient(employeeToken);
            var res = await client.GetAsync("/api/users");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }
        finally
        {
            await SetDbRoleAsync(AppRoles.Employee); // trả lại trạng thái cho các test khác trong collection
        }
    }

    [Fact]
    public async Task ChangePassword_RevokesOtherDevices_ButKeepsCurrentOne()
    {
        const string currentSid = "__test_sid_current__";
        const string otherSid = "__test_sid_other__";
        var token = await _factory.EmployeeTokenAsync(currentSid);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            await using var conn = await db.OpenAsync();
            foreach (var sid in new[] { currentSid, otherSid })
                await conn.Cmd(
                    @"INSERT INTO user_sessions
                          (session_token, username, machine_name, started_at, last_seen, is_active, client_kind, revoked)
                      VALUES (@t, @u, 'Test', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, TRUE, 'Web', FALSE)
                      ON CONFLICT (session_token) DO UPDATE SET
                          username = EXCLUDED.username, revoked = FALSE, is_active = TRUE,
                          last_seen = CURRENT_TIMESTAMP")
                    .With("@t", sid).With("@u", _factory.EmpUser).ExecuteNonQueryAsync();
        }

        try
        {
            using var client = NewClient(token);
            var res = await client.PostAsJsonAsync("/api/auth/change-password",
                new { CurrentPassword = "test-pass", NewPassword = "test-pass-new" });
            Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            await using var conn = await db.OpenAsync();
            async Task<bool> RevokedAsync(string sid) => Convert.ToBoolean(
                await conn.Cmd("SELECT revoked FROM user_sessions WHERE session_token = @t")
                    .With("@t", sid).ExecuteScalarAsync());

            // Kẻ đang giữ token cũ trên máy khác bị đá...
            Assert.True(await RevokedAsync(otherSid));
            // ...còn chính người vừa đổi mật khẩu thì KHÔNG bị văng ra (trải nghiệm liền mạch).
            Assert.False(await RevokedAsync(currentSid));
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            await using var conn = await db.OpenAsync();
            await conn.Cmd("UPDATE app_users SET password_hash = @ph WHERE username = @u")
                .With("@ph", PasswordHasher.Hash("test-pass")).With("@u", _factory.EmpUser).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM user_sessions WHERE session_token = ANY(@t)")
                .With("@t", new[] { currentSid, otherSid }).ExecuteNonQueryAsync();
        }
    }
}
