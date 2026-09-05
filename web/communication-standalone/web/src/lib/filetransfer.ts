import { useSyncExternalStore } from "react";
import { api } from "./api";
import { sendSignal, subscribeSignal } from "./communicationTransport";

/* =============================================================================
   Gửi tệp qua LAN — P2P bằng WebRTC DataChannel.

   Nội dung tệp truyền THẲNG giữa 2 trình duyệt qua mạng LAN; server CHỈ:
     • trung chuyển tín hiệu bắt tay (invite/accept/offer/answer/ICE) qua SignalR,
     • lưu MỘT dòng metadata "đã gửi tệp X" (KHÔNG lưu nội dung tệp).

   Vì tệp không được lưu ở đâu cả, CẢ HAI người phải online cùng lúc thì mới truyền được.
   Người nhận phải bấm "Đồng ý" thì quá trình truyền mới bắt đầu.
   ========================================================================== */

const CHUNK_SIZE = 16 * 1024; // 16KB mỗi mảnh — an toàn cho SCTP của WebRTC
const HIGH_WATER = 1 * 1024 * 1024; // 1MB: tạm dừng bơm khi hàng đợi đầy (chống nghẽn RAM)
const LOW_WATER = 256 * 1024; // bơm tiếp khi hàng đợi rút xuống mức này
const INVITE_TIMEOUT_MS = 30_000; // chờ người nhận đồng ý; quá hạn coi như họ offline
// Không dùng STUN/TURN: trong LAN các "host candidate" (IP nội bộ) là đủ để kết nối trực tiếp.
const RTC_CONFIG: RTCConfiguration = { iceServers: [] };

export type TransferStatus =
  | "inviting" // phía gửi: đã mời, chờ người nhận đồng ý
  | "incoming" // phía nhận: có lời mời, chờ quyết định
  | "connecting" // đang bắt tay WebRTC
  | "transferring"
  | "uploading" // phía gửi: người nhận offline → đang tải lên server giữ tạm
  | "stored" // phía gửi: đã lưu trên server, chờ người nhận online tải về
  | "done"
  | "declined"
  | "canceled"
  | "error";

export type TransferInfo = {
  tid: string;
  msgId: number; // id tin nhắn metadata để gắn với bong bóng chat
  convId: string; // id cuộc trò chuyện (để gọi API upload khi fallback)
  peer: string; // username người kia
  direction: "send" | "recv";
  name: string;
  size: number;
  mime?: string;
  status: TransferStatus;
  transferred: number; // số byte đã truyền (cho thanh tiến trình)
  error?: string;
  blobUrl?: string; // phía nhận: link tải xuống sau khi nhận xong
};

// Phần "chạy ngầm" của một phiên (không nằm trong store React).
type Runtime = {
  epoch: number;
  pc?: RTCPeerConnection;
  channel?: RTCDataChannel;
  file?: File; // phía gửi
  chunks?: ArrayBuffer[]; // phía nhận
  pendingIce: RTCIceCandidateInit[]; // ICE đến trước khi có remoteDescription
  inviteTimer?: number;
  cancelBufferWait?: () => void;
  uploading?: boolean; // đang/đã fallback tải lên server (tránh upload trùng)
};

const store = new Map<string, TransferInfo>();
const runtimes = new Map<string, Runtime>();
const transferEpochs = new Map<string, number>();
const byMsgId = new Map<number, string>();
const subscribers = new Set<() => void>();

let snapshot: ReadonlyMap<number, TransferInfo> = new Map();
let initialized = false;
let identityEpoch = 0;
let identityAbortController = new AbortController();

function isCurrentTransfer(tid: string, epoch: number) {
  return epoch === identityEpoch && transferEpochs.get(tid) === epoch;
}

function rebuildSnapshot() {
  const next = new Map<number, TransferInfo>();
  for (const t of store.values()) if (t.msgId > 0) next.set(t.msgId, t);
  snapshot = next;
}

function emit() {
  rebuildSnapshot();
  for (const s of subscribers) s();
}

function setStatus(tid: string, patch: Partial<TransferInfo>, epoch: number) {
  if (!isCurrentTransfer(tid, epoch)) return;
  const cur = store.get(tid);
  if (!cur) return;
  store.set(tid, { ...cur, ...patch });
  emit();
}

function newTid() {
  return (
    (crypto.randomUUID?.() as string | undefined) ??
    `t-${Date.now()}-${Math.random().toString(36).slice(2)}`
  );
}

function isInlineMedia(name: string, mime?: string) {
  const normalizedMime = (mime ?? "").toLowerCase();
  const normalizedName = name.toLowerCase().split(/[?#]/)[0];
  if (normalizedMime.startsWith("image/") || normalizedMime.startsWith("video/")) return true;
  if (/^ghi-am-/.test(normalizedName)) return false;
  return /\.(avif|bmp|gif|heic|heif|jpe?g|png|svg|webp|3gp|avi|m4v|mkv|mov|mp4|mpeg|mpg|ogv|webm)$/.test(normalizedName);
}

function send(peer: string, msg: Record<string, unknown>, epoch: number) {
  if (epoch !== identityEpoch) return;
  void sendSignal(peer, JSON.stringify(msg));
}

function disposeRuntime(rt: Runtime) {
  if (rt.inviteTimer != null) window.clearTimeout(rt.inviteTimer);
  rt.inviteTimer = undefined;
  rt.cancelBufferWait?.();
  rt.cancelBufferWait = undefined;
  if (rt.channel) {
    rt.channel.onopen = null;
    rt.channel.onmessage = null;
    rt.channel.onclose = null;
    rt.channel.onerror = null;
    try {
      rt.channel.close();
    } catch {
      /* ignore */
    }
  }
  if (rt.pc) {
    rt.pc.onicecandidate = null;
    rt.pc.onconnectionstatechange = null;
    rt.pc.ondatachannel = null;
    try {
      rt.pc.close();
    } catch {
      /* ignore */
    }
  }
  rt.channel = undefined;
  rt.pc = undefined;
  rt.chunks = undefined;
  rt.file = undefined;
}

function cleanup(tid: string, keepBlob: boolean, epoch: number) {
  if (!isCurrentTransfer(tid, epoch)) return;
  const rt = runtimes.get(tid);
  if (rt) {
    disposeRuntime(rt);
    runtimes.delete(tid);
  }
  if (!keepBlob) {
    const info = store.get(tid);
    if (info?.blobUrl) URL.revokeObjectURL(info.blobUrl);
  }
}

/* ---------------------------- Phía GỬI ---------------------------- */

/**
 * Khởi tạo phiên gửi (hybrid). Nếu người nhận đang ONLINE → mời để truyền thẳng P2P; nếu họ không
 * phản hồi (timeout) hoặc kết nối P2P hỏng → fallback tải lên server giữ tạm. Nếu người nhận OFFLINE
 * ngay từ đầu → bỏ qua P2P, tải thẳng lên server giữ tạm để họ tải sau.
 */
export function startSend(peer: string, file: File, msgId: number, convId: string, peerOnline: boolean) {
  const epoch = identityEpoch;
  const tid = newTid();
  const mime = file.type || undefined;
  const blobUrl = isInlineMedia(file.name, mime) ? URL.createObjectURL(file) : undefined;
  store.set(tid, {
    tid,
    msgId,
    convId,
    peer,
    direction: "send",
    name: file.name,
    size: file.size,
    mime,
    status: peerOnline ? "inviting" : "uploading",
    transferred: 0,
    blobUrl,
  });
  transferEpochs.set(tid, epoch);
  byMsgId.set(msgId, tid);
  const rt: Runtime = { epoch, file, pendingIce: [] };
  runtimes.set(tid, rt);
  emit();

  if (!peerOnline) {
    void uploadFallback(tid, epoch); // người nhận offline → lưu tạm server luôn
    return;
  }

  send(peer, { t: "invite", tid, msgId, name: file.name, size: file.size, mime }, epoch);
  rt.inviteTimer = window.setTimeout(() => {
    if (isCurrentTransfer(tid, epoch) && store.get(tid)?.status === "inviting") {
      void uploadFallback(tid, epoch); // không phản hồi → lưu tạm server
    }
  }, INVITE_TIMEOUT_MS);
}

/** Người nhận không nhận trực tiếp được → tải nội dung tệp lên server giữ tạm (store-and-forward). */
async function uploadFallback(tid: string, epoch: number) {
  if (!isCurrentTransfer(tid, epoch)) return;
  const requestSignal = identityAbortController.signal;
  const rt = runtimes.get(tid);
  const info = store.get(tid);
  if (!rt?.file || rt.epoch !== epoch || !info) return;
  if (info.status === "done") return; // đã xong P2P thì thôi
  if (rt.uploading) return; // tránh upload trùng
  rt.uploading = true;

  if (rt.inviteTimer != null) window.clearTimeout(rt.inviteTimer);
  rt.inviteTimer = undefined;
  try {
    rt.pc?.close();
  } catch {
    /* ignore */
  }
  // Báo người nhận bỏ lời mời P2P (chuyển sang nhận qua server).
  send(info.peer, { t: "cancel", tid }, epoch);
  setStatus(tid, { status: "uploading", transferred: 0 }, epoch);

  try {
    await api.postBlob(
      `/api/chat/conversations/${info.convId}/messages/${info.msgId}/upload`,
      rt.file,
      requestSignal,
    );
    if (!isCurrentTransfer(tid, epoch)) return;
    setStatus(tid, { status: "stored" }, epoch);
  } catch (e) {
    if (!isCurrentTransfer(tid, epoch)) return;
    setStatus(
      tid,
      { status: "error", error: e instanceof Error ? e.message : "Lưu tệp lên máy chủ thất bại." },
      epoch,
    );
  }
  cleanup(tid, true, epoch); // tệp đã ở trên server; bỏ runtime nhưng giữ dòng trạng thái
}

async function beginOffer(tid: string, epoch: number) {
  if (!isCurrentTransfer(tid, epoch)) return;
  const rt = runtimes.get(tid);
  const info = store.get(tid);
  if (!rt || rt.epoch !== epoch || !info || !rt.file) return;
  if (rt.inviteTimer != null) window.clearTimeout(rt.inviteTimer);
  rt.inviteTimer = undefined;

  const pc = new RTCPeerConnection(RTC_CONFIG);
  rt.pc = pc;
  pc.onicecandidate = (e) => {
    if (isCurrentTransfer(tid, epoch) && e.candidate) {
      send(info.peer, { t: "ice", tid, candidate: e.candidate.toJSON() }, epoch);
    }
  };
  pc.onconnectionstatechange = () => {
    if (isCurrentTransfer(tid, epoch) && pc.connectionState === "failed") {
      void uploadFallback(tid, epoch); // P2P hỏng → lưu tạm server
    }
  };

  const channel = pc.createDataChannel("file", { ordered: true });
  channel.binaryType = "arraybuffer";
  channel.bufferedAmountLowThreshold = LOW_WATER;
  rt.channel = channel;
  channel.onopen = () => {
    if (isCurrentTransfer(tid, epoch)) void pumpFile(tid, epoch);
  };
  channel.onmessage = (e) => {
    if (!isCurrentTransfer(tid, epoch)) return;
    // Phía nhận báo đã nhận xong (hoặc lỗi) qua kênh điều khiển text.
    if (typeof e.data === "string") {
      if (e.data === "done") {
        setStatus(tid, { status: "done", transferred: info.size }, epoch);
        cleanup(tid, !!info.blobUrl, epoch);
      } else if (e.data === "error") {
        setStatus(tid, { status: "error", error: "Người nhận gặp lỗi khi nhận tệp." }, epoch);
        cleanup(tid, false, epoch);
      }
    }
  };

  setStatus(tid, { status: "connecting" }, epoch);
  const offer = await pc.createOffer();
  if (!isCurrentTransfer(tid, epoch) || rt.pc !== pc) return;
  await pc.setLocalDescription(offer);
  if (!isCurrentTransfer(tid, epoch) || rt.pc !== pc) return;
  send(info.peer, { t: "offer", tid, sdp: offer.sdp }, epoch);
}

async function pumpFile(tid: string, epoch: number) {
  if (!isCurrentTransfer(tid, epoch)) return;
  const rt = runtimes.get(tid);
  const info = store.get(tid);
  if (!rt?.channel || rt.epoch !== epoch || !rt.file || !info) return;
  const channel = rt.channel;
  const file = rt.file;
  setStatus(tid, { status: "transferring", transferred: 0 }, epoch);

  let offset = 0;
  while (offset < file.size) {
    if (!isCurrentTransfer(tid, epoch) || channel.readyState !== "open") return; // hủy giữa chừng
    const slice = file.slice(offset, offset + CHUNK_SIZE);
    const buf = await slice.arrayBuffer();
    if (!isCurrentTransfer(tid, epoch) || channel.readyState !== "open") return;
    // Chống nghẽn: nếu hàng đợi gửi đầy thì đợi nó rút bớt.
    if (channel.bufferedAmount > HIGH_WATER) {
      await new Promise<void>((resolve) => {
        let settled = false;
        const finish = () => {
          if (settled) return;
          settled = true;
          channel.removeEventListener("bufferedamountlow", onLow);
          channel.removeEventListener("close", finish);
          channel.removeEventListener("error", finish);
          if (rt.cancelBufferWait === finish) rt.cancelBufferWait = undefined;
          resolve();
        };
        const onLow = () => finish();
        rt.cancelBufferWait = finish;
        channel.addEventListener("bufferedamountlow", onLow);
        channel.addEventListener("close", finish, { once: true });
        channel.addEventListener("error", finish, { once: true });
      });
      if (!isCurrentTransfer(tid, epoch) || channel.readyState !== "open") return;
    }
    channel.send(buf);
    offset += buf.byteLength;
    setStatus(tid, { transferred: offset }, epoch);
  }
  // Đã bơm hết; chờ phía nhận xác nhận "done" qua onmessage để chốt trạng thái.
}

/* ---------------------------- Phía NHẬN ---------------------------- */

/** Người nhận đồng ý → bắt đầu bắt tay. */
export function acceptTransfer(tid: string) {
  const rt = runtimes.get(tid);
  const info = store.get(tid);
  if (
    !rt
    || !info
    || info.direction !== "recv"
    || info.status !== "incoming"
    || !isCurrentTransfer(tid, rt.epoch)
  ) return;
  const epoch = rt.epoch;

  const pc = new RTCPeerConnection(RTC_CONFIG);
  rt.pc = pc;
  rt.chunks = [];
  pc.onicecandidate = (e) => {
    if (isCurrentTransfer(tid, epoch) && e.candidate) {
      send(info.peer, { t: "ice", tid, candidate: e.candidate.toJSON() }, epoch);
    }
  };
  pc.onconnectionstatechange = () => {
    if (isCurrentTransfer(tid, epoch) && pc.connectionState === "failed") {
      setStatus(tid, { status: "error", error: "Kết nối P2P thất bại." }, epoch);
      cleanup(tid, false, epoch);
    }
  };
  pc.ondatachannel = (e) => {
    if (!isCurrentTransfer(tid, epoch)) {
      e.channel.close();
      return;
    }
    const channel = e.channel;
    channel.binaryType = "arraybuffer";
    rt.channel = channel;
    let received = 0;
    channel.onmessage = (ev) => {
      if (!isCurrentTransfer(tid, epoch)) return;
      if (typeof ev.data === "string") return;
      const buf: ArrayBuffer = ev.data instanceof ArrayBuffer ? ev.data : new Uint8Array(ev.data).buffer;
      rt.chunks?.push(buf);
      received += buf.byteLength;
      setStatus(tid, { transferred: received }, epoch);
      if (received >= info.size) finishReceive(tid, epoch);
    };
  };

  setStatus(tid, { status: "connecting" }, epoch);
  send(info.peer, { t: "accept", tid }, epoch);
}

/** Người nhận từ chối. */
export function declineTransfer(tid: string) {
  const info = store.get(tid);
  const epoch = transferEpochs.get(tid);
  if (!info || epoch == null || !isCurrentTransfer(tid, epoch)) return;
  send(info.peer, { t: "decline", tid }, epoch);
  setStatus(tid, { status: "declined" }, epoch);
  cleanup(tid, false, epoch);
}

function finishReceive(tid: string, epoch: number) {
  if (!isCurrentTransfer(tid, epoch)) return;
  const rt = runtimes.get(tid);
  const info = store.get(tid);
  if (!rt?.chunks || rt.epoch !== epoch || !info) return;
  try {
    const blob = new Blob(rt.chunks, info.mime ? { type: info.mime } : undefined);
    const url = URL.createObjectURL(blob);
    if (!isCurrentTransfer(tid, epoch)) {
      URL.revokeObjectURL(url);
      return;
    }
    setStatus(tid, { status: "done", transferred: info.size, blobUrl: url }, epoch);
    rt.channel?.send("done"); // báo phía gửi đã nhận xong
    if (!isInlineMedia(info.name, info.mime)) triggerDownload(url, info.name);
    cleanup(tid, true, epoch); // giữ blobUrl để cho phép tải lại
  } catch {
    rt.channel?.send("error");
    setStatus(tid, { status: "error", error: "Không ghép được tệp đã nhận." }, epoch);
    cleanup(tid, false, epoch);
  }
}

/** Tải lại tệp đã nhận (dùng lại blob còn giữ trong bộ nhớ phiên). */
export function redownload(tid: string) {
  const info = store.get(tid);
  if (info?.blobUrl) triggerDownload(info.blobUrl, info.name);
}

function triggerDownload(url: string, name: string) {
  const a = document.createElement("a");
  a.href = url;
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
}

/* ---------------------------- Tín hiệu đến ---------------------------- */

async function onSignal(from: string, raw: string, epoch: number) {
  if (epoch !== identityEpoch) return;
  let msg: Record<string, unknown>;
  try {
    msg = JSON.parse(raw);
  } catch {
    return;
  }
  const t = msg.t as string;
  const tid = msg.tid as string;
  if (!tid || epoch !== identityEpoch) return;

  if (t === "invite") {
    // Không cho một gói lặp/giả mạo cùng tid ghi đè runtime đang giữ PC,
    // chunks hoặc blob URL của phiên hiện tại.
    if (store.has(tid) || runtimes.has(tid) || transferEpochs.has(tid)) return;
    const msgId = Number(msg.msgId) || 0;
    store.set(tid, {
      tid,
      msgId,
      convId: "", // phía nhận không cần (chỉ phía gửi mới upload); tải từ server dùng activeId của trang
      peer: from,
      direction: "recv",
      name: String(msg.name ?? "Tệp"),
      size: Number(msg.size) || 0,
      mime: (msg.mime as string) || undefined,
      status: "incoming",
      transferred: 0,
    });
    transferEpochs.set(tid, epoch);
    if (msgId > 0) byMsgId.set(msgId, tid);
    runtimes.set(tid, { epoch, pendingIce: [] });
    emit();
    return;
  }

  const rt = runtimes.get(tid);
  const info = store.get(tid);
  if (
    !rt
    || rt.epoch !== epoch
    || !info
    || info.peer.toLowerCase() !== from.toLowerCase()
    || !isCurrentTransfer(tid, epoch)
  ) return;

  switch (t) {
    case "accept": // phía gửi nhận được đồng ý → tạo offer
      await beginOffer(tid, epoch);
      break;
    case "decline":
      setStatus(tid, { status: "declined" }, epoch);
      cleanup(tid, false, epoch);
      break;
    case "cancel":
      setStatus(tid, { status: "canceled" }, epoch);
      cleanup(tid, false, epoch);
      break;
    case "offer": {
      const pc = rt.pc;
      if (!pc) return;
      await pc.setRemoteDescription({ type: "offer", sdp: msg.sdp as string });
      if (!isCurrentTransfer(tid, epoch) || rt.pc !== pc) return;
      await flushIce(tid, epoch);
      if (!isCurrentTransfer(tid, epoch) || rt.pc !== pc) return;
      const answer = await pc.createAnswer();
      if (!isCurrentTransfer(tid, epoch) || rt.pc !== pc) return;
      await pc.setLocalDescription(answer);
      if (!isCurrentTransfer(tid, epoch) || rt.pc !== pc) return;
      send(info.peer, { t: "answer", tid, sdp: answer.sdp }, epoch);
      break;
    }
    case "answer": {
      const pc = rt.pc;
      if (!pc) return;
      await pc.setRemoteDescription({ type: "answer", sdp: msg.sdp as string });
      if (!isCurrentTransfer(tid, epoch) || rt.pc !== pc) return;
      await flushIce(tid, epoch);
      break;
    }
    case "ice": {
      const cand = msg.candidate as RTCIceCandidateInit;
      const pc = rt.pc;
      if (pc?.remoteDescription) {
        try {
          await pc.addIceCandidate(cand);
        } catch {
          /* ignore */
        }
      } else if (isCurrentTransfer(tid, epoch)) {
        rt.pendingIce.push(cand); // đợi có remoteDescription rồi nạp
      }
      break;
    }
  }
}

async function flushIce(tid: string, epoch: number) {
  if (!isCurrentTransfer(tid, epoch)) return;
  const rt = runtimes.get(tid);
  if (!rt?.pc || rt.epoch !== epoch) return;
  const peer = rt.pc;
  while (rt.pendingIce.length) {
    const candidate = rt.pendingIce.shift();
    if (!candidate) continue;
    try {
      await peer.addIceCandidate(candidate);
    } catch {
      /* ignore */
    }
    if (!isCurrentTransfer(tid, epoch) || rt.pc !== peer) return;
  }
}

/** Khởi tạo một lần: lắng nghe tín hiệu WebRTC đến. Gọi sau startRealtime(). */
export function initFileTransfers() {
  if (initialized) return;
  initialized = true;
  subscribeSignal((from, payload) => {
    const epoch = identityEpoch;
    void onSignal(from, payload, epoch).catch(() => undefined);
  });
}

/**
 * Xóa toàn bộ dữ liệu/tài nguyên thuộc danh tính hiện tại trước khi logout hoặc
 * chuyển tài khoản. Subscription SignalR được giữ lại để phiên sau dùng tiếp,
 * nhưng epoch mới khiến mọi Promise/callback cũ trở thành no-op.
 */
export function resetFileTransfers() {
  // Fetch abort là đồng bộ: chặn response 401 và body upload cũ trước khi
  // controller của danh tính kế tiếp được công bố.
  identityAbortController.abort();
  identityEpoch += 1;
  identityAbortController = new AbortController();

  for (const rt of runtimes.values()) disposeRuntime(rt);
  for (const info of store.values()) {
    if (info.blobUrl) URL.revokeObjectURL(info.blobUrl);
  }
  runtimes.clear();
  store.clear();
  transferEpochs.clear();
  byMsgId.clear();
  emit();
}

/* ---------------------------- Hook React ---------------------------- */

export function useTransfers(): ReadonlyMap<number, TransferInfo> {
  return useSyncExternalStore(
    (cb) => {
      subscribers.add(cb);
      return () => subscribers.delete(cb);
    },
    () => snapshot,
  );
}

/** Danh sách các lời mời nhận tệp đang chờ quyết định (cho prompt toàn cục). */
export function useIncomingInvites(): TransferInfo[] {
  const map = useTransfers();
  const out: TransferInfo[] = [];
  for (const t of map.values()) if (t.direction === "recv" && t.status === "incoming") out.push(t);
  return out;
}

/** Bỏ lời mời khỏi danh sách (sau khi đã xử lý xong, để dọn UI). */
export function dismissTransfer(tid: string) {
  const info = store.get(tid);
  const epoch = transferEpochs.get(tid);
  if (!info || epoch == null || !isCurrentTransfer(tid, epoch)) return;
  cleanup(tid, false, epoch);
  store.delete(tid);
  transferEpochs.delete(tid);
  if (info.msgId > 0 && byMsgId.get(info.msgId) === tid) byMsgId.delete(info.msgId);
  emit();
}
