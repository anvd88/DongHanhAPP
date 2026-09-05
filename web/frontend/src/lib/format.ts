/** Định dạng hiển thị dùng chung cho toàn bộ web. */

/** Ký hiệu cho ô không có giá trị. Dùng gạch ngang thường, không dùng gạch dài. */
export const EMPTY = '-'

const money0 = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 })
const dec2 = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 })
const dec3 = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 3 })
const dateFmt = new Intl.DateTimeFormat('vi-VN', { day: '2-digit', month: '2-digit', year: 'numeric' })
const timeFmt = new Intl.DateTimeFormat('vi-VN', { hour: '2-digit', minute: '2-digit', hour12: false })

/** Tiền đồng, không lẻ. Số âm giữ dấu trừ để lệch dòng nhìn ra ngay. */
export const vnd = (value: number | null | undefined) =>
  value == null ? EMPTY : money0.format(value)

export const num = (value: number | null | undefined) =>
  value == null ? EMPTY : dec2.format(value)

/** Số lượng: tối đa 3 chữ số lẻ. */
export const qty = (value: number | null | undefined) =>
  value == null ? EMPTY : dec3.format(value)

export const pct = (value: number | null | undefined, digits = 1) =>
  value == null
    ? EMPTY
    : `${new Intl.NumberFormat('vi-VN', { maximumFractionDigits: digits }).format(value)}%`

/** Số có dấu: dương thêm "+", dùng cho chênh lệch so với kỳ trước. */
export const signed = (value: number | null | undefined) =>
  value == null ? EMPTY : value > 0 ? `+${money0.format(value)}` : money0.format(value)

function toDate(value: string | number | Date | null | undefined) {
  if (value == null || value === '') return null
  const d = value instanceof Date ? value : new Date(value)
  return Number.isNaN(d.getTime()) ? null : d
}

export const date = (value: string | Date | null | undefined) => {
  const d = toDate(value)
  return d ? dateFmt.format(d) : EMPTY
}

export const time = (value: string | Date | null | undefined) => {
  const d = toDate(value)
  return d ? timeFmt.format(d) : EMPTY
}

export const dateTime = (value: string | Date | null | undefined) => {
  const d = toDate(value)
  return d ? `${dateFmt.format(d)} ${timeFmt.format(d)}` : EMPTY
}

/** Khoảng cách thời gian tương đối, dùng cho chuông thông báo và nhật ký. */
export function ago(value: string | Date | null | undefined) {
  const d = toDate(value)
  if (!d) return EMPTY
  const secs = Math.round((Date.now() - d.getTime()) / 1000)
  if (secs < 60) return 'vừa xong'
  if (secs < 3600) return `${Math.floor(secs / 60)} phút trước`
  if (secs < 86400) return `${Math.floor(secs / 3600)} giờ trước`
  if (secs < 7 * 86400) return `${Math.floor(secs / 86400)} ngày trước`
  return dateFmt.format(d)
}

const pad = (n: number) => String(n).padStart(2, '0')

/** Ngày dạng yyyy-MM-dd theo giờ địa phương. */
export const toISODate = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`

export const todayISO = () => toISODate(new Date())

/** Đọc chuỗi yyyy-MM-dd thành Date địa phương; sai định dạng trả về null. */
export function parseISODate(value: string | null | undefined): Date | null {
  if (!value) return null
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(value)
  if (!m) return null
  const d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]))
  return Number.isNaN(d.getTime()) ? null : d
}

/** Đọc chuỗi người dùng gõ (dd/MM/yyyy, d/M/yyyy, ddMMyyyy, dd-MM-yyyy) thành yyyy-MM-dd. */
export function parseUserDate(text: string): string | null {
  const t = text.trim()
  if (!t) return null
  let d: number, m: number, y: number
  const sep = /^(\d{1,2})[/.-](\d{1,2})[/.-](\d{2,4})$/.exec(t)
  const compact = /^(\d{2})(\d{2})(\d{4})$/.exec(t)
  const iso = /^(\d{4})-(\d{2})-(\d{2})$/.exec(t)
  if (sep) {
    d = Number(sep[1])
    m = Number(sep[2])
    y = Number(sep[3])
    if (y < 100) y += 2000
  } else if (compact) {
    d = Number(compact[1])
    m = Number(compact[2])
    y = Number(compact[3])
  } else if (iso) {
    y = Number(iso[1])
    m = Number(iso[2])
    d = Number(iso[3])
  } else return null
  const dt = new Date(y, m - 1, d)
  if (dt.getFullYear() !== y || dt.getMonth() !== m - 1 || dt.getDate() !== d) return null
  return toISODate(dt)
}

/** Tháng dạng yyyy-MM, khớp bộ lọc tháng của backend. */
export const monthKey = (d = new Date()) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}`

/** "2026-09" → "Tháng 9/2026". */
export function monthLabel(key: string | null | undefined) {
  if (!key) return EMPTY
  const [y, m] = key.split('-')
  return `Tháng ${Number(m)}/${y}`
}

/** Dịch kỳ tháng đi n tháng. */
export function shiftMonth(key: string, delta: number) {
  const [y, m] = key.split('-').map(Number)
  const d = new Date(y, m - 1 + delta, 1)
  return monthKey(d)
}

/** Ngày đầu và cuối của một kỳ tháng, dạng yyyy-MM-dd. */
export function monthRange(key: string) {
  const [y, m] = key.split('-').map(Number)
  const from = new Date(y, m - 1, 1)
  const to = new Date(y, m, 0)
  return { from: toISODate(from), to: toISODate(to) }
}

/** Số phút thành "2 giờ 15 phút"; 0 phút trả về gạch. */
export function duration(minutes: number | null | undefined) {
  if (minutes == null) return EMPTY
  const value = Math.round(minutes)
  if (value === 0) return EMPTY
  const sign = value < 0 ? '-' : ''
  const abs = Math.abs(value)
  const h = Math.floor(abs / 60)
  const m = abs % 60
  if (h === 0) return `${sign}${m} phút`
  if (m === 0) return `${sign}${h} giờ`
  return `${sign}${h} giờ ${m} phút`
}

/** Số giờ thành "7,5 giờ". */
export const hours = (value: number | null | undefined) =>
  value == null || value === 0 ? EMPTY : `${dec2.format(value)} giờ`

export const initials = (name: string) =>
  name
    .trim()
    .split(/\s+/)
    .slice(-2)
    .map((p) => p[0] ?? '')
    .join('')
    .toUpperCase() || '?'

/**
 * Đọc số từ ô nhập kiểu Việt Nam: dấu chấm ngăn hàng nghìn, dấu phẩy là phần lẻ.
 * Cho phép gõ "1.234.567", "1234567", "1234,5". Không đọc được thì trả về null.
 */
export function parseAmount(raw: string, allowNegative = false): number | null {
  let cleaned = raw.replace(/\s/g, '').replace(/\./g, '').replace(/,/g, '.')
  cleaned = cleaned.replace(allowNegative ? /[^0-9.-]/g : /[^0-9.]/g, '')
  if (!cleaned || cleaned === '-' || cleaned === '.') return null
  const value = Number(cleaned)
  return Number.isFinite(value) ? value : null
}

/** Hiển thị số trong ô nhập: ngăn hàng nghìn, phần lẻ theo số chữ số cho phép. */
export function formatAmountInput(value: number, decimals = 0) {
  return new Intl.NumberFormat('vi-VN', {
    minimumFractionDigits: 0,
    maximumFractionDigits: decimals,
  }).format(value)
}
