import { useCallback, useEffect, useRef, useState } from "react";
import { Loader2, Mic, MicOff, Phone, PhoneOff, Video, VideoOff, X } from "lucide-react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { useAppNotifications } from "../components/AppNotifications";
import { useAuth } from "../lib/auth";
import { initials } from "../lib/format";
import {
  acceptWebCall,
  hangupWebCall,
  resumeIncomingWebCall,
  startWebCall,
  toggleWebCallCamera,
  toggleWebCallMute,
  upgradeWebCall,
  useWebCall,
  type WebCallSession,
} from "../lib/webcall";

function formatDuration(ms: number) {
  const total = Math.max(0, Math.floor(ms / 1000));
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, "0")}`;
}

function callStatus(call: WebCallSession) {
  if (call.stage === "incoming") return `Cuộc gọi ${call.media === "video" ? "video" : "thoại"} đến`;
  if (call.stage === "outgoing") return call.remoteRinging ? "Đang đổ chuông…" : "Đang gọi…";
  if (call.stage === "connecting") return "Đang kết nối…";
  if (call.stage === "active") return "Đã kết nối";
  const reasons: Record<string, string> = {
    declined: "Đã từ chối",
    busy: "Người nhận đang bận",
    canceled: "Đã hủy",
    no_answer: "Không trả lời",
    missed: "Cuộc gọi nhỡ",
    media_denied: "Không có quyền micro/camera",
    media_error: "Không mở được âm thanh/hình ảnh",
    disconnected: "Mất kết nối",
    ended: "Cuộc gọi đã kết thúc",
    closed: "Đã đóng cửa sổ cuộc gọi",
  };
  return reasons[call.endedReason ?? ""] ?? "Cuộc gọi đã kết thúc";
}

export function CallPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const { user } = useAuth();
  const { notify } = useAppNotifications();
  const call = useWebCall();
  const localVideoRef = useRef<HTMLVideoElement>(null);
  const remoteVideoRef = useRef<HTMLVideoElement>(null);
  const bootRef = useRef(false);
  const [booted, setBooted] = useState(false);
  const [error, setError] = useState("");
  const [now, setNow] = useState(0);

  const incoming = searchParams.get("incoming") === "1";
  const callId = searchParams.get("callId")?.trim() ?? "";
  const peerUsername = searchParams.get("peer")?.trim() ?? "";
  const peerName = searchParams.get("name")?.trim() || peerUsername;
  const media = searchParams.get("media") === "video" ? "video" : "audio";

  const leavePage = useCallback(() => {
    if (window.opener && !window.opener.closed) {
      window.close();
      return;
    }
    navigate("/chats", { replace: true });
  }, [navigate]);

  useEffect(() => {
    if (!user || bootRef.current) return;
    bootRef.current = true;
    const boot = async () => {
      if (!peerUsername) throw new Error("Thiếu tài khoản cần gọi.");
      if (incoming) {
        if (!callId) throw new Error("Thiếu mã cuộc gọi đến.");
        await resumeIncomingWebCall({ callId, peerUsername, peerName, media });
      } else {
        await startWebCall(peerUsername, peerName, media, user.fullName || user.username || "Người dùng web");
      }
    };
    void boot()
      .catch((reason: unknown) => {
        setError(reason instanceof Error ? reason.message : "Không bắt đầu được cuộc gọi.");
      })
      .finally(() => setBooted(true));
  }, [callId, incoming, media, peerName, peerUsername, user]);

  useEffect(() => {
    if (localVideoRef.current) localVideoRef.current.srcObject = call?.localStream ?? null;
    if (remoteVideoRef.current) remoteVideoRef.current.srcObject = call?.remoteStream ?? null;
  }, [call?.localStream, call?.media, call?.remoteStream]);

  useEffect(() => {
    if (call?.stage !== "active") return;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [call?.stage]);

  useEffect(() => {
    const handleClose = () => hangupWebCall("closed");
    window.addEventListener("beforeunload", handleClose);
    return () => window.removeEventListener("beforeunload", handleClose);
  }, []);

  useEffect(() => {
    if (!booted || error || call) return;
    const timer = window.setTimeout(leavePage, 250);
    return () => window.clearTimeout(timer);
  }, [booted, call, error, leavePage]);

  const handleMicrophone = () => {
    if (!toggleWebCallMute()) notify.warning("Micro đang được khởi tạo, vui lòng thử lại.");
  };

  const handleCamera = async () => {
    if (!call) return;
    try {
      if (call.media === "audio") {
        if (call.stage !== "active") {
          notify.info("Camera sẽ bật sau khi cuộc gọi được kết nối.");
          return;
        }
        await upgradeWebCall();
      } else if (!toggleWebCallCamera()) {
        notify.warning("Camera đang được khởi tạo, vui lòng thử lại.");
      }
    } catch (reason) {
      notify.error(reason instanceof Error ? reason.message : "Không bật được camera.");
    }
  };

  if (error) {
    return (
      <main className="fixed inset-0 z-[200] grid place-items-center bg-slate-950 p-6 text-white">
        <div className="max-w-md text-center">
          <div className="mx-auto mb-5 grid h-16 w-16 place-items-center rounded-full bg-red-500/15 text-red-400">
            <PhoneOff className="h-7 w-7" />
          </div>
          <h1 className="text-xl font-black">Không mở được cuộc gọi</h1>
          <p className="mt-2 text-sm text-white/65">{error}</p>
          <button type="button" onClick={leavePage} className="mt-6 rounded-full bg-white px-5 py-2.5 text-sm font-bold text-slate-950">
            Đóng
          </button>
        </div>
      </main>
    );
  }

  if (!call) {
    return (
      <main className="fixed inset-0 z-[200] grid place-items-center bg-slate-950 text-white">
        <div className="text-center">
          <Loader2 className="mx-auto h-8 w-8 animate-spin text-cyan-400" />
          <div className="mt-3 text-sm font-semibold text-white/70">Đang chuẩn bị cuộc gọi…</div>
        </div>
      </main>
    );
  }

  const elapsed = call.startedAt ? now - call.startedAt : 0;
  const hasMicrophone = Boolean(call.localStream?.getAudioTracks().length);
  const hasCamera = Boolean(call.localStream?.getVideoTracks().length);

  return (
    <main className="fixed inset-0 z-[200] overflow-hidden bg-[#020617] text-white" aria-label="Cuộc gọi">
      {call.media === "video" ? (
        <>
          <video ref={remoteVideoRef} autoPlay playsInline className="absolute inset-0 h-full w-full bg-slate-950 object-cover" />
          <video
            ref={localVideoRef}
            autoPlay
            playsInline
            muted
            className="absolute right-4 top-4 z-20 h-40 w-28 rounded-2xl border border-white/25 bg-slate-900 object-cover shadow-2xl sm:right-6 sm:top-6 sm:h-52 sm:w-36"
          />
        </>
      ) : (
        <video ref={remoteVideoRef} autoPlay playsInline className="pointer-events-none absolute h-px w-px opacity-0" />
      )}

      <button
        type="button"
        onClick={() => {
          hangupWebCall();
        }}
        className="absolute left-4 top-4 z-30 grid h-10 w-10 place-items-center rounded-full bg-white/10 text-white/80 transition hover:bg-white/20 hover:text-white sm:left-6 sm:top-6"
        aria-label="Đóng cuộc gọi"
      >
        <X className="h-5 w-5" />
      </button>

      <section className="absolute inset-0 flex flex-col items-center justify-center bg-gradient-to-b from-black/20 via-transparent to-black/70 px-5 pb-32 text-center">
        {call.media === "audio" && (
          <div className="mb-5 grid h-28 w-28 place-items-center rounded-full bg-gradient-to-br from-cyan-500 to-violet-500 text-4xl font-black shadow-2xl sm:h-36 sm:w-36 sm:text-5xl">
            {initials(call.peerName)}
          </div>
        )}
        <h1 className="max-w-[80vw] truncate text-2xl font-black sm:text-3xl">{call.peerName}</h1>
        <div className="mt-2 text-sm font-semibold text-white/70">
          {call.stage === "active" ? formatDuration(elapsed) : callStatus(call)}
        </div>
      </section>

      <div className="absolute inset-x-0 bottom-0 z-30 flex min-h-28 items-center justify-center gap-5 bg-black/45 px-4 py-6 backdrop-blur-xl">
        {call.stage === "incoming" ? (
          <>
            <button
              type="button"
              onClick={() => hangupWebCall("declined")}
              className="grid h-14 w-14 place-items-center rounded-full bg-red-500 shadow-lg transition hover:bg-red-600"
              aria-label="Từ chối cuộc gọi"
            >
              <PhoneOff className="h-6 w-6" />
            </button>
            <button
              type="button"
              onClick={() => void acceptWebCall().catch((reason: unknown) => notify.error(reason instanceof Error ? reason.message : "Không nghe máy được."))}
              className="grid h-14 w-14 place-items-center rounded-full bg-emerald-500 shadow-lg transition hover:bg-emerald-600"
              aria-label="Nghe máy"
            >
              <Phone className="h-6 w-6" />
            </button>
          </>
        ) : call.stage !== "ended" ? (
          <>
            <button
              type="button"
              onClick={handleMicrophone}
              className={`grid h-13 w-13 place-items-center rounded-full transition ${call.muted ? "bg-white text-slate-950" : "bg-white/15 hover:bg-white/25"} ${hasMicrophone ? "" : "opacity-60"}`}
              aria-label={call.muted ? "Bật micro" : "Tắt micro"}
              title={call.muted ? "Bật micro" : "Tắt micro"}
            >
              {call.muted ? <MicOff className="h-5 w-5" /> : <Mic className="h-5 w-5" />}
            </button>
            <button
              type="button"
              onClick={() => void handleCamera()}
              className={`grid h-13 w-13 place-items-center rounded-full transition ${call.media === "video" && !call.cameraOn ? "bg-white text-slate-950" : "bg-white/15 hover:bg-white/25"} ${call.media === "video" && !hasCamera ? "opacity-60" : ""}`}
              aria-label={call.media === "audio" ? "Chuyển sang gọi video" : call.cameraOn ? "Tắt camera" : "Bật camera"}
              title={call.media === "audio" ? "Chuyển sang gọi video" : call.cameraOn ? "Tắt camera" : "Bật camera"}
            >
              {call.media === "video" && !call.cameraOn ? <VideoOff className="h-5 w-5" /> : <Video className="h-5 w-5" />}
            </button>
            <button
              type="button"
              onClick={() => hangupWebCall()}
              className="grid h-14 w-14 place-items-center rounded-full bg-red-500 shadow-lg transition hover:bg-red-600"
              aria-label="Kết thúc cuộc gọi"
              title="Kết thúc cuộc gọi"
            >
              <PhoneOff className="h-6 w-6" />
            </button>
          </>
        ) : (
          <div className="text-sm font-semibold text-white/75">{callStatus(call)}</div>
        )}
      </div>
    </main>
  );
}
