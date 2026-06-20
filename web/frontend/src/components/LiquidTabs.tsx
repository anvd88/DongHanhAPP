import { useCallback, useEffect, useLayoutEffect, useRef } from "react";

export interface LiquidTab {
  key: string;
  label: string;
}

/**
 * Thanh tab phong cách Liquid Glass với thanh chọn "giọt nước".
 *
 * - Một lớp kính chọn DUY NHẤT (indicator) nằm sau chữ; chữ và icon không bị biến dạng.
 * - Chuyển tab bằng spring 2 mép (mép dẫn hướng cứng hơn, mép theo sau mềm hơn) → kéo giãn
 *   rồi co lại đàn hồi như chất lỏng. Bấm liên tục thì tiếp nối mượt từ trạng thái hiện tại.
 * - Animation chạy bằng requestAnimationFrame ghi thẳng vào style (transform + width),
 *   KHÔNG setState mỗi frame → không re-render cả thanh tab.
 * - Tự đo vị trí/kích thước thật của tab (ResizeObserver + fonts.ready) nên đúng khi tên tab
 *   dài ngắn khác nhau, resize cửa sổ, hoặc font tải chậm.
 * - Tôn trọng prefers-reduced-motion (nhảy thẳng tới vị trí, bỏ hiệu ứng chất lỏng).
 */
export function LiquidTabs({
  tabs,
  value,
  onChange,
  className = "",
}: {
  tabs: LiquidTab[];
  value: string;
  onChange: (key: string) => void;
  className?: string;
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const indicatorRef = useRef<HTMLSpanElement>(null);
  const tabRefs = useRef<(HTMLButtonElement | null)[]>([]);
  const rects = useRef<{ left: number; width: number }[]>([]);

  // Trạng thái spring (theo 2 mép trái/phải của thanh chọn) — giữ trong ref để không re-render.
  const cur = useRef({ l: 0, r: 0 });
  const vel = useRef({ l: 0, r: 0 });
  const target = useRef({ l: 0, r: 0 });
  const k = useRef({ l: 160, r: 160 });
  const c = useRef({ l: 22, r: 22 });
  const raf = useRef<number | null>(null);
  const tickRef = useRef<(time: number) => void>(() => {});
  const lastT = useRef(0);
  const inited = useRef(false);
  const reduce = useRef(false);

  const activeIdxRef = useRef(0);
  const dragStartX = useRef<number | null>(null);
  const dragging = useRef(false);
  const pointerId = useRef<number | null>(null);
  const previewIdx = useRef<number | null>(null);
  const commitTimer = useRef<number | null>(null);

  const activeIndex = Math.max(0, tabs.findIndex((t) => t.key === value));

  const measure = useCallback(() => {
    rects.current = tabs.map((_, i) => {
      const el = tabRefs.current[i];
      return el ? { left: el.offsetLeft, width: el.offsetWidth } : { left: 0, width: 0 };
    });
  }, [tabs]);

  const draw = useCallback((speed = 0, dir = 0) => {
    const el = indicatorRef.current;
    if (!el) return;
    const cont = containerRef.current;
    const maxR = cont ? cont.scrollWidth : Number.POSITIVE_INFINITY;
    let l = cur.current.l;
    let r = cur.current.r;
    if (l < 0) l = 0; // không vượt ra ngoài khung
    if (r > maxR) r = maxR;
    el.style.transform = `translate3d(${l}px, 0, 0)`;
    el.style.width = `${Math.max(0, r - l)}px`;
    el.style.setProperty("--speed", String(speed));
    el.style.setProperty("--vx", String(dir));
  }, []);

  const tick = useCallback(
    (now: number) => {
      const dt = Math.min((now - lastT.current) / 1000, 1 / 30) || 0;
      lastT.current = now;
      let maxV = 0;
      (["l", "r"] as const).forEach((e) => {
        const a = -k.current[e] * (cur.current[e] - target.current[e]) - c.current[e] * vel.current[e];
        vel.current[e] += a * dt;
        cur.current[e] += vel.current[e] * dt;
        maxV = Math.max(maxV, Math.abs(vel.current[e]));
      });
      const avgV = (vel.current.l + vel.current.r) / 2;
      const dir = Math.max(-1, Math.min(1, avgV / 600)); // hướng ánh sáng
      const speed = Math.min(1, maxV / 1400); // cường độ ánh sáng động
      draw(speed, dir);

      const settled =
        Math.abs(cur.current.l - target.current.l) < 0.25 &&
        Math.abs(cur.current.r - target.current.r) < 0.25 &&
        maxV < 1.5;
      if (settled) {
        cur.current = { l: target.current.l, r: target.current.r };
        vel.current = { l: 0, r: 0 };
        draw(0, 0); // ánh sáng trở về cân bằng
        raf.current = null;
      } else {
        raf.current = requestAnimationFrame((nextTime) => tickRef.current(nextTime));
      }
    },
    [draw]
  );

  useLayoutEffect(() => {
    activeIdxRef.current = activeIndex;
  }, [activeIndex]);

  useLayoutEffect(() => {
    tickRef.current = tick;
  }, [tick]);

  const startSpring = useCallback(() => {
    if (indicatorRef.current) indicatorRef.current.style.transition = "none"; // spring tự ghi mỗi frame
    if (raf.current == null) {
      lastT.current = performance.now();
      raf.current = requestAnimationFrame((nextTime) => tickRef.current(nextTime));
    }
  }, []);

  const moveTo = useCallback(
    (i: number, animate = true) => {
      const m = rects.current[i];
      if (!m) return;
      const tl = m.left;
      const tr = m.left + m.width;
      target.current = { l: tl, r: tr };

      if (!animate) {
        cur.current = { l: tl, r: tr };
        vel.current = { l: 0, r: 0 };
        if (raf.current != null) {
          cancelAnimationFrame(raf.current);
          raf.current = null;
        }
        draw(0, 0);
        return;
      }

      // Giảm chuyển động: trượt nhẹ bằng CSS transition (bỏ hiệu ứng kéo giãn/đàn hồi),
      // vẫn có di chuyển nhưng tối giản — tôn trọng prefers-reduced-motion.
      if (reduce.current) {
        cur.current = { l: tl, r: tr };
        vel.current = { l: 0, r: 0 };
        if (raf.current != null) {
          cancelAnimationFrame(raf.current);
          raf.current = null;
        }
        const el = indicatorRef.current;
        if (el) {
          el.style.transition = "transform 0.22s ease, width 0.22s ease";
          el.style.transform = `translateX(${tl}px)`;
          el.style.width = `${Math.max(0, tr - tl)}px`;
        }
        return;
      }

      // Mép dẫn hướng cứng hơn (tới trước) — mép theo sau mềm hơn nhiều (nhớt, kéo giãn rõ rồi co lại).
      // Giảm chấn thấp (ζ≈0.3) → vượt qua rồi nảy lại nhiều nhịp = kính lỏng nảy rõ.
      // K cao hơn để dao động nhanh, nảy nhiều mà không bị ì.
      const LEAD_K = 220, LEAD_C = 24, TRAIL_K = 118, TRAIL_C = 18;
      const movingRight = (tl + tr) / 2 >= (cur.current.l + cur.current.r) / 2;
      if (movingRight) {
        k.current = { l: TRAIL_K, r: LEAD_K };
        c.current = { l: TRAIL_C, r: LEAD_C };
      } else {
        k.current = { l: LEAD_K, r: TRAIL_K };
        c.current = { l: LEAD_C, r: TRAIL_C };
      }
      startSpring();
    },
    [draw, startSpring]
  );

  const snapToActive = useCallback(() => {
    const m = rects.current[activeIdxRef.current];
    if (!m) return;
    cur.current = { l: m.left, r: m.left + m.width };
    target.current = { l: m.left, r: m.left + m.width };
    vel.current = { l: 0, r: 0 };
    if (raf.current == null) draw(0, 0);
  }, [draw]);

  // Khởi tạo: đo & đặt thanh chọn vào tab đang chọn (không animation lúc mount).
  useLayoutEffect(() => {
    reduce.current = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
    measure();
    const m = rects.current[activeIndex];
    if (m) {
      cur.current = { l: m.left, r: m.left + m.width };
      target.current = { l: m.left, r: m.left + m.width };
      draw(0, 0);
    }
    inited.current = true;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Đổi tab (value thay đổi) → trượt kiểu chất lỏng tới vị trí mới.
  useEffect(() => {
    if (!inited.current) return;
    measure();
    moveTo(activeIndex, true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value]);

  // Đo lại khi resize / font tải xong → đặt lại đúng vị trí (nhảy thẳng, không animation).
  useEffect(() => {
    const cont = containerRef.current;
    if (!cont) return;
    const ro = new ResizeObserver(() => {
      measure();
      snapToActive();
    });
    ro.observe(cont);
    tabRefs.current.forEach((el) => el && ro.observe(el));
    document.fonts?.ready
      .then(() => {
        measure();
        snapToActive();
      })
      .catch(() => {});
    return () => ro.disconnect();
  }, [measure, snapToActive]);

  useEffect(() => () => {
    if (raf.current != null) cancelAnimationFrame(raf.current);
    if (commitTimer.current != null) clearTimeout(commitTimer.current);
  }, []);

  const select = useCallback(
    (i: number) => {
      if (i < 0 || i >= tabs.length) return;
      if (tabs[i].key === value) return; // không kích hoạt lại nếu chọn đúng tab đang mở
      measure();
      moveTo(i, true); // indicator trượt NGAY, không chờ đổi nội dung
      // Đổi nội dung khi indicator đã đi ~30% quãng đường (đồng bộ, không đổi quá sớm).
      // Một timer duy nhất, xóa khi bấm tab khác → bấm liên tục không bị commit nhầm.
      if (commitTimer.current != null) clearTimeout(commitTimer.current);
      commitTimer.current = window.setTimeout(() => {
        commitTimer.current = null;
        onChange(tabs[i].key);
      }, reduce.current ? 0 : 170);
    },
    [tabs, value, onChange, measure, moveTo]
  );

  const idxFromPointer = useCallback((clientX: number) => {
    const cont = containerRef.current;
    if (!cont) return activeIdxRef.current;
    const x = clientX - cont.getBoundingClientRect().left + cont.scrollLeft;
    let best = 0;
    let bestD = Number.POSITIVE_INFINITY;
    rects.current.forEach((m, i) => {
      const d = Math.abs(m.left + m.width / 2 - x);
      if (d < bestD) {
        bestD = d;
        best = i;
      }
    });
    return best;
  }, []);

  // Kéo thanh bằng chuột/cảm ứng: thanh chọn chảy theo, thả ra thì chốt vào tab gần nhất.
  const onPointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    if (e.button !== 0 && e.pointerType === "mouse") return;
    dragStartX.current = e.clientX;
    pointerId.current = e.pointerId;
    dragging.current = false;
    previewIdx.current = null;
  };
  const onPointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    if (dragStartX.current == null) return;
    if (!dragging.current && Math.abs(e.clientX - dragStartX.current) < 6) return;
    if (!dragging.current) {
      dragging.current = true;
      try { e.currentTarget.setPointerCapture(e.pointerId); } catch { /* noop */ }
    }
    const i = idxFromPointer(e.clientX);
    previewIdx.current = i;
    moveTo(i, true); // xem trước (chưa đổi value)
  };
  const endDrag = (e: React.PointerEvent<HTMLDivElement>) => {
    if (pointerId.current != null) {
      try { e.currentTarget.releasePointerCapture(pointerId.current); } catch { /* noop */ }
    }
    if (dragging.current && previewIdx.current != null) {
      select(previewIdx.current);
    }
    dragStartX.current = null;
    dragging.current = false;
    pointerId.current = null;
    previewIdx.current = null;
  };

  const onKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    let i = activeIdxRef.current;
    if (e.key === "ArrowRight") i = Math.min(tabs.length - 1, i + 1);
    else if (e.key === "ArrowLeft") i = Math.max(0, i - 1);
    else if (e.key === "Home") i = 0;
    else if (e.key === "End") i = tabs.length - 1;
    else return;
    e.preventDefault();
    select(i);
    tabRefs.current[i]?.focus();
  };

  return (
    <div
      ref={containerRef}
      role="tablist"
      aria-orientation="horizontal"
      className={`liquid-tabs ${className}`}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={endDrag}
      onPointerCancel={endDrag}
      onKeyDown={onKeyDown}
    >
      <span ref={indicatorRef} className="liquid-tabs-indicator" aria-hidden="true" />
      {tabs.map((t, i) => (
        <button
          key={t.key}
          ref={(el) => { tabRefs.current[i] = el; }}
          role="tab"
          type="button"
          aria-selected={t.key === value}
          tabIndex={t.key === value ? 0 : -1}
          data-active={t.key === value}
          className="liquid-tabs-tab"
          onClick={() => select(i)}
        >
          {t.label}
        </button>
      ))}
    </div>
  );
}
