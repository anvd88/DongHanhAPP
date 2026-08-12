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
  /** Hướng mặt tính NGAY TRÊN TRÌNH DUYỆT (tỉ lệ hình học, CÙNG quy ước với FacePose của server):
   *  yaw > 0 ⇒ người dùng quay sang TRÁI; pitch nhỏ = ngước lên, lớn = cúi xuống. Dùng cho đăng ký realtime. */
  yaw: number;
  pitch: number;
  hasPose: boolean;
};

const NONE: Framing = {
  state: "none", hint: "Đưa khuôn mặt vào khung", ok: false,
  blinkCount: 0, motion: 0, yaw: 0, pitch: 0, hasPose: false,
};

// Chỉ số landmark MediaPipe FaceMesh dùng để ước lượng hướng mặt (khớp công thức 5 điểm của server).
const LM_NOSE = 1; // đầu mũi
const LM_REYE = [33, 133]; // khóe mắt phải (ngoài, trong)
const LM_LEYE = [263, 362]; // khóe mắt trái (ngoài, trong)
const LM_MOUTH = [61, 291]; // hai khóe miệng

type LMPoint = { x: number; y: number };

/** Ước lượng yaw/pitch từ landmark — cùng quy ước hình học với PoseFrom() phía server. */
function estimatePose(lm: LMPoint[]): { yaw: number; pitch: number; hasPose: boolean } {
  const get = (i: number) => lm[i];
  const mid = (idx: number[]) => ({
    x: (get(idx[0]).x + get(idx[1]).x) / 2,
    y: (get(idx[0]).y + get(idx[1]).y) / 2,
  });
  if (!get(LM_NOSE) || !get(LM_REYE[0]) || !get(LM_LEYE[0]) || !get(LM_MOUTH[0]))
    return { yaw: 0, pitch: 0, hasPose: false };

  const rEye = mid(LM_REYE);
  const lEye = mid(LM_LEYE);
  const nose = get(LM_NOSE);
  const eyeMidX = (rEye.x + lEye.x) / 2;
  const eyeMidY = (rEye.y + lEye.y) / 2;
  const eyeDist = Math.abs(rEye.x - lEye.x);
  if (eyeDist < 1e-4) return { yaw: 0, pitch: 0, hasPose: false };

  const mouthMidY = (get(LM_MOUTH[0]).y + get(LM_MOUTH[1]).y) / 2;
  const faceVert = mouthMidY - eyeMidY;
  const yaw = (nose.x - eyeMidX) / eyeDist;
  const pitch = Math.abs(faceVert) < 1e-4 ? 0.5 : (nose.y - eyeMidY) / faceVert;
  return { yaw, pitch, hasPose: true };
}

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
type LandmarkConnection = { start: number; end: number };
let faceMeshConnections: LandmarkConnection[] = [];

function getLandmarker(): Promise<FaceLandmarker> {
  if (!landmarkerPromise) {
    landmarkerPromise = (async () => {
      const vision = await import("@mediapipe/tasks-vision");
      const fileset = await vision.FilesetResolver.forVisionTasks("/mediapipe/wasm");
      faceMeshConnections = vision.FaceLandmarker.FACE_LANDMARKS_TESSELATION as LandmarkConnection[];
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

const LANDMARK_RADIUS = 0.8;
const MAX_CANVAS_PIXEL_RATIO = 2;

function clearLandmarks(canvas: HTMLCanvasElement | null) {
  if (!canvas) return;
  const ctx = canvas.getContext("2d");
  if (!ctx) return;
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.clearRect(0, 0, canvas.width, canvas.height);
}

/** Vẽ lưới tam giác Face Mesh lên đúng vị trí video sau object-fit: cover và lật gương selfie. */
function drawLandmarkMesh(
  canvas: HTMLCanvasElement,
  landmarks: LMPoint[],
  sourceWidth: number,
  sourceHeight: number,
  displayWidth: number,
  displayHeight: number,
  framingOk: boolean,
) {
  const ctx = canvas.getContext("2d");
  if (!ctx || displayWidth <= 0 || displayHeight <= 0) return;

  const pixelRatio = Math.min(window.devicePixelRatio || 1, MAX_CANVAS_PIXEL_RATIO);
  const canvasWidth = Math.max(1, Math.round(displayWidth * pixelRatio));
  const canvasHeight = Math.max(1, Math.round(displayHeight * pixelRatio));
  if (canvas.width !== canvasWidth || canvas.height !== canvasHeight) {
    canvas.width = canvasWidth;
    canvas.height = canvasHeight;
  }

  ctx.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0);
  ctx.clearRect(0, 0, displayWidth, displayHeight);

  const scale = Math.max(displayWidth / sourceWidth, displayHeight / sourceHeight);
  const offsetX = (displayWidth - sourceWidth * scale) / 2;
  const offsetY = (displayHeight - sourceHeight * scale) / 2;

  const projected = landmarks.map((point) => ({
    x: displayWidth - (offsetX + point.x * sourceWidth * scale),
    y: offsetY + point.y * sourceHeight * scale,
    valid: Number.isFinite(point.x) && Number.isFinite(point.y),
  }));

  // Nối các điểm theo topology chính thức của MediaPipe để tạo lưới phủ sát bề mặt khuôn mặt.
  ctx.beginPath();
  for (const connection of faceMeshConnections) {
    const start = projected[connection.start];
    const end = projected[connection.end];
    if (!start?.valid || !end?.valid) continue;
    ctx.moveTo(start.x, start.y);
    ctx.lineTo(end.x, end.y);
  }
  ctx.strokeStyle = framingOk ? "rgba(52, 211, 153, 0.38)" : "rgba(250, 204, 21, 0.34)";
  ctx.lineWidth = 0.65;
  ctx.lineJoin = "round";
  ctx.stroke();

  // Giữ các nút landmark nhỏ để người dùng nhìn rõ lưới đang bám theo chuyển động thật.
  ctx.beginPath();
  for (const point of projected) {
    if (!point.valid) continue;
    ctx.moveTo(point.x + LANDMARK_RADIUS, point.y);
    ctx.arc(point.x, point.y, LANDMARK_RADIUS, 0, Math.PI * 2);
  }
  ctx.fillStyle = framingOk ? "rgba(110, 231, 183, 0.92)" : "rgba(253, 224, 71, 0.9)";
  ctx.shadowColor = framingOk ? "rgba(16, 185, 129, 0.7)" : "rgba(250, 204, 21, 0.65)";
  ctx.shadowBlur = 3;
  ctx.fill();
  ctx.shadowBlur = 0;
}

function blendScore(cats: Category[] | undefined, name: string): number {
  if (!cats) return 0;
  const c = cats.find((x) => x.categoryName === name || x.displayName === name);
  return c ? c.score : 0;
}

/**
 * Điểm mốc + khung bám theo khuôn mặt + liveness chớp mắt: chạy MediaPipe FaceLandmarker ngay trên
 * trình duyệt, vẽ landmark/khung đi theo mặt và đếm số lần chớp mắt qua blendshape.
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
  drawLandmarks,
}: {
  videoRef: RefObject<HTMLVideoElement | null>;
  active: boolean;
  onFraming?: (f: Framing) => void;
  /** Vẽ khung bám mặt hay chỉ chạy phát hiện ngầm (kiosk dùng false để lấy tín hiệu căn khung). */
  drawBox?: boolean;
  /** Vẽ toàn bộ điểm mốc MediaPipe. Mặc định đi cùng drawBox để kiosk ẩn không tốn công vẽ. */
  drawLandmarks?: boolean;
}) {
  const rootRef = useRef<HTMLDivElement>(null);
  const landmarkCanvasRef = useRef<HTMLCanvasElement>(null);
  const [box, setBox] = useState<Box | null>(null);
  const [ok, setOk] = useState(false);
  const [failed, setFailed] = useState(false);
  const shouldDrawLandmarks = drawLandmarks ?? drawBox;
  const onFramingRef = useRef(onFraming);
  useEffect(() => {
    onFramingRef.current = onFraming;
  }, [onFraming]);

  useEffect(() => {
    if (!active) {
      clearLandmarks(landmarkCanvasRef.current);
      onFramingRef.current?.(NONE);
      return;
    }

    let cancelled = false;
    let raf = 0;
    let landmarker: FaceLandmarker | null = null;
    const canvasAtEffectStart = landmarkCanvasRef.current;
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
        clearLandmarks(landmarkCanvasRef.current);
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
      const pose = estimatePose(lm as LMPoint[]); // hướng mặt realtime cho đăng ký

      let framing: Omit<Framing, "blinkCount" | "motion">;
      if (sizeRatio < FACE_MIN_RATIO) framing = { state: "far", hint: "Lại gần hơn", ok: false, ...pose };
      else if (sizeRatio > FACE_MAX_RATIO) framing = { state: "near", hint: "Lùi ra một chút", ok: false, ...pose };
      else if (Math.abs(cx) > OFFCENTER || Math.abs(cy) > OFFCENTER)
        framing = { state: "offcenter", hint: "Đưa khuôn mặt vào giữa khung", ok: false, ...pose };
      else framing = { state: "good", hint: "Giữ yên — đã vào khung", ok: true, ...pose };

      if (shouldDrawLandmarks && landmarkCanvasRef.current) {
        drawLandmarkMesh(landmarkCanvasRef.current, lm as LMPoint[], vw, vh, W, H, framing.ok);
      }

      setBox({ left, top, width, height });
      setOk(framing.ok);
      emit(framing);
    };

    getLandmarker()
      .then((d) => {
        if (cancelled) return;
        setFailed(false);
        landmarker = d;
        loop();
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      });

    return () => {
      cancelled = true;
      cancelAnimationFrame(raf);
      clearLandmarks(canvasAtEffectStart);
    };
  }, [active, shouldDrawLandmarks, videoRef]);

  // Không nạp được model → không hiện khung gì (server vẫn phát hiện & nhận diện như cũ).
  if (failed) return null;

  return (
    <div ref={rootRef} className="cc-face-overlay" aria-hidden="true">
      {shouldDrawLandmarks && <canvas ref={landmarkCanvasRef} className="cc-face-landmarks" />}
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
