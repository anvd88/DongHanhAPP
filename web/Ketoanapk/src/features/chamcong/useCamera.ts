import { useCallback, useEffect, useRef, useState } from "react";

/**
 * Hook điều khiển webcam bằng WebRTC getUserMedia.
 * Lưu ý: getUserMedia chỉ chạy trên HTTPS hoặc localhost.
 */
export function useCamera() {
  const videoRef = useRef<HTMLVideoElement>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const [active, setActive] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const start = useCallback(async () => {
    setError(null);
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        // Độ phân giải cao hơn (720p) → nhiều chi tiết hơn cho ánh sáng yếu / mặt nhỏ ở xa.
        video: { facingMode: "user", width: { ideal: 1280 }, height: { ideal: 720 } },
        audio: false,
      });
      streamRef.current = stream;
      if (videoRef.current) {
        videoRef.current.srcObject = stream;
        await videoRef.current.play();
      }
      setActive(true);
    } catch (e) {
      setError(
        e instanceof DOMException && e.name === "NotAllowedError"
          ? "Bạn chưa cấp quyền dùng camera."
          : e instanceof Error
            ? e.message
            : "Không mở được camera.",
      );
      setActive(false);
    }
  }, []);

  const stop = useCallback(() => {
    streamRef.current?.getTracks().forEach((t) => t.stop());
    streamRef.current = null;
    if (videoRef.current) videoRef.current.srcObject = null;
    setActive(false);
  }, []);

  /** Chụp 1 khung hình hiện tại, trả về data URL JPEG (base64) hoặc null. */
  const capture = useCallback((): string | null => {
    const video = videoRef.current;
    if (!video || !video.videoWidth) return null;
    const canvas = document.createElement("canvas");
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    const ctx = canvas.getContext("2d");
    if (!ctx) return null;
    ctx.drawImage(video, 0, 0);
    // Chất lượng JPEG cao hơn để giữ chi tiết khuôn mặt cho khâu phân tích phía server.
    return canvas.toDataURL("image/jpeg", 0.92);
  }, []);

  /**
   * Chụp 1 LOẠT khung hình liên tiếp (mặc định 10 khung, cách nhau ~110ms ≈ 1.2s) để server
   * chọn ảnh tốt nhất. Bỏ qua các khung không lấy được. Trả mảng data URL base64.
   */
  const captureBurst = useCallback(
    async (count = 10, gapMs = 110): Promise<string[]> => {
      const frames: string[] = [];
      for (let i = 0; i < count; i++) {
        const img = capture();
        if (img) frames.push(img);
        if (i < count - 1) await new Promise<void>((r) => window.setTimeout(r, gapMs));
      }
      return frames;
    },
    [capture],
  );

  // Tự tắt camera khi component bị gỡ.
  useEffect(() => () => stop(), [stop]);

  return { videoRef, active, error, start, stop, capture, captureBurst };
}
