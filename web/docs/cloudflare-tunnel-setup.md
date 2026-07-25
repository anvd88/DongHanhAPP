# Public ra Internet qua Cloudflare Tunnel (Web + KetoanAPK)

Mục tiêu: cho web và app KetoanAPK truy cập được **ngoài mạng LAN** (từ 4G, nhà khác, chi nhánh khác) mà **không mở port router**, **không lộ IP**, dùng **cert HTTPS hợp lệ** (không còn cert tự ký `192.168.1.88`).

Domain thực tế đang dùng: **`ketoancp.click`** (đã ở Cloudflare). Tunnel: **`ketoancp`**.

> ⚠️ **Hai cách quản lý tunnel — chọn ĐÚNG cách bạn đã tạo:**
> - **Dashboard-managed (bạn đang dùng)**: tạo tunnel trên Zero Trust → cấu hình route bằng **Public Hostname trên dashboard**, KHÔNG dùng `config.yml`. → Xem [BƯỚC 3B](#bước-3b--dashboard-managed-cách-bạn-đang-dùng).
> - **Locally-managed**: tạo tunnel bằng CLI (`cloudflared tunnel create`) + file `config.yml` cục bộ. → Bước 3/4/5 gốc bên dưới.

---

## Kiến trúc

```
[Điện thoại/PC ngoài Internet]
        │  HTTPS 443 (cert Cloudflare hợp lệ)
        ▼
   Cloudflare Edge
        │  đường hầm mã hóa (outbound, không cần mở port)
        ▼
   cloudflared (chạy trên máy chủ Windows)
        │  https://localhost:5443  (noTLSVerify)
        ▼
   KetoanMini.Api (Kestrel, wwwroot + /api + /hubs)
```

Backend **không đổi**: tunnel trỏ thẳng vào endpoint HTTPS 5443 sẵn có. CORS đã mở, `AllowedHosts=*`, nên `<DOMAIN>` hoạt động ngay.

---

## BƯỚC 1 — Đưa domain về Cloudflare (bắt buộc, làm 1 lần)

DNS hiện chưa ở Cloudflare, nên phải chuyển nameserver trước thì Tunnel mới tự cấp cert + tạo bản ghi.

1. Tạo tài khoản miễn phí tại https://dash.cloudflare.com → **Add a site** → nhập domain gốc (ví dụ `congty.vn`).
2. Chọn gói **Free**. Cloudflare quét bản ghi DNS hiện có → kiểm tra giữ đủ (MX email, các A/CNAME đang dùng).
3. Cloudflare cấp **2 nameserver** (ví dụ `xxx.ns.cloudflare.com`). Vào trang quản trị domain ở nhà cung cấp hiện tại (Mắt Bão / PA / GoDaddy…) → đổi nameserver sang 2 cái này.
4. Chờ kích hoạt (thường vài phút–vài giờ, tối đa 24h). Khi dashboard báo **Active** là xong.

> Nếu chỉ muốn test nhanh chưa cần domain: chạy `cloudflared tunnel --url https://localhost:5443 --no-tls-verify` sẽ ra URL `*.trycloudflare.com` tạm (đổi mỗi lần chạy). Không dùng cho production.

---

## BƯỚC 2 — Cài cloudflared trên máy chủ

PowerShell (Admin):

```powershell
winget install --id Cloudflare.cloudflared
# hoặc tải .msi: https://github.com/cloudflare/cloudflared/releases
cloudflared --version
```

Đăng nhập (mở trình duyệt, chọn domain vừa add):

```powershell
cloudflared tunnel login
```

---

## BƯỚC 3B — Dashboard-managed (CÁCH BẠN ĐANG DÙNG)

Tunnel `ketoancp` đã tạo trên dashboard, đang **Healthy** nhưng **No routes** → chỉ cần thêm route:

1. **Zero Trust → Networks → Tunnels → `ketoancp` → Configure → tab Public Hostname → Add a public hostname**.
2. Chọn **Published application**. Điền:
   - **Subdomain**: `app`  → host thành `app.ketoancp.click`
   - **Domain**: `ketoancp.click`
   - **Path**: để trống
   - **Service URL**: `http://localhost:5239`
3. **Save**.

   > ⚠️ Giao diện "Published application" mới của Cloudflare **đã bỏ nút "No TLS Verify"**. Vì vậy KHÔNG dùng origin HTTPS self-signed (`https://localhost:5443`) — sẽ bị **502** do cloudflared không tin cert mkcert. Thay bằng **cổng HTTP thuần `http://localhost:5239`**. Backend đã thêm `UseForwardedHeaders` đọc `X-Forwarded-Proto=https` (Cloudflare gắn) nên không bị vòng lặp redirect mà vẫn ép HTTPS cho request http thật. Lớp HTTPS công khai do cert Cloudflare lo.

Đảm bảo:
- **cloudflared connector chạy trên đúng máy chủ có backend** (máy có Kestrel `localhost:5443`). Cài connector bằng lệnh token mà dashboard cho khi tạo tunnel (`cloudflared service install <token>`), hoặc `Get-Service cloudflared` → Running.
- **Backend đang chạy** (`--launch-profile lan`).

Với cách này **bỏ qua BƯỚC 3/4/5 gốc và `config.example.yml`** (chúng chỉ dành cho locally-managed). Nhảy tới [BƯỚC 6](#bước-6--cập-nhật-ketoanapk-trỏ-ra-domain).

---

## BƯỚC 3 — Tạo tunnel + route DNS (chỉ khi locally-managed)

```powershell
# Tạo tunnel tên "ketoanmini" -> sinh file credentials <TUNNEL_ID>.json trong %USERPROFILE%\.cloudflared\
cloudflared tunnel create ketoanmini

# Trỏ subdomain <DOMAIN> vào tunnel (tự tạo bản ghi CNAME proxied trên Cloudflare)
cloudflared tunnel route dns ketoanmini <DOMAIN>
```

Ghi lại `<TUNNEL_ID>` in ra ở bước create (hoặc `cloudflared tunnel list`).

---

## BƯỚC 4 — File cấu hình ingress

Tạo `%USERPROFILE%\.cloudflared\config.yml` (mẫu có sẵn trong repo: [`deploy/cloudflared/config.example.yml`](../deploy/cloudflared/config.example.yml)):

```yaml
tunnel: ketoanmini
credentials-file: C:\Users\Admin\.cloudflared\<TUNNEL_ID>.json

ingress:
  - hostname: <DOMAIN>
    service: https://localhost:5443
    originRequest:
      # cert Kestrel tự ký cho 192.168.1.88/localhost -> bỏ qua verify ở chặng nội bộ localhost
      noTLSVerify: true
      # giữ WebSocket cho SignalR (/hubs) sống lâu
      connectTimeout: 30s
  - service: http_status:404
```

---

## BƯỚC 5 — Chạy cloudflared như dịch vụ Windows (tự bật khi khởi động)

```powershell
cloudflared service install
Start-Service cloudflared
Get-Service cloudflared        # kiểm tra Running
```

Kiểm tra ngoài Internet: mở `https://<DOMAIN>` trên điện thoại **tắt WiFi (dùng 4G)** → phải thấy trang đăng nhập web, ổ khóa HTTPS xanh (cert Cloudflare).

> Backend vẫn phải đang chạy (`dotnet run --launch-profile lan`, xem memory dev-run-setup). Tunnel chỉ là ống dẫn.

---

## BƯỚC 6 — Cập nhật KetoanAPK trỏ ra domain

**Code APK đã được sửa sẵn cho `ketoancp.click`** (đã áp dụng trong repo):

1. ✅ `Ketoanapk/android/app/build.gradle` → `API_BASE_URL = "https://ketoancp.click"` (bỏ `:5443`, Cloudflare phục vụ 443); `versionCode 16→17`, `versionName 1.2.11`.
2. ✅ `Ketoanapk/android/app/src/main/res/xml/network_security_config.xml` → domain `ketoancp.click`, chỉ tin CA `system` (bỏ CA riêng `ketoanmini_ca`).
3. ~~`frontend/capacitor.config.ts` → `allowNavigation`~~ — KHÔNG còn áp dụng: vỏ Capacitor đã bị gỡ hẳn
   (2026-07-19). APK hiện là native Kotlin/Compose, không có WebView nên không có danh sách điều hướng.

**Việc còn lại của bạn:** build + phát hành. Dùng script phát hành (tự tăng versionCode, ký, chép ra
`artifacts/`):

```powershell
.\build-ketoanapk-release.ps1
```

Hoặc build tay bằng gradle (KHÔNG còn lệnh npm nào cho APK):

```powershell
$env:JAVA_HOME="C:\Program Files\Android\Android Studio\jbr"
cd Ketoanapk\android
.\gradlew.bat assembleRelease --no-problems-report
```
→ **đăng bản mới ở form APK trên WEB** để thiết bị tự cập nhật (xem memory apk-update-mechanism). Nhớ `versionCode` mới (17) phải > mã đang cài trên máy.

Thiết bị đã cài bản cũ vẫn trỏ `192.168.1.88` (chỉ chạy trong LAN) cho tới khi cập nhật.

---

## Lưu ý bảo mật khi mở ra Internet

- **Đăng nhập/JWT/HTTPS** đã có sẵn. Đổi `Bootstrap:AdminPassword` mặc định `admin123` nếu chưa đổi.
- **IP thật client**: qua tunnel, backend thấy request từ `127.0.0.1`; IP thật nằm ở header `CF-Connecting-IP`. Nếu tính năng geofence/nhật ký cần IP thật (chấm công ngoại tuyến, audit), cần đọc header này — hiện CHƯA wiring, cân nhắc bổ sung sau khi test.
- **Chặn quét/bot**: bật Cloudflare **WAF** + **Rate Limiting** (gói Free có mức cơ bản) cho `/api/auth/*`.
- **Giới hạn truy cập nội bộ**: nếu chỉ nhân viên dùng, cân nhắc **Cloudflare Access** (Zero Trust) đặt cổng đăng nhập trước toàn site.
- **Camera**: `getUserMedia` cần HTTPS — domain Cloudflare là HTTPS hợp lệ nên chấm công khuôn mặt trên web chạy được ngoài LAN.
