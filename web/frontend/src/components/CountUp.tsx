import { useEffect, useRef, useState } from "react";

/**
 * Hiệu ứng "số chạy": nhận vào một CHUỖI đã định dạng sẵn (vd "1.234.567 ₫", "0",
 * "12 phiếu") hoặc một SỐ, tách phần số ra rồi chạy từ 0 → giá trị thật khi xuất hiện.
 * Giữ nguyên tiền tố/hậu tố (₫, %, chữ...) và cách ngăn nghìn kiểu vi-VN.
 *
 * Đây là chuyển động chức năng do người dùng chủ động yêu cầu, vì vậy vẫn chạy khi Windows
 * đang tắt hiệu ứng chuyển động. Các animation trang trí khác vẫn tôn trọng chế độ giảm chuyển động.
 */

const FINAL_COUNT_UNITS = 20;
const HUNDREDS_COUNT_UNITS = 180;
const SHORT_PHASE_RATIO = 0.2;

function hermite(
  from: number,
  to: number,
  fromTangent: number,
  toTangent: number,
  progress: number,
) {
  const t = Math.min(1, Math.max(0, progress));
  const t2 = t * t;
  const t3 = t2 * t;
  return (
    (2 * t3 - 3 * t2 + 1) * from
    + (t3 - 2 * t2 + t) * fromTangent
    + (-2 * t3 + 3 * t2) * to
    + (t3 - t2) * toTangent
  );
}

function harmonicMean(a: number, b: number) {
  return a > 0 && b > 0 ? (2 * a * b) / (a + b) : 0;
}

// Bắt token số đầu tiên: phần nguyên có thể ngăn nghìn bằng '.', phần thập phân bằng ','.
const NUM_RE = /-?\d[\d.]*(?:,\d+)?/;

function parseViNumber(token: string): { value: number; decimals: number } {
  const decIdx = token.indexOf(",");
  const decimals = decIdx >= 0 ? token.length - decIdx - 1 : 0;
  const value = parseFloat(token.replace(/\./g, "").replace(",", "."));
  return { value: Number.isNaN(value) ? 0 : value, decimals };
}

export function CountUp({
  text,
  duration = 5000,
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
  // Bỏ useMemo: React Compiler không giữ được memo thủ công ở đây (nó cảnh báo deps có thể bị đổi sau),
  // mà dựng một Intl.NumberFormat vốn rất rẻ so với việc chạy animation 60 khung/giây ngay bên dưới.
  // Để compiler tự lo phần ghi nhớ thì đúng hơn là ép nó bằng tay rồi bị bỏ tối ưu cả component.
  const formatter = new Intl.NumberFormat("vi-VN", {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  });

  const [display, setDisplay] = useState(0);
  const fromRef = useRef(0);
  const rafRef = useRef<number | null>(null);

  // CHÚ Ý: chỉ phụ thuộc các GIÁ TRỊ NGUYÊN THUỶ ổn định. Không đưa `match` (một object
  // mới sinh mỗi lần render) vào deps — nếu không, mỗi khung hình setDisplay → re-render →
  // match mới → effect chạy lại → animation bị KHỞI ĐỘNG LẠI liên tục và số đứng yên ở 0.
  useEffect(() => {
    if (!hasNumber) {
      // Không có số trong chuỗi thì component trả thẳng `str` (xem `if (!match)` bên dưới) — `display`
      // KHÔNG được vẽ ra, nên gán nó ở đây chỉ tốn một vòng render thừa. Chỉ cần đặt lại mốc `fromRef`
      // để lần sau chuỗi có số trở lại thì vẫn chạy từ 0 đúng như cũ.
      fromRef.current = target;
      return;
    }
    if (fromRef.current === target) {
      setDisplay(target);
      return;
    }
    const from = fromRef.current;
    const delta = target - from;
    const direction = Math.sign(delta);
    const distance = Math.abs(delta);
    const finalDistance = Math.min(FINAL_COUNT_UNITS, distance * SHORT_PHASE_RATIO);
    const hundredsDistance = Math.min(HUNDREDS_COUNT_UNITS, distance * SHORT_PHASE_RATIO);
    const finalDelta = direction * finalDistance;
    const hundredsDelta = direction * hundredsDistance;
    const leadDelta = delta - hundredsDelta - finalDelta;
    const hundredsTarget = target - finalDelta;
    const leadTarget = hundredsTarget - hundredsDelta;
    const leadDuration = duration * (1 - 2 * SHORT_PHASE_RATIO);
    const hundredsDuration = duration * SHORT_PHASE_RATIO;
    const finalDuration = duration - leadDuration - hundredsDuration;
    const leadSpeed = leadDuration > 0 ? Math.abs(leadDelta) / leadDuration : 0;
    const hundredsSpeed = hundredsDuration > 0 ? Math.abs(hundredsDelta) / hundredsDuration : 0;
    const finalSpeed = finalDuration > 0 ? Math.abs(finalDelta) / finalDuration : 0;
    const leadHundredsSpeed = harmonicMean(leadSpeed, hundredsSpeed);
    const hundredsFinalSpeed = harmonicMean(hundredsSpeed, finalSpeed);
    const startVelocity = direction * Math.max(0, 2 * leadSpeed - leadHundredsSpeed);
    const leadHundredsVelocity = direction * leadHundredsSpeed;
    const hundredsFinalVelocity = direction * hundredsFinalSpeed;
    // Mốc thời gian lấy từ CHÍNH khung hình đầu tiên (rAF truyền timestamp vào) thay vì gọi
    // performance.now() sẵn: bỏ được khoảng trễ giữa lúc effect chạy và lúc trình duyệt vẽ khung
    // đầu — nếu không, quãng trễ đó bị tính vào thời lượng và số nhảy vọt ngay khung đầu tiên.
    let start: number | null = null;
    const step = (now: number) => {
      start ??= now;
      const elapsed = Math.min(duration, now - start);
      const value = elapsed <= leadDuration && leadDuration > 0
        ? hermite(
            from,
            leadTarget,
            startVelocity * leadDuration,
            leadHundredsVelocity * leadDuration,
            elapsed / leadDuration,
          )
        : elapsed <= leadDuration + hundredsDuration && hundredsDuration > 0
          ? hermite(
              leadTarget,
              hundredsTarget,
              leadHundredsVelocity * hundredsDuration,
              hundredsFinalVelocity * hundredsDuration,
              (elapsed - leadDuration) / hundredsDuration,
            )
          : finalDuration > 0
            ? hermite(
              hundredsTarget,
              target,
              hundredsFinalVelocity * finalDuration,
              0,
              (elapsed - leadDuration - hundredsDuration) / finalDuration,
            )
            : target;
      const p = duration > 0 ? elapsed / duration : 1;
      setDisplay(p >= 1 ? target : value);
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
  }, [target, decimals, duration, hasNumber]);

  if (!match) return <>{str}</>;

  const prefix = str.slice(0, match.index);
  const suffix = str.slice((match.index ?? 0) + match[0].length);
  const formatted = formatter.format(display);

  return (
    <>
      {prefix}
      {formatted}
      {suffix}
    </>
  );
}
