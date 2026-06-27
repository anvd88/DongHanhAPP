import { useEffect, useRef, useState, type RefObject } from "react";
import type { FaceLandmarker } from "@mediapipe/tasks-vision";

/** Trạng thái căn khung mặt + tín hiệu liveness (cử động tự nhiên + chớp mắt) từ landmark. */
export type Framing = {
  state: "none" | "far" | "near" | "offcenter" | "good";
  hint: string;
  ok: boolean;
  /** Tổng số lần chớp mắt phát hiện được từ khi mở camera (tăng dần) — liveness chủ động (step-up). */
  blinkCount: number;
  /** Tổng "cử động biểu cảm" tích lũy (tăng dần) — liveness THỤ ĐỘNG: người thật luôn nhúc nhích,
   *  ảnh tĩnh thì gần như đứng yên. So sánh mức tăng trong cửa sổ giữ khung để biết có phải mặt sống. */
  motion: number;
};

const NONE: Framing = { state: "none", hint: "Đưa khuôn mặt vào khung", ok: false, blinkCount: 0, motion: 0 };

// Ngưỡng căn khung (theo tỉ lệ bbox khuôn mặt so với khung hình nguồn).
const FACE_MIN_RATIO = 0.16; // mặt nhỏ hơn ⇒ ở quá xa
const FACE_MAX_RATIO = 0.62; // mặt lớn hơn ⇒ ở quá gần
const OFFCENTER = 0.2; // lệch tâm quá ngưỡng ⇒ nhắc vào giữa
const DETECT_INTERVAL_MS = 50; // FaceLandmarker nặng hơn FaceDetector → giãn nhịp một chút
// Phát hiện chớp mắt từ blendshape eyeBlink (0 = mở, 1 = nhắm). Trễ vào/ra để khỏi đếm nhiễu.
const BLINK_CLOSE = 0.45; // vượt ngưỡng ⇒ coi như đang nhắm
const BLINK_OPEN = 0.22; // tụt dưới ngưỡng sau khi nhắm ⇒ tính 1 lần chớp
// Liveness thụ động: cộng dồn thay đổi của vài blendshape biểu cảm. Có "vùng chết" để bỏ nhiễu
// rung của model (mặt đứng yên không cộng), chỉ cử động thật mới làm số này tăng.
const EXPR_KEYS = [
  "eyeBlinkLeft", "eyeBlinkRight", "jawOpen",
  "mouthSmileLeft", "mouthSmileRight", "mouthPucker",
  "browInnerUp", "browDownLeft", "browDownRight",
];
const EXPR_DEADZONE = 0.02; // thay đổi nhỏ hơn coi là nhiễu, không tính

// Nạp MediaPipe FaceLandmarker một lần (dùng chung mọi lần gắn). Tài nguyên tự host trong /public.
let landmarkerPromise: Promise<FaceLandmarker> | null = null;
function getLandmarker(): Promise<FaceLandmarker> {
  if (!landmarkerPromise) {
    landmarkerPromise = (async () => {
      const vision = await import("@mediapipe/tasks-vision");
      const fileset = await vision.FilesetResolver.forVisionTasks("/mediapipe/wasm");
      return vision.FaceLandmarker.createFromOptions(fileset, {
        baseOptions: { modelAssetPath: "/mediapipe/face_landmarker.task" },
        runningMode: "VIDEO",
        numFaces: 1,
        outputFaceBlendshapes: true, // cần eyeBlinkLeft/Right để bắt chớp mắt
        outputFacialTransformationMatrixes: false,
      });
    })().catch((e) => {
      landmarkerPromise = null; // cho phép thử lại lần sau
      throw e;
    });
  }
  return landmarkerPromise;
}

type Box = { left: number; top: number; width: number; height: number };
type Category = { categoryName?: string; displayName?: string; score: number };

function blendScore(cats: Category[] | undefined, name: string): number {
  if (!cats) return 0;
  const c = cats.find((x) => x.categoryName === name || x.displayName === name);
  return c ? c.score : 0;
}

/**
 * Khung bám theo khuôn mặt + liveness chớp mắt: chạy MediaPipe FaceLandmarker ngay trên trình
 * duyệt, vẽ khung đi theo mặt (đổi màu theo độ căn khung) và đếm số lần chớp mắt qua blendshape.
 * Tự xử lý gương (scaleX(-1)) và object-fit: cover để khung khớp đúng mặt. Nếu không nạp được
 * model thì lui về không hiện gì (server vẫn nhận diện & chống giả mạo như cũ).
 *
 * Đặt phủ kín ô video (inset:0). KHÔNG đụng tới nhận diện ở server.
 */
export function FaceTrackingOverlay({
  videoRef,
  active,
  onFraming,
  drawBox = true,
}: {
  videoRef: RefObject<HTMLVideoElement | null>;
  active: boolean;
  onFraming?: (f: Framing) => void;
  /** Vẽ khung bám mặt hay chỉ chạy phát hiện ngầm (kiosk dùng false để lấy tín hiệu căn khung). */
  drawBox?: boolean;
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
    let landmarker: FaceLandmarker | null = null;
    let lastDetect = 0;
    let lastTs = -1;
    let eyesClosed = false; // trạng thái mắt cho máy đếm chớp
    let blinkCount = 0;
    let prevExpr: number[] | null = null; // blendshape biểu cảm khung trước
    let motion = 0; // cộng dồn cử động biểu cảm (liveness thụ động)

    const emit = (f: Omit<Framing, "blinkCount" | "motion">) =>
      onFramingRef.current?.({ ...f, blinkCount, motion });

    const loop = () => {
      if (cancelled) return;
      raf = requestAnimationFrame(loop);

      const video = videoRef.current;
      const root = rootRef.current;
      const now = performance.now();
      if (!landmarker || !video || !root || !video.videoWidth || video.readyState < 2) return;
      if (now - lastDetect < DETECT_INTERVAL_MS) return;
      lastDetect = now;

      const ts = now <= lastTs ? lastTs + 1 : now; // MediaPipe yêu cầu timestamp tăng dần
      lastTs = ts;

      let result;
      try {
        result = landmarker.detectForVideo(video, ts);
      } catch {
        return;
      }

      const lm = result?.faceLandmarks?.[0];
      if (!lm || lm.length === 0) {
        setBox(null);
        setOk(false);
        emit(NONE);
        return;
      }

      // Máy đếm chớp mắt (trung bình 2 mắt) — có trễ vào/ra để khỏi đếm nhiễu rung.
      const cats = result?.faceBlendshapes?.[0]?.categories as Category[] | undefined;
      const blink = (blendScore(cats, "eyeBlinkLeft") + blendScore(cats, "eyeBlinkRight")) / 2;
      if (!eyesClosed && blink > BLINK_CLOSE) eyesClosed = true;
      else if (eyesClosed && blink < BLINK_OPEN) {
        eyesClosed = false;
        blinkCount += 1;
      }

      // Liveness thụ động: cộng dồn cử động biểu cảm (qua vùng chết để bỏ nhiễu).
      const expr = EXPR_KEYS.map((k) => blendScore(cats, k));
      if (prevExpr) {
        let d = 0;
        for (let i = 0; i < expr.length; i++) {
          const dd = Math.abs(expr[i] - prevExpr[i]);
          if (dd > EXPR_DEADZONE) d += dd;
        }
        motion += d;
      }
      prevExpr = expr;

      // Bbox khuôn mặt từ landmark (toạ độ chuẩn hoá 0..1).
      let minX = 1, minY = 1, maxX = 0, maxY = 0;
      for (const p of lm) {
        if (p.x < minX) minX = p.x;
        if (p.x > maxX) maxX = p.x;
        if (p.y < minY) minY = p.y;
        if (p.y > maxY) maxY = p.y;
      }

      const vw = video.videoWidth;
      const vh = video.videoHeight;
      const W = root.clientWidth;
      const H = root.clientHeight;

      // Quy đổi toạ độ nguồn → hiển thị theo object-fit: cover, rồi lật ngang do video bị gương.
      const oxX = minX * vw;
      const oxY = minY * vh;
      const bw = (maxX - minX) * vw;
      const bh = (maxY - minY) * vh;
      const s = Math.max(W / vw, H / vh);
      const ox = (W - vw * s) / 2;
      const oy = (H - vh * s) / 2;
      const width = bw * s;
      const height = bh * s;
      const left = W - (ox + (oxX + bw) * s);
      const top = oy + oxY * s;

      // Đánh giá căn khung trên toạ độ chuẩn hoá (không phụ thuộc gương).
      const sizeRatio = maxX - minX;
      const cx = (minX + maxX) / 2 - 0.5;
      const cy = (minY + maxY) / 2 - 0.5;

      let framing: Omit<Framing, "blinkCount" | "motion">;
      if (sizeRatio < FACE_MIN_RATIO) framing = { state: "far", hint: "Lại gần hơn", ok: false };
      else if (sizeRatio > FACE_MAX_RATIO) framing = { state: "near", hint: "Lùi ra một chút", ok: false };
      else if (Math.abs(cx) > OFFCENTER || Math.abs(cy) > OFFCENTER)
        framing = { state: "offcenter", hint: "Đưa khuôn mặt vào giữa khung", ok: false };
      else framing = { state: "good", hint: "Giữ yên — đã vào khung", ok: true };

      setBox({ left, top, width, height });
      setOk(framing.ok);
      emit(framing);
    };

    getLandmarker()
      .then((d) => {
        if (cancelled) return;
        landmarker = d;
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
      {drawBox && box && (
        <div
          className="cc-face-box"
          data-ok={ok}
          style={{ left: box.left, top: box.top, width: box.width, height: box.height }}
        />
      )}
    </div>
  );
}
