import { forwardRef, type HTMLAttributes } from "react";
import { cn } from "../../lib/cn";

export interface GlassPanelProps extends HTMLAttributes<HTMLDivElement> {
  /** Nền kính đậm hơn (dùng cho panel chứa bảng dữ liệu). */
  strong?: boolean;
  /** Bỏ highlight phản chiếu cạnh trên. */
  flat?: boolean;
}

/** Tấm kính dùng chung: nền bán trong suốt, blur, viền mảnh, highlight + shadow. */
export const GlassPanel = forwardRef<HTMLDivElement, GlassPanelProps>(
  ({ className, strong, flat, children, ...rest }, ref) => (
    <div
      ref={ref}
      className={cn("gc-panel", strong && "gc-panel--strong", flat && "gc-panel--flat", className)}
      {...rest}
    >
      {children}
    </div>
  ),
);
GlassPanel.displayName = "GlassPanel";
