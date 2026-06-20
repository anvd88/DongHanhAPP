using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Services;
using Microsoft.Data.SqlClient;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Chấm công bằng khuôn mặt. Luồng nghiệp vụ hoàn chỉnh; phần học máy nằm sau
/// <see cref="IFaceEngine"/> (xem Services/FaceEngine.cs) để dễ thay engine thật.
///
/// Hai giai đoạn:
///   • Đăng ký (admin): lưu vài "mẫu" vector đặc trưng cho mỗi nhân viên.
///   • Chấm công: chụp ảnh → liveness → trích vector → so khớp toàn bộ mẫu → ghi Vào/Ra.
/// </summary>
public static class ChamCongEndpoints
{
    // Số mẫu khuôn mặt tối đa giữ cho mỗi nhân viên (mẫu admin đăng ký + mẫu tự học).
    private const int MaxFaceSamples = 5;
    // Chỉ TỰ HỌC khi độ khớp cao hơn HẲN ngưỡng nhận diện (0.363) để chắc chắn đúng người
    // → tránh "nhiễm" hồ sơ bằng một lần khớp sai. Tăng/giảm nếu cần chặt/lỏng hơn.
    private const double AdaptiveLearnMinSimilarity = 0.5;
    // Nhãn ở cột created_by để phân biệt mẫu hệ thống TỰ HỌC với mẫu admin đăng ký.
    private const string AutoLearnTag = "(tự học)";

    public static async Task EnsureTables(Database db)
    {
        await using var conn = await db.OpenAsync();

        // Mẫu khuôn mặt đã đăng ký (mỗi nhân viên có thể nhiều dòng = nhiều góc chụp).
        await conn.Cmd(@"
            IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='cham_cong_face' AND xtype='U')
            CREATE TABLE cham_cong_face (
                id BIGINT IDENTITY(1,1) PRIMARY KEY,
                username NVARCHAR(100) NOT NULL,
                full_name NVARCHAR(200) NOT NULL DEFAULT '',
                embedding VARBINARY(MAX) NOT NULL,
                anh NVARCHAR(MAX) NULL,              -- ảnh đăng ký (base64), tùy chọn
                created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                created_by NVARCHAR(100) NOT NULL DEFAULT '');").ExecuteNonQueryAsync();

        // Nhật ký chấm công.
        await conn.Cmd(@"
            IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='cham_cong_log' AND xtype='U')
            CREATE TABLE cham_cong_log (
                id BIGINT IDENTITY(1,1) PRIMARY KEY,
                username NVARCHAR(100) NOT NULL,
                full_name NVARCHAR(200) NOT NULL DEFAULT '',
                loai NVARCHAR(10) NOT NULL,          -- N'Vào' / N'Ra'
                similarity FLOAT NOT NULL DEFAULT 0,
                anh NVARCHAR(MAX) NULL,              -- ảnh lúc chấm (base64), tùy chọn
                occurred_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                ghi_chu NVARCHAR(500) NOT NULL DEFAULT '');").ExecuteNonQueryAsync();
    }

    public static void MapChamCong(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/chamcong").RequireAuthorization();

        // Cho frontend biết đang chạy engine giả lập hay thật + ngưỡng khớp.
        // Ẩn danh: màn hình kiosk (ngoài trang đăng nhập) cần đọc trạng thái này.
        g.MapGet("/trangthai", (IFaceEngine engine) =>
            Results.Ok(new FaceEngineStatusDto(engine.Name, engine.IsReal, engine.MatchThreshold)))
            .AllowAnonymous();

        // Danh sách nhân viên đã đăng ký khuôn mặt (gộp theo username).
        g.MapGet("/dadangky", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var list = new List<FaceNguoiDungDto>();
            await using var r = await conn.Cmd(
                @"SELECT username, MAX(full_name) full_name, COUNT(*) so_mau, MAX(created_at) created_at
                  FROM cham_cong_face GROUP BY username ORDER BY MAX(created_at) DESC").ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new FaceNguoiDungDto(r.Str("username"), r.Str("full_name"),
                    r.Int("so_mau"), r.DtNull("created_at")));
            return Results.Ok(list);
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // Nhật ký từng mẫu khuôn mặt đã đăng ký. Chỉ Admin được xem/quản lý dữ liệu sinh trắc.
        g.MapGet("/dangky/log", async (Database db, string? search) =>
        {
            await using var conn = await db.OpenAsync();
            var where = "WHERE 1=1";
            if (!string.IsNullOrWhiteSpace(search)) where += " AND (username LIKE @s OR full_name LIKE @s OR created_by LIKE @s)";

            var cmd = conn.Cmd(
                $@"SELECT TOP 500 id, username, full_name, created_at, created_by
                   FROM cham_cong_face {where}
                   ORDER BY created_at DESC, id DESC");
            if (!string.IsNullOrWhiteSpace(search)) cmd.With("@s", $"%{search}%");

            var list = new List<FaceRegistrationLogDto>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new FaceRegistrationLogDto(r.Long("id"), r.Str("username"), r.Str("full_name"),
                    r.Dt("created_at"), r.Str("created_by")));
            return Results.Ok(list);
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // Đăng ký 1 mẫu khuôn mặt cho nhân viên (Admin). Gọi nhiều lần để thêm nhiều góc chụp.
        g.MapPost("/dangky", async (DangKyKhuonMatRequest req, ClaimsPrincipal u, Database db, IFaceEngine engine) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username))
                return Results.BadRequest(new { message = "Thiếu tên đăng nhập nhân viên." });

            if (!TryDecodeImage(req.ImageBase64, out var bytes))
                return Results.BadRequest(new { message = "Ảnh không hợp lệ." });

            var emb = engine.ExtractEmbedding(bytes);
            if (emb is null)
                return Results.BadRequest(new { message = "Không phát hiện được khuôn mặt trong ảnh. Hãy chụp lại rõ hơn." });

            await using var conn = await db.OpenAsync();
            await conn.Cmd(
                @"INSERT INTO cham_cong_face (username, full_name, embedding, anh, created_at, created_by)
                  VALUES (@u, @fn, @emb, @anh, SYSUTCDATETIME(), @by)")
                .With("@u", req.Username.Trim())
                .With("@fn", req.FullName ?? "")
                .With("@emb", EmbeddingCodec.ToBytes(emb))
                .With("@anh", (object?)req.ImageBase64 ?? DBNull.Value)
                .With("@by", u.Username())
                .ExecuteNonQueryAsync();

            await db.RecordAudit(u.Username(), "Đăng ký khuôn mặt", "ChamCong", req.Username, "Thêm mẫu khuôn mặt (web).");
            return Results.Ok(new { message = "Đã lưu mẫu khuôn mặt." });
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // Ước lượng hướng mặt (cho wizard quét tự động kiểm tra tư thế trước khi lưu mẫu). Admin.
        g.MapPost("/huongmat", (NhanDienRequest req, IFaceEngine engine) =>
        {
            if (!TryDecodeImage(req.ImageBase64, out var bytes))
                return Results.BadRequest(new { message = "Ảnh không hợp lệ." });
            var pose = engine.EstimatePose(bytes);
            return Results.Ok(pose is { } p ? new FacePoseDto(true, p.Yaw, p.Pitch) : new FacePoseDto(false, 0, 0));
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // Xóa toàn bộ mẫu khuôn mặt của 1 nhân viên (Admin).
        g.MapDelete("/dangky/{username}", async (string username, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("DELETE FROM cham_cong_face WHERE username=@u")
                .With("@u", username).ExecuteNonQueryAsync();
            if (n > 0) await db.RecordAudit(u.Username(), "Xóa khuôn mặt", "ChamCong", username, "Xóa mẫu khuôn mặt (web).");
            return n > 0 ? Results.NoContent() : Results.NotFound();
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // Xóa 1 mẫu khuôn mặt cụ thể trong nhật ký đăng ký (Admin).
        g.MapDelete("/dangky/mau/{id:long}", async (long id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var owner = "";
            await using (var r = await conn.Cmd("SELECT TOP 1 username FROM cham_cong_face WHERE id=@id")
                .With("@id", id).ExecuteReaderAsync())
            {
                if (await r.ReadAsync()) owner = r.Str("username");
            }

            var n = await conn.Cmd("DELETE FROM cham_cong_face WHERE id=@id")
                .With("@id", id).ExecuteNonQueryAsync();
            if (n > 0) await db.RecordAudit(u.Username(), "Xóa mẫu khuôn mặt", "ChamCong", owner, $"Xóa mẫu khuôn mặt id={id} (web).");
            return n > 0 ? Results.NoContent() : Results.NotFound();
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // Chấm công: chụp ảnh -> liveness -> trích vector -> so khớp -> ghi Vào/Ra.
        // Ẩn danh: cho phép chấm công ở kiosk màn hình đăng nhập (không cần tài khoản).
        g.MapPost("/nhandien", async (NhanDienRequest req, Database db, IFaceEngine engine) =>
        {
            if (!TryDecodeImage(req.ImageBase64, out var bytes))
                return Results.BadRequest(new { message = "Ảnh không hợp lệ." });

            if (!engine.CheckLiveness(bytes))
                return Results.Ok(new NhanDienResult(false, null, null, 0, null, null,
                    "Nghi ngờ giả mạo (không phải người thật). Vui lòng thử lại."));

            var probe = engine.ExtractEmbedding(bytes);
            if (probe is null)
                return Results.Ok(new NhanDienResult(false, null, null, 0, null, null,
                    "Không phát hiện được khuôn mặt. Hãy nhìn thẳng vào camera."));

            await using var conn = await db.OpenAsync();

            // So khớp tuyến tính với toàn bộ mẫu đã đăng ký.
            // 💡 Khi số nhân viên lớn: chuyển sang DB vector (pgvector) hoặc đánh chỉ mục ANN.
            string? bestUser = null, bestName = null;
            double best = 0;
            await using (var r = await conn.Cmd(
                "SELECT username, full_name, embedding FROM cham_cong_face").ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    var emb = EmbeddingCodec.FromBytes((byte[])r["embedding"]);
                    var sim = engine.Compare(probe, emb);
                    if (sim > best) { best = sim; bestUser = r.Str("username"); bestName = r.Str("full_name"); }
                }
            }

            if (bestUser is null || best < engine.MatchThreshold)
                return Results.Ok(new NhanDienResult(false, null, null, best, null, null,
                    "Không nhận diện được. Khuôn mặt chưa được đăng ký hoặc ảnh chưa rõ."));

            // Lần chấm gần nhất TRONG NGÀY của nhân viên (loại + thời điểm).
            string? lastLoai = null;
            DateTime? lastAt = null;
            await using (var lr = await conn.Cmd(
                @"SELECT TOP 1 loai, occurred_at FROM cham_cong_log
                  WHERE username=@u AND CONVERT(date, occurred_at) = CONVERT(date, SYSUTCDATETIME())
                  ORDER BY occurred_at DESC").With("@u", bestUser).ExecuteReaderAsync())
            {
                if (await lr.ReadAsync()) { lastLoai = lr.Str("loai"); lastAt = lr.Dt("occurred_at"); }
            }

            // Chống chấm trùng: tự động chụp bắn liên tục, nên nếu vừa chấm trong vòng
            // COOLDOWN giây thì KHÔNG ghi thêm (tránh Vào rồi Ra ngay lập tức).
            const int cooldownSeconds = 30;
            if (lastAt is not null && (DateTime.UtcNow - lastAt.Value).TotalSeconds < cooldownSeconds)
                return Results.Ok(new NhanDienResult(true, bestUser, bestName, best, lastLoai, lastAt,
                    $"{bestName} đã chấm công {lastLoai} rồi."));

            // Quyết định Vào/Ra theo lần chấm gần nhất trong ngày.
            var loai = lastLoai == "Vào" ? "Ra" : "Vào";

            // KHÔNG lưu ảnh vào log: cột anh không hiển thị ở bất kỳ đâu nên chỉ làm phình DB.
            // (Cột vẫn còn trong bảng để tương thích cũ; đơn giản là không ghi nữa → mặc định NULL.)
            await conn.Cmd(
                @"INSERT INTO cham_cong_log (username, full_name, loai, similarity, occurred_at)
                  VALUES (@u, @fn, @loai, @sim, SYSUTCDATETIME())")
                .With("@u", bestUser).With("@fn", bestName ?? "")
                .With("@loai", loai).With("@sim", best)
                .ExecuteNonQueryAsync();

            await db.RecordAudit(bestUser, $"Chấm công {loai}", "ChamCong", bestUser, $"Độ khớp {best:0.000} (web).");

            // Tự học: khớp chắc + đã qua liveness → lưu thêm mẫu (tối đa 5/người, không đụng mẫu admin).
            // Là phụ trợ: lỗi ở đây tuyệt đối không được làm hỏng việc chấm công.
            try { await TryAdaptiveLearnAsync(conn, bestUser, bestName ?? "", probe, best); }
            catch { /* bỏ qua, chấm công vẫn thành công */ }

            return Results.Ok(new NhanDienResult(true, bestUser, bestName, best, loai, DateTime.UtcNow,
                $"{bestName} đã chấm công {loai}."));
        }).AllowAnonymous();

        // Nhật ký chấm công (lọc theo ngày yyyy-MM-dd và/hoặc từ khóa).
        g.MapGet("/log", async (Database db, string? date, string? search) =>
        {
            await using var conn = await db.OpenAsync();
            var where = "WHERE 1=1";
            if (!string.IsNullOrWhiteSpace(date)) where += " AND CONVERT(date, occurred_at) = @d";
            if (!string.IsNullOrWhiteSpace(search)) where += " AND (username LIKE @s OR full_name LIKE @s)";

            var cmd = conn.Cmd(
                $@"SELECT TOP 500 id, username, full_name, loai, similarity, occurred_at, ghi_chu
                   FROM cham_cong_log {where} ORDER BY occurred_at DESC");
            if (!string.IsNullOrWhiteSpace(date)) cmd.With("@d", date);
            if (!string.IsNullOrWhiteSpace(search)) cmd.With("@s", $"%{search}%");

            var list = new List<ChamCongLogDto>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new ChamCongLogDto(r.Long("id"), r.Str("username"), r.Str("full_name"),
                    r.Str("loai"), r.IsDBNull(r.GetOrdinal("similarity")) ? 0 : r.GetDouble(r.GetOrdinal("similarity")),
                    r.Dt("occurred_at"), r.Str("ghi_chu")));
            return Results.Ok(list);
        }).RequireAuthorization(p => p.RequireRole("Admin"));
    }

    /// <summary>
    /// Tự học (adaptive enrollment): sau một lần chấm công ĐÃ qua liveness và khớp RẤT chắc,
    /// lưu thêm embedding làm mẫu để nhận diện bền hơn khi diện mạo/ánh sáng đổi dần.
    /// An toàn: chỉ học khi similarity ≥ ngưỡng tự học; tối đa <see cref="MaxFaceSamples"/> mẫu/người;
    /// mỗi người chỉ học 1 mẫu/ngày (tránh phình DB); KHÔNG bao giờ xóa mẫu admin đăng ký —
    /// khi đã đủ 5 mẫu thì chỉ thay mẫu TỰ HỌC cũ nhất (cuốn chiếu để bám diện mạo hiện tại).
    /// </summary>
    private static async Task TryAdaptiveLearnAsync(
        SqlConnection conn, string username, string fullName, float[] embedding, double similarity)
    {
        if (similarity < AdaptiveLearnMinSimilarity) return;

        // Mỗi người chỉ học tối đa 1 mẫu/ngày.
        var learnedToday = await conn.Cmd(
            @"SELECT TOP 1 1 FROM cham_cong_face
              WHERE username=@u AND created_by=@auto
                AND CONVERT(date, created_at) = CONVERT(date, SYSUTCDATETIME())")
            .With("@u", username).With("@auto", AutoLearnTag).ExecuteScalarAsync();
        if (learnedToday is not null and not DBNull) return;

        var total = Convert.ToInt32(await conn.Cmd(
            "SELECT COUNT(*) FROM cham_cong_face WHERE username=@u")
            .With("@u", username).ExecuteScalarAsync());

        if (total >= MaxFaceSamples)
        {
            // Hết chỗ → thay mẫu TỰ HỌC cũ nhất. Nếu cả 5 đều là mẫu admin thì thôi (không học).
            var oldestAuto = await conn.Cmd(
                @"SELECT TOP 1 id FROM cham_cong_face
                  WHERE username=@u AND created_by=@auto ORDER BY created_at ASC, id ASC")
                .With("@u", username).With("@auto", AutoLearnTag).ExecuteScalarAsync();
            if (oldestAuto is null or DBNull) return;
            await conn.Cmd("DELETE FROM cham_cong_face WHERE id=@id")
                .With("@id", Convert.ToInt64(oldestAuto)).ExecuteNonQueryAsync();
        }

        await conn.Cmd(
            @"INSERT INTO cham_cong_face (username, full_name, embedding, anh, created_at, created_by)
              VALUES (@u, @fn, @emb, NULL, SYSUTCDATETIME(), @auto)")
            .With("@u", username).With("@fn", fullName)
            .With("@emb", EmbeddingCodec.ToBytes(embedding))
            .With("@auto", AutoLearnTag)
            .ExecuteNonQueryAsync();
    }

    /// <summary>Giải mã ảnh base64 (chấp nhận cả tiền tố data URL "data:image/...;base64,").</summary>
    private static bool TryDecodeImage(string? b64, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(b64)) return false;
        var comma = b64.IndexOf(',');
        if (b64.StartsWith("data:") && comma >= 0) b64 = b64[(comma + 1)..];
        try { bytes = Convert.FromBase64String(b64); return bytes.Length > 0; }
        catch { return false; }
    }
}
