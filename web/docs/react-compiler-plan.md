# Kế hoạch: trả nợ luật React Compiler & bật trình biên dịch

Trạng thái đầu kỳ (2026-07-19): **39 cảnh báo** trong 16 tệp, `npm run lint` xanh vì ba luật
`react-hooks/set-state-in-effect`, `refs`, `purity` đang để mức `warn` (xem `eslint.config.js`).
Đợt trước đã dọn 12/51 (nhóm form/dialog).

Mục tiêu cuối: 0 cảnh báo → bật React Compiler cho toàn bộ mã → đưa ba luật về mức `error`.

> ## ✅ CẬP NHẬT (2026-07-19, đợt dọn thứ hai)
>
> **Đã làm:** nhóm A, B, C, D, E(phần purity), F — **39 → 20 cảnh báo**. Mỗi nhóm kiểm tay trên
> trình duyệt (instance DB nháp, xem "Đã kiểm chứng gì" cuối tài liệu). Cộng đợt 1 là **31/51 đã dọn**.
>
> **Còn lại 20 cảnh báo, cố ý để lại:** 19 trong `src/features/chamcong/**` (nhóm G — cần camera thật)
> và 1 ở `PhieuChi.tsx:106` (cần dựng thủ quỹ + quét QR bằng app).
>
> **React Compiler: ĐÃ THỬ, QUYẾT ĐỊNH CHƯA BẬT.** Hai phát hiện đổi cục diện so với kế hoạch gốc:
> 1. **Chỉ thị `"use memo"` mức MODULE không opt-in được** (đã kiểm bằng chính compiler + logger).
>    Phải gắn trong TỪNG hàm ⇒ ≈**486 chỗ / 110 tệp**. Bước 5 kiểu "gắn use memo cho tệp đã sạch"
>    như kế hoạch mô tả là **không khả thi** (diff khổng lồ, không có test để bảo chứng).
> 2. **Chế độ biên dịch-tất-cả** (bỏ qua bước 5, nhảy thẳng ý bước 7): 485/637 hàm biên dịch được,
>    nhưng **lý do bail số 1 là `try/finally` (51) + `try`-không-`catch` (8)** — nằm đúng trong các
>    handler lưu/xoá của những trang NẶNG (KhachHang, KeToan, PhieuChi…), nên chúng bị bỏ qua CẢ
>    component. **Đo đếm render thật:** KhachHang gõ 11 phím ⇒ StatCard render 44 lần, **BẬT hay TẮT
>    compiler đều 44** (không cải thiện vì trang bị bail). Đổi lại **gói to thêm 11,9% (gzip +53,5 kB)**.
>
>    → Theo nguyên tắc "cái nào cải thiện thì làm, không thì thôi": **chưa bật**. `vite.config.ts` đã
>    gỡ compiler, ba gói toolchain đã gỡ. Bản dọn A–F giữ nguyên (mã đã sẵn sàng cho compiler sau này).
>    Muốn bật thật sự có ích thì trước hết phải **refactor `try/finally` → `try/catch` / `.finally()`**
>    ở ~46 tệp (một đợt riêng, cần quyết định riêng vì đụng hành vi + không có test).

---

## 1. Nền tảng đã kiểm chứng

| Việc | Trạng thái |
|---|---|
| React | **19.2** — compiler chạy thẳng, không cần gói runtime phụ |
| `babel-plugin-react-compiler` | **1.0.0**, đã phát hành ổn định (không còn beta/rc) |
| `@vitejs/plugin-react` 6.x | có sẵn helper `reactCompilerPreset` |
| Lint | `eslint-plugin-react-hooks@7` đã gồm luật compiler — **không cần cài thêm** |
| **Kiểm thử frontend** | **KHÔNG CÓ** — không vitest/jest. Chỉ có `tsc` + `eslint` + kiểm tay trên trình duyệt |

Dòng cuối là ràng buộc lớn nhất của cả kế hoạch: **mọi thay đổi chỉ được bảo chứng bằng mắt người
trên trình duyệt**. Vì thế kế hoạch chia nhóm theo "kiểm chứng được dễ hay khó", không theo số dòng.

### Cách cài (theo README của chính plugin đang dùng)

```sh
npm install -D @rolldown/plugin-babel @babel/core babel-plugin-react-compiler @types/babel__core
```

```js
// vite.config.ts
import react, { reactCompilerPreset } from '@vitejs/plugin-react'
import babel from '@rolldown/plugin-babel'

plugins: [react(), tailwindcss(), babel({ presets: [reactCompilerPreset({ compilationMode: 'annotation' })] })]
```

**`compilationMode: 'annotation'` là chìa khoá của kế hoạch này**: ở chế độ đó compiler CHỈ biên dịch
component có chỉ thị `"use memo"` ở đầu hàm. Nhờ vậy bật trình biên dịch lên không đụng gì tới mã
hiện tại, rồi dọn sạch tệp nào thì opt-in tệp đó — mỗi bước đều đo được, lùi được.

---

## 2. Nguyên tắc

1. **Mỗi nhóm là một lần commit + một lần kiểm trên trình duyệt.** Không gộp nhiều nhóm rồi kiểm một thể.
2. **Dọn sạch tệp → gắn `"use memo"` → kiểm lại lần nữa.** Lần kiểm thứ hai quan trọng ngang lần đầu:
   compiler ghi nhớ (memo hoá) mạnh tay, lỗi ẩn về tính thuần khiết sẽ lộ ra đúng lúc này.
3. **Không sửa hành vi.** Nếu một chỗ buộc phải đổi hành vi mới hợp luật, dừng lại và hỏi — đừng tự quyết.
4. **Nhóm nào không kiểm chứng được thì để lại**, ghi rõ lý do, hơn là sửa mù rồi hy vọng.

---

## 3. Các nhóm việc, theo thứ tự nên làm

### Nhóm A — Chọn/đặt lại giá trị mặc định (7 cảnh báo · 6 tệp) · rủi ro THẤP

| Tệp | Dòng | Việc |
|---|---|---|
| `ConfirmDialog.tsx` | 37 | đặt lại cờ `busy` khi hộp thoại đóng |
| `TimesheetCalendar.tsx` | 132 | bỏ chọn ngày khi đổi tháng |
| `KhachHang.tsx` | 88 | chọn khách hàng đầu tiên khi danh sách đổi |
| `PhanHoi.tsx` | 122 | chọn cuộc hỗ trợ đầu tiên |
| `QuanLyBangCong.tsx` | 44, 159 | đặt lại ô tìm kiếm + chọn nhân viên đầu tiên |
| `CountUp.tsx` | 56 | nhánh "không phải số" gán thẳng giá trị |

Cùng đúng hai kỹ thuật đã dùng ở đợt form/dialog: suy ra lúc render, hoặc gán một lần có mốc chặn.
**Kiểm:** mở từng trang, đổi tháng/đổi danh sách, xem lựa chọn nhảy đúng. Nhanh và chắc.

### Nhóm B — Trang Hệ thống (3) · rủi ro THẤP–VỪA

`SystemSettings.tsx:102, 568, 719` — nạp tuỳ chọn nhắc nước/mắt từ localStorage, nạp thông báo hệ
thống, và tự đề xuất mã phiên bản APK kế tiếp.
**Kiểm:** mở `/caidat`, bật/tắt nhắc, sửa thông báo, mở form đăng bản APK xem số versionCode gợi ý.
Lưu ý: mục 719 dính luồng phát hành APK — kiểm kỹ, đừng để gợi ý sai số.

### Nhóm C — Nhắc nước / nhắc mắt (3) · rủi ro VỪA

`EyeReminderPopup.tsx:138, 191` + `WaterReminderPopup.tsx:137` — đọc trạng thái ngày từ localStorage
và vòng đếm ngược nghỉ mắt.
**Kiểm:** khó vì phải chờ tới giờ nhắc. Nên tạm rút ngắn chu kỳ trong lúc kiểm, hoặc gọi thẳng hàm
trong console. Ghi rõ đã kiểm bằng cách nào.

### Nhóm D — Cắt ảnh đại diện (3, luật `refs`) · rủi ro VỪA

`AvatarCropper.tsx:41, 157, 158` — đọc `coverScale.current` / `natural.current` **lúc render** để
tính khung ảnh. Đây là vi phạm THẬT chứ không phải luật soi nhầm: khi compiler ghi nhớ, các giá trị
này sẽ không tính lại đúng lúc. Cần chuyển sang state hoặc tính lại từ props/kích thước ảnh.
**Kiểm:** đổi ảnh đại diện, kéo–thu phóng, lưu, xem ảnh ra đúng khung.

### Nhóm E — Phiếu chi (2) · rủi ro VỪA (một nửa dễ, một nửa khó)

- `:155` luật `purity` — `Date.now()` trong một **hàm xử lý sự kiện**, luật tưởng nhầm là lúc render.
  Sửa 5 phút (tách mốc thời gian ra ngoài, hoặc `"use no memo"` cho riêng component kèm ghi chú).
- `:106` — đồng bộ mã QR đang mở theo dữ liệu realtime khi người nhận vừa ký. **Kiểm khó**: phải dựng
  thủ quỹ (role Kế toán + phòng ban `is_accounting`), lập phiếu, rồi quét QR ký nhận bằng ứng dụng.
  Cân nhắc để lại nhóm này tới sau.

### Nhóm F — Thông báo trò chuyện (2) · rủi ro VỪA–CAO

`ChatNotifications.tsx:94, 121` — dựng đăng ký realtime khi đăng nhập, và tải lại khi vào `/chats`.
Đây là hạ tầng thông báo dùng chung toàn hệ thống; hỏng thì mất chuông và huy hiệu chưa đọc.
**Kiểm:** cần **hai tài khoản** nhắn tin qua lại, xem toast + số chưa đọc + việc tự tải lại khi mở
trang chat. Không kiểm được kiểu này thì đừng đụng.

### Nhóm G — Camera & chấm công (19) · rủi ro CAO — **làm cuối**

| Tệp | Số | Ghi chú |
|---|---|---|
| `CheckInScanner.tsx` | 16 | **cả 16 từ MỘT nguyên nhân**: truyền `framingRef` vào `useBurstCheckIn`, luật "nhuộm" mọi giá trị hook trả về |
| `FaceTrackingOverlay.tsx` | 1 | dọn khung khi tắt camera |
| `EnrollWizard.tsx` | 1 | đặt lại trạng thái khi đổi người đăng ký |
| `AttendancePage.tsx` | 1 | đọc `kioskKey` từ URL rồi xoá khỏi thanh địa chỉ |

Nhóm này chiếm **gần một nửa số cảnh báo còn lại nhưng gần như toàn bộ rủi ro**. Sửa `CheckInScanner`
nghĩa là đổi cách dữ liệu căn khung mặt chảy vào vòng lặp quét bất đồng bộ.
**Kiểm:** phải có camera thật + mặt thật, thử đủ: quét thành công, sai tư thế, ảnh giả, mất mạng
(chấm công ngoại tuyến), và màn kiosk. Không có bàn kiểm đó thì **không nên làm**.

> Gợi ý thực tế: nếu chỉ muốn hưởng lợi ích của compiler mà không đụng camera, có thể dừng ở nhóm F
> và **loại trừ thư mục `src/features/chamcong/**` khỏi compiler** (dùng `rolldown.filter.id.exclude`
> như README hướng dẫn). Đổi lại: phần đó không được tối ưu, nhưng cũng không rủi ro.

---

## 4. Lộ trình

| Bước | Việc | Kết quả đo được | Trạng thái |
|---|---|---|---|
| 0 | Cài compiler ở chế độ `annotation` | Toolchain sẵn sàng | ✅ đã làm → **đã GỠ** (xem phát hiện) |
| 1 | Nhóm A | 39 → 32 | ✅ xong + kiểm trình duyệt |
| 2 | Nhóm B + C | 32 → 26 | ✅ xong + kiểm trình duyệt |
| 3 | Nhóm D + E (phần `purity`) | 26 → 22 | ✅ xong + kiểm trình duyệt |
| 4 | Nhóm F | 22 → 20 | ✅ xong + kiểm 2 tài khoản |
| 5 | **Chốt giữa kỳ**: gắn `"use memo"` cho mọi tệp đã sạch, đo lại | Có lợi ích thật, đo được | ⚠️ cơ chế "use memo" mức module KHÔNG chạy — đã đo bằng chế độ mặc định thay thế |
| 6 | Nhóm G (nếu có bàn kiểm camera) | 20 → 0 | ⛔ bỏ (không có camera) — đã loại trừ `chamcong` khi thử compiler |
| 7 | Đổi `compilationMode` sang mặc định (biên dịch tất cả) | Toàn bộ mã được tối ưu | ⛔ đã thử + đo, **quyết định chưa bật** (try/finally bail + gói +12%) |
| 8 | Ba luật trong `eslint.config.js` về `error` | Nợ đóng, không tái phát | ⛔ chưa (còn 20 cảnh báo + compiler chưa bật) — vẫn để `warn` |

Bước 5 là mốc quan trọng: đó là lúc công sức bắt đầu **đổi lấy được cái gì đó**. **Kết quả thực tế:**
cơ chế bước 5 (gắn `"use memo"` mức tệp) không tồn tại như kế hoạch tưởng; đo bằng chế độ mặc định
cho thấy lợi ích trên các trang nặng bằng 0 (bị bail do `try/finally`) trong khi gói to thêm 12% →
đã quyết định **chưa bật compiler**. Bản dọn nợ A–F là giá trị chắc chắn của đợt này (mã sẵn sàng
cho compiler khi nào xử lý xong `try/finally`).

**Nhóm G đã bỏ** (không có camera): khi thử compiler đã loại trừ `src/features/chamcong/**` bằng
`rolldown.filter.id.exclude` và xác nhận chạy đúng (chunk chamcong có 0 lời gọi memo-cache). Nay
compiler đã gỡ nên phần loại trừ đó cũng gỡ theo.

---

## Đã kiểm chứng gì (trung thực)

Dựng instance riêng trên DB nháp `ketoanmini_rc_test` (port 5399), tự động hoá bằng JS trong trình
duyệt. **Đã xoá DB nháp sau khi xong.**

| Nhóm | Kiểm bằng cách nào | Kết quả |
|---|---|---|
| A — ConfirmDialog | Mở/huỷ/mở-lại hộp thoại xoá khách; nút không kẹt "Đang xử lý" | ✅ |
| A — KhachHang | Tự chọn khách đầu; xoá khách đang chọn → nhảy sang khách kế | ✅ |
| A — TimesheetCalendar | Chọn ngày 15 → đổi tháng → ngày bị bỏ chọn (nhiều vòng) | ✅ |
| A — QuanLyBangCong | Tự chọn NV đầu; ô tìm điền sẵn tên; gõ tìm + chọn NV khác | ✅ |
| A — PhanHoi | Tab "Chat hỗ trợ" tự chọn cuộc đầu | ✅ |
| A — CountUp | Thẻ "Tăng ca —" (nhánh không-có-số) hiện đúng | ✅ (rAF bị tab ẩn tiết lưu, xác nhận qua logic) |
| B — SystemSettings | Nạp tuỳ chọn từ localStorage; lưu cấu hình + versionCode gợi ý; sau lưu KHÔNG đè giá trị vừa nhập | ✅ |
| C — Eye/WaterReminder | Lùi mốc để ép nhắc mắt tới hạn; đếm ngược 20s → hoàn thành (ghi vào localStorage) | ✅ (popup dùng framer-motion, đóng qua remount) |
| D — AvatarCropper | Tải ảnh 600×400 → khung cắt 450×300 căn giữa; zoom 2×; kéo bị kẹp mép; "Áp dụng" ra ảnh 256×256 đúng màu | ✅ (đây là vi phạm THẬT đã sửa) |
| E — PhieuChi (purity) | Trang mở không lỗi (admin bị chặn lập/duyệt phiếu theo thiết kế) | ✅ một phần — nhánh QR cần thủ quỹ |
| F — ChatNotifications | 2 tài khoản nhắn qua lại: toast hiện + huy hiệu chưa đọc tăng 1→5; tắt "đọc trước" → toast ẩn nội dung | ✅ |

**Chưa kiểm được:** `PhieuChi.tsx:106` (cần thủ quỹ + quét QR) và toàn bộ nhóm G `chamcong` (cần
camera thật) — đều để nguyên, không đụng.

---

## 5. Cách đo lợi ích (đừng bỏ qua)

Không đo thì không biết có đáng công không. Trước bước 5 và sau bước 5, đo cùng một kịch bản:

- **React DevTools Profiler**: số lần render khi gõ vào một form dài (vd sửa chứng từ nhiều dòng
  hàng, hoặc bảng lương). Đây là chỗ compiler ăn điểm rõ nhất.
- **Kích thước gói**: `npm run build` in ra kích thước — compiler làm mã **to hơn** một chút. Ghi lại
  con số để biết đánh đổi.
- Máy yếu: bật chế độ `perf-lite` rồi thử lại, vì đó mới là đối tượng hưởng lợi thật.

---

## 6. Rủi ro & cách chặn

| Rủi ro | Cách chặn |
|---|---|
| Không có kiểm thử tự động cho frontend | Mỗi nhóm một lần commit riêng để lùi được từng phần; kiểm tay theo danh sách trong mục 3 |
| Compiler ghi nhớ mạnh làm lộ lỗi ẩn | Bật theo chế độ `annotation`, opt-in từng tệp; gặp lạ thì `"use no memo"` cho đúng tệp đó rồi điều tra |
| Sửa nhầm mã camera/chấm công | Để nhóm G cuối cùng, và chỉ làm khi có camera thật để thử |
| Nợ mới sinh ra trong lúc đang trả nợ | Mã mới phải sạch ngay (đợt vừa rồi các tệp mới không góp cảnh báo nào) |

## 7. Cân nhắc: có nên thêm vitest không?

Kế hoạch này không cần vitest, nhưng **thiếu nó là lý do chính khiến nhóm F và G đắt đến vậy**. Nếu
định làm tới nhóm G, nên cân nhắc thêm vitest + Testing Library trước, và viết test cho đúng vài
hook lõi (`useBurstCheckIn`, `ChatNotifications`). Đó là việc riêng, cần quyết định riêng — không gói
vào đợt này.
