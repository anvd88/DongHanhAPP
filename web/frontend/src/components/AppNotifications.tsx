import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from "react";
import { AlertCircle, CheckCircle2, Info, X } from "lucide-react";
import { ConfirmDialog, type ConfirmDialogTone } from "./ConfirmDialog";
import "./app-notifications.css";

type ToastTone = "success" | "error" | "info" | "warning";

type ToastInput = {
  title?: string;
  message: ReactNode;
  tone?: ToastTone;
  duration?: number;
};

type Toast = Required<Pick<ToastInput, "tone" | "duration">> & {
  id: number;
  title: string;
  message: ReactNode;
};

type ConfirmInput = {
  title?: string;
  description: ReactNode;
  detail?: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  tone?: ConfirmDialogTone;
};

type PendingConfirm = Required<Pick<ConfirmInput, "title" | "confirmLabel" | "cancelLabel" | "tone">> &
  Pick<ConfirmInput, "description" | "detail"> & {
    resolve: (value: boolean) => void;
  };

type NotificationsApi = {
  notify: {
    show: (input: ToastInput) => void;
    success: (message: ReactNode, title?: string) => void;
    error: (message: ReactNode, title?: string) => void;
    info: (message: ReactNode, title?: string) => void;
    warning: (message: ReactNode, title?: string) => void;
  };
  confirm: (input: ConfirmInput) => Promise<boolean>;
};

const NotificationsContext = createContext<NotificationsApi | null>(null);

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
  const [toasts, setToasts] = useState<Toast[]>([]);
  const [pendingConfirm, setPendingConfirm] = useState<PendingConfirm | null>(null);

  const dismiss = useCallback((id: number) => {
    setToasts((items) => items.filter((item) => item.id !== id));
  }, []);

  const show = useCallback((input: ToastInput) => {
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

  const confirm = useCallback((input: ConfirmInput) => {
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

  const value = useMemo<NotificationsApi>(() => ({
    notify: {
      show,
      success: (message, title) => show({ message, title, tone: "success" }),
      error: (message, title) => show({ message, title, tone: "error" }),
      info: (message, title) => show({ message, title, tone: "info" }),
      warning: (message, title) => show({ message, title, tone: "warning" }),
    },
    confirm,
  }), [confirm, show]);

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

export function useAppNotifications() {
  const context = useContext(NotificationsContext);
  if (!context) throw new Error("useAppNotifications must be used inside AppNotificationProvider");
  return context;
}
