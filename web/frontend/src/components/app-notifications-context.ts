import { createContext, useContext, type ReactNode } from "react";
import type { ConfirmDialogTone } from "./ConfirmDialog";

/**
 * Kiểu + Context + hook của hệ thống thông báo, tách khỏi AppNotifications.tsx để file đó chỉ còn export
 * COMPONENT. Đó là điều kiện để Fast Refresh của Vite hoán đổi nóng được: file vừa export component vừa
 * export hook thì mỗi lần sửa sẽ tải lại cả trang thay vì giữ nguyên state.
 */
export type ToastTone = "success" | "error" | "info" | "warning";

export type ToastInput = {
  title?: string;
  message: ReactNode;
  tone?: ToastTone;
  duration?: number;
};

export type ConfirmInput = {
  title?: string;
  description: ReactNode;
  detail?: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  tone?: ConfirmDialogTone;
};

export type NotificationsApi = {
  notify: {
    show: (input: ToastInput) => void;
    success: (message: ReactNode, title?: string) => void;
    error: (message: ReactNode, title?: string) => void;
    info: (message: ReactNode, title?: string) => void;
    warning: (message: ReactNode, title?: string) => void;
  };
  confirm: (input: ConfirmInput) => Promise<boolean>;
  /** Xóa toàn bộ nội dung nổi khi kết thúc/đổi phiên để tài khoản sau không thấy dữ liệu tài khoản trước. */
  clear: () => void;
};

export const NotificationsContext = createContext<NotificationsApi | null>(null);

export function useAppNotifications() {
  const context = useContext(NotificationsContext);
  if (!context) throw new Error("useAppNotifications must be used inside AppNotificationProvider");
  return context;
}
