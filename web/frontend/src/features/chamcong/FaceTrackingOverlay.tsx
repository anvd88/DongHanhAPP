import { useEffect, useRef, useState, type RefObject } from "react";
import type { FaceDetector } from "@mediapipe/tasks-vision";

/** Trạng thái căn khung mặt suy ra từ vị trí/kích cỡ mặt trong khung hình. */
export type Framing = {
  state: "none" | "far" | "near" | "offcenter" | "good";
  hint: string;
  ok: boolean;
};

const NONE: Framing = { state: "none", hint: "Đưa khuôn mặt vào khung", ok: false };

// Ngưỡng căn khung (theo tỉ lệ so với khung hình nguồn).
const FACE_MIN_RATIO = 0.16; // mặt nhỏ hơn ⇒ ở quá xa
const FACE_MAX_RATIO = 0.6; // mặt lớn hơn ⇒ ở quá gần
const OFFCENTER = 0.2; // lệch tâm quá ngưỡng ⇒ nhắc vào giữa
const DETECT_INTERVAL_MS = 45; // ~22fps: đủ mượt, nhẹ cho máy yếu

// Nạp MediaPipe FaceDetector một lần (dùng chung mọi lần gắn). Tài nguyên tự host trong /public.
let detectorPromise: Promise<FaceDetector> | null = null;
function getDetector(): Promise<FaceDetector> {
  if (!detectorPromise) {
    detectorPromise = (async () => {
      const vision = await import("@mediapipe/tasks-vision");
      const fileset = await vision.FilesetResolver.forVisionTasks("/mediapipe/wasm");
      return vision.FaceDetector.createFromOptions(fileset, {
        baseOptions: { modelAssetPath: "/mediapipe/blaze_face_short_range.tflite" },
        runningMode: "VIDEO",
        minDetectionConfidence: 0.5,
      });
    })().catch((e) => {
      detectorPromise = null; // cho phép thử lại lần sau
      throw e;
    });
  }
  return detectorPromise;
}

type Box = { left: number; top: number; width: number; height: number };

/**
 * Khung bám theo khuôn mặt: chạy MediaPipe ngay trên trình duyệt, vẽ khung đi theo mặt và đổi
 * màu theo độ căn khung (xanh = tốt, hổ phách = lệch/xa/gần). Tự xử lý gương (scaleX(-1)) và
 * object-fit: cover để khung khớp đúng mặt. Nếu không nạp được model thì lui về khung tĩnh cũ.
 *
 * Đặt phủ kín ô video (inset:0). KHÔNG đụng tới nhận diện/chống giả mạo — đó vẫn ở server.
 */
export function FaceTrackingOverlay({
  videoRef,
  active,
  onFraming,
}: {
  videoRef: RefObject<HTMLVideoElement | null>;
  active: boolean;
  onFraming?: (f: Framing) => void;
}) {
  const rootRef = useRef<HTMLDivElement>(null);
  const [box, setBox] = useState<Box | null>(null);
  const [ok, setOk] = useState(false);
  const [failed, setFailed] = useState(false);
  const onFramingRef = useRef(onFraming);
  useEffect(() => {
    onFramingRef.current = onFraming;
  }, [onFraming]);

  useEffect(() => {
    if (!active) {
      setBox(null);
      setOk(false);
      onFramingRef.current?.(NONE);
      return;
    }

    let cancelled = false;
    let raf = 0;
    let detector: FaceDetector | null = null;
    let lastDetect = 0;
    let lastTs = -1;

    const emit = (f: Framing) => onFramingRef.current?.(f);

    const loop = () => {
      if (cancelled) return;
      raf = requestAnimationFrame(loop);

      const video = videoRef.current;
      const root = rootRef.current;
      const now = performance.now();
      if (!detector || !video || !root || !video.videoWidth || video.readyState < 2) return;
      if (now - lastDetect < DETECT_INTERVAL_MS) return;
      lastDetect = now;

      const ts = now <= lastTs ? lastTs + 1 : now; // MediaPipe yêu cầu timestamp tăng dần
      lastTs = ts;

      let result;
      try {
        result = detector.detectForVideo(video, ts);
      } catch {
        return;
      }

      const det = (result?.detections ?? [])
        .filter((d) => d.boundingBox)
        .sort((a, b) => b.boundingBox!.width * b.boundingBox!.height - a.boundingBox!.width * a.boundingBox!.height)[0];

      if (!det?.boundingBox) {
        setBox(null);
        setOk(false);
        emit(NONE);
        return;
      }

      const bb = det.boundingBox;
      const vw = video.videoWidth;
      const vh = video.videoHeight;
      const W = root.clientWidth;
      const H = root.clientHeight;

      // Quy đổi toạ độ nguồn → hiển thị theo object-fit: cover, rồi lật ngang do video bị gương.
      const s = Math.max(W / vw, H / vh);
      const ox = (W - vw * s) / 2;
      const oy = (H - vh * s) / 2;
      const width = bb.width * s;
      const height = bb.height * s;
      const left = W - (ox + (bb.originX + bb.width) * s);
      const top = oy + bb.originY * s;

      // Đánh giá căn khung trên toạ độ nguồn (không phụ thuộc gương).
      const sizeRatio = bb.width / vw;
      const cx = (bb.originX + bb.width / 2) / vw - 0.5;
      const cy = (bb.originY + bb.height / 2) / vh - 0.5;

      let framing: Framing;
      if (sizeRatio < FACE_MIN_RATIO) framing = { state: "far", hint: "Lại gần hơn", ok: false };
      else if (sizeRatio > FACE_MAX_RATIO) framing = { state: "near", hint: "Lùi ra một chút", ok: false };
      else if (Math.abs(cx) > OFFCENTER || Math.abs(cy) > OFFCENTER)
        framing = { state: "offcenter", hint: "Đưa khuôn mặt vào giữa khung", ok: false };
      else framing = { state: "good", hint: "Giữ yên — đã vào khung", ok: true };

      setBox({ left, top, width, height });
      setOk(framing.ok);
      emit(framing);
    };

    getDetector()
      .then((d) => {
        if (cancelled) return;
        detector = d;
        loop();
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      });

    return () => {
      cancelled = true;
      cancelAnimationFrame(raf);
    };
  }, [active, videoRef]);

  // Không nạp được model → không hiện khung gì (server vẫn phát hiện & nhận diện như cũ).
  if (failed) return null;

  return (
    <div ref={rootRef} className="cc-face-overlay" aria-hidden="true">
      {box && (
        <div
          className="cc-face-box"
          data-ok={ok}
          style={{ left: box.left, top: box.top, width: box.width, height: box.height }}
        />
      )}
    </div>
  );
}
