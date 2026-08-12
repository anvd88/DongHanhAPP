import { useCallback, useEffect, useRef, useState } from "react";
import { Camera, CameraOff, CheckCircle2, Loader2, Play, RotateCcw, ScanLine } from "lucide-react";
import { api } from "../../lib/api";
import { useCamera } from "./useCamera";
import { FaceTrackingOverlay, type Framing } from "./FaceTrackingOverlay";

/**
 * Đăng ký khuôn mặt theo THỜI GIAN THỰC: bám hướng mặt liên tục bằng MediaPipe ngay trên trình
 * duyệt (KHÔNG còn chụp loạt ảnh rồi hỏi server từng khung). Mỗi bước nhắc 1 tư thế; khi người dùng
 * GIỮ ĐÚNG góc đủ ổn định, hệ thống tự chụp ĐÚNG MỘT khung và gửi /dangky để lưu mẫu. Hướng mặt được
 * tính HOÀN TOÀN client-side (xem estimatePose trong FaceTrackingOverlay) — server không còn endpoint
 * ước lượng tư thế nào nữa, /huongmat đã gỡ vì không ai gọi.
 */
type Dir = "center" | "left" | "right" | "up" | "down";
type Step = { dir: Dir; label: string };

const STEPS: Step[] = [
  { dir: "center", label: "Nhìn thẳng vào camera" },
  { dir: "left", label: "Quay mặt sang trái một chút" },
  { dir: "right", label: "Quay mặt sang phải một chút" },
  { dir: "up", label: "Ngẩng mặt nhẹ lên" },
  { dir: "down", label: "Cúi mặt nhẹ xuống" },
];

// Ngưỡng (tỉ lệ hình học landmark, không phải độ) — cùng thang với FacePose phía máy chủ
// (AdaFaceR50Engine.PoseFrom), vì server vẫn dùng thang đó để chấm tư thế khi lưu mẫu.
const YAW_FRONTAL = 0.13; // |yaw| dưới mức này coi là chính diện
const YAW_DELTA = 0.14; // mức quay trái/phải tối thiểu so với chính diện
const PITCH_DELTA = 0.07; // mức ngước/cúi tối thiểu so với chính diện
// Giữ ổn định khoảng 0,7 giây trước khi chụp để tránh lưu khung đang lia mặt hoặc bị nhòe.
const HOLD_FRAMES = 14;
const PAUSE_AFTER_SAVE_MS = 1300;

export function EnrollWizard({
  username,
  fullName,
  onSaved,
}: {
  username: string;
  fullName: string;
  onSaved: () => void;
}) {
  const { videoRef, active, error, start: startCam, stop: stopCam, capture } = useCamera();

  const [running, setRunning] = useState(false);
  const [done, setDone] = useState(false);
  const [step, setStep] = useState(0);
  const [busy, setBusy] = useState(false);
  const [paused, setPaused] = useState(false);
  const [hint, setHint] = useState("");
  const [clearFirst, setClearFirst] = useState(true);

  // Refs cho vòng lặp realtime (callback từ rAF không thấy state mới nhất).
  const stepRef = useRef(0);
  const baseRef = useRef<{ yaw: number; pitch: number } | null>(null);
  const holdRef = useRef(0);
  const runningRef = useRef(false);
  const doneRef = useRef(false);
  const pausedRef = useRef(false);
  const savingRef = useRef(false);
  const pauseTimer = useRef<ReturnType<typeof setTimeout>>(undefined);

  useEffect(() => () => clearTimeout(pauseTimer.current), []);

  const resetState = useCallback(() => {
    clearTimeout(pauseTimer.current);
    stepRef.current = 0;
    baseRef.current = null;
    holdRef.current = 0;
    runningRef.current = false;
    doneRef.current = false;
    pausedRef.current = false;
    savingRef.current = false;
    setRunning(false);
    setDone(false);
    setPaused(false);
    setStep(0);
    setHint("");
  }, []);

  // Đổi nhân viên → hủy phiên đang chạy.
  useEffect(() => {
    resetState();
  }, [username, resetState]);

  const start = async () => {
    if (!username || !active) return;
    if (clearFirst) {
      try {
        await api.del(`/api/chamcong/dangky/${encodeURIComponent(username)}`);
      } catch {
        /* chưa có mẫu cũ */
      }
    }
    resetState();
    runningRef.current = true;
    setRunning(true);
  };

  const matches = (dir: Dir, yaw: number, pitch: number): boolean => {
    const base = baseRef.current;
    if (dir === "center") return Math.abs(yaw) < YAW_FRONTAL;
    if (!base) return true; // chưa có baseline (hiếm) → chấp nhận
    if (dir === "left") return yaw - base.yaw > YAW_DELTA;
    if (dir === "right") return base.yaw - yaw > YAW_DELTA;
    if (dir === "up") return base.pitch - pitch > PITCH_DELTA;
    return pitch - base.pitch > PITCH_DELTA; // down
  };

  const guide = (dir: Dir): string => {
    if (dir === "left") return "Quay thêm sang trái…";
    if (dir === "right") return "Quay thêm sang phải…";
    if (dir === "up") return "Ngước lên thêm…";
    if (dir === "down") return "Cúi xuống thêm…";
    return "Nhìn thẳng vào camera…";
  };

  // Lưu 1 mẫu cho tư thế hiện tại (chụp đúng 1 khung). Bọc savingRef để không gửi trùng.
  const captureAndSave = useCallback(
    async (cur: Step, yaw: number, pitch: number) => {
      savingRef.current = true;
      setBusy(true);
      try {
        const image = capture();
        if (!image) {
          setHint("Không chụp được khung — thử lại");
          return;
        }
        await api.post("/api/chamcong/dangky", { username, fullName, imageBase64: image });
        if (cur.dir === "center" && baseRef.current === null) {
          baseRef.current = { yaw, pitch };
        }
        const next = stepRef.current + 1;
        if (next >= STEPS.length) {
          stepRef.current = STEPS.length;
          runningRef.current = false;
          doneRef.current = true;
          setStep(STEPS.length);
          setRunning(false);
          setDone(true);
          onSaved();
        } else {
          stepRef.current = next;
          setStep(next);
          // Tạm dừng để người dùng chuyển sang tư thế kế tiếp (khỏi chụp dồn).
          pausedRef.current = true;
          setPaused(true);
          clearTimeout(pauseTimer.current);
          pauseTimer.current = setTimeout(() => {
            pausedRef.current = false;
            setPaused(false);
          }, PAUSE_AFTER_SAVE_MS);
        }
      } catch {
        setHint("Lưu mẫu thất bại — thử lại");
      } finally {
        savingRef.current = false;
        setBusy(false);
      }
    },
    [capture, username, fullName, onSaved],
  );

  // Nhận tín hiệu hướng mặt realtime từ overlay (≈20 lần/giây).
  const onFraming = useCallback(
    (f: Framing) => {
      if (!runningRef.current || doneRef.current || savingRef.current || pausedRef.current) return;

      if (!f.hasPose || f.state === "none") {
        holdRef.current = 0;
        setHint("Chưa thấy khuôn mặt — đưa mặt vào khung");
        return;
      }
      // Căn khung chưa đạt (xa/gần/lệch tâm) → hướng dẫn căn khung trước khi xét góc.
      if (!f.ok) {
        holdRef.current = 0;
        setHint(f.hint);
        return;
      }

      const cur = STEPS[stepRef.current];
      if (matches(cur.dir, f.yaw, f.pitch)) {
        holdRef.current += 1;
        if (holdRef.current >= HOLD_FRAMES) {
          holdRef.current = 0;
          void captureAndSave(cur, f.yaw, f.pitch);
        } else {
          setHint("Đúng góc — giữ yên…");
        }
      } else {
        holdRef.current = 0;
        setHint(guide(cur.dir));
      }
    },
    [captureAndSave],
  );

  const progress = Math.round((step / STEPS.length) * 100);
  const warn = hint.startsWith("Chưa thấy");
  const scanHint = busy ? "Đang lưu mẫu…" : hint || STEPS[step]?.label || "Đưa mặt đúng góc để tự chụp";

  return (
    <div className="cc-grid">
      <div className="space-y-3">
        <div className="cc-camera glass">
          <div className="cc-video-wrap">
            <video ref={videoRef} playsInline muted className="cc-video" data-active={active} />
            {!active && (
              <div className="cc-video-empty">
                <Camera className="h-8 w-8" />
                <span>Camera đang tắt</span>
              </div>
            )}
            <span className="cc-frame-guide" aria-hidden="true" />
            {active && <FaceTrackingOverlay videoRef={videoRef} active={active} onFraming={onFraming} />}
            {running && !done && (
              <div className="cc-scan-hint">
                {busy ? (
                  <>
                    <Loader2 className="h-3.5 w-3.5 animate-spin" /> {scanHint}
                  </>
                ) : paused ? null : (
                  <>
                    <ScanLine className="h-3.5 w-3.5" /> {scanHint}
                  </>
                )}
              </div>
            )}
          </div>

          {error && <div className="cc-error">{error}</div>}

          <div className="cc-camera-actions">
            {!active ? (
              <button className="cc-btn cc-btn-primary" onClick={startCam} type="button">
                <Camera className="h-4 w-4" /> Bật camera
              </button>
            ) : (
              <button className="cc-btn" onClick={stopCam} type="button">
                <CameraOff className="h-4 w-4" /> Tắt
              </button>
            )}
            {active && !running && (
              <button className="cc-btn cc-btn-primary" onClick={start} type="button" disabled={!username}>
                <Play className="h-4 w-4" /> {done ? "Quét lại" : "Bắt đầu quét tự động"}
              </button>
            )}
            {running && (
              <button className="cc-btn" onClick={resetState} type="button">
                <RotateCcw className="h-4 w-4" /> Dừng
              </button>
            )}
          </div>
        </div>

        <label className="cc-auto-toggle">
          <input type="checkbox" checked={clearFirst} onChange={(e) => setClearFirst(e.target.checked)} />
          Xóa mẫu cũ trước khi quét lại
        </label>
      </div>

      <div className="cc-result glass">
        {done ? (
          <div className="cc-result-ok">
            <CheckCircle2 className="h-12 w-12" />
            <div className="cc-result-name">Đã lưu {STEPS.length} mẫu khuôn mặt</div>
            <div className="cc-result-meta">{fullName || username}</div>
          </div>
        ) : running ? (
          <div className="cc-enroll">
            <div className="cc-enroll-count">
              Mẫu {Math.min(step + 1, STEPS.length)}/{STEPS.length}
            </div>
            <div className="cc-enroll-step">{STEPS[step]?.label ?? ""}</div>
            <div className="cc-enroll-bar">
              <span style={{ width: `${progress}%` }} />
            </div>
            <div className="cc-enroll-hint" data-warn={warn}>
              {paused ? "Đã lưu ✓ — chuẩn bị tư thế tiếp theo…" : hint || "Đưa mặt đúng góc, hệ thống sẽ tự chụp…"}
            </div>
          </div>
        ) : (
          <div className="cc-result-empty">
            <p>
              {!username
                ? "Hãy chọn nhân viên trước."
                : !active
                  ? "Bật camera, rồi bấm “Bắt đầu quét tự động”."
                  : "Bấm “Bắt đầu quét tự động” và làm theo hướng dẫn."}
            </p>
          </div>
        )}
      </div>
    </div>
  );
}
