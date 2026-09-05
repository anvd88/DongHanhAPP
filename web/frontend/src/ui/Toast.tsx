import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react'
import { X } from 'lucide-react'
import { cn } from '@/lib/cn'

type ToastTone = 'ok' | 'danger' | 'info' | 'warn'

interface ToastItem {
  id: number
  tone: ToastTone
  title: string
  message?: string
}

interface ToastApi {
  notify: (toast: Omit<ToastItem, 'id'>) => void
  success: (title: string, message?: string) => void
  error: (title: string, message?: string) => void
  info: (title: string, message?: string) => void
}

const ToastContext = createContext<ToastApi | null>(null)

const BORDER: Record<ToastTone, string> = {
  ok: 'border-l-ok',
  danger: 'border-l-danger',
  info: 'border-l-brand',
  warn: 'border-l-warn',
}

/**
 * Thông báo ngắn ở góc dưới phải: kết quả lưu/huỷ/thao tác hàng loạt. Lỗi ở lâu hơn để đọc kịp.
 * Không dùng cho lỗi nhập liệu (lỗi đó nằm ngay dưới ô nhập).
 */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([])
  const counter = useRef(0)

  const remove = useCallback((id: number) => setItems((prev) => prev.filter((t) => t.id !== id)), [])

  const notify = useCallback(
    (toast: Omit<ToastItem, 'id'>) => {
      const id = ++counter.current
      setItems((prev) => [...prev.slice(-4), { ...toast, id }])
      window.setTimeout(() => remove(id), toast.tone === 'danger' ? 7000 : 4000)
    },
    [remove],
  )

  const api = useMemo<ToastApi>(
    () => ({
      notify,
      success: (title, message) => notify({ tone: 'ok', title, message }),
      error: (title, message) => notify({ tone: 'danger', title, message }),
      info: (title, message) => notify({ tone: 'info', title, message }),
    }),
    [notify],
  )

  return (
    <ToastContext.Provider value={api}>
      {children}
      <div
        aria-live="polite"
        className="pointer-events-none fixed right-4 bottom-4 z-[70] flex w-80 max-w-[calc(100vw-2rem)] flex-col gap-2"
      >
        {items.map((item) => (
          <div
            key={item.id}
            role="status"
            className={cn(
              'pointer-events-auto flex items-start gap-3 rounded-md border border-line border-l-2 bg-panel px-3 py-2.5 shadow-pop',
              BORDER[item.tone],
            )}
            style={{ animation: 'km-toast-in 0.16s var(--ease-out-soft)' }}
          >
            <div className="min-w-0 flex-1">
              <p className="text-sm font-medium text-ink">{item.title}</p>
              {item.message && <p className="mt-0.5 text-xs text-ink-2">{item.message}</p>}
            </div>
            <button
              type="button"
              aria-label="Đóng thông báo"
              onClick={() => remove(item.id)}
              className="grid size-6 shrink-0 place-items-center rounded-sm text-ink-3 hover:bg-panel-3 hover:text-ink"
            >
              <X className="size-3.5" strokeWidth={1.8} />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}

export function useToast() {
  const value = useContext(ToastContext)
  if (!value) throw new Error('useToast phải nằm trong <ToastProvider>')
  return value
}
