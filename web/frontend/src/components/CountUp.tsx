import { useEffect, useRef, useState } from "react";

/**
 * Hiệu ứng "số chạy": nhận vào một CHUỖI đã định dạng sẵn (vd "1.234.567 ₫", "0",
 * "12 phiếu") hoặc một SỐ, tách phần số ra rồi chạy từ 0 → giá trị thật khi xuất hiện.
 * Giữ nguyên tiền tố/hậu tố (₫, %, chữ...) và cách ngăn nghìn kiểu vi-VN.
 *
 * Tôn trọng "prefers-reduced-motion" và chế độ nhẹ (html.perf-lite): khi đó hiện thẳng
 * số cuối, không chạy — để máy yếu / người dùng nhạy cảm chuyển động không bị ảnh hưởng.
 */

const easeOutExpo = (t: number) => (t >= 1 ? 1 : 1 - Math.pow(2, -10 * t));

// Bắt token số đầu tiên: phần nguyên có thể ngăn nghìn bằng '.', phần thập phân bằng ','.
const NUM_RE = /-?\d[\d.]*(?:,\d+)?/;

function parseViNumber(token: string): { value: number; decimals: number } {
  const decIdx = token.indexOf(",");
  const decimals = decIdx >= 0 ? token.length - decIdx - 1 : 0;
  const value = parseFloat(token.replace(/\./g, "").replace(",", "."));
  return { value: Number.isNaN(value) ? 0 : value, decimals };
}

/**
 * Có nên bỏ qua animation không. Chỉ tôn trọng "prefers-reduced-motion" (yêu cầu trợ năng).
 * KHÔNG gắn với chế độ nhẹ (perf-lite): số chạy chỉ là hiệu ứng một-lần rất nhẹ (vài lần
 * setState trong ~1s, không blur/GPU), khác với animation trang trí lặp vô hạn mà perf-lite tắt.
 */
function prefersStatic(): boolean {
  if (typeof window === "undefined") return true;
  return window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
}

export function CountUp({
  text,
  duration = 1000,
}: {
  /** Giá trị hiển thị: chuỗi đã định dạng hoặc số thô. */
  text: string | number;
  /** Thời lượng chạy số (ms). */
  duration?: number;
}) {
  const str = typeof text === "number" ? String(text) : text;
  const match = str.match(NUM_RE);
  const parsed = match ? parseViNumber(match[0]) : null;
  const target = parsed?.value ?? 0;
  const decimals = parsed?.decimals ?? 0;
  const hasNumber = match !== null;

  const staticMode = useRef(prefersStatic()).current;
  const [display, setDisplay] = useState(staticMode ? target : 0);
  const fromRef = useRef(staticMode ? target : 0);
  const rafRef = useRef<number | null>(null);

  // CHÚ Ý: chỉ phụ thuộc các GIÁ TRỊ NGUYÊN THUỶ ổn định. Không đưa `match` (một object
  // mới sinh mỗi lần render) vào deps — nếu không, mỗi khung hình setDisplay → re-render →
  // match mới → effect chạy lại → animation bị KHỞI ĐỘNG LẠI liên tục và số đứng yên ở 0.
  useEffect(() => {
    if (!hasNumber || staticMode) {
      setDisplay(target);
      fromRef.current = target;
      return;
    }
    if (fromRef.current === target) {
      setDisplay(target);
      return;
    }
    const from = fromRef.current;
    const start = performance.now();
    const step = (now: number) => {
      const p = Math.min(1, (now - start) / duration);
      setDisplay(from + (target - from) * easeOutExpo(p));
      if (p < 1) {
        rafRef.current = requestAnimationFrame(step);
      } else {
        fromRef.current = target;
        rafRef.current = null;
      }
    };
    if (rafRef.current) cancelAnimationFrame(rafRef.current);
    rafRef.current = requestAnimationFrame(step);
    return () => {
      if (rafRef.current) cancelAnimationFrame(rafRef.current);
    };
  }, [target, decimals, duration, staticMode, hasNumber]);

  if (!match) return <>{str}</>;

  const prefix = str.slice(0, match.index);
  const suffix = str.slice((match.index ?? 0) + match[0].length);
  const formatted = new Intl.NumberFormat("vi-VN", {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  }).format(display);

  return (
    <>
      {prefix}
      {formatted}
      {suffix}
    </>
  );
}
