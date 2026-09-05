import type { ComponentPropsWithRef, ReactNode } from 'react'
import { cn } from '@/lib/cn'

export type ButtonVariant = 'primary' | 'default' | 'subtle' | 'ghost' | 'danger' | 'link'
export type ButtonSize = 'sm' | 'md'

/**
 * Nút bấm. Mỗi màn hình chỉ có một nút tô màu cho hành động chính; các nút còn lại dùng viền
 * hoặc nền trong suốt. Nhấn xuống dịch 1px để có cảm giác bấm thật.
 */
export interface ButtonProps extends ComponentPropsWithRef<'button'> {
  variant?: ButtonVariant
  size?: ButtonSize
  icon?: ReactNode
  loading?: boolean
}

const BASE =
  'inline-flex items-center justify-center gap-1.5 rounded-md border font-medium whitespace-nowrap select-none ' +
  'transition-[background-color,border-color,color] duration-100 ' +
  'disabled:pointer-events-none disabled:opacity-50 active:translate-y-px'

const VARIANTS: Record<ButtonVariant, string> = {
  primary: 'bg-brand text-on-brand border-brand hover:bg-brand-ink hover:border-brand-ink',
  default: 'bg-panel text-ink border-line hover:bg-panel-2 hover:border-ink-3',
  subtle: 'bg-brand-wash text-brand-ink border-transparent hover:bg-brand-wash-2',
  ghost: 'bg-transparent text-ink-2 border-transparent hover:bg-panel-3 hover:text-ink',
  danger: 'bg-panel text-danger border-line hover:bg-danger-wash hover:border-danger',
  link: 'bg-transparent text-brand border-transparent px-0 hover:underline underline-offset-2',
}

const SIZES: Record<ButtonSize, string> = {
  sm: 'h-7 px-2.5 text-xs',
  md: 'h-8 px-3 text-sm',
}

/** Lớp CSS của nút, dùng cho thẻ <a> hoặc <Link> muốn trông như nút. */
export function buttonClass(variant: ButtonVariant = 'default', size: ButtonSize = 'md', className?: string) {
  return cn(BASE, SIZES[size], VARIANTS[variant], variant === 'link' && 'h-auto', className)
}

export function Button({
  variant = 'default',
  size = 'md',
  icon,
  loading,
  className,
  children,
  disabled,
  type = 'button',
  ...rest
}: ButtonProps) {
  return (
    <button type={type} disabled={disabled || loading} className={buttonClass(variant, size, className)} {...rest}>
      {loading ? <Spinner /> : icon}
      {children}
    </button>
  )
}

/** Nút chỉ có biểu tượng, luôn kèm nhãn cho trình đọc màn hình và tooltip. */
export function IconButton({
  label,
  icon,
  size = 'md',
  variant = 'default',
  className,
  ...rest
}: Omit<ComponentPropsWithRef<'button'>, 'children'> & {
  label: string
  icon: ReactNode
  size?: ButtonSize
  variant?: Exclude<ButtonVariant, 'link'>
}) {
  return (
    <button
      type="button"
      title={label}
      aria-label={label}
      className={cn(
        'grid shrink-0 place-items-center rounded-md border transition-colors duration-100',
        'disabled:pointer-events-none disabled:opacity-50 active:translate-y-px',
        size === 'sm' ? 'size-7' : 'size-8',
        VARIANTS[variant],
        className,
      )}
      {...rest}
    >
      {icon}
    </button>
  )
}

export function Spinner({ className }: { className?: string }) {
  return (
    <span
      role="status"
      aria-label="Đang xử lý"
      className={cn(
        'inline-block size-3.5 shrink-0 animate-spin rounded-full border-2 border-current border-t-transparent',
        className,
      )}
    />
  )
}
