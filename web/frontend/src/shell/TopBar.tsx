import { Link } from 'react-router-dom'
import { Building2, ChevronDown, CircleHelp, Menu, Search } from 'lucide-react'
import { monthLabel } from '@/lib/format'
import { useAuth } from '@/auth/AuthProvider'
import { PERM } from '@/lib/permissions'
import { MonthPicker, Select, Field } from '@/ui'
import { useFiscal } from './FiscalContext'
import { NotificationBell } from './NotificationBell'
import { UserMenu } from './UserMenu'
import { Popover } from './Popover'

/**
 * Thanh trên: đơn vị và kỳ đang xem, ô tìm màn hình, trợ giúp, chuông, tài khoản.
 * Không đặt hành động nghiệp vụ tại đây; hành động thuộc thanh công cụ của từng màn hình.
 */
export function TopBar({
  onOpenCommand,
  onOpenMobileNav,
  onThemeChange,
  onOpenShortcuts,
}: {
  onOpenCommand: () => void
  onOpenMobileNav: () => void
  onThemeChange: () => void
  onOpenShortcuts: () => void
}) {
  const fiscal = useFiscal()
  const auth = useAuth()
  const thisYear = new Date().getFullYear()
  const years = Array.from({ length: 4 }, (_, i) => thisYear - 2 + i)

  return (
    <header className="print-hide flex h-12 shrink-0 items-center gap-1.5 border-b border-line bg-panel px-2 sm:px-3">
      <button
        type="button"
        onClick={onOpenMobileNav}
        aria-label="Mở menu"
        className="grid size-8 place-items-center rounded-sm text-ink-2 hover:bg-panel-3 hover:text-ink lg:hidden"
      >
        <Menu className="size-[18px]" strokeWidth={1.7} />
      </button>

      <Popover
        label="Đơn vị và kỳ kế toán"
        align="start"
        panelClassName="w-72"
        trigger={({ toggle, open }) => (
          <button
            type="button"
            onClick={toggle}
            aria-expanded={open}
            className="flex h-8 items-center gap-2 rounded-sm px-2 text-left hover:bg-panel-3"
          >
            <Building2 className="size-4 shrink-0 text-ink-3" strokeWidth={1.7} />
            <span className="min-w-0">
              <span className="block truncate text-sm font-semibold text-ink">{fiscal.company}</span>
              <span className="hidden max-w-56 truncate text-2xs leading-none whitespace-nowrap text-ink-3 sm:block">
                Năm tài chính {fiscal.year} · {monthLabel(fiscal.period)}
              </span>
            </span>
            <ChevronDown className="size-3.5 shrink-0 text-ink-3" strokeWidth={1.8} />
          </button>
        )}
      >
        {() => (
          <div className="flex flex-col gap-3 p-3">
            <div>
              <p className="text-xs text-ink-3">Đơn vị</p>
              <p className="text-sm font-semibold text-ink">{fiscal.company}</p>
            </div>
            <Field label="Năm tài chính">
              <Select value={fiscal.year} onChange={(event) => fiscal.setYear(Number(event.target.value))} size="sm">
                {years.map((year) => (
                  <option key={year} value={year}>
                    {year}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Kỳ đang xem">
              <MonthPicker value={fiscal.period} onChange={fiscal.setPeriod} size="sm" />
            </Field>
          </div>
        )}
      </Popover>

      <button
        type="button"
        onClick={onOpenCommand}
        className="ml-auto flex h-8 shrink-0 items-center gap-2 rounded-md border border-line bg-panel px-2.5 text-xs whitespace-nowrap text-ink-3 hover:border-ink-3 hover:text-ink-2 lg:w-64"
      >
        <Search className="size-3.5 shrink-0" strokeWidth={1.8} />
        <span className="hidden lg:inline">Tìm màn hình, chứng từ</span>
        <kbd className="kbd ml-auto hidden lg:inline-block">Ctrl K</kbd>
      </button>

      {auth.can(PERM.portalRead) ? (
        <Link
          to="/tro-giup"
          aria-label="Trợ giúp"
          title="Trợ giúp"
          className="grid size-8 place-items-center rounded-sm text-ink-2 hover:bg-panel-3 hover:text-ink"
        >
          <CircleHelp className="size-[18px]" strokeWidth={1.7} />
        </Link>
      ) : (
        <button
          type="button"
          onClick={onOpenShortcuts}
          aria-label="Phím tắt"
          title="Phím tắt"
          className="grid size-8 place-items-center rounded-sm text-ink-2 hover:bg-panel-3 hover:text-ink"
        >
          <CircleHelp className="size-[18px]" strokeWidth={1.7} />
        </button>
      )}
      <NotificationBell />
      <UserMenu onThemeChange={onThemeChange} onOpenShortcuts={onOpenShortcuts} />
    </header>
  )
}
