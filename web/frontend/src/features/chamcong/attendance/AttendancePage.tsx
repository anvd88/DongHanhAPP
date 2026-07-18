import { useEffect, useState } from "react";
import "@fontsource/be-vietnam-pro/400.css";
import "@fontsource/be-vietnam-pro/500.css";
import "@fontsource/be-vietnam-pro/600.css";
import "@fontsource/be-vietnam-pro/700.css";
import "@fontsource/be-vietnam-pro/800.css";
import { CheckInScanner } from "../CheckInScanner";
import { AttendanceHeader } from "./AttendanceHeader";
import { AttendanceGlassCard } from "./AttendanceGlassCard";
import { kioskKeyStore } from "../../../lib/api";
import "./attendance.css";

/**
 * Trang kiosk chấm công (Liquid Glass). Mặc định hiển thị card chờ; bấm
 * "Bắt đầu quét" sẽ gắn bộ quét khuôn mặt thật (camera) và tự khởi động.
 *
 * Khi backend bật khóa kiosk (Security:KioskApiKey), thiết bị này cần "mã kiosk" để chấm công ẩn danh:
 * mở một lần bằng /kiosk?kioskKey=<mã> (tự lưu vào máy), hoặc bấm "Thiết lập mã kiosk" bên dưới.
 */
export function AttendancePage() {
  const [scanning, setScanning] = useState(false);
  const [hasKey, setHasKey] = useState(() => !!kioskKeyStore.get());

  // Nạp mã kiosk từ tham số URL ?kioskKey=... (thiết lập một lần cho thiết bị), rồi xóa khỏi URL.
  useEffect(() => {
    const url = new URL(window.location.href);
    const k = url.searchParams.get("kioskKey");
    if (k) {
      kioskKeyStore.set(k);
      setHasKey(true);
      url.searchParams.delete("kioskKey");
      window.history.replaceState(null, "", url.pathname + url.search + url.hash);
    }
  }, []);

  const setupKey = () => {
    const current = kioskKeyStore.get() ?? "";
    const next = window.prompt("Nhập mã kiosk cấp cho thiết bị này (để trống để xóa):", current);
    if (next === null) return;
    if (next.trim()) { kioskKeyStore.set(next); setHasKey(true); }
    else { kioskKeyStore.clear(); setHasKey(false); }
  };

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
        <button type="button" className="att-kiosk-key-setup" onClick={setupKey}>
          {hasKey ? "Đã cấu hình mã kiosk · Đổi/xóa" : "Thiết lập mã kiosk"}
        </button>
      </div>
    </div>
  );
}
