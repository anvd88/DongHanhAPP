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

## Build Release (R8 + resource shrinking)

```powershell
cd C:\Users\Admin\Desktop\KetoanMiniDotNet_Code_20260615_155926\web\Ketoanapk\android

$env:JAVA_HOME = "C:\Program Files\Android\Android Studio\jbr"
$env:PATH = "$env:JAVA_HOME\bin;$env:PATH"

.\gradlew.bat testDebugUnitTest lintRelease bundleRelease assembleRelease
```

APK và Android App Bundle nằm tại:

```text
android\app\build\outputs\apk\release\app-release.apk
android\app\build\outputs\bundle\release\app-release.aab
```

## Cấu Hình API

API backend đang được nhúng trong [android/app/build.gradle](android/app/build.gradle) tại:

```gradle
buildConfigField "String", "API_BASE_URL", "\"https://app.ketoancp.click\""
```

Nếu IP backend đổi, sửa giá trị này rồi build lại APK.

## Tăng Version

Version thật của APK nằm trong [android/app/build.gradle](android/app/build.gradle):

```gradle
versionCode 48
versionName "1.2.42"
```

Mỗi bản phát hành mới phải tăng `versionCode` để Android cho phép cài đè và cơ chế tự cập nhật nhận ra bản mới.

## Khóa Ký Release

- File khóa: `android\app\ketoan-release.jks` (alias `ketoan`).
- Cấu hình mật khẩu: `android\keystore.properties`.
- Hai file này đang bị `.gitignore`; cần sao lưu riêng. Nếu mất khóa, Android sẽ không cho cài đè bản cập nhật đã ký bằng khóa khác.

## Kiểm thử và phát hành

- Unit test: `testDebugUnitTest`; UI/instrumentation: `connectedDebugAndroidTest` trên ít nhất hai thiết bị thật.
- `minSdkVersion 29` (Android 10); kiểm thử API 29, một bản trung gian và API 36, gồm màn hình nhỏ/lớn và cỡ chữ 130%.
- Release dùng R8, resource shrinking và AAB để Play tự tách ABI/density; không phân phối APK tách ABI thủ công.
- Ghi kết quả thiết bị, hiệu năng, ký, cập nhật và rollback theo [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md).
- Thay đổi của bản hiện tại nằm trong [RELEASE_NOTES.md](RELEASE_NOTES.md).

## Đổi Tên Hiển Thị

Tên app lấy từ [android/app/src/main/res/values/strings.xml](android/app/src/main/res/values/strings.xml):

```xml
<string name="app_name">Ketoan - Nhân sự</string>
<string name="title_activity_main">Ketoan - Nhân sự</string>
```
