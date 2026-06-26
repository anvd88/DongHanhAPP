import { type ReactNode } from "react";
import { X } from "lucide-react";
import { GlassCard } from "./Glass";

export function Modal({
  open,
  onClose,
  title,
  children,
  footer,
  wide,
  solid,
}: {
  open: boolean;
  onClose: () => void;
  title: string;
  children: ReactNode;
  footer?: ReactNode;
  wide?: boolean;
  solid?: boolean;
}) {
  if (!open) return null;
  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      style={{ background: "rgba(8,12,20,0.45)", backdropFilter: "blur(4px)" }}
      onClick={onClose}
    >
      <GlassCard
        strong
        glow={false}
        className={`fade-in flex max-h-[90vh] w-full flex-col overflow-hidden ${
          solid ? "modal-solid-surface" : ""
        } ${wide ? "max-w-4xl" : "max-w-lg"}`}
      >
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
      </GlassCard>
    </div>
  );
}
