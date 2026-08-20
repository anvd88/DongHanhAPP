using System.Net;
using System.Security.Claims;
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
    o.SwaggerDoc("v1", new() { Title = "KetoanMini API", Version = "v1" });
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
// Phiếu xuất kho được điền vào workbook mẫu rồi Microsoft Excel gửi thẳng tới máy in mặc định của máy chủ.
builder.Services.AddSingleton<WarehouseVoucherPrintService>();
// Phiên đăng nhập QR chỉ sống 5 phút trong RAM: poll trạng thái không tạo tải lên PostgreSQL.
// Đăng ký cùng một instance vừa làm service nghiệp vụ vừa tự dọn phiên hết hạn ở nền.
builder.Services.AddSingleton<QrLoginService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<QrLoginService>());
// Giao thức QR server-driven: vé quyết định được Data Protection mã hóa, tự chứa và không cần cache
// RAM. Các action message/link HTTPS đọc bằng IOptionsMonitor nên đổi cấu hình server không cần build APK.
builder.Services.AddDataProtection();
builder.Services.AddSingleton<LoginBootstrapService>();
builder.Services.Configure<QrScannerOptions>(builder.Configuration.GetSection("QrScanner"));
builder.Services.AddSingleton<QrActionTokenService>();
builder.Services.AddSingleton<QrConfiguredActionRegistry>();
// Mã hóa dữ liệu nhạy cảm khi lưu trữ (embedding khuôn mặt…) — khóa từ Security:FieldEncryptionKey.
builder.Services.AddSingleton<FieldCipher>();

// NGUỒN PHÂN QUYỀN THỐNG NHẤT (vai trò + quyền + phạm vi dữ liệu + giao diện mặc định), đọc thẳng
// từ CSDL. Xem Security/AccessProfileService.cs — mọi quyết định phân quyền phải đi qua đây.
builder.Services.AddSingleton<AccessProfileService>();

// HttpClient (dùng gọi API Cloudflare TURN cấp credential cho WebRTC).
builder.Services.AddHttpClient();

// Thông báo đẩy tức thì qua Firebase Cloud Messaging (tắt an toàn nếu chưa cấu hình Firebase:CredentialsPath).
// Hàng chờ bền cho việc-có-hậu-quả: endpoint chỉ ghi một dòng, worker mới gọi FCM. Xem OutboxQueue.
builder.Services.AddSingleton<OutboxQueue>();
builder.Services.AddSingleton<PushService>();
builder.Services.AddSingleton<IOutboxHandler, PushOutboxHandler>();
builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddSingleton<AttendanceReminderService>();
builder.Services.AddHostedService<AttendanceReminderWorker>();

// Bộ máy nhận diện khuôn mặt cho chấm công: YuNet + căn chỉnh 5 điểm + AdaFace R50 ONNX Runtime.
// Development dựng lười ở request đầu; Production chủ động nạp lúc khởi động để kiểm chứng đủ model.
builder.Services.AddSingleton<IFaceEngine, AdaFaceR50Engine>();

// Bộ nhớ đệm RAM dùng chung (token xác nhận chấm công, và các thứ tạm thời khác).
builder.Services.AddMemoryCache();
// Token xác nhận chấm công: giữ kết quả xem trước để bước "Xác nhận" khỏi nhận diện lại (xem
// Services/AttendancePreviewTokens.cs — tiết kiệm một nửa khối lượng suy luận mỗi lượt chấm).
builder.Services.AddSingleton<AttendancePreviewTokens>();
// Vòng đệm số đo Silent-Face gần nhất (hiển thị lên panel để hiệu chỉnh ngưỡng chống ảnh/màn hình).
builder.Services.AddSingleton<LivenessMetricsLog>();

// Tín hiệu real-time: hub WebSocket + dịch vụ nền theo dõi thay đổi DB.
builder.Services.AddSignalR();
// Định danh kết nối hub theo username để phát tín hiệu chat đúng thành viên (Clients.Users).
builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, NameUserIdProvider>();
// HIỆN DIỆN ONLINE qua chính kết nối SignalR (thay nhịp tim HTTP 45s cho web; app foreground cũng
// dùng): hub đánh dấu online khi kết nối, service nền làm tươi last_seen theo lô mỗi 45s cho các phiên
// đang mở socket. Xem HubPresenceRegistry.
builder.Services.AddSingleton<HubPresenceRegistry>();
builder.Services.AddHostedService<HubPresenceRefresher>();
builder.Services.AddHostedService<ChangeWatcher>();
// Dọn tệp "giữ tạm" (gửi tệp qua LAN khi người nhận offline) đã quá hạn khỏi đĩa.
builder.Services.AddHostedService<LanFileCleanupService>();
// Enforce the 14-day retention limit for encrypted biometric templates awaiting HR verification.
// The worker sweeps immediately at host start and hourly afterwards, independent of API traffic.
builder.Services.AddHostedService<FaceEnrollmentCleanupService>();

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
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                // Thứ tự tìm token, dừng ở cái đầu tiên có:
                //  1. Header Authorization — ỨNG DỤNG ANDROID (giữ nguyên, không đụng gì).
                //  2. Cookie HttpOnly km_auth — TRÌNH DUYỆT. JavaScript không đọc được cookie này,
                //     nên XSS không mang được phiên đăng nhập ra khỏi máy. Cookie cũng tự đi theo
                //     WebSocket handshake của SignalR nên hub không cần token trên URL nữa.
                //  3. Query "access_token" cho /hubs — CHỈ còn cho app native (WebSocket của app
                //     không gắn được header Authorization). Web không dùng đường này nữa: token
                //     nằm trên URL sẽ lọt vào log máy chủ, proxy và lịch sử trình duyệt.
                if (!string.IsNullOrEmpty(ctx.Request.Headers.Authorization)) return Task.CompletedTask;

                var cookie = ctx.Request.Cookies[AuthCookies.AuthCookie];
                if (!string.IsNullOrEmpty(cookie))
                {
                    ctx.Token = cookie;
                    return Task.CompletedTask;
                }

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
    // MỘT policy cho MỖI quyền trong Security/Permissions.cs. Endpoint chốt cửa bằng .RequirePermission(...)
    // chứ không liệt kê tên vai trò — thêm vai trò mới chỉ cần sửa bảng vai trò→quyền, không sửa endpoint.
    //
    // Đã GỠ các policy theo tên vai trò ("Accounting", "HR", "Workforce", "Kiosk"): chúng buộc mỗi lần
    // thêm vai trò phải đi sửa từng chỗ liệt kê, và không nói lên endpoint đó bảo vệ ĐIỀU GÌ.
    options.AddPermissionPolicies();
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
    // Mỗi lần mở trang Login có đúng một bootstrap tiền xác thực. Dùng quota riêng, đủ rộng cho cả
    // văn phòng chung NAT và không ăn vào quota thử mật khẩu của policy "login".
    options.AddPolicy("login-bootstrap", http => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitKey(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 180, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
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

// Production phải nạp và kiểm chứng đầy đủ Silent-Face TRƯỚC khi nhận request. Nếu thiếu/hỏng model,
// dừng triển khai ngay; runtime inference cũng fail-closed (điểm live = 0) nếu phát sinh lỗi về sau.
ProductionSecurityValidator.ValidateFaceEngine(
    app.Environment,
    app.Services.GetRequiredService<IFaceEngine>());

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
              "worker-src 'self' blob:; frame-src 'self' blob:; connect-src 'self' https: wss:; form-action 'self'";
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
var staticFileOptions = new StaticFileOptions
{
    ContentTypeProvider = staticContentTypes,
    OnPrepareResponse = context =>
    {
        // index.html always points to the newest hashed bundles. Do not cache the SPA shell,
        // so a normal refresh after restart/build loads the latest frontend without Ctrl+F5.
        if (string.Equals(context.File.Name, "index.html", StringComparison.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Context.Response.Headers.Pragma = "no-cache";
            context.Context.Response.Headers.Expires = "0";
        }
    }
};
app.UseStaticFiles(staticFileOptions);

app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();

// ── CHỐNG CSRF cho phiên bằng cookie ─────────────────────────────────────────────────────────────
// Cookie được trình duyệt gửi TỰ ĐỘNG, nên một trang web lạ có thể ép trình duyệt của người đang
// đăng nhập gọi API của ta. Double-submit: header X-CSRF-Token phải khớp cookie km_csrf — trang lạ
// gửi được cookie nhưng KHÔNG ĐỌC được nó (khác origin) nên không dựng nổi header khớp.
//
// Chỉ áp cho request xác thực BẰNG COOKIE: ứng dụng Android gửi Bearer (không phải ambient
// credential, trang lạ không có cách nào bắt app gắn header đó) nên không cần và không bị ảnh hưởng.
// Cũng bỏ qua mọi phương thức chỉ-đọc (GET/HEAD/OPTIONS).
if (AuthCookies.Enabled(app.Configuration))
{
    app.Use(async (ctx, next) =>
    {
        if (AuthCookies.UsesCookieAuth(ctx))
        {
            // ── /hubs: chốt bằng ORIGIN, không bằng CSRF token ──────────────────────────────────
            // WebSocket KHÔNG bị CORS chặn, nên với phiên cookie thì trang web lạ có thể mở kết nối
            // realtime dưới danh nghĩa người đang đăng nhập và nghe lén tín hiệu của họ
            // (Cross-Site WebSocket Hijacking). Header Origin do trình duyệt tự đặt, JavaScript không
            // sửa được, nên nó chặn đúng thứ cần chặn — cho CẢ negotiate (POST, có Origin khi chéo
            // site) LẪN WebSocket handshake (GET, không gắn được header tuỳ ý nên CSRF token bất lực).
            //
            // Vì Origin đã bao trùm, /hubs không đi qua chốt CSRF: bắt SignalR tự gắn header cho
            // negotiate là thứ rất dễ vỡ (kết nối dựng một lần rồi thử lại mãi với header đã cũ).
            if (ctx.Request.Path.StartsWithSegments("/hubs"))
            {
                if (!AuthCookies.IsAllowedOrigin(ctx, corsOrigins))
                {
                    app.Logger.LogWarning("Chan ket noi hub tu origin la: {Origin}", ctx.Request.Headers.Origin.ToString());
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await ctx.Response.WriteAsJsonAsync(new { message = "Origin không được phép.", code = "origin_denied" });
                    return;
                }
                await next();
                return;
            }

            if (!AuthCookies.ValidateCsrf(ctx))
            {
                app.Logger.LogWarning("Chan request thieu/sai CSRF token: {Method} {Path}",
                    ctx.Request.Method, ctx.Request.Path);
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new
                {
                    message = "Yêu cầu không hợp lệ (thiếu mã chống giả mạo). Hãy tải lại trang và thử lại.",
                    code = "csrf_required"
                });
                return;
            }
        }
        await next();
    });
}

// Số ngày một phiên được phép "nhàn rỗi" trước khi hết hiệu lực (đăng nhập bền cho người dùng năng
// động, nhưng phiên bỏ không dùng quá lâu thì tự vô hiệu → phải đăng nhập lại). 0 = tắt (giữ 365 ngày).
var sessionIdleDays = app.Configuration.GetValue("Security:SessionIdleDays", 7);

// Chặn ngay tài khoản vừa bị admin khóa/xóa (từ web HAY app desktop). JWT có thể còn hạn
// nhiều giờ, nên nếu chỉ dựa vào hết hạn token thì người dùng web vẫn thao tác được sau khi
// bị khóa. Kiểm tra is_active mỗi request đã xác thực → trả 401 để client tự đăng xuất.
// Kết hợp tín hiệu realtime "changed" (đổi is_active) → web refetch → bị đá ra gần như tức thì.
// ĐỒNG THỜI: phiên nhàn rỗi > sessionIdleDays (last_seen quá cũ) → 401 (tự hết hạn); mỗi request có
// hoạt động sẽ làm mới last_seen (giới hạn ghi 2 phút/lần) nên người dùng năng động không bị đá ra.
//
// VỊ TRÍ TRONG PIPELINE RẤT QUAN TRỌNG: middleware này phải nằm GIỮA UseAuthentication và
// UseAuthorization. AuthorizationMiddleware chấm policy (RequireRole...) ngay tại vị trí của nó, nên
// nếu đặt sau UseAuthorization thì role trong JWT đã được dùng để quyết định xong xuôi rồi — làm tươi
// lúc đó là vô nghĩa. Đứng trước UseAuthorization thì claim vai trò được thay bằng dữ liệu DB kịp lúc.
app.Use(async (ctx, next) =>
{
    // Endpoint AllowAnonymous phải thực sự độc lập với Bearer cũ. Nếu trình duyệt còn JWT của tài
    // khoản đã xóa/hết phiên, middleware kiểm tra phiên không được chặn vòng poll QR công khai.
    var allowsAnonymous = ctx.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
    // Riêng các endpoint kiosk AllowAnonymous vẫn phải kiểm chứng JWT nếu request có gửi JWT; nếu
    // không, token của tài khoản vừa khóa/xóa sẽ bị KioskAccess hiểu nhầm là thiết bị hợp lệ.
    var requiresFreshKioskIdentity = ctx.Request.Path.Equals("/api/chamcong/nhandien")
        || ctx.Request.Path.Equals("/api/chamcong/cham")
        || ctx.Request.Path.Equals("/api/chamcong/trangthai");
    if ((!allowsAnonymous || requiresFreshKioskIdentity) && ctx.User.Identity?.IsAuthenticated == true)
    {
        var username = ctx.User.Username();
        if (requiresFreshKioskIdentity && string.IsNullOrEmpty(username))
        {
            AuthCookies.Clear(ctx);
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(new
            {
                message = "Phiên đăng nhập không có danh tính hợp lệ. Vui lòng đăng nhập lại."
            });
            return;
        }
        if (!string.IsNullOrEmpty(username))
        {
            var locked = false;
            var revoked = false;
            var idleExpired = false;
            var sessionInactive = false;
            var accountStateVerified = false;
            var sessionAlive = false;
            // Vai trò HIỆN HÀNH đọc từ DB (chính + phụ) để thay cho claim trong JWT — xem phần ghi
            // claim bên dưới. null = chưa đọc được (DB lỗi) → giữ nguyên claim cũ, không đá người dùng.
            List<string>? freshRoles = null;
            // sid (nếu token có) → kiểm tra thiết bị đã bị thu hồi từ xa hay chưa.
            var sid = ctx.User.FindFirst("sid")?.Value;
            try
            {
                var db = ctx.RequestServices.GetRequiredService<Database>();
                await using var conn = await db.OpenAsync(ctx.RequestAborted);
                await using (var r = await conn.Cmd(
                    @"SELECT u.is_active,
                             s.session_token IS NOT NULL AS session_exists,
                             COALESCE(s.is_active, FALSE) AS session_active,
                             COALESCE(s.revoked, FALSE) AS revoked,
                             (s.session_token IS NOT NULL AND s.last_seen IS NOT NULL
                              AND @idleDays > 0
                              AND s.last_seen < CURRENT_TIMESTAMP - make_interval(days => @idleDays)) AS idle_expired,
                             u.role,
                             COALESCE((SELECT string_agg(ur.role, ',' ORDER BY ur.role)
                                       FROM user_roles ur
                                       WHERE ur.username = u.username
                                         AND (ur.expires_at IS NULL OR ur.expires_at > CURRENT_TIMESTAMP)), '') AS secondary_roles
                      FROM app_users u
                      LEFT JOIN user_sessions s
                        ON s.session_token = @sid AND s.username = u.username
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
                        var sessionExists = !r.IsDBNull(1) && Convert.ToBoolean(r.GetValue(1));
                        var sessionActive = !r.IsDBNull(2) && Convert.ToBoolean(r.GetValue(2));
                        sessionInactive = sessionExists && !sessionActive;
                        revoked = !r.IsDBNull(3) && Convert.ToBoolean(r.GetValue(3));
                        idleExpired = !r.IsDBNull(4) && Convert.ToBoolean(r.GetValue(4));
                        sessionAlive = !locked && sessionExists && sessionActive && !revoked && !idleExpired;

                        // Gộp vai trò chính + phụ theo đúng cách TokenService dựng claim lúc đăng nhập
                        // (chính thiếu/không hợp lệ thì coi là Employee) để hai đường cho kết quả giống nhau.
                        freshRoles = AccessProfileService.Combine(
                            r.IsDBNull(5) ? null : r.GetString(5),
                            r.IsDBNull(6) ? "" : r.GetString(6));
                    }
                    accountStateVerified = true;
                }

                // Làm mới last_seen khi có hoạt động (giới hạn 2 phút/lần để không ghi mỗi request).
                // TRỪ vòng poll nền của app (WorkManager ~15 phút, chạy cả khi app đã đóng): nó không
                // phải người dùng đang dùng app, tính vào đây thì phiên không bao giờ hết hạn nhàn rỗi.
                // Client tự gắn cờ này; kẻ gian có giả cờ cũng chỉ làm phiên CỦA MÌNH hết hạn sớm hơn.
                if (sessionAlive && !ctx.Request.Headers.ContainsKey("X-Background-Poll"))
                    await conn.Cmd(
                        @"UPDATE user_sessions SET last_seen = CURRENT_TIMESTAMP
                          WHERE session_token = @sid AND username = @u
                            AND is_active = TRUE AND revoked = FALSE
                            AND last_seen < CURRENT_TIMESTAMP - INTERVAL '2 minutes'")
                        .With("@sid", sid!).With("@u", username)
                        .ExecuteNonQueryAsync(ctx.RequestAborted);
            }
            catch
            {
                // DB chập chờn → không đá người dùng ra (fail-open), giống heartbeat của app desktop.
            }

            // Phiên không còn hiệu lực → XOÁ LUÔN cookie. Không xoá thì trình duyệt cứ gửi lại cookie
            // chết ở mọi request cho tới ngày hết hạn, và người dùng thấy một vòng 401 không lối ra.
            async Task Reject(string message)
            {
                AuthCookies.Clear(ctx);
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new { message });
            }

            if (locked) { await Reject("Tài khoản đã bị khóa."); return; }
            if (revoked) { await Reject("Thiết bị này đã bị thu hồi. Vui lòng đăng nhập lại."); return; }
            if (idleExpired) { await Reject("Phiên đăng nhập đã hết hạn do lâu không hoạt động. Vui lòng đăng nhập lại."); return; }
            if (sessionInactive) { await Reject("Phiên đăng nhập đã kết thúc. Vui lòng đăng nhập lại."); return; }
            if (requiresFreshKioskIdentity && !accountStateVerified)
            {
                ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await ctx.Response.WriteAsJsonAsync(new
                {
                    message = "Không kiểm chứng được trạng thái tài khoản; chấm công đã được khóa an toàn."
                });
                return;
            }
            if (requiresFreshKioskIdentity && !sessionAlive)
            {
                await Reject("Phiên đăng nhập không còn hiệu lực. Vui lòng đăng nhập lại.");
                return;
            }
            if (sessionAlive)
                ctx.Items[KioskAccess.FreshAccountItem] = true;

            // VAI TRÒ LẤY TỪ DB, KHÔNG TIN CLAIM TRONG JWT. Token sống tới 365 ngày, nên nếu cứ tin
            // claim thì admin bị hạ quyền vẫn qua được mọi endpoint RequireRole("Admin") cho tới khi
            // token hết hạn. Thay claim ở đây => cấp/thu quyền có hiệu lực ngay từ request kế tiếp mà
            // KHÔNG phải đăng xuất ai — người dùng không hề bị gián đoạn.
            // DB lỗi (freshRoles == null) thì giữ nguyên claim cũ, đồng bộ với hướng fail-open ở trên.
            if (ctx.User.Identity is ClaimsIdentity identity)
            {
                // Claim QUYỀN luôn được dựng lại từ đầu và CHỈ từ dữ liệu DB vừa đọc. Nó cố tình
                // KHÔNG có trong JWT: token cũ không thể mang theo quyền cũ, và khi DB không đọc được
                // (freshRoles == null) thì request này KHÔNG có quyền nào ⇒ mọi endpoint chốt bằng
                // .RequirePermission(...) trả 403 (đóng mặc định) thay vì tin vào quyền cũ.
                foreach (var stalePerm in identity.FindAll(Permissions.ClaimType).ToList())
                    identity.TryRemoveClaim(stalePerm);

                if (freshRoles is not null)
                {
                    foreach (var stale in identity.FindAll(identity.RoleClaimType).ToList())
                        identity.TryRemoveClaim(stale);
                    foreach (var role in freshRoles)
                        identity.AddClaim(new Claim(identity.RoleClaimType, role));
                    foreach (var perm in Permissions.For(freshRoles))
                        identity.AddClaim(new Claim(Permissions.ClaimType, perm));
                }
            }

            // GIA HẠN TRƯỢT phiên web. Cookie sống ngắn (mặc định 7 ngày) để phiên bỏ quên trên máy
            // dùng chung tự chết, nhưng người dùng ĐANG LÀM VIỆC thì không được đá ra giữa chừng.
            // Chỉ cấp lại khi đã đi qua nửa vòng đời — tránh viết Set-Cookie ở mọi phản hồi.
            // KHÔNG gia hạn cho vòng poll nền: nó không phải người dùng đang dùng máy, cùng lý do với
            // last_seen ở trên (nếu không, một tab bỏ quên sẽ tự giữ phiên sống mãi).
            if (AuthCookies.Enabled(app.Configuration)
                && AuthCookies.UsesCookieAuth(ctx)
                && !ctx.Request.Headers.ContainsKey("X-Background-Poll")
                && ctx.User.FindFirst("exp")?.Value is { } expRaw
                && long.TryParse(expRaw, out var expUnix))
            {
                var tokens = ctx.RequestServices.GetRequiredService<TokenService>();
                var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expUnix);
                var full = TimeSpan.FromHours(AuthCookies.WebExpireHours(app.Configuration));
                if (expiresAt - DateTimeOffset.UtcNow < full / 2)
                    AuthCookies.Refresh(ctx, tokens.RenewWebToken(ctx.User), tokens.WebExpiresAt());
            }
        }
    }

    await next();
});

// Chấm quyền SAU khi claim vai trò đã được làm tươi từ DB ở middleware trên.
app.UseAuthorization();

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

app.MapGet("/api/info", () => Results.Ok(new { app = "KetoanMini Web API", status = "ok" }));
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

// Schema danh tính là điều kiện an toàn bắt buộc. Không được chạy API trên schema cũ/mơ hồ khi
// migration tài khoản thất bại; ghi log Critical rồi để host dừng để bộ giám sát/triển khai rollback.
try { await PostgresSchema.EnsureAsync(app.Services.GetRequiredService<Database>(), app.Configuration, app.Logger); }
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Khong khoi tao duoc schema PostgreSQL bat buoc; dung khoi dong de dong mac dinh.");
    throw;
}

// Bản phát hành cũ còn nằm trong cột bytea → chuyển ra đĩa (một lần, đọc theo khúc). Phải chạy SAU
// khi schema có cột has_apk và TRƯỚC khi phục vụ request để app không thấy bản "đã đăng mà không tải được".
try { await ReleaseStorage.MigrateDatabaseBlobsAsync(app.Services.GetRequiredService<Database>(), app.Logger); }
catch (Exception ex) { app.Logger.LogWarning("Khong chuyen duoc APK tu DB ra dia: {Msg}", ex.Message); }

app.MapAuth();
app.MapQrActions();
app.MapAccounting();
app.MapCoreAccounting();
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
    .RequireAuthorization()
    .RequireRateLimiting("signalr");

// SPA fallback: mọi route không phải /api và không phải file tĩnh → trả index.html
// để React Router xử lý (deep-link /dashboard, /giacong… reload không 404).
app.MapFallbackToFile("index.html", staticFileOptions);

// Tạo bảng gia công nếu chưa có (best-effort, không chặn khởi động nếu DB tạm thời offline).
try { await GiaCongEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Không tạo được bảng gia công lúc khởi động: {Msg}", ex.Message); }

// Tạo bảng chấm công khuôn mặt nếu chưa có (best-effort).
try { await ChamCongEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Không tạo được bảng chấm công lúc khởi động: {Msg}", ex.Message); }

// Mã hóa AES các mẫu khuôn mặt cũ còn ở dạng thô (chạy một lần, no-op nếu đã mã hóa/thiếu khóa).
try
{
    await ChamCongEndpoints.EncryptExistingEmbeddings(
        app.Services.GetRequiredService<Database>(),
        app.Services.GetRequiredService<FieldCipher>());
}
catch (Exception ex) when (!app.Environment.IsProduction())
{
    app.Logger.LogWarning(ex,
        "Khong ma hoa duoc embedding cu luc khoi dong {Environment}; se thu lai o lan khoi dong sau.",
        app.Environment.EnvironmentName);
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex,
        "Production cannot start because biometric embedding migration failed.");
    throw new InvalidOperationException(
        "Production startup aborted: biometric embedding migration failed.", ex);
}

try { await PreferenceEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang tuy chon nguoi dung luc khoi dong: {Msg}", ex.Message); }

try { await AuthEndpoints.EnsureAvatarTable(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang anh dai dien luc khoi dong: {Msg}", ex.Message); }

try { await ChatEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang tro chuyen luc khoi dong: {Msg}", ex.Message); }

try { await FeedbackEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang phan hoi luc khoi dong: {Msg}", ex.Message); }

// Nền tảng nhân sự + role/position migration là điều kiện BẮT BUỘC: middleware phân quyền chỉ được
// phục vụ sau khi identity không phân biệt hoa/thường, chức vụ và vai trò đã reconcile thành công.
// Nuốt lỗi ở đây sẽ khiến ứng dụng chạy nửa migration và có thể cấp nhầm quyền, nên phải fail closed.
try { await HrEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Khong khoi tao duoc nen tang role/nhan su bat buoc; dung khoi dong de dong mac dinh.");
    throw;
}

try { await RequestEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Khong khoi tao duoc bang don tu/correction/reminder bat buoc; dung khoi dong.");
    throw;
}

// Giao viec & nghiem thu — sau hr_employees (danh sach nguoi nhan viec).
try { await TaskAssignmentEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang giao viec luc khoi dong: {Msg}", ex.Message); }

try { await NotificationEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang token thiet bi luc khoi dong: {Msg}", ex.Message); }

try { await ShiftEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Khong khoi tao duoc ca lam/effective attendance view bat buoc; dung khoi dong.");
    throw;
}

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

try { await CoreAccountingEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang ke toan loi luc khoi dong: {Msg}", ex.Message); }

try { await PortalEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang cong thong tin luc khoi dong: {Msg}", ex.Message); }

try { await TalentEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang phat trien nhan su luc khoi dong: {Msg}", ex.Message); }

try { await SurveyEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang khao sat luc khoi dong: {Msg}", ex.Message); }

// Bảng hàng chờ phải có TRƯỚC khi OutboxWorker chạy (worker khởi động cùng app.Run() bên dưới).
try { await OutboxQueue.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Không tạo được outbox bền bắt buộc; dừng khởi động để không mất thông báo.");
    throw;
}

try { await HelpEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang FAQ tro giup luc khoi dong: {Msg}", ex.Message); }

app.Run();

// Cho phép dự án kiểm thử tích hợp dùng WebApplicationFactory<Program> (top-level statements).
public partial class Program;
