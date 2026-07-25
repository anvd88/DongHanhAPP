using KetoanMini.Api.Services;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Token xác nhận chấm công thay cho việc gửi lại cả loạt ảnh, nên nó chính là "giấy phép ghi công".
/// Các tính chất dưới đây là thứ giữ cho việc bỏ bước nhận diện lần hai không mở ra đường gian lận.
/// </summary>
public sealed class AttendancePreviewTokenTests
{
    private static AttendancePreviewTokens NewService() =>
        new(new MemoryCache(new MemoryCacheOptions()));

    private static AttendancePreviewTokens.Pending PendingFor(string requester) =>
        new(requester, requester, "Nguyễn Văn A", 0.87, 0.62, [0.1f, 0.2f, 0.3f]);

    [Fact]
    public void Token_IsSingleUse_AndBoundToTheAccountThatPreviewed()
    {
        var service = NewService();
        var token = service.Issue(PendingFor("an"));
        Assert.NotNull(token);

        // Lần dùng hợp lệ trả đúng kết quả đã chốt ở bước xem trước (kể cả vector để tự học).
        // (Trường hợp người khác cầm token nằm ở phép thử bên dưới — chạm sai người là token chết luôn.)
        var pending = service.Consume("an", token);
        Assert.NotNull(pending);
        Assert.Equal("an", pending!.MatchedUser);
        Assert.Equal(0.87, pending.Similarity);
        Assert.Equal([0.1f, 0.2f, 0.3f], pending.Probe);

        // DÙNG MỘT LẦN: không phát lại được để ghi hai lượt công từ một lần quét.
        Assert.Null(service.Consume("an", token));
    }

    /// <summary>
    /// Đoán sai người thì token cũng phải chết theo: token đã lộ ra ngoài coi như hỏng, không cho kẻ
    /// tấn công thử user này tới user khác trên cùng một token.
    /// </summary>
    [Fact]
    public void Token_DiesAfterAWrongAccountTouchesIt()
    {
        var service = NewService();
        var token = service.Issue(PendingFor("an"));

        Assert.Null(service.Consume("bảo", token));
        Assert.Null(service.Consume("an", token));
    }

    /// <summary>
    /// "Dùng một lần" phải đúng cả khi bị bấm/gửi song song. Nếu Consume không nguyên tử, hai request
    /// xác nhận đồng thời cùng lọt qua và ghi HAI dòng công cho một lần quét mặt.
    /// </summary>
    [Fact]
    public async Task ConcurrentConsume_LetsExactlyOneCallerThrough()
    {
        for (var round = 0; round < 50; round++)
        {
            var service = NewService();
            var token = service.Issue(PendingFor("an"))!;

            // Thả tất cả cùng lúc để thật sự chạm vào nhau, không phải chạy nối đuôi.
            using var start = new Barrier(16);
            var winners = 0;
            var racers = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
            {
                start.SignalAndWait();
                if (service.Consume("an", token) is not null) Interlocked.Increment(ref winners);
            }));

            await Task.WhenAll(racers);
            Assert.Equal(1, winners);
        }
    }

    [Fact]
    public void Token_IsNotIssuedForAnonymousKiosk()
    {
        // Kiosk ẩn danh không có danh tính để ràng buộc ⇒ giữ nguyên luồng gửi lại ảnh, không cấp token.
        Assert.Null(NewService().Issue(PendingFor("")));
    }

    [Fact]
    public void UnknownOrEmptyToken_IsRejected()
    {
        var service = NewService();
        Assert.Null(service.Consume("an", null));
        Assert.Null(service.Consume("an", ""));
        Assert.Null(service.Consume("an", "khong-ton-tai"));

        // Không có danh tính thì không dùng được token nào, kể cả token thật.
        var token = service.Issue(PendingFor("an"));
        Assert.Null(service.Consume("", token));
    }
}
