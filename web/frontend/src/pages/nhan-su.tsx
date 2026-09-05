import { useEffect, useMemo, useState } from 'react'
import { ChevronRight, Plus, Star } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { PERM } from '@/lib/permissions'
import { date, dateTime, hours, monthLabel, monthKey, num, todayISO, vnd } from '@/lib/format'
import { matches } from '@/lib/text'
import { cn } from '@/lib/cn'
import { useFiscal } from '@/shell/FiscalContext'
import {
  LEAVE_TYPE_LABELS,
  employeeStatus,
  useBankAccounts,
  useBanks,
  useBenefits,
  useCompleteOnboardingTask,
  useContracts,
  useDeleteBankAccount,
  useDepartments,
  useDirectory,
  useEmployee,
  useEmployeeDocuments,
  useEmployees,
  useJobPositions,
  useLeaveBalances,
  useLocations,
  useMyDocuments,
  useMyEmployee,
  useOnboarding,
  useOrgChart,
  usePerformance,
  useSaveBankAccount,
  useSaveDepartment,
  useSaveLocation,
  useSaveSelfReview,
  useSetDefaultBankAccount,
  useTraining,
  type BankAccount,
  type Department,
  type DirectoryEntry,
  type JobPosition,
  type Location,
  type OrgNode,
} from '@/api/hr'
import {
  penaltyStatus,
  refundStatus,
  useAcknowledgePayslip,
  useMyPayslips,
  usePenalties,
  usePenaltyRefunds,
  usePenaltyTypes,
  usePublishedPayslips,
  useSalaries,
  useSavePenalty,
  useWaivePenalty,
  type MyPayslip,
  type Penalty,
} from '@/api/payroll'
import {
  Avatar,
  Button,
  Combobox,
  ConfirmDialog,
  DataTable,
  DatePicker,
  Drawer,
  Field,
  Figure,
  FigureStrip,
  InlineAlert,
  Input,
  KeyValue,
  Modal,
  Money,
  MonthPicker,
  NumberInput,
  Panel,
  SearchInput,
  Select,
  Stack,
  StatusBadge,
  Tabs,
  Textarea,
  useToast,
  type Column,
} from '@/ui'
import { ModuleScreen, errorMessage } from './_shared'

/* ============================================================================
   Không gian của tôi
   ========================================================================== */

const MY_TABS = [
  { id: 'profile', label: 'Hồ sơ' },
  { id: 'contracts', label: 'Hợp đồng' },
  { id: 'documents', label: 'Giấy tờ' },
  { id: 'payslips', label: 'Phiếu lương' },
  { id: 'leave', label: 'Ngày phép' },
]

/** Hồ sơ, hợp đồng, giấy tờ, phiếu lương và ngày phép của chính người đang đăng nhập. */
export function MySpacePage() {
  const me = useMyEmployee()
  const [tab, setTab] = useState('profile')
  const employeeId = me.data?.id

  const contracts = useContracts(tab === 'contracts' ? employeeId : undefined)
  const documents = useMyDocuments()
  const payslips = useMyPayslips()
  const leave = useLeaveBalances(tab === 'leave' ? employeeId : undefined)

  const latest = payslips.data?.[0]
  const pendingAck = (payslips.data ?? []).filter((p) => !p.acknowledgedAt)
  const overdue = pendingAck.filter((p) => p.acknowledgementOverdue)
  const annual = (leave.data ?? []).find((b) => b.leaveType === 'annual' && b.year === new Date().getFullYear())

  return (
    <Stack>
      {overdue.length > 0 && (
        <InlineAlert tone="danger" title="Bạn có phiếu lương quá hạn xác nhận">
          Chấm công và một số thao tác bị khoá cho tới khi bạn xác nhận phiếu lương kỳ{' '}
          {overdue.map((p) => monthLabel(p.period).toLowerCase()).join(', ')}.
        </InlineAlert>
      )}

      <FigureStrip>
        <Figure label="Nhân viên" value={me.data?.fullName ?? '…'} sub={me.data?.employeeCode || undefined} />
        <Figure label="Phòng ban" value={me.data?.departmentName || '…'} sub={me.data?.position || undefined} />
        <Figure
          label={latest ? `Thực nhận ${monthLabel(latest.period).toLowerCase()}` : 'Thực nhận kỳ gần nhất'}
          value={latest ? vnd(latest.netPay) : '…'}
        />
        <Figure
          label="Phiếu lương chờ xác nhận"
          value={payslips.data ? pendingAck.length : '…'}
          tone={pendingAck.length ? 'warn' : undefined}
        />
      </FigureStrip>

      <Panel>
        <Tabs items={MY_TABS} active={tab} onChange={setTab} />

        {tab === 'profile' && (
          <div className="grid gap-x-8 gap-y-4 px-3.5 py-3.5 lg:grid-cols-2">
            <div className="flex items-center gap-3">
              <Avatar url={me.data?.avatar} name={me.data?.fullName ?? ''} size="lg" />
              <div className="min-w-0">
                <p className="truncate text-sm font-semibold text-ink">{me.data?.fullName}</p>
                <p className="truncate text-xs text-ink-3">
                  {me.data?.position}
                  {me.data?.departmentName ? ` · ${me.data.departmentName}` : ''}
                </p>
              </div>
              {me.data && (
                <span className="ml-auto">
                  <StatusBadge tone={employeeStatus(me.data.status).tone}>
                    {employeeStatus(me.data.status).label}
                  </StatusBadge>
                </span>
              )}
            </div>
            <div />
            <KeyValue
              rows={[
                ['Mã nhân viên', me.data?.employeeCode || null],
                ['Tên đăng nhập', me.data?.username || null],
                ['Ngày sinh', me.data?.dob ? date(me.data.dob) : null],
                ['Giới tính', me.data?.gender || null],
                ['Điện thoại', me.data?.phone || null],
                ['Thư điện tử', me.data?.email || null],
              ]}
            />
            <KeyValue
              rows={[
                ['Chức danh', me.data?.position || null],
                ['Chức vụ được gán', me.data?.positions?.map((p) => p.name).join(', ') || null],
                ['Phòng ban', me.data?.departmentName || null],
                ['Địa điểm', me.data?.locationName || null],
                ['Quản lý trực tiếp', me.data?.managerName || null],
                ['Ngày vào làm', me.data?.hireDate ? date(me.data.hireDate) : null],
                ['Địa chỉ', me.data?.address || null],
              ]}
            />
          </div>
        )}

        {tab === 'contracts' && (
          <DataTable
            columns={CONTRACT_COLUMNS}
            rows={contracts.data ?? []}
            getKey={(row) => row.id}
            loading={contracts.isLoading}
            error={contracts.isError ? errorMessage(contracts.error) : undefined}
            emptyTitle="Chưa có hợp đồng nào trong hồ sơ"
          />
        )}

        {tab === 'documents' && (
          <DataTable
            columns={DOCUMENT_COLUMNS}
            rows={documents.data ?? []}
            getKey={(row) => row.id}
            loading={documents.isLoading}
            error={documents.isError ? errorMessage(documents.error) : undefined}
            emptyTitle="Chưa có giấy tờ nào trong hồ sơ"
            emptyDescription="Giấy tờ nộp từ ứng dụng Nhân sự cũng hiện ở đây."
          />
        )}

        {tab === 'payslips' && <MyPayslipList payslips={payslips.data ?? []} loading={payslips.isLoading} />}

        {tab === 'leave' && (
          <>
            {annual && (
              <div className="border-b border-line-2 px-3.5 py-2.5 text-sm text-ink-2">
                Phép năm {annual.year}: còn <strong className="tnum font-semibold text-ink">{num(annual.remainingDays)}</strong>{' '}
                trên {num(annual.totalDays)} ngày.
              </div>
            )}
            <DataTable
              columns={[
                { key: 'year', priority: 1, header: 'Năm', cell: (row) => <span className="tnum">{row.year}</span>, sortValue: (r) => r.year },
                { key: 'type', priority: 1, header: 'Loại phép', cell: (row) => LEAVE_TYPE_LABELS[row.leaveType] ?? row.leaveType },
                { key: 'total', priority: 1, header: 'Được hưởng', align: 'right', cell: (row) => <span className="tnum">{num(row.totalDays)}</span> },
                { key: 'used', priority: 2, header: 'Đã dùng', align: 'right', cell: (row) => <span className="tnum">{num(row.usedDays)}</span> },
                {
                  key: 'remaining', priority: 1,
                  header: 'Còn lại',
                  align: 'right',
                  cell: (row) => <span className="tnum font-semibold">{num(row.remainingDays)}</span>,
                },
              ]}
              rows={leave.data ?? []}
              getKey={(row) => row.id}
              loading={leave.isLoading}
              emptyTitle="Chưa được cấp ngày phép"
            />
          </>
        )}
      </Panel>
    </Stack>
  )
}

const CONTRACT_COLUMNS: Column<import('@/api/hr').Contract>[] = [
  { key: 'no', priority: 1, header: 'Số hợp đồng', cell: (row) => <span className="tnum font-medium">{row.contractNo}</span> },
  { key: 'type', priority: 2, header: 'Loại', cell: (row) => row.contractType },
  { key: 'start', priority: 1, header: 'Từ ngày', cell: (row) => date(row.startDate) },
  { key: 'end', priority: 2, header: 'Đến ngày', cell: (row) => (row.endDate ? date(row.endDate) : 'Không thời hạn') },
  { key: 'base', priority: 1, header: 'Lương ký', align: 'right', cell: (row) => <Money value={row.baseSalary} /> },
  { key: 'raise', priority: 3, header: 'Đã tăng', align: 'right', cell: (row) => <Money value={row.raiseTotal} /> },
  { key: 'current', priority: 1, header: 'Lương hiện hưởng', align: 'right', cell: (row) => <Money value={row.currentSalary} strong /> },
  {
    key: 'status', priority: 1,
    header: 'Trạng thái',
    cell: (row) =>
      row.status === 'Active' ? <StatusBadge tone="ok">Hiệu lực</StatusBadge> : <StatusBadge>{row.status}</StatusBadge>,
  },
]

const DOCUMENT_COLUMNS: Column<import('@/api/hr').HrDocument>[] = [
  { key: 'title', priority: 1, header: 'Giấy tờ', cell: (row) => <span className="font-medium">{row.title}</span> },
  { key: 'type', priority: 2, header: 'Loại', cell: (row) => row.docType },
  { key: 'number', priority: 3, header: 'Số hiệu', cell: (row) => <span className="tnum">{row.docNumber}</span> },
  { key: 'issued', priority: 2, header: 'Ngày cấp', cell: (row) => date(row.issuedDate) },
  { key: 'expires', priority: 2, header: 'Hết hạn', cell: (row) => date(row.expiresAt) },
  {
    key: 'status', priority: 1,
    header: 'Duyệt',
    cell: (row) => {
      const key = (row.approvalStatus || '').toLowerCase()
      if (key === 'approved') return <StatusBadge tone="ok">Đã duyệt</StatusBadge>
      if (key === 'rejected') return <StatusBadge tone="danger">Từ chối</StatusBadge>
      return <StatusBadge tone="warn">Chờ duyệt</StatusBadge>
    },
  },
  {
    key: 'file', priority: 1,
    header: 'Tệp',
    cell: (row) =>
      row.hasFile ? (
        <a
          className="link"
          href={`/api/hr/documents/${row.id}/file`}
          target="_blank"
          rel="noreferrer"
          onClick={(event) => event.stopPropagation()}
        >
          {row.fileName || 'Tải xuống'}
        </a>
      ) : row.fileUrl ? (
        <a className="link" href={row.fileUrl} target="_blank" rel="noreferrer">
          Liên kết
        </a>
      ) : null,
  },
]

function MyPayslipList({ payslips, loading }: { payslips: MyPayslip[]; loading: boolean }) {
  const toast = useToast()
  const acknowledge = useAcknowledgePayslip()
  const [open, setOpen] = useState<MyPayslip | null>(null)

  return (
    <>
      <DataTable
        columns={[
          { key: 'period', priority: 1, header: 'Kỳ lương', cell: (row) => <span className="font-medium">{monthLabel(row.period)}</span>, sortValue: (r) => r.period },
          { key: 'worked', priority: 3, header: 'Ngày công', align: 'right', cell: (row) => <span className="tnum">{num(row.workedDays)}</span> },
          { key: 'overtime', priority: 3, header: 'Giờ tăng ca', align: 'right', cell: (row) => <span className="tnum">{num(row.overtimeHours)}</span> },
          { key: 'earnings', priority: 2, header: 'Tổng thu nhập', align: 'right', cell: (row) => <Money value={row.totalEarnings} /> },
          { key: 'deductions', priority: 2, header: 'Khấu trừ', align: 'right', cell: (row) => <Money value={row.totalDeductions} /> },
          { key: 'net', priority: 1, header: 'Thực nhận', align: 'right', cell: (row) => <Money value={row.netPay} strong /> },
          {
            key: 'status', priority: 1,
            header: 'Xác nhận',
            cell: (row) =>
              row.acknowledgedAt ? (
                <StatusBadge tone="ok">Đã xác nhận</StatusBadge>
              ) : row.acknowledgementOverdue ? (
                <StatusBadge tone="danger">Quá hạn</StatusBadge>
              ) : (
                <StatusBadge tone="warn">Chờ xác nhận</StatusBadge>
              ),
          },
        ]}
        rows={payslips}
        getKey={(row) => row.id}
        loading={loading}
        onRowClick={(row) => setOpen(row)}
        activeKey={open?.id}
        emptyTitle="Chưa có phiếu lương nào được phát hành"
      />

      <Drawer
        open={!!open}
        onClose={() => setOpen(null)}
        width="md"
        title={open ? `Phiếu lương ${monthLabel(open.period).toLowerCase()}` : ''}
        meta={
          open && (
            <>
              <span>Phát hành {date(open.publishedAt)}</span>
              {open.acknowledgedAt ? (
                <StatusBadge tone="ok">Đã xác nhận</StatusBadge>
              ) : (
                <StatusBadge tone="warn">Hạn xác nhận {date(open.acknowledgementDueAt)}</StatusBadge>
              )}
            </>
          )
        }
        footer={
          open &&
          !open.acknowledgedAt && (
            <Button
              variant="primary"
              size="sm"
              className="ml-auto"
              loading={acknowledge.isPending}
              onClick={async () => {
                try {
                  await acknowledge.mutateAsync({ id: open.id, revision: open.revisionToken })
                  toast.success('Đã xác nhận phiếu lương')
                  setOpen(null)
                } catch (error) {
                  toast.error('Không xác nhận được', errorMessage(error))
                }
              }}
            >
              Xác nhận đã nhận lương
            </Button>
          )
        }
      >
        {open && (
          <div className="flex flex-col gap-3 p-3">
            <Panel title="Công trong kỳ" padded>
              <KeyValue
                rows={[
                  ['Ngày công', num(open.workedDays)],
                  ['Ngày vắng', num(open.absentDays)],
                  ['Lượt đi muộn', num(open.lateDays)],
                  ['Tổng giờ làm', hours(open.totalWorkedHours)],
                  ['Giờ tăng ca', num(open.overtimeHours)],
                ]}
              />
            </Panel>
            <Panel title="Các khoản cộng">
              <DataTable
                columns={[
                  { key: 'label', priority: 1, header: 'Khoản', cell: (row) => row.label, total: 'Tổng thu nhập' },
                  { key: 'amount', priority: 1, header: 'Số tiền', align: 'right', cell: (row) => <Money value={row.amount} />, total: <Money value={open.totalEarnings} zero="zero" /> },
                ]}
                rows={open.earnings ?? []}
                getKey={(_, i) => i}
                density="compact"
                emptyTitle="Không có khoản cộng"
              />
            </Panel>
            <Panel title="Các khoản trừ">
              <DataTable
                columns={[
                  { key: 'label', priority: 1, header: 'Khoản', cell: (row) => row.label, total: 'Tổng khấu trừ' },
                  { key: 'amount', priority: 1, header: 'Số tiền', align: 'right', cell: (row) => <Money value={row.amount} />, total: <Money value={open.totalDeductions} zero="zero" /> },
                ]}
                rows={open.deductions ?? []}
                getKey={(_, i) => i}
                density="compact"
                emptyTitle="Không có khoản trừ"
              />
            </Panel>
            <div className="panel flex items-center justify-between px-3.5 py-3">
              <span className="text-sm font-semibold text-ink">Thực nhận</span>
              <Money value={open.netPay} strong className="text-lg" />
            </div>
            {open.note && (
              <Panel title="Ghi chú" padded>
                <p className="text-sm text-ink-2">{open.note}</p>
              </Panel>
            )}
          </div>
        )}
      </Drawer>
    </>
  )
}

/* ============================================================================
   Danh bạ và sơ đồ tổ chức
   ========================================================================== */

export function DirectoryPage() {
  const [tab, setTab] = useState('list')
  const [search, setSearch] = useState('')
  const [department, setDepartment] = useState('')
  const directory = useDirectory({ search: search.trim() || undefined, departmentId: department || undefined })
  const departments = useDepartments()
  const orgChart = useOrgChart(tab === 'org')
  const [open, setOpen] = useState<DirectoryEntry | null>(null)

  const online = (directory.data ?? []).filter((e) => e.online).length

  if (tab === 'org')
    return (
      <Stack>
        <Panel>
          <Tabs
            items={[
              { id: 'list', label: 'Danh bạ', count: directory.data?.length },
              { id: 'org', label: 'Sơ đồ tổ chức' },
            ]}
            active={tab}
            onChange={setTab}
          />
          <div className="px-3.5 py-3">
            {orgChart.isLoading && <p className="text-sm text-ink-3">Đang tải sơ đồ</p>}
            {orgChart.isError && <InlineAlert tone="danger">{errorMessage(orgChart.error)}</InlineAlert>}
            {(orgChart.data ?? []).length === 0 && !orgChart.isLoading && (
              <p className="text-sm text-ink-3">Chưa khai báo quản lý trực tiếp nên chưa dựng được sơ đồ.</p>
            )}
            <ul className="flex flex-col gap-1">
              {(orgChart.data ?? []).map((node) => (
                <OrgBranch key={node.id} node={node} depth={0} />
              ))}
            </ul>
          </div>
        </Panel>
      </Stack>
    )

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label="Nhân viên đang làm việc" value={directory.data?.length ?? '…'} />
            <Figure label="Đang trực tuyến" value={directory.data ? online : '…'} tone={online ? 'ok' : undefined} />
            <Figure label="Phòng ban" value={departments.data?.length ?? '…'} />
          </FigureStrip>
        }
        tabs={[
          { id: 'list', label: 'Danh bạ', count: directory.data?.length },
          { id: 'org', label: 'Sơ đồ tổ chức' },
        ]}
        tab={tab}
        onTabChange={setTab}
        filters={
          <>
            <SearchInput
              size="sm"
              className="w-64"
              placeholder="Tên hoặc chức vụ, gõ không dấu cũng được"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              onClear={() => setSearch('')}
            />
            <div className="w-48">
              <Combobox
                size="sm"
                value={department}
                onChange={setDepartment}
                clearable
                placeholder="Mọi phòng ban"
                options={(departments.data ?? []).map((d) => ({ value: d.id, label: d.name }))}
              />
            </div>
          </>
        }
        columns={[
          {
            key: 'name', priority: 1,
            header: 'Họ tên',
            cell: (row) => (
              <span className="flex items-center gap-2">
                <span
                  aria-hidden
                  className={cn('size-1.5 shrink-0 rounded-full', row.online ? 'bg-ok' : 'bg-line')}
                  title={row.online ? 'Đang trực tuyến' : 'Ngoại tuyến'}
                />
                <span className="font-medium">{row.fullName}</span>
              </span>
            ),
            sortValue: (r) => r.fullName,
          },
          { key: 'position', priority: 1, header: 'Chức vụ', cell: (row) => row.position, sortValue: (r) => r.position },
          { key: 'department', priority: 2, header: 'Phòng ban', cell: (row) => row.departmentName, sortValue: (r) => r.departmentName ?? '' },
          { key: 'manager', priority: 3, header: 'Quản lý', cell: (row) => row.managerName, sortValue: (r) => r.managerName ?? '' },
          {
            key: 'phone', priority: 2,
            header: 'Điện thoại',
            cell: (row) =>
              row.phone ? (
                <a className="link tnum" href={`tel:${row.phone}`} onClick={(e) => e.stopPropagation()}>
                  {row.phone}
                </a>
              ) : row.canSeeContact ? null : (
                <span className="text-ink-3">Ẩn</span>
              ),
          },
          {
            key: 'email', priority: 3,
            header: 'Thư điện tử',
            cell: (row) =>
              row.email ? (
                <a className="link" href={`mailto:${row.email}`} onClick={(e) => e.stopPropagation()}>
                  {row.email}
                </a>
              ) : null,
            truncate: true,
          },
          {
            key: 'presence', priority: 1,
            header: 'Trạng thái',
            cell: (row) => (row.online ? <StatusBadge tone="ok">Trực tuyến</StatusBadge> : <StatusBadge>Ngoại tuyến</StatusBadge>),
          },
        ]}
        rows={directory.data ?? []}
        loading={directory.isLoading}
        error={directory.error}
        onRefresh={() => directory.refetch()}
        onRowClick={(row) => setOpen(row)}
        activeKey={open?.id}
        defaultSort={{ key: 'name', dir: 'asc' }}
        emptyTitle="Không có ai khớp bộ lọc này"
      />

      <Drawer open={!!open} onClose={() => setOpen(null)} width="sm" title={open?.fullName ?? ''} meta={open?.position}>
        <div className="p-3">
          <Panel padded>
            <KeyValue
              rows={[
                ['Chức vụ', open?.position || null],
                ['Phòng ban', open?.departmentName || null],
                ['Quản lý trực tiếp', open?.managerName || null],
                ['Điện thoại', open?.phone || (open?.canSeeContact ? null : 'Không được xem')],
                ['Thư điện tử', open?.email || (open?.canSeeContact ? null : 'Không được xem')],
                ['Trạng thái', open?.online ? 'Đang trực tuyến' : 'Ngoại tuyến'],
              ]}
            />
          </Panel>
        </div>
      </Drawer>
    </>
  )
}

function OrgBranch({ node, depth }: { node: OrgNode; depth: number }) {
  const [open, setOpen] = useState(depth < 2)
  const hasReports = node.reports.length > 0
  return (
    <li>
      <div
        className="flex items-center gap-2 rounded-sm py-1 hover:bg-panel-2"
        style={{ paddingLeft: `${depth * 20}px` }}
      >
        {hasReports ? (
          <button
            type="button"
            onClick={() => setOpen((value) => !value)}
            aria-label={open ? 'Thu gọn' : 'Mở rộng'}
            className="grid size-5 shrink-0 place-items-center rounded-sm text-ink-3 hover:bg-panel-3 hover:text-ink"
          >
            <ChevronRight className={cn('size-3.5 transition-transform', open && 'rotate-90')} strokeWidth={1.8} />
          </button>
        ) : (
          <span className="size-5 shrink-0" />
        )}
        <span className="min-w-0 flex-1 text-sm">
          <span className="font-medium text-ink">{node.fullName}</span>
          {node.position && <span className="text-ink-3"> · {node.position}</span>}
        </span>
        {node.departmentName && <span className="shrink-0 text-xs text-ink-3">{node.departmentName}</span>}
        {hasReports && <span className="tnum shrink-0 text-xs text-ink-3">{node.reports.length}</span>}
      </div>
      {open && hasReports && (
        <ul>
          {node.reports.map((child) => (
            <OrgBranch key={child.id} node={child} depth={depth + 1} />
          ))}
        </ul>
      )}
    </li>
  )
}

/* ============================================================================
   Quản lý nhân sự
   ========================================================================== */

export function HrEmployeesPage() {
  const auth = useAuth()
  const [tab, setTab] = useState('employees')
  const [search, setSearch] = useState('')
  const [department, setDepartment] = useState('')
  const [status, setStatus] = useState('active')
  const [openId, setOpenId] = useState<string | null>(null)
  const [editDepartment, setEditDepartment] = useState<Department | null | 'new'>(null)
  const [editLocation, setEditLocation] = useState<Location | null | 'new'>(null)

  // Bốn danh mục đều nhẹ và cùng nuôi dải số liệu, nên nạp sẵn thay vì chờ mở tab.
  const employees = useEmployees({ departmentId: department || undefined })
  const departments = useDepartments()
  const locations = useLocations()
  const positions = useJobPositions()
  const canManage = auth.can(PERM.hrManage)

  const tabs = HR_TABS(employees.data?.length, departments.data?.length, locations.data?.length, positions.data?.length)

  const rows = useMemo(
    () =>
      (employees.data ?? []).filter((e) => {
        const key = (e.status || '').toLowerCase()
        if (status === 'active' && key !== 'active') return false
        if (status === 'left' && key === 'active') return false
        if (search && !matches(`${e.fullName} ${e.employeeCode} ${e.username} ${e.position}`, search)) return false
        return true
      }),
    [employees.data, status, search],
  )

  if (tab !== 'employees')
    return (
      <>
        {tab === 'departments' && (
          <DepartmentsTab
            tabs={tabs}
            tab={tab}
            onTabChange={setTab}
            canManage={canManage}
            onAdd={() => setEditDepartment('new')}
            onEdit={setEditDepartment}
          />
        )}
        {tab === 'locations' && (
          <LocationsTab
            tabs={tabs}
            tab={tab}
            onTabChange={setTab}
            canManage={canManage}
            onAdd={() => setEditLocation('new')}
            onEdit={setEditLocation}
          />
        )}
        {tab === 'positions' && <PositionsTab tabs={tabs} tab={tab} onTabChange={setTab} />}
        <DepartmentModal item={editDepartment} onClose={() => setEditDepartment(null)} departments={departments.data ?? []} />
        <LocationModal item={editLocation} onClose={() => setEditLocation(null)} />
      </>
    )

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label="Đang làm việc" value={employees.data ? employees.data.filter((e) => e.status.toLowerCase() === 'active').length : '…'} />
            <Figure label="Phòng ban" value={departments.data?.length ?? '…'} />
            <Figure label="Địa điểm" value={locations.data?.length ?? '…'} />
            <Figure label="Chức danh" value={positions.data?.length ?? '…'} />
          </FigureStrip>
        }
        tabs={tabs}
        tab={tab}
        onTabChange={setTab}
        filters={
          <>
            <SearchInput
              size="sm"
              className="w-56"
              placeholder="Tên, mã nhân viên, tài khoản"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              onClear={() => setSearch('')}
            />
            <div className="w-44">
              <Combobox
                size="sm"
                value={department}
                onChange={setDepartment}
                clearable
                placeholder="Mọi phòng ban"
                options={(departments.data ?? []).map((d) => ({ value: d.id, label: d.name }))}
              />
            </div>
            <Select size="sm" value={status} onChange={(event) => setStatus(event.target.value)} className="w-36">
              <option value="active">Đang làm việc</option>
              <option value="all">Tất cả</option>
              <option value="left">Đã nghỉ</option>
            </Select>
          </>
        }
        columns={[
          { key: 'code', priority: 2, header: 'Mã NV', width: '6rem', cell: (row) => <span className="tnum">{row.employeeCode}</span>, sortValue: (r) => r.employeeCode },
          {
            key: 'name', priority: 1,
            header: 'Họ tên',
            cell: (row) => (
              <span className="flex items-center gap-2">
                <Avatar url={row.avatar} name={row.fullName} size="sm" />
                <span className="min-w-0">
                  <span className="block truncate font-medium">{row.fullName}</span>
                  <span className="block truncate text-xs text-ink-3">@{row.username}</span>
                </span>
              </span>
            ),
            sortValue: (r) => r.fullName,
          },
          { key: 'position', priority: 2, header: 'Chức danh', cell: (row) => row.position, sortValue: (r) => r.position },
          { key: 'department', priority: 2, header: 'Phòng ban', cell: (row) => row.departmentName, sortValue: (r) => r.departmentName },
          { key: 'location', priority: 3, header: 'Địa điểm', cell: (row) => row.locationName, sortValue: (r) => r.locationName },
          { key: 'manager', priority: 3, header: 'Quản lý', cell: (row) => row.managerName, hidden: true },
          { key: 'phone', priority: 3, header: 'Điện thoại', cell: (row) => <span className="tnum">{row.phone}</span>, hidden: true },
          { key: 'hire', priority: 3, header: 'Vào làm', cell: (row) => date(row.hireDate), sortValue: (r) => r.hireDate ?? '' },
          {
            key: 'status', priority: 1,
            header: 'Trạng thái',
            cell: (row) => <StatusBadge tone={employeeStatus(row.status).tone}>{employeeStatus(row.status).label}</StatusBadge>,
            sortValue: (r) => r.status,
          },
        ]}
        rows={rows}
        loading={employees.isLoading}
        error={employees.error}
        onRefresh={() => employees.refetch()}
        onRowClick={(row) => setOpenId(row.id)}
        activeKey={openId}
        defaultSort={{ key: 'name', dir: 'asc' }}
        emptyTitle="Không có nhân viên nào khớp bộ lọc"
      />
      <EmployeeDrawer employeeId={openId} onClose={() => setOpenId(null)} />
    </>
  )
}

const HR_TABS = (employees?: number, departments?: number, locations?: number, positions?: number) => [
  { id: 'employees', label: 'Nhân viên', count: employees },
  { id: 'departments', label: 'Phòng ban', count: departments },
  { id: 'locations', label: 'Địa điểm', count: locations },
  { id: 'positions', label: 'Chức danh', count: positions },
]

/** Ba tab danh mục tách riêng để mỗi bảng giữ đúng kiểu dòng của nó. */
interface CatalogTabProps {
  tabs: Array<{ id: string; label: string; count?: number }>
  tab: string
  onTabChange: (id: string) => void
}

function DepartmentsTab({
  tabs,
  tab,
  onTabChange,
  canManage,
  onAdd,
  onEdit,
}: CatalogTabProps & { canManage: boolean; onAdd: () => void; onEdit: (item: Department) => void }) {
  const departments = useDepartments()
  return (
    <ModuleScreen
      tabs={tabs}
      tab={tab}
      onTabChange={onTabChange}
      actions={
        canManage && (
          <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={onAdd}>
            Thêm phòng ban
          </Button>
        )
      }
      columns={[
        { key: 'name', priority: 1, header: 'Phòng ban', cell: (row) => <span className="font-medium">{row.name}</span>, sortValue: (r) => r.name },
        { key: 'code', priority: 2, header: 'Mã', cell: (row) => <span className="tnum">{row.code}</span> },
        { key: 'parent', priority: 2, header: 'Trực thuộc', cell: (row) => row.parentName },
        { key: 'manager', priority: 2, header: 'Trưởng phòng', cell: (row) => row.managerName },
        { key: 'count', priority: 1, header: 'Số nhân viên', align: 'right', cell: (row) => row.employeeCount, sortValue: (r) => r.employeeCount },
        {
          key: 'accounting', priority: 3,
          header: 'Vai trò',
          cell: (row) => (row.isAccounting ? <StatusBadge tone="info">Phòng kế toán</StatusBadge> : null),
        },
        {
          key: 'actions', priority: 1,
          header: '',
          align: 'right',
          locked: true,
          cell: (row) =>
            canManage ? (
              <span className="row-actions">
                <Button size="sm" variant="ghost" onClick={(e) => { e.stopPropagation(); onEdit(row) }}>
                  Sửa
                </Button>
              </span>
            ) : null,
        },
      ]}
      rows={departments.data ?? []}
      loading={departments.isLoading}
      error={departments.error}
      onRefresh={() => departments.refetch()}
      defaultSort={{ key: 'name', dir: 'asc' }}
      emptyTitle="Chưa khai báo phòng ban nào"
    />
  )
}

function LocationsTab({
  tabs,
  tab,
  onTabChange,
  canManage,
  onAdd,
  onEdit,
}: CatalogTabProps & { canManage: boolean; onAdd: () => void; onEdit: (item: Location) => void }) {
  const locations = useLocations()
  return (
    <ModuleScreen
      tabs={tabs}
      tab={tab}
      onTabChange={onTabChange}
      actions={
        canManage && (
          <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={onAdd}>
            Thêm địa điểm
          </Button>
        )
      }
      columns={[
        { key: 'name', priority: 1, header: 'Địa điểm', cell: (row) => <span className="font-medium">{row.name}</span>, sortValue: (r) => r.name },
        { key: 'code', priority: 2, header: 'Mã', cell: (row) => <span className="tnum">{row.code}</span> },
        { key: 'address', priority: 2, header: 'Địa chỉ', cell: (row) => row.address, truncate: true },
        { key: 'count', priority: 1, header: 'Số nhân viên', align: 'right', cell: (row) => row.employeeCount, sortValue: (r) => r.employeeCount },
        {
          key: 'actions', priority: 1,
          header: '',
          align: 'right',
          locked: true,
          cell: (row) =>
            canManage ? (
              <span className="row-actions">
                <Button size="sm" variant="ghost" onClick={(e) => { e.stopPropagation(); onEdit(row) }}>
                  Sửa
                </Button>
              </span>
            ) : null,
        },
      ]}
      rows={locations.data ?? []}
      loading={locations.isLoading}
      error={locations.error}
      onRefresh={() => locations.refetch()}
      defaultSort={{ key: 'name', dir: 'asc' }}
      emptyTitle="Chưa khai báo địa điểm nào"
    />
  )
}

function PositionsTab({ tabs, tab, onTabChange }: CatalogTabProps) {
  const positions = useJobPositions()
  return (
    <ModuleScreen
      tabs={tabs}
      tab={tab}
      onTabChange={onTabChange}
      columns={[
        { key: 'name', priority: 1, header: 'Chức danh', cell: (row) => <span className="font-medium">{row.name}</span>, sortValue: (r) => r.name },
        { key: 'code', priority: 2, header: 'Mã', cell: (row) => <span className="tnum">{row.code}</span> },
        { key: 'role', priority: 2, header: 'Vai trò mặc định', cell: (row) => row.defaultRoleLabel },
        {
          key: 'system', priority: 3,
          header: 'Nguồn',
          cell: (row) => (row.isSystem ? <StatusBadge>Hệ thống</StatusBadge> : <StatusBadge tone="info">Tự khai báo</StatusBadge>),
        },
        {
          key: 'active', priority: 1,
          header: 'Trạng thái',
          cell: (row) => (row.isActive ? <StatusBadge tone="ok">Đang dùng</StatusBadge> : <StatusBadge>Ngừng</StatusBadge>),
        },
      ] as Column<JobPosition>[]}
      rows={positions.data ?? []}
      loading={positions.isLoading}
      error={positions.error}
      onRefresh={() => positions.refetch()}
      defaultSort={{ key: 'name', dir: 'asc' }}
      emptyTitle="Chưa khai báo chức danh nào"
    />
  )
}

function EmployeeDrawer({ employeeId, onClose }: { employeeId: string | null; onClose: () => void }) {
  const [tab, setTab] = useState('profile')
  const employee = useEmployee(employeeId ?? undefined)
  const contracts = useContracts(tab === 'contracts' ? (employeeId ?? undefined) : undefined)
  const documents = useEmployeeDocuments(tab === 'documents' ? (employeeId ?? undefined) : undefined)
  const leave = useLeaveBalances(tab === 'leave' ? (employeeId ?? undefined) : undefined)

  useEffect(() => {
    if (employeeId) setTab('profile')
  }, [employeeId])

  const e = employee.data

  return (
    <Drawer
      open={!!employeeId}
      onClose={onClose}
      width="lg"
      title={e?.fullName ?? 'Hồ sơ nhân viên'}
      meta={
        e && (
          <>
            <span className="tnum">{e.employeeCode}</span>
            <span>@{e.username}</span>
            <StatusBadge tone={employeeStatus(e.status).tone}>{employeeStatus(e.status).label}</StatusBadge>
          </>
        )
      }
    >
      <div className="border-b border-line bg-panel">
        <Tabs
          items={[
            { id: 'profile', label: 'Hồ sơ' },
            { id: 'contracts', label: 'Hợp đồng' },
            { id: 'documents', label: 'Giấy tờ' },
            { id: 'leave', label: 'Ngày phép' },
          ]}
          active={tab}
          onChange={setTab}
        />
      </div>
      <div className="p-3">
        {tab === 'profile' && (
          <Panel padded>
            <div className="mb-3 flex items-center gap-3">
              <Avatar url={e?.avatar} name={e?.fullName ?? ''} size="lg" />
              <div className="min-w-0">
                <p className="truncate text-sm font-semibold text-ink">{e?.fullName}</p>
                <p className="truncate text-xs text-ink-3">{e?.position}</p>
              </div>
            </div>
            <KeyValue
              rows={[
                ['Mã nhân viên', e?.employeeCode || null],
                ['Tài khoản', e?.username || null],
                ['Ngày sinh', e?.dob ? date(e.dob) : null],
                ['Giới tính', e?.gender || null],
                ['Điện thoại', e?.phone || null],
                ['Thư điện tử', e?.email || null],
                ['Địa chỉ', e?.address || null],
                ['Phòng ban', e?.departmentName || null],
                ['Địa điểm', e?.locationName || null],
                ['Quản lý trực tiếp', e?.managerName || null],
                ['Chức vụ được gán', e?.positions?.map((p) => p.name).join(', ') || null],
                ['Ngày vào làm', e?.hireDate ? date(e.hireDate) : null],
              ]}
            />
          </Panel>
        )}
        {tab === 'contracts' && (
          <Panel>
            <DataTable
              columns={CONTRACT_COLUMNS}
              rows={contracts.data ?? []}
              getKey={(row) => row.id}
              loading={contracts.isLoading}
              density="compact"
              emptyTitle="Chưa có hợp đồng"
            />
          </Panel>
        )}
        {tab === 'documents' && (
          <Panel>
            <DataTable
              columns={DOCUMENT_COLUMNS}
              rows={documents.data ?? []}
              getKey={(row) => row.id}
              loading={documents.isLoading}
              density="compact"
              emptyTitle="Chưa có giấy tờ"
            />
          </Panel>
        )}
        {tab === 'leave' && (
          <Panel>
            <DataTable
              columns={[
                { key: 'year', priority: 1, header: 'Năm', cell: (row) => <span className="tnum">{row.year}</span> },
                { key: 'type', priority: 1, header: 'Loại phép', cell: (row) => LEAVE_TYPE_LABELS[row.leaveType] ?? row.leaveType },
                { key: 'total', priority: 1, header: 'Được hưởng', align: 'right', cell: (row) => <span className="tnum">{num(row.totalDays)}</span> },
                { key: 'used', priority: 2, header: 'Đã dùng', align: 'right', cell: (row) => <span className="tnum">{num(row.usedDays)}</span> },
                { key: 'remaining', priority: 1, header: 'Còn lại', align: 'right', cell: (row) => <span className="tnum font-semibold">{num(row.remainingDays)}</span> },
              ]}
              rows={leave.data ?? []}
              getKey={(row) => row.id}
              loading={leave.isLoading}
              density="compact"
              emptyTitle="Chưa cấp ngày phép"
            />
          </Panel>
        )}
      </div>
    </Drawer>
  )
}

function DepartmentModal({
  item,
  onClose,
  departments,
}: {
  item: Department | null | 'new'
  onClose: () => void
  departments: Department[]
}) {
  const toast = useToast()
  const save = useSaveDepartment()
  const employees = useEmployees({}, item !== null)
  const open = item !== null
  const editing = item && item !== 'new' ? item : null
  const [form, setForm] = useState({ code: '', name: '', parentId: '', managerEmployeeId: '' })
  const [touched, setTouched] = useState(false)

  useEffect(() => {
    if (open) {
      setForm({
        code: editing?.code ?? '',
        name: editing?.name ?? '',
        parentId: editing?.parentId ?? '',
        managerEmployeeId: editing?.managerEmployeeId ?? '',
      })
      setTouched(false)
    }
  }, [open, editing])

  const submit = async () => {
    setTouched(true)
    if (!form.name.trim()) return
    try {
      await save.mutateAsync({
        id: editing?.id,
        body: {
          code: form.code.trim(),
          name: form.name.trim(),
          parentId: form.parentId || null,
          managerEmployeeId: form.managerEmployeeId || null,
        },
      })
      toast.success(editing ? 'Đã cập nhật phòng ban' : 'Đã thêm phòng ban')
      onClose()
    } catch (error) {
      toast.error('Không lưu được', errorMessage(error))
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? `Sửa phòng ban ${editing.name}` : 'Thêm phòng ban'}
      size="sm"
      dismissible={false}
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={save.isPending}>
            Huỷ
          </Button>
          <Button size="sm" variant="primary" loading={save.isPending} onClick={() => void submit()}>
            Lưu
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        <Field label="Tên phòng ban" required error={touched && !form.name.trim() ? 'Nhập tên phòng ban' : null}>
          <Input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} data-autofocus="" />
        </Field>
        <Field label="Mã phòng ban">
          <Input value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} className="tnum" />
        </Field>
        <Field label="Trực thuộc">
          <Combobox
            value={form.parentId}
            onChange={(v) => setForm({ ...form, parentId: v })}
            clearable
            placeholder="Không trực thuộc phòng nào"
            options={departments.filter((d) => d.id !== editing?.id).map((d) => ({ value: d.id, label: d.name }))}
          />
        </Field>
        <Field label="Trưởng phòng">
          <Combobox
            value={form.managerEmployeeId}
            onChange={(v) => setForm({ ...form, managerEmployeeId: v })}
            clearable
            placeholder="Chưa phân công"
            loading={employees.isLoading}
            options={(employees.data ?? []).map((e) => ({ value: e.id, label: e.fullName, description: e.position }))}
          />
        </Field>
        {editing?.isAccounting && (
          <InlineAlert tone="info">
            Đây là phòng kế toán của hệ thống. Cờ này do hệ thống quản lý, không sửa được ở đây.
          </InlineAlert>
        )}
      </div>
    </Modal>
  )
}

function LocationModal({ item, onClose }: { item: Location | null | 'new'; onClose: () => void }) {
  const toast = useToast()
  const save = useSaveLocation()
  const open = item !== null
  const editing = item && item !== 'new' ? item : null
  const [form, setForm] = useState({ code: '', name: '', address: '' })
  const [touched, setTouched] = useState(false)

  useEffect(() => {
    if (open) {
      setForm({ code: editing?.code ?? '', name: editing?.name ?? '', address: editing?.address ?? '' })
      setTouched(false)
    }
  }, [open, editing])

  const submit = async () => {
    setTouched(true)
    if (!form.name.trim()) return
    try {
      await save.mutateAsync({ id: editing?.id, body: { ...form, name: form.name.trim() } })
      toast.success(editing ? 'Đã cập nhật địa điểm' : 'Đã thêm địa điểm')
      onClose()
    } catch (error) {
      toast.error('Không lưu được', errorMessage(error))
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? `Sửa địa điểm ${editing.name}` : 'Thêm địa điểm'}
      size="sm"
      dismissible={false}
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={save.isPending}>
            Huỷ
          </Button>
          <Button size="sm" variant="primary" loading={save.isPending} onClick={() => void submit()}>
            Lưu
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        <Field label="Tên địa điểm" required error={touched && !form.name.trim() ? 'Nhập tên địa điểm' : null}>
          <Input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} data-autofocus="" />
        </Field>
        <Field label="Mã địa điểm">
          <Input value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} className="tnum" />
        </Field>
        <Field label="Địa chỉ">
          <Textarea value={form.address} onChange={(e) => setForm({ ...form, address: e.target.value })} rows={2} />
        </Field>
      </div>
    </Modal>
  )
}

/* ============================================================================
   Bảng lương
   ========================================================================== */

export function PayrollPage() {
  const fiscal = useFiscal()
  const [tab, setTab] = useState('published')
  const [search, setSearch] = useState('')
  const [ackStatus, setAckStatus] = useState('all')

  const published = usePublishedPayslips(fiscal.period, { search: search.trim() || undefined, status: ackStatus }, tab === 'published')
  const salaries = useSalaries(tab === 'structure')

  const summary = published.data?.summary

  if (tab === 'structure') {
    const rows = (salaries.data ?? []).filter(
      (s) => !search || matches(`${s.employeeName} ${s.employeeCode} ${s.departmentName}`, search),
    )
    const totalBase = rows.reduce((sum, r) => sum + r.baseSalary, 0)
    return (
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label="Nhân viên có mức lương" value={salaries.data ? salaries.data.filter((s) => s.hasSalary).length : '…'} />
            <Figure label="Chưa gán lương" value={salaries.data ? salaries.data.filter((s) => !s.hasSalary).length : '…'} tone={salaries.data?.some((s) => !s.hasSalary) ? 'warn' : undefined} />
            <Figure label="Tổng lương cứng tháng" value={salaries.data ? vnd(salaries.data.reduce((s, r) => s + r.baseSalary, 0)) : '…'} />
          </FigureStrip>
        }
        tabs={PAYROLL_TABS(summary?.publishedCount, salaries.data?.length)}
        tab={tab}
        onTabChange={setTab}
        filters={
          <SearchInput
            size="sm"
            className="w-64"
            placeholder="Nhân viên, mã, phòng ban"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            onClear={() => setSearch('')}
          />
        }
        columns={[
          { key: 'code', priority: 2, header: 'Mã NV', cell: (row) => <span className="tnum">{row.employeeCode}</span>, sortValue: (r) => r.employeeCode },
          { key: 'name', priority: 1, header: 'Nhân viên', cell: (row) => <span className="font-medium">{row.employeeName}</span>, sortValue: (r) => r.employeeName, total: 'Tổng cộng' },
          { key: 'department', priority: 2, header: 'Phòng ban', cell: (row) => row.departmentName, sortValue: (r) => r.departmentName },
          {
            key: 'contract', priority: 3,
            header: 'Hợp đồng',
            cell: (row) =>
              row.hardSalary.fromContract ? (
                <span className="tnum">{row.hardSalary.contractNo}</span>
              ) : (
                <span className="text-ink-3">Chưa có</span>
              ),
          },
          { key: 'contractBase', priority: 3, header: 'Lương ký', align: 'right', cell: (row) => <Money value={row.hardSalary.contractBase} />, hidden: true },
          { key: 'raise', priority: 3, header: 'Đã tăng', align: 'right', cell: (row) => <Money value={row.hardSalary.raiseTotal} /> },
          { key: 'base', priority: 1, header: 'Lương cứng', align: 'right', cell: (row) => <Money value={row.baseSalary} strong />, sortValue: (r) => r.baseSalary, total: <Money value={totalBase} zero="zero" /> },
          { key: 'allowance', priority: 2, header: 'Phụ cấp', align: 'right', cell: (row) => <Money value={row.allowance} /> },
          { key: 'extra', priority: 3, header: 'Khoản tự nhập', align: 'right', cell: (row) => row.extraCount || null },
          {
            key: 'status', priority: 1,
            header: 'Trạng thái',
            cell: (row) =>
              !row.hasSalary ? (
                <StatusBadge tone="danger">Chưa gán lương</StatusBadge>
              ) : row.hardSalary.fromContract && !row.hardSalary.effective ? (
                <StatusBadge tone="warn">Hợp đồng hết hiệu lực</StatusBadge>
              ) : (
                <StatusBadge tone="ok">Đã gán</StatusBadge>
              ),
          },
        ]}
        rows={rows}
        getKey={(row) => row.employeeId}
        loading={salaries.isLoading}
        error={salaries.error}
        onRefresh={() => salaries.refetch()}
        defaultSort={{ key: 'name', dir: 'asc' }}
        emptyTitle="Chưa có nhân viên nào"
      />
    )
  }

  return (
    <ModuleScreen
      figures={
        <FigureStrip>
          <Figure label="Nhân viên đang làm việc" value={summary?.activeEmployeeCount ?? '…'} />
          <Figure label="Phiếu đã phát hành" value={summary?.publishedCount ?? '…'} />
          <Figure
            label="Chờ nhân viên xác nhận"
            value={summary?.pendingAcknowledgementCount ?? '…'}
            tone={summary?.pendingAcknowledgementCount ? 'warn' : undefined}
          />
          <Figure label="Tổng khấu trừ" value={summary ? vnd(summary.totalDeductions) : '…'} />
          <Figure label="Tổng thực trả" value={summary ? vnd(summary.totalNetPay) : '…'} tone="brand" />
        </FigureStrip>
      }
      tabs={PAYROLL_TABS(summary?.publishedCount, salaries.data?.length)}
      tab={tab}
      onTabChange={setTab}
      filters={
        <>
          <MonthPicker value={fiscal.period} onChange={fiscal.setPeriod} size="sm" className="w-40" />
          <SearchInput
            size="sm"
            className="w-56"
            placeholder="Nhân viên, mã, phòng ban"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            onClear={() => setSearch('')}
          />
          <Select size="sm" value={ackStatus} onChange={(event) => setAckStatus(event.target.value)} className="w-40">
            <option value="all">Mọi trạng thái</option>
            <option value="pending">Chờ xác nhận</option>
            <option value="acknowledged">Đã xác nhận</option>
          </Select>
        </>
      }
      columns={[
        { key: 'code', priority: 2, header: 'Mã NV', cell: (row) => <span className="tnum">{row.employeeCode}</span>, sortValue: (r) => r.employeeCode },
        { key: 'name', priority: 1, header: 'Nhân viên', cell: (row) => <span className="font-medium">{row.employeeName}</span>, sortValue: (r) => r.employeeName, total: 'Tổng cộng' },
        { key: 'department', priority: 2, header: 'Phòng ban', cell: (row) => row.departmentName, sortValue: (r) => r.departmentName },
        { key: 'location', priority: 3, header: 'Địa điểm', cell: (row) => row.locationName, hidden: true },
        { key: 'overtime', priority: 3, header: 'Giờ tăng ca', align: 'right', cell: (row) => <span className="tnum">{num(row.overtimeHours)}</span> },
        { key: 'earnings', priority: 2, header: 'Tổng thu nhập', align: 'right', cell: (row) => <Money value={row.totalEarnings} />, sortValue: (r) => r.totalEarnings, total: <Money value={summary?.totalEarnings} zero="zero" /> },
        { key: 'deductions', priority: 2, header: 'Khấu trừ', align: 'right', cell: (row) => <Money value={row.totalDeductions} />, total: <Money value={summary?.totalDeductions} zero="zero" /> },
        { key: 'net', priority: 1, header: 'Thực nhận', align: 'right', cell: (row) => <Money value={row.netPay} strong />, sortValue: (r) => r.netPay, total: <Money value={summary?.totalNetPay} zero="zero" /> },
        {
          key: 'status', priority: 1,
          header: 'Xác nhận',
          cell: (row) =>
            row.status === 'Acknowledged' ? (
              <StatusBadge tone="ok">Đã xác nhận</StatusBadge>
            ) : (
              <StatusBadge tone="warn">Chờ xác nhận</StatusBadge>
            ),
          sortValue: (r) => r.status,
        },
        { key: 'ackAt', priority: 3, header: 'Xác nhận lúc', cell: (row) => (row.acknowledgedAt ? dateTime(row.acknowledgedAt) : null), hidden: true },
      ]}
      rows={published.data?.items ?? []}
      getKey={(row) => row.id}
      loading={published.isLoading}
      error={published.error}
      onRefresh={() => published.refetch()}
      defaultSort={{ key: 'name', dir: 'asc' }}
      emptyTitle={`Chưa phát hành phiếu lương nào cho ${monthLabel(fiscal.period).toLowerCase()}`}
    />
  )
}

const PAYROLL_TABS = (published?: number, employees?: number) => [
  { id: 'published', label: 'Phiếu lương đã phát hành', count: published },
  { id: 'structure', label: 'Mức lương nhân viên', count: employees },
]

/* ============================================================================
   Phạt và kỷ luật
   ========================================================================== */

export function PenaltiesPage() {
  const auth = useAuth()
  const toast = useToast()
  const canManage = auth.can(PERM.penaltyManage)
  const [tab, setTab] = useState('open')
  const [search, setSearch] = useState('')
  const penalties = usePenalties({ scope: canManage ? 'all' : 'mine' }, tab !== 'refunds')
  const refunds = usePenaltyRefunds(canManage ? 'all' : 'mine', tab === 'refunds')
  const types = usePenaltyTypes()
  const waive = useWaivePenalty()
  const [composing, setComposing] = useState(false)
  const [waiving, setWaiving] = useState<Penalty | null>(null)
  const [open, setOpen] = useState<Penalty | null>(null)

  const all = penalties.data ?? []
  const settled = (p: Penalty) => p.status === 'Waived' || !!p.progress?.settled || p.penaltyType !== 'fine'
  const rows = all.filter((p) => {
    if (tab === 'open' && settled(p)) return false
    if (tab === 'settled' && !settled(p)) return false
    if (search && !matches(`${p.penaltyNo} ${p.employeeName} ${p.reason} ${p.penaltyTypeLabel}`, search)) return false
    return true
  })

  const outstanding = all.filter((p) => !settled(p)).reduce((sum, p) => sum + (p.progress?.remaining ?? p.amount), 0)
  const collected = all.reduce((sum, p) => sum + (p.progress?.deducted ?? 0), 0)

  if (tab === 'refunds')
    return (
      <ModuleScreen
        tabs={PENALTY_TABS(all.filter((p) => !settled(p)).length, all.filter(settled).length, refunds.data?.length)}
        tab={tab}
        onTabChange={setTab}
        columns={[
          { key: 'no', priority: 1, header: 'Số hoàn', cell: (row) => <span className="tnum font-medium">{row.refundNo}</span> },
          { key: 'employee', priority: 1, header: 'Nhân viên', cell: (row) => row.employeeName, sortValue: (r) => r.employeeName },
          { key: 'penalty', priority: 2, header: 'Quyết định phạt', cell: (row) => <span className="tnum">{row.penaltyNo}</span> },
          { key: 'amount', priority: 1, header: 'Số tiền hoàn', align: 'right', cell: (row) => <Money value={row.amount} strong />, sortValue: (r) => r.amount },
          { key: 'reason', priority: 3, header: 'Lý do', cell: (row) => row.reason, truncate: true },
          { key: 'method', priority: 3, header: 'Hình thức', cell: (row) => (row.payoutMethod === 'payroll' ? `Bù vào lương ${row.appliedPeriod}` : 'Chi tiền mặt') },
          { key: 'created', priority: 2, header: 'Tạo lúc', cell: (row) => dateTime(row.createdAt), sortValue: (r) => r.createdAt },
          {
            key: 'status', priority: 1,
            header: 'Trạng thái',
            cell: (row) => <StatusBadge tone={refundStatus(row.status).tone}>{refundStatus(row.status).label}</StatusBadge>,
          },
        ]}
        rows={refunds.data ?? []}
        loading={refunds.isLoading}
        error={refunds.error}
        onRefresh={() => refunds.refetch()}
        defaultSort={{ key: 'created', dir: 'desc' }}
        emptyTitle="Chưa có yêu cầu hoàn tiền phạt nào"
      />
    )

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label="Quyết định còn hiệu lực" value={penalties.data ? all.filter((p) => !settled(p)).length : '…'} />
            <Figure label="Còn phải thu" value={penalties.data ? vnd(outstanding) : '…'} tone={outstanding ? 'warn' : undefined} />
            <Figure label="Đã thu" value={penalties.data ? vnd(collected) : '…'} />
            <Figure label="Đã tất toán" value={penalties.data ? all.filter((p) => p.progress?.settled).length : '…'} />
          </FigureStrip>
        }
        tabs={PENALTY_TABS(all.filter((p) => !settled(p)).length, all.filter(settled).length, refunds.data?.length)}
        tab={tab}
        onTabChange={setTab}
        filters={
          <SearchInput
            size="sm"
            className="w-64"
            placeholder="Số quyết định, nhân viên, lý do"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            onClear={() => setSearch('')}
          />
        }
        actions={
          canManage && (
            <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setComposing(true)}>
              Ghi nhận vi phạm
            </Button>
          )
        }
        columns={[
          { key: 'no', priority: 1, header: 'Số quyết định', width: '8rem', cell: (row) => <span className="tnum font-medium">{row.penaltyNo}</span>, sortValue: (r) => r.penaltyNo },
          { key: 'date', priority: 1, header: 'Ngày', width: '6.5rem', cell: (row) => date(row.penaltyDate), sortValue: (r) => r.penaltyDate },
          { key: 'employee', priority: 1, header: 'Nhân viên', cell: (row) => row.employeeName, sortValue: (r) => r.employeeName },
          { key: 'type', priority: 2, header: 'Hình thức', cell: (row) => row.penaltyTypeLabel, sortValue: (r) => r.penaltyType },
          { key: 'reason', priority: 3, header: 'Lý do', cell: (row) => row.reason, truncate: true },
          { key: 'amount', priority: 1, header: 'Mức phạt', align: 'right', cell: (row) => <Money value={row.amount} zero="blank" />, sortValue: (r) => r.amount },
          { key: 'deducted', priority: 2, header: 'Đã thu', align: 'right', cell: (row) => <Money value={row.progress?.deducted} zero="blank" /> },
          { key: 'remaining', priority: 1, header: 'Còn lại', align: 'right', cell: (row) => <Money value={row.progress?.remaining} zero="blank" strong /> },
          {
            key: 'installments', priority: 3,
            header: 'Kỳ trừ',
            cell: (row) => (row.penaltyType === 'fine' ? `${row.progress?.paidMonths ?? 0}/${row.installments}` : null),
          },
          {
            key: 'status', priority: 1,
            header: 'Trạng thái',
            cell: (row) => <StatusBadge tone={penaltyStatus(row).tone}>{penaltyStatus(row).label}</StatusBadge>,
          },
          {
            key: 'actions', priority: 1,
            header: '',
            align: 'right',
            locked: true,
            cell: (row) =>
              canManage && row.status !== 'Waived' && !row.progress?.settled ? (
                <span className="row-actions">
                  <Button size="sm" variant="ghost" onClick={(e) => { e.stopPropagation(); setWaiving(row) }}>
                    Miễn
                  </Button>
                </span>
              ) : null,
          },
        ]}
        rows={rows}
        loading={penalties.isLoading}
        error={penalties.error}
        onRefresh={() => penalties.refetch()}
        onRowClick={(row) => setOpen(row)}
        activeKey={open?.id}
        defaultSort={{ key: 'date', dir: 'desc' }}
        emptyTitle="Không có quyết định nào trong bộ lọc này"
      />

      <Drawer
        open={!!open}
        onClose={() => setOpen(null)}
        width="md"
        title={open ? `Quyết định ${open.penaltyNo}` : ''}
        meta={open && <><span>{open.employeeName}</span><StatusBadge tone={penaltyStatus(open).tone}>{penaltyStatus(open).label}</StatusBadge></>}
      >
        {open && (
          <div className="flex flex-col gap-3 p-3">
            <Panel padded>
              <KeyValue
                rows={[
                  ['Nhân viên', `${open.employeeName} (${open.employeeCode})`],
                  ['Hình thức', open.penaltyTypeLabel],
                  ['Ngày vi phạm', date(open.penaltyDate)],
                  ['Lý do', open.reason],
                  ['Ghi chú', open.note || null],
                  ['Mức phạt', <Money key="a" value={open.amount} zero="zero" strong />],
                  ['Số kỳ trừ', open.penaltyType === 'fine' ? open.installments : null],
                  ['Bắt đầu trừ từ kỳ', open.startPeriod ? monthLabel(open.startPeriod) : null],
                  ['Người ghi nhận', `${open.createdBy} · ${dateTime(open.createdAt)}`],
                ]}
              />
            </Panel>
            {open.progress && (
              <Panel title="Tiến trình khấu trừ" padded>
                <KeyValue
                  rows={[
                    ['Đã thu', <Money key="d" value={open.progress.deducted} zero="zero" />],
                    ['Còn lại', <Money key="r" value={open.progress.remaining} zero="zero" strong />],
                    ['Số kỳ đã trừ', `${open.progress.paidMonths}/${open.installments}`],
                    ['Kỳ trừ kế tiếp', open.progress.nextPeriod ? `${monthLabel(open.progress.nextPeriod)} · ${vnd(open.progress.nextAmount)}` : null],
                  ]}
                />
              </Panel>
            )}
          </div>
        )}
      </Drawer>

      {composing && <PenaltyModal onClose={() => setComposing(false)} types={types.data ?? []} />}

      <ConfirmDialog
        open={!!waiving}
        onClose={() => setWaiving(null)}
        title={`Miễn quyết định ${waiving?.penaltyNo ?? ''}`}
        message="Phần chưa thu sẽ ngừng khấu trừ vào các kỳ lương sau. Số đã thu giữ nguyên."
        confirmLabel="Miễn phạt"
        tone="danger"
        busy={waive.isPending}
        onConfirm={async () => {
          if (!waiving) return
          try {
            await waive.mutateAsync(waiving.id)
            toast.success('Đã miễn quyết định phạt')
            setWaiving(null)
          } catch (error) {
            toast.error('Không miễn được', errorMessage(error))
          }
        }}
      />
    </>
  )
}

const PENALTY_TABS = (open?: number, settled?: number, refunds?: number) => [
  { id: 'open', label: 'Còn hiệu lực', count: open },
  { id: 'settled', label: 'Đã tất toán hoặc miễn', count: settled },
  { id: 'refunds', label: 'Hoàn tiền phạt', count: refunds },
]

function PenaltyModal({ onClose, types }: { onClose: () => void; types: Array<{ type: string; label: string }> }) {
  const toast = useToast()
  const save = useSavePenalty()
  const employees = useEmployees({})
  const [form, setForm] = useState({
    employeeId: '',
    penaltyType: 'fine',
    penaltyDate: todayISO(),
    amount: null as number | null,
    installments: 1,
    startPeriod: monthKey(),
    reason: '',
    note: '',
  })
  const [touched, setTouched] = useState(false)
  const isFine = form.penaltyType === 'fine'
  const problems = {
    employee: !form.employeeId ? 'Chọn nhân viên' : null,
    reason: !form.reason.trim() ? 'Nhập lý do phạt' : null,
    amount: isFine && !form.amount ? 'Nhập mức phạt' : null,
  }

  const submit = async () => {
    setTouched(true)
    if (problems.employee || problems.reason || problems.amount) return
    try {
      await save.mutateAsync({
        body: {
          employeeId: form.employeeId,
          penaltyType: form.penaltyType,
          penaltyDate: form.penaltyDate,
          amount: form.amount ?? 0,
          installments: isFine ? form.installments : 1,
          startPeriod: form.startPeriod,
          reason: form.reason.trim(),
          note: form.note.trim(),
        },
      })
      toast.success('Đã ghi nhận vi phạm')
      onClose()
    } catch (error) {
      toast.error('Không lưu được', errorMessage(error))
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Ghi nhận vi phạm"
      size="sm"
      dismissible={false}
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={save.isPending}>
            Huỷ
          </Button>
          <Button size="sm" variant="primary" loading={save.isPending} onClick={() => void submit()}>
            Ghi nhận
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        <Field label="Nhân viên" required error={touched ? problems.employee : null}>
          <Combobox
            value={form.employeeId}
            onChange={(v) => setForm({ ...form, employeeId: v })}
            placeholder="Chọn nhân viên"
            loading={employees.isLoading}
            options={(employees.data ?? []).map((e) => ({ value: e.id, label: e.fullName, description: e.employeeCode, keywords: e.username }))}
          />
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Hình thức">
            <Select value={form.penaltyType} onChange={(e) => setForm({ ...form, penaltyType: e.target.value })}>
              {types.map((t) => (
                <option key={t.type} value={t.type}>
                  {t.label}
                </option>
              ))}
            </Select>
          </Field>
          <Field label="Ngày vi phạm" required>
            <DatePicker value={form.penaltyDate} onChange={(v) => setForm({ ...form, penaltyDate: v })} clearable={false} />
          </Field>
        </div>
        {isFine && (
          <>
            <Field label="Mức phạt" required error={touched ? problems.amount : null}>
              <NumberInput value={form.amount} onChange={(v) => setForm({ ...form, amount: v })} />
            </Field>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Số kỳ trừ" hint="Chia đều vào các kỳ lương">
                <NumberInput value={form.installments} onChange={(v) => setForm({ ...form, installments: Math.max(1, v ?? 1) })} />
              </Field>
              <Field label="Bắt đầu trừ từ kỳ">
                <MonthPicker value={form.startPeriod} onChange={(v) => setForm({ ...form, startPeriod: v })} />
              </Field>
            </div>
          </>
        )}
        <Field label="Lý do" required error={touched ? problems.reason : null}>
          <Input value={form.reason} onChange={(e) => setForm({ ...form, reason: e.target.value })} />
        </Field>
        <Field label="Ghi chú">
          <Textarea value={form.note} onChange={(e) => setForm({ ...form, note: e.target.value })} rows={2} />
        </Field>
      </div>
    </Modal>
  )
}

/* ============================================================================
   Phát triển nhân sự
   ========================================================================== */

export function TalentPage() {
  const [tab, setTab] = useState('onboarding')
  const onboarding = useOnboarding(tab === 'onboarding')
  const performance = usePerformance(tab === 'performance')
  const training = useTraining(tab === 'training')
  const benefits = useBenefits(tab === 'benefits')
  const complete = useCompleteOnboardingTask()
  const toast = useToast()
  const [review, setReview] = useState<import('@/api/hr').PerformanceReview | null>(null)

  const tabs = [
    { id: 'onboarding', label: 'Hội nhập', count: onboarding.data?.items.filter((i) => !i.completed).length },
    { id: 'performance', label: 'Mục tiêu và đánh giá', count: performance.data?.goals.length },
    { id: 'training', label: 'Đào tạo', count: training.data?.length },
    { id: 'benefits', label: 'Quyền lợi' },
  ]

  return (
    <Stack>
      <Panel>
        <Tabs items={tabs} active={tab} onChange={setTab} />

        {tab === 'onboarding' && (
          <>
            {onboarding.data?.mentorName && (
              <div className="border-b border-line-2 px-3.5 py-2.5 text-sm text-ink-2">
                Người hướng dẫn: <strong className="font-medium text-ink">{onboarding.data.mentorName}</strong>
              </div>
            )}
            <DataTable
              columns={[
                { key: 'title', priority: 1, header: 'Việc cần làm', cell: (row) => <span className={cn(row.completed && 'text-ink-3 line-through')}>{row.title}</span> },
                { key: 'due', priority: 1, header: 'Hạn', cell: (row) => (row.dueAt ? date(row.dueAt) : null) },
                {
                  key: 'status', priority: 1,
                  header: 'Trạng thái',
                  cell: (row) => (row.completed ? <StatusBadge tone="ok">Hoàn tất</StatusBadge> : <StatusBadge tone="warn">Chưa xong</StatusBadge>),
                },
                {
                  key: 'action', priority: 1,
                  header: '',
                  align: 'right',
                  locked: true,
                  cell: (row) =>
                    row.completed ? null : (
                      <Button
                        size="sm"
                        variant="ghost"
                        loading={complete.isPending && complete.variables === row.id}
                        onClick={async () => {
                          try {
                            await complete.mutateAsync(row.id)
                            toast.success('Đã đánh dấu hoàn tất')
                          } catch (error) {
                            toast.error('Không cập nhật được', errorMessage(error))
                          }
                        }}
                      >
                        Đánh dấu xong
                      </Button>
                    ),
                },
              ]}
              rows={onboarding.data?.items ?? []}
              getKey={(row) => row.id}
              loading={onboarding.isLoading}
              error={onboarding.isError ? errorMessage(onboarding.error) : undefined}
              emptyTitle="Không có việc hội nhập nào"
            />
          </>
        )}

        {tab === 'performance' && (
          <>
            <DataTable
              columns={[
                { key: 'title', priority: 1, header: 'Mục tiêu', cell: (row) => <span className="font-medium">{row.title}</span> },
                { key: 'description', priority: 3, header: 'Mô tả', cell: (row) => row.description, truncate: true },
                { key: 'due', priority: 1, header: 'Hạn', cell: (row) => (row.dueAt ? date(row.dueAt) : null) },
                {
                  key: 'progress', priority: 1,
                  header: 'Tiến độ',
                  align: 'right',
                  cell: (row) => (
                    <span className="tnum">
                      {num(row.progress)} / {num(row.target)} {row.unit}
                    </span>
                  ),
                },
              ]}
              rows={performance.data?.goals ?? []}
              getKey={(row) => row.id}
              loading={performance.isLoading}
              emptyTitle="Chưa được giao mục tiêu nào"
            />
            <div className="border-t border-line">
              <DataTable
                columns={[
                  { key: 'period', priority: 1, header: 'Kỳ đánh giá', cell: (row) => <span className="font-medium">{row.period}</span> },
                  { key: 'closes', priority: 3, header: 'Đóng lúc', cell: (row) => (row.closesAt ? date(row.closesAt) : null) },
                  { key: 'self', priority: 3, header: 'Tự đánh giá', cell: (row) => row.selfAssessment, truncate: true },
                  { key: 'manager', priority: 3, header: 'Nhận xét quản lý', cell: (row) => row.managerComment, truncate: true },
                  { key: 'score', priority: 1, header: 'Điểm', align: 'right', cell: (row) => (row.score == null ? null : <span className="tnum">{num(row.score)}</span>) },
                  {
                    key: 'status', priority: 1,
                    header: 'Trạng thái',
                    cell: (row) => (row.status === 'open' ? <StatusBadge tone="warn">Đang mở</StatusBadge> : <StatusBadge>Đã đóng</StatusBadge>),
                  },
                  {
                    key: 'action', priority: 1,
                    header: '',
                    align: 'right',
                    locked: true,
                    cell: (row) =>
                      row.status === 'open' ? (
                        <Button size="sm" variant="ghost" onClick={() => setReview(row)}>
                          Tự đánh giá
                        </Button>
                      ) : null,
                  },
                ]}
                rows={performance.data?.reviews ?? []}
                getKey={(row) => row.id}
                loading={performance.isLoading}
                emptyTitle="Chưa có kỳ đánh giá nào"
              />
            </div>
          </>
        )}

        {tab === 'training' && (
          <DataTable
            columns={[
              { key: 'title', priority: 1, header: 'Khoá học', cell: (row) => <span className="font-medium">{row.title}</span> },
              { key: 'description', priority: 3, header: 'Nội dung', cell: (row) => row.description, truncate: true },
              { key: 'progress', priority: 1, header: 'Tiến độ', align: 'right', cell: (row) => <span className="tnum">{row.progress}%</span> },
              { key: 'score', priority: 2, header: 'Điểm bài kiểm tra', align: 'right', cell: (row) => (row.score == null ? null : <span className="tnum">{num(row.score)}</span>) },
              { key: 'cert', priority: 3, header: 'Chứng chỉ hết hạn', cell: (row) => (row.certificateExpiresAt ? date(row.certificateExpiresAt) : null) },
              {
                key: 'status', priority: 1,
                header: 'Trạng thái',
                cell: (row) =>
                  row.completedAt ? (
                    <StatusBadge tone="ok">Đã hoàn thành</StatusBadge>
                  ) : row.progress > 0 ? (
                    <StatusBadge tone="warn">Đang học</StatusBadge>
                  ) : (
                    <StatusBadge>Chưa bắt đầu</StatusBadge>
                  ),
              },
              {
                key: 'link', priority: 1,
                header: '',
                align: 'right',
                locked: true,
                cell: (row) =>
                  row.materialUrl ? (
                    <a className="link text-xs" href={row.materialUrl} target="_blank" rel="noreferrer">
                      Mở tài liệu
                    </a>
                  ) : null,
              },
            ]}
            rows={training.data ?? []}
            getKey={(row) => row.id}
            loading={training.isLoading}
            error={training.isError ? errorMessage(training.error) : undefined}
            emptyTitle="Chưa có khoá đào tạo nào"
          />
        )}

        {tab === 'benefits' && (
          <div className="flex flex-col gap-3 p-3">
            <FigureStrip>
              <Figure label="Phép năm được hưởng" value={benefits.data ? num(benefits.data.leaveTotal) : '…'} />
              <Figure label="Đã dùng" value={benefits.data ? num(benefits.data.leaveUsed) : '…'} />
              <Figure label="Còn lại" value={benefits.data ? num(benefits.data.leaveRemaining) : '…'} tone="ok" />
              <Figure label="Điểm thưởng" value={benefits.data ? benefits.data.rewards.reduce((s, r) => s + r.points, 0) : '…'} />
            </FigureStrip>
            <Panel title="Quyền lợi">
              <DataTable
                columns={[
                  { key: 'title', priority: 1, header: 'Quyền lợi', cell: (row) => <span className="font-medium">{row.title}</span> },
                  { key: 'type', priority: 2, header: 'Nhóm', cell: (row) => row.type },
                  { key: 'value', priority: 2, header: 'Nội dung', cell: (row) => row.value, truncate: true },
                  { key: 'from', priority: 2, header: 'Từ ngày', cell: (row) => date(row.validFrom) },
                  { key: 'to', priority: 2, header: 'Đến ngày', cell: (row) => date(row.validTo) },
                ]}
                rows={benefits.data?.benefits ?? []}
                getKey={(row) => row.id}
                loading={benefits.isLoading}
                density="compact"
                emptyTitle="Chưa có quyền lợi nào được ghi nhận"
              />
            </Panel>
            <Panel title="Khen thưởng">
              <DataTable
                columns={[
                  { key: 'title', priority: 1, header: 'Nội dung', cell: (row) => <span className="font-medium">{row.title}</span> },
                  { key: 'points', priority: 1, header: 'Điểm', align: 'right', cell: (row) => <span className="tnum">{row.points}</span> },
                  { key: 'date', priority: 2, header: 'Ngày', cell: (row) => date(row.awardedAt) },
                  { key: 'note', priority: 3, header: 'Ghi chú', cell: (row) => row.note, truncate: true },
                ]}
                rows={benefits.data?.rewards ?? []}
                getKey={(row) => row.id}
                loading={benefits.isLoading}
                density="compact"
                emptyTitle="Chưa có khen thưởng nào"
              />
            </Panel>
          </div>
        )}
      </Panel>

      <SelfReviewModal review={review} onClose={() => setReview(null)} />
    </Stack>
  )
}

function SelfReviewModal({
  review,
  onClose,
}: {
  review: import('@/api/hr').PerformanceReview | null
  onClose: () => void
}) {
  const toast = useToast()
  const save = useSaveSelfReview()
  const [text, setText] = useState('')
  useEffect(() => {
    if (review) setText(review.selfAssessment)
  }, [review])

  return (
    <Modal
      open={!!review}
      onClose={onClose}
      title={review ? `Tự đánh giá kỳ ${review.period}` : ''}
      size="sm"
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={save.isPending}>
            Huỷ
          </Button>
          <Button
            size="sm"
            variant="primary"
            loading={save.isPending}
            onClick={async () => {
              if (!review) return
              try {
                await save.mutateAsync({ id: review.id, text: text.trim() })
                toast.success('Đã lưu tự đánh giá')
                onClose()
              } catch (error) {
                toast.error('Không lưu được', errorMessage(error))
              }
            }}
          >
            Lưu
          </Button>
        </>
      }
    >
      <Field label="Tự đánh giá">
        <Textarea value={text} onChange={(e) => setText(e.target.value)} rows={6} data-autofocus="" />
      </Field>
    </Modal>
  )
}

/* ============================================================================
   Tài khoản ngân hàng
   ========================================================================== */

export function BankAccountsPage() {
  const toast = useToast()
  const accounts = useBankAccounts()
  const banks = useBanks()
  const setDefault = useSetDefaultBankAccount()
  const remove = useDeleteBankAccount()
  const [editing, setEditing] = useState<BankAccount | null | 'new'>(null)
  const [deleting, setDeleting] = useState<BankAccount | null>(null)

  const bankName = (code: string) => banks.data?.find((b) => b.code === code)?.shortName ?? code

  return (
    <>
      <ModuleScreen
        actions={
          <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setEditing('new')}>
            Thêm tài khoản
          </Button>
        }
        columns={[
          { key: 'bank', priority: 1, header: 'Ngân hàng', cell: (row) => <span className="font-medium">{bankName(row.bank)}</span>, sortValue: (r) => r.bank },
          { key: 'number', priority: 1, header: 'Số tài khoản', cell: (row) => <span className="tnum">{row.accountNumber}</span> },
          { key: 'holder', priority: 2, header: 'Chủ tài khoản', cell: (row) => row.accountHolder },
          { key: 'branch', priority: 3, header: 'Chi nhánh', cell: (row) => row.branch },
          { key: 'note', priority: 3, header: 'Ghi chú', cell: (row) => row.note, truncate: true, hidden: true },
          {
            key: 'default', priority: 1,
            header: 'Mặc định',
            cell: (row) =>
              row.isDefault ? (
                <StatusBadge tone="ok">
                  <Star className="mr-1 size-3" strokeWidth={2} /> Nhận lương
                </StatusBadge>
              ) : null,
          },
          {
            key: 'actions', priority: 1,
            header: '',
            align: 'right',
            locked: true,
            cell: (row) => (
              <span className="row-actions inline-flex gap-1">
                {!row.isDefault && (
                  <Button
                    size="sm"
                    variant="ghost"
                    onClick={async (e) => {
                      e.stopPropagation()
                      try {
                        await setDefault.mutateAsync(row.id)
                        toast.success('Đã đặt làm tài khoản nhận lương')
                      } catch (error) {
                        toast.error('Không đặt được', errorMessage(error))
                      }
                    }}
                  >
                    Đặt mặc định
                  </Button>
                )}
                <Button size="sm" variant="ghost" onClick={(e) => { e.stopPropagation(); setEditing(row) }}>
                  Sửa
                </Button>
                <Button size="sm" variant="ghost" className="text-danger" onClick={(e) => { e.stopPropagation(); setDeleting(row) }}>
                  Xoá
                </Button>
              </span>
            ),
          },
        ]}
        rows={accounts.data ?? []}
        loading={accounts.isLoading}
        error={accounts.error}
        onRefresh={() => accounts.refetch()}
        emptyTitle="Chưa có tài khoản ngân hàng nào"
        emptyDescription="Tài khoản mặc định là nơi công ty chuyển lương."
        emptyAction={
          <Button size="sm" onClick={() => setEditing('new')}>
            Thêm tài khoản đầu tiên
          </Button>
        }
      />

      <BankAccountModal account={editing} onClose={() => setEditing(null)} banks={banks.data ?? []} />

      <ConfirmDialog
        open={!!deleting}
        onClose={() => setDeleting(null)}
        title="Xoá tài khoản ngân hàng"
        message={deleting ? `${bankName(deleting.bank)} · ${deleting.accountNumber}` : undefined}
        confirmLabel="Xoá"
        tone="danger"
        busy={remove.isPending}
        onConfirm={async () => {
          if (!deleting) return
          try {
            await remove.mutateAsync(deleting.id)
            toast.success('Đã xoá tài khoản')
            setDeleting(null)
          } catch (error) {
            toast.error('Không xoá được', errorMessage(error))
          }
        }}
      />
    </>
  )
}

function BankAccountModal({
  account,
  onClose,
  banks,
}: {
  account: BankAccount | null | 'new'
  onClose: () => void
  banks: Array<{ code: string; name: string; shortName: string }>
}) {
  const toast = useToast()
  const save = useSaveBankAccount()
  const open = account !== null
  const editing = account && account !== 'new' ? account : null
  const [form, setForm] = useState({ bank: '', accountNumber: '', accountHolder: '', branch: '', isDefault: false, note: '' })
  const [touched, setTouched] = useState(false)

  useEffect(() => {
    if (open) {
      setForm({
        bank: editing?.bank ?? banks[0]?.code ?? 'vietcombank',
        accountNumber: editing?.accountNumber ?? '',
        accountHolder: editing?.accountHolder ?? '',
        branch: editing?.branch ?? '',
        isDefault: editing?.isDefault ?? false,
        note: editing?.note ?? '',
      })
      setTouched(false)
    }
  }, [open, editing, banks])

  const submit = async () => {
    setTouched(true)
    if (!form.accountNumber.trim()) return
    try {
      await save.mutateAsync({ id: editing?.id, body: { ...form, accountNumber: form.accountNumber.trim() } })
      toast.success(editing ? 'Đã cập nhật tài khoản' : 'Đã thêm tài khoản')
      onClose()
    } catch (error) {
      toast.error('Không lưu được', errorMessage(error))
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? 'Sửa tài khoản ngân hàng' : 'Thêm tài khoản ngân hàng'}
      size="sm"
      dismissible={false}
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={save.isPending}>
            Huỷ
          </Button>
          <Button size="sm" variant="primary" loading={save.isPending} onClick={() => void submit()}>
            Lưu
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        <Field label="Ngân hàng" required>
          <Select value={form.bank} onChange={(e) => setForm({ ...form, bank: e.target.value })}>
            {banks.map((b) => (
              <option key={b.code} value={b.code}>
                {b.shortName}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Số tài khoản" required error={touched && !form.accountNumber.trim() ? 'Nhập số tài khoản' : null}>
          <Input
            value={form.accountNumber}
            onChange={(e) => setForm({ ...form, accountNumber: e.target.value })}
            inputMode="numeric"
            className="tnum"
            data-autofocus=""
          />
        </Field>
        <Field label="Chủ tài khoản" hint="Để trống thì lấy theo tên trong hồ sơ">
          <Input value={form.accountHolder} onChange={(e) => setForm({ ...form, accountHolder: e.target.value })} />
        </Field>
        <Field label="Chi nhánh">
          <Input value={form.branch} onChange={(e) => setForm({ ...form, branch: e.target.value })} />
        </Field>
        <Field label="Ghi chú">
          <Input value={form.note} onChange={(e) => setForm({ ...form, note: e.target.value })} />
        </Field>
        <label className="inline-flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            className="checkbox"
            checked={form.isDefault}
            onChange={(e) => setForm({ ...form, isDefault: e.target.checked })}
          />
          Dùng tài khoản này để nhận lương
        </label>
      </div>
    </Modal>
  )
}
