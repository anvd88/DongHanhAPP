import { useCallback, useMemo, useRef, useState, type ReactNode } from "react";
import { AlertCircle, CheckCircle2, Info, X } from "lucide-react";
import { ConfirmDialog } from "./ConfirmDialog";
import {
  NotificationsContext,
  type ConfirmInput,
  type NotificationsApi,
  type ToastInput,
  type ToastTone,
} from "./app-notifications-context";
import "./app-notifications.css";

type Toast = Required<Pick<ToastInput, "tone" | "duration">> & {
  id: number;
  title: string;
  message: ReactNode;
};

type PendingConfirm = Required<Pick<ConfirmInput, "title" | "confirmLabel" | "cancelLabel" | "tone">> &
  Pick<ConfirmInput, "description" | "detail"> & {
    resolve: (value: boolean) => void;
  };

const toneTitle: Record<ToastTone, string> = {
  success: "Thành công",
  error: "Có lỗi xảy ra",
  info: "Thông báo",
  warning: "Cần chú ý",
};

const ToastIcon = ({ tone }: { tone: ToastTone }) => {
  if (tone === "success") return <CheckCircle2 className="h-5 w-5" />;
  if (tone === "error" || tone === "warning") return <AlertCircle className="h-5 w-5" />;
  return <Info className="h-5 w-5" />;
};

export function AppNotificationProvider({ children }: { children: ReactNode }) {
  const nextId = useRef(1);
  const generationRef = useRef(0);
  const [generation, setGeneration] = useState(0);
  const [toasts, setToasts] = useState<Toast[]>([]);
  const [pendingConfirm, setPendingConfirm] = useState<PendingConfirm | null>(null);

  const dismiss = useCallback((id: number) => {
    setToasts((items) => items.filter((item) => item.id !== id));
  }, []);

  const enqueueToast = useCallback((input: ToastInput) => {
    const tone = input.tone ?? "info";
    const toast: Toast = {
      id: nextId.current++,
      title: input.title ?? toneTitle[tone],
      message: input.message,
      tone,
      duration: input.duration ?? (tone === "error" ? 7000 : 4500),
    };
    setToasts((items) => [...items.slice(-3), toast]);
    window.setTimeout(() => dismiss(toast.id), toast.duration);
  }, [dismiss]);

  const openConfirm = useCallback((input: ConfirmInput) => {
    return new Promise<boolean>((resolve) => {
      setPendingConfirm({
        title: input.title ?? "Xác nhận thao tác",
        description: input.description,
        detail: input.detail,
        confirmLabel: input.confirmLabel ?? "Xác nhận",
        cancelLabel: input.cancelLabel ?? "Hủy",
        tone: input.tone ?? "danger",
        resolve,
      });
    });
  }, []);

  const closeConfirm = useCallback((value: boolean) => {
    setPendingConfirm((current) => {
      current?.resolve(value);
      return null;
    });
  }, []);

  const clear = useCallback(() => {
    // Tăng generation trước khi setState: callback async của phiên cũ bị vô hiệu ngay cả trong
    // cùng microtask, trước khi React kịp render context mới cho tài khoản kế tiếp.
    generationRef.current += 1;
    setGeneration(generationRef.current);
    setToasts([]);
    setPendingConfirm((current) => {
      // Một hộp xác nhận thuộc phiên cũ không được treo Promise hoặc xuất hiện cho tài khoản mới.
      current?.resolve(false);
      return null;
    });
  }, []);

  const value = useMemo<NotificationsApi>(() => {
    // Mỗi consumer giữ các hàm gắn với generation tại lúc render. Promise/event handler của admin
    // hoàn tất sau khi đã đổi sang nhân viên vẫn cầm generation cũ và bị bỏ, không thể dựng toast mới.
    const show = (input: ToastInput) => {
      if (generationRef.current !== generation) return;
      enqueueToast(input);
    };
    const confirm = (input: ConfirmInput) => {
      if (generationRef.current !== generation) return Promise.resolve(false);
      return openConfirm(input);
    };
    return {
      notify: {
        show,
        success: (message, title) => show({ message, title, tone: "success" }),
        error: (message, title) => show({ message, title, tone: "error" }),
        info: (message, title) => show({ message, title, tone: "info" }),
        warning: (message, title) => show({ message, title, tone: "warning" }),
      },
      confirm,
      clear,
    };
  }, [clear, enqueueToast, generation, openConfirm]);

  return (
    <NotificationsContext.Provider value={value}>
      {children}
      <div className="app-toast-host" aria-live="polite" aria-relevant="additions">
        {toasts.map((toast) => (
          <div className="app-toast" data-tone={toast.tone} key={toast.id} role="status">
            <div className="app-toast-icon" aria-hidden="true">
              <ToastIcon tone={toast.tone} />
            </div>
            <div className="app-toast-copy">
              <div className="app-toast-title">{toast.title}</div>
              <div className="app-toast-message">{toast.message}</div>
            </div>
            <button className="app-toast-close" type="button" onClick={() => dismiss(toast.id)} aria-label="Đóng thông báo">
              <X className="h-4 w-4" />
            </button>
          </div>
        ))}
      </div>
      {pendingConfirm && (
        <ConfirmDialog
          open
          title={pendingConfirm.title}
          description={pendingConfirm.description}
          detail={pendingConfirm.detail}
          confirmLabel={pendingConfirm.confirmLabel}
          cancelLabel={pendingConfirm.cancelLabel}
          tone={pendingConfirm.tone}
          onClose={() => closeConfirm(false)}
          onConfirm={() => closeConfirm(true)}
        />
      )}
    </NotificationsContext.Provider>
  );
}
