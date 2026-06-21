import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { AnimatePresence, motion, useReducedMotion } from "framer-motion";
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
  /** Tự bắt đầu quét ngay khi gắn (dùng cho kiosk khi đã bấm nút bên ngoài). */
  autoStart?: boolean;
};

type FaceScanAnimationStatus = "idle" | "starting" | "scanning" | "success" | "error";

const BIOMETRIC_YAW_FRONTAL = 0.14;
const BIOMETRIC_PITCH_MIN = 0.25;
const BIOMETRIC_PITCH_MAX = 0.82;
const START_SCAN_ANIMATION_MS = 460;
const wait = (ms: number) => new Promise<void>((resolve) => window.setTimeout(resolve, ms));

/**
 * Khối quét chấm công dùng chung cho tab "Chấm công" trong app và màn hình kiosk
 * ngoài trang đăng nhập. Kiosk có thể dùng giao diện quét khuôn mặt ẩn camera.
 */
export function CheckInScanner({ returnToLoginOnOk = false, biometricMode = false, autoStart = false }: CheckInScannerProps) {
  return biometricMode ? (
    <BiometricCheckInScanner autoStart={autoStart} />
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

function BiometricCheckInScanner({ autoStart = false }: { autoStart?: boolean }) {
  const { videoRef, active, error, start, stop, capture } = useCamera();
  const [phase, setPhase] = useState<"idle" | "opening" | "scanning" | "checking" | "warning" | "success">("idle");
  const [hint, setHint] = useState("Sẵn sàng ghi nhận công");
  const [lastCheckIn, setLastCheckIn] = useState<CheckInPopup | null>(null);
  const busyRef = useRef(false);
  const demoTimer = useRef<ReturnType<typeof setTimeout>>(undefined);
  const demoMode = typeof window !== "undefined" && new URLSearchParams(window.location.search).get("demoFaceScan") === "1";

  const scan = useCallback(async () => {
    if (demoMode || busyRef.current || lastCheckIn) return;

    const image = capture();
    if (!image) {
      if (active || phase === "scanning" || phase === "checking") {
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
    }
  }, [active, capture, demoMode, lastCheckIn, phase, stop]);

  const begin = async () => {
    clearTimeout(demoTimer.current);
    setLastCheckIn(null);
    busyRef.current = false;
    setPhase("opening");
    setHint("Đang chuẩn bị quét...");
    if (demoMode) {
      demoTimer.current = setTimeout(() => {
        setPhase("scanning");
        setHint("Đang chạy thử hoạt ảnh quét...");
        demoTimer.current = setTimeout(() => {
          setPhase("success");
          setHint("Nguyễn Văn A đã chấm công");
          setLastCheckIn({
            name: "Nguyễn Văn A",
            loai: "Demo",
            occurredAt: new Date().toISOString(),
          });
        }, 1700);
      }, START_SCAN_ANIMATION_MS);
      return;
    }
    await Promise.all([start(), wait(START_SCAN_ANIMATION_MS)]);
    setPhase("scanning");
    setHint("Nhìn thẳng vào vùng quét");
  };

  const autoStartedRef = useRef(false);
  useEffect(() => {
    if (autoStart && !autoStartedRef.current) {
      autoStartedRef.current = true;
      void begin();
    }
    // begin được tạo mới mỗi render; chỉ chạy 1 lần khi gắn nên không đưa vào deps.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoStart]);

  useEffect(() => {
    if (!active || lastCheckIn || phase === "success") return;
    const id = window.setInterval(scan, 900);
    return () => window.clearInterval(id);
  }, [active, phase, lastCheckIn, scan]);

  useEffect(
    () => () => {
      clearTimeout(demoTimer.current);
      busyRef.current = false;
    },
    [],
  );

  const lastCheckInTime = lastCheckIn?.occurredAt ? new Date(lastCheckIn.occurredAt).toLocaleTimeString("vi-VN") : null;
  const scanStatus: FaceScanAnimationStatus =
    phase === "success"
      ? "success"
      : phase === "warning"
        ? "error"
        : phase === "opening"
          ? "starting"
          : phase === "scanning" || phase === "checking" || active
            ? "scanning"
            : "idle";
  const scanRunning = active || phase === "opening" || phase === "scanning" || phase === "checking";

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
        <FaceScanAnimation status={scanStatus} />

        <div className="cc-bio-copy">
          <div className="cc-bio-title">
            {phase === "success" ? "Chấm công thành công" : "Chấm công khuôn mặt"}
          </div>
          {phase === "success" && lastCheckIn ? (
            <motion.div
              className="cc-bio-success"
              initial={{ opacity: 0, y: 6 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.24, ease: "easeOut", delay: 0.08 }}
            >
              <div className="cc-bio-success-name">{lastCheckIn.name} đã chấm công</div>
              <div className="cc-bio-success-meta">
                {[lastCheckIn.loai, lastCheckInTime].filter(Boolean).join(" · ")}
              </div>
            </motion.div>
          ) : (
            <div className="cc-bio-hint" data-warn={phase === "warning"}>
              {hint}
            </div>
          )}
        </div>

        {phase !== "success" && !scanRunning && (
          <button className="cc-bio-start" onClick={begin} type="button">
            <Camera className="h-4 w-4" /> Bắt đầu quét
          </button>
        )}
        {phase !== "success" && scanRunning && (
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

function FaceScanAnimation({
  status,
  size = "clamp(5.6rem, 24vw, 6.8rem)",
  className = "",
}: {
  status: FaceScanAnimationStatus;
  size?: number | string;
  className?: string;
}) {
  const reducedMotion = useReducedMotion();
  const isStarting = status === "starting";
  const isScanning = status === "scanning" || status === "error";
  const isSuccess = status === "success";
  const cornerColor = status === "error" ? "#b45309" : isSuccess ? "#16a34a" : "currentColor";
  const glowColor = status === "error" ? "rgba(245, 158, 11, 0.14)" : isSuccess ? "rgba(34, 197, 94, 0.18)" : "rgba(37, 99, 235, 0.16)";

  return (
    <motion.div
      className={`cc-bio-mark ${className}`}
      data-status={status}
      style={{ width: size, height: size }}
      initial={false}
      animate={
        reducedMotion
          ? { opacity: 1 }
          : isSuccess
            ? { scale: [1, 0.94, 1.06, 1], borderRadius: "50%" }
            : isStarting
              ? { scale: [0.78, 1.06, 1], borderRadius: ["999px", "30%", "24%"] }
            : isScanning
              ? { scale: [1, 1.025, 1], opacity: [0.9, 1, 0.92], borderRadius: "28%" }
              : { scale: [0.96, 1.02, 1], opacity: 0.96, borderRadius: "28%" }
      }
      transition={
        reducedMotion
          ? { duration: 0.12 }
          : isSuccess
            ? { duration: 0.68, times: [0, 0.18, 0.58, 1], ease: "easeInOut" }
            : isStarting
              ? { duration: 0.42, times: [0, 0.58, 1], ease: "easeOut" }
            : isScanning
              ? { duration: 1.28, repeat: Infinity, ease: "easeInOut" }
              : { duration: 0.28, ease: "easeOut" }
      }
    >
      <AnimatePresence>
        {isStarting && !reducedMotion && (
          <motion.div
            key="start-burst"
            className="cc-bio-start-burst"
            initial={{ opacity: 0, scaleX: 0.38, scaleY: 0.72 }}
            animate={{ opacity: [0, 0.82, 0], scaleX: [0.38, 1.18, 1.3], scaleY: [0.72, 0.94, 1.02] }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.42, ease: "easeOut" }}
          />
        )}
      </AnimatePresence>
      <motion.div
        className="cc-bio-mark-glow"
        animate={
          reducedMotion
            ? { opacity: isSuccess ? 0.4 : 0.16 }
            : isStarting
              ? { opacity: [0.12, 0.46, 0.2] }
              : isScanning
                ? { opacity: [0.16, 0.38, 0.18] }
              : isSuccess
                ? { opacity: [0.16, 0.55, 0.28] }
                : { opacity: 0.14 }
        }
        transition={
          reducedMotion
            ? { duration: 0.12 }
            : isStarting
              ? { duration: 0.42, ease: "easeOut" }
              : isScanning
                ? { duration: 1.28, repeat: Infinity, ease: "easeInOut" }
              : { duration: 0.5, ease: "easeOut" }
        }
        style={{ background: glowColor }}
      />
      <svg className="cc-bio-svg" viewBox="0 0 100 100" aria-hidden="true" focusable="false">
        <defs>
          <clipPath id="cc-bio-scan-clip">
            <rect x="24" y="22" width="52" height="56" rx="16" />
          </clipPath>
          <linearGradient id="cc-bio-scan-gradient" x1="28" y1="0" x2="72" y2="0" gradientUnits="userSpaceOnUse">
            <stop offset="0" stopColor="#2563eb" stopOpacity="0" />
            <stop offset="0.5" stopColor="#2563eb" stopOpacity="1" />
            <stop offset="1" stopColor="#22d3ee" stopOpacity="0" />
          </linearGradient>
          <linearGradient id="cc-bio-scan-wash" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0" stopColor="#60a5fa" stopOpacity="0" />
            <stop offset="0.48" stopColor="#60a5fa" stopOpacity="0.18" />
            <stop offset="1" stopColor="#22d3ee" stopOpacity="0" />
          </linearGradient>
          <radialGradient id="cc-bio-scan-field" cx="50%" cy="50%" r="58%">
            <stop offset="0" stopColor="#dbeafe" stopOpacity="0.34" />
            <stop offset="1" stopColor="#93c5fd" stopOpacity="0" />
          </radialGradient>
          <filter id="cc-bio-soft-glow" x="-30%" y="-30%" width="160%" height="160%">
            <feGaussianBlur stdDeviation="0.7" result="blur" />
            <feMerge>
              <feMergeNode in="blur" />
              <feMergeNode in="SourceGraphic" />
            </feMerge>
          </filter>
        </defs>

        <motion.g
          clipPath="url(#cc-bio-scan-clip)"
          initial={false}
          animate={{
            opacity: isSuccess ? 0 : isScanning ? 1 : isStarting ? 0.72 : 0,
          }}
          transition={{ duration: isStarting ? 0.28 : 0.2 }}
        >
          <rect x="24" y="22" width="52" height="56" rx="16" fill="url(#cc-bio-scan-field)" />
          {isScanning && !reducedMotion && (
            <motion.rect
              x="24"
              y="56"
              width="52"
              height="24"
              rx="12"
              fill="url(#cc-bio-scan-wash)"
              animate={{ y: [54, 22, 54], opacity: [0.42, 0.72, 0.42] }}
              transition={{ duration: 1.28, repeat: Infinity, ease: "easeInOut" }}
            />
          )}
        </motion.g>

        <motion.g
          className="cc-bio-svg-corners"
          initial={false}
          animate={{
            opacity: isSuccess ? 0 : isScanning && !reducedMotion ? [0.88, 1, 0.9] : isStarting ? [0.45, 1] : 1,
            scale: isSuccess ? 0.93 : isStarting && !reducedMotion ? [0.84, 1.03, 1] : 1,
          }}
          transition={
            isScanning && !reducedMotion
              ? { duration: 1.28, repeat: Infinity, ease: "easeInOut" }
              : isStarting && !reducedMotion
                ? { duration: 0.42, ease: "easeOut" }
                : { duration: 0.2 }
          }
          stroke={cornerColor}
        >
          <path d="M27 42 V33 A7 7 0 0 1 34 26 H44" />
          <path d="M56 26 H66 A7 7 0 0 1 73 33 V42" />
          <path d="M73 58 V67 A7 7 0 0 1 66 74 H56" />
          <path d="M44 74 H34 A7 7 0 0 1 27 67 V58" />
        </motion.g>

        <motion.g
          className="cc-bio-svg-face"
          initial={false}
          animate={{
            opacity: isSuccess ? 0 : isScanning && !reducedMotion ? [0.78, 1, 0.82] : isStarting ? [0, 1] : 0.94,
            scale: isSuccess ? 0.92 : isStarting && !reducedMotion ? [0.82, 1.02, 1] : 1,
          }}
          transition={
            isScanning && !reducedMotion
              ? { duration: 1.28, repeat: Infinity, ease: "easeInOut" }
              : isStarting && !reducedMotion
                ? { duration: 0.42, ease: "easeOut" }
                : { duration: 0.2 }
          }
          stroke={cornerColor}
        >
          <path d="M41 40.5 V46.5" />
          <path d="M59 40.5 V46.5" />
          <path d="M50 41 V54.5 A5 5 0 0 1 45 59.5 H43" />
          <path d="M40 62 C44.5 67.6 55.5 67.6 60 62" />
        </motion.g>

        <AnimatePresence>
          {isScanning && !reducedMotion && (
            <motion.g
              key="scanline"
              clipPath="url(#cc-bio-scan-clip)"
              initial={{ opacity: 0.72 }}
              animate={{ opacity: [0.82, 1, 0.86] }}
              exit={{ opacity: 0, transition: { duration: 0.16 } }}
              transition={{ duration: 1.28, repeat: Infinity, ease: "easeInOut" }}
            >
              <motion.line
                x1="25"
                x2="75"
                y1="74"
                y2="74"
                stroke="#60a5fa"
                strokeOpacity="0.44"
                strokeWidth="7"
                strokeLinecap="round"
                filter="url(#cc-bio-soft-glow)"
                animate={{ y1: [74, 26, 74], y2: [74, 26, 74] }}
                transition={{ duration: 1.28, repeat: Infinity, ease: "easeInOut" }}
              />
              <motion.line
                x1="25"
                x2="75"
                y1="74"
                y2="74"
                stroke="url(#cc-bio-scan-gradient)"
                strokeWidth="3.8"
                strokeLinecap="round"
                animate={{ y1: [74, 26, 74], y2: [74, 26, 74] }}
                transition={{ duration: 1.28, repeat: Infinity, ease: "easeInOut" }}
              />
            </motion.g>
          )}
        </AnimatePresence>

        <AnimatePresence>
          {isSuccess && (
            <motion.g
              key="check"
              initial={{ opacity: 0, scale: 0.76 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.88 }}
              transition={{ type: "spring", stiffness: 360, damping: 24, delay: 0.12 }}
              transform="translate(0 0)"
            >
              <motion.circle
                cx="50"
                cy="50"
                r="24"
                fill="none"
                stroke="rgba(34, 197, 94, 0.2)"
                strokeWidth="9"
                initial={{ opacity: 0, scale: 0.96 }}
                animate={{ opacity: [0, 0.9, 0.28], scale: [0.96, 1.08, 1] }}
                transition={{ duration: reducedMotion ? 0.16 : 0.42, ease: "easeOut", delay: reducedMotion ? 0 : 0.12 }}
              />
              <motion.circle
                cx="50"
                cy="50"
                r="24"
                fill="none"
                stroke="#16a34a"
                strokeWidth="5"
                strokeLinecap="round"
                initial={{ pathLength: 0, opacity: 0, rotate: -90 }}
                animate={{ pathLength: 1, opacity: 1, rotate: -90 }}
                transition={{ duration: reducedMotion ? 0.16 : 0.34, ease: "easeOut", delay: reducedMotion ? 0 : 0.12 }}
                style={{ transformOrigin: "50% 50%" }}
              />
              <motion.path
                d="M37 50.5 L46 59 L64 41"
                fill="none"
                stroke="#16a34a"
                strokeWidth="6"
                strokeLinecap="round"
                strokeLinejoin="round"
                initial={{ pathLength: 0, opacity: 0 }}
                animate={{ pathLength: 1, opacity: 1 }}
                transition={{ duration: reducedMotion ? 0.16 : 0.34, ease: "easeOut", delay: reducedMotion ? 0 : 0.42 }}
              />
            </motion.g>
          )}
        </AnimatePresence>
      </svg>
    </motion.div>
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
