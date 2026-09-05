using KetoanMini.Api.BuildingBlocks.Realtime;
using KetoanMini.Api.Data;
using KetoanMini.Api.Realtime;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>Database-level proof for the legacy trigger → transactional integration-outbox bridge.</summary>
[Collection(ApiCollection.Name)]
public sealed class RealtimeWatcherTests(ApiFactory factory)
{
    [Fact]
    public async Task RolledBackBusinessTransaction_LeavesNoRealtimeOutboxEvent()
    {
        var db = factory.Services.GetRequiredService<Database>();
        await DatabaseChangePublisher.EnsureAsync(db, [("customers", ["debts"])]);
        await using var conn = await db.OpenAsync();
        long transactionId;
        await using (var tx = await conn.BeginTransactionAsync())
        {
            transactionId = Convert.ToInt64(await conn.Cmd("SELECT txid_current()", tx).ExecuteScalarAsync());
            await InsertCustomerAsync(conn, tx);
            await tx.RollbackAsync();
        }

        // Đếm theo bridge_key của CHÍNH giao dịch này, không đếm tổng số dòng trong bảng. Bản trước
        // so tổng trước/sau, mà CSDL kiểm thử dùng chung với ứng dụng đang chạy và có vòng dọn theo
        // hạn giữ — nên con số nhúc nhích vì lý do chẳng liên quan gì tới phép thử, và bài kiểm thử
        // đỏ lên một cách ngẫu nhiên.
        var leaked = Convert.ToInt64(await conn.Cmd(
            "SELECT COUNT(*) FROM integration_outbox WHERE bridge_key LIKE @prefix")
            .With("@prefix", $"tx:{transactionId}:scope:%").ExecuteScalarAsync());
        Assert.Equal(0, leaked);
        await DatabaseChangePublisher.EnsureAsync(db);
    }

    /// <summary>
    /// Mọi bảng trong <see cref="DatabaseChangePublisher.Watched"/> phải thật sự MANG trigger, và
    /// trigger phải mang đúng danh sách chủ đề đã khai. Đây là chỗ duy nhất soi bản cài đặt thật
    /// trong CSDL: khai đúng trong C# mà cài sai xuống CSDL thì màn hình tương ứng đứng im, và không
    /// bài kiểm thử nào khác nhìn thấy.
    /// </summary>
    [Fact]
    public async Task EveryWatchedTable_CarriesTriggersWithItsDeclaredTopics()
    {
        var db = factory.Services.GetRequiredService<Database>();
        var missing = await DatabaseChangePublisher.EnsureAsync(db);

        await using var conn = await db.OpenAsync();
        var installed = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var r = await conn.Cmd("""
            SELECT cls.relname || '.' || trg.tgname AS key,
                   replace(pg_get_triggerdef(trg.oid), ' ', '') AS definition
            FROM pg_trigger trg
            JOIN pg_class cls ON cls.oid = trg.tgrelid
            JOIN pg_namespace ns ON ns.oid = cls.relnamespace
            WHERE NOT trg.tgisinternal AND ns.nspname = 'public'
              AND trg.tgname LIKE 'ketoanmini_publish_change%'
            """).ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) installed[r.GetString(0)] = r.GetString(1);
        }

        var problems = new List<string>();
        foreach (var (table, scopes) in DatabaseChangePublisher.Watched)
        {
            if (missing.Contains(table)) continue;
            var expectedCall = $"ketoanmini_publish_change({string.Join(",", scopes.Select(s => $"'{s}'"))})";
            foreach (var suffix in new[] { "ins", "upd", "del" })
            {
                var key = $"{table}.ketoanmini_publish_change_{suffix}";
                if (!installed.TryGetValue(key, out var definition))
                    problems.Add($"{key}: chưa cài trigger");
                else if (!definition.Contains(expectedCall, StringComparison.Ordinal))
                    problems.Add($"{key}: chủ đề sai, mong đợi {expectedCall}");
            }
        }

        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    /// <summary>
    /// Trigger mức STATEMENT vẫn chạy khi câu lệnh khớp 0 dòng. Không lọc thì lớp xác thực — vốn chạy
    /// đúng một câu UPDATE như thế ở MỌI request — bắt cả hệ thống làm mới sau từng lần bấm chuột.
    /// </summary>
    [Fact]
    public async Task StatementThatChangesNothing_PublishesNoEvent()
    {
        var db = factory.Services.GetRequiredService<Database>();
        await DatabaseChangePublisher.EnsureAsync(db, [("customers", ["debts"])]);
        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var transactionId = Convert.ToInt64(await conn.Cmd("SELECT txid_current()", tx).ExecuteScalarAsync());
        await conn.Cmd("UPDATE customers SET updated_at=updated_at WHERE FALSE", tx).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM customers WHERE FALSE", tx).ExecuteNonQueryAsync();
        await tx.CommitAsync();

        var count = Convert.ToInt32(await conn.Cmd(
            "SELECT COUNT(*) FROM integration_outbox WHERE bridge_key=@key")
            .With("@key", $"tx:{transactionId}:scope:debts").ExecuteScalarAsync());
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task MultipleTablesInOneTransaction_AreDeduplicatedByScope()
    {
        var db = factory.Services.GetRequiredService<Database>();
        await DatabaseChangePublisher.EnsureAsync(db,
            [("customers", ["debts"]), ("products", ["debts"])]);
        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var transactionId = Convert.ToInt64(await conn.Cmd("SELECT txid_current()", tx).ExecuteScalarAsync());
        await InsertCustomerAsync(conn, tx);
        await conn.Cmd("""
            INSERT INTO products(id,code,name) VALUES (@id,@code,'San pham kiem thu realtime')
            """, tx).With("@id", Guid.NewGuid()).With("@code", $"RT{Guid.NewGuid():N}"[..12])
            .ExecuteNonQueryAsync();
        await tx.CommitAsync();

        var count = Convert.ToInt32(await conn.Cmd(
            "SELECT COUNT(*) FROM integration_outbox WHERE bridge_key=@key")
            .With("@key", $"tx:{transactionId}:scope:debts").ExecuteScalarAsync());

        await conn.Cmd("DELETE FROM customers WHERE name='Khach kiem thu realtime'").ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM products WHERE name='San pham kiem thu realtime'").ExecuteNonQueryAsync();
        // Trả trigger về bản khai thật: bài này cố tình cho products đi chung chủ đề với customers để
        // đo phép khử trùng, nếu bỏ nguyên thì bài kiểm tra bản cài đặt sẽ đỏ vì một lý do bịa.
        await DatabaseChangePublisher.EnsureAsync(db);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task OutboxInsert_DoesNotRecursivelyCreateAnotherOutboxRow()
    {
        var db = factory.Services.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        var id = Guid.NewGuid();
        // Phong bì ĐẦY ĐỦ chứ không phải mảnh vụn: projector đang chạy trong chính host kiểm thử sẽ
        // nhặt dòng này. Một payload thiếu eventType từng nằm lại vĩnh viễn trong hàng đợi thật của
        // DB kiểm thử và (trước bản vá) chặn mọi sự kiện realtime phía sau nó.
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            eventId = id,
            eventType = "test.v1",
            occurredAt = DateTimeOffset.UtcNow,
            producer = "KetoanMini.Api.Tests",
            audience = new[] { "all" },
            data = new { scope = "debts" },
        });
        await conn.Cmd("""
            INSERT INTO integration_outbox
                (id,event_type,routing_key,payload,occurred_at)
            VALUES (@id,'test.v1','test.event.v1',@payload::jsonb,CURRENT_TIMESTAMP)
            """).With("@id", id).With("@payload", payload)
            .ExecuteNonQueryAsync();
        var count = Convert.ToInt32(await conn.Cmd(
            "SELECT COUNT(*) FROM integration_outbox WHERE id=@id").With("@id", id).ExecuteScalarAsync());
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task EnsureAsync_ReportsWatchedTablesThatDoNotExist()
    {
        var db = factory.Services.GetRequiredService<Database>();
        var skipped = await DatabaseChangePublisher.EnsureAsync(db,
            [("customers", ["debts"]), ("bang_khong_ton_tai_de_kiem_thu", ["hr"])]);
        Assert.Contains("bang_khong_ton_tai_de_kiem_thu", skipped);
        Assert.DoesNotContain("customers", skipped);
    }

    /// <summary>
    /// Máy khách khai chủ đề nào thì chỉ nhận chủ đề đó. Đây là chỗ cắt tải thật: trước bản này mọi
    /// khung đi tới mọi máy đang mở, nên một phiếu thu bắt cả toà nhà tải lại năm màn hình.
    /// </summary>
    [Fact]
    public void TopicSubscription_KeepsOnlyWhatTheConnectionAskedFor()
    {
        var topics = RealtimeEventStore.ParseTopics("sales,cash");
        Assert.True(RealtimeEventStore.ShouldDeliver("sales", topics));
        Assert.True(RealtimeEventStore.ShouldDeliver("cash", topics));
        Assert.False(RealtimeEventStore.ShouldDeliver("hr", topics));
        Assert.False(RealtimeEventStore.ShouldDeliver("attendance", topics));
    }

    /// <summary>
    /// Hai lối đi vòng qua bộ lọc. Tin về phiên làm việc (quyền đổi, phiên bị thu hồi) và lệnh nạp
    /// lại toàn bộ luôn phải tới; còn kết nối không khai gì thì nhận tất, để máy khách đời trước và
    /// APK không hoá câm sau khi bộ lọc được bật.
    /// </summary>
    [Fact]
    public void TopicSubscription_NeverSilencesSessionEventsOrLegacyClients()
    {
        var topics = RealtimeEventStore.ParseTopics("sales");
        Assert.True(RealtimeEventStore.ShouldDeliver("access", topics));
        Assert.True(RealtimeEventStore.ShouldDeliver("all", topics));

        var unsubscribed = RealtimeEventStore.ParseTopics(null);
        Assert.True(RealtimeEventStore.ShouldDeliver("hr", unsubscribed));
        Assert.True(RealtimeEventStore.ShouldDeliver("attendance", unsubscribed));
    }

    /// <summary>Tên chủ đề bịa ra bị loại ngay khi đọc, không đi tiếp vào bất kỳ câu truy vấn nào.</summary>
    [Fact]
    public void TopicSubscription_DropsUnknownNames()
    {
        var topics = RealtimeEventStore.ParseTopics("sales, khong-co-that ,cash");
        Assert.Equal(["cash", "sales"], topics.OrderBy(x => x, StringComparer.Ordinal));
        Assert.False(RealtimeEventStore.ShouldDeliver("khong-co-that", topics));
    }

    /// <summary>
    /// Mọi chủ đề mà máy chủ CÓ THỂ phát đều phải nằm trong danh sách hợp lệ của bộ lọc. Thiếu một
    /// tên ở đó là màn hình tương ứng im lặng vĩnh viễn — đúng kiểu hỏng không ai thấy.
    /// </summary>
    [Fact]
    public void EveryPublishedScope_IsAcceptedByTheTopicFilter()
    {
        var published = DatabaseChangePublisher.Watched.SelectMany(w => w.Scopes).Distinct().ToArray();
        var accepted = RealtimeEventStore.ParseTopics(string.Join(',', published));
        Assert.Equal(published.OrderBy(x => x, StringComparer.Ordinal),
            accepted.OrderBy(x => x, StringComparer.Ordinal));
    }

    private static Task<int> InsertCustomerAsync(NpgsqlConnection conn, NpgsqlTransaction tx)
        => conn.Cmd("INSERT INTO customers(id,name) VALUES (@id,'Khach kiem thu realtime')", tx)
            .With("@id", Guid.NewGuid()).ExecuteNonQueryAsync();
}
