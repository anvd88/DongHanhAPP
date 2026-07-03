import { useState } from "react";
import "@fontsource/be-vietnam-pro/400.css";
import "@fontsource/be-vietnam-pro/500.css";
import "@fontsource/be-vietnam-pro/600.css";
import "@fontsource/be-vietnam-pro/700.css";
import "@fontsource/be-vietnam-pro/800.css";
import { CheckInScanner } from "../CheckInScanner";
import { AttendanceHeader } from "./AttendanceHeader";
import { AttendanceGlassCard } from "./AttendanceGlassCard";
import "./attendance.css";

/**
 * Trang kiosk chấm công (Liquid Glass). Mặc định hiển thị card chờ; bấm
 * "Bắt đầu quét" sẽ gắn bộ quét khuôn mặt thật (camera) và tự khởi động.
 */
export function AttendancePage() {
  const [scanning, setScanning] = useState(false);

  return (
    <div className="att-page">
      <div className="att-shell">
        <AttendanceHeader />
        <main className="att-main">
          {scanning ? (
            <CheckInScanner biometricMode returnToLoginOnOk autoStart />
          ) : (
            <AttendanceGlassCard onStart={() => setScanning(true)} />
          )}
        </main>
      </div>
    </div>
  );
}
