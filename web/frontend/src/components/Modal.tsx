import { type ReactNode } from "react";
import { X } from "lucide-react";
import { GlassCard } from "./Glass";
import { GlassPanel } from "./glass/GlassPanel";
import "../features/giacong/giacong.css";

export function Modal({
  open,
  onClose,
  title,
  children,
  footer,
  wide,
  solid,
  panel,
  className,
}: {
  open: boolean;
  onClose: () => void;
  title: string;
  children: ReactNode;
  footer?: ReactNode;
  wide?: boolean;
  solid?: boolean;
  /** Dùng bề mặt GlassPanel (gc-panel) đặc hơn → chữ rõ hơn, nhất là ở chế độ tối. */
  panel?: boolean;
  /** Lớp CSS bổ sung cho bề mặt kính (tùy chỉnh nền/độ đục theo từng dialog). */
  className?: string;
}) {
  if (!open) return null;

  const sizeClass = wide ? "max-w-4xl" : "max-w-lg";

  const inner = (
    <div onClick={(e) => e.stopPropagation()} className="flex max-h-[90vh] flex-col">
      <div className="flex items-center justify-between border-b border-[var(--glass-border)] px-6 py-4">
        <h2 className="text-lg font-bold text-[var(--text)]">{title}</h2>
        <button
          onClick={onClose}
          className="rounded-lg p-1.5 text-[var(--text-muted)] transition-colors hover:bg-black/5 dark:hover:bg-white/10"
        >
          <X className="h-5 w-5" />
        </button>
      </div>
      <div className="scroll-thin overflow-y-auto px-6 py-5">{children}</div>
      {footer && (
        <div className="flex justify-end gap-2 border-t border-[var(--glass-border)] px-6 py-4">{footer}</div>
      )}
    </div>
  );

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      style={{ background: "rgba(8,12,20,0.45)", backdropFilter: "blur(4px)" }}
      onClick={onClose}
    >
      {panel ? (
        <GlassPanel
          strong
          className={`gc-root fade-in flex max-h-[90vh] w-full flex-col overflow-hidden ${sizeClass} ${className ?? ""}`}
        >
          {inner}
        </GlassPanel>
      ) : (
        <GlassCard
          strong
          glow={false}
          className={`fade-in flex max-h-[90vh] w-full flex-col overflow-hidden ${
            solid ? "modal-solid-surface" : ""
          } ${sizeClass}`}
        >
          {inner}
        </GlassCard>
      )}
    </div>
  );
}
