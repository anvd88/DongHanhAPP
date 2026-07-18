# Checklist phát hành Android

## Trước khi ký

- [ ] Chốt API contract/migration với Nhóm 1 và chạy smoke test môi trường staging.
- [ ] `testDebugUnitTest`, `lintRelease`, `assembleDebugAndroidTest`, `bundleRelease` đều xanh.
- [ ] Không còn dữ liệu mẫu, secret, token hoặc log chứa PII trong source/artifact.
- [ ] Kiểm tra API/SignalR/FCM/TURN relay, offline/retry và notification deep link.
- [ ] Chạy Android API 29, bản trung gian và API 36; màn hình nhỏ/lớn; portrait/landscape; font 100%/130%.
- [ ] Chạy ít nhất hai thiết bị thật: Wi-Fi↔Wi-Fi, Wi-Fi↔4G, 4G↔4G, khóa màn hình, app nền/bị đóng, Bluetooth/tai nghe/loa, camera trước/sau.
- [ ] Lưu ảnh/video nghiệm thu và thông số thiết bị/build vào biên bản phát hành.

## Hiệu năng và ổn định

- [ ] Android Studio Profiler/Perfetto: không ANR/crash ở login, chấm công, đơn, chat, lương và cuộc gọi 15 phút.
- [ ] Ghi RAM đỉnh, CPU, nhiệt và mức hao pin cho chat/call; kiểm tra đổi mạng giữa cuộc gọi.
- [ ] Kiểm tra kích thước AAB/APK bằng APK Analyzer và native libraries theo ABI.
- [ ] Chạy accessibility scanner/TalkBack, tương phản và vùng chạm 48dp cho luồng chính.

## Ký và phát hành

- [ ] Tăng đồng bộ `versionCode`/`versionName`, release note và bản ghi release backend.
- [ ] Sao lưu keystore ngoài repo; đối chiếu SHA-256 certificate với bản đang phát hành.
- [ ] Ký AAB bằng upload key, kiểm tra `apksigner verify --verbose --print-certs` đối với APK nghiệm thu.
- [ ] Phát hành staged rollout; theo dõi crash/ANR, login, push, API error và support ticket.

## Cập nhật và rollback

- [ ] Thử cập nhật đè từ bản production trước, xác nhận token/cache/draft còn nguyên và migration DB thành công.
- [ ] Nếu lỗi: dừng rollout, tắt tính năng bằng remote config, khôi phục backend tương thích ngược và phát hành hotfix có `versionCode` lớn hơn.
- [ ] Không hạ `versionCode` và không đổi signing key; ghi sự cố, phạm vi ảnh hưởng và quyết định rollback.
