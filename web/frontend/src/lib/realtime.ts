import * as signalR from "@microsoft/signalr";
import { tokenStore } from "./api";

/**
 * Kết nối SignalR tới hub /hubs/changes của backend. Backend phát tín hiệu "changed"
 * kèm "phạm vi" (scope):
 *   • "data"     — thay đổi nghiệp vụ (chứng từ, thanh toán, gia công, chấm công…)
 *   • "presence" — hiện diện online + thay đổi tài khoản
 *   • "chat"     — tin nhắn mới/sửa/gỡ (chỉ gửi tới ĐÚNG thành viên cuộc trò chuyện)
 * Client chỉ LẮNG NGHE WebSocket — không poll. Mỗi listener đăng ký phạm vi quan tâm để
 * tránh refetch thừa: trang chat không tải lại khi có thay đổi kế toán và ngược lại.
 */
export type RealtimeScope = "data" | "presence" | "chat";
type Listener = (scope: RealtimeScope, payload?: string) => void;

const listeners = new Set<Listener>();
let connection: signalR.HubConnection | null = null;

/** Đăng ký lắng nghe. Bỏ trống `scopes` = nhận mọi phạm vi (tương thích ngược). */
export function subscribeRealtime(cb: Listener, scopes?: RealtimeScope[]): () => void {
  const wrapped: Listener =
    scopes && scopes.length
      ? (scope, payload) => {
          if (scopes.includes(scope)) cb(scope, payload);
        }
      : cb;
  listeners.add(wrapped);
  return () => {
    listeners.delete(wrapped);
  };
}

export function startRealtime() {
  if (connection) return;

  connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/changes", {
      // Gửi JWT qua query "access_token" để backend định danh kết nối (chat nhắm đúng người).
      accessTokenFactory: () => tokenStore.get() ?? "",
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  connection.on("changed", (scope: RealtimeScope = "data", payload?: string) => {
    for (const cb of listeners) cb(scope, payload);
  });

  // Tự thử lại nếu lần kết nối đầu thất bại (backend chưa sẵn sàng).
  const connect = () => {
    connection?.start().catch(() => setTimeout(connect, 3000));
  };
  connect();
}
