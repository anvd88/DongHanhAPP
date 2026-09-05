import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { date, dateTime } from '@/lib/format'
import { matches } from '@/lib/text'
import { documentStatus, useSalesDocuments } from '@/api/sales'
import { Money, SearchInput, StatusBadge } from '@/ui'
import { ModuleScreen } from '../_shared'

/** Giao hàng, đọc từ sổ phiếu bán hàng. */
export function DeliveryPage() {
  const documents = useSalesDocuments()
  const navigate = useNavigate()
  const [tab, setTab] = useState('open')
  const [search, setSearch] = useState('')
  const rows = (documents.data ?? []).filter((d) => {
    const s = documentStatus(d).id
    if (!d.deliveryTaskStatus && !d.deliveryDriverName) return false
    if (tab === 'open' && !['delivering'].includes(s)) return false
    if (tab === 'submitted' && s !== 'submitted') return false
    if (tab === 'completed' && s !== 'returned') return false
    if (search && !matches(`${d.voucherNo} ${d.customerName} ${d.deliveryDriverName}`, search)) return false
    return true
  })
  return (
    <ModuleScreen
      tabs={[
        { id: 'open', label: 'Đang giao' },
        { id: 'submitted', label: 'Đã nộp phiếu' },
        { id: 'completed', label: 'Đã về kho' },
        { id: 'all', label: 'Tất cả' },
      ]}
      tab={tab}
      onTabChange={setTab}
      filters={
        <SearchInput size="sm" className="w-64" placeholder="Số phiếu, khách hàng, lái xe" value={search} onChange={(e) => setSearch(e.target.value)} onClear={() => setSearch('')} />
      }
      columns={[
        { key: 'voucherNo', priority: 1, header: 'Phiếu', cell: (row) => <span className="font-medium">{row.voucherNo}</span>, sortValue: (r) => r.voucherNo },
        { key: 'customer', priority: 1, header: 'Khách hàng', cell: (row) => row.customerName, truncate: true, sortValue: (r) => r.customerName },
        { key: 'driver', priority: 2, header: 'Lái xe', cell: (row) => row.deliveryDriverName, sortValue: (r) => r.deliveryDriverName },
        { key: 'date', priority: 2, header: 'Ngày phiếu', cell: (row) => date(row.date), sortValue: (r) => r.date },
        { key: 'returned', priority: 3, header: 'Về kho lúc', cell: (row) => (row.deliveryReturnedAt ? dateTime(row.deliveryReturnedAt) : null) },
        { key: 'total', priority: 1, header: 'Tiền hàng', align: 'right', cell: (row) => <Money value={row.total} /> },
        { key: 'status', priority: 1, header: 'Chặng', cell: (row) => <StatusBadge tone={documentStatus(row).tone}>{documentStatus(row).label}</StatusBadge> },
      ]}
      rows={rows}
      loading={documents.isLoading}
      error={documents.error}
      onRefresh={() => documents.refetch()}
      onRowClick={(row) => navigate(`/ban-hang/${row.id}`)}
      emptyTitle="Không có phiếu nào ở chặng này"
      defaultSort={{ key: 'date', dir: 'desc' }}
    />
  )
}
