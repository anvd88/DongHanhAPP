import { useCallback, useEffect, useRef, type ReactNode, type CSSProperties, type MouseEvent } from "react";

/** Hook tái hiện hiệu ứng glow bám theo con trỏ của LiquidGlassBorder (bản desktop). */
export function useGlow() {
  const ref = useRef<HTMLDivElement>(null);
  const frame = useRef(0);
  const next = useRef<{ x: number; y: number } | null>(null);

  const flush = useCallback(() => {
    frame.current = 0;
    const el = ref.current;
    const position = next.current;
    if (!el || !position) return;
    el.style.setProperty("--mx", `${position.x}%`);
    el.style.setProperty("--my", `${position.y}%`);
  }, []);

  const onMouseMove = useCallback((e: MouseEvent<HTMLDivElement>) => {
    const el = ref.current;
    if (!el) return;
    const r = el.getBoundingClientRect();
    next.current = {
      x: ((e.clientX - r.left) / r.width) * 100,
      y: ((e.clientY - r.top) / r.height) * 100,
    };
    if (!frame.current) frame.current = window.requestAnimationFrame(flush);
  }, [flush]);

  useEffect(
    () => () => {
      if (frame.current) window.cancelAnimationFrame(frame.current);
    },
    [],
  );

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
