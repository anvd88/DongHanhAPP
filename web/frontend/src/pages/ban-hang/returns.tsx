import { useMemo, useState } from 'react'
import { Plus } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { PERM } from '@/lib/permissions'
import { date, dateTime, qty, vnd } from '@/lib/format'
import { matches } from '@/lib/text'
import { useCustomers } from '@/api/sales'
import { useCancelReturn, useReturn, useReturns, type GoodsReturn } from '@/api/returns'
import {
  Button,
  Combobox,
  ConfirmDialog,
  DataTable,
  DateRangePicker,
  Drawer,
  Figure,
  FigureStrip,
  InlineAlert,
  KeyValue,
  Money,
  Panel,
  SearchInput,
  StatusBadge,
  useToast,
  type Column,
  type DateRange,
} from '@/ui'
import { ModuleScreen, errorMessage } from '../_shared'
import { ReturnComposer } from './return-composer'

/**
 * Hàng trả về.
 *
 * Khách không nhận hoặc trả lại một phần. Máy truy đơn nguồn để lấy đúng đơn giá đã bán, và tổng
 * đã trả không bao giờ vượt số đã bán. Phiếu nguồn chưa chốt về kho thì hạ thẳng số lượng trên
 * phiếu đó; phiếu đã chốt thì sinh phiếu trả hàng riêng.
 */
export function ReturnsPage() {
  const auth = useAuth()
  const toast = useToast()
  const [range, setRange] = useState<DateRange>({ from: '', to: '' })
  const [search, setSearch] = useState('')
  const [customerId, setCustomerId] = useState('')
  const [openId, setOpenId] = useState<string | null>(null)
  const [composing, setComposing] = useState(false)
  const [cancelling, setCancelling] = useState<GoodsReturn | null>(null)

  const returns = useReturns({
    customerId: customerId || undefined,
    from: range.from || undefined,
    to: range.to || undefined,
  })
  const customers = useCustomers()
  const cancel = useCancelReturn()

  const all = returns.data?.items ?? []
  const rows = useMemo(
    () => all.filter((r) => !search || matches(`${r.voucherNo} ${r.customerName} ${r.content} ${r.note}`, search)),
    [all, search],
  )
  const live = rows.filter((r) => !r.cancelledAt)

  const columns: Column<GoodsReturn>[] = [
    {
      key: 'voucherNo',
      priority: 1,
      header: 'Số phiếu trả',
      width: '8rem',
      cell: (row) => <span className="font-medium tnum">{row.voucherNo}</span>,
      sortValue: (r) => r.voucherNo,
      total: 'Tổng cộng',
    },
    { key: 'createdAt', priority: 1, header: 'Ngày', width: '6.5rem', cell: (row) => date(row.docDate), sortValue: (r) => r.docDate },
    { key: 'customer', priority: 1, header: 'Khách hàng', cell: (row) => row.customerName, sortValue: (r) => r.customerName, truncate: true },
    { key: 'content', priority: 2, header: 'Lý do trả', cell: (row) => row.content, truncate: true },
    { key: 'note', priority: 3, header: 'Ghi chú', cell: (row) => row.note, truncate: true, hidden: true },
    {
      key: 'amount',
      priority: 1,
      header: 'Giá trị trả',
      align: 'right',
      cell: (row) => <Money value={row.total} muted={!!row.cancelledAt} />,
      sortValue: (r) => r.total,
      total: <Money value={live.reduce((s, r) => s + r.total, 0)} zero="zero" />,
    },
    {
      key: 'status',
      priority: 1,
      header: 'Trạng thái',
      width: '8rem',
      cell: (row) => (row.cancelledAt ? <StatusBadge tone="danger">Đã huỷ</StatusBadge> : <StatusBadge tone="ok">Có hiệu lực</StatusBadge>),
      sortValue: (r) => (r.cancelledAt ? 1 : 0),
    },
  ]

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label="Phiếu trả trong bộ lọc" value={returns.data ? live.length : '…'} />
            <Figure label="Giá trị hàng trả" value={returns.data ? vnd(live.reduce((s, r) => s + r.total, 0)) : '…'} tone="warn" />
            <Figure label="Phiếu đã huỷ" value={returns.data ? rows.length - live.length : '…'} />
          </FigureStrip>
        }
        actions={
          auth.can(PERM.vouchersCreate) && (
            <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setComposing(true)}>
              Lập phiếu trả hàng
            </Button>
          )
        }
        filters={
          <>
            <SearchInput
              size="sm"
              className="w-64"
              placeholder="Số phiếu, khách hàng"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onClear={() => setSearch('')}
            />
            <DateRangePicker value={range} onChange={setRange} size="sm" />
            <div className="w-52">
              <Combobox
                size="sm"
                value={customerId}
                onChange={setCustomerId}
                clearable
                placeholder="Mọi khách hàng"
                options={(customers.data ?? []).map((c) => ({ value: c.id, label: c.name, description: c.phone }))}
              />
            </div>
          </>
        }
        columns={columns}
        rows={rows}
        loading={returns.isLoading}
        error={returns.error}
        onRefresh={() => returns.refetch()}
        onRowClick={(row) => setOpenId(row.id)}
        activeKey={openId}
        defaultSort={{ key: 'createdAt', dir: 'desc' }}
        emptyTitle="Chưa có phiếu trả hàng nào"
      />

      <ReturnDrawer returnId={openId} onClose={() => setOpenId(null)} onCancel={(row) => setCancelling(row)} />
      {composing && <ReturnComposer onClose={() => setComposing(false)} />}

      <ConfirmDialog
        open={!!cancelling}
        onClose={() => setCancelling(null)}
        title={`Huỷ phiếu trả ${cancelling?.voucherNo ?? ''}`}
        message="Phiếu ở lại sổ với dấu đã huỷ, và số đã trả của dòng nguồn được nhả ra."
        confirmLabel="Huỷ phiếu"
        tone="danger"
        requireReason
        reasonLabel="Lý do huỷ"
        busy={cancel.isPending}
        onConfirm={async (reason) => {
          if (!cancelling) return
          try {
            await cancel.mutateAsync({ id: cancelling.id, reason })
            toast.success('Đã huỷ phiếu trả hàng')
            setCancelling(null)
            setOpenId(null)
          } catch (e) {
            toast.error('Không huỷ được phiếu', errorMessage(e))
          }
        }}
      />
    </>
  )
}

function ReturnDrawer({
  returnId,
  onClose,
  onCancel,
}: {
  returnId: string | null
  onClose: () => void
  onCancel: (row: GoodsReturn) => void
}) {
  const auth = useAuth()
  const detail = useReturn(returnId)
  const list = useReturns()
  const head = detail.data?.document
  const listRow = list.data?.items.find((r) => r.id === returnId)
  const total = detail.data?.lines.reduce((s, l) => s + l.amount, 0) ?? 0

  return (
    <Drawer
      open={!!returnId}
      onClose={onClose}
      width="lg"
      title={head ? `Phiếu trả ${head.voucherNo}` : 'Phiếu trả hàng'}
      meta={
        head && (
          <>
            <span>{date(head.docDate)}</span>
            <span>{head.customerName}</span>
            {head.cancelledAt ? <StatusBadge tone="danger">Đã huỷ</StatusBadge> : <StatusBadge tone="ok">Có hiệu lực</StatusBadge>}
          </>
        )
      }
      actions={
        head && !head.cancelledAt && auth.can(PERM.vouchersCancel) && listRow ? (
          <Button size="sm" variant="danger" onClick={() => onCancel(listRow)}>
            Huỷ phiếu
          </Button>
        ) : null
      }
    >
      <div className="flex flex-col gap-3 p-3">
        {head?.cancelledAt && (
          <InlineAlert tone="danger" title={`Đã huỷ lúc ${dateTime(head.cancelledAt)}`}>
            {head.cancelReason || 'Không ghi lý do.'}
          </InlineAlert>
        )}
        <Panel title="Thông tin phiếu" padded>
          <KeyValue
            rows={[
              ['Khách hàng', head?.customerName],
              ['Ngày trả', head ? date(head.docDate) : null],
              ['Lý do trả', head?.content || null],
              ['Ghi chú', head?.note || null],
              ['Lập lúc', head ? dateTime(head.createdAt) : null],
            ]}
          />
        </Panel>
        <Panel title="Dòng hàng trả" meta={detail.data ? `${detail.data.lines.length} dòng` : undefined}>
          <DataTable
            columns={[
              { key: 'source', priority: 1, header: 'Đơn nguồn', width: '9rem', cell: (row) => (
                <span className="flex flex-col">
                  <span className="tnum">{row.sourceVoucherNo || '—'}</span>
                  {row.sourceDate && <span className="text-xs text-ink-3">{date(row.sourceDate)}</span>}
                </span>
              ) },
              { key: 'name', priority: 1, header: 'Tên hàng', cell: (row) => row.content, total: 'Tổng cộng' },
              { key: 'spec', priority: 2, header: 'Quy cách', cell: (row) => row.spec },
              { key: 'qty', priority: 1, header: 'Số lượng trả', align: 'right', cell: (row) => qty(row.quantity) },
              { key: 'price', priority: 2, header: 'Đơn giá đã bán', align: 'right', cell: (row) => <Money value={row.unitPrice} /> },
              { key: 'amount', priority: 1, header: 'Giá trị', align: 'right', cell: (row) => <Money value={row.amount} />, total: <Money value={total} zero="zero" /> },
            ]}
            rows={detail.data?.lines ?? []}
            getKey={(row) => row.lineNo}
            loading={detail.isLoading}
            density="compact"
          />
        </Panel>
      </div>
    </Drawer>
  )
}
