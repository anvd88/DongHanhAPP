# Kiến trúc backend Rust KetoanMini

## Mục tiêu

Tiến trình `ketoanmini-server` là backend độc lập, bind mặc định `127.0.0.1:5240`. Một origin ổn định
(Cloudflare/front proxy) trỏ vào tiến trình này để web và Android không cần đổi contract. Route đã port
được xử lý bằng Axum/SQLx; route chưa port được stream tới ASP.NET Core chỉ qua loopback.

```text
Web + Android
      |
 TLS / một API origin
      |
 ketoanmini-server :5240
      |-- native Rust route ------ PostgreSQL
      |-- compatibility stream --- ASP.NET :5239
                                      |-- FCM / face
                                      |-- Excel COM / printer / blob worker
```

Gateway không buffer toàn bộ body và giữ nguyên Authorization/cookie/CSRF/status/Set-Cookie. Upstream
bị giới hạn cứng ở HTTP loopback để cấu hình sai không gửi credential ra
máy khác. URI, query và header không được ghi log vì có thể chứa JWT hoặc mã QR.

## Cấu trúc source

```text
src/
  auth/       JWT, cookie/CSRF, session freshness, role, permission, password hash
  compat/     streaming reverse proxy cho HTTP/SSE
  http/       router, security headers, native route modules
  db.rs       pool và parser connection string Npgsql/PostgreSQL
  schema.rs   startup guard read-only; tuyệt đối không chạy DDL
  network.rs  trusted proxy và phân loại mạng nội bộ
  state.rs    immutable shared application state
```

Mỗi domain là một module route độc lập. Handler nhận danh tính từ `AuthContext`, không nhận username/user
ID từ body để quyết định ownership. SQL luôn bind parameter. JSON giữ camelCase, bỏ null và thời gian UTC
`.fffZ` như backend hiện tại.

## Bất biến khi chuyển đổi

1. Mỗi domain chỉ có một writer tại một thời điểm; không dual-write .NET/Rust.
2. Chỉ một migration runner. Giai đoạn tương thích giữ .NET làm schema owner, Rust chỉ fail startup nếu
   schema thiếu.
3. Workflow phải được chuyển nguyên khối cùng transaction, `FOR UPDATE`, advisory lock, audit và outbox.
4. Contract test so status, JSON, content type, header, cookie và binary body; không chỉ so dữ liệu cuối.
5. DDL trong thời gian rollback phải additive/backward-compatible.
6. PostgreSQL và release volume phải được snapshot cùng một mốc trước cutover.
7. Mọi state phía client có dữ liệu theo tài khoản (toast và callback async)
   phải bị hủy hoặc đổi generation tại ranh giới đăng xuất/đăng nhập; xóa DOM hiện tại thôi chưa đủ.

## Thành phần giữ sidecar ban đầu

- Firebase/outbox delivery: giữ đúng lease 2 phút, retry, dedupe, TTL và token pruning.
- Face: ONNX/OpenCV, liveness và AES-GCM embedding `KME1` cho đến khi có golden corpus.
- Excel COM/máy in vật lý: chạy trong interactive Windows session của đúng user.
- QR/app login: chuyển cả cụm một lần hoặc dùng shared store vì state hiện sống RAM 5 phút.

## Thứ tự port

1. Identity/session/RBAC và API đọc.
2. CRUD ít side effect theo nguyên route group.
3. Requests, shifts/timesheet, HR và task state machine.
4. Accounting, cash collection, payout, payroll và penalty với concurrency tests.
5. File upload/download khi đã chốt một owner cho volume.
6. Realtime/workers/native sidecars sau golden tests.

## Nợ bảo mật tương thích cần xử lý có kiểm soát

Backend hiện tại cho JWT hợp lệ tiếp tục qua endpoint thường nếu `sid` không có dòng `user_sessions`, và
fail-open danh tính cơ bản khi DB lỗi (permission đặc quyền vẫn fail-closed). Rust đang giữ hành vi này để
không làm hỏng token/app hiện hữu. Sau khi mọi client được phát lại session có SID, cần bật strict-session
theo một đợt migration riêng; không siết ngầm trong lúc port chức năng.
