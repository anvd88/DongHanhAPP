import { useEffect, useId, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { X } from 'lucide-react'
import { cn } from '@/lib/cn'
import { Button } from './Button'
import { Field, Textarea } from './form'
import { hasOpenLayer } from './Layer'

const FOCUSABLE =
  'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

/** Khoá cuộn nền, đưa tiêu điểm vào hộp và trả về chỗ cũ khi đóng. */
function useDialogBehaviour(open: boolean, onClose: () => void, panelRef: React.RefObject<HTMLElement | null>) {
  useEffect(() => {
    if (!open) return
    const previous = document.activeElement as HTMLElement | null
    const prevOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'

    const focusTimer = window.setTimeout(() => {
      const panel = panelRef.current
      if (!panel) return
      const preferred = panel.querySelector<HTMLElement>('[data-autofocus]')
      const first = preferred ?? panel.querySelector<HTMLElement>(FOCUSABLE)
      ;(first ?? panel).focus()
    }, 0)

    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !hasOpenLayer()) {
        event.stopPropagation()
        onClose()
      }
      if (event.key === 'Tab' && panelRef.current) {
        // Giữ tiêu điểm trong hộp thoại.
        const items = Array.from(panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE)).filter(
          (el) => el.offsetParent !== null,
        )
        if (items.length === 0) return
        const first = items[0]
        const last = items[items.length - 1]
        if (event.shiftKey && document.activeElement === first) {
          event.preventDefault()
          last.focus()
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault()
          first.focus()
        }
      }
    }
    document.addEventListener('keydown', onKey)
    return () => {
      window.clearTimeout(focusTimer)
      document.removeEventListener('keydown', onKey)
      document.body.style.overflow = prevOverflow
      previous?.focus?.()
    }
  }, [open, onClose, panelRef])
}

const SIZES = {
  sm: 'max-w-md',
  md: 'max-w-2xl',
  lg: 'max-w-4xl',
  xl: 'max-w-6xl',
}

/** Hộp thoại giữa màn hình cho thao tác ngắn: tạo/sửa một bản ghi, xác nhận. */
export function Modal({
  open,
  onClose,
  title,
  description,
  size = 'md',
  children,
  footer,
  dismissible = true,
  className,
}: {
  open: boolean
  onClose: () => void
  title: ReactNode
  description?: ReactNode
  size?: keyof typeof SIZES
  children: ReactNode
  footer?: ReactNode
  /** Bấm nền mờ có đóng không. Tắt cho form đang nhập dở. */
  dismissible?: boolean
  className?: string
}) {
  const panelRef = useRef<HTMLDivElement>(null)
  const titleId = useId()
  useDialogBehaviour(open, onClose, panelRef)
  if (!open) return null

  return createPortal(
    <div
      className="overlay-veil fixed inset-0 z-50 flex items-start justify-center overflow-y-auto px-4 py-[6vh]"
      onPointerDown={(event) => {
        if (dismissible && event.target === event.currentTarget) onClose()
      }}
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        data-modal=""
        tabIndex={-1}
        className={cn('modal-panel flex max-h-[88vh] w-full flex-col outline-none', SIZES[size], className)}
      >
        <header className="flex items-start gap-3 border-b border-line px-4 py-3">
          <div className="min-w-0 flex-1">
            <h2 id={titleId} className="text-base font-semibold text-ink">
              {title}
            </h2>
            {description && <p className="mt-0.5 text-xs text-ink-3">{description}</p>}
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Đóng"
            className="grid size-7 shrink-0 place-items-center rounded-sm text-ink-3 hover:bg-panel-3 hover:text-ink"
          >
            <X className="size-4" strokeWidth={1.8} />
          </button>
        </header>
        <div className="min-h-0 flex-1 overflow-y-auto px-4 py-4">{children}</div>
        {footer && (
          <footer className="flex flex-wrap items-center justify-end gap-2 border-t border-line bg-panel-2 px-4 py-3">
            {footer}
          </footer>
        )}
      </div>
    </div>,
    document.body,
  )
}

/**
 * Xác nhận hành động khó hoàn tác (huỷ phiếu, thu hồi, khoá kỳ). Có thể bắt nhập lý do,
 * lý do được đưa vào nhật ký hoạt động ở máy chủ.
 */
export function ConfirmDialog({
  open,
  onClose,
  onConfirm,
  title,
  message,
  confirmLabel = 'Xác nhận',
  tone = 'primary',
  requireReason,
  reasonLabel = 'Lý do',
  reasonMinLength = 1,
  busy,
  error,
}: {
  open: boolean
  onClose: () => void
  onConfirm: (reason: string) => void
  title: ReactNode
  message?: ReactNode
  confirmLabel?: string
  tone?: 'primary' | 'danger'
  requireReason?: boolean
  reasonLabel?: string
  reasonMinLength?: number
  busy?: boolean
  error?: string | null
}) {
  const [reason, setReason] = useState('')
  useEffect(() => {
    if (open) setReason('')
  }, [open])
  const reasonOk = !requireReason || reason.trim().length >= reasonMinLength

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={title}
      size="sm"
      dismissible={!busy}
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={busy}>
            Huỷ
          </Button>
          <Button
            size="sm"
            variant={tone === 'danger' ? 'danger' : 'primary'}
            className={tone === 'danger' ? 'border-danger bg-danger text-white hover:bg-danger hover:text-white' : undefined}
            loading={busy}
            disabled={!reasonOk}
            onClick={() => onConfirm(reason.trim())}
            data-autofocus={requireReason ? undefined : ''}
          >
            {confirmLabel}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 text-sm text-ink">
        {message && <div>{message}</div>}
        {requireReason && (
          <Field label={reasonLabel} required>
            <Textarea
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              rows={3}
              data-autofocus=""
            />
          </Field>
        )}
        {error && <p className="text-xs text-danger">{error}</p>}
      </div>
    </Modal>
  )
}

const DRAWER_WIDTHS = {
  sm: 'max-w-sm',
  md: 'max-w-xl',
  lg: 'max-w-3xl',
  xl: 'max-w-5xl',
}

/** Ngăn kéo trượt từ mép phải: xem chi tiết một dòng mà không rời khỏi danh sách. */
export function Drawer({
  open,
  onClose,
  title,
  meta,
  actions,
  width = 'md',
  children,
  footer,
  className,
}: {
  open: boolean
  onClose: () => void
  title: ReactNode
  meta?: ReactNode
  actions?: ReactNode
  width?: keyof typeof DRAWER_WIDTHS
  children: ReactNode
  footer?: ReactNode
  className?: string
}) {
  const panelRef = useRef<HTMLElement>(null)
  const titleId = useId()
  useDialogBehaviour(open, onClose, panelRef)
  if (!open) return null

  return createPortal(
    <div
      className="overlay-veil fixed inset-0 z-50 flex justify-end"
      onPointerDown={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <aside
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        data-modal=""
        tabIndex={-1}
        className={cn(
          'slide-panel flex h-full w-full flex-col border-l border-line bg-panel outline-none',
          DRAWER_WIDTHS[width],
          className,
        )}
      >
        <header className="flex items-start gap-3 border-b border-line px-4 py-3">
          <div className="min-w-0 flex-1">
            <h2 id={titleId} className="truncate text-base font-semibold text-ink">
              {title}
            </h2>
            {meta && <div className="mt-0.5 flex flex-wrap items-center gap-2 text-xs text-ink-3">{meta}</div>}
          </div>
          {actions && <div className="flex shrink-0 items-center gap-1.5">{actions}</div>}
          <button
            type="button"
            onClick={onClose}
            aria-label="Đóng"
            className="grid size-7 shrink-0 place-items-center rounded-sm text-ink-3 hover:bg-panel-3 hover:text-ink"
          >
            <X className="size-4" strokeWidth={1.8} />
          </button>
        </header>
        <div className="min-h-0 flex-1 overflow-y-auto bg-paper">{children}</div>
        {footer && (
          <footer className="flex flex-wrap items-center gap-2 border-t border-line bg-panel px-4 py-3">
            {footer}
          </footer>
        )}
      </aside>
    </div>,
    document.body,
  )
}
