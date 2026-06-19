using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KetoanMini.Api.Data;
using KetoanMini.Api.Endpoints;
using KetoanMini.Api.Realtime;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// DateOnly tuần tự hóa dạng "yyyy-MM-dd" để khớp với input date của trình duyệt.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<TokenService>();

// Tín hiệu real-time: hub WebSocket + dịch vụ nền theo dõi thay đổi DB.
builder.Services.AddSignalR();
builder.Services.AddHostedService<ChangeWatcher>();

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
    });
builder.Services.AddAuthorization();

// Cho phép gọi từ LAN khi chạy frontend dev riêng (production phục vụ cùng origin nên không cần CORS).
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Phục vụ frontend đã build (wwwroot) — gộp chung 1 cổng với API.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Bắt lỗi DB không kết nối được → trả JSON rõ ràng thay vì 500 trống.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (Microsoft.Data.SqlClient.SqlException ex)
    {
        ctx.Response.StatusCode = 503;
        await ctx.Response.WriteAsJsonAsync(new { message = "Không kết nối được cơ sở dữ liệu SQL Server.", detail = ex.Message });
    }
});

app.MapGet("/api/info", () => Results.Ok(new { app = "KetoanMini Web API", status = "ok" }));
app.MapGet("/api/health", async (Database db) =>
{
    try { await using var c = await db.OpenAsync(); return Results.Ok(new { db = "connected" }); }
    catch (Exception ex) { return Results.Json(new { db = "error", detail = ex.Message }, statusCode: 503); }
});

app.MapAuth();
app.MapAccounting();
app.MapGiaCong();
app.MapUsers();
app.MapReleases();

// Hub tín hiệu real-time (web + desktop kết nối tới đây).
app.MapHub<ChangesHub>("/hubs/changes");

// SPA fallback: mọi route không phải /api và không phải file tĩnh → trả index.html
// để React Router xử lý (deep-link /dashboard, /giacong… reload không 404).
app.MapFallbackToFile("index.html");

// Tạo bảng gia công nếu chưa có (best-effort, không chặn khởi động nếu DB tạm thời offline).
try { await GiaCongEndpoints.EnsureTables(app.Services.GetRequiredService<Database>()); }
catch (Exception ex) { app.Logger.LogWarning("Không tạo được bảng gia công lúc khởi động: {Msg}", ex.Message); }

app.Run();
