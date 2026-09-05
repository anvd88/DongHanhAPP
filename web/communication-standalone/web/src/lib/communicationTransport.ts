import * as signalR from "@microsoft/signalr";
import { session } from "./api";
import { appUrl } from "./appConfig";

/** SignalR transport kept exclusively for chat/call/P2P until communication becomes its own app. */
type SignalListener = (fromUsername: string, payload: string) => void;
type ChatChangedListener = () => void;

const signalListeners = new Set<SignalListener>();
const chatChangedListeners = new Set<ChatChangedListener>();
let connection: signalR.HubConnection | null = null;
let retryTimer: number | null = null;

export function subscribeSignal(cb: SignalListener): () => void {
  signalListeners.add(cb);
  return () => { signalListeners.delete(cb); };
}

export function subscribeCommunicationChanged(cb: ChatChangedListener): () => void {
  chatChangedListeners.add(cb);
  return () => { chatChangedListeners.delete(cb); };
}

export async function sendSignal(toUsername: string, payload: string): Promise<boolean> {
  if (!connection || connection.state !== signalR.HubConnectionState.Connected) return false;
  try { await connection.invoke("Relay", toUsername, payload); return true; }
  catch { return false; }
}

export function startCommunicationTransport() {
  if (connection || !session.isSignedIn()) return;
  const current = new signalR.HubConnectionBuilder()
    .withUrl(appUrl("/hubs/changes"), { withCredentials: true })
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();
  connection = current;
  current.on("changed", (scope: string) => {
    if (connection !== current || scope !== "chat") return;
    for (const cb of chatChangedListeners) cb();
  });
  current.on("signal", (from: string, payload: string) => {
    if (connection !== current) return;
    for (const cb of signalListeners) cb(from, payload);
  });
  current.onreconnected(() => {
    if (connection !== current) return;
    for (const cb of chatChangedListeners) cb();
  });
  current.onclose(() => {
    if (connection !== current) return;
    connection = null;
    retryTimer = window.setTimeout(startCommunicationTransport, 3000);
  });
  current.start().catch(() => {
    if (connection !== current) return;
    connection = null;
    retryTimer = window.setTimeout(startCommunicationTransport, 3000);
  });
}

export async function stopCommunicationTransport() {
  if (retryTimer !== null) window.clearTimeout(retryTimer);
  retryTimer = null;
  const old = connection;
  connection = null;
  if (old) await old.stop().catch(() => {});
}

export async function restartCommunicationTransport() {
  await stopCommunicationTransport();
  startCommunicationTransport();
}
