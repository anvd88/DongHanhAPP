import { useEffect, useMemo, useRef, useState } from 'react'
import { ChevronDown, X } from 'lucide-react'
import { cn } from '@/lib/cn'
import { matches } from '@/lib/text'
import { useFieldContext } from './form'
import { AnchoredLayer } from './Layer'
import { Spinner } from './Button'

export interface ComboOption<D = unknown> {
  value: string
  label: string
  /** Dòng phụ bên phải hoặc bên dưới nhãn, ví dụ số điện thoại, số dư. */
  description?: string
  /** Từ khoá thêm cho tìm kiếm (bí danh, mã cũ). */
  keywords?: string
  disabled?: boolean
  data?: D
}

const MAX_SHOWN = 60

/**
 * Ô chọn có tìm kiếm. Gõ để lọc (không phân biệt dấu), mũi tên di chuyển, Enter chọn, Esc đóng.
 * `allowCustom` cho phép giữ nguyên chữ đã gõ khi không khớp mục nào (khách hàng lạ, hàng lạ).
 */
export function Combobox<D = unknown>({
  value,
  onChange,
  onSelect,
  options,
  placeholder,
  allowCustom,
  loading,
  emptyText = 'Không có kết quả phù hợp',
  disabled,
  size = 'md',
  className,
  clearable,
  autoFocus,
  id,
}: {
  value: string
  onChange: (value: string) => void
  /** Gọi khi chọn đúng một mục trong danh sách; dùng để điền thêm các ô khác. */
  onSelect?: (option: ComboOption<D>) => void
  options: ComboOption<D>[]
  placeholder?: string
  allowCustom?: boolean
  loading?: boolean
  emptyText?: string
  disabled?: boolean
  size?: 'sm' | 'md'
  className?: string
  clearable?: boolean
  autoFocus?: boolean
  id?: string
}) {
  const ctx = useFieldContext()
  const anchor = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const listRef = useRef<HTMLUListElement>(null)
  const [open, setOpen] = useState(false)
  const [focused, setFocused] = useState(false)
  const [query, setQuery] = useState('')
  const [cursor, setCursor] = useState(0)

  const selected = useMemo(() => options.find((o) => o.value === value), [options, value])
  const shownText = focused ? query : (selected?.label ?? (allowCustom ? value : ''))

  const filtered = useMemo(() => {
    const q = query.trim()
    const list = q
      ? options.filter((o) => matches(`${o.label} ${o.description ?? ''} ${o.keywords ?? ''}`, q))
      : options
    return list.slice(0, MAX_SHOWN)
  }, [options, query])

  useEffect(() => setCursor(0), [query])

  useEffect(() => {
    if (!open) return
    const el = listRef.current?.children[cursor] as HTMLElement | undefined
    el?.scrollIntoView({ block: 'nearest' })
  }, [cursor, open])

  const pick = (option: ComboOption<D>) => {
    if (option.disabled) return
    onChange(option.value)
    onSelect?.(option)
    setQuery(option.label)
    setOpen(false)
  }

  const beginEdit = () => {
    setFocused(true)
    setQuery(selected?.label ?? (allowCustom ? value : ''))
  }

  const endEdit = () => {
    setFocused(false)
    setOpen(false)
    if (!allowCustom) return
    // Chữ đã gõ chính là giá trị khi không khớp mục nào.
    const exact = options.find((o) => o.label.trim().toLowerCase() === query.trim().toLowerCase())
    if (exact) {
      if (exact.value !== value) {
        onChange(exact.value)
        onSelect?.(exact)
      }
    } else if (query !== value) onChange(query)
  }

  return (
    <>
      <div
        ref={anchor}
        className={cn(
          'control flex items-center gap-1 px-0',
          size === 'sm' && 'control-sm',
          disabled && 'cursor-not-allowed',
          className,
        )}
        aria-disabled={disabled || undefined}
        aria-invalid={ctx?.invalid || undefined}
      >
        <input
          ref={inputRef}
          id={id ?? ctx?.id}
          role="combobox"
          aria-expanded={open}
          aria-autocomplete="list"
          aria-describedby={ctx?.describedBy}
          autoFocus={autoFocus}
          disabled={disabled}
          value={shownText}
          placeholder={placeholder}
          autoComplete="off"
          onFocus={beginEdit}
          onClick={() => {
            if (!disabled) setOpen(true)
          }}
          onBlur={endEdit}
          onChange={(event) => {
            setQuery(event.target.value)
            setOpen(true)
            if (allowCustom) onChange(event.target.value)
          }}
          onKeyDown={(event) => {
            if (event.key === 'ArrowDown') {
              event.preventDefault()
              if (!open) setOpen(true)
              else setCursor((c) => Math.min(c + 1, filtered.length - 1))
            } else if (event.key === 'ArrowUp') {
              event.preventDefault()
              setCursor((c) => Math.max(c - 1, 0))
            } else if (event.key === 'Enter') {
              if (open && filtered[cursor]) {
                event.preventDefault()
                pick(filtered[cursor])
              } else if (open) {
                setOpen(false)
              }
            } else if (event.key === 'Escape' && open) {
              event.preventDefault()
              event.stopPropagation()
              setOpen(false)
            }
          }}
          className="h-full min-w-0 flex-1 bg-transparent px-2 outline-none placeholder:text-ink-3 disabled:cursor-not-allowed"
        />
        {loading && <Spinner className="mr-1 size-3 text-ink-3" />}
        {clearable && !disabled && (value || query) && (
          <button
            type="button"
            tabIndex={-1}
            aria-label="Xoá"
            onMouseDown={(event) => event.preventDefault()}
            onClick={() => {
              onChange('')
              setQuery('')
              inputRef.current?.focus()
            }}
            className="grid size-5 place-items-center rounded-sm text-ink-3 hover:bg-panel-3 hover:text-ink"
          >
            <X className="size-3" strokeWidth={2} />
          </button>
        )}
        <button
          type="button"
          tabIndex={-1}
          aria-label="Mở danh sách"
          disabled={disabled}
          onMouseDown={(event) => event.preventDefault()}
          onClick={() => {
            if (!focused) inputRef.current?.focus()
            setOpen((o) => !o)
          }}
          className="grid h-full w-6 shrink-0 place-items-center text-ink-3 hover:text-ink disabled:opacity-50"
        >
          <ChevronDown className="size-3.5" strokeWidth={1.8} />
        </button>
      </div>

      <AnchoredLayer
        anchorRef={anchor}
        open={open && !disabled}
        onClose={() => setOpen(false)}
        minWidth={280}
        label="Danh sách lựa chọn"
        className="max-w-md overflow-hidden"
      >
        <ul ref={listRef} role="listbox" className="max-h-64 overflow-y-auto py-1">
          {filtered.length === 0 && (
            <li className="px-3 py-2 text-xs text-ink-3">
              {allowCustom && query.trim() ? `Dùng "${query.trim()}"` : emptyText}
            </li>
          )}
          {filtered.map((option, index) => (
            <li
              key={option.value}
              role="option"
              aria-selected={option.value === value}
              aria-disabled={option.disabled || undefined}
              onMouseDown={(event) => event.preventDefault()}
              onMouseMove={() => setCursor(index)}
              onClick={() => pick(option)}
              className={cn(
                'flex cursor-pointer items-baseline gap-3 px-3 py-1.5 text-sm',
                index === cursor && 'bg-panel-3',
                option.value === value && 'font-medium text-brand-ink',
                option.disabled && 'cursor-not-allowed text-ink-3',
              )}
            >
              <span className="min-w-0 flex-1 truncate">{option.label}</span>
              {option.description && (
                <span className="shrink-0 text-xs text-ink-3">{option.description}</span>
              )}
            </li>
          ))}
        </ul>
      </AnchoredLayer>
    </>
  )
}
