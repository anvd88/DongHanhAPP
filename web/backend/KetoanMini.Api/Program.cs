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

var ffmpegCaptureOptions = builder.Configuration["KioskCamera:FfmpegCaptureOptions"];
if (!string.IsNullOrWhiteSpace(ffmpegCaptureOptions))
    Environment.SetEnvironmentVariable("OPENCV_FFMPEG_CAPTURE_OPTIONS", ffmpegCaptureOptions);

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

// Bộ máy nhận diện khuôn mặt cho chấm công: YuNet + căn chỉnh 5 điểm + AdaFace R50 ONNX Runtime.
// Engine dựng lười ở lần gọi /api/chamcong đầu tiên nên lỗi model không làm sập API lúc khởi động.
builder.Services.AddSingleton<IFaceEngine, AdaFaceR50Engine>();

// Tín hiệu real-time: hub WebSocket + dịch vụ nền theo dõi thay đổi DB.
builder.Services.AddSignalR();
// Định danh kết nối hub theo username để phát tín hiệu chat đúng thành viên (Clients.Users).
builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, NameUserIdProvider>();
builder.Services.AddHostedService<ChangeWatcher>();
builder.Services.AddSingleton<CameraSnapshotBridgeService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CameraSnapshotBridgeService>());
builder.Services.AddSingleton<RtspAttendanceWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RtspAttendanceWorker>());
// Dọn tệp "giữ tạm" (gửi tệp qua LAN khi người nhận offline) đã quá hạn khỏi đĩa.
builder.Services.AddHostedService<LanFileCleanupService>();

var jwt = builder.Configuration.GetSection("Jwt");
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
            try
            {
                var db = ctx.RequestServices.GetRequiredService<Database>();
                await using var conn = await db.OpenAsync(ctx.RequestAborted);
                var active = await conn.Cmd(
                    "SELECT is_active FROM app_users WHERE username = @u AND is_deleted = FALSE LIMIT 1")
                    .With("@u", username)
                    .ExecuteScalarAsync(ctx.RequestAborted);
                // NULL/không có dòng sống (đã xóa) hoặc is_active = 0 → coi như bị khóa.
                locked = active is null or DBNull || !Convert.ToBoolean(active);
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
