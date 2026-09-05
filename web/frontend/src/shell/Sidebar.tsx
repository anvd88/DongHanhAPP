import { NavLink } from 'react-router-dom'
import { PanelLeftClose, PanelLeftOpen } from 'lucide-react'
import { cn } from '@/lib/cn'
import type { NavGroup, NavRoute } from '@/nav/navigation'
import type { RealtimeStatus } from '@/lib/realtime'

const STATUS_TEXT: Record<RealtimeStatus, string> = {
  live: 'Đã kết nối máy chủ',
  connecting: 'Đang kết nối',
  offline: 'Mất kết nối, đang thử lại',
}

export interface MenuGroup {
  group: NavGroup
  routes: NavRoute[]
}

/**
 * Menu trái: danh sách phân hệ, một tầng, nền navy. Các màn hình của phân hệ nằm ở dải tab
 * trong vùng nội dung, nhờ vậy menu không đổi chiều cao khi chuyển phân hệ.
 */
export function Sidebar({
  groups,
  activeGroupId,
  realtime,
  collapsed,
  onToggleCollapse,
  footer,
}: {
  groups: MenuGroup[]
  activeGroupId?: string
  realtime: RealtimeStatus
  collapsed?: boolean
  onToggleCollapse?: () => void
  footer?: React.ReactNode
}) {
  return (
    <nav
      aria-label="Phân hệ"
      className={cn(
        'flex shrink-0 flex-col bg-rail text-rail-ink',
        collapsed ? 'w-14' : 'w-56',
      )}
    >
      <div className={cn('flex h-12 shrink-0 items-center border-b border-rail-line', collapsed ? 'justify-center' : 'gap-2.5 px-3')}>
        <NavLink to="/" aria-label="Trang chủ" className="flex min-w-0 items-center gap-2.5">
          <BrandMark />
          {!collapsed && (
            <span className="truncate text-sm font-semibold tracking-tight text-rail-ink-hi">KetoanMini</span>
          )}
        </NavLink>
        {footer}
      </div>

      <ul className="flex-1 overflow-y-auto py-2">
        {groups.map(({ group, routes }) => {
          const Icon = group.icon
          const active = group.id === activeGroupId
          return (
            <li key={group.id}>
              <NavLink
                to={routes[0].path}
                data-active={active}
                title={collapsed ? group.label : undefined}
                aria-label={collapsed ? group.label : undefined}
                className={cn(
                  'rail-item flex h-9 items-center gap-2.5 text-[13px]',
                  collapsed ? 'justify-center px-0' : 'px-3',
                  active
                    ? 'bg-rail-active font-medium text-rail-ink-hi'
                    : 'text-rail-ink hover:bg-rail-2 hover:text-rail-ink-hi',
                )}
              >
                <Icon className={cn('size-[17px] shrink-0', active && 'text-rail-accent')} strokeWidth={1.7} />
                {!collapsed && <span className="truncate">{group.label}</span>}
              </NavLink>
            </li>
          )
        })}
      </ul>

      <div className={cn('flex shrink-0 items-center border-t border-rail-line', collapsed ? 'h-12 justify-center' : 'h-10 gap-2 px-3')}>
        {!collapsed && (
          <span className="flex min-w-0 flex-1 items-center gap-2 text-2xs text-rail-ink">
            <StatusDot status={realtime} />
            <span className="truncate">{STATUS_TEXT[realtime]}</span>
          </span>
        )}
        {onToggleCollapse && (
          <button
            type="button"
            onClick={onToggleCollapse}
            title={collapsed ? 'Mở rộng menu' : 'Thu gọn menu'}
            aria-label={collapsed ? 'Mở rộng menu' : 'Thu gọn menu'}
            className="hidden size-7 place-items-center rounded-sm text-rail-ink hover:bg-rail-2 hover:text-rail-ink-hi lg:grid"
          >
            {collapsed ? (
              <PanelLeftOpen className="size-4" strokeWidth={1.7} />
            ) : (
              <PanelLeftClose className="size-4" strokeWidth={1.7} />
            )}
          </button>
        )}
      </div>
    </nav>
  )
}

/** Đèn báo đường truyền: đây là trạng thái thật của kết nối, không phải trang trí. */
function StatusDot({ status }: { status: RealtimeStatus }) {
  return (
    <span
      aria-hidden
      className={cn(
        'inline-block size-1.5 shrink-0 rounded-full',
        status === 'live' && 'bg-rail-accent',
        status === 'connecting' && 'bg-warn',
        status === 'offline' && 'bg-danger',
      )}
    />
  )
}

function BrandMark() {
  return (
    <span
      aria-hidden
      className="grid size-7 shrink-0 place-items-center rounded-sm bg-rail-accent text-rail"
    >
      <svg viewBox="0 0 24 24" className="size-4" fill="none">
        <path d="M5 6.5h14M5 12h14M5 17.5h8" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" />
      </svg>
    </span>
  )
}

/** Màn hình có hợp với loại máy đang dùng không. Xem `deviceScope` ở navigation.ts. */
export function fitsDevice(route: NavRoute, handheld: boolean) {
  if (route.deviceScope === 'handheld') return handheld
  if (route.deviceScope === 'desktop') return !handheld
  return true
}

/**
 * Lọc danh sách màn hình theo quyền của tài khoản trước khi dựng menu. Màn hình không hợp loại máy
 * bị bỏ khỏi menu, nhưng route vẫn còn nên đường dẫn cũ không gãy.
 */
export function visibleRoutes(
  routes: NavRoute[],
  check: { can: (p?: string) => boolean; canAny: (p?: readonly string[]) => boolean },
  handheld = true,
) {
  return routes.filter(
    (route) =>
      !route.hidden &&
      fitsDevice(route, handheld) &&
      check.can(route.requires) &&
      check.canAny(route.requiresAny),
  )
}
