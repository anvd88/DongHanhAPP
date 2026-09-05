# Security cutover checklist

Các giá trị bí mật từng vào Git phải được coi là đã lộ. Xóa chúng khỏi commit hiện tại không làm
chúng an toàn trở lại; phải cấp credential mới ở hệ thống nguồn, triển khai credential mới, restart,
rồi thu hồi credential cũ.

## Thứ tự rotate

1. PostgreSQL: tạo role/password mới có đúng quyền cần thiết; cập nhật
   `ConnectionStrings__KetoanMini`; restart API; thử kết nối bằng credential mới; sau đó `ALTER ROLE`
   hoặc xóa role/password cũ và xác nhận credential cũ bị `password authentication failed`.
2. TURN: tạo Cloudflare TURN key/token mới hoặc đổi `static-auth-secret` của coturn; cập nhật
   `Turn__Cloudflare__KeyId` + `Turn__Cloudflare__ApiToken` hoặc `Turn__Secret`; restart API/coturn;
   thu hồi key/token/secret cũ và thử cấp credential bằng key cũ phải thất bại.
3. Firebase/API keys: tạo service account/key mới với quyền tối thiểu; cập nhật file secret ngoài Git
   (`Firebase__CredentialsPath`); restart API; disable/delete private key cũ trong Firebase/GCP IAM.
4. JWT: đặt `Jwt__Key` thành chuỗi ngẫu nhiên mới ít nhất 32 byte rồi restart API. Mọi JWT ký bằng
   key cũ phải trả 401. Không lưu signing key vào Git hay APK.
5. Bootstrap admin: đặt `Bootstrap__AdminUsername` riêng và mật khẩu ngẫu nhiên tối thiểu 14 ký tự.
   Cấu hình theo dõi bởi Git để trống. Production sẽ từ chối khởi động nếu còn `admin`, `admin123`,
   secret bắt buộc trống, placeholder hoặc quá ngắn.

Lệnh sinh secret nên chạy ngay trong secret manager/máy chủ, không dán kết quả vào ticket hoặc log:

```powershell
$bytes = New-Object byte[] 48
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($bytes)
[Convert]::ToBase64String($bytes)
```

## Cloudflare WAF và rate limiting

Public hostname phải đi qua Cloudflare Tunnel/proxy; nếu DNS-only hoặc mở origin trực tiếp thì WAF
không bảo vệ được đường đó. Bật Cloudflare Managed Rules cho zone, rồi tạo Rate Limiting Rules với
đặc tả tối thiểu sau (action `Block`, response `429`, counting characteristic theo IP nguồn):

| Tên | Expression | Ngưỡng khởi điểm |
|---|---|---:|
| Login | `http.request.method eq "POST" and http.request.uri.path eq "/api/auth/login"` | 5/phút/IP |
| Face reset | `http.request.method eq "POST" and http.request.uri.path eq "/api/auth/forgot-password-face"` | 2/10 phút/IP |
| Attendance | `http.request.method eq "POST" and starts_with(http.request.uri.path, "/api/chamcong/")` | 30/phút/IP |

Giữ limiter ASP.NET đang có làm lớp bảo vệ origin/LAN. Sau khi bật, chạy burst vượt từng ngưỡng và
xác nhận cả hostname public lẫn origin trả 429. Theo dõi Security Events 24 giờ đầu để điều chỉnh
ngưỡng chấm công cho các kiosk dùng chung một IP NAT.

## Tiêu chí nghiệm thu

- Credential DB/TURN/Firebase cũ không còn xác thực được.
- JWT lấy trước cutover trả 401 sau restart.
- `POST /api/auth/forgot-password-face` trả 404 khi không phải Development.
- `Employee` gọi `/api/documents`, `/api/customers`, `/api/dashboard`, `/api/reports` hoặc
  `/api/giacong` nhận 403.
- Nhân viên đổi `{id}` sang hồ sơ HR của người khác nhận 403.
- Burst vượt limiter nhận 429; JSON/APK/ảnh vượt trần nhận 413 hoặc 400.
