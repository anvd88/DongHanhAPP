using KetoanMini.Api.Data;
using KetoanMini.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Hàng chờ bền cho việc-có-hậu-quả. Điều cần chứng minh: việc KHÔNG mất khi bên nhận hỏng, KHÔNG
/// nhân đôi khi xếp trùng, và KHÔNG nuốt mất người nhận khi nhiều người dùng chung một chữ ký sự kiện.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OutboxTests
{
    private readonly ApiFactory _factory;
    public OutboxTests(ApiFactory factory) => _factory = factory;

    /// <summary>Bên nhận giả: đếm số lần được gọi và cho phép bắt nó hỏng.</summary>
    private sealed class FakeHandler : IOutboxHandler
    {
        public int Calls;
        public bool Succeed = true;
        public bool Throw;
        public List<string> Payloads { get; } = new();

        public Task<bool> HandleAsync(OutboxMessage message, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            lock (Payloads) Payloads.Add(message.Payload);
            if (Throw) throw new InvalidOperationException("FCM giả vờ sập");
            return Task.FromResult(Succeed);
        }
    }

    private static async Task<(OutboxQueue Queue, Database Db)> NewQueueAsync(ApiFactory factory)
    {
        var db = factory.Services.CreateScope().ServiceProvider.GetRequiredService<Database>();
        await OutboxQueue.EnsureTables(db);
        return (new OutboxQueue(db, NullLogger<OutboxQueue>.Instance), db);
    }

    private static string UniqueKey() => $"test|{Guid.NewGuid()}";

    private static async Task<(string Status, int Attempts)> ReadRowAsync(Database db, string dedupeKey)
    {
        await using var conn = await db.OpenAsync();
        await using var r = await conn.Cmd(
            "SELECT status, attempts FROM app_outbox WHERE dedupe_key=@k").With("@k", dedupeKey).ExecuteReaderAsync();
        Assert.True(await r.ReadAsync(), "Không tìm thấy việc vừa xếp vào hàng chờ.");
        return (r.GetString(0), r.GetInt32(1));
    }

    [Fact]
    public async Task Enqueue_ThenWorkerRuns_MarksJobDone()
    {
        var (queue, db) = await NewQueueAsync(_factory);
        var handler = new FakeHandler();
        var key = UniqueKey();

        await queue.EnqueueAsync(OutboxQueue.KindUserPush,
            new { Username = "nguoidung", Title = "T", Body = "B", NotifId = "n1", Target = (string?)null }, key);

        var claimed = await queue.ClaimAsync(20);
        var mine = claimed.Single(c => c.Payload.Contains("nguoidung", StringComparison.Ordinal));
        Assert.True(await handler.HandleAsync(mine, default));
        await queue.CompleteAsync(mine.Id);

        var (status, _) = await ReadRowAsync(db, key);
        Assert.Equal("done", status);
    }

    /// <summary>
    /// Đây là lý do dựng hàng chờ: FCM sập thì việc PHẢI còn nguyên để thử lại, chứ không bốc hơi như
    /// lối gọi thẳng trong request trước đây (lỗi bị nuốt, không ai biết thông báo đã mất).
    /// </summary>
    [Fact]
    public async Task FailedJob_StaysPendingForRetry_AndBacksOff()
    {
        var (queue, db) = await NewQueueAsync(_factory);
        var key = UniqueKey();
        await queue.EnqueueAsync(OutboxQueue.KindUserPush,
            new { Username = "hong", Title = "T", Body = "B", NotifId = "n2", Target = (string?)null }, key);

        var mine = (await queue.ClaimAsync(50)).Single(c => c.Payload.Contains("\"hong\"", StringComparison.Ordinal));
        await queue.FailAsync(mine.Id, mine.Attempts, "FCM sập");

        var (status, attempts) = await ReadRowAsync(db, key);
        Assert.Equal("pending", status);      // còn nguyên, chờ thử lại
        Assert.Equal(1, attempts);

        // Đang trong thời gian lùi thì KHÔNG được giành lại ngay (nếu không sẽ quay vòng đốt CPU/quota).
        var again = await queue.ClaimAsync(50);
        Assert.DoesNotContain(again, c => c.Id == mine.Id);
    }

    /// <summary>Quá số lần thử thì chuyển "chết" và giữ lại để điều tra, không xoá âm thầm.</summary>
    [Fact]
    public async Task JobIsDeadLettered_AfterMaxAttempts()
    {
        var (queue, db) = await NewQueueAsync(_factory);
        var key = UniqueKey();
        await queue.EnqueueAsync(OutboxQueue.KindUserPush,
            new { Username = "chet", Title = "T", Body = "B", NotifId = "n3", Target = (string?)null }, key);

        var mine = (await queue.ClaimAsync(50)).Single(c => c.Payload.Contains("\"chet\"", StringComparison.Ordinal));
        await queue.FailAsync(mine.Id, OutboxQueue.MaxAttempts, "hỏng mãi");

        var (status, _) = await ReadRowAsync(db, key);
        Assert.Equal("dead", status);
    }

    /// <summary>Xếp lại đúng sự kiện cho đúng người thì chỉ còn một việc — máy nhận không đổ chuông hai lần.</summary>
    [Fact]
    public async Task SameEventForSameRecipient_IsEnqueuedOnce()
    {
        var (queue, db) = await NewQueueAsync(_factory);
        var key = UniqueKey();
        var job = new { Username = "trung", Title = "T", Body = "B", NotifId = "n4", Target = (string?)null };

        await queue.EnqueueAsync(OutboxQueue.KindUserPush, job, key);
        await queue.EnqueueAsync(OutboxQueue.KindUserPush, job, key);

        await using var conn = await db.OpenAsync();
        var count = Convert.ToInt32(await conn.Cmd("SELECT COUNT(*) FROM app_outbox WHERE dedupe_key=@k")
            .With("@k", key).ExecuteScalarAsync());
        Assert.Equal(1, count);
    }

    /// <summary>
    /// BẪY đã suýt mắc: một sự kiện nghiệp vụ gửi cho nhiều người dùng CHUNG một notif_id.
    /// Nếu khoá khử trùng chỉ là chữ ký sự kiện thì chỉ người đầu tiên được xếp
    /// hàng, những người còn lại bị coi là trùng và MẤT thông báo. Khoá phải kèm người nhận.
    /// </summary>
    [Fact]
    public async Task SameEventForDifferentRecipients_IsEnqueuedForEach()
    {
        var (queue, db) = await NewQueueAsync(_factory);
        var notifId = $"task:{Guid.NewGuid()}:assigned";
        string[] recipients = ["an", "binh", "chi"];

        foreach (var who in recipients)
            await queue.EnqueueAsync(OutboxQueue.KindUserPush,
                new { Username = who, Title = "Việc mới", Body = "B", NotifId = notifId, Target = "Tasks" },
                $"{OutboxQueue.KindUserPush}|{who}|{notifId}");

        await using var conn = await db.OpenAsync();
        var count = Convert.ToInt32(await conn.Cmd(
            "SELECT COUNT(*) FROM app_outbox WHERE dedupe_key LIKE @p")
            .With("@p", $"%{notifId}").ExecuteScalarAsync());
        Assert.Equal(recipients.Length, count);
    }

    /// <summary>Việc đã bỏ hẳn phải đếm được — đó là cơ sở để nhắc lại định kỳ trong log.</summary>
    [Fact]
    public async Task DeadCount_CountsAbandonedJobs()
    {
        var (queue, _) = await NewQueueAsync(_factory);
        var before = await queue.DeadCountAsync();

        await queue.EnqueueAsync(OutboxQueue.KindUserPush,
            new { Username = "demxac", Title = "T", Body = "B", NotifId = "n6", Target = (string?)null }, UniqueKey());
        var mine = (await queue.ClaimAsync(50)).Single(c => c.Payload.Contains("demxac", StringComparison.Ordinal));
        await queue.FailAsync(mine.Id, OutboxQueue.MaxAttempts, "hỏng hẳn");

        Assert.Equal(before + 1, await queue.DeadCountAsync());
    }

    /// <summary>
    /// Đầu-tới-cuối qua chính DI của ứng dụng: worker chạy nền TRONG app phải tự rút việc và đóng sổ.
    /// Chọn người nhận không có thiết bị nào nên không có thông báo thật nào được gửi đi, nhưng vẫn đi
    /// trọn đường xếp hàng → giành lượt → gửi → đánh dấu xong.
    /// </summary>
    [Fact]
    public async Task HostedWorker_DrainsQueue_InTheRealApp()
    {
        var queue = _factory.Services.GetRequiredService<OutboxQueue>();
        var db = _factory.Services.CreateScope().ServiceProvider.GetRequiredService<Database>();
        var key = UniqueKey();

        await queue.EnqueueAsync(OutboxQueue.KindUserPush,
            new
            {
                Username = $"khong-ton-tai-{Guid.NewGuid():N}",
                Title = "T", Body = "B", NotifId = "n-e2e", Target = (string?)null,
            }, key);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        string status;
        do
        {
            await Task.Delay(250);
            (status, _) = await ReadRowAsync(db, key);
        } while (status == "pending" && DateTime.UtcNow < deadline);

        Assert.Equal("done", status);
    }

    /// <summary>
    /// Khoá khử trùng dựng từ PushService — chỗ dễ sai nhất của cả thiết kế, nên kiểm riêng: cùng sự
    /// kiện mà KHÁC người nhận thì phải ra khoá khác, không thì người sau mất thông báo.
    /// </summary>
    [Fact]
    public void DedupeKey_SeparatesRecipients_ButNotCaseOfUsername()
    {
        const string notifId = "task:abc:assigned";
        var an = PushService.DedupeKey(OutboxQueue.KindUserPush, "an", notifId);
        var binh = PushService.DedupeKey(OutboxQueue.KindUserPush, "binh", notifId);
        var anHoa = PushService.DedupeKey(OutboxQueue.KindUserPush, "AN", notifId);

        Assert.NotEqual(an, binh);   // cùng sự kiện, hai người → hai việc
        Assert.Equal(an, anHoa);     // cùng người viết hoa/thường → vẫn là một việc

        // Gửi cho admin và gửi cho một người trùng tên "admins" không được đụng nhau.
        Assert.NotEqual(
            PushService.DedupeKey(OutboxQueue.KindAdminsPush, null, notifId),
            PushService.DedupeKey(OutboxQueue.KindUserPush, "admins", notifId));
    }

    /// <summary>
    /// Bên nhận NÉM lỗi (không chỉ trả false) thì worker vẫn phải giữ việc lại để thử tiếp, chứ không
    /// để ngoại lệ làm chết cả vòng xử lý.
    /// </summary>
    [Fact]
    public async Task WorkerKeepsJob_WhenHandlerThrows()
    {
        var (queue, db) = await NewQueueAsync(_factory);
        var handler = new FakeHandler { Throw = true };
        var worker = new OutboxWorker(queue, handler, NullLogger<OutboxWorker>.Instance);
        var key = UniqueKey();

        await queue.EnqueueAsync(OutboxQueue.KindUserPush,
            new { Username = "nem", Title = "T", Body = "B", NotifId = "n5", Target = (string?)null }, key);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await worker.StartAsync(cts.Token);
        // Chờ worker chạm tới việc rồi dừng lại xem hàng chờ còn giữ không.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (handler.Calls == 0 && DateTime.UtcNow < deadline) await Task.Delay(100, cts.Token);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(handler.Calls > 0, "Worker chưa xử lý việc nào.");
        var (status, attempts) = await ReadRowAsync(db, key);
        Assert.Equal("pending", status);
        Assert.True(attempts >= 1);
    }
}
