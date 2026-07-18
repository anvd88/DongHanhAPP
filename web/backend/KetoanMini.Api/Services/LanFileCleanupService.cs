using KetoanMini.Api.Data;
using KetoanMini.Api.Endpoints;

namespace KetoanMini.Api.Services;

/// <summary>
/// Dọn blob file thường đã quá hạn và file mồ côi. Voice, ảnh và video là nội dung bền vững của
/// tin nhắn nên không bị sweep theo TTL; chỉ thao tác gỡ tin mới xóa chúng. Chạy mỗi giờ.
/// </summary>
public sealed class LanFileCleanupService(Database db, ILogger<LanFileCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await SweepAsync(ct); }
            catch (Exception ex) { logger.LogWarning("Dọn tệp giữ tạm lỗi: {Msg}", ex.Message); }
            try { await Task.Delay(TimeSpan.FromHours(1), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);

        // Metadata voice chưa upload xong không bao giờ được phát realtime. Nếu tiến trình/client chết,
        // dọn cứng sau 2 giờ để không tạo bong bóng rỗng hoặc phình DB.
        await conn.Cmd(
            @"DELETE FROM web_chat_messages
              WHERE kind = 'voice' AND has_blob = FALSE AND is_removed = FALSE
                AND created_at < CURRENT_TIMESTAMP - INTERVAL '2 hours'")
            .ExecuteNonQueryAsync(ct);

        // 1) Các FILE THƯỜNG đã quá hạn nhưng vẫn còn cờ has_blob → xóa tệp + bỏ cờ.
        // Lọc thêm bằng policy để bảo vệ voice, ảnh/video và bản ghi ghi-am-* từ APK cũ.
        var expired = new List<long>();
        await using (var r = await conn.Cmd(
            @"SELECT id, kind, file_name, file_mime FROM web_chat_messages
              WHERE has_blob = TRUE AND blob_expires_at IS NOT NULL AND blob_expires_at < CURRENT_TIMESTAMP")
            .ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                var kind = r.IsDBNull(1) ? "" : r.GetString(1);
                var name = r.IsDBNull(2) ? "" : r.GetString(2);
                var mime = r.IsDBNull(3) ? "" : r.GetString(3);
                if (!ChatAttachmentPolicy.IsPersistentAttachment(kind, name, mime)) expired.Add(r.GetInt64(0));
            }
        }
        foreach (var id in expired)
        {
            ChatEndpoints.TryDeleteBlob(id);
            await conn.Cmd("UPDATE web_chat_messages SET has_blob = FALSE, blob_expires_at = NULL WHERE id = @id")
                .With("@id", id).ExecuteNonQueryAsync(ct);
        }

        // 2) Tệp mồ côi trên đĩa (không còn tin nào giữ cờ has_blob) → xóa.
        foreach (var f in Directory.EnumerateFiles(ChatEndpoints.BlobDir(), "*.bin"))
        {
            if (ct.IsCancellationRequested) break;
            if (!long.TryParse(Path.GetFileNameWithoutExtension(f), out var mid)) continue;
            var keep = await conn.Cmd("SELECT COUNT(*) FROM web_chat_messages WHERE id = @id AND has_blob = TRUE")
                .With("@id", mid).ExecuteScalarAsync(ct);
            if (Convert.ToInt32(keep) == 0)
            {
                try { File.Delete(f); } catch { /* đang khóa → để lần sau */ }
            }
        }

        // 3) Tiến trình chết giữa upload có thể để lại file staging; final .bin không bị ảnh hưởng.
        foreach (var f in Directory.EnumerateFiles(ChatEndpoints.BlobDir(), "*.upload"))
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.Subtract(TimeSpan.FromHours(2))) File.Delete(f);
            }
            catch { /* đang upload/đang khóa → để lần sau */ }
        }
    }
}
