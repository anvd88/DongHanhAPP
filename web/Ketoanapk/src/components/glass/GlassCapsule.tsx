import { forwardRef, type HTMLAttributes } from "react";
import { cn } from "../../lib/cn";

/** Pill kính cho các control nhỏ (ô tìm kiếm, nhóm nút...). */
export const GlassCapsule = forwardRef<HTMLDivElement, HTMLAttributes<HTMLDivElement>>(
  ({ className, children, ...rest }, ref) => (
    <div ref={ref} className={cn("gc-capsule", className)} {...rest}>
      {children}
    </div>
  ),
);
GlassCapsule.displayName = "GlassCapsule";
