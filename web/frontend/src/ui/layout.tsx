import type { CSSProperties, ReactNode, Ref } from 'react'
import { Link } from 'react-router-dom'
import { ChevronRight } from 'lucide-react'
import { cn } from '@/lib/cn'
import { Button } from './Button'
import { Drawer } from './Modal'

/** Khối nội dung: trắng, viền mảnh, bo 4px, không bóng. */
export function Panel({
  title,
  meta,
  actions,
  children,
  className,
  bodyClassName,
  padded,
  footer,
  ref,
}: {
  title?: ReactNode
  meta?: ReactNode
  actions?: ReactNode
  children?: ReactNode
  className?: string
  bodyClassName?: string
  /** Đệm 14px quanh nội dung; bảng và danh sách thì không đệm. */
  padded?: boolean
  footer?: ReactNode
  /** Để nơi dùng đo được bề rộng thật của khối, phục vụ bố cục theo khung chứa. */
  ref?: Ref<HTMLElement>
}) {
  return (
    <section ref={ref} className={cn('panel min-w-0', className)}>
      {(title || actions) && <PanelHeader title={title} meta={meta} actions={actions} />}
      <div className={cn(padded && 'px-3.5 py-3', bodyClassName)}>{children}</div>
      {footer && <footer className="border-t border-line-2 px-3.5 py-2.5">{footer}</footer>}
    </section>
  )
}

export function PanelHeader({
  title,
  meta,
  actions,
  className,
}: {
  title?: ReactNode
  meta?: ReactNode
  actions?: ReactNode
  className?: string
}) {
  return (
    <header className={cn('panel-bar flex min-h-10 flex-wrap items-center gap-x-3 gap-y-1 px-3.5 py-1.5', className)}>
      {title && <h2 className="text-sm font-semibold text-ink">{title}</h2>}
      {meta && <span className="text-xs text-ink-3">{meta}</span>}
      {actions && <div className="ml-auto flex items-center gap-1.5">{actions}</div>}
    </header>
  )
}

/** Tên cũ, giữ để mã hiện có vẫn biên dịch. */
export const Sheet = Panel
export const SheetHeader = PanelHeader

/** Tiêu đề nhóm nhỏ trong một khối. */
export function SectionTitle({ children, className }: { children: ReactNode; className?: string }) {
  return <h3 className={cn('text-xs font-semibold text-ink-2', className)}>{children}</h3>
}

export interface Crumb {
  label: string
  to?: string
}

export function Breadcrumbs({ items, className }: { items: Crumb[]; className?: string }) {
  return (
    <nav aria-label="Đường dẫn" className={className}>
      <ol className="flex flex-wrap items-center gap-1 text-xs text-ink-3">
        {items.map((item, index) => {
          const last = index === items.length - 1
          return (
            <li key={`${item.label}-${index}`} className="flex items-center gap-1">
              {item.to && !last ? (
                <Link to={item.to} className="hover:text-ink hover:underline underline-offset-2">
                  {item.label}
                </Link>
              ) : (
                <span className={last ? 'text-ink-2' : undefined} aria-current={last ? 'page' : undefined}>
                  {item.label}
                </span>
              )}
              {!last && <ChevronRight aria-hidden className="size-3" strokeWidth={1.8} />}
            </li>
          )
        })}
      </ol>
    </nav>
  )
}

/** Đầu trang chi tiết: đường dẫn, tên bản ghi, trạng thái và hành động cấp trang. */
export function PageHeader({
  title,
  crumbs,
  meta,
  actions,
  children,
  className,
}: {
  title: ReactNode
  crumbs?: Crumb[]
  meta?: ReactNode
  actions?: ReactNode
  children?: ReactNode
  className?: string
}) {
  return (
    <header className={cn('flex flex-wrap items-start gap-x-4 gap-y-2', className)}>
      <div className="min-w-0 flex-1">
        {crumbs && <Breadcrumbs items={crumbs} className="mb-1" />}
        <div className="flex flex-wrap items-center gap-2">
          <h1 className="text-lg font-semibold tracking-tight text-ink">{title}</h1>
          {meta}
        </div>
        {children}
      </div>
      {actions && <div className="flex shrink-0 flex-wrap items-center gap-1.5">{actions}</div>}
    </header>
  )
}

/**
 * Thanh công cụ của bảng. Thứ tự cố định trên mọi màn hình: bộ lọc và ô tìm bên trái, tiện ích
 * và hành động chính dồn về bên phải.
 */
export function Toolbar({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cn('panel-bar flex flex-wrap items-center gap-2 px-3 py-2', className)}>{children}</div>
}

export function ToolbarSpacer() {
  return (
    <>
      <span className="ml-auto" />
      <span aria-hidden className="toolbar-sep hidden sm:block" />
    </>
  )
}

export interface TabItem {
  id: string
  label: string
  count?: number
}

/** Bộ lọc trạng thái trên bảng: các nút liền nhau có viền, mục đang chọn nền xanh nhạt. */
export function Segmented({
  items,
  active,
  onChange,
  className,
  size = 'sm',
}: {
  items: TabItem[]
  active: string
  onChange: (id: string) => void
  className?: string
  size?: 'sm' | 'md'
}) {
  return (
    <div
      role="tablist"
      className={cn('inline-flex max-w-full overflow-x-auto rounded-md border border-line bg-panel', className)}
    >
      {items.map((item) => {
        const selected = item.id === active
        return (
          <button
            key={item.id}
            type="button"
            role="tab"
            aria-selected={selected}
            onClick={() => onChange(item.id)}
            className={cn(
              'shrink-0 border-r border-line px-2.5 text-xs whitespace-nowrap transition-colors last:border-r-0',
              size === 'sm' ? 'h-7' : 'h-8 text-sm',
              selected ? 'bg-brand-wash font-medium text-brand-ink' : 'text-ink-2 hover:bg-panel-2 hover:text-ink',
            )}
          >
            {item.label}
            {item.count !== undefined && (
              <span className={cn('tnum ml-1.5', selected ? 'text-brand-ink' : 'text-ink-3')}>{item.count}</span>
            )}
          </button>
        )
      })}
    </div>
  )
}

/** Dải tab gạch chân bên trong một trang hoặc ngăn kéo chi tiết. */
export function Tabs({
  items,
  active,
  onChange,
  className,
}: {
  items: TabItem[]
  active: string
  onChange: (id: string) => void
  className?: string
}) {
  return (
    <div
      className={cn('flex gap-4 overflow-x-auto overflow-y-hidden border-b border-line px-3.5', className)}
      role="tablist"
    >
      {items.map((item) => {
        const selected = item.id === active
        return (
          <button
            key={item.id}
            type="button"
            role="tab"
            aria-selected={selected}
            onClick={() => onChange(item.id)}
            className={cn(
              'relative border-b-2 py-2 text-sm whitespace-nowrap transition-colors',
              selected ? 'border-brand font-medium text-ink' : 'border-transparent text-ink-2 hover:text-ink',
            )}
          >
            {item.label}
            {item.count !== undefined && <span className="tnum ml-1.5 text-xs text-ink-3">{item.count}</span>}
          </button>
        )
      })}
    </div>
  )
}

/** Bố cục dọc chuẩn của trang. */
export function Stack({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cn('flex flex-col gap-3', className)}>{children}</div>
}

/** Cột chính bên trái, cột phụ bên phải, xếp dọc trên màn hẹp. */
export function Split({
  main,
  aside,
  asideWidth = '20rem',
  className,
}: {
  main: ReactNode
  aside: ReactNode
  asideWidth?: string
  className?: string
}) {
  return (
    <div
      className={cn('grid min-w-0 gap-3 xl:grid-cols-[minmax(0,1fr)_var(--aside)]', className)}
      style={{ '--aside': asideWidth } as CSSProperties}
    >
      <div className="min-w-0">{main}</div>
      <div className="flex min-w-0 flex-col gap-3">{aside}</div>
    </div>
  )
}

/** Ngăn kéo bộ lọc, dùng cho bộ điều kiện dài không đặt vừa thanh công cụ. */
export function FilterDrawer({
  open,
  title = 'Bộ lọc',
  onClose,
  onApply,
  onReset,
  children,
}: {
  open: boolean
  title?: string
  onClose: () => void
  onApply?: () => void
  onReset?: () => void
  children: ReactNode
}) {
  return (
    <Drawer
      open={open}
      onClose={onClose}
      title={title}
      width="sm"
      footer={
        <>
          {onReset && (
            <Button size="sm" variant="ghost" onClick={onReset}>
              Bỏ lọc
            </Button>
          )}
          <span className="ml-auto flex gap-2">
            <Button size="sm" onClick={onClose}>
              Đóng
            </Button>
            <Button
              size="sm"
              variant="primary"
              onClick={() => {
                onApply?.()
                onClose()
              }}
            >
              Áp dụng
            </Button>
          </span>
        </>
      }
    >
      <div className="flex flex-col gap-3.5 bg-panel px-4 py-4">{children}</div>
    </Drawer>
  )
}
