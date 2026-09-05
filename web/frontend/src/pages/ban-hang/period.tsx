import { monthRange } from '@/lib/format'
import { DateRangePicker, MonthPicker, Segmented, Select, type DateRange } from '@/ui'
import type { DebtPeriod } from '@/api/sales'

/**
 * Bộ chọn kỳ cho các màn hình công nợ.
 *
 * Bốn chế độ chứ không phải một bộ chọn khoảng ngày duy nhất: kế toán xem theo tháng và theo năm là
 * chính, hai thao tác đó phải nhanh. Khoảng tự chọn giữ lại cho những lần đối chiếu lệch kỳ, còn
 * "Tất cả" là cách đọc cũ của màn hình trước khi có bộ lọc này.
 */
type Mode = 'month' | 'year' | 'range' | 'all'

export interface PeriodValue {
  mode: Mode
  /** Dạng yyyy-MM. */
  month: string
  year: number
  range: DateRange
}

export function initialPeriod(fiscalPeriod: string): PeriodValue {
  return {
    mode: 'month',
    month: fiscalPeriod,
    year: Number(fiscalPeriod.slice(0, 4)),
    range: { from: '', to: '' },
  }
}

/** Kỳ gửi lên máy chủ. Trả về undefined nghĩa là không lọc. */
export function periodOf(value: PeriodValue): DebtPeriod | undefined {
  if (value.mode === 'all') return undefined
  if (value.mode === 'month') return monthRange(value.month)
  if (value.mode === 'year') return { from: `${value.year}-01-01`, to: `${value.year}-12-31` }
  if (!value.range.from && !value.range.to) return undefined
  return { from: value.range.from || undefined, to: value.range.to || undefined }
}

/** Nhãn ngắn để ghi lên dải số liệu và tiêu đề, ví dụ "tháng 09/2026". */
export function periodLabel(value: PeriodValue): string {
  if (value.mode === 'month') return `tháng ${value.month.slice(5)}/${value.month.slice(0, 4)}`
  if (value.mode === 'year') return `năm ${value.year}`
  if (value.mode === 'range' && periodOf(value)) return 'kỳ đã chọn'
  return 'toàn bộ'
}

export function PeriodPicker({
  value,
  onChange,
  years = 5,
}: {
  value: PeriodValue
  onChange: (next: PeriodValue) => void
  years?: number
}) {
  const options = Array.from({ length: years }, (_, i) => value.year + 1 - i)
    .filter((year) => year <= new Date().getFullYear() + 1)

  return (
    <>
      <Segmented
        items={[
          { id: 'month', label: 'Tháng' },
          { id: 'year', label: 'Năm' },
          { id: 'range', label: 'Khoảng' },
          { id: 'all', label: 'Tất cả' },
        ]}
        active={value.mode}
        onChange={(mode) => onChange({ ...value, mode: mode as Mode })}
      />
      {value.mode === 'month' && (
        <MonthPicker
          size="sm"
          className="w-40"
          value={value.month}
          onChange={(month) => onChange({ ...value, month, year: Number(month.slice(0, 4)) })}
        />
      )}
      {value.mode === 'year' && (
        <Select
          size="sm"
          className="w-28"
          value={value.year}
          onChange={(e) => onChange({ ...value, year: Number(e.target.value) })}
          aria-label="Năm"
        >
          {options.map((year) => (
            <option key={year} value={year}>
              {year}
            </option>
          ))}
        </Select>
      )}
      {value.mode === 'range' && (
        <DateRangePicker size="sm" value={value.range} onChange={(range) => onChange({ ...value, range })} />
      )}
    </>
  )
}
