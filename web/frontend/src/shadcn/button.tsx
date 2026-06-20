import { forwardRef } from "react";
import { motion, type HTMLMotionProps } from "motion/react";
import { cva, type VariantProps } from "class-variance-authority";
import { cn } from "../lib/cn";

const buttonVariants = cva(
  "relative inline-flex select-none items-center justify-center gap-2 rounded-xl text-sm font-semibold outline-none transition-[filter,background,color] focus-visible:ring-2 focus-visible:ring-[rgba(var(--gc-accent-rgb),0.5)] disabled:pointer-events-none disabled:opacity-50",
  {
    variants: {
      variant: {
        primary: "gc-btn-primary text-white",
        ghost: "gc-capsule text-[var(--gc-text-soft)] hover:text-[var(--gc-text)]",
        soft: "text-[var(--gc-accent)] [background:rgba(var(--gc-accent-rgb),0.12)] hover:[background:rgba(var(--gc-accent-rgb),0.2)]",
        danger: "text-white [background:linear-gradient(180deg,#ef4444,#dc2626)] hover:brightness-110",
      },
      size: {
        md: "h-[38px] px-4",
        sm: "h-[32px] px-3 text-[0.8rem]",
        icon: "h-[38px] w-[38px]",
      },
    },
    defaultVariants: { variant: "primary", size: "md" },
  },
);

export interface ButtonProps
  extends HTMLMotionProps<"button">,
    VariantProps<typeof buttonVariants> {}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant, size, type, children, ...rest }, ref) => (
    <motion.button
      ref={ref}
      type={type ?? "button"}
      whileHover={{ y: -2, scale: 1.015 }}
      whileTap={{ y: 1, scale: 0.965 }}
      transition={{ type: "spring", stiffness: 480, damping: 26, mass: 0.7 }}
      className={cn(buttonVariants({ variant, size }), className)}
      {...rest}
    >
      {children}
    </motion.button>
  ),
);
Button.displayName = "Button";

export { buttonVariants };
