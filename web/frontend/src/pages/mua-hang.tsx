import { useEffect, useMemo, useState } from 'react'
import { Plus, Trash2 } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { PERM } from '@/lib/permissions'
import { date, dateTime, monthRange, qty, todayISO, vnd } from '@/lib/format'
import { matches } from '@/lib/text'
import { useFiscal } from '@/shell/FiscalContext'
import { useProducts, type Product } from '@/api/sales'
import {
  purchaseStatus,
  useCancelPurchase,
  usePurchase,
  usePurchases,
  useSavePurchase,
  useAddSupplierAlias,
  useDeleteSupplierAlias,
  useSaveSupplier,
  useSupplierAliases,
  useSupplierStock,
  useSuppliers,
  type Purchase,
  type PurchaseDetail,
  type SavePurchaseRequest,
  type Supplier,
} from '@/api/purchases'
import {
  Button,
  Checkbox,
  Combobox,
  ConfirmDialog,
  DataTable,
  DatePicker,
  DateRangePicker,
  DocumentForm,
  DocumentLines,
  DocumentSummary,
  Drawer,
  Field,
  IconButton,
  Figure,
  FigureStrip,
  FormGrid,
  InlineAlert,
  Input,
  KeyValue,
  Modal,
  Money,
  NumberInput,
  Panel,
  SearchInput,
  StatusBadge,
  Tabs,
  Textarea,
  useToast,
  type Column,
  type DateRange,
} from '@/ui'
import { ModuleScreen, errorMessage } from './_shared'

/* ============================================================================
   Phiếu nhập mua
   ========================================================================== */

export function PurchasesPage() {
  const auth = useAuth()
  const fiscal = useFiscal()
  const toast = useToast()
  const [range, setRange] = useState<DateRange>({ from: '', to: '' })
  const purchases = usePurchases({ from: range.from || undefined, to: range.to || undefined })
  const suppliers = useSuppliers()
  const cancel = useCancelPurchase()

  const [tab, setTab] = useState('all')
  const [search, setSearch] = useState('')
  const [supplier, setSupplier] = useState('')
  const [openId, setOpenId] = useState<string | null>(null)
  const [composer, setComposer] = useState<null | { detail?: PurchaseDetail }>(null)
  const [cancelling, setCancelling] = useState<Purchase | null>(null)

  const all = purchases.data?.items ?? []
  const rows = useMemo(
    () =>
      all.filter((p) => {
        const s = purchaseStatus(p).id
        if (tab !== 'all' && s !== tab) return false
        if (supplier && p.supplierName !== supplier) return false
        if (search && !matches(`${p.voucherNo} ${p.supplierName} ${p.supplierInvoiceNo} ${p.note}`, search)) return false
        return true
      }),
    [all, tab, supplier, search],
  )

  const period = monthRange(fiscal.period)
  const live = all.filter((p) => !p.cancelledAt)
  const inPeriod = live.filter((p) => p.docDate >= period.from && p.docDate <= period.to)
  const totals = rows.reduce(
    (acc, p) => (p.cancelledAt ? acc : { total: acc.total + p.total, paid: acc.paid + p.paidAmount, remaining: acc.remaining + p.remaining }),
    { total: 0, paid: 0, remaining: 0 },
  )

  const columns: Column<Purchase>[] = [
    { key: 'voucherNo', priority: 1, header: 'Số phiếu', width: '7rem', cell: (row) => <span className="font-medium">{row.voucherNo}</span>, sortValue: (r) => r.voucherNo, total: 'Tổng cộng' },
    { key: 'date', priority: 1, header: 'Ngày nhập', width: '6.5rem', cell: (row) => date(row.docDate), sortValue: (r) => r.docDate },
    { key: 'supplier', priority: 1, header: 'Nhà cung cấp', cell: (row) => row.supplierName, sortValue: (r) => r.supplierName, truncate: true },
    { key: 'invoice', priority: 3, header: 'Số HĐ nhà cung cấp', cell: (row) => <span className="tnum">{row.supplierInvoiceNo}</span>, hidden: true },
    { key: 'note', priority: 3, header: 'Ghi chú', cell: (row) => row.note, truncate: true, hidden: true },
    { key: 'createdBy', priority: 3, header: 'Người lập', cell: (row) => row.createdBy, hidden: true },
    { key: 'total', priority: 1, header: 'Tổng tiền', align: 'right', cell: (row) => <Money value={row.total} muted={!!row.cancelledAt} />, sortValue: (r) => r.total, total: <Money value={totals.total} zero="zero" /> },
    { key: 'paid', priority: 2, header: 'Đã trả', align: 'right', cell: (row) => <Money value={row.paidAmount} muted={!!row.cancelledAt} />, sortValue: (r) => r.paidAmount, total: <Money value={totals.paid} zero="zero" /> },
    { key: 'remaining', priority: 1, header: 'Còn nợ', align: 'right', cell: (row) => <Money value={row.cancelledAt ? 0 : row.remaining} strong />, sortValue: (r) => r.remaining, total: <Money value={totals.remaining} zero="zero" /> },
    { key: 'status', priority: 1, header: 'Trạng thái', width: '7.5rem', cell: (row) => <StatusBadge tone={purchaseStatus(row).tone}>{purchaseStatus(row).label}</StatusBadge>, sortValue: (r) => purchaseStatus(r).label },
  ]

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label={`Mua trong kỳ ${fiscal.period.slice(5)}/${fiscal.year}`} value={purchases.data ? vnd(inPeriod.reduce((s, p) => s + p.total, 0)) : '…'} />
            <Figure label="Đã trả trong kỳ" value={purchases.data ? vnd(inPeriod.reduce((s, p) => s + p.paidAmount, 0)) : '…'} />
            <Figure label="Còn phải trả" value={purchases.data ? vnd(live.reduce((s, p) => s + p.remaining, 0)) : '…'} tone="warn" to="/nha-cung-cap" />
            <Figure label="Phiếu chưa trả hết" value={purchases.data ? live.filter((p) => p.remaining > 0).length : '…'} />
          </FigureStrip>
        }
        tabs={[
          { id: 'all', label: 'Tất cả', count: all.length },
          { id: 'unpaid', label: 'Chưa trả', count: all.filter((p) => purchaseStatus(p).id === 'unpaid').length },
          { id: 'partial', label: 'Trả một phần', count: all.filter((p) => purchaseStatus(p).id === 'partial').length },
          { id: 'paid', label: 'Đã trả đủ', count: all.filter((p) => purchaseStatus(p).id === 'paid').length },
          { id: 'cancelled', label: 'Đã huỷ', count: all.filter((p) => purchaseStatus(p).id === 'cancelled').length },
        ]}
        tab={tab}
        onTabChange={setTab}
        filters={
          <>
            <SearchInput size="sm" className="w-56" placeholder="Số phiếu, nhà cung cấp, số hoá đơn" value={search} onChange={(e) => setSearch(e.target.value)} onClear={() => setSearch('')} />
            <DateRangePicker value={range} onChange={setRange} size="sm" />
            <div className="w-52">
              <Combobox
                size="sm"
                value={supplier}
                onChange={setSupplier}
                clearable
                placeholder="Mọi nhà cung cấp"
                options={(suppliers.data?.items ?? []).map((s) => ({ value: s.name, label: s.name, description: s.phone }))}
              />
            </div>
          </>
        }
        actions={
          auth.can(PERM.vouchersCreate) && (
            <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setComposer({})}>
              Lập phiếu nhập
            </Button>
          )
        }
        columns={columns}
        rows={rows}
        loading={purchases.isLoading}
        error={purchases.error}
        onRefresh={() => purchases.refetch()}
        onRowClick={(row) => setOpenId(row.id)}
        activeKey={openId}
        defaultSort={{ key: 'date', dir: 'desc' }}
        emptyTitle="Không có phiếu nhập nào trong bộ lọc này"
      />

      <PurchaseDrawer
        purchaseId={openId}
        onClose={() => setOpenId(null)}
        onEdit={(detail) => {
          setOpenId(null)
          setComposer({ detail })
        }}
        onCancel={(p) => setCancelling(p)}
      />

      {composer && (
        <PurchaseComposer
          initial={composer.detail}
          onClose={() => setComposer(null)}
          onSaved={(id) => {
            setComposer(null)
            setOpenId(id)
          }}
        />
      )}

      <ConfirmDialog
        open={!!cancelling}
        onClose={() => setCancelling(null)}
        title={`Huỷ phiếu nhập ${cancelling?.voucherNo ?? ''}`}
        message="Phiếu ở lại sổ với dấu đã huỷ để tháng sau còn đối chiếu được."
        confirmLabel="Huỷ phiếu"
        tone="danger"
        requireReason
        reasonLabel="Lý do huỷ"
        busy={cancel.isPending}
        onConfirm={async (reason) => {
          if (!cancelling) return
          try {
            await cancel.mutateAsync({ id: cancelling.id, reason })
            toast.success('Đã huỷ phiếu nhập')
            setCancelling(null)
            setOpenId(null)
          } catch (error) {
            toast.error('Không huỷ được phiếu', errorMessage(error))
          }
        }}
      />
    </>
  )
}

function PurchaseDrawer({
  purchaseId,
  onClose,
  onEdit,
  onCancel,
}: {
  purchaseId: string | null
  onClose: () => void
  onEdit: (detail: PurchaseDetail) => void
  onCancel: (purchase: Purchase) => void
}) {
  const auth = useAuth()
  const detail = usePurchase(purchaseId ?? undefined)
  const list = usePurchases()
  const head = detail.data?.purchase
  const listRow = list.data?.items.find((p) => p.id === purchaseId)
  const total = detail.data?.lines?.reduce((s, l) => s + l.quantity * l.unitPrice, 0) ?? 0
  const status = head ? purchaseStatus({ cancelledAt: head.cancelledAt, total, paidAmount: head.paidAmount }) : null

  return (
    <Drawer
      open={!!purchaseId}
      onClose={onClose}
      width="lg"
      title={head ? `Phiếu nhập ${head.voucherNo}` : 'Phiếu nhập'}
      meta={
        head && (
          <>
            <span>{date(head.docDate)}</span>
            <span>{head.supplierName}</span>
            {status && <StatusBadge tone={status.tone}>{status.label}</StatusBadge>}
          </>
        )
      }
      actions={
        head &&
        !head.cancelledAt && (
          <>
            {auth.can(PERM.vouchersUpdate) && detail.data && (
              <Button size="sm" onClick={() => onEdit(detail.data!)}>
                Sửa
              </Button>
            )}
            {auth.can(PERM.vouchersCancel) && listRow && (
              <Button size="sm" variant="danger" onClick={() => onCancel(listRow)}>
                Huỷ phiếu
              </Button>
            )}
          </>
        )
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
              ['Nhà cung cấp', head?.supplierName],
              ['Số hoá đơn nhà cung cấp', head?.supplierInvoiceNo || null],
              ['Ngày nhập', head ? date(head.docDate) : null],
              ['Ghi chú', head?.note || null],
              ['Người lập', listRow?.createdBy || null],
            ]}
          />
        </Panel>
        <Panel title="Dòng hàng" meta={detail.data ? `${detail.data.lines?.length ?? 0} dòng` : undefined}>
          <DataTable
            columns={[
              { key: 'no', priority: 3, header: '#', width: '2.5rem', align: 'center', cell: (row) => <span className="text-ink-3">{row.lineNo}</span> },
              { key: 'name', priority: 1, header: 'Tên hàng', cell: (row) => row.lineContent, total: 'Tổng cộng' },
              { key: 'spec', priority: 2, header: 'Quy cách', cell: (row) => row.spec },
              { key: 'qty', priority: 1, header: 'Số lượng', align: 'right', cell: (row) => qty(row.quantity) },
              { key: 'price', priority: 2, header: 'Đơn giá', align: 'right', cell: (row) => <Money value={row.unitPrice} /> },
              { key: 'amount', priority: 1, header: 'Thành tiền', align: 'right', cell: (row) => <Money value={row.quantity * row.unitPrice} />, total: <Money value={total} zero="zero" /> },
            ]}
            rows={detail.data?.lines ?? []}
            getKey={(row) => row.lineNo}
            loading={detail.isLoading}
            density="compact"
          />
        </Panel>
        <DocumentSummary
          rows={[
            { label: 'Tổng tiền hàng', value: vnd(total) },
            { label: 'Đã trả nhà cung cấp', value: vnd(head?.paidAmount ?? 0) },
            { label: 'Còn phải trả', value: vnd(total - (head?.paidAmount ?? 0)), strong: true },
          ]}
        />
      </div>
    </Drawer>
  )
}

interface DraftLine {
  id: number
  productId: string | null
  lineContent: string
  spec: string
  quantity: number | null
  unitPrice: number | null
  note: string
}

const emptyLine = (id: number): DraftLine => ({ id, productId: null, lineContent: '', spec: '', quantity: null, unitPrice: null, note: '' })
const lineAmount = (l: DraftLine) => (l.quantity ?? 0) * (l.unitPrice ?? 0)

function PurchaseComposer({
  initial,
  onClose,
  onSaved,
}: {
  initial?: PurchaseDetail
  onClose: () => void
  onSaved: (id: string) => void
}) {
  const toast = useToast()
  const suppliers = useSuppliers()
  const products = useProducts()
  const save = useSavePurchase()

  const [supplierId, setSupplierId] = useState<string | null>(initial?.purchase.supplierId ?? null)
  const [supplierName, setSupplierName] = useState(initial?.purchase.supplierName ?? '')
  const [docDate, setDocDate] = useState(initial?.purchase.docDate ?? todayISO())
  const [voucherNo, setVoucherNo] = useState(initial?.purchase.voucherNo ?? '')
  const [invoiceNo, setInvoiceNo] = useState(initial?.purchase.supplierInvoiceNo ?? '')
  const [paid, setPaid] = useState<number | null>(initial?.purchase.paidAmount ?? null)
  const [note, setNote] = useState(initial?.purchase.note ?? '')
  const [lines, setLines] = useState<DraftLine[]>(() =>
    initial?.lines?.length
      ? initial.lines.map((l) => ({ id: l.lineNo, productId: l.productId, lineContent: l.lineContent, spec: l.spec, quantity: l.quantity, unitPrice: l.unitPrice, note: l.note }))
      : [emptyLine(1), emptyLine(2), emptyLine(3)],
  )
  const [touched, setTouched] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const patch = (id: number, change: Partial<DraftLine>) => setLines((c) => c.map((l) => (l.id === id ? { ...l, ...change } : l)))
  const filled = lines.filter((l) => l.lineContent.trim())
  const total = filled.reduce((s, l) => s + lineAmount(l), 0)
  const problems = {
    supplier: !supplierName.trim() && !supplierId ? 'Chọn hoặc nhập nhà cung cấp' : null,
    lines: filled.length === 0 ? 'Phiếu nhập cần ít nhất một dòng hàng' : null,
    paid: (paid ?? 0) > total ? 'Số đã trả không được lớn hơn giá trị phiếu' : null,
  }
  const valid = !problems.supplier && !problems.lines && !problems.paid

  // Bí danh đi vào keywords chứ không thành mục riêng: gõ "anh A" vẫn ra "Công ty Đại Phát", và
  // danh sách không bị nhân đôi một nhà cung cấp thành nhiều dòng.
  const supplierOptions = useMemo(
    () =>
      (suppliers.data?.items ?? []).map((s) => ({
        value: s.name,
        label: s.name,
        description: s.aliases.length > 0 ? s.aliases.join(', ') : s.phone,
        keywords: s.aliases.join(' '),
        data: s,
      })),
    [suppliers.data],
  )
  const productOptions = useMemo(
    () =>
      (products.data?.items ?? []).map((p) => ({
        value: p.id,
        label: p.spec ? `${p.name} · ${p.spec}` : p.name,
        description: p.lastCost != null ? vnd(p.lastCost) : undefined,
        keywords: p.code,
        data: p,
      })),
    [products.data],
  )

  const submit = async () => {
    setTouched(true)
    if (!valid) return
    setError(null)
    const body: SavePurchaseRequest = {
      voucherNo: voucherNo.trim() || undefined,
      date: docDate,
      supplierId,
      supplierName: supplierName.trim(),
      supplierInvoiceNo: invoiceNo.trim(),
      note: note.trim(),
      paidAmount: paid ?? 0,
      lines: filled.map((l) => ({
        productId: l.productId,
        lineContent: l.lineContent.trim(),
        spec: l.spec.trim(),
        quantity: l.quantity ?? 0,
        unitPrice: l.unitPrice ?? 0,
        note: l.note.trim(),
      })),
    }
    try {
      const result = await save.mutateAsync({ id: initial?.purchase.id, body })
      toast.success(initial ? 'Đã cập nhật phiếu nhập' : `Đã lưu phiếu nhập ${result.voucherNo}`)
      onSaved(result.id)
    } catch (e) {
      setError(errorMessage(e, 'Không lưu được phiếu nhập.'))
    }
  }

  return (
    <DocumentForm
      title={initial ? 'Sửa phiếu nhập mua' : 'Phiếu nhập mua'}
      code={initial?.purchase.voucherNo}
      error={error}
      busy={save.isPending}
      onClose={onClose}
      onSave={() => void submit()}
      fields={
        <FormGrid cols={3}>
          <Field label="Nhà cung cấp" required error={touched ? problems.supplier : null}>
            <Combobox
              value={supplierName}
              onChange={(value) => {
                setSupplierName(value)
                const known = suppliers.data?.items.find((s) => s.name === value)
                setSupplierId(known?.id ?? null)
              }}
              onSelect={(option) => setSupplierId((option.data as Supplier).id)}
              allowCustom
              autoFocus
              placeholder="Gõ để tìm, tên chưa có sẽ được tạo mới"
              loading={suppliers.isLoading}
              options={supplierOptions}
            />
          </Field>
          <Field label="Ngày nhập" required>
            <DatePicker value={docDate} onChange={setDocDate} clearable={false} />
          </Field>
          <Field label="Số phiếu" hint={initial ? undefined : 'Để trống để hệ thống cấp số'}>
            <Input value={voucherNo} onChange={(e) => setVoucherNo(e.target.value)} disabled={!!initial} />
          </Field>
          <Field label="Số hoá đơn nhà cung cấp">
            <Input value={invoiceNo} onChange={(e) => setInvoiceNo(e.target.value)} className="tnum" />
          </Field>
          <Field label="Đã trả nhà cung cấp" error={touched ? problems.paid : null}>
            <NumberInput value={paid} onChange={setPaid} />
          </Field>
          <Field label="Ghi chú">
            <Input value={note} onChange={(e) => setNote(e.target.value)} />
          </Field>
        </FormGrid>
      }
      lines={
        <DocumentLines
          count={lines.length}
          onAddLine={() => setLines((c) => [...c, emptyLine(Date.now())])}
          onClearLines={() => setLines([emptyLine(1)])}
          head={
            <>
              <th className="w-8 text-center">#</th>
              <th>Tên hàng</th>
              <th className="w-40">Quy cách</th>
              <th className="w-24 text-right">Số lượng</th>
              <th className="w-32 text-right">Đơn giá</th>
              <th className="w-32 text-right">Thành tiền</th>
              <th className="w-40">Ghi chú</th>
              <th className="w-8" />
            </>
          }
          totals={
            <tr>
              <td />
              <td colSpan={4}>Tổng cộng</td>
              <td className="text-right">
                <Money value={total} zero="zero" strong />
              </td>
              <td colSpan={2} />
            </tr>
          }
        >
          {lines.map((line, index) => (
            <tr key={line.id}>
              <td className="tnum text-center text-ink-3">{index + 1}</td>
              <td>
                <Combobox
                  size="sm"
                  value={line.productId ?? line.lineContent}
                  allowCustom
                  options={productOptions}
                  placeholder="Tên hàng"
                  onChange={(value) => {
                    if (!products.data?.items.some((p) => p.id === value)) patch(line.id, { lineContent: value, productId: null })
                  }}
                  onSelect={(option) => {
                    const p = option.data as Product
                    patch(line.id, { productId: p.id, lineContent: p.name, spec: p.spec, unitPrice: line.unitPrice ?? p.lastCost ?? null })
                  }}
                />
              </td>
              <td>
                <Input size="sm" value={line.spec} onChange={(e) => patch(line.id, { spec: e.target.value })} />
              </td>
              <td>
                <NumberInput size="sm" value={line.quantity} decimals={3} onChange={(v) => patch(line.id, { quantity: v })} />
              </td>
              <td>
                <NumberInput size="sm" value={line.unitPrice} onChange={(v) => patch(line.id, { unitPrice: v })} />
              </td>
              <td className="text-right">
                <Money value={lineAmount(line)} zero="blank" />
              </td>
              <td>
                <Input size="sm" value={line.note} onChange={(e) => patch(line.id, { note: e.target.value })} />
              </td>
              <td className="text-center">
                <button
                  type="button"
                  aria-label="Xoá dòng"
                  onClick={() => setLines((c) => (c.length > 1 ? c.filter((l) => l.id !== line.id) : c))}
                  className="grid size-6 place-items-center rounded-sm text-ink-3 hover:bg-danger-wash hover:text-danger"
                >
                  <Trash2 className="size-3.5" strokeWidth={1.7} />
                </button>
              </td>
            </tr>
          ))}
        </DocumentLines>
      }
      summary={
        <>
          {touched && problems.lines && <InlineAlert tone="danger">{problems.lines}</InlineAlert>}
          <DocumentSummary
            rows={[
              { label: `Tổng tiền hàng (${filled.length} dòng)`, value: vnd(total) },
              { label: 'Đã trả nhà cung cấp', value: vnd(paid ?? 0) },
              { label: 'Còn phải trả', value: vnd(total - (paid ?? 0)), strong: true },
            ]}
          />
        </>
      }
    />
  )
}

/* ============================================================================
   Nhà cung cấp
   ========================================================================== */

export function SuppliersPage() {
  const auth = useAuth()
  const [tab, setTab] = useState('active')
  const suppliers = useSuppliers(tab !== 'active')
  const [search, setSearch] = useState('')
  const [openId, setOpenId] = useState<string | null>(null)
  const [editing, setEditing] = useState<Supplier | null | 'new'>(null)

  const all = suppliers.data?.items ?? []
  const rows = all.filter((s) => {
    if (tab === 'active' && !s.isActive) return false
    if (tab === 'owing' && s.balance <= 0) return false
    if (tab === 'inactive' && s.isActive) return false
    if (search && !matches(`${s.name} ${s.taxCode} ${s.phone}`, search)) return false
    return true
  })
  const totals = rows.reduce((acc, s) => ({ bought: acc.bought + s.purchasedTotal, paid: acc.paid + s.paidTotal, balance: acc.balance + s.balance }), { bought: 0, paid: 0, balance: 0 })
  const payable = all.filter((s) => s.isActive).reduce((s, x) => s + Math.max(x.balance, 0), 0)

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label="Nhà cung cấp đang giao dịch" value={suppliers.data ? all.filter((s) => s.isActive).length : '…'} />
            <Figure label="Còn nợ nhà cung cấp" value={suppliers.data ? all.filter((s) => s.balance > 0).length : '…'} />
            <Figure label="Tổng phải trả" value={suppliers.data ? vnd(payable) : '…'} tone={payable > 0 ? 'warn' : undefined} />
          </FigureStrip>
        }
        tabs={[
          { id: 'active', label: 'Đang giao dịch' },
          { id: 'owing', label: 'Còn nợ' },
          { id: 'inactive', label: 'Ngừng giao dịch' },
        ]}
        tab={tab}
        onTabChange={setTab}
        filters={<SearchInput size="sm" className="w-64" placeholder="Tên, mã số thuế, điện thoại" value={search} onChange={(e) => setSearch(e.target.value)} onClear={() => setSearch('')} />}
        actions={
          auth.can(PERM.vouchersCreate) && (
            <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setEditing('new')}>
              Thêm nhà cung cấp
            </Button>
          )
        }
        columns={[
          { key: 'name', priority: 1, header: 'Nhà cung cấp', cell: (row) => <span className="font-medium">{row.name}</span>, sortValue: (r) => r.name, total: 'Tổng cộng' },
          { key: 'taxCode', priority: 3, header: 'Mã số thuế', cell: (row) => <span className="tnum">{row.taxCode}</span> },
          { key: 'phone', priority: 2, header: 'Điện thoại', cell: (row) => <span className="tnum">{row.phone}</span> },
          { key: 'address', priority: 3, header: 'Địa chỉ', cell: (row) => row.address, truncate: true, hidden: true },
          { key: 'count', priority: 3, header: 'Số phiếu', align: 'right', cell: (row) => row.purchaseCount, sortValue: (r) => r.purchaseCount },
          { key: 'bought', priority: 2, header: 'Đã mua', align: 'right', cell: (row) => <Money value={row.purchasedTotal} />, sortValue: (r) => r.purchasedTotal, total: <Money value={totals.bought} zero="zero" /> },
          { key: 'paid', priority: 2, header: 'Đã trả', align: 'right', cell: (row) => <Money value={row.paidTotal} />, sortValue: (r) => r.paidTotal, total: <Money value={totals.paid} zero="zero" /> },
          { key: 'balance', priority: 1, header: 'Còn nợ', align: 'right', cell: (row) => <Money value={row.balance} strong />, sortValue: (r) => r.balance, total: <Money value={totals.balance} zero="zero" /> },
          { key: 'last', priority: 3, header: 'Mua gần nhất', cell: (row) => date(row.lastPurchaseDate), sortValue: (r) => r.lastPurchaseDate ?? '' },
          { key: 'status', priority: 1, header: 'Trạng thái', cell: (row) => (row.isActive ? <StatusBadge tone="ok">Đang giao dịch</StatusBadge> : <StatusBadge>Ngừng</StatusBadge>) },
        ]}
        rows={rows}
        loading={suppliers.isLoading}
        error={suppliers.error}
        onRefresh={() => suppliers.refetch()}
        onRowClick={(row) => setOpenId(row.id)}
        activeKey={openId}
        defaultSort={{ key: 'balance', dir: 'desc' }}
        emptyTitle="Chưa có nhà cung cấp nào khớp"
      />
      <SupplierDrawer supplier={all.find((s) => s.id === openId) ?? null} onClose={() => setOpenId(null)} onEdit={(s) => setEditing(s)} />
      <SupplierModal supplier={editing} onClose={() => setEditing(null)} />
    </>
  )
}

function SupplierDrawer({ supplier, onClose, onEdit }: { supplier: Supplier | null; onClose: () => void; onEdit: (s: Supplier) => void }) {
  const auth = useAuth()
  const [tab, setTab] = useState('overview')
  const purchases = usePurchases({ supplierId: supplier?.id })
  const stock = useSupplierStock(tab === 'stock' ? supplier?.id : null)
  const stockTotal = (stock.data?.items ?? []).reduce((sum, item) => sum + item.remaining, 0)
  useEffect(() => {
    if (supplier) setTab('overview')
  }, [supplier])
  return (
    <Drawer
      open={!!supplier}
      onClose={onClose}
      width="lg"
      title={supplier?.name ?? ''}
      meta={
        supplier && (
          <>
            {supplier.phone && <span className="tnum">{supplier.phone}</span>}
            {supplier.taxCode && <span className="tnum">MST {supplier.taxCode}</span>}
            {!supplier.isActive && <StatusBadge>Ngừng giao dịch</StatusBadge>}
          </>
        )
      }
      actions={
        supplier &&
        auth.can(PERM.vouchersCreate) && (
          <Button size="sm" onClick={() => onEdit(supplier)}>
            Sửa
          </Button>
        )
      }
    >
      <div className="border-b border-line bg-panel">
        <FigureStrip className="rounded-none border-0">
          <Figure label="Đã mua" value={supplier ? vnd(supplier.purchasedTotal) : '…'} />
          <Figure label="Đã trả" value={supplier ? vnd(supplier.paidTotal) : '…'} />
          <Figure label="Còn nợ" value={supplier ? vnd(supplier.balance) : '…'} tone={supplier && supplier.balance > 0 ? 'warn' : undefined} />
        </FigureStrip>
        <Tabs
          items={[
            { id: 'overview', label: 'Tổng quan' },
            { id: 'purchases', label: 'Phiếu nhập', count: purchases.data?.items.length },
            { id: 'stock', label: 'Hàng còn trong kho' },
            { id: 'aliases', label: 'Bí danh', count: supplier?.aliases.length },
          ]}
          active={tab}
          onChange={setTab}
        />
      </div>
      <div className="p-3">
        {tab === 'overview' && (
          <Panel padded>
            <KeyValue
              rows={[
                ['Tên nhà cung cấp', supplier?.name],
                ['Mã số thuế', supplier?.taxCode || null],
                ['Điện thoại', supplier?.phone || null],
                ['Địa chỉ', supplier?.address || null],
                ['Ghi chú', supplier?.note || null],
                ['Số phiếu nhập', supplier?.purchaseCount ?? null],
                ['Mua gần nhất', supplier?.lastPurchaseDate ? date(supplier.lastPurchaseDate) : null],
              ]}
            />
          </Panel>
        )}
        {tab === 'purchases' && (
          <Panel>
            <DataTable
              columns={[
                { key: 'voucherNo', priority: 1, header: 'Số phiếu', cell: (row) => <span className="font-medium">{row.voucherNo}</span> },
                { key: 'date', priority: 2, header: 'Ngày', cell: (row) => date(row.docDate) },
                { key: 'total', priority: 1, header: 'Tổng tiền', align: 'right', cell: (row) => <Money value={row.total} muted={!!row.cancelledAt} /> },
                { key: 'paid', priority: 3, header: 'Đã trả', align: 'right', cell: (row) => <Money value={row.paidAmount} /> },
                { key: 'remaining', priority: 1, header: 'Còn nợ', align: 'right', cell: (row) => <Money value={row.cancelledAt ? 0 : row.remaining} strong /> },
                { key: 'status', priority: 1, header: 'Trạng thái', cell: (row) => <StatusBadge tone={purchaseStatus(row).tone}>{purchaseStatus(row).label}</StatusBadge> },
              ]}
              rows={purchases.data?.items ?? []}
              getKey={(row) => row.id}
              loading={purchases.isLoading}
              emptyTitle="Chưa có phiếu nhập từ nhà cung cấp này"
              density="compact"
            />
          </Panel>
        )}
        {tab === 'aliases' && supplier && <AliasPanel supplier={supplier} />}
        {tab === 'stock' && (
          <Panel>
            <DataTable
              columns={[
                { key: 'name', priority: 1, header: 'Tên hàng', cell: (row) => <span className="font-medium">{row.name}</span>, total: 'Tổng cộng' },
                { key: 'spec', priority: 2, header: 'Quy cách', cell: (row) => row.spec },
                { key: 'bought', priority: 2, header: 'Đã nhập', align: 'right', cell: (row) => qty(row.bought) },
                { key: 'sold', priority: 2, header: 'Đã bán', align: 'right', cell: (row) => qty(row.sold) },
                {
                  key: 'remaining', priority: 1, header: 'Còn lại', align: 'right',
                  cell: (row) => <span className="tnum font-semibold">{qty(row.remaining)}</span>,
                  total: qty(stockTotal),
                },
                { key: 'cost', priority: 3, header: 'Giá nhập gần nhất', align: 'right', cell: (row) => <Money value={row.lastCost} /> },
                { key: 'lastBought', priority: 3, header: 'Nhập gần nhất', cell: (row) => date(row.lastBoughtDate) },
              ]}
              rows={stock.data?.items ?? []}
              getKey={(row) => row.productId}
              loading={stock.isLoading}
              emptyTitle="Chưa có hàng nào của nhà cung cấp này trong kho"
              emptyDescription="Số này chỉ đếm được khi dòng phiếu nhập và dòng phiếu xuất đều gắn được mã hàng trong danh mục."
              density="compact"
            />
          </Panel>
        )}
      </div>
    </Drawer>
  )
}

/**
 * Bí danh của một nhà cung cấp.
 *
 * Người trong kho gọi "anh A - Đại Phát", giấy tờ ghi "Công ty TNHH Đại Phát". Đặt bí danh rồi thì
 * lập phiếu nhập gõ kiểu nào cũng về đúng một hồ sơ, thay vì đẻ ra nhà cung cấp thứ hai và chẻ đôi
 * công nợ phải trả.
 */
function AliasPanel({ supplier }: { supplier: Supplier }) {
  const toast = useToast()
  const aliases = useSupplierAliases(supplier.id)
  const add = useAddSupplierAlias()
  const remove = useDeleteSupplierAlias()
  const [draft, setDraft] = useState('')

  const submit = async () => {
    const alias = draft.trim()
    if (!alias) return
    try {
      await add.mutateAsync({ supplierId: supplier.id, alias })
      setDraft('')
      toast.success(`Đã thêm bí danh "${alias}"`)
    } catch (e) {
      toast.error('Không thêm được bí danh', errorMessage(e))
    }
  }

  return (
    <Panel>
      <div className="flex items-end gap-2 border-b border-line p-3">
        <Field label="Tên gọi khác" className="flex-1">
          <Input
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault()
                void submit()
              }
            }}
            placeholder="Ví dụ: anh A - Đại Phát"
          />
        </Field>
        <Button size="sm" variant="primary" loading={add.isPending} onClick={() => void submit()}>
          Thêm
        </Button>
      </div>
      <DataTable
        columns={[
          { key: 'alias', priority: 1, header: 'Bí danh', cell: (row) => <span className="font-medium">{row.alias}</span> },
          { key: 'by', priority: 3, header: 'Người đặt', cell: (row) => row.createdBy },
          {
            key: 'remove', priority: 1, header: '', width: '3rem', align: 'center',
            cell: (row) => (
              <IconButton
                label={`Xoá bí danh ${row.alias}`}
                size="sm"
                variant="ghost"
                icon={<Trash2 className="size-3.5" strokeWidth={1.7} />}
                onClick={() => {
                  void remove
                    .mutateAsync({ supplierId: supplier.id, aliasId: row.id })
                    .then(() => toast.success('Đã xoá bí danh'))
                    .catch((e) => toast.error('Không xoá được', errorMessage(e)))
                }}
              />
            ),
          },
        ]}
        rows={aliases.data?.items ?? []}
        getKey={(row) => row.id}
        loading={aliases.isLoading}
        emptyTitle="Chưa có bí danh nào"
        emptyDescription="Đặt bí danh để lập phiếu nhập gõ kiểu nào cũng về đúng nhà cung cấp này."
        density="compact"
      />
    </Panel>
  )
}

function SupplierModal({ supplier, onClose }: { supplier: Supplier | null | 'new'; onClose: () => void }) {
  const toast = useToast()
  const save = useSaveSupplier()
  const open = supplier !== null
  const editing = supplier && supplier !== 'new' ? supplier : null
  const [form, setForm] = useState({ name: '', taxCode: '', phone: '', address: '', note: '', isActive: true })
  const [touched, setTouched] = useState(false)
  useEffect(() => {
    if (open) {
      setForm({
        name: editing?.name ?? '',
        taxCode: editing?.taxCode ?? '',
        phone: editing?.phone ?? '',
        address: editing?.address ?? '',
        note: editing?.note ?? '',
        isActive: editing?.isActive ?? true,
      })
      setTouched(false)
    }
  }, [open, editing])

  const submit = async () => {
    setTouched(true)
    if (!form.name.trim()) return
    try {
      await save.mutateAsync({ id: editing?.id, body: { ...form, name: form.name.trim(), isActive: editing ? form.isActive : undefined } })
      toast.success(editing ? 'Đã cập nhật nhà cung cấp' : 'Đã thêm nhà cung cấp')
      onClose()
    } catch (error) {
      toast.error('Không lưu được', errorMessage(error))
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? `Sửa ${editing.name}` : 'Thêm nhà cung cấp'}
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
        <Field label="Tên nhà cung cấp" required error={touched && !form.name.trim() ? 'Nhập tên nhà cung cấp' : null}>
          <Input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} data-autofocus="" />
        </Field>
        <Field label="Mã số thuế">
          <Input value={form.taxCode} onChange={(e) => setForm({ ...form, taxCode: e.target.value })} className="tnum" />
        </Field>
        <Field label="Điện thoại">
          <Input value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} inputMode="tel" className="tnum" />
        </Field>
        <Field label="Địa chỉ">
          <Textarea value={form.address} onChange={(e) => setForm({ ...form, address: e.target.value })} rows={2} />
        </Field>
        <Field label="Ghi chú">
          <Input value={form.note} onChange={(e) => setForm({ ...form, note: e.target.value })} />
        </Field>
        {editing && <Checkbox label="Đang giao dịch" checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} />}
      </div>
    </Modal>
  )
}
