using KetoanMini.Api.Data;
using KetoanMini.Api.Endpoints;

namespace KetoanMini.Api.Services;

/// <summary>
/// Dọn các tệp "giữ tạm" (store-and-forward cho gửi tệp qua LAN khi người nhận offline) khỏi đĩa:
/// xóa tệp đã quá hạn (blob_expires_at) và tệp "mồ côi" không còn cờ has_blob. Chạy mỗi giờ.
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

        // 1) Các tin đã quá hạn nhưng vẫn còn cờ has_blob → xóa tệp + bỏ cờ.
        var expired = new List<long>();
        await using (var r = await conn.Cmd(
            @"SELECT id FROM web_chat_messages
              WHERE has_blob = TRUE AND blob_expires_at IS NOT NULL AND blob_expires_at < CURRENT_TIMESTAMP")
            .ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct)) expired.Add(r.GetInt64(0));
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
    }
}
