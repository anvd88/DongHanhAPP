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
    // Mọi thao tác có thể kích hoạt vector vào kho sinh trắc dùng cùng một khóa toàn cục để
    // duplicate-scan + insert là một miền tới hạn, không bị write-skew khi hai HR duyệt đồng thời.
    private const long FaceRegistryLockKey = 723974401234567890L;
    private static readonly HashSet<string> SelfEnrollPoseNames =
        new(["front", "side1", "side2", "up", "down"], StringComparer.OrdinalIgnoreCase);
    // Chỉ TỰ HỌC khi độ khớp cao hơn hẳn ngưỡng nhận diện để chắc chắn đúng người
    // → tránh "nhiễm" hồ sơ bằng một lần khớp sai. Tăng/giảm nếu cần chặt/lỏng hơn.
    private const double AdaptiveLearnMinSimilarity = 0.65;
    // Nhãn ở cột created_by để phân biệt mẫu hệ thống TỰ HỌC với mẫu admin đăng ký.
    private const string AutoLearnTag = "(tự học)";
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
    private const string CfgSmileEnabled = "attendance.smile.enabled";   // app hướng dẫn + server xác minh nụ cười
    private const string CfgSmileThreshold = "attendance.smile.threshold";

    // Liveness QUAY ĐẦU: cần người dùng chủ động quay đầu → để admin tự bật khi sẵn sàng (tránh phiền hà).
    private const bool DefaultMotionEnabled = false;
    private const bool DefaultMotionEnforce = false;
    private const bool DefaultSmileEnabled = false;
    private const double DefaultSmileThreshold = 0.65;
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

            -- Tự đăng ký trên app KHÔNG đi thẳng vào kho mẫu đã duyệt. Chỉ vector AES-GCM nằm tạm
            -- trong yêu cầu chờ HR đối chiếu trực tiếp danh tính; tuyệt đối không lưu ảnh camera.
            CREATE TABLE IF NOT EXISTS cham_cong_face_enrollments (
                id uuid PRIMARY KEY,
                username varchar(100) NOT NULL,
                full_name varchar(200) NOT NULL DEFAULT '',
                status varchar(20) NOT NULL DEFAULT 'pending'
                    CHECK (status IN ('pending', 'approved', 'rejected', 'expired')),
                sample_count integer NOT NULL DEFAULT 0,
                requested_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                expires_at timestamptz NOT NULL DEFAULT (CURRENT_TIMESTAMP + interval '14 days'),
                reviewed_by varchar(100) NOT NULL DEFAULT '',
                reviewed_at timestamptz NULL,
                review_note varchar(500) NOT NULL DEFAULT '',
                identity_verification_method varchar(40) NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS cham_cong_face_enrollment_samples (
                id bigserial PRIMARY KEY,
                request_id uuid NOT NULL REFERENCES cham_cong_face_enrollments(id) ON DELETE CASCADE,
                pose varchar(20) NOT NULL,
                embedding bytea NOT NULL
                    CHECK (substring(embedding FROM 1 FOR 4) = '\x4b4d4531'::bytea),
                quality double precision NOT NULL DEFAULT 0,
                liveness double precision NOT NULL DEFAULT 0,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE (request_id, pose)
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_cham_cong_face_enrollments_pending_user
                ON cham_cong_face_enrollments (lower(username)) WHERE status='pending';
            CREATE INDEX IF NOT EXISTS ix_cham_cong_face_enrollments_status_time
                ON cham_cong_face_enrollments (status, requested_at DESC);
            CREATE INDEX IF NOT EXISTS ix_cham_cong_face_enrollment_samples_request
                ON cham_cong_face_enrollment_samples (request_id);
            """).ExecuteNonQueryAsync();

        // Hết hạn thì xóa ngay vector sinh trắc tạm; chỉ giữ metadata yêu cầu để audit.
        await conn.Cmd("""
            UPDATE cham_cong_face_enrollments
               SET status='expired', reviewed_at=CURRENT_TIMESTAMP,
                   review_note='Tự động hết hạn sau 14 ngày.'
             WHERE status='pending' AND expires_at <= CURRENT_TIMESTAMP;
            DELETE FROM cham_cong_face_enrollment_samples s
             USING cham_cong_face_enrollments r
             WHERE s.request_id=r.id AND r.status='expired';
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

        // Không tự xóa blob có độ dài lạ ở đây. Một ciphertext KME1 bị hỏng vài byte đầu cũng trông
        // giống "mẫu cũ chưa mã hóa"; xóa trước bước xác thực AES-GCM sẽ làm mất bằng chứng/dữ liệu và
        // cho Production khởi động nhầm. EncryptExistingEmbeddings bên dưới sẽ kiểm tra trong transaction
        // và dừng an toàn nếu gặp mẫu không thể xác định/không hợp lệ.

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
        await using var tx = await conn.BeginTransactionAsync(ct);

        var pending = new List<(long Id, byte[] Bytes)>();
        await using (var r = await conn.Cmd(
            @"SELECT id, embedding FROM cham_cong_face
              WHERE substring(embedding FROM 1 FOR 4) <> '\x4b4d4531'::bytea", tx).ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
                if (r["embedding"] is byte[] bytes && bytes.Length > 0)
                    pending.Add((r.Long("id"), bytes));
        }

        foreach (var (id, bytes) in pending)
        {
            try
            {
                ValidatePlaintextEmbeddingForMigration(bytes, id);
                await conn.Cmd("UPDATE cham_cong_face SET embedding=@e WHERE id=@id", tx)
                    .With("@e", cipher.Encrypt(bytes)).With("@id", id).ExecuteNonQueryAsync(ct);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
            }
        }

        // Không chỉ nhìn magic KME1: thử xác thực AES-GCM của TOÀN BỘ kho active + staging bằng
        // khóa hiện hành. Sai/đổi khóa hoặc blob hỏng phải làm Production dừng trước khi nhận request,
        // thay vì để lượt chấm công đầu tiên văng 500 giữa vòng quét.
        await using (var r = await conn.Cmd(
            @"SELECT 'active' AS source, id::text AS item_id, embedding FROM cham_cong_face
              UNION ALL
              SELECT 'staging' AS source, request_id::text || ':' || id::text AS item_id, embedding
                FROM cham_cong_face_enrollment_samples", tx).ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                ValidateEncryptedEmbedding(cipher, (byte[])r["embedding"],
                    r.Str("source"), r.Str("item_id"));
            }
        }

        // Chặn cả thao tác import/SQL ghi plaintext SAU lúc startup. Constraint chỉ được thêm sau khi
        // mọi mẫu cũ hợp lệ đã được mã hóa trong cùng transaction, nên không có cửa sổ kho trộn lẫn.
        await conn.Cmd("""
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                     WHERE conname = 'ck_cham_cong_face_embedding_kme1'
                       AND conrelid = 'cham_cong_face'::regclass
                ) THEN
                    ALTER TABLE cham_cong_face
                        ADD CONSTRAINT ck_cham_cong_face_embedding_kme1
                        CHECK (substring(embedding FROM 1 FOR 4) = '\x4b4d4531'::bytea);
                END IF;
            END $$;
            """, tx).ExecuteNonQueryAsync(ct);

        await tx.CommitAsync(ct);
    }

    internal static void ValidatePlaintextEmbeddingForMigration(byte[] stored, long itemId)
    {
        if (stored.Length != 512 * sizeof(float))
            throw new InvalidOperationException(
                $"Unencrypted biometric active item {itemId} has an unexpected length; migration was aborted.");

        var decoded = EmbeddingCodec.FromBytes(stored);
        try
        {
            if (decoded.Any(v => !float.IsFinite(v)))
                throw new InvalidOperationException(
                    $"Unencrypted biometric active item {itemId} has an invalid embedding; migration was aborted.");
        }
        finally
        {
            Array.Clear(decoded);
        }
    }

    internal static void ValidateEncryptedEmbedding(
        FieldCipher cipher, byte[] stored, string source, string itemId)
    {
        if (!FieldCipher.IsEncrypted(stored))
            throw new InvalidOperationException(
                $"Biometric {source} item {itemId} is not AES-GCM encrypted.");

        float[]? decoded = null;
        try
        {
            decoded = cipher.DecryptEmbedding(stored);
            if (decoded.Length != 512 || decoded.Any(v => !float.IsFinite(v)))
                throw new InvalidOperationException(
                    $"Biometric {source} item {itemId} has an invalid embedding.");
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            throw new InvalidOperationException(
                $"Cannot authenticate biometric {source} item {itemId} with the configured key.", ex);
        }
        finally
        {
            if (decoded is not null) Array.Clear(decoded);
        }
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

        // Cấu hình yêu cầu CƯỜI. App dùng để hướng dẫn/lấy ảnh đúng thời điểm; server vẫn tự đo lại
        // nụ cười từ ảnh cùng với nhận diện danh tính, liveness và ghi công.
        g.MapGet("/smile-config", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            return Results.Ok(new SmileConfigDto(
                await GetSettingBoolAsync(conn, CfgSmileEnabled, DefaultSmileEnabled),
                await GetSettingDoubleAsync(conn, CfgSmileThreshold) ?? DefaultSmileThreshold));
        });

        // QR dự phòng do HR/công trình cấp. Token là chuỗi ngẫu nhiên, có thể thu hồi bằng cách tắt địa điểm.
        g.MapPost("/qr", async (QrAttendanceRequest req, ClaimsPrincipal u, Database db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Token)) return Results.BadRequest(new { message = "Mã QR trống." });
            await using var conn = await db.OpenAsync();
            var username = u.Username();
            if (await PayrollEndpoints.ReadPendingPayslipRequirement(conn, username, overdueOnly: true) is { } overdue)
            {
                return Results.Ok(PayslipAcknowledgementRequired(overdue));
            }
            string site = "", project = "";
            await using (var r = await conn.Cmd("SELECT name, project_name FROM cham_cong_qr_sites WHERE qr_token=@t AND active=TRUE")
                .With("@t", req.Token.Trim()).ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) return Results.BadRequest(new { message = "Mã QR không hợp lệ hoặc đã bị thu hồi." });
                site = r.Str("name"); project = r.Str("project_name");
            }
            var fullName = await conn.Cmd("SELECT full_name FROM hr_employees WHERE username=@u LIMIT 1")
                .With("@u", username).ExecuteScalarAsync() as string ?? username;
            var decision = await AttendancePolicy.DecideAsync(conn, username, fullName);
            if (!decision.ShouldRecord)
                return Results.Ok(new ChamCongResult("ok", true, username, fullName, 1, decision.Loai,
                    decision.ExistingAt, 1, decision.Message, null));
            var loai = decision.Loai;
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

        g.MapPut("/smile-config", async (SmileConfigDto cfg, ClaimsPrincipal u, Database db) =>
        {
            var threshold = Math.Clamp(cfg.Threshold, 0.35, 0.95);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            await using var conn = await db.OpenAsync();
            await SetSettingAsync(conn, CfgSmileEnabled, cfg.Enabled ? "1" : "0", u.Username());
            await SetSettingAsync(conn, CfgSmileThreshold, threshold.ToString(inv), u.Username());
            await db.RecordAudit(u.Username(), "Cấu hình yêu cầu cười", "ChamCong", "",
                $"smile enabled={cfg.Enabled} threshold={threshold.ToString(inv)}");
            return Results.Ok(new { message = "Đã lưu yêu cầu cười khi chấm công (áp dụng ngay)." });
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
            if (!FaceAntiSpoofSecurity.IsOperational(engine))
                return Results.Json(new { message = "Hệ thống chống giả mạo đang không khả dụng; không thể đăng ký an toàn." }, statusCode: 503);
            if (!cipher.Enabled)
                return Results.Json(new { message = "Máy chủ chưa bật mã hóa dữ liệu sinh trắc; không thể đăng ký an toàn." }, statusCode: 503);
            if (string.IsNullOrWhiteSpace(req.Username))
                return Results.BadRequest(new { message = "Thiếu tên đăng nhập nhân viên." });

            if (!TryDecodeImage(req.ImageBase64, out var bytes))
                return Results.BadRequest(new { message = "Ảnh không hợp lệ." });
            if (engine.AssessFrame(bytes) is not { FaceFound: true } quality || quality.Score < MinFrameQuality)
                return Results.BadRequest(new { message = "Không phát hiện được khuôn mặt đủ rõ. Hãy chụp lại ở nơi đủ sáng." });
            if (FaceAntiSpoofSecurity.ProbabilityReal(engine, bytes) < engine.LivenessThreshold)
                return Results.BadRequest(new { message = "Nghi ngờ giả mạo. Nhân viên phải có mặt trực tiếp, không dùng ảnh hoặc màn hình." });
            var emb = engine.ExtractEmbedding(bytes);
            if (emb is null)
                return Results.BadRequest(new { message = "Không phát hiện được khuôn mặt trong ảnh. Hãy chụp lại rõ hơn." });

            await using var conn = await db.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await conn.Cmd("SELECT pg_advisory_xact_lock(@key)", tx)
                .With("@key", FaceRegistryLockKey).ExecuteScalarAsync();

            var username = req.Username.Trim();
            string fullName = "", approval = "";
            bool active = false, deleted = true;
            await using (var reader = await conn.Cmd(
                @"SELECT full_name, is_active, COALESCE(is_deleted,FALSE) AS is_deleted, approval_status
                    FROM app_users WHERE lower(username)=lower(@u) FOR UPDATE", tx)
                .With("@u", username).ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                    return Results.NotFound(new { message = "Không tìm thấy tài khoản nhân viên." });
                fullName = reader.Str("full_name");
                active = reader.Bool("is_active");
                deleted = reader.Bool("is_deleted");
                approval = reader.Str("approval_status");
            }
            if (!active || deleted || approval != "Approved")
                return Results.Conflict(new { message = "Tài khoản đang bị khóa, đã xóa hoặc chưa được duyệt." });

            var sampleCount = Convert.ToInt32(await conn.Cmd(
                "SELECT COUNT(*) FROM cham_cong_face WHERE lower(username)=lower(@u)", tx)
                .With("@u", username).ExecuteScalarAsync());
            if (sampleCount >= MaxFaceSamples)
                return Results.Conflict(new { message = $"Tài khoản đã đủ tối đa {MaxFaceSamples} mẫu khuôn mặt." });

            var otherEmbeddings = new List<float[]>();
            await using (var reader = await conn.Cmd(
                "SELECT embedding FROM cham_cong_face WHERE lower(username)<>lower(@u)", tx)
                .With("@u", username).ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    otherEmbeddings.Add(cipher.DecryptEmbedding((byte[])reader["embedding"]));
            }
            var duplicateThreshold = Math.Max(0.60, engine.MatchThreshold + 0.10);
            if (otherEmbeddings.Any(other => engine.Compare(emb, other) >= duplicateThreshold))
                return Results.Conflict(new { message = "Khuôn mặt trùng mạnh với một tài khoản khác. Không thể đăng ký." });

            var encrypted = cipher.EncryptEmbedding(emb);
            if (!FieldCipher.IsEncrypted(encrypted))
                return Results.Json(new { message = "Không thể mã hóa vector khuôn mặt; đăng ký đã bị khóa an toàn." }, statusCode: 503);
            // CHỈ lưu vector đặc trưng, KHÔNG lưu ảnh gốc khuôn mặt (riêng tư + gọn DB). Ảnh chỉ dùng
            // tạm để trích embedding rồi bỏ; nhận diện về sau chỉ cần vector (cột anh để mặc định NULL).
            await conn.Cmd(
                @"INSERT INTO cham_cong_face (username, full_name, embedding, created_at, created_by)
                  VALUES (@u, @fn, @emb, CURRENT_TIMESTAMP, @by)", tx)
                .With("@u", username)
                .With("@fn", fullName)
                .With("@emb", encrypted)
                .With("@by", u.Username())
                .ExecuteNonQueryAsync();
            await tx.CommitAsync();

            await db.RecordAudit(u.Username(), "Đăng ký khuôn mặt", "ChamCong", username,
                "HR đăng ký trực tiếp: PAD cùng khung đạt, vector AES-GCM, đã kiểm tra trùng.");
            return Results.Ok(new { message = "Đã lưu mẫu khuôn mặt." });
        }).RequirePermission(Permissions.AttendanceManage);

        // ── Tự đăng ký khuôn mặt (nhân viên tự làm trên app) ────────────────────────
        // Trạng thái của CHÍNH tài khoản: phân biệt mẫu đã kích hoạt với yêu cầu đang chờ HR duyệt.
        g.MapGet("/dangky/cua-toi", async (ClaimsPrincipal u, Database db) =>
        {
            var me = u.Username();
            if (string.IsNullOrWhiteSpace(me)) return Results.Unauthorized();

            await using var conn = await db.OpenAsync();
            await ExpireFaceEnrollmentsAsync(conn);
            var count = 0;
            DateTime? first = null;
            await using var r = await conn.Cmd(
                "SELECT COUNT(*) AS c, MIN(created_at) AS f FROM cham_cong_face WHERE username=@u")
                .With("@u", me).ExecuteReaderAsync();
            if (await r.ReadAsync()) { count = r.Int("c"); first = r.DtNull("f"); }

            if (count > 0)
                return Results.Ok(new SelfFaceStatusDto(true, count, first, RequestStatus: "registered"));

            Guid? requestId = null;
            string? status = null, note = null;
            DateTime? requestedAt = null;
            await using var pending = await conn.Cmd(
                @"SELECT id, status, requested_at, review_note
                    FROM cham_cong_face_enrollments
                   WHERE lower(username)=lower(@u)
                   ORDER BY requested_at DESC LIMIT 1")
                .With("@u", me).ExecuteReaderAsync();
            if (await pending.ReadAsync())
            {
                requestId = pending.GetGuid(pending.GetOrdinal("id"));
                status = pending.Str("status");
                requestedAt = pending.Dt("requested_at");
                note = pending.Str("review_note");
            }
            return Results.Ok(new SelfFaceStatusDto(false, 0, null,
                string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase),
                requestId, status ?? "not_enrolled", requestedAt, note));
        });

        // Tự đăng ký khuôn mặt: quét 5 góc (thẳng, hai bên, ngẩng, cúi), mỗi góc là một loạt ảnh.
        // Server kiểm tra chất lượng/PAD rồi chỉ lưu vector AES-GCM vào staging. HR phải đối chiếu trực tiếp
        // danh tính và duyệt thì vector mới được chuyển nguyên tử vào cham_cong_face.
        g.MapPost("/dangky/tu", async (SelfFaceEnrollRequest req, ClaimsPrincipal u, Database db, IFaceEngine engine, FieldCipher cipher, PushService push) =>
        {
            if (!FaceAntiSpoofSecurity.IsOperational(engine))
                return Results.Json(new { message = "Hệ thống chống giả mạo đang không khả dụng. Đăng ký khuôn mặt đã được khóa an toàn; vui lòng báo quản trị viên." }, statusCode: 503);
            if (!cipher.Enabled)
                return Results.Json(new { message = "Máy chủ chưa bật mã hóa dữ liệu sinh trắc. Đăng ký khuôn mặt đã được khóa an toàn; vui lòng báo quản trị viên." }, statusCode: 503);
            var me = u.Username();
            if (string.IsNullOrWhiteSpace(me)) return Results.Unauthorized();
            if (req?.Poses is null || req.Poses.Count == 0)
                return Results.BadRequest(new { message = "Thiếu ảnh khuôn mặt." });
            if (req.Poses.Sum(p => p?.Images?.Count ?? 0) > PayloadLimits.MaxImagesPerEnrollRequest)
                return Results.BadRequest(new { message = $"Tối đa {PayloadLimits.MaxImagesPerEnrollRequest} ảnh mỗi yêu cầu." });
            if (req.Poses.SelectMany(p => p?.Images ?? []).Any(img => !PayloadLimits.TryDecodeImage(img, out _)))
                return Results.BadRequest(new { message = $"Mỗi ảnh phải nhỏ hơn {PayloadLimits.MaxImageBytes / 1024 / 1024} MB." });

            await using var conn = await db.OpenAsync();
            await ExpireFaceEnrollmentsAsync(conn);

            var existing = Convert.ToInt32(await conn.Cmd(
                "SELECT COUNT(*) FROM cham_cong_face WHERE username=@u").With("@u", me).ExecuteScalarAsync());
            if (existing > 0)
                return Results.BadRequest(new { message = "Bạn đã đăng ký khuôn mặt rồi. Mỗi tài khoản chỉ được đăng ký một lần." });
            var alreadyPending = Convert.ToInt32(await conn.Cmd(
                "SELECT COUNT(*) FROM cham_cong_face_enrollments WHERE lower(username)=lower(@u) AND status='pending'")
                .With("@u", me).ExecuteScalarAsync());
            if (alreadyPending > 0)
                return Results.Conflict(new { message = "Yêu cầu đăng ký khuôn mặt của bạn đang chờ HR xác minh và duyệt." });

            var fullName = u.FindFirstValue("fullName") ?? "";

            // Xử lý từng góc. Chỉ khung TỰ NÓ vượt PAD mới được dùng để trích embedding;
            // không được lấy liveness của khung A rồi lấy danh tính từ khung B.
            var samples = new List<(string Pose, float[] Embedding, double Quality, double Liveness, FacePose DetectedPose)>();
            var frontOk = false;
            // Chỉ nhận đúng 5 góc nghiệp vụ, bỏ tên lạ/trùng để một request không thể vượt trần mẫu.
            var poses = req.Poses
                .Where(p => p is not null && SelfEnrollPoseNames.Contains(p.Pose?.Trim() ?? ""))
                .DistinctBy(p => p.Pose.Trim(), StringComparer.OrdinalIgnoreCase)
                .Take(MaxFaceSamples)
                .ToList();
            foreach (var pose in poses)
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

                // Chất lượng tối thiểu (nới nhẹ cho góc nghiêng vì mặt nhỏ hơn/khó nét hơn chính diện).
                var minQuality = isFront ? MinFrameQuality : MinFrameQuality * 0.8;
                var eligible = candidates
                    .Where(c => c.Q.Score >= minQuality && SelfEnrollPoseMatches(pose.Pose, c.Q.Pose))
                    .Take(5)
                    .ToList();
                if (eligible.Count == 0) continue;

                (byte[] Bytes, FaceFrameQuality Q, double Liveness)? selected = null;
                foreach (var c in eligible)
                {
                    var liveness = FaceAntiSpoofSecurity.ProbabilityReal(engine, c.Bytes);
                    if (liveness < engine.LivenessThreshold) continue;
                    selected = (c.Bytes, c.Q, liveness);
                    break;
                }
                if (selected is null)
                    return Results.BadRequest(new { message = "Nghi ngờ giả mạo (không phải người thật). Hãy nhìn trực tiếp vào camera, không dùng ảnh/màn hình." });

                var accepted = selected.Value;
                var emb = engine.ExtractEmbedding(accepted.Bytes);
                if (emb is null) continue;
                samples.Add((pose.Pose.Trim().ToLowerInvariant(), emb, accepted.Q.Score,
                    accepted.Liveness, accepted.Q.Pose));
                if (isFront) frontOk = true;
            }

            if (!frontOk)
                return Results.BadRequest(new { message = "Chưa lấy được ảnh chính diện rõ nét. Hãy đăng ký lại ở nơi đủ sáng và nhìn thẳng vào camera." });
            if (samples.Count < 3)
                return Results.BadRequest(new { message = "Chưa đủ ít nhất 3 góc khuôn mặt hợp lệ. Vui lòng quét lại theo đúng hướng dẫn." });

            var side1 = samples.FirstOrDefault(s => s.Pose == "side1");
            var side2 = samples.FirstOrDefault(s => s.Pose == "side2");
            if (side1.Embedding is not null && side2.Embedding is not null
                && Math.Sign(side1.DetectedPose.Yaw) == Math.Sign(side2.DetectedPose.Yaw))
                return Results.BadRequest(new { message = "Hai góc quay phải ở hai hướng đối diện. Vui lòng quay sang bên còn lại và quét lại." });

            // Không cho một request trộn khuôn mặt của nhiều người ở các góc khác nhau.
            var frontEmbedding = samples.First(s => s.Pose == "front").Embedding;
            var consistencyThreshold = Math.Max(0.33, engine.MatchThreshold - 0.12);
            if (samples.Any(s => s.Pose != "front" && engine.Compare(frontEmbedding, s.Embedding) < consistencyThreshold))
                return Results.BadRequest(new { message = "Các góc quét không cùng một khuôn mặt. Vui lòng chỉ một mình bạn ở trước camera và quét lại." });

            var requestId = Guid.NewGuid();
            await using var tx = await conn.BeginTransactionAsync();
            // Khóa theo username để hai lần bấm/gửi đồng thời không tạo hai yêu cầu.
            await conn.Cmd("SELECT pg_advisory_xact_lock(hashtext(@key))", tx)
                .With("@key", "face-enrollment:" + me.ToLowerInvariant()).ExecuteScalarAsync();
            await ExpireFaceEnrollmentsAsync(conn, tx);

            var existing2 = Convert.ToInt32(await conn.Cmd(
                "SELECT COUNT(*) FROM cham_cong_face WHERE lower(username)=lower(@u)", tx)
                .With("@u", me).ExecuteScalarAsync());
            if (existing2 > 0)
            {
                await tx.RollbackAsync();
                return Results.BadRequest(new { message = "Bạn đã đăng ký khuôn mặt rồi." });
            }
            var pending2 = Convert.ToInt32(await conn.Cmd(
                "SELECT COUNT(*) FROM cham_cong_face_enrollments WHERE lower(username)=lower(@u) AND status='pending'", tx)
                .With("@u", me).ExecuteScalarAsync());
            if (pending2 > 0)
            {
                await tx.RollbackAsync();
                return Results.Conflict(new { message = "Yêu cầu đăng ký khuôn mặt của bạn đang chờ HR xác minh và duyệt." });
            }

            var dbFullName = await conn.Cmd(
                @"SELECT full_name FROM app_users
                   WHERE lower(username)=lower(@u) AND is_active=TRUE
                     AND COALESCE(is_deleted,FALSE)=FALSE AND approval_status='Approved'
                   LIMIT 1 FOR UPDATE", tx)
                .With("@u", me).ExecuteScalarAsync() as string;
            if (dbFullName is null)
            {
                await tx.RollbackAsync();
                return Results.Conflict(new { message = "Tài khoản đang bị khóa, đã xóa hoặc chưa được duyệt." });
            }
            if (!string.IsNullOrWhiteSpace(dbFullName)) fullName = dbFullName;

            await conn.Cmd(
                @"INSERT INTO cham_cong_face_enrollments
                    (id, username, full_name, status, sample_count, requested_at, expires_at)
                  VALUES (@id, @u, @fn, 'pending', @count, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP + interval '14 days')", tx)
                .With("@id", requestId).With("@u", me).With("@fn", fullName).With("@count", samples.Count)
                .ExecuteNonQueryAsync();

            foreach (var sample in samples)
            {
                var encrypted = cipher.EncryptEmbedding(sample.Embedding);
                if (!FieldCipher.IsEncrypted(encrypted))
                    throw new InvalidOperationException("Face enrollment staging requires AES-GCM encryption.");
                await conn.Cmd(
                    @"INSERT INTO cham_cong_face_enrollment_samples
                        (request_id, pose, embedding, quality, liveness)
                      VALUES (@id, @pose, @emb, @quality, @live)", tx)
                    .With("@id", requestId).With("@pose", sample.Pose).With("@emb", encrypted)
                    .With("@quality", sample.Quality).With("@live", sample.Liveness)
                    .ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();

            await db.RecordAudit(me, "Gửi yêu cầu đăng ký khuôn mặt", "ChamCong", me,
                $"Yêu cầu {requestId} · {samples.Count} vector đã mã hóa · chờ HR đối chiếu trực tiếp.");
            await push.SendToAdminsAsync("Yêu cầu khuôn mặt chờ duyệt",
                $"{fullName} ({me}) đã gửi yêu cầu đăng ký khuôn mặt.", $"face-enroll:{requestId}", "Attendance");
            return Results.Json(new SelfFaceEnrollResult(
                "Đã gửi yêu cầu. HR cần đối chiếu trực tiếp danh tính trước khi kích hoạt khuôn mặt.",
                samples.Count, "pending", requestId), statusCode: StatusCodes.Status202Accepted);
        });

        // Danh sách yêu cầu sinh trắc: chỉ HR/Admin có quyền quản lý chấm công. Không endpoint nào trả
        // ảnh hoặc embedding; người duyệt bắt buộc đối chiếu nhân viên trực tiếp ngoài hệ thống.
        g.MapGet("/face-enrollments", async (Database db, string? status) =>
        {
            var filter = (status ?? "pending").Trim().ToLowerInvariant();
            if (filter is not ("pending" or "approved" or "rejected" or "expired" or "all"))
                return Results.BadRequest(new { message = "Trạng thái không hợp lệ." });
            await using var conn = await db.OpenAsync();
            await ExpireFaceEnrollmentsAsync(conn);
            var cmd = conn.Cmd(
                @"SELECT r.id, r.username, r.full_name, r.status, r.requested_at, r.expires_at,
                         r.reviewed_by, r.reviewed_at, r.review_note, r.identity_verification_method,
                         r.sample_count
                    FROM cham_cong_face_enrollments r
                   WHERE (@status='all' OR r.status=@status)
                   ORDER BY CASE WHEN r.status='pending' THEN 0 ELSE 1 END, r.requested_at DESC
                   LIMIT 500")
                .With("@status", filter);
            var rows = new List<FaceEnrollmentRequestDto>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                rows.Add(new FaceEnrollmentRequestDto(
                    reader.GetGuid(reader.GetOrdinal("id")), reader.Str("username"), reader.Str("full_name"),
                    reader.Str("status"), reader.Int("sample_count"), reader.Dt("requested_at"),
                    reader.Dt("expires_at"), reader.Str("reviewed_by"), reader.DtNull("reviewed_at"),
                    reader.Str("review_note"), reader.Str("identity_verification_method")));
            return Results.Ok(rows);
        }).RequirePermission(Permissions.AttendanceManage);

        g.MapPost("/face-enrollments/{id:guid}/approve", async (Guid id, FaceEnrollmentApproveRequest req,
            ClaimsPrincipal u, Database db, IFaceEngine engine, FieldCipher cipher, PushService push) =>
        {
            var actor = u.Username();
            var method = (req?.VerificationMethod ?? "").Trim().ToLowerInvariant();
            if (req?.IdentityVerified != true || method != "in_person")
                return Results.BadRequest(new { message = "Chỉ được duyệt sau khi đã đối chiếu trực tiếp người đăng ký. Hãy xác nhận phương thức in_person." });
            if (!cipher.Enabled)
                return Results.Json(new { message = "Máy chủ thiếu khóa mã hóa dữ liệu sinh trắc; không thể kích hoạt an toàn." }, statusCode: 503);
            if (!FaceAntiSpoofSecurity.IsOperational(engine))
                return Results.Json(new { message = "Hệ thống chống giả mạo đang không khả dụng; không thể xác minh trực tiếp an toàn." }, statusCode: 503);
            var note = (req.Note ?? "").Trim();
            if (note.Length > 500) return Results.BadRequest(new { message = "Ghi chú tối đa 500 ký tự." });
            if (req.VerificationImages is null || req.VerificationImages.Count is < 2 or > 3)
                return Results.BadRequest(new { message = "Cần chụp trực tiếp từ 2 đến 3 ảnh xác minh khi nhân viên có mặt cùng HR." });
            var verificationFrames = new List<byte[]>();
            foreach (var image in req.VerificationImages)
            {
                if (!TryDecodeImage(image, out var bytes))
                    return Results.BadRequest(new { message = "Ảnh xác minh trực tiếp không hợp lệ hoặc vượt giới hạn dung lượng." });
                verificationFrames.Add(bytes);
            }
            if (verificationFrames
                .Select(f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(f)))
                .Distinct(StringComparer.Ordinal)
                .Count() < 2)
                return Results.BadRequest(new { message = "Hai ảnh xác minh phải là hai khung chụp trực tiếp khác nhau." });

            await using var conn = await db.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            // Đọc username bất biến trước để có thể khóa theo thứ tự: registry → app_user → request.
            var requestedUsername = await conn.Cmd(
                "SELECT username FROM cham_cong_face_enrollments WHERE id=@id", tx)
                .With("@id", id).ExecuteScalarAsync() as string;
            if (string.IsNullOrWhiteSpace(requestedUsername)) return Results.NotFound();

            await conn.Cmd("SELECT pg_advisory_xact_lock(@key)", tx)
                .With("@key", FaceRegistryLockKey).ExecuteScalarAsync();

            string accountName = "", accountApproval = "";
            bool accountActive = false, accountDeleted = true;
            await using (var reader = await conn.Cmd(
                @"SELECT full_name, is_active, COALESCE(is_deleted,FALSE) AS is_deleted, approval_status
                    FROM app_users WHERE lower(username)=lower(@u) FOR UPDATE", tx)
                .With("@u", requestedUsername).ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                    return Results.Conflict(new { message = "Tài khoản đăng ký không còn tồn tại." });
                accountName = reader.Str("full_name");
                accountActive = reader.Bool("is_active");
                accountDeleted = reader.Bool("is_deleted");
                accountApproval = reader.Str("approval_status");
            }
            if (!accountActive || accountDeleted || accountApproval != "Approved")
                return Results.Conflict(new { message = "Tài khoản đăng ký đang bị khóa, đã xóa hoặc chưa được duyệt." });

            string username = "", fullName = "", currentStatus = "";
            DateTime expiresAt = default;
            await using (var reader = await conn.Cmd(
                @"SELECT username, full_name, status, expires_at
                    FROM cham_cong_face_enrollments WHERE id=@id FOR UPDATE", tx)
                .With("@id", id).ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync()) return Results.NotFound();
                username = reader.Str("username"); fullName = reader.Str("full_name");
                currentStatus = reader.Str("status"); expiresAt = reader.Dt("expires_at");
            }
            if (!string.Equals(username, requestedUsername, StringComparison.OrdinalIgnoreCase))
                return Results.Conflict(new { message = "Yêu cầu đăng ký đã thay đổi không hợp lệ." });
            if (!string.IsNullOrWhiteSpace(accountName)) fullName = accountName;
            if (!string.Equals(currentStatus, "pending", StringComparison.OrdinalIgnoreCase))
                return Results.Conflict(new { message = "Yêu cầu này đã được xử lý." });
            if (expiresAt <= DateTime.UtcNow)
            {
                await conn.Cmd("UPDATE cham_cong_face_enrollments SET status='expired', reviewed_at=CURRENT_TIMESTAMP, review_note='Tự động hết hạn sau 14 ngày.' WHERE id=@id", tx)
                    .With("@id", id).ExecuteNonQueryAsync();
                await conn.Cmd("DELETE FROM cham_cong_face_enrollment_samples WHERE request_id=@id", tx)
                    .With("@id", id).ExecuteNonQueryAsync();
                await tx.CommitAsync();
                return Results.Conflict(new { message = "Yêu cầu đã hết hạn; nhân viên cần đăng ký lại." });
            }
            if (string.Equals(actor, username, StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { message = "Không được tự duyệt yêu cầu khuôn mặt của chính mình." }, statusCode: 403);

            var staged = new List<(string Pose, byte[] Encrypted, float[] Embedding)>();
            await using (var reader = await conn.Cmd(
                "SELECT pose, embedding FROM cham_cong_face_enrollment_samples WHERE request_id=@id ORDER BY id", tx)
                .With("@id", id).ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var encrypted = (byte[])reader["embedding"];
                    if (!FieldCipher.IsEncrypted(encrypted))
                        return Results.Json(new { message = "Vector staging không được mã hóa đúng chuẩn; đã khóa duyệt an toàn." }, statusCode: 503);
                    staged.Add((reader.Str("pose"), encrypted, cipher.DecryptEmbedding(encrypted)));
                }
            }
            if (staged.Count < 3 || staged.All(s => s.Pose != "front"))
                return Results.Conflict(new { message = "Yêu cầu không còn đủ mẫu hợp lệ; nhân viên cần đăng ký lại." });

            // Xác minh trực tiếp tại thời điểm duyệt: embedding chỉ được trích từ chính từng frame đã
            // vượt PAD. Ảnh và probe chỉ sống trong bộ nhớ của request này, không ghi DB/log.
            var liveProbes = new List<float[]>();
            foreach (var frame in verificationFrames)
            {
                if (engine.AssessFrame(frame) is not { FaceFound: true } quality
                    || quality.Score < MinFrameQuality
                    || CheckPosture(quality.Pose) is not null)
                    continue;
                if (FaceAntiSpoofSecurity.ProbabilityReal(engine, frame) < engine.LivenessThreshold)
                    continue;
                if (engine.ExtractEmbedding(frame) is { } probe) liveProbes.Add(probe);
            }
            if (liveProbes.Count < 2)
                return Results.BadRequest(new { message = "Chưa có đủ 2 ảnh chính diện vượt kiểm tra người thật. Hãy chụp lại khi nhân viên đang có mặt." });

            var verificationConsistency = Math.Max(0.33, engine.MatchThreshold - 0.12);
            if (liveProbes.Skip(1).Any(p => engine.Compare(liveProbes[0], p) < verificationConsistency))
                return Results.BadRequest(new { message = "Các ảnh xác minh trực tiếp không cùng một người. Không thể duyệt." });
            var stagedFront = staged.Where(s => s.Pose == "front").Select(s => s.Embedding).ToList();
            if (liveProbes.Any(p => stagedFront.All(s => engine.Compare(p, s) < engine.MatchThreshold)))
                return Results.Conflict(new { message = "Người đang được HR xác minh không khớp với yêu cầu đã gửi. Không thể kích hoạt khuôn mặt." });

            var targetHasFace = Convert.ToInt32(await conn.Cmd(
                "SELECT COUNT(*) FROM cham_cong_face WHERE lower(username)=lower(@u)", tx)
                .With("@u", username).ExecuteScalarAsync());
            if (targetHasFace > 0)
                return Results.Conflict(new { message = "Tài khoản đã có mẫu khuôn mặt được kích hoạt." });

            // Chặn cùng một khuôn mặt bị gắn mạnh vào tài khoản khác. Dùng ngưỡng cao để tránh từ chối
            // nhầm; bước đối chiếu trực tiếp của HR vẫn là kiểm soát danh tính bắt buộc.
            var active = new List<float[]>();
            await using (var reader = await conn.Cmd(
                "SELECT embedding FROM cham_cong_face WHERE lower(username)<>lower(@u)", tx)
                .With("@u", username).ExecuteReaderAsync())
            {
                while (await reader.ReadAsync()) active.Add(cipher.DecryptEmbedding((byte[])reader["embedding"]));
            }
            var duplicateThreshold = Math.Max(0.60, engine.MatchThreshold + 0.10);
            if (staged.Any(s => active.Any(a => engine.Compare(s.Embedding, a) >= duplicateThreshold)))
                return Results.Conflict(new { message = "Khuôn mặt trùng mạnh với một hồ sơ khác. Không thể kích hoạt; HR cần kiểm tra tài khoản." });

            await conn.Cmd(
                @"INSERT INTO cham_cong_face (username, full_name, embedding, created_at, created_by)
                  SELECT r.username, @fn, s.embedding, CURRENT_TIMESTAMP, @by
                    FROM cham_cong_face_enrollments r
                    JOIN cham_cong_face_enrollment_samples s ON s.request_id=r.id
                   WHERE r.id=@id AND r.status='pending'", tx)
                .With("@by", actor).With("@fn", fullName).With("@id", id).ExecuteNonQueryAsync();
            await conn.Cmd(
                @"UPDATE cham_cong_face_enrollments
                     SET status='approved', reviewed_by=@by, reviewed_at=CURRENT_TIMESTAMP,
                         review_note=@note, identity_verification_method='in_person'
                   WHERE id=@id AND status='pending'", tx)
                .With("@by", actor)
                .With("@note", note.Length > 0 ? note : "Đã đối chiếu trực tiếp danh tính nhân viên.")
                .With("@id", id).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM cham_cong_face_enrollment_samples WHERE request_id=@id", tx)
                .With("@id", id).ExecuteNonQueryAsync();
            await tx.CommitAsync();

            await db.RecordAudit(actor, "Duyệt đăng ký khuôn mặt", "ChamCong", username,
                $"Yêu cầu {id} · PAD trực tiếp {liveProbes.Count} khung khớp · kích hoạt {staged.Count} mẫu mã hóa.");
            await push.SendToUserAsync(username, "Khuôn mặt đã được duyệt",
                "HR đã xác minh và kích hoạt khuôn mặt để chấm công.", $"face-enroll:{id}:approved", "Settings");
            return Results.Ok(new { message = $"Đã xác minh và kích hoạt {staged.Count} mẫu khuôn mặt." });
        }).RequirePermission(Permissions.AttendanceManage);

        g.MapPost("/face-enrollments/{id:guid}/reject", async (Guid id, FaceEnrollmentRejectRequest req,
            ClaimsPrincipal u, Database db, PushService push) =>
        {
            var actor = u.Username();
            var reason = (req?.Reason ?? "").Trim();
            if (reason.Length < 5 || reason.Length > 500)
                return Results.BadRequest(new { message = "Lý do từ chối phải từ 5 đến 500 ký tự." });

            await using var conn = await db.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            string username = "", status = "";
            await using (var reader = await conn.Cmd(
                "SELECT username, status FROM cham_cong_face_enrollments WHERE id=@id FOR UPDATE", tx)
                .With("@id", id).ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync()) return Results.NotFound();
                username = reader.Str("username"); status = reader.Str("status");
            }
            if (status != "pending") return Results.Conflict(new { message = "Yêu cầu này đã được xử lý." });
            if (string.Equals(actor, username, StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { message = "Không được tự xử lý yêu cầu khuôn mặt của chính mình." }, statusCode: 403);

            await conn.Cmd(
                @"UPDATE cham_cong_face_enrollments
                     SET status='rejected', reviewed_by=@by, reviewed_at=CURRENT_TIMESTAMP, review_note=@reason
                   WHERE id=@id AND status='pending'", tx)
                .With("@by", actor).With("@reason", reason).With("@id", id).ExecuteNonQueryAsync();
            // Quyền riêng tư: xóa vector sinh trắc ngay khi từ chối, chỉ giữ metadata/audit.
            await conn.Cmd("DELETE FROM cham_cong_face_enrollment_samples WHERE request_id=@id", tx)
                .With("@id", id).ExecuteNonQueryAsync();
            await tx.CommitAsync();

            await db.RecordAudit(actor, "Từ chối đăng ký khuôn mặt", "ChamCong", username,
                $"Yêu cầu {id}. Lý do: {reason}");
            await push.SendToUserAsync(username, "Yêu cầu khuôn mặt bị từ chối", reason,
                $"face-enroll:{id}:rejected", "Settings");
            return Results.Ok(new { message = "Đã từ chối và xóa toàn bộ vector sinh trắc tạm." });
        }).RequirePermission(Permissions.AttendanceManage);

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
            await using var tx = await conn.BeginTransactionAsync();
            // Dùng đúng khóa app_user mà adaptive learning dùng. Nếu một lượt tự học đang chạy,
            // thao tác xóa sẽ đợi nó hoàn tất rồi xóa cả mẫu vừa thêm; nếu xóa chạy trước,
            // lượt tự học phải đợi và sẽ thấy kho mẫu đã trống nên không thể tái tạo mẫu mồ côi.
            await LockFaceOwnerForMutationAsync(conn, tx, username);
            var n = await conn.Cmd("DELETE FROM cham_cong_face WHERE lower(username)=lower(@u)", tx)
                .With("@u", username).ExecuteNonQueryAsync();
            await tx.CommitAsync();
            if (n > 0) await db.RecordAudit(u.Username(), "Xóa khuôn mặt", "ChamCong", username, "Xóa mẫu khuôn mặt (web).");
            return n > 0 ? Results.NoContent() : Results.NotFound();
        }).RequirePermission(Permissions.AttendanceManage);

        // Xóa 1 mẫu khuôn mặt cụ thể trong nhật ký đăng ký (Admin).
        g.MapDelete("/dangky/mau/{id:long}", async (long id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var owner = "";
            await using (var r = await conn.Cmd("SELECT username FROM cham_cong_face WHERE id=@id LIMIT 1", tx)
                .With("@id", id).ExecuteReaderAsync())
            {
                if (await r.ReadAsync()) owner = r.Str("username");
            }

            if (owner.Length == 0)
            {
                await tx.RollbackAsync();
                return Results.NotFound();
            }

            await LockFaceOwnerForMutationAsync(conn, tx, owner);
            var n = await conn.Cmd("DELETE FROM cham_cong_face WHERE id=@id", tx)
                .With("@id", id).ExecuteNonQueryAsync();
            await tx.CommitAsync();
            if (n > 0) await db.RecordAudit(u.Username(), "Xóa mẫu khuôn mặt", "ChamCong", owner, $"Xóa mẫu khuôn mặt id={id} (web).");
            return n > 0 ? Results.NoContent() : Results.NotFound();
        }).RequirePermission(Permissions.AttendanceManage);

        // Chấm công: chụp ảnh -> liveness -> trích vector -> so khớp -> ghi Vào/Ra.
        // Ẩn danh: cho phép chấm công ở kiosk màn hình đăng nhập (không cần tài khoản).
        g.MapPost("/nhandien", async (NhanDienRequest req, Database db, IFaceEngine engine, FieldCipher cipher, HttpContext http) =>
        {
            if (!FaceAntiSpoofSecurity.IsOperational(engine))
                return Results.Json(new { message = "Hệ thống chống giả mạo đang không khả dụng. Chấm công khuôn mặt đã được khóa an toàn." }, statusCode: 503);
            // Ẩn danh (kiosk, chưa đăng nhập) ⇒ KHÔNG trả username/họ tên đầy đủ để tránh thu thập danh
            // tính. Đăng nhập rồi (chấm cho chính mình) ⇒ trả đủ thông tin như trước.
            var anon = http.User.Identity?.IsAuthenticated != true;
            if (!TryDecodeImage(req.ImageBase64, out var bytes))
                return Results.BadRequest(new { message = "Ảnh không hợp lệ." });

            if (FaceAntiSpoofSecurity.ProbabilityReal(engine, bytes) < engine.LivenessThreshold)
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
                @"SELECT f.username, f.full_name, f.embedding
                    FROM cham_cong_face f
                    JOIN app_users a ON lower(a.username)=lower(f.username)
                   WHERE a.is_active=TRUE AND COALESCE(a.is_deleted,FALSE)=FALSE
                     AND a.approval_status='Approved'").ExecuteReaderAsync())
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

            await using var attendanceTx = await conn.BeginTransactionAsync();
            if (!await LockActiveFaceOwnerAsync(conn, attendanceTx, bestUser))
            {
                await attendanceTx.RollbackAsync();
                return Results.Ok(new NhanDienResult(false, null, null, 0, null, null,
                    "Tài khoản đã bị khóa hoặc khuôn mặt không còn hiệu lực."));
            }
            // KHÔNG lưu ảnh vào log: cột anh không hiển thị ở bất kỳ đâu nên chỉ làm phình DB.
            // (Cột vẫn còn trong bảng để tương thích cũ; đơn giản là không ghi nữa → mặc định NULL.)
            await conn.Cmd(
                @"INSERT INTO cham_cong_log (username, full_name, loai, similarity, occurred_at)
                  VALUES (@u, @fn, @loai, @sim, CURRENT_TIMESTAMP)", attendanceTx)
                .With("@u", bestUser).With("@fn", bestName ?? "")
                .With("@loai", loai).With("@sim", best)
                .ExecuteNonQueryAsync();
            await attendanceTx.CommitAsync();

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
        g.MapPost("/cham", async (ChamCongBurstRequest req, Database db, IFaceEngine engine, ClaimsPrincipal u, FieldCipher cipher, LivenessMetricsLog livenessLog, AttendancePreviewTokens previewTokens, IHubContext<ChangesHub> hub, ILoggerFactory lf, HttpContext http, PushService push) =>
        {
            var currentUser = u.Username();
            var selfOnly = !string.IsNullOrWhiteSpace(currentUser) && !u.Can(Permissions.AttendanceManage);
            // Kiosk ẩn danh vẫn hoạt động; mọi tài khoản đang đăng nhập (kể cả quản lý) phải xác nhận
            // phiếu lương của chính mình trước khi dùng luồng chấm công trong app.
            if (!string.IsNullOrWhiteSpace(currentUser))
            {
                await using var gateConn = await db.OpenAsync();
                if (await PayrollEndpoints.ReadPendingPayslipRequirement(gateConn, currentUser, overdueOnly: true) is { } overdue)
                    return Results.Ok(PayslipAcknowledgementRequired(overdue));
            }
            if (!FaceAntiSpoofSecurity.IsOperational(engine))
                return Results.Json(new { message = "Hệ thống chống giả mạo đang không khả dụng. Chấm công khuôn mặt đã được khóa an toàn." }, statusCode: 503);
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

                await using var confirmTx = await confirmConn.BeginTransactionAsync();
                if (!await LockActiveFaceOwnerAsync(confirmConn, confirmTx, pending.MatchedUser))
                {
                    await confirmTx.RollbackAsync();
                    return Results.Ok(new ChamCongResult("disabled", false, null, null, 0, null, null,
                        pending.Quality, "Tài khoản đã bị khóa hoặc khuôn mặt không còn hiệu lực.",
                        "Liên hệ HR nếu bạn cho rằng đây là nhầm lẫn."));
                }
                await confirmConn.Cmd(
                    @"INSERT INTO cham_cong_log (username, full_name, loai, similarity, occurred_at, ghi_chu)
                      VALUES (@u, @fn, @loai, @sim, CURRENT_TIMESTAMP, '')", confirmTx)
                    .With("@u", pending.MatchedUser).With("@fn", pending.MatchedName)
                    .With("@loai", confirmDecision.Loai).With("@sim", pending.Similarity)
                    .ExecuteNonQueryAsync();
                await confirmTx.CommitAsync();

                await db.RecordAudit(pending.MatchedUser, $"Chấm công {confirmDecision.Loai}", "ChamCong",
                    pending.MatchedUser,
                    $"Độ khớp {pending.Similarity:0.000}, chất lượng ảnh {pending.Quality:0.00} (xác nhận sau xem trước).");

                await ManagementFeed.AnnounceAttendanceAsync(push, lf.CreateLogger("ManagementFeed"),
                    pending.MatchedUser, pending.MatchedName, confirmDecision.Loai, DateTime.UtcNow);

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
            // 2) PAD được gắn với từng khung. Chỉ các khung tự vượt ngưỡng mới được phép cung cấp
            // embedding/pose/eye/smile cho các bước sau; điều này chặn việc ghép người thật ở khung A
            // với ảnh của nạn nhân ở khung B.
            const int livenessFramesToCheck = 5;
            var liveScores = new List<double>();
            var liveCandidates = new List<(byte[] Bytes, FaceFrameQuality Q, double Liveness)>();
            foreach (var c in candidates.Take(livenessFramesToCheck))
            {
                var score = FaceAntiSpoofSecurity.ProbabilityReal(engine, c.Bytes);
                liveScores.Add(score); // tính hết để có đủ số đo hiệu chỉnh
                if (score >= engine.LivenessThreshold) liveCandidates.Add((c.Bytes, c.Q, score));
            }
            var livePassed = liveCandidates.Count > 0;
            var bestBytes = livePassed ? liveCandidates[0].Bytes : candidates[0].Bytes;
            var best = livePassed ? liveCandidates[0].Q : candidates[0].Q;

            var signalFrames = livePassed
                ? liveCandidates.Select(c => c.Q).ToList()
                : new List<FaceFrameQuality> { best };
            var bestEyeOpen = signalFrames.Max(q => q.EyeOpen);
            var bestSmile = signalFrames
                .Where(q => Math.Abs(q.Pose.Yaw) < 0.20)
                .Select(q => q.Smile)
                .DefaultIfEmpty(0)
                .Max();

            // 4a) LIVENESS QUAY ĐẦU (challenge-response): biên độ góc quay yaw của loạt (từ pose các khung
            // đã có sẵn). Ảnh tĩnh không quay đầu ⇒ span ≈ 0. Chỉ xét khi app báo motionCheck.
            var yaws = liveCandidates.Select(c => c.Q.Pose.Yaw).ToList();
            var motionSpan = req.MotionCheck && yaws.Count >= 2 ? yaws.Max() - yaws.Min() : -1;
            bool motionEnabled = false, motionEnforce = false;
            // Cấu hình MỞ MẮT: đọc LUÔN (không phụ thuộc motionCheck) vì đây là lớp server độc lập.
            bool eyeOpenEnforce;
            double eyeOpenThreshold;
            bool smileEnabled;
            double smileThreshold;
            {
                await using var smc = await db.OpenAsync();
                if (req.MotionCheck)
                {
                    motionEnabled = await GetSettingBoolAsync(smc, CfgMotionEnabled, DefaultMotionEnabled);
                    motionEnforce = await GetSettingBoolAsync(smc, CfgMotionEnforce, DefaultMotionEnforce);
                }
                eyeOpenEnforce = await GetSettingBoolAsync(smc, CfgEyeOpenEnforce, DefaultEyeOpenEnforce);
                eyeOpenThreshold = await GetSettingDoubleAsync(smc, CfgEyeOpenThreshold) ?? DefaultEyeOpenThreshold;
                smileEnabled = await GetSettingBoolAsync(smc, CfgSmileEnabled, DefaultSmileEnabled);
                smileThreshold = await GetSettingDoubleAsync(smc, CfgSmileThreshold) ?? DefaultSmileThreshold;
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

            // 3) Tư thế/chất lượng cũng phải được đánh giá trên chính khung đã vượt PAD.
            var posture = CheckPosture(best.Pose);
            if (posture is not null)
                return Results.Ok(new ChamCongResult("posture", false, null, null, 0, null, null, best.Score,
                    "Sai tư thế chấm công.", posture));
            if (best.Score < MinFrameQuality)
                return Results.Ok(new ChamCongResult("lowquality", false, null, null, 0, null, null, best.Score,
                    "Ảnh chưa đủ rõ (thiếu sáng, loá hoặc bị nhòe).",
                    "Tìm nơi đủ sáng, giữ máy ổn định và nhìn thẳng rồi chấm lại."));

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

            // Yêu cầu cười là tùy chọn nhưng khi đã bật thì server bắt buộc kiểm tra lại từ ảnh.
            if (smileEnabled && bestSmile < smileThreshold)
                return Results.Ok(new ChamCongResult("nosmile", false, null, null, 0, null, null, best.Score,
                    "Chưa xác nhận được nụ cười.", "Hãy nhìn thẳng, mỉm cười rõ hơn và quét lại khuôn mặt."));

            // ĐÃ GỠ: 4b) active-flash liveness (đối chiếu màu phản xạ với chuỗi màu màn hình). Khối này
            // chỉ chạy khi client gửi challengeId + slotIndices, mà cả APK lẫn web đều đã ngừng gửi từ
            // lâu ⇒ nó chưa từng gác gì trên thực tế. Xem ghi chú đầu lớp.

            // Gộp vector NHIỀU khung CHÍNH DIỆN tốt nhất (trung bình + chuẩn hóa) → ổn định hơn 1 khung,
            // giảm nhận nhầm/từ chối nhầm. Loại khung QUAY ĐẦU (yaw lớn — khi bật liveness quay đầu) để
            // không làm méo vector. Không có khung chính diện nào ⇒ dùng khung tốt nhất.
            const int fuseFrames = 5;
            const double frontalYawLimit = 0.18;
            var fuseBytes = liveCandidates
                .Where(c => Math.Abs(c.Q.Pose.Yaw) < frontalYawLimit)
                .Take(fuseFrames)
                .Select(c => c.Bytes)
                .ToList();
            if (fuseBytes.Count == 0) fuseBytes.Add(bestBytes);

            // Một burst có thể chứa nhiều người thật. Kiểm tra danh tính từng khung trước khi fuse để
            // không tạo vector lai hoặc cho một người "mượn" chuyển động của người khác.
            var frameEmbeddings = new List<float[]>();
            foreach (var frame in fuseBytes)
            {
                if (engine.ExtractEmbedding(frame) is { } frameEmbedding)
                    frameEmbeddings.Add(frameEmbedding);
            }
            if (frameEmbeddings.Count == 0)
                return Results.Ok(new ChamCongResult("noface", false, null, null, 0, null, null, best.Score,
                    "Không trích được đặc trưng khuôn mặt.", "Nhìn thẳng vào camera rồi chấm lại."));
            var burstConsistencyThreshold = Math.Max(0.33, engine.MatchThreshold - 0.12);
            if (frameEmbeddings.Skip(1).Any(e => engine.Compare(frameEmbeddings[0], e) < burstConsistencyThreshold))
                return Results.Ok(new ChamCongResult("spoof", false, null, null, 0, null, null, best.Score,
                    "Phát hiện nhiều khuôn mặt khác nhau trong lượt quét.",
                    "Chỉ một người đứng trước camera và chấm công lại."));

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
                @"SELECT f.username, f.full_name, f.embedding
                    FROM cham_cong_face f
                    JOIN app_users a ON lower(a.username)=lower(f.username)
                   WHERE a.is_active=TRUE AND COALESCE(a.is_deleted,FALSE)=FALSE
                     AND a.approval_status='Approved'").ExecuteReaderAsync())
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
                await using var offlineTx = await conn.BeginTransactionAsync();
                if (!await LockActiveFaceOwnerAsync(conn, offlineTx, bestUser))
                {
                    await offlineTx.RollbackAsync();
                    return Results.Ok(new ChamCongResult("disabled", false, null, null, 0, null, null,
                        best.Score, "Tài khoản đã bị khóa hoặc khuôn mặt không còn hiệu lực.", null));
                }
                await CreateOfflinePendingAsync(conn, http, bestUser, bestName ?? "", decision,
                    bestSim, best.Score, occurredAtUtc!.Value, req.GpsLat, req.GpsLng, offlineTx);
                await offlineTx.CommitAsync();
                await db.RecordAudit(bestUser, "Chấm công ngoại tuyến (chờ duyệt)", "ChamCong", bestUser,
                    $"Chờ duyệt · độ khớp {bestSim:0.000} · giờ chấm {occurredAtUtc:yyyy-MM-dd HH:mm} (UTC).");
                return Results.Ok(new ChamCongResult("pending", true, outUser, outName, bestSim, decision.Loai,
                    occurredAtUtc, best.Score, "Đã đồng bộ — chờ quản lý duyệt.", null));
            }

            if (!decision.ShouldRecord)
                return Results.Ok(new ChamCongResult("ok", true, outUser, outName, bestSim, decision.Loai,
                    decision.ExistingAt, best.Score, decision.Message, null));

            var loai = decision.Loai;
            await using var writeTx = await conn.BeginTransactionAsync();
            if (!await LockActiveFaceOwnerAsync(conn, writeTx, bestUser))
            {
                await writeTx.RollbackAsync();
                return Results.Ok(new ChamCongResult("disabled", false, null, null, 0, null, null,
                    best.Score, "Tài khoản đã bị khóa hoặc khuôn mặt không còn hiệu lực.", null));
            }
            await conn.Cmd(
                @"INSERT INTO cham_cong_log (username, full_name, loai, similarity, occurred_at, ghi_chu)
                  VALUES (@u, @fn, @loai, @sim, COALESCE(@at, CURRENT_TIMESTAMP), @note)", writeTx)
                .With("@u", bestUser).With("@fn", bestName ?? "")
                .With("@loai", loai).With("@sim", bestSim)
                .With("@at", (object?)occurredAtUtc ?? DBNull.Value)
                .With("@note", isOffline ? "Đồng bộ ngoại tuyến" : "")
                .ExecuteNonQueryAsync();
            await writeTx.CommitAsync();

            await db.RecordAudit(bestUser, $"Chấm công {loai}", "ChamCong", bestUser,
                $"Độ khớp {bestSim:0.000}, chất lượng ảnh {best.Score:0.00}{(isOffline ? ", đồng bộ ngoại tuyến" : "")} (web).");

            await ManagementFeed.AnnounceAttendanceAsync(push, lf.CreateLogger("ManagementFeed"),
                bestUser, bestName ?? "", loai, occurredAtUtc ?? DateTime.UtcNow);

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
            // Đọc username không khóa trước để giữ thứ tự khóa toàn hệ thống: app_user -> bản offline.
            // DeleteUserEverywhere cũng khóa app_user trước rồi mới xóa offline, nên không tạo vòng deadlock.
            var expectedUsername = Convert.ToString(await conn.Cmd(
                "SELECT username FROM cham_cong_offline WHERE id=@id LIMIT 1")
                .With("@id", id).ExecuteScalarAsync());
            if (string.IsNullOrWhiteSpace(expectedUsername)) return Results.NotFound();

            await using var tx = await conn.BeginTransactionAsync();
            if (!await LockActiveFaceOwnerAsync(conn, tx, expectedUsername))
            {
                await tx.RollbackAsync();
                return Results.Conflict(new { message = "Không thể duyệt: tài khoản hoặc khuôn mặt của nhân viên không còn hoạt động." });
            }

            string username = "", fullName = "", loai = "", curStatus = "";
            DateTime occurredAt = default;
            double sim = 0;
            await using (var r = await conn.Cmd(
                "SELECT username, full_name, loai, occurred_at, similarity, status FROM cham_cong_offline WHERE id=@id FOR UPDATE", tx)
                .With("@id", id).ExecuteReaderAsync())
            {
                if (!await r.ReadAsync())
                {
                    await tx.RollbackAsync();
                    return Results.NotFound();
                }
                username = r.Str("username"); fullName = r.Str("full_name"); loai = r.Str("loai");
                occurredAt = r.Dt("occurred_at"); sim = r.GetDouble(r.GetOrdinal("similarity")); curStatus = r.Str("status");
            }
            if (!string.Equals(username, expectedUsername, StringComparison.OrdinalIgnoreCase))
            {
                await tx.RollbackAsync();
                return Results.Conflict(new { message = "Bản chấm công đã thay đổi chủ sở hữu; yêu cầu bị hủy an toàn." });
            }
            // Retry hoặc hai quản trị viên duyệt đồng thời đều nhận thành công nhưng không ghi log lần hai.
            if (curStatus == "approved")
            {
                await tx.CommitAsync();
                return Results.Ok(new { message = "Bản này đã được duyệt và ghi công trước đó.", alreadyApproved = true });
            }
            if (curStatus != "pending")
            {
                await tx.RollbackAsync();
                return Results.Conflict(new { message = "Bản này đã được xử lý với trạng thái khác." });
            }

            await conn.Cmd(
                @"INSERT INTO cham_cong_log (username, full_name, loai, similarity, occurred_at, ghi_chu)
                  VALUES (@u, @fn, @loai, @sim, @at, @note)", tx)
                .With("@u", username).With("@fn", fullName)
                .With("@loai", loai).With("@sim", sim)
                .With("@at", occurredAt)
                .With("@note", "Ngoại tuyến (đã duyệt)")
                .ExecuteNonQueryAsync();

            await conn.Cmd(
                @"UPDATE cham_cong_offline SET status='approved', reviewed_by=@by, reviewed_at=CURRENT_TIMESTAMP,
                    review_note=@note WHERE id=@id AND status='pending'", tx)
                .With("@by", u.Username()).With("@note", body?.Note ?? "").With("@id", id)
                .ExecuteNonQueryAsync();
            await tx.CommitAsync();

            await db.RecordAudit(u.Username(), "Duyệt chấm công ngoại tuyến", "ChamCong", username,
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

    private static ChamCongResult PayslipAcknowledgementRequired(
        PayrollEndpoints.PendingPayslipRequirement requirement) =>
        new("payslip_required", false, null, null, 0, null, null, 0,
            $"Phiếu lương kỳ {requirement.Period} đã quá hạn xác nhận.",
            "Mở mục Phiếu lương, kiểm tra chi tiết và bấm Xác nhận trước khi chấm công.");

    private static async Task SetSettingAsync(NpgsqlConnection conn, string key, string value, string by)
    {
        await conn.Cmd(
            @"INSERT INTO web_system_settings (setting_key, setting_value, updated_at, updated_by)
              VALUES (@k, @v, CURRENT_TIMESTAMP, @by)
              ON CONFLICT (setting_key) DO UPDATE SET setting_value=@v, updated_at=CURRENT_TIMESTAMP, updated_by=@by")
            .With("@k", key).With("@v", value).With("@by", by).ExecuteNonQueryAsync();
    }

    /// <summary>Hết hạn yêu cầu và xóa ngay vector staging; metadata được giữ lại cho audit.</summary>
    private static async Task ExpireFaceEnrollmentsAsync(NpgsqlConnection conn, NpgsqlTransaction? tx = null)
    {
        const string sql = """
            UPDATE cham_cong_face_enrollments
               SET status='expired', reviewed_at=CURRENT_TIMESTAMP,
                   review_note='Tự động hết hạn sau 14 ngày.'
             WHERE status='pending' AND expires_at <= CURRENT_TIMESTAMP;
            DELETE FROM cham_cong_face_enrollment_samples s
             USING cham_cong_face_enrollments r
             WHERE s.request_id=r.id AND r.status='expired';
            """;
        var cmd = tx is null ? conn.Cmd(sql) : conn.Cmd(sql, tx);
        await cmd.ExecuteNonQueryAsync();
    }

    private static bool SelfEnrollPoseMatches(string? requestedPose, FacePose detected)
    {
        return (requestedPose ?? "").Trim().ToLowerInvariant() switch
        {
            "front" => CheckPosture(detected) is null,
            "side1" or "side2" => Math.Abs(detected.Yaw) > PostureYawMax,
            "up" => Math.Abs(detected.Yaw) <= PostureYawMax && detected.Pitch < PosturePitchMin,
            "down" => Math.Abs(detected.Yaw) <= PostureYawMax && detected.Pitch > PosturePitchMax,
            _ => false
        };
    }

    private static async Task<bool> LockActiveFaceOwnerAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string username)
    {
        string? canonical = null;
        // Khóa tài khoản TRƯỚC, rồi mới kiểm tra kho mặt bằng một statement mới. Nhờ vậy một
        // transaction xóa đã commit trong lúc ta chờ khóa không thể bị snapshot cũ che khuất.
        await using (var reader = await conn.Cmd(
            @"SELECT username
                FROM app_users
               WHERE lower(username)=lower(@u)
                 AND is_active=TRUE
                 AND COALESCE(is_deleted,FALSE)=FALSE
                 AND approval_status='Approved'
               ORDER BY username
               FOR UPDATE", tx).With("@u", username).ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var candidate = reader.Str("username");
                if (canonical is null || string.Equals(candidate, username, StringComparison.Ordinal))
                    canonical = candidate;
            }
        }
        if (canonical is null) return false;

        var face = await conn.Cmd(
            "SELECT 1 FROM cham_cong_face WHERE lower(username)=lower(@u) LIMIT 1", tx)
            .With("@u", canonical).ExecuteScalarAsync();
        return face is not null and not DBNull;
    }

    private static async Task<string?> LockFaceOwnerForMutationAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string username)
    {
        string? canonical = null;
        // Không lọc active: quản trị viên vẫn phải xóa được dữ liệu sinh trắc của tài khoản đã khóa.
        // Đọc hết để khóa mọi biến thể hoa/thường nếu dữ liệu cũ từng cho phép chúng cùng tồn tại.
        await using var reader = await conn.Cmd(
            "SELECT username FROM app_users WHERE lower(username)=lower(@u) ORDER BY username FOR UPDATE", tx)
            .With("@u", username).ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var candidate = reader.Str("username");
            if (canonical is null || string.Equals(candidate, username, StringComparison.Ordinal))
                canonical = candidate;
        }
        return canonical;
    }

    /// <summary>
    /// Tạo bản chấm công ngoại tuyến CHỜ DUYỆT + tính các cờ rủi ro: lùi giờ (occurred so với lúc nhận),
    /// có ở LAN công ty không (IP riêng/khớp cấu hình), có trong geofence không (nếu đã cấu hình toạ độ).
    /// </summary>
    private static async Task CreateOfflinePendingAsync(
        NpgsqlConnection conn, HttpContext http, string username, string fullName,
        AttendanceDecision decision, double similarity, double quality, DateTime occurredAtUtc,
        double? gpsLat, double? gpsLng, NpgsqlTransaction tx)
    {
        var nowUtc = DateTime.UtcNow;
        var backdateMinutes = Math.Max(0, (int)(nowUtc - occurredAtUtc).TotalMinutes);
        var ip = (http.Connection.RemoteIpAddress?.MapToIPv4() ?? http.Connection.RemoteIpAddress)?.ToString() ?? "";
        var onLan = IsPrivateIp(http.Connection.RemoteIpAddress);

        var maxBackdate = (int)(await GetSettingDoubleAsync(conn, CfgMaxBackdate, tx) ?? DefaultMaxBackdateMinutes);
        var geoLat = await GetSettingDoubleAsync(conn, CfgGeofenceLat, tx);
        var geoLng = await GetSettingDoubleAsync(conn, CfgGeofenceLng, tx);
        var geoRadius = await GetSettingDoubleAsync(conn, CfgGeofenceRadius, tx) ?? DefaultGeofenceRadiusM;

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
              VALUES (@u, @fn, @loai, @sim, @q, @at, CURRENT_TIMESTAMP, @bd, @ip, @lan, @la, @lo, @dist, @inf, @flags, 'pending')", tx)
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

    private static async Task<double?> GetSettingDoubleAsync(
        NpgsqlConnection conn, string key, NpgsqlTransaction? tx = null)
    {
        const string sql = "SELECT setting_value FROM web_system_settings WHERE setting_key=@k LIMIT 1";
        var cmd = tx is null ? conn.Cmd(sql) : conn.Cmd(sql, tx);
        var v = await cmd.With("@k", key).ExecuteScalarAsync();
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

        await using var tx = await conn.BeginTransactionAsync();
        // Khóa cùng hàng app_user với luồng xóa/khóa. Nếu tài khoản vừa bị vô hiệu hóa sau bước
        // nhận diện, tuyệt đối không được tái tạo một mẫu khuôn mặt mồ côi.
        if (!await LockActiveFaceOwnerAsync(conn, tx, username))
        {
            await tx.RollbackAsync();
            return;
        }

        // Mỗi người chỉ học tối đa 1 mẫu/ngày.
        var learnedToday = await conn.Cmd(
            @"SELECT 1 FROM cham_cong_face
              WHERE username=@u AND created_by=@auto
                AND created_at::date = CURRENT_DATE
              LIMIT 1", tx)
            .With("@u", username).With("@auto", AutoLearnTag).ExecuteScalarAsync();
        if (learnedToday is not null and not DBNull) return;

        var total = Convert.ToInt32(await conn.Cmd(
            "SELECT COUNT(*) FROM cham_cong_face WHERE username=@u", tx)
            .With("@u", username).ExecuteScalarAsync());

        if (total >= MaxFaceSamples)
        {
            // Hết chỗ → thay mẫu TỰ HỌC cũ nhất. Nếu cả 5 đều là mẫu admin thì thôi (không học).
            var oldestAuto = await conn.Cmd(
                @"SELECT id FROM cham_cong_face
                  WHERE username=@u AND created_by=@auto ORDER BY created_at ASC, id ASC LIMIT 1", tx)
                .With("@u", username).With("@auto", AutoLearnTag).ExecuteScalarAsync();
            if (oldestAuto is null or DBNull) return;
            await conn.Cmd("DELETE FROM cham_cong_face WHERE id=@id", tx)
                .With("@id", Convert.ToInt64(oldestAuto)).ExecuteNonQueryAsync();
        }

        await conn.Cmd(
            @"INSERT INTO cham_cong_face (username, full_name, embedding, anh, created_at, created_by)
              VALUES (@u, @fn, @emb, NULL, CURRENT_TIMESTAMP, @auto)", tx)
            .With("@u", username).With("@fn", fullName)
            .With("@emb", cipher.EncryptEmbedding(embedding))
            .With("@auto", AutoLearnTag)
            .ExecuteNonQueryAsync();
        await tx.CommitAsync();
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
