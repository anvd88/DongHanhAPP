import { useCallback, useEffect, useRef, useState } from "react";
import { tokenStore } from "../../lib/api";

/**
 * Hook lấy hình từ CAMERA IP (RTSP kiosk) qua ảnh snapshot mới nhất do FFmpeg ghi liên tục.
 * API bề mặt giống useCamera (start/stop/capture/captureBurst) để bộ quét chấm công dùng chung,
 * nhưng nguồn khung hình là ảnh JPEG poll từ server thay vì webcam của thiết bị.
 */
const SNAPSHOT_URL = "/api/chamcong/rtsp/snapshot";
const POLL_INTERVAL_MS = 120; // Khớp nhịp ghi của FFmpeg (~10 khung/giây) để hình mượt, ít giật.

function blobToDataUrl(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as string);
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(blob);
  });
}

export function useIpCamera() {
  const [active, setActive] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [frameUrl, setFrameUrl] = useState<string | null>(null);

  const objectUrlRef = useRef<string | null>(null);
  const timerRef = useRef<number | undefined>(undefined);
  const activeRef = useRef(false);
  const busyRef = useRef(false);

  // Hiển thị 1 khung: tạo objectURL mới rồi thu hồi cái cũ (rẻ, không đụng CPU như base64).
  const showBlob = useCallback((blob: Blob) => {
    const next = URL.createObjectURL(blob);
    if (objectUrlRef.current) URL.revokeObjectURL(objectUrlRef.current);
    objectUrlRef.current = next;
    setFrameUrl(next);
  }, []);

  // Tải 1 khung snapshot ở dạng nhị phân. Luồng HIỂN THỊ chỉ cần blob → KHÔNG đổi base64 ở đây
  // (đổi base64 mỗi khung là thủ phạm chính gây giật/đơ); chỉ captureBurst mới đổi để gửi server.
  const grabBlob = useCallback(async (): Promise<Blob | null> => {
    const headers: Record<string, string> = {};
    const token = tokenStore.get();
    if (token) headers["Authorization"] = `Bearer ${token}`;
    try {
      const res = await fetch(SNAPSHOT_URL, { headers, cache: "no-store" });
      if (!res.ok) {
        setError(
          res.status === 404 || res.status === 503
            ? "Đang chờ tín hiệu từ camera IP…"
            : "Không lấy được hình từ camera IP.",
        );
        return null;
      }
      setError(null);
      return await res.blob();
    } catch {
      setError("Không kết nối được camera IP.");
      return null;
    }
  }, []);

  const start = useCallback(async () => {
    setError(null);
    activeRef.current = true;
    setActive(true);
    const first = await grabBlob();
    if (first) showBlob(first);
    if (timerRef.current) window.clearInterval(timerRef.current);
    timerRef.current = window.setInterval(() => {
      if (!activeRef.current || busyRef.current || document.visibilityState !== "visible") return;
      busyRef.current = true;
      void grabBlob()
        .then((b) => {
          if (b) showBlob(b);
        })
        .finally(() => {
          busyRef.current = false;
        });
    }, POLL_INTERVAL_MS);
  }, [grabBlob, showBlob]);

  const stop = useCallback(() => {
    activeRef.current = false;
    setActive(false);
    if (timerRef.current) {
      window.clearInterval(timerRef.current);
      timerRef.current = undefined;
    }
  }, []);

  /**
   * Gom 1 LOẠT khung từ camera IP (base64) để server chọn ảnh tốt nhất. Tải tuần tự, tạm dừng nhịp
   * poll nền để không tranh request. Chỉ ở đây mới đổi base64 (số khung ít nên không gây giật).
   */
  const captureBurst = useCallback(
    async (count = 8, gapMs = 280): Promise<string[]> => {
      const frames: string[] = [];
      busyRef.current = true;
      try {
        for (let i = 0; i < count; i++) {
          const blob = await grabBlob();
          if (blob) {
            showBlob(blob);
            frames.push(await blobToDataUrl(blob));
          }
          if (i < count - 1) await new Promise<void>((r) => window.setTimeout(r, gapMs));
        }
      } finally {
        busyRef.current = false;
      }
      return frames;
    },
    [grabBlob, showBlob],
  );

  useEffect(
    () => () => {
      activeRef.current = false;
      if (timerRef.current) window.clearInterval(timerRef.current);
      if (objectUrlRef.current) URL.revokeObjectURL(objectUrlRef.current);
    },
    [],
  );

  return { active, error, frameUrl, start, stop, captureBurst };
}
