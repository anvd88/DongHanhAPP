import { useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import {
  ArrowLeft,
  Ban,
  Bell,
  CheckCheck,
  Forward,
  Home,
  Loader2,
  MessageCircle,
  MoreHorizontal,
  Pencil,
  Phone,
  Plus,
  Search,
  Send,
  Smile,
  SmilePlus,
  SquarePen,
  Trash2,
  User,
  Video,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { initials } from "../lib/format";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Modal } from "../components/Modal";
import { VerifiedBadge } from "../components/VerifiedBadge";
import { useApi } from "../lib/useApi";
import { api } from "../lib/api";
import type { ChatContact, ChatConversation, ChatMessage, ChatReaction } from "../lib/types";
import "../features/giacong/giacong.css";

/* =========================================================================
   Trang Trò chuyện — khung 3 cột (danh sách / hội thoại / hồ sơ).
   Dữ liệu thật từ /api/chat: danh bạ và tên hiển thị lấy từ hệ tài khoản của web
   (full_name), trạng thái online từ phiên đăng nhập, tích xanh cho Admin / tài
   khoản được cấp. Giữ nguyên hệ màu & tấm kính của app (accent + glass).
   ========================================================================= */

type Filter = "all" | "unread" | "groups";

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

/** Tên + tích xanh (nếu đã xác minh). */
function NameWithBadge({ name, verified, className }: { name: string; verified?: boolean; className?: string }) {
  return (
    <span className={`inline-flex min-w-0 items-center gap-1 ${className ?? ""}`}>
      <span className="truncate">{name}</span>
      {verified && <VerifiedBadge size={15} />}
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
  icon: typeof Forward;
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
            {mine && <CheckCheck className="h-3.5 w-3.5" />}
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
}: {
  children: React.ReactNode;
  label: string;
  onClick?: () => void;
  active?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label={label}
      aria-pressed={active}
      className={`grid h-9 w-9 place-items-center rounded-full transition ${
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

export function Chats() {
  const navigate = useNavigate();
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
  const [isMobile, setIsMobile] = useState(() =>
    typeof window !== "undefined" ? window.matchMedia("(max-width: 767px)").matches : false,
  );
  const scrollRef = useRef<HTMLDivElement>(null);
  const draftInputRef = useRef<HTMLInputElement>(null);
  const touchStartRef = useRef<{ x: number; y: number } | null>(null);

  const { data: convData, loading, reload: reloadConversations } = useApi<ChatConversation[]>("/api/chat/conversations");
  const allConversations = useMemo(() => convData ?? [], [convData]);

  const { data: msgData, loading: msgLoading, reload: reloadMessages } = useApi<ChatMessage[]>(
    activeId ? `/api/chat/conversations/${activeId}/messages` : null,
    [activeId],
  );
  const messages = useMemo(() => msgData ?? [], [msgData]);

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

  // Tự chọn hội thoại đầu tiên khi vào trang (nếu chưa chọn).
  useEffect(() => {
    if (!isMobile && !activeId && allConversations.length > 0) setActiveId(allConversations[0].id);
  }, [activeId, allConversations, isMobile]);

  const conversations = useMemo(() => {
    const q = query.trim().toLowerCase();
    return allConversations.filter((c) => {
      if (filter === "unread" && !c.unread) return false;
      if (filter === "groups" && !c.isGroup) return false;
      if (q && !c.title.toLowerCase().includes(q) && !c.preview.toLowerCase().includes(q)) return false;
      return true;
    });
  }, [allConversations, filter, query]);

  const active =
    allConversations.find((c) => c.id === activeId) ??
    (activeFallback?.id === activeId ? activeFallback : null);

  const scrollToBottom = (smooth = true) =>
    requestAnimationFrame(() =>
      scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: smooth ? "smooth" : "auto" }),
    );

  // Cuộn xuống cuối khi đổi hội thoại hoặc có tin nhắn mới.
  useEffect(() => {
    scrollToBottom(false);
  }, [activeId, messages.length]);

  // Hủy chỉnh sửa khi chuyển sang hội thoại khác.
  useEffect(() => {
    setEditingId(null);
    setEditDraft("");
  }, [activeId]);

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
      await api.post(`/api/chat/conversations/${activeId}/messages`, { body: text });
      reloadMessages({ silent: true });
      reloadConversations({ silent: true });
      scrollToBottom();
    } catch (e) {
      setDraft(text);
      alert(e instanceof Error ? e.message : "Không gửi được tin nhắn");
    } finally {
      setSending(false);
      if (shouldKeepKeyboard) {
        requestAnimationFrame(() => draftInputRef.current?.focus({ preventScroll: true }));
      }
    }
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
        preview: "",
        lastAt: null,
        unread: 0,
      });
      reloadConversations({ silent: true });
    } catch (e) {
      alert(e instanceof Error ? e.message : "Không mở được cuộc trò chuyện");
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
      alert(e instanceof Error ? e.message : "Không sửa được tin nhắn");
    }
  };

  const removeMsg = async (m: ChatMessage) => {
    if (!activeId) return;
    if (!confirm("Gỡ tin nhắn này? Mọi người trong cuộc trò chuyện sẽ không xem được nữa.")) return;
    try {
      await api.del(`/api/chat/conversations/${activeId}/messages/${m.id}`);
      reloadMessages({ silent: true });
      reloadConversations({ silent: true });
    } catch (e) {
      alert(e instanceof Error ? e.message : "Không gỡ được tin nhắn");
    }
  };

  // Thả / đổi / bỏ biểu cảm cho tin nhắn. Backend tự toggle (bấm lại đúng biểu cảm → bỏ).
  const toggleReaction = async (m: ChatMessage, emoji: string) => {
    if (!activeId) return;
    try {
      await api.post(`/api/chat/conversations/${activeId}/messages/${m.id}/react`, { emoji });
      reloadMessages({ silent: true });
    } catch (e) {
      alert(e instanceof Error ? e.message : "Không thả được biểu cảm");
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
        preview: "",
        lastAt: null,
        unread: 0,
      });
      reloadConversations({ silent: true });
    } catch (e) {
      alert(e instanceof Error ? e.message : "Không chuyển tiếp được tin nhắn");
    }
  };

  return (
    <div
      className={`gc-root chat-mobile-shell flex h-full min-h-0 gap-3 ${activeId ? "is-chat-open" : ""} ${
        composerFocused ? "is-composer-focused" : ""
      }`}
      onTouchStart={handleTouchStart}
      onTouchEnd={handleTouchEnd}
    >
      {/* ---------- Cột danh sách hội thoại ---------- */}
      <GlassPanel strong className="chat-conversation-list flex w-[320px] shrink-0 flex-col overflow-hidden p-3">
        <div className="mb-3 flex items-center justify-between">
          <div className="flex min-w-0 items-center gap-2">
            <button
              type="button"
              onClick={() => navigate("/dashboard")}
              className="chat-home-button grid h-9 w-9 shrink-0 place-items-center rounded-xl text-[var(--text-secondary)] transition hover:bg-[var(--accent-soft)] hover:text-[var(--accent)]"
              aria-label="Về trang chủ"
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
              <button
                key={c.id}
                type="button"
                onClick={() => openConversation(c)}
                className="mb-1 flex w-full items-center gap-3 rounded-2xl p-2.5 text-left transition"
                style={on ? { background: "var(--accent-soft)" } : undefined}
              >
                <Avatar name={c.title} url={c.avatarUrl} online={c.isOnline} group={c.isGroup} />
                <div className="min-w-0 flex-1">
                  <div className="flex items-baseline justify-between gap-2">
                    <NameWithBadge
                      name={c.title}
                      verified={c.verified}
                      className="text-sm font-bold text-[var(--text)]"
                    />
                    <span className="shrink-0 text-[0.68rem] font-medium text-[var(--text-muted)]">{fmtTime(c.lastAt)}</span>
                  </div>
                  <div className="flex items-center justify-between gap-2">
                    <span className="truncate text-xs text-[var(--text-secondary)]">{c.preview || "Bắt đầu trò chuyện…"}</span>
                    {c.unread ? (
                      <span
                        className="grid h-5 min-w-[20px] shrink-0 place-items-center rounded-full px-1.5 text-[0.66rem] font-bold text-white"
                        style={{ background: "var(--accent)" }}
                      >
                        {c.unread}
                      </span>
                    ) : null}
                  </div>
                </div>
              </button>
            );
          })}
          {loading && conversations.length === 0 && <ConversationSkeletons />}
          {!loading && conversations.length === 0 && (
            <div className="px-2 py-10 text-center text-sm text-[var(--text-muted)]">
              {allConversations.length === 0 ? "Chưa có cuộc trò chuyện nào. Bấm + để bắt đầu." : "Không có hội thoại phù hợp."}
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
                  <NameWithBadge name={active.title} verified={active.verified} className="text-sm font-bold text-[var(--text)]" />
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
                <CircleIconButton label="Tìm kiếm"><Search className="h-[18px] w-[18px]" /></CircleIconButton>
                <CircleIconButton label="Gọi thoại"><Phone className="h-[18px] w-[18px]" /></CircleIconButton>
                <CircleIconButton label="Gọi video"><Video className="h-[18px] w-[18px]" /></CircleIconButton>
                <CircleIconButton label="Hồ sơ" active={profileOpen} onClick={() => setProfileOpen((o) => !o)}>
                  <MoreHorizontal className="h-[18px] w-[18px]" />
                </CircleIconButton>
              </div>
            </header>

            <div ref={scrollRef} className="scroll-thin flex-1 overflow-y-auto p-4">
              {msgLoading ? (
                <MessageSkeletons />
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
                  {messages.map((m) =>
                    editingId === m.id ? (
                      <EditRow
                        key={m.id}
                        value={editDraft}
                        onChange={setEditDraft}
                        onSave={saveEdit}
                        onCancel={cancelEdit}
                      />
                    ) : (
                      <Bubble
                        key={m.id}
                        msg={m}
                        group={active.isGroup}
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

            <footer className="flex items-center gap-2 border-t border-[var(--glass-border)] p-3">
              <button
                type="button"
                className="grid h-10 w-10 shrink-0 place-items-center rounded-full text-[var(--accent)] transition hover:bg-[var(--accent-soft)]"
                aria-label="Đính kèm"
              >
                <Plus className="h-5 w-5" />
              </button>
              <div
                className="flex flex-1 items-center gap-2 rounded-full px-4 py-2"
                style={{ background: "var(--glass-bg-strong)", border: "1px solid var(--glass-border)" }}
              >
                <input
                  ref={draftInputRef}
                  value={draft}
                  onChange={(e) => setDraft(e.target.value)}
                  onFocus={() => setComposerFocused(true)}
                  onBlur={() => setComposerFocused(false)}
                  onKeyDown={(e) => e.key === "Enter" && send()}
                  placeholder="Nhập tin nhắn…"
                  className="w-full bg-transparent text-sm text-[var(--text)] outline-none"
                />
                <button type="button" className="text-[var(--text-muted)] transition hover:text-[var(--accent)]" aria-label="Biểu cảm">
                  <Smile className="h-5 w-5" />
                </button>
              </div>
              <button
                type="button"
                onMouseDown={(e) => e.preventDefault()}
                onClick={send}
                disabled={sending || !draft.trim()}
                className="grid h-10 w-10 shrink-0 place-items-center rounded-full text-white transition hover:opacity-90 disabled:opacity-50"
                style={{ background: "var(--accent)" }}
                aria-label="Gửi"
              >
                {sending ? <Loader2 className="h-[18px] w-[18px] animate-spin" /> : <Send className="h-[18px] w-[18px]" />}
              </button>
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
                <NameWithBadge name={active.title} verified={active.verified} className="mt-3 text-lg font-bold text-[var(--text)]" />
                <div className="text-sm text-[var(--text-secondary)]">
                  {active.isGroup ? "Nhóm trò chuyện" : active.isOnline ? "Trực tuyến" : lastActive(active.lastSeen)}
                </div>
                {active.username && <div className="text-xs text-[var(--text-muted)]">@{active.username}</div>}
              </div>

              <div className="mt-4 grid grid-cols-4 gap-2">
                {[
                  { icon: User, label: "Hồ sơ" },
                  { icon: Bell, label: "Tắt báo" },
                  { icon: Search, label: "Tìm" },
                  { icon: MoreHorizontal, label: "Thêm" },
                ].map((a) => (
                  <button
                    key={a.label}
                    type="button"
                    className="flex flex-col items-center gap-1.5 rounded-2xl py-3 text-[var(--text-secondary)] transition hover:text-[var(--accent)]"
                    style={{ background: "var(--glass-bg-strong)", border: "1px solid var(--glass-border)" }}
                  >
                    <a.icon className="h-5 w-5" />
                    <span className="text-[0.66rem] font-semibold">{a.label}</span>
                  </button>
                ))}
              </div>

              {active.verified && (
                <div
                  className="mt-4 flex items-center gap-2 rounded-2xl p-3.5 text-sm"
                  style={{ background: "var(--glass-bg-strong)", border: "1px solid var(--glass-border)" }}
                >
                  <VerifiedBadge size={20} />
                  <span className="font-semibold text-[var(--text)]">Tài khoản đã xác minh</span>
                </div>
              )}
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
                  <NameWithBadge name={c.displayName} verified={c.verified} className="text-sm font-bold text-[var(--text)]" />
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
