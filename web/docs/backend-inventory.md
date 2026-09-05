# KIỂM KÊ CHỨC NĂNG BACKEND (nguồn chuẩn cho tái cấu trúc)

> Lưu ý: các số liệu và mục communication trong tài liệu này là snapshot trước khi tách. Chat,
> P2P, voice/video call và signaling hiện nằm ở `communication-standalone/`; OpenAPI và schema đi
> kèm đã được cập nhật để chỉ mô tả host nghiệp vụ hiện tại.

Lập ngày 2026-08-28 bằng cách đọc trực tiếp mã nguồn `backend/KetoanMini.Api` (49.878 dòng C#).
Mục tiêu: liệt kê **không sót chức năng nào**, kể cả nhỏ nhất, để việc tái cấu trúc không đánh rơi
hành vi đang chạy. Mọi con số dưới đây đếm từ mã, không phải ước lượng.

> **Đã kiểm chứng lại 2026-08-28 (lần 2)** bằng cách chạy thật ứng dụng + `pg_dump` CSDL đang dùng.
> Kèm theo tài liệu này:
> - [`backend-schema.sql`](backend-schema.sql) — DDL thật (pg_dump schema-only, 6.477 dòng).
> - [`openapi.baseline.json`](openapi.baseline.json) — hợp đồng API sinh từ ứng dụng đang chạy.
> - [`backend-port-spec.md`](backend-port-spec.md) — **đặc tả để viết lại BE bằng ngôn ngữ khác**.
>
> Sửa so với bản đầu: bảng là **114** (không phải 112); bổ sung **5 trigger toàn vẹn ở CSDL** mà
> bản đầu bỏ sót; ngưỡng tăng ca là **15 phút** (ra từ 17:15), không phải 17:20.

| Hạng mục | Số lượng |
|---|---|
| Endpoint HTTP | **396** (394 trong module + 2 ở `Program.cs`) — *đã đối chiếu với OpenAPI runtime: khớp* |
| Đường dẫn duy nhất (path) | 317 · GET 162 · POST 157 · PUT 46 · DELETE 31 |
| Nhóm route (`MapGroup`) | 39 |
| Bảng PostgreSQL | **114** |
| View PostgreSQL | 2 (`cash_fund_ledger`, `hr_effective_attendance_log`) |
| Index / PK / UNIQUE / FK / CHECK | 126 / 114 / 19 / 71 / **0** |
| Trigger / hàm plpgsql | 101 (96 realtime + 5 toàn vẹn) / 6 |
| Quyền (permission) | 45 |
| Vai trò | 12 |
| Hosted service chạy nền | 9 |
| Hàm `EnsureTables` (DDL lúc khởi động) | 25 |
| Migration có version | 9 |
| Lệnh SQL thô (`.Cmd(`) | 837 |
| File test | 56 |

---

## PHẦN A — KIẾN TRÚC HIỆN TẠI

### A.1 Hai tiến trình

```
Web (React) + Android (Kotlin native)
        │
        ├── ketoanmini-server (Rust/Axum, :5240)  ← CỔNG, không phải bản thay thế
        │      ├── route đã port native ──────── PostgreSQL
        │      └── compat proxy (stream) ───────┐
        │                                       ▼
        └────────────────────────────► ASP.NET Core :5239 / :5443  (BE CHÍNH)
                                              ├── SignalR /hubs/changes
                                              ├── FCM / outbox
                                              ├── Face engine (ONNX)
                                              ├── Excel COM → máy in vật lý
                                              └── wwwroot (SPA build)
```

- **ASP.NET Core** (`backend/KetoanMini.Api`) là **toàn bộ** nghiệp vụ — đây là thứ cần tái cấu trúc.
- **Rust** (`backend/KetoanMini.Rust`) là gateway đã port native **68/396 operation** trong 16 module
  đọc/CRUD nhẹ: `app_config, auth, bank_accounts, catalogs, directory, gia_cong, help, notifications,
  penalty_refunds, portal, preferences, schedule, surveys, system, talent, worklist`. Schema owner vẫn là .NET;
  Rust chỉ fail startup nếu thiếu bảng, tuyệt đối không chạy DDL.
- API chạy chung một cổng với frontend: `UseDefaultFiles` + `UseStaticFiles(wwwroot)` +
  `MapFallbackToFile("index.html")` cho SPA deep-link.

### A.2 Pipeline middleware — **thứ tự là nghiệp vụ, không được đảo**

| # | Middleware | Ghi chú bắt buộc giữ |
|---|---|---|
| 1 | `ProductionSecurityValidator.Validate` | chặn boot nếu secret yếu |
| 2 | `Kestrel MaxRequestBodySize` | trần mặc định `PayloadLimits.MaxJsonBodyBytes` |
| 3 | Swagger (`UseSwagger` mọi env, `SwaggerUI` chỉ Development) | |
| 4 | `UseForwardedHeaders` (chỉ tin loopback) | vì Cloudflare Tunnel → cloudflared cùng máy |
| 5 | Security headers + CSP | bật/tắt `Security:SecurityHeaders`, CSP đổi được qua config |
| 6 | Chặn `/api/auth/forgot-password-face` ngoài Development → 404 | trước cả model binding |
| 7 | Trần payload **theo từng endpoint** (`PayloadLimits.MaxRequestBytesFor`) → 413 | áp trần JSON chung sẽ chặn nhầm upload APK |
| 8 | `UseHsts` (ngoài Dev) + `UseHttpsRedirection` | |
| 9 | Static files (`.tflite`, `.task`, `.wasm` có MIME riêng; `index.html` no-store) | |
| 10 | `UseRouting` → `UseCors` → `UseRateLimiter` → `UseAuthentication` | |
| 11 | **Chống CSRF double-submit** (`km_csrf` + `X-CSRF-Token`) chỉ với phiên cookie | `/hubs` **không** đi qua CSRF, chốt bằng **Origin** thay thế (chống Cross-Site WebSocket Hijacking) |
| 12 | **Middleware làm tươi danh tính** (xem A.3) | **PHẢI nằm giữa** `UseAuthentication` và `UseAuthorization` |
| 13 | `UseAuthorization` | |
| 14 | Bắt `NpgsqlException` → 503 JSON chung | không lộ chi tiết exception |

### A.3 Middleware làm tươi danh tính (trái tim của phân quyền)

Mỗi request đã xác thực, **một truy vấn duy nhất** đọc từ DB rồi quyết định:

- `locked` — `app_users.is_active=false` hoặc `is_deleted` → 401 “Tài khoản đã bị khóa.”
- `revoked` — `user_sessions.revoked` → 401 “Thiết bị này đã bị thu hồi.”
- `idleExpired` — `last_seen` cũ hơn `Security:SessionIdleDays` (mặc định 7) → 401
- `sessionInactive` — phiên đã kết thúc → 401
- Làm tươi `last_seen` (giới hạn 2 phút/lần), **bỏ qua** khi có header `X-Background-Poll`
- **Xoá sạch claim quyền cũ và dựng lại từ DB.** JWT sống 365 ngày nên claim vai trò trong token
  không đáng tin. DB lỗi → **không có quyền nào** (đóng mặc định) nhưng **không đá người dùng ra**
  (fail-open danh tính, fail-closed đặc quyền).
- Gia hạn trượt cookie web khi đã qua nửa vòng đời.
- Endpoint kiosk (`/api/chamcong/nhandien|cham|trangthai`) tuy `AllowAnonymous` nhưng **vẫn** bị
  kiểm chứng nếu request có mang JWT; không kiểm chứng được → **503**, khoá an toàn.

### A.4 Xác thực & phiên

| Kênh | Cơ chế | Hạn |
|---|---|---|
| Android | `Authorization: Bearer` | `Jwt:ExpireHours` = 8760h (365 ngày) |
| Trình duyệt | cookie HttpOnly `km_auth` + `km_csrf` | `Jwt:WebExpireHours` = 168h (7 ngày), gia hạn trượt |
| SignalR (app native) | query `access_token`, **chỉ** cho path `/hubs` | |
| SignalR (web) | cookie tự đi theo handshake | |

Thứ tự tìm token: Header → Cookie → query (chỉ `/hubs`). **Cố ý không có refresh token.**
**Một tài khoản chỉ một máy**: đăng nhập máy mới thu hồi phiên cũ + bắn SignalR `kicked` + heartbeat 401.

### A.5 Rate limit (10 policy, tính theo IP)

`login` 40/1p · `login-bootstrap` 180/1p · `reauth` 40/5p · `app-pin` 120/1p · `qr-start` 20/1p ·
`qr-poll` 1200/1p · `qr-confirm` 30/1p · `qr-action` 120/1p · `face-reset` 8/10p ·
`attendance` 90/1p · `signalr` 120/1p.
Lý do hạn mức rộng: cả văn phòng dùng chung một IP công khai sau Cloudflare Tunnel + NAT.

### A.6 Realtime

- Hub duy nhất `/hubs/changes` (`RequireAuthorization` + rate limit `signalr`).
- **Quy tắc bất di bất dịch**: endpoint **KHÔNG** được gọi hub. Muốn màn hình tự làm mới →
  thêm bảng vào `DatabaseChangePublisher.Watched`. Trigger PostgreSQL mức STATEMENT phát
  `NOTIFY ketoanmini_changes` sau khi commit; `ChangeWatcher` LISTEN rồi broadcast `changed <scope>`.
  Quy tắc này được `RealtimeCoverageTests` cưỡng chế.
- 18 chủ đề hợp lệ: `sales, debts, cash, purchases, catalog, hr, attendance, presence, tasks,
  portal, config, audit, release, feedback, talent, notify, liveness, access`. Danh sách chốt ở
  `RealtimeEventStore.KnownTopics`.
- Năm chủ đề kế toán đầu tiên trước đây là một chủ đề gộp tên `data`; chẻ ra kèm bộ lọc theo kết nối
  (`GET /api/realtime/stream?topics=...`) để một lần sửa phiếu không đánh thức mọi màn hình đang mở.
- `presence` bị gộp tối đa 1 lần/15 giây (chống bùng nổ N²).
- Nối lại sau khi rớt → broadcast `changed "all"` để client nạp lại (PostgreSQL không giữ notify).
- Method của hub: `Relay(toUsername, payload)` — trung chuyển bắt tay WebRTC (gửi tệp P2P + gọi
  thoại/video). Giới hạn 120 gói/5 giây/kết nối, payload ≤ 64KB. Media đi thẳng P2P (DTLS-SRTP).
- Sự kiện gửi tới client: `changed`, `signal`, `kicked`, `feedbackResolved`.
- Hiện diện online: chính kết nối SignalR đánh dấu `user_sessions.last_seen`; `HubPresenceRefresher`
  làm tươi theo lô mỗi 45s. Ngắt kết nối **không** ghi `is_active=false` — để `last_seen` tự cũ đi
  sau 90s.

---

## PHẦN B — HẠ TẦNG XUYÊN SUỐT

### B.1 Mô hình phân quyền (`Security/`)

**12 vai trò** (`AppRoles`): `Admin, Executive, ChiefAccountant, Accounting, Payroll, Cashier,
Warehouse, Hr, Manager, Driver, Employee, Kiosk`.
- `Assignable` (gắn được cho hồ sơ nhân sự): tất cả trừ `Kiosk`.
- `Secondary` (vai trò phụ qua bảng `user_roles`): `Warehouse, Manager, ChiefAccountant, Accounting,
  Payroll, Cashier, Hr, Driver` — **cố ý không có Admin/Kiosk**.
- `PrimaryPriority` quyết định vai trò chính khi kiêm nhiệm; `Normalize` nhận cả tên tiếng Việt
  có dấu/không dấu.

**45 quyền** (`Permissions`) nhóm theo module:
`users.read/manage`, `roles.manage`, `system.settings.manage`, `system.releases.manage`,
`scope.company.all`, `audit.read`, `accounting.access`, `vouchers.read/create/update/approve/cancel`,
`payout.read/create/approve/pay`, `collections.self/read.all/create/receive/resolve`,
`cashfund.read/manage`, `report.read/export`, `attendance.self/read/manage/kiosk`,
`payroll.read/manage`, `hr.self.access/read/manage`, `requests.self/approve/manage`,
`penalty.read/manage`, `tasks.self/assign`, `portal.read/manage`, `chat.access`.

**Bảng vai trò → quyền** là chỗ **duy nhất** quyết định vai trò làm được gì. Điểm cần giữ khi tái
cấu trúc:
- `Admin` = mọi quyền **trừ** nhóm `collections.*` và `cashfund.manage` (giữ tiền mặt phải theo chức
  danh, Admin phải kiêm Kế toán/Lái xe mới tham gia).
- `ChiefAccountant` = **spread** `AccountingPermissions` + duyệt — không sao chép danh sách, để quyền
  kế toán mới tự động có ở kế toán trưởng.
- `Cashier` chỉ thực chi, không lập/duyệt. `Payroll` tách riêng, không nhận toàn bộ quyền kế toán.
- `Executive` chỉ đọc phạm vi toàn công ty. `Kiosk` chỉ có `attendance.kiosk`.

**Phạm vi dữ liệu** (`AccessScope`): `Self | Department | Branch | All` + `DepartmentId`/`LocationId`.
Quyền mở **CỬA**, scope mở **PHẠM VI** — hai thứ tách rời.

**`AccessProfileDto`** là thứ **duy nhất** client được dùng để dựng giao diện:
username, fullName, primaryRole, roles[], roleLabels[], permissions[], scope, departmentId,
locationId, uiProfile, landingPath, **authorizationVersion** (tăng mỗi lần quyền đổi).

Các file phụ trợ: `AccessProfileService`, `EmployeePositionRoleService`, `PermissionDirectory`,
`PermissionEndpoints` (extension `.RequirePermission`), `AppPinPolicy`, `AuthCookies`, `CorsPolicy`,
`FieldCipher` (AES-GCM `KME1` cho embedding), `KioskAccess`, `LoginBootstrapService`,
`PasswordHasher`, `PayloadLimits`, `ProductionSecurityValidator`, `RecoveryCodes`, `TokenService`.

### B.2 Tiến trình nền (9 hosted service)

| Service | Việc | Nhịp |
|---|---|---|
| `QrLoginService` | kho phiên đăng nhập QR trong RAM (5 phút, chỉ giữ SHA-256 token) + tự dọn | nền |
| `OutboxWorker` | rút việc khỏi `app_outbox`, giao `IOutboxHandler` (`PushOutboxHandler` → FCM) | lease 2 phút, retry lũy thừa |
| `AttendanceReminderWorker` | reconcile bền nhắc thiếu giờ Ra; ledger `hr_attendance_reminders` là nguồn idempotency | định kỳ |
| `FaceEngineIdleUnloader` | thả model AdaFace sau `FaceRecognition:IdleUnloadMinutes` (mặc định 10) | ~348MB RAM ≈ 79% tiến trình |
| `HubPresenceRefresher` | làm tươi `last_seen` theo lô cho kết nối đang mở socket | 45s |
| `ChangeWatcher` | LISTEN/NOTIFY → SignalR broadcast; cảnh báo hàng chờ NOTIFY > 25% | liên tục, đo hàng chờ mỗi 1 phút |
| `LanFileCleanupService` | dọn blob file thường quá hạn + mồ côi (voice/ảnh/video **không** bị sweep) | 1 giờ |
| `FaceEnrollmentCleanupService` | xoá mẫu sinh trắc chờ HR duyệt quá 14 ngày | quét ngay lúc boot + mỗi giờ |

### B.3 Dịch vụ nghiệp vụ (`Services/`)

- `PushService` — **cửa duy nhất** ghi hộp thư `web_notifications` + bắn FCM.
  API: `SendToUserAsync`, `SendToPermissionAsync`, `SendWebOnlyToPermissionAsync`, `SendToAllAsync`,
  `SendToAdminsAsync`, `SendToEmployeeAsync`, `SendCallInviteAsync`, `SendCallCancelAsync`.
  Ánh xạ target → đường dẫn web: `Tasks→/cong-viec`, `CashCollection→/lenh-thu-tien`,
  `Approval→/pheduyet`, `Requests→/dontu`, `Penalty→/phat`, `Attendance→/chamcong`,
  `Settings→/caidat`, `AppUpdate→/tai-apk`, `Chat→/chats`.
- `NotificationGroups` — 5 nhóm tắt được: `delivery, collection, accounting, work, people`.
  **Không tắt được**: `security` (đăng nhập máy mới), `system`, `chat`. Chốt ở **máy chủ** nên tắt
  là im cả chuông web lẫn rung điện thoại. Khoá lưu: `web_user_preferences["notifyGroup.<group>"]`;
  không có dòng = BẬT.
- `ManagementFeed` — bảng tin điều hành cho người có `attendance.read` hoặc `hr.read` + admin.
  **Chỉ ghi chuông web, không FCM**. Người tự gây sự kiện không nhận tin của chính mình.
- `WorkforceAvailability` — nguồn sự thật duy nhất "hôm nay ai có mặt để nhận việc". **Không xoá**
  người khỏi danh sách, trả kèm `Label` ("Chưa chấm công", "Đang nghỉ phép").
- `AttendancePolicy` — múi giờ VN cho `AT TIME ZONE`, quy đổi mốc VN → UTC.
- `AttendancePreviewTokens` — token dùng-một-lần giữ kết quả nhận diện ở bước xem trước; bước xác
  nhận không suy luận lại (tiết kiệm 50% inference mỗi lượt chấm).
- `ChatAttachmentPolicy` — file thường = giữ tạm; voice/ảnh/video = nội dung tin nhắn, chỉ xoá khi gỡ tin.
- `QrActionService` / `QrActionTokenService` / `QrConfiguredActionRegistry` — vé QR tự chứa mã hoá
  bằng Data Protection, gắn tài khoản + phiên app, tự hết hạn.
- `ReleaseStorage` — APK trên **đĩa** (buffer 80KB, sendfile), DB chỉ giữ metadata;
  `MigrateDatabaseBlobsAsync` chuyển bản cũ từ `bytea` ra đĩa lúc boot.
- `WarehouseVoucherPrintService` — điền workbook mẫu rồi Excel COM in thẳng ra máy in mặc định.
- Face: `IFaceEngine` ← `LazyFaceEngine` ← `AdaFaceR50Engine` (YuNet + căn chỉnh 5 điểm + AdaFace R50
  ONNX); `SilentFaceLiveness` (2 model MiniFASNetV2 2.7 + MiniFASNetV1SE 4.0, softmax cộng, argmax==1
  ⇒ thật); `LivenessMetricsLog` (vòng đệm RAM số đo để hiệu chỉnh ngưỡng).
- `OutboxQueue` / `OutboxWorker` / `PushOutboxHandler` — hàng chờ bền cho việc-có-hậu-quả.
  Lý do không dùng LISTEN/NOTIFY: notify **không bền**, mất kết nối là mất tin.

---

## PHẦN C — DANH MỤC CHỨC NĂNG THEO MODULE (396 endpoint)

Ký hiệu: `[P:x]` = quyền chốt ở nhóm route · `[A]` = ẩn danh · `[Auth]` = chỉ cần đăng nhập ·
`[inline]` = quyền/phạm vi kiểm tra **bên trong handler** (không ở khai báo route).

### C.1 Hạ tầng — `Program.cs` (2)
- `GET /api/info` — tên + trạng thái app.
- `GET /api/health` — kiểm tra DB; **chỉ trả về từ loopback/LAN**, client ngoài nhận 404.

### C.2 Xác thực & tài khoản — `AuthEndpoints.cs` (38) — nhóm `/api/auth`

**Đăng nhập**
- `POST /bootstrap` `[A]` — dữ liệu tiền-xác-thực khi mở trang Login (rate `login-bootstrap`).
- `POST /login` `[A]` — đăng nhập user/mật khẩu (rate `login`).

**Đăng nhập QR cho WEB (app quét mã giúp)** — 8 endpoint
`POST /qr/start` `[A]` · `/qr/scan` `[Auth]` · `/qr/confirm` `[Auth]` · `/qr/account` `[Auth]` ·
`/qr/reject` `[Auth]` · `/qr/poll` `[A]` · `/qr/ack` `[A]` · `/qr/cancel` `[A]`

**Đăng nhập QR cho APP (web hiển thị mã)** — 7 endpoint
`POST /app-login/start` `[A]` · `/resolve` `[A]` · `/confirm` `[Auth]` · `/reject` `[Auth]` ·
`/poll` `[A]` · `/ack` `[A]` · `/cancel` `[A]`

**Quên mật khẩu** — 3 endpoint (rate `face-reset`)
- `POST /forgot-password-face` `[A]` — **bị middleware trả 404 ngoài Development**.
- `POST /reset-with-recovery-code` `[A]`, `POST /verify-recovery-code` `[A]` — luồng OTP 3 bước / 5 ô.

**Hồ sơ cá nhân**
- `GET /me`, `GET /access-profile` — hồ sơ truy cập server tính lại từ DB.
- `PUT /profile`, `PUT /avatar`, `DELETE /avatar`.
- `POST /change-password`, `POST /verify-password` (rate `reauth`).

**Mã bảo mật 6 số của APK** (lưu **ở máy chủ**, bảng `app_pin_codes`)
- `GET /app-pin` (trạng thái), `POST /app-pin` (đặt), `POST /app-pin/verify` (rate `app-pin`),
  `POST /app-pin/reset` (rate `reauth`). Khoá thử sai đếm **theo tài khoản**, trả 423 + số giây còn lại.

**Phiên & thiết bị**
- `POST /heartbeat`, `POST /logout`.
- `GET /devices`, `POST /devices/{sid}/revoke`, `POST /devices/revoke-all`
  (revoke-all cũng xoá sạch `hr_device_tokens`).
- `GET /account-settings`, `PUT /account-settings` — bật/tắt đăng nhập web cho chính tài khoản.

### C.3 QR do server điều khiển — `QrActionEndpoints.cs` (2) — nhóm `/api/qr`
- `POST /resolve` — server quyết định nội dung mã; trả `unhandled` thì APK mới tự đọc thô.
- `POST /decision` — thực thi action (`message`, `open_https_url` với host allowlist).
Rate `qr-action`. Kind action cấu hình trong `QrScanner:Actions`, **đổi không cần build lại APK**.

### C.4 Kế toán / chứng từ — `AccountingEndpoints.cs` (29) — nhóm `/api` `[P:accounting.access]`

- `GET /dashboard`, `GET /accounting/system-status`, `GET /reports`
- Phiếu (`documents`): `GET /documents`, `GET /documents/stack`, `GET /documents/{id}`,
  `POST /documents`, `PUT /documents/{id}`, `PUT /documents/{id}/cancel` `[P:vouchers.cancel]`,
  `DELETE /documents/{id}` `[P:vouchers.cancel]`
- In kho: `POST /documents/{id}/warehouse-print` (Excel COM → máy in),
  `GET /documents/{id}/warehouse-preview`
- Phiếu thu/chi (`cash-vouchers`): `GET /`, `GET /{id}`, `POST /`, `PUT /{id}`,
  `PUT /{id}/issued`, `PUT /{id}/cancel` `[P:vouchers.cancel]`, `DELETE /{id}` `[P:vouchers.cancel]`,
  `DELETE /{id}/permanent` `[P:vouchers.cancel]`
- Khách hàng: `GET /customers`, `GET /customers/{id}/report`, `POST`, `PUT`, `DELETE`
- Công nợ: `GET /debts`, `GET /debts/{customerId}`, `PUT /debts/{customerId}/opening-balance`,
  `POST /debts/{customerId}/payments`

### C.5 Kế toán lõi (sổ kép) — ĐÃ GỠ
Nhóm `/api/core-accounting` và bảy bảng `core_*` đã bị xoá (migration `011_drop_core_accounting`).
Mọi số liệu tổng hợp trong hệ dẫn xuất lúc đọc từ chứng từ gốc, nên sổ kép chỉ là bản sao thứ hai
của cùng sự thật, lại phải chạy bằng một nút bấm tay theo kỳ với định khoản gán cứng. Bản viết lại
KHÔNG dựng lại nhóm này; nếu cần báo cáo tài chính thì dựng thẳng từ chứng từ.

### C.6 Gia công — `GiaCongEndpoints.cs` (6) — nhóm `/api/giacong` `[P:accounting.access]`
`GET /` · `GET /report` (tổng hợp Xuất/Nhập gia công theo đối tác + hàng hoá) · `GET /{id}` ·
`POST /` · `PUT /{id}` · `DELETE /{id}`

### C.7 Danh mục hàng hoá — `ProductCatalogEndpoints.cs` (5) `[P:accounting.access]`
`GET /api/products` · `GET /api/products/suggestions` (gợi ý dựng từ phiếu cũ) · `POST /api/products` ·
`PUT /api/products/{id}` · `POST /api/products/import`
Nguyên tắc: **gợi ý, không ép** — ô nhập trên phiếu vẫn gõ tay được; chọn danh mục thì dòng phiếu
mang theo `product_id`.

### C.8 Mua hàng & nhà cung cấp — `PurchaseEndpoints.cs` (8) `[P:accounting.access]`
`GET/POST /api/suppliers`, `PUT /api/suppliers/{id}` ·
`GET /api/purchases`, `GET /api/purchases/{id}`, `POST`, `PUT /{id}`, `PUT /{id}/cancel`
Bảng riêng `purchases`/`purchase_lines`, **không** dùng chung `documents`. Công nợ phải trả ở mức
`paid_amount` (chưa có sổ chi tiết thanh toán NCC).

### C.9 Giao hàng — `DeliveryAssignmentEndpoints.cs` (4) `[P:accounting.access]`
`GET /api/delivery-assignments/drivers` · `GET /api/delivery-assignments` ·
`GET /api/documents/{id}/delivery` · `POST /api/documents/{id}/delivery`

**Bất biến phải giữ:**
1. Chỉ gán phiếu đã phát hành (`issued_at IS NOT NULL`) và chưa huỷ.
2. Mỗi phiếu có **đúng một** việc giao hàng còn sống (`ux_work_tasks_delivery_document`).
3. Đổi sang "khách lấy tại kho" → huỷ việc giao hàng đang mở.
4. Đổi lái xe được kể cả khi đã `in_progress`/`rejected`, **bắt buộc có lý do**, lái xe cũ được báo.
5. Từ `submitted`/`completed` trở đi **hết đổi**.

### C.10 Đối soát phiếu giao — `DeliverySettlementEndpoints.cs` (3) `[P:accounting.access]`
`GET /api/documents/{id}/settlement` · `PUT /api/documents/{id}/settlement` ·
`POST /api/documents/{id}/settlement/return`

- `document_issued_lines` = ảnh chụp bất biến lúc phát hành (**con số trên tờ giấy**).
- Kế toán sửa `document_lines` thành hàng **thực nhận** → mọi báo cáo/công nợ tự khớp.
- Mỗi dòng đổi đẻ bản ghi cũ→mới ở `document_line_edits` kèm lý do + người sửa.
- Xác nhận "phiếu về kho" → việc giao hàng nhảy thẳng `completed`, **từ bất kỳ chặng nào chưa kết thúc**.
- **Không còn chặng nghiệm thu** cho giao hàng (chốt 2026-08-24).

### C.11 Hàng trả về — `GoodsReturnEndpoints.cs` (5) `[P:accounting.access]`
`GET /api/returns/sources` (truy đơn nguồn để lấy đúng đơn giá) · `POST /api/returns` ·
`GET /api/returns` · `GET /api/returns/{id}` · `PUT /api/returns/{id}/cancel`

Hai đường ghi sổ, **một dòng chỉ đi đúng một đường**:
- Nguồn là chính đơn vừa giao & phiếu chưa chốt về kho ⇒ hạ thẳng số lượng trên đơn đó.
- Còn lại ⇒ sinh phiếu trả hàng riêng (`documents.document_type='return'`).
Bất biến: tổng đã trả của một dòng nguồn **không bao giờ vượt** số đã bán trên dòng đó.

### C.12 Lệnh thu tiền khách hàng — `CashCollectionEndpoints.cs` (12) — nhóm `/api/cash-collections` `[Auth]` + `[inline]`
`GET /customers` · `GET /drivers` · `GET /` · `GET /history` · `GET /{id}` · `POST /` ·
`POST /{id}/accept` · `/fail` · `/collect` · `/receive` · `/resolve` · `/cancel`

**Trạng thái:** `Assigned → Accepted → PendingHandover → Completed`;
nhánh `Failed`, `Variance` (sai lệch, cần `collections.resolve` duyệt), `Cancelled`.
Kế toán giao → tài xế xác nhận số tiền **theo mệnh giá** (`cash_count_sessions`/`cash_count_lines`)
→ thủ quỹ đếm lại → khớp (hoặc sai lệch được duyệt) mới ghi "đã nộp đủ tiền".
**Không thu thập GPS, không lưu địa chỉ khách trong lệnh.**
Đã **cố ý bỏ 2 chốt bất kiêm nhiệm** (tránh kẹt lệnh khi chỉ có 1 người) — đừng tự thêm lại.

### C.13 Quỹ tiền mặt — `CashFundEndpoints.cs` (5) — nhóm `/api/cash-fund` `[Auth]`
`GET /balance` · `GET /` · `GET /entries` · `POST /entries` · `POST /entries/{id}/reverse`
Sổ quỹ là **VIEW hợp nhất** `cash_fund_ledger` đọc thẳng từ: lệnh thu hoàn tất (VÀO), phiếu chi đã
chi (RA), phiếu thu/chi `documents` còn hiệu lực (VÀO/RA), bút toán thủ công (VÀO/RA).
Chọn VIEW **có chủ ý**: chép sang bảng riêng thì mỗi đường sửa lại phải nhớ đồng bộ.

### C.14 Phiếu chi tiền mặt — `PayoutVoucherEndpoints.cs` (15) — nhóm `/api/payout-vouchers` `[Auth]` + `[inline]`
`GET /categories` · `POST /categories` · `PUT /categories/{id}` · `DELETE /categories/{id}` ·
`GET /recipients` · `GET /sources/refunds` · `GET /` · `GET /{id}/history` · `GET /summary` ·
`POST /` · `POST /{id}/qr` · `/approve` · `/complete` · `/reject` · `/cancel`

**Trạng thái:** `AwaitingScan → Confirmed → AwaitingApproval → Approved → Paid`, nhánh
`Rejected`/`Cancelled`. **Chưa quét QR ký nhận thì không duyệt chi được** — đây là chốt chống gian
lận của cả luồng. Tách quyền `payout.create` / `payout.approve` / `payout.pay`, **cộng thêm** ràng
buộc hồ sơ nhân viên thuộc phòng ban `is_accounting` (Admin bị loại).

### C.15 Phạt / kỷ luật — `PenaltyEndpoints.cs` (7) — nhóm `/api/penalties` `[P:penalty.read]`
`GET /types` · `GET /deductions` · `GET /` · `POST /` · `PUT /{id}` · `POST /{id}/waive` · `DELETE /{id}`
Sổ cái `hr_penalty_ledger`: cap theo lương, phần thiếu chuyển kỳ sau, tổng thu ≤ mức phạt,
thu đủ → "Đã tất toán". Khiếu nại chốt theo **đã thu**.

### C.16 Hoàn tiền phạt — `PenaltyRefundEndpoints.cs` (4) — nhóm `/api/penalty-refunds` `[P:penalty.read]`
`GET /` · `POST /{id}/approve` · `/reject` · `/mark-paid`
Người xử lý phải là nhân sự đang hoạt động thuộc phòng ban `is_accounting`.
Hình thức: cộng vào phiếu lương kỳ sau **hoặc** chi tiền mặt (→ sinh nguồn cho phiếu chi).

### C.17 Nhân sự — `HrEndpoints.cs` (42) — nhóm `/api/hr` `[P:hr.self.access]` + **40 chỗ `[inline]`**
- Danh mục: `GET /job-positions` `[P:hr.read]` · `GET/POST/PUT/DELETE /departments[/{id}]` ·
  `GET/POST/PUT/DELETE /locations[/{id}]`
- Của tôi: `GET /me` · `GET/POST /me/documents` · `PUT /me/avatar`
- Kỷ niệm: `GET/PUT /anniversary/template` · `GET /anniversary/my-greeting`
- Nhân viên: `GET /employees` · `GET /employees/{id}` · `POST` · `PUT /{id}` · `DELETE /{id}`
- Hợp đồng: `GET/POST /employees/{id}/contracts` · `PUT/DELETE /contracts/{cid}`
- Tăng lương: `GET/POST /employees/{id}/salary-raises` · `PUT/DELETE /salary-raises/{rid}`
- Phiếu lương: `GET/POST /employees/{id}/payslips` · `DELETE /payslips/{pid}`
- Số phép: `GET/POST /employees/{id}/leave-balances`
- Hồ sơ giấy tờ: `GET/POST /employees/{id}/documents` · `DELETE /documents/{did}`
- Quản lý: `GET /manager/summary` · `/manager/attendance` · `/manager/contracts/expiring` ·
  `/manager/reports` · `/manager/alerts`

### C.18 Đơn từ & phê duyệt — `RequestEndpoints.cs` (13) — nhóm `/api/requests` `[P:requests.self]`
`GET /types` · `GET /` · `GET /inbox-count` · `GET /{id}` · `POST /` · `PUT /{id}` ·
`POST /{id}/approve` · `/reject` · `/cancel` · `/remind` ·
`POST /{id}/attachments` · `GET /{id}/attachments/{attachmentId}` · `PUT /delegations/me`

Engine dùng chung cho **mọi** loại đơn (nghỉ phép, nghỉ ốm, tăng ca, thanh toán, tạm ứng, mua vật tư,
điều chỉnh công, đổi ca `shift_swap`, đăng ký xe/phòng họp…). Chi tiết linh hoạt trong cột `jsonb`.
Luồng nhiều cấp: nhân viên → quản lý trực tiếp → hàng đợi HR, lưu ở `hr_request_approvals`,
có ký xác nhận điện tử + uỷ quyền duyệt (`hr_approval_delegations`).

### C.19 Việc cần làm — `WorklistEndpoints.cs` (1) — `GET /api/worklist/` `[P:requests.self]`
Tổng hợp 5 nguồn: đơn chờ mình duyệt (kèm SLA) · phiếu lương chưa xác nhận · giấy tờ sắp hết hạn ·
hợp đồng sắp hết hạn · thông báo bắt buộc. Mỗi tác vụ có khoá ổn định `kind:id` (tính lại không tạo trùng).

### C.20 Giao việc & nghiệm thu — `TaskAssignmentEndpoints.cs` (14) — nhóm `/api/tasks` `[P:tasks.self]` + 17 `[inline]`
`GET /meta` · `GET /` · `GET /history` · `GET /{id}` · `POST /` · `PUT /{id}` ·
`POST /{id}/start` · `/progress` · `/submit` · `/accept` · `/reject` · `/cancel` · `/comment` ·
`DELETE /{id}`

**Vòng đời chuẩn:** `assigned → in_progress → submitted → accepted`;
`submitted → rejected → in_progress` (nộp lại); bất kỳ chặng chưa kết thúc → `cancelled`.
**Việc giao hàng** (`source_kind='delivery'`) đi đường ngắn hơn: `assigned → in_progress → submitted
→ completed` — **không có chặng nghiệm thu**.

### C.21 Ca làm & bảng công — `ShiftEndpoints.cs` (12)
Nhóm `/api/shifts` `[P:attendance.self]` (10):
`GET /` · `POST /` · `PUT /{id}` · `DELETE /{id}` ·
`GET /assignments` · `POST /assignments` · `DELETE /assignments/{id}` ·
`GET /holidays` · `POST /holidays` · `DELETE /holidays/{id}`

Nhóm `/api/timesheet` `[P:attendance.self]` (2):
`GET /me` · `GET /employee/{id}`

Bảng công đối chiếu `cham_cong_log` (khoá theo `username`) với ca được phân qua view
`hr_effective_attendance_log` để tính đi muộn / về sớm / tăng ca.
Tăng ca (`ShiftEndpoints.CalculateOvertimeMinutes`, đã đối chiếu `OvertimeCalculationTests`):
- **Sáng**: vào trước 08:00 ⇒ số phút trước 08:00.
- **Chiều**: ra sau 17:00 ⇒ số phút sau 17:00.
- Mỗi vế **chỉ tính khi ≥ 15 phút**, và **xét độc lập** (vào 7:50 + ra 17:10 ⇒ 0 phút).
- Ca qua đêm (`is_overnight`) **không** tính tăng ca theo công thức này.
Admin duyệt từng ngày khi lập phiếu.

### C.22 Lịch cá nhân — `ScheduleEndpoints.cs` (1) — `GET /api/schedule/ical` `[Auth]`
Xuất `.ics` lịch ca của **chính mình** cho Google/Apple/Outlook Calendar.

### C.23 Chấm công khuôn mặt — `ChamCongEndpoints.cs` (29) — nhóm `/api/chamcong`

**Chấm công (kiosk)**
- `GET /trangthai` `[A]` · `POST /nhandien` · `POST /cham` — rate `attendance`.
  Ba endpoint này `AllowAnonymous` nhưng bị middleware ép kiểm chứng JWT nếu có; không kiểm chứng
  được → **503 khoá an toàn**. Bảo vệ thêm bằng `Security:KioskApiKey` (header `X-Kiosk-Key`) +
  `KioskAllowLan`.
- Luồng 2 bước: `nhandien` (previewOnly) cấp token → `cham` dùng token, **không suy luận lại**.

**Chính sách Vào/Ra:** lần đầu = Vào; lần sau = Ra lấy **muộn nhất** (ở lại tăng ca cập nhật được
giờ ra; về sớm ghi được giờ ra, không trừ lương).

**Chống giả mạo:** active-flash **cố định luôn bật + chặn thật** (đã gỡ công tắc/endpoint/UI để
chống lỡ tắt) + Silent-Face + kiểm tra mở mắt/nhìn thẳng (client ML Kit nhắc, server enforce bằng
heuristic OpenCV, đo trước/fail-open, status `eyesclosed`).

**Cấu hình nhận diện**
`GET/PUT /motion-config` · `GET/PUT /smile-config` · `PUT /eyeopen-config` ·
`GET /liveness-metrics` · `POST /qr` · `POST /qr-sites`

**Đăng ký khuôn mặt**
- `GET /dadangky` · `GET /dangky/log` · `POST /dangky` (admin) ·
  `GET /dangky/cua-toi` · `POST /dangky/tu` (nhân viên tự đăng ký, **1 lần/tài khoản**, 3 góc) ·
  `GET /face-enrollments` · `POST /face-enrollments/{id}/approve` · `/reject` ·
  `DELETE /dangky/{username}` · `DELETE /dangky/mau/{id}`
- Embedding **mã hoá AES-GCM** (`FieldCipher`, tiền tố `KME1`); bản cũ dạng thô được
  `EncryptExistingEmbeddings` mã hoá lúc boot (Production lỗi ⇒ **dừng khởi động**).

**Chấm công ngoại tuyến (mất điện)** — kiểu chờ duyệt
`GET /log` · `GET /offline/mine` · `GET /offline-policy` · `GET /offline` ·
`POST /offline/{id}/approve` · `/reject` · `GET/PUT /offline-config`
Có cờ rủi ro: lùi giờ máy / không cùng LAN / ngoài geofence — chống "chấm ở nhà rồi đồng bộ".

### C.24 Bảng lương — `PayrollEndpoints.cs` (15) — nhóm `/api/payroll` `[Auth]` + 8 `[inline]`
- Cấu trúc lương: `GET /salaries` · `GET /salaries/{employeeId}` · `PUT /salaries/{employeeId}`
- Của tôi: `GET /my-estimate` (lương dự tính, gồm phạt) · `GET /my-day` (nhật ký ngày) ·
  `GET /my-payslips` · `GET /my-payslips/requirement` · `POST /my-payslips/{id}/ack` ·
  `POST /my-payslips/{id}/inquiries` (khiếu nại) · `GET /my-payslips/{id}/pdf`
- Quản trị: `GET /payslips/published` · `GET /compute` · `GET /payslips/history` ·
  `POST /payslips` · `GET /export` (ClosedXML: mỗi NV 1 sheet + phiếu lương 6/A4)

Lương cứng dẫn xuất từ hợp đồng + các kỳ tăng lương (`hr_salary_raises`).
Tính = mức lương + tổng hợp bảng công (ngày công, giờ tăng ca) + phạt khấu trừ trong kỳ.

### C.25 Tài khoản ngân hàng — `BankAccountEndpoints.cs` (6) — nhóm `/api/bank-accounts` `[P:hr.self.access]`
`GET /banks` (hiện chỉ Vietcombank & Sacombank) · `GET /` · `POST /` · `PUT /{id}` ·
`POST /{id}/default` · `DELETE /{id}`

### C.26 Phát triển nhân sự — `TalentEndpoints.cs` (9) — nhóm `/api/talent` `[Auth]`
`GET /onboarding` · `POST /onboarding/{id}/complete` ·
`GET /performance` · `PUT /performance/goals/{id}` · `PUT /performance/reviews/{id}/self` ·
`GET /training` · `PUT /training/{id}/progress` · `POST /training/{id}/quiz` ·
`GET /benefits`

### C.27 Danh bạ & sơ đồ tổ chức — `DirectoryEndpoints.cs` (2) — nhóm `/api/directory` `[Auth]`
`GET /` (tìm theo tên/chức vụ **tiếng Việt không dấu**, lọc phòng ban, trạng thái online) ·
`GET /org-chart` (cây theo `manager_id`).
Phân quyền xem SĐT/email: Admin/HR xem tất cả; quản lý xem nhân viên mình; người khác chỉ thấy
tên/chức vụ/phòng ban; bản thân luôn xem được.

### C.28 Trò chuyện & gọi — `ChatEndpoints.cs` (26) — nhóm `/api/chat` `[P:chat.access]` + 14 `[inline]`
**Gọi thoại/video (P2P WebRTC)**
`POST /call/ring` · `GET /call/turn` (credential TURN Cloudflare/coturn có hạn giờ) ·
`POST /call/cancel` · `POST /call/missed` · `GET /call/missed` · `POST /call/missed/seen` ·
`POST /call/history` · `GET /call/history`

**Hội thoại & tin nhắn**
`GET /contacts` · `GET /conversations` · `POST /direct/{username}` · `POST /support/{username}` ·
`GET /conversations/{id}/messages` · `POST /conversations/{id}/messages` ·
`POST /conversations/{id}/messages/file` · `POST /.../{msgId}/upload` · `GET /.../{msgId}/download` ·
`PUT /.../{msgId}` · `DELETE /.../{msgId}` · `POST /.../{msgId}/react` ·
`POST /conversations/{id}/read` · `/pin` · `/hide` · `DELETE /conversations/{id}` ·
`POST /conversations/{id}/report` · `GET /db-usage`

Gửi tệp: WebRTC P2P qua LAN + **hybrid store-and-forward** (người nhận offline: server giữ tạm
≤100MB/7 ngày, nhận xong xoá). Voice/ảnh/video là nội dung bền, không bị sweep TTL.

### C.29 Phản hồi & hỗ trợ — `FeedbackEndpoints.cs` (9) — nhóm `/api/feedback` `[P:chat.access]`
`GET /` · `POST /attendance` · `POST /{id}/resolve` (bắn `feedbackResolved`) ·
`GET /surveys/open` · `POST /surveys/{id}/responses` ·
`POST /general` · `GET /general/mine` · `POST /support` · `GET /support/mine`
(`app_support_tickets` giữ mã yêu cầu, phiên bản app, loại máy, trạng thái xử lý.)

### C.30 Khảo sát & bình chọn — `SurveyEndpoints.cs` (8) — nhóm `/api/surveys` `[P:portal.read]`
`POST /` · `GET /` · `GET /active` · `GET /{id}` · `POST /{id}/respond` · `GET /{id}/results` ·
`POST /{id}/close` · `DELETE /{id}`
Phản hồi **ẩn danh thực sự**: không lưu username; chống gửi trùng bằng **HMAC(username) một chiều**
(khoá = `Jwt:Key`). Kết quả chỉ ở dạng số đếm / câu trả lời không kèm danh tính.

### C.31 Cổng thông tin — `PortalEndpoints.cs` (7) — nhóm `/api/portal` `[P:portal.read]`
`GET /feed` · `GET /posts` · `POST /posts` · `PUT /posts/{id}` · `DELETE /posts/{id}` ·
`GET /about` · `PUT /about`

### C.32 Trung tâm trợ giúp — `HelpEndpoints.cs` (5) — nhóm `/api/help` `[P:portal.read]`
`GET /faqs` · `POST /faqs` · `PUT /faqs/{id}` · `DELETE /faqs/{id}` · `GET /status`

### C.33 Thông báo — `NotificationEndpoints.cs` (7) — nhóm `/api/notifications` `[Auth]`
`POST /register-token` · `POST /unregister-token` (token FCM là PRIMARY KEY → đổi chủ tự gán lại) ·
`GET /` · `POST /{id}/read` · `POST /read-all` · `DELETE /read` · `DELETE /{id}`
Hộp thư `web_notifications` là **bản sao để đọc**, dọn theo `RetentionDays` lúc khởi động.

### C.34 Tuỳ chọn cá nhân — `PreferenceEndpoints.cs` (4) — nhóm `/api/preferences` `[Auth]`
`GET /` · `PUT /` · `GET /notifications` · `PUT /notifications` (5 nhóm tắt được, chốt server-side)

### C.35 Quản lý tài khoản — `UserEndpoints.cs` (12) — nhóm `/api/users` `[P:users.manage]`
`GET /api/roles/catalog` · `GET /` · `POST /` ·
`POST /{id}/role` (vai trò chính) · `POST /{id}/secondary-role` (vai trò phụ) ·
`POST /{id}/approve` · `/lock` · `/verify` (tích xanh `web_verified_users`) ·
`/diamond` (`web_diamond_members`) · `/reset-password` · `/recovery-code` (mã một lần cho
`/api/auth/reset-with-recovery-code`) · `DELETE /{id}` (xoá mềm + dọn tham chiếu)

### C.36 Bản cập nhật APK — `ReleaseEndpoints.cs` (9)
- Công khai: `GET /api/releases/public/latest` · `GET /api/releases/public/{id}/download` `[A]`
- Người dùng `[Auth]`: `GET /api/releases/latest` · `GET /api/releases/{id}/download`
- Quản trị `[P:system.releases.manage]`: `GET /api/releases/` · `POST /api/releases/`
  (`DisableAntiforgery`, trần riêng `PayloadLimits.MaxApkBytes`, cứng 200MB) ·
  `POST /{id}/publish` · `DELETE /{id}` · `POST /bulk-delete`
- **Bẫy đã gặp**: chốt 413 so path thiếu dấu `/` cuối làm không đăng được bản cập nhật.
- **Bẫy đã gặp**: `versionCode` phải > mã đã baked trong APK.

### C.37 Cấu hình app từ xa — `AppConfigEndpoints.cs` (2)
`GET /api/app-config` (mọi user đăng nhập) · `PUT /api/app-config` (admin).
Một dòng duy nhất `id=1`. Điều khiển: thông báo chạy chữ trong app, bật/tắt banner nhắc đăng ký
khuôn mặt, nhịp tự làm mới nền… **đổi không cần phát hành APK mới**.

### C.38 Nhật ký hoạt động — `AuditEndpoints.cs` (3) — nhóm `/api/audit` `[P:audit.read]`
`GET /` (phân trang + tương thích tham số `take` cũ) · `GET /filters` · `GET /export` (CSV/Excel)
- Lọc: người dùng, hành động, đối tượng, **tháng (yyyy-MM)**, nhóm nghiệp vụ, khoảng thời gian.
- Trả nội dung TRƯỚC/SAU, **che** mật khẩu/token/hash/embedding.
- Phạm vi do **server** ép ở `ResolveScopeAsync`: Admin xem toàn bộ; **Kế toán** (role `Accounting`
  + phòng ban `is_accounting`) chỉ xem **phần tiền** (`MoneyEntities`) — đây là ngoại lệ có chủ ý
  của "audit chỉ admin". Còn lại bị từ chối.
- Trang web là `/saoluu` (**không phải** trang sao lưu).

---

## PHẦN D — NỀN DỮ LIỆU

### D.1 112 bảng, gom theo miền

| Miền | Bảng |
|---|---|
| Danh tính & phiên | `app_users`, `user_sessions`, `user_roles`, `user_role_history`, `system_roles`, `registration_codes`, `password_reset_requests`, `password_recovery_codes`, `work_access_requests`, `app_pin_codes`, `web_login_settings`, `web_verified_users`, `web_diamond_members`, `web_user_avatars`, `web_user_preferences` |
| Kế toán bán hàng | `documents`, `document_lines`, `document_issued_lines`, `document_line_edits`, `customers`, `customer_aliases`, `customer_opening_balances`, `payments`, `products` |
| Mua hàng | `suppliers`, `purchases`, `purchase_lines` |
| Gia công | `gia_cong_phieu`, `gia_cong_hang_hoa` |
| Tiền mặt | `cash_collection_orders`, `cash_collection_events`, `cash_count_sessions`, `cash_count_lines`, `cash_fund_manual_entries`, **view** `cash_fund_ledger` |
| Chi tiền | `hr_payout_vouchers`, `hr_payout_voucher_events`, `hr_payout_categories` |
| Nhân sự | `hr_employees`, `hr_departments`, `hr_locations`, `hr_job_positions`, `hr_employee_positions`, `hr_contracts`, `hr_salary_raises`, `hr_documents`, `hr_leave_balances`, `hr_bank_accounts`, `hr_anniversary_letter`, `hr_employee_benefits`, `hr_employee_rewards` |
| Lương | `hr_salaries`, `hr_payslips`, `hr_payslip_history`, `hr_payslip_inquiries` |
| Phạt | `hr_penalties`, `hr_penalty_ledger`, `hr_penalty_refunds` |
| Đơn từ | `hr_requests`, `hr_request_approvals`, `hr_request_attachments`, `hr_approval_delegations` |
| Ca & chấm công | `hr_shifts`, `hr_shift_assignments`, `hr_holidays`, `hr_attendance_corrections`, `hr_attendance_reminders`, `cham_cong_log`, `cham_cong_face`, `cham_cong_face_enrollments`, `cham_cong_face_enrollment_samples`, `cham_cong_offline`, `cham_cong_qr_sites`, **view** `hr_effective_attendance_log` |
| Việc | `work_tasks`, `work_task_events` |
| Talent | `hr_onboarding_tasks`, `hr_performance_goals`, `hr_performance_reviews`, `hr_training_courses`, `hr_training_enrollments` |
| Chat & gọi | `web_chat_conversations`, `web_chat_members`, `web_chat_messages`, `web_chat_reactions`, `web_chat_reports`, `web_call_events`, `web_call_history` |
| Cổng TT & khảo sát | `app_portal_posts`, `app_portal_about`, `app_surveys`, `app_survey_responses`, `surveys`, `survey_questions`, `survey_responses`, `survey_answers`, `help_faqs` |
| Phản hồi | `app_feedbacks`, `app_general_feedback`, `app_support_tickets` |
| Hệ thống | `app_config`, `app_settings`, `app_releases`, `app_outbox`, `audit_logs`, `web_notifications`, `web_system_settings`, `hr_device_tokens`, `schema_migrations` |

> Lưu ý nợ kỹ thuật: tồn tại **hai bộ bảng khảo sát** (`app_surveys`/`app_survey_responses` và
> `surveys`/`survey_questions`/`survey_responses`/`survey_answers`) — cần hợp nhất khi tái cấu trúc.

### D.2 Bất biến được cưỡng chế **ở tầng CSDL** (bản đầu bỏ sót — port phải mang theo)

6 hàm plpgsql + 101 trigger. Đây là những quy tắc mà **đổi ngôn ngữ BE cũng không mất**, nhưng nếu
dựng CSDL mới từ đầu mà quên thì mất im lặng:

| Hàm | Trigger gắn vào | Cưỡng chế |
|---|---|---|
| `ketoanmini_publish_change()` | **96 bảng** | `pg_notify('ketoanmini_changes', <scope>)` cho mỗi scope trong `TG_ARGV`, mức STATEMENT, sau commit |
| `prevent_cash_collection_event_mutation()` | `cash_collection_events` | append-only — UPDATE/DELETE ⇒ `RAISE EXCEPTION` |
| `prevent_hr_payout_voucher_event_mutation()` | `hr_payout_voucher_events` | append-only |
| `prevent_hr_payslip_history_mutation()` | `hr_payslip_history` | append-only |
| `prevent_document_physical_delete()` | `documents` | cấm DELETE vật lý, `ERRCODE=23514`, buộc chuyển trạng thái huỷ |
| `prevent_issued_warehouse_voucher_no_change()` | `documents` | phiếu `document_type='document'` đã `issued_at` ⇒ **không đổi được `voucher_no`**, `ERRCODE=23514` |

> **0 ràng buộc CHECK** trong toàn CSDL: mọi kiểm tra giá trị (khoảng hợp lệ, enum trạng thái, số
> tiền không âm) nằm hoàn toàn trong mã ứng dụng. Port sang ngôn ngữ khác = phải chép lại đủ, hoặc
> nhân dịp này đẩy xuống CHECK constraint.

### D.3 Hai cơ chế DDL song song (điểm yếu chính)

**(a) 24 hàm `EnsureTables` chạy tuần tự trong `Program.cs`, thứ tự là ngầm định:**
`GiaCong → ChamCong → EncryptExistingEmbeddings → Preference → Avatar → Chat → Feedback →
**Hr (critical)** → **Request (critical)** → TaskAssignment → Notification → **Shift (critical)** →
Penalty → PenaltyRefund → PayoutVoucher → **CashCollection (critical)** → CashFund → Payroll →
BankAccount → AppConfig → Audit → Portal → Talent → Survey →
**Outbox (critical)** → Help`

Phân loại xử lý lỗi hiện tại:
- **`throw` (fail closed)**: `PostgresSchema`, `Hr`, `Request`, `Shift`, `CashCollection`, `Outbox`,
  và `EncryptExistingEmbeddings` (chỉ Production).
- **`LogWarning` rồi chạy tiếp**: 18 module còn lại → app có thể chạy với schema thiếu một nửa.

**(b) 9 migration có version** (`Data/*Migration.cs`, ghi `schema_migrations`):
`IdentityConsistency`, `RoleFoundation`, `EmployeePosition` (`004_employee_multiple_positions`),
`LegacyRolePositionBackfill` (`006_backfill_roles_to_employee_positions`),
`CanonicalRolePositionCorrection`, `DriverRole`, `RoleCatalogExpansion`,
`JobPositionCatalogExpansion`, `PayrollRoleAndScopedBranch`.

---

## PHẦN E — CẤU HÌNH

Bí mật đọc từ `appsettings.Local.json` (đã gitignore) rồi biến môi trường ghi đè.

| Khoá | Ý nghĩa |
|---|---|
| `ConnectionStrings:KetoanMini` | PostgreSQL, `Timezone=UTC`, pool 100 |
| `Bootstrap:Admin*` | tài khoản admin đầu tiên; **bắt buộc ở Production**, mật khẩu ≥14 ký tự |
| `Jwt:Key / Issuer / Audience` | key ≥32 ký tự; Production thiếu ⇒ **throw**, Dev ⇒ sinh tạm + cảnh báo |
| `Jwt:ExpireHours` = 8760 · `WebExpireHours` = 168 | token app vs cookie web |
| `Security:RequireHttps` / `HttpsPort` (5443) | ép HTTPS + HSTS ngoài Dev |
| `Security:FieldEncryptionKey` | AES-256 base64 cho embedding at-rest |
| `Security:KioskApiKey` / `KioskAllowLan` | khoá chấm công ẩn danh; **trống = MỞ** (có log cảnh báo) |
| `Security:SecurityHeaders` / `ContentSecurityPolicy` | CSP đổi được không cần build |
| `Security:SessionIdleDays` = 7 | phiên nhàn rỗi tự hết hiệu lực |
| `Security:CookieAuth` = true | phiên trình duyệt bằng cookie + CSRF |
| `Cors:Origins` | domain thật; origin local/LAN luôn được phép |
| `Chat:BlobDirectory` / `Releases:BlobDirectory` | **phải ngoài `bin/`** và được backup |
| `QrScanner:AllowedHttpsHosts` / `Actions` | action QR server-driven |
| `FaceRecognition:AdaFaceR50ModelPath` / `IdleUnloadMinutes` (10) | 0 = giữ model thường trú |
| `Firebase:CredentialsPath` | trống ⇒ tắt push, app vẫn chạy |
| `Turn:Cloudflare:{KeyId,ApiToken}` hoặc `Turn:{Secret,Urls}` + `TtlSeconds` | TURN cho gọi khác mạng; trống ⇒ chỉ gọi trong LAN |
| `Kestrel:Endpoints` | `http://localhost:5239` + `https://0.0.0.0:5443` |

---

## PHẦN F — LƯỚI AN TOÀN HIỆN CÓ (56 file test)

Đây là hợp đồng hành vi **phải còn xanh sau khi tái cấu trúc**:

- **Phân quyền/danh tính**: `PermissionModelTests`, `RoleFoundationTests`, `WorkflowRbacTests`,
  `TokenRoleFreshnessTests`, `SessionOwnershipSecurityTests`, `CookieSessionTests`,
  `UsernameDerivationTests`, `AuditScopeTests`, `SecurityTests`, `ProductionSecurityValidatorTests`
- **Chấm công**: `AttendanceRaceSecurityTests`, `AttendanceConfirmEndpointTests`,
  `AttendancePreviewTokenTests`, `AttendanceReminderServiceTests`, `KioskSessionFreshnessTests`,
  `AntiSpoofStatusTests`, `FaceEnrollmentApprovalTests`, `FaceEnrollmentCleanupServiceTests`,
  `LazyFaceEngineTests`, `ForgotCheckoutRegressionTests`, `OvertimeCalculationTests`
- **Tiền**: `CashCollectionTests`, `PayoutVoucherTests`, `PenaltyLedgerTests`, `PayrollHistoryTests`,
  `PurchaseTests`, `GoodsReturnTests`, `DeliveryAssignmentTests`, `DeliverySettlementTests`,
  `ProductCatalogTests`
- **Hạ tầng**: `RealtimeCoverageTests` (**cưỡng chế quy tắc "endpoint không gọi hub"**),
  `RealtimeWatcherTests`, `OutboxTests`, `PayloadLimitsTests`, `InfrastructureTests`,
  `ReleaseTests`, `AppConfigTests`, `WarehouseVoucherPrintServiceTests`
- **Khác**: `QrLogin*`, `QrAction*`, `AppPinTests`, `ChatVoiceTests`, `ChatAttachmentPolicyTests`,
  `RequestApprovalTests`, `WorklistTests`, `WorkforceAvailabilityTests`, `SurveyTests`,
  `ScheduleTests`, `DirectoryTests`, `HelpTests`, `AuditTests`, `AnniversaryGreetingTests`,
  `PasswordHasherTests`, `LoginBootstrapServiceTests`

---

## PHẦN G — VÌ SAO VIBE CODING HAY VỠ (chẩn đoán từ mã)

| # | Vấn đề | Bằng chứng | Hậu quả khi sửa bằng AI |
|---|---|---|---|
| 1 | **File endpoint khổng lồ, một hàm `Map*` dài hàng nghìn dòng** | `HrEndpoints` 3.152 dòng / 42 route; `ChamCong` 2.025; `Payroll` 1.846; `Request` 1.664 | Agent không đọc nổi cả file → sửa mù, phá route khác trong cùng file |
| 2 | **Phân quyền nằm 2 chỗ**: khai báo route **và** trong thân handler | 42 route HR chỉ có **1** `RequirePermission`, nhưng **40** chỗ `.Can(...)`/`IsHrManager()` trong handler | Thêm route mới rất dễ quên chốt cửa; không grep ra được "ai vào được endpoint này" |
| 3 | **DDL rải rác 25 nơi, thứ tự ngầm định** | `Program.cs` gọi 25 `EnsureTables` theo thứ tự phụ thuộc chỉ ghi trong comment | Thêm module mới đặt sai chỗ ⇒ FK lỗi lúc boot; 19/25 chỉ log warning nên **hỏng âm thầm** |
| 4 | **Hai cơ chế schema song song** (`EnsureTables` vs `schema_migrations`) | 25 vs 9 | Không có "trạng thái schema" duy nhất để so; rollback không xác định |
| 5 | **837 câu SQL thô nội tuyến trong handler** | `grep .Cmd(` | Đổi một cột phải rà cả 837 chỗ; agent sửa được 5 chỗ rồi báo "xong" |
| 6 | **Không có tầng domain/repository** — handler = HTTP + quyền + SQL + audit + push + realtime | mọi file `Endpoints/*.cs` | Không viết được unit test cho quy tắc nghiệp vụ; test hiện tại đa số phải dựng cả host |
| 7 | **Máy trạng thái là chuỗi string rải rác** | `CashCollection` 7 hằng, `PayoutVoucher` 7 hằng; `work_tasks`/`hr_requests` **không có** hằng, dùng literal | Agent tự bịa trạng thái mới, không ai chặn |
| 8 | **Quy tắc realtime là quy ước, không phải kiểu dữ liệu** | phải nhớ thêm bảng vào `DatabaseChangePublisher.Watched` | Thêm bảng mới ⇒ màn hình im lặng không tự làm mới; chỉ `RealtimeCoverageTests` bắt được |
| 9 | **Ràng buộc nghiệp vụ chỉ sống trong comment tiếng Việt** | các block `/// Bất biến:` ở Delivery/GoodsReturn/Payout | AI đọc lướt → phá bất biến mà build vẫn xanh |
| 10 | **Trùng lặp miền** | 2 bộ bảng khảo sát; 2 luồng QR login gần giống nhau (15 endpoint); `app_feedbacks` vs `app_general_feedback` vs `app_support_tickets` | Sửa một bên, quên bên kia |
| 11 | **`Program.cs` 928 dòng gánh cả DI + middleware + bootstrap schema** | | Mỗi thay đổi hạ tầng đụng một file ai cũng phải sửa → xung đột liên miên |

---

## PHẦN H — ĐỀ XUẤT TÁI CẤU TRÚC (không đổi hành vi)

Nguyên tắc xuyên suốt: **giữ nguyên 396 contract HTTP**. Frontend và APK không được sửa một dòng.
`docs/api-contract.md` + OpenAPI JSON là lưới kiểm chứng.

### Bước 0 — Chốt lưới an toàn (trước khi động vào code)
1. Sinh snapshot OpenAPI hiện tại → `docs/openapi.baseline.json`. Thêm test so sánh: mọi route,
   method, tên tham số, kiểu trả về phải khớp baseline.
2. Thêm test "mọi endpoint phải có ít nhất một chốt cửa" (duyệt `EndpointDataSource`, fail nếu
   endpoint không `AllowAnonymous` mà cũng không có policy quyền).

### Bước 1 — Tách `Program.cs` (đụng ít, lợi ngay)
```
Startup/
  ServiceRegistration.cs      ← toàn bộ builder.Services (hiện ~130 dòng)
  AuthenticationSetup.cs      ← JWT + cookie + CSRF
  RateLimitPolicies.cs        ← 10 policy
  MiddlewarePipeline.cs       ← thứ tự pipeline, có comment "không đảo"
  DatabaseBootstrap.cs        ← thay 25 lời gọi EnsureTables
  EndpointRegistration.cs     ← 37 lời gọi app.MapXxx()
```
`Program.cs` còn ~40 dòng.

### Bước 2 — Một cơ chế schema duy nhất
- Chuyển 25 `EnsureTables` thành **migration có version** trong `Data/Migrations/NNN_*.cs`, chạy qua
  một runner duy nhất đọc/ghi `schema_migrations`.
- Mỗi migration khai báo `DependsOn` tường minh thay vì dựa vào thứ tự dòng trong `Program.cs`.
- **Bỏ hẳn** `LogWarning` rồi chạy tiếp: schema thiếu ⇒ fail startup. (Đây chính là nguồn của loại
  lỗi "chạy được nhưng một màn hình trống trơn".)

### Bước 3 — Chuẩn hoá module thành *feature slice*
Mỗi module một thư mục, file nào cũng < ~400 dòng:
```
Features/Hr/
  HrEndpoints.cs        ← CHỈ khai báo route + chốt quyền (không SQL)
  HrQueries.cs          ← SQL đọc
  HrCommands.cs         ← SQL ghi (transaction, FOR UPDATE, advisory lock)
  HrRules.cs            ← bất biến nghiệp vụ, thuần hàm → unit-test được
  HrDtos.cs
```
Ưu tiên tách theo thứ tự đau: `Hr` (3.152) → `ChamCong` (2.025) → `Payroll` (1.846) →
`Request` (1.664) → `Auth` (1.451) → `Chat` (1.433) → `Accounting` (1.376) →
`PayoutVoucher` (1.222) → `CashCollection` (1.068).

### Bước 4 — Dồn phân quyền về **một** chỗ
- Mọi endpoint chốt cửa bằng `.RequirePermission(...)` ở khai báo route.
- Cái còn lại trong handler **chỉ được là phạm vi dữ liệu** (`AccessScope`), gói vào một helper
  `IScopeFilter` trả về mệnh đề `WHERE` — không còn `IsAdmin()` rải rác.
- Ràng buộc "phòng ban `is_accounting`" (đang lặp ở 4 file, 19 chỗ) → một `IAccountingDeskGuard`.

### Bước 5 — Máy trạng thái thành kiểu dữ liệu
Mỗi workflow (`work_tasks`, `hr_requests`, `cash_collection_orders`, `hr_payout_vouchers`,
`documents`) có một `static class XxxStatus` + bảng chuyển trạng thái hợp lệ
`(from, action) → to`. Handler chỉ gọi `Transition(...)`; chuyển trạng thái sai là lỗi biên dịch/test,
không phải lỗi runtime phát hiện sau ba tuần.

### Bước 6 — Bọc truy cập dữ liệu
Giữ SQL thô (đúng, và Npgsql nhanh) nhưng đưa vào lớp `*Queries`/`*Commands` để:
- một chỗ duy nhất biết tên cột → đổi schema grep ra hết;
- ép sẵn cách xử lý `NULL` có kiểu (`::uuid`, `::date`) — chặn lại lỗi Npgsql **42P08** đã gặp
  (`@x IS NULL OR col=@x` với `DBNull` → 503 giả "mất kết nối DB");
- ép lọc tháng bằng **khoảng ngày** thay vì `to_char` (đã biết là giết index).

### Bước 7 — Hợp nhất phần trùng
- Hai bộ bảng khảo sát → một.
- Hai luồng QR login (`/qr/*` và `/app-login/*`, 15 endpoint) → một service tham số hoá theo hướng
  (web-được-app-xác-nhận / app-được-web-xác-nhận), giữ nguyên 15 route.
- Ba bảng phản hồi → một bảng có cột `kind`.

### Việc **không** nên làm
- Không port thêm sang Rust trong lúc tái cấu trúc .NET — sẽ có hai bản đang biến động cùng lúc.
- Không siết "nợ bảo mật tương thích" (fail-open khi `sid` không có dòng `user_sessions`) chung một
  đợt với refactor: đó là migration riêng, có kế hoạch phát lại token.
- Không đổi tên route / hình dạng JSON. Không đụng `AccessProfileDto`.
- Không tự thêm lại 2 chốt bất kiêm nhiệm ở lệnh thu tiền (đã cố ý bỏ).
- Không gọi hub từ endpoint (dùng `DatabaseChangePublisher.Watched`).
