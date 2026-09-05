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
        Assert.Contains("TG_NAME IN ('ketoanmini_publish_change_ins'", sql, StringComparison.Ordinal);
        Assert.Contains("trg.tgname = 'ketoanmini_publish_change'",
            DatabaseChangePublisher.LegacyTriggerCleanupSql, StringComparison.Ordinal);
        // Vòng quét checksum toàn bảng 1.5 giây đã bị thay hẳn bằng LISTEN/NOTIFY.
        Assert.DoesNotContain("COUNT(*) FROM documents", sql, StringComparison.Ordinal);

        // Một hành động nghiệp vụ có thể chạm nhiều dòng; trigger mức STATEMENT chỉ phát một lần.
        // Chấm công phát 'attendance' (chủ đề riêng, không đi chung với kế toán) + 'hr' cho bảng công.
        var chamCong = Array.Find(DatabaseChangePublisher.Watched, w => w.Table == "cham_cong_log");
        Assert.Equal(["attendance", "hr"], chamCong.Scopes);
    }

    /// <summary>
    /// Khối kế toán từng dùng chung một chủ đề tên "data", nên sửa một phiếu là đánh thức mọi màn
    /// hình của mọi máy đang mở. Bảng dưới đây chốt việc chẻ nhỏ: mỗi bảng chỉ gọi tên đúng những
    /// màn hình đọc nó. Đổi một dòng ở đây mà quên đổi khoá truy vấn bên frontend là màn hình đó
    /// đứng im — nên hai nơi phải sửa cùng lúc.
    /// </summary>
    [Theory]
    [InlineData("documents", new[] { "sales", "debts", "cash" })]
    [InlineData("document_lines", new[] { "sales", "debts", "cash" })]
    [InlineData("payments", new[] { "debts" })]
    [InlineData("customers", new[] { "debts" })]
    [InlineData("products", new[] { "catalog" })]
    [InlineData("purchases", new[] { "purchases" })]
    [InlineData("cash_fund_manual_entries", new[] { "cash" })]
    [InlineData("hr_payout_vouchers", new[] { "cash", "hr" })]
    public void AccountingTables_PublishNarrowTopicsInsteadOfOneCatchAll(string table, string[] expected)
    {
        var watched = Array.Find(DatabaseChangePublisher.Watched, w => w.Table == table);
        Assert.Equal(table, watched.Table);
        Assert.Equal(expected, watched.Scopes);
    }

    /// <summary>
    /// Đường Pub/Sub đầy đủ, đo trên một bảng nháp do chính bài kiểm thử dựng lên rồi xoá đi.
    ///
    /// Vì sao không đo trên bảng nghiệp vụ: bài này từng chạy một câu UPDATE khớp 0 dòng để "không
    /// đụng dữ liệu", nhưng hàm trigger nay bỏ qua đúng những câu như thế (xem cửa đầu tiên trong
    /// FunctionSql) nên nó không thể xanh trở lại. Còn ghi thật vào bảng nghiệp vụ thì bẩn CSDL dùng
    /// chung. Bảng nháp cho phép ghi thật, và tên chủ đề sinh ngẫu nhiên nên tín hiệu của ứng dụng
    /// đang chạy cùng CSDL không thể bị nhận nhầm thành tín hiệu của bài kiểm thử.
    /// </summary>
    [Fact]
    public async Task RealtimeChanges_FireOnRealRowChangesOnly()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var table = $"realtime_probe_{suffix}";
        var topic = $"probe_{suffix}";

        await using var owner = await db.OpenAsync();
        await owner.Cmd($"CREATE TABLE public.{table} (id int PRIMARY KEY, note text NOT NULL DEFAULT '')")
            .ExecuteNonQueryAsync();
        try
        {
            await DatabaseChangePublisher.EnsureAsync(db, [(table, [topic])]);

            await using var listener = await db.OpenAsync();
            var payloads = new List<string>();
            listener.Notification += (_, notification) => payloads.Add(notification.Payload);
            await listener.Cmd($"LISTEN {DatabaseChangePublisher.ChannelName}").ExecuteNonQueryAsync();

            // Câu lệnh khớp 0 dòng: lớp xác thực chạy đúng một câu như thế ở MỌI request, nên nếu nó
            // phát tín hiệu thì mỗi lần bấm chuột của mỗi người là một lượt đánh thức toàn hệ thống.
            await using (var writer = await db.OpenAsync())
                await writer.Cmd($"UPDATE public.{table} SET note = note WHERE FALSE").ExecuteNonQueryAsync();
            Assert.False(await WaitForScopeAsync(listener, payloads, topic, TimeSpan.FromSeconds(2)),
                "Câu lệnh không đổi dòng nào vẫn phát tín hiệu realtime.");

            // Đổi thật một dòng rồi commit → tín hiệu phải tới.
            await using (var writer = await db.OpenAsync())
                await writer.Cmd($"INSERT INTO public.{table}(id) VALUES (1)").ExecuteNonQueryAsync();
            Assert.True(await WaitForScopeAsync(listener, payloads, topic, TimeSpan.FromSeconds(5)),
                "Lệnh ghi đã commit nhưng không có tín hiệu realtime nào.");
        }
        finally
        {
            // DROP TABLE mang theo cả ba trigger. Dòng hàng đợi mà bảng nháp sinh ra thì để nguyên:
            // projector đang chạy trong chính host kiểm thử có thể đang cầm nó, xoá tay là giành
            // nhau với nó. Vòng dọn theo hạn giữ sẽ tự lấy đi.
            await owner.Cmd($"DROP TABLE IF EXISTS public.{table}").ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Chờ đúng chủ đề mình quan tâm chứ không chờ "một tín hiệu bất kỳ": CSDL kiểm thử dùng chung
    /// với ứng dụng đang chạy, nên tín hiệu của người khác chen vào giữa là chuyện thường.
    /// </summary>
    private static async Task<bool> WaitForScopeAsync(
        Npgsql.NpgsqlConnection listener, List<string> payloads, string scope, TimeSpan window)
    {
        using var timeout = new CancellationTokenSource(window);
        try
        {
            while (!payloads.Contains(scope, StringComparer.Ordinal))
                await listener.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { }
        return payloads.Contains(scope, StringComparer.Ordinal);
    }
}
