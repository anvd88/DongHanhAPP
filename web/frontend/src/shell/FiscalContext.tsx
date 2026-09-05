import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import { monthKey } from '@/lib/format'
import { readPref, writePref } from '@/lib/prefs'

/**
 * Ngữ cảnh làm việc chung của thanh trên: đơn vị và kỳ kế toán đang xem. Các màn hình sổ sách
 * lấy kỳ mặc định từ đây để người dùng không phải chọn lại kỳ ở từng màn hình.
 */
interface FiscalValue {
  company: string
  year: number
  setYear: (year: number) => void
  /** Kỳ tháng đang xem, dạng yyyy-MM. */
  period: string
  setPeriod: (period: string) => void
}

const FiscalContext = createContext<FiscalValue | null>(null)

const PERIOD_KEY = 'km.fiscal.period'

export function FiscalProvider({ children }: { children: ReactNode }) {
  const [period, setPeriodState] = useState(() => readPref<string>(PERIOD_KEY, monthKey()))

  const setPeriod = useCallback((next: string) => {
    setPeriodState(next)
    writePref(PERIOD_KEY, next)
  }, [])

  const value = useMemo<FiscalValue>(
    () => ({
      company: 'KetoanMini',
      year: Number(period.slice(0, 4)),
      setYear: (year) => setPeriod(`${year}-${period.slice(5, 7)}`),
      period,
      setPeriod,
    }),
    [period, setPeriod],
  )

  return <FiscalContext.Provider value={value}>{children}</FiscalContext.Provider>
}

export function useFiscal() {
  const value = useContext(FiscalContext)
  if (!value) throw new Error('useFiscal phải nằm trong <FiscalProvider>')
  return value
}
