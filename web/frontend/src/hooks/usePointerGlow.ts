import { useCallback, useEffect, useRef } from "react";

/**
 * Theo dõi vị trí con trỏ trong phần tử và ghi thẳng vào CSS variables
 * `--mouse-x` / `--mouse-y` qua ref — KHÔNG setState nên không gây re-render.
 * Việc ghi được gom vào một khung hình (requestAnimationFrame) để tối đa ~60fps.
 *
 * Dùng cho lớp ánh sáng radial-gradient bám theo chuột (Liquid Glass).
 */
export function usePointerGlow<T extends HTMLElement = HTMLDivElement>() {
  const ref = useRef<T>(null);
  const frame = useRef(0);
  const next = useRef<{ x: number; y: number } | null>(null);

  const flush = useCallback(() => {
    frame.current = 0;
    const el = ref.current;
    const pos = next.current;
    if (!el || !pos) return;
    el.style.setProperty("--mouse-x", `${pos.x}%`);
    el.style.setProperty("--mouse-y", `${pos.y}%`);
  }, []);

  const onPointerMove = useCallback(
    (event: React.PointerEvent<T>) => {
      const el = ref.current;
      if (!el) return;
      const rect = el.getBoundingClientRect();
      next.current = {
        x: ((event.clientX - rect.left) / rect.width) * 100,
        y: ((event.clientY - rect.top) / rect.height) * 100,
      };
      if (!frame.current) frame.current = window.requestAnimationFrame(flush);
    },
    [flush],
  );

  const onPointerLeave = useCallback(() => {
    const el = ref.current;
    if (!el) return;
    if (frame.current) {
      window.cancelAnimationFrame(frame.current);
      frame.current = 0;
    }
    el.style.setProperty("--mouse-x", "50%");
    el.style.setProperty("--mouse-y", "50%");
  }, []);

  useEffect(
    () => () => {
      if (frame.current) window.cancelAnimationFrame(frame.current);
    },
    [],
  );

  return { ref, onPointerMove, onPointerLeave };
}
