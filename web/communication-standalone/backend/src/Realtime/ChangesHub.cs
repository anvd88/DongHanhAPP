using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace KetoanMini.Api.Realtime;

/// <summary>
/// Communication-only legacy hub for chat/call/P2P signaling.
/// Hub YÊU CẦU ĐĂNG NHẬP: <c>Program.cs</c> gắn <c>RequireAuthorization()</c>,
/// nên kết nối ẩn danh bị chặn ngay từ bước negotiate (quan trọng vì hub đang lộ ra Internet qua
/// Cloudflare Tunnel). Vì vậy <see cref="HubCallerContext.UserIdentifier"/> luôn có giá trị —
/// token nào cũng mang claim Name (xem <c>TokenService</c>); thấy "(ẩn danh)" trong log là dấu hiệu
/// token không tới được hub, cần điều tra chứ không phải trạng thái bình thường.
/// Ngoài ra hub còn TRUNG CHUYỂN tín hiệu bắt tay WebRTC (gửi tệp P2P + GỌI THOẠI/VIDEO) qua LAN/Internet.
/// </summary>
public sealed class ChangesHub : Hub
{
    private readonly ILogger<ChangesHub> _log;
    public ChangesHub(ILogger<ChangesHub> log) => _log = log;

    // Log định danh mỗi kết nối để chẩn đoán cuộc gọi: nếu UserIdentifier NULL nghĩa là token không
    // tới được hub (kết nối ẩn danh) → Relay bị bỏ → không nhận được cuộc gọi. Xem có "user=<username>".
    public override async Task OnConnectedAsync()
    {
        _log.LogInformation("Hub kết nối: user={User} conn={Conn}", Context.UserIdentifier ?? "(ẩn danh)", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    // Chống lạm dụng kênh Relay (DoS/flood): giới hạn số gói mỗi cửa sổ thời gian theo TỪNG kết nối.
    // Bắt tay WebRTC (ICE/offer/answer) chỉ vài chục gói ngắn — hạn mức này thừa cho gọi + gửi tệp,
    // nhưng chặn kẻ dùng kết nối đã đăng nhập để bơm tín hiệu quấy phá người khác.
    private const int MaxSignalsPerWindow = 120;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(5);
    private const int MaxPayloadBytes = 64 * 1024; // SDP/ICE chỉ vài KB; chặn payload phình bất thường.

    private static readonly ConcurrentDictionary<string, RateState> Buckets = new();

    private sealed class RateState
    {
        public int Count;
        public long WindowStartTicks;
    }

    /// <summary>
    /// Chuyển một gói tín hiệu WebRTC (mời gọi / offer / answer / ICE / đồng ý / từ chối / kết thúc)
    /// từ người ĐANG ĐĂNG NHẬP tới đúng MỘT người nhận. Đây CHỈ là tín hiệu bắt tay — media (âm
    /// thanh/hình ảnh của cuộc gọi, nội dung tệp) KHÔNG đi qua server mà truyền thẳng P2P và mã hóa
    /// DTLS-SRTP. Kết nối ẩn danh (không có UserIdentifier) bị bỏ qua để không lạm dụng kênh.
    /// </summary>
    public async Task Relay(string toUsername, string payload)
    {
        var from = Context.UserIdentifier;
        if (string.IsNullOrEmpty(from) || string.IsNullOrWhiteSpace(toUsername))
        {
            _log.LogWarning("Relay BỎ: from={From} to={To} (kết nối ẩn danh?)", from ?? "(null)", toUsername);
            return;
        }
        if (string.IsNullOrEmpty(payload) || payload.Length > MaxPayloadBytes) return;
        if (!AllowSignal(Context.ConnectionId)) return; // vượt hạn mức → âm thầm bỏ (chống flood)

        _log.LogInformation("Relay {From} → {To} ({Len} bytes)", from, toUsername, payload.Length);
        await Clients.User(toUsername).SendAsync("signal", from, payload);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        Buckets.TryRemove(Context.ConnectionId, out _);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Cửa sổ trượt đơn giản theo kết nối: cho phép tối đa <see cref="MaxSignalsPerWindow"/> gói mỗi <see cref="Window"/>.</summary>
    private static bool AllowSignal(string connectionId)
    {
        var now = DateTime.UtcNow.Ticks;
        var state = Buckets.GetOrAdd(connectionId, _ => new RateState { WindowStartTicks = now });
        lock (state)
        {
            if (now - state.WindowStartTicks > Window.Ticks)
            {
                state.WindowStartTicks = now;
                state.Count = 0;
            }
            if (state.Count >= MaxSignalsPerWindow) return false;
            state.Count++;
            return true;
        }
    }
}
