import { useMemo, useState } from 'react'
import { FileText } from 'lucide-react'
import { date, vnd } from '@/lib/format'
import { matches } from '@/lib/text'
import { debtStatementUrl, useDebts } from '@/api/sales'
import { useFiscal } from '@/shell/FiscalContext'
import { Figure, FigureStrip, Money, SearchInput } from '@/ui'
import { CustomerDrawer } from './customer-drawer'
import { PeriodPicker, initialPeriod, periodLabel, periodOf } from './period'
import { ModuleScreen } from '../_shared'

/** Công nợ phải thu, xem theo tháng, theo năm hoặc theo khoảng ngày tự chọn. */
export function DebtsPage() {
  const fiscal = useFiscal()
  const [period, setPeriod] = useState(() => initialPeriod(fiscal.period))
  const range = useMemo(() => periodOf(period), [period])
  const label = periodLabel(period)

  const debts = useDebts(range)
  const [search, setSearch] = useState('')
  const [tab, setTab] = useState('owing')
  const [openId, setOpenId] = useState<string | null>(null)
  const d = debts.data
  const rows = (d?.customers ?? []).filter((c) => {
    if (tab === 'owing' && c.balance <= 0) return false
    if (tab === 'overpaid' && c.balance >= 0) return false
    if (search && !matches(`${c.customer.name} ${c.customer.phone}`, search)) return false
    return true
  })
  const totals = rows.reduce(
    (acc, c) => ({
      carried: acc.carried + c.carriedBalance,
      sales: acc.sales + c.salesTotal,
      returns: acc.returns + c.returnsTotal,
      collected: acc.collected + c.collectedTotal,
      balance: acc.balance + c.balance,
    }),
    { carried: 0, sales: 0, returns: 0, collected: 0, balance: 0 },
  )

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure
              label="Còn phải thu cuối kỳ"
              value={d ? vnd(d.totalReceivable) : '…'}
              tone={d && d.totalReceivable > 0 ? 'warn' : undefined}
            />
            <Figure label="Khách còn nợ" value={d?.debtorCount ?? '…'} />
            <Figure label={`Đã bán ${label}`} value={d ? vnd(d.totalSales) : '…'} />
            <Figure label={`Đã thu ${label}`} value={d ? vnd(d.totalCollected) : '…'} />
          </FigureStrip>
        }
        tabs={[
          { id: 'owing', label: 'Còn nợ', count: d?.debtorCount },
          { id: 'all', label: 'Tất cả', count: d?.customers.length },
          { id: 'overpaid', label: 'Trả thừa' },
        ]}
        tab={tab}
        onTabChange={setTab}
        filters={
          <>
            <PeriodPicker value={period} onChange={setPeriod} />
            <SearchInput
              size="sm"
              className="w-56"
              placeholder="Khách hàng"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onClear={() => setSearch('')}
            />
          </>
        }
        columns={[
          { key: 'name', priority: 1, header: 'Khách hàng', cell: (row) => <span className="font-medium">{row.customer.name}</span>, sortValue: (r) => r.customer.name, total: 'Tổng cộng' },
          { key: 'carried', priority: 2, header: 'Đầu kỳ', align: 'right', cell: (row) => <Money value={row.carriedBalance} />, sortValue: (r) => r.carriedBalance, total: <Money value={totals.carried} zero="zero" /> },
          { key: 'sales', priority: 2, header: 'Đã bán', align: 'right', cell: (row) => <Money value={row.salesTotal} />, sortValue: (r) => r.salesTotal, total: <Money value={totals.sales} zero="zero" /> },
          { key: 'returns', priority: 3, header: 'Trả lại', align: 'right', cell: (row) => <Money value={row.returnsTotal} />, sortValue: (r) => r.returnsTotal, total: <Money value={totals.returns} zero="zero" /> },
          { key: 'collected', priority: 2, header: 'Đã thu', align: 'right', cell: (row) => <Money value={row.collectedTotal} />, sortValue: (r) => r.collectedTotal, total: <Money value={totals.collected} zero="zero" /> },
          { key: 'balance', priority: 1, header: 'Cuối kỳ', align: 'right', cell: (row) => <Money value={row.balance} strong />, sortValue: (r) => r.balance, total: <Money value={totals.balance} zero="zero" /> },
          { key: 'count', priority: 3, header: 'Số phiếu', align: 'right', cell: (row) => row.invoiceCount, sortValue: (r) => r.invoiceCount, hidden: true },
          { key: 'last', priority: 3, header: 'Gần nhất', cell: (row) => date(row.lastActivityDate), sortValue: (r) => r.lastActivityDate ?? '' },
          {
            key: 'pdf',
            priority: 1,
            header: 'Sổ chi tiết',
            width: '6rem',
            align: 'center',
            cell: (row) => (
              <a
                className="link inline-flex items-center gap-1"
                href={debtStatementUrl(row.customer.id, range)}
                onClick={(e) => e.stopPropagation()}
                title={`Tải sổ chi tiết công nợ ${label} của ${row.customer.name}`}
              >
                <FileText className="size-3.5" strokeWidth={1.7} />
                PDF
              </a>
            ),
          },
        ]}
        rows={rows}
        getKey={(row) => row.customer.id}
        loading={debts.isLoading}
        error={debts.error}
        onRefresh={() => debts.refetch()}
        onRowClick={(row) => setOpenId(row.customer.id)}
        activeKey={openId}
        defaultSort={{ key: 'balance', dir: 'desc' }}
        emptyTitle="Không có khách hàng nào trong bộ lọc này"
      />
      <CustomerDrawer customerId={openId} onClose={() => setOpenId(null)} initialTab="debt" period={range} />
    </>
  )
}
