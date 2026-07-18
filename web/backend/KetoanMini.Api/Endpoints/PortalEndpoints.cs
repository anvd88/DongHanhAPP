using System.Security.Claims;
using KetoanMini.Api.Data;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Cổng thông tin công ty: tin tức nội bộ / thông báo, sự kiện (lịch) và phần giới thiệu công ty.
/// App KetoanAPK hiển thị (feed công khai), web admin quản trị (thêm/sửa/xóa).
/// </summary>
public static class PortalEndpoints
{
    // Giới hạn để tránh payload quá lớn khi app kéo feed.
    private const int FeedLimit = 60;

    public static void MapPortal(this WebApplication app)
    {
        var g = app.MapGroup("/api/portal").RequireAuthorization();

        // ---- Feed công khai cho app: giới thiệu + tin tức + sự kiện (chỉ mục đã đăng) ----
        g.MapGet("/feed", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var about = await ReadAbout(conn);
            var news = await ReadPosts(conn, kind: "news", publishedOnly: true, limit: FeedLimit);
            // Sự kiện: chỉ lấy SẮP TỚI (kể cả đang diễn ra trong hôm nay), gần nhất lên đầu — để
            // nhân viên "hóng" lịch nghỉ/sự kiện sắp diễn ra; sự kiện đã qua tự rơi khỏi feed.
            var events = await ReadPosts(conn, kind: "event", publishedOnly: true, upcomingOnly: true, limit: FeedLimit);
            return Results.Ok(new PortalFeedDto(about, news, events));
        });

        // ---- Danh sách bài (admin, gồm cả bài chưa đăng) ----
        g.MapGet("/posts", async (string? kind, ClaimsPrincipal principal, Database db) =>
        {
            if (!principal.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var k = NormalizeKind(kind);
            var posts = await ReadPosts(conn, kind: k, publishedOnly: false, limit: 500);
            return Results.Ok(posts);
        });

        g.MapPost("/posts", async (PortalPostRequest req, ClaimsPrincipal principal, Database db) =>
        {
            if (!principal.IsAdmin()) return Results.Forbid();
            var (kind, title, summary, body, cover, location, eventAt, error) = ValidatePost(req);
            if (error is not null) return Results.BadRequest(new { message = error });

            await using var conn = await db.OpenAsync();
            var author = principal.Username();
            var id = (long)(await conn.Cmd(
                """
                INSERT INTO app_portal_posts
                    (kind, title, summary, body, cover_image, location, event_at, pinned, published, author_username, created_at, updated_at)
                VALUES
                    (@kind, @title, @summary, @body, @cover, @location, @eventAt, @pinned, @published, @author, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                RETURNING id
                """)
                .With("@kind", kind)
                .With("@title", title)
                .With("@summary", summary)
                .With("@body", body)
                .With("@cover", (object?)cover)
                .With("@location", location)
                .With("@eventAt", (object?)eventAt)
                .With("@pinned", req.Pinned)
                .With("@published", req.Published)
                .With("@author", author)
                .ExecuteScalarAsync())!;

            await db.RecordAudit(author, "Đăng bài cổng thông tin", "PortalPost", id.ToString(), $"[{kind}] {title}");
            return Results.Ok(new { id });
        });

        g.MapPut("/posts/{id:long}", async (long id, PortalPostRequest req, ClaimsPrincipal principal, Database db) =>
        {
            if (!principal.IsAdmin()) return Results.Forbid();
            var (kind, title, summary, body, cover, location, eventAt, error) = ValidatePost(req);
            if (error is not null) return Results.BadRequest(new { message = error });

            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd(
                """
                UPDATE app_portal_posts SET
                    kind = @kind,
                    title = @title,
                    summary = @summary,
                    body = @body,
                    cover_image = @cover,
                    location = @location,
                    event_at = @eventAt,
                    pinned = @pinned,
                    published = @published,
                    updated_at = CURRENT_TIMESTAMP
                WHERE id = @id
                """)
                .With("@id", id)
                .With("@kind", kind)
                .With("@title", title)
                .With("@summary", summary)
                .With("@body", body)
                .With("@cover", (object?)cover)
                .With("@location", location)
                .With("@eventAt", (object?)eventAt)
                .With("@pinned", req.Pinned)
                .With("@published", req.Published)
                .ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound(new { message = "Bài viết không còn tồn tại." });

            await db.RecordAudit(principal.Username(), "Sửa bài cổng thông tin", "PortalPost", id.ToString(), $"[{kind}] {title}");
            return Results.NoContent();
        });

        g.MapDelete("/posts/{id:long}", async (long id, ClaimsPrincipal principal, Database db) =>
        {
            if (!principal.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("DELETE FROM app_portal_posts WHERE id = @id")
                .With("@id", id)
                .ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound(new { message = "Bài viết không còn tồn tại." });

            await db.RecordAudit(principal.Username(), "Xóa bài cổng thông tin", "PortalPost", id.ToString(), "");
            return Results.NoContent();
        });

        // ---- Giới thiệu công ty (một bản ghi) ----
        g.MapGet("/about", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            return Results.Ok(await ReadAbout(conn));
        });

        g.MapPut("/about", async (PortalAboutRequest req, ClaimsPrincipal principal, Database db) =>
        {
            if (!principal.IsAdmin()) return Results.Forbid();
            var title = Trim(req.Title, 300);
            var content = Trim(req.Content, 20000);
            var cover = string.IsNullOrWhiteSpace(req.CoverImage) ? null : req.CoverImage.Trim();
            var address = Trim(req.Address, 400);
            var hotline = Trim(req.Hotline, 100);
            var email = Trim(req.Email, 200);
            var website = Trim(req.Website, 200);

            await using var conn = await db.OpenAsync();
            await conn.Cmd(
                """
                INSERT INTO app_portal_about (id, title, content, cover_image, address, hotline, email, website, updated_at)
                VALUES (1, @title, @content, @cover, @address, @hotline, @email, @website, CURRENT_TIMESTAMP)
                ON CONFLICT (id) DO UPDATE SET
                    title = EXCLUDED.title,
                    content = EXCLUDED.content,
                    cover_image = EXCLUDED.cover_image,
                    address = EXCLUDED.address,
                    hotline = EXCLUDED.hotline,
                    email = EXCLUDED.email,
                    website = EXCLUDED.website,
                    updated_at = CURRENT_TIMESTAMP
                """)
                .With("@title", title)
                .With("@content", content)
                .With("@cover", (object?)cover)
                .With("@address", address)
                .With("@hotline", hotline)
                .With("@email", email)
                .With("@website", website)
                .ExecuteNonQueryAsync();

            await db.RecordAudit(principal.Username(), "Sửa giới thiệu công ty", "PortalAbout", "1", title);
            return Results.NoContent();
        });
    }

    private static async Task<List<PortalPostDto>> ReadPosts(
        Npgsql.NpgsqlConnection conn, string kind, bool publishedOnly, int limit, bool upcomingOnly = false)
    {
        // Tin tức: ghim trước, rồi mới nhất trước. Sự kiện: sắp diễn ra trước (theo thời gian tăng dần).
        var order = kind == "event"
            ? "p.event_at ASC NULLS LAST, p.created_at DESC"
            : "p.pinned DESC, p.created_at DESC";
        // Chỉ lấy sự kiện sắp tới: từ đầu ngày hôm nay trở đi (sự kiện trong hôm nay vẫn còn hiện).
        var upcomingFilter = upcomingOnly
            ? "AND (p.event_at IS NULL OR p.event_at >= date_trunc('day', CURRENT_TIMESTAMP))"
            : "";
        var rows = new List<PortalPostDto>();
        await using var r = await conn.Cmd(
            $"""
            SELECT p.id, p.kind, p.title, p.summary, p.body, p.cover_image, p.location, p.event_at,
                   p.pinned, p.published, p.author_username,
                   COALESCE(NULLIF(u.full_name, ''), p.author_username) AS author_name,
                   p.created_at, p.updated_at
            FROM app_portal_posts p
            LEFT JOIN app_users u ON u.username = p.author_username
            WHERE p.kind = @kind {(publishedOnly ? "AND p.published = TRUE" : "")} {upcomingFilter}
            ORDER BY {order}
            LIMIT @limit
            """)
            .With("@kind", kind)
            .With("@limit", limit)
            .ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var coverIdx = r.GetOrdinal("cover_image");
            rows.Add(new PortalPostDto(
                r.Long("id"),
                r.Str("kind"),
                r.Str("title"),
                r.Str("summary"),
                r.Str("body"),
                r.IsDBNull(coverIdx) ? null : r.Str("cover_image"),
                r.Str("location"),
                r.DtNull("event_at"),
                r.Bool("pinned"),
                r.Bool("published"),
                r.Str("author_username"),
                r.Str("author_name"),
                r.Dt("created_at"),
                r.Dt("updated_at")));
        }
        return rows;
    }

    private static async Task<PortalAboutDto> ReadAbout(Npgsql.NpgsqlConnection conn)
    {
        await using var r = await conn.Cmd(
            """
            SELECT title, content, cover_image, address, hotline, email, website, updated_at
            FROM app_portal_about WHERE id = 1
            """)
            .ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return new PortalAboutDto("", "", null, "", "", "", "", default);
        var coverIdx = r.GetOrdinal("cover_image");
        return new PortalAboutDto(
            r.Str("title"),
            r.Str("content"),
            r.IsDBNull(coverIdx) ? null : r.Str("cover_image"),
            r.Str("address"),
            r.Str("hotline"),
            r.Str("email"),
            r.Str("website"),
            r.Dt("updated_at"));
    }

    private static (string kind, string title, string summary, string body, string? cover, string location, DateTime? eventAt, string? error)
        ValidatePost(PortalPostRequest req)
    {
        var kind = NormalizeKind(req.Kind);
        var title = Trim(req.Title, 300);
        if (string.IsNullOrWhiteSpace(title))
            return (kind, "", "", "", null, "", null, "Vui lòng nhập tiêu đề.");
        var summary = Trim(req.Summary, 600);
        var body = Trim(req.Body, 20000);
        var cover = string.IsNullOrWhiteSpace(req.CoverImage) ? null : req.CoverImage.Trim();
        var location = Trim(req.Location, 300);
        var eventAt = req.EventAt;
        if (kind == "event" && eventAt is null)
            return (kind, title, summary, body, cover, location, null, "Sự kiện cần có thời gian diễn ra.");
        return (kind, title, summary, body, cover, location, eventAt, null);
    }

    private static string NormalizeKind(string? kind)
        => (kind ?? "").Trim().ToLowerInvariant() == "event" ? "event" : "news";

    private static string Trim(string? value, int max)
    {
        var v = (value ?? "").Trim();
        return v.Length > max ? v[..max] : v;
    }

    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd(
            """
            CREATE TABLE IF NOT EXISTS app_portal_posts (
                id bigserial NOT NULL PRIMARY KEY,
                kind varchar(16) NOT NULL DEFAULT 'news',
                title varchar(300) NOT NULL,
                summary varchar(600) NOT NULL DEFAULT '',
                body text NOT NULL DEFAULT '',
                cover_image text NULL,
                location varchar(300) NOT NULL DEFAULT '',
                event_at timestamptz NULL,
                pinned boolean NOT NULL DEFAULT FALSE,
                published boolean NOT NULL DEFAULT TRUE,
                author_username varchar(128) NOT NULL DEFAULT '',
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS ix_app_portal_posts_kind_created
                ON app_portal_posts (kind, published, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_app_portal_posts_event
                ON app_portal_posts (kind, event_at);

            CREATE TABLE IF NOT EXISTS app_portal_about (
                id int NOT NULL PRIMARY KEY DEFAULT 1 CHECK (id = 1),
                title varchar(300) NOT NULL DEFAULT '',
                content text NOT NULL DEFAULT '',
                cover_image text NULL,
                address varchar(400) NOT NULL DEFAULT '',
                hotline varchar(100) NOT NULL DEFAULT '',
                email varchar(200) NOT NULL DEFAULT '',
                website varchar(200) NOT NULL DEFAULT '',
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """)
            .ExecuteNonQueryAsync(ct);
    }
}

public record PortalPostDto(
    long Id,
    string Kind,
    string Title,
    string Summary,
    string Body,
    string? CoverImage,
    string Location,
    DateTime? EventAt,
    bool Pinned,
    bool Published,
    string AuthorUsername,
    string AuthorName,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record PortalAboutDto(
    string Title,
    string Content,
    string? CoverImage,
    string Address,
    string Hotline,
    string Email,
    string Website,
    DateTime UpdatedAt);

public record PortalFeedDto(
    PortalAboutDto About,
    List<PortalPostDto> News,
    List<PortalPostDto> Events);

public record PortalPostRequest(
    string? Kind,
    string? Title,
    string? Summary,
    string? Body,
    string? CoverImage,
    string? Location,
    DateTime? EventAt,
    bool Pinned = false,
    bool Published = true);

public record PortalAboutRequest(
    string? Title,
    string? Content,
    string? CoverImage,
    string? Address,
    string? Hotline,
    string? Email,
    string? Website);
