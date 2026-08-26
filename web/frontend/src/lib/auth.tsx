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
import {
  LogoutTransitionLayer,
  type LogoutAvatarSnapshot,
  type LogoutTransitionPhase,
  type LogoutTransitionState,
} from "../components/LogoutTransitionLayer";
import { ConfirmDialog } from "../components/ConfirmDialog";
import { LogOut } from "lucide-react";
import { api, session } from "./api";
import type { User } from "./types";
import { ensureWaterDailyLogin } from "./waterReminderClock";
import { ensureEyeDailyLogin } from "./eyeReminderClock";
import { loadUserPreferences } from "./userPreferences";
import { restartRealtime, stopRealtime } from "./realtime";
import { resetFileTransfers } from "./filetransfer";
import { resetWebCall } from "./webcall";
import { useAppNotifications } from "../components/app-notifications-context";

interface AuthCtx {
  user: User | null;
  loading: boolean;
  login: (username: string, password: string) => Promise<{ user: User }>;
  pollQrLogin: (pollToken: string, signal?: AbortSignal) => Promise<QrLoginPollResult>;
  completeExternalLogin: (login: { user: User }, origin?: LoginTransitionOrigin) => void;
  completeLoginWithTransition: (login: { user: User }, origin: LoginTransitionOrigin) => void;
  loginTransitionPhase: LoginTransitionPhase | null;
  revealLoginTransition: () => void;
  logout: (avatarElement?: HTMLElement | null) => void;
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
const LOGOUT_TRANSITION_COVER_FALLBACK_MS = 1_250;
const LOGOUT_TRANSITION_REMOVE_FALLBACK_MS = 1_500;

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

function logoutInitials(user: User) {
  const name = (user.fullName || user.username || "?").trim();
  const parts = name.split(/\s+/).filter(Boolean);
  return (parts.length > 1 ? `${parts[0][0]}${parts.at(-1)?.[0] || ""}` : name.slice(0, 2)).toUpperCase();
}

function visibleLogoutAvatar() {
  return Array
    .from(document.querySelectorAll<HTMLElement>("[data-logout-avatar-origin='true']"))
    .find((element) => {
      const rect = element.getBoundingClientRect();
      const style = window.getComputedStyle(element);
      return rect.width >= 20 && rect.height >= 20 && style.display !== "none" && style.visibility !== "hidden";
    }) ?? null;
}

function captureLogoutAvatar(user: User, preferred?: HTMLElement | null): LogoutAvatarSnapshot {
  const avatar = preferred && preferred.isConnected ? preferred : visibleLogoutAvatar();
  const rect = avatar?.getBoundingClientRect();
  const style = avatar ? window.getComputedStyle(avatar) : null;
  const image = avatar?.querySelector<HTMLImageElement>("img");
  const fallbackSize = 48;
  return {
    origin: rect && rect.width >= 20 && rect.height >= 20
      ? { left: rect.left, top: rect.top, width: rect.width, height: rect.height }
      : {
          left: window.innerWidth / 2 - fallbackSize / 2,
          top: window.innerHeight / 2 - fallbackSize / 2,
          width: fallbackSize,
          height: fallbackSize,
        },
    imageSrc: image?.currentSrc || image?.src || user.avatarUrl || null,
    label: image ? "" : avatar?.textContent?.trim() || logoutInitials(user),
    backgroundImage: style?.backgroundImage || "linear-gradient(145deg, #3457d5, #129887)",
    backgroundColor: style?.backgroundColor || "#3457d5",
    color: style?.color || "#ffffff",
    fontFamily: style?.fontFamily || "inherit",
    fontSize: style?.fontSize || "0.9rem",
    fontWeight: style?.fontWeight || "800",
  };
}

// HIỆN DIỆN ONLINE giờ đi theo KẾT NỐI SignalR, không còn nhịp tim HTTP 45s ở đây nữa. Backend đánh
// dấu online ngay khi hub kết nối (ChangesHub.OnConnectedAsync) và làm tươi last_seen theo lô mỗi 45s
// cho các phiên đang mở socket (HubPresenceRefresher) — trình duyệt đang mở app là đã có sẵn kết nối
// đó. Đóng tab ⇒ socket rớt ⇒ ngừng làm tươi ⇒ desktop thấy offline sau ≤90s, y như nhịp tim cũ ngừng.
// (Ứng dụng Android vẫn dùng POST /api/auth/heartbeat khi SignalR tắt/ở nền — endpoint đó được giữ lại.)

export function AuthProvider({ children }: { children: ReactNode }) {
  const { clear: clearNotifications } = useAppNotifications();
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(() => session.isSignedIn());
  const [loginTransition, setLoginTransition] = useState<LoginTransitionState | null>(null);
  const [logoutConfirmOpen, setLogoutConfirmOpen] = useState(false);
  const [logoutTransition, setLogoutTransition] = useState<LogoutTransitionState | null>(null);
  const loginTransitionId = useRef(0);
  const logoutTransitionId = useRef(0);
  const logoutTransitionPhase = useRef<LogoutTransitionPhase | null>(null);
  const pendingTransitionLogin = useRef<{ user: User } | null>(null);
  const loginCoverTimer = useRef<number | null>(null);
  const loginReadyRevealTimer = useRef<number | null>(null);
  const loginRevealSafetyTimer = useRef<number | null>(null);
  const loginTransitionRemoveTimer = useRef<number | null>(null);
  const loginTransitionCoveredAt = useRef<number | null>(null);
  const loginRevealStarted = useRef(false);
  const pendingLogoutAvatarElement = useRef<HTMLElement | null>(null);
  const logoutCoverTimer = useRef<number | null>(null);
  const logoutRemoveTimer = useRef<number | null>(null);

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
  // lưu. Xóa thông báo của danh tính trước trước khi dựng trạng thái cho tài khoản vừa xác thực.
  const finishLogin = useCallback((res: { user: User }) => {
    // Dựng user trước; các tiện ích phụ không được phép chặn một phiên đã xác thực thành công
    // (một số trình duyệt có thể chặn storage hoặc ném lỗi quota).
    // Dọn dấu vết của danh tính trước KHÔNG được phép làm hỏng phiên vừa xác thực: nếu một trong
    // các hàm này ném lỗi mà không ai bắt thì `setUser` bên dưới không bao giờ chạy — người dùng
    // đăng nhập đúng nhưng ở lại màn hình đăng nhập (đã bị chuyển cảnh làm mờ) và tưởng là trắng màn.
    try { resetFileTransfers(); } catch { /* Gửi tệp P2P hỏng không được chặn đăng nhập. */ }
    try { resetWebCall(); } catch { /* Cuộc gọi cũ hỏng không được chặn đăng nhập. */ }
    try { clearNotifications(); } catch { /* Dọn toast hỏng không được chặn đăng nhập. */ }
    setUser(res.user);
    try { ensureWaterDailyLogin(res.user.id); } catch { /* Không chặn đăng nhập vì đồng hồ nhắc nước. */ }
    try { ensureEyeDailyLogin(res.user.id); } catch { /* Không chặn đăng nhập vì đồng hồ nhắc mắt. */ }
    try { loadUserPreferences(res.user.id).catch(() => {}); } catch { /* Storage có thể bị chặn. */ }
    void restartRealtime().catch(() => { /* Kết nối hub sẽ tự thử lại ở luồng realtime. */ });
  }, [clearNotifications]);

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

  useLayoutEffect(() => {
    const phase = logoutTransition?.phase;
    const origin = logoutTransition?.avatar.origin;
    if (phase) {
      document.documentElement.dataset.logoutTransitionPhase = phase;
      if (origin) {
        document.documentElement.style.setProperty("--logout-origin-x", `${origin.left + origin.width / 2}px`);
        document.documentElement.style.setProperty("--logout-origin-y", `${origin.top + origin.height / 2}px`);
      }
    } else {
      delete document.documentElement.dataset.logoutTransitionPhase;
    }
    return () => {
      delete document.documentElement.dataset.logoutTransitionPhase;
      document.documentElement.style.removeProperty("--logout-origin-x");
      document.documentElement.style.removeProperty("--logout-origin-y");
    };
  }, [logoutTransition]);

  useEffect(() => () => {
    if (loginCoverTimer.current !== null) window.clearTimeout(loginCoverTimer.current);
    if (loginReadyRevealTimer.current !== null) window.clearTimeout(loginReadyRevealTimer.current);
    if (loginRevealSafetyTimer.current !== null) window.clearTimeout(loginRevealSafetyTimer.current);
    if (loginTransitionRemoveTimer.current !== null) window.clearTimeout(loginTransitionRemoveTimer.current);
    if (logoutCoverTimer.current !== null) window.clearTimeout(logoutCoverTimer.current);
    if (logoutRemoveTimer.current !== null) window.clearTimeout(logoutRemoveTimer.current);
    pendingTransitionLogin.current = null;
    pendingLogoutAvatarElement.current = null;
    logoutTransitionPhase.current = null;
  }, []);

  const login = async (username: string, password: string) => {
    // Đăng nhập là endpoint công khai: 401 ở đây là sai thông tin đăng nhập, không phải một phiên
    // đang hoạt động bị hết hạn. postPublic giữ nguyên thông báo thật từ máy chủ và vẫn gửi cookie
    // bootstrap HttpOnly cùng-origin vừa được khởi tạo ở màn Login.
    return api.postPublic<{ user: User }>("/api/auth/login", { username, password, sid: webSessionId() });
  };

  // Poll QR: máy chủ đặt cookie phiên ngay trong phản hồi "authenticated" (không trả token ra).
  // Kết quả QR/App cũng đi qua cùng transition; finishLogin vẫn là điểm duy nhất dựng phiên trong app.
  const pollQrLogin = useCallback((pollToken: string, signal?: AbortSignal) =>
    api.postPublic<QrLoginPollResult>("/api/auth/qr/poll", { pollToken }, signal), []);

  const finishLocalLogout = useCallback(() => {
    session.clearLocal();
    resetFileTransfers();
    resetWebCall();
    void stopRealtime();
    clearNotifications();

    // Nếu người dùng đăng xuất ngay sau khi đăng nhập, dừng sạch chuyển cảnh chiều vào trước khi
    // dựng màn hình đăng nhập theo chiều ngược lại.
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
  }, [clearNotifications]);

  const removeLogoutTransition = useCallback(() => {
    if (logoutCoverTimer.current !== null) {
      window.clearTimeout(logoutCoverTimer.current);
      logoutCoverTimer.current = null;
    }
    if (logoutRemoveTimer.current !== null) {
      window.clearTimeout(logoutRemoveTimer.current);
      logoutRemoveTimer.current = null;
    }
    logoutTransitionPhase.current = null;
    pendingLogoutAvatarElement.current = null;
    setLogoutTransition(null);
  }, []);

  const completeLogoutTransitionCover = useCallback(() => {
    if (logoutTransitionPhase.current !== "covering") return;
    logoutTransitionPhase.current = "waiting";
    if (logoutCoverTimer.current !== null) {
      window.clearTimeout(logoutCoverTimer.current);
      logoutCoverTimer.current = null;
    }

    // Chỉ xóa cookie và tháo app sau khi curtain đã phủ kín. Cách này tránh request nền nhận 401
    // rồi hard-redirect giữa lúc người dùng vẫn còn nhìn thấy giao diện cũ.
    void api.post("/api/auth/logout", { sid: webSessionId() }).catch(() => {});
    finishLocalLogout();
    setLogoutTransition((current) => current ? { ...current, phase: "waiting" } : current);
  }, [finishLocalLogout]);

  const revealLogoutTransition = useCallback(() => {
    if (logoutTransitionPhase.current !== "waiting") return;
    logoutTransitionPhase.current = "revealing";
    setLogoutTransition((current) => (
      current?.phase === "waiting" ? { ...current, phase: "revealing" } : current
    ));
    if (logoutRemoveTimer.current !== null) window.clearTimeout(logoutRemoveTimer.current);
    logoutRemoveTimer.current = window.setTimeout(
      removeLogoutTransition,
      LOGOUT_TRANSITION_REMOVE_FALLBACK_MS,
    );
  }, [removeLogoutTransition]);

  const confirmLogout = useCallback(() => {
    if (!user || logoutTransition || logoutTransitionPhase.current !== null) return;

    // Đo lại ở đúng thời điểm xác nhận để resize hoặc thay đổi layout trong lúc mở hộp thoại
    // không làm iris nhảy khỏi avatar mà người dùng vừa chọn.
    const avatar = captureLogoutAvatar(user, pendingLogoutAvatarElement.current);

    setLogoutConfirmOpen(false);
    logoutTransitionId.current += 1;
    logoutTransitionPhase.current = "covering";
    setLogoutTransition({
      id: logoutTransitionId.current,
      phase: "covering",
      avatar,
      accountName: user.fullName?.trim() || user.username,
    });

    // animation completion là mốc chính; timer chỉ dự phòng khi tab nền không phát callback.
    logoutCoverTimer.current = window.setTimeout(
      completeLogoutTransitionCover,
      LOGOUT_TRANSITION_COVER_FALLBACK_MS,
    );
  }, [completeLogoutTransitionCover, logoutTransition, user]);

  // Các nút đăng xuất thủ công chỉ mở bước xác nhận. Việc xóa phiên thật bắt đầu ở confirmLogout.
  const logout = useCallback((avatarElement?: HTMLElement | null) => {
    if (!user || logoutTransition || logoutTransitionPhase.current !== null) return;
    pendingLogoutAvatarElement.current = avatarElement?.isConnected ? avatarElement : null;
    setLogoutConfirmOpen(true);
  }, [logoutTransition, user]);

  // Đăng xuất do hết thời gian chờ không được dừng lại ở một hộp thoại không có người thao tác.
  const logoutImmediately = useCallback(() => {
    setLogoutConfirmOpen(false);
    removeLogoutTransition();
    void api.post("/api/auth/logout", { sid: webSessionId() }).catch(() => {});
    finishLocalLogout();
  }, [finishLocalLogout, removeLogoutTransition]);

  // Tự động đăng xuất khi không hoạt động: mỗi thao tác (chuột/phím/chạm/cuộn) đặt lại đồng hồ;
  // hết ngưỡng mà không có thao tác nào → đăng xuất và đặt cờ để trang đăng nhập báo lý do.
  useEffect(() => {
    if (!user || IDLE_LOGOUT_MS <= 0) return;
    let timer: ReturnType<typeof setTimeout>;
    const onIdle = () => {
      try { sessionStorage.setItem(IDLE_LOGOUT_FLAG, "1"); } catch { /* bỏ qua */ }
      logoutImmediately();
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
  }, [logoutImmediately, user]);

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
      <ConfirmDialog
        open={logoutConfirmOpen}
        title="Bạn muốn đăng xuất?"
        description={<>Phiên làm việc của <strong>{user?.fullName || user?.username || "tài khoản hiện tại"}</strong> sẽ được kết thúc.</>}
        detail="Mọi dữ liệu đã lưu vẫn được giữ nguyên. Bạn sẽ cần đăng nhập lại để tiếp tục làm việc."
        confirmLabel="Đăng xuất"
        cancelLabel="Ở lại"
        busyLabel="Đang đăng xuất..."
        tone="danger"
        icon={<LogOut className="h-6 w-6" />}
        onClose={() => setLogoutConfirmOpen(false)}
        onConfirm={confirmLogout}
      />
      <LogoutTransitionLayer
        transition={logoutTransition}
        onCoverComplete={completeLogoutTransitionCover}
        onLoginReady={revealLogoutTransition}
        onRevealComplete={removeLogoutTransition}
      />
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
