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
/// Khoá hợp đồng "phiên web nằm trong cookie HttpOnly, app native vẫn dùng Bearer".
///
/// Từng điểm ở đây đều là một cách làm hỏng cả đợt nâng cấp nếu ai đó sửa nhầm về sau:
///  • Trả token ra thân phản hồi cho trình duyệt ⇒ nó lại vào localStorage, XSS lại lấy được.
///  • Quên HttpOnly ⇒ JavaScript đọc được cookie, y như cũ.
///  • Bỏ chốt CSRF ⇒ trang lạ thao tác được thay người đang đăng nhập.
///  • Bỏ chốt Origin ở /hubs ⇒ trang lạ mở được WebSocket và nghe lén tín hiệu realtime.
///  • Đụng vào đường Bearer ⇒ gãy ứng dụng Android đang chạy ngoài thực tế.
/// </summary>
/// <summary>
/// Thuộc tính cookie (không cần CSDL). Cờ Secure bám theo scheme của request THẬT: production luôn
/// HTTPS (đã ép ở Program.cs, và UseForwardedHeaders khiến Request.IsHttps = true sau Cloudflare
/// Tunnel) nên cookie có Secure; dev chạy http://localhost thì không, nếu không trình duyệt vứt
/// cookie đi và không ai đăng nhập được ở máy lập trình.
/// </summary>
public sealed class AuthCookieAttributeTests
{
    private static string[] IssueAndRead(bool https)
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Request.Scheme = https ? "https" : "http";
        ctx.Request.Host = new Microsoft.AspNetCore.Http.HostString("app.ketoancp.click");
        AuthCookies.Issue(ctx, "jwt-gia-lap", DateTimeOffset.UtcNow.AddDays(7));
        return ctx.Response.Headers.SetCookie.ToArray()!;
    }

    [Fact]
    public void TrenHttps_CookieCoCoSecure()
    {
        var cookies = IssueAndRead(https: true);
        Assert.All(cookies, c => Assert.Contains("secure", c, StringComparison.OrdinalIgnoreCase));
        var auth = Assert.Single(cookies, c => c.StartsWith($"{AuthCookies.AuthCookie}=", StringComparison.Ordinal));
        Assert.Contains("httponly", auth, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrenHttpDev_KhongDatSecure_DeConDangNhapDuocOMayLapTrinh()
        => Assert.All(IssueAndRead(https: false),
            c => Assert.DoesNotContain("secure", c, StringComparison.OrdinalIgnoreCase));

    /// <summary>Origin lạ bị chặn, origin của chính hệ thống thì không — chốt chống Cross-Site
    /// WebSocket Hijacking phải phân biệt được hai thứ đó kể cả khi chạy sau reverse proxy.</summary>
    [Theory]
    [InlineData("https://app.ketoancp.click", true)]   // chính mình
    [InlineData("http://app.ketoancp.click", true)]    // chính mình, scheme lệch do proxy
    [InlineData("https://ke-gian.example", false)]
    [InlineData("https://app.ketoancp.click.ke-gian.example", false)] // tiền tố lừa mắt
    [InlineData("", true)]                             // không phải trình duyệt (app native)
    public void KiemTraOrigin(string origin, bool duocPhep)
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new Microsoft.AspNetCore.Http.HostString("app.ketoancp.click");
        if (origin.Length > 0) ctx.Request.Headers.Origin = origin;
        Assert.Equal(duocPhep, AuthCookies.IsAllowedOrigin(ctx, []));
    }
}

[Collection(ApiCollection.Name)]
public sealed class CookieSessionTests : IDisposable
{
    private readonly ApiFactory _factory;
    public CookieSessionTests(ApiFactory factory) => _factory = factory;

    private const string Password = "cookie-test-pass";

    // Tài khoản RIÊNG của bộ test này. Cả collection dùng chung một ApiFactory, nên nếu mượn
    // ApiFactory.EmpUser thì việc đặt lại mật khẩu ở đây sẽ làm hỏng các test chạy song song
    // (SecurityTests, TokenRoleFreshnessTests… đều dùng chung tài khoản đó).
    private readonly string _user = $"__test_cookie_{Guid.NewGuid():N}__";

    /// <summary>Tạo tài khoản test và đăng nhập bằng mật khẩu, trả về phản hồi thô để soi cookie.</summary>
    private async Task<(HttpClient Client, HttpResponseMessage Response)> LoginAsync(string? client = null)
    {
        await EnsureAccountAsync();
        var http = _factory.CreateBrowserClient();
        var sid = $"cookie-{Guid.NewGuid():N}";
        if (client is null) await BootstrapAsync(http, sid);
        var res = await http.PostAsJsonAsync("/api/auth/login",
            new { username = _user, password = Password, sid, client });
        return (http, res);
    }

    private static async Task<HttpResponseMessage> BootstrapAsync(HttpClient http, string sid)
    {
        var response = await http.PostAsJsonAsync("/api/auth/bootstrap", new { sid });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response;
    }

    private async Task EnsureAccountAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd(
            @"INSERT INTO app_users (id, username, full_name, email, role, password_hash, is_active,
                                     approval_status, approved_at, approved_by, created_at, is_deleted)
              VALUES (@id, @u, 'Cookie Test', '', 'Employee', @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test',
                      CURRENT_TIMESTAMP, FALSE)
              ON CONFLICT (username) DO UPDATE SET password_hash = EXCLUDED.password_hash,
                                                   is_active = TRUE, is_deleted = FALSE")
            .With("@id", Guid.NewGuid()).With("@u", _user)
            .With("@ph", PasswordHasher.Hash(Password)).ExecuteNonQueryAsync();
    }

    private static string[] SetCookies(HttpResponseMessage res)
        => res.Headers.TryGetValues("Set-Cookie", out var v) ? v.ToArray() : [];

    [Fact]
    public async Task Bootstrap_PhatCookieHttpOnlyNganHan_VaKhongTaoPhienTaiKhoan()
    {
        var http = _factory.CreateBrowserClient();
        var response = await BootstrapAsync(http, $"bootstrap-{Guid.NewGuid():N}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("ready").GetBoolean());
        Assert.Equal(LoginBootstrapService.Protocol, body.GetProperty("protocol").GetString());
        Assert.True(body.GetProperty("expiresAt").GetDateTime() > DateTime.UtcNow);
        Assert.True(response.Headers.CacheControl?.NoStore);

        var cookies = SetCookies(response);
        var bootstrap = Assert.Single(cookies,
            c => c.StartsWith($"{LoginBootstrapService.CookieName}=", StringComparison.Ordinal));
        Assert.Contains("httponly", bootstrap, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", bootstrap, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", bootstrap, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(cookies, c => c.StartsWith($"{AuthCookies.AuthCookie}=", StringComparison.Ordinal));
        Assert.DoesNotContain(cookies, c => c.StartsWith($"{AuthCookies.CsrfCookie}=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DangNhapWeb_ThieuHoacSaiBootstrap_BiTuChoiCungMotCach()
    {
        await EnsureAccountAsync();
        var missing = _factory.CreateBrowserClient();
        var without = await missing.PostAsJsonAsync("/api/auth/login",
            new { username = _user, password = Password, sid = "sid-without-bootstrap" });
        Assert.Equal((HttpStatusCode)428, without.StatusCode);
        Assert.Equal("login_bootstrap_required",
            (await without.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var wrongSid = _factory.CreateBrowserClient();
        await BootstrapAsync(wrongSid, "sid-a");
        var mismatched = await wrongSid.PostAsJsonAsync("/api/auth/login",
            new { username = _user, password = Password, sid = "sid-b" });
        Assert.Equal((HttpStatusCode)428, mismatched.StatusCode);
        Assert.Equal("login_bootstrap_required",
            (await mismatched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task SaiMatKhau_KhongLamMatVeBootstrapConHan()
    {
        await EnsureAccountAsync();
        var http = _factory.CreateBrowserClient();
        var sid = $"retry-{Guid.NewGuid():N}";
        await BootstrapAsync(http, sid);

        var wrong = await http.PostAsJsonAsync("/api/auth/login",
            new { username = _user, password = "sai-mat-khau", sid });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var correct = await http.PostAsJsonAsync("/api/auth/login",
            new { username = _user, password = Password, sid });
        Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
    }

    [Fact]
    public async Task DangNhapWeb_DatCookieHttpOnly_VaKhongTraTokenRaThanPhanHoi()
    {
        var (_, res) = await LoginAsync();
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.TryGetProperty("token", out var token) && token.ValueKind != JsonValueKind.Null,
            "Đăng nhập trên trình duyệt KHÔNG được trả JWT ra thân phản hồi.");

        var cookies = SetCookies(res);
        var auth = Assert.Single(cookies, c => c.StartsWith($"{AuthCookies.AuthCookie}=", StringComparison.Ordinal));
        Assert.Contains("httponly", auth, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", auth, StringComparison.OrdinalIgnoreCase);

        // Cookie CSRF thì CỐ Ý đọc được từ JavaScript — frontend phải gắn lại nó vào header.
        var csrf = Assert.Single(cookies, c => c.StartsWith($"{AuthCookies.CsrfCookie}=", StringComparison.Ordinal));
        Assert.DoesNotContain("httponly", csrf, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(cookies,
            c => c.StartsWith($"{LoginBootstrapService.CookieName}=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DangNhapApp_GiuNguyenBearer_KhongDatCookie()
    {
        var (_, res) = await LoginAsync(client: "apk");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("token").GetString()),
            "Ứng dụng Android vẫn phải nhận JWT trong thân phản hồi.");
        Assert.Empty(SetCookies(res));
    }

    [Fact]
    public async Task CookiePhien_DuDeGoiApi_KhongCanHeaderAuthorization()
    {
        var (http, _) = await LoginAsync();
        var res = await http.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task PhienWeb_HetHanNganHonNhieuSoVoiTokenApp()
    {
        var (_, appRes) = await LoginAsync(client: "apk");
        var appToken = (await appRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        var (_, webRes) = await LoginAsync();
        var webCookie = Assert.Single(SetCookies(webRes), c => c.StartsWith($"{AuthCookies.AuthCookie}=", StringComparison.Ordinal));
        var webToken = webCookie.Split(';')[0].Split('=', 2)[1];

        Assert.True(ExpiryOf(webToken) < ExpiryOf(appToken),
            "Phiên trình duyệt phải sống ngắn hơn token app (trình duyệt thường là máy dùng chung).");
    }

    private static DateTime ExpiryOf(string jwt)
        => new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(jwt).ValidTo;

    /// <summary>Đây chính là kịch bản CSRF: trình duyệt tự gửi cookie, nhưng trang lạ không có header.</summary>
    [Fact]
    public async Task RequestGhi_ThieuHeaderCsrf_BiTuChoi()
    {
        var (_, res) = await LoginAsync();
        var cookies = SetCookies(res);

        // Client "trần": mang cookie phiên như trình duyệt bị lừa, nhưng KHÔNG có header CSRF.
        var attacker = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        foreach (var c in cookies)
            attacker.DefaultRequestHeaders.Add("Cookie", c.Split(';')[0]);

        var write = await attacker.PostAsJsonAsync("/api/auth/heartbeat", new { sid = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);

        // Nhưng ĐỌC thì vẫn được — CSRF chỉ chặn thao tác GHI.
        Assert.Equal(HttpStatusCode.OK, (await attacker.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task RequestGhi_CoHeaderCsrfDung_DiQua()
    {
        var (http, _) = await LoginAsync(); // CreateBrowserClient tự gắn header, y như frontend
        var res = await http.PostAsJsonAsync("/api/auth/heartbeat", new { sid = "x" });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    [Fact]
    public async Task BearerCuaApp_KhongCanCsrf()
    {
        var (_, res) = await LoginAsync(client: "apk");
        var token = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();

        var app = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        app.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.NoContent,
            (await app.PostAsJsonAsync("/api/auth/heartbeat", new { sid = "x" })).StatusCode);
    }

    /// <summary>
    /// Cross-Site WebSocket Hijacking: WebSocket không bị CORS chặn, nên chốt duy nhất là Origin.
    /// Nếu test này rớt, một trang web lạ có thể mở kết nối realtime dưới danh nghĩa người đang đăng
    /// nhập và nghe lén tín hiệu (kể cả tin nhắn) của họ.
    /// </summary>
    [Fact]
    public async Task Hub_TuOriginLa_BiTuChoi()
    {
        var (http, _) = await LoginAsync();

        var evil = new HttpRequestMessage(HttpMethod.Post, "/hubs/changes/negotiate?negotiateVersion=1");
        evil.Headers.Add("Origin", "https://ke-gian.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(evil)).StatusCode);

        // Cùng origin thì vẫn phải thông (không thì realtime chết cả hệ thống).
        var ours = new HttpRequestMessage(HttpMethod.Post, "/hubs/changes/negotiate?negotiateVersion=1");
        ours.Headers.Add("Origin", "http://localhost");
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(ours)).StatusCode);
    }

    [Fact]
    public async Task DangXuat_XoaCookie_VaPhienHetHieuLuc()
    {
        var (http, _) = await LoginAsync();

        var logout = await http.PostAsJsonAsync("/api/auth/logout", new { sid = "x" });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(SetCookies(logout),
            c => c.StartsWith($"{AuthCookies.AuthCookie}=;", StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/auth/me")).StatusCode);
    }

    // ── Thu hồi thiết bị từ xa ───────────────────────────────────────────────────────────────────
    // Màn quản lý thiết bị nằm trong ỨNG DỤNG ANDROID (web không có màn này), nên chiều quan trọng
    // nhất là "app dùng Bearer thu hồi phiên web dùng cookie". Nếu chiều đó gãy thì người dùng mất
    // khả năng đá một trình duyệt lạ ra khỏi tài khoản mình — đúng lúc họ cần nó nhất.

    /// <summary>Đăng nhập thêm một phiên nữa cho CÙNG tài khoản, trả về client + sid của phiên đó.</summary>
    private async Task<(HttpClient Client, string Sid)> ExtraWebSessionAsync()
    {
        await EnsureAccountAsync();
        var sid = $"web-{Guid.NewGuid():N}";
        var http = _factory.CreateBrowserClient();
        await BootstrapAsync(http, sid);
        var res = await http.PostAsJsonAsync("/api/auth/login",
            new { username = _user, password = Password, sid });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (http, sid);
    }

    private async Task<(HttpClient Client, string Sid)> AppSessionAsync()
    {
        await EnsureAccountAsync();
        var sid = $"apk-{Guid.NewGuid():N}";
        var login = _factory.CreateBrowserClient();
        var res = await login.PostAsJsonAsync("/api/auth/login",
            new { username = _user, password = Password, sid, client = "apk" });
        var token = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();

        var app = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        app.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (app, sid);
    }

    [Fact]
    public async Task App_ThuHoiDuocPhienWeb_VaCookieChetBiXoaNgay()
    {
        var (web, webSid) = await ExtraWebSessionAsync();
        var (app, _) = await AppSessionAsync();

        Assert.Equal(HttpStatusCode.OK, (await web.GetAsync("/api/auth/me")).StatusCode);

        // App dùng Bearer nên KHÔNG cần header CSRF — nếu chốt CSRF lỡ áp cả cho Bearer thì test này đỏ.
        var revoke = await app.PostAsJsonAsync($"/api/auth/devices/{webSid}/revoke", new { });
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var after = await web.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        // Không xoá cookie chết thì trình duyệt cứ gửi lại nó tới ngày hết hạn → vòng 401 không lối ra.
        Assert.Contains(SetCookies(after),
            c => c.StartsWith($"{AuthCookies.AuthCookie}=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Web_ThuHoiDuocPhienApp()
    {
        var (web, _) = await LoginAsync();
        var (app, appSid) = await AppSessionAsync();

        Assert.Equal(HttpStatusCode.NoContent,
            (await web.PostAsJsonAsync($"/api/auth/devices/{appSid}/revoke", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.GetAsync("/api/auth/me")).StatusCode);
    }

    /// <summary>
    /// Thiếu chốt CSRF ở đây thì một trang web lạ có thể đá người đang đăng nhập ra khỏi MỌI thiết bị
    /// của họ chỉ bằng cách dụ họ mở một trang — phá hoại tài khoản mà không cần biết mật khẩu.
    /// </summary>
    [Fact]
    public async Task ThuHoiThietBi_ThieuCsrf_BiTuChoi()
    {
        var (_, res) = await LoginAsync();

        var attacker = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        foreach (var c in SetCookies(res))
            attacker.DefaultRequestHeaders.Add("Cookie", c.Split(';')[0]);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await attacker.PostAsJsonAsync("/api/auth/devices/revoke-all", new { })).StatusCode);
    }

    /// <summary>Thu hồi chỉ được đụng thiết bị CỦA CHÍNH MÌNH — sid là chuỗi client tự đặt, nên nếu
    /// câu lệnh quên lọc theo username thì đoán trúng sid là đá được người khác ra.</summary>
    [Fact]
    public async Task KhongThuHoiDuocThietBiCuaNguoiKhac()
    {
        var (victim, victimSid) = await ExtraWebSessionAsync();

        // Tài khoản KHÁC (EmpUser của fixture) thử thu hồi phiên của nạn nhân.
        var other = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        other.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await _factory.EmployeeTokenAsync());

        Assert.Equal(HttpStatusCode.NotFound,
            (await other.PostAsJsonAsync($"/api/auth/devices/{victimSid}/revoke", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await victim.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task DanhSachThietBi_ChiHienCuaChinhMinh()
    {
        var (web, sid) = await ExtraWebSessionAsync();

        // Phiên của một tài khoản KHÁC, phải không được lọt vào danh sách.
        var otherSid = $"other-{Guid.NewGuid():N}";
        var other = _factory.CreateBrowserClient();
        await _factory.EmployeeTokenAsync(); // đảm bảo tài khoản kia tồn tại
        await BootstrapAsync(other, otherSid);
        var otherLogin = await other.PostAsJsonAsync("/api/auth/login",
            new { username = _factory.EmpUser, password = "test-pass", sid = otherSid });
        // Nếu lần đăng nhập này hỏng thì chẳng có phiên nào của người khác để mà lọt vào danh sách,
        // và test sẽ xanh một cách vô nghĩa. Chốt lại để nó luôn kiểm đúng thứ nó nói là kiểm.
        Assert.Equal(HttpStatusCode.OK, otherLogin.StatusCode);

        var devices = (await web.GetFromJsonAsync<JsonElement>("/api/auth/devices"))
            .EnumerateArray().ToArray();
        var sids = devices.Select(d => d.GetProperty("sid").GetString()).ToArray();

        Assert.Contains(sid, sids);
        Assert.DoesNotContain(otherSid, sids);
        // Phiên đang gọi phải tự đánh dấu "current" thì giao diện mới cảnh báo đúng lúc người dùng
        // sắp thu hồi chính thiết bị mình đang ngồi.
        Assert.Contains(devices, d => d.GetProperty("sid").GetString() == sid && d.GetProperty("current").GetBoolean());
        Assert.All(devices, d => Assert.False(
            d.GetProperty("current").GetBoolean() && d.GetProperty("sid").GetString() != sid));
    }

    [Fact]
    public async Task RevokeAll_DaSachMoiThietBi_KeCaPhienDangGoi()
    {
        var (web1, _) = await ExtraWebSessionAsync();
        var (web2, _) = await ExtraWebSessionAsync();
        var (app, _) = await AppSessionAsync();

        Assert.Equal(HttpStatusCode.NoContent,
            (await web1.PostAsJsonAsync("/api/auth/devices/revoke-all", new { })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await web2.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await web1.GetAsync("/api/auth/me")).StatusCode);
    }

    /// <summary>Dọn tài khoản riêng của bộ test để DB test không phình ra sau mỗi lần chạy.</summary>
    public void Dispose()
    {
        try
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            using var conn = db.OpenAsync().GetAwaiter().GetResult();
            conn.Cmd("DELETE FROM user_sessions WHERE username = @u").With("@u", _user)
                .ExecuteNonQueryAsync().GetAwaiter().GetResult();
            conn.Cmd("DELETE FROM app_users WHERE username = @u").With("@u", _user)
                .ExecuteNonQueryAsync().GetAwaiter().GetResult();
        }
        catch { /* dọn dẹp best-effort, giống ApiFactory.Dispose */ }
    }
}
