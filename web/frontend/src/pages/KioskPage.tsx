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
    <div className="cc-root mx-auto flex min-h-screen w-full max-w-5xl flex-col gap-5 p-4 sm:p-6">
      <header className="flex items-center justify-between gap-3">
        <div>
          <h1 className="cc-title flex items-center gap-2">
            <ScanFace className="h-7 w-7 text-[var(--accent)]" /> Chấm công
          </h1>
          <p className="cc-subtitle">Nhìn vào camera để ghi nhận giờ vào / ra</p>
        </div>
        <Link to="/login" className="cc-btn">
          <ArrowLeft className="h-4 w-4" /> Đăng nhập
        </Link>
      </header>

      <CheckInScanner returnToLoginOnOk />
    </div>
  );
}
