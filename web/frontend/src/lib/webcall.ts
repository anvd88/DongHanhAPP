import { useSyncExternalStore } from "react";
import { api } from "./api";
import { sendSignal, subscribeSignal } from "./realtime";

export type WebCallMedia = "audio" | "video";
export type WebCallStage = "outgoing" | "incoming" | "connecting" | "active" | "ended";

export type WebCallSession = {
  callId: string;
  peerUsername: string;
  peerName: string;
  media: WebCallMedia;
  incoming: boolean;
  stage: WebCallStage;
  remoteRinging: boolean;
  startedAt?: number;
  endedReason?: string;
  muted: boolean;
  cameraOn: boolean;
  localStream?: MediaStream;
  remoteStream?: MediaStream;
};

type CallConfig = {
  callsEnabled: boolean;
  videoCallEnabled: boolean;
  stunServers: string[];
  forceRelay: boolean;
  outgoingTimeoutSeconds: number;
  incomingTimeoutSeconds: number;
};

type TurnCredentials = { urls: string[]; username: string; credential: string };

export type IncomingWebCallHandoff = {
  callId: string;
  peerUsername: string;
  peerName: string;
  media: WebCallMedia;
};

const DEFAULT_CONFIG: CallConfig = {
  callsEnabled: true,
  videoCallEnabled: true,
  stunServers: ["stun:stun.l.google.com:19302", "stun:stun1.l.google.com:19302"],
  forceRelay: false,
  outgoingTimeoutSeconds: 30,
  incomingTimeoutSeconds: 45,
};

let session: WebCallSession | null = null;
let sessionIdentityEpoch: number | null = null;
let pc: RTCPeerConnection | null = null;
let pcCallId: string | null = null;
let pcIdentityEpoch: number | null = null;
let initialized = false;
let timeout: number | null = null;
let clearEndedTimer: number | null = null;
let config = DEFAULT_CONFIG;
let turn: TurnCredentials | null = null;
let selfDisplayName = "";
let identityEpoch = 0;
let identityAbortController = new AbortController();
let startingCallEpoch: number | null = null;
let pendingInviteId: string | null = null;
let pendingInviteEpoch: number | null = null;
let signalChain = Promise.resolve();
const pendingIce: RTCIceCandidateInit[] = [];
const subscribers = new Set<() => void>();
const signalRetryWaiters = new Set<() => void>();

function isCurrentIdentity(epoch: number) {
  return epoch === identityEpoch;
}

function isCurrentCall(epoch: number, callId: string) {
  return isCurrentIdentity(epoch)
    && sessionIdentityEpoch === epoch
    && session?.callId === callId;
}

function emit() {
  for (const subscriber of subscribers) subscriber();
}

function patch(next: Partial<WebCallSession>, epoch: number, callId: string) {
  if (!isCurrentCall(epoch, callId) || !session) return;
  session = { ...session, ...next };
  emit();
}

function waitForSignalRetry(epoch: number) {
  return new Promise<void>((resolve) => {
    let settled = false;
    const timer = window.setTimeout(finish, 300);
    function finish() {
      if (settled) return;
      settled = true;
      window.clearTimeout(timer);
      signalRetryWaiters.delete(finish);
      resolve();
    }
    signalRetryWaiters.add(finish);
    if (!isCurrentIdentity(epoch)) finish();
  });
}

async function deliverSignal(peer: string, payload: string, epoch: number) {
  // A dedicated call tab starts its own SignalR connection. Keep the signal
  // until that connection is ready instead of silently dropping the first
  // invite/accept packet while the tab is still loading.
  for (let attempt = 0; attempt < 50; attempt += 1) {
    if (!isCurrentIdentity(epoch)) return;
    if (await sendSignal(peer, payload)) return;
    if (!isCurrentIdentity(epoch)) return;
    await waitForSignalRetry(epoch);
  }
}

function signal(
  peer: string,
  type: string,
  callId: string,
  epoch: number,
  extra: Record<string, unknown> = {},
) {
  if (!isCurrentIdentity(epoch)) return;
  void deliverSignal(peer, JSON.stringify({ k: "call", type, id: callId, ...extra }), epoch);
}

async function refreshConfiguration(epoch: number) {
  if (!isCurrentIdentity(epoch)) return false;
  const requestSignal = identityAbortController.signal;
  const [appConfig, credentials] = await Promise.all([
    api.get<{ call?: Partial<CallConfig> }>("/api/app-config", requestSignal).catch(() => null),
    api.get<TurnCredentials>("/api/chat/call/turn", requestSignal).catch(() => null),
  ]);
  if (!isCurrentIdentity(epoch)) return false;
  config = { ...DEFAULT_CONFIG, ...(appConfig?.call ?? {}) };
  turn = credentials?.urls?.length ? credentials : null;
  return true;
}

function rtcConfiguration(): RTCConfiguration {
  const iceServers: RTCIceServer[] = [];
  if (config.stunServers?.length) iceServers.push({ urls: config.stunServers });
  if (turn?.urls?.length) {
    iceServers.push({ urls: turn.urls, username: turn.username, credential: turn.credential });
  }
  return { iceServers, iceTransportPolicy: config.forceRelay ? "relay" : "all" };
}

function stopTimeout() {
  if (timeout != null) window.clearTimeout(timeout);
  timeout = null;
}

function armTimeout(seconds: number, epoch: number, callId: string) {
  if (!isCurrentCall(epoch, callId)) return;
  stopTimeout();
  timeout = window.setTimeout(() => {
    const current = session;
    if (
      !current
      || !isCurrentCall(epoch, callId)
      || (current.stage !== "incoming" && current.stage !== "outgoing")
    ) return;
    if (!current.incoming) {
      signal(current.peerUsername, "cancel", current.callId, epoch);
      const requestSignal = identityAbortController.signal;
      void api.post("/api/chat/call/missed", {
        toUsername: current.peerUsername,
        callId: current.callId,
        media: current.media,
      }, requestSignal).catch(() => undefined);
    }
    teardown(current.incoming ? "missed" : "no_answer", epoch, callId);
  }, Math.max(5, seconds) * 1000);
}

async function acquireMedia(media: WebCallMedia, epoch: number, callId: string) {
  const current = session;
  if (!current || !isCurrentCall(epoch, callId)) throw new Error("Cuộc gọi đã kết thúc.");
  if (current.localStream) return current.localStream;
  const stream = await navigator.mediaDevices.getUserMedia({
    audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true },
    video: media === "video" ? { facingMode: "user", width: { ideal: 1280 }, height: { ideal: 720 } } : false,
  });
  if (!isCurrentCall(epoch, callId) || session?.stage === "ended") {
    stream.getTracks().forEach((track) => track.stop());
    throw new Error("Cuộc gọi đã kết thúc.");
  }
  patch({ localStream: stream, cameraOn: media === "video" }, epoch, callId);
  return stream;
}

async function addLocalVideo(epoch: number, callId: string) {
  const current = session;
  if (!current || !isCurrentCall(epoch, callId)) throw new Error("Cuộc gọi đã kết thúc.");
  if (current.localStream?.getVideoTracks().length) {
    patch({ media: "video", cameraOn: true }, epoch, callId);
    return current.localStream;
  }
  const camera = await navigator.mediaDevices.getUserMedia({
    audio: false,
    video: { facingMode: "user", width: { ideal: 1280 }, height: { ideal: 720 } },
  });
  if (!isCurrentCall(epoch, callId) || session?.stage === "ended") {
    camera.getTracks().forEach((track) => track.stop());
    throw new Error("Cuộc gọi đã kết thúc.");
  }
  const target = session?.localStream ?? new MediaStream();
  for (const track of camera.getVideoTracks()) target.addTrack(track);
  if (pc && pcCallId === callId && pcIdentityEpoch === epoch) attachLocalTracks(pc, target);
  patch({ media: "video", cameraOn: true, localStream: target }, epoch, callId);
  return target;
}

function disposePeer(peer: RTCPeerConnection) {
  peer.onicecandidate = null;
  peer.ontrack = null;
  peer.onconnectionstatechange = null;
  for (const sender of peer.getSenders()) sender.track?.stop();
  for (const receiver of peer.getReceivers()) receiver.track?.stop();
  try {
    peer.close();
  } catch {
    /* ignore */
  }
}

function ensurePeer(epoch: number, callId: string) {
  if (!isCurrentCall(epoch, callId)) throw new Error("Cuộc gọi đã kết thúc.");
  if (pc && pcCallId === callId && pcIdentityEpoch === epoch) return pc;
  if (pc) disposePeer(pc);
  const current = session;
  if (!current) throw new Error("Cuộc gọi đã kết thúc.");
  const peer = new RTCPeerConnection(rtcConfiguration());
  pc = peer;
  pcCallId = callId;
  pcIdentityEpoch = epoch;
  const remoteStream = new MediaStream();
  patch({ remoteStream }, epoch, callId);
  peer.onicecandidate = (event) => {
    const active = session;
    if (!active || !isCurrentCall(epoch, callId) || pc !== peer || !event.candidate) return;
    signal(active.peerUsername, "ice", active.callId, epoch, {
      mid: event.candidate.sdpMid,
      line: event.candidate.sdpMLineIndex,
      cand: event.candidate.candidate,
    });
  };
  peer.ontrack = (event) => {
    if (!isCurrentCall(epoch, callId) || pc !== peer) return;
    const target = session?.remoteStream ?? remoteStream;
    if (!target.getTracks().some((track) => track.id === event.track.id)) target.addTrack(event.track);
    patch({ remoteStream: target }, epoch, callId);
  };
  peer.onconnectionstatechange = () => {
    if (!isCurrentCall(epoch, callId) || pc !== peer) return;
    if (peer.connectionState === "connected") {
      stopTimeout();
      patch({ stage: "active", startedAt: session?.startedAt ?? Date.now() }, epoch, callId);
    } else if (peer.connectionState === "failed" || peer.connectionState === "closed") {
      teardown("disconnected", epoch, callId);
    }
  };
  return peer;
}

function attachLocalTracks(peer: RTCPeerConnection, stream: MediaStream) {
  for (const track of stream.getTracks()) {
    if (!peer.getSenders().some((sender) => sender.track?.id === track.id)) peer.addTrack(track, stream);
  }
}

async function createOffer(epoch: number, callId: string) {
  const current = session;
  if (!current || !isCurrentCall(epoch, callId)) return;
  const peer = ensurePeer(epoch, callId);
  const stream = await acquireMedia(current.media, epoch, callId);
  if (!isCurrentCall(epoch, callId) || pc !== peer) return;
  attachLocalTracks(peer, stream);
  const offer = await peer.createOffer();
  if (!isCurrentCall(epoch, callId) || pc !== peer) return;
  await peer.setLocalDescription(offer);
  if (!isCurrentCall(epoch, callId) || pc !== peer) return;
  signal(current.peerUsername, "offer", current.callId, epoch, { sdp: offer.sdp ?? "" });
}

async function acceptOffer(sdp: string, epoch: number, callId: string) {
  const current = session;
  if (!current || !sdp || !isCurrentCall(epoch, callId)) return;
  const peer = ensurePeer(epoch, callId);
  const stream = await acquireMedia(current.media, epoch, callId);
  if (!isCurrentCall(epoch, callId) || pc !== peer) return;
  attachLocalTracks(peer, stream);
  await peer.setRemoteDescription({ type: "offer", sdp });
  if (!isCurrentCall(epoch, callId) || pc !== peer) return;
  while (pendingIce.length) {
    const candidate = pendingIce.shift();
    if (candidate) await peer.addIceCandidate(candidate);
    if (!isCurrentCall(epoch, callId) || pc !== peer) return;
  }
  const answer = await peer.createAnswer();
  if (!isCurrentCall(epoch, callId) || pc !== peer) return;
  await peer.setLocalDescription(answer);
  if (!isCurrentCall(epoch, callId) || pc !== peer) return;
  signal(current.peerUsername, "answer", current.callId, epoch, { sdp: answer.sdp ?? "" });
}

async function acceptAnswer(sdp: string, epoch: number, callId: string) {
  const peer = pc;
  if (!peer || !sdp || !isCurrentCall(epoch, callId) || pcCallId !== callId || pcIdentityEpoch !== epoch) return;
  await peer.setRemoteDescription({ type: "answer", sdp });
  if (!isCurrentCall(epoch, callId) || pc !== peer) return;
  while (pendingIce.length) {
    const candidate = pendingIce.shift();
    if (candidate) await peer.addIceCandidate(candidate);
    if (!isCurrentCall(epoch, callId) || pc !== peer) return;
  }
}

async function handleSignal(from: string, raw: string, epoch: number) {
  if (!isCurrentIdentity(epoch)) return;
  let message: Record<string, unknown>;
  try {
    message = JSON.parse(raw) as Record<string, unknown>;
  } catch {
    return;
  }
  if (message.k !== "call") return;
  const type = String(message.type ?? "");
  const callId = String(message.id ?? "");
  if (!callId || !isCurrentIdentity(epoch)) return;

  if (type === "invite") {
    if (session || pendingInviteId || startingCallEpoch === epoch) {
      if (session?.callId !== callId && pendingInviteId !== callId) {
        signal(from, "reject", callId, epoch, { reason: "busy" });
      }
      return;
    }
    pendingInviteId = callId;
    pendingInviteEpoch = epoch;
    try {
      if (!await refreshConfiguration(epoch)) return;
      if (!isCurrentIdentity(epoch) || pendingInviteId !== callId || pendingInviteEpoch !== epoch) return;
      if (session || startingCallEpoch === epoch) {
        signal(from, "reject", callId, epoch, { reason: "busy" });
        return;
      }
      const media: WebCallMedia = message.media === "video" ? "video" : "audio";
      if (!config.callsEnabled || (media === "video" && !config.videoCallEnabled)) {
        signal(from, "reject", callId, epoch, { reason: "disabled" });
        return;
      }
      session = {
        callId,
        peerUsername: from,
        peerName: String(message.name || from),
        media,
        incoming: true,
        stage: "incoming",
        remoteRinging: false,
        muted: false,
        cameraOn: media === "video",
      };
      sessionIdentityEpoch = epoch;
      emit();
      signal(from, "ringing", callId, epoch);
      armTimeout(config.incomingTimeoutSeconds, epoch, callId);
    } finally {
      if (pendingInviteId === callId && pendingInviteEpoch === epoch) {
        pendingInviteId = null;
        pendingInviteEpoch = null;
      }
    }
    return;
  }

  const current = session;
  if (
    !current
    || !isCurrentCall(epoch, callId)
    || current.peerUsername.toLowerCase() !== from.toLowerCase()
  ) return;
  try {
    if (type === "ringing" && current.stage === "outgoing") {
      patch({ remoteRinging: true }, epoch, callId);
    }
    else if (type === "accept" && current.stage === "outgoing") {
      stopTimeout();
      patch({ stage: "connecting" }, epoch, callId);
      await createOffer(epoch, callId);
    } else if (type === "offer") await acceptOffer(String(message.sdp ?? ""), epoch, callId);
    else if (type === "answer") await acceptAnswer(String(message.sdp ?? ""), epoch, callId);
    else if (type === "upgrade" && config.videoCallEnabled) await addLocalVideo(epoch, callId);
    else if (type === "ice") {
      const candidate: RTCIceCandidateInit = {
        sdpMid: String(message.mid ?? ""),
        sdpMLineIndex: Number(message.line ?? 0),
        candidate: String(message.cand ?? ""),
      };
      const peer = pc;
      if (peer?.remoteDescription && pcCallId === callId && pcIdentityEpoch === epoch) {
        await peer.addIceCandidate(candidate);
      } else if (isCurrentCall(epoch, callId)) pendingIce.push(candidate);
    } else if (type === "reject") teardown(String(message.reason || "declined"), epoch, callId);
    else if (type === "end" || type === "cancel") {
      teardown(type === "cancel" ? "canceled" : "ended", epoch, callId);
    }
  } catch {
    teardown("media_error", epoch, callId);
  }
}

function initialize() {
  if (initialized) return;
  initialized = true;
  subscribeSignal((from, raw) => {
    const epoch = identityEpoch;
    signalChain = signalChain.then(() => handleSignal(from, raw, epoch)).catch(() => undefined);
  });
  void refreshConfiguration(identityEpoch);
}

function recordHistory(original: WebCallSession, reason: string, endedAt: number, epoch: number) {
  if (!isCurrentIdentity(epoch)) return;
  const requestSignal = identityAbortController.signal;
  void api.post("/api/chat/call/history", {
    peerUsername: original.peerUsername,
    peerName: original.peerName,
    callId: original.callId,
    media: original.media,
    direction: original.incoming ? "incoming" : "outgoing",
    outcome: reason,
    startedAtEpochMs: original.startedAt ?? null,
    endedAtEpochMs: endedAt,
  }, requestSignal).catch(() => undefined);
}

function teardown(reason: string, epoch: number, callId: string) {
  const original = session;
  if (!original || !isCurrentCall(epoch, callId) || original.stage === "ended") return;
  stopTimeout();
  pendingIce.length = 0;
  original.localStream?.getTracks().forEach((track) => track.stop());
  original.remoteStream?.getTracks().forEach((track) => track.stop());
  if (pc) disposePeer(pc);
  pc = null;
  pcCallId = null;
  pcIdentityEpoch = null;
  const endedAt = Date.now();
  recordHistory(original, reason, endedAt, epoch);
  session = { ...original, stage: "ended", endedReason: reason, localStream: undefined, remoteStream: undefined };
  emit();
  if (clearEndedTimer != null) window.clearTimeout(clearEndedTimer);
  clearEndedTimer = window.setTimeout(() => {
    if (isCurrentCall(epoch, callId) && session?.stage === "ended") {
      session = null;
      sessionIdentityEpoch = null;
      emit();
    }
  }, 1200);
}

export async function startWebCall(peerUsername: string, peerName: string, media: WebCallMedia, displayName: string) {
  initialize();
  const epoch = identityEpoch;
  if (session || pendingInviteId || startingCallEpoch === epoch) throw new Error("Bạn đang có một cuộc gọi khác.");
  if (!navigator.mediaDevices?.getUserMedia || typeof RTCPeerConnection === "undefined") {
    throw new Error("Trình duyệt này không hỗ trợ cuộc gọi WebRTC.");
  }
  startingCallEpoch = epoch;
  try {
    if (!await refreshConfiguration(epoch) || startingCallEpoch !== epoch) {
      return;
    }
    if (session || pendingInviteId) throw new Error("Bạn đang có một cuộc gọi khác.");
    if (!config.callsEnabled) throw new Error("Quản trị viên đang tắt chức năng cuộc gọi.");
    if (media === "video" && !config.videoCallEnabled) throw new Error("Quản trị viên đang tắt gọi video.");
    selfDisplayName = displayName;
    const callId = crypto.randomUUID?.() ?? `web-call-${Date.now()}-${Math.random().toString(16).slice(2)}`;
    session = {
      callId,
      peerUsername,
      peerName,
      media,
      incoming: false,
      stage: "outgoing",
      remoteRinging: false,
      muted: false,
      cameraOn: media === "video",
    };
    sessionIdentityEpoch = epoch;
    emit();
    try {
      // Request permission in the call tab and create tracks immediately so
      // the microphone/camera controls also work while the call is ringing.
      await acquireMedia(media, epoch, callId);
    } catch (error) {
      // Reset/account-switch là một thao tác hủy chủ động, không trả lỗi muộn
      // cho component của danh tính cũ để nó dựng lại toast/error state.
      if (!isCurrentCall(epoch, callId)) return;
      teardown("media_denied", epoch, callId);
      throw error;
    }
    if (!isCurrentCall(epoch, callId)) return;
    signal(peerUsername, "invite", callId, epoch, { media, name: selfDisplayName });
    void api.post(
      "/api/chat/call/ring",
      { toUsername: peerUsername, callId, media },
      identityAbortController.signal,
    ).catch(() => undefined);
    armTimeout(config.outgoingTimeoutSeconds, epoch, callId);
  } finally {
    if (startingCallEpoch === epoch) startingCallEpoch = null;
  }
}

export async function acceptWebCall() {
  const current = session;
  const epoch = sessionIdentityEpoch;
  if (!current || epoch == null || current.stage !== "incoming" || !isCurrentCall(epoch, current.callId)) return;
  const callId = current.callId;
  try {
    if (!await refreshConfiguration(epoch) || !isCurrentCall(epoch, callId)) return;
    await acquireMedia(current.media, epoch, callId);
    if (!isCurrentCall(epoch, callId)) return;
    stopTimeout();
    patch({ stage: "connecting" }, epoch, callId);
    signal(current.peerUsername, "accept", callId, epoch);
  } catch (error) {
    if (!isCurrentCall(epoch, callId)) return;
    signal(current.peerUsername, "reject", callId, epoch, { reason: "media_denied" });
    teardown("media_denied", epoch, callId);
    throw error;
  }
}

export function hangupWebCall(reason = "ended") {
  const current = session;
  const epoch = sessionIdentityEpoch;
  if (!current || epoch == null || !isCurrentCall(epoch, current.callId)) return;
  const requestSignal = identityAbortController.signal;
  signal(current.peerUsername, current.stage === "incoming" ? "reject" : "end", current.callId, epoch, { reason });
  if (!current.incoming && (current.stage === "outgoing" || current.stage === "connecting")) {
    void api.post(
      "/api/chat/call/cancel",
      { toUsername: current.peerUsername, callId: current.callId },
      requestSignal,
    ).catch(() => undefined);
  }
  if (!current.incoming && current.stage === "outgoing") {
    void api.post("/api/chat/call/missed", {
      toUsername: current.peerUsername,
      callId: current.callId,
      media: current.media,
    }, requestSignal).catch(() => undefined);
  }
  teardown(reason, epoch, current.callId);
}

export function toggleWebCallMute(): boolean {
  const epoch = sessionIdentityEpoch;
  if (!session || epoch == null || !isCurrentCall(epoch, session.callId) || !session.localStream?.getAudioTracks().length) {
    return false;
  }
  const muted = !session.muted;
  session.localStream.getAudioTracks().forEach((track) => { track.enabled = !muted; });
  patch({ muted }, epoch, session.callId);
  return true;
}

export function toggleWebCallCamera(): boolean {
  const epoch = sessionIdentityEpoch;
  if (!session || epoch == null || !isCurrentCall(epoch, session.callId) || !session.localStream?.getVideoTracks().length) {
    return false;
  }
  const cameraOn = !session.cameraOn;
  session.localStream.getVideoTracks().forEach((track) => { track.enabled = cameraOn; });
  patch({ cameraOn }, epoch, session.callId);
  return true;
}

/** Release an unanswered call from the chat tab without rejecting it. */
export function handoffIncomingWebCall(callId: string): IncomingWebCallHandoff | null {
  const current = session;
  const epoch = sessionIdentityEpoch;
  if (!current || epoch == null || !isCurrentCall(epoch, callId) || current.stage !== "incoming") return null;
  stopTimeout();
  pendingIce.length = 0;
  session = null;
  sessionIdentityEpoch = null;
  emit();
  return {
    callId: current.callId,
    peerUsername: current.peerUsername,
    peerName: current.peerName,
    media: current.media,
  };
}

/** Restore an incoming call in the dedicated tab, then answer it there. */
export async function resumeIncomingWebCall(handoff: IncomingWebCallHandoff) {
  initialize();
  const epoch = identityEpoch;
  if (session || pendingInviteId || startingCallEpoch === epoch) throw new Error("Bạn đang có một cuộc gọi khác.");
  if (!navigator.mediaDevices?.getUserMedia || typeof RTCPeerConnection === "undefined") {
    throw new Error("Trình duyệt này không hỗ trợ cuộc gọi WebRTC.");
  }
  startingCallEpoch = epoch;
  try {
    if (!await refreshConfiguration(epoch) || startingCallEpoch !== epoch) {
      return;
    }
    if (session || pendingInviteId) throw new Error("Bạn đang có một cuộc gọi khác.");
    if (!config.callsEnabled || (handoff.media === "video" && !config.videoCallEnabled)) {
      signal(handoff.peerUsername, "reject", handoff.callId, epoch, { reason: "disabled" });
      throw new Error("Quản trị viên đang tắt chức năng cuộc gọi này.");
    }
    session = {
      ...handoff,
      incoming: true,
      stage: "incoming",
      remoteRinging: false,
      muted: false,
      cameraOn: handoff.media === "video",
    };
    sessionIdentityEpoch = epoch;
    emit();
  } finally {
    if (startingCallEpoch === epoch) startingCallEpoch = null;
  }
  if (!isCurrentCall(epoch, handoff.callId)) return;
  await acceptWebCall();
}

export async function upgradeWebCall() {
  const current = session;
  const epoch = sessionIdentityEpoch;
  if (
    !current
    || epoch == null
    || !isCurrentCall(epoch, current.callId)
    || current.stage !== "active"
    || current.media === "video"
  ) return;
  const callId = current.callId;
  if (!config.videoCallEnabled) throw new Error("Quản trị viên đang tắt gọi video.");
  try {
    await addLocalVideo(epoch, callId);
  } catch (error) {
    if (!isCurrentCall(epoch, callId)) return;
    throw error;
  }
  if (!isCurrentCall(epoch, callId)) return;
  signal(current.peerUsername, "upgrade", callId, epoch);
  await createOffer(epoch, callId);
}

/**
 * Hủy và quên toàn bộ tài nguyên thuộc danh tính hiện tại trước khi logout hoặc
 * chuyển tài khoản. Không ghi lịch sử và không gửi signaling trong reset: các
 * request/callback cũ không được phép chạy tiếp bằng cookie của danh tính mới.
 */
export function resetWebCall() {
  identityAbortController.abort();
  // Đổi epoch trước khi đóng tài nguyên để event `close` phát đồng bộ cũng không
  // thể teardown hoặc patch một cuộc gọi của phiên kế tiếp.
  identityEpoch += 1;
  identityAbortController = new AbortController();

  stopTimeout();
  if (clearEndedTimer != null) window.clearTimeout(clearEndedTimer);
  clearEndedTimer = null;
  for (const finish of [...signalRetryWaiters]) finish();
  signalRetryWaiters.clear();

  session?.localStream?.getTracks().forEach((track) => track.stop());
  session?.remoteStream?.getTracks().forEach((track) => track.stop());
  if (pc) disposePeer(pc);
  pc = null;
  pcCallId = null;
  pcIdentityEpoch = null;

  pendingIce.length = 0;
  pendingInviteId = null;
  pendingInviteEpoch = null;
  startingCallEpoch = null;
  signalChain = Promise.resolve();
  selfDisplayName = "";
  turn = null;
  config = { ...DEFAULT_CONFIG, stunServers: [...DEFAULT_CONFIG.stunServers] };
  session = null;
  sessionIdentityEpoch = null;
  emit();
}

export function useWebCall() {
  return useSyncExternalStore(
    (subscriber) => {
      initialize();
      subscribers.add(subscriber);
      return () => subscribers.delete(subscriber);
    },
    () => session,
    () => null,
  );
}
