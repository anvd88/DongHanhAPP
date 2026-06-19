import * as signalR from "@microsoft/signalr";

/**
 * Kết nối SignalR tới hub /hubs/changes của backend. Khi backend phát tín hiệu
 * "changed" (có thay đổi dữ liệu từ web/desktop/bất kỳ nguồn nào), gọi mọi listener
 * để các trang tự refetch. Client chỉ LẮNG NGHE WebSocket — không poll.
 */
type Listener = () => void;

const listeners = new Set<Listener>();
let connection: signalR.HubConnection | null = null;

export function subscribeRealtime(cb: Listener): () => void {
  listeners.add(cb);
  return () => {
    listeners.delete(cb);
  };
}

export function startRealtime() {
  if (connection) return;

  connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/changes")
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  connection.on("changed", () => {
    for (const cb of listeners) cb();
  });

  // Tự thử lại nếu lần kết nối đầu thất bại (backend chưa sẵn sàng).
  const connect = () => {
    connection?.start().catch(() => setTimeout(connect, 3000));
  };
  connect();
}
