using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Realtime;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
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
    // Nhãn created_by cho mẫu do NHÂN VIÊN tự đăng ký trên app (phân biệt với mẫu admin/ tự học).
    private const string SelfEnrollTag = "(tự đăng ký)";

    // Cổng tư thế cho chấm công loạt ảnh: lệch quá ngưỡng ⇒ báo trực tiếp, KHÔNG ghi nhật ký.
    // Khớp ngưỡng phía kiosk cũ (yaw chính diện, pitch trong khoảng nhìn thẳng).
    private const double PostureYawMax = 0.16;
    private const double PosturePitchMin = 0.25;
    private const double PosturePitchMax = 0.82;
    // Điểm chất lượng tối thiểu của khung tốt nhất; thấp hơn ⇒ yêu cầu chụp lại (mờ/tối/loá).
    private const double MinFrameQuality = 0.28;

    // ── Chính sách chấm công NGOẠI TUYẾN (chờ duyệt) — có thể ghi đè bằng khóa cấu hình trong
    //    web_system_settings (attendance.offline.*). Mặc định đủ dùng nếu chưa cấu hình.
    // ĐÃ GỠ HẲN active-flash liveness (chuỗi màu màn hình). Nó đã CHẾT trên thực tế từ lâu mà code phía
    // máy chủ vẫn tự nhận là "luôn bật + chặn thật": đo trên máy thật cho thấy react≈0 kể cả người thật
    // (AWB camera triệt hết cast màu) nên không tách được thật/giả, camera trong APK đã bỏ toàn bộ pha
    // chiếu màu, và không client nào còn gửi challengeId/slotIndices — tức khối kiểm tra chưa từng chạy.
    // Giữ lại chỉ tạo ra một lời hứa bảo mật sai. Lớp chống giả mạo thật hiện nay: Silent-Face (bắt buộc)
    // + self-only; liveness QUAY ĐẦU là lớp chủ động thay thế, bật bằng hai cờ dưới đây khi đã hiệu chỉnh.
    private const string CfgMotionEnabled = "attendance.motion.enabled"; // app yêu cầu quay đầu lúc quét
    private const string CfgMotionEnforce = "attendance.motion.enforce"; // chặn nếu biên độ quay quá nhỏ

    // Liveness QUAY ĐẦU: cần người dùng chủ động quay đầu → để admin tự bật khi sẵn sàng (tránh phiền hà).
    private const bool DefaultMotionEnabled = false;
    private const bool DefaultMotionEnforce = false;
    // yaw span tối thiểu, theo thang của FacePose (tỉ lệ hình học từ 5 landmark, KHÔNG phải độ) —
    // xem AdaFaceR50Engine.PoseFrom. CẦN hiệu chỉnh bằng số đo thật trước khi bật enforce.
    private const double MinMotionSpan = 0.30;

    // Kiểm tra MỞ MẮT phía server (xem AdaFaceR50Engine.EyeOpenScore). Luôn ĐO (ghi vào panel để hiệu
    // chỉnh); chỉ CHẶN khi admin bật enforce → tránh khoá nhầm nhân viên vì heuristic chưa chuẩn. Ngưỡng
    // theo thang EyeOpenScore 0..1: bestEyeOpen (khung mở mắt nhất của loạt) < ngưỡng ⇒ coi là nhắm mắt.
    private const string CfgEyeOpenEnforce = "attendance.eyeopen.enforce";
    private const string CfgEyeOpenThreshold = "attendance.eyeopen.threshold";
    private const bool DefaultEyeOpenEnforce = false;
    private const double DefaultEyeOpenThreshold = 0.35;
    private const string CfgMaxBackdate = "attendance.offline.maxBackdateMinutes";
    private const string CfgGeofenceLat = "attendance.offline.geofenceLat";
    private const string CfgGeofenceLng = "attendance.offline.geofenceLng";
    private const string CfgGeofenceRadius = "attendance.offline.geofenceRadiusM";
    private const int DefaultMaxBackdateMinutes = 20;   // lùi giờ quá mức này ⇒ gắn cờ rủi ro
    private const double DefaultGeofenceRadiusM = 300;  // bán kính geofence mặc định (mét)

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

        // Chấm công NGOẠI TUYẾN chờ duyệt: khi mất điện/mất mạng, app xếp hàng rồi đồng bộ sau. Những
        // bản này KHÔNG ghi thẳng vào cham_cong_log (vì không chứng minh được có mặt tại công ty) mà
        // vào đây chờ quản lý duyệt, kèm các cờ rủi ro (lùi giờ, không ở LAN công ty, ngoài geofence).
        await conn.Cmd("""
            CREATE TABLE IF NOT EXISTS cham_cong_offline (
                id bigserial PRIMARY KEY,
                username varchar(100) NOT NULL,
                full_name varchar(200) NOT NULL DEFAULT '',
                loai varchar(10) NOT NULL,
                similarity double precision NOT NULL DEFAULT 0,
                quality double precision NOT NULL DEFAULT 0,
                occurred_at timestamptz NOT NULL,
                synced_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                backdate_minutes integer NOT NULL DEFAULT 0,
                client_ip varchar(64) NOT NULL DEFAULT '',
                on_company_lan boolean NOT NULL DEFAULT FALSE,
                gps_lat double precision NULL,
                gps_lng double precision NULL,
                distance_m double precision NULL,
                in_geofence boolean NULL,
                flags varchar(400) NOT NULL DEFAULT '',
                status varchar(20) NOT NULL DEFAULT 'pending',
                reviewed_by varchar(100) NOT NULL DEFAULT '',
                reviewed_at timestamptz NULL,
                review_note varchar(500) NOT NULL DEFAULT '',
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_cham_cong_offline_status ON cham_cong_offline (status, synced_at DESC);

            CREATE TABLE IF NOT EXISTS cham_cong_qr_sites (
                id uuid PRIMARY KEY,
                name varchar(160) NOT NULL,
                project_name varchar(160) NOT NULL DEFAULT '',
                qr_token varchar(120) NOT NULL UNIQUE,
                active boolean NOT NULL DEFAULT TRUE,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """).ExecuteNonQueryAsync();

        // AdaFace R50 emits 512-float embeddings. SFace embeddings from older versions are incompatible,
        // so drop stale templates once during startup; newly registered AdaFace templates are preserved.
        // ⚠️ Chỉ áp cho mẫu CHƯA mã hóa (không có magic "KME1"): mẫu đã mã hóa AES có độ dài khác 2048
        // nên phải loại trừ để không xóa nhầm (giải mã xong mới đúng 2048).
        await conn.Cmd(
            @"DELETE FROM cham_cong_face
              WHERE substring(embedding FROM 1 FOR 4) <> '\x4b4d4531'::bytea
                AND octet_length(embedding) <> 2048")
            .ExecuteNonQueryAsync();

        // Quyền riêng tư: hệ thống KHÔNG lưu ảnh gốc khuôn mặt nữa (chỉ giữ vector đặc trưng).
        // Dọn mọi ảnh đăng ký cũ còn sót lại. Tự chữa: sau lần đầu sẽ là no-op (0 dòng).
        await conn.Cmd("UPDATE cham_cong_face SET anh = NULL WHERE anh IS NOT NULL")
            .ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Di trú một lần: mã hóa các mẫu khuôn mặt CŨ đang lưu ở dạng chưa mã hóa (không có magic
    /// "KME1"). Chỉ chạy khi đã cấu hình khóa (cipher.Enabled). Đọc hết vào bộ nhớ rồi cập nhật
    /// từng dòng để không giữ reader mở khi UPDATE trên cùng kết nối. Rất nhẹ (số mẫu nhỏ).
    /// </summary>
    public static async Task EncryptExistingEmbeddings(Database db, FieldCipher cipher, CancellationToken ct = default)
    {
        if (!cipher.Enabled) return;
        await using var conn = await db.OpenAsync(ct);

        var pending = new List<(long Id, byte[] Bytes)>();
        await using (var r = await conn.Cmd(
            @"SELECT id, embedding FROM cham_cong_face
              WHERE substring(embedding FROM 1 FOR 4) <> '\x4b4d4531'::bytea").ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
                if (r["embedding"] is byte[] bytes && bytes.Length > 0)
                    pending.Add((r.Long("id"), bytes));
        }

        foreach (var (id, bytes) in pending)
            await conn.Cmd("UPDATE cham_cong_face SET embedding=@e WHERE id=@id")
                .With("@e", cipher.Encrypt(bytes)).With("@id", id).ExecuteNonQueryAsync(ct);
    }

    public static void MapChamCong(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/chamcong")
            .RequirePermission(Permissions.AttendanceSelf)
            .RequireRateLimiting("attendance");

        // Cho frontend/kiosk biết tên engine + ngưỡng khớp.
        // Ẩn danh: màn hình kiosk (ngoài trang đăng nhập) cần đọc trạng thái này.
        //
        // Gác bằng KioskAccessFilter như /nhandien và /cham. Lý do: endpoint này có inject IFaceEngine,
        // mà engine là singleton dựng LƯỜI — nên chỉ cần chạm vào là nạp adaface.onnx (~174 MB) vào RAM.
        // Không gác thì bất kỳ ai trên Internet (hệ thống có mở qua Cloudflare Tunnel) cũng ép được server
        // nạp model dù họ không hề chấm công được. Sau khi gác, đúng những thiết bị ĐƯỢC PHÉP chấm công
        // mới chạm tới engine — và vì APK gọi /trangthai ngay khi mở màn hình chấm công, việc nạp model
        // vẫn diễn ra sớm như cũ (làm nóng trong lúc người dùng còn đang đọc màn hình), không ai chậm đi.
        g.MapGet("/trangthai", (IFaceEngine engine) =>
            Results.Ok(new FaceEngineStatusDto(engine.Name, engine.MatchThreshold)))
            .AllowAnonymous().AddEndpointFilter<KioskAccessFilter>();

        // ĐÃ GỠ: GET /flash-challenge (cấp chuỗi màu active-flash). Xem ghi chú ở đầu lớp.

        // Cấu hình liveness QUAY ĐẦU. GET cho MỌI tài khoản (app đọc để biết có yêu cầu quay đầu không);
        // PUT chỉ Admin. Runtime, không build lại app.
        g.MapGet("/motion-config", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            return Results.Ok(new MotionConfigDto(
                await GetSettingBoolAsync(conn, CfgMotionEnabled, DefaultMotionEnabled),
                await GetSettingBoolAsync(conn, CfgMotionEnforce, DefaultMotionEnforce)));
        });

        // QR dự phòng do HR/công trình cấp. Token là chuỗi ngẫu nhiên, có thể thu hồi bằng cách tắt địa điểm.
        g.MapPost("/qr", async (QrAttendanceRequest req, ClaimsPrincipal u, Database db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Token)) return Results.BadRequest(new { message = "Mã QR trống." });
            await using var conn = await db.OpenAsync();
            string site = "", project = "";
            await using (var r = await conn.Cmd("SELECT name, project_name FROM cham_cong_qr_sites WHERE qr_token=@t AND active=TRUE")
                .With("@t", req.Token.Trim()).ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) return Results.BadRequest(new { message = "Mã QR không hợp lệ hoặc đã bị thu hồi." });
                site = r.Str("name"); project = r.Str("project_name");
            }
            var username = u.Username();
            var fullName = await conn.Cmd("SELECT full_name FROM hr_employees WHERE username=@u LIMIT 1")
                .With("@u", username).ExecuteScalarAsync() as string ?? username;
            var last = await conn.Cmd("SELECT loai FROM cham_cong_log WHERE username=@u AND (occurred_at AT TIME ZONE @tz)::date=(CURRENT_TIMESTAMP AT TIME ZONE @tz)::date ORDER BY occurred_at DESC LIMIT 1")
                .With("@u", username).With("@tz", "Asia/Ho_Chi_Minh").ExecuteScalarAsync() as string;
            var loai = string.Equals(last, "Vào", StringComparison.OrdinalIgnoreCase) ? "Ra" : "Vào";
            var note = $"QR dự phòng · {site}" + (project.Length > 0 ? $" · Công trình: {project}" : "");
            await conn.Cmd("INSERT INTO cham_cong_log (username, full_name, loai, similarity, occurred_at, ghi_chu) VALUES (@u,@n,@l,1,CURRENT_TIMESTAMP,@note)")
                .With("@u", username).With("@n", fullName).With("@l", loai).With("@note", note).ExecuteNonQueryAsync();
            await db.RecordAudit(username, "Chấm công QR", "ChamCong", site, note);
            return Results.Ok(new ChamCongResult("ok", true, username, fullName, 1, loai, DateTime.UtcNow, 1,
                $"Đã chấm công tại {site}" + (project.Length > 0 ? $" ({project})" : "") + ".", null));
        });

        g.MapPost("/qr-sites", async (CreateQrSiteRequest req, ClaimsPrincipal u, Database db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new { message = "Tên địa điểm là bắt buộc." });
            var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
            await using var conn = await db.OpenAsync();
            var id = Guid.NewGuid();
            await conn.Cmd("INSERT INTO cham_cong_qr_sites (id,name,project_name,qr_token) VALUES (@id,@n,@p,@t)")
                .With("@id", id).With("@n", req.Name.Trim()).With("@p", req.ProjectName?.Trim() ?? "").With("@t", token).ExecuteNonQueryAsync();
            return Results.Ok(new { id, token });
        }).RequirePermission(Permissions.AttendanceManage);

        g.MapPut("/motion-config", async (MotionConfigDto cfg, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            await SetSettingAsync(conn, CfgMotionEnabled, cfg.Enabled ? "1" : "0", u.Username());
            await SetSettingAsync(conn, CfgMotionEnforce, cfg.Enforce ? "1" : "0", u.Username());
            await db.RecordAudit(u.Username(), "Cấu hình liveness quay đầu", "ChamCong", "",
                $"motion enabled={cfg.Enabled} enforce={cfg.Enforce}");
            return Results.Ok(new { message = "Đã lưu cấu hình liveness quay đầu (áp dụng ngay)." });
        }).RequirePermission(Permissions.AttendanceManage);

        // Cấu hình kiểm tra MỞ MẮT phía server (Admin). Mặc định enforce=false (chỉ đo). Bật enforce sau khi
        // xem số đo EyeOpen ở panel liveness và chọn ngưỡng hợp lý (thường ~0.30–0.40).
        g.MapPut("/eyeopen-config", async (EyeOpenConfigDto cfg, ClaimsPrincipal u, Database db) =>
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var threshold = Math.Clamp(cfg.Threshold, 0, 1);
            await using var conn = await db.OpenAsync();
            await SetSettingAsync(conn, CfgEyeOpenEnforce, cfg.Enforce ? "1" : "0", u.Username());
            await SetSettingAsync(conn, CfgEyeOpenThreshold, threshold.ToString(inv), u.Username());
            await db.RecordAudit(u.Username(), "Cấu hình kiểm tra mở mắt", "ChamCong", "",
                $"eyeopen enforce={cfg.Enforce} threshold={threshold.ToString(inv)}");
            return Results.Ok(new { message = "Đã lưu cấu hình kiểm tra mở mắt (áp dụng ngay)." });
        }).RequirePermission(Permissions.AttendanceManage);

        // Số đo Silent-Face (chống ảnh/màn hình) gần nhất (Admin) — hiệu chỉnh ngưỡng ngay trên panel.
        // Kèm MỨC chống giả mạo đang chạy thật: nếu model không nạp được thì mọi ảnh đều được coi là
        // người thật mà chấm công vẫn chạy y như bình thường — kiểu hỏng không có triệu chứng, nên phải
        // hiện ngay tại panel admin chứ không chỉ nằm trong log.
        g.MapGet("/liveness-metrics", async (LivenessMetricsLog log, IFaceEngine engine, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var eyeCfg = new EyeOpenConfigDto(
                await GetSettingBoolAsync(conn, CfgEyeOpenEnforce, DefaultEyeOpenEnforce),
                await GetSettingDoubleAsync(conn, CfgEyeOpenThreshold) ?? DefaultEyeOpenThreshold);
            return Results.Ok(new LivenessPanelDto(
                new AntiSpoofDto(engine.AntiSpoof.Level.ToString(), engine.AntiSpoof.Detail),
                [.. log.Recent().Select(m => new LivenessMetricDto(
                    m.AtUtc, m.User, m.Best, m.Mean, m.Second, m.Frames, m.Threshold, m.Passed, m.MotionSpan, m.EyeOpen))],
                eyeCfg));
        }).RequirePermission(Permissions.AttendanceManage);

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
        }).RequirePermission(Permissions.AttendanceManage);

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
        }).RequirePermission(Permissions.AttendanceManage);

        // Đăng ký 1 mẫu khuôn mặt cho nhân viên (Admin). Gọi nhiều lần để thêm nhiều góc chụp.
        g.MapPost("/dangky", async (DangKyKhuonMatRequest req, ClaimsPrincipal u, Database db, IFaceEngine engine, FieldCipher cipher) =>
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
                .With("@emb", cipher.EncryptEmbedding(emb))
                .With("@by", u.Username())
                .ExecuteNonQueryAsync();

            await db.RecordAudit(u.Username(), "Đăng ký khuôn mặt", "ChamCong", req.Username, "Thêm mẫu khuôn mặt (web).");
            return Results.Ok(new { message = "Đã lưu mẫu khuôn mặt." });
        }).RequirePermission(Permissions.AttendanceManage);

        // ── Tự đăng ký khuôn mặt (nhân viên tự làm trên app) ────────────────────────
        // Trạng thái đã đăng ký của CHÍNH tài khoản đang đăng nhập → app dùng để làm mờ nút "Đăng ký
        // khuôn mặt" (mỗi tài khoản chỉ đăng ký một lần). Xác định người TỪ TOKEN.
        g.MapGet("/dangky/cua-toi", async (ClaimsPrincipal u, Database db) =>
        {
            var me = u.Username();
            if (string.IsNullOrWhiteSpace(me)) return Results.Unauthorized();

            await using var conn = await db.OpenAsync();
            var count = 0;
            DateTime? first = null;
            await using var r = await conn.Cmd(
                "SELECT COUNT(*) AS c, MIN(created_at) AS f FROM cham_cong_face WHERE username=@u")
                .With("@u", me).ExecuteReaderAsync();
            if (await r.ReadAsync()) { count = r.Int("c"); first = r.DtNull("f"); }
            return Results.Ok(new SelfFaceStatusDto(count > 0, count, first));
        });

        // Tự đăng ký khuôn mặt: quét NHIỀU góc (nhìn thẳng + nghiêng 2 bên), mỗi góc là một loạt ảnh.
        // Server chọn khung tốt nhất mỗi góc, kiểm tra chất lượng + liveness rồi lưu 1 mẫu/góc.
        // CHẶN CỨNG: mỗi tài khoản chỉ đăng ký MỘT lần (đã có mẫu ⇒ từ chối) — xác định người TỪ TOKEN,
        // KHÔNG tin client. Nhờ vậy không thể tự đăng ký khuôn mặt của mình đè lên tài khoản khác.
        g.MapPost("/dangky/tu", async (SelfFaceEnrollRequest req, ClaimsPrincipal u, Database db, IFaceEngine engine, FieldCipher cipher) =>
        {
            var me = u.Username();
            if (string.IsNullOrWhiteSpace(me)) return Results.Unauthorized();
            if (req?.Poses is null || req.Poses.Count == 0)
                return Results.BadRequest(new { message = "Thiếu ảnh khuôn mặt." });
            if (req.Poses.Sum(p => p?.Images?.Count ?? 0) > PayloadLimits.MaxImagesPerEnrollRequest)
                return Results.BadRequest(new { message = $"Tối đa {PayloadLimits.MaxImagesPerEnrollRequest} ảnh mỗi yêu cầu." });
            if (req.Poses.SelectMany(p => p?.Images ?? []).Any(img => !PayloadLimits.TryDecodeImage(img, out _)))
                return Results.BadRequest(new { message = $"Mỗi ảnh phải nhỏ hơn {PayloadLimits.MaxImageBytes / 1024 / 1024} MB." });

            await using var conn = await db.OpenAsync();

            // Đã đăng ký rồi → chặn (mỗi tài khoản chỉ đăng ký một lần).
            var existing = Convert.ToInt32(await conn.Cmd(
                "SELECT COUNT(*) FROM cham_cong_face WHERE username=@u").With("@u", me).ExecuteScalarAsync());
            if (existing > 0)
                return Results.BadRequest(new { message = "Bạn đã đăng ký khuôn mặt rồi. Mỗi tài khoản chỉ được đăng ký một lần." });

            var fullName = u.FindFirstValue("fullName") ?? "";

            // Xử lý từng góc → chọn khung tốt nhất, kiểm tra chất lượng + liveness, trích embedding.
            var samples = new List<float[]>();
            var frontOk = false;
            foreach (var pose in req.Poses)
            {
                if (pose?.Images is null || pose.Images.Count == 0) continue;
                var isFront = string.Equals(pose.Pose, "front", StringComparison.OrdinalIgnoreCase);

                var candidates = new List<(byte[] Bytes, FaceFrameQuality Q)>();
                foreach (var img in pose.Images)
                {
                    if (!TryDecodeImage(img, out var bytes)) continue;
                    if (engine.AssessFrame(bytes) is not { FaceFound: true } q) continue;
                    candidates.Add((bytes, q));
                }
                if (candidates.Count == 0) continue;
                candidates.Sort((a, b) => b.Q.Score.CompareTo(a.Q.Score));
                var best = candidates[0];

                // Chất lượng tối thiểu (nới nhẹ cho góc nghiêng vì mặt nhỏ hơn/khó nét hơn chính diện).
                var minQuality = isFront ? MinFrameQuality : MinFrameQuality * 0.8;
                if (best.Q.Score < minQuality) continue;

                // Mẫu CHÍNH DIỆN phải thực sự nhìn thẳng (chấm công vốn ép chính diện nên đây là mẫu chuẩn).
                if (isFront && CheckPosture(best.Q.Pose) is not null) continue;

                // Chống giả mạo trên vài khung tốt nhất của góc này (ảnh/màn hình giả thấp ở mọi khung).
                double bestLive = 0;
                foreach (var c in candidates.Take(5))
                {
                    bestLive = Math.Max(bestLive, engine.LivenessProbability(c.Bytes));
                    if (bestLive >= engine.LivenessThreshold) break;
                }
                if (bestLive < engine.LivenessThreshold)
                    return Results.BadRequest(new { message = "Nghi ngờ giả mạo (không phải người thật). Hãy nhìn trực tiếp vào camera, không dùng ảnh/màn hình." });

                var emb = engine.ExtractEmbedding(best.Bytes);
                if (emb is null) continue;
                samples.Add(emb);
                if (isFront) frontOk = true;
            }

            if (!frontOk)
                return Results.BadRequest(new { message = "Chưa lấy được ảnh chính diện rõ nét. Hãy đăng ký lại ở nơi đủ sáng và nhìn thẳng vào camera." });
            if (samples.Count < 2)
                return Results.BadRequest(new { message = "Chưa đủ mẫu khuôn mặt. Vui lòng quét lại đủ các góc theo hướng dẫn." });

            // Kiểm tra lại lần cuối rồi chèn tất cả mẫu (tránh đăng ký hai lần nếu bấm dồn).
            var existing2 = Convert.ToInt32(await conn.Cmd(
                "SELECT COUNT(*) FROM cham_cong_face WHERE username=@u").With("@u", me).ExecuteScalarAsync());
            if (existing2 > 0)
                return Results.BadRequest(new { message = "Bạn đã đăng ký khuôn mặt rồi." });

            foreach (var emb in samples)
                await conn.Cmd(
                    @"INSERT INTO cham_cong_face (username, full_name, embedding, created_at, created_by)
                      VALUES (@u, @fn, @emb, CURRENT_TIMESTAMP, @by)")
                    .With("@u", me).With("@fn", fullName)
                    .With("@emb", cipher.EncryptEmbedding(emb))
                    .With("@by", SelfEnrollTag)
                    .ExecuteNonQueryAsync();

            await db.RecordAudit(me, "Đăng ký khuôn mặt", "ChamCong", me, $"Tự đăng ký {samples.Count} mẫu (app).");
            return Results.Ok(new SelfFaceEnrollResult("Đăng ký khuôn mặt thành công.", samples.Count));
        });

        // ĐÃ GỠ: POST /huongmat (ước lượng hướng mặt cho EnrollWizard). Trình đăng ký khuôn mặt trên web
        // nay tự tính tư thế NGAY TRÊN TRÌNH DUYỆT bằng đúng công thức hình học đó (xem
        // FaceTrackingOverlay/EnrollWizard) nên endpoint không còn ai gọi. Giữ lại thì hở đúng chỗ vừa bịt
        // ở /trangthai, mà còn nặng hơn: ẩn danh, không gác KioskAccessFilter, và mỗi request đều chạy
        // YuNet thật trên ảnh tùy ý — tức bất kỳ ai trên Internet cũng ép được server nạp model rồi suy
        // luận hộ. Cần lại thì dựng lại kèm .AddEndpointFilter<KioskAccessFilter>() như /nhandien.

        // Xóa toàn bộ mẫu khuôn mặt của 1 nhân viên (Admin).
        g.MapDelete("/dangky/{username}", async (string username, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("DELETE FROM cham_cong_face WHERE username=@u")
                .With("@u", username).ExecuteNonQueryAsync();
            if (n > 0) await db.RecordAudit(u.Username(), "Xóa khuôn mặt", "ChamCong", username, "Xóa mẫu khuôn mặt (web).");
            return n > 0 ? Results.NoContent() : Results.NotFound();
        }).RequirePermission(Permissions.AttendanceManage);

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
        }).RequirePermission(Permissions.AttendanceManage);

        // Chấm công: chụp ảnh -> liveness -> trích vector -> so khớp -> ghi Vào/Ra.
        // Ẩn danh: cho phép chấm công ở kiosk màn hình đăng nhập (không cần tài khoản).
        g.MapPost("/nhandien", async (NhanDienRequest req, Database db, IFaceEngine engine, FieldCipher cipher, HttpContext http) =>
        {
            // Ẩn danh (kiosk, chưa đăng nhập) ⇒ KHÔNG trả username/họ tên đầy đủ để tránh thu thập danh
            // tính. Đăng nhập rồi (chấm cho chính mình) ⇒ trả đủ thông tin như trước.
            var anon = http.User.Identity?.IsAuthenticated != true;
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
                    var emb = cipher.DecryptEmbedding((byte[])r["embedding"]);
                    var sim = engine.Compare(probe, emb);
                    if (sim > best) { best = sim; bestUser = r.Str("username"); bestName = r.Str("full_name"); }
                }
            }

            if (bestUser is null || best < engine.MatchThreshold)
                return Results.Ok(new NhanDienResult(false, null, null, best, null, null,
                    "Không nhận diện được. Khuôn mặt chưa được đăng ký hoặc ảnh chưa rõ."));

            // Tên hiển thị cho phản hồi + thông điệp: che khi ẩn danh (message của policy có kèm tên).
            var display = anon ? MaskName(bestName ?? bestUser) : (bestName ?? bestUser);
            var outUser = anon ? null : bestUser;
            var outName = anon ? display : bestName;

            var decision = await AttendancePolicy.DecideAsync(conn, bestUser, display);
            if (!decision.ShouldRecord)
                return Results.Ok(new NhanDienResult(true, outUser, outName, best, decision.Loai, decision.ExistingAt,
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
            try { await TryAdaptiveLearnAsync(conn, bestUser, bestName ?? "", probe, best, cipher); }
            catch { /* bỏ qua, chấm công vẫn thành công */ }

            return Results.Ok(new NhanDienResult(true, outUser, outName, best, loai, DateTime.UtcNow,
                decision.Message));
        }).AllowAnonymous().AddEndpointFilter<KioskAccessFilter>();

        // Chấm công bằng LOẠT ẢNH: KHÔNG quét trực tiếp liên tục — client chụp 1 loạt khung,
        // server chọn ẢNH TỐT NHẤT (nét, đủ sáng, mặt to & chính diện), kiểm tra tư thế (báo
        // trực tiếp nếu sai), liveness rồi nhận diện và ghi nhật ký. Ẩn danh để dùng ở kiosk.
        g.MapPost("/cham", async (ChamCongBurstRequest req, Database db, IFaceEngine engine, ClaimsPrincipal u, FieldCipher cipher, LivenessMetricsLog livenessLog, AttendancePreviewTokens previewTokens, IHubContext<ChangesHub> hub, ILoggerFactory lf, HttpContext http) =>
        {
            // ── XÁC NHẬN BẰNG TOKEN XEM TRƯỚC ────────────────────────────────────────────────────
            // Người dùng vừa xem trước xong và bấm "Xác nhận": mọi cổng (tư thế, chất lượng, Silent-Face,
            // quay đầu, so khớp đúng người) ĐÃ qua vài giây trước và kết quả còn nguyên
            // trong token. Chỉ việc ghi công — không nhận diện lại, tiết kiệm một nửa suy luận mỗi lượt.
            if (!string.IsNullOrWhiteSpace(req?.ConfirmToken))
            {
                // Hai cờ này loại trừ nhau: token LÀ lệnh ghi công. Nhận cả hai rồi vẫn ghi thì token bị
                // tiêu trong một request tự nhận là "chỉ xem trước" — từ chối thẳng cho khỏi mập mờ.
                if (req.PreviewOnly)
                    return Results.BadRequest(new { message = "Không thể vừa xem trước vừa xác nhận." });

                var requester = u.Username();
                if (previewTokens.Consume(requester, req.ConfirmToken) is not { } pending)
                    return Results.Ok(new ChamCongResult("expired", false, null, null, 0, null, null, 0,
                        "Phiên xác nhận đã hết hạn.", "Vui lòng quét lại khuôn mặt."));

                await using var confirmConn = await db.OpenAsync();

                // Quyết định Vào/Ra tính LẠI ở đây chứ không lấy từ lúc xem trước: giữa hai bước có thể đã
                // qua mốc giờ, hoặc đã có lượt chấm khác chen vào. Đây là truy vấn nhẹ, không phải AI.
                var confirmDecision = await AttendancePolicy.DecideAsync(
                    confirmConn, pending.MatchedUser, pending.MatchedName);

                if (!confirmDecision.ShouldRecord)
                    return Results.Ok(new ChamCongResult("ok", true, pending.MatchedUser, pending.MatchedName,
                        pending.Similarity, confirmDecision.Loai, confirmDecision.ExistingAt, pending.Quality,
                        confirmDecision.Message, null));

                await confirmConn.Cmd(
                    @"INSERT INTO cham_cong_log (username, full_name, loai, similarity, occurred_at, ghi_chu)
                      VALUES (@u, @fn, @loai, @sim, CURRENT_TIMESTAMP, '')")
                    .With("@u", pending.MatchedUser).With("@fn", pending.MatchedName)
                    .With("@loai", confirmDecision.Loai).With("@sim", pending.Similarity)
                    .ExecuteNonQueryAsync();

                await db.RecordAudit(pending.MatchedUser, $"Chấm công {confirmDecision.Loai}", "ChamCong",
                    pending.MatchedUser,
                    $"Độ khớp {pending.Similarity:0.000}, chất lượng ảnh {pending.Quality:0.00} (xác nhận sau xem trước).");

                // Tự học vẫn chạy như luồng cũ nhờ vector đặc trưng đã giữ trong token.
                try
                {
                    await TryAdaptiveLearnAsync(confirmConn, pending.MatchedUser, pending.MatchedName,
                        pending.Probe, pending.Similarity, cipher);
                }
                catch { /* tự học là phụ trợ, lỗi không được làm hỏng chấm công */ }

                return Results.Ok(new ChamCongResult("ok", true, pending.MatchedUser, pending.MatchedName,
                    pending.Similarity, confirmDecision.Loai, DateTime.UtcNow, pending.Quality,
                    confirmDecision.Message, null));
            }

            if (req?.Images is null || req.Images.Count == 0)
                return Results.BadRequest(new { message = "Thiếu ảnh chấm công." });
            if (req.Images.Count > PayloadLimits.MaxImagesPerRequest)
                return Results.BadRequest(new { message = $"Tối đa {PayloadLimits.MaxImagesPerRequest} ảnh mỗi yêu cầu." });
            if (req.Images.Any(img => !PayloadLimits.TryDecodeImage(img, out _)))
                return Results.BadRequest(new { message = $"Mỗi ảnh phải nhỏ hơn {PayloadLimits.MaxImageBytes / 1024 / 1024} MB." });

            // CHẶN CỨNG chế độ "chỉ chấm cho chính mình": xác định TỪ TOKEN phía server, KHÔNG tin cờ
            // client (req.SelfOnly). Mọi tài khoản ĐÃ đăng nhập không có attendance.manage đều bắt buộc
            // chỉ chấm cho CHÍNH MÌNH. Người quản lý chấm công được chấm hộ; kiosk ẩn danh vẫn so khớp mở.
            var currentUser = u.Username();
            var selfOnly = !string.IsNullOrWhiteSpace(currentUser) && !u.Can(Permissions.AttendanceManage);
            // Ẩn danh (kiosk, chưa đăng nhập) ⇒ che username/họ tên trong phản hồi để tránh thu thập danh tính.
            var anon = string.IsNullOrWhiteSpace(currentUser);

            // Đồng hồ đo TOÀN BỘ khâu nhận diện (giải mã ảnh + YuNet + Silent-Face + AdaFace + quét mẫu).
            // Đây chính là phần mà bước "Xác nhận" từng chạy lại lần thứ hai; giữ lại để còn đo được
            // thay vì phải suy đoán. Rẻ: một Stopwatch và một dòng log cho mỗi lượt chấm.
            var recognizeSw = System.Diagnostics.Stopwatch.StartNew();

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

            // MỞ MẮT: lấy khung "mở mắt nhất" của loạt — người thật chỉ cần MỞ MẮT ở MỘT khung tốt là đủ
            // (chớp mắt vài khung không sao). Đo phía server nên không tin được cờ client. Giá trị ~1.0 khi
            // heuristic không đánh giá được (mặt nhỏ/lỗi) ⇒ fail-open.
            var bestEyeOpen = candidates.Max(c => c.Q.EyeOpen);

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
            var liveScores = new List<double>();
            foreach (var c in candidates.Take(livenessFramesToCheck))
                liveScores.Add(engine.LivenessProbability(c.Bytes)); // tính HẾT để có đủ số đo hiệu chỉnh
            var bestLive = liveScores.Count > 0 ? liveScores.Max() : 0;
            var livePassed = bestLive >= engine.LivenessThreshold;

            // 4a) LIVENESS QUAY ĐẦU (challenge-response): biên độ góc quay yaw của loạt (từ pose các khung
            // đã có sẵn). Ảnh tĩnh không quay đầu ⇒ span ≈ 0. Chỉ xét khi app báo motionCheck.
            var yaws = candidates.Where(c => c.Q.FaceFound).Select(c => c.Q.Pose.Yaw).ToList();
            var motionSpan = req.MotionCheck && yaws.Count >= 2 ? yaws.Max() - yaws.Min() : -1;
            bool motionEnabled = false, motionEnforce = false;
            // Cấu hình MỞ MẮT: đọc LUÔN (không phụ thuộc motionCheck) vì đây là lớp server độc lập.
            bool eyeOpenEnforce;
            double eyeOpenThreshold;
            {
                await using var smc = await db.OpenAsync();
                if (req.MotionCheck)
                {
                    motionEnabled = await GetSettingBoolAsync(smc, CfgMotionEnabled, DefaultMotionEnabled);
                    motionEnforce = await GetSettingBoolAsync(smc, CfgMotionEnforce, DefaultMotionEnforce);
                }
                eyeOpenEnforce = await GetSettingBoolAsync(smc, CfgEyeOpenEnforce, DefaultEyeOpenEnforce);
                eyeOpenThreshold = await GetSettingDoubleAsync(smc, CfgEyeOpenThreshold) ?? DefaultEyeOpenThreshold;
            }

            // Ghi số đo (Silent-Face + biên độ quay + độ mở mắt) để hiển thị lên panel hiệu chỉnh.
            livenessLog.Record(currentUser, liveScores, engine.LivenessThreshold, livePassed, motionSpan, bestEyeOpen);
            // These diagnostics only live in memory, so no database trigger can publish them.
            // Notify the open admin panel instead of making it poll this endpoint every four seconds.
            try { await hub.Clients.All.SendAsync("changed", "liveness", http.RequestAborted); }
            catch (Exception ex) { lf.CreateLogger("LivenessRealtime").LogDebug(ex, "Could not publish liveness metrics."); }

            if (!livePassed)
                return Results.Ok(new ChamCongResult("spoof", false, null, null, 0, null, null, best.Score,
                    "Nghi ngờ giả mạo (không phải người thật).", "Hãy nhìn trực tiếp vào camera, không dùng ảnh/màn hình."));

            // Chặn nếu bật kiểm tra chuyển động + biên độ quay quá nhỏ (nghi ảnh tĩnh). Fail-open khi thiếu
            // dữ liệu (span < 0). Mặc định enforce=false (chỉ ghi log) để hiệu chỉnh trước.
            if (req.MotionCheck && motionEnabled && motionEnforce && motionSpan >= 0 && motionSpan < MinMotionSpan)
                return Results.Ok(new ChamCongResult("spoof", false, null, null, 0, null, null, best.Score,
                    "Chưa xác nhận được người thật (không thấy quay đầu).",
                    "Làm theo hướng dẫn: nhìn thẳng rồi từ từ quay đầu sang hai bên."));

            // 4c) MỞ MẮT: chặn nếu KHÔNG có khung nào mắt mở đủ (nhắm mắt/lim dim/giơ ảnh mắt nhắm). Chỉ khi
            // admin bật enforce; mặc định chỉ ghi log để hiệu chỉnh trước. Fail-open khi heuristic không đo
            // được (bestEyeOpen ~1.0). Client cũng đã nhắc mở mắt lúc quét nên người thật gần như luôn qua.
            if (eyeOpenEnforce && bestEyeOpen < eyeOpenThreshold)
                return Results.Ok(new ChamCongResult("eyesclosed", false, null, null, 0, null, null, best.Score,
                    "Chưa xác nhận mở mắt.", "Hãy mở mắt và nhìn thẳng vào màn hình rồi chấm lại."));

            // ĐÃ GỠ: 4b) active-flash liveness (đối chiếu màu phản xạ với chuỗi màu màn hình). Khối này
            // chỉ chạy khi client gửi challengeId + slotIndices, mà cả APK lẫn web đều đã ngừng gửi từ
            // lâu ⇒ nó chưa từng gác gì trên thực tế. Xem ghi chú đầu lớp.

            // Gộp vector NHIỀU khung CHÍNH DIỆN tốt nhất (trung bình + chuẩn hóa) → ổn định hơn 1 khung,
            // giảm nhận nhầm/từ chối nhầm. Loại khung QUAY ĐẦU (yaw lớn — khi bật liveness quay đầu) để
            // không làm méo vector. Không có khung chính diện nào ⇒ dùng khung tốt nhất.
            const int fuseFrames = 5;
            const double frontalYawLimit = 0.18;
            var fuseBytes = candidates
                .Where(c => Math.Abs(c.Q.Pose.Yaw) < frontalYawLimit)
                .Take(fuseFrames)
                .Select(c => c.Bytes)
                .ToList();
            if (fuseBytes.Count == 0) fuseBytes.Add(bestBytes);
            var probe = engine.ExtractFusedEmbedding(fuseBytes);
            if (probe is null)
                return Results.Ok(new ChamCongResult("noface", false, null, null, 0, null, null, best.Score,
                    "Không trích được đặc trưng khuôn mặt.", "Nhìn thẳng vào camera rồi chấm lại."));

            await using var conn = await db.OpenAsync();

            // 5) So khớp toàn bộ mẫu đã đăng ký. Đồng thời theo dõi độ khớp cao nhất với chính tài
            //    khoản đang đăng nhập (phục vụ chế độ self-only bên dưới).
            string? bestUser = null, bestName = null;
            double bestSim = 0;
            double selfSim = 0;
            string? selfName = null;
            await using (var r = await conn.Cmd(
                "SELECT username, full_name, embedding FROM cham_cong_face").ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    var uname = r.Str("username");
                    var emb = cipher.DecryptEmbedding((byte[])r["embedding"]);
                    var sim = engine.Compare(probe, emb);
                    if (sim > bestSim) { bestSim = sim; bestUser = uname; bestName = r.Str("full_name"); }
                    if (selfOnly && sim > selfSim
                        && string.Equals(uname, currentUser, StringComparison.OrdinalIgnoreCase))
                    { selfSim = sim; selfName = r.Str("full_name"); }
                }
            }

            recognizeSw.Stop();
            // Con số này là thứ bước "Xác nhận" từng tiêu tốn lần thứ hai cho mỗi lượt chấm công. Ghi ở
            // mức Information để đo được ngay trên máy thật mà không phải bật chế độ gỡ lỗi.
            lf.CreateLogger("ChamCongPerf").LogInformation(
                "Nhận diện xong trong {Ms} ms — {Frames} khung gửi lên, {Faces} khung có mặt, xem trước={Preview}.",
                recognizeSw.ElapsedMilliseconds, req.Images.Count, candidates.Count, req.PreviewOnly);

            // 5b) Self-only: chỉ cho phép nhân viên chấm công cho CHÍNH MÌNH.
            if (selfOnly)
            {
                if (selfSim >= engine.MatchThreshold)
                {
                    // Đúng là người đang đăng nhập → ghi công cho họ (bỏ qua mọi khớp của người khác).
                    bestUser = currentUser;
                    bestName = selfName ?? "";
                    bestSim = selfSim;
                }
                else if (bestUser is not null && bestSim >= engine.MatchThreshold
                         && !string.Equals(bestUser, currentUser, StringComparison.OrdinalIgnoreCase))
                {
                    // Khuôn mặt khớp NHÂN VIÊN KHÁC → chặn, không cho chấm công hộ.
                    return Results.Ok(new ChamCongResult("proxy", false, null, null, bestSim, null, null, best.Score,
                        "Không được chấm công hộ nhân viên khác trong công ty.",
                        "Mỗi người chỉ được chấm công bằng khuôn mặt của chính mình."));
                }
                else
                {
                    // Không khớp chính mình và cũng không rõ là ai → yêu cầu thử lại.
                    return Results.Ok(new ChamCongResult("unknown", false, null, null, bestSim, null, null, best.Score,
                        "Khuôn mặt không khớp. Vui lòng thử lại.", null));
                }
            }

            if (bestUser is null || bestSim < engine.MatchThreshold)
                return Results.Ok(new ChamCongResult("unknown", false, null, null, bestSim, null, null, best.Score,
                    "Không nhận diện được. Khuôn mặt chưa đăng ký hoặc ảnh chưa rõ.", null));

            // 6) Quyết định Vào/Ra + ghi nhật ký.
            // Đồng bộ ngoại tuyến: dùng giờ chấm thật (req.OccurredAt) cho cả quyết định Vào/Ra lẫn log.
            var occurredAtUtc = req.OccurredAt?.ToUniversalTime();
            var isOffline = occurredAtUtc is not null;
            // Tên hiển thị cho phản hồi + thông điệp policy: che khi ẩn danh. DB/audit vẫn ghi tên thật.
            var display = anon ? MaskName(bestName ?? bestUser) : (bestName ?? bestUser);
            var outUser = anon ? null : bestUser;
            var outName = anon ? display : bestName;
            var decision = await AttendancePolicy.DecideAsync(conn, bestUser, display, atUtc: occurredAtUtc);

            // Chế độ XEM TRƯỚC: đã nhận diện chắc chắn + đã qua liveness, nhưng CHƯA ghi nhật ký.
            // Trả về ai + Vào/Ra dự kiến + giờ dự kiến để app hiện form xác nhận. App bấm "Xác nhận"
            // sẽ gọi lại đúng loạt ảnh này với PreviewOnly=false để ghi công thật.
            if (req.PreviewOnly)
            {
                var previewAt = decision.ShouldRecord ? (occurredAtUtc ?? DateTime.UtcNow) : decision.ExistingAt;

                // Cấp token giữ sẵn kết quả để bước "Xác nhận" khỏi chạy lại toàn bộ khâu nhận diện.
                // Chỉ cấp cho request đã đăng nhập (kiosk ẩn danh giữ nguyên luồng gửi lại ảnh).
                //
                // KHÔNG cấp cho lượt có occurredAt (đồng bộ ngoại tuyến): lượt đó phải đi qua bảng chờ
                // duyệt kèm cờ rủi ro, mà nhánh xác nhận bằng token thì ghi thẳng vào sổ công. Client
                // nắm cả hai cờ này nên phải chặn ở server, không dựa vào việc app "không gửi như thế".
                var previewToken = isOffline ? null : previewTokens.Issue(new AttendancePreviewTokens.Pending(
                    currentUser, bestUser, bestName ?? "", bestSim, best.Score, probe));

                return Results.Ok(new ChamCongResult("ok", true, outUser, outName, bestSim, decision.Loai,
                    previewAt, best.Score, decision.Message, null, previewToken));
            }

            // ĐỒNG BỘ NGOẠI TUYẾN: KHÔNG ghi thẳng vào bảng công (không chứng minh được có mặt tại công
            // ty lúc chấm) → tạo bản CHỜ DUYỆT + gắn cờ rủi ro để quản lý soi và duyệt/từ chối trên web.
            if (isOffline)
            {
                await CreateOfflinePendingAsync(conn, http, bestUser, bestName ?? "", decision,
                    bestSim, best.Score, occurredAtUtc!.Value, req.GpsLat, req.GpsLng);
                await db.RecordAudit(bestUser, "Chấm công ngoại tuyến (chờ duyệt)", "ChamCong", bestUser,
                    $"Chờ duyệt · độ khớp {bestSim:0.000} · giờ chấm {occurredAtUtc:yyyy-MM-dd HH:mm} (UTC).");
                return Results.Ok(new ChamCongResult("pending", true, outUser, outName, bestSim, decision.Loai,
                    occurredAtUtc, best.Score, "Đã đồng bộ — chờ quản lý duyệt.", null));
            }

            if (!decision.ShouldRecord)
                return Results.Ok(new ChamCongResult("ok", true, outUser, outName, bestSim, decision.Loai,
                    decision.ExistingAt, best.Score, decision.Message, null));

            var loai = decision.Loai;
            await conn.Cmd(
                @"INSERT INTO cham_cong_log (username, full_name, loai, similarity, occurred_at, ghi_chu)
                  VALUES (@u, @fn, @loai, @sim, COALESCE(@at, CURRENT_TIMESTAMP), @note)")
                .With("@u", bestUser).With("@fn", bestName ?? "")
                .With("@loai", loai).With("@sim", bestSim)
                .With("@at", (object?)occurredAtUtc ?? DBNull.Value)
                .With("@note", isOffline ? "Đồng bộ ngoại tuyến" : "")
                .ExecuteNonQueryAsync();

            await db.RecordAudit(bestUser, $"Chấm công {loai}", "ChamCong", bestUser,
                $"Độ khớp {bestSim:0.000}, chất lượng ảnh {best.Score:0.00}{(isOffline ? ", đồng bộ ngoại tuyến" : "")} (web).");

            try { await TryAdaptiveLearnAsync(conn, bestUser, bestName ?? "", probe, bestSim, cipher); }
            catch { /* tự học là phụ trợ, lỗi không được làm hỏng chấm công */ }

            return Results.Ok(new ChamCongResult("ok", true, outUser, outName, bestSim, loai,
                occurredAtUtc ?? DateTime.UtcNow, best.Score, decision.Message, null));
        }).AllowAnonymous().AddEndpointFilter<KioskAccessFilter>();

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
        }).RequirePermission(Permissions.AttendanceManage);

        // ── Chấm công ngoại tuyến chờ duyệt (Admin) ─────────────────────────────
        // Danh sách bản chờ duyệt (mặc định status=pending; truyền status=all để xem cả đã xử lý).
        g.MapGet("/offline/mine", async (ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var list = new List<ChamCongOfflineDto>();
            await using var r = await conn.Cmd("""
                SELECT id, username, full_name, loai, similarity, quality, occurred_at, synced_at,
                       backdate_minutes, client_ip, on_company_lan, gps_lat, gps_lng, distance_m,
                       in_geofence, flags, status, reviewed_by, reviewed_at, review_note
                FROM cham_cong_offline WHERE username=@u ORDER BY synced_at DESC LIMIT 100
                """).With("@u", u.Username()).ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new ChamCongOfflineDto(
                    r.Long("id"), r.Str("username"), r.Str("full_name"), r.Str("loai"),
                    r.GetDouble(r.GetOrdinal("similarity")), r.GetDouble(r.GetOrdinal("quality")),
                    r.Dt("occurred_at"), r.Dt("synced_at"), r.Int("backdate_minutes"), r.Str("client_ip"),
                    r.Bool("on_company_lan"),
                    r.IsDBNull(r.GetOrdinal("gps_lat")) ? null : r.GetDouble(r.GetOrdinal("gps_lat")),
                    r.IsDBNull(r.GetOrdinal("gps_lng")) ? null : r.GetDouble(r.GetOrdinal("gps_lng")),
                    r.IsDBNull(r.GetOrdinal("distance_m")) ? null : r.GetDouble(r.GetOrdinal("distance_m")),
                    r.IsDBNull(r.GetOrdinal("in_geofence")) ? null : r.Bool("in_geofence"),
                    r.Str("flags"), r.Str("status"), r.Str("reviewed_by"), r.DtNull("reviewed_at"), r.Str("review_note")));
            return Results.Ok(list);
        });

        g.MapGet("/offline-policy", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            return Results.Ok(new OfflineConfigDto(
                await GetSettingDoubleAsync(conn, CfgGeofenceLat),
                await GetSettingDoubleAsync(conn, CfgGeofenceLng),
                await GetSettingDoubleAsync(conn, CfgGeofenceRadius) ?? DefaultGeofenceRadiusM,
                (int)(await GetSettingDoubleAsync(conn, CfgMaxBackdate) ?? DefaultMaxBackdateMinutes)));
        });

        g.MapGet("/offline", async (Database db, string? status) =>
        {
            await using var conn = await db.OpenAsync();
            var where = status is null or "pending" ? "WHERE status = 'pending'"
                : status == "all" ? "" : "WHERE status = @st";
            var cmd = conn.Cmd(
                $@"SELECT id, username, full_name, loai, similarity, quality, occurred_at, synced_at,
                          backdate_minutes, client_ip, on_company_lan, gps_lat, gps_lng, distance_m,
                          in_geofence, flags, status, reviewed_by, reviewed_at, review_note
                   FROM cham_cong_offline {where}
                   ORDER BY (status = 'pending') DESC, synced_at DESC LIMIT 500");
            if (where.Contains("@st")) cmd.With("@st", status!);

            var list = new List<ChamCongOfflineDto>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new ChamCongOfflineDto(
                    r.Long("id"), r.Str("username"), r.Str("full_name"), r.Str("loai"),
                    r.GetDouble(r.GetOrdinal("similarity")), r.GetDouble(r.GetOrdinal("quality")),
                    r.Dt("occurred_at"), r.Dt("synced_at"), r.Int("backdate_minutes"),
                    r.Str("client_ip"), r.Bool("on_company_lan"),
                    r.IsDBNull(r.GetOrdinal("gps_lat")) ? null : r.GetDouble(r.GetOrdinal("gps_lat")),
                    r.IsDBNull(r.GetOrdinal("gps_lng")) ? null : r.GetDouble(r.GetOrdinal("gps_lng")),
                    r.IsDBNull(r.GetOrdinal("distance_m")) ? null : r.GetDouble(r.GetOrdinal("distance_m")),
                    r.IsDBNull(r.GetOrdinal("in_geofence")) ? null : r.Bool("in_geofence"),
                    r.Str("flags"), r.Str("status"), r.Str("reviewed_by"), r.DtNull("reviewed_at"), r.Str("review_note")));
            return Results.Ok(list);
        }).RequirePermission(Permissions.AttendanceManage);

        // Duyệt: ghi bản ngoại tuyến vào bảng công (đúng giờ chấm thật) rồi đánh dấu approved.
        g.MapPost("/offline/{id:long}/approve", async (long id, OfflineReviewRequest? body, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            string? username = null, fullName = null, loai = null; DateTime occurredAt = default; double sim = 0; string curStatus = "";
            await using (var r = await conn.Cmd(
                "SELECT username, full_name, loai, occurred_at, similarity, status FROM cham_cong_offline WHERE id=@id")
                .With("@id", id).ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) return Results.NotFound();
                username = r.Str("username"); fullName = r.Str("full_name"); loai = r.Str("loai");
                occurredAt = r.Dt("occurred_at"); sim = r.GetDouble(r.GetOrdinal("similarity")); curStatus = r.Str("status");
            }
            if (curStatus != "pending")
                return Results.BadRequest(new { message = "Bản này đã được xử lý." });

            await conn.Cmd(
                @"INSERT INTO cham_cong_log (username, full_name, loai, similarity, occurred_at, ghi_chu)
                  VALUES (@u, @fn, @loai, @sim, @at, @note)")
                .With("@u", username!).With("@fn", fullName ?? "")
                .With("@loai", loai!).With("@sim", sim)
                .With("@at", occurredAt)
                .With("@note", "Ngoại tuyến (đã duyệt)")
                .ExecuteNonQueryAsync();

            await conn.Cmd(
                @"UPDATE cham_cong_offline SET status='approved', reviewed_by=@by, reviewed_at=CURRENT_TIMESTAMP,
                    review_note=@note WHERE id=@id")
                .With("@by", u.Username()).With("@note", body?.Note ?? "").With("@id", id)
                .ExecuteNonQueryAsync();

            await db.RecordAudit(u.Username(), "Duyệt chấm công ngoại tuyến", "ChamCong", username!,
                $"Duyệt bản #{id} · {loai} · {occurredAt:yyyy-MM-dd HH:mm} (UTC).");
            return Results.Ok(new { message = "Đã duyệt và ghi công." });
        }).RequirePermission(Permissions.AttendanceManage);

        // Từ chối: đánh dấu rejected, KHÔNG ghi công.
        g.MapPost("/offline/{id:long}/reject", async (long id, OfflineReviewRequest? body, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd(
                @"UPDATE cham_cong_offline SET status='rejected', reviewed_by=@by, reviewed_at=CURRENT_TIMESTAMP,
                    review_note=@note WHERE id=@id AND status='pending'")
                .With("@by", u.Username()).With("@note", body?.Note ?? "").With("@id", id)
                .ExecuteNonQueryAsync();
            if (n == 0) return Results.BadRequest(new { message = "Không tìm thấy bản chờ duyệt." });
            await db.RecordAudit(u.Username(), "Từ chối chấm công ngoại tuyến", "ChamCong", "", $"Từ chối bản #{id}.");
            return Results.Ok(new { message = "Đã từ chối." });
        }).RequirePermission(Permissions.AttendanceManage);

        // Cấu hình chính sách chấm công ngoại tuyến (Admin): geofence công ty + ngưỡng lùi giờ.
        g.MapGet("/offline-config", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            return Results.Ok(new OfflineConfigDto(
                await GetSettingDoubleAsync(conn, CfgGeofenceLat),
                await GetSettingDoubleAsync(conn, CfgGeofenceLng),
                await GetSettingDoubleAsync(conn, CfgGeofenceRadius) ?? DefaultGeofenceRadiusM,
                (int)(await GetSettingDoubleAsync(conn, CfgMaxBackdate) ?? DefaultMaxBackdateMinutes)));
        }).RequirePermission(Permissions.AttendanceManage);

        g.MapPut("/offline-config", async (OfflineConfigDto cfg, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            // Toạ độ rỗng ("") = tắt geofence (GetSettingDoubleAsync trả null → không kiểm tra khoảng cách).
            await SetSettingAsync(conn, CfgGeofenceLat, cfg.GeofenceLat?.ToString(inv) ?? "", u.Username());
            await SetSettingAsync(conn, CfgGeofenceLng, cfg.GeofenceLng?.ToString(inv) ?? "", u.Username());
            await SetSettingAsync(conn, CfgGeofenceRadius, cfg.GeofenceRadiusM.ToString(inv), u.Username());
            await SetSettingAsync(conn, CfgMaxBackdate, cfg.MaxBackdateMinutes.ToString(inv), u.Username());
            await db.RecordAudit(u.Username(), "Cập nhật cấu hình chấm công ngoại tuyến", "ChamCong", "",
                $"Geofence bán kính {cfg.GeofenceRadiusM:0}m · lùi giờ tối đa {cfg.MaxBackdateMinutes} phút.");
            return Results.Ok(new { message = "Đã lưu cấu hình." });
        }).RequirePermission(Permissions.AttendanceManage);
    }

    private static async Task SetSettingAsync(NpgsqlConnection conn, string key, string value, string by)
    {
        await conn.Cmd(
            @"INSERT INTO web_system_settings (setting_key, setting_value, updated_at, updated_by)
              VALUES (@k, @v, CURRENT_TIMESTAMP, @by)
              ON CONFLICT (setting_key) DO UPDATE SET setting_value=@v, updated_at=CURRENT_TIMESTAMP, updated_by=@by")
            .With("@k", key).With("@v", value).With("@by", by).ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Tạo bản chấm công ngoại tuyến CHỜ DUYỆT + tính các cờ rủi ro: lùi giờ (occurred so với lúc nhận),
    /// có ở LAN công ty không (IP riêng/khớp cấu hình), có trong geofence không (nếu đã cấu hình toạ độ).
    /// </summary>
    private static async Task CreateOfflinePendingAsync(
        NpgsqlConnection conn, HttpContext http, string username, string fullName,
        AttendanceDecision decision, double similarity, double quality, DateTime occurredAtUtc,
        double? gpsLat, double? gpsLng)
    {
        var nowUtc = DateTime.UtcNow;
        var backdateMinutes = Math.Max(0, (int)(nowUtc - occurredAtUtc).TotalMinutes);
        var ip = (http.Connection.RemoteIpAddress?.MapToIPv4() ?? http.Connection.RemoteIpAddress)?.ToString() ?? "";
        var onLan = IsPrivateIp(http.Connection.RemoteIpAddress);

        var maxBackdate = (int)(await GetSettingDoubleAsync(conn, CfgMaxBackdate) ?? DefaultMaxBackdateMinutes);
        var geoLat = await GetSettingDoubleAsync(conn, CfgGeofenceLat);
        var geoLng = await GetSettingDoubleAsync(conn, CfgGeofenceLng);
        var geoRadius = await GetSettingDoubleAsync(conn, CfgGeofenceRadius) ?? DefaultGeofenceRadiusM;

        double? distanceM = null;
        bool? inGeofence = null;
        if (geoLat is { } gla && geoLng is { } glo && gpsLat is { } pla && gpsLng is { } plo)
        {
            distanceM = HaversineMeters(gla, glo, pla, plo);
            inGeofence = distanceM <= geoRadius;
        }

        var flags = new List<string>();
        if (backdateMinutes > maxBackdate) flags.Add($"Lùi giờ {backdateMinutes} phút (> {maxBackdate})");
        if (!onLan) flags.Add("Không ở mạng LAN công ty");
        if (inGeofence == false) flags.Add($"Ngoài phạm vi công ty ({distanceM:0} m)");
        if (gpsLat is null || gpsLng is null) flags.Add("Không có vị trí GPS");
        if (!decision.ShouldRecord) flags.Add("Có thể trùng: đã có bản chấm công");

        await conn.Cmd(
            @"INSERT INTO cham_cong_offline
                (username, full_name, loai, similarity, quality, occurred_at, synced_at, backdate_minutes,
                 client_ip, on_company_lan, gps_lat, gps_lng, distance_m, in_geofence, flags, status)
              VALUES (@u, @fn, @loai, @sim, @q, @at, CURRENT_TIMESTAMP, @bd, @ip, @lan, @la, @lo, @dist, @inf, @flags, 'pending')")
            .With("@u", username).With("@fn", fullName).With("@loai", decision.Loai)
            .With("@sim", similarity).With("@q", quality).With("@at", occurredAtUtc)
            .With("@bd", backdateMinutes).With("@ip", ip).With("@lan", onLan)
            .With("@la", (object?)gpsLat ?? DBNull.Value).With("@lo", (object?)gpsLng ?? DBNull.Value)
            .With("@dist", (object?)distanceM ?? DBNull.Value).With("@inf", (object?)inGeofence ?? DBNull.Value)
            .With("@flags", string.Join("; ", flags))
            .ExecuteNonQueryAsync();
    }

    private static async Task<bool> GetSettingBoolAsync(NpgsqlConnection conn, string key, bool dflt)
    {
        var v = await conn.Cmd("SELECT setting_value FROM web_system_settings WHERE setting_key=@k LIMIT 1")
            .With("@k", key).ExecuteScalarAsync();
        return v is string s ? (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase)) : dflt;
    }

    private static async Task<double?> GetSettingDoubleAsync(NpgsqlConnection conn, string key)
    {
        var v = await conn.Cmd("SELECT setting_value FROM web_system_settings WHERE setting_key=@k LIMIT 1")
            .With("@k", key).ExecuteScalarAsync();
        return v is string s && double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>IP riêng (RFC1918/loopback/link-local) — coi như đang trong mạng LAN công ty.</summary>
    private static bool IsPrivateIp(System.Net.IPAddress? addr)
    {
        if (addr is null) return false;
        if (System.Net.IPAddress.IsLoopback(addr)) return true;
        var v4 = addr.MapToIPv4().GetAddressBytes();
        return v4[0] == 10
            || (v4[0] == 192 && v4[1] == 168)
            || (v4[0] == 172 && v4[1] >= 16 && v4[1] <= 31)
            || (v4[0] == 169 && v4[1] == 254); // link-local
    }

    /// <summary>Khoảng cách hai điểm GPS theo mét (công thức haversine).</summary>
    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371000;
        double ToRad(double d) => d * Math.PI / 180;
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return r * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>
    /// Tự học (adaptive enrollment): sau một lần chấm công ĐÃ qua liveness và khớp RẤT chắc,
    /// lưu thêm embedding làm mẫu để nhận diện bền hơn khi diện mạo/ánh sáng đổi dần.
    /// An toàn: chỉ học khi similarity ≥ ngưỡng tự học; tối đa <see cref="MaxFaceSamples"/> mẫu/người;
    /// mỗi người chỉ học 1 mẫu/ngày (tránh phình DB); KHÔNG bao giờ xóa mẫu admin đăng ký —
    /// khi đã đủ 5 mẫu thì chỉ thay mẫu TỰ HỌC cũ nhất (cuốn chiếu để bám diện mạo hiện tại).
    /// </summary>
    private static async Task TryAdaptiveLearnAsync(
        NpgsqlConnection conn, string username, string fullName, float[] embedding, double similarity, FieldCipher cipher)
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
            .With("@emb", cipher.EncryptEmbedding(embedding))
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
        => PayloadLimits.TryDecodeImage(b64, out bytes);

    /// <summary>
    /// Che họ tên khi trả về cho lời gọi ẨN DANH (kiosk): giữ tên gọi (từ cuối) để người chấm nhận ra
    /// mình, che các từ họ/đệm còn lại thành chữ cái đầu. "Nguyễn Văn An" → "N. V. An". Đủ phản hồi mà
    /// không lộ danh tính đầy đủ cho kẻ dò khuôn mặt.
    /// </summary>
    private static string MaskName(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return "•••";
        var parts = n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
            return parts[0].Length <= 2 ? parts[0] : parts[0][..1] + new string('•', parts[0].Length - 1);
        var given = parts[^1];
        var initials = string.Join(" ", parts[..^1].Select(p => char.ToUpperInvariant(p[0]) + "."));
        return $"{initials} {given}";
    }
}
