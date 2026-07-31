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
  fullScreen,
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
  /** Dùng gần toàn bộ viewport, phù hợp cho xem trước tài liệu dài. */
  fullScreen?: boolean;
}) {
  if (!open) return null;

  const sizeClass = fullScreen
    ? "h-[calc(100vh-1rem)] max-w-none"
    : wide
      ? "max-w-4xl"
      : "max-w-lg";

  const inner = (
    <div
      onClick={(e) => e.stopPropagation()}
      className={`flex min-h-0 flex-col ${fullScreen ? "h-full" : "max-h-[90vh]"}`}
    >
      <div
        className={`flex items-center justify-between border-b border-[var(--glass-border)] ${
          fullScreen ? "px-4 py-3" : "px-6 py-4"
        }`}
      >
        <h2 className="text-lg font-bold text-[var(--text)]">{title}</h2>
        <button
          onClick={onClose}
          className="rounded-lg p-1.5 text-[var(--text-muted)] transition-colors hover:bg-black/5 dark:hover:bg-white/10"
        >
          <X className="h-5 w-5" />
        </button>
      </div>
      <div
        className={
          fullScreen
            ? "min-h-0 flex-1 overflow-hidden px-3 py-3 sm:px-4"
            : "scroll-thin overflow-y-auto px-6 py-5"
        }
      >
        {children}
      </div>
      {footer && (
        <div
          className={`flex justify-end gap-2 border-t border-[var(--glass-border)] ${
            fullScreen ? "px-4 py-3" : "px-6 py-4"
          }`}
        >
          {footer}
        </div>
      )}
    </div>
  );

  return (
    <div
      className={`fixed inset-0 z-50 flex items-center justify-center ${fullScreen ? "p-2" : "p-4"}`}
      style={{ background: "rgba(8,12,20,0.45)", backdropFilter: "blur(4px)" }}
      onClick={onClose}
    >
      {panel ? (
        <GlassPanel
          strong
          className={`gc-root fade-in flex w-full flex-col overflow-hidden ${
            fullScreen ? "max-h-none" : "max-h-[90vh]"
          } ${sizeClass} ${className ?? ""}`}
        >
          {inner}
        </GlassPanel>
      ) : (
        <GlassCard
          strong
          glow={false}
          className={`fade-in flex w-full flex-col overflow-hidden ${
            fullScreen ? "max-h-none" : "max-h-[90vh]"
          } ${
            solid ? "modal-solid-surface" : ""
          } ${sizeClass} ${className ?? ""}`}
        >
          {inner}
        </GlassCard>
      )}
    </div>
  );
}
