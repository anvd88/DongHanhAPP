import * as signalR from "@microsoft/signalr";
import { session } from "./api";
import { appUrl } from "./appConfig";

/**
 * Kết nối SignalR tới hub /hubs/changes của backend. Backend phát tín hiệu "changed"
 * kèm "phạm vi" (scope):
 *   • "data"     — thay đổi nghiệp vụ (chứng từ, thanh toán, gia công, chấm công…)
 *   • "presence" — hiện diện online + thay đổi tài khoản
 *   • "chat"     — tin nhắn mới/sửa/gỡ (chỉ gửi tới ĐÚNG thành viên cuộc trò chuyện)
 *   • "notify"   — hộp thư thông báo có dòng mới (chuông trên header)
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
  | "attendance"
  // "notify" — có thông báo mới trong hộp thư (bảng web_notifications). Tín hiệu KHÔNG mang nội
  // dung: mỗi máy khách tự gọi /api/notifications và chỉ nhận được phần của chính mình.
  | "notify"
  // "access" — quyền của CHÍNH tài khoản này vừa bị admin thay đổi. Chỉ gửi tới đúng người đó
  // (Clients.User), không phát ra toàn hệ thống. Frontend nạp lại hồ sơ truy cập → menu/layout/route
  // cập nhật ngay mà không phải đăng nhập lại. Xem lib/access.tsx.
  | "access"
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
  // Chỉ kết nối hub khi ĐÃ đăng nhập. Trang công khai (đăng nhập, kiosk, tải APK, tính toán) chưa có
  // phiên → tránh spam negotiate 401 mỗi 3 giây (nhất là màn kiosk chạy liên tục). Sau khi đăng nhập,
  // auth.tsx gọi restartRealtime() để kết nối lại.
  if (!session.isSignedIn()) return;

  connection = new signalR.HubConnectionBuilder()
    // KHÔNG còn accessTokenFactory: phiên nằm trong cookie HttpOnly và trình duyệt tự đính cookie
    // vào cả negotiate lẫn WebSocket handshake (cùng origin). Nhờ vậy token không còn xuất hiện trên
    // URL — chỗ mà nó sẽ bị ghi lại trong log máy chủ, log proxy và lịch sử trình duyệt.
    // (Ứng dụng Android vẫn dùng query access_token; backend vẫn chấp nhận đường đó cho app.)
    //
    // Đường /hubs được máy chủ chốt bằng kiểm tra ORIGIN chứ không bằng header CSRF — xem Program.cs.
    // Lý do: WebSocket handshake không gắn được header tuỳ ý, nên CSRF token không bảo vệ nổi nó,
    // trong khi Origin thì bảo vệ được cả handshake lẫn negotiate.
    .withUrl(appUrl("/hubs/changes"), { withCredentials: true })
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();
  const current = connection;

  connection.on("changed", (scope: RealtimeScope = "data", payload?: string) => {
    if (connection !== current) return;
    emitChanged(scope, payload);
  });

  connection.on("signal", (fromUsername: string, payload: string) => {
    if (connection !== current) return;
    for (const cb of signalListeners) cb(fromUsername, payload);
  });

  connection.on("feedbackResolved", (message: string) => {
    if (connection !== current) return;
    for (const cb of feedbackResolvedListeners) cb(message);
  });

  connection.onreconnected(() => {
    if (connection !== current) return;
    emitChanged("all");
  });

  connection.onclose(() => {
    if (connection !== current) return;
    connection = null;
    setTimeout(startRealtime, 3000);
  });

  // Tự thử lại nếu lần kết nối đầu thất bại (backend chưa sẵn sàng, hoặc phiên chưa kịp có).
  //
  // DỰNG LẠI KẾT NỐI MỚI chứ không gọi start() lại trên đối tượng cũ: đối tượng cũ giữ nguyên trạng
  // thái phiên lúc nó được tạo. Trước đây nó chỉ mang token, nay là cookie/đăng nhập — nên nếu lần
  // đầu thất bại vì CHƯA đăng nhập, thử lại đối tượng cũ sẽ hỏng mãi mãi kể cả sau khi đã đăng nhập
  // xong. Dựng lại thì mỗi lần thử là một lần đọc lại trạng thái phiên hiện tại.
  current.start().catch(() => {
    if (connection !== current) return;
    connection = null;
    setTimeout(startRealtime, 3000);
  });
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
