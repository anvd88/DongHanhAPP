using System.Net;
using KetoanMini.Api.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using KetoanMini.Api.Realtime;
using Xunit;

namespace KetoanMini.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class InfrastructureTests
{
    private readonly ApiFactory _factory;
    public InfrastructureTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task OpenApiContract_IsPublished()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"openapi\"", json);
        Assert.Contains("/api/auth/login", json);
    }

    [Fact]
    public async Task BaselineMigration_IsRecordedAndIdempotent()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await PostgresSchema.EnsureAsync(db, new ConfigurationBuilder().Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        await PostgresSchema.EnsureAsync(db, new ConfigurationBuilder().Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        await using var connection = await db.OpenAsync();
        var count = Convert.ToInt32(await connection.Cmd(
            "SELECT COUNT(*) FROM schema_migrations WHERE version='001_baseline'").ExecuteScalarAsync());
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task AccountingDocuments_CanBeCancelledButNotPhysicallyDeleted()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var connection = await db.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var id = Guid.NewGuid();

        await new Npgsql.NpgsqlCommand(
                @"INSERT INTO documents
                    (id, voucher_no, doc_date, document_type, content, note)
                  VALUES (@id, @voucherNo, CURRENT_DATE, 'payment', 'Test cancel', '')",
                connection, transaction)
            .With("@id", id)
            .With("@voucherNo", $"PC-TEST-{id:N}")
            .ExecuteNonQueryAsync();

        await new Npgsql.NpgsqlCommand(
                @"UPDATE documents
                  SET cancelled_at = CURRENT_TIMESTAMP,
                      cancelled_by = 'integration-test',
                      cancel_reason = 'Kiểm tra lưu vết'
                  WHERE id = @id",
                connection, transaction)
            .With("@id", id)
            .ExecuteNonQueryAsync();

        var cancelled = Convert.ToBoolean(await new Npgsql.NpgsqlCommand(
                "SELECT cancelled_at IS NOT NULL FROM documents WHERE id = @id",
                connection, transaction)
            .With("@id", id)
            .ExecuteScalarAsync());
        Assert.True(cancelled);

        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            new Npgsql.NpgsqlCommand("DELETE FROM documents WHERE id = @id", connection, transaction)
                .With("@id", id)
                .ExecuteNonQueryAsync());
        Assert.Equal("23514", error.SqlState);

        await transaction.RollbackAsync();
    }

    [Fact]
    public void EveryNonPublicApiEndpoint_DeclaresAuthorization()
    {
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) == true)
            .Where(e => e.RoutePattern.RawText is not "/api/info" and not "/api/health")
            .ToList();

        var missing = endpoints.Where(e =>
                e.Metadata.GetMetadata<IAllowAnonymous>() is null &&
                e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
            .Select(e => e.RoutePattern.RawText)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        Assert.True(missing.Length == 0,
            "API endpoints must explicitly require authentication or explicitly opt into AllowAnonymous: " +
            string.Join(", ", missing));
    }

    [Fact]
    public void RealtimeChanges_UseStatementLevelDatabasePubSub()
    {
        var sql = DatabaseChangePublisher.FunctionSql;

        Assert.Contains("pg_notify('ketoanmini_changes'", sql, StringComparison.Ordinal);
        // Vòng quét checksum toàn bảng 1.5 giây đã bị thay hẳn bằng LISTEN/NOTIFY.
        Assert.DoesNotContain("COUNT(*) FROM documents", sql, StringComparison.Ordinal);

        // Một hành động nghiệp vụ có thể chạm nhiều dòng; trigger mức STATEMENT chỉ phát một lần.
        // Chấm công phát 'attendance' (không còn đi chung scope bắt-tất 'data') + 'hr' cho bảng công.
        var chamCong = Array.Find(DatabaseChangePublisher.Watched, w => w.Table == "cham_cong_log");
        Assert.Equal(["attendance", "hr"], chamCong.Scopes);
    }

    [Fact]
    public async Task RealtimeChanges_PublishCommittedDatabaseStatements()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await DatabaseChangePublisher.EnsureAsync(db);

        await using var listener = await db.OpenAsync();
        var payloads = new List<string>();
        listener.Notification += (_, notification) => payloads.Add(notification.Payload);
        await listener.Cmd($"LISTEN {DatabaseChangePublisher.ChannelName}").ExecuteNonQueryAsync();

        // A statement-level trigger also fires for a zero-row update. This exercises the complete
        // Pub/Sub path without changing business data in the shared integration-test database.
        await using (var writer = await db.OpenAsync())
            await writer.Cmd("UPDATE customers SET updated_at = updated_at WHERE FALSE").ExecuteNonQueryAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await listener.WaitAsync(timeout.Token);
        Assert.Contains("data", payloads);
    }

    [Theory]
    [InlineData("UPDATE work_tasks SET updated_at = updated_at WHERE FALSE", "tasks")]
    [InlineData("UPDATE app_portal_posts SET updated_at = updated_at WHERE FALSE", "portal")]
    [InlineData("UPDATE app_config SET updated_at = updated_at WHERE FALSE", "config")]
    [InlineData("UPDATE audit_logs SET occurred_at = occurred_at WHERE FALSE", "audit")]
    [InlineData("UPDATE hr_onboarding_tasks SET created_at = created_at WHERE FALSE", "talent")]
    [InlineData("UPDATE cham_cong_log SET occurred_at = occurred_at WHERE FALSE", "attendance")]
    public async Task RealtimeChanges_PublishGranularScopes(string statement, string expectedScope)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await DatabaseChangePublisher.EnsureAsync(db);

        await using var listener = await db.OpenAsync();
        var payloads = new List<string>();
        listener.Notification += (_, notification) => payloads.Add(notification.Payload);
        await listener.Cmd($"LISTEN {DatabaseChangePublisher.ChannelName}").ExecuteNonQueryAsync();

        await using (var writer = await db.OpenAsync())
            await writer.Cmd(statement).ExecuteNonQueryAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await listener.WaitAsync(timeout.Token);
        Assert.Contains(expectedScope, payloads);
    }
}
