/* eslint-disable react-refresh/only-export-components -- Context provider và hook dùng chung cần ở cùng module. */
import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from "react";
import { api, session } from "./api";
import type { User } from "./types";
import { ensureWaterDailyLogin } from "./waterReminderClock";
import { ensureEyeDailyLogin } from "./eyeReminderClock";
import { loadUserPreferences } from "./userPreferences";
import { restartRealtime, stopRealtime } from "./realtime";

interface AuthCtx {
  user: User | null;
  loading: boolean;
  login: (username: string, password: string) => Promise<void>;
  pollQrLogin: (pollToken: string, signal?: AbortSignal) => Promise<QrLoginPollResult>;
  completeExternalLogin: (login: { user: User }) => void;
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
    ensureWaterDailyLogin(res.user.id);
    ensureEyeDailyLogin(res.user.id);
    loadUserPreferences(res.user.id).catch(() => {});
    void restartRealtime(); // kết nối hub cũng chính là tín hiệu "đang online"
    setUser(res.user);
  }, []);

  const login = async (username: string, password: string) => {
    const res = await api.post<{ user: User }>("/api/auth/login", { username, password, sid: webSessionId() });
    finishLogin(res);
  };

  // Poll QR: máy chủ đặt cookie phiên ngay trong phản hồi "authenticated" (không trả token ra).
  // Dùng chung finishLogin để mọi kiểu đăng nhập đều khởi động realtime và nhắc sức khỏe theo cùng một cách.
  const pollQrLogin = useCallback((pollToken: string, signal?: AbortSignal) =>
    api.postPublic<QrLoginPollResult>("/api/auth/qr/poll", { pollToken }, signal), []);

  const logout = () => {
    // Chỉ máy chủ xoá được cookie HttpOnly, nên đăng xuất PHẢI gọi lên máy chủ — không còn cách
    // "xoá localStorage cho xong" như trước. Gọi hỏng (mất mạng) thì cookie vẫn còn; phiên sẽ chết
    // theo hạn cookie hoặc khi người dùng đăng nhập lại.
    api.post("/api/auth/logout", { sid: webSessionId() }).catch(() => {});
    session.clearLocal();
    void stopRealtime();
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
      value={{ user, loading, login, pollQrLogin, completeExternalLogin: finishLogin, logout, updateUser: setUser }}
    >
      {children}
    </Ctx.Provider>
  );
}
