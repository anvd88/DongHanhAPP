import { AttendancePage } from "../features/chamcong/attendance/AttendancePage";

/**
 * Màn hình kiosk chấm công — mở từ trang đăng nhập, KHÔNG cần tài khoản.
 * Giao diện Liquid Glass; bấm "Bắt đầu quét" để nhận diện khuôn mặt (Vào/Ra).
 */
export function KioskPage() {
  return (
    <div data-login-route-ready="true" style={{ display: "contents" }}>
      <AttendancePage />
    </div>
  );
}
