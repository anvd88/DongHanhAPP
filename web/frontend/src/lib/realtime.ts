import * as signalR from "@microsoft/signalr";
import { tokenStore } from "./api";
import { appUrl } from "./appConfig";

/**
 * Kết nối SignalR tới hub /hubs/changes của backend. Backend phát tín hiệu "changed"
 * kèm "phạm vi" (scope):
 *   • "data"     — thay đổi nghiệp vụ (chứng từ, thanh toán, gia công, chấm công…)
 *   • "presence" — hiện diện online + thay đổi tài khoản
 *   • "chat"     — tin nhắn mới/sửa/gỡ (chỉ gửi tới ĐÚNG thành viên cuộc trò chuyện)
 * Client chỉ LẮNG NGHE WebSocket — không poll. Mỗi listener đăng ký phạm vi quan tâm để
 * tránh refetch thừa: trang chat không tải lại khi có thay đổi kế toán và ngược lại.
 */
export type RealtimeScope =
  | "data"
  | "presence"
  | "chat"
  | "feedback"
  | "hr"
  | "tasks"
  | "portal"
  | "config"
  | "audit"
  | "talent"
  | "liveness"
  | "release"
  | "all";
type Listener = (scope: RealtimeScope, payload?: string) => void;

const listeners = new Set<Listener>();
let connection: signalR.HubConnection | null = null;

function emitChanged(scope: RealtimeScope, payload?: string) {
  for (const cb of listeners) cb(scope, payload);
}

/* ----- Tín hiệu bắt tay WebRTC để gửi tệp thẳng P2P qua LAN (xem lib/filetransfer.ts) -----
 * Server (ChangesHub.Relay) chỉ TRUNG CHUYỂN gói tín hiệu giữa 2 người; nội dung tệp KHÔNG
 * đi qua server. "signal" mang (fromUsername, payloadJson). */
type SignalListener = (fromUsername: string, payload: string) => void;
const signalListeners = new Set<SignalListener>();
type FeedbackResolvedListener = (message: string) => void;
const feedbackResolvedListeners = new Set<FeedbackResolvedListener>();

/** Lắng nghe tín hiệu WebRTC gửi tới mình. */
export function subscribeSignal(cb: SignalListener): () => void {
  signalListeners.add(cb);
  return () => {
    signalListeners.delete(cb);
  };
}

export function subscribeFeedbackResolved(cb: FeedbackResolvedListener): () => void {
  feedbackResolvedListeners.add(cb);
  return () => {
    feedbackResolvedListeners.delete(cb);
  };
}

/** Gửi một gói tín hiệu WebRTC tới đúng một người dùng (qua hub). Trả về true nếu đã gửi. */
export async function sendSignal(toUsername: string, payload: string): Promise<boolean> {
  if (!connection || connection.state !== signalR.HubConnectionState.Connected) return false;
  try {
    await connection.invoke("Relay", toUsername, payload);
    return true;
  } catch {
    return false;
  }
}

/** Đăng ký lắng nghe. Bỏ trống `scopes` = nhận mọi phạm vi (tương thích ngược). */
export function subscribeRealtime(cb: Listener, scopes?: RealtimeScope[]): () => void {
  const wrapped: Listener =
    scopes && scopes.length
      ? (scope, payload) => {
          if (scope === "all" || scopes.includes(scope)) cb(scope, payload);
        }
      : cb;
  listeners.add(wrapped);
  return () => {
    listeners.delete(wrapped);
  };
}

export function startRealtime() {
  if (connection) return;
  // Chỉ kết nối hub khi ĐÃ đăng nhập. Trang công khai (đăng nhập, kiosk, tải APK, tính toán) không có
  // token → tránh spam negotiate 401 mỗi 3 giây (nhất là màn kiosk chạy liên tục). Sau khi đăng nhập,
  // auth.tsx gọi restartRealtime() để kết nối lại.
  if (!tokenStore.get()) return;

  connection = new signalR.HubConnectionBuilder()
    .withUrl(appUrl("/hubs/changes"), {
      // Gửi JWT qua query "access_token" để backend định danh kết nối (chat nhắm đúng người).
      accessTokenFactory: () => tokenStore.get() ?? "",
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();
  const current = connection;

  connection.on("changed", (scope: RealtimeScope = "data", payload?: string) => {
    emitChanged(scope, payload);
  });

  connection.on("signal", (fromUsername: string, payload: string) => {
    for (const cb of signalListeners) cb(fromUsername, payload);
  });

  connection.on("feedbackResolved", (message: string) => {
    for (const cb of feedbackResolvedListeners) cb(message);
  });

  connection.onreconnected(() => {
    emitChanged("all");
  });

  connection.onclose(() => {
    if (connection !== current) return;
    connection = null;
    setTimeout(startRealtime, 3000);
  });

  // Tự thử lại nếu lần kết nối đầu thất bại (backend chưa sẵn sàng).
  const connect = () => {
    if (connection !== current) return;
    current.start().catch(() => {
      if (connection === current) setTimeout(connect, 3000);
    });
  };
  connect();
}

export async function restartRealtime() {
  const old = connection;
  connection = null;
  if (old) {
    try {
      await old.stop();
    } catch {
      // The old connection may already be gone; build a fresh one with the current token.
    }
  }
  startRealtime();
}

export async function stopRealtime() {
  const old = connection;
  connection = null;
  if (old) {
    try {
      await old.stop();
    } catch {
      // Best effort when signing out or switching sessions.
    }
  }
}
