import { useCallback, useEffect, useRef, useState } from "react";
import { ArrowLeft, CheckCircle2, CircleX, Loader2, RefreshCw, Smartphone, Timer, X } from "lucide-react";
import { QRCodeSVG } from "qrcode.react";
import { ApiError, api } from "../lib/api";
import { useAuth, webSessionId, type QrLoginAccount } from "../lib/auth";
import { Button } from "./ui";

type QrSessionResponse = {
  qrCode: string;
  pollToken: string;
  expiresAt: string;
  clientMode: "desktop_qr";
};

type QrSession = QrSessionResponse & {
  /** Mốc hết hạn tính theo đồng hồ của chính máy này (đã chặn trường hợp lệch giờ với máy chủ). */
  deadline: number;
};

const QR_LIFETIME_SECONDS = 5 * 60;
const POLL_INTERVAL_MS = 1_500;

function accountInitials(account: QrLoginAccount) {
  const name = account.fullName.trim() || account.username.trim();
  const words = name.split(/\s+/).filter(Boolean);
  return words
    .slice(-2)
    .map((word) => Array.from(word)[0]?.toLocaleUpperCase("vi") ?? "")
    .join("") || "?";
}

export function QrLoginModal({
  onClose,
  embedded = false,
  className,
}: {
  /** Trả về false khi cảnh hiện tại chưa thể đóng (ví dụ đang giao cảnh). */
  onClose: (trigger?: HTMLElement) => boolean | void;
  /** Hiển thị gọn trong thẻ đăng nhập (thay cho ô tài khoản/mật khẩu) thay vì bật hộp thoại nổi. */
  embedded?: boolean;
  /** Lớp bổ sung khi nhúng trong màn hình đăng nhập. */
  className?: string;
}) {
  const { pollQrLogin, completeExternalLogin } = useAuth();
  const [session, setSession] = useState<QrSession | null>(null);
  const [secondsLeft, setSecondsLeft] = useState(QR_LIFETIME_SECONDS);
  const [loading, setLoading] = useState(true);
  const [expired, setExpired] = useState(false);
  const [error, setError] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [rejected, setRejected] = useState(false);
  const [scannedAccount, setScannedAccount] = useState<QrLoginAccount | null>(null);
  const pollTokenRef = useRef<string | null>(null);
  const avatarLookupTokenRef = useRef<string | null>(null);
  const generationRef = useRef(0);
  // Khung hiển thị mã QR/ảnh đại diện: chuyển cảnh đăng nhập lấy đúng ô này làm điểm khởi phát,
  // để các khối bung ra từ chỗ người dùng đang nhìn thay vì từ giữa màn hình trống.
  const stageRef = useRef<HTMLDivElement>(null);

  const cancelToken = useCallback(async (token: string | null) => {
    if (!token) return;
    await api.postPublic("/api/auth/qr/cancel", { pollToken: token }).catch(() => {});
  }, []);

  const refresh = useCallback(async () => {
    const generation = ++generationRef.current;
    const oldToken = pollTokenRef.current;
    pollTokenRef.current = null;
    avatarLookupTokenRef.current = null;
    setLoading(true);
    setError("");
    setExpired(false);
    setConfirmed(false);
    setRejected(false);
    setScannedAccount(null);
    setSession(null);
    setSecondsLeft(QR_LIFETIME_SECONDS);

    try {
      // Hủy xong phiên cũ trước khi tạo phiên mới để điện thoại không thể quét nhầm một QR
      // mà trình duyệt đã thôi theo dõi. Server vẫn có chốt "phiên mới nhất thắng" cho nhiều tab.
      await cancelToken(oldToken);
      if (generation !== generationRef.current) return;

      const created = await api.postPublic<QrSessionResponse>("/api/auth/qr/start", {
        sid: webSessionId(),
        clientMode: "desktop_qr",
      });
      if (generation !== generationRef.current) {
        void cancelToken(created.pollToken);
        return;
      }
      pollTokenRef.current = created.pollToken;
      // Đồng hồ đếm ngược chạy theo mốc của máy chủ, nhưng chỉ khi mốc đó hợp lý so với đồng hồ máy
      // này. Máy lệch giờ thì lấy trọn vòng đời tính từ lúc nhận mã, để không có chuyện mã vừa hiện
      // ra đã bị coi là hết hạn. Phán quyết cuối cùng vẫn của máy chủ qua vòng poll.
      const serverDeadline = new Date(created.expiresAt).getTime();
      const localDeadline = Date.now() + QR_LIFETIME_SECONDS * 1_000;
      const trustServer = Number.isFinite(serverDeadline)
        && serverDeadline > Date.now()
        && serverDeadline <= localDeadline;
      setSession({ ...created, deadline: trustServer ? serverDeadline : localDeadline });
    } catch (err) {
      if (generation === generationRef.current)
        setError(err instanceof Error ? err.message : "Không tạo được mã QR.");
    } finally {
      if (generation === generationRef.current) setLoading(false);
    }
  }, [cancelToken]);

  useEffect(() => {
    const startTimer = window.setTimeout(() => void refresh(), 0);
    return () => {
      window.clearTimeout(startTimer);
      generationRef.current += 1;
      void cancelToken(pollTokenRef.current);
      pollTokenRef.current = null;
    };
  }, [cancelToken, refresh]);

  useEffect(() => {
    if (!session || confirmed || rejected || expired) return;
    const tick = () => {
      const left = Math.max(0, Math.ceil((session.deadline - Date.now()) / 1_000));
      setSecondsLeft(left);
      // Đồng hồ chạy hết là hết hạn — không chờ máy chủ trả "expired" mới biết. Trước đây trạng thái
      // hết hạn chỉ đến từ vòng poll, nên mất mạng (hoặc phiên đã bị dọn) là đồng hồ đứng ở 00:00
      // mà không có đường nào tạo lại mã.
      if (left === 0) setExpired(true);
    };
    tick();
    const timer = window.setInterval(tick, 250);
    return () => window.clearInterval(timer);
  }, [confirmed, expired, rejected, session]);

  useEffect(() => {
    if (!session || expired || confirmed || rejected) return;
    const generation = generationRef.current;
    const controller = new AbortController();
    let stopped = false;
    let terminal = false;
    let timer: number | undefined;

    const poll = async () => {
      let retryDelay = POLL_INTERVAL_MS;
      try {
        const result = await pollQrLogin(session.pollToken, controller.signal);
        if (stopped || generation !== generationRef.current) return;
        if (result.status === "authenticated") {
          terminal = true;
          const stage = stageRef.current?.getBoundingClientRect();
          completeExternalLogin(
            { user: result.user },
            stage && stage.width > 0 && stage.height > 0
              ? { left: stage.left, top: stage.top, width: stage.width, height: stage.height }
              : undefined,
          );
          // Chỉ xóa phiên server sau khi trình duyệt đã nhận phản hồi (và cùng với nó là cookie phiên).
          // Nếu response trước đó bị thất lạc, poll kế tiếp vẫn có thể nhận lại kết quả thay vì buộc
          // người dùng quét QR mới.
          void api.postPublic("/api/auth/qr/ack", { pollToken: session.pollToken }).catch(() => {});
          pollTokenRef.current = null;
          setConfirmed(true);
          return;
        }
        if (result.status === "expired") {
          terminal = true;
          setScannedAccount(null);
          setExpired(true);
          setError("");
          return;
        }
        if (result.status === "rejected") {
          terminal = true;
          setScannedAccount(null);
          setRejected(true);
          setError("");
          return;
        }
        if (result.status === "scanned") {
          setScannedAccount((current) =>
            current?.username === result.account.username
              ? { ...result.account, avatarUrl: current.avatarUrl }
              : result.account,
          );
          // Ảnh đại diện được lấy riêng đúng một lần cho mỗi phiên để poll luôn nhẹ và không giữ ảnh trong RAM server.
          if (avatarLookupTokenRef.current !== session.pollToken) {
            avatarLookupTokenRef.current = session.pollToken;
            void api
              .postPublic<{ avatarUrl?: string | null }>(
                "/api/auth/qr/account",
                { pollToken: session.pollToken },
                controller.signal,
              )
              .then(({ avatarUrl }) => {
                if (stopped || generation !== generationRef.current) return;
                setScannedAccount((current) =>
                  current?.username === result.account.username ? { ...current, avatarUrl: avatarUrl ?? null } : current,
                );
              })
              .catch(() => {});
          }
        } else {
          // Một phiên mới hoặc server đã trả lại trạng thái chờ: không giữ thông tin lần quét cũ.
          setScannedAccount(null);
        }
        setError("");
      } catch (err) {
        if (stopped || controller.signal.aborted || generation !== generationRef.current) return;
        setError(err instanceof Error ? err.message : "Mất kết nối tới máy chủ.");
        // Không để một lỗi 4xx/5xx hay mất mạng tạm thời giết vĩnh viễn vòng poll. Đặc biệt, bản web
        // cũ từng dừng tại 401 do vô tình gửi Bearer hết hạn vào endpoint QR công khai.
        if (err instanceof ApiError && err.status === 401) retryDelay = 250;
      } finally {
        if (!stopped && !terminal && generation === generationRef.current)
          timer = window.setTimeout(poll, retryDelay);
      }
    };

    timer = window.setTimeout(poll, 250);
    return () => {
      stopped = true;
      controller.abort();
      if (timer !== undefined) window.clearTimeout(timer);
    };
  }, [completeExternalLogin, confirmed, expired, pollQrLogin, rejected, session]);

  const close = useCallback((trigger?: HTMLElement) => {
    if (onClose(trigger) === false) return;
    generationRef.current += 1;
    void cancelToken(pollTokenRef.current);
    pollTokenRef.current = null;
  }, [cancelToken, onClose]);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") close();
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [close]);

  const minutes = Math.floor(secondsLeft / 60).toString().padStart(2, "0");
  const seconds = (secondsLeft % 60).toString().padStart(2, "0");
  const progress = Math.max(0, Math.min(100, (secondsLeft / QR_LIFETIME_SECONDS) * 100));
  const scannedName = scannedAccount?.fullName.trim() || scannedAccount?.username || "";
  // Nút tạo lại mã chỉ xuất hiện khi mã không còn dùng được nữa: hết hạn, bị từ chối, hoặc không tạo
  // được mã. Mã đang còn hiệu lực thì ẩn nút đi để không ai bấm nhầm mà mất mã đang chờ quét.
  const canRenew = !confirmed && (expired || rejected || (!loading && !session));

  const body = (
    <>
          <div
            ref={stageRef}
            className="mx-auto flex min-h-[248px] w-full max-w-[280px] items-center justify-center"
            aria-live="polite"
          >
            {confirmed ? (
              <div className="flex flex-col items-center px-4 text-center">
                <CheckCircle2 className="h-14 w-14 text-emerald-500" />
                <p className="mt-4 font-semibold text-emerald-700 dark:text-emerald-400">Đăng nhập thành công</p>
              </div>
            ) : rejected ? (
              <div className="flex flex-col items-center px-4 text-center">
                <CircleX className="h-14 w-14 text-red-500" />
                <p className="mt-4 font-semibold text-slate-900 dark:text-slate-100">Đã từ chối đăng nhập</p>
                <p className="mt-2 text-sm leading-6 text-slate-500 dark:text-slate-400">
                  Yêu cầu đăng nhập đã bị từ chối trên thiết bị di động.
                </p>
              </div>
            ) : expired ? (
              <div className="flex flex-col items-center px-4 text-center">
                <Timer className="h-12 w-12 text-slate-500" />
                <p className="mt-4 font-semibold text-slate-700 dark:text-slate-200">Mã QR đã hết hạn</p>
                <p className="mt-2 text-sm leading-6 text-slate-500 dark:text-slate-400">
                  Hãy tạo mã QR mới rồi quét lại bằng ứng dụng Nhân sự.
                </p>
              </div>
            ) : scannedAccount ? (
              <div className="flex flex-col items-center px-2 text-center">
                <div className="relative flex h-24 w-24 items-center justify-center overflow-hidden rounded-full bg-blue-100 text-2xl font-bold text-blue-700 ring-4 ring-white shadow-md dark:bg-blue-950 dark:text-blue-200 dark:ring-slate-950">
                  {accountInitials(scannedAccount)}
                  {scannedAccount.avatarUrl && (
                    <img
                      src={scannedAccount.avatarUrl}
                      alt={`Ảnh đại diện ${scannedName}`}
                      className="absolute inset-0 h-full w-full object-cover"
                      onError={(event) => { event.currentTarget.hidden = true; }}
                    />
                  )}
                </div>
                <p className="mt-5 text-base font-bold text-slate-900 dark:text-slate-100">{scannedName}</p>
                <p className="mt-2 text-sm leading-6 text-slate-500 dark:text-slate-400">
                  Quét mã thành công. Vui lòng chọn “Đăng nhập” trên thiết bị di động của bạn.
                </p>
              </div>
            ) : (
              <div className="flex aspect-square w-full max-w-[248px] items-center justify-center rounded-2xl border border-slate-200 bg-slate-100 p-3 shadow-inner dark:border-slate-700 dark:bg-slate-900">
                {loading && <Loader2 className="h-8 w-8 animate-spin text-[var(--accent)]" />}
                {!loading && session && (
                  <div className="w-full max-w-[222px] rounded-xl bg-white p-1.5">
                    <QRCodeSVG
                      className="h-auto w-full"
                      value={session.qrCode}
                      size={210}
                      level="M"
                      marginSize={4}
                      title="Mã QR đăng nhập web"
                    />
                  </div>
                )}
                {!loading && !session && (
                  <div className="px-5 text-center text-sm font-medium text-slate-600 dark:text-slate-300">
                    Chưa tạo được mã QR.
                  </div>
                )}
              </div>
            )}
          </div>

          {!confirmed && !rejected && !expired && (
            <div className="mt-4">
              <div className="flex items-center justify-between text-xs font-medium text-slate-500 dark:text-slate-400">
                <span className="flex items-center gap-1.5"><Timer className="h-3.5 w-3.5" /> Hiệu lực còn lại</span>
                <span className={secondsLeft <= 30 ? "text-[var(--danger)]" : "text-[var(--accent)]"}>{minutes}:{seconds}</span>
              </div>
              <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-slate-200 dark:bg-slate-800">
                <div className="h-full rounded-full bg-[var(--accent)] transition-[width] duration-300" style={{ width: `${progress}%` }} />
              </div>
            </div>
          )}

          {!scannedAccount && !rejected && !confirmed && !expired && (
            <div className="mt-4 flex gap-3 rounded-2xl bg-blue-50 p-3 text-xs leading-5 text-slate-600 dark:bg-slate-900 dark:text-slate-300">
              <Smartphone className="mt-0.5 h-5 w-5 shrink-0 text-[var(--accent)]" />
              <p>Mở ứng dụng đã đăng nhập, vào <strong>Cài đặt</strong> → <strong>Đăng nhập web bằng QR</strong>, rồi quét mã này.</p>
            </div>
          )}

          {error && <div className="mt-3 rounded-xl bg-red-50 px-3 py-2.5 text-sm font-medium text-[var(--danger)] dark:bg-red-950">{error}</div>}

          {canRenew && (
            <Button
              type="button"
              variant="soft"
              className="mt-4 w-full py-3"
              onClick={() => void refresh()}
              loading={loading}
            >
              <RefreshCw className="h-4 w-4" /> Tạo mã QR mới
            </Button>
          )}
          {!expired && !confirmed && !rejected && session && (
            <p className="mt-3 flex items-center justify-center gap-2 text-xs text-slate-500 dark:text-slate-400">
              <span className="h-2 w-2 animate-pulse rounded-full bg-emerald-500" />
              {scannedAccount ? "Đang chờ xác nhận trên điện thoại…" : "Đang chờ quét mã…"}
            </p>
          )}
    </>
  );

  if (embedded) {
    return (
      <div className={`login-qr-inline${className ? ` ${className}` : ""}`}>
        {body}
        <button type="button" className="login-qr-back" onClick={(event) => close(event.currentTarget)}>
          <ArrowLeft aria-hidden="true" /> Quay lại đăng nhập tài khoản
        </button>
      </div>
    );
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" onClick={() => close()}>
      <div
        className="fade-in w-full max-w-sm overflow-hidden rounded-3xl border border-slate-200 bg-white shadow-2xl dark:border-slate-700 dark:bg-slate-950"
        role="dialog"
        aria-modal="true"
        aria-labelledby="qr-login-title"
        onClick={(event) => event.stopPropagation()}
      >
        <div className="relative flex min-h-14 items-center justify-center border-b border-slate-200 px-14 dark:border-slate-800">
          <span id="qr-login-title" className="text-center font-semibold text-slate-900 dark:text-slate-100">
            Đăng nhập qua mã QR
          </span>
          <button
            type="button"
            onClick={() => close()}
            className="absolute right-4 rounded-lg p-2 text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-900 dark:text-slate-400 dark:hover:bg-slate-800 dark:hover:text-white"
            aria-label="Đóng"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="p-6">{body}</div>
      </div>
    </div>
  );
}
