using System.Text.Json;
using KetoanMini.Api.BuildingBlocks.Messaging;
using KetoanMini.Api.Data;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KetoanMini.Api.BuildingBlocks.Realtime;

public sealed class RealtimeOptions
{
    public bool SseEnabled { get; set; } = true;
    public int RetentionHours { get; set; } = 48;
    public int PollMilliseconds { get; set; } = 2000;
    public int HeartbeatSeconds { get; set; } = 17;
    public int ReplayBatchSize { get; set; } = 128;
}

/// <summary>Thời gian giữ lại dòng outbox/inbox đã xử lý xong. Cấu hình ở khoá "Messaging".</summary>
public sealed class MessagingRetentionOptions
{
    public int ProcessedRetentionDays { get; set; } = 7;
}

public sealed record StoredRealtimeEvent(
    long SequenceNo, Guid EventId, string EventType, string Scope,
    string AudienceType, string? AudienceKey, string Payload, DateTimeOffset OccurredAt);

public sealed class RealtimeEventStore(Database db, IOptions<RealtimeOptions> configured)
{
    private readonly RealtimeOptions _options = configured.Value;

    /// <summary>
    /// Chủ đề luôn được gửi, kể cả khi kết nối không đăng ký. Đây là tin về chính phiên làm việc chứ
    /// không phải tin "một màn hình đã cũ", nên lọc mất là bỏ rơi người dùng ở lại với quyền cũ.
    /// </summary>
    private static readonly string[] AlwaysDelivered = ["access", "all"];

    /// <summary>
    /// Tên chủ đề hợp lệ. Máy khách gửi lên tên gì cũng phải đi qua đây trước khi vào câu SQL, để một
    /// danh sách chủ đề bịa ra không trở thành cách dò nội dung bảng sự kiện.
    /// </summary>
    private static readonly HashSet<string> KnownTopics = new(StringComparer.Ordinal)
    {
        "sales", "debts", "cash", "purchases", "catalog",
        "hr", "attendance", "presence", "tasks", "portal", "config", "audit",
        "release", "feedback", "talent", "notify", "liveness", "access",
    };

    /// <summary>Lọc danh sách chủ đề máy khách gửi lên; tên lạ bị bỏ đi chứ không làm hỏng kết nối.</summary>
    public static IReadOnlyCollection<string> ParseTopics(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(KnownTopics.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Kết nối này có cần khung đó không. Danh sách chủ đề rỗng = không đăng ký gì, nhận tất; đó là
    /// hành vi của máy khách đời trước và của APK, nên bộ lọc không được làm chúng câm.
    /// </summary>
    public static bool ShouldDeliver(string scope, IReadOnlyCollection<string> topics)
        => topics.Count == 0 || AlwaysDelivered.Contains(scope) || topics.Contains(scope);

    public async Task<long?> AppendAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        IntegrationEventEnvelope envelope,
        CancellationToken ct)
    {
        var (audienceType, audienceKey) = ParseAudience(envelope.Audience);
        var scope = ScopeOf(envelope);
        var payload = JsonSerializer.Serialize(new { scope }, IntegrationEventJson.Options);
        var value = await new NpgsqlCommand("""
            INSERT INTO realtime_events
                (event_id,event_type,scope,audience_type,audience_key,payload,occurred_at,expires_at)
            VALUES
                (@id,@type,@scope,@audienceType,@audienceKey,@payload::jsonb,@occurred,
                 @occurred+@retention)
            ON CONFLICT (event_id) DO NOTHING
            RETURNING sequence_no
            """, conn, tx)
        {
            Parameters =
            {
                new("@id", envelope.EventId), new("@type", PublicEventType(envelope.EventType)),
                new("@scope", scope), new("@audienceType", audienceType),
                new("@audienceKey", (object?)audienceKey ?? DBNull.Value), new("@payload", payload),
                // ToUniversalTime BẮT BUỘC: Npgsql chỉ nhận DateTimeOffset lệch 0 cho timestamptz.
                // Mốc thời gian trong phong bì do trigger sinh bằng jsonb_build_object, tức được kết
                // xuất theo TimeZone của KẾT NỐI đã ghi. Mọi kết nối không đặt UTC — psql, pgAdmin,
                // script bảo trì, một tiến trình khác — đẻ ra sự kiện +07:00 mà projector KHÔNG THỂ
                // chiếu; trước bản vá hàng đợi kẹt lại ở đó vĩnh viễn và không ai hay.
                new("@occurred", envelope.OccurredAt.ToUniversalTime()),
                new("@retention", TimeSpan.FromHours(Math.Clamp(_options.RetentionHours, 1, 168))),
            }
        }.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    /// <summary>
    /// Đọc các sự kiện mới cho một kết nối. KHÔNG lọc theo chủ đề ở đây, cố ý.
    ///
    /// Lọc trong câu SQL nghe có vẻ tiết kiệm hơn, nhưng nó phá con trỏ: mốc đọc chỉ tiến tới sự
    /// kiện CUỐI CÙNG ĐƯỢC GỬI, nên mọi sự kiện bị lọc bỏ nằm lại phía sau mốc và bị quét lại ở
    /// từng vòng lặp, mãi mãi cho tới khi hết hạn 48 giờ. Đọc hết rồi bỏ bớt lúc ghi khung thì mốc
    /// luôn tiến, mà thứ đắt tiền — khung gửi đi và cơn tải lại dữ liệu nó gây ra ở máy khách —
    /// vẫn được cắt. Xem <see cref="ShouldDeliver"/>.
    /// </summary>
    public async Task<IReadOnlyList<StoredRealtimeEvent>> ReadAsync(
        long after, string username, string sessionId, CancellationToken ct)
    {
        var result = new List<StoredRealtimeEvent>();
        await using var conn = await db.OpenAsync(ct);
        await using var reader = await conn.Cmd("""
            SELECT sequence_no,event_id,event_type,scope,audience_type,audience_key,payload::text,occurred_at
            FROM realtime_events
            WHERE sequence_no>@after AND expires_at>CURRENT_TIMESTAMP
              AND (audience_type='all'
                   OR (audience_type='user' AND lower(audience_key)=lower(@username))
                   OR (audience_type='session' AND audience_key=@session))
            ORDER BY sequence_no
            LIMIT @max
            """).With("@after", after).With("@username", username).With("@session", sessionId)
            .With("@max", Math.Clamp(_options.ReplayBatchSize, 16, 512)).ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(new(reader.GetInt64(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7)));
        return result;
    }

    public async Task<(long Min, long Max)> BoundsAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var r = await conn.Cmd("""
            SELECT COALESCE(MIN(sequence_no),0),COALESCE(MAX(sequence_no),0)
            FROM realtime_events WHERE expires_at>CURRENT_TIMESTAMP
            """).ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return (r.GetInt64(0), r.GetInt64(1));
    }

    /// <summary>
    /// Kiểm phiên còn hiệu lực VÀ làm mới hiện diện bằng đúng một câu lệnh.
    ///
    /// Chính kết nối SSE mới là nhịp hiện diện thật: nó chỉ tồn tại khi ứng dụng đang mở (APK đóng
    /// luồng khi xuống nền). Trước đây đây chỉ là một câu SELECT, còn last_seen thì trông chờ vào
    /// nhịp tim 5 phút/lần của APK — trong khi cửa sổ Online chỉ 90 giây, nên người ĐANG dùng app
    /// hiện Offline gần hết thời gian. Nhịp 30 giây ở đây ngắn hơn cửa sổ Online nên vừa giữ đúng
    /// trạng thái, vừa KHÔNG sinh sự kiện nào (xem UpdateGuards trong DatabaseChangePublisher).
    /// </summary>
    public async Task<bool> IsSessionAliveAsync(string username, string sessionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(sessionId)) return false;
        await using var conn = await db.OpenAsync(ct);
        return await conn.Cmd("""
            UPDATE user_sessions s SET last_seen=CURRENT_TIMESTAMP
            FROM app_users u
            WHERE lower(u.username)=lower(s.username)
              AND lower(s.username)=lower(@username) AND s.session_token=@session
              AND u.is_active=TRUE AND u.is_deleted=FALSE AND s.is_active=TRUE AND s.revoked=FALSE
            RETURNING 1
            """).With("@username", username).With("@session", sessionId).ExecuteScalarAsync(ct)
            is not null and not DBNull;
    }

    public async Task<int> CleanupAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.Cmd("DELETE FROM realtime_events WHERE expires_at<=CURRENT_TIMESTAMP")
            .ExecuteNonQueryAsync(ct);
    }

    private static (string Type, string? Key) ParseAudience(string[]? audience)
    {
        var item = audience?.FirstOrDefault() ?? "all";
        if (item.StartsWith("user:", StringComparison.OrdinalIgnoreCase)) return ("user", item[5..]);
        if (item.StartsWith("session:", StringComparison.OrdinalIgnoreCase)) return ("session", item[8..]);
        return ("all", null);
    }

    private static string ScopeOf(IntegrationEventEnvelope envelope)
    {
        if (envelope.Data.ValueKind == JsonValueKind.Object &&
            envelope.Data.TryGetProperty("scope", out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? "all";
        return "all";
    }

    private static string PublicEventType(string type) => type switch
    {
        var t when t.StartsWith("identity.access.changed", StringComparison.Ordinal) => "access.changed",
        var t when t.StartsWith("identity.session.revoked", StringComparison.Ordinal) => "session.revoked",
        var t when t.StartsWith("presence.changed", StringComparison.Ordinal) => "presence.changed",
        var t when t.StartsWith("feedback.resolved", StringComparison.Ordinal) => "feedback.resolved",
        _ => "invalidated",
    };
}
