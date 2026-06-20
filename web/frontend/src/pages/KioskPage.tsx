import { Link } from "react-router-dom";
import { ArrowLeft, ScanFace } from "lucide-react";
import { CheckInScanner } from "../features/chamcong/CheckInScanner";
import "../features/chamcong/chamcong.css";

/**
 * Màn hình kiosk chấm công — mở từ trang đăng nhập, KHÔNG cần tài khoản.
 * Chỉ quét khuôn mặt và thông báo ai vừa chấm công (Vào/Ra).
 */
export function KioskPage() {
  return (
    <div className="cc-root cc-kiosk-page">
      <header className="cc-kiosk-header">
        <div className="cc-kiosk-heading">
          <div className="cc-kiosk-title-row">
            <ScanFace className="cc-kiosk-brand-icon" />
            <h1>Chấm công</h1>
          </div>
          <p>Nhìn vào camera để ghi nhận giờ vào / ra</p>
        </div>
        <Link to="/login" className="cc-kiosk-login">
          <ArrowLeft className="h-4 w-4" /> Đăng nhập
        </Link>
      </header>

      <main className="cc-kiosk-main">
        <CheckInScanner returnToLoginOnOk biometricMode />
      </main>
    </div>
  );
}
