using System.Text.Json;
using System.Threading.Channels;
using KetoanMini.Api.Data;

namespace KetoanMini.Api.Services;

/// <summary>Một việc đang chờ gửi, đã được worker giành lượt.</summary>
public sealed record OutboxMessage(long Id, string Kind, string Payload, int Attempts);

/// <summary>
/// Hàng chờ BỀN cho việc-có-hậu-quả (hiện tại: thông báo đẩy). Endpoint chỉ ghi một dòng vào bảng rồi
/// trả kết quả ngay; <see cref="OutboxWorker"/> mới thật sự gọi FCM.
///
/// Vì sao không dùng LISTEN/NOTIFY cho việc này: thông báo của PostgreSQL KHÔNG bền — mất kết nối là
/// mất tin. Với tín hiệu "dữ liệu đã cũ" thì không sao (máy khách nạp lại là xong), nhưng "gửi thông
/// báo cho người này" mà rơi thì không có cách nào biết. Việc nào có hậu quả thì phải nằm trong BẢNG.
///
/// Đổi lại được hai thứ:
///   • Độ trễ: gọi FCM không còn nằm trong request. Trước đây duyệt một đơn phải chờ multicast HTTP
///     tới mọi thiết bị admin xong mới trả lời.
///   • Độ bền: FCM lỗi/mạng đứt thì việc vẫn nằm đó, thử lại theo cấp số nhân qua cả lần khởi động lại.
///
/// Ngữ nghĩa AT-LEAST-ONCE (có thể gửi lặp, không mất). Hợp với push vì máy nhận đã khử trùng theo
/// notif_id sẵn (xem NotificationCenter trong app).
///
/// GIỚI HẠN CÒN LẠI, nói thẳng: phần lớn endpoint không mở transaction, nên đây là "gần như nguyên tử"
/// chứ chưa phải outbox nguyên tử đúng nghĩa — vẫn còn khe hẹp giữa lúc ghi nghiệp vụ và lúc ghi hàng
/// chờ. Bịt hẳn phải bọc transaction cho mọi đường ghi, là việc lớn hơn nhiều.
/// </summary>
public sealed class OutboxQueue(Database db, ILogger<OutboxQueue> log)
{
    public const string KindUserPush = "push.user";
    public const string KindAdminsPush = "push.admins";
    public const string KindAllPush = "push.all";

    /// <summary>Thử tối đa bấy nhiêu lần rồi bỏ vào "chết" và log to — tổng thời gian lùi ~30 phút.</summary>
    public const int MaxAttempts = 8;

    /// <summary>
    /// Thời gian giành lượt. Worker chết giữa chừng thì hết hạn này việc tự quay lại hàng chờ, nên
    /// không có việc nào kẹt vĩnh viễn ở trạng thái "đang xử lý".
    /// </summary>
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    /// <summary>Dọn việc đã xong cũ hơn mốc này. Cũng là cửa sổ hiệu lực của khoá khử trùng.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    // Đánh thức worker NGAY khi có việc mới (cùng tiến trình nên không tốn gì). Worker vẫn quét định
    // kỳ như lưới đỡ, phòng khi việc được ghi từ nơi khác hoặc cú đánh thức này rơi.
    private readonly Channel<byte> _wake = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    internal ChannelReader<byte> Wake => _wake.Reader;

    public static async Task EnsureTables(Database database, CancellationToken ct = default)
    {
        await using var conn = await database.OpenAsync(ct);
        await conn.Cmd("""
            CREATE TABLE IF NOT EXISTS app_outbox (
                id bigserial PRIMARY KEY,
                kind varchar(40) NOT NULL,
                payload jsonb NOT NULL,
                dedupe_key varchar(300) NOT NULL DEFAULT '',
                status varchar(12) NOT NULL DEFAULT 'pending',
                attempts integer NOT NULL DEFAULT 0,
                available_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                processed_at timestamptz NULL,
                last_error text NOT NULL DEFAULT ''
            );
            -- Worker chỉ hỏi đúng "việc đang chờ đã tới hạn": index riêng phần cho rẻ.
            CREATE INDEX IF NOT EXISTS ix_app_outbox_ready ON app_outbox (available_at, id)
                WHERE status = 'pending';
            -- Khử trùng trong cửa sổ lưu: gửi lại cùng một sự kiện cho cùng một người thì bỏ qua.
            CREATE UNIQUE INDEX IF NOT EXISTS ux_app_outbox_dedupe ON app_outbox (dedupe_key)
                WHERE dedupe_key <> '';
            """).ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Xếp một việc vào hàng chờ. <paramref name="dedupeKey"/> PHẢI gồm cả NGƯỜI NHẬN, không chỉ chữ
    /// ký sự kiện: một tin nhắn chat gửi cho nhiều người dùng chung notif_id, nếu khoá chỉ có chữ ký
    /// thì chỉ người đầu tiên được xếp hàng, những người sau bị coi là trùng và mất thông báo.
    /// </summary>
    public async Task EnqueueAsync(string kind, object payload, string dedupeKey, CancellationToken ct = default)
    {
        try
        {
            await using var conn = await db.OpenAsync(ct);
            await conn.Cmd("""
                INSERT INTO app_outbox (kind, payload, dedupe_key)
                VALUES (@kind, @payload::jsonb, @dedupe)
                ON CONFLICT DO NOTHING
                """)
                .With("@kind", kind)
                .With("@payload", JsonSerializer.Serialize(payload))
                .With("@dedupe", dedupeKey)
                .ExecuteNonQueryAsync(ct);
            _wake.Writer.TryWrite(0);
        }
        catch (Exception ex)
        {
            // Không để việc phụ (thông báo) làm hỏng việc chính (nghiệp vụ vừa ghi xong). Ghi log ở mức
            // cảnh báo vì đây LÀ mất thông báo — không im lặng nuốt.
            log.LogWarning("Không xếp được việc {Kind} vào hàng chờ: {Msg}", kind, ex.Message);
        }
    }

    /// <summary>
    /// Giành một lô việc tới hạn. FOR UPDATE SKIP LOCKED cho phép nhiều worker chạy song song mà không
    /// giẫm chân; đẩy available_at tới trước chính là "thuê lượt" (xem <see cref="Lease"/>).
    /// </summary>
    public async Task<IReadOnlyList<OutboxMessage>> ClaimAsync(int max, CancellationToken ct = default)
    {
        var claimed = new List<OutboxMessage>();
        await using var conn = await db.OpenAsync(ct);
        await using var r = await conn.Cmd("""
            UPDATE app_outbox SET attempts = attempts + 1, available_at = CURRENT_TIMESTAMP + @lease
            WHERE id IN (
                SELECT id FROM app_outbox
                WHERE status = 'pending' AND available_at <= CURRENT_TIMESTAMP
                ORDER BY id
                FOR UPDATE SKIP LOCKED
                LIMIT @max
            )
            RETURNING id, kind, payload::text AS payload, attempts
            """)
            .With("@lease", Lease).With("@max", max).ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            claimed.Add(new OutboxMessage(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt32(3)));
        return claimed;
    }

    public async Task CompleteAsync(long id, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd(
            "UPDATE app_outbox SET status='done', processed_at=CURRENT_TIMESTAMP, last_error='' WHERE id=@id")
            .With("@id", id).ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Đánh dấu một lần thử hỏng: hẹn thử lại theo cấp số nhân, hoặc bỏ vào "chết" nếu đã quá số lần.
    /// Việc chết KHÔNG bị xóa — để còn lần ra được chuyện gì đã hỏng.
    /// </summary>
    public async Task FailAsync(long id, int attempts, string error, CancellationToken ct = default)
    {
        var dead = attempts >= MaxAttempts;
        var backoff = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempts), 600));
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd("""
            UPDATE app_outbox
               SET status = CASE WHEN @dead THEN 'dead' ELSE 'pending' END,
                   available_at = CURRENT_TIMESTAMP + @backoff,
                   last_error = @err
             WHERE id = @id
            """)
            .With("@id", id).With("@dead", dead).With("@backoff", backoff)
            .With("@err", error.Length > 500 ? error[..500] : error)
            .ExecuteNonQueryAsync(ct);

        if (dead)
            log.LogError("Việc {Id} đã bỏ sau {Attempts} lần thử — thông báo này KHÔNG tới nơi: {Err}",
                id, attempts, error);
    }

    /// <summary>
    /// Số việc đã bỏ hẳn. Một dòng log lúc nó chết thì trôi mất giữa hàng nghìn dòng khác — con số này
    /// được nhắc lại định kỳ để "có thông báo không tới nơi" là thứ nhìn thấy được, không phải thứ chỉ
    /// phát hiện khi người dùng phàn nàn.
    /// </summary>
    public async Task<int> DeadCountAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        return Convert.ToInt32(await conn.Cmd("SELECT COUNT(*) FROM app_outbox WHERE status='dead'")
            .ExecuteScalarAsync(ct));
    }

    /// <summary>Dọn việc đã xong quá hạn lưu. Việc "chết" giữ lại để còn điều tra.</summary>
    public async Task<int> CleanupAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        return await conn.Cmd(
            "DELETE FROM app_outbox WHERE status='done' AND processed_at < CURRENT_TIMESTAMP - @keep")
            .With("@keep", Retention).ExecuteNonQueryAsync(ct);
    }
}
