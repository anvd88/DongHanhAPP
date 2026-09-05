import { useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { useNavigate } from 'react-router-dom'
import { CornerDownLeft } from 'lucide-react'
import { cn } from '@/lib/cn'
import { matches } from '@/lib/text'
import { readRecent } from '@/lib/prefs'
import { useAuth } from '@/auth/AuthProvider'
import { useIsHandheld } from '@/lib/device'
import { NAV, ROUTE_ICONS, type NavGroup, type NavRoute } from '@/nav/navigation'
import { visibleRoutes } from './Sidebar'
import { Kbd } from '@/ui'

interface Entry {
  route: NavRoute
  group: NavGroup
}

/**
 * Bảng lệnh Ctrl+K. Chỉ liệt kê màn hình mà tài khoản có quyền; tìm không dấu.
 * Khi chưa gõ gì thì gợi ý các màn hình mở gần đây.
 */
export function CommandPalette({ open, onClose }: { open: boolean; onClose: () => void }) {
  const navigate = useNavigate()
  const auth = useAuth()
  const handheld = useIsHandheld()
  const [query, setQuery] = useState('')
  const [cursor, setCursor] = useState(0)
  const inputRef = useRef<HTMLInputElement>(null)

  const entries = useMemo<Entry[]>(
    () =>
      NAV.flatMap((group) =>
        visibleRoutes(group.routes, auth, handheld).map((route) => ({ route, group })),
      ),
    [auth, handheld],
  )

  const sections = useMemo(() => {
    const q = query.trim()
    if (q) {
      const hits = entries
        .filter(({ route, group }) => matches(`${route.label} ${group.label} ${route.keywords ?? ''}`, q))
        .slice(0, 14)
      return [{ title: 'Màn hình', items: hits }]
    }
    const recentPaths = readRecent()
    const recent = recentPaths
      .map((path) => entries.find((entry) => entry.route.path === path))
      .filter((entry): entry is Entry => !!entry)
      .slice(0, 6)
    const rest = entries.filter((entry) => !recent.includes(entry)).slice(0, 10)
    const list = []
    if (recent.length) list.push({ title: 'Mở gần đây', items: recent })
    list.push({ title: 'Màn hình', items: rest })
    return list
  }, [entries, query])

  const flat = useMemo(() => sections.flatMap((section) => section.items), [sections])

  useEffect(() => {
    if (open) {
      setQuery('')
      setCursor(0)
      queueMicrotask(() => inputRef.current?.focus())
    }
  }, [open])

  useEffect(() => setCursor(0), [query])

  if (!open) return null

  const go = (entry?: Entry) => {
    if (!entry) return
    navigate(entry.route.path)
    onClose()
  }

  let runningIndex = -1

  return createPortal(
    <div
      className="overlay-veil fixed inset-0 z-50 flex items-start justify-center px-4 pt-[10vh]"
      onPointerDown={onClose}
    >
      <div
        role="dialog"
        aria-label="Tìm màn hình"
        className="pop-panel w-full max-w-xl overflow-hidden"
        onPointerDown={(event) => event.stopPropagation()}
      >
        <input
          ref={inputRef}
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Escape') onClose()
            if (event.key === 'ArrowDown') {
              event.preventDefault()
              setCursor((c) => Math.min(c + 1, flat.length - 1))
            }
            if (event.key === 'ArrowUp') {
              event.preventDefault()
              setCursor((c) => Math.max(c - 1, 0))
            }
            if (event.key === 'Enter') {
              event.preventDefault()
              go(flat[cursor])
            }
          }}
          placeholder="Gõ tên màn hình, ví dụ: phiếu bán, công nợ, bảng lương"
          className="h-12 w-full border-b border-line bg-transparent px-4 text-sm text-ink placeholder:text-ink-3 focus:outline-none"
        />

        <div className="max-h-[24rem] overflow-y-auto py-1">
          {flat.length === 0 && (
            <p className="px-4 py-6 text-center text-sm text-ink-3">Không có màn hình nào khớp</p>
          )}
          {sections.map((section) =>
            section.items.length === 0 ? null : (
              <div key={section.title}>
                <p className="px-4 pt-2 pb-1 text-2xs font-semibold text-ink-3">{section.title}</p>
                <ul>
                  {section.items.map((entry) => {
                    runningIndex += 1
                    const index = runningIndex
                    const Icon = ROUTE_ICONS[entry.route.path] ?? entry.group.icon
                    const active = index === cursor
                    return (
                      <li key={entry.route.path}>
                        <button
                          type="button"
                          onMouseMove={() => setCursor(index)}
                          onClick={() => go(entry)}
                          className={cn(
                            'mx-1 flex w-[calc(100%-0.5rem)] items-center gap-3 rounded-sm px-3 py-2 text-left',
                            active ? 'bg-brand-wash' : 'hover:bg-panel-2',
                          )}
                        >
                          <Icon
                            className={cn('size-4 shrink-0', active ? 'text-brand' : 'text-ink-3')}
                            strokeWidth={1.7}
                          />
                          <span className="min-w-0 flex-1">
                            <span className="block truncate text-sm text-ink">{entry.route.label}</span>
                            <span className="block truncate text-xs text-ink-3">{entry.group.label}</span>
                          </span>
                          {active && <CornerDownLeft className="size-3.5 shrink-0 text-ink-3" strokeWidth={1.7} />}
                        </button>
                      </li>
                    )
                  })}
                </ul>
              </div>
            ),
          )}
        </div>

        <footer className="flex items-center gap-3 border-t border-line bg-panel-2 px-4 py-2 text-2xs text-ink-3">
          <span className="flex items-center gap-1">
            <Kbd>↑</Kbd>
            <Kbd>↓</Kbd> chọn
          </span>
          <span className="flex items-center gap-1">
            <Kbd>Enter</Kbd> mở
          </span>
          <span className="flex items-center gap-1">
            <Kbd>Esc</Kbd> đóng
          </span>
        </footer>
      </div>
    </div>,
    document.body,
  )
}
