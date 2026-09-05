import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useQueries } from '@tanstack/react-query'
import { ChevronRight, Download, Printer } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { PERM } from '@/lib/permissions'
import { api } from '@/lib/http'
import { date, dateTime, monthLabel, shiftMonth, vnd } from '@/lib/format'
import { downloadCsv } from '@/lib/export'
import { useFiscal } from '@/shell/FiscalContext'
import {
  useCashBalance,
  useDashboard,
  useReports,
  useWorklist,
  worklistRoute,
  type CashBalance,
  type WorklistItem,
} from '@/api/accounting'
import { documentStatus, useDebts, useSalesDocuments } from '@/api/sales'
import { usePurchases, useSuppliers } from '@/api/purchases'
import {
  Button,
  DataTable,
  Figure,
  FigureStrip,
  Money,
  MonthPicker,
  Panel,
  PanelHeader,
  Skeleton,
  Stack,
  StatusBadge,
  type Tone,
} from '@/ui'
import { ModuleScreen } from './_shared'

/* ============================================================================
   Bảng điều hành
   ========================================================================== */

/**
 * Trang đích của kế toán và ban giám đốc. Trả lời câu hỏi "hôm nay cần xử lý gì" trước, rồi mới
 * tới số liệu của kỳ; không xếp hàng thẻ chỉ số giống nhau.
 */
export function DashboardPage() {
  const auth = useAuth()
  const fiscal = useFiscal()
  const period = fiscal.period
  const canAccounting = auth.can(PERM.accountingAccess)
  const canCash = auth.can(PERM.cashFundRead)
  const canWorklist = auth.can(PERM.requestsSelf)

  const dashboard = useDashboard(canAccounting)
  const documents = useSalesDocuments()
  const debts = useDebts(undefined, canAccounting)
  const purchases = usePurchases()
  const suppliers = useSuppliers()
  const cash = useCashBalance(period, canCash)
  const worklist = useWorklist(canWorklist)

  const months = useMemo(() => Array.from({ length: 6 }, (_, i) => shiftMonth(period, -(5 - i))), [period])
  const cashByMonth = useQueries({
    queries: months.map((month) => ({
      queryKey: ['cash', 'cash-fund', 'balance', month],
      queryFn: () => api.get<CashBalance>('/cash-fund/balance', { query: { month } }),
      enabled: canCash,
      staleTime: 60_000,
    })),
  })

  const docs = documents.data ?? []

  const draftCount = docs.filter((d) => documentStatus(d).id === 'draft').length
  const deliveringDocs = docs.filter((d) => ['delivering', 'submitted'].includes(documentStatus(d).id))
  const openPurchases = (purchases.data?.items ?? []).filter((p) => !p.cancelledAt && p.remaining > 0)
  const payable = (suppliers.data?.items ?? []).reduce((sum, s) => sum + Math.max(s.balance, 0), 0)

  const attention: AttentionItem[] = [
    { label: 'Phiếu bán chưa phát hành', count: draftCount, to: '/ban-hang', loading: documents.isLoading },
    {
      label: 'Phiếu đang giao chưa về kho',
      count: deliveringDocs.length,
      to: '/giao-hang',
      loading: documents.isLoading,
    },
    {
      label: 'Khách hàng còn nợ',
      count: debts.data?.debtorCount,
      sub: debts.data ? `Tổng ${vnd(debts.data.totalReceivable)}` : undefined,
      to: '/cong-no',
      loading: debts.isLoading,
    },
    {
      label: 'Phiếu nhập chưa trả hết',
      count: openPurchases.length,
      sub: openPurchases.length ? `Còn ${vnd(openPurchases.reduce((s, p) => s + p.remaining, 0))}` : undefined,
      to: '/mua-hang',
      loading: purchases.isLoading,
    },
    ...(canWorklist
      ? [
          {
            label: 'Đơn chờ tôi duyệt',
            count: worklist.data?.summary.approvals,
            to: '/pheduyet',
            loading: worklist.isLoading,
          } satisfies AttentionItem,
          {
            label: 'Việc quá hạn',
            count: worklist.data?.summary.overdue,
            to: '/viec-can-lam',
            tone: 'danger' as Tone,
            loading: worklist.isLoading,
          } satisfies AttentionItem,
        ]
      : []),
  ]

  const pendingDocs = docs
    .filter((d) => !['cancelled', 'returned'].includes(documentStatus(d).id))
    .slice(0, 8)

  return (
    <Stack>
      <div className="grid gap-3 xl:grid-cols-12">
        <Panel className="xl:col-span-7" title="Cần xử lý" meta={date(new Date())}>
          <AttentionList items={attention} />
        </Panel>

        <Panel
          className="xl:col-span-5"
          title={`Số liệu ${monthLabel(period).toLowerCase()}`}
          actions={
            auth.can(PERM.reportRead) && (
              <Link to="/bao-cao" className="link text-xs">
                Báo cáo
              </Link>
            )
          }
        >
          <FigureRows
            rows={[
              {
                label: 'Doanh thu bán hàng',
                value: <Money value={dashboard.data?.monthRevenue} zero="zero" strong />,
                to: '/ban-hang',
                loading: dashboard.isLoading,
              },
              {
                label: 'Phải thu khách hàng',
                value: <Money value={debts.data?.totalReceivable} zero="zero" />,
                to: '/cong-no',
                loading: debts.isLoading,
              },
              {
                label: 'Phải trả nhà cung cấp',
                value: <Money value={suppliers.data ? payable : undefined} zero="zero" />,
                to: '/nha-cung-cap',
                loading: suppliers.isLoading,
              },
              ...(canCash
                ? [
                    {
                      label: 'Tồn quỹ tiền mặt',
                      value: <Money value={cash.data?.balance} zero="zero" strong />,
                      to: '/quy-tien-mat',
                      loading: cash.isLoading,
                    },
                  ]
                : []),
            ]}
          />
        </Panel>
      </div>

      <div className="grid gap-3 xl:grid-cols-2">
        <Panel
          title="Phiếu bán chờ xử lý"
          meta={documents.data ? `${pendingDocs.length} phiếu gần nhất` : undefined}
          actions={
            <Link to="/ban-hang" className="link text-xs">
              Xem tất cả
            </Link>
          }
        >
          <DataTable
            columns={[
              {
                key: 'voucherNo', priority: 1,
                header: 'Số phiếu',
                cell: (row) => (
                  <Link to={`/ban-hang/${row.id}`} className="link font-medium">
                    {row.voucherNo}
                  </Link>
                ),
              },
              { key: 'customer', priority: 1, header: 'Khách hàng', cell: (row) => row.customerName, truncate: true },
              { key: 'date', priority: 2, header: 'Ngày', cell: (row) => date(row.date) },
              { key: 'total', priority: 1, header: 'Tổng tiền', align: 'right', cell: (row) => <Money value={row.total} /> },
              {
                key: 'status', priority: 1,
                header: 'Trạng thái',
                cell: (row) => {
                  const s = documentStatus(row)
                  return <StatusBadge tone={s.tone}>{s.label}</StatusBadge>
                },
              },
            ]}
            rows={pendingDocs}
            getKey={(row) => row.id}
            loading={documents.isLoading}
            emptyTitle="Không có phiếu nào đang chờ"
            density="compact"
          />
        </Panel>

        <Panel
          title="Phiếu nhập còn nợ nhà cung cấp"
          meta={purchases.data ? `${openPurchases.length} phiếu` : undefined}
          actions={
            <Link to="/mua-hang" className="link text-xs">
              Xem tất cả
            </Link>
          }
        >
          <DataTable
            columns={[
              { key: 'voucherNo', priority: 1, header: 'Số phiếu', cell: (row) => <span className="font-medium">{row.voucherNo}</span> },
              { key: 'supplier', priority: 1, header: 'Nhà cung cấp', cell: (row) => row.supplierName, truncate: true },
              { key: 'date', priority: 2, header: 'Ngày', cell: (row) => date(row.docDate) },
              { key: 'total', priority: 1, header: 'Tổng tiền', align: 'right', cell: (row) => <Money value={row.total} /> },
              { key: 'remaining', priority: 2, header: 'Còn nợ', align: 'right', cell: (row) => <Money value={row.remaining} strong /> },
            ]}
            rows={openPurchases.slice(0, 8)}
            getKey={(row) => row.id}
            loading={purchases.isLoading}
            emptyTitle="Không còn phiếu nhập nào nợ nhà cung cấp"
            density="compact"
          />
        </Panel>
      </div>

      <div className="grid gap-3 xl:grid-cols-2">
        {canCash && (
          <Panel title="Dòng tiền mặt 6 tháng gần đây">
            <DataTable
              columns={[
                { key: 'month', priority: 1, header: 'Tháng', cell: (row) => monthLabel(row.month) },
                { key: 'in', priority: 1, header: 'Thu', align: 'right', cell: (row) => <Money value={row.monthIn} zero="zero" /> },
                { key: 'out', priority: 1, header: 'Chi', align: 'right', cell: (row) => <Money value={row.monthOut} zero="zero" /> },
                {
                  key: 'net', priority: 1,
                  header: 'Ròng',
                  align: 'right',
                  cell: (row) => <Money value={row.monthIn - row.monthOut} zero="zero" strong />,
                },
                { key: 'count', priority: 3, header: 'Giao dịch', align: 'right', cell: (row) => row.monthCount },
              ]}
              rows={cashByMonth.map((q, i) => q.data ?? { month: months[i], monthIn: 0, monthOut: 0, monthCount: 0, balance: 0 })}
              getKey={(row) => row.month}
              loading={cashByMonth.some((q) => q.isLoading)}
              density="compact"
            />
          </Panel>
        )}

        <Panel title="Giao dịch bán hàng gần đây">
          <DataTable
            columns={[
              { key: 'date', priority: 2, header: 'Ngày', cell: (row) => date(row.date) },
              {
                key: 'voucherNo', priority: 1,
                header: 'Số phiếu',
                cell: (row) => (
                  <Link to={`/ban-hang/${row.id}`} className="link font-medium">
                    {row.voucherNo}
                  </Link>
                ),
              },
              { key: 'customer', priority: 1, header: 'Khách hàng', cell: (row) => row.customerName, truncate: true },
              { key: 'content', priority: 3, header: 'Nội dung', cell: (row) => row.content, truncate: true, hidden: true },
              { key: 'total', priority: 1, header: 'Tổng tiền', align: 'right', cell: (row) => <Money value={row.total} /> },
            ]}
            rows={dashboard.data?.recent ?? []}
            getKey={(row) => row.id}
            loading={dashboard.isLoading}
            error={dashboard.isError ? 'Không tải được giao dịch gần đây.' : undefined}
            onRetry={() => dashboard.refetch()}
            emptyTitle="Chưa có giao dịch"
            density="compact"
          />
        </Panel>
      </div>
    </Stack>
  )
}

interface AttentionItem {
  label: string
  count: number | undefined
  to: string
  sub?: string
  tone?: Tone
  loading?: boolean
}

function AttentionList({ items }: { items: AttentionItem[] }) {
  const visible = items.filter((item) => item.loading || (item.count ?? 0) > 0)
  if (visible.length === 0)
    return <p className="px-3.5 py-6 text-center text-sm text-ink-3">Không có việc nào tồn đọng</p>
  return (
    <ul>
      {visible.map((item) => (
        <li key={item.label} className="border-b border-line-2 last:border-b-0">
          <Link to={item.to} className="flex items-center gap-3 px-3.5 py-2 hover:bg-panel-2">
            <span className="min-w-0 flex-1">
              <span className="block text-sm text-ink">{item.label}</span>
              {item.sub && <span className="block text-xs text-ink-3">{item.sub}</span>}
            </span>
            {item.loading ? (
              <Skeleton className="h-4 w-8" />
            ) : (
              <span
                className={`tnum text-base font-semibold ${
                  item.tone === 'danger' ? 'text-danger' : item.tone === 'warn' ? 'text-warn' : 'text-ink'
                }`}
              >
                {item.count}
              </span>
            )}
            <ChevronRight className="size-4 shrink-0 text-ink-3" strokeWidth={1.7} />
          </Link>
        </li>
      ))}
    </ul>
  )
}

function FigureRows({
  rows,
}: {
  rows: Array<{ label: string; value: ReactNode; to?: string; loading?: boolean }>
}) {
  return (
    <table className="w-full text-sm">
      <tbody>
        {rows.map((row) => (
          <tr key={row.label} className="border-b border-line-2 last:border-b-0">
            <td className="px-3.5 py-2 text-ink-2">
              {row.to ? (
                <Link to={row.to} className="hover:text-ink hover:underline underline-offset-2">
                  {row.label}
                </Link>
              ) : (
                row.label
              )}
            </td>
            <td className="px-3.5 py-2 text-right">
              {row.loading ? <Skeleton className="ml-auto h-4 w-24" /> : row.value}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

/* ============================================================================
   Việc cần làm
   ========================================================================== */

const PRIORITY: Record<WorklistItem['priority'], { label: string; tone: Tone }> = {
  high: { label: 'Cao', tone: 'danger' },
  medium: { label: 'Vừa', tone: 'warn' },
  normal: { label: 'Thường', tone: 'neutral' },
}

export function WorklistPage() {
  const worklist = useWorklist()
  const [tab, setTab] = useState('all')
  const items = worklist.data?.items ?? []
  const filtered = items.filter((item) => {
    if (tab === 'approval') return item.kind === 'approval'
    if (tab === 'payslip') return item.kind === 'payslip'
    if (tab === 'expiring') return item.kind === 'document' || item.kind === 'contract'
    return true
  })
  const summary = worklist.data?.summary

  return (
    <ModuleScreen
      figures={
        <FigureStrip>
          <Figure label="Tổng việc" value={summary?.total ?? '…'} />
          <Figure label="Chờ tôi duyệt" value={summary?.approvals ?? '…'} to="/pheduyet" />
          <Figure label="Quá hạn" value={summary?.overdue ?? '…'} tone={summary?.overdue ? 'danger' : undefined} />
          <Figure label="Sắp hết hạn" value={summary ? summary.documents + summary.contracts : '…'} />
        </FigureStrip>
      }
      tabs={[
        { id: 'all', label: 'Tất cả', count: summary?.total },
        { id: 'approval', label: 'Chờ tôi duyệt', count: summary?.approvals },
        { id: 'payslip', label: 'Phiếu lương chưa xác nhận', count: summary?.payslips },
        { id: 'expiring', label: 'Sắp hết hạn', count: summary ? summary.documents + summary.contracts : undefined },
      ]}
      tab={tab}
      onTabChange={setTab}
      columns={[
        {
          key: 'priority',
          header: 'Ưu tiên',
          width: '6rem',
          cell: (row) => <StatusBadge tone={PRIORITY[row.priority].tone}>{PRIORITY[row.priority].label}</StatusBadge>,
          sortValue: (row) => ({ high: 0, medium: 1, normal: 2 })[row.priority],
        },
        { key: 'title', priority: 1, header: 'Loại việc', cell: (row) => row.title, sortValue: (row) => row.title },
        { key: 'description', priority: 2, header: 'Nội dung', cell: (row) => row.description, truncate: true },
        {
          key: 'due', priority: 1,
          header: 'Hạn xử lý',
          cell: (row) => (row.dueAt ? dateTime(row.dueAt) : null),
          sortValue: (row) => row.dueAt ?? '',
        },
        {
          key: 'action', priority: 1,
          header: '',
          align: 'right',
          locked: true,
          cell: (row) => (
            <Link to={worklistRoute(row)} className="link text-xs">
              Mở
            </Link>
          ),
        },
      ]}
      rows={filtered}
      getKey={(row) => row.key}
      loading={worklist.isLoading}
      error={worklist.error}
      onRefresh={() => worklist.refetch()}
      emptyTitle="Không còn việc nào chờ bạn"
      defaultSort={{ key: 'priority', dir: 'asc' }}
    />
  )
}

/* ============================================================================
   Trung tâm báo cáo
   ========================================================================== */

/**
 * Chỉ còn các báo cáo dựng thẳng từ chứng từ gốc. Nhóm kết quả kinh doanh, cân đối kế toán, cân đối
 * phát sinh, sổ cái và thuế GTGT đã bỏ cùng sổ kế toán kép: chúng đọc số từ bút toán do một nút bấm
 * tay sinh ra theo định khoản gán cứng, nên chỉ đúng khi có người nhớ bấm — dựng lại thì phải dựng
 * từ chứng từ, không phải từ một bản sao thứ hai.
 */
type ReportId = 'cash' | 'ar' | 'ap' | 'sales'

const REPORTS: { id: ReportId; label: string; group: string }[] = [
  { id: 'cash', label: 'Lưu chuyển tiền mặt', group: 'Báo cáo tài chính' },
  { id: 'ar', label: 'Công nợ phải thu', group: 'Công nợ' },
  { id: 'ap', label: 'Công nợ phải trả', group: 'Công nợ' },
  { id: 'sales', label: 'Doanh thu theo tháng', group: 'Bán hàng' },
]

/**
 * Trung tâm báo cáo: danh mục báo cáo bên trái, báo cáo đang xem bên phải. Mọi báo cáo dùng chung
 * thanh chọn kỳ, so sánh kỳ trước, in và xuất tệp.
 */
export function ReportsPage() {
  const fiscal = useFiscal()
  const [report, setReport] = useState<ReportId>('cash')
  const [exporter, setExporter] = useState<(() => void) | null>(null)
  // Bọc trong hàm để setState không nhầm hàm xuất là hàm cập nhật trạng thái.
  const handleExporter = useCallback((fn: (() => void) | null) => setExporter(() => fn), [])
  const current = REPORTS.find((r) => r.id === report)!
  const groups = Array.from(new Set(REPORTS.map((r) => r.group)))

  return (
    <div className="grid min-w-0 gap-3 lg:grid-cols-[14rem_minmax(0,1fr)]">
      <Panel className="print-hide self-start" title="Báo cáo">
        {groups.map((group) => (
          <div key={group} className="border-b border-line-2 py-1 last:border-b-0">
            <p className="px-3.5 pt-1 pb-0.5 text-2xs font-semibold text-ink-3">{group}</p>
            <ul>
              {REPORTS.filter((r) => r.group === group).map((r) => (
                <li key={r.id}>
                  <button
                    type="button"
                    onClick={() => setReport(r.id)}
                    aria-current={r.id === report ? 'page' : undefined}
                    className={`block w-full px-3.5 py-1.5 text-left text-sm ${
                      r.id === report
                        ? 'border-l-2 border-brand bg-brand-wash font-medium text-brand-ink'
                        : 'border-l-2 border-transparent text-ink-2 hover:bg-panel-2 hover:text-ink'
                    }`}
                  >
                    {r.label}
                  </button>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </Panel>

      <Panel className="min-w-0">
        <PanelHeader
          title={current.label}
          meta={monthLabel(fiscal.period)}
          actions={
            <>
              <span className="print-hide flex items-center gap-2">
                <MonthPicker value={fiscal.period} onChange={fiscal.setPeriod} size="sm" className="w-40" />
                <Button size="sm" icon={<Printer className="size-3.5" strokeWidth={1.7} />} onClick={() => window.print()}>
                  In
                </Button>
                <Button
                  size="sm"
                  icon={<Download className="size-3.5" strokeWidth={1.7} />}
                  disabled={!exporter}
                  onClick={() => exporter?.()}
                >
                  Xuất CSV
                </Button>
              </span>
            </>
          }
        />
        <ReportBody id={report} period={fiscal.period} onExporter={handleExporter} />
      </Panel>
    </div>
  )
}

function ReportBody({
  id,
  period,
  onExporter,
}: {
  id: ReportId
  period: string
  onExporter: (fn: (() => void) | null) => void
}) {
  switch (id) {
    case 'cash':
      return <CashFlowReport period={period} onExporter={onExporter} />
    case 'ar':
      return <ReceivableReport onExporter={onExporter} />
    case 'ap':
      return <PayableReport onExporter={onExporter} />
    case 'sales':
      return <SalesByMonthReport onExporter={onExporter} />
  }
}

function useExporter(
  onExporter: (fn: (() => void) | null) => void,
  build: () => { name: string; headers: string[]; rows: Array<Array<string | number | null | undefined>> } | null,
) {
  // Đăng ký hàm xuất khi dữ liệu đổi; nút Xuất CSV ở thanh tiêu đề gọi hàm này.
  const built = build()
  const signature = built ? `${built.name}|${built.rows.length}|${JSON.stringify(built.rows.slice(0, 5))}` : ''
  useEffect(() => {
    onExporter(built ? () => downloadCsv(built.name, built.headers, built.rows) : null)
    return () => onExporter(null)
    // Chỉ chạy lại khi nội dung xuất đổi, không chạy theo từng lần vẽ.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [signature, onExporter])
}

function CashFlowReport({ period, onExporter }: { period: string; onExporter: (fn: (() => void) | null) => void }) {
  const year = period.slice(0, 4)
  const months = useMemo(() => Array.from({ length: 12 }, (_, i) => `${year}-${String(i + 1).padStart(2, '0')}`), [year])
  const queries = useQueries({
    queries: months.map((month) => ({
      queryKey: ['cash', 'cash-fund', 'balance', month],
      queryFn: () => api.get<CashBalance>('/cash-fund/balance', { query: { month } }),
      staleTime: 60_000,
    })),
  })
  const rows = queries.map((q, i) => q.data ?? { month: months[i], monthIn: 0, monthOut: 0, monthCount: 0, balance: 0 })
  const totalIn = rows.reduce((s, r) => s + r.monthIn, 0)
  const totalOut = rows.reduce((s, r) => s + r.monthOut, 0)
  useExporter(onExporter, () => ({
    name: `luu-chuyen-tien-mat-${year}`,
    headers: ['Tháng', 'Thu', 'Chi', 'Ròng'],
    rows: rows.map((r) => [monthLabel(r.month), r.monthIn, r.monthOut, r.monthIn - r.monthOut]),
  }))
  return (
    <DataTable
      columns={[
        { key: 'month', priority: 1, header: `Tháng trong năm ${year}`, cell: (row) => monthLabel(row.month), total: 'Cả năm' },
        { key: 'in', priority: 1, header: 'Thu vào quỹ', align: 'right', cell: (row) => <Money value={row.monthIn} zero="zero" />, total: <Money value={totalIn} zero="zero" /> },
        { key: 'out', priority: 1, header: 'Chi từ quỹ', align: 'right', cell: (row) => <Money value={row.monthOut} zero="zero" />, total: <Money value={totalOut} zero="zero" /> },
        {
          key: 'net', priority: 1,
          header: 'Ròng',
          align: 'right',
          cell: (row) => <Money value={row.monthIn - row.monthOut} zero="zero" strong />,
          total: <Money value={totalIn - totalOut} zero="zero" />,
        },
        { key: 'count', priority: 3, header: 'Số giao dịch', align: 'right', cell: (row) => row.monthCount },
      ]}
      rows={rows}
      getKey={(row) => row.month}
      loading={queries.some((q) => q.isLoading)}
      error={queries.some((q) => q.isError) ? 'Không đọc được sổ quỹ. Bạn cần quyền xem quỹ tiền mặt.' : undefined}
    />
  )
}

function ReceivableReport({ onExporter }: { onExporter: (fn: (() => void) | null) => void }) {
  const debts = useDebts()
  const rows = (debts.data?.customers ?? []).filter((c) => c.balance !== 0 || c.salesTotal !== 0)
  useExporter(onExporter, () =>
    debts.data
      ? {
          name: 'cong-no-phai-thu',
          headers: ['Khách hàng', 'Đầu kỳ', 'Bán', 'Trả lại', 'Đã thu', 'Còn nợ', 'Gần nhất'],
          rows: rows.map((c) => [c.customer.name, c.openingBalance, c.salesTotal, c.returnsTotal, c.collectedTotal, c.balance, date(c.lastActivityDate)]),
        }
      : null,
  )
  const d = debts.data
  return (
    <DataTable
      columns={[
        { key: 'name', priority: 1, header: 'Khách hàng', cell: (row) => row.customer.name, total: 'Tổng cộng' },
        { key: 'opening', priority: 3, header: 'Đầu kỳ', align: 'right', cell: (row) => <Money value={row.openingBalance} />, total: <Money value={d?.totalOpeningBalance} zero="zero" /> },
        { key: 'sales', priority: 2, header: 'Đã bán', align: 'right', cell: (row) => <Money value={row.salesTotal} />, total: <Money value={d?.totalSales} zero="zero" /> },
        { key: 'returns', priority: 3, header: 'Trả lại', align: 'right', cell: (row) => <Money value={row.returnsTotal} />, total: <Money value={d?.totalReturns} zero="zero" /> },
        { key: 'collected', priority: 2, header: 'Đã thu', align: 'right', cell: (row) => <Money value={row.collectedTotal} />, total: <Money value={d?.totalCollected} zero="zero" /> },
        { key: 'balance', priority: 1, header: 'Còn nợ', align: 'right', cell: (row) => <Money value={row.balance} strong />, total: <Money value={d?.totalReceivable} zero="zero" /> },
        { key: 'last', priority: 3, header: 'Hoạt động gần nhất', cell: (row) => date(row.lastActivityDate) },
      ]}
      rows={rows}
      getKey={(row) => row.customer.id}
      loading={debts.isLoading}
      error={debts.isError ? 'Không tải được công nợ.' : undefined}
      onRetry={() => debts.refetch()}
    />
  )
}

function PayableReport({ onExporter }: { onExporter: (fn: (() => void) | null) => void }) {
  const suppliers = useSuppliers(true)
  const rows = (suppliers.data?.items ?? []).filter((s) => s.purchaseCount > 0)
  const totals = rows.reduce(
    (acc, s) => ({ bought: acc.bought + s.purchasedTotal, paid: acc.paid + s.paidTotal, balance: acc.balance + s.balance }),
    { bought: 0, paid: 0, balance: 0 },
  )
  useExporter(onExporter, () =>
    suppliers.data
      ? {
          name: 'cong-no-phai-tra',
          headers: ['Nhà cung cấp', 'Đã mua', 'Đã trả', 'Còn nợ', 'Mua gần nhất'],
          rows: rows.map((s) => [s.name, s.purchasedTotal, s.paidTotal, s.balance, date(s.lastPurchaseDate)]),
        }
      : null,
  )
  return (
    <DataTable
      columns={[
        { key: 'name', priority: 1, header: 'Nhà cung cấp', cell: (row) => row.name, total: 'Tổng cộng' },
        { key: 'count', priority: 3, header: 'Số phiếu', align: 'right', cell: (row) => row.purchaseCount },
        { key: 'bought', priority: 2, header: 'Đã mua', align: 'right', cell: (row) => <Money value={row.purchasedTotal} />, total: <Money value={totals.bought} zero="zero" /> },
        { key: 'paid', priority: 2, header: 'Đã trả', align: 'right', cell: (row) => <Money value={row.paidTotal} />, total: <Money value={totals.paid} zero="zero" /> },
        { key: 'balance', priority: 1, header: 'Còn nợ', align: 'right', cell: (row) => <Money value={row.balance} strong />, total: <Money value={totals.balance} zero="zero" /> },
        { key: 'last', priority: 3, header: 'Mua gần nhất', cell: (row) => date(row.lastPurchaseDate) },
      ]}
      rows={rows}
      getKey={(row) => row.id}
      loading={suppliers.isLoading}
      error={suppliers.isError ? 'Không tải được nhà cung cấp.' : undefined}
      onRetry={() => suppliers.refetch()}
    />
  )
}

function SalesByMonthReport({ onExporter }: { onExporter: (fn: (() => void) | null) => void }) {
  const reports = useReports()
  const rows = reports.data?.monthly ?? []
  const total = rows.reduce((s, r) => s + r.total, 0)
  useExporter(onExporter, () =>
    reports.data
      ? {
          name: 'doanh-thu-theo-thang',
          headers: ['Tháng', 'Số phiếu', 'Doanh thu ròng'],
          rows: rows.map((r) => [`${r.month}/${r.year}`, r.documentCount, r.total]),
        }
      : null,
  )
  return (
    <DataTable
      columns={[
        { key: 'month', priority: 1, header: 'Tháng', cell: (row) => `Tháng ${row.month}/${row.year}`, total: 'Tổng cộng' },
        { key: 'count', priority: 2, header: 'Số phiếu bán', align: 'right', cell: (row) => row.documentCount, total: rows.reduce((s, r) => s + r.documentCount, 0) },
        { key: 'total', priority: 1, header: 'Doanh thu ròng', align: 'right', cell: (row) => <Money value={row.total} strong />, total: <Money value={total} zero="zero" /> },
      ]}
      rows={rows}
      getKey={(row) => `${row.year}-${row.month}`}
      loading={reports.isLoading}
      error={reports.isError ? 'Không tải được báo cáo bán hàng.' : undefined}
      onRetry={() => reports.refetch()}
    />
  )
}

/** Trang đích dự phòng cho tài khoản chưa được cấp màn hình nào. */
export function BlankLanding() {
  return (
    <Panel padded>
      <p className="text-sm text-ink-2">Tài khoản này chưa được mở màn hình nào. Liên hệ quản trị viên để được cấp quyền.</p>
    </Panel>
  )
}
