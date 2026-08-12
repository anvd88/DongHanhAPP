using KetoanMini.Api.Data;
using KetoanMini.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KetoanMini.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class FaceEnrollmentCleanupServiceTests(ApiFactory factory)
{
    [Fact]
    public async Task Sweep_ExpiresDueRequestAndDeletesItsStagedBiometricTemplates()
    {
        var requestId = Guid.NewGuid();
        var username = "face-expired-" + Guid.NewGuid().ToString("N")[..12];
        var db = factory.Services.GetRequiredService<Database>();
        var encryptedBlob = new byte[36];
        "KME1"u8.CopyTo(encryptedBlob);

        await using (var conn = await db.OpenAsync())
        {
            await conn.Cmd(
                """
                INSERT INTO cham_cong_face_enrollments
                    (id, username, full_name, status, sample_count, requested_at, expires_at)
                VALUES
                    (@id, @u, 'Expired cleanup test', 'pending', 1,
                     CURRENT_TIMESTAMP - INTERVAL '15 days', CURRENT_TIMESTAMP - INTERVAL '1 minute')
                """)
                .With("@id", requestId).With("@u", username).ExecuteNonQueryAsync();
            await conn.Cmd(
                """
                INSERT INTO cham_cong_face_enrollment_samples (request_id, pose, embedding, quality, liveness)
                VALUES (@id, 'front', @embedding, 0.9, 0.9)
                """)
                .With("@id", requestId).With("@embedding", encryptedBlob).ExecuteNonQueryAsync();
        }

        try
        {
            var service = new FaceEnrollmentCleanupService(
                db, NullLogger<FaceEnrollmentCleanupService>.Instance);

            var result = await service.SweepAsync();

            Assert.True(result.ExpiredRequests >= 1);
            Assert.True(result.DeletedSamples >= 1);
            await using var conn = await db.OpenAsync();
            Assert.Equal("expired", Convert.ToString(await conn.Cmd(
                    "SELECT status FROM cham_cong_face_enrollments WHERE id=@id")
                .With("@id", requestId).ExecuteScalarAsync()));
            Assert.Equal(0, Convert.ToInt32(await conn.Cmd(
                    "SELECT COUNT(*) FROM cham_cong_face_enrollment_samples WHERE request_id=@id")
                .With("@id", requestId).ExecuteScalarAsync()));
        }
        finally
        {
            await using var conn = await db.OpenAsync();
            await conn.Cmd("DELETE FROM cham_cong_face_enrollments WHERE id=@id")
                .With("@id", requestId).ExecuteNonQueryAsync();
        }
    }
}
