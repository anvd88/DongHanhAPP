import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { cn } from '@/lib/cn'
import { EMPTY, vnd } from '@/lib/format'

export type Tone = 'neutral' | 'ok' | 'warn' | 'danger' | 'info' | 'brand'

const BADGE_TONES: Record<Tone, string> = {
  neutral: 'bg-panel-3 text-ink-2',
  ok: 'bg-ok-wash text-ok',
  warn: 'bg-warn-wash text-warn',
  danger: 'bg-danger-wash text-danger',
  info: 'bg-info-wash text-info',
  brand: 'bg-brand-wash text-brand-ink',
}

/**
 * Nhãn trạng thái chứng từ. Chữ mang nghĩa, màu chỉ hỗ trợ: xanh lá = đã xong, vàng = đang chờ,
 * đỏ = huỷ/lỗi, xám = nháp/đã đóng, xanh = đang xử lý.
 */
export function StatusBadge({
  tone = 'neutral',
  children,
  className,
  title,
}: {
  tone?: Tone
  children: ReactNode
  className?: string
  title?: string
}) {
  return (
    <span title={title} className={cn('badge', BADGE_TONES[tone], className)}>
      {children}
    </span>
  )
}

/** Tên cũ, giữ để mã hiện có vẫn biên dịch. */
export const StatusChip = StatusBadge

/**
 * Số tiền: chữ số đều bề rộng, số âm đỏ kèm dấu trừ, số 0 có thể hiện mờ hoặc để trống.
 * Luôn đặt trong ô canh phải.
 */
export function Money({
  value,
  zero = 'dash',
  className,
  strong,
  muted,
}: {
  value: number | null | undefined
  /** Cách hiện số 0: gạch mờ, số 0, hoặc để trống. */
  zero?: 'dash' | 'zero' | 'blank'
  className?: string
  strong?: boolean
  muted?: boolean
}) {
  if (value == null) return <span className={cn('tnum text-ink-3', className)}>{EMPTY}</span>
  if (value === 0) {
    if (zero === 'blank') return null
    return <span className={cn('tnum text-ink-3', className)}>{zero === 'dash' ? EMPTY : '0'}</span>
  }
  return (
    <span
      className={cn(
        'tnum',
        value < 0 ? 'text-danger' : muted ? 'text-ink-2' : 'text-ink',
        strong && 'font-semibold',
        className,
      )}
    >
      {vnd(value)}
    </span>
  )
}

export function Skeleton({ className }: { className?: string }) {
  return <div className={cn('animate-pulse rounded-sm bg-panel-3', className)} />
}

/** Trạng thái rỗng: nói rõ đang thiếu gì và làm gì tiếp, không minh hoạ. */
export function EmptyState({
  title,
  description,
  action,
  compact,
  className,
}: {
  title: string
  description?: ReactNode
  action?: ReactNode
  compact?: boolean
  className?: string
}) {
  return (
    <div
      className={cn(
        'flex flex-col items-center gap-2 px-6 text-center',
        compact ? 'py-6' : 'py-12',
        className,
      )}
    >
      <p className="text-sm font-medium text-ink-2">{title}</p>
      {description && <p className="max-w-md text-xs text-ink-3">{description}</p>}
      {action && <div className="mt-1">{action}</div>}
    </div>
  )
}

export function ErrorNote({
  message,
  action,
  className,
}: {
  message: string
  action?: ReactNode
  className?: string
}) {
  return (
    <div
      role="alert"
      className={cn(
        'flex flex-wrap items-center gap-3 rounded-md border border-line border-l-2 border-l-danger bg-panel px-3.5 py-2.5',
        className,
      )}
    >
      <p className="flex-1 text-sm text-ink">{message}</p>
      {action}
    </div>
  )
}

/** Thông báo trong trang: lưu ý nghiệp vụ, cảnh báo kỳ đã khoá, hướng dẫn bước tiếp. */
export function InlineAlert({
  tone = 'info',
  title,
  children,
  action,
  className,
}: {
  tone?: Exclude<Tone, 'neutral' | 'brand'>
  title?: ReactNode
  children?: ReactNode
  action?: ReactNode
  className?: string
}) {
  const border = {
    info: 'border-l-brand',
    ok: 'border-l-ok',
    warn: 'border-l-warn',
    danger: 'border-l-danger',
  }[tone]
  return (
    <div
      className={cn(
        'flex flex-wrap items-start gap-3 rounded-md border border-line border-l-2 bg-panel px-3.5 py-2.5 text-sm',
        border,
        className,
      )}
    >
      <div className="min-w-0 flex-1">
        {title && <p className="font-medium text-ink">{title}</p>}
        {children && <div className={cn('text-ink-2', title && 'mt-0.5 text-xs')}>{children}</div>}
      </div>
      {action}
    </div>
  )
}

/**
 * Một ô số liệu trong dải số liệu: nhãn nhỏ, con số, tuỳ chọn dòng phụ và liên kết.
 * Dùng trong FigureStrip thay cho các thẻ KPI rời.
 */
export function Figure({
  label,
  value,
  sub,
  tone,
  to,
  className,
}: {
  label: ReactNode
  value: ReactNode
  sub?: ReactNode
  tone?: Tone
  to?: string
  className?: string
}) {
  const color =
    tone === 'ok'
      ? 'text-ok'
      : tone === 'warn'
        ? 'text-warn'
        : tone === 'danger'
          ? 'text-danger'
          : tone === 'brand' || tone === 'info'
            ? 'text-brand'
            : 'text-ink'
  const body = (
    <>
      <span className="block truncate text-xs text-ink-3">{label}</span>
      <span className={cn('tnum block truncate text-lg leading-6 font-semibold tracking-tight', color)}>{value}</span>
      {sub && <span className="block truncate text-xs text-ink-3">{sub}</span>}
    </>
  )
  if (to)
    return (
      <Link to={to} className={cn('block min-w-0 px-4 py-2.5 hover:bg-panel-2', className)}>
        {body}
      </Link>
    )
  return <div className={cn('min-w-0 px-4 py-2.5', className)}>{body}</div>
}

export function FigureStrip({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cn('figure-strip', className)}>{children}</div>
}

/** Bảng khoá : giá trị cho phần thông tin của một bản ghi. */
export function KeyValue({
  rows,
  className,
}: {
  rows: Array<[ReactNode, ReactNode]>
  className?: string
}) {
  return (
    <dl className={cn('kv', className)}>
      {rows.map(([label, value], index) => (
        <div key={index} className="contents">
          <dt>{label}</dt>
          <dd>{value ?? <span className="text-ink-3">{EMPTY}</span>}</dd>
        </div>
      ))}
    </dl>
  )
}

export function Kbd({ children }: { children: ReactNode }) {
  return <kbd className="kbd">{children}</kbd>
}

/** Tên ngắn kiểu Nguyễn Văn A → "VA", dùng cho ảnh đại diện mặc định. */
export function Avatar({
  url,
  name,
  size = 'md',
  className,
}: {
  url?: string | null
  name: string
  size?: 'sm' | 'md' | 'lg'
  className?: string
}) {
  const box = size === 'lg' ? 'size-10 text-sm' : size === 'sm' ? 'size-6 text-[10px]' : 'size-7 text-2xs'
  const initials =
    name
      .trim()
      .split(/\s+/)
      .slice(-2)
      .map((p) => p[0] ?? '')
      .join('')
      .toUpperCase() || '?'
  if (url)
    return (
      <img
        src={url}
        alt=""
        className={cn(box, 'shrink-0 rounded-sm border border-line object-cover', className)}
      />
    )
  return (
    <span
      aria-hidden
      className={cn(box, 'grid shrink-0 place-items-center rounded-sm bg-brand-wash font-semibold text-brand-ink', className)}
    >
      {initials}
    </span>
  )
}
