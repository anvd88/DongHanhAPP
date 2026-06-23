import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { api, tokenStore } from "./api";
import type { User } from "./types";
import { ensureWaterDailyLogin } from "./waterReminderClock";
import { ensureEyeDailyLogin } from "./eyeReminderClock";

interface AuthCtx {
  user: User | null;
  loading: boolean;
  login: (username: string, password: string) => Promise<void>;
  logout: () => void;
  updateUser: (u: User) => void;
}

const Ctx = createContext<AuthCtx>(null!);
export const useAuth = () => useContext(Ctx);

// Mã định danh phiên web ổn định theo từng trình duyệt (để backend ghi nhận hiện diện online).
// crypto.randomUUID chỉ có ở ngữ cảnh bảo mật (https/localhost); LAN chạy http nên cần fallback.
const SID_KEY = "km_sid";
function sessionId(): string {
  let sid = localStorage.getItem(SID_KEY);
  if (!sid) {
    sid = crypto.randomUUID?.() ?? `web-${Date.now()}-${Math.random().toString(16).slice(2)}`;
    localStorage.setItem(SID_KEY, sid);
  }
  return sid;
}

// Báo "đang online" cho hệ thống (app desktop đọc last_seen của user_sessions). Nếu tài khoản
// bị khóa, backend trả 401 và lớp api tự đăng xuất → ở đây chỉ cần nuốt lỗi.
function sendHeartbeat() {
  api.post("/api/auth/heartbeat", { sid: sessionId() }).catch(() => {});
}

function sendOfflineKeepalive() {
  const token = tokenStore.get();
  if (!token) return;
  fetch("/api/auth/logout", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ sid: sessionId() }),
    keepalive: true,
  }).catch(() => {});
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!tokenStore.get()) {
      setLoading(false);
      return;
    }
    api
      .get<User>("/api/auth/me")
      .then((currentUser) => {
        ensureWaterDailyLogin(currentUser.id);
        ensureEyeDailyLogin(currentUser.id);
        setUser(currentUser);
      })
      .catch(() => tokenStore.clear())
      .finally(() => setLoading(false));
  }, []);

  // Nhịp tim hiện diện: khi đã đăng nhập, ping ngay rồi lặp mỗi 45s và mỗi khi tab hiện lại.
  // 90s là ngưỡng "online" phía desktop nên 45s giữ trạng thái luôn tươi.
  useEffect(() => {
    if (!user) return;
    sendHeartbeat();
    const id = setInterval(sendHeartbeat, 45_000);
    const onVisible = () => {
      if (document.visibilityState === "visible") sendHeartbeat();
    };
    const onPageLeave = () => sendOfflineKeepalive();
    document.addEventListener("visibilitychange", onVisible);
    window.addEventListener("pagehide", onPageLeave);
    window.addEventListener("beforeunload", onPageLeave);
    return () => {
      clearInterval(id);
      document.removeEventListener("visibilitychange", onVisible);
      window.removeEventListener("pagehide", onPageLeave);
      window.removeEventListener("beforeunload", onPageLeave);
    };
  }, [user]);

  const login = async (username: string, password: string) => {
    const res = await api.post<{ token: string; user: User }>("/api/auth/login", { username, password });
    tokenStore.set(res.token);
    ensureWaterDailyLogin(res.user.id);
    ensureEyeDailyLogin(res.user.id);
    setUser(res.user); // kích hoạt heartbeat ngay qua effect ở trên
  };

  const logout = () => {
    api.post("/api/auth/logout", { sid: sessionId() }).catch(() => {}); // tắt hiện diện ngay (best-effort)
    tokenStore.clear();
    setUser(null);
    location.href = "/login";
  };

  return <Ctx.Provider value={{ user, loading, login, logout, updateUser: setUser }}>{children}</Ctx.Provider>;
}
