# Cutover backend qua Rust gateway

Trạng thái hiện tại là **Rust-first hybrid**: 68/396 operation chạy native Rust; 328 operation còn lại,
SPA và các API chưa port được stream về .NET qua HTTP loopback. Vì .NET còn giữ migration, worker, FCM, face,
Excel COM và máy in, không được tắt tiến trình .NET sau khi đổi origin.

## 1. Khởi động và kiểm tra

Từ thư mục gốc dự án:

```bat
set KETOANMINI_USE_RUST_GATEWAY=1 && restart.bat
```

`restart.bat` sẽ publish .NET, đóng gói Rust, chạy Rust ở `127.0.0.1:5240`, chờ health và chạy
`scripts/verify-cutover-windows.ps1`. Nếu Rust không qua kiểm tra, script dừng Rust nhưng giữ .NET ở
`:5239`/`:5443` để có đường rollback.

Có thể xác minh lại độc lập:

```powershell
cd backend\KetoanMini.Rust
.\scripts\verify-cutover-windows.ps1
```

## 2. Đổi public origin

Chỉ sau khi dòng `CUTOVER CHECK OK` xuất hiện, đổi Cloudflare Tunnel public hostname từ:

```text
http://localhost:5239
```

sang:

```text
http://localhost:5240
```

Đây là thay đổi duy nhất đưa traffic Internet qua Rust. `restart.bat` cố ý không tự sửa Cloudflare
Dashboard. Cổng LAN `https://192.168.1.88:5443` vẫn đi thẳng .NET để giữ luồng chấm công/ảnh nội bộ.

## 3. Rollback

Nếu có lỗi sau cutover:

1. Đổi Cloudflare origin về `http://localhost:5239`.
2. Xác minh `https://app.ketoancp.click/api/info` trả `status: ok`.
3. Dừng `ketoanmini-server.exe` hoặc chạy lại `restart.bat` mà không đặt
   `KETOANMINI_USE_RUST_GATEWAY=1`.

Rollback không yêu cầu khôi phục CSDL: Rust không chạy migration/DDL, và các route đã port dùng chung
PostgreSQL cùng contract với .NET.
