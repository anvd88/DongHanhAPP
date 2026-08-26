import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
} from "react";
import { AnimatePresence, motion } from "motion/react";
import { Check, Loader2, type LucideIcon } from "lucide-react";
import type { VariantProps } from "class-variance-authority";
import { buttonVariants } from "../shadcn/button";
import { cn } from "../lib/cn";

const EASE_IOS = [0.22, 1, 0.36, 1] as const;

/**
 * Giữ pha "đang chạy" tối thiểu ngần này. Có việc xong trong vài mili-giây (dựng file ngay trên
 * trình duyệt) — không chặn dưới thì spinner nháy một cái rồi biến, người dùng chỉ thấy giật.
 */
const MIN_BUSY_MS = 520;

/**
 * Hàm việc-đang-chạy gọi ngược để báo tiến trình THẬT:
 * - `report(đã_xong, tổng)` — ví dụ `report(3, 12)` là xong 3/12 phiếu;
 * - `report(tỉ_lệ)` — tỉ lệ 0…1 khi tự tính sẵn;
 * - `report(đã_nhận, null)` — biết số đã làm nhưng KHÔNG biết tổng ⇒ giữ thanh ở chế độ không đo được.
 *
 * Không gọi `report` thì thanh chạy kiểu không xác định (vạch trượt qua lại) chứ không bịa ra %.
 */
export type ProgressReport = (done: number, total?: number | null) => void;

type Phase = "idle" | "busy" | "done";

export type ActionProgressButtonProps = {
  /** Việc thật. Trả về `false` = bỏ qua (không có gì để làm / lỗi) → nút quay về ngay. */
  onRun: (report: ProgressReport) => unknown | Promise<unknown>;
  icon: LucideIcon;
  idleLabel: string;
  busyLabel: string;
  doneLabel: string;
  disabled?: boolean;
  className?: string;
  style?: CSSProperties;
  /** Dùng đúng bộ class của `shadcn/button` khi chỗ gọi vốn là <Button>; bỏ trống thì className tự lo. */
  variant?: VariantProps<typeof buttonVariants>["variant"];
  size?: VariantProps<typeof buttonVariants>["size"];
  /** Thời gian giữ trạng thái đã xong trước khi thu về. */
  successMs?: number;
  title?: string;
};

/**
 * Nút hành động có hiệu ứng: bấm → co nhẹ, nở ngang sang trạng thái đang chạy kèm thanh tiến trình ở
 * cạnh dưới, xong đổi sang dấu ✓ rồi tự thu lại. Bề rộng chạy bằng số pixel đo được của nhãn hiện
 * tại (không phải layout animation) nên các nút bên cạnh trôi theo mượt chứ không nhảy cóc.
 */
export function ActionProgressButton({
  onRun,
  icon: Icon,
  idleLabel,
  busyLabel,
  doneLabel,
  disabled,
  className,
  style,
  variant,
  size,
  successMs = 1200,
  title,
}: ActionProgressButtonProps) {
  const [phase, setPhase] = useState<Phase>("idle");
  const [labelWidth, setLabelWidth] = useState<number | null>(null);
  // null = chưa đo được tiến trình ⇒ chạy kiểu không xác định thay vì bịa ra con số.
  const [progress, setProgress] = useState<number | null>(null);
  const [fillSeconds, setFillSeconds] = useState(0.25);
  const measureRef = useRef<HTMLSpanElement>(null);
  const progressRef = useRef<number | null>(null);
  const aliveRef = useRef(true);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const label = phase === "busy" ? busyLabel : phase === "done" ? doneLabel : idleLabel;

  // Đo bề rộng thật của nhãn đang hiển thị để animate `width` bằng pixel. Dùng `offsetWidth` /
  // `contentRect` chứ không phải `getBoundingClientRect()`: nút có scale khi rê/bấm chuột, đo qua
  // rect sẽ dính luôn hệ số scale rồi cộng dồn sai số qua từng lần đổi nhãn.
  useLayoutEffect(() => {
    const el = measureRef.current;
    if (el) setLabelWidth(el.offsetWidth);
  }, [label]);

  // Font web (Be Vietnam Pro) nạp xong SAU lần render đầu và rộng hơn font dự phòng — không đo lại
  // thì nhãn bị cắt mất đuôi ngay từ trạng thái ban đầu.
  useEffect(() => {
    const el = measureRef.current;
    if (!el || typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver(([entry]) => {
      const width = entry?.contentRect.width ?? 0;
      if (width > 0) setLabelWidth(width);
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    aliveRef.current = true;
    return () => {
      aliveRef.current = false;
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, []);

  const report = useCallback<ProgressReport>((done, total) => {
    if (!aliveRef.current) return;
    // Không biết tổng ⇒ giữ nguyên chế độ hiện tại, tuyệt đối không đoán bừa một con số.
    if (total === null || (total !== undefined && (!Number.isFinite(total) || total <= 0))) return;
    const ratio = total === undefined ? done : done / total;
    if (!Number.isFinite(ratio)) return;
    let next = Math.min(1, Math.max(0, ratio));
    // Tiến trình chỉ được tiến, không được lùi (các tác vụ song song về đích không theo thứ tự).
    if (progressRef.current !== null && next < progressRef.current) next = progressRef.current;
    progressRef.current = next;
    setFillSeconds(0.25);
    setProgress(next);
  }, []);

  const run = useCallback(async () => {
    if (disabled || phase !== "idle") return;
    progressRef.current = null;
    setProgress(null);
    setFillSeconds(0.25);
    setPhase("busy");
    const startedAt = performance.now();
    let ok = true;
    try {
      ok = (await onRun(report)) !== false;
    } catch (err) {
      console.error(`${idleLabel}: thất bại`, err);
      ok = false;
    }
    const remain = MIN_BUSY_MS - (performance.now() - startedAt);
    if (aliveRef.current && ok) {
      // Việc đã xong thật ⇒ chạy nốt thanh về 100% trong đúng quãng chờ tối thiểu còn lại.
      setFillSeconds(Math.max(0.24, remain / 1000));
      progressRef.current = 1;
      setProgress(1);
    }
    if (remain > 0) await new Promise((resolve) => setTimeout(resolve, remain));
    if (!aliveRef.current) return;
    if (!ok) {
      setPhase("idle");
      setProgress(null);
      progressRef.current = null;
      return;
    }
    setPhase("done");
    timerRef.current = setTimeout(() => {
      if (!aliveRef.current) return;
      setPhase("idle");
      setProgress(null);
      progressRef.current = null;
    }, successMs);
  }, [disabled, idleLabel, onRun, phase, report, successMs]);

  return (
    <motion.button
      type="button"
      title={title}
      onClick={() => void run()}
      disabled={disabled}
      aria-busy={phase === "busy"}
      data-phase={phase}
      data-progress={progress === null ? "unknown" : Math.round(progress * 100)}
      whileHover={phase === "idle" ? { y: -2, scale: 1.015 } : undefined}
      whileTap={phase === "idle" ? { y: 1, scale: 0.955 } : undefined}
      transition={{ type: "spring", stiffness: 480, damping: 26, mass: 0.7 }}
      style={style}
      className={cn(
        "relative overflow-hidden",
        variant || size ? buttonVariants({ variant, size }) : null,
        className,
      )}
    >
      {/* Ô icon cố định 1rem: đổi icon ↔ spinner ↔ ✓ mà không đụng vào bề rộng nút. */}
      <span className="relative block h-4 w-4 shrink-0">
        <AnimatePresence initial={false} mode="wait">
          <motion.span
            key={phase}
            className="absolute inset-0 flex items-center justify-center"
            initial={{ opacity: 0, scale: 0.55, rotate: -35 }}
            animate={{ opacity: 1, scale: 1, rotate: 0 }}
            exit={{ opacity: 0, scale: 0.55, rotate: 35 }}
            transition={{ duration: 0.16, ease: EASE_IOS }}
          >
            {phase === "busy" ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : phase === "done" ? (
              <Check className="h-4 w-4" />
            ) : (
              <Icon className="h-4 w-4" />
            )}
          </motion.span>
        </AnimatePresence>
      </span>

      {/* Nhãn: bản vô hình giữ chiều cao + cho số đo, bản thật trượt mờ khi đổi chữ. */}
      <motion.span
        className="relative block overflow-hidden text-left"
        initial={false}
        animate={{ width: labelWidth ?? "auto" }}
        transition={{ duration: 0.3, ease: EASE_IOS }}
      >
        {/* `inline-block` chứ không phải `block`: block sẽ ăn theo bề rộng (đang bị animate) của thẻ
            cha nên đo ra chính con số cũ, nhãn dài hơn không bao giờ nở ra được. */}
        <span ref={measureRef} aria-hidden className="invisible inline-block whitespace-nowrap">
          {label}
        </span>
        <AnimatePresence initial={false}>
          <motion.span
            key={label}
            className="absolute inset-0 flex items-center whitespace-nowrap"
            initial={{ opacity: 0, y: 9, filter: "blur(4px)" }}
            animate={{ opacity: 1, y: 0, filter: "blur(0px)" }}
            exit={{ opacity: 0, y: -9, filter: "blur(4px)" }}
            transition={{ duration: 0.2, ease: EASE_IOS }}
          >
            {label}
          </motion.span>
        </AnimatePresence>
      </motion.span>

      {/* Thanh tiến trình sát cạnh dưới, ăn theo màu chữ của nút nên hợp mọi variant. */}
      <AnimatePresence>
        {phase !== "idle" && (
          <motion.span
            key="progress"
            className="pointer-events-none absolute inset-x-0 bottom-0 block h-[3px]"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.18, ease: EASE_IOS }}
          >
            <span className="absolute inset-0 block bg-current opacity-[0.16]" />
            {progress === null ? (
              // Chưa đo được (đang chờ máy chủ trả lời): vạch trượt qua lại — báo "đang chạy" mà
              // không vờ như đã xong x%. `key` bắt buộc phải khác nhánh dưới: cùng key thì React
              // dùng lại đúng thẻ DOM đó và vạch đo được thừa hưởng luôn translateX của vạch trượt
              // (đo thật: vạch nằm lệch hẳn ra ngoài nút, tưởng như thanh không chạy).
              <motion.span
                key="indeterminate"
                className="absolute inset-y-0 left-0 block w-2/5 bg-current opacity-90"
                // Chạy đều (linear) và bám sát hai mép: easing kiểu ease-in-out sẽ nấn ná ở hai đầu
                // đúng lúc vạch nằm ngoài khung, nhìn ra thành nút đứng hình mất mấy phần mười giây.
                initial={{ x: "-100%" }}
                animate={{ x: "250%" }}
                transition={{ duration: 1.2, ease: "linear", repeat: Infinity }}
              />
            ) : (
              <motion.span
                key="determinate"
                className="absolute inset-y-0 left-0 block overflow-hidden bg-current opacity-90"
                initial={{ width: 0, x: 0 }}
                animate={{ width: `${progress * 100}%` }}
                transition={{ duration: fillSeconds, ease: EASE_IOS }}
              >
                <motion.span
                  className="absolute inset-y-0 w-1/3 bg-gradient-to-r from-transparent via-white/55 to-transparent"
                  animate={{ x: ["-120%", "420%"] }}
                  transition={{ duration: 1.1, ease: "linear", repeat: Infinity }}
                />
              </motion.span>
            )}
          </motion.span>
        )}
      </AnimatePresence>
    </motion.button>
  );
}
