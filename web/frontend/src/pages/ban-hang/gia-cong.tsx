import { useState } from 'react'
import { Plus } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { PERM } from '@/lib/permissions'
import { date, qty, vnd } from '@/lib/format'
import {
  useDeleteGiaCong,
  useGiaCong,
  useGiaCongReport,
  useGiaCongVouchers,
  type GiaCongVoucher,
} from '@/api/returns'
import {
  Button,
  ConfirmDialog,
  DataTable,
  DateRangePicker,
  Drawer,
  Figure,
  FigureStrip,
  KeyValue,
  Money,
  Panel,
  SearchInput,
  Segmented,
  Select,
  Stack,
  StatusBadge,
  useToast,
  type Column,
  type DateRange,
} from '@/ui'
import { ModuleScreen, errorMessage } from '../_shared'
import { GiaCongComposer } from './gia-cong-composer'

/**
 * Gia công.
 *
 * Xuất hàng sang xưởng đối tác rồi nhập lại. Tiền gia công chỉ tính trên phiếu NHẬP, nên đơn giá
 * trên phiếu xuất bị máy chủ bỏ qua.
 */
export function GiaCongPage() {
  const auth = useAuth()
  const toast = useToast()
  const [tab, setTab] = useState('list')
  const [filter, setFilter] = useState('')
  const [search, setSearch] = useState('')
  const [partner, setPartner] = useState('')
  const [range, setRange] = useState<DateRange>({ from: '', to: '' })
  const [openId, setOpenId] = useState<number | null>(null)
  const [composer, setComposer] = useState<null | { id?: number }>(null)
  const [removing, setRemoving] = useState<GiaCongVoucher | null>(null)

  const vouchers = useGiaCongVouchers({ filter: filter || undefined, search: search || undefined })
  const report = useGiaCongReport(
    { doiTac: partner || undefined, from: range.from || undefined, to: range.to || undefined },
    tab === 'report',
  )
  const remove = useDeleteGiaCong()

  const rows = vouchers.data ?? []
  const partners = [...new Set(rows.map((r) => r.doiTac).filter(Boolean))]

  const columns: Column<GiaCongVoucher>[] = [
    {
      key: 'voucherNo',
      priority: 1,
      header: 'Số phiếu',
      width: '8rem',
      cell: (row) => <span className="font-medium tnum">{row.maPhieu}</span>,
      sortValue: (r) => r.maPhieu,
      total: 'Tổng cộng',
    },
    { key: 'date', priority: 1, header: 'Ngày', width: '6.5rem', cell: (row) => date(row.ngayLap), sortValue: (r) => r.ngayLap },
    { key: 'partner', priority: 1, header: 'Đối tác', cell: (row) => row.doiTac, sortValue: (r) => r.doiTac, truncate: true },
    {
      key: 'direction',
      priority: 1,
      header: 'Xuất / Nhập',
      width: '9rem',
      cell: (row) => (
        <StatusBadge tone={row.loaiPhieu.includes('Nhập') ? 'ok' : 'brand'}>
          {row.loaiPhieu.includes('Nhập') ? 'Nhập gia công' : 'Xuất gia công'}
        </StatusBadge>
      ),
      sortValue: (r) => r.loaiPhieu,
    },
    { key: 'staff', priority: 3, header: 'Người phụ trách', cell: (row) => row.nhanVienPhuTrach, hidden: true },
    { key: 'due', priority: 2, header: 'Hạn hoàn thành', width: '8rem', cell: (row) => (row.hanHoanThanh ? date(row.hanHoanThanh) : null), sortValue: (r) => r.hanHoanThanh ?? '' },
    {
      key: 'quantity',
      priority: 1,
      header: 'Số lượng',
      align: 'right',
      cell: (row) => qty(row.loaiPhieu.includes('Nhập') ? row.soLuongNhap : row.soLuongXuat),
      sortValue: (r) => (r.loaiPhieu.includes('Nhập') ? r.soLuongNhap : r.soLuongXuat),
    },
    {
      key: 'amount',
      priority: 1,
      header: 'Tiền gia công',
      align: 'right',
      cell: (row) => <Money value={row.tienGiaCongPhaiTra} />,
      sortValue: (r) => r.tienGiaCongPhaiTra,
      total: <Money value={rows.reduce((s, r) => s + r.tienGiaCongPhaiTra, 0)} zero="zero" />,
    },
    {
      key: 'action',
      priority: 1,
      header: '',
      align: 'right',
      locked: true,
      cell: (row) =>
        auth.can(PERM.vouchersCancel) ? (
          <span className="row-actions">
            <Button size="sm" variant="ghost" className="text-danger" onClick={(e) => { e.stopPropagation(); setRemoving(row) }}>
              Xoá
            </Button>
          </span>
        ) : null,
    },
  ]

  if (tab === 'report') {
    return (
      <Stack>
        <FigureStrip>
          <Figure label="Đã xuất đi gia công" value={report.data ? qty(report.data.soLuongXuat) : '…'} />
          <Figure label="Đã nhập về" value={report.data ? qty(report.data.soLuongNhap) : '…'} />
          <Figure label="Còn ở xưởng đối tác" value={report.data ? qty(report.data.soLuongConTaiCongTy) : '…'} tone="warn" />
          <Figure label="Tiền gia công phải trả" value={report.data ? vnd(report.data.tienGiaCongPhaiTra) : '…'} />
        </FigureStrip>

        <Panel
          title="Báo cáo tổng hợp"
          actions={
            <>
              <Segmented
                items={[
                  { id: 'list', label: 'Phiếu gia công' },
                  { id: 'report', label: 'Báo cáo tổng hợp' },
                ]}
                active={tab}
                onChange={setTab}
              />
              <DateRangePicker value={range} onChange={setRange} size="sm" />
              <Select size="sm" className="w-48" value={partner} onChange={(e) => setPartner(e.target.value)}>
                <option value="">Mọi đối tác</option>
                {partners.map((p) => (
                  <option key={p} value={p}>
                    {p}
                  </option>
                ))}
              </Select>
            </>
          }
        >
          <DataTable
            columns={[
              { key: 'partner', priority: 1, header: 'Đối tác', cell: (row) => row.doiTac, total: 'Tổng cộng' },
              { key: 'out', priority: 1, header: 'Đã xuất', align: 'right', cell: (row) => qty(row.soLuongXuat), total: qty(report.data?.soLuongXuat ?? 0) },
              { key: 'in', priority: 1, header: 'Đã nhập', align: 'right', cell: (row) => qty(row.soLuongNhap), total: qty(report.data?.soLuongNhap ?? 0) },
              { key: 'left', priority: 1, header: 'Còn ở xưởng', align: 'right', cell: (row) => qty(row.soLuongConTaiCongTy), total: qty(report.data?.soLuongConTaiCongTy ?? 0) },
              { key: 'fee', priority: 1, header: 'Tiền gia công', align: 'right', cell: (row) => <Money value={row.tienGiaCongPhaiTra} />, total: <Money value={report.data?.tienGiaCongPhaiTra ?? 0} zero="zero" /> },
            ]}
            rows={report.data?.partners ?? []}
            getKey={(row) => row.doiTac}
            loading={report.isLoading}
            error={report.error ? errorMessage(report.error) : undefined}
            emptyTitle="Chưa có số liệu gia công trong kỳ này"
          />
        </Panel>

        <Panel title="Chi tiết theo hàng hoá">
          <DataTable
            columns={[
              { key: 'partner', priority: 2, header: 'Đối tác', cell: (row) => row.doiTac },
              { key: 'name', priority: 1, header: 'Tên hàng', cell: (row) => row.tenHang, truncate: true },
              { key: 'spec', priority: 2, header: 'Quy cách', cell: (row) => row.quyCach },
              { key: 'unit', priority: 3, header: 'Đơn vị', cell: (row) => row.donViTinh, hidden: true },
              { key: 'out', priority: 1, header: 'Đã xuất', align: 'right', cell: (row) => qty(row.soLuongXuat) },
              { key: 'in', priority: 1, header: 'Đã nhập', align: 'right', cell: (row) => qty(row.soLuongNhap) },
              { key: 'left', priority: 1, header: 'Còn ở xưởng', align: 'right', cell: (row) => qty(row.soLuongConTaiCongTy) },
              { key: 'fee', priority: 1, header: 'Tiền gia công', align: 'right', cell: (row) => <Money value={row.tienGiaCongPhaiTra} /> },
            ]}
            rows={report.data?.items ?? []}
            getKey={(row, index) => `${row.doiTac}:${row.tenHang}:${index}`}
            loading={report.isLoading}
            density="compact"
            emptyTitle="Chưa có hàng hoá nào"
          />
        </Panel>
      </Stack>
    )
  }

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label="Phiếu gia công" value={vouchers.data ? rows.length : '…'} />
            <Figure label="Còn ở xưởng đối tác" value={vouchers.data ? qty(rows.reduce((s, r) => s + r.soLuongConTaiCongTy, 0)) : '…'} tone="warn" />
            <Figure label="Tiền gia công phải trả" value={vouchers.data ? vnd(rows.reduce((s, r) => s + r.tienGiaCongPhaiTra, 0)) : '…'} />
          </FigureStrip>
        }
        tabs={[
          { id: 'list', label: 'Phiếu gia công', count: rows.length },
          { id: 'report', label: 'Báo cáo tổng hợp' },
        ]}
        tab={tab}
        onTabChange={setTab}
        actions={
          auth.can(PERM.vouchersCreate) && (
            <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setComposer({})}>
              Lập phiếu gia công
            </Button>
          )
        }
        filters={
          <>
            <SearchInput
              size="sm"
              className="w-64"
              placeholder="Đối tác gia công, số phiếu"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onClear={() => setSearch('')}
            />
            <Select size="sm" className="w-40" value={filter} onChange={(e) => setFilter(e.target.value)}>
              <option value="">Xuất và nhập</option>
              <option value="xuat">Chỉ phiếu xuất</option>
              <option value="nhap">Chỉ phiếu nhập</option>
            </Select>
          </>
        }
        columns={columns}
        rows={rows}
        getKey={(row) => row.id}
        loading={vouchers.isLoading}
        error={vouchers.error}
        onRefresh={() => vouchers.refetch()}
        onRowClick={(row) => setOpenId(row.id)}
        activeKey={openId}
        defaultSort={{ key: 'date', dir: 'desc' }}
        emptyTitle="Chưa có phiếu gia công nào"
      />

      <GiaCongDrawer
        voucherId={openId}
        onClose={() => setOpenId(null)}
        onEdit={(id) => {
          setOpenId(null)
          setComposer({ id })
        }}
      />
      {composer && <GiaCongComposer voucherId={composer.id} onClose={() => setComposer(null)} />}

      <ConfirmDialog
        open={!!removing}
        onClose={() => setRemoving(null)}
        title={`Xoá phiếu ${removing?.maPhieu ?? ''}`}
        message="Phiếu và các dòng hàng của nó biến mất khỏi sổ gia công."
        confirmLabel="Xoá phiếu"
        tone="danger"
        busy={remove.isPending}
        onConfirm={async () => {
          if (!removing) return
          try {
            await remove.mutateAsync(removing.id)
            toast.success('Đã xoá phiếu gia công')
            setRemoving(null)
          } catch (e) {
            toast.error('Không xoá được phiếu', errorMessage(e))
          }
        }}
      />
    </>
  )
}

function GiaCongDrawer({
  voucherId,
  onClose,
  onEdit,
}: {
  voucherId: number | null
  onClose: () => void
  onEdit: (id: number) => void
}) {
  const auth = useAuth()
  const detail = useGiaCong(voucherId)
  const head = detail.data
  const isNhap = !!head?.loaiPhieu.includes('Nhập')
  const total = head?.lines.reduce((s, l) => s + l.thanhTien, 0) ?? 0

  return (
    <Drawer
      open={voucherId !== null}
      onClose={onClose}
      width="lg"
      title={head ? `Phiếu ${head.maPhieu}` : 'Phiếu gia công'}
      meta={
        head && (
          <>
            <span>{date(head.ngayLap)}</span>
            <span>{head.doiTac}</span>
            <StatusBadge tone={isNhap ? 'ok' : 'brand'}>{isNhap ? 'Nhập gia công' : 'Xuất gia công'}</StatusBadge>
          </>
        )
      }
      actions={
        head && auth.can(PERM.vouchersUpdate) ? (
          <Button size="sm" onClick={() => onEdit(head.id)}>
            Sửa
          </Button>
        ) : null
      }
    >
      <div className="flex flex-col gap-3 p-3">
        <Panel title="Thông tin phiếu" padded>
          <KeyValue
            rows={[
              ['Đối tác gia công', head?.doiTac],
              ['Người phụ trách', head?.nhanVienPhuTrach || null],
              ['Ngày lập', head ? date(head.ngayLap) : null],
              ['Hạn hoàn thành', head?.hanHoanThanh ? date(head.hanHoanThanh) : null],
              ['Ghi chú', head?.ghiChu || null],
            ]}
          />
        </Panel>
        <Panel title="Dòng hàng" meta={head ? `${head.lines.length} dòng` : undefined}>
          <DataTable
            columns={[
              { key: 'code', priority: 2, header: 'Mã hàng', cell: (row) => row.maHang },
              { key: 'name', priority: 1, header: 'Tên hàng', cell: (row) => row.tenHang, total: 'Tổng cộng' },
              { key: 'spec', priority: 2, header: 'Quy cách', cell: (row) => row.quyCach },
              { key: 'unit', priority: 3, header: 'Đơn vị', cell: (row) => row.donViTinh, hidden: true },
              { key: 'qty', priority: 1, header: 'Số lượng', align: 'right', cell: (row) => qty(row.soLuong) },
              { key: 'price', priority: 2, header: 'Đơn giá gia công', align: 'right', cell: (row) => <Money value={row.donGiaGiaCong} /> },
              { key: 'amount', priority: 1, header: 'Thành tiền', align: 'right', cell: (row) => <Money value={row.thanhTien} />, total: <Money value={total} zero="zero" /> },
            ]}
            rows={head?.lines ?? []}
            getKey={(row) => row.id}
            loading={detail.isLoading}
            density="compact"
          />
        </Panel>
      </div>
    </Drawer>
  )
}
