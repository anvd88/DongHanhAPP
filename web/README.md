# KetoanMini Web

Phiên bản web của ứng dụng kế toán **KetoanMini** (hiện tại là app desktop .NET 8 WPF).

> Nhánh: `web-version` · Thư mục này độc lập với app WPF trong `src/KetoanMini`.

---

## 1. Công nghệ đề xuất

Mục tiêu: **giao diện đẹp, mượt, hiện đại** — đồng thời **tận dụng tối đa code C# và CSDL SQL Server** đang có.

### Kiến trúc: tách Backend (C#) + Frontend (React)

| Tầng | Công nghệ | Lý do |
|------|-----------|-------|
| **Frontend** | **Next.js 15 (React 19) + TypeScript** | Hệ sinh thái web UI hiện đại nhất hiện nay; render nhanh, mượt. |
| Giao diện | **Tailwind CSS + shadcn/ui** | Bộ component cao cấp, đẹp sẵn, dễ tùy biến — chuẩn "đẹp mượt hiện đại". |
| Bảng dữ liệu | **TanStack Table** | Lưới dữ liệu mạnh cho kế toán (sort, filter, phân trang, cố định cột). |
| Biểu đồ Dashboard | **Recharts / Tremor** | Biểu đồ dashboard đẹp, tương tác mượt. |
| **Backend** | **ASP.NET Core Web API (C# .NET 9)** | **Tái sử dụng logic C# sẵn có** (AccountingStore, GiaCongStore, NhanSu…). |
| Truy cập DB | **Microsoft.Data.SqlClient** (đang dùng) | Giữ nguyên **SQL Server** và schema hiện tại — không phải migrate dữ liệu. |
| Xác thực | JWT + ASP.NET Identity | Thay cho LoginWindow desktop. |

### Vì sao chọn hướng này thay vì các lựa chọn khác?

- **So với Blazor** (cũng là C#): Blazor cho phép viết toàn bộ bằng C#, nhưng hệ component đẹp/mượt sẵn còn hạn chế hơn React + shadcn/ui. Với yêu cầu "đẹp, mượt, hiện đại" → **React thắng**.
- **So với viết lại toàn bộ bằng Next.js full-stack (TypeScript)**: phải viết lại toàn bộ nghiệp vụ kế toán đang có trong C# → tốn công, dễ sai sót. Giữ backend C# an toàn hơn.

> **Kết luận:** **Backend ASP.NET Core Web API (C#)** + **Frontend Next.js + Tailwind + shadcn/ui**.
> Tái dùng nghiệp vụ C# và SQL Server, đổi mới hoàn toàn giao diện.

---

## 2. Cấu trúc thư mục (dự kiến)

```
web/
├── README.md          ← file này
├── backend/           ← ASP.NET Core Web API (C#) — tái dùng logic từ src/KetoanMini
│   ├── KetoanMini.Api/        (controllers, endpoints)
│   ├── KetoanMini.Core/       (domain: tách logic kế toán dùng chung)
│   └── KetoanMini.Data/       (truy cập SQL Server)
└── frontend/          ← Next.js + TypeScript + Tailwind + shadcn/ui
    ├── app/                   (các trang: dashboard, nhân sự, gia công…)
    ├── components/            (UI components)
    └── lib/                   (gọi API)
```

## 3. Lộ trình triển khai (gợi ý)

1. **Tách nghiệp vụ dùng chung** từ `src/KetoanMini` ra `KetoanMini.Core` (không phụ thuộc WPF).
2. **Dựng Web API** expose các endpoint: đăng nhập, dashboard, nhân sự, gia công.
3. **Dựng frontend Next.js** + layout (sidebar giống app desktop) bằng shadcn/ui.
4. Làm lần lượt từng module: Dashboard → Nhân sự → Gia công → Chat.
5. Triển khai (IIS / Docker / cloud) + HTTPS.

---

*Chưa có code — đây là khung và kế hoạch. Xác nhận công nghệ trước khi mình scaffold backend + frontend.*
