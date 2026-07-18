using System.Security.Claims;
using System.Text;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Realtime;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace KetoanMini.Api.Endpoints;

public static class ReleaseEndpoints
{
    public static void MapReleases(this IEndpointRouteBuilder app)
    {
        var publicReleases = app.MapGroup("/api/releases/public");

        publicReleases.MapGet("/latest", async (string? appTarget, Database db) =>
        {
            var target = NormalizeTarget(appTarget);

            await using var conn = await db.OpenAsync();
            await using var r = await conn.Cmd(
                @"SELECT id, release_notes, is_mandatory, published_at
                  FROM app_releases
                  WHERE app_target=@target AND is_published=TRUE AND has_apk=TRUE
                  ORDER BY version_code DESC, published_at DESC, id DESC
                  LIMIT 1")
                .With("@target", target)
                .ExecuteReaderAsync();

            if (!await r.ReadAsync())
                return Results.Ok(new { hasUpdate = false, appTarget = target });

            var id = r.Long("id");
            return Results.Ok(new
            {
                hasUpdate = true,
                id,
                releaseNotes = r.Str("release_notes"),
                isMandatory = r.Bool("is_mandatory"),
                publishedAt = r.Dt("published_at"),
                downloadUrl = $"/api/releases/public/{id}/download",
            });
        }).AllowAnonymous();

        publicReleases.MapGet("/{id:long}/download", DownloadPublishedReleasePublic).AllowAnonymous();

        var user = app.MapGroup("/api/releases").RequireAuthorization();

        user.MapGet("/latest", async (string? appTarget, int? currentVersionCode, Database db) =>
        {
            var target = NormalizeTarget(appTarget);
            var current = Math.Max(0, currentVersionCode ?? 0);

            await using var conn = await db.OpenAsync();
            await using var r = await conn.Cmd(
                @"SELECT id, app_target, version, version_code, release_notes, is_mandatory,
                         published_at, apk_file_name, apk_size, apk_sha256
                  FROM app_releases
                  WHERE app_target=@target AND is_published=TRUE AND has_apk=TRUE
                  ORDER BY version_code DESC, published_at DESC, id DESC
                  LIMIT 1")
                .With("@target", target)
                .ExecuteReaderAsync();

            if (!await r.ReadAsync())
                return Results.Ok(new { hasUpdate = false, appTarget = target, currentVersionCode = current });

            var id = r.Long("id");
            var versionCode = r.Int("version_code");
            return Results.Ok(new
            {
                hasUpdate = versionCode > current,
                id,
                appTarget = r.Str("app_target"),
                version = r.Str("version"),
                versionCode,
                releaseNotes = r.Str("release_notes"),
                isMandatory = r.Bool("is_mandatory"),
                publishedAt = r.Dt("published_at"),
                apkFileName = r.Str("apk_file_name"),
                apkSize = r.Long("apk_size"),
                apkSha256 = r.Str("apk_sha256"),
                downloadUrl = $"/api/releases/{id}/download",
            });
        });

        user.MapGet("/{id:long}/download", DownloadPublishedRelease);

        var g = app.MapGroup("/api/releases").RequireAuthorization(p => p.RequireRole("Admin"));

        g.MapGet("/", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var list = new List<ReleaseDto>();
            await using var r = await conn.Cmd(
                @"SELECT id, app_target, version, version_code, release_notes, is_mandatory, is_published,
                         published_at, published_by, apk_file_name, apk_size, apk_sha256
                  FROM app_releases ORDER BY id DESC LIMIT 50").ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(ReadRelease(r));
            return Results.Ok(list);
        });

        g.MapPost("/", UploadRelease).DisableAntiforgery();

        g.MapPost("/{id:long}/publish", async (long id, PublishReleaseRequest req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub, PushService push) =>
        {
            await using var conn = await db.OpenAsync();
            string version = "", notes = "";
            int versionCode = 0;
            if (req.IsPublished)
            {
                string target;
                bool hasApk;
                await using (var release = await conn.Cmd(
                    @"SELECT app_target, version, version_code, release_notes, has_apk
                      FROM app_releases
                      WHERE id=@id")
                    .With("@id", id)
                    .ExecuteReaderAsync())
                {
                    if (!await release.ReadAsync()) return Results.NotFound(new { message = "KhÃ´ng tÃ¬m tháº¥y báº£n phÃ¡t hÃ nh." });
                    target = release.Str("app_target");
                    version = release.Str("version");
                    versionCode = release.Int("version_code");
                    notes = release.Str("release_notes");
                    hasApk = release.Bool("has_apk");
                }

                if (!hasApk) return Results.BadRequest(new { message = "Ban phat hanh chua co file APK." });

                var latestPublished = await ReadLatestPublishedVersionCode(conn, target, id);
                if (versionCode <= latestPublished)
                    return Results.BadRequest(new { message = VersionCodeMustIncreaseMessage(latestPublished) });
            }

            var changed = await conn.Cmd(
                @"UPDATE app_releases
                  SET is_published=@pub,
                      is_mandatory=COALESCE(@mandatory, is_mandatory),
                      published_at=CASE WHEN @pub THEN CURRENT_TIMESTAMP ELSE published_at END,
                      published_by=CASE WHEN @pub THEN @by ELSE published_by END,
                      updated_at=CURRENT_TIMESTAMP
                  WHERE id=@id")
                .With("@id", id)
                .With("@pub", req.IsPublished)
                .With("@mandatory", req.IsMandatory)
                .With("@by", u.Username())
                .ExecuteNonQueryAsync();

            if (changed == 0) return Results.NotFound(new { message = "Không tìm thấy bản phát hành." });
            await NotifyReleaseChanged(hub);
            if (req.IsPublished) await SendReleasePush(push, version, versionCode, notes);
            await db.RecordAudit(u.Username(), req.IsPublished ? "Phát hành APK" : "Gỡ phát hành APK", "Release", id.ToString(), "");
            return Results.NoContent();
        });

        g.MapDelete("/{id:long}", async (long id, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            await using var conn = await db.OpenAsync();
            var changed = await conn.Cmd("DELETE FROM app_releases WHERE id=@id")
                .With("@id", id)
                .ExecuteNonQueryAsync();
            if (changed == 0) return Results.NotFound(new { message = "Không tìm thấy bản phát hành." });
            ReleaseStorage.TryDelete(id);
            await NotifyReleaseChanged(hub);
            await db.RecordAudit(u.Username(), "Xóa bản phát hành APK", "Release", id.ToString(), "");
            return Results.NoContent();
        });

        // Gỡ HÀNG LOẠT các bản phát hành đã tích chọn (kèm APK trong DB) trong một lần.
        g.MapPost("/bulk-delete", async (BulkDeleteReleasesRequest req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            var ids = (req.Ids ?? Array.Empty<long>()).Where(x => x > 0).Distinct().ToArray();
            if (ids.Length == 0) return Results.BadRequest(new { message = "Chưa chọn bản phát hành nào." });

            await using var conn = await db.OpenAsync();
            var deleted = await conn.Cmd("DELETE FROM app_releases WHERE id = ANY(@ids)")
                .With("@ids", ids)
                .ExecuteNonQueryAsync();
            foreach (var id in ids) ReleaseStorage.TryDelete(id);
            await NotifyReleaseChanged(hub);
            await db.RecordAudit(u.Username(), "Xóa hàng loạt bản phát hành APK", "Release", string.Join(",", ids), $"{deleted} bản");
            return Results.Ok(new { deleted });
        });
    }

    /// <summary>
    /// Đăng bản cập nhật. Đọc multipart THEO LUỒNG (MultipartReader) thay vì ReadFormAsync: APK chảy
    /// thẳng từ socket xuống đĩa qua buffer 80KB nên một bản 100MB cũng không chiếm 100MB RAM, và
    /// không phải nhờ tệp tạm của hệ thống (thư mục temp có thể nằm trên RAM disk).
    /// </summary>
    private static async Task<IResult> UploadRelease(HttpRequest request, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub, PushService push)
    {
        var boundary = ReadBoundary(request);
        if (boundary is null)
            return Results.BadRequest(new { message = "Vui lòng gửi multipart/form-data kèm file APK." });

        // Nâng trần body cho RIÊNG endpoint này TRƯỚC khi đọc: Kestrel đang áp trần chung 16MB
        // (PayloadLimits.MaxJsonBodyBytes đặt trong Program) mà APK thường 40–100MB. Không nâng thì
        // Kestrel hủy request giữa chừng và trình duyệt chỉ báo "Failed to fetch".
        var maxBodySize = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxBodySize is { IsReadOnly: false })
            maxBodySize.MaxRequestBodySize = PayloadLimits.MaxApkBytes;

        var ct = request.HttpContext.RequestAborted;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var reader = new MultipartReader(boundary, request.Body) { BodyLengthLimit = PayloadLimits.MaxApkBytes };
        string? tempPath = null;
        var apkFileName = "";
        long apkSize = 0;
        var sha256 = "";

        try
        {
            while (await reader.ReadNextSectionAsync(ct) is { } section)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)) continue;

                if (disposition.IsFileDisposition())
                {
                    // Chỉ nhận tệp ĐẦU TIÊN; phần thừa vẫn phải đọc hết để không cắt ngang luồng.
                    if (tempPath is not null) continue;
                    apkFileName = Path.GetFileName(HeaderUtilities.RemoveQuotes(disposition.FileName).Value ?? "");
                    tempPath = ReleaseStorage.NewTempPath();
                    (apkSize, sha256) = await ReleaseStorage.WriteStreamAsync(section.Body, tempPath, ct);
                }
                else if (disposition.IsFormDisposition())
                {
                    var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value ?? "";
                    using var text = new StreamReader(section.Body, Encoding.UTF8);
                    fields[name] = (await text.ReadToEndAsync(ct)).Trim();
                }
            }

            if (tempPath is null || apkSize == 0)
                return Results.BadRequest(new { message = "Chưa chọn file APK." });
            if (!apkFileName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { message = "File cập nhật phải là APK." });

            var version = Field(fields, "version");
            if (string.IsNullOrWhiteSpace(version))
                return Results.BadRequest(new { message = "Vui lòng nhập phiên bản." });

            var versionCode = ParseInt(Field(fields, "versionCode"), 1);
            var target = NormalizeTarget(Field(fields, "appTarget", "hr-apk"));
            var notes = Field(fields, "releaseNotes");
            var isMandatory = ParseBool(Field(fields, "isMandatory"));
            var isPublished = ParseBool(Field(fields, "isPublished"));

            await using var conn = await db.OpenAsync(ct);
            if (isPublished)
            {
                var latestPublished = await ReadLatestPublishedVersionCode(conn, target);
                if (versionCode <= latestPublished)
                    return Results.BadRequest(new { message = VersionCodeMustIncreaseMessage(latestPublished) });
            }

            // Ghi dòng với has_apk=FALSE trước để lấy id (tên tệp APK theo id). Chỉ khi tệp đã nằm đúng
            // chỗ mới bật cờ — nên bản phát hành không bao giờ được quảng bá khi chưa có APK trên đĩa.
            ReleaseDto dto;
            await using (var r = await conn.Cmd(
                @"INSERT INTO app_releases
                    (app_target, version, version_code, release_notes, is_mandatory, is_published,
                     apk_file_name, apk_mime_type, apk_size, apk_sha256, has_apk, published_at, published_by, updated_at)
                  VALUES
                    (@target, @version, @versionCode, @notes, @mandatory, @published,
                     @fileName, @mime, @size, @sha, FALSE, CURRENT_TIMESTAMP, @by, CURRENT_TIMESTAMP)
                  RETURNING id, app_target, version, version_code, release_notes, is_mandatory, is_published,
                            published_at, published_by, apk_file_name, apk_size, apk_sha256")
                .With("@target", target)
                .With("@version", version.Trim())
                .With("@versionCode", versionCode)
                .With("@notes", notes)
                .With("@mandatory", isMandatory)
                .With("@published", isPublished)
                .With("@fileName", apkFileName)
                .With("@mime", "application/vnd.android.package-archive")
                .With("@size", apkSize)
                .With("@sha", sha256)
                .With("@by", isPublished ? u.Username() : "")
                .ExecuteReaderAsync(ct))
            {
                await r.ReadAsync(ct);
                dto = ReadRelease(r);
            }

            try
            {
                File.Move(tempPath, ReleaseStorage.ApkPath(dto.Id), overwrite: true);
                tempPath = null;
                await conn.Cmd("UPDATE app_releases SET has_apk=TRUE WHERE id=@id")
                    .With("@id", dto.Id).ExecuteNonQueryAsync(ct);
            }
            catch (Exception)
            {
                // Không cất được tệp → gỡ luôn dòng vừa ghi để admin đăng lại, không để bản "rỗng".
                await conn.Cmd("DELETE FROM app_releases WHERE id=@id").With("@id", dto.Id).ExecuteNonQueryAsync(ct);
                throw;
            }

            await NotifyReleaseChanged(hub);
            if (isPublished) await SendReleasePush(push, dto.Version, dto.VersionCode, notes);
            await db.RecordAudit(u.Username(), isPublished ? "Đăng bản cập nhật APK" : "Tạo bản cập nhật APK", "Release", dto.Version, apkFileName);
            return Results.Created($"/api/releases/{dto.Id}", dto);
        }
        catch (InvalidDataException)
        {
            // MultipartReader ném khi một phần vượt BodyLengthLimit.
            return Results.Json(new { message = $"APK vượt giới hạn {PayloadLimits.MaxApkBytes / 1024 / 1024} MB." }, statusCode: 413);
        }
        finally
        {
            if (tempPath is not null) ReleaseStorage.TryDeletePath(tempPath);
        }
    }

    private static ReleaseDto ReadRelease(Npgsql.NpgsqlDataReader r) => new(
        r.Long("id"),
        r.Str("app_target"),
        r.Str("version"),
        r.Int("version_code"),
        r.Str("release_notes"),
        r.Bool("is_mandatory"),
        r.Bool("is_published"),
        r.Dt("published_at"),
        r.Str("published_by"),
        r.Str("apk_file_name"),
        r.Long("apk_size"),
        r.Str("apk_sha256"));

    private static async Task<IResult> DownloadPublishedRelease(long id, Database db)
        => await ServeApk(id, db, downloadName: null);

    private static async Task<IResult> DownloadPublishedReleasePublic(long id, Database db)
        => await ServeApk(id, db, downloadName: "KetoanAPK.apk");

    /// <summary>
    /// Gửi APK TỪ ĐĨA: Results.File(đường-dẫn) để Kestrel dùng sendfile, không nạp byte nào vào RAM.
    /// Nhờ vậy 50 máy tải một bản 100MB cùng lúc cũng không tốn 5GB RAM như cách đọc bytea trước đây.
    /// enableRangeProcessing giữ nguyên để app tải tiếp được khi mạng đứt giữa chừng.
    /// </summary>
    private static async Task<IResult> ServeApk(long id, Database db, string? downloadName)
    {
        string fileName, mime;
        await using (var conn = await db.OpenAsync())
        {
            await using var r = await conn.Cmd(
                @"SELECT apk_file_name, apk_mime_type
                  FROM app_releases
                  WHERE id=@id AND is_published=TRUE AND has_apk=TRUE")
                .With("@id", id)
                .ExecuteReaderAsync();

            if (!await r.ReadAsync()) return Results.NotFound(new { message = "Không tìm thấy bản APK đã phát hành." });

            fileName = string.IsNullOrWhiteSpace(r.Str("apk_file_name")) ? $"ketoan-hr-{id}.apk" : r.Str("apk_file_name");
            mime = string.IsNullOrWhiteSpace(r.Str("apk_mime_type"))
                ? "application/vnd.android.package-archive"
                : r.Str("apk_mime_type");
        }

        var path = ReleaseStorage.ApkPath(id);
        if (!File.Exists(path)) return Results.NotFound(new { message = "Không tìm thấy bản APK đã phát hành." });

        return Results.File(path, mime, downloadName ?? fileName, enableRangeProcessing: true);
    }

    private static async Task<int> ReadLatestPublishedVersionCode(Npgsql.NpgsqlConnection conn, string target, long? excludeId = null)
    {
        var sql = excludeId.HasValue
            ? @"SELECT COALESCE(MAX(version_code), 0)
                FROM app_releases
                WHERE app_target=@target AND is_published=TRUE AND id<>@excludeId"
            : @"SELECT COALESCE(MAX(version_code), 0)
                FROM app_releases
                WHERE app_target=@target AND is_published=TRUE";
        using var cmd = conn.Cmd(sql).With("@target", target);
        if (excludeId.HasValue) cmd.With("@excludeId", excludeId.Value);

        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? 0 : Convert.ToInt32(value);
    }

    private static string VersionCodeMustIncreaseMessage(int latestPublished)
        => latestPublished > 0
            ? $"Ma ban phat hanh phai lon hon ma dang phat hanh hien tai ({latestPublished})."
            : "Ma ban phat hanh phai lon hon 0.";

    private static string NormalizeTarget(string? value)
        => string.IsNullOrWhiteSpace(value) ? "hr-apk" : value.Trim().ToLowerInvariant();

    /// <summary>Boundary của multipart; null nghĩa là request không phải multipart hợp lệ.</summary>
    private static string? ReadBoundary(HttpRequest request)
    {
        if (!request.HasFormContentType || !MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType))
            return null;
        var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;
        return string.IsNullOrWhiteSpace(boundary) ? null : boundary;
    }

    private static string Field(IReadOnlyDictionary<string, string> fields, string key, string fallback = "")
        => fields.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : fallback;

    private static int ParseInt(string value, int fallback)
        => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    private static bool ParseBool(string value)
        => value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("on", StringComparison.OrdinalIgnoreCase);

    private static Task NotifyReleaseChanged(IHubContext<ChangesHub> hub)
        => hub.Clients.All.SendAsync("changed", "release");

    /// <summary>
    /// Đẩy thông báo tới MỌI thiết bị khi admin phát hành bản mới. "Chữ ký" <c>release:{versionCode}</c>
    /// ổn định để chống bắn trùng; target <c>AppUpdate</c> để app mở hộp thoại cập nhật khi bấm vào.
    /// </summary>
    private static async Task SendReleasePush(PushService push, string version, int versionCode, string notes)
    {
        var body = string.IsNullOrWhiteSpace(notes)
            ? "Bản cập nhật mới đã sẵn sàng. Nhấn để cập nhật."
            : notes.Trim();
        await push.SendToAllAsync($"Đã có bản cập nhật {version}", body, $"release:{versionCode}", "AppUpdate");
    }

    public record PublishReleaseRequest(bool IsPublished, bool? IsMandatory);

    public record BulkDeleteReleasesRequest(long[]? Ids);
}
