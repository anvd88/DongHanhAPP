import { useEffect, useRef, useState } from 'react'
import { CalendarDays, ChevronDown, ChevronLeft, ChevronRight } from 'lucide-react'
import { cn } from '@/lib/cn'
import {
  date as formatDate,
  monthKey,
  monthLabel,
  monthRange,
  parseISODate,
  parseUserDate,
  todayISO,
  toISODate,
} from '@/lib/format'
import { useFieldContext } from './form'
import { AnchoredLayer } from './Layer'

const WEEKDAYS = ['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN']

function calendarCells(year: number, month: number) {
  const first = new Date(year, month, 1)
  // Tuần bắt đầu từ thứ Hai.
  const offset = (first.getDay() + 6) % 7
  const start = new Date(year, month, 1 - offset)
  return Array.from({ length: 42 }, (_, i) => new Date(start.getFullYear(), start.getMonth(), start.getDate() + i))
}

function inRange(iso: string, min?: string, max?: string) {
  if (min && iso < min) return false
  if (max && iso > max) return false
  return true
}

/**
 * Ô ngày: gõ trực tiếp dd/mm/yyyy (Enter hoặc rời ô để nhận) hoặc mở lịch bằng nút bên phải.
 * Hợp đồng giá trị là chuỗi yyyy-MM-dd, rỗng khi chưa chọn.
 */
export function DatePicker({
  value,
  onChange,
  placeholder = 'dd/mm/yyyy',
  disabled,
  min,
  max,
  clearable = true,
  size = 'md',
  className,
  id,
  autoFocus,
}: {
  value: string
  onChange: (value: string) => void
  placeholder?: string
  disabled?: boolean
  min?: string
  max?: string
  clearable?: boolean
  size?: 'sm' | 'md'
  className?: string
  id?: string
  autoFocus?: boolean
}) {
  const ctx = useFieldContext()
  const anchor = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [text, setText] = useState(value ? formatDate(value) : '')

  useEffect(() => {
    setText(value ? formatDate(value) : '')
  }, [value])

  const commitText = () => {
    if (!text.trim()) {
      if (value) onChange('')
      return
    }
    const parsed = parseUserDate(text)
    if (parsed && inRange(parsed, min, max)) {
      if (parsed !== value) onChange(parsed)
      else setText(formatDate(value))
    } else setText(value ? formatDate(value) : '')
  }

  return (
    <>
      <div
        ref={anchor}
        className={cn(
          'control flex items-center px-0',
          size === 'sm' && 'control-sm',
          disabled && 'cursor-not-allowed',
          className,
        )}
        aria-disabled={disabled || undefined}
        aria-invalid={ctx?.invalid || undefined}
      >
        <input
          id={id ?? ctx?.id}
          aria-describedby={ctx?.describedBy}
          disabled={disabled}
          autoFocus={autoFocus}
          value={text}
          placeholder={placeholder}
          inputMode="numeric"
          autoComplete="off"
          onChange={(event) => setText(event.target.value)}
          onBlur={commitText}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              event.preventDefault()
              commitText()
            } else if (event.key === 'ArrowDown' && !open) {
              event.preventDefault()
              setOpen(true)
            }
          }}
          className="tnum h-full min-w-0 flex-1 bg-transparent px-2 outline-none placeholder:text-ink-3 disabled:cursor-not-allowed"
        />
        <button
          type="button"
          tabIndex={-1}
          aria-label="Mở lịch"
          disabled={disabled}
          onMouseDown={(event) => event.preventDefault()}
          onClick={() => setOpen((o) => !o)}
          className="grid h-full w-7 shrink-0 place-items-center text-ink-3 hover:text-ink disabled:opacity-50"
        >
          <CalendarDays className="size-3.5" strokeWidth={1.7} />
        </button>
      </div>

      <AnchoredLayer anchorRef={anchor} open={open && !disabled} onClose={() => setOpen(false)} label="Lịch">
        <Calendar
          value={value}
          min={min}
          max={max}
          onPick={(iso) => {
            onChange(iso)
            setOpen(false)
          }}
          onClear={
            clearable && value
              ? () => {
                  onChange('')
                  setOpen(false)
                }
              : undefined
          }
        />
      </AnchoredLayer>
    </>
  )
}

function Calendar({
  value,
  min,
  max,
  onPick,
  onClear,
}: {
  value: string
  min?: string
  max?: string
  onPick: (iso: string) => void
  onClear?: () => void
}) {
  const selected = parseISODate(value) ?? new Date()
  const [view, setView] = useState({ year: selected.getFullYear(), month: selected.getMonth() })
  const today = todayISO()
  const cells = calendarCells(view.year, view.month)
  const todayAllowed = inRange(today, min, max)

  return (
    <div className="w-64 p-2 select-none">
      <div className="mb-1 flex items-center">
        <button
          type="button"
          aria-label="Tháng trước"
          onClick={() => setView((v) => ({ year: v.month === 0 ? v.year - 1 : v.year, month: (v.month + 11) % 12 }))}
          className="grid size-7 place-items-center rounded-sm text-ink-2 hover:bg-panel-3"
        >
          <ChevronLeft className="size-4" strokeWidth={1.8} />
        </button>
        <span className="flex-1 text-center text-sm font-semibold text-ink">
          Tháng {view.month + 1}/{view.year}
        </span>
        <button
          type="button"
          aria-label="Tháng sau"
          onClick={() => setView((v) => ({ year: v.month === 11 ? v.year + 1 : v.year, month: (v.month + 1) % 12 }))}
          className="grid size-7 place-items-center rounded-sm text-ink-2 hover:bg-panel-3"
        >
          <ChevronRight className="size-4" strokeWidth={1.8} />
        </button>
      </div>
      <div className="grid grid-cols-7 text-center text-2xs font-medium text-ink-3">
        {WEEKDAYS.map((d) => (
          <span key={d} className="py-1">
            {d}
          </span>
        ))}
      </div>
      <div className="grid grid-cols-7 gap-px">
        {cells.map((d) => {
          const iso = toISODate(d)
          const outside = d.getMonth() !== view.month
          const isSelected = iso === value
          const isToday = iso === today
          const blocked = !inRange(iso, min, max)
          return (
            <button
              key={iso}
              type="button"
              disabled={blocked}
              aria-label={formatDate(iso)}
              aria-pressed={isSelected}
              onClick={() => onPick(iso)}
              className={cn(
                'tnum h-8 rounded-sm text-sm',
                outside ? 'text-ink-3' : 'text-ink',
                !isSelected && !blocked && 'hover:bg-panel-3',
                isToday && !isSelected && 'font-semibold text-brand',
                isSelected && 'bg-brand font-medium text-on-brand',
                blocked && 'cursor-not-allowed opacity-35',
              )}
            >
              {d.getDate()}
            </button>
          )
        })}
      </div>
      <div className="mt-2 flex items-center gap-1 border-t border-line-2 pt-2">
        {todayAllowed && (
          <button
            type="button"
            onClick={() => onPick(today)}
            className="rounded-sm px-2 py-1 text-xs font-medium text-brand hover:bg-brand-wash"
          >
            Hôm nay
          </button>
        )}
        {onClear && (
          <button
            type="button"
            onClick={onClear}
            className="ml-auto rounded-sm px-2 py-1 text-xs text-ink-2 hover:bg-panel-3"
          >
            Xoá
          </button>
        )}
      </div>
    </div>
  )
}

/** Ô chọn kỳ tháng, giá trị dạng yyyy-MM. */
export function MonthPicker({
  value,
  onChange,
  disabled,
  min,
  max,
  clearable,
  placeholder = 'Mọi kỳ',
  size = 'md',
  className,
  id,
}: {
  value: string
  onChange: (value: string) => void
  disabled?: boolean
  min?: string
  max?: string
  clearable?: boolean
  placeholder?: string
  size?: 'sm' | 'md'
  className?: string
  id?: string
}) {
  const ctx = useFieldContext()
  const anchor = useRef<HTMLButtonElement>(null)
  const [open, setOpen] = useState(false)
  const now = monthKey()
  const [viewYear, setViewYear] = useState(() => Number((value || now).slice(0, 4)))

  useEffect(() => {
    if (open) setViewYear(Number((value || now).slice(0, 4)))
  }, [open, value, now])

  return (
    <>
      <button
        ref={anchor}
        id={id ?? ctx?.id}
        type="button"
        disabled={disabled}
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
        className={cn(
          'control flex items-center gap-2 text-left',
          size === 'sm' && 'control-sm',
          !value && 'text-ink-3',
          className,
        )}
      >
        <CalendarDays className="size-3.5 shrink-0 text-ink-3" strokeWidth={1.7} />
        <span className="min-w-0 flex-1 truncate">{value ? monthLabel(value) : placeholder}</span>
        <ChevronDown className="size-3.5 shrink-0 text-ink-3" strokeWidth={1.8} />
      </button>

      <AnchoredLayer anchorRef={anchor} open={open && !disabled} onClose={() => setOpen(false)} label="Chọn kỳ">
        <div className="w-60 p-2 select-none">
          <div className="mb-1 flex items-center">
            <button
              type="button"
              aria-label="Năm trước"
              onClick={() => setViewYear((y) => y - 1)}
              className="grid size-7 place-items-center rounded-sm text-ink-2 hover:bg-panel-3"
            >
              <ChevronLeft className="size-4" strokeWidth={1.8} />
            </button>
            <span className="tnum flex-1 text-center text-sm font-semibold text-ink">{viewYear}</span>
            <button
              type="button"
              aria-label="Năm sau"
              onClick={() => setViewYear((y) => y + 1)}
              className="grid size-7 place-items-center rounded-sm text-ink-2 hover:bg-panel-3"
            >
              <ChevronRight className="size-4" strokeWidth={1.8} />
            </button>
          </div>
          <div className="grid grid-cols-3 gap-1">
            {Array.from({ length: 12 }, (_, i) => {
              const key = `${viewYear}-${String(i + 1).padStart(2, '0')}`
              const blocked = (!!min && key < min) || (!!max && key > max)
              const isSelected = key === value
              const isCurrent = key === now
              return (
                <button
                  key={key}
                  type="button"
                  disabled={blocked}
                  aria-pressed={isSelected}
                  onClick={() => {
                    onChange(key)
                    setOpen(false)
                  }}
                  className={cn(
                    'h-8 rounded-sm text-sm',
                    !isSelected && !blocked && 'hover:bg-panel-3',
                    isCurrent && !isSelected && 'font-semibold text-brand',
                    isSelected && 'bg-brand font-medium text-on-brand',
                    blocked && 'cursor-not-allowed opacity-35',
                  )}
                >
                  Tháng {i + 1}
                </button>
              )
            })}
          </div>
          <div className="mt-2 flex items-center gap-1 border-t border-line-2 pt-2">
            <button
              type="button"
              onClick={() => {
                onChange(now)
                setOpen(false)
              }}
              className="rounded-sm px-2 py-1 text-xs font-medium text-brand hover:bg-brand-wash"
            >
              Tháng này
            </button>
            {clearable && value && (
              <button
                type="button"
                onClick={() => {
                  onChange('')
                  setOpen(false)
                }}
                className="ml-auto rounded-sm px-2 py-1 text-xs text-ink-2 hover:bg-panel-3"
              >
                Bỏ chọn
              </button>
            )}
          </div>
        </div>
      </AnchoredLayer>
    </>
  )
}

export interface DateRange {
  from: string
  to: string
}

type Preset = { id: string; label: string; range: () => DateRange }

const PRESETS: Preset[] = [
  { id: 'today', label: 'Hôm nay', range: () => ({ from: todayISO(), to: todayISO() }) },
  {
    id: 'week',
    label: 'Tuần này',
    range: () => {
      const d = new Date()
      const offset = (d.getDay() + 6) % 7
      const from = new Date(d.getFullYear(), d.getMonth(), d.getDate() - offset)
      const to = new Date(from.getFullYear(), from.getMonth(), from.getDate() + 6)
      return { from: toISODate(from), to: toISODate(to) }
    },
  },
  { id: 'month', label: 'Tháng này', range: () => monthRange(monthKey()) },
  {
    id: 'last-month',
    label: 'Tháng trước',
    range: () => {
      const d = new Date()
      return monthRange(monthKey(new Date(d.getFullYear(), d.getMonth() - 1, 1)))
    },
  },
  {
    id: 'quarter',
    label: 'Quý này',
    range: () => {
      const d = new Date()
      const q = Math.floor(d.getMonth() / 3) * 3
      return { from: toISODate(new Date(d.getFullYear(), q, 1)), to: toISODate(new Date(d.getFullYear(), q + 3, 0)) }
    },
  },
  {
    id: 'year',
    label: 'Năm nay',
    range: () => {
      const y = new Date().getFullYear()
      return { from: `${y}-01-01`, to: `${y}-12-31` }
    },
  },
]

/** Khoảng ngày: hai ô ngày và một nút chọn nhanh theo kỳ. */
export function DateRangePicker({
  value,
  onChange,
  className,
  size = 'md',
}: {
  value: DateRange
  onChange: (range: DateRange) => void
  className?: string
  size?: 'sm' | 'md'
}) {
  const anchor = useRef<HTMLButtonElement>(null)
  const [open, setOpen] = useState(false)
  return (
    <div className={cn('flex items-center gap-1', className)}>
      <DatePicker
        value={value.from}
        onChange={(from) => onChange({ ...value, from })}
        max={value.to || undefined}
        clearable
        size={size}
        className="w-32"
        placeholder="Từ ngày"
      />
      <span className="text-xs text-ink-3">đến</span>
      <DatePicker
        value={value.to}
        onChange={(to) => onChange({ ...value, to })}
        min={value.from || undefined}
        clearable
        size={size}
        className="w-32"
        placeholder="Đến ngày"
      />
      <button
        ref={anchor}
        type="button"
        aria-label="Chọn nhanh khoảng ngày"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
        className={cn(
          'control flex w-auto shrink-0 items-center gap-1 px-2 text-xs text-ink-2 hover:text-ink',
          size === 'sm' && 'control-sm',
        )}
      >
        Kỳ
        <ChevronDown className="size-3.5" strokeWidth={1.8} />
      </button>
      <AnchoredLayer anchorRef={anchor} open={open} onClose={() => setOpen(false)} align="end" label="Khoảng ngày nhanh">
        <ul className="w-40 py-1">
          {PRESETS.map((preset) => (
            <li key={preset.id}>
              <button
                type="button"
                className="menu-item"
                onClick={() => {
                  onChange(preset.range())
                  setOpen(false)
                }}
              >
                {preset.label}
              </button>
            </li>
          ))}
          <li className="mt-1 border-t border-line-2 pt-1">
            <button
              type="button"
              className="menu-item text-ink-2"
              onClick={() => {
                onChange({ from: '', to: '' })
                setOpen(false)
              }}
            >
              Bỏ lọc ngày
            </button>
          </li>
        </ul>
      </AnchoredLayer>
    </div>
  )
}
