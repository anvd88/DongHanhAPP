# Ketoanapk

App nhân sự HR tách độc lập khỏi web chính. Thư mục này dùng để chỉnh giao diện HR, chạy qua LAN và build APK.

## Chạy dev qua LAN

```powershell
cd C:\Users\Admin\Desktop\KetoanMiniDotNet_Code_20260615_155926\web\Ketoanapk
npm.cmd install
npm.cmd run dev:lan
```

Mở trên máy đang chạy:

```text
http://127.0.0.1:5173/#/login
```

Mở từ máy/điện thoại cùng mạng LAN:

```text
http://<IP-may-tinh>:5173/#/login
```

Khi chạy dev, API mặc định đi qua proxy `/api` và `/hubs` trong `vite.config.ts` sang backend local `https://localhost:5443`, nên tránh lỗi chứng chỉ khi test bằng trình duyệt.

## Build web độc lập

```powershell
npm.cmd run build
```

Output nằm trong `dist`.

## Build APK

```powershell
npm.cmd run apk:debug
```

Output debug APK nằm trong:

```text
android\app\build\outputs\apk\debug\
```

Khi build APK, Vite dùng `.env.android`. Hiện file này đang trỏ API LAN:

```text
VITE_API_BASE_URL=https://192.168.1.88:5443
```

Nếu IP backend đổi, sửa dòng này trước khi build APK.
