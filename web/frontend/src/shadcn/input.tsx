import { forwardRef, type InputHTMLAttributes } from "react";
import { cn } from "../lib/cn";

export const Input = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement>>(
  ({ className, ...rest }, ref) => (
    <input
      ref={ref}
      className={cn(
        "h-[38px] w-full rounded-xl border border-[var(--gc-border)] bg-white/55 px-3.5 text-sm text-[var(--gc-text)] outline-none transition-all placeholder:text-[var(--gc-text-muted)] focus:border-[rgba(var(--gc-accent-rgb),0.6)] focus:ring-2 focus:ring-[rgba(var(--gc-accent-rgb),0.18)] dark:bg-white/5",
        className,
      )}
      {...rest}
    />
  ),
);
Input.displayName = "Input";
