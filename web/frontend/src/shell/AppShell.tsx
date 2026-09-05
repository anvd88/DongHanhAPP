import { useEffect, useMemo, useReducer, useState } from 'react'
import { Outlet, useLocation } from 'react-router-dom'
import { X } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { useRealtime } from '@/lib/useRealtime'
import { pushRecent, readPref, writePref } from '@/lib/prefs'
import { useBreakpoint, useIsHandheld } from '@/lib/device'
import { NAV, findRoute, groupOf } from '@/nav/navigation'
import { Sidebar, visibleRoutes } from './Sidebar'
import { ModuleTabs } from './ModuleTabs'
import { TopBar } from './TopBar'
import { CommandPalette } from './CommandPalette'
import { ShortcutsHelp } from './ShortcutsHelp'

/**
 * Trạng thái menu trái. Ba giá trị chứ không phải hai: `'auto'` nghĩa là người dùng chưa chọn gì,
 * khi đó menu tự thu gọn ở khổ laptop để trả chỗ cho bảng, và mở ra từ 1280px. Bấm nút thu gọn
 * là chọn dứt khoát và từ đó bề rộng máy không còn quyết định nữa.
 */
const COLLAPSE_KEY = 'km.nav.collapsed'
type CollapsePref = 'auto' | boolean

/**
 * Khung ứng dụng: menu trái nền navy (phân hệ), thanh trên (đơn vị, kỳ, tìm, chuông, tài khoản),
 * dải màn hình của phân hệ, và vùng làm việc nền xám nhạt. Phân hệ và màn hình đều lọc theo quyền.
 */
export function AppShell() {
  const auth = useAuth()
  const realtime = useRealtime()
  const handheld = useIsHandheld()
  const location = useLocation()
  const [, forceRender] = useReducer((n: number) => n + 1, 0)

  const breakpoint = useBreakpoint()
  const [collapsePref, setCollapsePref] = useState<CollapsePref>(() =>
    readPref<CollapsePref>(COLLAPSE_KEY, 'auto'),
  )
  const collapsed = collapsePref === 'auto' ? breakpoint === 'laptop' : collapsePref
  const [mobileNavOpen, setMobileNavOpen] = useState(false)
  const [commandOpen, setCommandOpen] = useState(false)
  const [shortcutsOpen, setShortcutsOpen] = useState(false)

  const groups = useMemo(
    () =>
      NAV.map((group) => ({ group, routes: visibleRoutes(group.routes, auth, handheld) })).filter(
        (entry) => entry.routes.length > 0,
      ),
    [auth, handheld],
  )

  const activeRoute = findRoute(location.pathname)
  const activeGroup = activeRoute ? groupOf(activeRoute) : undefined
  const activeEntry = groups.find((entry) => entry.group.id === activeGroup?.id)

  useEffect(() => {
    setMobileNavOpen(false)
    if (activeRoute && !activeRoute.hidden) pushRecent(activeRoute.path)
  }, [location.pathname, activeRoute])

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault()
        setCommandOpen((open) => !open)
        return
      }
      if (event.key === '?' && !event.ctrlKey && !event.metaKey) {
        const target = event.target as HTMLElement
        if (target.closest('input, textarea, select, [contenteditable]')) return
        event.preventDefault()
        setShortcutsOpen(true)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  const toggleCollapsed = () => {
    const next = !collapsed
    writePref(COLLAPSE_KEY, next)
    setCollapsePref(next)
  }

  return (
    <div className="flex h-dvh overflow-hidden bg-paper">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-[80] focus:rounded-sm focus:bg-panel focus:px-3 focus:py-1.5 focus:text-sm focus:text-ink focus:shadow-pop"
      >
        Bỏ qua tới nội dung
      </a>

      <div className="print-hide hidden lg:flex">
        <Sidebar
          groups={groups}
          activeGroupId={activeGroup?.id}
          realtime={realtime}
          collapsed={collapsed}
          onToggleCollapse={toggleCollapsed}
        />
      </div>

      {mobileNavOpen && (
        <div className="overlay-veil fixed inset-0 z-40 flex lg:hidden" onPointerDown={() => setMobileNavOpen(false)}>
          <div className="flex h-full shadow-modal" onPointerDown={(event) => event.stopPropagation()}>
            <Sidebar
              groups={groups}
              activeGroupId={activeGroup?.id}
              realtime={realtime}
              footer={
                <button
                  type="button"
                  aria-label="Đóng menu"
                  onClick={() => setMobileNavOpen(false)}
                  className="ml-auto grid size-7 place-items-center rounded-sm text-rail-ink hover:bg-rail-2 hover:text-rail-ink-hi"
                >
                  <X className="size-4" strokeWidth={1.7} />
                </button>
              }
            />
          </div>
        </div>
      )}

      <div className="flex min-w-0 flex-1 flex-col overflow-hidden">
        <TopBar
          onOpenCommand={() => setCommandOpen(true)}
          onOpenMobileNav={() => setMobileNavOpen(true)}
          onThemeChange={forceRender}
          onOpenShortcuts={() => setShortcutsOpen(true)}
        />
        <ModuleTabs group={activeEntry?.group} routes={activeEntry?.routes ?? []} />

        <main id="main" className="print-page min-w-0 flex-1 overflow-y-auto px-3 py-3 sm:px-4">
          <Outlet />
        </main>
      </div>

      <CommandPalette open={commandOpen} onClose={() => setCommandOpen(false)} />
      <ShortcutsHelp open={shortcutsOpen} onClose={() => setShortcutsOpen(false)} />
    </div>
  )
}
