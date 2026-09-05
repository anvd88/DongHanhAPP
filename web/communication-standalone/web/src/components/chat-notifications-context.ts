import { createContext, useContext } from "react";
import type { ChatConversation } from "../lib/types";

/**
 * Context + hook của thông báo chat, tách khỏi ChatNotifications.tsx để file đó chỉ còn export
 * COMPONENT (điều kiện để Fast Refresh của Vite hoán đổi nóng thay vì tải lại cả trang).
 */
export type ChatNotificationContextValue = {
  unreadCount: number;
  conversations: ChatConversation[];
  reload: (silent?: boolean) => void;
};

export const ChatNotificationContext = createContext<ChatNotificationContextValue | null>(null);

/** Ngoài provider thì trả giá trị rỗng — có màn (vd trang công khai) không bọc provider. */
export function useChatNotifications() {
  return useContext(ChatNotificationContext) ?? { unreadCount: 0, conversations: [], reload: () => {} };
}
