# TURN server (coturn) cho gọi thoại/video KetoanAPK

Nhân viên gọi nhau **khác mạng** (4G ↔ WiFi, qua Internet) thì WebRTC chỉ có STUN **không xuyên
được NAT đối xứng** → cần một TURN server chuyển tiếp. Media vẫn **mã hóa đầu-cuối DTLS-SRTP**,
server chỉ thấy gói mã hóa, không nghe/xem được cuộc gọi.

> ⚠️ **Không dùng Cloudflare Tunnel cho TURN.** Tunnel chỉ proxy HTTP(S). TURN cần một máy có
> **IP công cộng thật** và mở được cổng UDP. Dùng một VPS rẻ (Oracle free tier / bất kỳ VPS nào),
> hoặc máy chủ công ty có IP tĩnh + forward cổng trên router.

## 1. Yêu cầu hạ tầng
- 1 máy Linux (Ubuntu/Debian) có **IP công cộng**.
- Mở tường lửa (cả VPS firewall lẫn router nếu sau NAT):
  - `3478/udp` và `3478/tcp` — cổng TURN chính
  - `49160-49200/udp` — dải relay (khớp `min-port`/`max-port` trong conf)
  - `5349/tcp` — chỉ khi bật TLS
- 1 subdomain trỏ tới IP đó, ví dụ `turn.ketoancp.click` → A record IP công cộng.
  **Bản ghi này để DNS-only (đám mây xám, KHÔNG proxy) trên Cloudflare.**

## 2. Sửa `turnserver.conf`
> Bản này dùng **TURN REST (shared secret)** — an toàn nhất: KHÔNG có mật khẩu tĩnh trong APK,
> backend cấp credential có hạn giờ. App không cần điền `TURN_*` vào build.gradle nữa.

Mở [turnserver.conf](turnserver.conf) và thay 2 chỗ:
- `external-ip=THAY_IP_CONG_CONG_CUA_BAN`
  - VPS IP trực tiếp: `external-ip=203.0.113.10`
  - Sau NAT (máy công ty): `external-ip=203.0.113.10/192.168.1.50` (công cộng/nội bộ)
- `static-auth-secret=...` → tạo secret mạnh và **điền CHÍNH secret đó vào backend** (bước 5):
  ```bash
  openssl rand -hex 32
  ```

## 3. Chạy coturn

### Cách A — cài trực tiếp (Ubuntu/Debian)
```bash
sudo apt update && sudo apt install -y coturn
sudo mkdir -p /var/log/turnserver
# chép file cấu hình đã sửa vào:
sudo cp turnserver.conf /etc/turnserver.conf
# bật daemon:
sudo sed -i 's/#TURNSERVER_ENABLED=1/TURNSERVER_ENABLED=1/' /etc/default/coturn
sudo systemctl enable coturn
sudo systemctl restart coturn
sudo systemctl status coturn --no-pager
```

### Cách B — Docker
```bash
# (đã sửa turnserver.conf ở bước 2, đặt cùng thư mục này)
docker compose up -d
docker logs -f coturn
```

## 4. (Tùy chọn) Bật TLS — nên làm nếu mạng khách chặn cổng lạ
```bash
sudo apt install -y certbot
sudo certbot certonly --standalone -d turn.ketoancp.click
```
Rồi bỏ chú thích 3 dòng `tls-listening-port`, `cert`, `pkey` trong `turnserver.conf` và restart.
Thêm url TLS vào `TURN_URL` (xem bước 5): `turns:turn.ketoancp.click:5349?transport=tcp`.

## 5. Cấu hình backend (KHÔNG cần đụng build.gradle)
Mở `backend/KetoanMini.Api/appsettings.Local.json`, điền `Turn.Secret` = **đúng secret** ở bước 2:
```json
"Turn": {
  "Secret": "<secret openssl rand -hex 32 y hệt static-auth-secret>",
  "Urls": "turn:turn.ketoancp.click:3478?transport=udp,turn:turn.ketoancp.click:3478?transport=tcp",
  "TtlSeconds": 3600
}
```
Khởi động lại backend. App sẽ tự gọi `GET /api/chat/call/turn` (đã đăng nhập) để lấy credential có
hạn giờ trước mỗi cuộc gọi — **không cần build lại APK vì lý do TURN** (chỉ cần build lại để nhận các
sửa lỗi chuông/kết nối, versionCode đã là 39). Để `TURN_*` trong build.gradle TRỐNG (không dùng nữa).

## 6. Kiểm tra TURN có chạy không
Trước tiên lấy 1 credential tạm để test (chạy trên máy có backend, hoặc tính tay bằng openssl):
```bash
SECRET="<secret của bạn>"
USER="$(( $(date +%s) + 3600 )):test"
PASS="$(echo -n "$USER" | openssl dgst -binary -sha1 -hmac "$SECRET" | base64)"
echo "username=$USER"; echo "credential=$PASS"
```
Mở trang test WebRTC Trickle-ICE:
https://webrtc.github.io/samples/src/content/peerconnection/trickle-ice/
- STUN or TURN URI: `turn:turn.ketoancp.click:3478?transport=udp`
- Username / Password: dán 2 giá trị vừa in ra
- Bấm **Add Server** → **Gather candidates**. Thấy dòng loại **`relay`** = TURN OK.
  Không thấy `relay` = sai secret/cổng/`external-ip` hoặc tường lửa chặn.

## Ghi chú bảo mật (đây là phương án an toàn nhất)
- **Không có mật khẩu tĩnh trong APK.** Backend ký credential HMAC-SHA1 hết hạn sau `TtlSeconds`
  (mặc định 1 giờ). Lộ ra ngoài cũng vô dụng sau khi hết hạn, và chỉ người ĐÃ ĐĂNG NHẬP mới xin được.
- Đổi `TtlSeconds` xuống ~600 (10 phút) nếu muốn credential sống ngắn hơn nữa.
- `denied-peer-ip` trong conf đã chặn TURN chuyển tiếp vào dải mạng nội bộ (chống lạm dụng quét LAN).
- Bật TLS (bước 4) để mạng khách khó chặn và khó chặn/nghe lén tầng signaling của TURN.
