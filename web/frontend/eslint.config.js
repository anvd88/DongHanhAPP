import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  // Chỉ lint MÃ NGUỒN. Trước đây chỉ bỏ qua 'dist' nên eslint quét luôn bundle đã build và thư viện
  // bên thứ ba trong public/ — sinh ra một loạt lỗi vô nghĩa (kể cả "rule không tồn tại" do file .js
  // đã build mang sẵn chỉ thị eslint-disable trỏ tới rule mà cấu hình này không nạp cho .js).
  globalIgnores(['dist', 'public/mediapipe']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      globals: globals.browser,
    },
  },
  {
    // ── Ba luật "sẵn sàng cho React Compiler" — ĐỂ MỨC CẢNH BÁO, CÓ CHỦ Ý ────────────────────────
    //
    // eslint-plugin-react-hooks v7 gộp bộ luật của React Compiler vào cấu hình recommended ở mức LỖI.
    // Plugin đã ở v7 từ commit đầu tiên có package.json (d985003) và chưa từng đổi phiên bản, nên các
    // luật này đã bật suốt — 51 chỗ vi phạm là nợ tích lại dần theo mã viết mới.
    //
    // TRẠNG THÁI (2026-07-19, sau đợt dọn thứ hai): ĐÃ DỌN 31/51, còn 20 cảnh báo — tất cả là cố ý
    // để lại, không có chỗ nào là lỗi thật:
    //   • [ĐÃ DỌN — đợt 1, form/dialog] dùng key dựng lại component / suy ra lúc render:
    //     CongViec, GiaCong EditorDialog, DocumentEditor, CongThongTin, RequestReviewModal, HRPages.
    //   • [ĐÃ DỌN — đợt 2, nhóm A–F] cùng hai kỹ thuật đó (mốc chặn gán-lúc-render, hoặc
    //     useSyncExternalStore cho cờ localStorage): ConfirmDialog, TimesheetCalendar, KhachHang,
    //     PhanHoi, QuanLyBangCong, CountUp, SystemSettings, EyeReminderPopup, WaterReminderPopup,
    //     AvatarCropper (vi phạm THẬT — đọc ref lúc render, đã chuyển sang state), PhieuChi (purity
    //     Date.now() → tách ra ngoài component), ChatNotifications.
    //   • [CÒN LẠI 19] src/features/chamcong/** — camera nhận diện khuôn mặt + chấm công. CheckInScanner
    //     truyền một ref VÀO useBurstCheckIn để hook đọc sau trong vòng lặp bất đồng bộ; luật không
    //     thấy được nên "nhuộm" luôn mọi giá trị hook trả về (15 cảnh báo từ 1 nguyên nhân). Để lại vì
    //     kiểm chứng cần camera + mặt thật + ảnh giả + thử mất mạng + màn kiosk (xem plan nhóm G).
    //   • [CÒN LẠI 1] PhieuChi.tsx:106 — đồng bộ mã QR đang mở theo dữ liệu realtime khi người nhận vừa
    //     ký. Để lại vì kiểm cần dựng thủ quỹ (role Kế toán + phòng is_accounting) rồi quét QR bằng app.
    //
    // ĐÃ CÂN NHẮC BẬT REACT COMPILER (2026-07-19) NHƯNG QUYẾT ĐỊNH CHƯA BẬT: đã cài thử toolchain
    // (babel-plugin-react-compiler 1.0 + @rolldown/plugin-babel) và ĐO thật. Hai phát hiện:
    //   1. Chỉ thị "use memo" mức MODULE không opt-in được — phải gắn trong từng hàm (≈486 chỗ / 110
    //      tệp), một diff khổng lồ không kiểm tay hết được (dự án không có test frontend).
    //   2. Chế độ biên dịch-tất-cả: 485/637 hàm biên dịch được, nhưng lý do bail số 1 là try/finally
    //      (51) + try-không-catch (8) — nằm đúng trong các handler lưu/xoá của những trang NẶNG
    //      (KhachHang, KeToan, PhieuChi…). Đo đếm số render: KhachHang gõ 11 phím = 44 lần render
    //      StatCard, BẬT hay TẮT compiler đều 44 (không cải thiện, vì trang bị bail). Đổi lại gói to
    //      thêm 11,9% (gzip +53,5 kB) cho mọi người tải. → Không đáng, để lại tới khi refactor try/finally.
    //
    // Vì CHƯA bật compiler nên ba luật vẫn ở mức "warn" (chuẩn bị cho tương lai), không phải error.
    // Muốn đưa về error thì trước hết phải dọn nốt 20 chỗ còn lại (nhóm G + PhieuChi:106) rồi mới bật.
    files: ['**/*.{ts,tsx}'],
    rules: {
      'react-hooks/set-state-in-effect': 'warn',
      'react-hooks/refs': 'warn',
      'react-hooks/purity': 'warn',
    },
  },
  {
    // shadcn/ui: mỗi file cố ý export kèm hằng/biến thể (buttonVariants…) cạnh component. Đó là quy ước
    // của thư viện, không phải lỗi của ta — chỉ làm mất Fast Refresh khi sửa chính file đó lúc dev.
    files: ['src/shadcn/**/*.{ts,tsx}'],
    rules: { 'react-refresh/only-export-components': 'off' },
  },
])
