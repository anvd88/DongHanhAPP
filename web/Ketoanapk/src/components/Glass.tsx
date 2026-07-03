import { useRef, type ReactNode, type CSSProperties, type MouseEvent } from "react";

/** Hook tái hiện hiệu ứng glow bám theo con trỏ của LiquidGlassBorder (bản desktop). */
export function useGlow() {
  const ref = useRef<HTMLDivElement>(null);
  const onMouseMove = (e: MouseEvent<HTMLDivElement>) => {
    const el = ref.current;
    if (!el) return;
    const r = el.getBoundingClientRect();
    el.style.setProperty("--mx", `${((e.clientX - r.left) / r.width) * 100}%`);
    el.style.setProperty("--my", `${((e.clientY - r.top) / r.height) * 100}%`);
  };
  return { ref, onMouseMove };
}

interface GlassCardProps {
  children: ReactNode;
  className?: string;
  glow?: boolean;
  strong?: boolean;
  style?: CSSProperties;
  onClick?: () => void;
}

/** Tấm kính cơ bản. glow=true bật viền sáng bám theo chuột. */
export function GlassCard({ children, className = "", glow = true, strong, style, onClick }: GlassCardProps) {
  const { ref, onMouseMove } = useGlow();
  return (
    <div
      ref={ref}
      onMouseMove={glow ? onMouseMove : undefined}
      onClick={onClick}
      style={style}
      className={`glass ${strong ? "glass-strong" : ""} ${glow ? "glass-glow" : ""} ${
        onClick ? "cursor-pointer" : ""
      } ${className}`}
    >
      {children}
    </div>
  );
}
