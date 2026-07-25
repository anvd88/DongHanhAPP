import { type ReactNode, type CSSProperties } from "react";
import { useGlow } from "./useGlow";

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
