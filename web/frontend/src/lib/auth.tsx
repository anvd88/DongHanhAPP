/* eslint-disable react-refresh/only-export-components -- Context provider và hook dùng chung cần ở cùng module. */
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import {
  LoginTransitionLayer,
  type LoginTransitionOrigin,
  type LoginTransitionPhase,
  type LoginTransitionState,
} from "../components/LoginTransitionLayer";
import { api, session } from "./api";
import type { User } from "./types";
import { ensureWaterDailyLogin } from "./waterReminderClock";
import { ensureEyeDailyLogin } from "./eyeReminderClock";
import { loadUserPreferences } from "./userPreferences";
import { restartRealtime, stopRealtime } from "./realtime";

interface AuthCtx {
  user: User | null;
  loading: boolean;
  login: (username: string, password: string) => Promise<{ user: User }>;
  pollQrLogin: (pollToken: string, signal?: AbortSignal) => Promise<QrLoginPollResult>;
  completeExternalLogin: (login: { user: User }, origin?: LoginTransitionOrigin) => void;
  completeLoginWithTransition: (login: { user: User }, origin: LoginTransitionOrigin) => void;
  loginTransitionPhase: LoginTransitionPhase | null;
  revealLoginTransition: () => void;
  logout: () => void;
  updateUser: (u: User) => void;
}

export type QrLoginAccount = {
  username: string;
  fullName: string;
  avatarUrl?: string | null;
};

export type QrLoginPollResult =
  | { status: "pending" | "rejected" | "expired" }
  | { status: "scanned"; account: QrLoginAccount }
  // Không có "token": phiên đi bằng cookie HttpOnly mà máy chủ đặt trong chính phản hồi này.
  | { status: "authenticated"; user: User };

const Ctx = createContext<AuthCtx>(null!);
export const useAuth = () => useContext(Ctx);
const LOGIN_TRANSITION_MIN_COVERED_MS = 600;
const LOGIN_TRANSITION_COVER_FALLBACK_MS = 1_350;
const LOGIN_TRANSITION_REMOVE_FALLBACK_MS = 1_500;

// Tự động đăng xuất khi không hoạt động (bảo vệ tài khoản trên thiết bị dùng chung).
// Ngưỡng lấy từ VITE_IDLE_LOGOUT_MINUTES (mặc định 15 phút; đặt 0 để tắt).
export const IDLE_LOGOUT_FLAG = "km_idle_logout";
const IDLE_LOGOUT_MS = (() => {
  const raw = Number(import.meta.env.VITE_IDLE_LOGOUT_MINUTES);
  const minutes = Number.isFinite(raw) && raw >= 0 ? raw : 15;
  return Math.round(minutes * 60_000);
})();

// Mã định danh phiên web ổn định theo từng trình duyệt (để backend ghi nhận hiện diện online).
// crypto.randomUUID chỉ có ở ngữ cảnh bảo mật (https/localhost); LAN chạy http nên cần fallback.
const SID_KEY = "km_sid";
export function webSessionId(): string {
  let sid = localStorage.getItem(SID_KEY);
  if (!sid) {
    sid = crypto.randomUUID?.() ?? `web-${Date.now()}-${Math.random().toString(16).slice(2)}`;
    localStorage.setItem(SID_KEY, sid);
  }
  return sid;
}

// HIỆN DIỆN ONLINE giờ đi theo KẾT NỐI SignalR, không còn nhịp tim HTTP 45s ở đây nữa. Backend đánh
// dấu online ngay khi hub kết nối (ChangesHub.OnConnectedAsync) và làm tươi last_seen theo lô mỗi 45s
// cho các phiên đang mở socket (HubPresenceRefresher) — trình duyệt đang mở app là đã có sẵn kết nối
// đó. Đóng tab ⇒ socket rớt ⇒ ngừng làm tươi ⇒ desktop thấy offline sau ≤90s, y như nhịp tim cũ ngừng.
// (Ứng dụng Android vẫn dùng POST /api/auth/heartbeat khi SignalR tắt/ở nền — endpoint đó được giữ lại.)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(() => session.isSignedIn());
  const [loginTransition, setLoginTransition] = useState<LoginTransitionState | null>(null);
  const loginTransitionId = useRef(0);
  const pendingTransitionLogin = useRef<{ user: User } | null>(null);
  const loginCoverTimer = useRef<number | null>(null);
  const loginReadyRevealTimer = useRef<number | null>(null);
  const loginRevealSafetyTimer = useRef<number | null>(null);
  const loginTransitionRemoveTimer = useRef<number | null>(null);
  const loginTransitionCoveredAt = useRef<number | null>(null);
  const loginRevealStarted = useRef(false);

  useEffect(() => {
    // Token cũ trong localStorage (phiên bản trước khi chuyển sang cookie) không còn giá trị gì —
    // dọn ngay lúc khởi động để không còn bản sao phiên đăng nhập nào mà JavaScript đọc được.
    session.clearLocal();
    if (!session.isSignedIn()) return;
    api
      .get<User>("/api/auth/me")
      .then((currentUser) => {
        ensureWaterDailyLogin(currentUser.id);
        ensureEyeDailyLogin(currentUser.id);
        loadUserPreferences(currentUser.id).catch(() => {});
        setUser(currentUser);
        void restartRealtime();
      })
      .catch(() => session.clearLocal())
      .finally(() => setLoading(false));
  }, []);

  // (Hiện diện online đã chuyển sang kết nối SignalR — xem ghi chú ở đầu tệp; không còn effect nhịp tim.)

  // Phiên đã nằm sẵn trong cookie do máy chủ đặt ở chính phản hồi đăng nhập — không có token nào để
  // lưu, và đó là điểm mấu chốt của đợt này. Ở đây chỉ còn dựng trạng thái trong ứng dụng.
  const finishLogin = useCallback((res: { user: User }) => {
    // Dựng user trước; các tiện ích phụ không được phép chặn một phiên đã xác thực thành công
    // (một số trình duyệt có thể chặn storage hoặc ném lỗi quota).
    setUser(res.user);
    try { ensureWaterDailyLogin(res.user.id); } catch { /* Không chặn đăng nhập vì đồng hồ nhắc nước. */ }
    try { ensureEyeDailyLogin(res.user.id); } catch { /* Không chặn đăng nhập vì đồng hồ nhắc mắt. */ }
    try { loadUserPreferences(res.user.id).catch(() => {}); } catch { /* Storage có thể bị chặn. */ }
    void restartRealtime().catch(() => { /* Kết nối hub sẽ tự thử lại ở luồng realtime. */ });
  }, []);

  const startLoginTransitionReveal = useCallback(() => {
    if (loginRevealStarted.current) return;
    loginRevealStarted.current = true;
    if (loginReadyRevealTimer.current !== null) {
      window.clearTimeout(loginReadyRevealTimer.current);
      loginReadyRevealTimer.current = null;
    }
    if (loginRevealSafetyTimer.current !== null) {
      window.clearTimeout(loginRevealSafetyTimer.current);
      loginRevealSafetyTimer.current = null;
    }
    if (loginTransitionRemoveTimer.current !== null) {
      window.clearTimeout(loginTransitionRemoveTimer.current);
    }

    setLoginTransition((current) => current ? { ...current, phase: "revealing" } : current);
    loginTransitionRemoveTimer.current = window.setTimeout(() => {
      setLoginTransition(null);
      loginTransitionRemoveTimer.current = null;
    }, LOGIN_TRANSITION_REMOVE_FALLBACK_MS);
  }, []);

  const revealLoginTransition = useCallback(() => {
    if (loginRevealStarted.current) return;
    const coveredAt = loginTransitionCoveredAt.current;
    const coveredFor = coveredAt === null ? LOGIN_TRANSITION_MIN_COVERED_MS : Date.now() - coveredAt;
    const remaining = Math.max(0, LOGIN_TRANSITION_MIN_COVERED_MS - coveredFor);

    if (remaining > 0) {
      if (loginReadyRevealTimer.current !== null) {
        window.clearTimeout(loginReadyRevealTimer.current);
      }
      loginReadyRevealTimer.current = window.setTimeout(startLoginTransitionReveal, remaining);
      return;
    }

    startLoginTransitionReveal();
  }, [startLoginTransitionReveal]);

  const completeLoginTransitionCover = useCallback(() => {
    if (loginCoverTimer.current !== null) {
      window.clearTimeout(loginCoverTimer.current);
      loginCoverTimer.current = null;
    }

    const loginResult = pendingTransitionLogin.current;
    if (!loginResult) return;
    pendingTransitionLogin.current = null;
    try {
      finishLogin(loginResult);
    } finally {
      loginTransitionCoveredAt.current = Date.now();
      setLoginTransition((current) =>
        current?.phase === "covering" ? { ...current, phase: "waiting" } : current,
      );

      // Chỉ mở màn sau khi route đích báo đã render. Đây là lối thoát an toàn cho
      // một route bất thường không dựng được shell, không phải timing chính của hiệu ứng.
      loginRevealSafetyTimer.current = window.setTimeout(revealLoginTransition, 8_000);
    }
  }, [finishLogin, revealLoginTransition]);

  const completeLoginWithTransition = useCallback((res: { user: User }, origin: LoginTransitionOrigin) => {
    if (loginCoverTimer.current !== null) window.clearTimeout(loginCoverTimer.current);
    if (loginReadyRevealTimer.current !== null) window.clearTimeout(loginReadyRevealTimer.current);
    if (loginRevealSafetyTimer.current !== null) window.clearTimeout(loginRevealSafetyTimer.current);
    if (loginTransitionRemoveTimer.current !== null) window.clearTimeout(loginTransitionRemoveTimer.current);

    pendingTransitionLogin.current = res;
    loginTransitionCoveredAt.current = null;
    loginRevealStarted.current = false;
    loginTransitionId.current += 1;
    setLoginTransition({
      id: loginTransitionId.current,
      phase: "covering",
      origin,
    });

    // animationend là mốc chính; timer chỉ dự phòng khi tab nền làm trình duyệt
    // không phát sự kiện kết thúc animation đúng lúc.
    loginCoverTimer.current = window.setTimeout(
      completeLoginTransitionCover,
      LOGIN_TRANSITION_COVER_FALLBACK_MS,
    );
  }, [completeLoginTransitionCover]);

  // Đăng nhập từ nguồn ngoài (QR/ứng dụng): nếu bên gọi biết vùng người dùng đang nhìn (khung mã QR)
  // thì chuyển cảnh khởi phát từ đúng đó; không thì lấy giữa màn hình.
  const completeExternalLogin = useCallback((res: { user: User }, origin?: LoginTransitionOrigin) => {
    completeLoginWithTransition(res, origin ?? {
      left: window.innerWidth / 2,
      top: window.innerHeight / 2,
      width: 0,
      height: 0,
    });
  }, [completeLoginWithTransition]);

  useLayoutEffect(() => {
    const phase = loginTransition?.phase;
    if (phase) document.documentElement.dataset.loginTransitionPhase = phase;
    else delete document.documentElement.dataset.loginTransitionPhase;
    return () => {
      delete document.documentElement.dataset.loginTransitionPhase;
    };
  }, [loginTransition?.phase]);

  useEffect(() => () => {
    if (loginCoverTimer.current !== null) window.clearTimeout(loginCoverTimer.current);
    if (loginReadyRevealTimer.current !== null) window.clearTimeout(loginReadyRevealTimer.current);
    if (loginRevealSafetyTimer.current !== null) window.clearTimeout(loginRevealSafetyTimer.current);
    if (loginTransitionRemoveTimer.current !== null) window.clearTimeout(loginTransitionRemoveTimer.current);
    pendingTransitionLogin.current = null;
  }, []);

  const login = async (username: string, password: string) => {
    return api.post<{ user: User }>("/api/auth/login", { username, password, sid: webSessionId() });
  };

  // Poll QR: máy chủ đặt cookie phiên ngay trong phản hồi "authenticated" (không trả token ra).
  // Kết quả QR/App cũng đi qua cùng transition; finishLogin vẫn là điểm duy nhất dựng phiên trong app.
  const pollQrLogin = useCallback((pollToken: string, signal?: AbortSignal) =>
    api.postPublic<QrLoginPollResult>("/api/auth/qr/poll", { pollToken }, signal), []);

  const logout = () => {
    // Chỉ máy chủ xoá được cookie HttpOnly, nên đăng xuất PHẢI gọi lên máy chủ — không còn cách
    // "xoá localStorage cho xong" như trước. Gọi hỏng (mất mạng) thì cookie vẫn còn; phiên sẽ chết
    // theo hạn cookie hoặc khi người dùng đăng nhập lại.
    api.post("/api/auth/logout", { sid: webSessionId() }).catch(() => {});
    session.clearLocal();
    void stopRealtime();
    if (loginCoverTimer.current !== null) window.clearTimeout(loginCoverTimer.current);
    if (loginReadyRevealTimer.current !== null) window.clearTimeout(loginReadyRevealTimer.current);
    if (loginRevealSafetyTimer.current !== null) window.clearTimeout(loginRevealSafetyTimer.current);
    if (loginTransitionRemoveTimer.current !== null) window.clearTimeout(loginTransitionRemoveTimer.current);
    loginCoverTimer.current = null;
    loginReadyRevealTimer.current = null;
    loginRevealSafetyTimer.current = null;
    loginTransitionRemoveTimer.current = null;
    pendingTransitionLogin.current = null;
    loginTransitionCoveredAt.current = null;
    loginRevealStarted.current = false;
    setLoginTransition(null);
    setUser(null);
  };

  // Tự động đăng xuất khi không hoạt động: mỗi thao tác (chuột/phím/chạm/cuộn) đặt lại đồng hồ;
  // hết ngưỡng mà không có thao tác nào → đăng xuất và đặt cờ để trang đăng nhập báo lý do.
  useEffect(() => {
    if (!user || IDLE_LOGOUT_MS <= 0) return;
    let timer: ReturnType<typeof setTimeout>;
    const onIdle = () => {
      try { sessionStorage.setItem(IDLE_LOGOUT_FLAG, "1"); } catch { /* bỏ qua */ }
      logout();
    };
    const reset = () => {
      clearTimeout(timer);
      timer = setTimeout(onIdle, IDLE_LOGOUT_MS);
    };
    const events: (keyof WindowEventMap)[] = ["mousemove", "mousedown", "keydown", "touchstart", "scroll", "wheel"];
    events.forEach((e) => window.addEventListener(e, reset, { passive: true }));
    reset();
    return () => {
      clearTimeout(timer);
      events.forEach((e) => window.removeEventListener(e, reset));
    };
  }, [user]);

  return (
    <Ctx.Provider
      value={{
        user,
        loading,
        login,
        pollQrLogin,
        completeExternalLogin,
        completeLoginWithTransition,
        loginTransitionPhase: loginTransition?.phase ?? null,
        revealLoginTransition,
        logout,
        updateUser: setUser,
      }}
    >
      {children}
      <LoginTransitionLayer
        transition={loginTransition}
        onCoverComplete={completeLoginTransitionCover}
        onRevealComplete={() => {
          if (loginTransitionRemoveTimer.current !== null) {
            window.clearTimeout(loginTransitionRemoveTimer.current);
            loginTransitionRemoveTimer.current = null;
          }
          loginTransitionCoveredAt.current = null;
          setLoginTransition(null);
        }}
      />
    </Ctx.Provider>
  );
}
