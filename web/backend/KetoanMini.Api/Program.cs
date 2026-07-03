using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KetoanMini.Api.Data;
using KetoanMini.Api.Endpoints;
using KetoanMini.Api.Json;
using KetoanMini.Api.Realtime;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Bí mật (khóa JWT, chuỗi kết nối DB, khóa mã hóa…) KHÔNG để trong mã nguồn: đọc từ
// appsettings.Local.json (đã .gitignore) rồi cho biến môi trường ghi đè lên trên cùng.
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// DateOnly tuần tự hóa dạng "yyyy-MM-dd" để khớp với input date của trình duyệt.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    // Mốc thời gian lưu UTC → luôn xuất kèm 'Z' để client hiển thị đúng giờ địa phương.
    o.SerializerOptions.Converters.Add(new UtcDateTimeConverter());
});

builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<TokenService>();
// Mã hóa dữ liệu nhạy cảm khi lưu trữ (embedding khuôn mặt…) — khóa từ Security:FieldEncryptionKey.
builder.Services.AddSingleton<FieldCipher>();

// Bộ máy nhận diện khuôn mặt cho chấm công: YuNet + căn chỉnh 5 điểm + AdaFace R50 ONNX Runtime.
// Engine dựng lười ở lần gọi /api/chamcong đầu tiên nên lỗi model không làm sập API lúc khởi động.
builder.Services.AddSingleton<IFaceEngine, AdaFaceR50Engine>();

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
builder.Services.AddAuthorization();

// Cho phép gọi từ LAN khi chạy frontend dev riêng (production phục vụ cùng origin nên không cần CORS).
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

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

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Chặn ngay tài khoản vừa bị admin khóa/xóa (từ web HAY app desktop). JWT có thể còn hạn
// nhiều giờ, nên nếu chỉ dựa vào hết hạn token thì người dùng web vẫn thao tác được sau khi
// bị khóa. Kiểm tra is_active mỗi request đã xác thực → trả 401 để client tự đăng xuất.
// Kết hợp tín hiệu realtime "changed" (đổi is_active) → web refetch → bị đá ra gần như tức thì.
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true)
    {
        var username = ctx.User.Username();
        if (!string.IsNullOrEmpty(username))
        {
            var locked = false;
            var revoked = false;
            // sid (nếu token có) → kiểm tra thiết bị đã bị thu hồi từ xa hay chưa.
            var sid = ctx.User.FindFirst("sid")?.Value;
            try
            {
                var db = ctx.RequestServices.GetRequiredService<Database>();
                await using var conn = await db.OpenAsync(ctx.RequestAborted);
                await using var r = await conn.Cmd(
                    @"SELECT u.is_active, COALESCE(s.revoked, FALSE) AS revoked
                      FROM app_users u
                      LEFT JOIN user_sessions s ON s.session_token = @sid
                      WHERE u.username = @u AND u.is_deleted = FALSE
                      LIMIT 1")
                    .With("@u", username)
                    .With("@sid", (object?)sid ?? DBNull.Value)
                    .ExecuteReaderAsync(ctx.RequestAborted);
                if (!await r.ReadAsync(ctx.RequestAborted))
                    locked = true; // không còn dòng sống (đã xóa)
                else
                {
                    locked = r.IsDBNull(0) || !Convert.ToBoolean(r.GetValue(0));
                    revoked = !r.IsDBNull(1) && Convert.ToBoolean(r.GetValue(1));
                }
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
        }
    }

    await next();
});

// Bắt lỗi DB không kết nối được → trả JSON rõ ràng thay vì 500 trống.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (NpgsqlException ex)
    {
        ctx.Response.StatusCode = 503;
        await ctx.Response.WriteAsJsonAsync(new { message = "Khong ket noi duoc co so du lieu PostgreSQL.", detail = ex.Message });
    }
});

app.MapGet("/api/info", () => Results.Ok(new { app = "KetoanMini Web API", status = "ok" }));
app.MapGet("/api/health", async (Database db) =>
{
    try { await using var c = await db.OpenAsync(); return Results.Ok(new { db = "connected" }); }
    catch (Exception ex) { return Results.Json(new { db = "error", detail = ex.Message }, statusCode: 503); }
});

try { await PostgresSchema.EnsureAsync(app.Services.GetRequiredService<Database>(), app.Configuration, app.Logger); }
catch (Exception ex) { app.Logger.LogWarning("Khong khoi tao duoc schema PostgreSQL luc khoi dong: {Msg}", ex.Message); }

app.MapAuth();
app.MapAccounting();
app.MapGiaCong();
app.MapChamCong();
app.MapUsers();
app.MapReleases();
app.MapPreferences();
app.MapChat();
app.MapFeedback();
app.MapHr();
app.MapRequests();
app.MapShifts();
app.MapTimesheet();
app.MapPenalties();
app.MapPenaltyRefunds();
app.MapPayroll();
app.MapBankAccounts();

// Hub tín hiệu real-time (web + desktop kết nối tới đây).
app.MapHub<ChangesHub>("/hubs/changes");

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

try { await ShiftEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang ca lam luc khoi dong: {Msg}", ex.Message); }

try { await PenaltyEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang phat/ky luat luc khoi dong: {Msg}", ex.Message); }

try { await PenaltyRefundEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang hoan tien phat luc khoi dong: {Msg}", ex.Message); }

try { await PayrollEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang bang luong luc khoi dong: {Msg}", ex.Message); }

try { await BankAccountEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Khong tao duoc bang tai khoan ngan hang luc khoi dong: {Msg}", ex.Message); }

app.Run();
