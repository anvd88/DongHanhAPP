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
let pc: RTCPeerConnection | null = null;
let initialized = false;
let timeout: number | null = null;
let clearEndedTimer: number | null = null;
let config = DEFAULT_CONFIG;
let turn: TurnCredentials | null = null;
let selfDisplayName = "";
let startingCall = false;
let pendingInviteId: string | null = null;
let signalChain = Promise.resolve();
const pendingIce: RTCIceCandidateInit[] = [];
const subscribers = new Set<() => void>();

function emit() {
  for (const subscriber of subscribers) subscriber();
}

function patch(next: Partial<WebCallSession>) {
  if (!session) return;
  session = { ...session, ...next };
  emit();
}

async function deliverSignal(peer: string, payload: string) {
  // A dedicated call tab starts its own SignalR connection. Keep the signal
  // until that connection is ready instead of silently dropping the first
  // invite/accept packet while the tab is still loading.
  for (let attempt = 0; attempt < 50; attempt += 1) {
    if (await sendSignal(peer, payload)) return;
    await new Promise<void>((resolve) => window.setTimeout(resolve, 300));
  }
}

function signal(peer: string, type: string, callId: string, extra: Record<string, unknown> = {}) {
  void deliverSignal(peer, JSON.stringify({ k: "call", type, id: callId, ...extra }));
}

async function refreshConfiguration() {
  const [appConfig, credentials] = await Promise.all([
    api.get<{ call?: Partial<CallConfig> }>("/api/app-config").catch(() => null),
    api.get<TurnCredentials>("/api/chat/call/turn").catch(() => null),
  ]);
  config = { ...DEFAULT_CONFIG, ...(appConfig?.call ?? {}) };
  turn = credentials?.urls?.length ? credentials : null;
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

function armTimeout(seconds: number) {
  stopTimeout();
  timeout = window.setTimeout(() => {
    const current = session;
    if (!current || (current.stage !== "incoming" && current.stage !== "outgoing")) return;
    if (!current.incoming) {
      signal(current.peerUsername, "cancel", current.callId);
      void api.post("/api/chat/call/missed", {
        toUsername: current.peerUsername,
        callId: current.callId,
        media: current.media,
      }).catch(() => undefined);
    }
    teardown(current.incoming ? "missed" : "no_answer");
  }, Math.max(5, seconds) * 1000);
}

async function acquireMedia(media: WebCallMedia) {
  const current = session;
  if (!current) throw new Error("Cuộc gọi đã kết thúc.");
  if (current.localStream) return current.localStream;
  const callId = current.callId;
  const stream = await navigator.mediaDevices.getUserMedia({
    audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true },
    video: media === "video" ? { facingMode: "user", width: { ideal: 1280 }, height: { ideal: 720 } } : false,
  });
  if (!session || session.callId !== callId || session.stage === "ended") {
    stream.getTracks().forEach((track) => track.stop());
    throw new Error("Cuộc gọi đã kết thúc.");
  }
  patch({ localStream: stream, cameraOn: media === "video" });
  return stream;
}

async function addLocalVideo() {
  const current = session;
  if (!current) throw new Error("Cuộc gọi đã kết thúc.");
  if (current.localStream?.getVideoTracks().length) {
    patch({ media: "video", cameraOn: true });
    return current.localStream;
  }
  const camera = await navigator.mediaDevices.getUserMedia({
    audio: false,
    video: { facingMode: "user", width: { ideal: 1280 }, height: { ideal: 720 } },
  });
  if (!session || session.callId !== current.callId || session.stage === "ended") {
    camera.getTracks().forEach((track) => track.stop());
    throw new Error("Cuộc gọi đã kết thúc.");
  }
  const target = current.localStream ?? new MediaStream();
  for (const track of camera.getVideoTracks()) target.addTrack(track);
  if (pc) attachLocalTracks(pc, target);
  patch({ media: "video", cameraOn: true, localStream: target });
  return target;
}

function ensurePeer() {
  if (pc) return pc;
  const current = session;
  if (!current) throw new Error("Cuộc gọi đã kết thúc.");
  const peer = new RTCPeerConnection(rtcConfiguration());
  pc = peer;
  const remoteStream = new MediaStream();
  patch({ remoteStream });
  peer.onicecandidate = (event) => {
    const active = session;
    if (!active || !event.candidate) return;
    signal(active.peerUsername, "ice", active.callId, {
      mid: event.candidate.sdpMid,
      line: event.candidate.sdpMLineIndex,
      cand: event.candidate.candidate,
    });
  };
  peer.ontrack = (event) => {
    const target = session?.remoteStream ?? remoteStream;
    if (!target.getTracks().some((track) => track.id === event.track.id)) target.addTrack(event.track);
    patch({ remoteStream: target });
  };
  peer.onconnectionstatechange = () => {
    if (peer.connectionState === "connected") {
      stopTimeout();
      patch({ stage: "active", startedAt: session?.startedAt ?? Date.now() });
    } else if (peer.connectionState === "failed" || peer.connectionState === "closed") {
      teardown("disconnected");
    }
  };
  return peer;
}

function attachLocalTracks(peer: RTCPeerConnection, stream: MediaStream) {
  for (const track of stream.getTracks()) {
    if (!peer.getSenders().some((sender) => sender.track?.id === track.id)) peer.addTrack(track, stream);
  }
}

async function createOffer() {
  const current = session;
  if (!current) return;
  const peer = ensurePeer();
  const stream = await acquireMedia(current.media);
  attachLocalTracks(peer, stream);
  const offer = await peer.createOffer();
  await peer.setLocalDescription(offer);
  signal(current.peerUsername, "offer", current.callId, { sdp: offer.sdp ?? "" });
}

async function acceptOffer(sdp: string) {
  const current = session;
  if (!current || !sdp) return;
  const peer = ensurePeer();
  const stream = await acquireMedia(current.media);
  attachLocalTracks(peer, stream);
  await peer.setRemoteDescription({ type: "offer", sdp });
  while (pendingIce.length) await peer.addIceCandidate(pendingIce.shift());
  const answer = await peer.createAnswer();
  await peer.setLocalDescription(answer);
  signal(current.peerUsername, "answer", current.callId, { sdp: answer.sdp ?? "" });
}

async function acceptAnswer(sdp: string) {
  if (!pc || !sdp) return;
  await pc.setRemoteDescription({ type: "answer", sdp });
  while (pendingIce.length) await pc.addIceCandidate(pendingIce.shift());
}

async function handleSignal(from: string, raw: string) {
  let message: Record<string, unknown>;
  try {
    message = JSON.parse(raw) as Record<string, unknown>;
  } catch {
    return;
  }
  if (message.k !== "call") return;
  const type = String(message.type ?? "");
  const callId = String(message.id ?? "");
  if (!callId) return;

  if (type === "invite") {
    if (session || pendingInviteId) {
      if (session?.callId !== callId && pendingInviteId !== callId) signal(from, "reject", callId, { reason: "busy" });
      return;
    }
    pendingInviteId = callId;
    try {
      await refreshConfiguration();
      const media: WebCallMedia = message.media === "video" ? "video" : "audio";
      if (!config.callsEnabled || (media === "video" && !config.videoCallEnabled)) {
        signal(from, "reject", callId, { reason: "disabled" });
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
      emit();
      signal(from, "ringing", callId);
      armTimeout(config.incomingTimeoutSeconds);
    } finally {
      pendingInviteId = null;
    }
    return;
  }

  const current = session;
  if (!current || current.callId !== callId || current.peerUsername.toLowerCase() !== from.toLowerCase()) return;
  try {
    if (type === "ringing" && current.stage === "outgoing") patch({ remoteRinging: true });
    else if (type === "accept" && current.stage === "outgoing") {
      stopTimeout();
      patch({ stage: "connecting" });
      await createOffer();
    } else if (type === "offer") await acceptOffer(String(message.sdp ?? ""));
    else if (type === "answer") await acceptAnswer(String(message.sdp ?? ""));
    else if (type === "upgrade" && config.videoCallEnabled) await addLocalVideo();
    else if (type === "ice") {
      const candidate: RTCIceCandidateInit = {
        sdpMid: String(message.mid ?? ""),
        sdpMLineIndex: Number(message.line ?? 0),
        candidate: String(message.cand ?? ""),
      };
      if (pc?.remoteDescription) await pc.addIceCandidate(candidate);
      else pendingIce.push(candidate);
    } else if (type === "reject") teardown(String(message.reason || "declined"));
    else if (type === "end" || type === "cancel") teardown(type === "cancel" ? "canceled" : "ended");
  } catch {
    teardown("media_error");
  }
}

function initialize() {
  if (initialized) return;
  initialized = true;
  subscribeSignal((from, raw) => {
    signalChain = signalChain.then(() => handleSignal(from, raw)).catch(() => undefined);
  });
  void refreshConfiguration();
}

function recordHistory(original: WebCallSession, reason: string, endedAt: number) {
  void api.post("/api/chat/call/history", {
    peerUsername: original.peerUsername,
    peerName: original.peerName,
    callId: original.callId,
    media: original.media,
    direction: original.incoming ? "incoming" : "outgoing",
    outcome: reason,
    startedAtEpochMs: original.startedAt ?? null,
    endedAtEpochMs: endedAt,
  }).catch(() => undefined);
}

function teardown(reason: string) {
  const original = session;
  if (!original || original.stage === "ended") return;
  stopTimeout();
  pendingIce.length = 0;
  original.localStream?.getTracks().forEach((track) => track.stop());
  original.remoteStream?.getTracks().forEach((track) => track.stop());
  pc?.close();
  pc = null;
  const endedAt = Date.now();
  recordHistory(original, reason, endedAt);
  session = { ...original, stage: "ended", endedReason: reason, localStream: undefined, remoteStream: undefined };
  emit();
  if (clearEndedTimer != null) window.clearTimeout(clearEndedTimer);
  clearEndedTimer = window.setTimeout(() => {
    if (session?.stage === "ended") {
      session = null;
      emit();
    }
  }, 1200);
}

export async function startWebCall(peerUsername: string, peerName: string, media: WebCallMedia, displayName: string) {
  initialize();
  if (session || startingCall) throw new Error("Bạn đang có một cuộc gọi khác.");
  if (!navigator.mediaDevices?.getUserMedia || typeof RTCPeerConnection === "undefined") {
    throw new Error("Trình duyệt này không hỗ trợ cuộc gọi WebRTC.");
  }
  startingCall = true;
  try {
    await refreshConfiguration();
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
    emit();
    try {
      // Request permission in the call tab and create tracks immediately so
      // the microphone/camera controls also work while the call is ringing.
      await acquireMedia(media);
    } catch (error) {
      teardown("media_denied");
      throw error;
    }
    signal(peerUsername, "invite", callId, { media, name: selfDisplayName });
    void api.post("/api/chat/call/ring", { toUsername: peerUsername, callId, media }).catch(() => undefined);
    armTimeout(config.outgoingTimeoutSeconds);
  } finally {
    startingCall = false;
  }
}

export async function acceptWebCall() {
  const current = session;
  if (!current || current.stage !== "incoming") return;
  try {
    await refreshConfiguration();
    await acquireMedia(current.media);
    stopTimeout();
    patch({ stage: "connecting" });
    signal(current.peerUsername, "accept", current.callId);
  } catch (error) {
    signal(current.peerUsername, "reject", current.callId, { reason: "media_denied" });
    teardown("media_denied");
    throw error;
  }
}

export function hangupWebCall(reason = "ended") {
  const current = session;
  if (!current) return;
  signal(current.peerUsername, current.stage === "incoming" ? "reject" : "end", current.callId, { reason });
  if (!current.incoming && (current.stage === "outgoing" || current.stage === "connecting")) {
    void api.post("/api/chat/call/cancel", { toUsername: current.peerUsername, callId: current.callId }).catch(() => undefined);
  }
  if (!current.incoming && current.stage === "outgoing") {
    void api.post("/api/chat/call/missed", {
      toUsername: current.peerUsername,
      callId: current.callId,
      media: current.media,
    }).catch(() => undefined);
  }
  teardown(reason);
}

export function toggleWebCallMute(): boolean {
  if (!session?.localStream?.getAudioTracks().length) return false;
  const muted = !session.muted;
  session.localStream.getAudioTracks().forEach((track) => { track.enabled = !muted; });
  patch({ muted });
  return true;
}

export function toggleWebCallCamera(): boolean {
  if (!session?.localStream?.getVideoTracks().length) return false;
  const cameraOn = !session.cameraOn;
  session.localStream.getVideoTracks().forEach((track) => { track.enabled = cameraOn; });
  patch({ cameraOn });
  return true;
}

/** Release an unanswered call from the chat tab without rejecting it. */
export function handoffIncomingWebCall(callId: string): IncomingWebCallHandoff | null {
  const current = session;
  if (!current || current.callId !== callId || current.stage !== "incoming") return null;
  stopTimeout();
  pendingIce.length = 0;
  session = null;
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
  if (session || startingCall) throw new Error("Bạn đang có một cuộc gọi khác.");
  if (!navigator.mediaDevices?.getUserMedia || typeof RTCPeerConnection === "undefined") {
    throw new Error("Trình duyệt này không hỗ trợ cuộc gọi WebRTC.");
  }
  await refreshConfiguration();
  if (!config.callsEnabled || (handoff.media === "video" && !config.videoCallEnabled)) {
    signal(handoff.peerUsername, "reject", handoff.callId, { reason: "disabled" });
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
  emit();
  await acceptWebCall();
}

export async function upgradeWebCall() {
  const current = session;
  if (!current || current.stage !== "active" || current.media === "video") return;
  if (!config.videoCallEnabled) throw new Error("Quản trị viên đang tắt gọi video.");
  await addLocalVideo();
  signal(current.peerUsername, "upgrade", current.callId);
  await createOffer();
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
