import {
  Children,
  cloneElement,
  isValidElement,
  useEffect,
  useMemo,
  type ReactElement,
  type ReactNode,
} from 'react'
import { createPortal } from 'react-dom'
import { Plus, X } from 'lucide-react'
import { cn } from '@/lib/cn'
import { useContainerWidth } from '@/lib/device'
import { Button } from './Button'
import { ErrorNote } from './data'
import { hasOpenLayer } from './Layer'

/**
 * Khung nhập chứng từ dùng chung: phiếu bán hàng, phiếu thu, phiếu chi, phiếu nhập, bút toán.
 *
 * Mở toàn màn hình thay cho hộp thoại; thông tin chung xếp ba cột; lưới dòng nhập trực tiếp
 * trong ô; khối tổng tiền góc phải; thanh nút cố định ở đáy.
 *
 * Phím tắt: Ctrl+S lưu, Ctrl+Shift+S lưu và thêm, Ctrl+Q lưu và đóng, Esc đóng.
 */
export function DocumentForm({
  title,
  code,
  status,
  kind,
  meta,
  fields,
  lines,
  summary,
  aside,
  error,
  busy,
  onClose,
  onSave,
  onSaveAndNew,
  onSaveAndClose,
  saveLabel = 'Lưu',
  extraActions,
}: {
  title: string
  /** Số chứng từ hiển thị cạnh tên, ví dụ PC00008. */
  code?: string
  status?: ReactNode
  /** Ô chọn loại chứng từ đặt cạnh tiêu đề. */
  kind?: ReactNode
  /** Người lập, thời điểm sửa cuối. */
  meta?: ReactNode
  fields: ReactNode
  lines?: ReactNode
  /** Khối tổng tiền (Tổng tiền hàng, thuế, tổng thanh toán). */
  summary?: ReactNode
  aside?: ReactNode
  error?: string | null
  busy?: boolean
  onClose: () => void
  onSave?: () => void
  onSaveAndNew?: () => void
  onSaveAndClose?: () => void
  saveLabel?: string
  extraActions?: ReactNode
}) {
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        if (hasOpenLayer() || document.querySelector('[data-modal]')) return
        event.preventDefault()
        onClose()
        return
      }
      if (!(event.ctrlKey || event.metaKey)) return
      const key = event.key.toLowerCase()
      if (key === 's') {
        event.preventDefault()
        if (event.shiftKey) onSaveAndNew?.()
        else onSave?.()
      }
      if (key === 'q') {
        event.preventDefault()
        onSaveAndClose?.()
      }
    }
    window.addEventListener('keydown', onKey)
    const prevOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      window.removeEventListener('keydown', onKey)
      document.body.style.overflow = prevOverflow
    }
  }, [onClose, onSave, onSaveAndNew, onSaveAndClose])

  return createPortal(
    <div className="fixed inset-0 z-50 flex flex-col bg-paper" role="dialog" aria-modal="true" aria-label={title}>
      <header className="flex h-12 shrink-0 items-center gap-3 border-b border-line bg-panel px-4">
        <h2 className="flex min-w-0 items-baseline gap-2 text-base font-semibold tracking-tight text-ink">
          <span className="truncate">{title}</span>
          {code && <span className="tnum text-sm font-medium text-ink-2">{code}</span>}
        </h2>
        {status}
        {kind}
        {meta && <span className="hidden text-xs text-ink-3 md:inline">{meta}</span>}
        <button
          type="button"
          onClick={onClose}
          aria-label="Đóng chứng từ"
          title="Esc"
          className="ml-auto grid size-8 place-items-center rounded-sm text-ink-3 hover:bg-panel-3 hover:text-ink"
        >
          <X className="size-4" strokeWidth={1.8} />
        </button>
      </header>

      <div className="min-h-0 flex-1 overflow-y-auto p-3 sm:p-4">
        <div className={cn('mx-auto flex max-w-[1400px] flex-col gap-3', aside && 'xl:grid xl:grid-cols-[minmax(0,1fr)_20rem]')}>
          <div className="flex min-w-0 flex-col gap-3">
            {error && <ErrorNote message={error} />}
            <section className="panel">
              <header className="panel-bar flex min-h-10 items-center px-3.5 py-1.5">
                <h3 className="text-sm font-semibold text-ink">Thông tin chung</h3>
              </header>
              <div className="px-3.5 py-3">{fields}</div>
            </section>
            {lines}
            {summary}
          </div>
          {aside && <div className="flex min-w-0 flex-col gap-3">{aside}</div>}
        </div>
      </div>

      <footer className="flex shrink-0 flex-wrap items-center gap-2 border-t border-line bg-panel px-4 py-2.5">
        {extraActions}
        <span className="ml-auto flex flex-wrap items-center gap-2">
          <Button size="sm" onClick={onClose} title="Esc" disabled={busy}>
            Huỷ
          </Button>
          {/* Hai nút lưu phụ là tiện ích nhập nhanh bằng bàn phím trên máy tính. Trên máy hẹp
              chúng làm thanh nút xuống hai hàng, nên chỉ giữ Huỷ và nút lưu chính. */}
          {onSaveAndNew && (
            <Button
              size="sm"
              onClick={onSaveAndNew}
              disabled={busy}
              title="Ctrl+Shift+S"
              className="hidden md:inline-flex"
            >
              Lưu và thêm
            </Button>
          )}
          {onSaveAndClose && (
            <Button
              size="sm"
              onClick={onSaveAndClose}
              disabled={busy}
              title="Ctrl+Q"
              className="hidden md:inline-flex"
            >
              Lưu và đóng
            </Button>
          )}
          <Button size="sm" variant="primary" loading={busy} onClick={onSave} title="Ctrl+S">
            {saveLabel}
          </Button>
        </span>
      </footer>
    </div>,
    document.body,
  )
}

/** Lưới dòng chứng từ: nhập trực tiếp trong ô, dòng tổng ở chân bảng, nút thêm dòng bên dưới. */
export function DocumentLines({
  title = 'Dòng hàng',
  head,
  children,
  onAddLine,
  onClearLines,
  totals,
  count,
  empty,
}: {
  title?: string
  head: ReactNode
  children: ReactNode
  onAddLine?: () => void
  onClearLines?: () => void
  totals?: ReactNode
  count?: number
  empty?: ReactNode
}) {
  // Khung hẹp thì lưới xếp chồng: mỗi dòng thành một thẻ, mỗi ô thành một hàng có nhãn lấy từ
  // đầu bảng. Ô nhập vẫn nguyên nên trạng thái React không đổi, chỉ cách bày là khác.
  const [wrapRef, wrapWidth] = useContainerWidth()
  const stacked = wrapWidth > 0 && wrapWidth < 640

  const labels = useMemo(() => headerLabels(head), [head])
  const body = useMemo(() => (stacked ? withLabels(children, labels) : children), [children, labels, stacked])

  return (
    <section className="panel">
      <header className="panel-bar flex min-h-10 items-center gap-3 px-3.5 py-1.5">
        <h3 className="text-sm font-semibold text-ink">{title}</h3>
        {count !== undefined && (
          <span className="tnum text-xs text-ink-3">{count} dòng</span>
        )}
      </header>
      <div ref={wrapRef} className="overflow-x-auto">
        <table className={cn('data-grid lines-grid', stacked && 'is-stacked')} data-density="compact">
          <thead>
            <tr>{head}</tr>
          </thead>
          <tbody>{body}</tbody>
          {totals && <tfoot>{totals}</tfoot>}
        </table>
        {count === 0 && empty}
      </div>
      {(onAddLine || onClearLines) && (
        <div className="flex flex-wrap items-center gap-2 border-t border-line-2 px-3 py-2">
          {onAddLine && (
            <Button size="sm" variant="ghost" icon={<Plus className="size-3.5" strokeWidth={1.8} />} onClick={onAddLine}>
              Thêm dòng
            </Button>
          )}
          {onClearLines && (
            <Button size="sm" variant="ghost" onClick={onClearLines}>
              Xoá hết dòng
            </Button>
          )}
        </div>
      )}
    </section>
  )
}

/** Nhãn của từng cột, đọc từ các `<th>` trong `head`. Ô nào không có chữ thì nhãn rỗng. */
function headerLabels(head: ReactNode): string[] {
  const cells = isValidElement<{ children?: ReactNode }>(head) ? head.props.children : head
  return Children.toArray(cells).map((cell) => {
    if (!isValidElement<{ children?: ReactNode }>(cell)) return ''
    const text = cell.props.children
    return typeof text === 'string' ? text : ''
  })
}

/**
 * Gắn nhãn cột vào từng ô để chế độ xếp chồng hiện được "Số lượng 12" thay vì một số trơ.
 *
 * Chỉ gắn khi số ô của dòng khớp số cột của đầu bảng. Dòng nào có cấu trúc khác — gộp ô, ô điều
 * kiện — thì giữ nguyên, thà không có nhãn còn hơn gắn nhãn lệch cột.
 */
function withLabels(rows: ReactNode, labels: string[]): ReactNode {
  return Children.map(rows, (row) => {
    if (!isValidElement<{ children?: ReactNode }>(row) || row.type !== 'tr') return row
    const cells = Children.toArray(row.props.children)
    if (cells.length !== labels.length) return row
    return cloneElement(
      row,
      undefined,
      cells.map((cell, index) =>
        isValidElement(cell)
          ? cloneElement(cell as ReactElement<Record<string, unknown>>, { 'data-label': labels[index] })
          : cell,
      ),
    )
  })
}

/** Khối tổng tiền góc phải dưới lưới dòng: các cặp nhãn : số tiền, dòng cuối đậm. */
export function DocumentSummary({
  rows,
  className,
}: {
  rows: Array<{ label: ReactNode; value: ReactNode; strong?: boolean }>
  className?: string
}) {
  return (
    <div className={cn('flex justify-end', className)}>
      <dl className="panel grid w-full max-w-sm grid-cols-[1fr_auto] gap-x-6 px-3.5 py-2 text-sm">
        {rows.map((row, index) => (
          <div
            key={index}
            className={cn(
              'contents',
              row.strong && '[&>dd]:text-base [&>dd]:font-semibold [&>dt]:font-semibold [&>dt]:text-ink',
            )}
          >
            <dt className={cn('py-1 text-ink-2', row.strong && 'border-t border-line')}>{row.label}</dt>
            <dd className={cn('tnum py-1 text-right text-ink', row.strong && 'border-t border-line')}>{row.value}</dd>
          </div>
        ))}
      </dl>
    </div>
  )
}
