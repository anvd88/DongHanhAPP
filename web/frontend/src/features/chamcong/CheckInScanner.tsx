import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { AnimatePresence, motion } from "framer-motion";
import { Camera, CheckCircle2, Loader2, ScanFace, XCircle } from "lucide-react";
import { api } from "../../lib/api";
import type { NhanDienResult } from "../../lib/types";
import { CameraPanel } from "./CameraPanel";
import { useCamera } from "./useCamera";

type CheckInPopup = {
  name: string;
  loai?: string;
  occurredAt?: string;
};

type FacePose = {
  found: boolean;
  yaw: number;
  pitch: number;
};

type CheckInScannerProps = {
  returnToLoginOnOk?: boolean;
  biometricMode?: boolean;
};

const BIOMETRIC_YAW_FRONTAL = 0.14;
const BIOMETRIC_PITCH_MIN = 0.25;
const BIOMETRIC_PITCH_MAX = 0.82;

/**
 * Khối quét chấm công dùng chung cho tab "Chấm công" trong app và màn hình kiosk
 * ngoài trang đăng nhập. Kiosk có thể dùng giao diện quét khuôn mặt ẩn camera.
 */
export function CheckInScanner({ returnToLoginOnOk = false, biometricMode = false }: CheckInScannerProps) {
  return biometricMode ? (
    <BiometricCheckInScanner />
  ) : (
    <ClassicCheckInScanner returnToLoginOnOk={returnToLoginOnOk} />
  );
}
function ClassicCheckInScanner({ returnToLoginOnOk }: Pick<CheckInScannerProps, "returnToLoginOnOk">) {
  const navigate = useNavigate();
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<NhanDienResult | null>(null);
  const [popup, setPopup] = useState<CheckInPopup | null>(null);
  const [auto, setAuto] = useState(true);
  const [cooldown, setCooldown] = useState(false);
  const [stopCameraSignal, setStopCameraSignal] = useState(0);
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined);

  const recognize = useCallback(async (image: string) => {
    setBusy(true);
    try {
      const res = await api.post<NhanDienResult>("/api/chamcong/nhandien", { imageBase64: image });
      setResult(res);
      clearTimeout(timer.current);
      if (res.matched) {
        setPopup({
          name: res.fullName || res.username || "Nhân viên",
          loai: res.loai,
          occurredAt: res.occurredAt,
        });
        setStopCameraSignal((value) => value + 1);

        setCooldown(true);
        timer.current = setTimeout(() => {
          setCooldown(false);
          setResult(null);
        }, 4000);
      } else {
        timer.current = setTimeout(() => setResult(null), 2500);
      }
    } catch (e) {
      setResult({ matched: false, similarity: 0, message: e instanceof Error ? e.message : "Lỗi nhận diện." });
    } finally {
      setBusy(false);
    }
  }, []);

  useEffect(
    () => () => {
      clearTimeout(timer.current);
    },
    [],
  );

  const closePopup = () => {
    setPopup(null);
    setCooldown(false);
    setResult(null);
    if (returnToLoginOnOk) navigate("/login", { replace: true });
  };

  return (
    <div className="cc-grid">
      <CheckInSuccessPopup popup={popup} onOk={closePopup} />

      <div className="space-y-3">
        <CameraPanel
          onCapture={recognize}
          busy={busy}
          auto={auto}
          paused={cooldown}
          stopSignal={stopCameraSignal}
          captureLabel="Chụp & chấm công"
        />
        <label className="cc-auto-toggle">
          <input type="checkbox" checked={auto} onChange={(e) => setAuto(e.target.checked)} />
          Tự động chụp khi có người trước camera
        </label>
      </div>

      <div className="cc-result glass">
        {!result ? (
          <div className="cc-result-empty">
            <ScanFace className="h-10 w-10" />
            <p>
              {auto
                ? "Bật camera — hệ thống tự nhận diện khi bạn nhìn vào."
                : "Bật camera và bấm “Chụp & chấm công”."}
            </p>
          </div>
        ) : result.matched ? (
          <div className="cc-result-ok">
            <CheckCircle2 className="h-12 w-12" />
            <div className="cc-result-name">{result.fullName || result.username}</div>
            <div className="cc-result-badge" data-loai={result.loai}>{result.loai}</div>
            <div className="cc-result-meta">Độ khớp {(result.similarity * 100).toFixed(1)}%</div>
          </div>
        ) : (
          <div className="cc-result-fail">
            <XCircle className="h-12 w-12" />
            <p>{result.message}</p>
          </div>
        )}
      </div>
    </div>
  );
}

function BiometricCheckInScanner() {
  const { videoRef, active, error, start, stop, capture } = useCamera();
  const [busy, setBusy] = useState(false);
  const [phase, setPhase] = useState<"idle" | "opening" | "scanning" | "checking" | "warning" | "success">("idle");
  const [hint, setHint] = useState("Sẵn sàng ghi nhận công");
  const [lastCheckIn, setLastCheckIn] = useState<CheckInPopup | null>(null);
  const busyRef = useRef(false);

  const scan = useCallback(async () => {
    if (busyRef.current || lastCheckIn) return;

    const image = capture();
    if (!image) {
      if (active) {
        if (phase !== "warning") {
          setPhase("scanning");
          setHint("Đang chờ camera ổn định...");
        }
      } else {
        setPhase("idle");
        setHint("Bấm bắt đầu để mở camera");
      }
      return;
    }

    busyRef.current = true;
    setBusy(true);

    try {
      const pose = await api.post<FacePose>("/api/chamcong/huongmat", { imageBase64: image });
      if (!pose.found) {
        setPhase("warning");
        setHint("Đưa khuôn mặt vào vùng quét");
        return;
      }
      if (Math.abs(pose.yaw) > BIOMETRIC_YAW_FRONTAL) {
        setPhase("warning");
        setHint("Nhìn thẳng vào vùng quét");
        return;
      }
      if (pose.pitch < BIOMETRIC_PITCH_MIN) {
        setPhase("warning");
        setHint("Hạ mặt xuống một chút");
        return;
      }
      if (pose.pitch > BIOMETRIC_PITCH_MAX) {
        setPhase("warning");
        setHint("Ngước mặt lên một chút");
        return;
      }

      setPhase("scanning");
      setHint("Đang nhận diện...");
      const res = await api.post<NhanDienResult>("/api/chamcong/nhandien", { imageBase64: image });
      if (!res.matched) {
        setPhase("warning");
        setHint(res.message || "Chưa nhận diện được. Vui lòng thử lại.");
        return;
      }

      const name = res.fullName || res.username || "Nhân viên";
      setPhase("success");
      setHint(`${name} đã chấm công`);
      setLastCheckIn({
        name,
        loai: res.loai,
        occurredAt: res.occurredAt,
      });
      stop();
    } catch (e) {
      setPhase("warning");
      setHint(e instanceof Error ? e.message : "Không nhận diện được. Vui lòng thử lại.");
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  }, [active, capture, lastCheckIn, phase, stop]);

  const begin = async () => {
    setLastCheckIn(null);
    busyRef.current = false;
    setBusy(false);
    setPhase("opening");
    setHint("Đang mở camera...");
    await start();
    setPhase("scanning");
    setHint("Nhìn thẳng vào vùng quét");
  };

  useEffect(() => {
    if (!active || lastCheckIn || phase === "success") return;
    const id = window.setInterval(scan, 900);
    return () => window.clearInterval(id);
  }, [active, phase, lastCheckIn, scan]);

  useEffect(
    () => () => {
      busyRef.current = false;
    },
    [],
  );

  const lastCheckInTime = lastCheckIn?.occurredAt ? new Date(lastCheckIn.occurredAt).toLocaleTimeString("vi-VN") : null;

  return (
    <div className="cc-bio-shell">
      <video ref={videoRef} playsInline muted className="cc-bio-video" aria-hidden="true" />

      <motion.div
        className="cc-bio-island"
        data-phase={phase}
        animate={{
          scale: phase === "success" ? 0.985 : 1,
        }}
        transition={{ type: "spring", stiffness: 260, damping: 24 }}
      >
        <motion.div
          className="cc-bio-mark"
          data-phase={phase}
          animate={{ scale: busy && phase !== "success" ? [1, 1.06, 1] : phase === "success" ? 1.12 : 1 }}
          transition={{ repeat: busy && phase !== "success" ? Infinity : 0, duration: 1.1 }}
        >
          <span className="cc-bio-corner cc-bio-corner--tl" />
          <span className="cc-bio-corner cc-bio-corner--tr" />
          <span className="cc-bio-corner cc-bio-corner--bl" />
          <span className="cc-bio-corner cc-bio-corner--br" />
          <AnimatePresence mode="wait">
            {phase === "success" ? (
              <motion.span
                key="check"
                className="cc-bio-check"
                initial={{ opacity: 0, scale: 0.65 }}
                animate={{ opacity: 1, scale: 1 }}
                exit={{ opacity: 0, scale: 0.65 }}
              >
                <CheckCircle2 className="h-8 w-8" />
              </motion.span>
            ) : (
              <motion.span
                key="scan"
                className="cc-bio-scanline"
                initial={{ opacity: 0 }}
                animate={{ opacity: active ? 1 : 0.55 }}
                exit={{ opacity: 0 }}
              />
            )}
          </AnimatePresence>
        </motion.div>

        <div className="cc-bio-copy">
          <div className="cc-bio-title">
            {phase === "success" ? "Chấm công thành công" : "Chấm công khuôn mặt"}
          </div>
          {phase === "success" && lastCheckIn ? (
            <div className="cc-bio-success">
              <div className="cc-bio-success-name">{lastCheckIn.name} đã chấm công</div>
              <div className="cc-bio-success-meta">
                {[lastCheckIn.loai, lastCheckInTime].filter(Boolean).join(" · ")}
              </div>
            </div>
          ) : (
            <div className="cc-bio-hint" data-warn={phase === "warning"}>
              {hint}
            </div>
          )}
        </div>

        {phase !== "success" && !active && (
          <button className="cc-bio-start" onClick={begin} type="button">
            <Camera className="h-4 w-4" /> Bắt đầu quét
          </button>
        )}
        {phase !== "success" && active && (
          <div className="cc-bio-active-pill">
            <Loader2 className="h-4 w-4 animate-spin" /> Đang quét
          </div>
        )}
        {phase === "success" && (
          <button className="cc-bio-start cc-bio-start--secondary" onClick={begin} type="button">
            <Camera className="h-4 w-4" /> Quét tiếp
          </button>
        )}
      </motion.div>

      {error && <div className="cc-bio-note" data-warn="true">{error}</div>}
    </div>
  );
}

function CheckInSuccessPopup({ popup, onOk }: { popup: CheckInPopup | null; onOk: () => void }) {
  const popupTime = popup?.occurredAt ? new Date(popup.occurredAt).toLocaleTimeString("vi-VN") : null;

  if (!popup) return null;
  return (
    <div className="cc-checkin-popup-backdrop" role="presentation">
      <div className="cc-checkin-popup" role="dialog" aria-modal="true" aria-labelledby="cc-checkin-popup-title">
        <CheckCircle2 className="h-12 w-12" />
        <div>
          <div id="cc-checkin-popup-title" className="cc-checkin-popup-title">
            {popup.name} đã chấm công
          </div>
          <div className="cc-checkin-popup-meta">
            {[popup.loai, popupTime].filter(Boolean).join(" · ")}
          </div>
        </div>
        <button className="cc-btn cc-btn-primary" onClick={onOk} type="button" autoFocus>
          OK
        </button>
      </div>
    </div>
  );
}
