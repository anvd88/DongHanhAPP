# Release notes 1.3.8 (84)

- Khóa an toàn toàn bộ xác thực khuôn mặt khi model chống giả mạo thiếu, lỗi hoặc trả kết quả không hợp lệ.
- Production chỉ khởi động khi nạp đủ hai model Silent-Face; không còn nhánh lỗi mặc định coi ảnh là người thật.
- Tự đăng ký khuôn mặt nay chỉ gửi vector AES-256-GCM vào vùng chờ, chưa dùng để chấm công ngay.
- HR phải chụp lại 2–3 ảnh trực tiếp khớp với vector chờ trước khi kích hoạt; không cho tự duyệt.
- Liveness và vector nhận diện luôn lấy từ cùng một khung ảnh, chặn ghép ảnh người thật với ảnh nạn nhân.
- Từ chối hoặc hết hạn sẽ xóa ngay vector sinh trắc tạm; ứng dụng hiển thị rõ trạng thái đang chờ duyệt.
- Bỏ MediaPipe/lưới mặt khỏi APK đăng ký; dùng ML Kit nhẹ để máy Android cũ ít RAM vẫn quét ổn định,
  còn PAD và nhận diện chính xác chạy bắt buộc trên máy chủ.

# Release notes 1.3.7 (83)

- Sửa crash trong luồng chấm công bằng cách ngừng khởi tạo/chạy MediaPipe Face Mesh trên camera chấm công.
- Làm lại màn quét với khung chữ nhật bốn góc cố định, không còn lưới hoặc khung bám theo khuôn mặt.
- Viết lại hướng dẫn theo ba bước: căn vị trí, điều chỉnh khoảng cách và xác thực khuôn mặt.
- Giảm tải xử lý ảnh trong lúc quét; vẫn giữ ML Kit chạy nền cho căn mặt, mở mắt, quay đầu và nụ cười.

# Release notes 1.2.42 (48)

- Hoàn thiện phê duyệt, nhật ký, trung tâm tác vụ và deep link thông báo.
- Bổ sung chat realtime/offline, danh bạ, gọi thoại/video và lịch sử cuộc gọi.
- Bổ sung lịch ca, chấm công nâng cao, QR, giải trình và vòng đời đơn từ.
- Bổ sung hồ sơ điện tử, onboarding, KPI, đào tạo, lương, hoàn ứng và phúc lợi.
- Bổ sung quản trị nhân sự, dashboard, khảo sát, trợ giúp và chẩn đoán kết nối.
- Bổ sung quản lý phiên, cảnh báo thiết bị mới, khóa dữ liệu nhạy cảm, cá nhân hóa và trợ năng.
- Release bật R8/resource shrinking; hỗ trợ AAB và kiểm thử các luồng trọng yếu.
