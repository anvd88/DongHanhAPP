using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using KetoanMini.Api.Data;
using KetoanMini.Api.Endpoints;
using KetoanMini.Api.Json;
using KetoanMini.Api.Realtime;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Bí mật (khóa JWT, chuỗi kết nối DB, khóa mã hóa…) KHÔNG để trong mã nguồn: đọc từ
// appsettings.Local.json (đã .gitignore) rồi cho biến môi trường ghi đè lên trên cùng.
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// Blob voice phải nằm ngoài bin/Debug|Release để dotnet clean/build không làm mất lịch sử âm thanh.
// Production nên trỏ Chat:BlobDirectory tới volume dữ liệu cố định và đưa thư mục này vào backup.
ChatEndpoints.ConfigureBlobDirectory(builder.Configuration["Chat:BlobDirectory"], builder.Environment.ContentRootPath);

// APK các bản phát hành nằm trên đĩa (không nhét vào DB/RAM). Cũng phải nằm ngoài bin/ và được backup:
// mất thư mục này là mất mọi bản đã phát hành. Production nên trỏ Releases:BlobDirectory tới volume dữ liệu.
ReleaseStorage.Configure(builder.Configuration["Releases:BlobDirectory"], builder.Environment.ContentRootPath);

ProductionSecurityValidator.Validate(builder);

// Mặc định chặn request quá lớn trước khi model binding cấp phát bộ nhớ. Endpoint upload APK
// được nâng giới hạn riêng nhưng vẫn có trần cứng 200 MB.
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = PayloadLimits.MaxJsonBodyBytes);

// DateOnly tuần tự hóa dạng "yyyy-MM-dd" để khớp với input date của trình duyệt.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    // Mốc thời gian lưu UTC → luôn xuất kèm 'Z' để client hiển thị đúng giờ địa phương.
    o.SerializerOptions.Converters.Add(new UtcDateTimeConverter());
});

// Contract API may doc: OpenAPI JSON la nguon chuan cho web/APK va kiem thu contract.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new() { Title = "Đồng Hành API", Version = "v1" });
    o.AddSecurityDefinition("Bearer", new()
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "JWT access token"
    });
    o.AddSecurityRequirement(new()
    {
        [new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>()
    });
});

// Cho phép form multipart lớn (tải APK 40–150MB qua trang cập nhật ứng dụng). Giới hạn body cứng
// của Kestrel đã được gỡ riêng cho endpoint tải APK trong ReleaseEndpoints.UploadRelease.
builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = PayloadLimits.MaxApkBytes;
});

builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<TokenService>();
// Phiên đăng nhập QR chỉ sống 5 phút trong RAM: poll trạng thái không tạo tải lên PostgreSQL.
// Đăng ký cùng một instance vừa làm service nghiệp vụ vừa tự dọn phiên hết hạn ở nền.
builder.Services.AddSingleton<QrLoginService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<QrLoginService>());
// Giao thức QR server-driven: vé quyết định được Data Protection mã hóa, tự chứa và không cần cache
// RAM. Các action message/link HTTPS đọc bằng IOptionsMonitor nên đổi cấu hình server không cần build APK.
builder.Services.AddDataProtection();
builder.Services.Configure<QrScannerOptions>(builder.Configuration.GetSection("QrScanner"));
builder.Services.AddSingleton<QrActionTokenService>();
builder.Services.AddSingleton<QrConfiguredActionRegistry>();
// Mã hóa dữ liệu nhạy cảm khi lưu trữ (embedding khuôn mặt…) — khóa từ Security:FieldEncryptionKey.
builder.Services.AddSingleton<FieldCipher>();

// HttpClient (dùng gọi API Cloudflare TURN cấp credential cho WebRTC).
builder.Services.AddHttpClient();

// Thông báo đẩy tức thì qua Firebase Cloud Messaging (tắt an toàn nếu chưa cấu hình Firebase:CredentialsPath).
builder.Services.AddSingleton<PushService>();

// Bộ máy nhận diện khuôn mặt cho chấm công: YuNet + căn chỉnh 5 điểm + AdaFace R50 ONNX Runtime.
// Engine dựng lười ở lần gọi /api/chamcong đầu tiên nên lỗi model không làm sập API lúc khởi động.
builder.Services.AddSingleton<IFaceEngine, AdaFaceR50Engine>();

// Active-flash liveness: sinh + xác minh chuỗi màu chống giả mạo chủ động (lưu tạm challenge trong RAM).
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<FlashLivenessChallenge>();
// Vòng đệm số đo Silent-Face gần nhất (hiển thị lên panel để hiệu chỉnh ngưỡng chống ảnh/màn hình).
builder.Services.AddSingleton<LivenessMetricsLog>();

// Tín hiệu real-time: hub WebSocket + dịch vụ nền theo dõi thay đổi DB.
builder.Services.AddSignalR();
// Định danh kết nối hub theo username để phát tín hiệu chat đúng thành viên (Clients.Users).
builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, NameUserIdProvider>();
builder.Services.AddHostedService<ChangeWatcher>();
// Dọn tệp "giữ tạm" (gửi tệp qua LAN khi người nhận offline) đã quá hạn khỏi đĩa.
builder.Services.AddHostedService<LanFileCleanupService>();

var jwt = builder.Configuration.GetSection("Jwt");

// Bảo vệ: không cho chạy với khóa JWT mặc định/yếu. Production → dừng hẳn để buộc cấu hình khóa
// thật; Development → tự sinh khóa ngẫu nhiên tạm thời (token mất hiệu lực sau mỗi lần khởi động
// lại) kèm cảnh báo, để lập trình viên vẫn chạy được mà không rò rỉ khóa trong mã nguồn.
{
    const string insecureKey = "doi-chuoi-bi-mat-nay-thanh-mot-gia-tri-ngau-nhien-dai-it-nhat-32-ky-tu";
    var key = jwt["Key"];
    if (string.IsNullOrWhiteSpace(key) || key.Length < 32 || key == insecureKey)
    {
        const string msg = "Jwt:Key dang dung gia tri mac dinh/khong an toan. Hay dat khoa ngau nhien >=32 ky tu qua bien moi truong Jwt__Key hoac appsettings.Local.json.";
        if (builder.Environment.IsProduction())
            throw new InvalidOperationException(msg);
        builder.Configuration["Jwt:Key"] =
            Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
        Console.Error.WriteLine("[CANH BAO BAO MAT] " + msg + " Da tao khoa tam thoi cho Development.");
    }
}

// Ép HTTPS: cần biết cổng HTTPS để chuyển hướng đúng (mặc định 5443 theo Kestrel:Endpoints).
var httpsPort = builder.Configuration.GetValue<int?>("Security:HttpsPort") ?? 5443;
builder.Services.AddHttpsRedirection(o => o.HttpsPort = httpsPort);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!)),
        };
        // WebSocket không gửi header Authorization được → SignalR truyền token qua query "access_token".
        // Đọc token đó cho riêng đường hub /hubs để định danh kết nối (chat nhắm đúng người).
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    ctx.Token = accessToken;
                }
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Accounting", p => p.RequireRole(AppRoles.Admin, AppRoles.Accounting));
    options.AddPolicy("HR", p => p.RequireRole(AppRoles.Admin, AppRoles.Hr));
    options.AddPolicy("Workforce", p => p.RequireRole(AppRoles.Admin, AppRoles.Hr, AppRoles.Employee, AppRoles.Accounting));
    options.AddPolicy("Kiosk", p => p.RequireRole(AppRoles.Admin, AppRoles.Kiosk));
});

static string RateLimitKey(HttpContext context)
    => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Quá nhiều yêu cầu. Vui lòng thử lại sau." }, cancellationToken);
    };
    // Giới hạn theo IP. LƯU Ý: sau Cloudflare Tunnel + NAT văn phòng, CẢ văn phòng thường DÙNG CHUNG
    // một IP công khai → hạn mức phải đủ rộng cho nhiều nhân viên đầu giờ/đổi ca (đăng nhập đồng loạt,
    // reconnect realtime hàng loạt) mà vẫn chặn được flood. Vẫn dừng brute-force/DoS ở mức hàng trăm/phút.
    options.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitKey(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 40, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
    // Xác minh lại mật khẩu để khôi phục PIN cục bộ. PIN còn có khóa thử sai ngay trên thiết bị;
    // giới hạn này bảo vệ thêm endpoint mật khẩu mà vẫn đủ rộng cho nhiều nhân viên chung NAT.
    options.AddPolicy("reauth", http => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitKey(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 40, Window = TimeSpan.FromMinutes(5), QueueLimit = 0, AutoReplenishment = true
        }));
    options.AddPolicy("qr-start", http => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitKey(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
    // Poll chỉ tra ConcurrentDictionary trong RAM. Hạn mức rộng để nhiều người cùng dùng sau NAT văn
    // phòng nhưng vẫn có trần chống flood; frontend poll mỗi 1,5 giây (~40 lần/phút/màn hình).
    options.AddPolicy("qr-poll", http => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitKey(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1_200, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
    options.AddPolicy("qr-confirm", http => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitKey(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
    // Resolver tổng quát dùng 2 request cho một lần đăng nhập (resolve + decision) và còn phục vụ các
    // QR thông tin khác. Nâng trần riêng để nhiều điện thoại sau cùng NAT văn phòng không chặn nhau.
    options.AddPolicy("qr-action", http => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitKey(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
    options.AddPolicy("face-reset", http => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitKey(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 8, Window = TimeSpan.FromMinutes(10), QueueLimit = 0, AutoReplenishment = true
        }));
    options.AddPolicy("attendance", http => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitKey(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 90, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
    options.AddPolicy("signalr", http => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitKey(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
});

// CORS: chỉ cho phép origin trong Cors:Origins (domain thật) + origin cục bộ/LAN (dev). KHÔNG còn
// "cho phép tất cả". Production phục vụ cùng origin nên request thật không đụng CORS — đây là lớp
// phòng thủ để trang web lạ không gọi được API.
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(origin => CorsPolicy.IsAllowed(origin, corsOrigins)).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// OpenAPI JSON is available in every environment for contract automation. Interactive UI is
// intentionally limited to Development to reduce production attack surface.
app.UseSwagger();
if (app.Environment.IsDevelopment()) app.UseSwaggerUI();

// Nhắc cấu hình khóa kiosk: chưa đặt Security:KioskApiKey ⇒ endpoint chấm công ẩn danh (/nhandien,
// /cham) vẫn MỞ cho mọi nơi (kể cả qua tunnel). Cảnh báo để không quên khóa khi đã public.
if (!KioskAccess.IsEnforced(app.Configuration))
    app.Logger.LogWarning("[BAO MAT] Chua dat Security:KioskApiKey — endpoint cham cong an danh dang MO. Dat khoa de siet khi mo ra Internet.");

// Chạy sau reverse proxy (Cloudflare Tunnel → cloudflared trỏ vào http://localhost:5239).
// Đọc X-Forwarded-Proto do Cloudflare gắn (=https) để Request.Scheme = https → KHÔNG bị
// UseHttpsRedirection đá vòng lặp, mà vẫn ép HTTPS cho request http thật (không có header này).
// Chỉ tin proxy chạy trên loopback (chính cloudflared cùng máy) để tránh giả mạo header từ LAN.
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeaders.KnownNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
forwardedHeaders.KnownProxies.Add(IPAddress.Loopback);
forwardedHeaders.KnownProxies.Add(IPAddress.IPv6Loopback);
app.UseForwardedHeaders(forwardedHeaders);

// Security headers cho MỌI phản hồi (kể cả file tĩnh SPA). Bật/tắt qua Security:SecurityHeaders.
// CSP để trong Security:ContentSecurityPolicy (đổi được không cần build lại) — mặc định đủ chặt mà
// KHÔNG phá app: script tự host (Vite, không inline) + WASM (MediaPipe) qua 'wasm-unsafe-eval';
// style/ảnh/camera (inline style, data:, blob:) được cho phép. Nếu luồng camera/nhận diện lỗi vì CSP,
// nới script-src thêm 'unsafe-eval' trong cấu hình.
if (app.Configuration.GetValue("Security:SecurityHeaders", true))
{
    var csp = app.Configuration["Security:ContentSecurityPolicy"];
    if (string.IsNullOrWhiteSpace(csp))
        csp = "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; " +
              "img-src 'self' data: blob: https:; media-src 'self' blob: https:; font-src 'self' data:; " +
              "style-src 'self' 'unsafe-inline'; script-src 'self' 'wasm-unsafe-eval'; " +
              "worker-src 'self' blob:; connect-src 'self' https: wss:; form-action 'self'";
    app.Use(async (ctx, next) =>
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["X-Frame-Options"] = "DENY";
        h["Content-Security-Policy"] = csp;
        await next();
    });
}

// Tắt hoàn toàn reset mật khẩu bằng khuôn mặt ngoài Development. Middleware chạy trước model
// binding nên không xử lý ảnh hay tiết lộ tài khoản có tồn tại hay không.
app.Use(async (ctx, next) =>
{
    if (!app.Environment.IsDevelopment() &&
        ctx.Request.Path.Equals("/api/auth/forgot-password-face", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next();
});

// Trả 413 rõ ràng khi Content-Length đã vượt trần. Request chunked vẫn bị Kestrel chặn bởi
// MaxRequestBodySize; các endpoint nhận payload lớn (APK, blob chat) tự đặt trần riêng trong handler
// trước khi đọc body. Trần ở đây phải theo TỪNG endpoint (PayloadLimits.MaxRequestBytesFor): áp trần
// JSON 16MB cho tất cả thì payload hợp lệ bị chính chốt này trả 413 trước khi handler kịp chạy.
app.Use(async (ctx, next) =>
{
    var max = PayloadLimits.MaxRequestBytesFor(ctx.Request.Method, ctx.Request.Path);
    if (ctx.Request.ContentLength is > 0 && ctx.Request.ContentLength > max)
    {
        ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        await ctx.Response.WriteAsJsonAsync(new { message = $"Payload vượt giới hạn {max} byte." });
        return;
    }
    await next();
});

// Ép HTTPS (mã hóa dữ liệu khi truyền tải). Cấu hình qua Security:RequireHttps (mặc định bật).
// HSTS chỉ bật ngoài môi trường Development để tránh "ghim" HTTPS cho localhost của dự án khác.
if (app.Configuration.GetValue("Security:RequireHttps", true))
{
    if (!app.Environment.IsDevelopment()) app.UseHsts();
    app.UseHttpsRedirection();
}

// Phục vụ frontend đã build (wwwroot) — gộp chung 1 cổng với API.
app.UseDefaultFiles();
// Khai báo MIME cho tài nguyên nhận diện mặt phía client (MediaPipe): .tflite là kiểu lạ nên
// static files mặc định sẽ trả 404; .wasm map sẵn cho chắc để FaceDetector nạp được.
var staticContentTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
staticContentTypes.Mappings[".tflite"] = "application/octet-stream";
staticContentTypes.Mappings[".task"] = "application/octet-stream"; // model FaceLandmarker (chống giả mạo chớp mắt)
staticContentTypes.Mappings[".wasm"] = "application/wasm";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = staticContentTypes });

app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Số ngày một phiên được phép "nhàn rỗi" trước khi hết hiệu lực (đăng nhập bền cho người dùng năng
// động, nhưng phiên bỏ không dùng quá lâu thì tự vô hiệu → phải đăng nhập lại). 0 = tắt (giữ 365 ngày).
var sessionIdleDays = app.Configuration.GetValue("Security:SessionIdleDays", 7);

// Chặn ngay tài khoản vừa bị admin khóa/xóa (từ web HAY app desktop). JWT có thể còn hạn
// nhiều giờ, nên nếu chỉ dựa vào hết hạn token thì người dùng web vẫn thao tác được sau khi
// bị khóa. Kiểm tra is_active mỗi request đã xác thực → trả 401 để client tự đăng xuất.
// Kết hợp tín hiệu realtime "changed" (đổi is_active) → web refetch → bị đá ra gần như tức thì.
// ĐỒNG THỜI: phiên nhàn rỗi > sessionIdleDays (last_seen quá cũ) → 401 (tự hết hạn); mỗi request có
// hoạt động sẽ làm mới last_seen (giới hạn ghi 2 phút/lần) nên người dùng năng động không bị đá ra.
app.Use(async (ctx, next) =>
{
    // Endpoint AllowAnonymous phải thực sự độc lập với Bearer cũ. Nếu trình duyệt còn JWT của tài
    // khoản đã xóa/hết phiên, middleware kiểm tra phiên không được chặn vòng poll QR công khai.
    var allowsAnonymous = ctx.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
    if (!allowsAnonymous && ctx.User.Identity?.IsAuthenticated == true)
    {
        var username = ctx.User.Username();
        if (!string.IsNullOrEmpty(username))
        {
            var locked = false;
            var revoked = false;
            var idleExpired = false;
            // sid (nếu token có) → kiểm tra thiết bị đã bị thu hồi từ xa hay chưa.
            var sid = ctx.User.FindFirst("sid")?.Value;
            try
            {
                var db = ctx.RequestServices.GetRequiredService<Database>();
                await using var conn = await db.OpenAsync(ctx.RequestAborted);
                var sessionAlive = false;
                await using (var r = await conn.Cmd(
                    @"SELECT u.is_active, COALESCE(s.revoked, FALSE) AS revoked,
                             (s.session_token IS NOT NULL AND s.last_seen IS NOT NULL
                              AND @idleDays > 0
                              AND s.last_seen < CURRENT_TIMESTAMP - make_interval(days => @idleDays)) AS idle_expired
                      FROM app_users u
                      LEFT JOIN user_sessions s ON s.session_token = @sid
                      WHERE u.username = @u AND u.is_deleted = FALSE
                      LIMIT 1")
                    .With("@u", username)
                    .With("@sid", (object?)sid ?? DBNull.Value)
                    .With("@idleDays", sessionIdleDays)
                    .ExecuteReaderAsync(ctx.RequestAborted))
                {
                    if (!await r.ReadAsync(ctx.RequestAborted))
                        locked = true; // không còn dòng sống (đã xóa)
                    else
                    {
                        locked = r.IsDBNull(0) || !Convert.ToBoolean(r.GetValue(0));
                        revoked = !r.IsDBNull(1) && Convert.ToBoolean(r.GetValue(1));
                        idleExpired = !r.IsDBNull(2) && Convert.ToBoolean(r.GetValue(2));
                        sessionAlive = !locked && !revoked && !idleExpired && !string.IsNullOrEmpty(sid);
                    }
                }

                // Làm mới last_seen khi có hoạt động (giới hạn 2 phút/lần để không ghi mỗi request).
                // TRỪ vòng poll nền của app (WorkManager ~15 phút, chạy cả khi app đã đóng): nó không
                // phải người dùng đang dùng app, tính vào đây thì phiên không bao giờ hết hạn nhàn rỗi.
                // Client tự gắn cờ này; kẻ gian có giả cờ cũng chỉ làm phiên CỦA MÌNH hết hạn sớm hơn.
                if (sessionAlive && !ctx.Request.Headers.ContainsKey("X-Background-Poll"))
                    await conn.Cmd(
                        @"UPDATE user_sessions SET last_seen = CURRENT_TIMESTAMP
                          WHERE session_token = @sid AND last_seen < CURRENT_TIMESTAMP - INTERVAL '2 minutes'")
                        .With("@sid", sid!).ExecuteNonQueryAsync(ctx.RequestAborted);
            }
            catch
            {
                // DB chập chờn → không đá người dùng ra (fail-open), giống heartbeat của app desktop.
            }

            if (locked)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new { message = "Tài khoản đã bị khóa." });
                return;
            }
            if (revoked)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new { message = "Thiết bị này đã bị thu hồi. Vui lòng đăng nhập lại." });
                return;
            }
            if (idleExpired)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new { message = "Phiên đăng nhập đã hết hạn do lâu không hoạt động. Vui lòng đăng nhập lại." });
                return;
            }
        }
    }

    await next();
});

// Bắt lỗi DB không kết nối được → trả JSON chung (KHÔNG lộ chi tiết ngoại lệ ra client); ghi log server.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (NpgsqlException ex)
    {
        app.Logger.LogError(ex, "Loi ket noi PostgreSQL khi xu ly {Path}", ctx.Request.Path);
        ctx.Response.StatusCode = 503;
        await ctx.Response.WriteAsJsonAsync(new { message = "Khong ket noi duoc co so du lieu PostgreSQL." });
    }
});

// Chỉ cho phép request nội bộ (loopback/LAN) — chặn dò từ Internet qua tunnel.
static bool IsInternalRequest(HttpContext ctx)
{
    var ip = ctx.Connection.RemoteIpAddress;
    if (ip is null) return false;
    if (IPAddress.IsLoopback(ip)) return true;
    if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
    var b = ip.GetAddressBytes();
    if (b.Length == 4)
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    return (b[0] & 0xFE) == 0xFC || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80);
}

app.MapGet("/api/info", () => Results.Ok(new { app = "Đồng Hành API", status = "ok" }));
// Health-check DÀNH CHO GIÁM SÁT NỘI BỘ: chỉ trả từ loopback/LAN (404 với client ngoài), không lộ chi tiết lỗi.
app.MapGet("/api/health", async (Database db, HttpContext ctx) =>
{
    if (!IsInternalRequest(ctx)) return Results.NotFound();
    try { await using var c = await db.OpenAsync(); return Results.Ok(new { db = "connected" }); }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Health-check DB that bai");
        return Results.Json(new { db = "error" }, statusCode: 503);
    }
});

try { await PostgresSchema.EnsureAsync(app.Services.GetRequiredService<Database>(), app.Configuration, app.Logger); }
catch (Exception ex) { app.Logger.LogWarning("Khong khoi tao duoc schema PostgreSQL luc khoi dong: {Msg}", ex.Message); }

// Bản phát hành cũ còn nằm trong cột bytea → chuyển ra đĩa (một lần, đọc theo khúc). Phải chạy SAU
// khi schema có cột has_apk và TRƯỚC khi phục vụ request để app không thấy bản "đã đăng mà không tải được".
try { await ReleaseStorage.MigrateDatabaseBlobsAsync(app.Services.GetRequiredService<Database>(), app.Logger); }
catch (Exception ex) { app.Logger.LogWarning("Khong chuyen duoc APK tu DB ra dia: {Msg}", ex.Message); }

app.MapAuth();
app.MapQrActions();
app.MapAccounting();
app.MapAudit();
app.MapGiaCong();
app.MapChamCong();
app.MapUsers();
app.MapReleases();
app.MapPreferences();
app.MapChat();
app.MapFeedback();
app.MapHr();
app.MapRequests();
app.MapWorklist();
app.MapTasks();
app.MapNotifications();
app.MapShifts();
app.MapTimesheet();
app.MapPenalties();
app.MapPenaltyRefunds();
app.MapPayoutVouchers();
app.MapPayroll();
app.MapBankAccounts();
app.MapAppConfig();
app.MapPortal();
app.MapTalent();
app.MapSurveys();
app.MapDirectory();
app.MapSchedule();
app.MapHelp();

// Hub tín hiệu real-time (web + desktop kết nối tới đây).
app.MapHub<ChangesHub>("/hubs/changes")
    .RequireAuthorization(p => p.RequireRole(AppRoles.All))
    .RequireRateLimiting("signalr");

// SPA fallback: mọi route không phải /api và không phải file tĩnh → trả index.html
// để React Router xử lý (deep-link /dashboard, /giacong… reload không 404).
app.MapFallbackToFile("index.html");

// Tạo bảng gia công nếu chưa có (best-effort, không chặn khởi động nếu DB tạm thời offline).
try { await GiaCongEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Không tạo được bảng gia công lúc khởi động: {Msg}", ex.Message); }

// Tạo bảng chấm công khuôn mặt nếu chưa có (best-effort).
try { await ChamCongEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Không tạo được bảng chấm công lúc khởi động: {Msg}", ex.Message); }

// Mã hóa AES các mẫu khuôn mặt cũ còn ở dạng thô (chạy một lần, no-op nếu đã mã hóa/thiếu khóa).
try { await ChamCongEndpoints.EncryptExistingEmbeddings(app.Services.GetRequiredService<Database>(), app.Services.GetRequiredService<FieldCipher>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong ma hoa duoc embedding cu luc khoi dong: {Msg}", ex.Message); }

try { await PreferenceEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang tuy chon nguoi dung luc khoi dong: {Msg}", ex.Message); }

try { await AuthEndpoints.EnsureAvatarTable(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang anh dai dien luc khoi dong: {Msg}", ex.Message); }

try { await ChatEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang tro chuyen luc khoi dong: {Msg}", ex.Message); }

try { await FeedbackEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang phan hoi luc khoi dong: {Msg}", ex.Message); }

// Nền tảng nhân sự phải tạo TRƯỚC (đơn từ & ca làm tham chiếu hr_employees).
try { await HrEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang nhan su luc khoi dong: {Msg}", ex.Message); }

try { await RequestEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang don tu luc khoi dong: {Msg}", ex.Message); }

// Giao viec & nghiem thu — sau hr_employees (danh sach nguoi nhan viec).
try { await TaskAssignmentEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang giao viec luc khoi dong: {Msg}", ex.Message); }

try { await NotificationEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang token thiet bi luc khoi dong: {Msg}", ex.Message); }

try { await ShiftEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang ca lam luc khoi dong: {Msg}", ex.Message); }

try { await PenaltyEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang phat/ky luat luc khoi dong: {Msg}", ex.Message); }

try { await PenaltyRefundEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang hoan tien phat luc khoi dong: {Msg}", ex.Message); }

// Sau bang hoan tien phat va hr_employees: phieu chi tham chieu ca hai.
try { await PayoutVoucherEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang phieu chi luc khoi dong: {Msg}", ex.Message); }

try { await PayrollEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang bang luong luc khoi dong: {Msg}", ex.Message); }

try { await BankAccountEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang tai khoan ngan hang luc khoi dong: {Msg}", ex.Message); }

try { await AppConfigEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang cau hinh ung dung luc khoi dong: {Msg}", ex.Message); }

try { await AuditEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong nang cap duoc bang nhat ky luc khoi dong: {Msg}", ex.Message); }

try { await PortalEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang cong thong tin luc khoi dong: {Msg}", ex.Message); }

try { await TalentEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang phat trien nhan su luc khoi dong: {Msg}", ex.Message); }

try { await SurveyEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang khao sat luc khoi dong: {Msg}", ex.Message); }

try { await HelpEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang FAQ tro giup luc khoi dong: {Msg}", ex.Message); }

app.Run();

// Cho phép dự án kiểm thử tích hợp dùng WebApplicationFactory<Program> (top-level statements).
public partial class Program;
