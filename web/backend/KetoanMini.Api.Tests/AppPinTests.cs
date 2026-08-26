using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Mã bảo mật 6 số của ứng dụng nằm Ở MÁY CHỦ, thiết bị không giữ bản sao nào. Bộ test này chốt đúng
/// những tính chất khiến việc chuyển lên máy chủ là NÂNG CẤP bảo mật chứ không chỉ đổi chỗ lưu:
///  • chỉ lưu hash Argon2id (CSDL lộ cũng không đọc ra mã, không dò rẻ được);
///  • đổi mã bắt buộc nhập đúng mã cũ (không có đường vòng qua /verify);
///  • đếm sai + khoá tăng dần theo TÀI KHOẢN (cài lại app không reset được);
///  • quên mã phải xác minh mật khẩu tài khoản, và client không có đường tự xoá mã.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AppPinTests
{
    private readonly ApiFactory _factory;
    public AppPinTests(ApiFactory factory) => _factory = factory;

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<HttpClient> EmployeeClientAsync()
    {
        var client = NewClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await _factory.EmployeeTokenAsync());
        await ClearPinAsync();
        return client;
    }

    /// <summary>Xoá mã của tài khoản test để mỗi test bắt đầu từ trạng thái sạch (kể cả bộ đếm khoá).</summary>
    private async Task ClearPinAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("DELETE FROM app_pin_codes WHERE username = @u").With("@u", _factory.EmpUser)
            .ExecuteNonQueryAsync();
    }

    private async Task<string> StoredHashAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        return Convert.ToString(await conn.Cmd("SELECT pin_hash FROM app_pin_codes WHERE username = @u")
            .With("@u", _factory.EmpUser).ExecuteScalarAsync()) ?? "";
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage res)
        => JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task AppPin_Endpoints_RequireAuthentication()
    {
        var client = NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/app-pin")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/app-pin", new { pin = "487213" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/app-pin/verify", new { pin = "487213" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/app-pin/reset", new { password = "test-pass" })).StatusCode);
    }

    [Fact]
    public async Task AppPin_CreateThenVerify_TracksStateOnServer()
    {
        var client = await EmployeeClientAsync();

        var before = await BodyAsync(await client.GetAsync("/api/auth/app-pin"));
        Assert.False(before.GetProperty("hasPin").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/auth/app-pin", new { pin = "487213" })).StatusCode);

        var after = await BodyAsync(await client.GetAsync("/api/auth/app-pin"));
        Assert.True(after.GetProperty("hasPin").GetBoolean());
        Assert.Equal(0, after.GetProperty("lockedForSeconds").GetInt64());

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/auth/app-pin/verify", new { pin = "487213" })).StatusCode);

        var wrong = await client.PostAsJsonAsync("/api/auth/app-pin/verify", new { pin = "487214" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var body = await BodyAsync(wrong);
        Assert.Equal("pin_incorrect", body.GetProperty("code").GetString());
        Assert.Equal(4, body.GetProperty("attemptsBeforeLock").GetInt32());

        // Nhập đúng lại thì bộ đếm sai về 0 — người gõ nhầm một lần không bị dồn tới mốc khoá.
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/auth/app-pin/verify", new { pin = "487213" })).StatusCode);
        var reset = await BodyAsync(await client.GetAsync("/api/auth/app-pin"));
        Assert.Equal(5, reset.GetProperty("attemptsBeforeLock").GetInt32());

        await ClearPinAsync();
    }

    [Fact]
    public async Task AppPin_StoredAsArgon2Hash_NeverPlainText()
    {
        var client = await EmployeeClientAsync();
        await client.PostAsJsonAsync("/api/auth/app-pin", new { pin = "487213" });

        var hash = await StoredHashAsync();
        Assert.StartsWith("ARGON2ID$", hash, StringComparison.Ordinal);
        Assert.DoesNotContain("487213", hash, StringComparison.Ordinal);
        Assert.True(PasswordHasher.Verify("487213", hash));
        Assert.False(PasswordHasher.Verify("487214", hash));

        await ClearPinAsync();
    }

    [Theory]
    [InlineData("000000")]
    [InlineData("123456")]
    [InlineData("654321")]
    public async Task AppPin_RejectsObviousCodes(string pin)
    {
        var client = await EmployeeClientAsync();
        var res = await client.PostAsJsonAsync("/api/auth/app-pin", new { pin });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("pin_too_obvious", (await BodyAsync(res)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    public async Task AppPin_RejectsMalformedCodes(string pin)
    {
        var client = await EmployeeClientAsync();
        var res = await client.PostAsJsonAsync("/api/auth/app-pin", new { pin });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("pin_invalid", (await BodyAsync(res)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task AppPin_Change_RequiresCurrentPin()
    {
        var client = await EmployeeClientAsync();
        await client.PostAsJsonAsync("/api/auth/app-pin", new { pin = "487213" });

        // Không kèm mã cũ → bị từ chối như một lần nhập sai (nếu không, đây là đường đổi mã tự do).
        var noCurrent = await client.PostAsJsonAsync("/api/auth/app-pin", new { pin = "562914" });
        Assert.Equal(HttpStatusCode.BadRequest, noCurrent.StatusCode);
        Assert.Equal("pin_incorrect", (await BodyAsync(noCurrent)).GetProperty("code").GetString());

        var wrongCurrent = await client.PostAsJsonAsync("/api/auth/app-pin",
            new { pin = "562914", currentPin = "487999" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/app-pin",
            new { pin = "562914", currentPin = "487213" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/auth/app-pin/verify", new { pin = "562914" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/app-pin/verify", new { pin = "487213" })).StatusCode);

        await ClearPinAsync();
    }

    [Fact]
    public async Task AppPin_FiveWrongAttempts_LockAccountNotDevice()
    {
        var client = await EmployeeClientAsync();
        await client.PostAsJsonAsync("/api/auth/app-pin", new { pin = "487213" });

        for (var i = 0; i < 4; i++)
        {
            var attempt = await client.PostAsJsonAsync("/api/auth/app-pin/verify", new { pin = "487214" });
            Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode);
        }

        var fifth = await client.PostAsJsonAsync("/api/auth/app-pin/verify", new { pin = "487214" });
        Assert.Equal(HttpStatusCode.Locked, fifth.StatusCode);
        var locked = await BodyAsync(fifth);
        Assert.Equal("pin_locked", locked.GetProperty("code").GetString());
        Assert.InRange(locked.GetProperty("lockedForSeconds").GetInt64(), 1, 30);

        // Khoá gắn với TÀI KHOẢN ở máy chủ: một client hoàn toàn mới (như cài lại app, xoá dữ liệu,
        // hay đổi sang máy khác) vẫn bị chặn — điều mà bộ đếm nằm trong máy không làm được.
        var freshDevice = NewClient();
        freshDevice.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await _factory.EmployeeTokenAsync());
        var correctOnFreshDevice = await freshDevice.PostAsJsonAsync("/api/auth/app-pin/verify", new { pin = "487213" });
        Assert.Equal(HttpStatusCode.Locked, correctOnFreshDevice.StatusCode);

        var status = await BodyAsync(await freshDevice.GetAsync("/api/auth/app-pin"));
        Assert.True(status.GetProperty("lockedForSeconds").GetInt64() > 0);

        await ClearPinAsync();
    }

    [Fact]
    public async Task AppPin_Reset_NeedsAccountPassword_AndClearsLock()
    {
        var client = await EmployeeClientAsync();
        await client.PostAsJsonAsync("/api/auth/app-pin", new { pin = "487213" });

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/app-pin/reset", new { password = "sai-mat-khau" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongPassword.StatusCode);
        Assert.True((await BodyAsync(await client.GetAsync("/api/auth/app-pin"))).GetProperty("hasPin").GetBoolean());

        var reset = await client.PostAsJsonAsync("/api/auth/app-pin/reset", new { password = "test-pass" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var after = await BodyAsync(await client.GetAsync("/api/auth/app-pin"));
        Assert.False(after.GetProperty("hasPin").GetBoolean());
        Assert.Equal(0, after.GetProperty("lockedForSeconds").GetInt64());

        // Không còn mã thì /verify phải nói rõ "chưa tạo" để app mời tạo mã mới, chứ không im lặng cho qua.
        var verify = await client.PostAsJsonAsync("/api/auth/app-pin/verify", new { pin = "487213" });
        Assert.Equal(HttpStatusCode.Conflict, verify.StatusCode);
        Assert.Equal("pin_not_set", (await BodyAsync(verify)).GetProperty("code").GetString());
    }
}
