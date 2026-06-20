import { useCallback, useEffect, useRef, useState } from "react";
import { CheckCircle2, Play, RotateCcw } from "lucide-react";
import { api } from "../../lib/api";
import { CameraPanel } from "./CameraPanel";

/**
 * Quét khuôn mặt TỰ ĐỘNG có hướng dẫn + KIỂM TRA HƯỚNG MẶT để lấy dữ liệu đăng ký.
 * Mỗi bước nhắc 1 tư thế; chỉ lưu mẫu khi hướng mặt khớp tư thế (gọi /huongmat để đo,
 * /dangky để lưu). Hướng được tính từ 5 điểm landmark phía server (SeetaFace).
 */
type Dir = "center" | "left" | "right" | "up" | "down";
type Step = { dir: Dir; label: string };
type Pose = { found: boolean; yaw: number; pitch: number };

const STEPS: Step[] = [
  { dir: "center", label: "Nhìn thẳng vào camera" },
  { dir: "left", label: "Quay mặt sang trái một chút" },
  { dir: "right", label: "Quay mặt sang phải một chút" },
];

// Ngưỡng (tỉ lệ hình học landmark, không phải độ) — chỉnh nếu thấy quá dễ/khó.
const YAW_FRONTAL = 0.13; // |yaw| dưới mức này coi là chính diện
const YAW_DELTA = 0.14; // mức quay trái/phải tối thiểu so với chính diện
const PITCH_DELTA = 0.07; // mức ngước/cúi tối thiểu so với chính diện
const INTERVAL = 700;

export function EnrollWizard({
  username,
  fullName,
  onSaved,
}: {
  username: string;
  fullName: string;
  onSaved: () => void;
}) {
  const [running, setRunning] = useState(false);
  const [done, setDone] = useState(false);
  const [step, setStep] = useState(0);
  const [busy, setBusy] = useState(false);
  const [paused, setPaused] = useState(false);
  const [hint, setHint] = useState("");
  const [clearFirst, setClearFirst] = useState(true);
  const [cameraActive, setCameraActive] = useState(false);

  const stepRef = useRef(0);
  const baseRef = useRef<{ yaw: number; pitch: number } | null>(null);
  const pauseTimer = useRef<ReturnType<typeof setTimeout>>(undefined);

  useEffect(() => () => clearTimeout(pauseTimer.current), []);

  // Đổi nhân viên → hủy phiên đang chạy.
  useEffect(() => {
    setRunning(false);
    setDone(false);
    setStep(0);
    setHint("");
    stepRef.current = 0;
    baseRef.current = null;
  }, [username]);

  const start = async () => {
    if (!username) return;
    if (clearFirst) {
      try {
        await api.del(`/api/chamcong/dangky/${encodeURIComponent(username)}`);
      } catch {
        /* chưa có mẫu cũ */
      }
    }
    stepRef.current = 0;
    baseRef.current = null;
    setStep(0);
    setDone(false);
    setPaused(false);
    setHint("");
    setRunning(true);
  };

  const reset = () => {
    clearTimeout(pauseTimer.current);
    setRunning(false);
    setDone(false);
    setPaused(false);
    setStep(0);
    setHint("");
    stepRef.current = 0;
    baseRef.current = null;
  };

  const matches = (dir: Dir, pose: Pose): boolean => {
    const base = baseRef.current;
    if (dir === "center") return Math.abs(pose.yaw) < YAW_FRONTAL;
    if (!base) return true; // chưa có baseline (hiếm) → chấp nhận
    if (dir === "left") return pose.yaw - base.yaw > YAW_DELTA;
    if (dir === "right") return base.yaw - pose.yaw > YAW_DELTA;
    if (dir === "up") return base.pitch - pose.pitch > PITCH_DELTA;
    return pose.pitch - base.pitch > PITCH_DELTA; // down
  };

  const guide = (dir: Dir): string => {
    if (dir === "left") return "Quay thêm sang trái…";
    if (dir === "right") return "Quay thêm sang phải…";
    if (dir === "up") return "Ngước lên thêm…";
    if (dir === "down") return "Cúi xuống thêm…";
    return "Nhìn thẳng vào camera…";
  };

  const onFrame = useCallback(
    async (image: string) => {
      setBusy(true);
      try {
        const pose = await api.post<Pose>("/api/chamcong/huongmat", { imageBase64: image });
        if (!pose.found) {
          setHint("Chưa thấy khuôn mặt — đưa mặt vào khung");
          return;
        }

        const cur = STEPS[stepRef.current];
        if (matches(cur.dir, pose)) {
          setHint("Đúng góc — đang chụp và lưu mẫu…");
          await api.post("/api/chamcong/dangky", { username, fullName, imageBase64: image });
          if (cur.dir === "center" && baseRef.current === null) {
            baseRef.current = { yaw: pose.yaw, pitch: pose.pitch };
          }
          const next = stepRef.current + 1;
          if (next >= STEPS.length) {
            stepRef.current = STEPS.length;
            setStep(STEPS.length);
            setRunning(false);
            setDone(true);
            onSaved();
          } else {
            stepRef.current = next;
            setStep(next);
            setPaused(true);
            clearTimeout(pauseTimer.current);
            pauseTimer.current = setTimeout(() => {
              setPaused(false);
            }, 1300);
          }
        } else {
          setHint(guide(cur.dir));
        }
      } catch {
        setHint("Chưa thấy khuôn mặt — đưa mặt vào khung");
      } finally {
        setBusy(false);
      }
    },
    [username, fullName, onSaved],
  );

  const progress = Math.round((step / STEPS.length) * 100);
  const warn = hint.startsWith("Chưa thấy");

  return (
    <div className="cc-grid">
      <div className="space-y-3">
        <CameraPanel
          onCapture={onFrame}
          busy={busy}
          auto={running && !done}
          paused={paused}
          intervalMs={INTERVAL}
          captureLabel="Chụp mẫu"
          autoHint={hint || STEPS[step]?.label || "Đưa mặt đúng góc để tự chụp"}
          autoBusyLabel="Đang kiểm tra góc…"
          onActiveChange={setCameraActive}
        />
        <label className="cc-auto-toggle">
          <input type="checkbox" checked={clearFirst} onChange={(e) => setClearFirst(e.target.checked)} />
          Xóa mẫu cũ trước khi quét lại
        </label>
        <div className="cc-camera-actions">
          {!running && (
            <button
              className="cc-btn cc-btn-primary"
              onClick={start}
              type="button"
              disabled={!username || !cameraActive}
            >
              <Play className="h-4 w-4" /> {done ? "Quét lại" : "Bắt đầu quét tự động"}
            </button>
          )}
          {running && (
            <button className="cc-btn" onClick={reset} type="button">
              <RotateCcw className="h-4 w-4" /> Dừng
            </button>
          )}
        </div>
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
              {paused ? "Đã chụp ✓ — chuẩn bị tư thế tiếp theo…" : hint || "Đưa mặt đúng góc, hệ thống sẽ tự chụp…"}
            </div>
          </div>
        ) : (
          <div className="cc-result-empty">
            <p>
              {!username
                ? "Hãy chọn nhân viên trước."
                : !cameraActive
                  ? "Bật camera, rồi bấm “Bắt đầu quét tự động”."
                  : "Bấm “Bắt đầu quét tự động” và làm theo hướng dẫn."}
            </p>
          </div>
        )}
      </div>
    </div>
  );
}
