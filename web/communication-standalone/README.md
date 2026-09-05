# Communication standalone

Thư mục này chứa mã nguồn đã tách khỏi hệ thống KetoanMini hiện tại cho các chức năng:

- chat và đính kèm/voice message;
- voice call và video call;
- WebRTC signaling, TURN và P2P file transfer;
- UI, Android client và các test chuyên biệt của communication.

Không có project/package hiện tại nào tham chiếu thư mục này. Backend, web và Android chính không
đăng ký route, navigation, SignalR hub hoặc WebRTC dependency của communication nữa.

## Cấu trúc

- `backend/`: endpoint, SignalR hub, attachment policy, cleanup worker và test .NET đã tách.
- `web/`: trang chat/call, communication transport và UI notification/file-transfer.
- `android/`: signaling, WebRTC/call/chat data/UI cùng test Android.
- `deploy/`: cấu hình TURN/coturn và hướng dẫn cấp credential đã tách khỏi deploy chính.
- `backend/schema.communication.sql`: DDL của các bảng `web_chat_*`, `web_call_*` và cột cấu hình gọi.
- `backend/openapi.communication.json`: các route/schema OpenAPI communication đã tách khỏi baseline chính.
- `integration-snapshots/`: bản lưu các file dùng chung trước khi gỡ đoạn tích hợp, dùng làm tài liệu
  khi xây host communication độc lập; các snapshot này không thuộc source set/build hiện tại.

## Trạng thái

Đây là source extraction, chưa phải deployment độc lập hoàn chỉnh. Để chạy riêng cần tạo host và
contract xác thực riêng, cấu hình database/FCM/TURN, chuyển schema `web_chat_*`, rồi khai báo URL
communication cho các client muốn cài module này. Không được đưa thư mục này trở lại build chính
bằng wildcard include.
