using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KetoanMini.Api.Data;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Khảo sát &amp; bình chọn — Đợt 6, nhiệm vụ 20. Hỗ trợ:
///  • Tạo biểu mẫu khảo sát/bình chọn nhiều câu hỏi (chọn 1, chọn nhiều, tự luận, chấm điểm).
///  • Phản hồi ẨN DANH THỰC SỰ: KHÔNG lưu username. Chống gửi trùng bằng HMAC(username) một chiều —
///    đủ để chặn trả lời 2 lần nhưng KHÔNG truy ngược ra người trả lời (khóa HMAC = Jwt:Key server giữ).
///  • Tổng hợp kết quả chỉ ở dạng SỐ ĐẾM / danh sách câu trả lời không kèm danh tính.
/// Quản trị tạo/đóng/xem kết quả; mọi nhân viên đăng nhập xem khảo sát đang mở và gửi phản hồi.
/// </summary>
public static class SurveyEndpoints
{
    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd("""
            CREATE TABLE IF NOT EXISTS surveys (
                id uuid PRIMARY KEY,
                title varchar(300) NOT NULL DEFAULT '',
                description text NOT NULL DEFAULT '',
                is_anonymous boolean NOT NULL DEFAULT TRUE,
                allow_multiple boolean NOT NULL DEFAULT FALSE,
                is_active boolean NOT NULL DEFAULT TRUE,
                created_by varchar(128) NOT NULL DEFAULT '',
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                closes_at timestamptz NULL
            );
            CREATE TABLE IF NOT EXISTS survey_questions (
                id uuid PRIMARY KEY,
                survey_id uuid NOT NULL REFERENCES surveys(id) ON DELETE CASCADE,
                question text NOT NULL DEFAULT '',
                qtype varchar(16) NOT NULL DEFAULT 'single',
                options jsonb NOT NULL DEFAULT '[]',
                order_no int NOT NULL DEFAULT 0,
                required boolean NOT NULL DEFAULT TRUE
            );
            CREATE INDEX IF NOT EXISTS ix_survey_questions_survey ON survey_questions(survey_id, order_no);

            CREATE TABLE IF NOT EXISTS survey_responses (
                id uuid PRIMARY KEY,
                survey_id uuid NOT NULL REFERENCES surveys(id) ON DELETE CASCADE,
                respondent_hash varchar(64) NULL,
                submitted_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            -- Chống trả lời 2 lần cho khảo sát chỉ-một-lần: duy nhất theo (survey, hash), bỏ qua khi hash NULL.
            CREATE UNIQUE INDEX IF NOT EXISTS ux_survey_once ON survey_responses(survey_id, respondent_hash)
                WHERE respondent_hash IS NOT NULL;

            CREATE TABLE IF NOT EXISTS survey_answers (
                id bigserial PRIMARY KEY,
                response_id uuid NOT NULL REFERENCES survey_responses(id) ON DELETE CASCADE,
                question_id uuid NOT NULL,
                answer text NOT NULL DEFAULT '',
                option_indices jsonb NOT NULL DEFAULT '[]'
            );
            CREATE INDEX IF NOT EXISTS ix_survey_answers_response ON survey_answers(response_id);
            CREATE INDEX IF NOT EXISTS ix_survey_answers_survey ON survey_answers(question_id);
            """).ExecuteNonQueryAsync(ct);
    }

    public static void MapSurveys(this WebApplication app)
    {
        var g = app.MapGroup("/api/surveys").RequireAuthorization();

        // ---------- Tạo / quản trị (Admin) ----------
        g.MapPost("/", async (CreateSurveyReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Title)) return Results.BadRequest(new { message = "Thiếu tiêu đề khảo sát." });
            if (req.Questions is null || req.Questions.Length == 0)
                return Results.BadRequest(new { message = "Khảo sát phải có ít nhất một câu hỏi." });

            var id = Guid.NewGuid();
            await using var conn = await db.OpenAsync();
            await conn.Cmd("""
                INSERT INTO surveys (id, title, description, is_anonymous, allow_multiple, is_active, created_by, closes_at)
                VALUES (@id, @t, @d, @anon, @multi, TRUE, @by, @closes)
                """)
                .With("@id", id).With("@t", req.Title.Trim()).With("@d", req.Description ?? "")
                .With("@anon", req.IsAnonymous ?? true).With("@multi", req.AllowMultiple ?? false)
                .With("@by", u.Username()).With("@closes", (object?)req.ClosesAt ?? DBNull.Value)
                .ExecuteNonQueryAsync();

            var order = 0;
            foreach (var q in req.Questions)
            {
                await conn.Cmd("""
                    INSERT INTO survey_questions (id, survey_id, question, qtype, options, order_no, required)
                    VALUES (@id, @sid, @q, @type, @opts::jsonb, @ord, @req)
                    """)
                    .With("@id", Guid.NewGuid()).With("@sid", id).With("@q", q.Question ?? "")
                    .With("@type", NormalizeType(q.Qtype)).With("@opts", JsonSerializer.Serialize(q.Options ?? Array.Empty<string>()))
                    .With("@ord", order++).With("@req", q.Required ?? true)
                    .ExecuteNonQueryAsync();
            }

            await db.RecordAudit(u.Username(), "Tạo khảo sát", "Survey", id.ToString(), req.Title.Trim());
            return Results.Ok(new { id });
        });

        // Danh sách toàn bộ (Admin) kèm số phản hồi.
        g.MapGet("/", async (ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var list = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT s.id, s.title, s.description, s.is_anonymous, s.allow_multiple, s.is_active, s.created_at, s.closes_at,
                       (SELECT COUNT(*) FROM survey_responses x WHERE x.survey_id=s.id) AS responses
                FROM surveys s ORDER BY s.created_at DESC
                """).ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new
                {
                    id = r.Guid("id"), title = r.Str("title"), description = r.Str("description"),
                    isAnonymous = r.Bool("is_anonymous"), allowMultiple = r.Bool("allow_multiple"),
                    isActive = r.Bool("is_active"), createdAt = r.Dt("created_at"), closesAt = r.DtNull("closes_at"),
                    responses = r.Int("responses"),
                });
            return Results.Ok(list);
        });

        // Khảo sát đang mở cho người dùng hiện tại (kèm cờ đã trả lời).
        g.MapGet("/active", async (ClaimsPrincipal u, Database db, IConfiguration cfg) =>
        {
            await using var conn = await db.OpenAsync();
            var list = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT id, title, description, is_anonymous, allow_multiple, closes_at
                FROM surveys
                WHERE is_active = TRUE AND (closes_at IS NULL OR closes_at > CURRENT_TIMESTAMP)
                ORDER BY created_at DESC
                """).ExecuteReaderAsync();
            var rows = new List<(Guid Id, string Title, string Desc, bool Anon, bool Multi, DateTime? Closes)>();
            while (await r.ReadAsync())
                rows.Add((r.Guid("id"), r.Str("title"), r.Str("description"), r.Bool("is_anonymous"), r.Bool("allow_multiple"), r.DtNull("closes_at")));
            await r.CloseAsync();

            foreach (var s in rows)
            {
                var responded = false;
                if (!s.Multi)
                {
                    var hash = RespondentHash(cfg, s.Id, u.Username());
                    responded = Convert.ToInt64(await conn.Cmd(
                        "SELECT COUNT(*) FROM survey_responses WHERE survey_id=@s AND respondent_hash=@h")
                        .With("@s", s.Id).With("@h", hash).ExecuteScalarAsync()) > 0;
                }
                list.Add(new { id = s.Id, title = s.Title, description = s.Desc, isAnonymous = s.Anon, allowMultiple = s.Multi, closesAt = s.Closes, responded });
            }
            return Results.Ok(list);
        });

        // Chi tiết + câu hỏi (để điền).
        g.MapGet("/{id:guid}", async (Guid id, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            object? head = null;
            await using (var r = await conn.Cmd(
                "SELECT id, title, description, is_anonymous, allow_multiple, is_active, closes_at FROM surveys WHERE id=@id")
                .With("@id", id).ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) return Results.NotFound();
                head = new { id = r.Guid("id"), title = r.Str("title"), description = r.Str("description"),
                    isAnonymous = r.Bool("is_anonymous"), allowMultiple = r.Bool("allow_multiple"),
                    isActive = r.Bool("is_active"), closesAt = r.DtNull("closes_at") };
            }
            var questions = await ReadQuestions(conn, id);
            return Results.Ok(new { survey = head, questions });
        });

        // Gửi phản hồi.
        g.MapPost("/{id:guid}/respond", async (Guid id, RespondReq req, ClaimsPrincipal u, Database db, IConfiguration cfg) =>
        {
            await using var conn = await db.OpenAsync();
            bool anon, multi, active; DateTime? closes;
            await using (var r = await conn.Cmd(
                "SELECT is_anonymous, allow_multiple, is_active, closes_at FROM surveys WHERE id=@id")
                .With("@id", id).ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) return Results.NotFound();
                anon = r.Bool("is_anonymous"); multi = r.Bool("allow_multiple"); active = r.Bool("is_active"); closes = r.DtNull("closes_at");
            }
            if (!active || (closes is not null && closes <= DateTime.UtcNow))
                return Results.BadRequest(new { message = "Khảo sát đã đóng." });
            if (req.Answers is null || req.Answers.Length == 0)
                return Results.BadRequest(new { message = "Chưa có câu trả lời." });

            var responseId = Guid.NewGuid();
            // Khảo sát một-lần: gắn HMAC(username) để chống trùng mà KHÔNG lưu danh tính.
            var hash = multi ? null : RespondentHash(cfg, id, u.Username());
            try
            {
                await conn.Cmd("INSERT INTO survey_responses (id, survey_id, respondent_hash) VALUES (@id, @s, @h)")
                    .With("@id", responseId).With("@s", id).With("@h", (object?)hash ?? DBNull.Value)
                    .ExecuteNonQueryAsync();
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return Results.Conflict(new { message = "Bạn đã gửi phản hồi cho khảo sát này rồi." });
            }

            foreach (var a in req.Answers)
                await conn.Cmd("""
                    INSERT INTO survey_answers (response_id, question_id, answer, option_indices)
                    VALUES (@rid, @qid, @ans, @opts::jsonb)
                    """)
                    .With("@rid", responseId).With("@qid", a.QuestionId).With("@ans", a.Answer ?? "")
                    .With("@opts", JsonSerializer.Serialize(a.OptionIndices ?? Array.Empty<int>()))
                    .ExecuteNonQueryAsync();

            return Results.Ok(new { ok = true });
        });

        // Kết quả tổng hợp (Admin) — chỉ số đếm / danh sách, KHÔNG kèm danh tính.
        g.MapGet("/{id:guid}/results", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();

            var total = Convert.ToInt32(await conn.Cmd("SELECT COUNT(*) FROM survey_responses WHERE survey_id=@id")
                .With("@id", id).ExecuteScalarAsync());
            var questions = await ReadQuestions(conn, id);

            var results = new List<object>();
            foreach (var q in questions)
            {
                var counts = new int[q.Options.Length];
                var texts = new List<string>();
                double ratingSum = 0; int ratingN = 0;
                await using var r = await conn.Cmd("""
                    SELECT a.answer, a.option_indices::text AS opts
                    FROM survey_answers a JOIN survey_responses s ON s.id=a.response_id
                    WHERE s.survey_id=@sid AND a.question_id=@qid
                    """).With("@sid", id).With("@qid", q.Id).ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    if (q.Qtype is "single" or "multi")
                        foreach (var idx in ParseInts(r.Str("opts")))
                            if (idx >= 0 && idx < counts.Length) counts[idx]++;
                    else if (q.Qtype == "rating")
                    {
                        if (double.TryParse(r.Str("answer"), out var v)) { ratingSum += v; ratingN++; }
                    }
                    else
                    {
                        var t = r.Str("answer");
                        if (!string.IsNullOrWhiteSpace(t)) texts.Add(t);
                    }
                }
                results.Add(new
                {
                    questionId = q.Id, question = q.Question, qtype = q.Qtype, options = q.Options,
                    optionCounts = counts, texts, ratingAvg = ratingN > 0 ? Math.Round(ratingSum / ratingN, 2) : (double?)null,
                });
            }
            return Results.Ok(new { total, results });
        });

        // Đóng khảo sát.
        g.MapPost("/{id:guid}/close", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("UPDATE surveys SET is_active=FALSE WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await db.RecordAudit(u.Username(), "Đóng khảo sát", "Survey", id.ToString(), "");
            return Results.NoContent();
        });

        // Xóa khảo sát (kèm câu hỏi + phản hồi qua ON DELETE CASCADE).
        g.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("DELETE FROM surveys WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await db.RecordAudit(u.Username(), "Xóa khảo sát", "Survey", id.ToString(), "");
            return Results.NoContent();
        });
    }

    private static async Task<List<QuestionDto>> ReadQuestions(NpgsqlConnection conn, Guid surveyId)
    {
        var list = new List<QuestionDto>();
        await using var r = await conn.Cmd(
            "SELECT id, question, qtype, options::text AS options, required FROM survey_questions WHERE survey_id=@id ORDER BY order_no")
            .With("@id", surveyId).ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new QuestionDto(r.Guid("id"), r.Str("question"), r.Str("qtype"), ParseStrings(r.Str("options")), r.Bool("required")));
        return list;
    }

    /// <summary>HMAC một chiều của username theo từng khảo sát — chống trùng mà không lưu/không truy ngược danh tính.</summary>
    private static string RespondentHash(IConfiguration cfg, Guid surveyId, string username)
    {
        var key = cfg["Jwt:Key"] ?? "survey-dedup-fallback-key";
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var bytes = h.ComputeHash(Encoding.UTF8.GetBytes($"{surveyId}|{username}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizeType(string? t) => (t ?? "").Trim().ToLowerInvariant() switch
    {
        "multi" => "multi", "text" => "text", "rating" => "rating", _ => "single",
    };

    private static string[] ParseStrings(string? json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json ?? "[]") ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static int[] ParseInts(string? json)
    {
        try { return JsonSerializer.Deserialize<int[]>(json ?? "[]") ?? Array.Empty<int>(); }
        catch { return Array.Empty<int>(); }
    }

    public record CreateSurveyReq(string? Title, string? Description, bool? IsAnonymous, bool? AllowMultiple,
        DateTime? ClosesAt, QuestionReq[]? Questions);
    public record QuestionReq(string? Question, string? Qtype, string[]? Options, bool? Required);
    public record RespondReq(AnswerReq[]? Answers);
    public record AnswerReq(Guid QuestionId, string? Answer, int[]? OptionIndices);
    public record QuestionDto(Guid Id, string Question, string Qtype, string[] Options, bool Required);
}
