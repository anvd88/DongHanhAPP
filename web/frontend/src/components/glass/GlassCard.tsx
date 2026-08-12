import { motion, type HTMLMotionProps } from "motion/react";
import type { ReactNode } from "react";
import { cn } from "../../lib/cn";
import { usePointerGlow } from "../../hooks/usePointerGlow";

export interface GlassCardProps extends Omit<HTMLMotionProps<"div">, "children"> {
  children?: ReactNode;
  /** Bật nâng nhẹ + scale khi hover (mặc định bật). */
  interactive?: boolean;
}

const enterpriseSpring = { type: "spring", stiffness: 420, damping: 34, mass: 0.7 } as const;

/**
 * Thẻ kính có ánh sáng bám theo con trỏ (ghi CSS var qua ref, không re-render),
 * hiệu ứng nâng + dải phản xạ kính (sheen) lướt qua khi hover bằng Motion + CSS.
 */
export function GlassCard({ className, interactive = true, children, ...rest }: GlassCardProps) {
  const { ref, onPointerMove, onPointerLeave } = usePointerGlow<HTMLDivElement>();
  return (
    <motion.div
      ref={ref}
      onPointerMove={onPointerMove}
      onPointerLeave={onPointerLeave}
      whileHover={interactive ? { y: -2 } : undefined}
      whileTap={interactive ? { y: 0, scale: 0.99 } : undefined}
      transition={enterpriseSpring}
      className={cn("gc-card", className)}
      {...rest}
    >
      <span className="gc-glass-sheen" aria-hidden="true" />
      {children}
    </motion.div>
  );
}
