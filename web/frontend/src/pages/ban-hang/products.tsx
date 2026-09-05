import { useState } from 'react'
import { date, qty } from '@/lib/format'
import { matches } from '@/lib/text'
import { useProductSources, useProducts, type Product } from '@/api/sales'
import { DataTable, Drawer, Money, Panel, SearchInput } from '@/ui'
import { ModuleScreen } from '../_shared'

/** Danh mục hàng hoá. Bấm vào một dòng để xem mặt hàng đó đang còn của những nhà cung cấp nào. */
export function ProductsPage() {
  const [tab, setTab] = useState('active')
  const [search, setSearch] = useState('')
  const [open, setOpen] = useState<Product | null>(null)
  const products = useProducts(tab === 'inactive')
  const rows = (products.data?.items ?? []).filter((p) => {
    if (tab === 'active' && !p.isActive) return false
    if (tab === 'inactive' && p.isActive) return false
    if (search && !matches(`${p.code} ${p.name} ${p.spec}`, search)) return false
    return true
  })
  return (
    <>
      <ModuleScreen
        tabs={[
          { id: 'active', label: 'Đang dùng' },
          { id: 'inactive', label: 'Ngừng dùng' },
        ]}
        tab={tab}
        onTabChange={setTab}
        filters={<SearchInput size="sm" className="w-64" placeholder="Mã hàng, tên hàng, quy cách" value={search} onChange={(e) => setSearch(e.target.value)} onClear={() => setSearch('')} />}
        columns={[
          { key: 'code', priority: 1, header: 'Mã hàng', cell: (row) => <span className="tnum">{row.code}</span>, sortValue: (r) => r.code },
          { key: 'name', priority: 1, header: 'Tên hàng hoá', cell: (row) => <span className="font-medium">{row.name}</span>, sortValue: (r) => r.name },
          { key: 'spec', priority: 2, header: 'Quy cách', cell: (row) => row.spec, sortValue: (r) => r.spec },
          { key: 'unit', priority: 2, header: 'ĐVT', cell: (row) => row.unit },
          { key: 'lastPrice', priority: 1, header: 'Giá bán gần nhất', align: 'right', cell: (row) => <Money value={row.lastPrice} />, sortValue: (r) => r.lastPrice ?? null },
          { key: 'lastCost', priority: 3, header: 'Giá nhập gần nhất', align: 'right', cell: (row) => <Money value={row.lastCost} />, sortValue: (r) => r.lastCost ?? null },
          { key: 'sold', priority: 2, header: 'Đã bán (SL)', align: 'right', cell: (row) => qty(row.soldQuantity), sortValue: (r) => r.soldQuantity },
          { key: 'bought', priority: 3, header: 'Đã nhập (SL)', align: 'right', cell: (row) => qty(row.boughtQuantity), sortValue: (r) => r.boughtQuantity, hidden: true },
          { key: 'times', priority: 3, header: 'Số phiếu', align: 'right', cell: (row) => row.timesUsed, sortValue: (r) => r.timesUsed },
          { key: 'lastSold', priority: 3, header: 'Bán gần nhất', cell: (row) => date(row.lastSoldDate), sortValue: (r) => r.lastSoldDate ?? '' },
        ]}
        rows={rows}
        loading={products.isLoading}
        error={products.error}
        onRefresh={() => products.refetch()}
        onRowClick={(row) => setOpen(row)}
        activeKey={open?.id}
        defaultSort={{ key: 'name', dir: 'asc' }}
        emptyTitle="Chưa có hàng hoá trong danh mục"
        emptyDescription="Hàng hoá được ghi nhận từ các dòng phiếu đã lập."
      />
      <ProductSourcesDrawer product={open} onClose={() => setOpen(null)} />
    </>
  )
}

/**
 * Mặt hàng này đang còn của những nhà cung cấp nào.
 *
 * Cùng một mặt hàng nhập từ nhiều nơi với giá khác nhau, nên tổng tồn không đủ để đi lấy hàng: thủ
 * kho cần biết cuộn nào của ai. Số ở đây trừ theo nguồn đã ghi trên từng dòng phiếu xuất, và cộng
 * ngược khi khách trả hàng.
 */
function ProductSourcesDrawer({ product, onClose }: { product: Product | null; onClose: () => void }) {
  const sources = useProductSources(product?.id)
  const items = sources.data?.items ?? []
  const total = items.reduce((sum, item) => sum + item.remaining, 0)

  return (
    <Drawer
      open={!!product}
      onClose={onClose}
      width="md"
      title={product?.name ?? ''}
      meta={
        product && (
          <>
            {product.code && <span className="tnum">{product.code}</span>}
            {product.spec && <span>{product.spec}</span>}
            {product.unit && <span>ĐVT: {product.unit}</span>}
          </>
        )
      }
    >
      <div className="p-3">
        <Panel>
          <DataTable
            columns={[
              { key: 'supplier', priority: 1, header: 'Nhà cung cấp', cell: (row) => <span className="font-medium">{row.supplierName}</span>, total: 'Tổng cộng' },
              { key: 'bought', priority: 2, header: 'Đã nhập', align: 'right', cell: (row) => qty(row.bought) },
              { key: 'sold', priority: 2, header: 'Đã bán', align: 'right', cell: (row) => qty(row.sold) },
              {
                key: 'remaining', priority: 1, header: 'Còn lại', align: 'right',
                cell: (row) => <span className="tnum font-semibold">{qty(row.remaining)}</span>,
                total: qty(total),
              },
              { key: 'cost', priority: 2, header: 'Giá nhập gần nhất', align: 'right', cell: (row) => <Money value={row.lastCost} /> },
              { key: 'lastBought', priority: 3, header: 'Nhập gần nhất', cell: (row) => date(row.lastBoughtDate) },
            ]}
            rows={items}
            getKey={(row) => row.supplierId}
            loading={sources.isLoading}
            emptyTitle="Chưa nhập mặt hàng này của nhà cung cấp nào"
            emptyDescription="Số chỉ đếm được khi dòng phiếu nhập và dòng phiếu xuất đều gắn được mã hàng trong danh mục."
            density="compact"
          />
        </Panel>
      </div>
    </Drawer>
  )
}
