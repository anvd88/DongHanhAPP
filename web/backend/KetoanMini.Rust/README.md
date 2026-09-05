# KetoanMini Rust server

Tiến trình backend Rust độc lập cho KetoanMini. Mục tiêu là giữ nguyên contract của web và Android,
chuyển từng vertical slice sang Rust và cuối cùng thay thế tiến trình ASP.NET Core mà không đổi hành vi
nghiệp vụ.

## Chế độ tương thích

Trong thời gian port, Rust đứng trước backend .NET:

- Route đã port chạy trực tiếp bằng Rust.
- Route chưa port, SPA, upload và SSE được stream sang .NET; không buffer toàn bộ request/response.
- `Authorization`, cookie HttpOnly, `X-CSRF-Token`, status, JSON và `Set-Cookie` được giữ nguyên.

Upstream bị giới hạn cứng ở loopback để token/cookie không thể bị gửi tới máy ngoài do cấu hình sai.
Rust không ghi URI/query/header vào log vì chúng có thể chứa token hoặc mã QR bí mật.

## Cấu hình

| Biến | Mặc định | Ý nghĩa |
|---|---:|---|
| `KETOANMINI_RUST_BIND` | `127.0.0.1:5240` | Listener riêng của tiến trình Rust. |
| `KETOANMINI_COMPAT_UPSTREAM` | trống | Ví dụ `http://127.0.0.1:5239`; bỏ khi đã port xong. |
| `KETOANMINI_DATABASE_URL` | bắt buộc | URL PostgreSQL; có thể dùng connection string Npgsql cũ. |
| `ConnectionStrings__KetoanMini` | fallback | Tái sử dụng secret hiện tại mà không ghi vào source. |
| `KETOANMINI_JWT_KEY` / `Jwt__Key` | bắt buộc | Cùng khóa ký JWT với .NET, tối thiểu 32 byte. |
| `Jwt__Issuer`, `Jwt__Audience` | `KetoanMini.Web` | Phải trùng cấu hình .NET. |
| `Jwt__WebExpireHours` | `168` | Thời hạn cookie web trước gia hạn trượt. |
| `Security__SessionIdleDays` | `7` | Hết hạn phiên không hoạt động; `0` là tắt. |
| `KETOANMINI_DB_MAX_CONNECTIONS` | `20` | Trần pool SQLx. |
| `KETOANMINI_DB_ACQUIRE_TIMEOUT_MS` | `3000` | Thời gian chờ pool. |
| `RUST_LOG` | info | Bộ lọc log có cấu trúc. |

TLS phải kết thúc ở Cloudflare/front proxy và chuyển vào listener loopback. Không mở listener HTTP ra LAN
hoặc Internet. Backend .NET vẫn là migration/worker owner cho tới khi từng ownership được chuyển rõ ràng.

## Chạy riêng

Toolchain Windows của dự án dùng host `gnullvm`. Trên máy mới, đặt host trước khi để
`rust-toolchain.toml` tự cài đúng Rust đã ghim:

```powershell
.\scripts\setup-toolchain-windows.ps1
```

Script ghim Rust `1.98.0`, LLVM-MinGW `20260616`, kiểm SHA-256 trước khi giải nén và chỉ đặt linker
trong `PATH` của lệnh Cargo con; không sửa `PATH` toàn máy. Có thể đặt
`KETOANMINI_LLVM_MINGW_BIN` nếu công ty quản lý LLVM-MinGW ở vị trí khác.

```powershell
cd backend/KetoanMini.Rust
$env:KETOANMINI_COMPAT_UPSTREAM = 'http://127.0.0.1:5239'
$env:ConnectionStrings__KetoanMini = '<secret hiện tại>'
.\scripts\cargo-windows.ps1 test --locked
.\scripts\cargo-windows.ps1 run --release --locked
```

Kiểm tra qua tiến trình Rust: `http://127.0.0.1:5240/api/info`. Không chuyển tunnel/domain production
trước khi contract/security suite và rollback test hoàn tất.

## Đóng gói Windows

```powershell
cd backend/KetoanMini.Rust
.\scripts\package-windows.ps1
```

Gói `dist/windows-x64` chứa executable riêng, `libunwind.dll` cần cho toolchain
`windows-gnullvm`, tệp cấu hình mẫu và SHA-256 manifest. Khi chạy bằng Windows service/supervisor, đặt
secret trong environment của service; không sao chép giá trị thật vào thư mục triển khai. Tiến trình
Rust chạy foreground để supervisor quản lý restart và graceful shutdown.

## Phần đã chạy native Rust

Hiện có **68/396 operation** chạy native; bề mặt này được đối chiếu tự động với
`docs/openapi.baseline.json` trong `tests/native_surface.rs`.

- `/api/info`, `/api/health`
- `/api/auth/me`, `/api/auth/access-profile`
- `/api/preferences`, `/api/preferences/notifications`
- `/api/directory`, `/api/directory/org-chart`
- `/api/schedule/ical`
- `/api/worklist`
- `/api/help/faqs`, `/api/help/status`
- `/api/bank-accounts`, `/api/bank-accounts/banks`
- `/api/notifications` (feed, đọc, đọc tất cả, xóa đã đọc, xóa một mục, đăng ký/hủy token)
- `/api/roles/catalog`, `/api/penalties/types`
- `/api/app-config`
- `/api/portal/feed`, `/api/portal/posts`, `/api/portal/about`
- `/api/talent/onboarding`, `/api/talent/performance`, `/api/talent/training`, `/api/talent/benefits`
- `/api/surveys`
- `/api/penalty-refunds`
- `/api/giacong`

Các route này dùng chung middleware JWT/cookie/CSRF/session/RBAC của Rust. Mọi route còn lại vẫn đi qua
streaming compatibility gateway nên SPA, Android và upload tiếp tục hoạt động trong khi từng
bounded context được port và kiểm thử. Rust chỉ kiểm tra schema ở startup bằng transaction read-only;
.NET vẫn là migration owner trong giai đoạn này. Guard cũng xác minh unique partial index chống gửi khảo
sát trùng; Rust không tự tạo hoặc sửa index khi khởi động.

Token push luôn lấy chủ sở hữu từ `AuthContext`; body không được tự khai username. Khi một thiết bị đổi
tài khoản, đăng ký mới chuyển token sang đúng người đang xác thực, còn hủy đăng ký chỉ được xóa token của
chính tài khoản đó. Đây là ranh giới bắt buộc để Android không nhận thông báo chéo khi dùng chung máy.

Xem [ARCHITECTURE.md](ARCHITECTURE.md) để biết ranh giới tiến trình, nguyên tắc một writer và thứ tự
cutover; xem [CUTOVER.md](CUTOVER.md) để chạy health gate và rollback trên máy Windows triển khai.
