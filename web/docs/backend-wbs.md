# SƠ ĐỒ CÂY CHỨC NĂNG BACKEND — dùng để giao việc

> Đây là cây kiểm kê trước khi tách communication. Các nhánh chat/P2P/voice/video call trong phần
> lịch sử bên dưới không còn được đăng ký trong host hiện tại; mã nguồn nằm tại
> `communication-standalone/`.

Phân rã từ **hệ thống → khối → module → nhóm chức năng → endpoint**.
Số trong ngoặc: `[LOC]` = số dòng mã của file, `[N ep]` = số endpoint, `[perm]` = quyền chốt cửa.

Nguồn: [`backend-inventory.md`](backend-inventory.md) · [`backend-port-spec.md`](backend-port-spec.md) ·
[`backend-schema.sql`](backend-schema.sql) · [`openapi.baseline.json`](openapi.baseline.json)

**Tổng: 396 endpoint · 114 bảng · 45 quyền · 12 vai trò · 9 worker · ~49.900 dòng C#**

---

## BẢNG GIAO VIỆC NHANH — 10 gói

| Gói | Tên | Endpoint | LOC | Người | Phụ thuộc |
|---|---|---|---|---|---|
| **G0** | Nền tảng & hạ tầng | 2 | ~2.400 | *(kỹ sư giỏi nhất, làm TRƯỚC)* | — |
| **G1** | Danh tính, phiên, phân quyền | 52 | ~2.900 | | G0 |
| **G2** | Kế toán bán hàng & chứng từ | 47 | ~2.700 | | G0, G1 |
| **G3** | Kế toán lõi & mua hàng | 29 | ~1.600 | | G0, G1 |
| **G4** | Tiền mặt (thu / chi / quỹ) | 36 | ~2.700 | | G1, G2 |
| **G5** | Nhân sự & đơn từ | 64 | ~5.100 | | G0, G1 |
| **G6** | Chấm công & khuôn mặt | 41 | ~4.100 | *(cần người biết ML/ONNX)* | G0, G1, G5 |
| **G7** | Lương & phạt | 41 | ~3.000 | | G5, G6 |
| **G8** | Giao việc & giao hàng | 26 | ~1.900 | | G2, G5 |
| **G9** | Giao tiếp & cổng thông tin | 58 | ~2.800 | | G0, G1 |
| **G10** | Hệ thống, APK, nhật ký | 30 | ~1.900 | | G0, G1 |

> **Thứ tự bắt buộc:** G0 xong mới chia tiếp. G1 là nền của mọi gói còn lại.
> G6 nên giao cho người có kinh nghiệm xử lý ảnh — nó là gói duy nhất có phụ thuộc native.

---

# CÂY CHỨC NĂNG ĐẦY ĐỦ

```
KETOANMINI BACKEND
│
├── G0. NỀN TẢNG & HẠ TẦNG  ─────────────────────────────  [2 ep · ~2.400 LOC]
│   │
│   ├── 0.1 Khởi động ứng dụng                             Program.cs [928]
│   │   ├── Đăng ký DI (~40 service)
│   │   ├── Nạp cấu hình: appsettings → appsettings.Local.json → biến môi trường
│   │   ├── ProductionSecurityValidator [112]  — chặn boot nếu secret yếu / thiếu model
│   │   ├── Bootstrap schema: 25 × EnsureTables theo thứ tự phụ thuộc ngầm
│   │   ├── Di trú blob APK từ bytea ra đĩa (một lần)
│   │   └── Mã hoá AES embedding cũ (Production lỗi ⇒ dừng khởi động)
│   │
│   ├── 0.2 Pipeline middleware (14 tầng — THỨ TỰ LÀ NGHIỆP VỤ)
│   │   ├── ForwardedHeaders (chỉ tin loopback — Cloudflare Tunnel)
│   │   ├── Security headers + CSP
│   │   ├── Chặn /api/auth/forgot-password-face ngoài Development → 404
│   │   ├── Trần payload theo từng endpoint → 413        PayloadLimits [88]
│   │   ├── HSTS + HttpsRedirection
│   │   ├── Static files (SPA, .tflite/.task/.wasm, index.html no-store)
│   │   ├── CORS                                          CorsPolicy [42]
│   │   ├── RateLimiter (11 policy)
│   │   ├── Chống CSRF double-submit                       AuthCookies [141]
│   │   ├── ★ Middleware làm tươi danh tính (GIỮA auth và authz)
│   │   └── Bắt NpgsqlException → 503 JSON chung
│   │
│   ├── 0.3 Truy cập dữ liệu                               Database.cs [126]
│   │   ├── NpgsqlDataSource pooled (max 100)
│   │   ├── EnsureDatabaseExistsAsync — tự tạo DB nếu chưa có
│   │   └── Extension .Cmd(sql).With(@p, v)  — 837 chỗ dùng
│   │
│   ├── 0.4 Schema & migration
│   │   ├── PostgresSchema.cs [617] — schema danh tính (BẮT BUỘC, fail-closed)
│   │   ├── 25 × EnsureTables rải trong các module endpoint
│   │   └── 9 migration có version (schema_migrations)
│   │       ├── IdentityConsistencyMigration [247]
│   │       ├── RoleFoundationMigration [249]
│   │       ├── EmployeePositionMigration [72]            (004)
│   │       ├── LegacyRolePositionBackfillMigration [105] (006)
│   │       ├── CanonicalRolePositionCorrectionMigration [98]
│   │       ├── DriverRoleMigration [103]
│   │       ├── RoleCatalogExpansionMigration [77]
│   │       ├── JobPositionCatalogExpansionMigration [62]
│   │       └── PayrollRoleAndScopedBranchMigration [213]
│   │
│   ├── 0.5 Realtime (xương sống)
│   │   ├── ChangesHub [139]           — hub SignalR /hubs/changes
│   │   │   ├── OnConnectedAsync → đánh dấu hiện diện theo sid
│   │   │   ├── Relay(to, payload)     — WebRTC ≤120 gói/5s, ≤64KB
│   │   │   └── OnDisconnectedAsync    — KHÔNG ghi is_active=false
│   │   ├── DatabaseChangePublisher [225] — cài 96 trigger, 12 scope
│   │   ├── ChangeWatcher [254]        — LISTEN → gộp 100ms → broadcast
│   │   ├── HubPresenceRegistry [35] + HubPresenceRefresher [57]  (45s/lô)
│   │   └── NameUserIdProvider [16]
│   │
│   ├── 0.6 Hàng chờ bền & thông báo đẩy
│   │   ├── OutboxQueue [219]   — bảng app_outbox, lease 2 phút
│   │   ├── OutboxWorker [122]  — rút việc, retry lũy thừa
│   │   ├── PushOutboxHandler [47] → FCM
│   │   ├── PushService [665]   — CỬA DUY NHẤT ghi web_notifications
│   │   │   ├── SendToUserAsync / SendToPermissionAsync
│   │   │   ├── SendWebOnlyToPermissionAsync (không FCM)
│   │   │   ├── SendToAllAsync / SendToAdminsAsync / SendToEmployeeAsync
│   │   │   └── SendCallInviteAsync / SendCallCancelAsync
│   │   └── NotificationGroups [56] — 5 nhóm tắt được + 3 nhóm không tắt được
│   │
│   ├── 0.7 JSON & thời gian
│   │   └── UtcDateTimeConverter [37] — "yyyy-MM-ddTHH:mm:ss.fffZ"
│   │
│   ├── 0.8 Bất biến ở tầng CSDL (6 hàm plpgsql · 101 trigger)
│   │   ├── ketoanmini_publish_change()                    × 96 bảng
│   │   ├── prevent_cash_collection_event_mutation()       append-only
│   │   ├── prevent_hr_payout_voucher_event_mutation()     append-only
│   │   ├── prevent_hr_payslip_history_mutation()          append-only
│   │   ├── prevent_document_physical_delete()             cấm DELETE vật lý
│   │   └── prevent_issued_warehouse_voucher_no_change()   khoá voucher_no đã in
│   │
│   └── 0.9 Endpoint hạ tầng
│       ├── GET /api/info
│       └── GET /api/health          (chỉ trả từ loopback/LAN, ngoài → 404)
│
├── G1. DANH TÍNH · PHIÊN · PHÂN QUYỀN  ────────────────  [52 ep · ~2.900 LOC]
│   │
│   ├── 1.1 Mô hình phân quyền (KHÔNG có endpoint — là thư viện)
│   │   ├── AppRoles [100]        — 12 vai trò, Normalize nhận tên tiếng Việt
│   │   │   ├── All / Assignable (11) / Secondary (8)
│   │   │   ├── PrimaryPriority — chọn vai trò chính khi kiêm nhiệm
│   │   │   └── Label — nhãn tiếng Việt
│   │   ├── Permissions [287]     — 45 quyền + bảng vai trò→quyền + Label
│   │   ├── AccessProfile [47]    — ScopeKind{Self,Department,Branch,All} + DTO
│   │   ├── AccessProfileService [184] — tính hồ sơ truy cập từ DB
│   │   ├── PermissionEndpoints [30]   — extension .RequirePermission
│   │   ├── PermissionDirectory [58]
│   │   └── EmployeePositionRoleService [193] — chức vụ ↔ vai trò
│   │
│   ├── 1.2 Mật mã (PHẢI TÁI LẬP CHÍNH XÁC — xem port-spec §3)
│   │   ├── PasswordHasher [143]  — Argon2id m=19456,t=2,p=1 + đọc PBKDF2 cũ
│   │   ├── TokenService [77]     — JWT HS256, claim URI dài, sid
│   │   ├── AuthCookies [141]     — km_auth (HttpOnly) + km_csrf + X-CSRF-Token
│   │   ├── FieldCipher [103]     — AES-256-GCM định dạng "KME1"
│   │   ├── RecoveryCodes [30]    — Crockford base32, 5 ký tự
│   │   ├── AppPinPolicy [69]     — PIN 6 số, khoá 30s/5p/30p
│   │   └── KioskAccess [92]      — 3 cửa: phiên tươi / LAN / X-Kiosk-Key
│   │
│   ├── 1.3 Đăng nhập                                      AuthEndpoints [1451]
│   │   ├── POST /api/auth/bootstrap          [A] rate:login-bootstrap
│   │   ├── POST /api/auth/login              [A] rate:login
│   │   └── LoginBootstrapService [118]
│   │
│   ├── 1.4 Đăng nhập QR cho WEB (app quét giúp)           8 ep
│   │   ├── POST /api/auth/qr/start           [A]
│   │   ├── POST /api/auth/qr/scan            [Auth]
│   │   ├── POST /api/auth/qr/confirm         [Auth]
│   │   ├── POST /api/auth/qr/account         [Auth]
│   │   ├── POST /api/auth/qr/reject          [Auth]
│   │   ├── POST /api/auth/qr/poll            [A]
│   │   ├── POST /api/auth/qr/ack             [A]
│   │   └── POST /api/auth/qr/cancel          [A]
│   │
│   ├── 1.5 Đăng nhập QR cho APP (web hiển thị mã)         7 ep
│   │   ├── POST /api/auth/app-login/start    [A]
│   │   ├── POST /api/auth/app-login/resolve  [A]
│   │   ├── POST /api/auth/app-login/confirm  [Auth]
│   │   ├── POST /api/auth/app-login/reject   [Auth]
│   │   ├── POST /api/auth/app-login/poll     [A]
│   │   ├── POST /api/auth/app-login/ack      [A]
│   │   └── POST /api/auth/app-login/cancel   [A]
│   │   └── QrLoginService [521] — phiên 5 phút RAM, chỉ giữ SHA-256
│   │
│   ├── 1.6 Quên mật khẩu                                  3 ep · rate:face-reset
│   │   ├── POST /api/auth/forgot-password-face    [A] (404 ngoài Development)
│   │   ├── POST /api/auth/reset-with-recovery-code [A]
│   │   └── POST /api/auth/verify-recovery-code     [A]
│   │
│   ├── 1.7 Hồ sơ cá nhân                                  7 ep
│   │   ├── GET  /api/auth/me
│   │   ├── GET  /api/auth/access-profile   ← thứ DUY NHẤT client dựng UI
│   │   ├── PUT  /api/auth/profile
│   │   ├── PUT  /api/auth/avatar
│   │   ├── DELETE /api/auth/avatar
│   │   ├── POST /api/auth/change-password
│   │   └── POST /api/auth/verify-password         rate:reauth
│   │
│   ├── 1.8 Mã bảo mật app (PIN 6 số)                      4 ep
│   │   ├── GET  /api/auth/app-pin
│   │   ├── POST /api/auth/app-pin
│   │   ├── POST /api/auth/app-pin/verify          rate:app-pin
│   │   └── POST /api/auth/app-pin/reset           rate:reauth
│   │
│   ├── 1.9 Phiên & thiết bị                               7 ep
│   │   ├── POST /api/auth/heartbeat
│   │   ├── POST /api/auth/logout
│   │   ├── GET  /api/auth/devices
│   │   ├── POST /api/auth/devices/{sid}/revoke
│   │   ├── POST /api/auth/devices/revoke-all      (+ xoá hr_device_tokens)
│   │   ├── GET  /api/auth/account-settings
│   │   └── PUT  /api/auth/account-settings        (bật/tắt đăng nhập web)
│   │
│   ├── 1.10 QR server-driven                              QrActionEndpoints [255]
│   │   ├── POST /api/qr/resolve        rate:qr-action
│   │   ├── POST /api/qr/decision       rate:qr-action
│   │   └── QrActionService [208] — vé Data Protection tự chứa
│   │
│   ├── 1.11 Quản lý tài khoản                             UserEndpoints [602] · [perm:users.manage]
│   │   ├── GET    /api/roles/catalog
│   │   ├── GET    /api/users/
│   │   ├── POST   /api/users/
│   │   ├── POST   /api/users/{id}/role              (vai trò chính)
│   │   ├── POST   /api/users/{id}/secondary-role    (vai trò phụ)
│   │   ├── POST   /api/users/{id}/approve
│   │   ├── POST   /api/users/{id}/lock
│   │   ├── POST   /api/users/{id}/verify            (tích xanh)
│   │   ├── POST   /api/users/{id}/diamond
│   │   ├── POST   /api/users/{id}/reset-password
│   │   ├── POST   /api/users/{id}/recovery-code
│   │   └── DELETE /api/users/{id}                   (xoá mềm + dọn tham chiếu)
│   │
│   └── 1.12 Danh bạ & sơ đồ tổ chức                       DirectoryEndpoints [135]
│       ├── GET /api/directory/          (tìm tiếng Việt không dấu, online)
│       └── GET /api/directory/org-chart (cây theo manager_id)
│           └── Phân quyền xem SĐT/email: Admin/HR tất cả · quản lý xem NV mình · bản thân luôn xem
│
├── G2. KẾ TOÁN BÁN HÀNG & CHỨNG TỪ  ───────────────────  [47 ep · ~2.700 LOC]
│   │                                                      [perm:accounting.access]
│   ├── 2.1 Bảng điều khiển & báo cáo                      AccountingEndpoints [1376]
│   │   ├── GET /api/dashboard
│   │   ├── GET /api/accounting/system-status
│   │   └── GET /api/reports
│   │
│   ├── 2.2 Phiếu (documents)                              7 ep
│   │   ├── GET    /api/documents
│   │   ├── GET    /api/documents/stack
│   │   ├── GET    /api/documents/{id}
│   │   ├── POST   /api/documents
│   │   ├── PUT    /api/documents/{id}
│   │   ├── PUT    /api/documents/{id}/cancel     [perm:vouchers.cancel]
│   │   └── DELETE /api/documents/{id}            [perm:vouchers.cancel]
│   │       └── ★ Trigger CSDL cấm DELETE vật lý (ERRCODE 23514)
│   │
│   ├── 2.3 In phiếu xuất kho                              2 ep · ⚠️ PHỤ THUỘC NATIVE
│   │   ├── POST /api/documents/{id}/warehouse-print
│   │   ├── GET  /api/documents/{id}/warehouse-preview
│   │   └── WarehouseVoucherPrintService [967]
│   │       ├── COM late-binding Excel.Application (ProgID) + dynamic PrintOut
│   │       ├── P/Invoke winspool: PrinterInfo2, JobInfo1, theo dõi hàng đợi in
│   │       ├── Template: Templates/PhieuXuatKho.xlsx
│   │       └── ★ Trigger CSDL khoá voucher_no sau khi issued_at
│   │
│   ├── 2.4 Phiếu thu / chi (cash-vouchers)                8 ep
│   │   ├── GET    /api/cash-vouchers
│   │   ├── GET    /api/cash-vouchers/{id}
│   │   ├── POST   /api/cash-vouchers
│   │   ├── PUT    /api/cash-vouchers/{id}
│   │   ├── PUT    /api/cash-vouchers/{id}/issued
│   │   ├── PUT    /api/cash-vouchers/{id}/cancel      [perm:vouchers.cancel]
│   │   ├── DELETE /api/cash-vouchers/{id}             [perm:vouchers.cancel]
│   │   └── DELETE /api/cash-vouchers/{id}/permanent   [perm:vouchers.cancel]
│   │
│   ├── 2.5 Khách hàng                                     5 ep
│   │   ├── GET    /api/customers
│   │   ├── GET    /api/customers/{id}/report
│   │   ├── POST   /api/customers
│   │   ├── PUT    /api/customers/{id}
│   │   └── DELETE /api/customers/{id}
│   │
│   ├── 2.6 Công nợ phải thu                               4 ep
│   │   ├── GET  /api/debts
│   │   ├── GET  /api/debts/{customerId}
│   │   ├── PUT  /api/debts/{customerId}/opening-balance
│   │   └── POST /api/debts/{customerId}/payments
│   │
│   ├── 2.7 Danh mục hàng hoá                              ProductCatalogEndpoints [272] · 5 ep
│   │   ├── GET  /api/products
│   │   ├── GET  /api/products/suggestions   (gợi ý dựng từ phiếu cũ)
│   │   ├── POST /api/products
│   │   ├── PUT  /api/products/{id}
│   │   ├── POST /api/products/import
│   │   └── Nguyên tắc: GỢI Ý, KHÔNG ÉP — ô nhập vẫn gõ tay được
│   │
│   └── 2.8 Gia công                                       GiaCongEndpoints [350] · 6 ep
│       ├── GET    /api/giacong/
│       ├── GET    /api/giacong/report   (tổng hợp Xuất/Nhập theo đối tác)
│       ├── GET    /api/giacong/{id}
│       ├── POST   /api/giacong/
│       ├── PUT    /api/giacong/{id}
│       └── DELETE /api/giacong/{id}
│
├── G3. MUA HÀNG  ──────────────────────────────────────  [14 ep · ~750 LOC]
│   │
│   ├── 3.2 Nhà cung cấp                                   PurchaseEndpoints [390] · 3 ep
│   │   ├── GET  /api/suppliers
│   │   ├── POST /api/suppliers
│   │   └── PUT  /api/suppliers/{id}
│   │
│   ├── 3.3 Phiếu nhập mua                                 5 ep
│   │   ├── GET /api/purchases
│   │   ├── GET /api/purchases/{id}
│   │   ├── POST /api/purchases
│   │   ├── PUT /api/purchases/{id}
│   │   └── PUT /api/purchases/{id}/cancel
│   │   └── Bảng RIÊNG (purchases/purchase_lines), KHÔNG dùng chung documents
│   │       Công nợ phải trả ở mức paid_amount (chưa có sổ chi tiết NCC)
│   │
│   └── 3.4 Hàng khách trả về                              GoodsReturnEndpoints [442] · 5 ep
│       ├── GET /api/returns/sources     (truy đơn nguồn lấy ĐÚNG đơn giá)
│       ├── POST /api/returns
│       ├── GET /api/returns
│       ├── GET /api/returns/{id}
│       ├── PUT /api/returns/{id}/cancel
│       └── ★ Hai đường ghi sổ, một dòng chỉ đi ĐÚNG MỘT:
│           ├── đơn vừa giao + phiếu chưa chốt ⇒ hạ thẳng số lượng
│           └── còn lại ⇒ phiếu trả hàng riêng (document_type='return')
│           Bất biến: tổng đã trả ≤ số đã bán trên dòng nguồn
│
├── G4. TIỀN MẶT  ──────────────────────────────────────  [36 ep · ~2.700 LOC]
│   │
│   ├── 4.1 Lệnh thu tiền khách hàng    CashCollectionEndpoints [1068] · 12 ep
│   │   ├── Máy trạng thái
│   │   │   Assigned → Accepted → PendingHandover → Completed
│   │   │   nhánh: Failed · Variance (cần collections.resolve) · Cancelled
│   │   ├── Tra cứu
│   │   │   ├── GET /api/cash-collections/customers
│   │   │   ├── GET /api/cash-collections/drivers
│   │   │   ├── GET /api/cash-collections/
│   │   │   ├── GET /api/cash-collections/history
│   │   │   └── GET /api/cash-collections/{id}
│   │   ├── Vòng đời
│   │   │   ├── POST /  (kế toán giao)                  [perm:collections.create]
│   │   │   ├── POST /{id}/accept   (tài xế nhận)       [perm:collections.self]
│   │   │   ├── POST /{id}/fail
│   │   │   ├── POST /{id}/collect  (đếm theo MỆNH GIÁ → cash_count_*)
│   │   │   ├── POST /{id}/receive  (thủ quỹ đếm lại)   [perm:collections.receive]
│   │   │   ├── POST /{id}/resolve  (duyệt sai lệch)    [perm:collections.resolve]
│   │   │   └── POST /{id}/cancel
│   │   └── ★ KHÔNG thu GPS · KHÔNG lưu địa chỉ khách
│   │       ★ Đã CỐ Ý bỏ 2 chốt bất kiêm nhiệm — đừng thêm lại
│   │       ★ Trigger CSDL: cash_collection_events append-only
│   │
│   ├── 4.2 Phiếu chi tiền mặt          PayoutVoucherEndpoints [1222] · 15 ep
│   │   ├── Máy trạng thái
│   │   │   AwaitingScan → Confirmed → AwaitingApproval → Approved → Paid
│   │   │   nhánh: Rejected · Cancelled
│   │   ├── Danh mục khoản chi
│   │   │   ├── GET/POST /api/payout-vouchers/categories
│   │   │   ├── PUT    /categories/{id}
│   │   │   └── DELETE /categories/{id}
│   │   ├── Tra cứu
│   │   │   ├── GET /recipients
│   │   │   ├── GET /sources/refunds    (nguồn = hoàn tiền phạt)
│   │   │   ├── GET /
│   │   │   ├── GET /{id}/history
│   │   │   └── GET /summary
│   │   ├── Vòng đời
│   │   │   ├── POST /                  [perm:payout.create]
│   │   │   ├── POST /{id}/qr           ← sinh QR ký nhận
│   │   │   ├── POST /{id}/approve      [perm:payout.approve]
│   │   │   ├── POST /{id}/complete     [perm:payout.pay]
│   │   │   ├── POST /{id}/reject
│   │   │   └── POST /{id}/cancel
│   │   └── ★ CHƯA QUÉT QR KÝ NHẬN ⇒ KHÔNG DUYỆT CHI ĐƯỢC
│   │       ★ Mọi thao tác buộc hồ sơ thuộc phòng ban is_accounting (Admin bị loại)
│   │       ★ Trigger CSDL: hr_payout_voucher_events append-only
│   │
│   ├── 4.3 Quỹ tiền mặt                CashFundEndpoints [363] · 5 ep
│   │   ├── GET  /api/cash-fund/balance
│   │   ├── GET  /api/cash-fund/
│   │   ├── GET  /api/cash-fund/entries
│   │   ├── POST /api/cash-fund/entries                 [perm:cashfund.manage]
│   │   ├── POST /api/cash-fund/entries/{id}/reverse
│   │   └── ★ VIEW cash_fund_ledger hợp nhất 4 nguồn (KHÔNG chép số):
│   │       lệnh thu hoàn tất(+) · phiếu chi đã chi(−) · documents còn hiệu lực(±) · bút toán tay(±)
│   │
│   └── 4.4 Tài khoản ngân hàng         BankAccountEndpoints [221] · 6 ep
│       ├── GET    /api/bank-accounts/banks   (Vietcombank, Sacombank)
│       ├── GET    /api/bank-accounts/
│       ├── POST   /api/bank-accounts/
│       ├── PUT    /api/bank-accounts/{id}
│       ├── POST   /api/bank-accounts/{id}/default
│       └── DELETE /api/bank-accounts/{id}
│
├── G5. NHÂN SỰ & ĐƠN TỪ  ──────────────────────────────  [64 ep · ~5.100 LOC]
│   │
│   ├── 5.1 Nhân sự                     HrEndpoints [3152] · 42 ep · [perm:hr.self.access]
│   │   │                                ⚠️ 40 chỗ kiểm quyền INLINE trong handler
│   │   ├── Danh mục
│   │   │   ├── GET /api/hr/job-positions                [perm:hr.read]
│   │   │   ├── GET/POST/PUT/DELETE /api/hr/departments[/{id}]
│   │   │   └── GET/POST/PUT/DELETE /api/hr/locations[/{id}]
│   │   ├── Của tôi
│   │   │   ├── GET  /api/hr/me
│   │   │   ├── GET/POST /api/hr/me/documents
│   │   │   └── PUT  /api/hr/me/avatar
│   │   ├── Thư kỷ niệm
│   │   │   ├── GET/PUT /api/hr/anniversary/template
│   │   │   └── GET     /api/hr/anniversary/my-greeting
│   │   ├── Hồ sơ nhân viên
│   │   │   ├── GET    /api/hr/employees
│   │   │   ├── GET    /api/hr/employees/{id}
│   │   │   ├── POST   /api/hr/employees
│   │   │   ├── PUT    /api/hr/employees/{id}
│   │   │   └── DELETE /api/hr/employees/{id}
│   │   ├── Hợp đồng
│   │   │   ├── GET/POST  /api/hr/employees/{id}/contracts
│   │   │   ├── PUT       /api/hr/contracts/{cid}
│   │   │   └── DELETE    /api/hr/contracts/{cid}
│   │   ├── Tăng lương  (lương cứng = contract_base + Σ salary_raises)
│   │   │   ├── GET/POST  /api/hr/employees/{id}/salary-raises
│   │   │   ├── PUT       /api/hr/salary-raises/{rid}
│   │   │   └── DELETE    /api/hr/salary-raises/{rid}
│   │   ├── Phiếu lương (hồ sơ)
│   │   │   ├── GET/POST  /api/hr/employees/{id}/payslips
│   │   │   └── DELETE    /api/hr/payslips/{pid}
│   │   ├── Số phép
│   │   │   └── GET/POST  /api/hr/employees/{id}/leave-balances
│   │   ├── Giấy tờ / bằng cấp
│   │   │   ├── GET/POST  /api/hr/employees/{id}/documents
│   │   │   └── DELETE    /api/hr/documents/{did}
│   │   └── Màn hình quản lý
│   │       ├── GET /api/hr/manager/summary
│   │       ├── GET /api/hr/manager/attendance
│   │       ├── GET /api/hr/manager/contracts/expiring
│   │       ├── GET /api/hr/manager/reports
│   │       └── GET /api/hr/manager/alerts
│   │
│   ├── 5.2 Engine đơn từ & phê duyệt    RequestEndpoints [1664] · 13 ep · [perm:requests.self]
│   │   ├── Loại đơn (dùng chung 1 engine, chi tiết trong jsonb)
│   │   │   nghỉ phép · nghỉ ốm · tăng ca · thanh toán · tạm ứng · mua vật tư
│   │   │   điều chỉnh công · đổi ca (shift_swap) · đăng ký xe/phòng họp …
│   │   ├── Tra cứu
│   │   │   ├── GET /api/requests/types
│   │   │   ├── GET /api/requests/
│   │   │   ├── GET /api/requests/inbox-count
│   │   │   └── GET /api/requests/{id}
│   │   ├── Tệp đính kèm
│   │   │   ├── POST /api/requests/{id}/attachments
│   │   │   └── GET  /api/requests/{id}/attachments/{attachmentId}
│   │   ├── Vòng đời
│   │   │   ├── POST /api/requests/
│   │   │   ├── PUT  /api/requests/{id}
│   │   │   ├── POST /api/requests/{id}/approve   [perm:requests.approve]
│   │   │   ├── POST /api/requests/{id}/reject
│   │   │   ├── POST /api/requests/{id}/cancel
│   │   │   └── POST /api/requests/{id}/remind
│   │   ├── Uỷ quyền duyệt
│   │   │   └── PUT /api/requests/delegations/me
│   │   └── Luồng nhiều cấp: NV → quản lý trực tiếp → hàng đợi HR (hr_request_approvals)
│   │       + ký xác nhận điện tử
│   │
│   ├── 5.3 Việc cần làm                WorklistEndpoints [180] · 1 ep
│   │   └── GET /api/worklist/  — gộp 5 nguồn, khoá ổn định "kind:id"
│   │       ├── đơn chờ mình duyệt (kèm SLA)
│   │       ├── phiếu lương chưa xác nhận
│   │       ├── giấy tờ sắp hết hạn
│   │       ├── hợp đồng sắp hết hạn
│   │       └── thông báo bắt buộc (mức cảnh báo/nghiêm trọng)
│   │
│   ├── 5.4 Phát triển nhân sự          TalentEndpoints [139] · 9 ep
│   │   ├── Hội nhập
│   │   │   ├── GET  /api/talent/onboarding
│   │   │   └── POST /api/talent/onboarding/{id}/complete
│   │   ├── Hiệu suất
│   │   │   ├── GET /api/talent/performance
│   │   │   ├── PUT /api/talent/performance/goals/{id}
│   │   │   └── PUT /api/talent/performance/reviews/{id}/self
│   │   ├── Đào tạo
│   │   │   ├── GET  /api/talent/training
│   │   │   ├── PUT  /api/talent/training/{id}/progress
│   │   │   └── POST /api/talent/training/{id}/quiz
│   │   └── GET /api/talent/benefits
│   │
│   └── 5.5 Điều phối nhân lực          WorkforceAvailability [168] · (thư viện)
│       └── "Hôm nay ai có mặt để nhận việc" — KHÔNG xoá người khỏi danh sách,
│           trả kèm Label ("Chưa chấm công", "Đang nghỉ phép")
│
├── G6. CHẤM CÔNG & KHUÔN MẶT  ─────────────────────────  [41 ep · ~4.100 LOC]
│   │                                                      ⚠️ PHỤ THUỘC NATIVE
│   ├── 6.1 Bộ máy nhận diện
│   │   ├── FaceEngine [159]           — giao diện IFaceEngine
│   │   ├── AdaFaceR50Engine [670]     — YuNet + căn 5 điểm + AdaFace R50 ONNX
│   │   │   ├── MatchThreshold 0.45 · LivenessThreshold 0.5
│   │   │   ├── Tăng sáng: DarkMean 110 · BrightMean 200 · GlareRatio 0.045
│   │   │   └── ★ BGR chứ không RGB (sai ⇒ đăng ký lại toàn bộ)
│   │   ├── SilentFaceLiveness [190]   — 2 model MiniFASNet (crop 2.7 & 4.0)
│   │   ├── LazyFaceEngine [331]       — thả model sau 10 phút nhàn rỗi (~348MB)
│   │   ├── LivenessMetricsLog [47]    — vòng đệm RAM để hiệu chỉnh ngưỡng
│   │   └── AttendancePreviewTokens [85] — token 1 lần, tiết kiệm 50% suy luận
│   │
│   ├── 6.2 Chấm công (kiosk)          ChamCongEndpoints [2025] · 3 ep
│   │   ├── GET  /api/chamcong/trangthai  [A] + KioskAccessFilter
│   │   ├── POST /api/chamcong/nhandien   [A] rate:attendance  ← bước XEM TRƯỚC
│   │   ├── POST /api/chamcong/cham       [A] rate:attendance  ← bước XÁC NHẬN
│   │   └── ★ AllowAnonymous nhưng VẪN kiểm chứng JWT nếu có; không được ⇒ 503
│   │
│   ├── 6.3 Chính sách Vào/Ra           AttendancePolicy [124]
│   │   ├── Ngày công logic (xét ca qua đêm)
│   │   ├── Lần đầu = Vào · lần sau = Ra lấy MUỘN NHẤT
│   │   ├── < 5 phút sau giờ Vào ⇒ không ghi
│   │   ├── < 3 phút sau lần chấm gần nhất ⇒ không ghi
│   │   └── Múi giờ Asia/Ho_Chi_Minh
│   │
│   ├── 6.4 Chống giả mạo
│   │   ├── Active-flash (CỐ ĐỊNH bật, đã gỡ công tắc)
│   │   ├── Silent-Face anti-spoof
│   │   ├── Kiểm mở mắt / nhìn thẳng (client ML Kit nhắc, server enforce)
│   │   └── Cấu hình: 5 ep
│   │       ├── GET/PUT /api/chamcong/motion-config
│   │       ├── GET/PUT /api/chamcong/smile-config
│   │       ├── PUT     /api/chamcong/eyeopen-config
│   │       ├── GET     /api/chamcong/liveness-metrics
│   │       ├── POST    /api/chamcong/qr
│   │       └── POST    /api/chamcong/qr-sites
│   │
│   ├── 6.5 Đăng ký khuôn mặt          10 ep
│   │   ├── GET    /api/chamcong/dadangky
│   │   ├── GET    /api/chamcong/dangky/log
│   │   ├── POST   /api/chamcong/dangky          (admin đăng ký hộ)
│   │   ├── GET    /api/chamcong/dangky/cua-toi
│   │   ├── POST   /api/chamcong/dangky/tu       (NV tự đăng ký, 1 lần, 3 góc)
│   │   ├── GET    /api/chamcong/face-enrollments
│   │   ├── POST   /api/chamcong/face-enrollments/{id}/approve
│   │   ├── POST   /api/chamcong/face-enrollments/{id}/reject
│   │   ├── DELETE /api/chamcong/dangky/{username}
│   │   ├── DELETE /api/chamcong/dangky/mau/{id}
│   │   └── FaceEnrollmentCleanupService [81] — xoá mẫu chờ duyệt > 14 ngày
│   │
│   ├── 6.6 Chấm công ngoại tuyến      8 ep
│   │   ├── GET  /api/chamcong/log
│   │   ├── GET  /api/chamcong/offline/mine
│   │   ├── GET  /api/chamcong/offline-policy
│   │   ├── GET  /api/chamcong/offline
│   │   ├── POST /api/chamcong/offline/{id}/approve
│   │   ├── POST /api/chamcong/offline/{id}/reject
│   │   ├── GET/PUT /api/chamcong/offline-config
│   │   └── Cờ rủi ro: lùi giờ máy · không cùng LAN · ngoài geofence
│   │
│   ├── 6.7 Ca làm việc                ShiftEndpoints [896] · 10 ep · [perm:attendance.self]
│   │   ├── Ca:        GET/POST /api/shifts/ · PUT/DELETE /api/shifts/{id}
│   │   ├── Phân ca:   GET/POST /api/shifts/assignments · DELETE /assignments/{id}
│   │   └── Ngày lễ:   GET/POST /api/shifts/holidays · DELETE /holidays/{id}
│   │
│   ├── 6.8 Bảng công                  2 ep
│   │   ├── GET /api/timesheet/me
│   │   ├── GET /api/timesheet/employee/{id}
│   │   ├── VIEW hr_effective_attendance_log
│   │   └── Tăng ca: sáng(<08:00) + chiều(>17:00), mỗi vế ≥15 phút, xét ĐỘC LẬP
│   │
│   ├── 6.9 Lịch cá nhân               ScheduleEndpoints [75] · 1 ep
│   │   └── GET /api/schedule/ical     (.ics cho Google/Apple/Outlook)
│   │
│   └── 6.10 Nhắc chấm công            AttendanceReminderService [225] + Worker
│       └── Reconcile bền, ledger hr_attendance_reminders là nguồn idempotency
│
├── G7. LƯƠNG & PHẠT  ──────────────────────────────────  [41 ep · ~3.000 LOC]
│   │
│   ├── 7.1 Bảng lương                 PayrollEndpoints [1846] · 15 ep
│   │   ├── Cấu trúc lương
│   │   │   ├── GET /api/payroll/salaries
│   │   │   ├── GET /api/payroll/salaries/{employeeId}
│   │   │   └── PUT /api/payroll/salaries/{employeeId}     [perm:payroll.manage]
│   │   ├── Của tôi
│   │   │   ├── GET  /api/payroll/my-estimate       (lương dự tính, gồm phạt)
│   │   │   ├── GET  /api/payroll/my-day            (nhật ký ngày)
│   │   │   ├── GET  /api/payroll/my-payslips
│   │   │   ├── GET  /api/payroll/my-payslips/requirement
│   │   │   ├── POST /api/payroll/my-payslips/{id}/ack
│   │   │   ├── POST /api/payroll/my-payslips/{id}/inquiries   (khiếu nại)
│   │   │   └── GET  /api/payroll/my-payslips/{id}/pdf
│   │   ├── Lập lương
│   │   │   ├── GET  /api/payroll/compute
│   │   │   ├── GET  /api/payroll/payslips/published
│   │   │   ├── GET  /api/payroll/payslips/history
│   │   │   └── POST /api/payroll/payslips
│   │   ├── Xuất Excel
│   │   │   └── GET /api/payroll/export  (ClosedXML: mỗi NV 1 sheet + phiếu 6/A4)
│   │   └── ★ Trigger CSDL: hr_payslip_history append-only
│   │
│   ├── 7.2 Phạt / kỷ luật             PenaltyEndpoints [611] · 7 ep · [perm:penalty.read]
│   │   ├── GET    /api/penalties/types
│   │   ├── GET    /api/penalties/deductions
│   │   ├── GET    /api/penalties/
│   │   ├── POST   /api/penalties/                [perm:penalty.manage]
│   │   ├── PUT    /api/penalties/{id}
│   │   ├── POST   /api/penalties/{id}/waive
│   │   ├── DELETE /api/penalties/{id}
│   │   └── ★ Sổ cái hr_penalty_ledger:
│   │       cap theo lương còn lại · carry-over sang kỳ sau
│   │       tổng thu ≤ mức phạt · thu đủ ⇒ "Đã tất toán"
│   │       khiếu nại chốt theo ĐÃ THU
│   │
│   └── 7.3 Hoàn tiền phạt             PenaltyRefundEndpoints [205] · 4 ep
│       ├── GET  /api/penalty-refunds/
│       ├── POST /api/penalty-refunds/{id}/approve
│       ├── POST /api/penalty-refunds/{id}/reject
│       ├── POST /api/penalty-refunds/{id}/mark-paid
│       └── Hình thức: cộng phiếu lương kỳ sau HOẶC chi tiền mặt (→ nguồn phiếu chi)
│           Người xử lý phải thuộc phòng ban is_accounting
│
├── G8. GIAO VIỆC & GIAO HÀNG  ─────────────────────────  [26 ep · ~1.900 LOC]
│   │
│   ├── 8.1 Giao việc & nghiệm thu     TaskAssignmentEndpoints [877] · 14 ep
│   │   ├── Vòng đời CHUẨN
│   │   │   assigned → in_progress → submitted → accepted
│   │   │   submitted → rejected → in_progress   (nộp lại)
│   │   │   * → cancelled
│   │   ├── Vòng đời GIAO HÀNG (source_kind='delivery') — KHÔNG có nghiệm thu
│   │   │   assigned → in_progress → submitted → completed
│   │   ├── Tra cứu
│   │   │   ├── GET /api/tasks/meta
│   │   │   ├── GET /api/tasks/
│   │   │   ├── GET /api/tasks/history
│   │   │   └── GET /api/tasks/{id}
│   │   ├── Người giao                                     [perm:tasks.assign]
│   │   │   ├── POST   /api/tasks/
│   │   │   ├── PUT    /api/tasks/{id}
│   │   │   ├── POST   /api/tasks/{id}/accept
│   │   │   ├── POST   /api/tasks/{id}/reject
│   │   │   ├── POST   /api/tasks/{id}/cancel
│   │   │   └── DELETE /api/tasks/{id}
│   │   └── Người nhận                                     [perm:tasks.self]
│   │       ├── POST /api/tasks/{id}/start
│   │       ├── POST /api/tasks/{id}/progress
│   │       ├── POST /api/tasks/{id}/submit
│   │       └── POST /api/tasks/{id}/comment
│   │
│   ├── 8.2 Gán phiếu giao hàng        DeliveryAssignmentEndpoints [499] · 4 ep
│   │   ├── GET  /api/delivery-assignments/drivers
│   │   ├── GET  /api/delivery-assignments
│   │   ├── GET  /api/documents/{id}/delivery
│   │   ├── POST /api/documents/{id}/delivery
│   │   └── ★ 5 bất biến:
│   │       1. chỉ gán phiếu đã phát hành & chưa huỷ
│   │       2. mỗi phiếu ĐÚNG MỘT việc giao hàng còn sống
│   │       3. đổi "khách lấy tại kho" ⇒ huỷ việc đang mở
│   │       4. đổi lái xe khi in_progress/rejected ⇒ BẮT BUỘC có lý do + báo lái xe cũ
│   │       5. từ submitted/completed ⇒ HẾT đổi
│   │
│   └── 8.3 Đối soát phiếu về kho      DeliverySettlementEndpoints [453] · 3 ep
│       ├── GET  /api/documents/{id}/settlement
│       ├── PUT  /api/documents/{id}/settlement
│       ├── POST /api/documents/{id}/settlement/return
│       └── ★ document_issued_lines BẤT BIẾN = con số trên tờ giấy đã in
│           Sửa document_lines = hàng THỰC NHẬN ⇒ báo cáo/công nợ tự khớp
│           Mỗi dòng đổi ⇒ bản ghi cũ→mới ở document_line_edits (lý do + người sửa)
│           Đóng được từ BẤT KỲ chặng nào chưa kết thúc
│
├── G9. GIAO TIẾP & CỔNG THÔNG TIN  ────────────────────  [58 ep · ~2.800 LOC]
│   │
│   ├── 9.1 Trò chuyện                 ChatEndpoints [1433] · 26 ep · [perm:chat.access]
│   │   ├── Gọi thoại / video (P2P WebRTC, DTLS-SRTP)
│   │   │   ├── POST /api/chat/call/ring
│   │   │   ├── GET  /api/chat/call/turn        (TURN Cloudflare/coturn có hạn giờ)
│   │   │   ├── POST /api/chat/call/cancel
│   │   │   ├── POST/GET /api/chat/call/missed
│   │   │   ├── POST /api/chat/call/missed/seen
│   │   │   └── POST/GET /api/chat/call/history
│   │   ├── Hội thoại
│   │   │   ├── GET  /api/chat/contacts
│   │   │   ├── GET  /api/chat/conversations
│   │   │   ├── POST /api/chat/direct/{username}
│   │   │   ├── POST /api/chat/support/{username}
│   │   │   ├── POST /api/chat/conversations/{id}/read
│   │   │   ├── POST /api/chat/conversations/{id}/pin
│   │   │   ├── POST /api/chat/conversations/{id}/hide
│   │   │   ├── POST /api/chat/conversations/{id}/report
│   │   │   └── DELETE /api/chat/conversations/{id}
│   │   ├── Tin nhắn
│   │   │   ├── GET  /conversations/{id}/messages
│   │   │   ├── POST /conversations/{id}/messages
│   │   │   ├── POST /conversations/{id}/messages/file
│   │   │   ├── POST /conversations/{id}/messages/{msgId}/upload
│   │   │   ├── GET  /conversations/{id}/messages/{msgId}/download
│   │   │   ├── PUT  /conversations/{id}/messages/{msgId}
│   │   │   ├── DELETE /conversations/{id}/messages/{msgId}
│   │   │   └── POST /conversations/{id}/messages/{msgId}/react
│   │   ├── GET /api/chat/db-usage
│   │   └── Chính sách tệp            ChatAttachmentPolicy [92]
│   │       ├── file thường ⇒ giữ TẠM (LanFileCleanupService [82] dọn mỗi giờ)
│   │       └── voice/ảnh/video ⇒ nội dung bền, chỉ xoá khi gỡ tin
│   │
│   ├── 9.2 Phản hồi & hỗ trợ          FeedbackEndpoints [228] · 9 ep
│   │   ├── GET  /api/feedback/
│   │   ├── POST /api/feedback/attendance
│   │   ├── POST /api/feedback/{id}/resolve      → sự kiện feedbackResolved
│   │   ├── GET  /api/feedback/surveys/open
│   │   ├── POST /api/feedback/surveys/{id}/responses
│   │   ├── POST/GET /api/feedback/general[/mine]
│   │   └── POST/GET /api/feedback/support[/mine]   (mã yêu cầu, phiên bản app, loại máy)
│   │
│   ├── 9.3 Khảo sát & bình chọn       SurveyEndpoints [334] · 8 ep · [perm:portal.read]
│   │   ├── POST   /api/surveys/
│   │   ├── GET    /api/surveys/
│   │   ├── GET    /api/surveys/active
│   │   ├── GET    /api/surveys/{id}
│   │   ├── POST   /api/surveys/{id}/respond
│   │   ├── GET    /api/surveys/{id}/results
│   │   ├── POST   /api/surveys/{id}/close
│   │   ├── DELETE /api/surveys/{id}
│   │   └── ★ ẨN DANH THẬT: không lưu username, chống trùng bằng
│   │       HMAC-SHA256(Jwt:Key, "surveyId|username")
│   │       ⚠️ đổi Jwt:Key ⇒ mọi người trả lời lại được
│   │
│   ├── 9.4 Cổng thông tin             PortalEndpoints [361] · 7 ep
│   │   ├── GET /api/portal/feed             (app hiển thị)
│   │   ├── GET/POST /api/portal/posts
│   │   ├── PUT/DELETE /api/portal/posts/{id}          [perm:portal.manage]
│   │   └── GET/PUT /api/portal/about
│   │
│   ├── 9.5 Trung tâm trợ giúp         HelpEndpoints [110] · 5 ep
│   │   ├── GET /api/help/faqs
│   │   ├── POST/PUT/DELETE /api/help/faqs[/{id}]
│   │   └── GET /api/help/status
│   │
│   └── 9.6 Bảng tin điều hành         ManagementFeed [95] · (thư viện)
│       └── CHỈ chuông web, KHÔNG FCM · người tự gây sự kiện không nhận tin của mình
│
└── G10. HỆ THỐNG · APK · NHẬT KÝ  ─────────────────────  [30 ep · ~1.900 LOC]
    │
    ├── 10.1 Hộp thư thông báo         NotificationEndpoints [182] · 7 ep
    │   ├── POST /api/notifications/register-token     (FCM, token = PRIMARY KEY)
    │   ├── POST /api/notifications/unregister-token
    │   ├── GET  /api/notifications/
    │   ├── POST /api/notifications/{id}/read
    │   ├── POST /api/notifications/read-all
    │   ├── DELETE /api/notifications/read
    │   └── DELETE /api/notifications/{id}
    │
    ├── 10.2 Tuỳ chọn cá nhân          PreferenceEndpoints [179] · 4 ep
    │   ├── GET/PUT /api/preferences/
    │   └── GET/PUT /api/preferences/notifications   (5 nhóm, chốt SERVER-side)
    │
    ├── 10.3 Bản cập nhật APK          ReleaseEndpoints [425] · 9 ep
    │   ├── Công khai
    │   │   ├── GET /api/releases/public/latest
    │   │   └── GET /api/releases/public/{id}/download        [A]
    │   ├── Người dùng
    │   │   ├── GET /api/releases/latest
    │   │   └── GET /api/releases/{id}/download
    │   ├── Quản trị                                 [perm:system.releases.manage]
    │   │   ├── GET    /api/releases/
    │   │   ├── POST   /api/releases/     (200MB, DisableAntiforgery)
    │   │   ├── POST   /api/releases/{id}/publish
    │   │   ├── DELETE /api/releases/{id}
    │   │   └── POST   /api/releases/bulk-delete
    │   └── ReleaseStorage [159] — đĩa, buffer 80KB, <id>.apk + <guid>.upload
    │
    ├── 10.4 Cấu hình app từ xa        AppConfigEndpoints [276] · 2 ep
    │   ├── GET /api/app-config    (mọi user đăng nhập)
    │   ├── PUT /api/app-config    (admin)            [perm:system.settings.manage]
    │   └── Điều khiển: thông báo chạy chữ · banner nhắc đăng ký mặt · nhịp làm mới nền
    │       ⇒ đổi KHÔNG cần phát hành APK mới
    │
    └── 10.5 Nhật ký hoạt động         AuditEndpoints [467] · 3 ep · [perm:audit.read]
        ├── GET /api/audit/         (phân trang + tương thích tham số "take" cũ)
        ├── GET /api/audit/filters
        ├── GET /api/audit/export   (CSV / Excel theo đúng bộ lọc đang xem)
        ├── Lọc: người dùng · hành động · đối tượng · THÁNG (yyyy-MM) · nhóm nghiệp vụ · khoảng
        ├── Che: mật khẩu · token · hash · embedding
        └── ★ Phạm vi do SERVER ép (ResolveScopeAsync):
            Admin = toàn bộ · Kế toán (role + phòng is_accounting) = CHỈ PHẦN TIỀN · còn lại từ chối
            Trang web là /saoluu (KHÔNG phải trang sao lưu)
```

---

## PHỤ LỤC A — 9 TIẾN TRÌNH NỀN (chia kèm gói nào)

| Worker | Gói | Nhịp |
|---|---|---|
| `QrLoginService` (tự dọn phiên) | G1 | nền |
| `OutboxWorker` | G0 | lease 2 phút |
| `AttendanceReminderWorker` | G6 | định kỳ |
| `FaceEngineIdleUnloader` | G6 | 10 phút nhàn rỗi |
| `HubPresenceRefresher` | G0 | 45 giây |
| `ChangeWatcher` | G0 | liên tục |
| `LanFileCleanupService` | G9 | 1 giờ |
| `FaceEnrollmentCleanupService` | G6 | boot + mỗi giờ |

## PHỤ LỤC B — 114 BẢNG CHIA THEO GÓI

| Gói | Bảng chính |
|---|---|
| G0 | `app_outbox`, `schema_migrations`, `web_notifications`, `hr_device_tokens` |
| G1 | `app_users`, `user_sessions`, `user_roles`, `user_role_history`, `system_roles`, `registration_codes`, `password_reset_requests`, `password_recovery_codes`, `work_access_requests`, `app_pin_codes`, `web_login_settings`, `web_verified_users`, `web_diamond_members`, `web_user_avatars` |
| G2 | `documents`, `document_lines`, `document_issued_lines`, `document_line_edits`, `customers`, `customer_aliases`, `customer_opening_balances`, `payments`, `products`, `gia_cong_phieu`, `gia_cong_hang_hoa` |
| G3 | `suppliers`, `purchases`, `purchase_lines` |
| G4 | `cash_collection_orders`, `cash_collection_events`, `cash_count_sessions`, `cash_count_lines`, `cash_fund_manual_entries`, `hr_payout_vouchers`, `hr_payout_voucher_events`, `hr_payout_categories`, `hr_bank_accounts`, **view** `cash_fund_ledger` |
| G5 | `hr_employees`, `hr_departments`, `hr_locations`, `hr_job_positions`, `hr_employee_positions`, `hr_contracts`, `hr_salary_raises`, `hr_documents`, `hr_leave_balances`, `hr_anniversary_letter`, `hr_employee_benefits`, `hr_employee_rewards`, `hr_requests`, `hr_request_approvals`, `hr_request_attachments`, `hr_approval_delegations`, `hr_onboarding_tasks`, `hr_performance_goals`, `hr_performance_reviews`, `hr_training_courses`, `hr_training_enrollments` |
| G6 | `cham_cong_log`, `cham_cong_face`, `cham_cong_face_enrollments`, `cham_cong_face_enrollment_samples`, `cham_cong_offline`, `cham_cong_qr_sites`, `hr_shifts`, `hr_shift_assignments`, `hr_holidays`, `hr_attendance_corrections`, `hr_attendance_reminders`, **view** `hr_effective_attendance_log` |
| G7 | `hr_salaries`, `hr_payslips`, `hr_payslip_history`, `hr_payslip_inquiries`, `hr_penalties`, `hr_penalty_ledger`, `hr_penalty_refunds` |
| G8 | `work_tasks`, `work_task_events` |
| G9 | `web_chat_conversations`, `web_chat_members`, `web_chat_messages`, `web_chat_reactions`, `web_chat_reports`, `web_call_events`, `web_call_history`, `app_portal_posts`, `app_portal_about`, `app_surveys`, `app_survey_responses`, `surveys`, `survey_questions`, `survey_responses`, `survey_answers`, `help_faqs`, `app_feedbacks`, `app_general_feedback`, `app_support_tickets` |
| G10 | `app_config`, `app_settings`, `app_releases`, `audit_logs`, `web_system_settings`, `web_user_preferences` |

## PHỤ LỤC C — QUY TẮC CHUNG CHO MỌI NGƯỜI NHẬN VIỆC

1. **Không gọi hub từ endpoint.** Muốn màn hình tự làm mới ⇒ thêm bảng vào danh sách trigger realtime.
2. **Chốt cửa bằng quyền** (`.RequirePermission`), không bằng tên vai trò. Trong handler chỉ được
   kiểm **phạm vi dữ liệu**.
3. **Ghi push chỉ qua `PushService`** — cửa duy nhất vào hộp thư `web_notifications`.
4. **Việc-có-hậu-quả ⇒ vào outbox**, không gọi FCM trong request.
5. **`@x IS NULL OR col = @x` phải ép kiểu** (`::uuid`, `::date`) — nếu không sẽ ra lỗi `42P08`
   hiện thành "mất kết nối DB" giả.
6. **Lọc tháng dùng khoảng ngày**, không `to_char`.
7. Mọi thay đổi phải kèm **golden test** của các endpoint đụng tới (xem `backend-port-spec.md` §8.1).
