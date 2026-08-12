import { useState, type ReactNode } from "react";
import { AlertTriangle, X } from "lucide-react";
import "./confirm-dialog.css";

export type ConfirmDialogTone = "danger" | "warning" | "info";

export interface ConfirmDialogProps {
  open: boolean;
  title: string;
  description: ReactNode;
  detail?: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  tone?: ConfirmDialogTone;
  icon?: ReactNode;
  busyLabel?: string;
  confirmDisabled?: boolean;
  onClose: () => void;
  onConfirm: () => Promise<void> | void;
}

export function ConfirmDialog({
  open,
  title,
  description,
  detail,
  confirmLabel = "Xác nhận",
  cancelLabel = "Hủy",
  tone = "danger",
  icon,
  busyLabel = "Đang xử lý...",
  confirmDisabled = false,
  onClose,
  onConfirm,
}: ConfirmDialogProps) {
  const [busy, setBusy] = useState(false);

  // Quên cờ "đang xử lý" mỗi khi hộp thoại đóng. Gán lúc render thay vì trong useEffect: effect
  // tốn thêm một vòng render, và React Compiler cấm setState đồng bộ trong effect. Mốc `busySeenOpen`
  // chỉ cho reset đúng lúc `open` ĐỔI (không phải mỗi lần render) nên không đè lên lần bấm đang chạy.
  const [busySeenOpen, setBusySeenOpen] = useState(open);
  if (busySeenOpen !== open) {
    setBusySeenOpen(open);
    if (!open) setBusy(false);
  }

  if (!open) return null;

  const confirm = async () => {
    if (busy) return;
    setBusy(true);
    try {
      await onConfirm();
      onClose();
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="confirm-dialog-backdrop" role="presentation">
      <div className="confirm-dialog-card" role="dialog" aria-modal="true" aria-labelledby="confirm-dialog-title">
        <button className="confirm-dialog-close" type="button" onClick={onClose} aria-label="Đóng" disabled={busy}>
          <X className="h-4 w-4" />
        </button>
        <div className="confirm-dialog-icon" data-tone={tone} aria-hidden="true">
          {icon ?? <AlertTriangle className="h-6 w-6" />}
        </div>
        <div className="confirm-dialog-copy">
          <h2 id="confirm-dialog-title">{title}</h2>
          <p>{description}</p>
          {detail && <div className="confirm-dialog-detail" data-tone={tone}>{detail}</div>}
        </div>
        <div className="confirm-dialog-actions">
          <button className="confirm-dialog-btn" type="button" onClick={onClose} disabled={busy}>
            {cancelLabel}
          </button>
          <button
            className="confirm-dialog-btn confirm-dialog-btn--primary"
            data-tone={tone}
            type="button"
            onClick={confirm}
            disabled={busy || confirmDisabled}
          >
            {busy ? busyLabel : confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
