import { useCallback, useEffect, useRef, useState, type RefObject } from "react";
import { useNavigate } from "react-router-dom";
import { AnimatePresence, motion, useReducedMotion } from "framer-motion";
import { Camera, Loader2, ScanFace, Smartphone, UserCheck, Video } from "lucide-react";
import { api } from "../../lib/api";
import type { ChamCongResult } from "../../lib/types";
import { useCamera } from "./useCamera";
import { useIpCamera } from "./useIpCamera";
import { IP_CAMERA_ENABLED } from "../../lib/features";
import { FaceTrackingOverlay, type Framing } from "./FaceTrackingOverlay";

/** Nguồn camera dùng để chấm công: webcam thiết bị (điện thoại/laptop) hoặc camera IP (RTSP kiosk). */
type CameraSource = "device" | "ip";

type CheckInToastData = {
  id: number;
  name: string;
  time: string;
};

type CheckInScannerProps = {
  returnToLoginOnOk?: boolean;
  /** Giao diện kiosk (Liquid Glass, ẩn camera) thay cho thẻ camera trong app. */
  biometricMode?: boolean;
  /** Tự bắt đầu chấm công ngay khi gắn (dùng cho kiosk khi đã bấm nút bên ngoài). */
  autoStart?: boolean;
};

type Phase = "idle" | "opening" | "aiming" | "capturing" | "analyzing" | "warning" | "success";
type FaceScanAnimationStatus = "idle" | "starting" | "scanning" | "success" | "error";

// Số khung chụp mỗi lượt và khoảng cách giữa các khung (≈ 1.2s) — đủ để có vài khung nét, chính diện.
const BURST_COUNT = 10;
const BURST_GAP_MS = 110;
// Camera IP: gom khung từ ảnh snapshot (FFmpeg ghi ~10 khung/giây) — giãn nhịp để có khung khác nhau.
const IP_BURST_COUNT = 8;
const IP_BURST_GAP_MS = 150;
const START_SCAN_ANIMATION_MS = 460;
// Cổng giữ khung: khuôn mặt phải ở đúng khung "đạt" LIÊN TỤC đủ ngần này mới chấm — tránh hệ thống
// quá nhạy bắt nhầm người chỉ thoáng qua khung. Rời khung là đếm lại từ đầu.
const HOLD_STABLE_MS = 3000;
const AIM_POLL_MS = 100; // nhịp kiểm tra trạng thái căn khung
const AIM_TIMEOUT_MS = 30000; // không ai giữ được khung đủ lâu ⇒ dừng để thử lại
const DETECTOR_GRACE_MS = 7000; // model căn khung không sẵn sàng ⇒ bỏ qua cổng, giữ hành vi cũ
// Tận dụng chính 3 giây giữ khung để QUÉT: chụp khung đều đặn ngay trong lúc giữ (không chờ
// xong rồi mới chụp loạt). Nhờ vậy tổng thời gian không dài thêm mà còn nhiều khung hơn để chọn.
const AIM_CAPTURE_EVERY_MS = 180; // ~16 khung trải đều trong 3s
const MAX_AIM_FRAMES = 16; // trần số khung gom được (giữ các khung mới nhất)
// Liveness THỤ ĐỘNG + step-up: mặc định KHÔNG bắt làm gì — chỉ cần trong lúc giữ khung có đủ
// cử động tự nhiên (người thật luôn nhúc nhích). Nếu mặt bất động đáng ngờ (nghi ảnh tĩnh) thì
// mới NÂNG lên yêu cầu chớp mắt 1 cái. Ngưỡng nới tay vì server (MiniFASNet) vẫn là chốt chặn.
const MOTION_THRESHOLD = 0.22; // tổng cử động biểu cảm tối thiểu coi là mặt sống
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
    <PanelCheckInScanner />
  );
}

/**
 * Hook lõi: quản lý camera + một lượt chấm công (mở camera → chụp loạt → POST /cham → kết quả).
 * Tách khỏi giao diện để dùng lại cho cả thẻ camera trong app lẫn kiosk Liquid Glass.
 */
function useBurstCheckIn(framingRef?: RefObject<Framing | null>) {
  const { videoRef, active, error, start, stop, capture, captureBurst } = useCamera();
  const [phase, setPhase] = useState<Phase>("idle");
  const [hint, setHint] = useState("Sẵn sàng chấm công");
  const [result, setResult] = useState<ChamCongResult | null>(null);
  // Đang giữ khung liên tục hay không. Thanh tiến độ tự chạy bằng CSS transition theo trạng thái
  // này (mượt ở GPU, không bị giật bởi JS/chụp khung). JS chỉ bật/tắt, không vẽ từng nấc.
  const [holding, setHolding] = useState(false);
  const holdingRef = useRef(false);
  const runningRef = useRef(false);
  const cancelRef = useRef(false);

  const setHold = useCallback((v: boolean) => {
    if (holdingRef.current !== v) {
      holdingRef.current = v;
      setHolding(v);
    }
  }, []);

  /**
   * Giai đoạn "ngắm": vừa chờ khuôn mặt ở khung "đạt" LIÊN TỤC đủ HOLD_STABLE_MS, vừa CHỤP khung
   * đều đặn ngay trong lúc giữ, vừa đòi CHỚP MẮT ít nhất 1 lần (liveness chủ động). Mặt rời khung
   * ⇒ đếm lại từ đầu. Trả về cả kết quả và các khung đã gom được. Nếu không có dữ liệu căn khung
   * (không truyền framingRef hoặc model lỗi) thì bỏ qua cổng để giữ hành vi cũ.
   */
  const aimAndCapture = useCallback(async (): Promise<{
    status: "ok" | "timeout" | "cancelled";
    frames: string[];
  }> => {
    if (!framingRef) return { status: "ok", frames: [] };
    const startedAt = performance.now();
    const deadline = startedAt + AIM_TIMEOUT_MS;
    let goodSince: number | null = null;
    let seenFraming = false;
    let blinkBaseline: number | null = null;
    let motionBaseline: number | null = null;
    let blinked = false;
    let lastCapture = 0;
    const frames: string[] = [];
    setHold(false);

    const grabFrame = (now: number) => {
      if (now - lastCapture < AIM_CAPTURE_EVERY_MS) return;
      lastCapture = now;
      const img = capture();
      if (img) {
        frames.push(img);
        if (frames.length > MAX_AIM_FRAMES) frames.shift(); // giữ các khung mới nhất
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
      // Model căn khung không sẵn sàng (chưa từng phát tín hiệu) ⇒ bỏ qua cổng, chấm như cũ.
      if (!seenFraming && now - startedAt > DETECTOR_GRACE_MS) return { status: "ok", frames };

      if (f?.ok) {
        if (goodSince == null) goodSince = now;
        const held = now - goodSince;
        grabFrame(now); // QUÉT ngay trong lúc giữ — không chờ xong mới chụp
        setHold(true); // thanh tiến độ tự chạy 0→100% trong HOLD_STABLE_MS bằng CSS

        // Liveness thụ động: đủ cử động tự nhiên HOẶC có chớp mắt ⇒ coi là mặt sống.
        const livenessOk = blinked || activity >= MOTION_THRESHOLD;
        if (held >= HOLD_STABLE_MS) {
          if (livenessOk) return { status: "ok", frames };
          // Mặt bất động đáng ngờ → NÂNG lên thử thách chớp mắt (chỉ lúc này mới làm phiền).
          setHint("Hãy chớp mắt để xác nhận");
        } else {
          const remain = Math.ceil((HOLD_STABLE_MS - held) / 1000);
          setHint(remain > 0 ? `Giữ yên trong khung… ${remain}s` : PROCESSING_HINT);
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
  }, [framingRef, capture]);

  const run = useCallback(async () => {
    if (runningRef.current) return;
    runningRef.current = true;
    cancelRef.current = false;
    setResult(null);
    try {
      if (!active) {
        setPhase("opening");
        setHint(PROCESSING_HINT);
        await start();
        await wait(START_SCAN_ANIMATION_MS); // chờ camera tự phơi sáng ổn định
      }

      // Cổng "ngắm": giữ khung 3s + chớp mắt, đồng thời gom khung ngay trong lúc giữ.
      let frames: string[] = [];
      if (framingRef) {
        setPhase("aiming");
        const aim = await aimAndCapture();
        setHold(false);
        if (aim.status === "cancelled") {
          setPhase("idle");
          setHint("Sẵn sàng chấm công");
          return;
        }
        if (aim.status === "timeout") {
          setPhase("warning");
          setHint("Chưa xác nhận được. Hãy giữ mặt trong khung và chớp mắt rồi thử lại.");
          return;
        }
        frames = aim.frames;
      }

      setPhase("capturing");
      setHint(PROCESSING_HINT);
      // Khung gom trong lúc giữ thường đã đủ; thiếu (vd. model căn khung lỗi) thì chụp bù 1 loạt.
      if (frames.length === 0) frames = await captureBurst(BURST_COUNT, BURST_GAP_MS);
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
  }, [active, start, stop, captureBurst, framingRef, aimAndCapture, setHold]);

  // Hủy lượt chấm đang chờ giữ khung và tắt camera.
  const cancel = useCallback(() => {
    cancelRef.current = true;
    setHold(false);
    stop();
  }, [stop, setHold]);

  const reset = useCallback(() => {
    cancelRef.current = true;
    setResult(null);
    setHold(false);
    setPhase("idle");
    setHint("Sẵn sàng chấm công");
  }, [setHold]);

  const aiming = phase === "aiming";
  const busy = phase === "opening" || phase === "capturing" || phase === "analyzing";

  return { videoRef, active, error, phase, hint, result, busy, aiming, holding, run, stop, cancel, reset };
}

/**
 * Hook chấm công bằng CAMERA IP: hiển thị hình trực tiếp từ snapshot rồi gom 1 loạt khung gửi
 * server nhận diện. Không có cổng "ngắm/giữ khung" như webcam (không gắn MediaPipe vào ảnh poll);
 * chất lượng/tư thế/liveness vẫn do server kiểm ở /api/chamcong/cham như chế độ điện thoại.
 */
function useIpBurstCheckIn() {
  const { active, error, frameUrl, start: startFeed, stop, captureBurst } = useIpCamera();
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
        await startFeed();
        await wait(START_SCAN_ANIMATION_MS);
      }

      setPhase("capturing");
      setHint(PROCESSING_HINT);
      const frames = await captureBurst(IP_BURST_COUNT, IP_BURST_GAP_MS);
      if (frames.length === 0) {
        setPhase("warning");
        setHint("Chưa lấy được hình từ camera IP. Kiểm tra kết nối camera rồi thử lại.");
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
  }, [active, startFeed, captureBurst]);

  const reset = useCallback(() => {
    setResult(null);
    setPhase("idle");
    setHint("Sẵn sàng chấm công");
  }, []);

  const busy = phase === "opening" || phase === "capturing" || phase === "analyzing";
  return { active, error, frameUrl, phase, hint, result, busy, start: startFeed, run, stop, reset };
}

/** Bộ chọn nguồn camera (Điện thoại / Camera IP) cho màn chấm công. */
function CameraSourceToggle({
  value,
  onChange,
  disabled,
}: {
  value: CameraSource;
  onChange: (next: CameraSource) => void;
  disabled?: boolean;
}) {
  return (
    <div className="cc-source-toggle" role="tablist" aria-label="Nguồn camera chấm công">
      <button
        type="button"
        role="tab"
        aria-selected={value === "device"}
        data-on={value === "device"}
        disabled={disabled}
        onClick={() => onChange("device")}
      >
        <Smartphone className="h-4 w-4" /> Điện thoại
      </button>
      <button
        type="button"
        role="tab"
        aria-selected={value === "ip"}
        data-on={value === "ip"}
        disabled={disabled}
        onClick={() => onChange("ip")}
      >
        <Video className="h-4 w-4" /> Camera IP
      </button>
    </div>
  );
}

/* --------------------------- Thẻ camera trong app --------------------------- */
function PanelCheckInScanner() {
  const [source, setSource] = useState<CameraSource>("device");
  const [framing, setFraming] = useState<Framing | null>(null);
  const framingRef = useRef<Framing | null>(null);
  const onFraming = useCallback((f: Framing) => {
    framingRef.current = f;
    setFraming(f);
  }, []);
  const device = useBurstCheckIn(framingRef);
  const ip = useIpBurstCheckIn();
  const [toast, setToast] = useState<CheckInToastData | null>(null);

  // Nguồn đang chọn quyết định pha/kết quả hiển thị chung cho cả thẻ.
  const phase = source === "ip" ? ip.phase : device.phase;
  const result = source === "ip" ? ip.result : device.result;

  // Đổi nguồn: tắt nguồn cũ và đặt lại trạng thái để tránh dính kết quả/khung hình cũ.
  const switchSource = useCallback(
    (next: CameraSource) => {
      if (next === source) return;
      device.cancel();
      ip.stop();
      device.reset();
      ip.reset();
      setFraming(null);
      framingRef.current = null;
      setSource(next);
    },
    [source, device, ip],
  );

  // Chọn Camera IP → bật ngay hình trực tiếp để nhân viên thấy mình trong khung trước khi chấm.
  useEffect(() => {
    if (source === "ip" && !ip.active) void ip.start();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [source]);

  useEffect(() => {
    if (phase === "success" && result?.matched) {
      setToast({
        id: Date.now(),
        name: result.fullName || result.username || "Nhân viên",
        time: new Date(result.occurredAt ?? Date.now()).toLocaleTimeString("vi-VN"),
      });
    }
  }, [phase, result]);

  return (
    <div className="cc-grid cc-checkin-grid">
      <CheckInToastHost toast={toast} onExpire={() => setToast(null)} />

      <div className="cc-camera glass">
        {IP_CAMERA_ENABLED && <CameraSourceToggle value={source} onChange={switchSource} />}
        {source === "device" ? (
          <DeviceCameraArea device={device} framing={framing} onFraming={onFraming} />
        ) : (
          <IpCameraArea ip={ip} />
        )}
      </div>
    </div>
  );
}

/** Khu camera webcam thiết bị (có cổng căn khung MediaPipe + thanh giữ khung). */
function DeviceCameraArea({
  device,
  framing,
  onFraming,
}: {
  device: ReturnType<typeof useBurstCheckIn>;
  framing: Framing | null;
  onFraming: (f: Framing) => void;
}) {
  const { videoRef, active, error, phase, hint, result, busy, aiming, holding, run, cancel } = device;
  const showFramingHint = active && !busy && phase !== "success" && framing != null && framing.state !== "good";

  return (
    <>
      <div className="cc-video-wrap">
        <video ref={videoRef} playsInline muted className="cc-video" data-active={active} />
        {!active && (
          <div className="cc-video-empty">
            <Camera className="h-8 w-8" />
            <span>Camera đang tắt</span>
          </div>
        )}
        {active && <FaceTrackingOverlay videoRef={videoRef} active={active} onFraming={onFraming} />}
        {busy ? (
          <div className="cc-scan-hint">
            <Loader2 className="h-3.5 w-3.5 animate-spin" /> {hint}
          </div>
        ) : aiming ? (
          <>
            <div className="cc-scan-hint" data-hold={holding ? "true" : undefined}>
              {hint}
            </div>
            <div className="cc-hold-bar" aria-hidden="true">
              {/* Thanh tự chạy bằng CSS transition trên transform (GPU) — mượt, không giật theo JS. */}
              <span
                data-run={holding ? "true" : "false"}
                style={{ transitionDuration: holding ? `${HOLD_STABLE_MS}ms` : "0ms" }}
              />
            </div>
          </>
        ) : showFramingHint ? (
          <div className="cc-scan-hint" data-warn="true">
            {framing!.hint}
          </div>
        ) : null}

        <AnimatePresence>
          {phase === "success" && result?.matched && (
            <CheckInSuccessOverlay
              name={result.fullName || result.username || "Nhân viên"}
              time={result.occurredAt ? new Date(result.occurredAt).toLocaleTimeString("vi-VN") : null}
              loai={result.loai}
            />
          )}
          {phase === "warning" && <CheckInFailOverlay message={hint} />}
        </AnimatePresence>
      </div>

      {error && <div className="cc-error">{error}</div>}

      <div className="cc-camera-actions">
        <button className="cc-btn cc-btn-primary" onClick={run} disabled={busy || aiming} type="button">
          {busy || aiming ? <Loader2 className="h-4 w-4 animate-spin" /> : <ScanFace className="h-4 w-4" />}{" "}
          {busy
            ? PROCESSING_HINT
            : aiming
              ? "Giữ khuôn mặt trong khung…"
              : active
                ? "Chấm công"
                : "Bật camera & chấm công"}
        </button>
        {active && (
          <button className="cc-btn" onClick={cancel} disabled={busy} type="button">
            Tắt camera
          </button>
        )}
      </div>
    </>
  );
}

/** Khu camera IP: hiển thị hình trực tiếp từ snapshot RTSP, không có cổng căn khung. */
function IpCameraArea({ ip }: { ip: ReturnType<typeof useIpBurstCheckIn> }) {
  const { active, error, frameUrl, phase, hint, result, busy, run } = ip;

  return (
    <>
      <div className="cc-video-wrap">
        {frameUrl ? (
          <img src={frameUrl} alt="Hình trực tiếp từ camera IP" className="cc-video cc-video--ip" data-active="true" />
        ) : (
          <div className="cc-video-empty">
            <Video className="h-8 w-8" />
            <span>{active ? "Đang kết nối camera IP…" : "Camera IP đang tắt"}</span>
          </div>
        )}
        {busy && (
          <div className="cc-scan-hint">
            <Loader2 className="h-3.5 w-3.5 animate-spin" /> {hint}
          </div>
        )}

        <AnimatePresence>
          {phase === "success" && result?.matched && (
            <CheckInSuccessOverlay
              name={result.fullName || result.username || "Nhân viên"}
              time={result.occurredAt ? new Date(result.occurredAt).toLocaleTimeString("vi-VN") : null}
              loai={result.loai}
            />
          )}
          {phase === "warning" && <CheckInFailOverlay message={hint} />}
        </AnimatePresence>
      </div>

      {error && <div className="cc-error">{error}</div>}

      <div className="cc-camera-actions">
        <button className="cc-btn cc-btn-primary" onClick={run} disabled={busy} type="button">
          {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <ScanFace className="h-4 w-4" />}{" "}
          {busy ? PROCESSING_HINT : active ? "Chấm công" : "Kết nối camera IP & chấm công"}
        </button>
      </div>
    </>
  );
}

/* ----------------------------- Kiosk Liquid Glass ----------------------------- */
function BiometricCheckInScanner({
  autoStart = false,
  returnToLoginOnOk = false,
}: Pick<CheckInScannerProps, "autoStart" | "returnToLoginOnOk">) {
  const navigate = useNavigate();
  const [source, setSource] = useState<CameraSource>("device");
  const framingRef = useRef<Framing | null>(null);
  const onFraming = useCallback((f: Framing) => {
    framingRef.current = f;
  }, []);
  const device = useBurstCheckIn(framingRef);
  const ip = useIpBurstCheckIn();

  const active = source === "ip" ? ip.active : device.active;
  const phase = source === "ip" ? ip.phase : device.phase;
  const hint = source === "ip" ? ip.hint : device.hint;
  const result = source === "ip" ? ip.result : device.result;
  const busy = source === "ip" ? ip.busy : device.busy;
  const aiming = source === "ip" ? false : device.aiming;
  const run = source === "ip" ? ip.run : device.run;

  const switchSource = useCallback(
    (next: CameraSource) => {
      if (next === source) return;
      device.cancel();
      ip.stop();
      device.reset();
      ip.reset();
      framingRef.current = null;
      setSource(next);
    },
    [source, device, ip],
  );

  const autoStartedRef = useRef(false);
  useEffect(() => {
    if (autoStart && !autoStartedRef.current) {
      autoStartedRef.current = true;
      void device.run();
    }
    // run đổi mỗi render; chỉ chạy 1 lần khi gắn nên không đưa vào deps.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoStart]);

  // Chọn Camera IP → bật ngay hình trực tiếp làm nền kiosk.
  useEffect(() => {
    if (source === "ip" && !ip.active) void ip.start();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [source]);

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
          : busy || aiming || active
            ? "scanning"
            : "idle";

  const islandPhase =
    phase === "warning" ? "warning" : phase === "success" ? "success" : busy || aiming ? "scanning" : "idle";

  return (
    <div className="cc-bio-shell">
      {/* Webcam thiết bị: ẩn (chỉ dùng để quét), MediaPipe căn khung chạy nền. */}
      <video ref={device.videoRef} playsInline muted className="cc-bio-video" aria-hidden="true" />
      {source === "device" && device.active && (
        <FaceTrackingOverlay videoRef={device.videoRef} active={device.active} drawBox={false} onFraming={onFraming} />
      )}
      {/* Camera IP: hiển thị hình trực tiếp làm nền phía sau đảo kính. */}
      {source === "ip" && ip.frameUrl && (
        <img src={ip.frameUrl} alt="Hình trực tiếp từ camera IP" className="cc-bio-ip-bg" aria-hidden="true" />
      )}

      <div className="cc-bio-source">
        {IP_CAMERA_ENABLED && <CameraSourceToggle value={source} onChange={switchSource} />}
      </div>

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

        {!success && !busy && !aiming && (
          <button className="cc-bio-start" onClick={run} type="button">
            <Camera className="h-4 w-4" /> {phase === "warning" ? "Thử lại" : "Bắt đầu chấm công"}
          </button>
        )}
        {!success && (busy || aiming) && (
          <div className="cc-bio-active-pill">
            <Loader2 className="h-4 w-4 animate-spin" /> {busy ? PROCESSING_HINT : "Giữ khuôn mặt trong khung…"}
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

/** Lớp phủ kết quả thành công hiện trực tiếp trên khung camera. */
function CheckInSuccessOverlay({
  name,
  time,
  loai,
}: {
  name: string;
  time: string | null;
  loai?: string | null;
}) {
  const reducedMotion = useReducedMotion();
  const meta = [loai, time].filter(Boolean).join(" · ");

  return (
    <motion.div
      className="cc-success-overlay"
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: 0.26, ease: "easeOut" }}
    >
      <motion.div
        className="cc-success-card"
        initial={{ scale: 0.92, y: 10, opacity: 0 }}
        animate={{ scale: 1, y: 0, opacity: 1 }}
        exit={{ scale: 0.96, opacity: 0 }}
        transition={{ type: "spring", stiffness: 260, damping: 22 }}
      >
        <div className="cc-success-mark">
          <motion.span
            className="cc-success-disc"
            initial={reducedMotion ? { scale: 1, opacity: 1 } : { scale: 0.82, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            transition={
              reducedMotion
                ? { duration: 0.12 }
                : { type: "spring", stiffness: 360, damping: 24, delay: 0.04 }
            }
          />
          <svg className="cc-success-svg" viewBox="0 0 100 100" aria-hidden="true">
            <motion.circle
              cx="50"
              cy="50"
              r="32"
              fill="none"
              stroke="currentColor"
              strokeWidth="4.5"
              strokeLinecap="round"
              initial={{ pathLength: 0, opacity: 0 }}
              animate={{ pathLength: 1, opacity: 1 }}
              transition={
                reducedMotion
                  ? { duration: 0.12 }
                  : { duration: 0.36, ease: "easeOut", delay: 0.08 }
              }
              style={{ rotate: -90, transformOrigin: "50% 50%" }}
            />
            <motion.path
              d="M34 51 L45 62 L67 38"
              fill="none"
              stroke="currentColor"
              strokeWidth="7"
              strokeLinecap="round"
              strokeLinejoin="round"
              initial={{ pathLength: 0, opacity: 0 }}
              animate={{ pathLength: 1, opacity: 1 }}
              transition={
                reducedMotion
                  ? { duration: 0.14, delay: 0.06 }
                  : { duration: 0.34, ease: "easeOut", delay: 0.32 }
              }
            />
          </svg>
        </div>

        <div className="cc-success-text">
          <div className="cc-success-eyebrow">Chấm công thành công</div>
          <div className="cc-success-name">{name}</div>
          {meta && <div className="cc-success-meta">{meta}</div>}
        </div>
      </motion.div>
    </motion.div>
  );
}

/** Lớp phủ kết quả thất bại hiện trực tiếp trên khung camera. */
function CheckInFailOverlay({ message }: { message: string }) {
  const reducedMotion = useReducedMotion();

  return (
    <motion.div
      className="cc-fail-overlay"
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: 0.26, ease: "easeOut" }}
    >
      <motion.div
        className="cc-fail-card"
        initial={{ scale: 0.92, y: 10, opacity: 0 }}
        animate={{ scale: 1, y: 0, opacity: 1 }}
        exit={{ scale: 0.96, opacity: 0 }}
        transition={{ type: "spring", stiffness: 260, damping: 22 }}
      >
        <motion.div
          className="cc-fail-mark"
          initial={reducedMotion ? { x: 0 } : { x: 0 }}
          animate={reducedMotion ? { x: 0 } : { x: [0, -4, 4, -2, 0] }}
          transition={reducedMotion ? { duration: 0 } : { duration: 0.36, delay: 0.18, ease: "easeInOut" }}
        >
          <motion.span
            className="cc-fail-disc"
            initial={reducedMotion ? { scale: 1, opacity: 1 } : { scale: 0.82, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            transition={
              reducedMotion
                ? { duration: 0.12 }
                : { type: "spring", stiffness: 360, damping: 24, delay: 0.04 }
            }
          />
          <svg className="cc-fail-svg" viewBox="0 0 100 100" aria-hidden="true">
            <motion.circle
              cx="50"
              cy="50"
              r="32"
              fill="none"
              stroke="currentColor"
              strokeWidth="4.5"
              strokeLinecap="round"
              initial={{ pathLength: 0, opacity: 0 }}
              animate={{ pathLength: 1, opacity: 1 }}
              transition={
                reducedMotion
                  ? { duration: 0.12 }
                  : { duration: 0.32, ease: "easeOut", delay: 0.08 }
              }
              style={{ rotate: -90, transformOrigin: "50% 50%" }}
            />
            <motion.path
              d="M39 39 L61 61"
              fill="none"
              stroke="currentColor"
              strokeWidth="6.5"
              strokeLinecap="round"
              initial={{ pathLength: 0, opacity: 0 }}
              animate={{ pathLength: 1, opacity: 1 }}
              transition={
                reducedMotion
                  ? { duration: 0.12, delay: 0.06 }
                  : { duration: 0.26, ease: "easeOut", delay: 0.24 }
              }
            />
            <motion.path
              d="M61 39 L39 61"
              fill="none"
              stroke="currentColor"
              strokeWidth="6.5"
              strokeLinecap="round"
              initial={{ pathLength: 0, opacity: 0 }}
              animate={{ pathLength: 1, opacity: 1 }}
              transition={
                reducedMotion
                  ? { duration: 0.12, delay: 0.06 }
                  : { duration: 0.26, ease: "easeOut", delay: 0.42 }
              }
            />
          </svg>
        </motion.div>

        <div className="cc-fail-text">
          <div className="cc-fail-eyebrow">Chấm công chưa thành công</div>
          {message && <div className="cc-fail-message">{message}</div>}
        </div>
      </motion.div>
    </motion.div>
  );
}

/**
 * Khu chứa toast chấm công (góc dưới phải). Toast trượt vào từ bên phải; sau ~5s (khi thanh
 * xanh chạy hết) thì trượt lên trên và mờ dần rồi biến mất. AnimatePresence lo hoạt ảnh thoát.
 */
function CheckInToastHost({ toast, onExpire }: { toast: CheckInToastData | null; onExpire: () => void }) {
  return (
    <div className="cc-toast-host" aria-live="polite">
      <AnimatePresence>
        {toast && <CheckInToast key={toast.id} data={toast} onExpire={onExpire} />}
      </AnimatePresence>
    </div>
  );
}

const TOAST_DURATION_MS = 5000;

function CheckInToast({ data, onExpire }: { data: CheckInToastData; onExpire: () => void }) {
  // Đặt hẹn giờ MỘT lần theo id của toast (không reset theo mỗi lần cha render lại).
  const onExpireRef = useRef(onExpire);
  useEffect(() => {
    onExpireRef.current = onExpire;
  });
  useEffect(() => {
    const id = window.setTimeout(() => onExpireRef.current(), TOAST_DURATION_MS);
    return () => window.clearTimeout(id);
  }, [data.id]);

  return (
    <motion.div
      className="cc-toast"
      role="status"
      initial={{ x: 420, opacity: 0 }}
      animate={{ x: 0, opacity: 1 }}
      exit={{ y: -72, opacity: 0 }}
      transition={{ type: "spring", stiffness: 300, damping: 30, opacity: { duration: 0.35 } }}
    >
      <div className="cc-toast-icon">
        <UserCheck className="h-5 w-5" />
      </div>
      <div className="cc-toast-body">
        <div className="cc-toast-title">{data.name} đã chấm công</div>
        <div className="cc-toast-sub">vào lúc {data.time}</div>
        <div className="cc-toast-bars" aria-hidden="true">
          <span />
        </div>
      </div>
    </motion.div>
  );
}
