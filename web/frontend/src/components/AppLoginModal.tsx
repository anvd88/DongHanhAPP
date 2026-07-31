import { useCallback, useEffect, useRef, useState } from "react";
import { CheckCircle2, CircleX, Loader2, Smartphone, Timer, X } from "lucide-react";
import { api } from "../lib/api";
import { useAuth, webSessionId } from "../lib/auth";
import type { User } from "../lib/types";
import { Button } from "./ui";

const CLIENT_MODE = "mobile_app";
const POLL_INTERVAL_MS = 1_000;

type AppLoginSession = {
  requestCode: string;
  pollToken: string;
  expiresAt: string;
  clientMode: typeof CLIENT_MODE;
};

type AppLoginPollResult =
  | { status: "pending" | "opened" | "rejected" | "expired"; expiresAt?: string }
  // Không có "token": phiên đi bằng cookie HttpOnly do máy chủ đặt trong chính phản hồi này.
  | { status: "authenticated"; user: User; expiresAt?: string };

type Phase = "starting" | "waiting" | "opened" | "authenticated" | "rejected" | "expired" | "error";

export function AppLoginModal({ onClose }: { onClose: () => void }) {
  const { completeExternalLogin } = useAuth();
  const [session, setSession] = useState<AppLoginSession | null>(null);
  const [phase, setPhase] = useState<Phase>("starting");
  const [error, setError] = useState("");
  const [secondsLeft, setSecondsLeft] = useState(5 * 60);
  const pollTokenRef = useRef<string | null>(null);

  const openApp = useCallback((requestCode: string) => {
    const request = encodeURIComponent(requestCode);
    const mode = encodeURIComponent(CLIENT_MODE);
    const fallback = encodeURIComponent(new URL("/tai-apk", window.location.origin).toString());
    // Gửi mã ở cả data URI và Intent extras. Một số bản Chrome/OEM chỉ đưa app lên
    // foreground nhưng làm mất query; extras là đường dự phòng để app vẫn nhận yêu cầu.
    window.location.href = `intent://app-login?request=${request}&client_mode=${mode}#Intent;scheme=ketoanhr;package=com.ketoanapk.hr;S.request_code=${request};S.client_mode=${mode};S.browser_fallback_url=${fallback};end`;
  }, []);

  useEffect(() => {
    let stopped = false;
    void api.postPublic<AppLoginSession>("/api/auth/app-login/start", {
      sid: webSessionId(),
      clientMode: CLIENT_MODE,
    }).then((created) => {
      if (stopped) {
        void api.postPublic("/api/auth/app-login/cancel", {
          pollToken: created.pollToken,
          clientMode: CLIENT_MODE,
        }).catch(() => {});
        return;
      }
      pollTokenRef.current = created.pollToken;
      setSession(created);
      setPhase("waiting");
      openApp(created.requestCode);
    }).catch((err) => {
      if (!stopped) {
        setError(err instanceof Error ? err.message : "Không tạo được yêu cầu đăng nhập ứng dụng.");
        setPhase("error");
      }
    });
    return () => { stopped = true; };
  }, [openApp]);

  useEffect(() => {
    if (!session || phase === "authenticated" || phase === "rejected" || phase === "expired") return;
    const expiresAt = new Date(session.expiresAt).getTime();
    const update = () => setSecondsLeft(Math.max(0, Math.ceil((expiresAt - Date.now()) / 1_000)));
    update();
    const timer = window.setInterval(update, 250);
    return () => window.clearInterval(timer);
  }, [phase, session]);

  useEffect(() => {
    if (!session || phase === "authenticated" || phase === "rejected" || phase === "expired") return;
    let stopped = false;
    let timer: number | undefined;
    const controller = new AbortController();

    const poll = async () => {
      try {
        const result = await api.postPublic<AppLoginPollResult>("/api/auth/app-login/poll", {
          pollToken: session.pollToken,
          clientMode: CLIENT_MODE,
        }, controller.signal);
        if (stopped) return;
        if (result.status === "authenticated") {
          completeExternalLogin({ user: result.user });
          void api.postPublic("/api/auth/app-login/ack", {
            pollToken: session.pollToken,
            clientMode: CLIENT_MODE,
          }).catch(() => {});
          pollTokenRef.current = null;
          setPhase("authenticated");
          return;
        }
        if (result.status === "opened") setPhase("opened");
        if (result.status === "rejected") { setPhase("rejected"); return; }
        if (result.status === "expired") { setPhase("expired"); return; }
        timer = window.setTimeout(poll, POLL_INTERVAL_MS);
      } catch (err) {
        if (stopped || controller.signal.aborted) return;
        setError(err instanceof Error ? err.message : "Mất kết nối tới máy chủ.");
        timer = window.setTimeout(poll, 1_500);
      }
    };
    timer = window.setTimeout(poll, 250);
    return () => {
      stopped = true;
      controller.abort();
      if (timer !== undefined) window.clearTimeout(timer);
    };
  }, [completeExternalLogin, phase, session]);

  const close = useCallback(() => {
    const token = pollTokenRef.current;
    pollTokenRef.current = null;
    if (token) void api.postPublic("/api/auth/app-login/cancel", {
      pollToken: token,
      clientMode: CLIENT_MODE,
    }).catch(() => {});
    onClose();
  }, [onClose]);

  const minutes = Math.floor(secondsLeft / 60).toString().padStart(2, "0");
  const seconds = (secondsLeft % 60).toString().padStart(2, "0");

  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-black/60 p-0 sm:items-center sm:p-4" onClick={close}>
      <section
        className="fade-in w-full rounded-t-3xl bg-white p-6 shadow-2xl dark:bg-slate-950 sm:max-w-sm sm:rounded-3xl"
        role="dialog"
        aria-modal="true"
        aria-labelledby="app-login-title"
        onClick={(event) => event.stopPropagation()}
      >
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="text-xs font-bold uppercase tracking-wider text-blue-600 dark:text-blue-400">Web mobile · Android</p>
            <h2 id="app-login-title" className="mt-1 text-lg font-bold text-slate-900 dark:text-white">Đăng nhập bằng ứng dụng Nhân sự</h2>
          </div>
          <button type="button" onClick={close} className="rounded-xl p-2 text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800" aria-label="Đóng">
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="flex min-h-56 flex-col items-center justify-center text-center" aria-live="polite">
          {(phase === "starting" || phase === "waiting") && <>
            <Loader2 className="h-11 w-11 animate-spin text-blue-600" />
            <p className="mt-5 font-semibold text-slate-900 dark:text-white">Đang mở ứng dụng Nhân sự…</p>
            <p className="mt-2 text-sm leading-6 text-slate-500">Xác nhận trong ứng dụng để đăng nhập trình duyệt này.</p>
          </>}
          {phase === "opened" && <>
            <Smartphone className="h-12 w-12 text-blue-600" />
            <p className="mt-5 font-semibold text-slate-900 dark:text-white">Ứng dụng đã nhận yêu cầu</p>
            <p className="mt-2 text-sm leading-6 text-slate-500">Hãy chọn “Đăng nhập” trong ứng dụng Nhân sự.</p>
          </>}
          {phase === "authenticated" && <>
            <CheckCircle2 className="h-12 w-12 text-emerald-500" />
            <p className="mt-5 font-semibold text-emerald-700 dark:text-emerald-400">Đăng nhập thành công</p>
          </>}
          {phase === "rejected" && <>
            <CircleX className="h-12 w-12 text-red-500" />
            <p className="mt-5 font-semibold text-slate-900 dark:text-white">Bạn đã từ chối yêu cầu</p>
          </>}
          {phase === "expired" && <>
            <Timer className="h-12 w-12 text-slate-500" />
            <p className="mt-5 font-semibold text-slate-900 dark:text-white">Yêu cầu đã hết hạn</p>
          </>}
          {phase === "error" && <>
            <CircleX className="h-12 w-12 text-red-500" />
            <p className="mt-5 font-semibold text-slate-900 dark:text-white">Không thể mở đăng nhập ứng dụng</p>
          </>}
        </div>

        {error && <p className="mb-3 rounded-xl bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">{error}</p>}
        {session && phase !== "authenticated" && phase !== "expired" && phase !== "rejected" && (
          <>
            <p className="mb-3 flex items-center justify-center gap-2 text-xs text-slate-500"><Timer className="h-4 w-4" /> Hiệu lực {minutes}:{seconds}</p>
            <Button type="button" className="w-full py-3" onClick={() => openApp(session.requestCode)}>
              <Smartphone className="h-4 w-4" /> Mở lại ứng dụng Nhân sự
            </Button>
          </>
        )}
        {(phase === "expired" || phase === "rejected" || phase === "error") && (
          <Button type="button" variant="soft" className="w-full py-3" onClick={close}>Đóng</Button>
        )}
      </section>
    </div>
  );
}
