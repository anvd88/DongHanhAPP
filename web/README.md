# KetoanMini Web

Phiên bản web của ứng dụng kế toán **KetoanMini** (bản desktop là .NET 8 WPF), giao diện
**Liquid Glass**, backend dùng **PostgreSQL**.

> Nhánh: `web-version` · Độc lập với app WPF trong `src/KetoanMini`.

---

## 1. Công nghệ

| Tầng | Công nghệ | Ghi chú |
|------|-----------|---------|
| **Backend** | ASP.NET Core 8 Minimal API (C#) | REST + JWT. Kết nối PostgreSQL qua `Npgsql`. |
| **Frontend** | React 19 + TypeScript + Vite | SPA. |
| Giao diện | Tailwind CSS v4 + hệ "Liquid Glass" tự xây | Nền gradient động, thẻ kính mờ, **glow bám theo chuột** (tái hiện `LiquidGlassBorder` của bản desktop). |
| Xác thực mật khẩu | PBKDF2-SHA256 (port nguyên văn) | Xác thực được mọi mật khẩu đã lưu trong DB → **đăng nhập bằng tài khoản hiện có**. |

Bảng màu lấy đúng từ `WpfTheme.cs`: accent sáng `#2563EB`, tối `#11C5BF`. Có **light/dark**.

## 2. Cấu trúc

```
web/
├── backend/KetoanMini.Api/   ASP.NET Core API
│   ├── Endpoints/            Auth, Accounting (chứng từ/dashboard/báo cáo/nhật ký), GiaCong, Users, Releases
│   ├── Data/Database.cs      Helper ADO.NET
│   ├── Security/             PasswordHasher (PBKDF2) + TokenService (JWT)
│   ├── Models/Dtos.cs
│   └── appsettings.json      ← chuỗi kết nối PostgreSQL + khóa JWT
└── frontend/                 React + Vite
    └── src/
        ├── components/       Glass, Sidebar, Header, Layout, Table, Modal, StatCard, ui
        ├── pages/            Login, Dashboard, KeToan, GiaCong, NhanSu, BaoCao, SaoLuu, CapNhat, StubPage
        └── lib/              api, auth, theme, format, useApi, types
```

## 3. Chạy (dev)

Cần: .NET 8 SDK, Node 18+, PostgreSQL. Hai cửa sổ terminal:

Đảm bảo PostgreSQL đang chạy ở `localhost:5432`. Mặc định backend dùng `postgres/postgres`,
tự tạo database `ketoanmini`, schema và admin đầu tiên nếu user này có quyền tạo DB/schema.

```bash
# 1) Backend  → http://localhost:5239
cd web/backend/KetoanMini.Api
dotnet run

# 2) Frontend → http://localhost:5173  (proxy /api sang backend tự động)
cd web/frontend
npm install      # lần đầu
npm run dev
```

Mở http://localhost:5173 và đăng nhập bằng tài khoản trong PostgreSQL. DB mới sẽ seed admin mặc định `admin` / `admin123`.

## 3b. Chạy & public qua LAN (1 cổng duy nhất)

Frontend build được gộp thẳng vào `wwwroot` của backend → chỉ cần chạy backend, máy khác
trong mạng truy cập một địa chỉ duy nhất `http://<IP-máy-chủ>:5080`.

```bash
# 1) Build giao diện vào wwwroot của backend
cd web/frontend && npm run build

# 2) Chạy backend ở chế độ LAN (lắng nghe 0.0.0.0:5080)
cd web/backend/KetoanMini.Api && dotnet run --launch-profile lan
```

**Mở cổng firewall (chạy PowerShell với quyền Administrator — chỉ làm 1 lần):**
```powershell
New-NetFirewallRule -DisplayName "KetoanMini Web (5080)" -Direction Inbound `
  -Protocol TCP -LocalPort 5080 -Action Allow -Profile Private,Domain
```

Máy chủ hiện tại: IP LAN `192.168.1.88` → các máy khác mở **http://192.168.1.88:5080**.

> Tùy chọn nâng cao: chạy nền tự khởi động cùng Windows bằng **NSSM** / Windows Service /
> Task Scheduler (chưa cấu hình — báo nếu cần).

## 4. Chức năng đã hiện thực (chạy thật với DB)

- **Đăng nhập** (JWT, PBKDF2) · light/dark · responsive.
- **Tổng quan**: 4 thẻ KPI + chứng từ gần đây.
- **Kế toán**: danh sách + tạo/sửa/xóa chứng từ có dòng hàng, tự tính tổng; tìm kiếm.
- **Bán hàng**: lọc chứng từ bán hàng.
- **Gia công**: tab lọc (xuất/nhập/đang xử lý) + tìm kiếm, tạo/sửa/xóa phiếu + hàng hóa, thanh tiến độ.
- **Nhân sự** (admin): danh sách + lọc vai trò/trạng thái, thêm/duyệt/khóa/mở khóa/đặt lại mật khẩu/xóa mềm.
- **Báo cáo**: KPI + tổng hợp theo tháng.
- **Sao lưu**: nhật ký hoạt động (audit log).
- **Cập nhật** (admin): lịch sử phát hành.
- Các module **Hàng tồn kho, Mua hàng, Tài sản, Danh mục, Công nợ, Cài đặt, Lịch hẹn, Tích hợp**:
  hiển thị "Module đang phát triển" — **giống đúng trạng thái trên bản desktop**.

## 5. Chưa port (đặc thù LAN desktop)

- **Chat LAN + truyền file P2P** (UDP/TCP) và **xuất Excel trực tiếp**: cơ chế desktop/LAN, trên web
  cần thiết kế lại (chat qua WebSocket server, xuất Excel sinh phía server). Ghi nhận làm bước sau.

## 6. Bảo mật cần làm trước khi lên production

- `appsettings.json` đang chứa chuỗi kết nối PostgreSQL + khóa JWT mẫu → chuyển sang biến môi trường / user-secrets,
  **đổi khóa JWT** thành chuỗi ngẫu nhiên ≥ 32 ký tự.
- Bật HTTPS và giới hạn CORS theo domain thật.
