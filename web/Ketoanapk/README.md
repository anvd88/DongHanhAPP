# KetoanAPK Native

Ứng dụng Android native Kotlin/Compose cho nhân sự, chấm công và các luồng tài khoản. Phần React/Vite cũ đã được gỡ khỏi thư mục này; mã ứng dụng nằm trong `android`.

## Build Debug

```powershell
cd C:\Users\Admin\Desktop\KetoanMiniDotNet_Code_20260615_155926\web\Ketoanapk\android

$env:JAVA_HOME = "C:\Program Files\Android\Android Studio\jbr"
$env:PATH = "$env:JAVA_HOME\bin;$env:PATH"

.\gradlew.bat assembleDebug
```

APK debug nằm tại:

```text
android\app\build\outputs\apk\debug\app-debug.apk
```

## Build Release

```powershell
cd C:\Users\Admin\Desktop\KetoanMiniDotNet_Code_20260615_155926\web\Ketoanapk\android

$env:JAVA_HOME = "C:\Program Files\Android\Android Studio\jbr"
$env:PATH = "$env:JAVA_HOME\bin;$env:PATH"

.\gradlew.bat assembleRelease
```

APK release nằm tại:

```text
android\app\build\outputs\apk\release\app-release.apk
```

## Cấu Hình API

API backend đang được nhúng trong [android/app/build.gradle](android/app/build.gradle) tại:

```gradle
buildConfigField "String", "API_BASE_URL", "\"https://192.168.1.88:5443\""
```

Nếu IP backend đổi, sửa giá trị này rồi build lại APK.

## Tăng Version

Version thật của APK nằm trong [android/app/build.gradle](android/app/build.gradle):

```gradle
versionCode 13
versionName "1.2.7"
```

Mỗi bản phát hành mới phải tăng `versionCode` để Android cho phép cài đè và cơ chế tự cập nhật nhận ra bản mới.

## Khóa Ký Release

- File khóa: `android\app\ketoan-release.jks` (alias `ketoan`).
- Cấu hình mật khẩu: `android\keystore.properties`.
- Hai file này đang bị `.gitignore`; cần sao lưu riêng. Nếu mất khóa, Android sẽ không cho cài đè bản cập nhật đã ký bằng khóa khác.

## Đổi Tên Hiển Thị

Tên app lấy từ [android/app/src/main/res/values/strings.xml](android/app/src/main/res/values/strings.xml):

```xml
<string name="app_name">Ketoan - Nhân sự</string>
<string name="title_activity_main">Ketoan - Nhân sự</string>
```
