# Đối chiếu nhiệm vụ Nhóm 2

Ngày rà soát: 2026-07-15. Phạm vi: Android `Ketoanapk/android` và API `backend/KetoanMini.Api`.

## Kết quả theo 24 nhiệm vụ

| # | Trạng thái mã nguồn | Bằng chứng chính |
|---|---|---|
| 1 | Đã triển khai | Chi tiết đơn có duyệt/từ chối, nhận xét, xác nhận, busy/error/success, realtime/deep link; `RequestApprovalUiTest`. |
| 2 | Đã triển khai | `AuditScreen`, `AuditEndpoints`: dữ liệu thật, quyền, tìm/lọc, phân trang, chi tiết và retry. |
| 3 | Đã triển khai | `TaskCenterScreen`: nhóm hạn, badge, điều hướng/thao tác nhanh và cập nhật realtime. |
| 4 | Đã triển khai | Onboarding quyền và `PermissionCenterScreen`; camera/micro/vị trí chỉ hỏi tại luồng dùng. |
| 5 | Đã triển khai | `RealChatScreen`, API chat, cache mã hóa, phân trang, file/voice, trạng thái/retry/edit/delete/reaction/pin/forward, SignalR/FCM/deep link. |
| 6 | Đã triển khai | `DirectoryScreen`: tìm kiếm, hồ sơ, sơ đồ quan hệ, chat/call/video/email và che liên hệ theo API. |
| 7 | Đã triển khai mã nguồn | WebRTC/TURN, đổi mạng/reconnect, incoming notification, lifecycle, thiết bị âm thanh/camera, chất lượng và lịch sử. Ma trận mạng/phần cứng vẫn cần thiết bị thật. |
| 8 | Đã triển khai | Tuần/tháng, màu loại lịch, chi tiết ngày, đổi/nhận ca, kiểm tra xung đột và nhắc ca cấu hình được. |
| 9 | Đã triển khai | Geofence/khoảng cách/độ chính xác, hàng đợi bền vững, lịch sử trạng thái, giải trình ảnh, công trình và QR dự phòng. |
| 10 | Đã triển khai | Nháp mã hóa, tệp/ký, sao chép/sửa/thu hồi, timeline/hạn, nhắc và ủy quyền. |
| 11 | Đã triển khai | `ElectronicProfileScreen`: thông tin/tài liệu, chụp/tải, duyệt HR, hết hạn, masking và device credential. |
| 12 | Đã triển khai | Checklist, tiến độ, xác nhận nội quy, action link, mentor và hạn/quá hạn. |
| 13 | Đã triển khai | Mục tiêu/KPI, cập nhật, tự đánh giá, nhận xét/kỳ trước, hạn đóng và progress chart. |
| 14 | Đã triển khai | Khóa học thật, tài liệu/video, resume, quiz server-side, tiến độ/điểm/chứng nhận/hết hạn; API không còn lộ đáp án đúng. |
| 15 | Đã triển khai | Chi tiết cộng/trừ, so sánh, xác nhận/thắc mắc, PDF, device credential và `FLAG_SECURE`. |
| 16 | Đã triển khai | Loại đơn tạm ứng/hoàn ứng, nhiều hóa đơn/tệp, nháp, số liệu quyết toán, timeline và lý do. |
| 17 | Đã triển khai | Phép/lịch sử, bảo hiểm/khám/phụ cấp, sinh nhật/thâm niên và khen thưởng/điểm. |
| 18 | Đã triển khai | Tìm/lọc/tải thêm, chi tiết, phòng ban/quản lý/chức vụ/trạng thái, lương, xác nhận và audit. |
| 19 | Đã triển khai | KPI, lọc ngày/đơn vị, xu hướng 7 ngày, biểu đồ phòng ban, drill-down và cảnh báo. |
| 20 | Đã triển khai | Khảo sát mở/bình chọn, góp ý ẩn danh, danh sách trạng thái và cập nhật realtime/push. |
| 21 | Đã triển khai | Cảnh báo thiết bị mới, danh sách phiên/last-seen, thu hồi một/tất cả phiên và khóa dữ liệu nhạy cảm. |
| 22 | Đã triển khai | FAQ, API/SignalR/FCM/camera/micro/TURN diagnostics, ticket có version/device/mã/trạng thái; không gửi raw token/log. |
| 23 | Chưa hoàn tất | Đã có sáng/tối, cỡ chữ, đổi thứ tự thẻ, chọn locale và data saver. Tuy nhiên hiện chỉ có resource `values` với vài chuỗi hệ thống; phần lớn giao diện còn viết trực tiếp bằng tiếng Việt, chưa có bản dịch English đầy đủ và chưa nghiệm thu TalkBack trên thiết bị. |
| 24 | Chưa hoàn tất nghiệm thu | Có 16 unit test, 9 instrumentation test trong mã nguồn, minSdk 29, R8/shrink, AAB, release note/README và checklist. Test chưa phủ riêng từng tính năng và instrumentation chưa được chạy trên ít nhất hai thiết bị thật. |

## Kiểm tra tự động tại máy phát triển

- `testDebugUnitTest`: 16/16 unit test đạt.
- `assembleDebugAndroidTest`: biên dịch thành công APK UI-test cho duyệt đơn, đăng nhập, chấm công, đơn từ, chat, lương và cache chat.
- `lintRelease`: thành công, không có lỗi `Error` hoặc `Fatal` (còn cảnh báo nâng cấp dependency/API và tài nguyên không dùng, không chặn phát hành).
- `bundleRelease`: thành công với R8 và resource shrinking; AAB khoảng 45 MB.
- Frontend production build thành công.
- Backend Release build với warning-as-error thành công; 33/33 integration test đạt trên PostgreSQL test riêng.
- CI `android-quality.yml` chạy unit test, biên dịch device test, lint, bundle và lưu test/lint/APK/AAB làm artifact.
- Token phiên chỉ được lưu sau khi mã hóa bằng Android Keystore; không còn fallback ghi JWT dạng thô. Release thiếu `google-services.json` bị chặn, ngoại trừ artifact kiểm tra CI được ghi rõ không dùng triển khai.
- Không có thiết bị Android kết nối trong lần rà soát này, nên không ghi nhận giả kết quả `connectedDebugAndroidTest` hoặc thiết bị thật.

## Nghiệm thu còn phải thực hiện ngoài mã nguồn

- Chạy `connectedDebugAndroidTest` và luồng tay trên ít nhất hai điện thoại thật theo `RELEASE_CHECKLIST.md`.
- Xác minh TURN có `relay candidate`; Wi-Fi↔Wi-Fi, Wi-Fi↔4G, 4G↔4G; Bluetooth/tai nghe/loa; camera trước/sau; app nền/đóng/khóa màn hình.
- Đo crash/ANR/RAM/CPU/nhiệt/pin và dung lượng bằng Profiler/Perfetto/APK Analyzer.
- Rà TalkBack, tương phản, vùng chạm, cỡ chữ 130% và toàn bộ bản dịch English trên các kích thước màn hình.
- Chạy smoke test contract OpenAPI cùng Nhóm 1 trên staging (backend tự động hiện đã đạt 33/33 test).
- Lưu ảnh/video và biên bản nghiệm thu thực tế trước staged rollout.

Không được đánh dấu bản phát hành production hoàn tất cho đến khi toàn bộ mục nghiệm thu ngoài mã nguồn ở trên có bằng chứng.
