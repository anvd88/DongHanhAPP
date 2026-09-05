import { useNavigate } from 'react-router-dom'
import { ChevronDown, Keyboard, LogOut, MonitorSmartphone, Moon, Sun, UserCog } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { ROLE_LABELS, SCOPE_LABELS } from '@/lib/permissions'
import { applyTheme, readTheme, type ThemeChoice } from '@/lib/theme'
import { cn } from '@/lib/cn'
import { Avatar, KeyValue } from '@/ui'
import { Popover } from './Popover'

const THEMES: { id: ThemeChoice; label: string; icon: typeof Sun }[] = [
  { id: 'light', label: 'Sáng', icon: Sun },
  { id: 'dark', label: 'Tối', icon: Moon },
  { id: 'system', label: 'Theo máy', icon: MonitorSmartphone },
]

export function UserMenu({
  onThemeChange,
  onOpenShortcuts,
}: {
  onThemeChange: () => void
  onOpenShortcuts: () => void
}) {
  const { profile, user, signOut } = useAuth()
  const navigate = useNavigate()
  const theme = readTheme()

  const name = profile?.fullName || profile?.username || 'Tài khoản'
  const roles = profile?.roleLabels?.length
    ? profile.roleLabels.join(', ')
    : ROLE_LABELS[profile?.primaryRole ?? ''] || ''

  return (
    <Popover
      label="Tài khoản"
      panelClassName="w-72"
      trigger={({ toggle, open }) => (
        <button
          type="button"
          onClick={toggle}
          aria-expanded={open}
          className="flex h-8 items-center gap-2 rounded-sm pr-1.5 pl-1 hover:bg-panel-3"
        >
          <Avatar url={user?.avatarUrl} name={name} />
          <span className="hidden text-left md:block">
            <span className="block max-w-36 truncate text-xs font-medium text-ink">{name}</span>
            {roles && <span className="block max-w-36 truncate text-2xs leading-none text-ink-3">{roles}</span>}
          </span>
          <ChevronDown className="hidden size-3.5 text-ink-3 md:block" strokeWidth={1.8} />
        </button>
      )}
    >
      {(close) => (
        <>
          <div className="flex items-center gap-3 border-b border-line px-3.5 py-3">
            <Avatar url={user?.avatarUrl} name={name} size="lg" />
            <div className="min-w-0">
              <p className="truncate text-sm font-semibold text-ink">{name}</p>
              <p className="truncate text-xs text-ink-3">@{profile?.username}</p>
            </div>
          </div>

          <div className="border-b border-line px-3.5 py-2.5">
            <KeyValue
              rows={[
                ['Vai trò', roles || null],
                ['Phạm vi', SCOPE_LABELS[profile?.scope ?? ''] ?? profile?.scope ?? null],
              ]}
              className="text-xs"
            />
          </div>

          <div className="border-b border-line px-3.5 py-2.5">
            <p className="mb-1.5 text-2xs font-semibold text-ink-3">Giao diện</p>
            <div className="inline-flex w-full overflow-hidden rounded-md border border-line">
              {THEMES.map((option) => {
                const Icon = option.icon
                const active = option.id === theme
                return (
                  <button
                    key={option.id}
                    type="button"
                    onClick={() => {
                      applyTheme(option.id)
                      onThemeChange()
                    }}
                    className={cn(
                      'flex h-7 flex-1 items-center justify-center gap-1.5 border-r border-line text-xs last:border-r-0',
                      active ? 'bg-brand-wash font-medium text-brand-ink' : 'text-ink-2 hover:bg-panel-2 hover:text-ink',
                    )}
                  >
                    <Icon className="size-3.5" strokeWidth={1.6} />
                    {option.label}
                  </button>
                )
              })}
            </div>
          </div>

          <div className="p-1">
            <MenuItem
              icon={UserCog}
              label="Hồ sơ và bảo mật"
              onClick={() => {
                navigate('/ho-so')
                close()
              }}
            />
            <MenuItem
              icon={MonitorSmartphone}
              label="Thiết bị và phiên đăng nhập"
              onClick={() => {
                navigate('/thiet-bi')
                close()
              }}
            />
            <MenuItem
              icon={Keyboard}
              label="Phím tắt"
              onClick={() => {
                close()
                onOpenShortcuts()
              }}
            />
            <MenuItem
              icon={LogOut}
              label="Đăng xuất"
              tone="danger"
              onClick={() => {
                close()
                void signOut()
              }}
            />
          </div>
        </>
      )}
    </Popover>
  )
}

function MenuItem({
  icon: Icon,
  label,
  onClick,
  tone,
}: {
  icon: typeof Sun
  label: string
  onClick: () => void
  tone?: 'danger'
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn('menu-item', tone === 'danger' ? 'text-danger' : 'text-ink')}
    >
      <Icon className="size-4 text-ink-3" strokeWidth={1.6} />
      {label}
    </button>
  )
}
