import { useEffect, useRef } from "react";
import { Aperture, Camera, CameraOff, Loader2, ScanLine } from "lucide-react";
import { useCamera } from "./useCamera";

/**
 * Khung camera dùng chung cho cả "Chấm công" và "Đăng ký khuôn mặt".
 * Tự quản lý vòng đời webcam; khi bấm chụp (hoặc tự động) sẽ gọi onCapture(dataUrlBase64).
 */
export function CameraPanel({
  onCapture,
  busy,
  captureLabel = "Chụp & nhận diện",
  auto = false,
  autoHint,
  autoBusyLabel = "Đang nhận diện…",
  paused = false,
  intervalMs = 1500,
  stopSignal = 0,
  onActiveChange,
}: {
  onCapture: (image: string) => void;
  busy?: boolean;
  captureLabel?: string;
  auto?: boolean; // tự động chụp theo nhịp khi camera đang bật
  autoHint?: string; // nội dung hướng dẫn khi đang tự động quét
  autoBusyLabel?: string;
  paused?: boolean; // tạm dừng tự động chụp (vd: đang hiện kết quả)
  intervalMs?: number;
  stopSignal?: number; // tăng giá trị để yêu cầu tắt camera từ component cha
  onActiveChange?: (active: boolean) => void; // báo cho cha biết camera đang bật/tắt
}) {
  const { videoRef, active, error, start, stop, capture } = useCamera();

  const snap = () => {
    const img = capture();
    if (img) onCapture(img);
  };

  // Giữ tham chiếu mới nhất để vòng lặp tự động không phải tạo lại mỗi lần render.
  const onCaptureRef = useRef(onCapture);
  useEffect(() => {
    onCaptureRef.current = onCapture;
  }, [onCapture]);

  // Báo trạng thái bật/tắt camera ra ngoài (vd: để bật/tắt nút "Bắt đầu quét").
  const onActiveChangeRef = useRef(onActiveChange);
  useEffect(() => {
    onActiveChangeRef.current = onActiveChange;
  }, [onActiveChange]);
  useEffect(() => {
    onActiveChangeRef.current?.(active);
  }, [active]);

  useEffect(() => {
    if (stopSignal > 0 && active) stop();
  }, [stopSignal, active, stop]);

  // Tự động chụp: chỉ chạy khi bật auto + camera đang mở + không bận + không tạm dừng.
  useEffect(() => {
    if (!auto || !active || busy || paused) return;
    const id = window.setInterval(() => {
      const img = capture();
      if (img) onCaptureRef.current(img);
    }, intervalMs);
    return () => window.clearInterval(id);
  }, [auto, active, busy, paused, intervalMs, capture]);

  return (
    <div className="cc-camera glass">
      <div className="cc-video-wrap">
        {/* video luôn render để giữ ref; ẩn khi chưa bật */}
        <video ref={videoRef} playsInline muted className="cc-video" data-active={active} />
        {!active && (
          <div className="cc-video-empty">
            <Camera className="h-8 w-8" />
            <span>Camera đang tắt</span>
          </div>
        )}
        <span className="cc-frame-guide" aria-hidden="true" />
        {auto && active && (
          <div className="cc-scan-hint">
            {busy ? (
              <>
                <Loader2 className="h-3.5 w-3.5 animate-spin" /> {autoBusyLabel}
              </>
            ) : paused ? null : (
              <>
                <ScanLine className="h-3.5 w-3.5" /> {autoHint || "Tự động quét — mời nhìn vào camera"}
              </>
            )}
          </div>
        )}
      </div>

      {error && <div className="cc-error">{error}</div>}

      <div className="cc-camera-actions">
        {!active ? (
          <button className="cc-btn cc-btn-primary" onClick={start} type="button">
            <Camera className="h-4 w-4" /> Bật camera
          </button>
        ) : (
          <>
            <button className="cc-btn" onClick={stop} type="button">
              <CameraOff className="h-4 w-4" /> Tắt
            </button>
            <button className="cc-btn cc-btn-primary" onClick={snap} disabled={busy} type="button">
              {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Aperture className="h-4 w-4" />} {captureLabel}
            </button>
          </>
        )}
      </div>
    </div>
  );
}
