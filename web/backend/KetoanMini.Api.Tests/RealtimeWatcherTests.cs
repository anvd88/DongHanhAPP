using System.Collections.Concurrent;
using KetoanMini.Api.Data;
using KetoanMini.Api.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Kiểm tra cầu nối Pub/Sub PostgreSQL → SignalR (<see cref="ChangeWatcher"/>) bằng DB THẬT:
/// phát NOTIFY qua trigger rồi xem hub nhận đúng những gì. Hai tính chất quan trọng:
///   • Không phạm vi nào bị bỏ rơi khi một phạm vi khác bị ghi dồn dập.
///   • Nhịp tim (user_sessions) không tạo bão broadcast "presence".
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RealtimeWatcherTests
{
    private readonly ApiFactory _factory;
    public RealtimeWatcherTests(ApiFactory factory) => _factory = factory;

    /// <summary>
    /// Hub giả: ghi lại mọi lời gọi SendAsync("changed", scope). <paramref name="sendDelay"/> mô phỏng
    /// máy khách chậm/đông — lúc đó vòng đọc bận phát tin nên thông báo mới dồn lại trong hàng chờ.
    /// </summary>
    private sealed class RecordingHubContext : IHubContext<ChangesHub>
    {
        public ConcurrentQueue<string> Sent { get; } = new();
        public IHubClients Clients { get; }
        public IGroupManager Groups => throw new NotSupportedException();
        public RecordingHubContext(TimeSpan sendDelay = default)
            => Clients = new RecordingClients(Sent, sendDelay);

        private sealed class RecordingClients : IHubClients
        {
            public RecordingClients(ConcurrentQueue<string> sent, TimeSpan delay)
                => All = new RecordingProxy(sent, delay);
            public IClientProxy All { get; }
            public IClientProxy AllExcept(IReadOnlyList<string> e) => All;
            public IClientProxy Client(string c) => All;
            public IClientProxy Clients(IReadOnlyList<string> c) => All;
            public IClientProxy Group(string g) => All;
            public IClientProxy GroupExcept(string g, IReadOnlyList<string> e) => All;
            public IClientProxy Groups(IReadOnlyList<string> g) => All;
            public IClientProxy User(string u) => All;
            public IClientProxy Users(IReadOnlyList<string> u) => All;
        }

        private sealed class RecordingProxy(ConcurrentQueue<string> sent, TimeSpan delay) : IClientProxy
        {
            public ConcurrentQueue<string> Sent => sent;
            public async Task SendCoreAsync(string method, object?[] args, CancellationToken ct = default)
            {
                if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
                if (method == "changed" && args.Length > 0 && args[0] is string scope) sent.Enqueue(scope);
            }
        }
    }

    private static async Task<(ChangeWatcher Watcher, RecordingHubContext Hub, Database Db)> StartWatcherAsync(
        ApiFactory factory, CancellationToken ct, TimeSpan sendDelay = default)
    {
        var db = factory.Services.CreateScope().ServiceProvider.GetRequiredService<Database>();
        var hub = new RecordingHubContext(sendDelay);
        var watcher = new ChangeWatcher(hub, db, NullLogger<ChangeWatcher>.Instance);
        await watcher.StartAsync(ct);
        // Chờ LISTEN sẵn sàng: phát thử tới khi thấy tín hiệu đầu tiên vọng về.
        await WaitUntilAsync(async () =>
        {
            await NotifyAsync(db, "UPDATE customers SET updated_at = updated_at WHERE FALSE");
            return !hub.Sent.IsEmpty;
        }, TimeSpan.FromSeconds(20));
        Assert.False(hub.Sent.IsEmpty, "ChangeWatcher chưa nhận được tín hiệu nào từ PostgreSQL.");
        return (watcher, hub, db);
    }

    /// <summary>Chạy một câu lệnh ghi 0 dòng — trigger mức STATEMENT vẫn phát NOTIFY.</summary>
    private static async Task NotifyAsync(Database db, string statement)
    {
        await using var conn = await db.OpenAsync();
        await conn.Cmd(statement).ExecuteNonQueryAsync();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(200);
        }
    }

    /// <summary>
    /// Tính chất cần giữ: một phạm vi bị ghi dồn dập KHÔNG được làm chìm phạm vi khác — 'hr' ghi giữa
    /// trận dội 'data' (máy khách chậm 250ms/lần phát) vẫn phải tới nơi.
    /// LƯU Ý: bản cũ (hàng đợi 64 ô DropOldest) cũng qua được test này — chưa dựng lại được cảnh nó
    /// đánh rơi 'hr'. Giữ test làm lưới an toàn cho tính chất trên, KHÔNG phải bằng chứng có lỗi cũ.
    /// </summary>
    [Fact]
    public async Task BurstOfOneScope_DoesNotStarveAnotherScope()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        // Máy khách chậm: mỗi lần phát mất 250ms → vòng đọc bận, thông báo mới phải xếp hàng.
        var (watcher, hub, db) = await StartWatcherAsync(_factory, cts.Token, TimeSpan.FromMilliseconds(250));
        try
        {
            hub.Sent.Clear();
            // Dội 'data' NỀN bằng 8 kết nối song song, mỗi kết nối chạy 400 câu lệnh RIÊNG (mỗi câu là
            // một giao dịch → một thông báo; gộp chung một giao dịch thì PostgreSQL tự khử trùng lặp
            // nên không tái hiện được lỗi).
            var burst = Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            {
                await using var conn = await db.OpenAsync();
                for (var i = 0; i < 400; i++)
                    await conn.Cmd("UPDATE customers SET updated_at = updated_at WHERE FALSE")
                        .ExecuteNonQueryAsync();
            }));

            // Chờ vòng đọc bận phát tin rồi mới ghi 'hr' — đúng thời điểm mà hàng đợi cũ đầy ắp 'data'
            // trùng lặp và sẽ đẩy văng 'hr'. Ghi trước lúc dội thì 'hr' được đọc ra ngay, không lộ lỗi.
            await Task.Delay(1000);
            await NotifyAsync(db, "UPDATE hr_employees SET updated_at = updated_at WHERE FALSE");
            await burst;

            await WaitUntilAsync(() => Task.FromResult(hub.Sent.Contains("hr")), TimeSpan.FromSeconds(30));
            Assert.Contains("hr", hub.Sent);
            Assert.Contains("data", hub.Sent);
        }
        finally { await watcher.StopAsync(CancellationToken.None); }
    }

    /// <summary>
    /// Rớt kết nối tới PostgreSQL là mất trắng thông báo: LISTEN/NOTIFY không giữ hàng chờ cho phiên
    /// đã ngắt. Máy khách vẫn nối SignalR nên không tự biết mà nạp lại → phải được bảo nạp toàn bộ
    /// ('all') ngay khi listener nối lại, nếu không chúng giữ dữ liệu cũ tới lần ghi kế tiếp.
    /// </summary>
    [Fact]
    public async Task ListenerReconnect_TellsClientsToResync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var (watcher, hub, db) = await StartWatcherAsync(_factory, cts.Token);
        try
        {
            hub.Sent.Clear();
            // Ngắt phũ kết nối đang LISTEN từ phía máy chủ (giống lúc mạng chớp / PostgreSQL khởi động lại).
            await using (var killer = await db.OpenAsync())
                await killer.Cmd(
                    @"SELECT pg_terminate_backend(pid) FROM pg_stat_activity
                      WHERE query LIKE 'LISTEN %' AND pid <> pg_backend_pid()")
                    .ExecuteNonQueryAsync();

            await WaitUntilAsync(() => Task.FromResult(hub.Sent.Contains("all")), TimeSpan.FromSeconds(45));
            Assert.Contains("all", hub.Sent);
        }
        finally { await watcher.StopAsync(CancellationToken.None); }
    }

    /// <summary>
    /// Mỗi nhịp tim (45 giây/người) ghi last_seen vào user_sessions → NOTIFY 'presence'. Trước đây mỗi
    /// thông báo thành một lần phát tới TOÀN BỘ máy khách (N người ⇒ N² tin nhắn). Giờ 'presence' bị
    /// gộp nhịp: dội liên tục cũng chỉ phát vài lần, trong khi phạm vi khác vẫn tới ngay.
    /// </summary>
    [Fact]
    public async Task PresenceHeartbeats_AreCoalescedIntoFewBroadcasts()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var (watcher, hub, db) = await StartWatcherAsync(_factory, cts.Token);
        try
        {
            hub.Sent.Clear();
            // Giả lập 30 nhịp tim dồn dập (30 người dùng cùng báo online).
            for (var i = 0; i < 30; i++)
            {
                await NotifyAsync(db, "UPDATE user_sessions SET last_seen = last_seen WHERE FALSE");
                await Task.Delay(50);
            }
            // 'hr' xen giữa phải được phát NGAY, không bị vạ lây bởi việc gộp nhịp 'presence'.
            await NotifyAsync(db, "UPDATE hr_employees SET updated_at = updated_at WHERE FALSE");
            await WaitUntilAsync(() => Task.FromResult(hub.Sent.Contains("hr")), TimeSpan.FromSeconds(15));

            var presence = hub.Sent.Count(s => s == "presence");
            Assert.Contains("hr", hub.Sent);
            Assert.True(presence <= 2,
                $"30 nhịp tim chỉ được gộp thành tối đa 2 lần phát 'presence', thực tế {presence}.");
            Assert.True(presence >= 1, "Phải phát 'presence' ít nhất một lần.");
        }
        finally { await watcher.StopAsync(CancellationToken.None); }
    }
}
