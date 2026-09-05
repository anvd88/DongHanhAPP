import {
  createContext,
  useContext,
  useEffect,
  useId,
  useState,
  type AriaAttributes,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
} from 'react'
import { ChevronDown, Search, X } from 'lucide-react'
import { cn } from '@/lib/cn'
import { formatAmountInput, parseAmount } from '@/lib/format'

interface FieldCtx {
  id: string
  invalid: boolean
  describedBy?: string
}

const FieldContext = createContext<FieldCtx | null>(null)

/** Ô nhập bên trong Field tự nhận id, trạng thái lỗi và mô tả từ Field. */
export function useFieldContext() {
  return useContext(FieldContext)
}

/**
 * Khối nhãn + ô nhập + dòng gợi ý/lỗi. Nhãn luôn ở trên ô nhập (hoặc bên trái khi `inline`),
 * lỗi hiển thị dưới ô nhập. Không dùng placeholder thay nhãn.
 */
export function Field({
  label,
  hint,
  error,
  required,
  inline,
  children,
  className,
}: {
  label?: ReactNode
  hint?: ReactNode
  error?: string | null
  required?: boolean
  /** Nhãn nằm bên trái, dùng cho form ngắn kiểu bảng khoá : giá trị. */
  inline?: boolean
  children: ReactNode
  className?: string
}) {
  const id = useId()
  const hintId = `${id}-hint`
  const ctx: FieldCtx = { id, invalid: !!error, describedBy: hint || error ? hintId : undefined }
  return (
    <FieldContext.Provider value={ctx}>
      <div
        className={cn(
          'flex min-w-0 flex-col gap-1',
          inline && 'sm:flex-row sm:items-start sm:gap-3',
          className,
        )}
      >
        {label && (
          <label
            htmlFor={id}
            className={cn(
              'text-xs font-medium text-ink-2',
              inline && 'sm:w-36 sm:shrink-0 sm:pt-1.5',
            )}
          >
            {label}
            {required && (
              <span aria-hidden className="ml-0.5 text-danger">
                *
              </span>
            )}
          </label>
        )}
        <div className="min-w-0 flex-1">
          {children}
          {(error || hint) && (
            <p id={hintId} className={cn('mt-1 text-xs', error ? 'text-danger' : 'text-ink-3')}>
              {error ?? hint}
            </p>
          )}
        </div>
      </div>
    </FieldContext.Provider>
  )
}

/**
 * Lưới ô nhập chuẩn của form: một cột dưới 768px, hai cột từ 768, `cols` cột từ 1280.
 *
 * Hai cột bắt đầu ở 768 chứ không phải 640: ở 640 một ô nhập chỉ còn khoảng 300px, đủ rộng cho
 * chữ nhưng không đủ cho nhãn và thông báo lỗi bên dưới nó.
 */
export function FormGrid({
  cols = 3,
  children,
  className,
}: {
  cols?: 2 | 3 | 4
  children: ReactNode
  className?: string
}) {
  return (
    <div
      className={cn(
        'grid gap-x-4 gap-y-3 md:grid-cols-2',
        cols === 3 && 'xl:grid-cols-3',
        cols === 4 && 'xl:grid-cols-4',
        className,
      )}
    >
      {children}
    </div>
  )
}

type ControlSize = 'sm' | 'md'

function useControlProps(props: {
  id?: string
  'aria-invalid'?: AriaAttributes['aria-invalid']
  'aria-describedby'?: string
}) {
  const ctx = useFieldContext()
  return {
    id: props.id ?? ctx?.id,
    'aria-invalid': props['aria-invalid'] ?? (ctx?.invalid ? true : undefined),
    'aria-describedby': props['aria-describedby'] ?? ctx?.describedBy,
  }
}

export function Input({
  className,
  size = 'md',
  ...rest
}: Omit<InputHTMLAttributes<HTMLInputElement>, 'size'> & { size?: ControlSize }) {
  const a11y = useControlProps(rest)
  return (
    <input
      {...rest}
      {...a11y}
      className={cn('control', size === 'sm' && 'control-sm', className)}
    />
  )
}

/**
 * Ô nhập số tiền/số lượng: canh phải, chữ số đều bề rộng, tự ngăn hàng nghìn khi rời ô.
 * Giá trị trao đổi là number (null khi trống), không phải chuỗi.
 */
export function NumberInput({
  value,
  onChange,
  decimals = 0,
  allowNegative = false,
  className,
  size = 'md',
  onBlur,
  onFocus,
  ...rest
}: Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'type' | 'size'> & {
  value: number | null
  onChange: (value: number | null) => void
  decimals?: number
  allowNegative?: boolean
  size?: ControlSize
}) {
  const a11y = useControlProps(rest)
  const [focused, setFocused] = useState(false)
  const [text, setText] = useState(value == null ? '' : formatAmountInput(value, decimals))

  useEffect(() => {
    if (!focused) setText(value == null ? '' : formatAmountInput(value, decimals))
  }, [value, focused, decimals])

  return (
    <input
      {...rest}
      {...a11y}
      type="text"
      inputMode="decimal"
      value={text}
      onChange={(event) => {
        const raw = event.target.value
        setText(raw)
        onChange(parseAmount(raw, allowNegative))
      }}
      onFocus={(event) => {
        setFocused(true)
        onFocus?.(event)
      }}
      onBlur={(event) => {
        setFocused(false)
        onBlur?.(event)
      }}
      className={cn('control tnum text-right', size === 'sm' && 'control-sm', className)}
    />
  )
}

/** Ô chọn gốc của trình duyệt với mũi tên riêng để đồng nhất giữa các hệ máy. */
export function Select({
  className,
  size = 'md',
  children,
  ...rest
}: Omit<SelectHTMLAttributes<HTMLSelectElement>, 'size'> & { size?: ControlSize }) {
  const a11y = useControlProps(rest)
  return (
    <span className={cn('relative block', className)}>
      <select
        {...rest}
        {...a11y}
        className={cn('control appearance-none pr-7', size === 'sm' && 'control-sm')}
      >
        {children}
      </select>
      <ChevronDown
        aria-hidden
        className="pointer-events-none absolute top-1/2 right-2 size-3.5 -translate-y-1/2 text-ink-3"
        strokeWidth={1.8}
      />
    </span>
  )
}

export function Textarea({ className, ...rest }: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  const a11y = useControlProps(rest)
  return <textarea {...rest} {...a11y} className={cn('control', className)} />
}

export function Checkbox({
  label,
  className,
  ...rest
}: Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> & { label?: ReactNode }) {
  return (
    <label className={cn('inline-flex cursor-pointer items-center gap-2 text-sm text-ink', className)}>
      <input type="checkbox" className="checkbox" {...rest} />
      {label}
    </label>
  )
}

/** Ô tìm kiếm trên thanh công cụ: nhãn ẩn, nút xoá nhanh khi đã gõ. */
export function SearchInput({
  className,
  label = 'Tìm kiếm',
  value,
  onChange,
  onClear,
  size = 'md',
  ...rest
}: Omit<InputHTMLAttributes<HTMLInputElement>, 'size'> & {
  label?: string
  onClear?: () => void
  size?: ControlSize
}) {
  const id = useId()
  const hasValue = typeof value === 'string' && value.length > 0
  return (
    <div className={cn('relative', className)}>
      <label htmlFor={id} className="sr-only">
        {label}
      </label>
      <input
        id={id}
        type="search"
        value={value}
        onChange={onChange}
        className={cn('control pl-7', hasValue && 'pr-7', size === 'sm' && 'control-sm')}
        {...rest}
      />
      <Search
        aria-hidden
        className="pointer-events-none absolute top-1/2 left-2 size-3.5 -translate-y-1/2 text-ink-3"
        strokeWidth={1.8}
      />
      {hasValue && onClear && (
        <button
          type="button"
          aria-label="Xoá tìm kiếm"
          onClick={onClear}
          className="absolute top-1/2 right-1.5 grid size-5 -translate-y-1/2 place-items-center rounded-sm text-ink-3 hover:bg-panel-3 hover:text-ink"
        >
          <X className="size-3" strokeWidth={2} />
        </button>
      )}
    </div>
  )
}
