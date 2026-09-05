import { useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore, type ReactNode } from "react";
import { MessageCircle } from "lucide-react";
import { useLocation, useNavigate } from "react-router-dom";
import { api } from "../lib/api";
import { useAuth } from "../lib/auth";
import { initials } from "../lib/format";
import { isMessagePreviewEnabled, subscribeMessagePreviewEnabled } from "../lib/messagePreviewPreference";
import { subscribeRealtime } from "../lib/realtime";
import { FileTransferPrompts } from "./FileTransferPrompts";
import type { ChatConversation, User } from "../lib/types";
import { ChatNotificationContext } from "./chat-notifications-context";

type ChatToast = {
  id: number;
  conversationId: string;
  title: string;
  body: string;
  avatarUrl?: string | null;
};

const TOAST_DURATION_MS = 6500;

function totalUnread(conversations: ChatConversation[]) {
  return conversations.reduce((sum, c) => sum + Math.max(0, c.unread || 0), 0);
}

function normalizePreview(preview: string) {
  return preview.trim() || "Tin nhắn mới";
}

function visibleChatConversations(conversations: ChatConversation[], admin: boolean) {
  return admin ? conversations.filter((c) => !c.supportConversation) : conversations;
}

export function ChatNotificationProvider({ children, suppress = false }: { children: ReactNode; suppress?: boolean }) {
  const { user } = useAuth();
  // `key` biến danh tính thành ranh giới vòng đời thật: đổi admin ↔ nhân viên sẽ tháo toàn bộ
  // state, subscription và callback toast của phiên trước trước khi dựng phiên mới.
  return (
    <AccountBoundChatNotificationProvider key={user?.id ?? "signed-out"} user={user} suppress={suppress}>
      {children}
    </AccountBoundChatNotificationProvider>
  );
}

function AccountBoundChatNotificationProvider({
  children,
  suppress,
  user,
}: {
  children: ReactNode;
  suppress: boolean;
  user: User | null;
}) {
  const admin = user?.role?.toLowerCase() === "admin";
  const navigate = useNavigate();
  const location = useLocation();
  const [conversations, setConversations] = useState<ChatConversation[]>([]);
  const [toast, setToast] = useState<ChatToast | null>(null);
  // Cờ "đọc trước nội dung tin nhắn" nằm ở localStorage — một NGUỒN NGOÀI React. Đọc bằng
  // useSyncExternalStore thay vì useState + useEffect(setState): React tự đăng ký / huỷ đăng ký và đọc
  // lại đúng lúc render, nên không còn cảnh "render một nhịp bằng giá trị cũ rồi mới setState sửa lại"
  // (chính là lỗi set-state-in-effect). Đúng cách đã dùng ở WaterReminderPopup và EyeReminderPopup.
  const userId = user?.id ?? null;
  const previewEnabled = useSyncExternalStore(
    useCallback(
      (onChange: () => void) => (userId ? subscribeMessagePreviewEnabled(userId, onChange) : () => {}),
      [userId],
    ),
    () => (userId ? isMessagePreviewEnabled(userId) : true),
  );
  const conversationsRef = useRef<ChatConversation[]>([]);
  const firstLoadRef = useRef(true);
  const previewEnabledRef = useRef(previewEnabled);
  const loadVersionRef = useRef(0);

  useEffect(() => () => {
    // Promise của instance vừa bị tháo không được phép tiếp tục chạm state, kể cả khi fetch không
    // hỗ trợ hủy. Việc remount theo key ở trên đồng thời bảo đảm toast/hội thoại cũ biến mất ngay.
    loadVersionRef.current += 1;
  }, []);

  useEffect(() => {
    conversationsRef.current = conversations;
  }, [conversations]);

  useEffect(() => {
    previewEnabledRef.current = previewEnabled;
  }, [previewEnabled]);

  const showToastForConversation = useCallback((conversation: ChatConversation) => {
    const body = previewEnabledRef.current ? normalizePreview(conversation.preview) : "Bạn có thông báo mới";
    setToast({
      id: Date.now(),
      conversationId: conversation.id,
      title: conversation.title || "Tin nhắn mới",
      body,
      avatarUrl: conversation.avatarUrl,
    });
  }, []);

  const loadConversations = useCallback(
    async ({ notify = false, payload }: { notify?: boolean; payload?: string } = {}) => {
      if (!user) return;
      const requestedUserId = user.id;
      const loadVersion = ++loadVersionRef.current;
      const allNext = await api.get<ChatConversation[]>("/api/chat/conversations");
      // Kết quả chỉ thuộc đúng danh tính đã khởi tạo request và phải là lần tải mới nhất. Điều này
      // chặn cả đổi admin → nhân viên lẫn hai lần refresh hoàn tất ngược thứ tự khi mạng chậm.
      if (user.id !== requestedUserId || loadVersionRef.current !== loadVersion) return;
      const next = visibleChatConversations(allNext, admin);
      const previous = conversationsRef.current;
      conversationsRef.current = next;
      setConversations(next);

      if (!notify || firstLoadRef.current) {
        firstLoadRef.current = false;
        return;
      }

      const previousById = new Map(previous.map((c) => [c.id, c]));
      const candidates = payload ? next.filter((c) => c.id === payload) : next;
      const newestIncoming = candidates.find((c) => {
        const before = previousById.get(c.id);
        return (c.unread || 0) > (before?.unread || 0) && c.lastAt !== before?.lastAt;
      });
      if (newestIncoming) showToastForConversation(newestIncoming);
    },
    [admin, showToastForConversation, user],
  );

  useEffect(() => {
    if (!user) {
      conversationsRef.current = [];
      return;
    }

    firstLoadRef.current = true;
    // BÁO NHẦM: `loadConversations` là hàm async và mọi setState trong đó đều nằm SAU
    // `await api.get(...)`, nên không có setState nào chạy đồng bộ trong thân effect. Luật chỉ thấy
    // "hàm này có gọi setState" chứ không lần được tới chỗ await.
    void loadConversations();

    const unsubscribeRealtime = subscribeRealtime((_, payload) => {
      void loadConversations({ notify: true, payload });
    }, ["chat"]);

    const onFocus = () => void loadConversations();
    window.addEventListener("focus", onFocus);

    return () => {
      unsubscribeRealtime();
      window.removeEventListener("focus", onFocus);
    };
  }, [loadConversations, user]);

  useEffect(() => {
    // BÁO NHẦM, cùng lý do như trên: setState trong `loadConversations` nằm sau `await`, không chạy
    // đồng bộ trong thân effect.
    if (location.pathname.startsWith("/chats")) void loadConversations();
  }, [loadConversations, location.pathname]);

  const value = useMemo(
    () => ({
      unreadCount: totalUnread(conversations),
      conversations,
      reload: (silent = true) => void loadConversations({ notify: !silent }),
    }),
    [conversations, loadConversations],
  );

  return (
    <ChatNotificationContext.Provider value={value}>
      {children}
      {/* Trên trang module nhân sự (suppress) chỉ giữ context (để sidebar/badge hoạt động và
          KHÔNG remount layout), nhưng ẩn phần nổi: lời nhắc gửi tệp + toast tin nhắn. */}
      {!suppress && <FileTransferPrompts />}
      {!suppress && (
        <div className="km-chat-toast-host" aria-live="polite">
          {toast && (
            <ChatMessageToast
              key={toast.id}
              toast={toast}
              onClose={() => setToast(null)}
              onOpen={() => {
                setToast(null);
                navigate(`/chats?conversation=${encodeURIComponent(toast.conversationId)}`);
              }}
            />
          )}
        </div>
      )}
    </ChatNotificationContext.Provider>
  );
}

function ChatMessageToast({
  toast,
  onClose,
  onOpen,
}: {
  toast: ChatToast;
  onClose: () => void;
  onOpen: () => void;
}) {
  const onCloseRef = useRef(onClose);

  useEffect(() => {
    onCloseRef.current = onClose;
  });

  useEffect(() => {
    const id = window.setTimeout(() => onCloseRef.current(), TOAST_DURATION_MS);
    return () => window.clearTimeout(id);
  }, [toast.id]);

  return (
    <button className="km-chat-toast" type="button" onClick={onOpen}>
      <span className="km-chat-toast-avatar">
        {toast.avatarUrl ? <img src={toast.avatarUrl} alt="" /> : initials(toast.title)}
      </span>
      <span className="km-chat-toast-body">
        <span className="km-chat-toast-kicker">
          <MessageCircle className="h-3.5 w-3.5" /> Tin nhắn mới
        </span>
        <span className="km-chat-toast-title">{toast.title}</span>
        <span className="km-chat-toast-message">{toast.body}</span>
        <span className="km-chat-toast-bars" aria-hidden="true">
          <span />
        </span>
      </span>
    </button>
  );
}
