import { useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import {
  ArrowLeft,
  Ban,
  CheckCheck,
  Download,
  EyeOff,
  FileText,
  Flag,
  Forward,
  Home,
  Loader2,
  MessageCircle,
  Mic,
  MoreHorizontal,
  Paperclip,
  Pause,
  Pencil,
  Phone,
  PhoneOff,
  Pin,
  PinOff,
  Play,
  Search,
  Send,
  Smile,
  SmilePlus,
  SquarePen,
  Trash2,
  Upload,
  User,
  Video,
  X,
} from "lucide-react";
import type { LucideIcon } from "lucide-react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { initials } from "../lib/format";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Modal } from "../components/Modal";
import { DiamondLabel, VerifiedBadge } from "../components/VerifiedBadge";
import { useAuth } from "../lib/auth";
import { useApi } from "../lib/useApi";
import { api } from "../lib/api";
import { useAppNotifications } from "../components/AppNotifications";
import { formatBytes } from "../lib/format";
import {
  redownload,
  startSend,
  useTransfers,
  type TransferInfo,
} from "../lib/filetransfer";
import type { ChatContact, ChatConversation, ChatMessage, ChatReaction } from "../lib/types";
import {
  handoffIncomingWebCall,
  hangupWebCall,
  useWebCall,
  type WebCallSession,
} from "../lib/webcall";
import "../features/giacong/giacong.css";

/* =========================================================================
   Trang Trò chuyện — khung 3 cột (danh sách / hội thoại / hồ sơ).
   Dữ liệu thật từ /api/chat: danh bạ và tên hiển thị lấy từ hệ tài khoản của web
   (full_name), trạng thái online từ phiên đăng nhập, tích xanh cho Admin / tài
   khoản được cấp. Giữ nguyên hệ màu & tấm kính của app (accent + glass).
   ========================================================================= */

type Filter = "all" | "unread" | "groups";
const SUPPORT_USERNAME = "__support__";

const FILTERS: { key: Filter; label: string }[] = [
  { key: "all", label: "Tất cả" },
  { key: "unread", label: "Chưa đọc" },
  { key: "groups", label: "Nhóm" },
];

/** Giờ HH:mm cho hôm nay, ngày/tháng cho hôm khác. */
function fmtTime(iso?: string | null) {
  if (!iso) return "";
  const d = new Date(iso);
  if (isNaN(d.getTime())) return "";
  const sameDay = d.toDateString() === new Date().toDateString();
  return sameDay
    ? d.toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit" })
    : d.toLocaleDateString("vi-VN", { day: "2-digit", month: "2-digit" });
}
function fmtClock(iso?: string | null) {
  if (!iso) return "";
  const d = new Date(iso);
  return isNaN(d.getTime()) ? "" : d.toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit" });
}

function fmtDuration(ms: number) {
  const total = Math.max(0, Math.floor(ms / 1000));
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, "0")}`;
}

/** Nhận cả kind=voice mới và bản ghi APK cũ từng được lưu dưới kind=file. */
function isVoiceMessage(msg: ChatMessage) {
  if (msg.removed) return false;
  if (msg.kind === "voice") return true;
  if (msg.kind !== "file") return false;
  const mime = (msg.fileMime ?? "").toLowerCase();
  const name = (msg.fileName ?? "").toLowerCase();
  if (mime.startsWith("audio/")) return true;
  if (mime.startsWith("image/") || mime.startsWith("video/")) return false;
  if (/^ghi-am-/.test(name)) return /\.(aac|amr|m4a|mp3|ogg|opus|wav|webm)$/.test(name);
  return /\.(aac|amr|m4a|mp3|ogg|opus|wav)$/.test(name);
}

type InlineMediaKind = "image" | "video";

function detectInlineMedia(nameValue?: string | null, mimeValue?: string | null): InlineMediaKind | null {
  const mime = (mimeValue ?? "").toLowerCase();
  const name = (nameValue ?? "").toLowerCase().split(/[?#]/)[0];
  if (mime.startsWith("image/")) return "image";
  if (mime.startsWith("video/")) return "video";
  if (/\.(avif|bmp|gif|heic|heif|jpe?g|png|svg|webp)$/.test(name)) return "image";
  if (/^ghi-am-/.test(name)) return null;
  if (/\.(3gp|avi|m4v|mkv|mov|mp4|mpeg|mpg|ogv|webm)$/.test(name)) return "video";
  return null;
}

/** APK và web cũ đều lưu ảnh/video dưới kind=file, nên nhận diện thêm bằng MIME và đuôi tệp. */
function inlineMediaKind(msg: ChatMessage, transfer?: TransferInfo): InlineMediaKind | null {
  return detectInlineMedia(msg.fileName ?? transfer?.name, msg.fileMime ?? transfer?.mime);
}

/** Trạng thái "Hoạt động X phút/giờ/ngày trước". Quá 1 giờ → "khoảng X giờ trước". */
function lastActive(iso?: string | null) {
  if (!iso) return "Không hoạt động";
  const d = new Date(iso);
  if (isNaN(d.getTime())) return "Hoạt động gần đây";
  const sec = Math.max(0, Math.floor((Date.now() - d.getTime()) / 1000));
  if (sec < 60) return "Vừa truy cập";
  const min = Math.floor(sec / 60);
  if (min < 60) return `Hoạt động ${min} phút trước`;
  const hr = Math.round(min / 60);
  if (hr < 24) return `Hoạt động khoảng ${hr} giờ trước`;
  const day = Math.round(hr / 24);
  if (day < 7) return `Hoạt động ${day} ngày trước`;
  const wk = Math.round(day / 7);
  if (wk < 5) return `Hoạt động ${wk} tuần trước`;
  const mo = Math.round(day / 30);
  if (mo < 12) return `Hoạt động ${mo} tháng trước`;
  return `Hoạt động ${Math.round(day / 365)} năm trước`;
}

/** Avatar: dùng ảnh đại diện nếu có, nếu không thì chữ viết tắt + gradient accent. */
function Avatar({
  name,
  url,
  size = 44,
  online,
  group,
}: {
  name: string;
  url?: string | null;
  size?: number;
  online?: boolean;
  group?: boolean;
}) {
  return (
    <div className="relative shrink-0" style={{ width: size, height: size }}>
      <div
        className="grid h-full w-full place-items-center overflow-hidden rounded-full font-bold text-white"
        style={{
          background: "linear-gradient(135deg, var(--accent), var(--purple))",
          fontSize: size * 0.36,
          boxShadow: "inset 0 1px 0 rgba(255,255,255,0.4)",
        }}
      >
        {url ? (
          <img src={url} alt="" className="h-full w-full object-cover" />
        ) : group ? (
          <MessageCircle style={{ width: size * 0.5, height: size * 0.5 }} />
        ) : (
          initials(name)
        )}
      </div>
      {online && (
        <span
          className="absolute bottom-0 right-0 rounded-full border-2"
          style={{
            width: size * 0.26,
            height: size * 0.26,
            background: "var(--success)",
            borderColor: "var(--glass-bg-strong)",
          }}
        />
      )}
    </div>
  );
}

/** Tên + tích xanh, nhãn kim cương nằm dưới để không chen mất tên. */
function NameWithBadge({
  name,
  verified,
  isDiamond,
  className,
}: {
  name: string;
  verified?: boolean;
  isDiamond?: boolean;
  className?: string;
}) {
  return (
    <span className={`inline-flex min-w-0 max-w-full flex-col items-start gap-1 ${className ?? ""}`}>
      <span className="inline-flex min-w-0 max-w-full items-center gap-1">
        <span className="min-w-0 truncate">{name}</span>
        {verified && <VerifiedBadge size={15} />}
      </span>
      {isDiamond && <DiamondLabel />}
    </span>
  );
}

/** Mục trong menu tùy chọn tin nhắn. */
function MenuItem({
  icon: Icon,
  label,
  onClick,
  danger,
}: {
  icon: LucideIcon;
  label: string;
  onClick: () => void;
  danger?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`flex w-full items-center gap-2 px-3 py-2 text-sm font-medium transition hover:bg-[var(--accent-soft)] ${
        danger ? "text-[var(--danger)]" : "text-[var(--text-secondary)]"
      }`}
    >
      <Icon className="h-4 w-4" />
      {label}
    </button>
  );
}

const MENU_WIDTH = 160;
const CONVERSATION_MENU_WIDTH = 184;

/** Nút "..." (hiện khi rê chuột) mở menu Chuyển tiếp / Chỉnh sửa / Gỡ.
 *  Menu render qua portal ra <body> với vị trí fixed → không bị vùng chat (overflow)
 *  cắt mất; tự mở lên trên nếu không đủ chỗ bên dưới. */
function MessageMenu({
  mine,
  onForward,
  onEdit,
  onRemove,
}: {
  mine: boolean;
  onForward: () => void;
  onEdit: () => void;
  onRemove: () => void;
}) {
  const [open, setOpen] = useState(false);
  const btnRef = useRef<HTMLButtonElement>(null);
  const [pos, setPos] = useState<PickerPosition>({ left: 0 });

  const toggle = () => {
    if (!open && btnRef.current) {
      const r = btnRef.current.getBoundingClientRect();
      let left = mine ? r.right - MENU_WIDTH : r.left;
      left = Math.max(8, Math.min(left, window.innerWidth - MENU_WIDTH - 8));
      // Đủ chỗ bên dưới thì mở xuống; nếu sát đáy thì neo đáy menu phía trên nút.
      const openUp = window.innerHeight - r.bottom < 170;
      setPos(openUp ? { left, bottom: window.innerHeight - r.top + 4 } : { left, top: r.bottom + 4 });
    }
    setOpen((o) => !o);
  };

  return (
    <div className="hidden transition sm:block sm:opacity-0 sm:group-hover:opacity-100">
      <button
        ref={btnRef}
        type="button"
        onClick={toggle}
        aria-label="Tùy chọn tin nhắn"
        className="grid h-7 w-7 place-items-center rounded-full text-[var(--text-muted)] transition hover:bg-[var(--accent-soft)] hover:text-[var(--accent)]"
      >
        <MoreHorizontal className="h-4 w-4" />
      </button>
      {open &&
        createPortal(
          <>
            <div className="fixed inset-0 z-[90]" onClick={() => setOpen(false)} />
            <div
              className="fixed z-[91] overflow-hidden rounded-xl border border-[var(--glass-border)] py-1 shadow-lg"
              style={{
                left: pos.left,
                top: pos.top,
                bottom: pos.bottom,
                width: MENU_WIDTH,
                background: "var(--glass-bg-strong)",
                backdropFilter: "blur(8px)",
                WebkitBackdropFilter: "blur(8px)",
              }}
            >
              <MenuItem icon={Forward} label="Chuyển tiếp" onClick={() => { setOpen(false); onForward(); }} />
              {mine && <MenuItem icon={Pencil} label="Chỉnh sửa" onClick={() => { setOpen(false); onEdit(); }} />}
              {mine && <MenuItem icon={Trash2} label="Gỡ" danger onClick={() => { setOpen(false); onRemove(); }} />}
            </div>
          </>,
          document.body,
        )}
    </div>
  );
}

/** Bộ biểu cảm có thể thả cho một tin nhắn. */
const REACTIONS = ["👍", "❤️", "😂", "😮", "😢", "🙏"];
const PICKER_WIDTH = 256;
type PickerPosition = { left: number; top?: number; bottom?: number };

function ReactionBarPortal({
  pos,
  onClose,
  onReact,
}: {
  pos: PickerPosition;
  onClose: () => void;
  onReact: (emoji: string) => void;
}) {
  return createPortal(
    <>
      <div className="fixed inset-0 z-[90]" onClick={onClose} />
      <div
        className="fixed z-[91] flex items-center gap-0.5 rounded-full border border-[var(--glass-border)] px-1.5 py-1 shadow-lg"
        style={{
          left: pos.left,
          top: pos.top,
          bottom: pos.bottom,
          background: "var(--glass-bg-strong)",
          backdropFilter: "blur(8px)",
          WebkitBackdropFilter: "blur(8px)",
        }}
      >
        {REACTIONS.map((e) => (
          <button
            key={e}
            type="button"
            onClick={() => {
              onClose();
              onReact(e);
            }}
            className="grid h-8 w-8 place-items-center rounded-full text-lg leading-none transition hover:scale-125 hover:bg-[var(--accent-soft)]"
          >
            {e}
          </button>
        ))}
      </div>
    </>,
    document.body,
  );
}

/** Nút "mặt cười +" (hiện khi rê chuột) mở thanh chọn biểu cảm. Cùng kiểu portal/fixed như
 *  MessageMenu để không bị vùng chat (overflow) cắt mất; tự mở lên trên nếu sát đáy. */
function ReactionPicker({ mine, onReact }: { mine: boolean; onReact: (emoji: string) => void }) {
  const [open, setOpen] = useState(false);
  const btnRef = useRef<HTMLButtonElement>(null);
  const [pos, setPos] = useState<{ left: number; top?: number; bottom?: number }>({ left: 0 });

  const toggle = () => {
    if (!open && btnRef.current) {
      const r = btnRef.current.getBoundingClientRect();
      let left = mine ? r.right - PICKER_WIDTH : r.left;
      left = Math.max(8, Math.min(left, window.innerWidth - PICKER_WIDTH - 8));
      const openUp = window.innerHeight - r.bottom < 80;
      setPos(openUp ? { left, bottom: window.innerHeight - r.top + 4 } : { left, top: r.bottom + 4 });
    }
    setOpen((o) => !o);
  };

  return (
    <div className="opacity-100 transition sm:opacity-0 sm:group-hover:opacity-100">
      <button
        ref={btnRef}
        type="button"
        onClick={toggle}
        aria-label="Thả biểu cảm"
        className="grid h-7 w-7 place-items-center rounded-full text-[var(--text-muted)] transition hover:bg-[var(--accent-soft)] hover:text-[var(--accent)]"
      >
        <SmilePlus className="h-4 w-4" />
      </button>
      {open &&
        createPortal(
          <>
            <div className="fixed inset-0 z-[90]" onClick={() => setOpen(false)} />
            <div
              className="fixed z-[91] flex items-center gap-0.5 rounded-full border border-[var(--glass-border)] px-1.5 py-1 shadow-lg"
              style={{
                left: pos.left,
                top: pos.top,
                bottom: pos.bottom,
                background: "var(--glass-bg-strong)",
                backdropFilter: "blur(8px)",
                WebkitBackdropFilter: "blur(8px)",
              }}
            >
              {REACTIONS.map((e) => (
                <button
                  key={e}
                  type="button"
                  onClick={() => { setOpen(false); onReact(e); }}
                  className="grid h-8 w-8 place-items-center rounded-full text-lg leading-none transition hover:scale-125 hover:bg-[var(--accent-soft)]"
                >
                  {e}
                </button>
              ))}
            </div>
          </>,
          document.body,
        )}
    </div>
  );
}

/** Hàng "chip" biểu cảm dưới mỗi bong bóng — bấm để bỏ/đổi nhanh biểu cảm của mình. */
function ReactionChips({
  reactions,
  mine,
  onReact,
}: {
  reactions: ChatReaction[];
  mine: boolean;
  onReact: (emoji: string) => void;
}) {
  if (reactions.length === 0) return null;
  return (
    <div className={`mt-1 flex flex-wrap gap-1 ${mine ? "justify-end pr-1" : "justify-start pl-1"}`}>
      {reactions.map((rx) => (
        <button
          key={rx.emoji}
          type="button"
          onClick={() => onReact(rx.emoji)}
          title={rx.mine ? "Bỏ biểu cảm" : "Thả biểu cảm này"}
          className="inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs font-semibold transition hover:opacity-90"
          style={
            rx.mine
              ? { background: "var(--accent-soft)", borderColor: "var(--accent)", color: "var(--accent)" }
              : { background: "var(--glass-bg-strong)", borderColor: "var(--glass-border)", color: "var(--text-secondary)" }
          }
        >
          <span className="text-sm leading-none">{rx.emoji}</span>
          <span>{rx.count}</span>
        </button>
      ))}
    </div>
  );
}

function Bubble({
  msg,
  group,
  onForward,
  onEdit,
  onRemove,
  onReact,
}: {
  msg: ChatMessage;
  group?: boolean;
  onForward: () => void;
  onEdit: () => void;
  onRemove: () => void;
  onReact: (emoji: string) => void;
}) {
  const mine = msg.mine;
  const reactions = msg.reactions ?? [];
  const [longPressPos, setLongPressPos] = useState<PickerPosition | null>(null);
  const longPressTimerRef = useRef<number | null>(null);
  const longPressStartRef = useRef<{ x: number; y: number } | null>(null);

  const clearLongPress = () => {
    if (longPressTimerRef.current != null) {
      window.clearTimeout(longPressTimerRef.current);
      longPressTimerRef.current = null;
    }
  };

  const openLongPressPicker = (target: HTMLDivElement) => {
    const r = target.getBoundingClientRect();
    let left = r.left + r.width / 2 - PICKER_WIDTH / 2;
    left = Math.max(8, Math.min(left, window.innerWidth - PICKER_WIDTH - 8));
    const openUp = r.top > 72;
    setLongPressPos(openUp ? { left, bottom: window.innerHeight - r.top + 8 } : { left, top: r.bottom + 8 });
    navigator.vibrate?.(12);
  };

  const handleBubbleTouchStart = (e: React.TouchEvent<HTMLDivElement>) => {
    if (!window.matchMedia("(max-width: 767px)").matches) return;
    const t = e.touches[0];
    if (!t) return;
    longPressStartRef.current = { x: t.clientX, y: t.clientY };
    const target = e.currentTarget;
    clearLongPress();
    longPressTimerRef.current = window.setTimeout(() => openLongPressPicker(target), 460);
  };

  const handleBubbleTouchMove = (e: React.TouchEvent<HTMLDivElement>) => {
    const start = longPressStartRef.current;
    const t = e.touches[0];
    if (!start || !t) return;
    if (Math.abs(t.clientX - start.x) > 12 || Math.abs(t.clientY - start.y) > 12) clearLongPress();
  };

  const handleBubbleTouchEnd = () => {
    clearLongPress();
    longPressStartRef.current = null;
  };

  // Tin đã gỡ: hiển thị placeholder mờ, không có menu tùy chọn.
  if (msg.removed) {
    return (
      <div className={`flex ${mine ? "justify-end" : "justify-start"}`}>
        <div className="inline-flex max-w-[78%] items-center gap-1.5 rounded-2xl border border-dashed border-[var(--glass-border)] px-3.5 py-2 text-[0.85rem] italic text-[var(--text-muted)]">
          <Ban className="h-3.5 w-3.5 shrink-0" />
          Tin nhắn đã được gỡ
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col">
      {group && !mine && (
        <span className="mb-0.5 ml-1 text-[0.68rem] font-semibold text-[var(--text-secondary)]">{msg.senderName}</span>
      )}
      <div className={`group flex w-full items-center gap-1 ${mine ? "justify-end" : "justify-start"}`}>
        {mine && (
          <>
            <MessageMenu mine onForward={onForward} onEdit={onEdit} onRemove={onRemove} />
            <ReactionPicker mine onReact={onReact} />
          </>
        )}
        <div
          className="chat-message-bubble max-w-[86%] rounded-2xl px-3.5 py-2.5 text-[0.9rem] leading-relaxed shadow-sm sm:max-w-[78%]"
          onTouchStart={handleBubbleTouchStart}
          onTouchMove={handleBubbleTouchMove}
          onTouchEnd={handleBubbleTouchEnd}
          onTouchCancel={handleBubbleTouchEnd}
          onSelect={(e) => e.preventDefault()}
          onContextMenu={(e) => {
            if (window.matchMedia("(max-width: 767px)").matches) e.preventDefault();
          }}
          style={
            mine
              ? { background: "var(--accent)", color: "#fff", borderTopRightRadius: 6 }
              : {
                  background: "var(--glass-bg-strong)",
                  border: "1px solid var(--glass-border)",
                  color: "var(--text)",
                  borderTopLeftRadius: 6,
                }
          }
        >
          {msg.forwarded && (
            <div className={`mb-1 flex items-center gap-1 text-[0.66rem] italic ${mine ? "text-white/80" : "text-[var(--text-muted)]"}`}>
              <Forward className="h-3 w-3" />
              Đã chuyển tiếp
            </div>
          )}
          <span className="whitespace-pre-wrap break-words">{msg.body}</span>
          <div
            className={`mt-1 flex items-center justify-end gap-1 text-[0.68rem] ${
              mine ? "text-white/75" : "text-[var(--text-muted)]"
            }`}
          >
            {msg.editedAt && <span>đã chỉnh sửa ·</span>}
            {fmtClock(msg.createdAt)}
            {mine && (
              <CheckCheck
                className={`h-3.5 w-3.5 ${msg.read ? "opacity-100" : "opacity-60"}`}
                aria-label={msg.read ? "Đã đọc" : "Đã gửi"}
              />
            )}
          </div>
        </div>
        {!mine && (
          <>
            <ReactionPicker mine={false} onReact={onReact} />
            <MessageMenu mine={false} onForward={onForward} onEdit={onEdit} onRemove={onRemove} />
          </>
        )}
      </div>
      <ReactionChips reactions={reactions} mine={mine} onReact={onReact} />
      {longPressPos && (
        <ReactionBarPortal pos={longPressPos} onClose={() => setLongPressPos(null)} onReact={onReact} />
      )}
    </div>
  );
}

/** Trình phát tin thoại ngay trong bong bóng, tải lười và giữ URL trong vòng đời của tin. */
function VoicePlayer({
  msg,
  mine,
  onLoad,
}: {
  msg: ChatMessage;
  mine: boolean;
  onLoad: () => Promise<Blob>;
}) {
  const audioRef = useRef<HTMLAudioElement>(null);
  const autoplayRef = useRef(false);
  const [url, setUrl] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [playing, setPlaying] = useState(false);
  const [duration, setDuration] = useState(0);
  const [position, setPosition] = useState(0);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => () => {
    if (url) URL.revokeObjectURL(url);
  }, [url]);

  useEffect(() => {
    if (!url || !autoplayRef.current) return;
    autoplayRef.current = false;
    audioRef.current?.play().catch(() => setError("Trình duyệt không phát được định dạng âm thanh này."));
  }, [url]);

  const toggle = async () => {
    const audio = audioRef.current;
    if (audio && url) {
      if (audio.paused) await audio.play().catch(() => setError("Không phát được tin thoại."));
      else audio.pause();
      return;
    }
    if (loading || !msg.hasBlob) return;
    setLoading(true);
    setError(null);
    try {
      const blob = await onLoad();
      autoplayRef.current = true;
      setUrl(URL.createObjectURL(blob));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Không tải được tin thoại.");
    } finally {
      setLoading(false);
    }
  };

  const safeDuration = Number.isFinite(duration) ? duration : 0;
  return (
    <div className="min-w-[230px]">
      <audio
        ref={audioRef}
        src={url ?? undefined}
        preload="metadata"
        onLoadedMetadata={(e) => setDuration(Number.isFinite(e.currentTarget.duration) ? e.currentTarget.duration : 0)}
        onDurationChange={(e) => setDuration(Number.isFinite(e.currentTarget.duration) ? e.currentTarget.duration : 0)}
        onTimeUpdate={(e) => setPosition(e.currentTarget.currentTime)}
        onPlay={() => setPlaying(true)}
        onPause={() => setPlaying(false)}
        onEnded={() => { setPlaying(false); setPosition(0); }}
      />
      <div className="flex items-center gap-2.5">
        <button
          type="button"
          onClick={() => void toggle()}
          disabled={loading || !msg.hasBlob}
          className="grid h-10 w-10 shrink-0 place-items-center rounded-full transition hover:opacity-85 disabled:opacity-50"
          style={{ background: mine ? "rgba(255,255,255,0.2)" : "var(--accent-soft)", color: mine ? "#fff" : "var(--accent)" }}
          aria-label={playing ? "Tạm dừng tin thoại" : "Phát tin thoại"}
        >
          {loading ? <Loader2 className="h-5 w-5 animate-spin" /> : playing ? <Pause className="h-5 w-5" /> : <Play className="ml-0.5 h-5 w-5" />}
        </button>
        <div className="min-w-0 flex-1">
          <div className="mb-1 flex items-center gap-1.5 text-xs font-semibold">
            <Mic className="h-3.5 w-3.5" />
            Tin nhắn thoại
          </div>
          <input
            type="range"
            min={0}
            max={Math.max(safeDuration, 0.01)}
            step={0.05}
            value={Math.min(position, Math.max(safeDuration, 0.01))}
            disabled={!url || safeDuration <= 0}
            onChange={(e) => {
              const next = Number(e.target.value);
              if (audioRef.current) audioRef.current.currentTime = next;
              setPosition(next);
            }}
            className="block h-1.5 w-full cursor-pointer accent-current disabled:cursor-default"
            aria-label="Vị trí phát tin thoại"
          />
        </div>
        <span className={`w-10 text-right text-[0.68rem] tabular-nums ${mine ? "text-white/75" : "text-[var(--text-muted)]"}`}>
          {fmtDuration((position || safeDuration) * 1000)}
        </span>
      </div>
      {error && <div className={`mt-1.5 text-xs ${mine ? "text-white/85" : "text-[var(--danger)]"}`}>{error}</div>}
    </div>
  );
}

/** Xem ảnh/video trực tiếp từ blob P2P hoặc blob có xác thực trên máy chủ. */
function InlineMedia({
  kind,
  messageId,
  conversationId,
  name,
  transferUrl,
  hasServerBlob,
  receiving,
  progress,
}: {
  kind: InlineMediaKind;
  messageId: number;
  conversationId: string;
  name: string;
  transferUrl?: string;
  hasServerBlob: boolean;
  receiving: boolean;
  progress: number | null;
}) {
  const [serverUrl, setServerUrl] = useState<string | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [mediaError, setMediaError] = useState(false);
  const [expanded, setExpanded] = useState(false);
  const [retryToken, setRetryToken] = useState(0);
  const loadingRef = useRef(false);
  const mountedRef = useRef(true);
  const ownedUrlsRef = useRef<string[]>([]);
  const sourceUrl = transferUrl ?? serverUrl;

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      for (const url of ownedUrlsRef.current) URL.revokeObjectURL(url);
      ownedUrlsRef.current = [];
    };
  }, []);

  useEffect(() => {
    if (sourceUrl || !hasServerBlob || loadingRef.current) return;
    loadingRef.current = true;
    void api
      .getBlob(`/api/chat/conversations/${conversationId}/messages/${messageId}/download`)
      .then((blob) => {
        if (!mountedRef.current) return;
        const url = URL.createObjectURL(blob);
        ownedUrlsRef.current.push(url);
        setServerUrl(url);
        setLoadError(null);
      })
      .catch((error: unknown) => {
        if (mountedRef.current) setLoadError(error instanceof Error ? error.message : "Không tải được nội dung.");
      })
      .finally(() => {
        loadingRef.current = false;
      });
  }, [conversationId, hasServerBlob, messageId, retryToken, sourceUrl]);

  if (!sourceUrl) {
    return (
      <div className="grid min-h-44 w-[min(360px,72vw)] place-items-center rounded-xl bg-black/15 px-5 py-8 text-center">
        <div>
          {receiving || (hasServerBlob && !loadError) ? (
            <Loader2 className="mx-auto h-7 w-7 animate-spin opacity-80" />
          ) : (
            <Video className="mx-auto h-8 w-8 opacity-65" />
          )}
          <div className="mt-2 text-xs font-semibold">
            {receiving
              ? `Đang nhận ${kind === "image" ? "ảnh" : "video"}${progress == null ? "…" : `… ${progress}%`}`
              : loadError
                ? "Không tải được nội dung"
                : hasServerBlob
                  ? `Đang tải ${kind === "image" ? "ảnh" : "video"}…`
                  : `${kind === "image" ? "Ảnh" : "Video"} không còn sẵn sàng`}
          </div>
          {loadError && (
            <button
              type="button"
              onClick={() => {
                setLoadError(null);
                setRetryToken((value) => value + 1);
              }}
              className="mt-2 rounded-full bg-white/15 px-3 py-1 text-xs font-bold hover:bg-white/25"
            >
              Thử lại
            </button>
          )}
        </div>
      </div>
    );
  }

  if (kind === "video") {
    return (
      <div className="w-[min(420px,74vw)] overflow-hidden rounded-xl bg-black">
        <video
          src={sourceUrl}
          controls
          playsInline
          preload="metadata"
          onError={() => setMediaError(true)}
          className="max-h-[420px] w-full object-contain"
          aria-label={name}
        />
        {mediaError && <div className="px-3 py-2 text-center text-xs text-white/80">Trình duyệt không phát được định dạng video này.</div>}
      </div>
    );
  }

  return (
    <>
      <button type="button" onClick={() => setExpanded(true)} className="block overflow-hidden rounded-xl" aria-label={`Xem ảnh ${name}`}>
        <img
          src={sourceUrl}
          alt={name}
          loading="lazy"
          onError={() => setMediaError(true)}
          className="max-h-[420px] w-[min(380px,74vw)] object-contain"
        />
        {mediaError && <span className="block px-3 py-2 text-xs">Không hiển thị được ảnh này.</span>}
      </button>
      {expanded &&
        createPortal(
          <div className="fixed inset-0 z-[130] grid place-items-center bg-black/90 p-4 backdrop-blur-sm" role="dialog" aria-modal="true" aria-label={name}>
            <button
              type="button"
              onClick={() => setExpanded(false)}
              className="absolute right-4 top-4 z-10 grid h-11 w-11 place-items-center rounded-full bg-white/15 text-white hover:bg-white/25"
              aria-label="Đóng ảnh"
            >
              <X className="h-6 w-6" />
            </button>
            <img src={sourceUrl} alt={name} className="max-h-full max-w-full object-contain" />
          </div>,
          document.body,
        )}
    </>
  );
}

/** Bong bóng ảnh/video, file LAN/store-and-forward hoặc voice bền vững dùng chung với APK. */
function FileBubble({
  msg,
  conversationId,
  transfer,
  serverBusy,
  onRedownload,
  onServerDownload,
  onLoadVoice,
  onReact,
}: {
  msg: ChatMessage;
  conversationId: string;
  transfer?: TransferInfo;
  serverBusy?: boolean;
  onRedownload: () => void;
  onServerDownload: () => void;
  onLoadVoice: () => Promise<Blob>;
  onReact: (emoji: string) => void;
}) {
  const mine = msg.mine;
  const voice = isVoiceMessage(msg);
  const mediaKind = inlineMediaKind(msg, transfer);
  const reactions = msg.reactions ?? [];
  const name = voice ? "Tin nhắn thoại" : (msg.fileName ?? transfer?.name ?? "Tệp");
  const size = msg.fileSize ?? transfer?.size ?? 0;
  const st = transfer?.status;
  const pct =
    transfer && transfer.size > 0 ? Math.min(100, Math.floor((transfer.transferred / transfer.size) * 100)) : null;
  const showBar = st === "transferring";

  // Người nhận có thể tải bản server khi không đang/đã nhận trực tiếp qua P2P. Voice vẫn còn trên
  // server sau lượt tải này; file thường giữ chính sách store-and-forward cũ.
  const p2pReceiving = st === "connecting" || st === "transferring";
  const p2pGotBlob = st === "done" && !!transfer?.blobUrl;
  const canServerDownload = !mine && !!msg.hasBlob && !p2pReceiving && !p2pGotBlob;
  const canRedownload = !mine && p2pGotBlob;

  type Tone = "muted" | "accent" | "success" | "danger";
  let statusText = mine ? "Đã gửi" : (voice ? "Tin thoại chưa sẵn sàng" : "Tệp đã gửi — cần gửi lại để tải");
  let tone: Tone = "muted";
  if (canServerDownload) {
    statusText = voice ? "Bấm để tải tin thoại" : "Người gửi đã lưu trên máy chủ — bấm để tải";
    tone = "accent";
  } else if (st === "inviting") {
    statusText = "Đang chờ người nhận đồng ý…";
  } else if (st === "incoming") {
    statusText = "Có lời mời nhận — xem thông báo";
    tone = "accent";
  } else if (st === "connecting") {
    statusText = "Đang kết nối…";
    tone = "accent";
  } else if (st === "transferring") {
    statusText = `${mine ? "Đang gửi" : "Đang nhận"}… ${pct ?? 0}%`;
    tone = "accent";
  } else if (st === "uploading") {
    statusText = "Người nhận offline — đang lưu lên máy chủ…";
    tone = "accent";
  } else if (st === "stored") {
    statusText = "Đã lưu trên máy chủ — chờ người nhận tải";
    tone = "success";
  } else if (st === "done") {
    statusText = mine ? "Đã gửi xong" : "Đã nhận xong";
    tone = "success";
  } else if (st === "declined") {
    statusText = mine ? "Người nhận đã từ chối" : "Bạn đã từ chối";
    tone = "danger";
  } else if (st === "canceled") {
    statusText = "Đã hủy";
    tone = "danger";
  } else if (st === "error") {
    statusText = transfer?.error ?? "Truyền tệp lỗi";
    tone = "danger";
  } else if (mine && msg.hasBlob) {
    statusText = voice ? "Tin thoại đã lưu trên máy chủ" : "Đã lưu trên máy chủ — chờ người nhận tải";
  }

  const toneColor =
    tone === "success"
      ? "var(--success)"
      : tone === "danger"
        ? "var(--danger)"
        : tone === "accent"
          ? mine
            ? "rgba(255,255,255,0.92)"
            : "var(--accent)"
          : mine
            ? "rgba(255,255,255,0.78)"
            : "var(--text-muted)";

  return (
    <div className="flex flex-col">
      <div className={`group flex w-full items-center gap-1 ${mine ? "justify-end" : "justify-start"}`}>
        {mine && <ReactionPicker mine onReact={onReact} />}
        <div
          className="chat-message-bubble max-w-[90%] rounded-2xl px-3 py-2.5 shadow-sm sm:max-w-[78%]"
          style={
            mine
              ? { background: "var(--accent)", color: "#fff", borderTopRightRadius: 6, minWidth: 220 }
              : {
                  background: "var(--glass-bg-strong)",
                  border: "1px solid var(--glass-border)",
                  color: "var(--text)",
                  borderTopLeftRadius: 6,
                  minWidth: 220,
                }
          }
        >
          {voice ? (
            <VoicePlayer msg={msg} mine={mine} onLoad={onLoadVoice} />
          ) : mediaKind ? (
            <InlineMedia
              kind={mediaKind}
              messageId={msg.id}
              conversationId={conversationId}
              name={name}
              transferUrl={transfer?.blobUrl}
              hasServerBlob={!!msg.hasBlob}
              receiving={p2pReceiving}
              progress={pct}
            />
          ) : (
            <div className="flex items-center gap-2.5">
              <span
                className="grid h-10 w-10 shrink-0 place-items-center rounded-xl"
                style={{ background: mine ? "rgba(255,255,255,0.18)" : "var(--accent-soft)", color: mine ? "#fff" : "var(--accent)" }}
              >
                <FileText className="h-5 w-5" />
              </span>
              <div className="min-w-0 flex-1">
                <div className="truncate text-[0.88rem] font-semibold" title={name}>
                  {name}
                </div>
                <div className={`text-[0.7rem] ${mine ? "text-white/75" : "text-[var(--text-muted)]"}`}>
                  {formatBytes(size)}
                </div>
              </div>
              {(canServerDownload || canRedownload) && (
                <button
                  type="button"
                  onClick={canServerDownload ? onServerDownload : onRedownload}
                  disabled={serverBusy}
                  aria-label="Tải tệp"
                  className="grid h-8 w-8 shrink-0 place-items-center rounded-full transition hover:opacity-80 disabled:opacity-50"
                  style={{ background: mine ? "rgba(255,255,255,0.18)" : "var(--accent-soft)", color: mine ? "#fff" : "var(--accent)" }}
                >
                  {serverBusy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Download className="h-4 w-4" />}
                </button>
              )}
            </div>
          )}
          {showBar && !voice && !mediaKind && (
            <div className="mt-2 h-1.5 w-full overflow-hidden rounded-full" style={{ background: mine ? "rgba(255,255,255,0.25)" : "var(--glass-border)" }}>
              <div className="h-full rounded-full transition-[width]" style={{ width: `${pct ?? 0}%`, background: mine ? "#fff" : "var(--accent)" }} />
            </div>
          )}
          {!voice && !mediaKind && <div className="mt-1.5 text-[0.72rem] font-medium" style={{ color: toneColor }}>{statusText}</div>}
          <div className={`mt-1 flex items-center justify-end gap-1 text-[0.68rem] ${mine ? "text-white/75" : "text-[var(--text-muted)]"}`}>
            {fmtClock(msg.createdAt)}
            {mine && <CheckCheck className={`h-3.5 w-3.5 ${msg.read ? "opacity-100" : "opacity-60"}`} />}
          </div>
        </div>
        {!mine && <ReactionPicker mine={false} onReact={onReact} />}
      </div>
      <ReactionChips reactions={reactions} mine={mine} onReact={onReact} />
    </div>
  );
}

/** Ô chỉnh sửa tin nhắn nội tuyến (thay cho bong bóng khi đang sửa). */
function EditRow({
  value,
  onChange,
  onSave,
  onCancel,
}: {
  value: string;
  onChange: (v: string) => void;
  onSave: () => void;
  onCancel: () => void;
}) {
  return (
    <div className="flex justify-end">
      <div
        className="w-full max-w-[86%] rounded-2xl p-2 sm:max-w-[78%]"
        style={{ background: "var(--glass-bg-strong)", border: "1px solid var(--accent)" }}
      >
        <textarea
          autoFocus
          rows={2}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter" && !e.shiftKey) {
              e.preventDefault();
              onSave();
            }
            if (e.key === "Escape") onCancel();
          }}
          className="w-full resize-none bg-transparent text-sm text-[var(--text)] outline-none"
        />
        <div className="mt-1 flex items-center justify-end gap-2 text-xs">
          <button
            type="button"
            onClick={onCancel}
            className="rounded-lg px-2.5 py-1 font-semibold text-[var(--text-secondary)] transition hover:bg-[var(--accent-soft)]"
          >
            Hủy
          </button>
          <button
            type="button"
            onClick={onSave}
            className="rounded-lg px-2.5 py-1 font-semibold text-white transition hover:opacity-90"
            style={{ background: "var(--accent)" }}
          >
            Lưu
          </button>
        </div>
      </div>
    </div>
  );
}

function CircleIconButton({
  children,
  label,
  onClick,
  active,
  disabled,
}: {
  children: React.ReactNode;
  label: string;
  onClick?: () => void;
  active?: boolean;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-label={label}
      aria-pressed={active}
      className={`grid h-9 w-9 place-items-center rounded-full transition disabled:cursor-not-allowed disabled:opacity-35 ${
        active
          ? "bg-[var(--accent-soft)] text-[var(--accent)]"
          : "text-[var(--text-secondary)] hover:bg-[var(--accent-soft)] hover:text-[var(--accent)]"
      }`}
    >
      {children}
    </button>
  );
}

/** Skeleton 1 dòng hội thoại (avatar tròn + 2 dòng chữ). */
function ConversationSkeletons({ count = 7 }: { count?: number }) {
  return (
    <>
      {Array.from({ length: count }).map((_, i) => (
        <div key={i} className="mb-1 flex w-full items-center gap-3 rounded-2xl p-2.5">
          <div className="gc-skeleton shrink-0" style={{ width: 44, height: 44, borderRadius: 9999 }} />
          <div className="min-w-0 flex-1 space-y-2">
            <div className="flex items-center justify-between gap-2">
              <div className="gc-skeleton h-3.5" style={{ width: "52%" }} />
              <div className="gc-skeleton h-2.5" style={{ width: "16%" }} />
            </div>
            <div className="gc-skeleton h-3" style={{ width: "76%" }} />
          </div>
        </div>
      ))}
    </>
  );
}

/** Skeleton các bong bóng tin nhắn (xen kẽ trái/phải), dồn xuống đáy như khung chat thật. */
function MessageSkeletons() {
  const rows = [
    { mine: false, w: "58%" },
    { mine: true, w: "44%" },
    { mine: true, w: "68%" },
    { mine: false, w: "52%" },
    { mine: false, w: "36%" },
    { mine: true, w: "48%" },
  ];
  return (
    <div className="flex min-h-full flex-col justify-end space-y-3">
      {rows.map((r, i) => (
        <div key={i} className={`flex ${r.mine ? "justify-end" : "justify-start"}`}>
          <div className="gc-skeleton" style={{ width: r.w, height: 42, borderRadius: 16 }} />
        </div>
      ))}
    </div>
  );
}

/** Skeleton 1 dòng danh bạ trong hộp chọn người. */
function ContactSkeletons({ count = 6 }: { count?: number }) {
  return (
    <>
      {Array.from({ length: count }).map((_, i) => (
        <div key={i} className="flex w-full items-center gap-3 rounded-2xl p-2.5">
          <div className="gc-skeleton shrink-0" style={{ width: 40, height: 40, borderRadius: 9999 }} />
          <div className="min-w-0 flex-1 space-y-2">
            <div className="gc-skeleton h-3.5" style={{ width: "45%" }} />
            <div className="gc-skeleton h-3" style={{ width: "65%" }} />
          </div>
        </div>
      ))}
    </>
  );
}

function ConversationContextMenu({
  pos,
  unread,
  pinned,
  onClose,
  onOpen,
  onShowProfile,
  onMarkRead,
  onTogglePin,
  onHide,
  onReport,
  onDelete,
}: {
  pos: PickerPosition;
  unread: number;
  pinned: boolean;
  onClose: () => void;
  onOpen: () => void;
  onShowProfile: () => void;
  onMarkRead: () => void;
  onTogglePin: () => void;
  onHide: () => void;
  onReport: () => void;
  onDelete: () => void;
}) {
  return createPortal(
    <>
      <div className="fixed inset-0 z-[90]" onClick={onClose} />
      <div
        className="fixed z-[91] overflow-hidden rounded-xl border border-[var(--glass-border)] py-1 shadow-lg"
        style={{
          left: pos.left,
          top: pos.top,
          bottom: pos.bottom,
          width: CONVERSATION_MENU_WIDTH,
          background: "var(--glass-bg-strong)",
          backdropFilter: "blur(8px)",
          WebkitBackdropFilter: "blur(8px)",
        }}
      >
        <MenuItem icon={MessageCircle} label="Mở trò chuyện" onClick={() => { onClose(); onOpen(); }} />
        <MenuItem icon={pinned ? PinOff : Pin} label={pinned ? "Bỏ ghim" : "Ghim hội thoại"} onClick={() => { onClose(); onTogglePin(); }} />
        <MenuItem icon={User} label="Xem hồ sơ" onClick={() => { onClose(); onShowProfile(); }} />
        {unread > 0 && (
          <MenuItem icon={CheckCheck} label="Đánh dấu đã đọc" onClick={() => { onClose(); onMarkRead(); }} />
        )}
        <MenuItem icon={EyeOff} label="Ẩn trò chuyện" onClick={() => { onClose(); onHide(); }} />
        <MenuItem icon={Flag} label="Báo xấu" onClick={() => { onClose(); onReport(); }} />
        <MenuItem icon={Trash2} label="Xóa hội thoại" danger onClick={() => { onClose(); onDelete(); }} />
      </div>
    </>,
    document.body,
  );
}

function ConversationRow({
  conversation,
  active,
  onOpen,
  onShowProfile,
  onMarkRead,
  onTogglePin,
  onHide,
  onReport,
  onDelete,
}: {
  conversation: ChatConversation;
  active: boolean;
  onOpen: () => void;
  onShowProfile: () => void;
  onMarkRead: () => void;
  onTogglePin: () => void;
  onHide: () => void;
  onReport: () => void;
  onDelete: () => void;
}) {
  const [menuPos, setMenuPos] = useState<PickerPosition | null>(null);
  const menuBtnRef = useRef<HTMLButtonElement>(null);

  const positionMenu = (left: number, top: number, anchorBottom?: number) => {
    const safeLeft = Math.max(8, Math.min(left, window.innerWidth - CONVERSATION_MENU_WIDTH - 8));
    const menuHeight = conversation.unread > 0 ? 276 : 236;
    const shouldOpenUp = window.innerHeight - top < menuHeight + 16;
    setMenuPos(
      shouldOpenUp
        ? { left: safeLeft, bottom: Math.max(8, window.innerHeight - (anchorBottom ?? top)) }
        : { left: safeLeft, top: Math.max(8, top) },
    );
  };

  const openMenu = () => {
    const r = menuBtnRef.current?.getBoundingClientRect();
    if (!r) return;
    positionMenu(r.right - CONVERSATION_MENU_WIDTH, r.bottom + 6, r.top - 6);
  };

  const handleRowKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (e.key !== "Enter" && e.key !== " ") return;
    e.preventDefault();
    onOpen();
  };

  const handleMenuClick = (e: React.MouseEvent<HTMLButtonElement>) => {
    e.stopPropagation();
    openMenu();
  };

  return (
    <>
      <div
        role="button"
        tabIndex={0}
        onClick={onOpen}
        onKeyDown={handleRowKeyDown}
        className="chat-conversation-row group mb-1 flex w-full cursor-pointer items-center gap-3 rounded-2xl p-2.5 text-left transition"
        style={active ? { background: "var(--accent-soft)" } : undefined}
      >
        <div className="flex min-w-0 flex-1 items-center gap-3">
          <Avatar name={conversation.title} url={conversation.avatarUrl} online={conversation.isOnline} group={conversation.isGroup} />
          <div className="min-w-0 flex-1">
            <div className="flex items-baseline justify-between gap-2">
              <span className="inline-flex min-w-0 items-center gap-1.5">
                {conversation.pinned && <Pin className="h-3.5 w-3.5 shrink-0" style={{ color: "var(--accent)" }} />}
                <NameWithBadge
                  name={conversation.title}
                  verified={conversation.verified}
                  isDiamond={conversation.isDiamond}
                  className="text-sm font-bold"
                />
              </span>
              <span className="shrink-0 text-[0.68rem] font-medium text-[var(--text-muted)]">{fmtTime(conversation.lastAt)}</span>
            </div>
            <div className="flex items-center justify-between gap-2">
              <span className="truncate text-xs text-[var(--text-secondary)]">{conversation.preview || "Bắt đầu trò chuyện..."}</span>

            </div>
          </div>
        </div>
        <div className={`chat-conversation-actions ${conversation.unread ? "has-unread" : ""}`}>
          <button
            ref={menuBtnRef}
            type="button"
            onClick={handleMenuClick}
            className="chat-conversation-menu-button grid h-8 w-8 shrink-0 place-items-center rounded-full text-[var(--text-muted)] opacity-100 transition hover:bg-[var(--accent-soft)] hover:text-[var(--accent)] sm:opacity-0 sm:group-hover:opacity-100"
            aria-label="Tuy chon hoi thoai"
            aria-haspopup="menu"
            aria-expanded={!!menuPos}
          >
            <MoreHorizontal className="h-4 w-4" />
          </button>
          {conversation.unread ? (
            <span className="chat-conversation-unread-badge">
              {conversation.unread}
            </span>
          ) : null}
        </div>
      </div>
      {menuPos && (
        <ConversationContextMenu
          pos={menuPos}
          unread={conversation.unread}
          pinned={!!conversation.pinned}
          onClose={() => setMenuPos(null)}
          onOpen={onOpen}
          onShowProfile={onShowProfile}
          onMarkRead={onMarkRead}
          onTogglePin={onTogglePin}
          onHide={onHide}
          onReport={onReport}
          onDelete={onDelete}
        />
      )}
    </>
  );
}

/** Lớp phủ gợi ý khi đang kéo tệp vào một vùng thả (gửi nhanh / xem trước). */
function DropOverlay({ icon, title, sub }: { icon: React.ReactNode; title: string; sub?: string }) {
  return (
    <div
      className="pointer-events-none absolute inset-2 z-20 grid place-items-center rounded-2xl border-2 border-dashed"
      style={{ borderColor: "var(--accent)", background: "var(--accent-soft)" }}
    >
      <div className="flex flex-col items-center gap-1 text-center text-[var(--accent)]">
        {icon}
        <div className="text-sm font-bold">{title}</div>
        {sub && <div className="text-xs opacity-80">{sub}</div>}
      </div>
    </div>
  );
}

function IncomingCallOverlay({ call, onAccept }: { call: WebCallSession; onAccept: () => void }) {
  return createPortal(
    <div className="fixed inset-0 z-[120] grid place-items-center bg-slate-950/70 p-4 backdrop-blur-md" role="dialog" aria-modal="true" aria-label="Cuộc gọi đến">
      <div className="w-[min(390px,94vw)] rounded-[28px] border border-white/15 bg-slate-950 p-6 text-center text-white shadow-2xl">
        <div className="mx-auto grid h-24 w-24 place-items-center rounded-full bg-gradient-to-br from-[var(--accent)] to-violet-500 text-3xl font-black shadow-xl">
          {initials(call.peerName)}
        </div>
        <h2 className="mt-4 max-w-full truncate text-xl font-black">{call.peerName}</h2>
        <div className="mt-1 text-sm font-semibold text-white/70">
          Cuộc gọi {call.media === "video" ? "video" : "thoại"} đến
        </div>
        <div className="mt-6 flex items-center justify-center gap-6">
          <button
            type="button"
            onClick={() => hangupWebCall("declined")}
            className="grid h-14 w-14 place-items-center rounded-full bg-red-500 text-white shadow-lg transition hover:bg-red-600"
            aria-label="Từ chối cuộc gọi"
          >
            <PhoneOff className="h-6 w-6" />
          </button>
          <button
            type="button"
            onClick={onAccept}
            className="grid h-14 w-14 place-items-center rounded-full bg-emerald-500 text-white shadow-lg transition hover:bg-emerald-600"
            aria-label="Nghe máy trong tab mới"
          >
            <Phone className="h-6 w-6" />
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}

export function Chats() {
  const navigate = useNavigate();
  const { user } = useAuth();
  const { notify, confirm } = useAppNotifications();
  const call = useWebCall();
  const [searchParams, setSearchParams] = useSearchParams();
  const [filter, setFilter] = useState<Filter>("all");
  const [query, setQuery] = useState("");
  const [draft, setDraft] = useState("");
  const [sending, setSending] = useState(false);
  const [profileOpen, setProfileOpen] = useState(false);
  const [newChatOpen, setNewChatOpen] = useState(false);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [activeFallback, setActiveFallback] = useState<ChatConversation | null>(null);
  const [composerFocused, setComposerFocused] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [editDraft, setEditDraft] = useState("");
  const [forwardMsg, setForwardMsg] = useState<ChatMessage | null>(null);
  const [messageSearchOpen, setMessageSearchOpen] = useState(false);
  const [messageQuery, setMessageQuery] = useState("");
  const [debouncedMessageQuery, setDebouncedMessageQuery] = useState("");
  const [olderMessages, setOlderMessages] = useState<ChatMessage[]>([]);
  const [loadingOlder, setLoadingOlder] = useState(false);
  const [paginationExhausted, setPaginationExhausted] = useState(false);
  const [composerEmojiOpen, setComposerEmojiOpen] = useState(false);
  const [sendAsSupport, setSendAsSupport] = useState(false);
  const [isMobile, setIsMobile] = useState(() =>
    typeof window !== "undefined" ? window.matchMedia("(max-width: 767px)").matches : false,
  );
  const scrollRef = useRef<HTMLDivElement>(null);
  const draftInputRef = useRef<HTMLInputElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const touchStartRef = useRef<{ x: number; y: number } | null>(null);
  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const voiceStreamRef = useRef<MediaStream | null>(null);
  const voiceChunksRef = useRef<Blob[]>([]);
  const voiceStartedAtRef = useRef(0);
  const voiceSendOnStopRef = useRef(false);
  const voiceTimerRef = useRef<number | null>(null);
  const viewedMessageSetRef = useRef("");
  const newestMessageIdRef = useRef<number | null>(null);
  const transfers = useTransfers();
  const [serverDownloading, setServerDownloading] = useState<number | null>(null);
  const [dragZone, setDragZone] = useState<null | "thread" | "composer">(null);
  const [previewFile, setPreviewFile] = useState<File | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const previewMediaKind = previewFile ? detectInlineMedia(previewFile.name, previewFile.type) : null;
  const [recordingVoice, setRecordingVoice] = useState(false);
  const [voiceElapsedMs, setVoiceElapsedMs] = useState(0);
  const [voiceSending, setVoiceSending] = useState(false);
  const [pendingVoice, setPendingVoice] = useState<{
    blob: Blob;
    name: string;
    mime: string;
    clientMessageId: string;
  } | null>(null);

  const { data: convData, loading, reload: reloadConversations } = useApi<ChatConversation[]>("/api/chat/conversations");
  const allConversations = useMemo(() => convData ?? [], [convData]);
  const requestedConversationId = searchParams.get("conversation");

  const messagePath = activeId
    ? `/api/chat/conversations/${activeId}/messages?take=50${
        debouncedMessageQuery ? `&search=${encodeURIComponent(debouncedMessageQuery)}` : ""
      }`
    : null;
  const { data: msgData, loading: msgLoading, error: msgError, reload: reloadMessages } = useApi<ChatMessage[]>(
    messagePath,
    [activeId, debouncedMessageQuery],
  );
  const messages = useMemo(() => {
    const merged = new Map<number, ChatMessage>();
    for (const message of olderMessages) merged.set(message.id, message);
    for (const message of msgData ?? []) merged.set(message.id, message);
    return [...merged.values()].sort((a, b) => a.id - b.id);
  }, [msgData, olderMessages]);
  const canLoadOlder = !paginationExhausted && (msgData?.length ?? 0) >= 50 && messages.length > 0;

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setOlderMessages([]);
      setPaginationExhausted(false);
      setLoadingOlder(false);
      setDebouncedMessageQuery(messageQuery.trim());
    }, 350);
    return () => window.clearTimeout(timer);
  }, [messageQuery]);

  useEffect(() => {
    const mq = window.matchMedia("(max-width: 767px)");
    const update = () => setIsMobile(mq.matches);
    update();
    mq.addEventListener("change", update);
    return () => mq.removeEventListener("change", update);
  }, []);

  useEffect(() => {
    if (!isMobile || !composerFocused) return;
    const viewport = window.visualViewport;
    const update = () => {
      const height = viewport?.height ?? window.innerHeight;
      document.documentElement.style.setProperty("--chat-visual-height", `${Math.floor(height)}px`);
    };
    update();
    viewport?.addEventListener("resize", update);
    viewport?.addEventListener("scroll", update);
    window.addEventListener("resize", update);
    return () => {
      viewport?.removeEventListener("resize", update);
      viewport?.removeEventListener("scroll", update);
      window.removeEventListener("resize", update);
      document.documentElement.style.removeProperty("--chat-visual-height");
    };
  }, [composerFocused, isMobile]);

  const admin = user?.role?.toLowerCase() === "admin";
  const chatConversations = useMemo(
    () => allConversations.filter((c) => !(admin && c.supportConversation)),
    [admin, allConversations],
  );

  // Tự chọn hội thoại đầu tiên khi vào trang (nếu chưa chọn).
  useEffect(() => {
    if (isMobile || activeId || chatConversations.length === 0) return;
    const timer = window.setTimeout(() => setActiveId(chatConversations[0].id), 0);
    return () => window.clearTimeout(timer);
  }, [activeId, chatConversations, isMobile]);

  useEffect(() => {
    if (!requestedConversationId) return;
    const found = allConversations.find((c) => c.id === requestedConversationId);
    if (!found) return;
    const timer = window.setTimeout(() => {
      setActiveId(found.id);
      setActiveFallback(found);
      setProfileOpen(false);
      setSearchParams({}, { replace: true });
    }, 0);
    return () => window.clearTimeout(timer);
  }, [allConversations, requestedConversationId, setSearchParams]);

  const conversations = useMemo(() => {
    const q = query.trim().toLowerCase();
    return chatConversations.filter((c) => {
      if (filter === "unread" && !c.unread) return false;
      if (filter === "groups" && !c.isGroup) return false;
      if (q && !c.title.toLowerCase().includes(q) && !c.preview.toLowerCase().includes(q)) return false;
      return true;
    });
  }, [chatConversations, filter, query]);

  const active =
    allConversations.find((c) => c.id === activeId) ??
    (activeFallback?.id === activeId ? activeFallback : null);
  const canUseSupportSender = !!admin && !!active && !active.isGroup && !!active.username && active.username !== SUPPORT_USERNAME;
  const outgoingAsSupport = canUseSupportSender && sendAsSupport;
  const isSupportPeer = active?.username === SUPPORT_USERNAME;
  const canSendLanFiles = !!user?.isDiamond && !isSupportPeer && !outgoingAsSupport;

  const scrollToBottom = (smooth = true) =>
    requestAnimationFrame(() =>
      scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: smooth ? "smooth" : "auto" }),
    );

  // Đổi hội thoại/tìm kiếm thì về kết quả mới nhất. Khi đang đọc tin cũ, tin realtime không được
  // giật người dùng xuống đáy; chỉ tự cuộn nếu họ vốn đang ở gần đáy.
  useEffect(() => {
    if (msgLoading) return;
    const viewKey = `${activeId ?? ""}:${debouncedMessageQuery}`;
    const newest = messages.at(-1)?.id ?? null;
    if (viewedMessageSetRef.current !== viewKey) {
      viewedMessageSetRef.current = viewKey;
      newestMessageIdRef.current = newest;
      scrollToBottom(false);
      return;
    }
    if (newest != null && newest !== newestMessageIdRef.current) {
      const scroller = scrollRef.current;
      const nearBottom = !scroller || scroller.scrollHeight - scroller.scrollTop - scroller.clientHeight < 180;
      newestMessageIdRef.current = newest;
      if (nearBottom) scrollToBottom();
    }
  }, [activeId, debouncedMessageQuery, messages, msgLoading]);

  // Hủy chỉnh sửa + xóa xem trước/kéo-thả khi chuyển sang hội thoại khác.
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setEditingId(null);
      setEditDraft("");
      setPreviewFile(null);
      setDragZone(null);
      setComposerEmojiOpen(false);
      setSendAsSupport(!!active?.supportConversation && active.username !== SUPPORT_USERNAME);
    }, 0);
    return () => window.clearTimeout(timer);
  }, [activeId, active?.supportConversation, active?.username]);

  useEffect(() => {
    voiceSendOnStopRef.current = false;
    const recorder = mediaRecorderRef.current;
    if (recorder && recorder.state !== "inactive") recorder.stop();
    voiceStreamRef.current?.getTracks().forEach((track) => track.stop());
    voiceStreamRef.current = null;
    mediaRecorderRef.current = null;
    if (voiceTimerRef.current != null) window.clearInterval(voiceTimerRef.current);
    voiceTimerRef.current = null;
    const resetTimer = window.setTimeout(() => {
      setMessageSearchOpen(false);
      setMessageQuery("");
      setDebouncedMessageQuery("");
      setOlderMessages([]);
      setPaginationExhausted(false);
      setLoadingOlder(false);
      setPendingVoice(null);
      setRecordingVoice(false);
      setVoiceElapsedMs(0);
    }, 0);
    return () => window.clearTimeout(resetTimer);
  }, [activeId]);

  useEffect(() => () => {
    voiceSendOnStopRef.current = false;
    const recorder = mediaRecorderRef.current;
    if (recorder && recorder.state !== "inactive") recorder.stop();
    voiceStreamRef.current?.getTracks().forEach((track) => track.stop());
    if (voiceTimerRef.current != null) window.clearInterval(voiceTimerRef.current);
  }, []);

  // Tạo URL xem trước cho ảnh/video; thu hồi object URL khi đổi/đóng.
  useEffect(() => {
    if (previewFile && detectInlineMedia(previewFile.name, previewFile.type)) {
      const url = URL.createObjectURL(previewFile);
      const timer = window.setTimeout(() => setPreviewUrl(url), 0);
      return () => {
        window.clearTimeout(timer);
        URL.revokeObjectURL(url);
      };
    }
    const timer = window.setTimeout(() => setPreviewUrl(null), 0);
    return () => window.clearTimeout(timer);
  }, [previewFile]);

  // Nhịp mỗi phút để cập nhật trạng thái "Hoạt động X phút trước" mà không cần tải lại.
  const [, setTick] = useState(0);
  useEffect(() => {
    const t = window.setInterval(() => setTick((n) => n + 1), 60_000);
    return () => window.clearInterval(t);
  }, []);

  const openConversation = (c: ChatConversation) => {
    setActiveId(c.id);
    setActiveFallback(c);
    setProfileOpen(false);
    if (c.unread) reloadConversations({ silent: true });
  };

  const notifyError = (e: unknown, fallback: string) => {
    notify.error(e instanceof Error ? e.message : fallback);
  };

  const openCallTab = (params: Record<string, string>) => {
    const url = new URL("/call", window.location.origin);
    for (const [key, value] of Object.entries(params)) url.searchParams.set(key, value);
    // This must stay synchronous inside the click handler so browsers do not
    // classify the new call tab as an unsolicited popup.
    const callTab = window.open(url.toString(), "ketoanmini-call");
    if (!callTab) {
      notify.warning("Trình duyệt đang chặn tab cuộc gọi. Hãy cho phép cửa sổ bật lên cho trang này.");
      return null;
    }
    callTab.focus();
    return callTab;
  };

  const beginCall = (media: "audio" | "video") => {
    if (!active?.username || active.isGroup || active.username === SUPPORT_USERNAME) {
      notify.warning("Cuộc gọi chỉ hỗ trợ hội thoại 1-1 với tài khoản nhân viên.");
      return;
    }
    openCallTab({ peer: active.username, name: active.title, media });
  };

  const acceptIncomingCallInTab = () => {
    if (!call || call.stage !== "incoming") return;
    const callTab = openCallTab({
      incoming: "1",
      callId: call.callId,
      peer: call.peerUsername,
      name: call.peerName,
      media: call.media,
    });
    if (!callTab) return;
    handoffIncomingWebCall(call.callId);
  };

  const loadOlderMessages = async () => {
    const beforeId = messages[0]?.id;
    if (!activeId || beforeId == null || loadingOlder || !canLoadOlder) return;
    const scroller = scrollRef.current;
    const previousHeight = scroller?.scrollHeight ?? 0;
    setLoadingOlder(true);
    try {
      const page = await api.get<ChatMessage[]>(
        `/api/chat/conversations/${activeId}/messages?take=50&beforeId=${beforeId}${
          debouncedMessageQuery ? `&search=${encodeURIComponent(debouncedMessageQuery)}` : ""
        }`,
      );
      setOlderMessages((current) => {
        const merged = new Map<number, ChatMessage>();
        for (const message of page) merged.set(message.id, message);
        for (const message of current) merged.set(message.id, message);
        return [...merged.values()].sort((a, b) => a.id - b.id);
      });
      if (page.length < 50) setPaginationExhausted(true);
      requestAnimationFrame(() => {
        if (scroller) scroller.scrollTop += scroller.scrollHeight - previousHeight;
      });
    } catch (e) {
      notifyError(e, "Không tải được tin nhắn cũ");
    } finally {
      setLoadingOlder(false);
    }
  };

  const showConversationProfile = (c: ChatConversation) => {
    setActiveId(c.id);
    setActiveFallback(c);
    setProfileOpen(true);
    if (c.unread) reloadConversations({ silent: true });
  };

  const markConversationRead = async (c: ChatConversation) => {
    try {
      await api.post(`/api/chat/conversations/${c.id}/read`);
      reloadConversations({ silent: true });
    } catch (e) {
      notifyError(e, "Không đánh dấu đã đọc được cuộc trò chuyện");
    }
  };

  const toggleConversationPin = async (c: ChatConversation) => {
    try {
      await api.post(`/api/chat/conversations/${c.id}/pin`, { pinned: !c.pinned });
      reloadConversations({ silent: true });
    } catch (e) {
      notifyError(e, "Không ghim được cuộc trò chuyện");
    }
  };

  const removeConversationFromList = (id: string) => {
    if (activeId === id) {
      setActiveId(null);
      setActiveFallback(null);
      setProfileOpen(false);
    }
    reloadConversations({ silent: true });
  };

  const hideConversation = async (c: ChatConversation) => {
    try {
      await api.post(`/api/chat/conversations/${c.id}/hide`);
      removeConversationFromList(c.id);
    } catch (e) {
      notifyError(e, "Không ẩn được cuộc trò chuyện");
    }
  };

  const deleteConversation = async (c: ChatConversation) => {
    const ok = await confirm({
      title: "Xóa hội thoại?",
      description: "Xóa hội thoại này khỏi danh sách của bạn? Tin nhắn phía người còn lại không bị xóa.",
      confirmLabel: "Xóa",
      tone: "danger",
    });
    if (!ok) return;
    try {
      await api.del(`/api/chat/conversations/${c.id}`);
      removeConversationFromList(c.id);
    } catch (e) {
      notifyError(e, "Không xóa được cuộc trò chuyện");
    }
  };

  const reportConversation = async (c: ChatConversation) => {
    const ok = await confirm({
      title: "Báo xấu cuộc trò chuyện?",
      description: "Bạn muốn gửi báo xấu cuộc trò chuyện này để quản trị viên xem xét?",
      confirmLabel: "Báo xấu",
      tone: "warning",
    });
    if (!ok) return;
    try {
      await api.post(`/api/chat/conversations/${c.id}/report`, { reason: "Người dùng báo xấu từ menu hội thoại." });
      notify.success("Đã gửi báo xấu sang mục Phản hồi của admin.");
    } catch (e) {
      notifyError(e, "Không báo xấu được cuộc trò chuyện");
    }
  };

  const backToList = () => {
    setProfileOpen(false);
    setActiveId(null);
  };

  const handleTouchStart = (e: React.TouchEvent<HTMLDivElement>) => {
    if (!isMobile || (!activeId && !profileOpen)) return;
    const t = e.touches[0];
    if (!t) return;
    if (!profileOpen && t.clientX > 56) {
      touchStartRef.current = null;
      return;
    }
    touchStartRef.current = { x: t.clientX, y: t.clientY };
  };

  const handleTouchEnd = (e: React.TouchEvent<HTMLDivElement>) => {
    const start = touchStartRef.current;
    touchStartRef.current = null;
    if (!start || !isMobile) return;
    const t = e.changedTouches[0];
    if (!t) return;
    const dx = t.clientX - start.x;
    const dy = Math.abs(t.clientY - start.y);
    if (dx < 70 || dy > 48) return;
    if (profileOpen) {
      setProfileOpen(false);
    } else if (activeId) {
      backToList();
    }
  };

  const send = async () => {
    const text = draft.trim();
    if (!text || !activeId || sending) return;
    const shouldKeepKeyboard = isMobile;
    if (shouldKeepKeyboard) {
      draftInputRef.current?.focus({ preventScroll: true });
    }
    setSending(true);
    setDraft("");
    try {
      let targetConversationId = activeId;
      if (outgoingAsSupport && active && !active.supportConversation && active.username && active.username !== SUPPORT_USERNAME) {
        const { id } = await api.post<{ id: string }>(`/api/chat/support/${encodeURIComponent(active.username)}`);
        targetConversationId = id;
        setActiveId(id);
        setActiveFallback({
          id,
          isGroup: false,
          title: active.title,
          username: active.username,
          avatarUrl: active.avatarUrl,
          isOnline: active.isOnline,
          verified: active.verified,
          isDiamond: active.isDiamond,
          preview: "",
          lastAt: null,
          unread: 0,
          supportConversation: true,
        });
      }
      await api.post(`/api/chat/conversations/${targetConversationId}/messages`, { body: text, sendAsSupport: outgoingAsSupport });
      if (targetConversationId === activeId) reloadMessages({ silent: true });
      reloadConversations({ silent: true });
      scrollToBottom();
    } catch (e) {
      setDraft(text);
      notifyError(e, "Không gửi được tin nhắn");
    } finally {
      setSending(false);
      if (shouldKeepKeyboard) {
        requestAnimationFrame(() => draftInputRef.current?.focus({ preventScroll: true }));
      }
    }
  };

  // Ảnh/video được lưu bền vững trên server để cả web và APK xem trực tiếp sau khi tải lại trang.
  // Các loại tệp khác vẫn ưu tiên truyền qua LAN (P2P) và chỉ fallback server khi cần.
  const MAX_FILE_BYTES = 2 * 1024 * 1024 * 1024; // 2GB: chặn nhầm; truyền P2P nên không giới hạn server
  const MAX_INLINE_MEDIA_BYTES = 100 * 1024 * 1024;
  const handleSendFile = async (file: File) => {
    if (!activeId || !active) return;
    if (active.isGroup || !active.username) {
      notify.warning("Hiện chỉ gửi tệp qua LAN trong cuộc trò chuyện 1-1.");
      return;
    }
    if (!canSendLanFiles) {
      notify.warning("Chỉ hội viên kim cương mới được gửi tệp qua LAN.");
      return;
    }
    if (file.size > MAX_FILE_BYTES) {
      notify.warning("Tệp quá lớn (giới hạn 2GB).");
      return;
    }
    const mediaKind = detectInlineMedia(file.name, file.type);
    if (mediaKind && file.size > MAX_INLINE_MEDIA_BYTES) {
      notify.warning(`${mediaKind === "image" ? "Ảnh" : "Video"} quá lớn (giới hạn xem trực tiếp 100MB).`);
      return;
    }
    const peer = active.username;
    try {
      const msg = await api.post<ChatMessage>(`/api/chat/conversations/${activeId}/messages/file`, {
        fileName: file.name,
        fileSize: file.size,
        fileMime: file.type || null,
        kind: "file",
      });
      if (mediaKind) {
        await api.postBlob(`/api/chat/conversations/${activeId}/messages/${msg.id}/upload`, file);
      } else {
        startSend(peer, file, msg.id, activeId, !!active.isOnline);
      }
      reloadMessages({ silent: true });
      reloadConversations({ silent: true });
      scrollToBottom();
    } catch (e) {
      notifyError(e, "Không gửi được tệp");
    }
  };

  const uploadVoice = async (voice: NonNullable<typeof pendingVoice>) => {
    if (!activeId || voiceSending) return;
    setVoiceSending(true);
    setPendingVoice(voice);
    try {
      const metadata = await api.post<ChatMessage>(`/api/chat/conversations/${activeId}/messages/file`, {
        fileName: voice.name,
        fileSize: voice.blob.size,
        fileMime: voice.mime,
        kind: "voice",
        clientMessageId: voice.clientMessageId,
      });
      // Retry có thể gặp bản ghi đã upload thành công trước đó; không tải blob lần hai.
      if (!metadata.hasBlob) {
        await api.postBlob(`/api/chat/conversations/${activeId}/messages/${metadata.id}/upload`, voice.blob);
      }
      setPendingVoice(null);
      reloadMessages({ silent: true });
      reloadConversations({ silent: true });
      scrollToBottom();
    } catch (e) {
      setPendingVoice(voice);
      notifyError(e, "Không gửi được tin nhắn thoại");
    } finally {
      setVoiceSending(false);
    }
  };

  const startVoiceRecording = async () => {
    if (!activeId || recordingVoice || voiceSending) return;
    if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === "undefined") {
      notify.warning("Trình duyệt này không hỗ trợ ghi âm. Hãy dùng Chrome/Edge mới trên HTTPS.");
      return;
    }
    setComposerEmojiOpen(false);
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const supportedMime = [
        "audio/webm;codecs=opus",
        "audio/ogg;codecs=opus",
        "audio/mp4",
        "audio/webm",
      ].find((mime) => MediaRecorder.isTypeSupported(mime));
      const recorder = supportedMime ? new MediaRecorder(stream, { mimeType: supportedMime }) : new MediaRecorder(stream);
      const mime = (recorder.mimeType || supportedMime || "audio/webm").split(";")[0];
      const extension = mime.includes("ogg") ? "ogg" : mime.includes("mp4") ? "m4a" : "webm";
      voiceStreamRef.current = stream;
      mediaRecorderRef.current = recorder;
      voiceChunksRef.current = [];
      voiceStartedAtRef.current = Date.now();
      voiceSendOnStopRef.current = false;
      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) voiceChunksRef.current.push(event.data);
      };
      recorder.onstop = () => {
        const shouldSend = voiceSendOnStopRef.current;
        const elapsed = Date.now() - voiceStartedAtRef.current;
        const blob = new Blob(voiceChunksRef.current, { type: mime });
        stream.getTracks().forEach((track) => track.stop());
        voiceStreamRef.current = null;
        mediaRecorderRef.current = null;
        voiceChunksRef.current = [];
        if (voiceTimerRef.current != null) window.clearInterval(voiceTimerRef.current);
        voiceTimerRef.current = null;
        setRecordingVoice(false);
        setVoiceElapsedMs(0);
        if (!shouldSend) return;
        if (elapsed < 700 || blob.size === 0) {
          notify.warning("Giữ lâu hơn một chút để ghi âm.");
          return;
        }
        const voice = {
          blob,
          mime,
          name: `ghi-am-${Date.now()}.${extension}`,
          clientMessageId: `web-voice:${crypto.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`}`,
        };
        setPendingVoice(voice);
        void uploadVoice(voice);
      };
      recorder.onerror = () => notify.error("Micro gặp lỗi trong lúc ghi âm.");
      recorder.start(250);
      setRecordingVoice(true);
      setVoiceElapsedMs(0);
      voiceTimerRef.current = window.setInterval(() => setVoiceElapsedMs(Date.now() - voiceStartedAtRef.current), 200);
    } catch (e) {
      voiceStreamRef.current?.getTracks().forEach((track) => track.stop());
      voiceStreamRef.current = null;
      mediaRecorderRef.current = null;
      notifyError(e, "Không mở được micro. Hãy cấp quyền ghi âm cho trang web.");
    }
  };

  const finishVoiceRecording = (sendRecording: boolean) => {
    const recorder = mediaRecorderRef.current;
    if (!recorder || recorder.state === "inactive") return;
    voiceSendOnStopRef.current = sendRecording;
    recorder.stop();
  };

  const loadVoiceBlob = (message: ChatMessage) => {
    if (!activeId) return Promise.reject(new Error("Hội thoại không còn được mở."));
    return api.getBlob(`/api/chat/conversations/${activeId}/messages/${message.id}/download`);
  };

  const onFileInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = ""; // cho phép chọn lại cùng tệp lần sau
    if (file) void handleSendFile(file);
  };

  // Tải tệp người gửi đã LƯU TẠM trên server (khi mình offline lúc họ gửi). Tải xong server tự xóa.
  const serverDownload = async (m: ChatMessage) => {
    if (!activeId) return;
    setServerDownloading(m.id);
    try {
      const blob = await api.getBlob(`/api/chat/conversations/${activeId}/messages/${m.id}/download`);
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = m.fileName || `tep-${m.id}`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      window.setTimeout(() => URL.revokeObjectURL(url), 10_000);
      reloadMessages({ silent: true });
      reloadConversations({ silent: true });
    } catch (e) {
      notifyError(e, "Không tải được tệp (có thể đã hết hạn hoặc đã tải).");
    } finally {
      setServerDownloading(null);
    }
  };

  // ----- Kéo-thả tệp: thả vào VÙNG TIN NHẮN → gửi nhanh; thả vào KHUNG NHẬP → xem trước rồi gửi -----
  const dragHasFiles = (e: React.DragEvent) => Array.from(e.dataTransfer?.types ?? []).includes("Files");
  const canDropFile = !!active && !active.isGroup && !!active.username && canSendLanFiles;

  const onZoneOver = (zone: "thread" | "composer") => (e: React.DragEvent) => {
    if (!dragHasFiles(e) || !canDropFile) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = "copy";
    setDragZone(zone);
  };
  const onZoneLeave = (zone: "thread" | "composer") => (e: React.DragEvent) => {
    // Chỉ tắt khi con trỏ rời hẳn vùng (không phải khi đi qua phần tử con).
    if (!e.currentTarget.contains(e.relatedTarget as Node | null)) {
      setDragZone((z) => (z === zone ? null : z));
    }
  };
  const onThreadDrop = (e: React.DragEvent) => {
    if (!dragHasFiles(e)) return;
    e.preventDefault();
    setDragZone(null);
    const f = e.dataTransfer.files?.[0];
    if (f) void handleSendFile(f); // gửi nhanh
  };
  const onComposerDrop = (e: React.DragEvent) => {
    if (!dragHasFiles(e)) return;
    e.preventDefault();
    setDragZone(null);
    const f = e.dataTransfer.files?.[0];
    if (f) setPreviewFile(f); // xem trước
  };
  const sendPreview = async () => {
    if (!previewFile) return;
    const f = previewFile;
    setPreviewFile(null);
    await handleSendFile(f);
  };

  const startChat = async (c: ChatContact) => {
    try {
      const { id } = await api.post<{ id: string }>(`/api/chat/direct/${encodeURIComponent(c.username)}`);
      setNewChatOpen(false);
      setActiveId(id);
      setActiveFallback({
        id,
        isGroup: false,
        title: c.displayName,
        username: c.username,
        avatarUrl: c.avatarUrl,
        isOnline: c.isOnline,
        verified: c.verified,
        isDiamond: c.isDiamond,
        preview: "",
        lastAt: null,
        unread: 0,
        supportConversation: c.username === SUPPORT_USERNAME,
      });
      reloadConversations({ silent: true });
    } catch (e) {
      notifyError(e, "Không mở được cuộc trò chuyện");
    }
  };

  const startEdit = (m: ChatMessage) => {
    setEditingId(m.id);
    setEditDraft(m.body);
  };
  const cancelEdit = () => {
    setEditingId(null);
    setEditDraft("");
  };
  const saveEdit = async () => {
    const text = editDraft.trim();
    if (editingId == null || !activeId) return cancelEdit();
    if (!text) return cancelEdit();
    try {
      await api.put(`/api/chat/conversations/${activeId}/messages/${editingId}`, { body: text });
      cancelEdit();
      reloadMessages({ silent: true });
      reloadConversations({ silent: true });
    } catch (e) {
      notifyError(e, "Không sửa được tin nhắn");
    }
  };

  const removeMsg = async (m: ChatMessage) => {
    if (!activeId) return;
    const ok = await confirm({
      title: "Gỡ tin nhắn?",
      description: "Mọi người trong cuộc trò chuyện sẽ không xem được tin nhắn này nữa.",
      confirmLabel: "Gỡ",
      tone: "danger",
    });
    if (!ok) return;
    try {
      await api.del(`/api/chat/conversations/${activeId}/messages/${m.id}`);
      reloadMessages({ silent: true });
      reloadConversations({ silent: true });
    } catch (e) {
      notifyError(e, "Không gỡ được tin nhắn");
    }
  };

  // Thả / đổi / bỏ biểu cảm cho tin nhắn. Backend tự toggle (bấm lại đúng biểu cảm → bỏ).
  const toggleReaction = async (m: ChatMessage, emoji: string) => {
    if (!activeId) return;
    try {
      await api.post(`/api/chat/conversations/${activeId}/messages/${m.id}/react`, { emoji });
      reloadMessages({ silent: true });
    } catch (e) {
      notifyError(e, "Không thả được biểu cảm");
    }
  };

  const doForward = async (c: ChatContact) => {
    const m = forwardMsg;
    if (!m) return;
    try {
      const { id } = await api.post<{ id: string }>(`/api/chat/direct/${encodeURIComponent(c.username)}`);
      await api.post(`/api/chat/conversations/${id}/messages`, { body: m.body, forwarded: true });
      setForwardMsg(null);
      setActiveId(id);
      setActiveFallback({
        id,
        isGroup: false,
        title: c.displayName,
        username: c.username,
        avatarUrl: c.avatarUrl,
        isOnline: c.isOnline,
        verified: c.verified,
        isDiamond: c.isDiamond,
        preview: "",
        lastAt: null,
        unread: 0,
        supportConversation: c.username === SUPPORT_USERNAME,
      });
      reloadConversations({ silent: true });
    } catch (e) {
      notifyError(e, "Không chuyển tiếp được tin nhắn");
    }
  };

  return (
    <div
      className={`gc-root chat-mobile-shell flex h-full min-h-0 gap-3 ${activeId ? "is-chat-open" : ""} ${
        composerFocused ? "is-composer-focused" : ""
      }`}
      onTouchStart={handleTouchStart}
      onTouchEnd={handleTouchEnd}
      // Chặn trình duyệt mở tệp khi vô tình thả ra ngoài vùng nhận (header, danh sách…).
      onDragOver={(e) => { if (dragHasFiles(e)) e.preventDefault(); }}
      onDrop={(e) => { if (dragHasFiles(e)) e.preventDefault(); }}
    >
      {/* ---------- Cột danh sách hội thoại ---------- */}
      <GlassPanel strong className="chat-conversation-list flex w-[320px] shrink-0 flex-col overflow-hidden p-3">
        <div className="mb-3 flex items-center justify-between">
          <div className="flex min-w-0 items-center gap-2">
            <button
              type="button"
              onClick={() => navigate("/dashboard")}
              className="chat-home-button grid h-9 w-9 shrink-0 place-items-center rounded-xl text-[var(--text-secondary)] transition hover:bg-[var(--accent-soft)] hover:text-[var(--accent)]"
              aria-label="Ve trang tong quan"
            >
              <Home className="h-[18px] w-[18px]" />
            </button>
            <h1 className="truncate text-xl font-bold text-[var(--text)]">Trò chuyện</h1>
          </div>
          <button
            type="button"
            onClick={() => setNewChatOpen(true)}
            className="grid h-9 w-9 place-items-center rounded-xl text-white transition hover:opacity-90"
            style={{ background: "var(--accent)" }}
            aria-label="Tin nhắn mới"
          >
            <SquarePen className="h-[18px] w-[18px]" />
          </button>
        </div>

        <div
          className="mb-3 flex items-center gap-2 rounded-xl px-3 py-2"
          style={{ background: "var(--glass-bg-strong)", border: "1px solid var(--glass-border)" }}
        >
          <Search className="h-4 w-4 shrink-0 text-[var(--text-muted)]" />
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Tìm tin nhắn hoặc người dùng…"
            className="w-full bg-transparent text-sm font-medium text-[var(--text)] outline-none"
          />
        </div>

        <div className="mb-2 flex gap-1.5">
          {FILTERS.map((f) => {
            const on = filter === f.key;
            return (
              <button
                key={f.key}
                type="button"
                onClick={() => setFilter(f.key)}
                className="rounded-full px-3 py-1.5 text-xs font-semibold transition"
                style={
                  on
                    ? { background: "var(--accent)", color: "#fff" }
                    : { background: "var(--glass-bg-strong)", color: "var(--text-secondary)", border: "1px solid var(--glass-border)" }
                }
              >
                {f.label}
              </button>
            );
          })}
        </div>

        <div className="scroll-thin -mx-1 flex-1 overflow-y-auto px-1 pt-[5px]">
          {conversations.map((c) => {
            const on = c.id === activeId;
            return (
              <ConversationRow
                key={c.id}
                conversation={c}
                active={on}
                onOpen={() => openConversation(c)}
                onShowProfile={() => showConversationProfile(c)}
                onMarkRead={() => markConversationRead(c)}
                onTogglePin={() => toggleConversationPin(c)}
                onHide={() => hideConversation(c)}
                onReport={() => reportConversation(c)}
                onDelete={() => deleteConversation(c)}
              />
            );
          })}
          {loading && conversations.length === 0 && <ConversationSkeletons />}
          {!loading && conversations.length === 0 && (
            <div className="px-2 py-10 text-center text-sm text-[var(--text-muted)]">
              {chatConversations.length === 0 ? "Chưa có cuộc trò chuyện nào. Bấm + để bắt đầu." : "Không có hội thoại phù hợp."}
            </div>
          )}
        </div>
      </GlassPanel>

      {/* ---------- Cột hội thoại ---------- */}
      <GlassPanel strong className="chat-thread-panel flex min-w-0 flex-1 flex-col overflow-hidden">
        {active ? (
          <>
            <header className="flex items-center gap-3 border-b border-[var(--glass-border)] p-3">
              <button
                type="button"
                onClick={backToList}
                aria-label="Quay lai danh sach tro chuyen"
                className="chat-mobile-back grid h-9 w-9 shrink-0 place-items-center rounded-full text-[var(--text-secondary)] transition hover:bg-[var(--accent-soft)] hover:text-[var(--accent)]"
              >
                <ArrowLeft className="h-[18px] w-[18px]" />
              </button>
              <button
                type="button"
                onClick={() => setProfileOpen((o) => !o)}
                aria-expanded={profileOpen}
                className="chat-id-trigger -m-1 flex min-w-0 items-center gap-3 rounded-xl p-1 text-left transition"
                title="Xem hồ sơ"
              >
                <Avatar name={active.title} url={active.avatarUrl} size={42} online={active.isOnline} group={active.isGroup} />
                <div className="min-w-0 max-w-[220px]">
                  <NameWithBadge
                    name={active.title}
                    verified={active.verified}
                    isDiamond={active.isDiamond}
                    className="text-sm font-bold text-[var(--text)]"
                  />
                  <div className="flex items-center gap-1.5 text-xs text-[var(--text-secondary)]">
                    {active.isGroup ? (
                      "Nhóm trò chuyện"
                    ) : active.isOnline ? (
                      <>
                        <span className="h-2 w-2 rounded-full" style={{ background: "var(--success)" }} />
                        Trực tuyến
                      </>
                    ) : (
                      lastActive(active.lastSeen)
                    )}
                  </div>
                </div>
              </button>
              <div className="ml-auto flex items-center gap-1">
                <CircleIconButton
                  label="Tìm tin nhắn"
                  active={messageSearchOpen}
                  onClick={() => {
                    setMessageSearchOpen((open) => !open);
                    if (messageSearchOpen) setMessageQuery("");
                  }}
                >
                  <Search className="h-[18px] w-[18px]" />
                </CircleIconButton>
                <CircleIconButton
                  label="Gọi thoại"
                  onClick={() => void beginCall("audio")}
                  disabled={!active.username || active.isGroup || active.username === SUPPORT_USERNAME || !!call}
                >
                  <Phone className="h-[18px] w-[18px]" />
                </CircleIconButton>
                <CircleIconButton
                  label="Gọi video"
                  onClick={() => void beginCall("video")}
                  disabled={!active.username || active.isGroup || active.username === SUPPORT_USERNAME || !!call}
                >
                  <Video className="h-[18px] w-[18px]" />
                </CircleIconButton>
                <CircleIconButton label="Hồ sơ" active={profileOpen} onClick={() => setProfileOpen((o) => !o)}>
                  <MoreHorizontal className="h-[18px] w-[18px]" />
                </CircleIconButton>
              </div>
            </header>

            {messageSearchOpen && (
              <div className="flex items-center gap-2 border-b border-[var(--glass-border)] px-3 py-2">
                <Search className="h-4 w-4 shrink-0 text-[var(--text-muted)]" />
                <input
                  autoFocus
                  value={messageQuery}
                  onChange={(e) => setMessageQuery(e.target.value)}
                  placeholder="Tìm nội dung hoặc tên tệp…"
                  className="min-w-0 flex-1 bg-transparent text-sm text-[var(--text)] outline-none"
                  aria-label="Tìm trong hội thoại"
                />
                {debouncedMessageQuery && !msgLoading && (
                  <span className="shrink-0 text-xs text-[var(--text-muted)]">{messages.length} kết quả</span>
                )}
                <button
                  type="button"
                  onClick={() => { setMessageQuery(""); setMessageSearchOpen(false); }}
                  className="grid h-8 w-8 shrink-0 place-items-center rounded-full text-[var(--text-muted)] hover:bg-[var(--accent-soft)] hover:text-[var(--accent)]"
                  aria-label="Đóng tìm kiếm"
                >
                  <X className="h-4 w-4" />
                </button>
              </div>
            )}

            <div
              className="relative flex min-h-0 flex-1 flex-col"
              onDragOver={onZoneOver("thread")}
              onDragLeave={onZoneLeave("thread")}
              onDrop={onThreadDrop}
            >
            <div ref={scrollRef} className="scroll-thin flex-1 overflow-y-auto p-4">
              {msgLoading ? (
                <MessageSkeletons />
              ) : msgError && messages.length === 0 ? (
                <div className="grid h-full place-items-center px-6 text-center text-sm text-[var(--danger)]">
                  <div>
                    <div>{msgError}</div>
                    <button
                      type="button"
                      onClick={() => reloadMessages()}
                      className="mt-3 rounded-xl px-3 py-2 font-semibold text-[var(--accent)] hover:bg-[var(--accent-soft)]"
                    >
                      Thử lại
                    </button>
                  </div>
                </div>
              ) : messages.length === 0 ? (
                <div className="grid h-full place-items-center text-center text-sm text-[var(--text-muted)]">
                  <div>
                    <MessageCircle className="mx-auto mb-2 h-8 w-8 opacity-50" />
                    Chưa có tin nhắn. Hãy gửi lời chào 👋
                  </div>
                </div>
              ) : (
                // min-h-full + justify-end: ít tin thì dồn xuống đáy, nhiều tin thì tràn lên trên
                // và cuộn bình thường — tin nhắn luôn "đi từ dưới lên" như app chat thật.
                <div className="flex min-h-full flex-col justify-end space-y-3">
                  {canLoadOlder && (
                    <button
                      type="button"
                      onClick={() => void loadOlderMessages()}
                      disabled={loadingOlder}
                      className="mx-auto mb-1 inline-flex items-center gap-2 rounded-full border border-[var(--glass-border)] px-3 py-1.5 text-xs font-semibold text-[var(--text-secondary)] hover:bg-[var(--accent-soft)] hover:text-[var(--accent)] disabled:opacity-60"
                    >
                      {loadingOlder && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
                      {loadingOlder ? "Đang tải…" : "Tải tin cũ hơn"}
                    </button>
                  )}
                  {messages.map((m) =>
                    editingId === m.id ? (
                      <EditRow
                        key={m.id}
                        value={editDraft}
                        onChange={setEditDraft}
                        onSave={saveEdit}
                        onCancel={cancelEdit}
                      />
                    ) : m.kind === "file" || m.kind === "voice" ? (
                      <FileBubble
                        key={m.id}
                        msg={m}
                        conversationId={active.id}
                        transfer={transfers.get(m.id)}
                        serverBusy={serverDownloading === m.id}
                        onRedownload={() => {
                          const t = transfers.get(m.id);
                          if (t) redownload(t.tid);
                        }}
                        onServerDownload={() => void serverDownload(m)}
                        onLoadVoice={() => loadVoiceBlob(m)}
                        onReact={(emoji) => toggleReaction(m, emoji)}
                      />
                    ) : (
                      <Bubble
                        key={m.id}
                        msg={m}
                        group={active.isGroup || active.supportConversation}
                        onForward={() => setForwardMsg(m)}
                        onEdit={() => startEdit(m)}
                        onRemove={() => removeMsg(m)}
                        onReact={(emoji) => toggleReaction(m, emoji)}
                      />
                    ),
                  )}
                </div>
              )}
            </div>
              {dragZone === "thread" && (
                <DropOverlay icon={<Send className="h-7 w-7" />} title="Thả để gửi nhanh" sub="Gửi ngay qua LAN" />
              )}
            </div>

            {previewFile && (
              <div className="border-t border-[var(--glass-border)] p-3">
                <div
                  className="flex items-center gap-3 rounded-2xl p-2.5"
                  style={{ background: "var(--glass-bg-strong)", border: "1px solid var(--glass-border)" }}
                >
                  {previewUrl && previewMediaKind === "image" ? (
                    <img src={previewUrl} alt="" className="h-14 w-14 shrink-0 rounded-xl object-cover" />
                  ) : previewUrl && previewMediaKind === "video" ? (
                    <video src={previewUrl} muted playsInline preload="metadata" className="h-14 w-20 shrink-0 rounded-xl bg-black object-cover" />
                  ) : (
                    <span
                      className="grid h-14 w-14 shrink-0 place-items-center rounded-xl"
                      style={{ background: "var(--accent-soft)", color: "var(--accent)" }}
                    >
                      <FileText className="h-6 w-6" />
                    </span>
                  )}
                  <div className="min-w-0 flex-1">
                    <div className="truncate text-sm font-semibold text-[var(--text)]" title={previewFile.name}>
                      {previewFile.name}
                    </div>
                    <div className="text-xs text-[var(--text-muted)]">{formatBytes(previewFile.size)} · Xem trước khi gửi</div>
                  </div>
                  <button
                    type="button"
                    onClick={() => setPreviewFile(null)}
                    className="rounded-lg px-3 py-1.5 text-sm font-semibold text-[var(--text-secondary)] transition hover:bg-[var(--accent-soft)]"
                  >
                    Hủy
                  </button>
                  <button
                    type="button"
                    onClick={() => void sendPreview()}
                    className="inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-sm font-semibold text-white transition hover:opacity-90"
                    style={{ background: "var(--accent)" }}
                  >
                    <Send className="h-4 w-4" />
                    Gửi
                  </button>
                </div>
              </div>
            )}

            {pendingVoice && !recordingVoice && (
              <div className="flex items-center gap-3 border-t border-[var(--glass-border)] px-3 py-2 text-sm">
                <span className="grid h-9 w-9 shrink-0 place-items-center rounded-full bg-[var(--accent-soft)] text-[var(--accent)]">
                  {voiceSending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Mic className="h-4 w-4" />}
                </span>
                <div className="min-w-0 flex-1">
                  <div className="font-semibold text-[var(--text)]">{voiceSending ? "Đang gửi tin thoại…" : "Tin thoại chưa gửi được"}</div>
                  <div className="text-xs text-[var(--text-muted)]">{formatBytes(pendingVoice.blob.size)}</div>
                </div>
                {!voiceSending && (
                  <>
                    <button
                      type="button"
                      onClick={() => setPendingVoice(null)}
                      className="rounded-xl px-3 py-2 font-semibold text-[var(--text-secondary)] hover:bg-[var(--accent-soft)]"
                    >
                      Bỏ
                    </button>
                    <button
                      type="button"
                      onClick={() => void uploadVoice(pendingVoice)}
                      className="rounded-xl px-3 py-2 font-semibold text-white hover:opacity-90"
                      style={{ background: "var(--accent)" }}
                    >
                      Gửi lại
                    </button>
                  </>
                )}
              </div>
            )}

            <footer
              className="relative flex items-center gap-2 border-t border-[var(--glass-border)] p-3"
              onDragOver={onZoneOver("composer")}
              onDragLeave={onZoneLeave("composer")}
              onDrop={onComposerDrop}
            >
              <input ref={fileInputRef} type="file" className="hidden" onChange={onFileInputChange} />
              {dragZone === "composer" && (
                <DropOverlay icon={<Upload className="h-6 w-6" />} title="Thả để xem trước" sub="Kiểm tra rồi mới gửi" />
              )}
              {recordingVoice ? (
                <>
                  <button
                    type="button"
                    onClick={() => finishVoiceRecording(false)}
                    className="grid h-10 w-10 shrink-0 place-items-center rounded-full text-[var(--danger)] hover:bg-[color-mix(in_srgb,var(--danger)_12%,transparent)]"
                    aria-label="Hủy bản ghi"
                  >
                    <X className="h-5 w-5" />
                  </button>
                  <div className="flex min-w-0 flex-1 items-center gap-2 rounded-full border border-[var(--glass-border)] px-4 py-2">
                    <span className="h-2.5 w-2.5 animate-pulse rounded-full bg-[var(--danger)]" />
                    <span className="truncate text-sm font-semibold text-[var(--text)]">Đang ghi âm</span>
                    <span className="ml-auto text-sm tabular-nums text-[var(--text-secondary)]">{fmtDuration(voiceElapsedMs)}</span>
                  </div>
                  <button
                    type="button"
                    onClick={() => finishVoiceRecording(true)}
                    className="grid h-10 w-10 shrink-0 place-items-center rounded-full text-white transition hover:opacity-90"
                    style={{ background: "var(--accent)" }}
                    aria-label="Dừng và gửi tin thoại"
                  >
                    <Send className="h-[18px] w-[18px]" />
                  </button>
                </>
              ) : (
                <>
                  <button
                    type="button"
                    onClick={() => fileInputRef.current?.click()}
                    disabled={active.isGroup || !canSendLanFiles}
                    title={
                      active.isGroup
                        ? "Gửi tệp chỉ hỗ trợ trò chuyện 1-1"
                        : isSupportPeer
                          ? "Tài khoản hỗ trợ không nhận tệp qua LAN"
                          : outgoingAsSupport
                            ? "Tắt chế độ Hỗ Trợ để gửi tệp bằng tài khoản admin"
                            : canSendLanFiles
                              ? "Gửi tệp qua LAN"
                              : "Chỉ hội viên kim cương mới được gửi tệp qua LAN"
                    }
                    className="grid h-10 w-10 shrink-0 place-items-center rounded-full text-[var(--accent)] transition hover:bg-[var(--accent-soft)] disabled:opacity-40"
                    aria-label="Gửi tệp qua LAN"
                  >
                    <Paperclip className="h-5 w-5" />
                  </button>
                  <button
                    type="button"
                    onClick={() => void startVoiceRecording()}
                    disabled={voiceSending || !!pendingVoice}
                    className="grid h-10 w-10 shrink-0 place-items-center rounded-full text-[var(--accent)] transition hover:bg-[var(--accent-soft)] disabled:opacity-40"
                    aria-label="Ghi tin nhắn thoại"
                    title="Ghi tin nhắn thoại"
                  >
                    <Mic className="h-5 w-5" />
                  </button>
                  {canUseSupportSender && (
                    <select
                      value={outgoingAsSupport ? "support" : "admin"}
                      onChange={(e) => setSendAsSupport(e.target.value === "support")}
                      className="h-10 shrink-0 rounded-full px-3 text-xs font-bold text-[var(--text)] outline-none transition"
                      style={{ background: "var(--glass-bg-strong)", border: "1px solid var(--glass-border)" }}
                      aria-label="Chọn tài khoản gửi"
                      title="Chọn tài khoản gửi"
                    >
                      <option value="admin">Admin</option>
                      <option value="support">Hỗ Trợ Người Dùng</option>
                    </select>
                  )}
                  <div className="relative flex min-w-0 flex-1 items-center gap-2 px-1 py-1.5">
                    {composerEmojiOpen && (
                      <div
                        className="absolute bottom-11 right-0 z-20 flex items-center gap-0.5 rounded-full border border-[var(--glass-border)] px-1.5 py-1 shadow-lg"
                        style={{ background: "var(--glass-bg-strong)", backdropFilter: "blur(10px)" }}
                      >
                        {REACTIONS.map((emoji) => (
                          <button
                            key={emoji}
                            type="button"
                            onMouseDown={(e) => e.preventDefault()}
                            onClick={() => { setDraft((value) => `${value}${emoji}`.slice(0, 4000)); setComposerEmojiOpen(false); draftInputRef.current?.focus(); }}
                            className="grid h-8 w-8 place-items-center rounded-full text-lg hover:scale-110 hover:bg-[var(--accent-soft)]"
                          >
                            {emoji}
                          </button>
                        ))}
                      </div>
                    )}
                    <input
                      ref={draftInputRef}
                      value={draft}
                      onChange={(e) => setDraft(e.target.value.slice(0, 4000))}
                      onFocus={() => setComposerFocused(true)}
                      onBlur={() => setComposerFocused(false)}
                      onKeyDown={(e) => e.key === "Enter" && void send()}
                      placeholder={outgoingAsSupport ? "Nhập tin nhắn với tài khoản Hỗ Trợ…" : "Nhập tin nhắn…"}
                      className="min-w-0 flex-1 bg-transparent text-sm text-[var(--text)] outline-none"
                    />
                    <button
                      type="button"
                      onMouseDown={(e) => e.preventDefault()}
                      onClick={() => setComposerEmojiOpen((open) => !open)}
                      className="text-[var(--text-muted)] transition hover:text-[var(--accent)]"
                      aria-label="Chèn biểu cảm"
                      aria-expanded={composerEmojiOpen}
                    >
                      <Smile className="h-5 w-5" />
                    </button>
                  </div>
                  <button
                    type="button"
                    onMouseDown={(e) => e.preventDefault()}
                    onClick={() => void send()}
                    disabled={sending || !draft.trim()}
                    className="grid h-10 w-10 shrink-0 place-items-center rounded-full text-white transition hover:opacity-90 disabled:opacity-50"
                    style={{ background: "var(--accent)" }}
                    aria-label="Gửi"
                  >
                    {sending ? <Loader2 className="h-[18px] w-[18px] animate-spin" /> : <Send className="h-[18px] w-[18px]" />}
                  </button>
                </>
              )}
            </footer>
          </>
        ) : (
          <div className="grid flex-1 place-items-center p-8 text-center text-[var(--text-muted)]">
            <div>
              <MessageCircle className="mx-auto mb-3 h-12 w-12 opacity-40" />
              <div className="text-base font-semibold text-[var(--text-secondary)]">Chọn một cuộc trò chuyện</div>
              <div className="mt-1 text-sm">hoặc bấm + để nhắn tin với đồng nghiệp.</div>
            </div>
          </div>
        )}
      </GlassPanel>

      {/* ---------- Cột hồ sơ (trượt từ phải sang, đẩy khung chat) ---------- */}
      <div
        className={`chat-profile-shell flex justify-end overflow-hidden transition-[width,margin] duration-300 ease-[cubic-bezier(0.22,1,0.36,1)] ${
          profileOpen && active ? "" : "pointer-events-none"
        }`}
        style={profileOpen && active ? { width: 300, marginLeft: 0 } : { width: 0, marginLeft: -12 }}
        aria-hidden={!profileOpen || !active}
      >
        <GlassPanel strong className="flex w-[300px] shrink-0 flex-col overflow-hidden">
          {active && (
            <div className="scroll-thin flex-1 overflow-y-auto p-4">
              <button
                type="button"
                onClick={() => setProfileOpen(false)}
                aria-label="Quay lai cuoc tro chuyen"
                className="chat-profile-back mb-3 h-10 w-10 place-items-center rounded-full text-[var(--text-secondary)] transition hover:bg-[var(--accent-soft)] hover:text-[var(--accent)]"
              >
                <ArrowLeft className="h-[18px] w-[18px]" />
              </button>
              <div className="flex flex-col items-center text-center">
                <Avatar name={active.title} url={active.avatarUrl} size={88} online={active.isOnline} group={active.isGroup} />
                <NameWithBadge
                  name={active.title}
                  verified={active.verified}
                  isDiamond={active.isDiamond}
                  className="mt-3 text-lg font-bold text-[var(--text)]"
                />
                <div className="text-sm text-[var(--text-secondary)]">
                  {active.isGroup ? "Nhóm trò chuyện" : active.isOnline ? "Trực tuyến" : lastActive(active.lastSeen)}
                </div>
                {active.username && <div className="text-xs text-[var(--text-muted)]">@{active.username}</div>}
              </div>

              <div className="mt-4 grid grid-cols-2 gap-2">
                {[
                  {
                    icon: active.pinned ? PinOff : Pin,
                    label: active.pinned ? "Bỏ ghim" : "Ghim",
                    onClick: () => void toggleConversationPin(active),
                  },
                  {
                    icon: Search,
                    label: "Tìm tin",
                    onClick: () => { setProfileOpen(false); setMessageSearchOpen(true); },
                  },
                ].map((a) => (
                  <button
                    key={a.label}
                    type="button"
                    onClick={a.onClick}
                    className="flex flex-col items-center gap-1.5 rounded-2xl py-3 text-[var(--text-secondary)] transition hover:text-[var(--accent)]"
                    style={{ background: "var(--glass-bg-strong)", border: "1px solid var(--glass-border)" }}
                  >
                    <a.icon className="h-5 w-5" />
                    <span className="text-[0.66rem] font-semibold">{a.label}</span>
                  </button>
                ))}
              </div>
            </div>
          )}
        </GlassPanel>
      </div>

      {newChatOpen && (
        <ContactPickerModal title="Tin nhắn mới" onClose={() => setNewChatOpen(false)} onPick={startChat} />
      )}
      {forwardMsg && (
        <ContactPickerModal title="Chuyển tiếp đến…" onClose={() => setForwardMsg(null)} onPick={doForward} />
      )}
      {call?.stage === "incoming" && <IncomingCallOverlay call={call} onAccept={acceptIncomingCallInTab} />}
    </div>
  );
}

/** Hộp thoại chọn người (danh bạ = tài khoản web) — dùng cho cả "Tin nhắn mới" và "Chuyển tiếp". */
function ContactPickerModal({
  title,
  onClose,
  onPick,
}: {
  title: string;
  onClose: () => void;
  onPick: (c: ChatContact) => void;
}) {
  const [search, setSearch] = useState("");
  const { data, loading } = useApi<ChatContact[]>(`/api/chat/contacts?search=${encodeURIComponent(search)}`, [search]);
  const contacts = data ?? [];

  return (
    <Modal open onClose={onClose} title={title}>
      <div className="space-y-3">
        <div
          className="flex items-center gap-2 rounded-xl px-3 py-2"
          style={{ background: "var(--glass-bg-strong)", border: "1px solid var(--glass-border)" }}
        >
          <Search className="h-4 w-4 shrink-0 text-[var(--text-muted)]" />
          <input
            autoFocus
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Tìm theo tên hoặc tên đăng nhập…"
            className="w-full bg-transparent text-sm font-medium text-[var(--text)] outline-none"
          />
        </div>

        <div className="scroll-thin max-h-[50vh] space-y-1 overflow-y-auto">
          {loading && contacts.length === 0 ? (
            <ContactSkeletons />
          ) : contacts.length === 0 ? (
            <div className="py-8 text-center text-sm text-[var(--text-muted)]">Không tìm thấy người dùng.</div>
          ) : (
            contacts.map((c) => (
              <button
                key={c.username}
                type="button"
                onClick={() => onPick(c)}
                className="flex w-full items-center gap-3 rounded-2xl p-2.5 text-left transition hover:bg-[var(--accent-soft)]"
              >
                <Avatar name={c.displayName} url={c.avatarUrl} size={40} online={c.isOnline} />
                <div className="min-w-0 flex-1">
                  <NameWithBadge
                    name={c.displayName}
                    verified={c.verified}
                    isDiamond={c.isDiamond}
                    className="text-sm font-bold text-[var(--text)]"
                  />
                  <div className="truncate text-xs text-[var(--text-secondary)]">
                    @{c.username} · {c.isOnline ? "Trực tuyến" : "Ngoại tuyến"}
                  </div>
                </div>
              </button>
            ))
          )}
        </div>
      </div>
    </Modal>
  );
}
