using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OpenCvSharp;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Chống giả mạo là chốt fail-closed: model thiếu/hỏng hoặc inference lỗi phải từ chối lượt quét,
/// đồng thời Production không được khởi động nếu chưa đủ ensemble Silent-Face.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AntiSpoofStatusTests(ApiFactory factory)
{
    [Fact]
    public void ProductionGuard_RequiresFullAntiSpoof_WhileDevelopmentMayDiagnoseDegradedModels()
    {
        var production = new TestEnvironment(Environments.Production);
        var development = new TestEnvironment(Environments.Development);

        ProductionSecurityValidator.ValidateFaceEngine(production, new StubFaceEngine(AntiSpoofLevel.Full, 1));
        Assert.Throws<InvalidOperationException>(() =>
            ProductionSecurityValidator.ValidateFaceEngine(production, new StubFaceEngine(AntiSpoofLevel.Basic, 1)));
        Assert.Throws<InvalidOperationException>(() =>
            ProductionSecurityValidator.ValidateFaceEngine(production, new StubFaceEngine(AntiSpoofLevel.None, 1)));
        ProductionSecurityValidator.ValidateFaceEngine(development, new StubFaceEngine(AntiSpoofLevel.None, 1));
    }

    [Fact]
    public void SharedLivenessBoundary_FailsClosed_ForMissingThrowingOrNonFiniteEngines()
    {
        Assert.False(FaceAntiSpoofSecurity.IsOperational(new StubFaceEngine(AntiSpoofLevel.None, 1)));
        Assert.Equal(0, FaceAntiSpoofSecurity.ProbabilityReal(
            new StubFaceEngine(AntiSpoofLevel.None, 1), [1]));
        Assert.Equal(0, FaceAntiSpoofSecurity.ProbabilityReal(
            new StubFaceEngine(AntiSpoofLevel.Full, double.NaN), [1]));
        Assert.Equal(0, FaceAntiSpoofSecurity.ProbabilityReal(
            new StubFaceEngine(AntiSpoofLevel.Full, 1, throws: true), [1]));
    }

    [Fact]
    public void SilentFace_RequiresBothModels_AndNoModelReturnsZero()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), "ketoanmini-sf-empty-" + Guid.NewGuid().ToString("N"));
        var partialDir = Path.Combine(Path.GetTempPath(), "ketoanmini-sf-partial-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyDir);
        Directory.CreateDirectory(partialDir);
        try
        {
            using (var empty = new SilentFaceLiveness(emptyDir))
            using (var image = new Mat())
            {
                Assert.False(empty.Available);
                Assert.Equal(0, empty.ProbabilityReal(image, default));
            }

            const string oneModel = "2.7_80x80_MiniFASNetV2.onnx";
            var source = Path.Combine(AppContext.BaseDirectory, "Models", "Face", oneModel);
            Assert.True(File.Exists(source), $"Missing packaged test model: {source}");
            File.Copy(source, Path.Combine(partialDir, oneModel));
            using var partial = new SilentFaceLiveness(partialDir);
            Assert.False(partial.Available);
        }
        finally
        {
            Directory.Delete(emptyDir, recursive: true);
            Directory.Delete(partialDir, recursive: true);
        }
    }

    /// <summary>
    /// Nếu ai đó làm hỏng việc đóng gói model (đổi luật copy trong .csproj, dọn nhầm thư mục
    /// Models/Face, đổi tên file), test này đỏ NGAY thay vì để hệ thống chạy không có chống giả mạo.
    /// </summary>
    [Fact]
    public void AntiSpoof_IsFullyLoaded_SoAttendanceIsNotSilentlyUnprotected()
    {
        var engine = factory.Services.GetRequiredService<IFaceEngine>();

        Assert.Equal(AntiSpoofLevel.Full, engine.AntiSpoof.Level);
        Assert.NotEmpty(engine.AntiSpoof.Detail);
    }

    /// <summary>
    /// Panel quản trị là nơi DUY NHẤT con người nhìn thấy mức chống giả mạo, nên hình dạng JSON của nó
    /// phải giữ đúng: giao diện web đọc theo tên trường (types.ts viết tay, không sinh tự động).
    /// </summary>
    [Fact]
    public async Task LivenessPanel_ExposesAntiSpoofLevel_ToTheAdminScreen()
    {
        var admin = "as-admin-" + Guid.NewGuid().ToString("N")[..10];
        var token = await MakeAdminAsync(admin);
        try
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync("/api/chamcong/liveness-metrics");
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal("Full", body.GetProperty("antiSpoof").GetProperty("level").GetString());
            Assert.Equal(JsonValueKind.Array, body.GetProperty("metrics").ValueKind);
        }
        finally
        {
            await CleanupAsync(admin);
        }
    }

    private async Task<string> MakeAdminAsync(string username)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();

        var userId = Guid.NewGuid();
        await conn.Cmd("""
            INSERT INTO app_users (id, username, full_name, email, role, password_hash, is_active,
                approval_status, approved_at, approved_by, created_at, is_deleted)
            VALUES (@id, @u, @n, '', 'Admin', @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test',
                CURRENT_TIMESTAMP, FALSE)
            """)
            .With("@id", userId).With("@u", username).With("@n", "Quản trị thử nghiệm")
            .With("@ph", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();

        return tokens.CreateToken(
            new UserDto(userId, username, "Quản trị thử nghiệm", "", "Admin", true, "Approved", DateTime.UtcNow),
            "app:as:" + Guid.NewGuid().ToString("N")[..16]);
    }

    private async Task CleanupAsync(string username)
    {
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            await using var conn = await db.OpenAsync();
            await conn.Cmd("DELETE FROM user_sessions WHERE username=@u").With("@u", username)
                .ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM app_users WHERE username=@u").With("@u", username)
                .ExecuteNonQueryAsync();
        }
        catch { /* dọn dẹp best-effort */ }
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class StubFaceEngine(AntiSpoofLevel level, double score, bool throws = false) : IFaceEngine
    {
        public string Name => "stub";
        public double MatchThreshold => 0.45;
        public AntiSpoofStatus AntiSpoof => new(level, "test");
        public bool CheckLiveness(byte[] imageBytes) => LivenessProbability(imageBytes) >= LivenessThreshold;
        public double LivenessProbability(byte[] imageBytes) => throws ? throw new InvalidOperationException("test") : score;
        public double LivenessThreshold => 0.5;
        public float[]? ExtractEmbedding(byte[] imageBytes) => [1, 0, 0];
        public double Compare(float[] a, float[] b) => 1;
        public FaceFrameQuality? AssessFrame(byte[] imageBytes) => null;
    }
}
