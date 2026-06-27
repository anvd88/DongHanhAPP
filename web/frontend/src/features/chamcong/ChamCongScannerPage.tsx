import { CheckInScanner } from "./CheckInScanner";
import "./chamcong.css";

/**
 * Trang chấm công riêng (/chamcong): chỉ giao diện nhận diện khuôn mặt, layout cố định lấp đầy
 * màn hình (không cuộn). Phần quản lý (đăng ký khuôn mặt, dữ liệu, nhật ký) nằm ở trang /ql-chamcong.
 */
export function ChamCongScannerPage() {
  return (
    <div className="cc-root cc-root--fit">
      <div>
        <h1 className="cc-title">Chấm công khuôn mặt</h1>
        <p className="cc-subtitle">Nhìn vào camera để ghi nhận giờ vào / ra</p>
      </div>
      <div className="cc-checkin-tab">
        <CheckInScanner />
      </div>
    </div>
  );
}
