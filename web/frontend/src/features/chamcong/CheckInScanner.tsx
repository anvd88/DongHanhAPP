import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { AnimatePresence, motion, useReducedMotion } from "framer-motion";
import { Camera, CheckCircle2, Loader2, ScanFace } from "lucide-react";
import { api } from "../../lib/api";
import type { ChamCongResult } from "../../lib/types";
import { useCamera } from "./useCamera";
import { FaceTrackingOverlay, type Framing } from "./FaceTrackingOverlay";

type CheckInPopup = {
  name: string;
  loai?: string;
  occurredAt?: string;
};

type CheckInScannerProps = {
  returnToLoginOnOk?: boolean;
  /** Giao diện kiosk (Liquid Glass, ẩn camera) thay cho thẻ camera trong app. */
  biometricMode?: boolean;
  /** Tự bắt đầu chấm công ngay khi gắn (dùng cho kiosk khi đã bấm nút bên ngoài). */
  autoStart?: boolean;
};

type Phase = "idle" | "opening" | "capturing" | "analyzing" | "warning" | "success";
type FaceScanAnimationStatus = "idle" | "starting" | "scanning" | "success" | "error";

// Số khung chụp mỗi lượt và khoảng cách giữa các khung (≈ 1.2s) — đủ để có vài khung nét, chính diện.
const BURST_COUNT = 10;
const BURST_GAP_MS = 110;
const START_SCAN_ANIMATION_MS = 460;
// Thông báo chung cho mọi pha xử lý — KHÔNG lộ cơ chế bên trong (chụp loạt, chọn ảnh tốt nhất…).
const PROCESSING_HINT = "Đang chấm công…";
const wait = (ms: number) => new Promise<void>((resolve) => window.setTimeout(resolve, ms));

/**
 * Chấm công bằng khuôn mặt theo luồng "chụp loạt → chọn ảnh tốt nhất → phân tích → ghi nhật ký".
 * KHÔNG quét trực tiếp liên tục: mỗi lượt chụp một loạt khung, server tự chọn khung tốt nhất,
 * báo trực tiếp nếu sai tư thế / thiếu sáng, rồi nhận diện. Dùng chung cho tab "Chấm công" và kiosk.
 */
export function CheckInScanner({ returnToLoginOnOk = false, biometricMode = false, autoStart = false }: CheckInScannerProps) {
  return biometricMode ? (
    <BiometricCheckInScanner autoStart={autoStart} returnToLoginOnOk={returnToLoginOnOk} />
  ) : (
    <PanelCheckInScanner returnToLoginOnOk={returnToLoginOnOk} />
  );
}

/**
 * Hook lõi: quản lý camera + một lượt chấm công (mở camera → chụp loạt → POST /cham → kết quả).
 * Tách khỏi giao diện để dùng lại cho cả thẻ camera trong app lẫn kiosk Liquid Glass.
 */
function useBurstCheckIn() {
  const { videoRef, active, error, start, stop, captureBurst } = useCamera();
  const [phase, setPhase] = useState<Phase>("idle");
  const [hint, setHint] = useState("Sẵn sàng chấm công");
  const [result, setResult] = useState<ChamCongResult | null>(null);
  const runningRef = useRef(false);

  const run = useCallback(async () => {
    if (runningRef.current) return;
    runningRef.current = true;
    setResult(null);
    try {
      if (!active) {
        setPhase("opening");
        setHint(PROCESSING_HINT);
        await start();
        await wait(START_SCAN_ANIMATION_MS); // chờ camera tự phơi sáng ổn định
      }

      setPhase("capturing");
      setHint(PROCESSING_HINT);
      const frames = await captureBurst(BURST_COUNT, BURST_GAP_MS);
      if (frames.length === 0) {
        setPhase("warning");
        setHint("Chưa chấm công được. Vui lòng thử lại.");
        setResult(null);
        return;
      }

      setPhase("analyzing");
      setHint(PROCESSING_HINT);
      const res = await api.post<ChamCongResult>("/api/chamcong/cham", { images: frames });
      setResult(res);

      if (res.matched && res.status === "ok") {
        setPhase("success");
        setHint(`${res.fullName || res.username || "Nhân viên"} đã chấm công`);
        stop();
      } else {
        setPhase("warning");
        setHint(res.guidance || res.message);
      }
    } catch (e) {
      setPhase("warning");
      setHint(e instanceof Error ? e.message : "Lỗi chấm công. Vui lòng thử lại.");
      setResult(null);
    } finally {
      runningRef.current = false;
    }
  }, [active, start, stop, captureBurst]);

  const reset = useCallback(() => {
    setResult(null);
    setPhase("idle");
    setHint("Sẵn sàng chấm công");
  }, []);

  const busy = phase === "opening" || phase === "capturing" || phase === "analyzing";

  return { videoRef, active, error, phase, hint, result, busy, run, stop, reset };
}

/* --------------------------- Thẻ camera trong app --------------------------- */
function PanelCheckInScanner({ returnToLoginOnOk }: Pick<CheckInScannerProps, "returnToLoginOnOk">) {
  const navigate = useNavigate();
  const { videoRef, active, error, phase, hint, result, busy, run, stop } = useBurstCheckIn();
  const [popup, setPopup] = useState<CheckInPopup | null>(null);
  const [framing, setFraming] = useState<Framing | null>(null);

  useEffect(() => {
    if (phase === "success" && result?.matched) {
      setPopup({
        name: result.fullName || result.username || "Nhân viên",
        loai: result.loai,
        occurredAt: result.occurredAt,
      });
    }
  }, [phase, result]);

  const closePopup = () => {
    setPopup(null);
    if (returnToLoginOnOk) navigate("/login", { replace: true });
  };

  // Gợi ý dưới camera: khi đang xử lý hiện thông báo trung tính; khi rảnh hiện hướng dẫn căn khung.
  const showFramingHint = active && !busy && phase !== "success" && framing != null && framing.state !== "good";

  return (
    <div className="cc-grid cc-checkin-grid">
      <CheckInSuccessPopup popup={popup} onOk={closePopup} />

      <div className="cc-camera glass">
        <div className="cc-video-wrap">
          <video ref={videoRef} playsInline muted className="cc-video" data-active={active} />
          {!active && (
            <div className="cc-video-empty">
              <Camera className="h-8 w-8" />
              <span>Camera đang tắt</span>
            </div>
          )}
          {active && <FaceTrackingOverlay videoRef={videoRef} active={active} onFraming={setFraming} />}
          {busy ? (
            <div className="cc-scan-hint">
              <Loader2 className="h-3.5 w-3.5 animate-spin" /> {hint}
            </div>
          ) : showFramingHint ? (
            <div className="cc-scan-hint" data-warn="true">
              {framing!.hint}
            </div>
          ) : null}
        </div>

        {error && <div className="cc-error">{error}</div>}

        <div className="cc-camera-actions">
          <button className="cc-btn cc-btn-primary" onClick={run} disabled={busy} type="button">
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <ScanFace className="h-4 w-4" />}{" "}
            {busy ? PROCESSING_HINT : active ? "Chấm công" : "Bật camera & chấm công"}
          </button>
          {active && (
            <button className="cc-btn" onClick={stop} disabled={busy} type="button">
              Tắt camera
            </button>
          )}
        </div>
      </div>

      <div className="cc-result glass">
        <PanelResult phase={phase} hint={hint} result={result} />
      </div>
    </div>
  );
}

function PanelResult({ phase, hint, result }: { phase: Phase; hint: string; result: ChamCongResult | null }) {
  if (phase === "success" && result?.matched) {
    return (
      <div className="cc-result-ok">
        <CheckCircle2 className="h-12 w-12" />
        <div className="cc-result-name">{result.fullName || result.username}</div>
        {result.loai && <div className="cc-result-badge" data-loai={result.loai}>{result.loai}</div>}
        <div className="cc-result-meta">Độ khớp {(result.similarity * 100).toFixed(1)}%</div>
      </div>
    );
  }

  if (phase === "warning") {
    return (
      <div className="cc-result-fail" data-tone={result?.status === "posture" || result?.status === "lowquality" ? "warn" : undefined}>
        <ScanFace className="h-12 w-12" />
        <div className="cc-result-name cc-result-name--sm">{result?.message ?? "Chưa chấm công được"}</div>
        {result?.guidance && <p>{result.guidance}</p>}
        {!result?.guidance && <p>{hint}</p>}
      </div>
    );
  }

  if (phase === "opening" || phase === "capturing" || phase === "analyzing") {
    return (
      <div className="cc-result-empty">
        <Loader2 className="h-10 w-10 animate-spin" />
        <p>{hint}</p>
      </div>
    );
  }

  return (
    <div className="cc-result-empty">
      <ScanFace className="h-10 w-10" />
      <p>Bấm “Chấm công” — hệ thống chụp một loạt ảnh, chọn ảnh rõ nhất rồi nhận diện.</p>
    </div>
  );
}

/* ----------------------------- Kiosk Liquid Glass ----------------------------- */
function BiometricCheckInScanner({
  autoStart = false,
  returnToLoginOnOk = false,
}: Pick<CheckInScannerProps, "autoStart" | "returnToLoginOnOk">) {
  const navigate = useNavigate();
  const { videoRef, active, error, phase, hint, result, busy, run } = useBurstCheckIn();

  const autoStartedRef = useRef(false);
  useEffect(() => {
    if (autoStart && !autoStartedRef.current) {
      autoStartedRef.current = true;
      void run();
    }
    // run đổi mỗi render; chỉ chạy 1 lần khi gắn nên không đưa vào deps.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoStart]);

  const success = phase === "success" && result?.matched;
  const successName = result?.fullName || result?.username || "Nhân viên";
  const successTime = result?.occurredAt ? new Date(result.occurredAt).toLocaleTimeString("vi-VN") : null;

  const scanStatus: FaceScanAnimationStatus =
    phase === "success"
      ? "success"
      : phase === "warning"
        ? "error"
        : phase === "opening"
          ? "starting"
          : busy || active
            ? "scanning"
            : "idle";

  const islandPhase = phase === "warning" ? "warning" : phase === "success" ? "success" : busy ? "scanning" : "idle";

  return (
    <div className="cc-bio-shell">
      <video ref={videoRef} playsInline muted className="cc-bio-video" aria-hidden="true" />

      <motion.div
        className="cc-bio-island"
        data-phase={islandPhase}
        animate={{ scale: success ? 0.985 : 1 }}
        transition={{ type: "spring", stiffness: 260, damping: 24 }}
      >
        <FaceScanAnimation status={scanStatus} />

        <div className="cc-bio-copy">
          <div className="cc-bio-title">{success ? "Chấm công thành công" : "Chấm công khuôn mặt"}</div>
          {success ? (
            <motion.div
              className="cc-bio-success"
              initial={{ opacity: 0, y: 6 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.24, ease: "easeOut", delay: 0.08 }}
            >
              <div className="cc-bio-success-name">{successName} đã chấm công</div>
              <div className="cc-bio-success-meta">{[result?.loai, successTime].filter(Boolean).join(" · ")}</div>
            </motion.div>
          ) : (
            <div className="cc-bio-hint" data-warn={phase === "warning"}>
              {hint}
            </div>
          )}
        </div>

        {!success && !busy && (
          <button className="cc-bio-start" onClick={run} type="button">
            <Camera className="h-4 w-4" /> {phase === "warning" ? "Thử lại" : "Bắt đầu chấm công"}
          </button>
        )}
        {!success && busy && (
          <div className="cc-bio-active-pill">
            <Loader2 className="h-4 w-4 animate-spin" /> {PROCESSING_HINT}
          </div>
        )}
        {success && (
          <div className="cc-bio-success-actions">
            <button className="cc-bio-start cc-bio-start--secondary" onClick={run} type="button">
              <Camera className="h-4 w-4" /> Chấm tiếp
            </button>
            {returnToLoginOnOk && (
              <button className="cc-bio-start" onClick={() => navigate("/login", { replace: true })} type="button">
                Xong
              </button>
            )}
          </div>
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
