import { useEffect, useMemo, useState } from 'react'
import { Trash2 } from 'lucide-react'
import { todayISO, vnd } from '@/lib/format'
import { useGiaCong, useSaveGiaCong } from '@/api/returns'
import { useProducts, type Product } from '@/api/sales'
import { useSuppliers } from '@/api/purchases'
import {
  Button,
  Combobox,
  DataTable,
  DatePicker,
  DocumentSummary,
  Field,
  FormGrid,
  IconButton,
  InlineAlert,
  Input,
  Modal,
  Money,
  NumberInput,
  Panel,
  Select,
  useToast,
  type Column,
} from '@/ui'
import { errorMessage } from '../_shared'

const GIA_CONG_TYPES = ['Xuất gia công', 'Nhập gia công']

interface GiaCongDraftLine {
  key: number
  /** Mã hàng trong danh mục, để phiếu gia công ghép được với phiếu bán và phiếu nhập mua. */
  productId: string | null
  maHang: string
  tenHang: string
  quyCach: string
  donViTinh: string
  soLuong: number | null
  donGiaGiaCong: number | null
  ghiChu: string
}

const emptyGiaCongLine = (key: number): GiaCongDraftLine => ({
  key,
  productId: null,
  maHang: '',
  tenHang: '',
  quyCach: '',
  donViTinh: '',
  soLuong: null,
  donGiaGiaCong: null,
  ghiChu: '',
})

export function GiaCongComposer({ voucherId, onClose }: { voucherId?: number; onClose: () => void }) {
  const toast = useToast()
  const detail = useGiaCong(voucherId ?? null)
  const save = useSaveGiaCong()

  const [loaded, setLoaded] = useState(!voucherId)
  const [loaiPhieu, setLoaiPhieu] = useState(GIA_CONG_TYPES[0])
  const products = useProducts()
  const suppliers = useSuppliers()
  // Xưởng gia công cũng là một nhà cung cấp: chọn từ danh mục thì phiếu mang theo khoá thật, gõ tay
  // thì máy chủ tự dựng hồ sơ — giống hệt phiếu nhập mua.
  const productOptions = useMemo(
    () =>
      (products.data?.items ?? []).map((p) => ({
        value: p.id,
        label: p.spec ? `${p.name} · ${p.spec}` : p.name,
        keywords: p.code,
        data: p,
      })),
    [products.data],
  )
  const [doiTac, setDoiTac] = useState('')
  const [nhanVien, setNhanVien] = useState('')
  const [ngayLap, setNgayLap] = useState(todayISO())
  const [hanHoanThanh, setHanHoanThanh] = useState('')
  const [ghiChu, setGhiChu] = useState('')
  const [lines, setLines] = useState<GiaCongDraftLine[]>([emptyGiaCongLine(1), emptyGiaCongLine(2), emptyGiaCongLine(3)])
  const [touched, setTouched] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (loaded || !detail.data) return
    const d = detail.data
    setLoaiPhieu(d.loaiPhieu.includes('Nhập') ? GIA_CONG_TYPES[1] : GIA_CONG_TYPES[0])
    setDoiTac(d.doiTac)
    setNhanVien(d.nhanVienPhuTrach)
    setNgayLap(d.ngayLap)
    setHanHoanThanh(d.hanHoanThanh ?? '')
    setGhiChu(d.ghiChu)
    setLines(
      d.lines.length
        ? d.lines.map((l, i) => ({
            key: i + 1,
            productId: l.productId ?? null,
            maHang: l.maHang,
            tenHang: l.tenHang,
            quyCach: l.quyCach,
            donViTinh: l.donViTinh,
            soLuong: l.soLuong,
            donGiaGiaCong: l.donGiaGiaCong,
            ghiChu: l.ghiChu,
          }))
        : [emptyGiaCongLine(1)],
    )
    setLoaded(true)
  }, [detail.data, loaded])

  const isNhap = loaiPhieu.includes('Nhập')
  const patch = (key: number, change: Partial<GiaCongDraftLine>) =>
    setLines((list) => list.map((l) => (l.key === key ? { ...l, ...change } : l)))
  const filled = lines.filter((l) => l.tenHang.trim())
  const total = filled.reduce((s, l) => s + (l.soLuong ?? 0) * (isNhap ? l.donGiaGiaCong ?? 0 : 0), 0)
  const problems = {
    doiTac: !doiTac.trim() ? 'Nhập tên đối tác gia công' : null,
    lines: filled.length === 0 ? 'Phiếu cần ít nhất một dòng hàng' : null,
  }
  const valid = !problems.doiTac && !problems.lines

  return (
    <Modal
      open
      onClose={onClose}
      dismissible={false}
      size="lg"
      title={voucherId ? 'Sửa phiếu gia công' : 'Lập phiếu gia công'}
      description="Tiền gia công chỉ tính trên phiếu nhập về, nên đơn giá của phiếu xuất được bỏ qua."
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
              setTouched(true)
              if (!valid) return
              setError(null)
              try {
                await save.mutateAsync({
                  id: voucherId,
                  body: {
                    loaiPhieu,
                    doiTac: doiTac.trim(),
                    nhanVienPhuTrach: nhanVien.trim(),
                    ngayLap,
                    hanHoanThanh: hanHoanThanh || null,
                    ghiChu: ghiChu.trim(),
                    lines: filled.map((l, index) => ({
                      id: index + 1,
                      loaiDong: loaiPhieu,
                      productId: l.productId,
                      maHang: l.maHang.trim(),
                      tenHang: l.tenHang.trim(),
                      quyCach: l.quyCach.trim(),
                      donViTinh: l.donViTinh.trim(),
                      soLuong: l.soLuong ?? 0,
                      donGiaGiaCong: isNhap ? l.donGiaGiaCong ?? 0 : 0,
                      ghiChu: l.ghiChu.trim(),
                    })),
                  },
                })
                toast.success(voucherId ? 'Đã cập nhật phiếu gia công' : 'Đã lập phiếu gia công')
                onClose()
              } catch (e) {
                setError(errorMessage(e, 'Không lưu được phiếu gia công.'))
              }
            }}
          >
            {voucherId ? 'Lưu thay đổi' : 'Lập phiếu'}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 p-4">
        {error && <InlineAlert tone="danger">{error}</InlineAlert>}
        <FormGrid cols={3}>
          <Field label="Loại phiếu" required>
            <Select value={loaiPhieu} onChange={(e) => setLoaiPhieu(e.target.value)}>
              {GIA_CONG_TYPES.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </Select>
          </Field>
          <Field label="Đối tác gia công" required error={touched ? problems.doiTac : null}>
            <Combobox
              value={doiTac}
              onChange={setDoiTac}
              allowCustom
              placeholder="Gõ để tìm, tên chưa có sẽ được tạo mới"
              loading={suppliers.isLoading}
              options={(suppliers.data?.items ?? []).map((sup) => ({
                value: sup.name,
                label: sup.name,
                description: sup.aliases.length > 0 ? sup.aliases.join(', ') : sup.phone,
                keywords: sup.aliases.join(' '),
              }))}
            />
          </Field>
          <Field label="Người phụ trách">
            <Input value={nhanVien} onChange={(e) => setNhanVien(e.target.value)} />
          </Field>
          <Field label="Ngày lập" required>
            <DatePicker value={ngayLap} onChange={setNgayLap} />
          </Field>
          <Field label="Hạn hoàn thành">
            <DatePicker value={hanHoanThanh} onChange={setHanHoanThanh} />
          </Field>
          <Field label="Ghi chú">
            <Input value={ghiChu} onChange={(e) => setGhiChu(e.target.value)} />
          </Field>
        </FormGrid>

        <Panel
          title="Dòng hàng"
          meta={touched && problems.lines ? problems.lines : `${filled.length} dòng`}
          actions={
            <Button size="sm" onClick={() => setLines((list) => [...list, emptyGiaCongLine(Date.now())])}>
              Thêm dòng
            </Button>
          }
        >
          <DataTable
            columns={[
              { key: 'code', priority: 2, header: 'Mã hàng', width: '8rem', cell: (row) => <Input size="sm" value={row.maHang} onChange={(e) => patch(row.key, { maHang: e.target.value })} aria-label="Mã hàng" /> },
              {
                key: 'name', priority: 1, header: 'Tên hàng',
                cell: (row) => (
                  <Combobox
                    size="sm"
                    value={row.productId ?? row.tenHang}
                    allowCustom
                    options={productOptions}
                    placeholder="Tên hàng"
                    onChange={(value) => {
                      const known = products.data?.items.find((p) => p.id === value)
                      if (!known) patch(row.key, { tenHang: value, productId: null })
                    }}
                    onSelect={(option) => {
                      const p = option.data as Product
                      patch(row.key, {
                        productId: p.id,
                        maHang: p.code,
                        tenHang: p.name,
                        quyCach: p.spec,
                        donViTinh: row.donViTinh || p.unit,
                      })
                    }}
                  />
                ),
              },
              { key: 'spec', priority: 2, header: 'Quy cách', width: '8rem', cell: (row) => <Input size="sm" value={row.quyCach} onChange={(e) => patch(row.key, { quyCach: e.target.value })} aria-label="Quy cách" /> },
              { key: 'unit', priority: 2, header: 'Đơn vị', width: '6rem', cell: (row) => <Input size="sm" value={row.donViTinh} onChange={(e) => patch(row.key, { donViTinh: e.target.value })} aria-label="Đơn vị tính" /> },
              { key: 'qty', priority: 1, header: 'Số lượng', align: 'right', width: '7rem', cell: (row) => <NumberInput size="sm" decimals={2} value={row.soLuong} onChange={(v) => patch(row.key, { soLuong: v })} aria-label="Số lượng" /> },
              ...(isNhap
                ? ([
                    { key: 'price', priority: 1, header: 'Đơn giá gia công', align: 'right', width: '8rem', cell: (row) => <NumberInput size="sm" value={row.donGiaGiaCong} onChange={(v) => patch(row.key, { donGiaGiaCong: v })} aria-label="Đơn giá gia công" /> },
                    { key: 'amount', priority: 1, header: 'Thành tiền', align: 'right', width: '8rem', cell: (row) => <Money value={(row.soLuong ?? 0) * (row.donGiaGiaCong ?? 0)} /> },
                  ] as Column<GiaCongDraftLine>[])
                : []),
              {
                key: 'remove',
                priority: 1,
                header: '',
                align: 'right',
                width: '3rem',
                locked: true,
                cell: (row) => (
                  <IconButton
                    label="Bỏ dòng"
                    size="sm"
                    onClick={() => setLines((list) => (list.length > 1 ? list.filter((l) => l.key !== row.key) : list))}
                    icon={<Trash2 className="size-3.5" strokeWidth={1.7} />}
                  />
                ),
              },
            ]}
            rows={lines}
            getKey={(row) => row.key}
            density="compact"
          />
        </Panel>

        {isNhap && <DocumentSummary rows={[{ label: 'Tiền gia công phải trả', value: vnd(total), strong: true }]} />}
      </div>
    </Modal>
  )
}
