# ĐẶC TẢ VIẾT LẠI BACKEND SANG NGÔN NGỮ KHÁC

> Phần mô tả SignalR/chat/call bên dưới là hợp đồng lịch sử trước extraction. Host nghiệp vụ hiện
> tại chỉ dùng SSE; toàn bộ communication source và tài liệu triển khai đã chuyển sang
> `communication-standalone/`.

Tài liệu này **không** liệt kê chức năng (xem [`backend-inventory.md`](backend-inventory.md)). Nó ghi
những thứ mà một bản viết lại **bắt buộc phải tái lập chính xác**, nếu không dữ liệu cũ sẽ không đọc
được, người dùng bị đăng xuất hàng loạt, hoặc client hiện có (web + APK native) gãy.

Lập 2026-08-28, kiểm chứng bằng cách **chạy thật ứng dụng** (build + khởi động trên cổng 5399/5499)
và **`pg_dump`** cơ sở dữ liệu đang dùng.

## Tài liệu đi kèm (đã sinh, dùng làm nguồn máy-đọc-được)

| Tệp | Nội dung | Cách sinh lại |
|---|---|---|
| [`backend-schema.sql`](backend-schema.sql) | DDL thật: 100 bảng, 2 view, 111 index, 100 PK, 16 UNIQUE, 66 FK, 93 trigger, 6 hàm plpgsql | `pg_dump --schema-only --no-owner --no-privileges` |
| [`openapi.baseline.json`](openapi.baseline.json) | 284 path / **355 operation** / 127 schema request | `GET /swagger/v1/swagger.json` khi app chạy |

---

## 0. TRẠNG THÁI HỢP ĐỒNG API — đọc phần này trước

### 0.1 Một lỗi đã phát hiện và đã vá trong lúc kiểm tra

`/swagger/v1/swagger.json` **trả 500 ở mọi môi trường** trước hôm nay:

```
SwaggerGeneratorException: Failed to generate Operation for action - POST /api/cash-fund/entries/{id:guid}/reverse
  InvalidOperationException: Can't use schemaId "$ReasonReq" for type
  "KetoanMini.Api.Endpoints.CashFundEndpoints+ReasonReq".
  The same schemaId is already used for type "KetoanMini.Api.Endpoints.CashCollectionEndpoints+ReasonReq"
```

Hai module đặt trùng tên DTO `ReasonReq`. Hệ quả: **suốt thời gian qua không hề có hợp đồng API
máy-đọc-được nào**, dù `Program.cs` ghi "OpenAPI JSON là nguồn chuẩn cho web/APK và kiểm thử contract".

Đã vá bằng một dòng trong `Program.cs` (đặt `CustomSchemaIds` theo tên đầy đủ). Sau khi vá:
`200 OK`, 271 KB, 396 operation.

### 0.2 Khoảng trống lớn nhất: **không có hợp đồng RESPONSE**

Đo trên chính spec vừa sinh:

| | Số lượng |
|---|---|
| Operation | 396 |
| Có **request body** mô tả được | 172 |
| Có **response schema** mô tả được | **0** |

Nguyên nhân: handler minimal API trả `Results.Ok(new { ... })` (đối tượng ẩn danh) nên
`ApiExplorer` không suy ra được kiểu. Phân bố kiểu trả về trong mã:

| Dạng trả về | Số chỗ |
|---|---|
| `Results.Ok(new { … })` — **ẩn danh, không có kiểu** | 113 |
| `Results.Ok(new SomeDto(…))` — có kiểu | 66 |
| `Results.Json(…)` | 75 |
| `Results.NoContent()` | 120 |
| `Results.File/Bytes/Stream` | 9 |
| `Results.BadRequest(…)` | 319 |
| `Results.Forbid/Unauthorized/NotFound/Conflict` | 427 |

**Kết luận cho việc port:** hình dạng JSON trả về hiện chỉ tồn tại trong đầu người và trong mã C#.
Đây là rủi ro số một — bản viết lại có thể "đúng logic" mà vẫn làm trắng màn hình web/APK.

**Việc bắt buộc làm trước khi viết dòng code mới nào** (mục 8.1).

---

## 1. RÀNG BUỘC NỀN TẢNG — quyết định chọn ngôn ngữ

Đây là những phần **không phải cứ dịch code là xong**. Cần chốt hướng cho từng cái *trước* khi chọn
ngôn ngữ, vì chúng có thể buộc phải giữ một sidecar .NET/Windows.

| # | Thành phần | Phụ thuộc thật | Mức khó khi port |
|---|---|---|---|
| 1 | **In phiếu xuất kho** (`WarehouseVoucherPrintService`, 967 dòng) | **COM late-binding `Excel.Application`** (`Type.GetTypeFromProgID` + `dynamic` + `PrintOut`) **+ P/Invoke winspool** (`PrinterInfo2`, `JobInfo1`, `Marshal.AllocHGlobal`) để theo dõi hàng đợi in | **Rất cao.** Cần Microsoft Excel cài trên máy, chạy trong phiên Windows tương tác. Hầu như ngôn ngữ nào cũng gọi được COM trên Windows nhưng chi phí lớn → **khuyến nghị giữ sidecar** |
| 2 | **Nhận diện khuôn mặt** | ONNX Runtime 1.18 + OpenCvSharp4 4.10 (YuNet, AdaFace R50, 2 model MiniFASNet) | Trung bình. ONNX Runtime + OpenCV có binding cho hầu hết ngôn ngữ. **Phải giữ nguyên tiền xử lý từng bit** (mục 4.4) nếu không muốn đăng ký lại toàn bộ khuôn mặt |
| 3 | **SignalR** `/hubs/changes` | Giao thức riêng của ASP.NET Core (negotiate + WebSocket + MessagePack/JSON hub protocol) | Cao — **client web và APK đang nói đúng giao thức này**. Đổi sang WebSocket thuần = phải sửa cả hai client. Khuyến nghị giữ sidecar hoặc dùng thư viện tương thích SignalR |
| 4 | **ASP.NET Data Protection** (vé QR `KetoanMini.QrActions.v1`) | Định dạng payload + key ring riêng của .NET | Thấp — vé chỉ sống vài phút, **có thể thay bằng JWT/HMAC riêng**, không có dữ liệu cũ cần đọc |
| 5 | **Xuất Excel bảng lương** | ClosedXML 0.104.2 | Thấp — thư viện xlsx nào cũng thay được, chỉ cần giữ đúng bố cục (mỗi NV 1 sheet + phiếu lương 6/A4) |
| 6 | **FCM** | FirebaseAdmin 3.1.0 | Thấp — REST API chuẩn |
| 7 | **PostgreSQL** | Npgsql 8, `LISTEN/NOTIFY`, `jsonb`, `bytea`, `make_interval`, `string_agg`, `FILTER (WHERE …)`, `AT TIME ZONE` | Thấp — nhưng phải giữ **cùng một PostgreSQL**, không đổi hệ CSDL |

> **Khuyến nghị:** port theo lối *strangler* giống hệt cách `KetoanMini.Rust` đang làm — cổng mới
> đứng trước, route nào chưa port thì stream về .NET. Ba thứ (1), (2), (3) là ứng viên **giữ lại
> lâu dài** làm sidecar chứ không cố port.

---

## 2. HỢP ĐỒNG DÂY (WIRE CONTRACT)

### 2.1 JSON

```
PropertyNamingPolicy      = camelCase
DefaultIgnoreCondition    = WhenWritingNull   (thuộc tính null bị BỎ HẲN khỏi payload)
DateTime / DateTime?      → "yyyy-MM-ddTHH:mm:ss.fffZ"   (luôn UTC, luôn có 'Z', đúng 3 chữ số ms)
DateOnly                  → "yyyy-MM-dd"
```

`UtcDateTimeConverter` xử lý cả chiều đọc: giá trị `Kind=Unspecified` (đọc từ Postgres) được coi là
**UTC**, không phải giờ địa phương. Sai chỗ này ⇒ toàn hệ thống lệch đúng 7 giờ.

### 2.2 Mã lỗi máy-đọc-được — **chỉ có 10**

`csrf_required` · `origin_denied` · `kiosk_key_required` · `login_bootstrap_required` ·
`payslip_changed` · `pin_not_set` · `pin_invalid` · `pin_incorrect` · `pin_locked` · `pin_too_obvious`

Mọi lỗi còn lại chỉ có trường `message` **tiếng Việt có dấu**. Trước khi port phải rà xem web/APK có
chỗ nào so khớp theo chuỗi message không — đổi một dấu phẩy là gãy.

### 2.3 Giới hạn payload (`PayloadLimits`)

| Hằng | Giá trị |
|---|---|
| `MaxJsonBodyBytes` | 16 MiB (trần mặc định Kestrel) |
| `MaxApkBytes` | 200 MiB (chỉ `POST /api/releases`) |
| `MaxQrActionBodyBytes` | 32 KiB (`/api/qr/*`) |
| `MaxImageBytes` | 2 MiB / ảnh |
| `MaxImagesPerRequest` | **16** khung / lượt quét |
| `MaxImagesPerEnrollRequest` | **36** (3 góc × ≤10 khung) |
| upload blob chat | `ChatEndpoints.MaxBlobBytes` |

Quy tắc chọn trần **theo từng endpoint**, so path **đã `TrimEnd('/')`** (web gọi `/api/releases/` có
dấu `/` cuối — đây chính là bẫy 413 đã từng làm không đăng được bản cập nhật). Vượt trần ⇒
`413` + `{"message":"Payload vượt giới hạn <n> byte."}`.

Ảnh gửi dạng base64, chấp nhận cả tiền tố `data:image/...;base64,`. Kiểm tra độ dài base64
**trước khi giải mã** (`((MaxImageBytes + 2) / 3) * 4`) để không cấp phát mảng lớn.

### 2.4 Security headers (mọi phản hồi, kể cả file tĩnh)

```
X-Content-Type-Options: nosniff
Referrer-Policy:        strict-origin-when-cross-origin
X-Frame-Options:        DENY
Content-Security-Policy: default-src 'self'; base-uri 'self'; object-src 'none';
  frame-ancestors 'none'; img-src 'self' data: blob: https:; media-src 'self' blob: https:;
  font-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'wasm-unsafe-eval';
  worker-src 'self' blob:; frame-src 'self' blob:; connect-src 'self' https: wss:; form-action 'self'
```

`'wasm-unsafe-eval'` là bắt buộc — MediaPipe (nhận diện mặt phía client) chạy WASM.

MIME tĩnh phải khai báo thêm: `.tflite` và `.task` → `application/octet-stream`, `.wasm` →
`application/wasm`. Thiếu ⇒ 404 và luồng camera chết.
`index.html` phải trả `Cache-Control: no-store, no-cache, must-revalidate`.

---

## 3. MẬT MÃ & ĐỊNH DẠNG PHẢI TÁI LẬP CHÍNH XÁC

Đây là phần **không có đường lùi**: sai một tham số là dữ liệu cũ thành rác.

### 3.1 Băm mật khẩu — hai định dạng cùng tồn tại

```
Argon2id (mới, sinh ra từ nay):
  ARGON2ID$v=19$m=19456,t=2,p=1$<base64 salt 16B>$<base64 hash 32B>

PBKDF2 (cũ, CHỈ đọc — app desktop cũ sinh ra):
  PBKDF2$<iterations>$<base64 salt>$<base64 hash>     HMAC-SHA256, độ dài khoá = độ dài hash đã lưu
```

- `Verify` phải nhận **cả hai**. Bỏ nhánh PBKDF2 ⇒ khoá cửa những tài khoản chưa từng đăng nhập lại.
- Tham số Argon2id nằm **trong chính chuỗi**, nên verify không được dùng hằng số hiện hành.
- `NeedsRehash` = true khi không phải Argon2id, hoặc `m < 19456 || t < 2 || p != 1`. Sau khi
  `Verify` thành công (lúc còn giữ mật khẩu thô) thì băm lại → di trú dần, không bắt ai đổi mật khẩu.
- So sánh phải **thời gian hằng số**.
- Cùng hàm băm này dùng cho **mã PIN 6 số** và **mã khôi phục**.

### 3.2 Mã hoá trường nhạy cảm at-rest — định dạng `KME1`

```
[magic "KME1" 4B][nonce 12B][tag 16B][ciphertext]      AES-256-GCM, tag 16B
khoá = base64(32 byte) trong Security:FieldEncryptionKey
```

- Không có magic ⇒ dữ liệu cũ chưa mã hoá ⇒ đọc nguyên trạng (di trú dần).
- Thiếu khoá ⇒ chế độ passthrough + cảnh báo (chỉ chấp nhận ở Development).
- **Ngoại lệ fail-closed**: `DecryptEmbedding` — khi đã có khoá mà blob **không** ở dạng `KME1` thì
  **ném lỗi**, không dùng nhánh passthrough. Lý do: một lần import/lỗi DB chèn blob thô sau khởi
  động sẽ khiến sinh trắc học chưa được xác thực bị dùng âm thầm.
- Vector đặc trưng khuôn mặt là `float[]` được `EmbeddingCodec` chuyển sang byte trước khi mã hoá —
  **phải giữ nguyên thứ tự byte và độ dài float**.

### 3.3 JWT

```
alg = HS256, khoá = UTF8 bytes của Jwt:Key (≥32 ký tự)
iss = Jwt:Issuer      aud = Jwt:Audience
```

Claim (tên claim theo **URI dài của .NET**, không phải tên ngắn):

| Claim | Nội dung |
|---|---|
| `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier` | `app_users.id` (GUID) |
| `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name` | `username` |
| `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` | vai trò chính **+ mỗi vai trò phụ một claim nữa** |
| `fullName` | tên hiển thị |
| `sid` | `user_sessions.session_token` — dùng để thu hồi thiết bị |
| `exp` | app 8760h · web 168h |

> Đổi sang tên claim ngắn (`sub`, `name`, `role`) sẽ làm **mọi token đang lưu trên điện thoại nhân
> viên mất hiệu lực**. Nếu muốn đổi, phải làm thành một đợt phát hành token riêng.

**Claim quyền (`perm`) cố ý KHÔNG nằm trong token** — được middleware dựng lại từ CSDL mỗi request.

`RenewWebToken` (gia hạn trượt) chỉ chép `nameidentifier`, `name`, `fullName`, `sid` — **cố ý không
chép role/perm** để không tạo bản sao cũ có thể sai.

### 3.4 Cookie phiên web + CSRF

| | |
|---|---|
| Cookie phiên | `km_auth` — **HttpOnly**, `SameSite=Lax`, `Path=/`, `Secure = Request.IsHttps` |
| Cookie CSRF | `km_csrf` — **KHÔNG HttpOnly** (frontend phải đọc được), cùng các thuộc tính còn lại |
| Header CSRF | `X-CSRF-Token` |
| Giá trị CSRF | `Convert.ToHexString(RandomNumberGenerator.GetBytes(16))` → 32 ký tự hex hoa |

Quy tắc:
- CSRF **chỉ áp cho request xác thực bằng cookie** (`không có header Authorization` **và** `có cookie km_auth`). App native gửi Bearer ⇒ miễn.
- Bỏ qua `GET`, `HEAD`, `OPTIONS`, `TRACE`.
- So sánh header ↔ cookie **thời gian hằng số**.
- `Refresh` (gia hạn trượt) **giữ nguyên giá trị CSRF cũ** — đổi ở đây sẽ đá hỏng các request đang bay.
- `Clear` phải dùng **đúng bộ thuộc tính lúc đặt**, nếu không trình duyệt coi là cookie khác.
- **`/hubs` KHÔNG đi qua CSRF** mà chốt bằng `Origin` (mục 3.5).

### 3.5 Chốt Origin cho WebSocket

WebSocket không bị CORS chặn ⇒ với phiên cookie, trang lạ có thể mở hub dưới danh nghĩa nạn nhân
(Cross-Site WebSocket Hijacking). `IsAllowedOrigin`:

1. Không có header `Origin` ⇒ **cho qua** (không phải trình duyệt; app native dùng Bearer).
2. Khớp `{scheme}://{host}` của chính request ⇒ cho qua.
3. Kết thúc bằng `://{host}` ⇒ cho qua (sau Cloudflare Tunnel scheme có thể lệch http/https).
4. Còn lại: theo `CorsPolicy.IsAllowed`.

Không đạt ⇒ `403` + `{"message":"Origin không được phép.","code":"origin_denied"}`.

### 3.6 CORS

Cho phép nếu: (a) khớp một origin trong `Cors:Origins` (so sau khi `TrimEnd('/')`, không phân biệt
hoa thường), **hoặc** (b) host là `localhost`, **hoặc** (c) host là IP loopback / IP riêng
(`10/8`, `172.16/12`, `192.168/16`, `169.254/16`, IPv6 `fc00::/7`, `fe80::/10`).

### 3.7 Mã khôi phục mật khẩu

```
Bảng chữ: "23456789ABCDEFGHJKMNPQRSTVWXYZ"   (Crockford base32, bỏ 0 O 1 I L U)
Độ dài  : 5 ký tự liền, không chia nhóm       Không gian: 30^5 ≈ 24,3 triệu
Chuẩn hoá đầu vào: bỏ '-' và ' ', trim, ToUpperInvariant
```
An toàn dựa vào: hết hạn **7 ngày** + **dùng một lần** + rate limit **8 lần / 10 phút**.

### 3.8 Mã PIN 6 số của app (`app_pin_codes`, lưu ở máy chủ)

- Đúng 6 chữ số. Từ chối mã "quá dễ": toàn một chữ số, hoặc dãy tăng/giảm liên tiếp (`123456`, `654321`).
- Băm bằng chính `PasswordHasher` (Argon2id) — không gian chỉ 10⁶ nên hàm băm rẻ là dò xong tức thì.
- Đếm sai **theo TÀI KHOẢN** (cài lại app không reset được).
- Khoá tăng dần theo **tổng số lần sai liên tiếp**, chỉ khoá đúng tại bội số của 5:

| Số lần sai | Khoá |
|---|---|
| < 5, hoặc không chia hết cho 5 | không khoá |
| 5 | 30 giây |
| 10 | 5 phút |
| ≥ 15 | 30 phút |

- Trả `423 Locked` + **số giây còn lại** (client đếm ngược mà không cần đồng hồ khớp máy chủ).

### 3.9 Chống trùng phản hồi khảo sát (ẩn danh thật)

```
HMAC-SHA256(key = Jwt:Key, message = $"{surveyId}|{username}")
```
Chỉ lưu HMAC, **không lưu username**. ⚠️ Ràng buộc kéo theo: **đổi `Jwt:Key` sẽ làm mọi người trả lời
lại được các khảo sát cũ**. Nếu đợt port có xoay khoá JWT thì phải tách khoá HMAC khảo sát ra riêng.

### 3.10 Đăng nhập QR

- Phiên sống **5 phút trong RAM** (không chạm DB khi poll).
- **Hai token độc lập**: token trong mã QR (để app xác nhận) và poll token (chỉ trình duyệt biết).
- Server chỉ giữ **SHA-256** của token, không giữ bản rõ.
- Vé quyết định QR action: ASP.NET Data Protection, purpose `KetoanMini.QrActions.v1`, tự chứa,
  gắn tài khoản + phiên app, tự hết hạn → **thay được tự do** (mục 1, #4).

### 3.11 Kho APK trên đĩa

```
<Releases:BlobDirectory>/<id>.apk        tệp hoàn chỉnh
<Releases:BlobDirectory>/<guid:N>.upload tệp staging (dọn lúc khởi động)
```
Chép theo luồng, buffer **80 KB**, không bao giờ nạp trọn vào RAM. DB chỉ giữ metadata
(version, size, SHA-256). Di trú một lần từ cột `bytea` cũ đọc theo khúc **4 MB**.

---

## 4. THUẬT TOÁN NGHIỆP VỤ CÓ SỐ CỤ THỂ

### 4.1 Quyết định Vào/Ra (`AttendancePolicy.DecideAsync`)

Múi giờ: `Asia/Ho_Chi_Minh` cho SQL (`AT TIME ZONE`); phía .NET dò lần lượt
`SE Asia Standard Time` → `Asia/Bangkok` → `TimeZoneInfo.Local`.

1. Xác định **ngày công logic**: mặc định là ngày địa phương; nếu nhân viên có ca `is_overnight`
   phủ thời điểm hiện tại (`work_date + start_time` ≤ t ≤ `work_date+1 + end_time + checkout_grace_minutes`)
   thì lấy `work_date` của ca đó.
2. Đọc từ view `hr_effective_attendance_log`: `MIN(occurred_at) FILTER (loai='Vào')` và
   `MAX(occurred_at) FILTER (loai='Ra')` trong ngày công đó.
3. Chưa có lần chấm nào ⇒ ghi **"Vào"**.
4. Cách giờ Vào **< 5 phút** ⇒ **không ghi** (bấm nhầm lặp lại; tránh tạo giờ Ra 0 phút).
5. Đã có giờ Ra và cách lần chấm gần nhất **< 3 phút** ⇒ **không ghi** (chống nhân đôi).
6. Ngược lại ⇒ ghi **"Ra"**, lấy mốc **muộn nhất**. Đã có giờ Ra trước đó ⇒ đây là **cập nhật**
   (ở lại thêm / tăng ca).

> Mốc 17:00 (`LoaiForLocalTime`) **không còn** dùng để phân loại Vào/Ra — nó chỉ còn ở đường phụ.
> Đồng bộ chấm công ngoại tuyến truyền `atUtc` = thời điểm chấm **thật** (lúc mất mạng), không phải
> lúc gửi lên.

### 4.2 Tăng ca (`CalculateOvertimeMinutes`)

```
sáng   = vào  < 08:00 ? phút(08:00 − vào)  : 0
chiều  = ra   > 17:00 ? phút(ra − 17:00)   : 0
tổng   = (sáng  >= 15 ? sáng  : 0) + (chiều >= 15 ? chiều : 0)
```
Hai vế xét **độc lập**: vào 7:50 (10ʹ) + ra 17:10 (10ʹ) ⇒ **0**, không phải 20.
Ca qua đêm không áp công thức này. (Ràng buộc chốt bởi `OvertimeCalculationTests`.)

### 4.3 Sổ cái phạt (`hr_penalty_ledger`)

- Số trừ mỗi kỳ bị **cap theo lương còn lại có thể trừ** = base + phụ cấp + tăng ca − các khấu trừ khác.
- Phần chưa thu đủ **chuyển sang kỳ sau** (carry-over); mục tiêu là luỹ kế phải thu tính đến hết kỳ.
- Vượt lịch trả góp ⇒ gom nốt carry-over.
- **Tổng đã thu không bao giờ vượt mức phạt**; thu đủ ⇒ "Đã tất toán".
- Khiếu nại chốt theo số **đã thu**, không theo số còn lại.
- Chế độ xem trước gọi với `availableForPenalties = decimal.MaxValue` ⇒ **không cap**.

### 4.4 Nhận diện khuôn mặt — ngưỡng phải giữ nguyên

| Hằng | Giá trị | Ghi chú |
|---|---|---|
| `DefaultMatchThreshold` | **0.45** | cosine; dưới ngưỡng ⇒ không nhận ra ai |
| `LivenessRealThreshold` | 0.5 | |
| `LivenessCropFactor` / `LivenessSize` | 1.5 / 128 | |
| ngưỡng trùng lúc đăng ký | `max(0.60, threshold + 0.10)` | chặn đăng ký trùng người |
| ngưỡng nhất quán loạt khung | `max(0.33, threshold − 0.12)` | |
| `AdaptiveLearnMinSimilarity` | 0.65 | mốc để học thêm mẫu |
| Tăng sáng | `EnhanceDarkMean=110`, `EnhanceBrightMean=200`, `EnhanceGlareRatio=0.045`, ngưỡng loá 245 | |

Silent-Face: **2 model** (`2.7_80x80_MiniFASNetV2`, `4_0_0_80x80_MiniFASNetV1SE`), mỗi model crop
quanh bbox theo **tỉ lệ riêng** (2.7 / 4.0 — không phải crop vuông cố định), resize 80×80, đưa
**BGR/255** vào ONNX → 3 lớp → softmax; **cộng softmax của 2 model** rồi `argmax == 1` ⇒ THẬT.
Thiếu file model ⇒ `Available = false`; Production thiếu ⇒ **dừng khởi động**; lỗi runtime ⇒
fail-closed (điểm live = 0).

> ⚠️ **BGR chứ không phải RGB.** Đã từng sai chỗ này và phải đăng ký lại toàn bộ khuôn mặt.

### 4.5 Bảng lương

Lương cứng **dẫn xuất từ hợp đồng**: `contract_base + SUM(hr_salary_raises.amount)`
(chỉ khi có hợp đồng còn hiệu lực). Phiếu lương lưu chi tiết dòng trong cột `details` (`jsonb`);
`totalEarnings = net_pay + deductions`. Khi đọc lại, giá trị trong `details` **thắng** cột tổng nếu
có (`det.NetPay != 0 ? det.NetPay : colNet`) — quy tắc dự phòng này phải giữ để phiếu cũ hiển thị đúng.

---

## 5. REALTIME, OUTBOX & THÔNG BÁO

### 5.1 Kênh Pub/Sub

```
Kênh NOTIFY : ketoanmini_changes
Trigger     : ketoanmini_publish_change  — mức STATEMENT, gắn trên 96 bảng
Thân hàm    : FOREACH scope_name IN ARRAY TG_ARGV LOOP PERFORM pg_notify('ketoanmini_changes', scope_name); END LOOP
```

12 scope hợp lệ: `data, presence, hr, tasks, portal, config, audit, talent, release, feedback,
attendance, notify`. Payload lạ bị bỏ qua.

Quy tắc vận hành phải giữ:
- Gộp cửa sổ **100 ms**; riêng `presence` tối đa **1 lần / 15 giây**.
- Hàng chờ gom là **HashSet**, không phải queue — trùng lặp tự tan, hàng chờ tối đa = số scope.
- **Nối lại sau khi rớt** ⇒ broadcast `changed "all"` (PostgreSQL không giữ notify cho phiên đã ngắt).
  Lần LISTEN **đầu tiên** thì không phát.
- Đo hàng chờ NOTIFY mỗi phút, cảnh báo ở **25%** dung lượng. Hàng chờ đầy ⇒ **mọi giao dịch có
  NOTIFY sẽ lỗi lúc COMMIT**, tức người dùng không lưu được dữ liệu.
- Endpoint **không được** gọi hub. Muốn màn hình tự làm mới ⇒ thêm bảng vào danh sách theo dõi.

### 5.2 Sự kiện hub

| Sự kiện | Hướng | Nội dung |
|---|---|---|
| `changed` | server → all | tên scope (hoặc `"all"`) — **không kèm dữ liệu nghiệp vụ** |
| `signal` | server → 1 user | trung chuyển WebRTC: `(fromUsername, payload)` |
| `kicked` | server → 1 user | phiên bị đá do đăng nhập máy khác |
| `feedbackResolved` | server → 1 user | phản hồi đã xử lý |
| `Relay(toUsername, payload)` | client → server | **≤ 120 gói / 5 giây / kết nối**, payload **≤ 64 KB**; vượt ⇒ âm thầm bỏ |

Hiện diện: hub đánh dấu `user_sessions.last_seen` theo `sid`; worker làm tươi theo lô mỗi **45 giây**;
ngưỡng offline **90 giây**. Ngắt kết nối **không** ghi `is_active=false`.

### 5.3 Outbox (việc-có-hậu-quả)

Bảng `app_outbox`. Endpoint chỉ **ghi một dòng** rồi trả về ngay; worker mới gọi FCM.
Lease **2 phút**, retry lũy thừa, dedupe, TTL, dọn token chết. Lý do không dùng LISTEN/NOTIFY:
notify **không bền** — mất kết nối là mất tin.

`PushService` là **cửa duy nhất** ghi hộp thư `web_notifications`. Ánh xạ target → đường dẫn web và
target → nhóm thông báo nằm ở mục C.33/B.3 của `backend-inventory.md`.

Nhóm tắt được: `delivery, collection, accounting, work, people`.
**Không tắt được**: `security`, `system`, `chat`. Khoá lưu `web_user_preferences["notifyGroup.<g>"]`,
**không có dòng = BẬT**.

---

## 6. NHỮNG BẤT BIẾN DỄ ĐÁNH RƠI NHẤT

Danh sách rút gọn để dán vào PR checklist của bản viết lại:

1. **Middleware làm tươi danh tính phải nằm GIỮA xác thực và phân quyền.** Đặt sau ⇒ quyền đã được
   chấm bằng claim cũ trong JWT trước khi kịp làm tươi.
2. **DB lỗi ⇒ không có quyền nào** (đóng mặc định) **nhưng không đá người dùng ra** (fail-open danh tính).
3. Endpoint kiosk `AllowAnonymous` **vẫn phải kiểm chứng JWT nếu request có mang JWT**; không kiểm
   chứng được ⇒ **503**, không phải cho qua.
4. Header `X-Background-Poll` ⇒ **không** làm tươi `last_seen`, **không** gia hạn cookie. Thiếu chốt
   này thì vòng poll nền của app giữ phiên sống mãi.
5. Phiên hết hiệu lực ⇒ **xoá cookie**, nếu không người dùng kẹt trong vòng 401 vô tận.
6. Một tài khoản **một máy**: đăng nhập máy mới ⇒ thu hồi phiên cũ + `kicked` + heartbeat 401.
   **Mất mạng KHÔNG đăng xuất** — chỉ 401 mới đá.
7. Phiếu đã phát hành: **không đổi `voucher_no`**, **không xoá vật lý** (trigger CSDL cưỡng chế).
8. `document_issued_lines` **bất biến** — nó là con số trên tờ giấy đã in.
9. Mỗi phiếu có **đúng một** việc giao hàng còn sống.
10. Hàng trả về: một dòng chỉ đi **đúng một** trong hai đường ghi sổ; tổng đã trả ≤ số đã bán.
11. Phiếu chi: **chưa quét QR ký nhận thì không duyệt chi được**.
12. Mọi thao tác tiền còn phải kiểm phòng ban `is_accounting` — **Admin bị loại** khỏi các quyền
    `collections.*` và `cashfund.manage`.
13. **Không** thêm lại 2 chốt bất kiêm nhiệm ở lệnh thu tiền (đã cố ý bỏ).
14. Việc giao hàng **không có** chặng nghiệm thu.
15. Khảo sát: **không lưu username**, chỉ HMAC.
16. Audit: Kế toán xem được **phần tiền** — ngoại lệ có chủ ý của "audit chỉ admin".
17. `@x IS NULL OR col = @x` với NULL **phải ép kiểu** (`::uuid`, `::date`), nếu không Npgsql/Postgres
    trả `42P08` → client thấy "mất kết nối DB" giả.
18. Lọc theo tháng phải dùng **khoảng ngày**, không `to_char` (giết index).

---

## 7. NHỮNG THỨ NÊN SỬA *TRONG* LÚC PORT (nợ đã biết)

| Nợ | Xử lý đề xuất |
|---|---|
| 0 ràng buộc CHECK trong CSDL | đẩy enum trạng thái + số tiền không âm xuống CHECK |
| Hai bộ bảng khảo sát (`app_surveys*` và `surveys/survey_*`) | hợp nhất một bộ |
| Ba bảng phản hồi (`app_feedbacks`, `app_general_feedback`, `app_support_tickets`) | một bảng + cột `kind` |
| Hai luồng QR login gần trùng (15 endpoint) | một service tham số hoá theo hướng, **giữ nguyên 15 route** |
| 25 `EnsureTables` + 9 migration song song | một migration runner có version, fail-closed |
| Trạng thái là chuỗi rải rác (`work_tasks`, `hr_requests` dùng literal trần) | enum + bảng chuyển trạng thái hợp lệ |
| Chỉ 10 mã lỗi máy-đọc-được | cấp mã cho mọi lỗi nghiệp vụ, giữ `message` làm phần hiển thị |
| "Nợ bảo mật tương thích": JWT hợp lệ vẫn qua khi `sid` không có dòng `user_sessions` | **không** siết chung đợt port — làm thành đợt phát lại token riêng |

---

## 8. LỘ TRÌNH PORT & TIÊU CHÍ "XONG"

### 8.1 Việc bắt buộc làm TRƯỚC — chụp hợp đồng response

Vì spec không mô tả được response nào (mục 0.2), phải tạo **golden test** trên bản .NET hiện tại:

1. Dựng CSDL hạt giống cố định (seed script, dữ liệu tất định — không dùng `now()`).
2. Với mỗi trong **396 operation**: gọi bằng vài vai trò tiêu biểu (Admin, Kế toán, Nhân viên, Lái xe,
   Kiosk), ghi lại **status + header + body JSON** vào `tests/golden/<method>_<path>.json`.
3. Bản viết lại chạy **cùng bộ golden** và phải khớp từng byte (sau khi chuẩn hoá thứ tự khoá và các
   trường thời gian sinh động).

Đây cũng chính là thứ bắt được các khác biệt âm thầm: thiếu trường vì `WhenWritingNull`, thiếu `Z`
ở mốc thời gian, đổi camelCase, đổi thứ tự mảng.

### 8.2 Thứ tự port (đi theo rủi ro tăng dần)

1. **Danh tính / phiên / RBAC + các API chỉ đọc** — nhiều thứ nhất, ít hậu quả nhất.
2. **CRUD ít tác dụng phụ**, port trọn từng route group.
3. **Đơn từ, ca/bảng công, nhân sự, máy trạng thái giao việc.**
4. **Kế toán, lệnh thu, phiếu chi, lương, phạt** — kèm test tương tranh (`FOR UPDATE`, advisory lock).
5. **Upload/download tệp** — sau khi chốt **một** chủ sở hữu cho volume.
6. **Realtime / worker / sidecar native** — sau cùng, sau khi golden test đã xanh.

### 8.3 Bất biến trong suốt quá trình chuyển đổi

1. Mỗi domain chỉ có **một writer** tại một thời điểm — **không dual-write**.
2. Chỉ **một** migration runner. Giai đoạn tương thích: .NET giữ quyền sở hữu schema, bản mới chỉ
   fail startup nếu schema thiếu, **tuyệt đối không chạy DDL**.
3. Workflow phải chuyển **nguyên khối** cùng transaction, `FOR UPDATE`, advisory lock, audit và outbox.
4. Contract test so **status, JSON, content type, header, cookie và binary body** — không chỉ so dữ liệu cuối.
5. DDL trong thời gian còn rollback được phải **additive / tương thích ngược**.
6. PostgreSQL + volume chat + volume APK phải được **snapshot cùng một mốc** trước cutover.
7. Mọi state phía client có dữ liệu theo tài khoản (toast, hội thoại, WebRTC, tệp P2P, callback bất
   đồng bộ) phải bị huỷ hoặc đổi generation tại ranh giới **đăng xuất/đăng nhập** — xoá DOM là chưa đủ.

### 8.4 Tiêu chí "xong" cho một route group

- [ ] Golden test của mọi operation trong group khớp.
- [ ] 56 file test hiện có, phần liên quan tới group, đã được dịch và xanh.
- [ ] Realtime: bảng của group đã nằm trong danh sách trigger, scope phát ra đúng như cũ.
- [ ] Audit: hành động ghi cùng `action`/`entity`/`entity_name` như bản cũ.
- [ ] Outbox/push: cùng `notifId`, `target`, `category` ⇒ cùng đường dẫn deep-link.
- [ ] Đã kiểm bằng **cả web lẫn APK thật**, không chỉ curl.
