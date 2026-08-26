import type { ButtonHTMLAttributes, CSSProperties, InputHTMLAttributes, ReactNode, SelectHTMLAttributes } from "react";
import { Loader2 } from "lucide-react";

type Variant = "primary" | "ghost" | "danger" | "soft";

export function Button({
  variant = "primary",
  loading,
  className = "",
  children,
  ...rest
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; loading?: boolean }) {
  const styles: Record<Variant, string> = {
    primary: "shadow-lg shadow-[rgba(var(--accent-rgb),0.28)] hover:brightness-110",
    soft: "text-[var(--accent)] hover:bg-[var(--accent-soft)]",
    ghost: "text-[var(--text-secondary)] hover:bg-black/5 dark:hover:bg-white/10",
    danger: "text-white bg-[var(--danger)] hover:brightness-110 shadow-lg shadow-red-500/30",
  };
  return (
    <button
      {...rest}
      disabled={rest.disabled || loading}
      style={
        variant === "primary"
          ? { background: "var(--btn-primary-bg)", color: "var(--btn-primary-text)", border: "1px solid var(--btn-primary-border)" }
          : variant === "soft"
            ? { background: "var(--accent-soft)" }
            : undefined
      }
      className={`inline-flex items-center justify-center gap-2 rounded-xl px-4 py-2.5 text-sm font-semibold transition-all active:scale-[0.97] disabled:opacity-50 disabled:pointer-events-none ${styles[variant]} ${className}`}
    >
      {loading && <Loader2 className="h-4 w-4 animate-spin" />}
      {children}
    </button>
  );
}

export function Input({ className = "", ...rest }: InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      {...rest}
      className={`km-form-control w-full rounded-xl border px-3.5 py-2.5 text-sm outline-none transition-all focus:border-[var(--accent)] focus:ring-2 focus:ring-[var(--accent-soft)] ${className}`}
    />
  );
}

/** "1000000" → "1.000.000" (kiểu Việt Nam: dấu chấm sau mỗi 3 chữ số). */
const groupThousands = (digits: string) => digits.replace(/\B(?=(\d{3})+(?!\d))/g, ".");

/**
 * Ô nhập số tiền: vừa gõ vừa chèn dấu ngăn cách hàng nghìn (1.000.000) cho dễ đọc, nhưng phát ra
 * chuỗi CHỈ gồm chữ số (kèm dấu "-" nếu cho phép số âm) nên nơi gọi vẫn `Number()` như bình thường.
 */
export function MoneyInput({
  value,
  onChange,
  placeholder = "0",
  allowNegative = false,
  disabled,
  className = "",
}: {
  value: string | number;
  onChange: (raw: string) => void;
  placeholder?: string;
  /** Cho phép nhập số âm (vd: ghi nhận GIẢM lương). */
  allowNegative?: boolean;
  disabled?: boolean;
  className?: string;
}) {
  const raw = String(value ?? "");
  const negative = allowNegative && raw.trimStart().startsWith("-");
  const digits = raw.replace(/[^\d]/g, "");
  // Giữ nguyên dấu "-" khi người dùng mới gõ mỗi dấu trừ, chưa kịp gõ số.
  const display = digits ? (negative ? "-" : "") + groupThousands(digits) : negative ? "-" : "";
  return (
    <Input
      type="text"
      inputMode={allowNegative ? "text" : "numeric"}
      value={display}
      placeholder={placeholder}
      disabled={disabled}
      className={className}
      onChange={(e) => {
        const next = e.target.value;
        const neg = allowNegative && next.trimStart().startsWith("-");
        onChange((neg ? "-" : "") + next.replace(/[^\d]/g, ""));
      }}
    />
  );
}

export function Select({ className = "", children, ...rest }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select
      {...rest}
      className={`km-form-control rounded-xl border px-3.5 py-2.5 text-sm outline-none transition-all focus:border-[var(--accent)] focus:ring-2 focus:ring-[var(--accent-soft)] ${className}`}
    >
      {children}
    </select>
  );
}

export function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1.5 block text-xs font-semibold text-[var(--text-secondary)]">{label}</span>
      {children}
    </label>
  );
}

export function Badge({ children, color = "accent" }: { children: ReactNode; color?: string }) {
  const map: Record<string, string> = {
    accent: "bg-[var(--accent-soft)] text-[var(--accent)]",
    success: "bg-emerald-500/15 text-emerald-600 dark:text-emerald-400",
    warning: "bg-amber-500/15 text-amber-600 dark:text-amber-400",
    danger: "bg-red-500/15 text-red-600 dark:text-red-400",
    muted: "bg-black/5 dark:bg-white/10 text-[var(--text-secondary)]",
    purple: "bg-violet-500/15 text-violet-600 dark:text-violet-400",
  };
  return (
    <span className={`inline-flex items-center gap-1 rounded-full px-2.5 py-1 text-xs font-semibold ${map[color] ?? map.accent}`}>
      {children}
    </span>
  );
}

export function Spinner() {
  return (
    <div
      className="flex items-center justify-center py-16 text-[var(--text-muted)]"
      data-login-blocking="true"
    >
      <Loader2 className="h-6 w-6 animate-spin" />
    </div>
  );
}

/** Khối skeleton dùng chung (đặt width/height qua className hoặc style). */
export function Skeleton({ className = "", style }: { className?: string; style?: CSSProperties }) {
  return <span className={`km-skeleton ${className}`} style={style} aria-hidden="true" />;
}

export function EmptyState({ icon, title, hint }: { icon?: ReactNode; title: string; hint?: string }) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 py-16 text-center text-[var(--text-muted)]">
      {icon && <div className="text-4xl opacity-60">{icon}</div>}
      <p className="text-sm font-medium text-[var(--text-secondary)]">{title}</p>
      {hint && <p className="text-xs">{hint}</p>}
    </div>
  );
}
