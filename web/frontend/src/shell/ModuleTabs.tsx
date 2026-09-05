import { useCallback, useEffect, useRef, useState } from 'react'
import { NavLink } from 'react-router-dom'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { cn } from '@/lib/cn'
import type { NavGroup, NavRoute } from '@/nav/navigation'

/**
 * Dải màn hình của phân hệ đang mở: tên phân hệ bên trái, các màn hình là tab gạch chân.
 * Đây là tầng điều hướng thứ hai sau menu trái, cũng là tiêu đề của màn hình danh sách.
 *
 * Trên máy hẹp dải này dài hơn màn hình. Thanh cuộn ngang bị ẩn đi vì nó chồng lên vạch gạch chân
 * của tab đang mở; thay vào đó hai đầu có nút mũi tên, chỉ hiện khi phía đó còn tab chưa thấy.
 */
export function ModuleTabs({ group, routes }: { group?: NavGroup; routes: NavRoute[] }) {
  const scroller = useRef<HTMLDivElement>(null)
  const [edges, setEdges] = useState({ start: false, end: false })

  const measure = useCallback(() => {
    const el = scroller.current
    if (!el) return
    setEdges({
      start: el.scrollLeft > 1,
      end: el.scrollLeft + el.clientWidth < el.scrollWidth - 1,
    })
  }, [])

  useEffect(() => {
    const el = scroller.current
    if (!el) return
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(el)
    return () => observer.disconnect()
  }, [measure, routes])

  const nudge = (direction: -1 | 1) => {
    const el = scroller.current
    if (!el) return
    el.scrollBy({ left: direction * Math.max(120, el.clientWidth * 0.6), behavior: 'smooth' })
    // Không chỉ dựa vào sự kiện cuộn: cuộn mượt có thể gộp hoặc bỏ qua sự kiện, khi đó nút hai
    // đầu sẽ hiện sai. Đo lại một lần sau khi hoạt cảnh cuộn kết thúc.
    window.setTimeout(measure, 400)
  }

  if (!group) return null

  return (
    <div className="print-hide relative flex h-10 shrink-0 border-b border-line bg-panel">
      <div
        ref={scroller}
        onScroll={measure}
        className="no-scrollbar flex flex-1 items-stretch gap-4 overflow-x-auto px-3 sm:px-4"
      >
        <span className="flex shrink-0 items-center text-sm font-semibold text-ink">{group.label}</span>
        {routes.length > 1 && (
          <>
            <span aria-hidden className="my-2.5 w-px shrink-0 bg-line" />
            <nav aria-label="Màn hình của phân hệ" className="flex items-stretch gap-4">
              {routes.map((route) => (
                <NavLink
                  key={route.path}
                  to={route.path}
                  className={({ isActive }) =>
                    cn(
                      'flex shrink-0 items-center border-b-2 text-sm whitespace-nowrap transition-colors',
                      isActive
                        ? 'border-brand font-medium text-ink'
                        : 'border-transparent text-ink-2 hover:text-ink',
                    )
                  }
                >
                  {route.label}
                </NavLink>
              ))}
            </nav>
          </>
        )}
      </div>

      {edges.start && <EdgeButton side="start" onClick={() => nudge(-1)} />}
      {edges.end && <EdgeButton side="end" onClick={() => nudge(1)} />}
    </div>
  )
}

function EdgeButton({ side, onClick }: { side: 'start' | 'end'; onClick: () => void }) {
  const start = side === 'start'
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label={start ? 'Xem các màn hình bên trái' : 'Xem các màn hình bên phải'}
      className={cn(
        'absolute inset-y-0 grid w-9 place-items-center text-ink-2 hover:text-ink',
        start
          ? 'left-0 bg-linear-to-r from-panel from-60% to-transparent'
          : 'right-0 bg-linear-to-l from-panel from-60% to-transparent',
      )}
    >
      {start ? (
        <ChevronLeft className="size-4" strokeWidth={1.8} />
      ) : (
        <ChevronRight className="size-4" strokeWidth={1.8} />
      )}
    </button>
  )
}
