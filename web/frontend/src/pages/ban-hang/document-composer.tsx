import { useMemo, useState } from 'react'
import { Trash2 } from 'lucide-react'
import { qty, todayISO, vnd } from '@/lib/format'
import {
  useCustomers,
  useProductSources,
  useProducts,
  useSaveSalesDocument,
  type DocumentLine,
  type Product,
  type SaveDocumentRequest,
} from '@/api/sales'
import { useSuppliers } from '@/api/purchases'
import {
  Combobox,
  type ComboOption,
  DatePicker,
  DocumentForm,
  DocumentLines,
  DocumentSummary,
  Field,
  FormGrid,
  InlineAlert,
  Input,
  Money,
  NumberInput,
  StatusBadge,
  useToast,
} from '@/ui'
import { errorMessage } from '../_shared'

interface DraftLine {
  id: number
  productId: string | null
  lineContent: string
  spec: string
  quantity: number | null
  unitPrice: number | null
  note: string
  /** Nguồn hàng của chính dòng này. Nội bộ: không in ra phiếu, không có trong PDF gửi khách. */
  supplierId: string | null
  supplierName: string
}

const emptyLine = (id: number): DraftLine => ({
  id,
  productId: null,
  lineContent: '',
  spec: '',
  quantity: null,
  unitPrice: null,
  note: '',
  supplierId: null,
  supplierName: '',
})

const lineAmount = (line: DraftLine) => (line.quantity ?? 0) * (line.unitPrice ?? 0)

/** Phiếu bán hàng mới: đầu phiếu, lưới dòng hàng, tổng tiền, thanh nút ở đáy. */
export function SalesDocumentComposer({
  onClose,
  onSaved,
}: {
  onClose: () => void
  onSaved?: (id: string) => void
}) {
  const toast = useToast()
  const customers = useCustomers()
  const products = useProducts()
  const save = useSaveSalesDocument()

  const [customer, setCustomer] = useState('')
  const [docDate, setDocDate] = useState(todayISO())
  const [voucherNo, setVoucherNo] = useState('')
  const [content, setContent] = useState('')
  const [note, setNote] = useState('')
  const [lines, setLines] = useState<DraftLine[]>([emptyLine(1), emptyLine(2), emptyLine(3)])
  const [touched, setTouched] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const patch = (id: number, change: Partial<DraftLine>) =>
    setLines((current) => current.map((line) => (line.id === id ? { ...line, ...change } : line)))
  const addLine = () => setLines((current) => [...current, emptyLine(Date.now())])
  const removeLine = (id: number) => setLines((current) => (current.length > 1 ? current.filter((l) => l.id !== id) : current))

  const filled = lines.filter((l) => l.lineContent.trim())
  const total = filled.reduce((sum, l) => sum + lineAmount(l), 0)
  const problems = {
    customer: !customer.trim() ? 'Chọn hoặc nhập tên khách hàng' : null,
    date: !docDate ? 'Chọn ngày lập phiếu' : null,
    lines: filled.length === 0 ? 'Phiếu cần ít nhất một dòng hàng' : null,
  }
  const valid = !problems.customer && !problems.date && !problems.lines

  const productOptions = useMemo(
    () =>
      (products.data?.items ?? []).map((p) => ({
        value: p.id,
        label: p.spec ? `${p.name} · ${p.spec}` : p.name,
        description: p.lastPrice != null ? vnd(p.lastPrice) : undefined,
        keywords: p.code,
        data: p,
      })),
    [products.data],
  )

  const body = (): SaveDocumentRequest => ({
    voucherNo: voucherNo.trim(),
    date: docDate,
    customerName: customer.trim(),
    content: content.trim(),
    note: note.trim(),
    documentType: 'document',
    lines: filled.map<DocumentLine>((l) => ({
      lineContent: l.lineContent.trim(),
      spec: l.spec.trim(),
      quantity: l.quantity ?? 0,
      unitPrice: l.unitPrice ?? 0,
      note: l.note.trim(),
      productId: l.productId,
      supplierId: l.supplierId,
      supplierName: l.supplierName,
    })),
  })

  const submit = async (mode: 'close' | 'new') => {
    setTouched(true)
    if (!valid) return
    setError(null)
    try {
      const result = await save.mutateAsync({ body: body() })
      toast.success('Đã lưu phiếu bán hàng')
      if (mode === 'new') {
        setCustomer('')
        setContent('')
        setNote('')
        setVoucherNo('')
        setLines([emptyLine(1), emptyLine(2), emptyLine(3)])
        setTouched(false)
      } else if (onSaved) onSaved(result.id)
      else onClose()
    } catch (e) {
      setError(errorMessage(e, 'Không lưu được phiếu.'))
    }
  }

  return (
    <DocumentForm
      title="Phiếu bán hàng"
      status={<StatusBadge>Nháp</StatusBadge>}
      error={error}
      busy={save.isPending}
      onClose={onClose}
      onSave={() => void submit('close')}
      onSaveAndNew={() => void submit('new')}
      fields={
        <FormGrid cols={3}>
          <Field label="Khách hàng" required error={touched ? problems.customer : null}>
            <Combobox
              value={customer}
              onChange={setCustomer}
              allowCustom
              autoFocus
              placeholder="Gõ để tìm hoặc nhập tên mới"
              loading={customers.isLoading}
              options={(customers.data ?? []).map((c) => ({
                  value: c.name,
                  label: c.name,
                  // Bí danh vào keywords: gõ "anh Ba - Hoà Phát" vẫn ra đúng khách, danh sách không nhân đôi.
                  description: c.aliases?.length ? c.aliases.join(', ') : c.phone,
                  keywords: c.aliases?.join(' '),
                }))}
            />
          </Field>
          <Field label="Ngày lập phiếu" required error={touched ? problems.date : null}>
            <DatePicker value={docDate} onChange={setDocDate} clearable={false} />
          </Field>
          <Field label="Số phiếu" hint="Để trống để hệ thống cấp số">
            <Input value={voucherNo} onChange={(e) => setVoucherNo(e.target.value)} />
          </Field>
          <Field label="Diễn giải" className="sm:col-span-2">
            <Input value={content} onChange={(e) => setContent(e.target.value)} placeholder="Nội dung bán hàng" />
          </Field>
          <Field label="Ghi chú">
            <Input value={note} onChange={(e) => setNote(e.target.value)} />
          </Field>
        </FormGrid>
      }
      lines={
        <DocumentLines
          count={lines.length}
          onAddLine={addLine}
          onClearLines={() => setLines([emptyLine(1)])}
          head={
            <>
              <th className="w-8 text-center">#</th>
              <th>Tên hàng</th>
              <th className="w-40">Quy cách</th>
              <th className="w-44" title="Chỉ nội bộ: không in ra phiếu, không có trong PDF gửi khách">
                Nguồn hàng <span className="font-normal text-ink-3">(nội bộ)</span>
              </th>
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
              <td colSpan={5}>Tổng cộng</td>
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
                    const product = products.data?.items.find((p) => p.id === value)
                    if (!product) patch(line.id, { lineContent: value, productId: null })
                  }}
                  onSelect={(option) => {
                    const p = option.data as Product
                    // Đổi mặt hàng thì nguồn hàng cũ không còn đúng: nhà cung cấp cũ có thể chưa
                    // từng nhập mặt hàng mới này.
                    patch(line.id, {
                      productId: p.id,
                      lineContent: p.name,
                      spec: p.spec,
                      unitPrice: line.unitPrice ?? p.lastPrice ?? null,
                      ...(p.id === line.productId ? {} : { supplierId: null, supplierName: '' }),
                    })
                  }}
                />
              </td>
              <td>
                <Input size="sm" value={line.spec} onChange={(e) => patch(line.id, { spec: e.target.value })} />
              </td>
              <td>
                <SourcePicker
                  productId={line.productId}
                  supplierId={line.supplierId}
                  supplierName={line.supplierName}
                  onChange={(supplierId, supplierName) => patch(line.id, { supplierId, supplierName })}
                />
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
                  onClick={() => removeLine(line.id)}
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
              { label: 'Tổng thanh toán', value: vnd(total), strong: true },
            ]}
          />
        </>
      }
    />
  )
}


/**
 * Ô chọn nguồn hàng của một dòng phiếu: cuộn sắp xuất là hàng nhập của nhà cung cấp nào.
 *
 * Danh sách ưu tiên những nhà cung cấp ĐÃ TỪNG nhập đúng mặt hàng này, kèm số còn lại và giá nhập
 * gần nhất — đó là thứ người lập phiếu cần để biết nên lấy hàng của ai. Chưa chọn mặt hàng trong
 * danh mục thì lùi về danh sách nhà cung cấp đầy đủ, vì phiếu cho hàng gõ tay vẫn phải lập được.
 */
function SourcePicker({
  productId,
  supplierId,
  supplierName,
  onChange,
}: {
  productId: string | null
  supplierId: string | null
  supplierName: string
  onChange: (supplierId: string | null, supplierName: string) => void
}) {
  const sources = useProductSources(productId)
  const suppliers = useSuppliers()

  const options = useMemo<ComboOption[]>(() => {
    const known = (sources.data?.items ?? []).map((source) => ({
      value: source.supplierId,
      label: source.supplierName,
      description: [
        `còn ${qty(source.remaining)}`,
        source.lastCost ? `nhập ${vnd(source.lastCost)}` : null,
      ]
        .filter(Boolean)
        .join('  ·  '),
    }))
    const list =
      known.length > 0
        ? known
        : (suppliers.data?.items ?? []).map((supplier) => ({ value: supplier.id, label: supplier.name }))

    // Nhà cung cấp đã chọn phải luôn có mặt trong danh sách, kể cả khi họ đã ngừng giao dịch hoặc
    // chưa từng nhập mặt hàng này — nếu không ô sẽ hiện trống và người dùng tưởng mình chưa chọn.
    if (supplierId && !list.some((option) => option.value === supplierId))
      return [{ value: supplierId, label: supplierName || 'Nhà cung cấp đã chọn' }, ...list]
    return list
  }, [sources.data, suppliers.data, supplierId, supplierName])

  return (
    <Combobox
      size="sm"
      value={supplierId ?? ''}
      options={options}
      clearable
      loading={sources.isLoading}
      placeholder={productId ? 'Chọn nguồn' : 'Chọn nhà cung cấp'}
      emptyText={productId ? 'Chưa nhập mặt hàng này của ai' : 'Chưa có nhà cung cấp'}
      onChange={(value) => {
        if (!value) onChange(null, '')
      }}
      onSelect={(option) => onChange(option.value, option.label)}
    />
  )
}
