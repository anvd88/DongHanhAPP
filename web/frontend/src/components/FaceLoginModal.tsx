import { useCallback, useEffect, useRef, useState } from "react";
import { Loader2, ScanFace, X } from "lucide-react";
import { useCamera } from "../features/chamcong/useCamera";
import { FaceTrackingOverlay, type Framing } from "../features/chamcong/FaceTrackingOverlay";
import { useAuth } from "../lib/auth";

type Phase = "opening" | "aiming" | "analyzing" | "success" | "error";

// Cổng căn khung: giữ mặt trong khung "đạt" liên tục đủ lâu mới chụp loạt — tránh chụp lúc mặt
// thoáng qua. Vừa giữ vừa gom khung để có nhiều ảnh tốt cho server chọn.
const HOLD_STABLE_MS = 1600;
const AIM_POLL_MS = 100;
const AIM_TIMEOUT_MS = 25000;
const DETECTOR_GRACE_MS = 6000; // model căn khung không sẵn sàng ⇒ bỏ qua cổng, chụp loạt như cũ
const AIM_CAPTURE_EVERY_MS = 160;
const MAX_AIM_FRAMES = 14;
const MOTION_THRESHOLD = 0.2; // tổng cử động tối thiểu coi là mặt sống (liveness thụ động)
const STABILIZE_MS = 420; // chờ camera tự phơi sáng sau khi mở

const wait = (ms: number) => new Promise<void>((resolve) => window.setTimeout(resolve, ms));

/**
 * Cửa sổ đăng nhập bằng khuôn mặt. Mở camera, căn khung + bắt cử động (liveness), chụp một loạt
 * ảnh rồi gửi lên server để nhận diện và cấp token. Server vẫn là chốt chặn chống giả mạo cuối.
 */
export function FaceLoginModal({ onClose, onSuccess }: { onClose: () => void; onSuccess: () => void }) {
  const { loginWithFace } = useAuth();
  const { videoRef, active, error: camError, start, stop, capture, captureBurst } = useCamera();

  const framingRef = useRef<Framing | null>(null);
  const onFraming = useCallback((f: Framing) => {
    framingRef.current = f;
  }, []);

  const [phase, setPhase] = useState<Phase>("opening");
  const [hint, setHint] = useState("Đang mở camera…");
  const [errorMsg, setErrorMsg] = useState("");
  const [hold, setHold] = useState(false);

  const cancelRef = useRef(false);
  const runningRef = useRef(false);

  // Gom khung trong lúc giữ mặt ở khung "đạt"; đòi có cử động tự nhiên (liveness thụ động). Trả về
  // các khung đã gom. Nếu không có dữ liệu căn khung (model lỗi) thì bỏ qua cổng, để chụp loạt sau.
  const aimAndCapture = useCallback(async (): Promise<{ status: "ok" | "timeout" | "cancelled"; frames: string[] }> => {
    const startedAt = performance.now();
    const deadline = startedAt + AIM_TIMEOUT_MS;
    let goodSince: number | null = null;
    let seenFraming = false;
    let motionBaseline: number | null = null;
    let blinkBaseline: number | null = null;
    let blinked = false;
    let lastCapture = 0;
    const frames: string[] = [];

    const grab = (now: number) => {
      if (now - lastCapture < AIM_CAPTURE_EVERY_MS) return;
      lastCapture = now;
      const img = capture();
      if (img) {
        frames.push(img);
        if (frames.length > MAX_AIM_FRAMES) frames.shift();
      }
    };

    while (!cancelRef.current) {
      const now = performance.now();
      const f = framingRef.current;
      let activity = 0;
      if (f) {
        seenFraming = true;
        if (blinkBaseline == null) blinkBaseline = f.blinkCount;
        else if (f.blinkCount > blinkBaseline) blinked = true;
        if (motionBaseline == null) motionBaseline = f.motion;
        else activity = f.motion - motionBaseline;
      }
      if (!seenFraming && now - startedAt > DETECTOR_GRACE_MS) return { status: "ok", frames };

      if (f?.ok) {
        if (goodSince == null) goodSince = now;
        grab(now);
        setHold(true);
        const held = now - goodSince;
        const livenessOk = blinked || activity >= MOTION_THRESHOLD;
        if (held >= HOLD_STABLE_MS) {
          if (livenessOk) return { status: "ok", frames };
          setHint("Hãy chớp mắt để xác nhận");
        } else {
          setHint("Giữ yên khuôn mặt trong khung…");
        }
      } else {
        goodSince = null;
        setHold(false);
        setHint(f?.hint ?? "Đưa khuôn mặt vào giữa khung");
      }
      if (now > deadline) return { status: "timeout", frames };
      await wait(AIM_POLL_MS);
    }
    return { status: "cancelled", frames };
  }, [capture]);

  const run = useCallback(async () => {
    if (runningRef.current) return;
    runningRef.current = true;
    cancelRef.current = false;
    setErrorMsg("");
    try {
      setPhase("opening");
      setHint("Đang mở camera…");
      await start();
      await wait(STABILIZE_MS);
      if (cancelRef.current) return;

      setPhase("aiming");
      const aim = await aimAndCapture();
      setHold(false);
      if (cancelRef.current || aim.status === "cancelled") return;
      if (aim.status === "timeout") {
        setPhase("error");
        setErrorMsg("Chưa nhận diện được. Hãy giữ mặt trong khung và thử lại.");
        return;
      }

      setPhase("analyzing");
      setHint("Đang nhận diện…");
      let frames = aim.frames;
      if (frames.length === 0) frames = await captureBurst(10, 120);
      if (frames.length === 0) {
        setPhase("error");
        setErrorMsg("Không chụp được ảnh. Vui lòng thử lại.");
        return;
      }

      await loginWithFace(frames);
      setPhase("success");
      setHint("Đăng nhập thành công");
      stop();
      onSuccess();
    } catch (e) {
      setPhase("error");
      setErrorMsg(e instanceof Error ? e.message : "Đăng nhập bằng khuôn mặt thất bại.");
    } finally {
      runningRef.current = false;
    }
  }, [start, stop, aimAndCapture, captureBurst, loginWithFace, onSuccess]);

  // Tự bắt đầu khi mở modal; dọn camera khi đóng.
  useEffect(() => {
    void run();
    return () => {
      cancelRef.current = true;
      stop();
    };
    // chỉ chạy 1 lần khi gắn
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const close = () => {
    cancelRef.current = true;
    stop();
    onClose();
  };

  const busy = phase === "opening" || phase === "analyzing";

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/55 p-4 backdrop-blur-sm">
      <div className="fade-in glass glass-strong w-full max-w-sm rounded-3xl p-6">
        <div className="mb-4 flex items-center justify-between">
          <div className="flex items-center gap-2 text-[var(--text)]">
            <ScanFace className="h-5 w-5 text-[var(--accent)]" />
            <span className="font-semibold">Đăng nhập bằng khuôn mặt</span>
          </div>
          <button
            onClick={close}
            className="rounded-lg p-1.5 text-[var(--text-muted)] transition-colors hover:bg-white/10 hover:text-[var(--text)]"
            aria-label="Đóng"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div
          className="relative mx-auto aspect-square w-full overflow-hidden rounded-2xl border border-[var(--glass-border)] bg-black/30"
          data-hold={hold ? "true" : undefined}
        >
          <video
            ref={videoRef}
            playsInline
            muted
            className="h-full w-full object-cover"
            style={{ transform: "scaleX(-1)", opacity: active ? 1 : 0 }}
          />
          {active && <FaceTrackingOverlay videoRef={videoRef} active={active} onFraming={onFraming} />}

          {phase === "success" && (
            <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-[var(--accent)]/15 text-[var(--accent)]">
              <ScanFace className="h-12 w-12" />
              <span className="font-semibold">Đăng nhập thành công</span>
            </div>
          )}

          <div
            className="absolute inset-x-0 bottom-0 flex items-center justify-center gap-2 bg-gradient-to-t from-black/65 to-transparent px-3 pb-3 pt-8 text-center text-sm font-medium text-white"
            data-warn={phase === "error" ? "true" : undefined}
          >
            {busy && <Loader2 className="h-4 w-4 animate-spin" />}
            {phase === "error" ? errorMsg : hint}
          </div>
        </div>

        {camError && <div className="mt-3 rounded-xl bg-red-500/10 px-3 py-2.5 text-sm font-medium text-[var(--danger)]">{camError}</div>}

        {phase === "error" && (
          <button
            onClick={run}
            className="mt-4 flex w-full items-center justify-center gap-2 rounded-xl border border-[var(--glass-border)] bg-white/40 py-3 text-sm font-semibold text-[var(--text)] transition-all hover:border-[var(--accent)] dark:bg-white/5"
          >
            <ScanFace className="h-4 w-4" /> Thử lại
          </button>
        )}
      </div>
    </div>
  );
}
