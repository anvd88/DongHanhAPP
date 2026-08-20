using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace KetoanMini.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class FaceEnrollmentApprovalTests(ApiFactory factory) : IAsyncLifetime
{
    private const byte Front = 1;
    private const byte SidePositive = 2;
    private const byte SideNegative = 3;
    private const byte Up = 4;
    private const byte Down = 5;
    private const byte BestQualityButSpoof = 10;
    private const byte LowerQualityLiveFront = 11;
    private const byte VerificationMatch1 = 20;
    private const byte VerificationMatch2 = 21;
    private const byte VerificationMismatch1 = 30;
    private const byte VerificationMismatch2 = 31;
    private const byte VerificationSplitA = 40;
    private const byte VerificationSplitB = 41;
    private const byte VerificationSpoof = 42;

    public Task InitializeAsync() => CleanupStaleFaceFixturesAsync(factory.Services);
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SelfEnrollment_IsEncryptedPending_AndOnlyLiveMatchingHrDecisionCanActivateIt()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var employee = "face-emp-" + suffix;
        var rejectedEmployee = "face-rej-" + suffix;
        var hr = "face-hr-" + suffix;

        using var app = CreateApp();
        var (employeeToken, rejectedToken, hrToken) = await SetupUsersAsync(
            app.Services, employee, rejectedEmployee, hr);
        try
        {
            var submit = await SubmitAsync(app, employeeToken);
            Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
            var submitted = await submit.Content.ReadFromJsonAsync<JsonElement>();
            var requestId = submitted.GetProperty("requestId").GetGuid();
            Assert.Equal("pending", submitted.GetProperty("status").GetString());

            // Bấm/gửi dồn không thể tạo yêu cầu pending thứ hai.
            Assert.Equal(HttpStatusCode.Conflict, (await SubmitAsync(app, employeeToken)).StatusCode);

            await using (var conn = await Db(app.Services).OpenAsync())
            {
                Assert.Equal(0, await CountAsync(conn, "cham_cong_face", employee));
                Assert.Equal(3, Convert.ToInt32(await conn.Cmd(
                    "SELECT COUNT(*) FROM cham_cong_face_enrollment_samples WHERE request_id=@id")
                    .With("@id", requestId).ExecuteScalarAsync()));
                await using var reader = await conn.Cmd(
                    "SELECT embedding FROM cham_cong_face_enrollment_samples WHERE request_id=@id")
                    .With("@id", requestId).ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    Assert.True(FieldCipher.IsEncrypted((byte[])reader["embedding"]));
            }

            // Nhân viên không có quyền xem hàng duyệt.
            using (var employeeClient = Client(app, employeeToken))
                Assert.Equal(HttpStatusCode.Forbidden,
                    (await employeeClient.GetAsync("/api/chamcong/face-enrollments?status=pending")).StatusCode);

            using var hrClient = Client(app, hrToken);
            var noAttestation = await ApproveAsync(hrClient, requestId, false, []);
            Assert.Equal(HttpStatusCode.BadRequest, noAttestation.StatusCode);

            // Lời khai của HR không đủ: phải có 2 ảnh live vừa chụp để server tự PAD + đối chiếu lại.
            var noVerificationImages = await ApproveAsync(hrClient, requestId, true, []);
            Assert.Equal(HttpStatusCode.BadRequest, noVerificationImages.StatusCode);
            var oneVerificationImage = await ApproveAsync(
                hrClient, requestId, true, [Image(VerificationMatch1)]);
            Assert.Equal(HttpStatusCode.BadRequest, oneVerificationImage.StatusCode);
            var spoofVerification = await ApproveAsync(
                hrClient, requestId, true, [Image(VerificationSpoof), Image(VerificationMatch2)]);
            Assert.Equal(HttpStatusCode.BadRequest, spoofVerification.StatusCode);

            // Cả hai probe phải khớp staging; không được duyệt một khuôn mặt khác.
            var mismatched = await ApproveAsync(hrClient, requestId, true,
                [Image(VerificationMismatch1), Image(VerificationMismatch2)]);
            Assert.Equal(HttpStatusCode.Conflict, mismatched.StatusCode);

            // Hai probe đều có thể gần mẫu staging nhưng lại không đồng nhất với nhau => vẫn phải chặn.
            var inconsistent = await ApproveAsync(hrClient, requestId, true,
                [Image(VerificationSplitA), Image(VerificationSplitB)]);
            Assert.Equal(HttpStatusCode.BadRequest, inconsistent.StatusCode);

            await AssertStillPendingAndInactiveAsync(app.Services, requestId, employee);

            var approved = await ApproveAsync(hrClient, requestId, true,
                [Image(VerificationMatch1), Image(VerificationMatch2)]);
            approved.EnsureSuccessStatusCode();

            await using (var conn = await Db(app.Services).OpenAsync())
            {
                Assert.Equal(3, await CountAsync(conn, "cham_cong_face", employee));
                Assert.Equal(0, Convert.ToInt32(await conn.Cmd(
                    "SELECT COUNT(*) FROM cham_cong_face_enrollment_samples WHERE request_id=@id")
                    .With("@id", requestId).ExecuteScalarAsync()));
                Assert.Equal("approved", Convert.ToString(await conn.Cmd(
                    "SELECT status FROM cham_cong_face_enrollments WHERE id=@id")
                    .With("@id", requestId).ExecuteScalarAsync()));
            }

            using (var employeeClient = Client(app, employeeToken))
            {
                var status = await employeeClient.GetFromJsonAsync<JsonElement>("/api/chamcong/dangky/cua-toi");
                Assert.True(status.GetProperty("registered").GetBoolean());
                Assert.False(status.GetProperty("pending").GetBoolean());
            }

            // Nhánh từ chối xóa vector staging và cho phép nhân viên gửi lại.
            var rejectedSubmit = await SubmitAsync(app, rejectedToken);
            Assert.Equal(HttpStatusCode.Accepted, rejectedSubmit.StatusCode);
            var rejectedId = (await rejectedSubmit.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("requestId").GetGuid();
            var rejected = await hrClient.PostAsJsonAsync(
                $"/api/chamcong/face-enrollments/{rejectedId}/reject",
                new { reason = "Chưa đối chiếu trực tiếp đúng nhân viên." });
            rejected.EnsureSuccessStatusCode();

            await using (var conn = await Db(app.Services).OpenAsync())
            {
                Assert.Equal(0, await CountAsync(conn, "cham_cong_face", rejectedEmployee));
                Assert.Equal(0, Convert.ToInt32(await conn.Cmd(
                    "SELECT COUNT(*) FROM cham_cong_face_enrollment_samples WHERE request_id=@id")
                    .With("@id", rejectedId).ExecuteScalarAsync()));
            }
            Assert.Equal(HttpStatusCode.Accepted, (await SubmitAsync(app, rejectedToken)).StatusCode);
        }
        finally
        {
            await CleanupAsync(app.Services, employee, rejectedEmployee, hr);
        }
    }

    [Fact]
    public async Task SelfEnrollment_ExtractsIdentityOnlyFromTheSameFrameThatPassedPad()
    {
        var employee = "face-frame-" + Guid.NewGuid().ToString("N")[..10];
        using var app = CreateApp();
        var token = await AddUserAsync(app.Services, employee, AppRoles.Employee);
        try
        {
            var response = await SubmitAsync(app, token,
            [
                new FaceEnrollPose("front",
                    [Image(BestQualityButSpoof), Image(LowerQualityLiveFront)]),
                new FaceEnrollPose("side1", [Image(SidePositive)]),
                new FaceEnrollPose("side2", [Image(SideNegative)]),
            ]);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var requestId = (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("requestId").GetGuid();

            await using var conn = await Db(app.Services).OpenAsync();
            var encrypted = (byte[])(await conn.Cmd(
                "SELECT embedding FROM cham_cong_face_enrollment_samples WHERE request_id=@id AND pose='front'")
                .With("@id", requestId).ExecuteScalarAsync())!;
            var cipher = app.Services.GetRequiredService<FieldCipher>();
            var staged = cipher.DecryptEmbedding(encrypted);

            Assert.Equal(EnrollmentFaceEngine.EnrolledIdentity, staged);
            Assert.NotEqual(EnrollmentFaceEngine.OtherIdentity, staged);
        }
        finally
        {
            await CleanupAsync(app.Services, employee);
        }
    }

    [Fact]
    public async Task SelfEnrollment_RequiresAtLeastThreeServerValidatedPosesIncludingFront()
    {
        using var app = CreateApp();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var cases = new (string User, List<FaceEnrollPose> Poses)[]
        {
            ("face-two-" + suffix,
            [
                new("front", [Image(Front)]),
                new("side1", [Image(SidePositive)]),
            ]),
            // Nhãn side1 nhưng server đo ra chính diện: không được tin nhãn do APK gửi.
            ("face-side-" + suffix,
            [
                new("front", [Image(Front)]),
                new("side1", [Image(Front)]),
                new("side2", [Image(SideNegative)]),
            ]),
            // Nhãn up nhưng pitch vẫn chính diện.
            ("face-up-" + suffix,
            [
                new("front", [Image(Front)]),
                new("side1", [Image(SidePositive)]),
                new("up", [Image(Front)]),
            ]),
            // Nhãn down nhưng pitch vẫn chính diện.
            ("face-down-" + suffix,
            [
                new("front", [Image(Front)]),
                new("side1", [Image(SidePositive)]),
                new("down", [Image(Front)]),
            ]),
            // Ba pose hợp lệ riêng lẻ nhưng hai góc bên cùng hướng cũng không hợp lệ.
            ("face-same-side-" + suffix,
            [
                new("front", [Image(Front)]),
                new("side1", [Image(SidePositive)]),
                new("side2", [Image(SidePositive)]),
            ]),
            // Có ba góc hợp lệ nhưng thiếu mẫu chuẩn chính diện.
            ("face-no-front-" + suffix,
            [
                new("side1", [Image(SidePositive)]),
                new("side2", [Image(SideNegative)]),
                new("up", [Image(Up)]),
            ]),
            // Tên pose tùy ý không được tính vào tối thiểu ba góc.
            ("face-unknown-" + suffix,
            [
                new("front", [Image(Front)]),
                new("side1", [Image(SidePositive)]),
                new("client_says_ok", [Image(Down)]),
            ]),
        };

        try
        {
            foreach (var testCase in cases)
            {
                var token = await AddUserAsync(app.Services, testCase.User, AppRoles.Employee);
                var response = await SubmitAsync(app, token, testCase.Poses);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                await using var conn = await Db(app.Services).OpenAsync();
                Assert.Equal(0, await CountAsync(conn, "cham_cong_face_enrollments", testCase.User));
            }
        }
        finally
        {
            await CleanupAsync(app.Services, cases.Select(c => c.User).ToArray());
        }
    }

    [Fact]
    public async Task ActiveBiometricStore_RejectsPlaintextAfterEncryptedMigration()
    {
        using var app = CreateApp();
        await using var conn = await Db(app.Services).OpenAsync();
        var plaintext = EmbeddingCodec.ToBytes(new float[512]);

        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            conn.Cmd(
                    @"INSERT INTO cham_cong_face
                        (username, full_name, embedding, created_at, created_by)
                      VALUES ('plaintext-must-fail', 'test', @e, CURRENT_TIMESTAMP, 'test')")
                .With("@e", plaintext)
                .ExecuteNonQueryAsync());

        Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task InactiveAccountTemplates_CannotBeRecognizedOrWriteAttendance()
    {
        var inactive = "face-inactive-" + Guid.NewGuid().ToString("N")[..10];
        using var app = CreateApp();
        var inactiveToken = await AddUserAsync(app.Services, inactive, AppRoles.Employee, active: false);
        try
        {
            var cipher = app.Services.GetRequiredService<FieldCipher>();
            await using (var conn = await Db(app.Services).OpenAsync())
            {
                await conn.Cmd(
                    @"INSERT INTO cham_cong_face (username, full_name, embedding, created_at, created_by)
                      VALUES (@u, 'Tài khoản đã khóa', @e, CURRENT_TIMESTAMP, 'test')")
                    .With("@u", inactive)
                    .With("@e", cipher.EncryptEmbedding(EnrollmentFaceEngine.EnrolledIdentity))
                    .ExecuteNonQueryAsync();
            }

            using var client = app.CreateClient();
            var recognized = await client.PostAsJsonAsync("/api/chamcong/nhandien",
                new { imageBase64 = Image(VerificationMatch1) });
            recognized.EnsureSuccessStatusCode();
            var recognizedBody = await recognized.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(recognizedBody.GetProperty("matched").GetBoolean());

            var attendance = await client.PostAsJsonAsync("/api/chamcong/cham",
                new { images = new[] { Image(VerificationMatch1) } });
            attendance.EnsureSuccessStatusCode();
            var attendanceBody = await attendance.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(attendanceBody.GetProperty("matched").GetBoolean());
            Assert.Equal("unknown", attendanceBody.GetProperty("status").GetString());

            // AllowAnonymous không được biến JWT còn hạn của tài khoản đã khóa thành vé qua cổng kiosk.
            using (var staleClient = Client(app, inactiveToken))
            {
                Assert.Equal(HttpStatusCode.Unauthorized,
                    (await staleClient.PostAsJsonAsync("/api/chamcong/nhandien",
                        new { imageBase64 = Image(VerificationMatch1) })).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized,
                    (await staleClient.PostAsJsonAsync("/api/chamcong/cham",
                        new { images = new[] { Image(VerificationMatch1) } })).StatusCode);
            }

            await using var verify = await Db(app.Services).OpenAsync();
            Assert.Equal(0, await CountAsync(verify, "cham_cong_log", inactive));
        }
        finally
        {
            await CleanupAsync(app.Services, inactive);
        }
    }

    [Fact]
    public async Task PreviewConfirmation_RechecksAccountBeforeWritingAttendance()
    {
        var username = "face-confirm-lock-" + Guid.NewGuid().ToString("N")[..8];
        var manager = "face-confirm-hr-" + Guid.NewGuid().ToString("N")[..8];
        using var app = CreateApp();
        await AddUserAsync(app.Services, username, AppRoles.Employee);
        var managerToken = await AddUserAsync(app.Services, manager, AppRoles.Hr);
        try
        {
            var cipher = app.Services.GetRequiredService<FieldCipher>();
            await using (var conn = await Db(app.Services).OpenAsync())
            {
                await conn.Cmd(
                    @"INSERT INTO cham_cong_face (username, full_name, embedding, created_at, created_by)
                      VALUES (@u, 'Nhân viên confirm', @e, CURRENT_TIMESTAMP, 'test')")
                    .With("@u", username)
                    .With("@e", cipher.EncryptEmbedding(EnrollmentFaceEngine.EnrolledIdentity))
                    .ExecuteNonQueryAsync();
            }

            // Dùng một HR còn hoạt động làm requester để middleware xác thực thành công;
            // đối tượng được nhận diện mới là tài khoản bị khóa giữa bước xem trước và xác nhận.
            using var client = Client(app, managerToken);
            var preview = await client.PostAsJsonAsync("/api/chamcong/cham", new
            {
                images = new[] { Image(VerificationMatch1), Image(VerificationMatch2) },
                previewOnly = true,
            });
            preview.EnsureSuccessStatusCode();
            var previewBody = await preview.Content.ReadFromJsonAsync<JsonElement>();
            var confirmToken = previewBody.GetProperty("previewToken").GetString();
            Assert.False(string.IsNullOrWhiteSpace(confirmToken));

            await using (var conn = await Db(app.Services).OpenAsync())
                await conn.Cmd("UPDATE app_users SET is_active=FALSE WHERE username=@u")
                    .With("@u", username).ExecuteNonQueryAsync();

            var confirm = await client.PostAsJsonAsync("/api/chamcong/cham", new { confirmToken });
            confirm.EnsureSuccessStatusCode();
            var confirmed = await confirm.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(confirmed.GetProperty("matched").GetBoolean());
            Assert.Equal("disabled", confirmed.GetProperty("status").GetString());

            await using var verify = await Db(app.Services).OpenAsync();
            Assert.Equal(0, await CountAsync(verify, "cham_cong_log", username));
        }
        finally
        {
            await CleanupAsync(app.Services, username, manager);
        }
    }

    private WebApplicationFactory<Program> CreateApp()
    {
        var encryptionKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Security:FieldEncryptionKey"] = encryptionKey }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFaceEngine>();
                services.AddSingleton<IFaceEngine>(new EnrollmentFaceEngine());
            });
        });
    }

    private static Task<HttpResponseMessage> SubmitAsync(WebApplicationFactory<Program> app, string token)
        => SubmitAsync(app, token,
        [
            new FaceEnrollPose("front", [Image(Front)]),
            new FaceEnrollPose("side1", [Image(SidePositive)]),
            new FaceEnrollPose("side2", [Image(SideNegative)]),
        ]);

    private static async Task<HttpResponseMessage> SubmitAsync(
        WebApplicationFactory<Program> app, string token, List<FaceEnrollPose> poses)
    {
        using var client = Client(app, token);
        return await client.PostAsJsonAsync("/api/chamcong/dangky/tu", new SelfFaceEnrollRequest(poses));
    }

    private static Task<HttpResponseMessage> ApproveAsync(
        HttpClient client, Guid requestId, bool identityVerified, string[] verificationImages)
        => client.PostAsJsonAsync($"/api/chamcong/face-enrollments/{requestId}/approve", new
        {
            identityVerified,
            verificationMethod = "in_person",
            note = "Đã gặp và đối chiếu trực tiếp.",
            verificationImages,
        });

    private static HttpClient Client(WebApplicationFactory<Program> app, string token)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Database Db(IServiceProvider services)
        => services.GetRequiredService<Database>();

    private static string Image(byte marker) => Convert.ToBase64String([marker]);

    private static async Task<int> CountAsync(
        Npgsql.NpgsqlConnection conn, string table, string username)
    {
        // table is a test constant, never user input.
        return Convert.ToInt32(await conn.Cmd($"SELECT COUNT(*) FROM {table} WHERE username=@u")
            .With("@u", username).ExecuteScalarAsync());
    }

    private static async Task AssertStillPendingAndInactiveAsync(
        IServiceProvider services, Guid requestId, string username)
    {
        await using var conn = await Db(services).OpenAsync();
        Assert.Equal("pending", Convert.ToString(await conn.Cmd(
            "SELECT status FROM cham_cong_face_enrollments WHERE id=@id")
            .With("@id", requestId).ExecuteScalarAsync()));
        Assert.Equal(3, Convert.ToInt32(await conn.Cmd(
            "SELECT COUNT(*) FROM cham_cong_face_enrollment_samples WHERE request_id=@id")
            .With("@id", requestId).ExecuteScalarAsync()));
        Assert.Equal(0, await CountAsync(conn, "cham_cong_face", username));
    }

    private static async Task<(string Employee, string Rejected, string Hr)> SetupUsersAsync(
        IServiceProvider services, string employee, string rejected, string hr)
        => (
            await AddUserAsync(services, employee, AppRoles.Employee),
            await AddUserAsync(services, rejected, AppRoles.Employee),
            await AddUserAsync(services, hr, AppRoles.Hr));

    private static async Task<string> AddUserAsync(
        IServiceProvider services, string username, string role, bool active = true)
    {
        var db = Db(services);
        var tokens = services.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();
        var id = Guid.NewGuid();
        var sid = "app:face:" + Guid.NewGuid().ToString("N")[..16];
        await conn.Cmd("""
            INSERT INTO app_users (id, username, full_name, email, role, password_hash, is_active,
                approval_status, approved_at, approved_by, created_at, is_deleted)
            VALUES (@id, @u, @n, '', @role, @ph, @active, 'Approved', CURRENT_TIMESTAMP, 'test',
                CURRENT_TIMESTAMP, FALSE)
            """)
            .With("@id", id).With("@u", username).With("@n", "Nhân viên kiểm thử")
            .With("@role", role).With("@active", active)
            .With("@ph", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();
        await conn.Cmd("""
            INSERT INTO user_sessions
                (session_token, username, machine_name, started_at, last_seen, is_active, client_kind, revoked)
            VALUES (@sid, @u, 'face-test', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, TRUE, 'App', FALSE)
            """)
            .With("@sid", sid).With("@u", username).ExecuteNonQueryAsync();
        return tokens.CreateToken(
            new UserDto(id, username, "Nhân viên kiểm thử", "", role, active, "Approved", DateTime.UtcNow),
            sid);
    }

    private static async Task CleanupAsync(IServiceProvider services, params string[] usernames)
    {
        try
        {
            await using var conn = await Db(services).OpenAsync();
            await conn.Cmd("DELETE FROM cham_cong_face_enrollments WHERE username = ANY(@u)")
                .With("@u", usernames).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM cham_cong_log WHERE username = ANY(@u)")
                .With("@u", usernames).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM cham_cong_face WHERE username = ANY(@u)")
                .With("@u", usernames).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM audit_logs WHERE username = ANY(@u) OR entity_name = ANY(@u)")
                .With("@u", usernames).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM user_sessions WHERE username = ANY(@u)")
                .With("@u", usernames).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM app_users WHERE username = ANY(@u)")
                .With("@u", usernames).ExecuteNonQueryAsync();
        }
        catch { /* cleanup best effort */ }
    }

    private static async Task CleanupStaleFaceFixturesAsync(IServiceProvider services)
    {
        // The PostgreSQL integration database intentionally survives between test processes. If a
        // previous process was interrupted, its fixed fake embedding would otherwise be recognized by
        // a later run and make these security tests depend on database history. Only test-only
        // usernames in the isolated *_test database are removed here.
        await using var conn = await Db(services).OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await conn.Cmd("DELETE FROM cham_cong_face_enrollments WHERE username LIKE 'face-%'", tx)
            .ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM cham_cong_log WHERE username LIKE 'face-%'", tx)
            .ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM cham_cong_face WHERE username LIKE 'face-%'", tx)
            .ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM audit_logs WHERE username LIKE 'face-%' OR entity_name LIKE 'face-%'", tx)
            .ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM user_sessions WHERE username LIKE 'face-%'", tx)
            .ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM app_users WHERE username LIKE 'face-%'", tx)
            .ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    private sealed class EnrollmentFaceEngine : IFaceEngine
    {
        public static readonly float[] EnrolledIdentity = [1, 0, 0, 0, 0, 0, 0];
        public static readonly float[] OtherIdentity = [0, 1, 0, 0, 0, 0, 0];
        private static readonly float[] SplitA = [0.6f, 0.8f, 0, 0, 0, 0, 0];
        private static readonly float[] SplitB = [0.6f, -0.8f, 0, 0, 0, 0, 0];

        public string Name => "test-full";
        public double MatchThreshold => 0.45;
        public AntiSpoofStatus AntiSpoof => new(AntiSpoofLevel.Full, "test ensemble");
        public bool CheckLiveness(byte[] imageBytes) => LivenessProbability(imageBytes) >= LivenessThreshold;
        public double LivenessProbability(byte[] imageBytes)
            => Marker(imageBytes) is BestQualityButSpoof or VerificationSpoof ? 0.05 : 0.99;
        public double LivenessThreshold => 0.5;

        public float[]? ExtractEmbedding(byte[] imageBytes) => Marker(imageBytes) switch
        {
            BestQualityButSpoof or VerificationMismatch1 or VerificationMismatch2 => [.. OtherIdentity],
            VerificationSplitA => [.. SplitA],
            VerificationSplitB => [.. SplitB],
            _ => [.. EnrolledIdentity],
        };

        public double Compare(float[] a, float[] b)
        {
            if (a.Length != b.Length || a.Length == 0) return 0;
            double dot = 0, na = 0, nb = 0;
            for (var i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                na += a[i] * a[i];
                nb += b[i] * b[i];
            }
            return na <= 0 || nb <= 0 ? 0 : dot / Math.Sqrt(na * nb);
        }

        public FaceFrameQuality? AssessFrame(byte[] imageBytes)
        {
            var marker = Marker(imageBytes);
            var pose = marker switch
            {
                SidePositive => new FacePose(0.30, 0.50),
                SideNegative => new FacePose(-0.30, 0.50),
                Up => new FacePose(0, 0.15),
                Down => new FacePose(0, 0.90),
                _ => new FacePose(0, 0.50),
            };
            var quality = marker == BestQualityButSpoof ? 0.99
                : marker == LowerQualityLiveFront ? 0.85
                : 0.90;
            return new FaceFrameQuality(true, quality, 0.9, 0.5, 0, 0.2, pose, 0.99);
        }

        private static byte Marker(byte[] bytes) => bytes.Length == 0 ? (byte)0 : bytes[0];
    }
}
