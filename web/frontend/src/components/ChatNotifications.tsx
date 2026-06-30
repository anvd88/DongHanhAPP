import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { MessageCircle } from "lucide-react";
import { useLocation, useNavigate } from "react-router-dom";
import { api } from "../lib/api";
import { useAuth } from "../lib/auth";
import { initials } from "../lib/format";
import { isMessagePreviewEnabled, subscribeMessagePreviewEnabled } from "../lib/messagePreviewPreference";
import { subscribeRealtime } from "../lib/realtime";
import type { ChatConversation } from "../lib/types";

type ChatToast = {
  id: number;
  conversationId: string;
  title: string;
  body: string;
  avatarUrl?: string | null;
};

type ChatNotificationContextValue = {
  unreadCount: number;
  conversations: ChatConversation[];
  reload: (silent?: boolean) => void;
};

const ChatNotificationContext = createContext<ChatNotificationContextValue | null>(null);
const TOAST_DURATION_MS = 6500;

export function useChatNotifications() {
  return useContext(ChatNotificationContext) ?? { unreadCount: 0, conversations: [], reload: () => {} };
}

function totalUnread(conversations: ChatConversation[]) {
  return conversations.reduce((sum, c) => sum + Math.max(0, c.unread || 0), 0);
}

function normalizePreview(preview: string) {
  return preview.trim() || "Tin nhắn mới";
}

export function ChatNotificationProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [conversations, setConversations] = useState<ChatConversation[]>([]);
  const [toast, setToast] = useState<ChatToast | null>(null);
  const [previewEnabled, setPreviewEnabled] = useState(() => (user ? isMessagePreviewEnabled(user.id) : true));
  const conversationsRef = useRef<ChatConversation[]>([]);
  const firstLoadRef = useRef(true);
  const previewEnabledRef = useRef(previewEnabled);

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
      const next = await api.get<ChatConversation[]>("/api/chat/conversations");
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
    [showToastForConversation, user],
  );

  useEffect(() => {
    if (!user) {
      conversationsRef.current = [];
      setConversations([]);
      setToast(null);
      return;
    }

    firstLoadRef.current = true;
    setPreviewEnabled(isMessagePreviewEnabled(user.id));
    void loadConversations();

    const unsubscribeRealtime = subscribeRealtime((_, payload) => {
      void loadConversations({ notify: true, payload });
    }, ["chat"]);
    const unsubscribePreview = subscribeMessagePreviewEnabled(user.id, () => {
      setPreviewEnabled(isMessagePreviewEnabled(user.id));
    });

    const onFocus = () => void loadConversations();
    window.addEventListener("focus", onFocus);

    return () => {
      unsubscribeRealtime();
      unsubscribePreview();
      window.removeEventListener("focus", onFocus);
    };
  }, [loadConversations, user]);

  useEffect(() => {
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
