import { useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { Plus } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { PERM } from '@/lib/permissions'
import { date, monthRange, vnd } from '@/lib/format'
import { matches } from '@/lib/text'
import { useFiscal } from '@/shell/FiscalContext'
import {
  DOC_STATUS_FILTERS,
  documentStatus,
  useCancelSalesDocument,
  useCustomers,
  useSalesDocuments,
  type DocumentListItem,
} from '@/api/sales'
import {
  Button,
  Combobox,
  ConfirmDialog,
  DateRangePicker,
  Figure,
  FigureStrip,
  Money,
  SearchInput,
  StatusBadge,
  useToast,
  type Column,
  type DateRange,
} from '@/ui'
import { ModuleScreen, errorMessage } from '../_shared'
import { SalesDocumentComposer } from './document-composer'

/**
 * Sổ phiếu xuất kho / bán hàng. Vòng đời: nháp → phát hành → giao hàng → về kho; phiếu đã phát
 * hành không xoá được, chỉ huỷ kèm lý do.
 */
export function SalesDocumentsPage() {
  const navigate = useNavigate()
  const auth = useAuth()
  const fiscal = useFiscal()
  const toast = useToast()
  const documents = useSalesDocuments()
  const customers = useCustomers()
  const cancel = useCancelSalesDocument()

  const [status, setStatus] = useState('all')
  const [search, setSearch] = useState('')
  const [range, setRange] = useState<DateRange>({ from: '', to: '' })
  const [customer, setCustomer] = useState('')
  const [composing, setComposing] = useState(false)
  const [cancelIds, setCancelIds] = useState<string[] | null>(null)

  const all = documents.data ?? []
  const rows = useMemo(
    () =>
      all.filter((d) => {
        if (status !== 'all' && documentStatus(d).id !== status) return false
        if (range.from && d.date < range.from) return false
        if (range.to && d.date > range.to) return false
        if (customer && d.customerName !== customer) return false
        if (search && !matches(`${d.voucherNo} ${d.customerName} ${d.content} ${d.createdBy}`, search)) return false
        return true
      }),
    [all, status, range, customer, search],
  )

  const period = monthRange(fiscal.period)
  const inPeriod = all.filter((d) => d.date >= period.from && d.date <= period.to && !d.cancelledAt)
  const revenue = inPeriod.reduce((sum, d) => sum + d.total, 0)
  const counts = {
    draft: all.filter((d) => documentStatus(d).id === 'draft').length,
    delivering: all.filter((d) => ['delivering', 'submitted'].includes(documentStatus(d).id)).length,
    returned: inPeriod.filter((d) => documentStatus(d).id === 'returned').length,
  }
  const filteredTotal = rows.reduce((sum, d) => sum + (d.cancelledAt ? 0 : d.total), 0)

  const columns: Column<DocumentListItem>[] = [
    {
      key: 'voucherNo', priority: 1,
      header: 'Số phiếu',
      width: '8rem',
      cell: (row) => (
        <Link to={`/ban-hang/${row.id}`} className="link font-medium" onClick={(e) => e.stopPropagation()}>
          {row.voucherNo}
        </Link>
      ),
      sortValue: (row) => row.voucherNo,
      total: 'Tổng cộng',
    },
    { key: 'date', priority: 1, header: 'Ngày', width: '6.5rem', cell: (row) => date(row.date), sortValue: (row) => row.date },
    { key: 'customer', priority: 1, header: 'Khách hàng', cell: (row) => row.customerName, sortValue: (row) => row.customerName, truncate: true },
    { key: 'content', priority: 3, header: 'Diễn giải', cell: (row) => row.content, truncate: true },
    { key: 'createdBy', priority: 3, header: 'Người lập', cell: (row) => row.createdBy, sortValue: (row) => row.createdBy, hidden: true },
    { key: 'driver', priority: 2, header: 'Lái xe', cell: (row) => row.deliveryDriverName, sortValue: (row) => row.deliveryDriverName },
    {
      key: 'total', priority: 1,
      header: 'Tổng tiền',
      align: 'right',
      width: '9rem',
      cell: (row) => <Money value={row.total} muted={!!row.cancelledAt} />,
      sortValue: (row) => row.total,
      total: <Money value={filteredTotal} zero="zero" />,
    },
    {
      key: 'status', priority: 1,
      header: 'Trạng thái',
      width: '8rem',
      cell: (row) => {
        const s = documentStatus(row)
        return <StatusBadge tone={s.tone}>{s.label}</StatusBadge>
      },
      sortValue: (row) => documentStatus(row).label,
    },
  ]

  const cancelSelected = async (reason: string) => {
    if (!cancelIds) return
    let ok = 0
    for (const id of cancelIds) {
      try {
        await cancel.mutateAsync({ id, reason })
        ok += 1
      } catch (error) {
        toast.error('Không huỷ được phiếu', errorMessage(error))
      }
    }
    if (ok > 0) toast.success(`Đã huỷ ${ok} phiếu`)
    setCancelIds(null)
  }

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label={`Doanh thu kỳ ${fiscal.period.slice(5)}/${fiscal.year}`} value={documents.data ? vnd(revenue) : '…'} />
            <Figure label="Phiếu nháp" value={documents.data ? counts.draft : '…'} tone={counts.draft ? 'warn' : undefined} />
            <Figure label="Đang giao" value={documents.data ? counts.delivering : '…'} />
            <Figure label="Đã về kho trong kỳ" value={documents.data ? counts.returned : '…'} />
          </FigureStrip>
        }
        tabs={DOC_STATUS_FILTERS.map((f) => ({
          id: f.id,
          label: f.label,
          count: f.id === 'all' ? all.length : all.filter((d) => documentStatus(d).id === f.id).length,
        }))}
        tab={status}
        onTabChange={setStatus}
        filters={
          <>
            <SearchInput
              size="sm"
              className="w-56"
              placeholder="Số phiếu, khách hàng, diễn giải"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onClear={() => setSearch('')}
            />
            <DateRangePicker value={range} onChange={setRange} size="sm" />
            <div className="w-52">
              <Combobox
                size="sm"
                value={customer}
                onChange={setCustomer}
                clearable
                placeholder="Mọi khách hàng"
                options={(customers.data ?? []).map((c) => ({ value: c.name, label: c.name, description: c.phone }))}
              />
            </div>
          </>
        }
        actions={
          auth.can(PERM.vouchersCreate) && (
            <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setComposing(true)}>
              Lập phiếu
            </Button>
          )
        }
        columns={columns}
        rows={rows}
        loading={documents.isLoading}
        error={documents.error}
        onRefresh={() => documents.refetch()}
        onRowClick={(row) => navigate(`/ban-hang/${row.id}`)}
        defaultSort={{ key: 'date', dir: 'desc' }}
        selectable={auth.can(PERM.vouchersCancel)}
        bulkActions={(selected) => (
          <Button
            size="sm"
            variant="danger"
            onClick={() =>
              setCancelIds(
                rows.filter((r) => selected.has(r.id) && !r.cancelledAt).map((r) => r.id),
              )
            }
          >
            Huỷ phiếu
          </Button>
        )}
        emptyTitle="Không có phiếu nào trong bộ lọc này"
        emptyAction={
          auth.can(PERM.vouchersCreate) && (
            <Button size="sm" onClick={() => setComposing(true)}>
              Lập phiếu đầu tiên
            </Button>
          )
        }
      />

      {composing && (
        <SalesDocumentComposer
          onClose={() => setComposing(false)}
          onSaved={(id) => {
            setComposing(false)
            navigate(`/ban-hang/${id}`)
          }}
        />
      )}

      <ConfirmDialog
        open={cancelIds !== null}
        onClose={() => setCancelIds(null)}
        title={`Huỷ ${cancelIds?.length ?? 0} phiếu bán hàng`}
        message="Phiếu đã huỷ vẫn nằm trong sổ với dấu đã huỷ và không cộng vào doanh thu, công nợ."
        confirmLabel="Huỷ phiếu"
        tone="danger"
        requireReason
        reasonLabel="Lý do huỷ"
        busy={cancel.isPending}
        onConfirm={(reason) => void cancelSelected(reason)}
      />
    </>
  )
}
