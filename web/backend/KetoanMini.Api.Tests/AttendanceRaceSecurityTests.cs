using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KetoanMini.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class AttendanceRaceSecurityTests(ApiFactory factory)
{
    [Fact]
    public async Task OfflineApproval_IsAtomicAndIdempotent_WhenTwoReviewersApproveConcurrently()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var employee = "offline-emp-" + suffix;
        var reviewer = "offline-hr-" + suffix;
        var (_, employeeToken) = await AddUserAsync(employee, AppRoles.Employee);
        var (_, reviewerToken) = await AddUserAsync(reviewer, AppRoles.Hr);
        _ = employeeToken;

        try
        {
            var offlineId = await AddFaceAndOfflineRecordAsync(employee);
            using var first = Client(reviewerToken);
            using var second = Client(reviewerToken);

            var approvals = await Task.WhenAll(
                first.PostAsJsonAsync($"/api/chamcong/offline/{offlineId}/approve", new { note = "review-a" }),
                second.PostAsJsonAsync($"/api/chamcong/offline/{offlineId}/approve", new { note = "review-b" }));

            Assert.All(approvals, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

            // Một retry sau khi cả hai request hoàn tất cũng là no-op thành công.
            var retry = await first.PostAsJsonAsync(
                $"/api/chamcong/offline/{offlineId}/approve", new { note = "retry" });
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);

            await using var conn = await Db().OpenAsync();
            Assert.Equal("approved", Convert.ToString(await conn.Cmd(
                "SELECT status FROM cham_cong_offline WHERE id=@id")
                .With("@id", offlineId).ExecuteScalarAsync()));
            Assert.Equal(1, Convert.ToInt32(await conn.Cmd(
                @"SELECT COUNT(*) FROM cham_cong_log
                   WHERE lower(username)=lower(@u) AND ghi_chu='Ngoại tuyến (đã duyệt)'")
                .With("@u", employee).ExecuteScalarAsync()));
        }
        finally
        {
            await CleanupAsync(employee, reviewer);
        }
    }

    [Fact]
    public async Task DeleteFaces_WaitsForAdaptiveWriter_ThenRemovesTheNewSampleToo()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var employee = "face-race-emp-" + suffix;
        var reviewer = "face-race-hr-" + suffix;
        await AddUserAsync(employee, AppRoles.Employee);
        var (_, reviewerToken) = await AddUserAsync(reviewer, AppRoles.Hr);

        try
        {
            await AddFaceAsync(employee, "test-original");

            // Mô phỏng đúng nửa đầu của adaptive learning: giữ khóa app_user, sau đó thêm mẫu mới.
            await using var adaptiveConn = await Db().OpenAsync();
            await using var adaptiveTx = await adaptiveConn.BeginTransactionAsync();
            await adaptiveConn.Cmd(
                "SELECT 1 FROM app_users WHERE lower(username)=lower(@u) FOR UPDATE", adaptiveTx)
                .With("@u", employee).ExecuteScalarAsync();

            using var reviewerClient = Client(reviewerToken);
            var deleteTask = reviewerClient.DeleteAsync($"/api/chamcong/dangky/{employee}");
            await Task.Delay(400);
            Assert.False(deleteTask.IsCompleted);

            var cipher = factory.Services.GetRequiredService<FieldCipher>();
            await adaptiveConn.Cmd(
                @"INSERT INTO cham_cong_face (username, full_name, embedding, created_at, created_by)
                  VALUES (@u, 'Nhân viên race', @e, CURRENT_TIMESTAMP, 'AUTO_LEARN')", adaptiveTx)
                .With("@u", employee)
                .With("@e", cipher.EncryptEmbedding(Embedding()))
                .ExecuteNonQueryAsync();
            await adaptiveTx.CommitAsync();

            var deleted = await deleteTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

            await using var verify = await Db().OpenAsync();
            Assert.Equal(0, Convert.ToInt32(await verify.Cmd(
                "SELECT COUNT(*) FROM cham_cong_face WHERE lower(username)=lower(@u)")
                .With("@u", employee).ExecuteScalarAsync()));
        }
        finally
        {
            await CleanupAsync(employee, reviewer);
        }
    }

    [Fact]
    public async Task DeleteUser_RemovesAttendanceAndBiometricArtifacts_IgnoringUsernameCase()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var employee = "case-emp-" + suffix;
        var artifactUsername = employee.ToUpperInvariant();
        var admin = "case-admin-" + suffix;
        var (employeeId, _) = await AddUserAsync(employee, AppRoles.Employee);
        var (_, adminToken) = await AddUserAsync(admin, AppRoles.Admin);
        var enrollmentId = Guid.NewGuid();

        try
        {
            var cipher = factory.Services.GetRequiredService<FieldCipher>();
            await using (var conn = await Db().OpenAsync())
            {
                await conn.Cmd(
                    @"INSERT INTO cham_cong_face (username, full_name, embedding, created_at, created_by)
                      VALUES (@u, 'Nhân viên casing', @e, CURRENT_TIMESTAMP, 'test')")
                    .With("@u", artifactUsername).With("@e", cipher.EncryptEmbedding(Embedding()))
                    .ExecuteNonQueryAsync();
                await conn.Cmd(
                    @"INSERT INTO cham_cong_log (username, full_name, loai, similarity, occurred_at, ghi_chu)
                      VALUES (@u, 'Nhân viên casing', 'Vào', 0.9, CURRENT_TIMESTAMP, 'case-test')")
                    .With("@u", artifactUsername).ExecuteNonQueryAsync();
                await conn.Cmd(
                    @"INSERT INTO cham_cong_offline
                        (username, full_name, loai, similarity, quality, occurred_at, status)
                      VALUES (@u, 'Nhân viên casing', 'Vào', 0.9, 0.8, CURRENT_TIMESTAMP, 'pending')")
                    .With("@u", artifactUsername).ExecuteNonQueryAsync();
                await conn.Cmd(
                    @"INSERT INTO cham_cong_face_enrollments
                        (id, username, full_name, status, sample_count)
                      VALUES (@id, @u, 'Nhân viên casing', 'pending', 0)")
                    .With("@id", enrollmentId).With("@u", artifactUsername).ExecuteNonQueryAsync();
            }

            using var client = Client(adminToken);
            var response = await client.DeleteAsync($"/api/users/{employeeId}");
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            await using var verify = await Db().OpenAsync();
            foreach (var table in new[]
                     {
                         "cham_cong_face", "cham_cong_face_enrollments", "cham_cong_offline", "cham_cong_log"
                     })
            {
                // table là hằng số test, không nhận từ input.
                Assert.Equal(0, Convert.ToInt32(await verify.Cmd(
                    $"SELECT COUNT(*) FROM {table} WHERE lower(username)=lower(@u)")
                    .With("@u", employee).ExecuteScalarAsync()));
            }
        }
        finally
        {
            await CleanupAsync(employee, admin);
        }
    }

    private Database Db() => factory.Services.GetRequiredService<Database>();

    private HttpClient Client(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(Guid Id, string Token)> AddUserAsync(string username, string role)
    {
        var id = Guid.NewGuid();
        var tokens = factory.Services.GetRequiredService<TokenService>();
        await using var conn = await Db().OpenAsync();
        await conn.Cmd("""
            INSERT INTO app_users (id, username, full_name, email, role, password_hash, is_active,
                approval_status, approved_at, approved_by, created_at, is_deleted)
            VALUES (@id, @u, @n, '', @role, @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test',
                CURRENT_TIMESTAMP, FALSE)
            """)
            .With("@id", id).With("@u", username).With("@n", "Nhân viên race test")
            .With("@role", role).With("@ph", PasswordHasher.Hash("test-pass"))
            .ExecuteNonQueryAsync();
        var token = tokens.CreateToken(
            new UserDto(id, username, "Nhân viên race test", "", role, true, "Approved", DateTime.UtcNow),
            "app:attendance-race:" + Guid.NewGuid().ToString("N")[..12]);
        return (id, token);
    }

    private async Task AddFaceAsync(string username, string createdBy)
    {
        var cipher = factory.Services.GetRequiredService<FieldCipher>();
        await using var conn = await Db().OpenAsync();
        await conn.Cmd(
            @"INSERT INTO cham_cong_face (username, full_name, embedding, created_at, created_by)
              VALUES (@u, 'Nhân viên race', @e, CURRENT_TIMESTAMP, @by)")
            .With("@u", username).With("@e", cipher.EncryptEmbedding(Embedding())).With("@by", createdBy)
            .ExecuteNonQueryAsync();
    }

    private async Task<long> AddFaceAndOfflineRecordAsync(string username)
    {
        await AddFaceAsync(username, "test");
        await using var conn = await Db().OpenAsync();
        return Convert.ToInt64(await conn.Cmd(
            @"INSERT INTO cham_cong_offline
                (username, full_name, loai, similarity, quality, occurred_at, status)
              VALUES (@u, 'Nhân viên offline', 'Vào', 0.91, 0.82,
                      CURRENT_TIMESTAMP - INTERVAL '5 minutes', 'pending')
              RETURNING id")
            .With("@u", username).ExecuteScalarAsync());
    }

    private static float[] Embedding()
    {
        var embedding = new float[512];
        embedding[0] = 1;
        return embedding;
    }

    private async Task CleanupAsync(params string[] usernames)
    {
        try
        {
            var normalized = usernames.Select(value => value.ToLowerInvariant()).ToArray();
            await using var conn = await Db().OpenAsync();
            await conn.Cmd(
                "DELETE FROM cham_cong_face_enrollments WHERE lower(username)=ANY(@u)")
                .With("@u", normalized).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM cham_cong_offline WHERE lower(username)=ANY(@u)")
                .With("@u", normalized).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM cham_cong_log WHERE lower(username)=ANY(@u)")
                .With("@u", normalized).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM cham_cong_face WHERE lower(username)=ANY(@u)")
                .With("@u", normalized).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM audit_logs WHERE lower(username)=ANY(@u) OR lower(entity_name)=ANY(@u)")
                .With("@u", normalized).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM user_sessions WHERE lower(username)=ANY(@u)")
                .With("@u", normalized).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM user_roles WHERE lower(username)=ANY(@u)")
                .With("@u", normalized).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM app_users WHERE lower(username)=ANY(@u)")
                .With("@u", normalized).ExecuteNonQueryAsync();
        }
        catch
        {
            // Dọn dẹp best effort để lỗi assertion gốc không bị che.
        }
    }
}
