import { useCallback, useEffect, useRef, type MouseEvent } from "react";

/**
 * Hook tái hiện hiệu ứng glow bám theo con trỏ của LiquidGlassBorder (bản desktop).
 *
 * Tách khỏi Glass.tsx để file đó chỉ còn export COMPONENT — điều kiện để Fast Refresh của Vite hoán đổi
 * nóng được thay vì tải lại cả trang mỗi lần sửa.
 */
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
