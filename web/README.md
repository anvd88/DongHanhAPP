# KetoanMini Web

KetoanMini Web là phiên bản chạy trên trình duyệt của phần mềm kế toán KetoanMini, giúp quản lý công việc kế toán, khách hàng, gia công, nhân sự và chấm công trong một giao diện gọn gàng, dễ dùng.

Ứng dụng được xây dựng với backend ASP.NET Core và frontend React, có thể chạy nội bộ trong công ty qua mạng LAN.

## Tính năng chính

- Đăng nhập tài khoản, phân quyền người dùng và khu vực quản trị.
- Màn hình tổng quan hiển thị nhanh số liệu, chứng từ và tình hình hoạt động.
- Quản lý kế toán: tạo, sửa, xóa, tìm kiếm chứng từ và các dòng hàng liên quan.
- Quản lý khách hàng, thông tin liên hệ và dữ liệu phục vụ bán hàng.
- Quản lý gia công: theo dõi phiếu xuất, phiếu nhập, tiến độ và giá trị thực hiện.
- Báo cáo tổng hợp giúp xem nhanh kết quả theo kỳ và theo tháng.
- Quản lý nhân sự dành cho admin: thêm người dùng, khóa/mở khóa, duyệt tài khoản, đặt lại mật khẩu.
- Chấm công bằng camera/khuôn mặt, có màn hình quét riêng và trang quản lý chấm công.
- Chat nội bộ để trao đổi nhanh trong hệ thống.
- Công cụ tính toán, sao lưu/nhật ký hoạt động và quản lý lịch sử cập nhật.
- Nhắc uống nước và nghỉ mắt khi người dùng làm việc lâu trên hệ thống.
- Giao diện hỗ trợ sáng/tối, thiết kế hiện đại và responsive cho nhiều kích thước màn hình.

## Các module đang có

- Tổng quan
- Kế toán
- Khách hàng
- Gia công
- Báo cáo
- Chấm công
- Quản lý chấm công
- Chat
- Nhân sự
- Công cụ
- Sao lưu
- Cài đặt hệ thống
- Cập nhật

Một số module như hàng tồn kho, mua hàng, tài sản, danh mục, công nợ, ngân hàng, chi phí, lịch hẹn và tích hợp đang được chuẩn bị để phát triển tiếp.

## Công nghệ sử dụng

- Backend: ASP.NET Core 8
- Frontend: React, TypeScript, Vite
- Cơ sở dữ liệu: PostgreSQL
- Xác thực: JWT
- Giao diện: Tailwind CSS, hỗ trợ light/dark mode

## Chạy dự án

Backend:

```bash
cd backend/KetoanMini.Api
dotnet run
```

Frontend:

```bash
cd frontend
npm install
npm run dev
```

Sau khi chạy, mở trình duyệt tại địa chỉ frontend do Vite hiển thị, thường là:

```text
http://localhost:5173
```

## Mục tiêu

KetoanMini Web hướng tới việc đưa các nghiệp vụ quản lý nội bộ lên nền tảng web, giúp nhiều người cùng truy cập, thao tác nhanh hơn và dễ triển khai trong môi trường doanh nghiệp nhỏ.
