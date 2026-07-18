# Cloudflare TURN cho gọi thoại/video KetoanAPK

Dùng dịch vụ TURN sẵn có của Cloudflare (miễn phí ~1TB/tháng) để gọi được khi 2 máy KHÁC MẠNG qua
Internet. Không cần dựng server, không mở cổng, chạy được kể cả khi máy chủ sau CGNAT.

Backend `GET /api/chat/call/turn` tự gọi API Cloudflare cấp credential CÓ HẠN GIỜ cho từng người dùng
đã đăng nhập (không nhúng bí mật vào APK). App không cần build lại vì TURN.

## 1. Tạo TURN App trên Cloudflare (lấy KeyId + API Token)

1. Đăng nhập https://dash.cloudflare.com → chọn **tài khoản** của bạn (không phải một domain cụ thể).
2. Menu trái: mở **Realtime** (một số tài khoản còn tên **Calls**).
3. Chọn tab **TURN** → bấm **Create** (tạo TURN key/app). Đặt tên, ví dụ `ketoan-turn`.
4. Sau khi tạo, Cloudflare hiện:
   - **Turn Token ID** (chuỗi hex)  → đây là **KeyId**
   - **API Token** (bí mật, CHỈ hiện 1 lần) → **ApiToken** — copy ngay và cất kỹ.

## 2. Điền vào backend

Mở `backend/KetoanMini.Api/appsettings.Local.json`:
```json
"Turn": {
  "TtlSeconds": 3600,
  "Cloudflare": {
    "KeyId":   "<Turn Token ID>",
    "ApiToken":"<API Token>"
  },
  "Secret": "",
  "Urls": ""
}
```
Khởi động lại backend. Xong — app sẽ tự lấy credential Cloudflare trước mỗi cuộc gọi.

## 3. Kiểm tra hoạt động

Lấy 1 token đăng nhập bất kỳ (từ app/web đang đăng nhập, hoặc gọi /api/auth/login), rồi:
```bash
curl -H "Authorization: Bearer <token>" https://app.ketoancp.click/api/chat/call/turn
```
Kết quả ĐÚNG sẽ dạng:
```json
{ "urls": ["stun:stun.cloudflare.com:3478","turn:turn.cloudflare.com:3478?transport=udp", ...],
  "username": "g0f8...", "credential": "xxxx", "ttl": 3600 }
```
- Có `turn:turn.cloudflare.com...` + `username`/`credential` → OK, gọi khác mạng sẽ chạy.
- Trả `{"urls":[], ...}` rỗng → KeyId/ApiToken sai hoặc chưa restart backend.

## Ghi chú
- Bảo mật: credential hết hạn sau `TtlSeconds` (1 giờ), chỉ người đã đăng nhập mới xin được. Không có
  bí mật nào nằm trong APK.
- Chi phí: Cloudflare tính theo dữ liệu media trung chuyển, ~1TB/tháng đầu miễn phí (xem trang giá
  Cloudflare Realtime để biết mức hiện hành). Cuộc gọi cùng mạng/xuyên NAT thẳng được thì KHÔNG tốn.
- Muốn ép MỌI cuộc gọi đi qua TURN (ổn định tối đa + giấu IP) thì bật remote config `call.forceRelay=true`
  (xem app_config) — đánh đổi là tốn băng thông TURN nhiều hơn.
- Tự dựng coturn (thay Cloudflare) vẫn hỗ trợ: xem deploy/coturn/ + điền Turn:Secret/Urls thay cho Cloudflare.
