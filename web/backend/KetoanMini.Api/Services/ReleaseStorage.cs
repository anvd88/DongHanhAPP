using KetoanMini.Api.Data;

namespace KetoanMini.Api.Services;

/// <summary>
/// Kho APK trên ĐĨA. Bản phát hành nặng 40–100MB nên KHÔNG bao giờ được nạp trọn vào RAM:
/// lúc đăng thì chép thẳng từ luồng request xuống tệp, lúc tải thì để Kestrel gửi tệp (sendfile).
/// Bộ nhớ tiêu tốn chỉ là một buffer 80KB, bất kể APK to cỡ nào hay bao nhiêu người tải cùng lúc.
///
/// DB chỉ giữ metadata (phiên bản, kích thước, SHA-256) — cột <c>apk_data</c> của bản cũ được
/// <see cref="MigrateDatabaseBlobsAsync"/> chuyển ra đĩa lúc khởi động rồi bỏ trống.
/// </summary>
public static class ReleaseStorage
{
    /// <summary>Buffer chép luồng — 80KB là mức .NET dùng cho Stream.CopyTo, đủ nhanh mà không phình RAM.</summary>
    private const int CopyBufferBytes = 81920;

    /// <summary>Đọc bytea theo từng khúc 4MB khi di trú, để bản 100MB cũ không nằm trọn trong RAM.</summary>
    private const int MigrationChunkBytes = 4 * 1024 * 1024;

    private static readonly TimeSpan StaleUploadAge = TimeSpan.FromHours(2);
    private static string? _directory;

    /// <summary>
    /// Chốt thư mục chứa APK (Program gọi trước khi nhận request). Nên trỏ ra volume dữ liệu bền vững
    /// và đưa vào backup — để trong bin/ thì <c>dotnet clean</c> sẽ xóa mất các bản đã phát hành.
    /// </summary>
    public static void Configure(string? configuredPath, string contentRootPath)
    {
        var requested = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine("App_Data", "apk_releases")
            : configuredPath.Trim();
        var target = Path.GetFullPath(Path.IsPathRooted(requested) ? requested : Path.Combine(contentRootPath, requested));
        Directory.CreateDirectory(target);
        _directory = target;
        SweepStaleUploads(target);
    }

    /// <summary>Tiến trình chết giữa lúc đăng có thể để lại tệp staging; tệp .apk hoàn chỉnh không bị đụng.</summary>
    private static void SweepStaleUploads(string directory)
    {
        foreach (var f in Directory.EnumerateFiles(directory, "*.upload"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.Subtract(StaleUploadAge)) File.Delete(f);
            }
            catch { /* đang khóa → để lần khởi động sau */ }
        }
    }

    public static string Dir()
    {
        var dir = _directory ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "apk_releases");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Tệp APK của một bản phát hành; tên theo id nên không phụ thuộc tên tệp admin đặt.</summary>
    public static string ApkPath(long id) => Path.Combine(Dir(), $"{id}.apk");

    /// <summary>Tệp staging: ghi xong mới đổi tên sang <see cref="ApkPath"/> nên không có APK dở dang.</summary>
    public static string NewTempPath() => Path.Combine(Dir(), $"{Guid.NewGuid():N}.upload");

    public static void TryDelete(long id) => TryDeletePath(ApkPath(id));

    public static void TryDeletePath(string path)
    {
        try { File.Delete(path); } catch { /* không có / đang khóa → bỏ qua */ }
    }

    /// <summary>
    /// Chép luồng xuống tệp và băm SHA-256 NGAY TRONG một lượt đọc — không giữ lại byte nào.
    /// Trả về kích thước và mã băm để lưu metadata; app dùng chúng kiểm tra tệp tải về.
    /// </summary>
    public static async Task<(long Size, string Sha256)> WriteStreamAsync(Stream source, string path, CancellationToken ct)
    {
        using var hasher = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);
        long total = 0;

        await using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         CopyBufferBytes, useAsync: true))
        {
            var buffer = new byte[CopyBufferBytes];
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                hasher.AppendData(buffer.AsSpan(0, read));
                await fs.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            await fs.FlushAsync(ct);
        }

        return (total, Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant());
    }

    /// <summary>
    /// Di trú các bản phát hành cũ còn nằm trong cột <c>apk_data</c> ra đĩa, đọc theo khúc 4MB.
    /// Chạy một lần lúc khởi động; bản nào đã chuyển thì <c>apk_data</c> về NULL nên lần sau bỏ qua.
    /// </summary>
    public static async Task MigrateDatabaseBlobsAsync(Database db, ILogger logger, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);

        var ids = new List<long>();
        await using (var r = await conn.Cmd(
            "SELECT id FROM app_releases WHERE apk_data IS NOT NULL ORDER BY id").ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct)) ids.Add(r.GetInt64(0));
        }
        if (ids.Count == 0) return;

        logger.LogInformation("Chuyển {Count} bản phát hành APK từ DB ra đĩa ({Dir})…", ids.Count, Dir());
        foreach (var id in ids)
        {
            var temp = NewTempPath();
            try
            {
                var total = Convert.ToInt64(await conn.Cmd(
                    "SELECT octet_length(apk_data) FROM app_releases WHERE id=@id")
                    .With("@id", id).ExecuteScalarAsync(ct));

                await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                 CopyBufferBytes, useAsync: true))
                {
                    // substring(bytea …) đếm từ 1 và chỉ nhận int → offset phải ép int (APK trần 200MB).
                    var offset = 0;
                    while (offset < total)
                    {
                        var chunk = (byte[])(await conn.Cmd(
                            "SELECT substring(apk_data from @off for @len) FROM app_releases WHERE id=@id")
                            .With("@id", id).With("@off", offset + 1).With("@len", MigrationChunkBytes)
                            .ExecuteScalarAsync(ct))!;
                        if (chunk.Length == 0) break;
                        await fs.WriteAsync(chunk, ct);
                        offset += chunk.Length;
                    }
                    await fs.FlushAsync(ct);
                }

                File.Move(temp, ApkPath(id), overwrite: true);
                await conn.Cmd("UPDATE app_releases SET has_apk=TRUE, apk_data=NULL WHERE id=@id")
                    .With("@id", id).ExecuteNonQueryAsync(ct);
                logger.LogInformation("Đã chuyển APK bản {Id} ra đĩa ({Bytes} byte).", id, total);
            }
            catch (Exception ex)
            {
                // Giữ nguyên apk_data để lần khởi động sau thử lại; bản này tạm thời chưa phát hành được.
                TryDeletePath(temp);
                logger.LogError("Không chuyển được APK bản {Id} ra đĩa: {Msg}", id, ex.Message);
            }
        }

        // Cột bytea vừa bỏ trống để lại tuple chết; nhường autovacuum thu hồi (VACUUM FULL nếu cần gấp).
        logger.LogInformation("Di trú APK xong. Chạy VACUUM app_releases nếu muốn thu hồi dung lượng DB ngay.");
    }
}
