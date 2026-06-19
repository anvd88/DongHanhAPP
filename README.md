# KetoanMini

KetoanMini là ứng dụng desktop nội bộ cho Công ty TNHH Inox Cường Phát, viết bằng C# .NET 8, kết hợp WinForms và WPF. Ứng dụng dùng SQL Server qua LAN để nhiều máy cùng làm việc trên một cơ sở dữ liệu chung.

Phiên bản hiện tại: `1.2.2`.

## Tính Năng Chính

- Đăng nhập, đăng ký tài khoản, duyệt tài khoản, khóa/mở khóa tài khoản.
- Phân quyền `Admin` và `User`.
- Một tài khoản chỉ có một phiên đăng nhập đang hoạt động; phiên cũ sẽ bị đăng xuất khi đăng nhập nơi khác.
- Theo dõi trạng thái online và số phút hoạt động trong ngày.
- Quản lý tăng ca, chấm công tăng ca và duyệt yêu cầu tăng ca.
- Nhật ký thao tác cho các hành động quan trọng.
- Quản lý khách hàng, bí danh khách hàng và truy xuất tên khách hàng không dấu.
- Nhập chứng từ kế toán, thanh toán, bán hàng và xuất dữ liệu Excel.
- Màn hình Gia công bằng WPF, gồm danh sách phiếu, chi tiết phiếu, tạo/sửa/xóa phiếu, cập nhật trạng thái.
- Màn hình Nhân sự bằng WPF cho admin quản lý người dùng.
- Chat LAN và lưu lịch sử tin nhắn/file trong SQL Server.
- Quản lý cập nhật phiên bản: admin phát hành file setup, máy client tự báo có bản mới.
- GitHub Actions build Debug, publish exe self-contained và build installer `.exe`.

## Module Đang Có

- `Tổng quan`: dashboard số liệu tổng hợp.
- `Kế toán`: chứng từ, thanh toán, khách hàng và xuất Excel.
- `Bán hàng`: nhập đơn bán hàng cơ bản.
- `Gia công`: quản lý phiếu gia công, danh sách hàng hóa, trạng thái và giá trị.
- `Nhân sự`: quản lý tài khoản, vai trò, online/offline, khóa/mở khóa, duyệt tài khoản.
- `Báo cáo`: tổng hợp số liệu và các bảng tra soát.
- `Sao lưu`: xuất dữ liệu.
- `Cập nhật`: phát hành phiên bản mới cho người dùng.

Các mục `Hàng tồn kho`, `Mua hàng`, `Tài sản cố định`, `Danh mục`, `Cài đặt` hiện là khung placeholder để phát triển tiếp.

## Công Nghệ

- .NET 8
- WinForms
- WPF
- SQL Server / SQL Server Express
- Microsoft.Data.SqlClient
- Inno Setup cho installer
- GitHub Actions cho CI/CD

## Yêu Cầu

Máy người dùng:

- Windows 10/11.
- Kết nối LAN tới máy chủ SQL Server.
- Không cần cài .NET nếu dùng installer hoặc bản publish self-contained.

Máy phát triển:

- Windows.
- .NET 8 SDK.
- SQL Server hoặc SQL Server Express để chạy đầy đủ app.
- Git.

Máy chủ SQL:

- SQL Server Express hoặc SQL Server.
- TCP/IP enabled.
- Port cố định, khuyến nghị `1433`.
- Firewall mở inbound TCP `1433`.
- Login SQL dùng cho app, mặc định gợi ý là `ketoan_app`.

## Tài Khoản Mặc Định

Khi database mới được tạo, app tự tạo tài khoản admin:

```text
Username: admin
Password: admin
```

Nên đổi mật khẩu admin ngay sau lần đăng nhập đầu tiên.

## Cấu Hình SQL Server LAN

Trên máy chủ SQL, chạy file sau bằng quyền Administrator:

```text
src/KetoanMini/tools/RUN_AS_ADMIN_configure_sql_lan.bat
```

Script sẽ:

- Bật TCP/IP cho SQL Server instance.
- Đặt port SQL cố định.
- Mở firewall TCP.
- Tạo database `KetoanMini` nếu chưa có.
- Tạo login SQL cho app.
- Ghi connection string mẫu vào `config/database.json`.

Tài liệu chi tiết nằm tại:

```text
src/KetoanMini/HUONG_DAN_SQL_LAN.txt
```

## Cấu Hình Máy Client

Khi mở app trên máy client mà chưa kết nối được database, app sẽ hiện cửa sổ `Cấu hình kết nối SQL Server`.

Nhập ví dụ:

```text
Máy chủ / IP:   192.168.1.88,1433
Database:      KetoanMini
Tài khoản SQL: ketoan_app
Mật khẩu:      KetoanMini@2026
```

Bấm `Kiểm tra & lưu`. Nếu kết nối thành công, app lưu cấu hình vào:

```text
%AppData%\KetoanMini\config\database.json
```

Ví dụ đường dẫn thật:

```text
C:\Users\<User>\AppData\Roaming\KetoanMini\config\database.json
```

App ưu tiên đọc cấu hình theo thứ tự:

1. Biến môi trường `KETOANMINI_CONNECTION_STRING`.
2. `%AppData%\KetoanMini\config\database.json`.
3. `config/database.json` cạnh file chạy app.
4. `database.json` cạnh file chạy app.
5. Fallback local `localhost\SQLEXPRESS01`.

Không nên sửa trực tiếp file trong `C:\Program Files\KetoanMini` nếu không cần, vì thư mục này yêu cầu quyền admin.

## Kiểm Tra Kết Nối SQL

Trên máy client, chạy PowerShell:

```powershell
Test-NetConnection 192.168.1.88 -Port 1433
```

Kết quả cần có:

```text
TcpTestSucceeded : True
```

Nếu ping được nhưng lệnh trên `False`, app vẫn không kết nối được SQL Server. Khi đó kiểm tra TCP/IP SQL Server, firewall, IP máy chủ và port.

## Chạy Dev

Từ thư mục repo:

```powershell
cd src/KetoanMini
dotnet run
```

Hoặc build:

```powershell
cd src/KetoanMini
dotnet build
```

File Debug:

```text
src/KetoanMini/bin/Debug/net8.0-windows/KetoanMini.exe
```

## Publish Exe Self-Contained

```powershell
dotnet restore src/KetoanMini/KetoanMini.csproj -r win-x64 /p:PublishReadyToRun=true
dotnet publish src/KetoanMini/KetoanMini.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:PublishReadyToRun=true --no-restore -o publish/win-x64
```

File chạy:

```text
publish/win-x64/KetoanMini.exe
```

## Build Installer

Workflow GitHub Actions:

```text
.github/workflows/build-installer.yml
```

Khi chạy workflow này, GitHub Actions sẽ:

1. Restore project cho `win-x64`.
2. Publish app Release self-contained.
3. Publish `KetoanMiniUpdater.exe`.
4. Tạo gói cập nhật nhanh `.zip`.
5. Cài Inno Setup trên runner.
6. Build file installer:

```text
KetoanMiniSetup-<version>-win-x64.exe
KetoanMiniUpdate-<version>-win-x64.zip
```

File installer dùng cho cả:

- Cài mới.
- Cập nhật bản đã cài, vì `AppId` trong Inno Setup được giữ cố định.

Workflow cũng tạo bộ gỡ độc lập:

```text
KetoanMiniUninstall-<version>-win-x64.exe
```

Bộ cài vẫn tự tạo uninstaller chuẩn của Inno Setup trong Windows Apps/Programs và thêm shortcut `Go KetoanMini` ở Start Menu. Bộ gỡ độc lập chỉ gọi uninstaller của app đã cài; không xóa database SQL Server hay cấu hình người dùng trong `%AppData%`.

Gói `KetoanMiniUpdate-<version>-win-x64.zip` dùng cho cập nhật nhanh: app tải zip, đóng lại, `KetoanMiniUpdater.exe` tự backup, giải nén đè file mới, rollback nếu lỗi và mở lại app. Lần đầu chuyển sang kiểu cập nhật nhanh, hãy phát hành bộ setup mới một lần để máy client có `KetoanMiniUpdater.exe`; các lần sau có thể phát hành file zip.

Script Inno Setup:

```text
installer/KetoanMini.iss
installer/KetoanMiniUninstall.iss
```

## Phát Hành Cập Nhật Cho Người Dùng

1. Tăng version trong `src/KetoanMini/KetoanMini.csproj`.
2. Push code lên GitHub.
3. Chạy workflow `Build Installer (Setup EXE)`.
4. Tải artifact:
   - `KetoanMiniSetup-<version>-win-x64.exe` nếu cần cài mới, sửa updater, hoặc chuyển máy cũ sang cơ chế update nhanh.
   - `KetoanMiniUpdate-<version>-win-x64.zip` cho các lần cập nhật thường ngày.
5. Đăng nhập app bằng admin.
6. Vào tab `Cập nhật`.
7. Nhập version, ghi chú phát hành và chọn file zip update hoặc file setup.
8. Bấm phát hành.

Khi client mở app, nếu có bản mới:

- Nếu không bắt buộc, người dùng có thể cập nhật hoặc để sau.
- Nếu admin bật chặn bản cũ, người dùng phải cập nhật mới được đăng nhập.

Khuyến nghị vận hành:

- Dùng file setup cho lần cài đầu, khi sửa chính updater, hoặc khi cần đảm bảo mọi máy đều có đủ thành phần nền.
- Dùng file zip update cho các bản sửa app thông thường để cập nhật mượt hơn, không chạy lại wizard cài đặt.
- Không tự copy đè file bằng tay trên máy client; để `KetoanMiniUpdater.exe` tự backup, copy và rollback.

## GitHub Actions

- `.github/workflows/dotnet-desktop.yml`: build Debug khi push/PR vào `main`.
- `.github/workflows/publish-release.yml`: publish single-file `.exe`.
- `.github/workflows/build-installer.yml`: build installer `.exe`, bộ gỡ và gói update `.zip`.
- `.github/workflows/build-update-package.yml`: chỉ build gói update `.zip` cho các lần cập nhật thường ngày.

## Dữ Liệu Và Bảo Mật

- Dữ liệu chính nằm trong SQL Server database `KetoanMini`.
- Mật khẩu người dùng được hash bằng PBKDF2, không lưu mật khẩu thô.
- Chat sử dụng khóa bảo mật cục bộ trong `%LocalAppData%\KetoanMini\chat-keys`.
- File cấu hình thật `config/database.json` không nên commit lên GitHub.
- Installer không đóng gói `database.json` thật vào artifact release.

## Xuất Excel

App có thể xuất workbook `.xlsx` trực tiếp, không cần mở Excel để tạo file.

Chạy từ dòng lệnh:

```powershell
KetoanMini.exe --export-openxml C:\duongdan\Cong_no.xlsx
```

## Review Kỹ Thuật

Điểm tốt:

- App đã chuyển các màn nặng như Gia công/Nhân sự sang WPF, giảm giật và dễ mở rộng UI.
- SQL Server LAN phù hợp cho nhiều máy cùng dùng chung dữ liệu.
- Có cơ chế update nội bộ bằng installer, phù hợp triển khai trong công ty.
- Có phân quyền admin/user, audit log, session control và hash mật khẩu.
- Workflow CI/CD đã có đủ build, publish và installer.

Điểm cần lưu ý:

- Một số module trên sidebar vẫn là placeholder, cần tránh giới thiệu là tính năng hoàn chỉnh.
- `AccountingStore` đang là lớp rất lớn, chứa nhiều trách nhiệm: database, user, kế toán, chat, update. Về lâu dài nên tách repository/service theo module.
- App còn lai WinForms/WPF, nên khi thêm màn mới cần chọn nhất quán; màn dữ liệu nặng nên ưu tiên WPF.
- Cấu hình SQL Server là điểm dễ lỗi nhất khi triển khai client. Dialog cấu hình trong app đã giảm rủi ro, nhưng vẫn cần tài liệu rõ cho port/firewall/login.
- Cần có chiến lược backup SQL Server định kỳ trên máy chủ, vì dữ liệu không còn nằm local trong thư mục app.

## Troubleshooting

Lỗi `error: 26 - Error Locating Server/Instance Specified`:

- Kiểm tra IP/port trong connection string.
- Chạy `Test-NetConnection IP_MAY_CHU -Port 1433`.
- Bật TCP/IP trong SQL Server Configuration Manager.
- Mở firewall TCP 1433 trên máy chủ.
- Kiểm tra SQL Server service đang chạy.

Không sửa được file trong `Program Files`:

- Không cần sửa trực tiếp.
- Mở app và dùng cửa sổ cấu hình SQL.
- App sẽ lưu vào `%AppData%\KetoanMini\config\database.json`.

Build GitHub Actions lỗi thiếu `net8.0-windows/win-x64`:

- Restore phải có runtime:

```powershell
dotnet restore src/KetoanMini/KetoanMini.csproj -r win-x64 /p:PublishReadyToRun=true
```

Installer lỗi thiếu `Vietnamese.isl`:

- Installer hiện dùng `compiler:Default.isl`, không phụ thuộc file tiếng Việt của Inno Setup.
