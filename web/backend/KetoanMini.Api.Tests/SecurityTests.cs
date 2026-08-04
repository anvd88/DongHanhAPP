using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Endpoints;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Mọi test chạm DB nằm CHUNG một collection: xUnit chạy các class trong cùng collection TUẦN TỰ và
/// cấp cho chúng CHUNG một ApiFactory.
///
/// Trước đây mỗi class giữ ApiFactory riêng qua IClassFixture và các class chạy SONG SONG trên CÙNG một
/// DB test, nên test rớt ngẫu nhiên ở những chỗ không liên quan tới thay đổi đang làm:
///  • Dispose của fixture class này xóa mất tài khoản mà class khác đang dùng dở → 401/NullReference;
///  • các lệnh DELETE ... ON DELETE CASCADE chạy đồng thời khóa chéo nhau → 40P01 deadlock;
///  • nhiều host cùng dựng schema/EnsureTables một lúc.
/// Dùng chung một host cũng đỡ tốn: dựng 1 lần thay vì 13 lần.
///
/// QrLoginServiceTests là unit test thuần (không chạm DB) nên KHÔNG thuộc collection này và vẫn chạy song song.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api-db";
}

/// <summary>
/// Kiểm thử tích hợp cho các chốt bảo mật: 401 (chưa đăng nhập), 403 (sai vai trò), 403 (không sở hữu),
/// 429 (rate limit), security headers, CORS. Chạy toàn bộ app qua TestServer + PostgreSQL thật.
/// Cần biến môi trường ConnectionStrings__KetoanMini (local đã có; CI cấp qua service Postgres).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    // Mỗi test class nhận một ApiFactory RIÊNG (IClassFixture) và các class chạy SONG SONG trên CÙNG
    // một DB test. EmpUser từng là hằng dùng chung, nên Dispose của BẤT KỲ class nào cũng xóa
    // '__test_employee__' — kể cả class không hề dùng tới — trong khi SecurityTests đang dùng dở.
    // Tên riêng cho từng fixture: mỗi fixture chỉ xóa đúng tài khoản của mình.
    public string EmpUser { get; } = $"__test_emp_{Guid.NewGuid():N}__";
    // Nhiều test class tạo ApiFactory song song nhưng ChatEndpoints giữ blob root static trong cùng process;
    // dùng chung một thư mục theo PID để các host test không đổi root qua lại giữa request.
    private static readonly string ChatBlobDirectory =
        Path.Combine(Path.GetTempPath(), $"ketoanmini-chat-tests-{Environment.ProcessId}");
    // APK test cũng ra thư mục tạm (cùng lý do): không để tệp vài chục MB rơi vào cây mã nguồn.
    private static readonly string ReleaseBlobDirectory =
        Path.Combine(Path.GetTempPath(), $"ketoanmini-apk-tests-{Environment.ProcessId}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // Không ghi Windows Event Log từ TestServer: CI/sandbox không có quyền tạo event source.
        builder.ConfigureLogging(logging => logging.ClearProviders());
        // Không ghi key Data Protection vào AppData của máy chạy test. Provider chỉ sống trong fixture,
        // đủ để kiểm giao thức cookie/QR mà không hạ cấu hình lưu key của production.
        builder.ConfigureServices(services =>
            services.AddDataProtection().UseEphemeralDataProtectionProvider());
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:RequireHttps"] = "false",  // TestServer là http → tránh 307 redirect
                ["Security:SessionIdleDays"] = "0",   // không để phiên test hết hạn giữa chừng
                ["Security:KioskApiKey"] = "",        // gate kiosk tắt cho test
                ["Chat:BlobDirectory"] = ChatBlobDirectory,
                ["Releases:BlobDirectory"] = ReleaseBlobDirectory,
                ["QrScanner:AllowedHttpsHosts:0"] = "example.com",
                ["QrScanner:Actions:0:Id"] = "test-message",
                ["QrScanner:Actions:0:ExactValue"] = "__test_qr_message__",
                ["QrScanner:Actions:0:Kind"] = "message",
                ["QrScanner:Actions:0:Title"] = "Thông báo từ server",
                ["QrScanner:Actions:0:Message"] = "Không cần cập nhật APK.",
                ["QrScanner:Actions:1:Id"] = "test-link",
                ["QrScanner:Actions:1:ExactValue"] = "__test_qr_link__",
                ["QrScanner:Actions:1:Kind"] = "open_https_url",
                ["QrScanner:Actions:1:Title"] = "Trang server",
                ["QrScanner:Actions:1:Message"] = "Chỉ mở sau khi người dùng đồng ý.",
                ["QrScanner:Actions:1:Url"] = "https://example.com/qr-help",
                // DATABASE TEST RIÊNG: không bao giờ đụng dữ liệu dev/thật. App tự tạo DB + schema lúc khởi động.
                ["ConnectionStrings:KetoanMini"] = TestConnectionString(),
            });
        });
    }

    /// <summary>
    /// Chuỗi kết nối tới DATABASE TEST tách riêng. Lấy chuỗi gốc (biến môi trường ConnectionStrings__KetoanMini
    /// hoặc mặc định localhost) rồi ĐỔI tên database sang bản test — mặc định thêm hậu tố "_test", có thể ép
    /// bằng biến KETOANMINI_TEST_DB. Nhờ Database.EnsureDatabaseExistsAsync + PostgresSchema, DB test được tạo
    /// và dựng schema tự động ở lần chạy đầu, nên test hoàn toàn cô lập khỏi dữ liệu vận hành.
    /// </summary>
    public static string TestConnectionString()
    {
        var baseCs = Environment.GetEnvironmentVariable("ConnectionStrings__KetoanMini")
            ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=ketoanmini";
        var b = new Npgsql.NpgsqlConnectionStringBuilder(baseCs);
        var overrideDb = Environment.GetEnvironmentVariable("KETOANMINI_TEST_DB");
        b.Database = !string.IsNullOrWhiteSpace(overrideDb)
            ? overrideDb
            : (string.IsNullOrWhiteSpace(b.Database) ? "ketoanmini" : b.Database) + "_test";
        return b.ConnectionString;
    }

    /// <summary>
    /// HttpClient CƯ XỬ NHƯ TRÌNH DUYỆT: giữ cookie (mặc định của WebApplicationFactory) VÀ tự gắn
    /// lại cookie km_csrf vào header X-CSRF-Token cho mọi request ghi — đúng việc mà lib/api.ts làm
    /// ở frontend. Không có phần thứ hai thì mọi POST sau khi đăng nhập đều bị chốt CSRF trả 403.
    /// </summary>
    public HttpClient CreateBrowserClient()
        => CreateDefaultClient(new BrowserLikeHandler());

    /// <summary>
    /// Hũ cookie tối giản + tự gắn header CSRF. Cố ý KHÔNG dùng CreateClient(HandleCookies): nó không
    /// nhận DelegatingHandler tuỳ ý, mà phần "gắn lại cookie CSRF vào header" mới là thứ phân biệt một
    /// trình duyệt thật với một request trần — và cũng chính là thứ các test CSRF cần để có ý nghĩa.
    /// </summary>
    private sealed class BrowserLikeHandler : DelegatingHandler
    {
        private static readonly string[] SafeMethods = ["GET", "HEAD", "OPTIONS"];
        private readonly Dictionary<string, string> _jar = new(StringComparer.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_jar.Count > 0)
                request.Headers.TryAddWithoutValidation(
                    "Cookie", string.Join("; ", _jar.Select(kv => $"{kv.Key}={kv.Value}")));

            if (!SafeMethods.Contains(request.Method.Method)
                && _jar.TryGetValue(Security.AuthCookies.CsrfCookie, out var csrf))
                request.Headers.TryAddWithoutValidation(Security.AuthCookies.CsrfHeader, csrf);

            var response = await base.SendAsync(request, cancellationToken);

            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
                foreach (var raw in setCookies)
                {
                    var pair = raw.Split(';')[0];
                    var i = pair.IndexOf('=');
                    if (i <= 0) continue;
                    var name = pair[..i];
                    var value = pair[(i + 1)..];
                    // Giá trị rỗng = máy chủ đang XOÁ cookie (đăng xuất, phiên bị thu hồi).
                    if (string.IsNullOrEmpty(value)) _jar.Remove(name);
                    else _jar[name] = value;
                }

            return response;
        }
    }

    /// <summary>Tạo (idempotent) một tài khoản vai trò Employee đang hoạt động và trả về JWT hợp lệ.</summary>
    public async Task<string> EmployeeTokenAsync(string? sid = null)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        // RETURNING id: lấy id ngay trong chính lệnh ghi, không còn khe hở giữa INSERT và SELECT.
        var row = await conn.Cmd(
            @"INSERT INTO app_users
                 (id, username, full_name, email, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at, is_deleted)
              VALUES
                 (@id, @u, 'Test Employee', '', 'Employee', @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)
              ON CONFLICT (username) DO UPDATE SET is_active = TRUE, is_deleted = FALSE, role = 'Employee', approval_status = 'Approved'
              RETURNING id")
            .With("@id", Guid.NewGuid()).With("@u", EmpUser).With("@ph", PasswordHasher.Hash("test-pass"))
            .ExecuteScalarAsync();
        if (row is not Guid id)
            throw new InvalidOperationException($"Không tạo được tài khoản test '{EmpUser}': DB không trả về id.");

        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        return tokens.CreateToken(
            new UserDto(id, EmpUser, "Test Employee", "", "Employee", true, "Approved", DateTime.UtcNow), sid);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                using var scope = Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<Database>();
                using var conn = db.OpenAsync().GetAwaiter().GetResult();
                conn.Cmd("DELETE FROM app_users WHERE username = @u").With("@u", EmpUser)
                    .ExecuteNonQueryAsync().GetAwaiter().GetResult();
            }
            catch { /* dọn dẹp best-effort */ }
        }
        base.Dispose(disposing);
    }
}

[Collection(ApiCollection.Name)]
public sealed class SecurityTests
{
    private readonly ApiFactory _factory;
    public SecurityTests(ApiFactory factory) => _factory = factory;

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static bool HasHeader(HttpResponseMessage r, string name) =>
        r.Headers.Contains(name) || r.Content.Headers.Contains(name);

    [Theory]
    [InlineData("/api/documents")]
    [InlineData("/api/customers")]
    [InlineData("/api/dashboard")]
    [InlineData("/api/reports")]
    [InlineData("/api/giacong")]
    [InlineData("/api/users")]
    [InlineData("/api/payroll/salaries")]
    public async Task Unauthenticated_ProtectedEndpoint_Returns401(string path)
    {
        var res = await NewClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Theory]
    [InlineData("/api/documents")] // policy "Accounting" (Admin/Accounting) → Employee bị 403
    [InlineData("/api/giacong")]
    [InlineData("/api/users")]     // Admin-only → Employee bị 403
    public async Task Employee_WrongRole_Returns403(string path)
    {
        var client = NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.EmployeeTokenAsync());
        var res = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Employee_OtherEmployeePayroll_Returns403_Ownership()
    {
        var client = NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.EmployeeTokenAsync());
        // employeeId ngẫu nhiên (không phải của mình) → endpoint payroll kiểm tra ownership → 403.
        var res = await client.GetAsync($"/api/payroll/salaries/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task RapidLogin_HitsRateLimit_429()
    {
        var client = NewClient();
        var codes = new List<int>();
        for (var i = 0; i < 60; i++)
        {
            var res = await client.PostAsJsonAsync("/api/auth/login", new { username = "nobody", password = "x" });
            codes.Add((int)res.StatusCode);
        }
        Assert.Contains(429, codes);
    }

    [Fact]
    public async Task VerifyPassword_RequiresAuthentication()
    {
        var res = await NewClient().PostAsJsonAsync("/api/auth/verify-password", new { password = "test-pass" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task VerifyPassword_AcceptsOnlyCurrentAccountPassword()
    {
        var client = NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await _factory.EmployeeTokenAsync());

        var wrong = await client.PostAsJsonAsync("/api/auth/verify-password", new { password = "wrong" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var correct = await client.PostAsJsonAsync("/api/auth/verify-password", new { password = "test-pass" });
        Assert.Equal(HttpStatusCode.NoContent, correct.StatusCode);
        Assert.Contains("no-store", correct.Headers.CacheControl?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Responses_IncludeSecurityHeaders()
    {
        var res = await NewClient().GetAsync("/api/info");
        Assert.True(HasHeader(res, "X-Content-Type-Options"), "thiếu X-Content-Type-Options");
        Assert.True(HasHeader(res, "X-Frame-Options"), "thiếu X-Frame-Options");
        Assert.True(HasHeader(res, "Content-Security-Policy"), "thiếu Content-Security-Policy");
        Assert.True(HasHeader(res, "Referrer-Policy"), "thiếu Referrer-Policy");
    }

    [Fact]
    public async Task Cors_DisallowedOrigin_NotReflected()
    {
        var client = NewClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/info");
        req.Headers.Add("Origin", "https://evil.example");
        var res = await client.SendAsync(req);
        Assert.False(res.Headers.Contains("Access-Control-Allow-Origin"));
    }

    /// <summary>
    /// Trần payload phải nới ĐÚNG endpoint có trần riêng — không nới nhầm cho phần còn lại của API.
    /// </summary>
    [Theory]
    // Endpoint có trần riêng: web gọi "/api/releases/" nên cả hai dạng path phải ra trần APK.
    [InlineData("POST", "/api/releases", PayloadLimits.MaxApkBytes)]
    [InlineData("POST", "/api/releases/", PayloadLimits.MaxApkBytes)]
    [InlineData("POST", "/api/chat/conversations/6f1b1e6e-0000-4000-8000-000000000001/messages/42/upload",
        ChatEndpoints.MaxBlobBytes)]
    [InlineData("POST", "/api/qr/resolve", PayloadLimits.MaxQrActionBodyBytes)]
    [InlineData("POST", "/api/qr/decision", PayloadLimits.MaxQrActionBodyBytes)]
    // Phần còn lại giữ trần JSON, kể cả các path chat/releases KHÔNG phải upload.
    [InlineData("POST", "/api/chat/conversations/6f1b1e6e-0000-4000-8000-000000000001/messages/file",
        PayloadLimits.MaxJsonBodyBytes)]
    [InlineData("GET", "/api/releases", PayloadLimits.MaxJsonBodyBytes)]
    [InlineData("POST", "/api/chamcong/nhandien", PayloadLimits.MaxJsonBodyBytes)]
    public void MaxRequestBytesFor_UsesPerEndpointLimit(string method, string path, long expected)
        => Assert.Equal(expected, PayloadLimits.MaxRequestBytesFor(method, path));
}
