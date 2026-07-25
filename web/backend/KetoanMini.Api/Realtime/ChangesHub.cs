using System.Collections.Concurrent;
using KetoanMini.Api.Data;
using Microsoft.AspNetCore.SignalR;

namespace KetoanMini.Api.Realtime;

/// <summary>
/// Hub đẩy tín hiệu thay đổi dữ liệu xuống mọi client (web + app).
/// Broadcast "changed" chỉ mang TÊN PHẠM VI ("hr", "data"…), không kèm dữ liệu nghiệp vụ — máy khách
/// nhận tín hiệu rồi tự gọi API (đã kiểm quyền) để lấy phần mình được xem.
/// Hub YÊU CẦU ĐĂNG NHẬP: <c>Program.cs</c> gắn <c>RequireAuthorization(RequireRole(AppRoles.All))</c>,
/// nên kết nối ẩn danh bị chặn ngay từ bước negotiate (quan trọng vì hub đang lộ ra Internet qua
/// Cloudflare Tunnel). Vì vậy <see cref="HubCallerContext.UserIdentifier"/> luôn có giá trị —
/// token nào cũng mang claim Name (xem <c>TokenService</c>); thấy "(ẩn danh)" trong log là dấu hiệu
/// token không tới được hub, cần điều tra chứ không phải trạng thái bình thường.
/// Ngoài ra hub còn TRUNG CHUYỂN tín hiệu bắt tay WebRTC (gửi tệp P2P + GỌI THOẠI/VIDEO) qua LAN/Internet.
/// </summary>
public sealed class ChangesHub : Hub
{
    private readonly ILogger<ChangesHub> _log;
    private readonly Database _db;
    private readonly HubPresenceRegistry _presence;

    public ChangesHub(ILogger<ChangesHub> log, Database db, HubPresenceRegistry presence)
    {
        _log = log;
        _db = db;
        _presence = presence;
    }

    // Log định danh mỗi kết nối để chẩn đoán cuộc gọi: nếu UserIdentifier NULL nghĩa là token không
    // tới được hub (kết nối ẩn danh) → Relay bị bỏ → không nhận được cuộc gọi. Xem có "user=<username>".
    public override async Task OnConnectedAsync()
    {
        _log.LogInformation("Hub kết nối: user={User} conn={Conn}", Context.UserIdentifier ?? "(ẩn danh)", Context.ConnectionId);
        await MarkPresenceAsync();
        await base.OnConnectedAsync();
    }

    // HIỆN DIỆN ONLINE — thay cho nhịp tim HTTP mà client tự bắn. Chính kết nối SignalR là bằng chứng
    // "đang online", còn keep-alive ping sẵn có của SignalR là "nhịp tim" ở tầng vận chuyển. Ở đây chỉ
    // làm tươi last_seen của ĐÚNG dòng user_sessions mà lúc đăng nhập đã tạo (khóa theo sid) rồi để
    // <see cref="HubPresenceRefresher"/> giữ tươi mỗi 45s.
    //
    // Áp cho MỌI kết nối đã xác thực có claim "sid" — cả TRÌNH DUYỆT (phiên cookie) LẪN APP native
    // (khi ở foreground). App ở nền tắt SignalR nên lúc đó nhịp tim HTTP dự phòng của app mới giữ hiện
    // diện. Khóa theo session_token (sid) nên chỉ đụng đúng dòng của kết nối này, không phân biệt
    // Web/App. Ghi last_seen kích hoạt trigger 'presence' của PostgreSQL nên các máy khác tự cập nhật.
    private async Task MarkPresenceAsync()
    {
        var username = Context.UserIdentifier;
        if (string.IsNullOrEmpty(username)) return;
        // sid nằm sẵn trong token (TokenService gắn claim "sid" = session_token của dòng user_sessions
        // tạo lúc đăng nhập). Không có sid thì không biết làm tươi dòng nào → bỏ qua.
        var sid = Context.User?.FindFirst("sid")?.Value;
        if (string.IsNullOrWhiteSpace(sid)) return;

        _presence.Track(Context.ConnectionId, sid);
        try
        {
            await using var conn = await _db.OpenAsync(Context.ConnectionAborted);
            await conn.Cmd(
                @"UPDATE user_sessions
                     SET last_seen = CURRENT_TIMESTAMP, is_active = TRUE, ended_at = NULL, end_reason = ''
                   WHERE session_token = @t AND username = @u AND revoked = FALSE")
                .With("@t", sid).With("@u", username)
                .ExecuteNonQueryAsync(Context.ConnectionAborted);
        }
        catch (Exception ex)
        {
            // DB chập chờn không được làm hỏng việc dựng kết nối realtime — bỏ qua, nhịp làm tươi sau sẽ vá.
            _log.LogDebug("Đánh dấu hiện diện lỗi cho {User}: {Msg}", username, ex.Message);
        }
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
        // Ngừng làm tươi hiện diện cho kết nối này. KHÔNG chủ động ghi is_active=false: cứ để last_seen
        // cũ dần rồi sau 90s tự hiện offline — đúng như khi nhịp tim cũ ngừng, và giữ nguyên dòng thiết
        // bị cho "Quản lý thiết bị"/thu hồi tới khi người dùng đăng xuất hẳn.
        _presence.Drop(Context.ConnectionId);
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
