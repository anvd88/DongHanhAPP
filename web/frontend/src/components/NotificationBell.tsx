import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  Banknote,
  Bell,
  BellOff,
  CheckCheck,
  FileText,
  MessageCircle,
  ShieldCheck,
  Trash2,
  Truck,
  Wallet,
  X,
} from "lucide-react";
import { api } from "../lib/api";
import { useAuth } from "../lib/auth";
import { subscribeRealtime } from "../lib/realtime";
import { useChatNotifications } from "./chat-notifications-context";
import { useAppNotifications } from "./app-notifications-context";
import "./notification-bell.css";

/**
 * CHUÔNG THÔNG BÁO trên header — MỘT hộp thư duy nhất cho cả tin nhắn chat lẫn thông báo nghiệp vụ
 * (giao hàng, thu tiền, chứng từ, nhân sự…), theo đúng lựa chọn "gộp chat + công việc vào 1 chuông".
 *
 * Hai nguồn, KHÔNG gộp ở máy chủ:
 *   • Thông báo nghiệp vụ nằm ở bảng web_notifications (/api/notifications) — có đọc/chưa đọc, xoá được.
 *   • Tin nhắn chat lấy từ danh sách hội thoại đã có sẵn (ChatNotificationProvider). Cố ý KHÔNG ghi
 *     thêm dòng vào hộp thư cho mỗi tin nhắn: "đã đọc" của chat là mở đúng cuộc trò chuyện, ghi trùng
 *     sang bảng khác sẽ đếm hai lần và không bao giờ khớp lại được.
 * Trộn ở phía trình duyệt nên mỗi bên vẫn giữ đúng khái niệm "đã đọc" của mình.
 *
 * Realtime: nghe scope "notify" (thông báo mới) và "chat" (tin nhắn mới) — không hề poll.
 */

type WorkNotification = {
  id: number;
  title: string;
  body: string;
  category: string;
  link: string;
  createdAt: string;
  read: boolean;
};

type Feed = { unread: number; items: WorkNotification[] };

type BellItem = {
  key: string;
  kind: "work" | "chat";
  id: number;
  title: string;
  body: string;
  category: string;
  at: number;
  unread: boolean;
  open: () => void;
};

const CATEGORY_ICON: Record<string, typeof Bell> = {
  delivery: Truck,
  collection: Banknote,
  document: FileText,
  payout: Wallet,
  task: CheckCheck,
  request: FileText,
  penalty: ShieldCheck,
  attendance: CheckCheck,
  security: ShieldCheck,
  chat: MessageCircle,
};

const CATEGORY_LABEL: Record<string, string> = {
  delivery: "Giao hàng",
  collection: "Thu tiền",
  document: "Chứng từ",
  payout: "Phiếu chi",
  task: "Việc được giao",
  request: "Đơn từ",
  penalty: "Kỷ luật",
  attendance: "Chấm công",
  security: "Bảo mật",
  system: "Hệ thống",
  chat: "Tin nhắn",
  general: "Thông báo",
};

/** "3 phút trước" đọc nhanh hơn ngày giờ đầy đủ khi đang lướt danh sách. */
function timeAgo(at: number) {
  const diff = Math.max(0, Date.now() - at);
  const minutes = Math.floor(diff / 60_000);
  if (minutes < 1) return "vừa xong";
  if (minutes < 60) return `${minutes} phút trước`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} giờ trước`;
  const days = Math.floor(hours / 24);
  if (days < 7) return `${days} ngày trước`;
  return new Date(at).toLocaleDateString("vi-VN", { day: "2-digit", month: "2-digit", year: "numeric" });
}

export function NotificationBell() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const { notify } = useAppNotifications();
  const { conversations, unreadCount: chatUnread } = useChatNotifications();
  const [feed, setFeed] = useState<Feed>({ unread: 0, items: [] });
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const panelRef = useRef<HTMLDivElement | null>(null);
  // Mốc để phát hiện "vừa có thông báo mới" mà không phải so cả danh sách. Lần tải ĐẦU TIÊN sau khi
  // mở trang không được báo: lúc đó mọi dòng đều "mới" với trình duyệt nhưng đã cũ với người dùng.
  const lastSeenIdRef = useRef<number | null>(null);

  const load = useCallback(
    async ({ announce = false }: { announce?: boolean } = {}) => {
      if (!user) return;
      try {
        const next = await api.get<Feed>("/api/notifications?limit=30");
        setFeed(next);
        const newest = next.items.find((item) => !item.read);
        if (announce && newest && lastSeenIdRef.current !== null && newest.id > lastSeenIdRef.current)
          notify.info(newest.body || newest.title, newest.title);
        lastSeenIdRef.current = next.items.length ? next.items[0].id : 0;
      } catch {
        // Mất mạng thoáng qua thì giữ nguyên danh sách cũ — chuông trống rỗng gây hiểu nhầm
        // "không có việc gì" nguy hiểm hơn là hiển thị dữ liệu chậm vài giây.
      }
    },
    [notify, user],
  );

  useEffect(() => {
    if (!user) {
      setFeed({ unread: 0, items: [] });
      lastSeenIdRef.current = null;
      return;
    }
    void load();
    const stop = subscribeRealtime(() => void load({ announce: true }), ["notify"]);
    const onFocus = () => void load();
    window.addEventListener("focus", onFocus);
    return () => {
      stop();
      window.removeEventListener("focus", onFocus);
    };
  }, [load, user]);

  // Bấm ra ngoài / bấm Esc thì đóng bảng, giống mọi menu khác trên header.
  useEffect(() => {
    if (!open) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") setOpen(false);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open]);

  const markRead = useCallback(async (id: number) => {
    setFeed((state) => ({
      unread: Math.max(0, state.unread - (state.items.find((i) => i.id === id && !i.read) ? 1 : 0)),
      items: state.items.map((i) => (i.id === id ? { ...i, read: true } : i)),
    }));
    try { await api.post(`/api/notifications/${id}/read`, {}); } catch { /* lần tải sau sẽ đồng bộ lại */ }
  }, []);

  const items = useMemo<BellItem[]>(() => {
    const work: BellItem[] = feed.items.map((n) => ({
      key: `work-${n.id}`,
      kind: "work",
      id: n.id,
      title: n.title,
      body: n.body,
      category: n.category,
      at: new Date(n.createdAt).getTime(),
      unread: !n.read,
      open: () => {
        void markRead(n.id);
        setOpen(false);
        if (n.link) navigate(n.link);
      },
    }));

    // Chỉ hội thoại CÒN tin chưa đọc mới lên chuông: chuông là "việc cần xử lý", không phải lịch sử chat.
    const chat: BellItem[] = conversations
      .filter((c) => (c.unread || 0) > 0)
      .map((c) => ({
        key: `chat-${c.id}`,
        kind: "chat",
        id: 0,
        title: c.title || "Tin nhắn mới",
        body: `${c.unread} tin nhắn chưa đọc${c.preview ? ` · ${c.preview}` : ""}`,
        category: "chat",
        at: c.lastAt ? new Date(c.lastAt).getTime() : Date.now(),
        unread: true,
        open: () => {
          setOpen(false);
          navigate(`/chats?conversation=${encodeURIComponent(c.id)}`);
        },
      }));

    return [...work, ...chat].sort((a, b) => b.at - a.at);
  }, [conversations, feed.items, markRead, navigate]);

  const totalUnread = feed.unread + chatUnread;

  const markAllRead = async () => {
    setBusy(true);
    try {
      await api.post("/api/notifications/read-all", {});
      setFeed((state) => ({ unread: 0, items: state.items.map((i) => ({ ...i, read: true })) }));
    } catch {
      notify.error("Không đánh dấu đã đọc được. Vui lòng thử lại.");
    } finally {
      setBusy(false);
    }
  };

  const clearRead = async () => {
    setBusy(true);
    try {
      await api.del("/api/notifications/read");
      await load();
    } catch {
      notify.error("Không dọn được thông báo đã đọc.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="km-bell">
      <button
        className="km-icon-button relative"
        type="button"
        aria-label={totalUnread ? `${totalUnread} thông báo chưa đọc` : "Thông báo"}
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
      >
        <Bell className="h-5 w-5" />
        {totalUnread > 0 && (
          <span className="km-notification-badge km-notification-badge--header">
            {totalUnread > 99 ? "99+" : totalUnread}
          </span>
        )}
      </button>

      {open && (
        <>
          <div className="km-bell-scrim" onClick={() => setOpen(false)} />
          <div className="km-bell-panel glass glass-strong fade-in" ref={panelRef} role="dialog" aria-label="Thông báo">
            <div className="km-bell-head">
              <div>
                <div className="km-bell-title">Thông báo</div>
                <div className="km-bell-sub">
                  {totalUnread > 0 ? `${totalUnread} mục chưa đọc` : "Bạn đã xem hết"}
                </div>
              </div>
              <button type="button" className="km-bell-close" aria-label="Đóng" onClick={() => setOpen(false)}>
                <X className="h-4 w-4" />
              </button>
            </div>

            <div className="km-bell-list">
              {items.length === 0 ? (
                <div className="km-bell-empty">
                  <BellOff className="h-7 w-7" />
                  <span>Chưa có thông báo nào.</span>
                  <small>Việc giao hàng, thu tiền, chứng từ và đơn từ sẽ hiện ở đây ngay khi phát sinh.</small>
                </div>
              ) : (
                items.map((item) => {
                  const Icon = CATEGORY_ICON[item.category] ?? Bell;
                  return (
                    <button
                      key={item.key}
                      type="button"
                      className={`km-bell-item ${item.unread ? "is-unread" : ""}`}
                      onClick={item.open}
                    >
                      <span className={`km-bell-icon km-bell-icon--${item.category}`}>
                        <Icon className="h-4 w-4" />
                      </span>
                      <span className="km-bell-body">
                        <span className="km-bell-kicker">
                          {CATEGORY_LABEL[item.category] ?? "Thông báo"} · {timeAgo(item.at)}
                        </span>
                        <span className="km-bell-item-title">{item.title}</span>
                        {item.body && <span className="km-bell-text">{item.body}</span>}
                      </span>
                      {item.unread && <span className="km-bell-dot" aria-hidden="true" />}
                    </button>
                  );
                })
              )}
            </div>

            <div className="km-bell-foot">
              <button type="button" disabled={busy || feed.unread === 0} onClick={() => void markAllRead()}>
                <CheckCheck className="h-4 w-4" /> Đánh dấu đã đọc
              </button>
              <button type="button" disabled={busy || feed.items.every((i) => !i.read)} onClick={() => void clearRead()}>
                <Trash2 className="h-4 w-4" /> Dọn mục đã đọc
              </button>
            </div>
            {chatUnread > 0 && (
              <div className="km-bell-note">
                Tin nhắn chỉ hết "chưa đọc" khi bạn mở đúng cuộc trò chuyện.
              </div>
            )}
          </div>
        </>
      )}
    </div>
  );
}
