using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

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
    // Chỉ TỰ HỌC khi độ khớp cao hơn hẳn ngưỡng nhận diện để chắc chắn đúng người
    // → tránh "nhiễm" hồ sơ bằng một lần khớp sai. Tăng/giảm nếu cần chặt/lỏng hơn.
    private const double AdaptiveLearnMinSimilarity = 0.65;
    // Nhãn ở cột created_by để phân biệt mẫu hệ thống TỰ HỌC với mẫu admin đăng ký.
    private const string AutoLearnTag = "(tự học)";
    private const string AutoAttendanceSettingKey = "KioskCamera.AutoAttendanceEnabled";

    // Cổng tư thế cho chấm công loạt ảnh: lệch quá ngưỡng ⇒ báo trực tiếp, KHÔNG ghi nhật ký.
    // Khớp ngưỡng phía kiosk cũ (yaw chính diện, pitch trong khoảng nhìn thẳng).
    private const double PostureYawMax = 0.16;
    private const double PosturePitchMin = 0.25;
    private const double PosturePitchMax = 0.82;
    // Điểm chất lượng tối thiểu của khung tốt nhất; thấp hơn ⇒ yêu cầu chụp lại (mờ/tối/loá).
    private const double MinFrameQuality = 0.28;

    public static async Task EnsureTables(Database db)
    {
        await using var conn = await db.OpenAsync();

        // Mẫu khuôn mặt đã đăng ký (mỗi nhân viên có thể nhiều dòng = nhiều góc chụp).
        await conn.Cmd("""
            CREATE TABLE IF NOT EXISTS cham_cong_face (
                id bigserial PRIMARY KEY,
                username varchar(100) NOT NULL,
                full_name varchar(200) NOT NULL DEFAULT '',
                embedding bytea NOT NULL,
                anh text NULL,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                created_by varchar(100) NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS cham_cong_log (
                id bigserial PRIMARY KEY,
                username varchar(100) NOT NULL,
                full_name varchar(200) NOT NULL DEFAULT '',
                loai varchar(10) NOT NULL,
                similarity double precision NOT NULL DEFAULT 0,
                anh text NULL,
                occurred_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                ghi_chu varchar(500) NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS web_system_settings (
                setting_key varchar(120) NOT NULL PRIMARY KEY,
                setting_value text NOT NULL DEFAULT '',
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_by varchar(100) NOT NULL DEFAULT ''
            );

            CREATE INDEX IF NOT EXISTS ix_cham_cong_face_username ON cham_cong_face (username, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_cham_cong_log_username_time ON cham_cong_log (username, occurred_at DESC);
            """).ExecuteNonQueryAsync();

        // AdaFace R50 emits 512-float embeddings. SFace embeddings from older versions are incompatible,
        // so drop stale templates once during startup; newly registered AdaFace templates are preserved.
        await conn.Cmd("DELETE FROM cham_cong_face WHERE octet_length(embedding) <> 2048")
            .ExecuteNonQueryAsync();

        // Quyền riêng tư: hệ thống KHÔNG lưu ảnh gốc khuôn mặt nữa (chỉ giữ vector đặc trưng).
        // Dọn mọi ảnh đăng ký cũ còn sót lại. Tự chữa: sau lần đầu sẽ là no-op (0 dòng).
        await conn.Cmd("UPDATE cham_cong_face SET anh = NULL WHERE anh IS NOT NULL")
            .ExecuteNonQueryAsync();
    }

    private static async Task SaveSystemSetting(Database db, string key, string value, string updatedBy)
    {
        try
        {
            await using var conn = await db.OpenAsync();
            await conn.Cmd(@"
                CREATE TABLE IF NOT EXISTS web_system_settings (
                    setting_key varchar(120) NOT NULL PRIMARY KEY,
                    setting_value text NOT NULL DEFAULT '',
                    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_by varchar(100) NOT NULL DEFAULT ''
                );

                INSERT INTO web_system_settings (setting_key, setting_value, updated_at, updated_by)
                VALUES (@key, @value, CURRENT_TIMESTAMP, @updatedBy)
                ON CONFLICT (setting_key) DO UPDATE SET
                    setting_value = EXCLUDED.setting_value,
                    updated_at = EXCLUDED.updated_at,
                    updated_by = EXCLUDED.updated_by;")
                .With("@key", key)
                .With("@value", value)
                .With("@updatedBy", updatedBy)
                .ExecuteNonQueryAsync();
        }
        catch
        {
            // Best effort: the runtime switch still applies even if persistence is temporarily unavailable.
        }
    }

    public static void MapChamCong(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/chamcong").RequireAuthorization();

        // Cho frontend/kiosk biết tên engine + ngưỡng khớp.
        // Ẩn danh: màn hình kiosk (ngoài trang đăng nhập) cần đọc trạng thái này.
        g.MapGet("/trangthai", (IFaceEngine engine) =>
            Results.Ok(new FaceEngineStatusDto(engine.Name, engine.MatchThreshold)))
            .AllowAnonymous();

        // ── Camera IP (RTSP kiosk) TẠM ẨN ──────────────────────────────────────────────
        // Toàn bộ nhóm endpoint /rtsp/* chỉ được đăng ký khi KioskCamera:Enabled = true.
        // Đặt KioskCamera:Enabled = false (appsettings) để ẩn hẳn API camera IP (trả 404).
        // Bật lại tính năng: chỉ cần đổi cờ này về true rồi khởi động lại backend.
        var cameraEnabled = app.ServiceProvider.GetService<IConfiguration>()?.GetValue("KioskCamera:Enabled", false) ?? false;
        if (cameraEnabled)
        {
        g.MapGet("/rtsp/status", (RtspAttendanceWorker worker) =>
            Results.Ok(worker.GetStatus()))
            .RequireAuthorization(p => p.RequireRole("Admin"));

        // Ảnh khung hình mới nhất của camera kiosk (snapshot do FFmpeg ghi liên tục ra latest.jpg).
        // Frontend poll ảnh này để hiển thị "khung chấm công" camera IP cho nhân viên.
        // Ẩn danh: màn hình kiosk chấm công (ngoài trang đăng nhập) cần xem được luồng camera.
        g.MapGet("/rtsp/snapshot", (IConfiguration cfg, IHostEnvironment env) =>
        {
            var configured = cfg["KioskCamera:LatestFramePath"];
            if (string.IsNullOrWhiteSpace(configured))
                return Results.NotFound(new { message = "Chưa cấu hình đường dẫn ảnh camera." });

            var path = Path.IsPathRooted(configured)
                ? Path.GetFullPath(configured)
                : Path.GetFullPath(Path.Combine(env.ContentRootPath, configured));

            if (!File.Exists(path))
                return Results.NotFound(new { message = "Chưa có ảnh camera. Đang chờ FFmpeg ghi khung hình đầu tiên." });

            try
            {
                // FileShare.ReadWrite: FFmpeg đang ghi liên tục nên phải cho phép đọc song song.
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                var bytes = ms.ToArray();
                if (bytes.Length == 0)
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

                // Không cache: mỗi lần poll phải lấy khung mới nhất.
                return Results.File(bytes, "image/jpeg", lastModified: File.GetLastWriteTimeUtc(path));
            }
            catch (IOException)
            {
                // Trùng nhịp ghi của FFmpeg → để client thử lại lần poll sau.
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }).AllowAnonymous();

        g.MapPost("/rtsp/reconnect", async (
            RtspAttendanceWorker worker,
            CameraSnapshotBridgeService bridge,
            CancellationToken ct) =>
        {
            worker.RequestReconnect("Dang ket noi lai camera...");
            await bridge.RestartAsync(ct);
            return Results.Ok(new { message = "Da gui lenh ket noi lai camera.", status = worker.GetStatus() });
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        g.MapPost("/rtsp/test-scan", ([FromBody] RtspTestScanRequest req, RtspAttendanceWorker worker) =>
        {
            var status = worker.SetTestScan(req.Enabled);
            return Results.Ok(new
            {
                message = req.Enabled
                    ? "Da bat che do test scan lien tuc."
                    : "Da tat che do test scan lien tuc.",
                status
            });
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        g.MapPost("/rtsp/auto-attendance", async (
            [FromBody] RtspAutoAttendanceRequest req,
            RtspAttendanceWorker worker,
            ClaimsPrincipal u,
            Database db) =>
        {
            var status = worker.SetAutoAttendance(req.Enabled);
            await SaveSystemSetting(db, AutoAttendanceSettingKey, req.Enabled ? "true" : "false", u.Username());
            await db.RecordAudit(u.Username(),
                req.Enabled ? "Bật chấm công tự động" : "Tắt chấm công tự động",
                "ChamCong",
                "RTSP",
                req.Enabled
                    ? "Admin bật chấm công nhận diện tự động từ camera IP (web)."
                    : "Admin tắt chấm công nhận diện tự động từ camera IP (web).");
            return Results.Ok(new
            {
                message = req.Enabled
                    ? "Da bat cham cong nhan dien tu dong."
                    : "Da tat cham cong nhan dien tu dong.",
                status
            });
        }).RequireAuthorization(p => p.RequireRole("Admin"));
        } // ── hết nhóm /rtsp/* (camera IP tạm ẩn) ──

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
            if (!string.IsNullOrWhiteSpace(search)) where += " AND (username ILIKE @s OR full_name ILIKE @s OR created_by ILIKE @s)";

            var cmd = conn.Cmd(
                $@"SELECT id, username, full_name, created_at, created_by
                   FROM cham_cong_face {where}
                   ORDER BY created_at DESC, id DESC LIMIT 500");
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
            // CHỈ lưu vector đặc trưng, KHÔNG lưu ảnh gốc khuôn mặt (riêng tư + gọn DB). Ảnh chỉ dùng
            // tạm để trích embedding rồi bỏ; nhận diện về sau chỉ cần vector (cột anh để mặc định NULL).
            await conn.Cmd(
                @"INSERT INTO cham_cong_face (username, full_name, embedding, created_at, created_by)
                  VALUES (@u, @fn, @emb, CURRENT_TIMESTAMP, @by)")
                .With("@u", req.Username.Trim())
                .With("@fn", req.FullName ?? "")
                .With("@emb", EmbeddingCodec.ToBytes(emb))
                .With("@by", u.Username())
                .ExecuteNonQueryAsync();

            await db.RecordAudit(u.Username(), "Đăng ký khuôn mặt", "ChamCong", req.Username, "Thêm mẫu khuôn mặt (web).");
            return Results.Ok(new { message = "Đã lưu mẫu khuôn mặt." });
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // Ước lượng hướng mặt — trình đăng ký khuôn mặt (EnrollWizard) dùng để hướng dẫn từng tư thế.
        g.MapPost("/huongmat", (NhanDienRequest req, IFaceEngine engine) =>
        {
            if (!TryDecodeImage(req.ImageBase64, out var bytes))
                return Results.BadRequest(new { message = "Ảnh không hợp lệ." });
            var pose = engine.EstimatePose(bytes);
            return Results.Ok(pose is { } p ? new FacePoseDto(true, p.Yaw, p.Pitch) : new FacePoseDto(false, 0, 0));
        }).AllowAnonymous();

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
            await using (var r = await conn.Cmd("SELECT username FROM cham_cong_face WHERE id=@id LIMIT 1")
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

            var decision = await AttendancePolicy.DecideAsync(conn, bestUser, bestName ?? bestUser);
            if (!decision.ShouldRecord)
                return Results.Ok(new NhanDienResult(true, bestUser, bestName, best, decision.Loai, decision.ExistingAt,
                    decision.Message));

            var loai = decision.Loai;

            // KHÔNG lưu ảnh vào log: cột anh không hiển thị ở bất kỳ đâu nên chỉ làm phình DB.
            // (Cột vẫn còn trong bảng để tương thích cũ; đơn giản là không ghi nữa → mặc định NULL.)
            await conn.Cmd(
                @"INSERT INTO cham_cong_log (username, full_name, loai, similarity, occurred_at)
                  VALUES (@u, @fn, @loai, @sim, CURRENT_TIMESTAMP)")
                .With("@u", bestUser).With("@fn", bestName ?? "")
                .With("@loai", loai).With("@sim", best)
                .ExecuteNonQueryAsync();

            await db.RecordAudit(bestUser, $"Chấm công {loai}", "ChamCong", bestUser, $"Độ khớp {best:0.000} (web).");

            // Tự học: khớp chắc + đã qua liveness → lưu thêm mẫu (tối đa 5/người, không đụng mẫu admin).
            // Là phụ trợ: lỗi ở đây tuyệt đối không được làm hỏng việc chấm công.
            try { await TryAdaptiveLearnAsync(conn, bestUser, bestName ?? "", probe, best); }
            catch { /* bỏ qua, chấm công vẫn thành công */ }

            return Results.Ok(new NhanDienResult(true, bestUser, bestName, best, loai, DateTime.UtcNow,
                decision.Message));
        }).AllowAnonymous();

        // Chấm công bằng LOẠT ẢNH: KHÔNG quét trực tiếp liên tục — client chụp 1 loạt khung,
        // server chọn ẢNH TỐT NHẤT (nét, đủ sáng, mặt to & chính diện), kiểm tra tư thế (báo
        // trực tiếp nếu sai), liveness rồi nhận diện và ghi nhật ký. Ẩn danh để dùng ở kiosk.
        g.MapPost("/cham", async (ChamCongBurstRequest req, Database db, IFaceEngine engine) =>
        {
            if (req?.Images is null || req.Images.Count == 0)
                return Results.BadRequest(new { message = "Thiếu ảnh chấm công." });

            // 1) Lấy mọi khung CÓ MẶT, xếp theo chất lượng giảm dần (nét, đủ sáng, mặt to & chính diện).
            var candidates = new List<(byte[] Bytes, FaceFrameQuality Q)>();
            foreach (var img in req.Images)
            {
                if (!TryDecodeImage(img, out var bytes)) continue;
                if (engine.AssessFrame(bytes) is not { FaceFound: true } q) continue;
                candidates.Add((bytes, q));
            }

            if (candidates.Count == 0)
                return Results.Ok(new ChamCongResult("noface", false, null, null, 0, null, null, 0,
                    "Không thấy khuôn mặt trong ảnh.", "Đưa khuôn mặt vào giữa khung hình rồi chấm lại."));

            candidates.Sort((a, b) => b.Q.Score.CompareTo(a.Q.Score));
            var bestBytes = candidates[0].Bytes;
            var best = candidates[0].Q;

            // 2) Cổng tư thế — báo trực tiếp, KHÔNG ghi nhật ký nếu sai.
            var posture = CheckPosture(best.Pose);
            if (posture is not null)
                return Results.Ok(new ChamCongResult("posture", false, null, null, 0, null, null, best.Score,
                    "Sai tư thế chấm công.", posture));

            // 3) Chất lượng quá thấp (mờ/thiếu sáng/loá) → yêu cầu chụp lại.
            if (best.Score < MinFrameQuality)
                return Results.Ok(new ChamCongResult("lowquality", false, null, null, 0, null, null, best.Score,
                    "Ảnh chưa đủ rõ (thiếu sáng, loá hoặc bị nhòe).",
                    "Tìm nơi đủ sáng, giữ máy ổn định và nhìn thẳng rồi chấm lại."));

            // 4) Chống giả mạo: chấm liveness trên VÀI khung tốt nhất của loạt, qua nếu CÓ khung đạt.
            // Model 1 ảnh tĩnh dao động mạnh ngay với người thật (cùng mặt lúc 0.99 lúc 0.11), nên xét
            // 1 khung dễ từ chối nhầm. Ảnh/màn hình giả thì thấp ở MỌI khung → vẫn bị chặn.
            const int livenessFramesToCheck = 5;
            double bestLive = 0;
            foreach (var c in candidates.Take(livenessFramesToCheck))
            {
                bestLive = Math.Max(bestLive, engine.LivenessProbability(c.Bytes));
                if (bestLive >= engine.LivenessThreshold) break; // đã có khung đạt → dừng sớm
            }
            if (bestLive < engine.LivenessThreshold)
                return Results.Ok(new ChamCongResult("spoof", false, null, null, 0, null, null, best.Score,
                    "Nghi ngờ giả mạo (không phải người thật).", "Hãy nhìn trực tiếp vào camera, không dùng ảnh/màn hình."));

            var probe = engine.ExtractEmbedding(bestBytes);
            if (probe is null)
                return Results.Ok(new ChamCongResult("noface", false, null, null, 0, null, null, best.Score,
                    "Không trích được đặc trưng khuôn mặt.", "Nhìn thẳng vào camera rồi chấm lại."));

            await using var conn = await db.OpenAsync();

            // 5) So khớp toàn bộ mẫu đã đăng ký.
            string? bestUser = null, bestName = null;
            double bestSim = 0;
            await using (var r = await conn.Cmd(
                "SELECT username, full_name, embedding FROM cham_cong_face").ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    var emb = EmbeddingCodec.FromBytes((byte[])r["embedding"]);
                    var sim = engine.Compare(probe, emb);
                    if (sim > bestSim) { bestSim = sim; bestUser = r.Str("username"); bestName = r.Str("full_name"); }
                }
            }

            if (bestUser is null || bestSim < engine.MatchThreshold)
                return Results.Ok(new ChamCongResult("unknown", false, null, null, bestSim, null, null, best.Score,
                    "Không nhận diện được. Khuôn mặt chưa đăng ký hoặc ảnh chưa rõ.", null));

            // 6) Quyết định Vào/Ra + ghi nhật ký.
            var decision = await AttendancePolicy.DecideAsync(conn, bestUser, bestName ?? bestUser);
            if (!decision.ShouldRecord)
                return Results.Ok(new ChamCongResult("ok", true, bestUser, bestName, bestSim, decision.Loai,
                    decision.ExistingAt, best.Score, decision.Message, null));

            var loai = decision.Loai;
            await conn.Cmd(
                @"INSERT INTO cham_cong_log (username, full_name, loai, similarity, occurred_at)
                  VALUES (@u, @fn, @loai, @sim, CURRENT_TIMESTAMP)")
                .With("@u", bestUser).With("@fn", bestName ?? "")
                .With("@loai", loai).With("@sim", bestSim)
                .ExecuteNonQueryAsync();

            await db.RecordAudit(bestUser, $"Chấm công {loai}", "ChamCong", bestUser,
                $"Độ khớp {bestSim:0.000}, chất lượng ảnh {best.Score:0.00} (web).");

            try { await TryAdaptiveLearnAsync(conn, bestUser, bestName ?? "", probe, bestSim); }
            catch { /* tự học là phụ trợ, lỗi không được làm hỏng chấm công */ }

            return Results.Ok(new ChamCongResult("ok", true, bestUser, bestName, bestSim, loai,
                DateTime.UtcNow, best.Score, decision.Message, null));
        }).AllowAnonymous();

        // Nhật ký chấm công (lọc theo ngày yyyy-MM-dd và/hoặc từ khóa).
        g.MapGet("/log", async (Database db, string? date, string? search) =>
        {
            await using var conn = await db.OpenAsync();
            var where = "WHERE 1=1";
            if (!string.IsNullOrWhiteSpace(date)) where += " AND occurred_at::date = @d::date";
            if (!string.IsNullOrWhiteSpace(search)) where += " AND (username ILIKE @s OR full_name ILIKE @s)";

            var cmd = conn.Cmd(
                $@"SELECT id, username, full_name, loai, similarity, occurred_at, ghi_chu
                   FROM cham_cong_log {where} ORDER BY occurred_at DESC LIMIT 500");
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
        NpgsqlConnection conn, string username, string fullName, float[] embedding, double similarity)
    {
        if (similarity < AdaptiveLearnMinSimilarity) return;

        // Mỗi người chỉ học tối đa 1 mẫu/ngày.
        var learnedToday = await conn.Cmd(
            @"SELECT 1 FROM cham_cong_face
              WHERE username=@u AND created_by=@auto
                AND created_at::date = CURRENT_DATE
              LIMIT 1")
            .With("@u", username).With("@auto", AutoLearnTag).ExecuteScalarAsync();
        if (learnedToday is not null and not DBNull) return;

        var total = Convert.ToInt32(await conn.Cmd(
            "SELECT COUNT(*) FROM cham_cong_face WHERE username=@u")
            .With("@u", username).ExecuteScalarAsync());

        if (total >= MaxFaceSamples)
        {
            // Hết chỗ → thay mẫu TỰ HỌC cũ nhất. Nếu cả 5 đều là mẫu admin thì thôi (không học).
            var oldestAuto = await conn.Cmd(
                @"SELECT id FROM cham_cong_face
                  WHERE username=@u AND created_by=@auto ORDER BY created_at ASC, id ASC LIMIT 1")
                .With("@u", username).With("@auto", AutoLearnTag).ExecuteScalarAsync();
            if (oldestAuto is null or DBNull) return;
            await conn.Cmd("DELETE FROM cham_cong_face WHERE id=@id")
                .With("@id", Convert.ToInt64(oldestAuto)).ExecuteNonQueryAsync();
        }

        await conn.Cmd(
            @"INSERT INTO cham_cong_face (username, full_name, embedding, anh, created_at, created_by)
              VALUES (@u, @fn, @emb, NULL, CURRENT_TIMESTAMP, @auto)")
            .With("@u", username).With("@fn", fullName)
            .With("@emb", EmbeddingCodec.ToBytes(embedding))
            .With("@auto", AutoLearnTag)
            .ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Kiểm tra tư thế khuôn mặt. Trả null nếu hợp lệ (nhìn thẳng), ngược lại trả câu hướng dẫn
    /// cụ thể để báo trực tiếp cho người dùng. Pitch nhỏ = đang ngước lên, lớn = đang cúi xuống.
    /// </summary>
    private static string? CheckPosture(FacePose pose)
    {
        if (Math.Abs(pose.Yaw) > PostureYawMax)
            return "Nhìn thẳng vào camera, đừng quay mặt sang bên.";
        if (pose.Pitch < PosturePitchMin)
            return "Hạ mặt xuống một chút và nhìn thẳng vào camera.";
        if (pose.Pitch > PosturePitchMax)
            return "Ngẩng mặt lên một chút và nhìn thẳng vào camera.";
        return null;
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
