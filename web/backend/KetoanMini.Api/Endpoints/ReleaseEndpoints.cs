using System.Security.Claims;
using System.Security.Cryptography;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace KetoanMini.Api.Endpoints;

public static class ReleaseEndpoints
{
    public static void MapReleases(this IEndpointRouteBuilder app)
    {
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
                  WHERE app_target=@target AND is_published=TRUE AND apk_data IS NOT NULL
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

        user.MapGet("/{id:long}/download", async (long id, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            await using var r = await conn.Cmd(
                @"SELECT apk_file_name, apk_mime_type, apk_data
                  FROM app_releases
                  WHERE id=@id AND is_published=TRUE AND apk_data IS NOT NULL")
                .With("@id", id)
                .ExecuteReaderAsync();

            if (!await r.ReadAsync()) return Results.NotFound(new { message = "Không tìm thấy bản APK đã phát hành." });

            var fileName = string.IsNullOrWhiteSpace(r.Str("apk_file_name")) ? $"ketoan-hr-{id}.apk" : r.Str("apk_file_name");
            var mime = string.IsNullOrWhiteSpace(r.Str("apk_mime_type"))
                ? "application/vnd.android.package-archive"
                : r.Str("apk_mime_type");
            var bytes = (byte[])r.GetValue(r.GetOrdinal("apk_data"));
            return Results.File(bytes, mime, fileName, enableRangeProcessing: true);
        });

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

        g.MapPost("/{id:long}/publish", async (long id, PublishReleaseRequest req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            await using var conn = await db.OpenAsync();
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
            await NotifyReleaseChanged(hub);
            await db.RecordAudit(u.Username(), "Xóa bản phát hành APK", "Release", id.ToString(), "");
            return Results.NoContent();
        });
    }

    private static async Task<IResult> UploadRelease(HttpRequest request, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub)
    {
        if (!request.HasFormContentType)
            return Results.BadRequest(new { message = "Vui lòng gửi multipart/form-data kèm file APK." });

        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("apk") ?? form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { message = "Chưa chọn file APK." });
        if (!file.FileName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { message = "File cập nhật phải là APK." });

        var version = FormValue(form, "version");
        if (string.IsNullOrWhiteSpace(version))
            return Results.BadRequest(new { message = "Vui lòng nhập phiên bản." });

        var versionCode = ParseInt(FormValue(form, "versionCode"), 1);
        var target = NormalizeTarget(FormValue(form, "appTarget", "hr-apk"));
        var notes = FormValue(form, "releaseNotes");
        var isMandatory = ParseBool(FormValue(form, "isMandatory"));
        var isPublished = ParseBool(FormValue(form, "isPublished"));

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var apkBytes = ms.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(apkBytes)).ToLowerInvariant();
        var safeFileName = Path.GetFileName(file.FileName);

        await using var conn = await db.OpenAsync();
        await using var r = await conn.Cmd(
            @"INSERT INTO app_releases
                (app_target, version, version_code, release_notes, is_mandatory, is_published,
                 apk_file_name, apk_mime_type, apk_size, apk_sha256, apk_data, published_at, published_by, updated_at)
              VALUES
                (@target, @version, @versionCode, @notes, @mandatory, @published,
                 @fileName, @mime, @size, @sha, @data, CURRENT_TIMESTAMP, @by, CURRENT_TIMESTAMP)
              RETURNING id, app_target, version, version_code, release_notes, is_mandatory, is_published,
                        published_at, published_by, apk_file_name, apk_size, apk_sha256")
            .With("@target", target)
            .With("@version", version.Trim())
            .With("@versionCode", versionCode)
            .With("@notes", notes)
            .With("@mandatory", isMandatory)
            .With("@published", isPublished)
            .With("@fileName", safeFileName)
            .With("@mime", "application/vnd.android.package-archive")
            .With("@size", apkBytes.LongLength)
            .With("@sha", sha256)
            .With("@data", apkBytes)
            .With("@by", isPublished ? u.Username() : "")
            .ExecuteReaderAsync();

        await r.ReadAsync();
        var dto = ReadRelease(r);
        await NotifyReleaseChanged(hub);
        await db.RecordAudit(u.Username(), isPublished ? "Đăng bản cập nhật APK" : "Tạo bản cập nhật APK", "Release", dto.Version, safeFileName);
        return Results.Created($"/api/releases/{dto.Id}", dto);
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

    private static string NormalizeTarget(string? value)
        => string.IsNullOrWhiteSpace(value) ? "hr-apk" : value.Trim().ToLowerInvariant();

    private static string FormValue(IFormCollection form, string key, string fallback = "")
        => form.TryGetValue(key, out var value) ? value.ToString().Trim() : fallback;

    private static int ParseInt(string value, int fallback)
        => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    private static bool ParseBool(string value)
        => value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("on", StringComparison.OrdinalIgnoreCase);

    private static Task NotifyReleaseChanged(IHubContext<ChangesHub> hub)
        => hub.Clients.All.SendAsync("changed", "release");

    public record PublishReleaseRequest(bool IsPublished, bool? IsMandatory);
}
