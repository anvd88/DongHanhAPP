import { useMemo, useState } from 'react'
import { QRCodeSVG } from 'qrcode.react'
import { Plus, Trash2 } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { PERM } from '@/lib/permissions'
import { date, dateTime, monthLabel, monthRange, todayISO, vnd } from '@/lib/format'
import { matches } from '@/lib/text'
import { useFiscal } from '@/shell/FiscalContext'
import {
  CASH_SOURCE_LABELS,
  useCashBalance,
  useCashLedger,
  useCreateCashEntry,
} from '@/api/accounting'
import { useCustomers, useCashVouchers, useSaveCashVoucher, useIssueCashVoucher, useCancelCashVoucher, type DocumentListItem } from '@/api/sales'
import {
  COLLECTION_EVENT_LABELS,
  DENOMINATIONS,
  PAYOUT_EVENT_LABELS,
  collectionStatus,
  payoutStatus,
  useAcceptCollection,
  useCancelCollection,
  useCashCollection,
  useCashCollections,
  useCollectionCustomers,
  useCollectionDrivers,
  useCountCash,
  useCreateCollection,
  useCreatePayoutVoucher,
  useFailCollection,
  usePayoutCancel,
  usePayoutCategories,
  usePayoutHistory,
  usePayoutRecipients,
  usePayoutRefundSources,
  usePayoutSummary,
  usePayoutTransition,
  usePayoutVouchers,
  useRegenerateVoucherQr,
  useResolveVariance,
  type CashCollection,
  type PayoutVoucher,
} from '@/api/cash'
import {
  Button,
  Checkbox,
  Combobox,
  ConfirmDialog,
  DataTable,
  DatePicker,
  DocumentForm,
  DocumentLines,
  DocumentSummary,
  Drawer,
  Field,
  Figure,
  FigureStrip,
  FormGrid,
  InlineAlert,
  Input,
  KeyValue,
  Modal,
  Money,
  MonthPicker,
  NumberInput,
  Panel,
  SearchInput,
  Segmented,
  Select,
  StatusBadge,
  Textarea,
  useToast,
  type Column,
} from '@/ui'
import { ModuleScreen, errorMessage } from './_shared'

/* ============================================================================
   Phiếu thu / chi tiền mặt
   ========================================================================== */

function cashVoucherStatus(row: DocumentListItem): { label: string; tone: 'neutral' | 'ok' | 'danger' } {
  if (row.cancelledAt) return { label: 'Đã huỷ', tone: 'danger' }
  if (row.issuedAt) return { label: 'Đã ghi sổ', tone: 'ok' }
  return { label: 'Nháp', tone: 'neutral' }
}

export function CashVouchersPage() {
  const auth = useAuth()
  const fiscal = useFiscal()
  const toast = useToast()
  const vouchers = useCashVouchers()
  const issue = useIssueCashVoucher()
  const cancel = useCancelCashVoucher()
  const [tab, setTab] = useState('all')
  const [search, setSearch] = useState('')
  const [composer, setComposer] = useState<null | 'receipt' | 'payment'>(null)
  const [cancelling, setCancelling] = useState<DocumentListItem | null>(null)

  const all = vouchers.data ?? []
  const isReceipt = (d: DocumentListItem) => d.documentType.toLowerCase().includes('thu')
  const rows = all.filter((d) => {
    if (tab === 'receipt' && !isReceipt(d)) return false
    if (tab === 'payment' && isReceipt(d)) return false
    if (tab === 'cancelled' && !d.cancelledAt) return false
    if (tab !== 'cancelled' && tab !== 'all' && d.cancelledAt) return false
    if (search && !matches(`${d.voucherNo} ${d.customerName} ${d.content}`, search)) return false
    return true
  })
  const range = monthRange(fiscal.period)
  const inPeriod = all.filter((d) => !d.cancelledAt && d.date >= range.from && d.date <= range.to)
  const receiptsIn = inPeriod.filter(isReceipt).reduce((s, d) => s + d.total, 0)
  const paymentsIn = inPeriod.filter((d) => !isReceipt(d)).reduce((s, d) => s + d.total, 0)

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label={`Thu trong kỳ ${Number(fiscal.period.slice(5))}/${fiscal.year}`} value={vouchers.data ? vnd(receiptsIn) : '…'} tone="ok" />
            <Figure label="Chi trong kỳ" value={vouchers.data ? vnd(paymentsIn) : '…'} />
            <Figure label="Thu trừ chi" value={vouchers.data ? vnd(receiptsIn - paymentsIn) : '…'} tone={receiptsIn - paymentsIn < 0 ? 'danger' : undefined} />
            <Figure label="Phiếu nháp" value={vouchers.data ? all.filter((d) => !d.issuedAt && !d.cancelledAt).length : '…'} />
          </FigureStrip>
        }
        tabs={[
          { id: 'all', label: 'Tất cả', count: all.length },
          { id: 'receipt', label: 'Phiếu thu' },
          { id: 'payment', label: 'Phiếu chi' },
          { id: 'cancelled', label: 'Đã huỷ' },
        ]}
        tab={tab}
        onTabChange={setTab}
        filters={<SearchInput size="sm" className="w-64" placeholder="Số phiếu, đối tượng, nội dung" value={search} onChange={(e) => setSearch(e.target.value)} onClear={() => setSearch('')} />}
        actions={
          auth.can(PERM.vouchersCreate) && (
            <>
              <Button size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setComposer('payment')}>
                Phiếu chi
              </Button>
              <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setComposer('receipt')}>
                Phiếu thu
              </Button>
            </>
          )
        }
        columns={[
          { key: 'voucherNo', priority: 1, header: 'Số phiếu', width: '8rem', cell: (d) => <span className="font-medium">{d.voucherNo}</span>, sortValue: (d) => d.voucherNo },
          { key: 'date', priority: 1, header: 'Ngày', width: '6.5rem', cell: (d) => date(d.date), sortValue: (d) => d.date },
          { key: 'type', priority: 2, header: 'Loại', width: '6.5rem', cell: (d) => d.documentType, sortValue: (d) => d.documentType },
          { key: 'party', priority: 1, header: 'Đối tượng', cell: (d) => d.customerName, truncate: true, sortValue: (d) => d.customerName },
          { key: 'content', priority: 3, header: 'Nội dung', cell: (d) => d.content, truncate: true },
          { key: 'in', priority: 1, header: 'Thu', align: 'right', cell: (d) => (isReceipt(d) ? <Money value={d.total} muted={!!d.cancelledAt} /> : null), total: <Money value={rows.filter((d) => isReceipt(d) && !d.cancelledAt).reduce((s, d) => s + d.total, 0)} zero="zero" /> },
          { key: 'out', priority: 1, header: 'Chi', align: 'right', cell: (d) => (!isReceipt(d) ? <Money value={d.total} muted={!!d.cancelledAt} /> : null), total: <Money value={rows.filter((d) => !isReceipt(d) && !d.cancelledAt).reduce((s, d) => s + d.total, 0)} zero="zero" /> },
          { key: 'status', priority: 1, header: 'Trạng thái', width: '7rem', cell: (d) => <StatusBadge tone={cashVoucherStatus(d).tone}>{cashVoucherStatus(d).label}</StatusBadge> },
          { key: 'createdBy', priority: 3, header: 'Người lập', cell: (d) => d.createdBy, hidden: true },
          {
            key: 'actions', priority: 1,
            header: '',
            align: 'right',
            locked: true,
            cell: (d) =>
              d.cancelledAt ? null : (
                <span className="row-actions inline-flex gap-1">
                  {!d.issuedAt && auth.can(PERM.vouchersApprove) && (
                    <Button size="sm" variant="ghost" loading={issue.isPending && issue.variables === d.id} onClick={async (e) => { e.stopPropagation(); try { await issue.mutateAsync(d.id); toast.success('Đã ghi sổ phiếu') } catch (error) { toast.error('Không ghi sổ được', errorMessage(error)) } }}>
                      Ghi sổ
                    </Button>
                  )}
                  {auth.can(PERM.vouchersCancel) && (
                    <Button size="sm" variant="ghost" className="text-danger" onClick={(e) => { e.stopPropagation(); setCancelling(d) }}>
                      Huỷ
                    </Button>
                  )}
                </span>
              ),
          },
        ]}
        rows={rows}
        loading={vouchers.isLoading}
        error={vouchers.error}
        onRefresh={() => vouchers.refetch()}
        defaultSort={{ key: 'date', dir: 'desc' }}
        emptyTitle="Chưa có phiếu thu chi nào trong bộ lọc này"
      />
      {composer && <CashVoucherComposer kind={composer} onClose={() => setComposer(null)} />}
      <ConfirmDialog
        open={!!cancelling}
        onClose={() => setCancelling(null)}
        title={`Huỷ ${cancelling?.documentType.toLowerCase() ?? 'phiếu'} ${cancelling?.voucherNo ?? ''}`}
        confirmLabel="Huỷ phiếu"
        tone="danger"
        requireReason
        reasonLabel="Lý do huỷ"
        busy={cancel.isPending}
        onConfirm={async (reason) => {
          if (!cancelling) return
          try {
            await cancel.mutateAsync({ id: cancelling.id, reason })
            toast.success('Đã huỷ phiếu')
            setCancelling(null)
          } catch (error) {
            toast.error('Không huỷ được', errorMessage(error))
          }
        }}
      />
    </>
  )
}

function CashVoucherComposer({ kind: initialKind, onClose }: { kind: 'receipt' | 'payment'; onClose: () => void }) {
  const toast = useToast()
  const customers = useCustomers()
  const save = useSaveCashVoucher()
  const [kind, setKind] = useState<'receipt' | 'payment'>(initialKind)
  const [party, setParty] = useState('')
  const [docDate, setDocDate] = useState(todayISO())
  const [voucherNo, setVoucherNo] = useState('')
  const [content, setContent] = useState('')
  const [note, setNote] = useState('')
  const [lines, setLines] = useState([{ id: 1, lineContent: '', quantity: 1 as number | null, unitPrice: null as number | null }])
  const [touched, setTouched] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const filled = lines.filter((l) => l.lineContent.trim() || l.unitPrice)
  const total = filled.reduce((s, l) => s + (l.quantity ?? 0) * (l.unitPrice ?? 0), 0)
  const problems = {
    party: !party.trim() ? 'Nhập đối tượng thu hoặc chi' : null,
    lines: total <= 0 ? 'Nhập ít nhất một khoản có số tiền' : null,
  }
  const patch = (id: number, change: Partial<(typeof lines)[number]>) => setLines((c) => c.map((l) => (l.id === id ? { ...l, ...change } : l)))

  const submit = async () => {
    setTouched(true)
    if (problems.party || problems.lines) return
    setError(null)
    try {
      await save.mutateAsync({
        body: {
          voucherNo: voucherNo.trim(),
          date: docDate,
          customerName: party.trim(),
          content: content.trim(),
          note: note.trim(),
          documentType: kind,
          lines: filled.map((l) => ({ lineContent: l.lineContent.trim() || content.trim() || (kind === 'receipt' ? 'Thu tiền' : 'Chi tiền'), spec: '', quantity: l.quantity ?? 1, unitPrice: l.unitPrice ?? 0, note: '' })),
        },
      })
      toast.success(kind === 'receipt' ? 'Đã lưu phiếu thu' : 'Đã lưu phiếu chi')
      onClose()
    } catch (e) {
      setError(errorMessage(e, 'Không lưu được phiếu.'))
    }
  }

  return (
    <DocumentForm
      title={kind === 'receipt' ? 'Phiếu thu tiền mặt' : 'Phiếu chi tiền mặt'}
      status={<StatusBadge>Nháp</StatusBadge>}
      kind={
        <Segmented items={[{ id: 'receipt', label: 'Phiếu thu' }, { id: 'payment', label: 'Phiếu chi' }]} active={kind} onChange={(id) => setKind(id as 'receipt' | 'payment')} />
      }
      error={error}
      busy={save.isPending}
      onClose={onClose}
      onSave={() => void submit()}
      fields={
        <FormGrid cols={3}>
          <Field label={kind === 'receipt' ? 'Người nộp tiền' : 'Người nhận tiền'} required error={touched ? problems.party : null}>
            <Combobox value={party} onChange={setParty} allowCustom autoFocus placeholder="Khách hàng hoặc tên người" options={(customers.data ?? []).map((c) => ({ value: c.name, label: c.name, description: c.phone }))} loading={customers.isLoading} />
          </Field>
          <Field label="Ngày" required>
            <DatePicker value={docDate} onChange={setDocDate} clearable={false} />
          </Field>
          <Field label="Số phiếu" hint="Để trống để hệ thống cấp số">
            <Input value={voucherNo} onChange={(e) => setVoucherNo(e.target.value)} />
          </Field>
          <Field label="Nội dung" className="sm:col-span-2">
            <Input value={content} onChange={(e) => setContent(e.target.value)} placeholder={kind === 'receipt' ? 'Thu tiền bán hàng' : 'Chi mua vật tư'} />
          </Field>
          <Field label="Ghi chú">
            <Input value={note} onChange={(e) => setNote(e.target.value)} />
          </Field>
        </FormGrid>
      }
      lines={
        <DocumentLines
          title="Khoản tiền"
          count={lines.length}
          onAddLine={() => setLines((c) => [...c, { id: Date.now(), lineContent: '', quantity: 1, unitPrice: null }])}
          head={
            <>
              <th className="w-8 text-center">#</th>
              <th>Diễn giải khoản</th>
              <th className="w-24 text-right">Số lượng</th>
              <th className="w-40 text-right">Đơn giá</th>
              <th className="w-40 text-right">Thành tiền</th>
              <th className="w-8" />
            </>
          }
          totals={
            <tr>
              <td />
              <td colSpan={3}>Tổng cộng</td>
              <td className="text-right"><Money value={total} zero="zero" strong /></td>
              <td />
            </tr>
          }
        >
          {lines.map((line, index) => (
            <tr key={line.id}>
              <td className="tnum text-center text-ink-3">{index + 1}</td>
              <td><Input size="sm" value={line.lineContent} onChange={(e) => patch(line.id, { lineContent: e.target.value })} /></td>
              <td><NumberInput size="sm" value={line.quantity} decimals={3} onChange={(v) => patch(line.id, { quantity: v })} /></td>
              <td><NumberInput size="sm" value={line.unitPrice} onChange={(v) => patch(line.id, { unitPrice: v })} /></td>
              <td className="text-right"><Money value={(line.quantity ?? 0) * (line.unitPrice ?? 0)} zero="blank" /></td>
              <td className="text-center">
                <button type="button" aria-label="Xoá dòng" onClick={() => setLines((c) => (c.length > 1 ? c.filter((l) => l.id !== line.id) : c))} className="grid size-6 place-items-center rounded-sm text-ink-3 hover:bg-danger-wash hover:text-danger">
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
          <DocumentSummary rows={[{ label: kind === 'receipt' ? 'Tổng thu' : 'Tổng chi', value: vnd(total), strong: true }]} />
        </>
      }
    />
  )
}

/* ============================================================================
   Quỹ tiền mặt
   ========================================================================== */

export function CashFundPage() {
  const auth = useAuth()
  const fiscal = useFiscal()
  const toast = useToast()
  const [direction, setDirection] = useState('all')
  const [source, setSource] = useState('')
  const [search, setSearch] = useState('')
  const ledger = useCashLedger({ month: fiscal.period, direction: direction === 'all' ? undefined : direction, source: source || undefined })
  const balance = useCashBalance(fiscal.period)
  const create = useCreateCashEntry()
  const [adding, setAdding] = useState(false)
  const [form, setForm] = useState({ direction: 'in' as 'in' | 'out', amount: null as number | null, date: todayISO(), reason: '', counterparty: '', note: '' })

  const rows = (ledger.data?.entries ?? []).filter((e) => !search || matches(`${e.sourceRef} ${e.reason} ${e.counterparty} ${e.actor}`, search))
  const l = ledger.data

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label="Tồn quỹ hiện tại" value={balance.data ? vnd(balance.data.balance) : '…'} tone="brand" />
            <Figure label={`Đầu kỳ ${monthLabel(fiscal.period).toLowerCase()}`} value={l ? vnd(l.openingBalance) : '…'} />
            <Figure label="Thu trong kỳ" value={l ? vnd(l.totalIn) : '…'} tone="ok" />
            <Figure label="Chi trong kỳ" value={l ? vnd(l.totalOut) : '…'} />
            <Figure label="Cuối kỳ" value={l ? vnd(l.closingBalance) : '…'} />
          </FigureStrip>
        }
        tabs={[
          { id: 'all', label: 'Tất cả' },
          { id: 'in', label: 'Thu' },
          { id: 'out', label: 'Chi' },
        ]}
        tab={direction}
        onTabChange={setDirection}
        filters={
          <>
            <MonthPicker value={fiscal.period} onChange={fiscal.setPeriod} size="sm" className="w-40" />
            <Select size="sm" value={source} onChange={(e) => setSource(e.target.value)} className="w-44">
              <option value="">Mọi nguồn</option>
              {(Object.keys(CASH_SOURCE_LABELS) as Array<keyof typeof CASH_SOURCE_LABELS>).map((k) => (
                <option key={k} value={k}>{CASH_SOURCE_LABELS[k]}</option>
              ))}
            </Select>
            <SearchInput size="sm" className="w-56" placeholder="Số chứng từ, diễn giải, đối tượng" value={search} onChange={(e) => setSearch(e.target.value)} onClear={() => setSearch('')} />
          </>
        }
        actions={
          auth.can(PERM.cashFundManage) && (
            <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setAdding(true)}>
              Bút toán quỹ
            </Button>
          )
        }
        columns={[
          { key: 'time', priority: 1, header: 'Thời điểm', width: '9rem', cell: (e) => dateTime(e.occurredAt), sortValue: (e) => e.occurredAt },
          { key: 'ref', priority: 2, header: 'Chứng từ', width: '8rem', cell: (e) => <span className="tnum font-medium">{e.sourceRef}</span>, sortValue: (e) => e.sourceRef },
          { key: 'source', priority: 3, header: 'Nguồn', width: '9rem', cell: (e) => CASH_SOURCE_LABELS[e.sourceKind] ?? e.sourceKind, sortValue: (e) => e.sourceKind },
          { key: 'reason', priority: 2, header: 'Diễn giải', cell: (e) => e.reason, truncate: true },
          { key: 'party', priority: 3, header: 'Đối tượng', cell: (e) => e.counterparty, truncate: true },
          { key: 'actor', priority: 3, header: 'Người thực hiện', cell: (e) => e.actor, hidden: true },
          { key: 'in', priority: 1, header: 'Thu', align: 'right', width: '8rem', cell: (e) => (e.direction === 'in' ? <Money value={e.amount} /> : null), total: <Money value={l?.totalIn} zero="zero" /> },
          { key: 'out', priority: 1, header: 'Chi', align: 'right', width: '8rem', cell: (e) => (e.direction === 'out' ? <Money value={e.amount} /> : null), total: <Money value={l?.totalOut} zero="zero" /> },
          { key: 'balance', priority: 1, header: 'Tồn quỹ', align: 'right', width: '9rem', cell: (e) => <Money value={e.balanceAfter} zero="zero" strong />, total: <Money value={l?.closingBalance} zero="zero" /> },
        ]}
        rows={rows}
        getKey={(e) => `${e.sourceKind}-${e.sourceId}`}
        loading={ledger.isLoading}
        error={ledger.error}
        onRefresh={() => ledger.refetch()}
        pageSize={50}
        emptyTitle="Không có phát sinh quỹ trong kỳ với bộ lọc này"
      />
      <Modal
        open={adding}
        onClose={() => setAdding(false)}
        title="Bút toán quỹ thủ công"
        description="Chỉ dùng cho tiền ra vào không có chứng từ khác: nộp ngân hàng, rút về quỹ, điều chỉnh kiểm kê."
        size="sm"
        dismissible={false}
        footer={
          <>
            <Button size="sm" onClick={() => setAdding(false)} disabled={create.isPending}>Huỷ</Button>
            <Button
              size="sm"
              variant="primary"
              loading={create.isPending}
              disabled={!form.amount || !form.reason.trim()}
              onClick={async () => {
                try {
                  await create.mutateAsync({ direction: form.direction, amount: form.amount ?? 0, occurredAt: `${form.date}T12:00:00`, reason: form.reason.trim(), counterparty: form.counterparty.trim(), note: form.note.trim() })
                  toast.success('Đã ghi bút toán quỹ')
                  setAdding(false)
                  setForm({ direction: 'in', amount: null, date: todayISO(), reason: '', counterparty: '', note: '' })
                } catch (error) {
                  toast.error('Không ghi được', errorMessage(error))
                }
              }}
            >
              Ghi
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3">
          <Field label="Chiều">
            <Segmented size="md" items={[{ id: 'in', label: 'Thu vào quỹ' }, { id: 'out', label: 'Chi từ quỹ' }]} active={form.direction} onChange={(id) => setForm({ ...form, direction: id as 'in' | 'out' })} />
          </Field>
          <div className="grid grid-cols-2 gap-3">
            <Field label="Số tiền" required>
              <NumberInput value={form.amount} onChange={(v) => setForm({ ...form, amount: v })} data-autofocus="" />
            </Field>
            <Field label="Ngày" required>
              <DatePicker value={form.date} onChange={(v) => setForm({ ...form, date: v })} clearable={false} />
            </Field>
          </div>
          <Field label="Lý do" required>
            <Input value={form.reason} onChange={(e) => setForm({ ...form, reason: e.target.value })} placeholder="Nộp tiền vào ngân hàng" />
          </Field>
          <Field label="Đối tượng">
            <Input value={form.counterparty} onChange={(e) => setForm({ ...form, counterparty: e.target.value })} />
          </Field>
          <Field label="Ghi chú">
            <Textarea value={form.note} onChange={(e) => setForm({ ...form, note: e.target.value })} rows={2} />
          </Field>
        </div>
      </Modal>
    </>
  )
}

/* ============================================================================
   Lệnh thu tiền

   Giao → người thu nhận → đếm theo mệnh giá → thủ quỹ nhận lại. Hai lần đếm lệch nhau thì lệnh
   chuyển sang "sai lệch" và chỉ kế toán trưởng gỡ được, bằng cách duyệt số thực nhận hoặc trả về
   cho người thu đếm lại.
   ========================================================================== */

function cashTotal(lines: Record<number, number | null>) {
  return DENOMINATIONS.reduce((sum, d) => sum + d * (lines[d] ?? 0), 0)
}

export function CashCollectionsPage() {
  const auth = useAuth()
  const toast = useToast()
  const fiscal = useFiscal()
  const canSeeAll = auth.canAny([PERM.collectionsReadAll, PERM.collectionsCreate, PERM.collectionsReceive, PERM.collectionsResolve])
  const scope: 'all' | 'mine' = canSeeAll ? 'all' : 'mine'
  const list = useCashCollections(scope)
  const cancel = useCancelCollection()

  const [tab, setTab] = useState('open')
  const [search, setSearch] = useState('')
  const [openId, setOpenId] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  const [cancelling, setCancelling] = useState<CashCollection | null>(null)

  const all = list.data ?? []
  const inTab = (row: CashCollection) => {
    if (tab === 'open') return row.status === 'Assigned' || row.status === 'Accepted'
    if (tab === 'pending') return row.status === 'PendingHandover'
    if (tab === 'variance') return row.status === 'Variance'
    return row.status === 'Completed' || row.status === 'Failed' || row.status === 'Cancelled'
  }
  const rows = useMemo(
    () =>
      all.filter((row) => {
        if (!inTab(row)) return false
        if (search && !matches(`${row.orderNo} ${row.customerName} ${row.driverName} ${row.note}`, search)) return false
        return true
      }),
    [all, tab, search],
  )
  const count = (id: string) => all.filter((row) => (id === 'open' ? row.status === 'Assigned' || row.status === 'Accepted' : id === 'pending' ? row.status === 'PendingHandover' : id === 'variance' ? row.status === 'Variance' : false)).length

  const columns: Column<CashCollection>[] = [
    {
      key: 'code',
      priority: 1,
      header: 'Lệnh',
      width: '8rem',
      cell: (row) => (
        <span className="flex flex-col">
          <span className="font-medium tnum">{row.orderNo}</span>
          <span className="text-xs text-ink-3">{date(row.scheduledDate)}</span>
        </span>
      ),
      sortValue: (r) => r.orderNo,
      total: 'Tổng cộng',
    },
    { key: 'customer', priority: 1, header: 'Khách hàng', cell: (row) => row.customerName, sortValue: (r) => r.customerName, truncate: true },
    { key: 'driver', priority: 1, header: 'Người thu', width: '11rem', cell: (row) => row.driverName || row.driverUsername, sortValue: (r) => r.driverName },
    {
      key: 'assignedAmount',
      priority: 1,
      header: 'Số phải thu',
      align: 'right',
      cell: (row) => <Money value={row.expectedAmount} />,
      sortValue: (r) => r.expectedAmount,
      total: <Money value={rows.reduce((s, r) => s + r.expectedAmount, 0)} zero="zero" />,
    },
    {
      key: 'countedAmount',
      priority: 1,
      header: 'Đã đếm',
      align: 'right',
      cell: (row) =>
        row.receivedAmount != null ? (
          <Money value={row.receivedAmount} strong={row.cashVariance} />
        ) : row.collectedAmount != null ? (
          <Money value={row.collectedAmount} muted />
        ) : null,
      sortValue: (r) => r.receivedAmount ?? r.collectedAmount ?? 0,
    },
    {
      key: 'due',
      priority: 2,
      header: 'Hạn nộp về',
      width: '10rem',
      cell: (row) => <span className={row.overdue ? 'text-danger' : undefined}>{dateTime(row.handoverDueAt)}</span>,
      sortValue: (r) => r.handoverDueAt,
    },
    {
      key: 'stage',
      priority: 1,
      header: 'Trạng thái',
      width: '11rem',
      cell: (row) => <StatusBadge tone={collectionStatus(row.status).tone}>{collectionStatus(row.status).label}</StatusBadge>,
      sortValue: (r) => r.status,
    },
  ]

  const live = all.filter((r) => r.status !== 'Cancelled' && r.status !== 'Failed')
  const period = monthRange(fiscal.period)
  const completedInPeriod = all.filter(
    (r) => r.status === 'Completed' && r.scheduledDate >= period.from && r.scheduledDate <= period.to,
  )

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label="Tiền đang đi đường" value={list.data ? vnd(live.filter((r) => r.status !== 'Completed').reduce((s, r) => s + r.expectedAmount, 0)) : '…'} tone="warn" />
            <Figure label="Chờ thủ quỹ nhận" value={list.data ? count('pending') : '…'} />
            <Figure label="Sai lệch chờ duyệt" value={list.data ? count('variance') : '…'} tone={count('variance') ? 'danger' : undefined} />
            <Figure label={`Đã nộp về trong ${monthLabel(fiscal.period)}`} value={list.data ? vnd(completedInPeriod.reduce((s, r) => s + (r.receivedAmount ?? 0), 0)) : '…'} />
          </FigureStrip>
        }
        tabs={[
          { id: 'open', label: 'Đang chạy', count: count('open') },
          { id: 'pending', label: 'Chờ thủ quỹ nhận', count: count('pending') },
          { id: 'variance', label: 'Sai lệch chờ duyệt', count: count('variance') },
          { id: 'history', label: 'Lịch sử' },
        ]}
        tab={tab}
        onTabChange={setTab}
        actions={
          auth.can(PERM.collectionsCreate) && (
            <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setCreating(true)}>
              Giao lệnh thu
            </Button>
          )
        }
        filters={
          <SearchInput
            size="sm"
            className="w-64"
            placeholder="Khách hàng, lái xe"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            onClear={() => setSearch('')}
          />
        }
        columns={columns}
        rows={rows}
        loading={list.isLoading}
        error={list.error}
        onRefresh={() => list.refetch()}
        onRowClick={(row) => setOpenId(row.id)}
        activeKey={openId}
        emptyTitle="Không có lệnh thu nào trong mục này"
      />

      <CollectionDrawer collectionId={openId} onClose={() => setOpenId(null)} onCancel={(row) => setCancelling(row)} />
      {creating && <CollectionComposer onClose={() => setCreating(false)} />}

      <ConfirmDialog
        open={!!cancelling}
        onClose={() => setCancelling(null)}
        title={`Huỷ lệnh ${cancelling?.orderNo ?? ''}`}
        message="Lệnh đóng lại, người thu không thao tác được nữa."
        confirmLabel="Huỷ lệnh"
        tone="danger"
        requireReason
        reasonLabel="Lý do huỷ"
        busy={cancel.isPending}
        onConfirm={async (reason) => {
          if (!cancelling) return
          try {
            await cancel.mutateAsync({ id: cancelling.id, reason })
            toast.success('Đã huỷ lệnh thu')
            setCancelling(null)
            setOpenId(null)
          } catch (e) {
            toast.error('Không huỷ được lệnh', errorMessage(e))
          }
        }}
      />
    </>
  )
}

function CollectionDrawer({
  collectionId,
  onClose,
  onCancel,
}: {
  collectionId: string | null
  onClose: () => void
  onCancel: (row: CashCollection) => void
}) {
  const toast = useToast()
  const detail = useCashCollection(collectionId)
  const accept = useAcceptCollection()
  const count = useCountCash()
  const resolve = useResolveVariance()
  const fail = useFailCollection()

  const [counting, setCounting] = useState<null | 'collect' | 'receive'>(null)
  const [failing, setFailing] = useState(false)
  const [resolving, setResolving] = useState<null | 'approve_actual' | 'return_to_driver'>(null)

  const order = detail.data?.order
  const status = order ? collectionStatus(order.status) : null

  return (
    <>
      <Drawer
        open={!!collectionId}
        onClose={onClose}
        width="lg"
        title={order ? `Lệnh thu ${order.orderNo}` : 'Lệnh thu tiền'}
        meta={
          order && (
            <>
              <span>{order.customerName}</span>
              <span>{vnd(order.expectedAmount)}</span>
              {status && <StatusBadge tone={status.tone}>{status.label}</StatusBadge>}
            </>
          )
        }
        actions={
          order && (
            <>
              {order.canAccept && (
                <Button
                  size="sm"
                  variant="primary"
                  loading={accept.isPending}
                  onClick={async () => {
                    try {
                      await accept.mutateAsync(order.id)
                      toast.success('Đã nhận lệnh')
                    } catch (e) {
                      toast.error('Không nhận được lệnh', errorMessage(e))
                    }
                  }}
                >
                  Nhận lệnh
                </Button>
              )}
              {order.canCollect && (
                <Button size="sm" variant="primary" onClick={() => setCounting('collect')}>
                  Kiểm đếm và nộp về
                </Button>
              )}
              {order.canReceive && (
                <Button size="sm" variant="primary" onClick={() => setCounting('receive')}>
                  Thủ quỹ kiểm đếm
                </Button>
              )}
              {order.canFail && (
                <Button size="sm" onClick={() => setFailing(true)}>
                  Không thu được
                </Button>
              )}
              {order.canCancel && (
                <Button size="sm" variant="ghost" className="text-danger" onClick={() => onCancel(order)}>
                  Huỷ lệnh
                </Button>
              )}
            </>
          )
        }
      >
        <div className="flex flex-col gap-3 p-3">
          {order?.status === 'Variance' && (
            <InlineAlert tone="danger" title="Số thủ quỹ đếm khác số người thu khai">
              Người thu khai {vnd(order.collectedAmount ?? 0)}, thủ quỹ đếm {vnd(order.receivedAmount ?? 0)}. Kế toán
              trưởng chọn duyệt số thực nhận hoặc trả lệnh về cho người thu đếm lại.
              {order.canResolve && (
                <span className="mt-2 flex flex-wrap gap-2">
                  <Button size="sm" variant="primary" onClick={() => setResolving('approve_actual')}>
                    Duyệt số thực nhận
                  </Button>
                  <Button size="sm" onClick={() => setResolving('return_to_driver')}>
                    Trả về cho người thu
                  </Button>
                </span>
              )}
            </InlineAlert>
          )}
          {order?.overdue && order.status !== 'Completed' && (
            <InlineAlert tone="warn" title="Quá hạn nộp về">
              Hạn bàn giao là {dateTime(order.handoverDueAt)}.
            </InlineAlert>
          )}

          <Panel title="Thông tin lệnh" padded>
            <KeyValue
              rows={[
                ['Khách hàng', order?.customerName],
                ['Điện thoại', order?.customerPhone || null],
                ['Người thu', order?.driverName || order?.driverUsername || null],
                ['Ngày đi thu', order ? date(order.scheduledDate) : null],
                ['Hạn nộp về', order ? dateTime(order.handoverDueAt) : null],
                ['Số phải thu', order ? vnd(order.expectedAmount) : null],
                ['Người thu khai', order?.collectedAmount != null ? vnd(order.collectedAmount) : null],
                ['Thủ quỹ đếm', order?.receivedAmount != null ? vnd(order.receivedAmount) : null],
                ['Ghi chú', order?.note || null],
                ['Lý do không thu được', order?.failureReason || null],
                ['Lý do huỷ', order?.cancelReason || null],
              ]}
            />
          </Panel>

          {(detail.data?.counts.length ?? 0) > 0 && (
            <Panel title="Các lần kiểm đếm" meta={`${detail.data?.counts.length} lần`}>
              <DataTable
                columns={[
                  { key: 'stage', priority: 1, header: 'Người đếm', width: '9rem', cell: (row) => (row.stage === 'driver' ? 'Người thu' : 'Thủ quỹ') },
                  { key: 'revision', priority: 2, header: 'Lần', width: '4rem', align: 'center', cell: (row) => row.revision },
                  { key: 'actor', priority: 2, header: 'Tài khoản', cell: (row) => row.actor },
                  {
                    key: 'lines',
                    priority: 1,
                    header: 'Chi tiết mệnh giá',
                    cell: (row) => row.lines.map((l) => `${l.denomination.toLocaleString('vi-VN')}×${l.quantity}`).join(', '),
                    truncate: true,
                  },
                  { key: 'total', priority: 1, header: 'Tổng', align: 'right', cell: (row) => <Money value={row.total} /> },
                  { key: 'at', priority: 2, header: 'Lúc', width: '10rem', cell: (row) => dateTime(row.confirmedAt) },
                ]}
                rows={detail.data?.counts ?? []}
                getKey={(row) => row.id}
                density="compact"
              />
            </Panel>
          )}

          <Panel title="Dòng thời gian" meta={detail.data ? `${detail.data.events.length} mốc` : undefined}>
            <DataTable
              columns={[
                { key: 'at', priority: 1, header: 'Thời điểm', width: '10rem', cell: (row) => dateTime(row.occurredAt) },
                { key: 'action', priority: 1, header: 'Việc', width: '12rem', cell: (row) => COLLECTION_EVENT_LABELS[row.action] ?? row.action },
                { key: 'actor', priority: 2, header: 'Người thực hiện', cell: (row) => row.actor },
                { key: 'note', priority: 2, header: 'Nội dung', cell: (row) => row.note, truncate: true },
              ]}
              rows={detail.data?.events ?? []}
              getKey={(row) => row.id}
              loading={detail.isLoading}
              density="compact"
            />
          </Panel>
        </div>
      </Drawer>

      {counting && order && (
        <CashCountModal
          stage={counting}
          expected={order.expectedAmount}
          declared={counting === 'receive' ? order.collectedAmount : null}
          busy={count.isPending}
          onClose={() => setCounting(null)}
          onSubmit={async (lines, reason) => {
            await count.mutateAsync({ id: order.id, stage: counting, lines, reason })
            toast.success(counting === 'collect' ? 'Đã nộp bảng kiểm đếm' : 'Đã ghi nhận kiểm đếm của thủ quỹ')
            setCounting(null)
          }}
        />
      )}

      <ConfirmDialog
        open={failing}
        onClose={() => setFailing(false)}
        title="Báo không thu được"
        message="Lệnh đóng lại với trạng thái không thu được."
        confirmLabel="Xác nhận"
        tone="danger"
        requireReason
        reasonLabel="Lý do không thu được"
        busy={fail.isPending}
        onConfirm={async (reason) => {
          if (!order) return
          try {
            await fail.mutateAsync({ id: order.id, reason })
            toast.success('Đã ghi nhận')
            setFailing(false)
          } catch (e) {
            toast.error('Không ghi nhận được', errorMessage(e))
          }
        }}
      />

      <ConfirmDialog
        open={!!resolving}
        onClose={() => setResolving(null)}
        title={resolving === 'approve_actual' ? 'Duyệt số thực nhận' : 'Trả lệnh về cho người thu'}
        message={
          resolving === 'approve_actual'
            ? `Ghi công nợ theo số thủ quỹ đã đếm: ${vnd(order?.receivedAmount ?? 0)}.`
            : 'Lệnh quay về chặng người thu đã nhận, cả hai bên đếm lại từ đầu.'
        }
        confirmLabel={resolving === 'approve_actual' ? 'Duyệt và ghi công nợ' : 'Trả về'}
        tone={resolving === 'approve_actual' ? 'primary' : 'danger'}
        requireReason
        reasonLabel="Lý do xử lý"
        busy={resolve.isPending}
        onConfirm={async (reason) => {
          if (!order || !resolving) return
          try {
            await resolve.mutateAsync({ id: order.id, action: resolving, reason })
            toast.success(resolving === 'approve_actual' ? 'Đã duyệt và ghi công nợ' : 'Đã trả lệnh về')
            setResolving(null)
          } catch (e) {
            toast.error('Không xử lý được sai lệch', errorMessage(e))
          }
        }}
      />
    </>
  )
}

/** Bảng kiểm đếm theo mệnh giá. Tổng cộng tính ngay để người đếm thấy lệch trước khi gửi. */
function CashCountModal({
  stage,
  expected,
  declared,
  busy,
  onClose,
  onSubmit,
}: {
  stage: 'collect' | 'receive'
  expected: number
  declared: number | null
  busy: boolean
  onClose: () => void
  onSubmit: (lines: Array<{ denomination: number; quantity: number }>, reason?: string) => Promise<void>
}) {
  const [lines, setLines] = useState<Record<number, number | null>>({})
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const total = cashTotal(lines)
  const reference = declared ?? expected
  const gap = total - reference

  return (
    <Modal
      open
      onClose={onClose}
      dismissible={false}
      title={stage === 'collect' ? 'Kiểm đếm tiền đã thu' : 'Thủ quỹ kiểm đếm tiền nhận về'}
      description="Nhập số tờ theo từng mệnh giá; tổng tiền tính tự động."
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={busy}>
            Huỷ
          </Button>
          <Button
            size="sm"
            variant="primary"
            disabled={total <= 0}
            loading={busy}
            onClick={async () => {
              setError(null)
              try {
                await onSubmit(
                  DENOMINATIONS.filter((d) => (lines[d] ?? 0) > 0).map((d) => ({ denomination: d, quantity: lines[d] ?? 0 })),
                  reason.trim() || undefined,
                )
              } catch (e) {
                setError(errorMessage(e, 'Không gửi được bảng kiểm đếm.'))
              }
            }}
          >
            Xác nhận đã đếm
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 p-4">
        {error && <InlineAlert tone="danger">{error}</InlineAlert>}
        <div className="grid gap-x-4 gap-y-2 sm:grid-cols-2">
          {DENOMINATIONS.map((d) => (
            <Field key={d} label={`${d.toLocaleString('vi-VN')} ₫`} inline>
              <NumberInput value={lines[d] ?? null} onChange={(v) => setLines((c) => ({ ...c, [d]: v }))} />
            </Field>
          ))}
        </div>
        <DocumentSummary
          rows={[
            { label: stage === 'receive' ? 'Người thu đã khai' : 'Số phải thu', value: vnd(reference) },
            { label: 'Bạn đếm được', value: vnd(total), strong: true },
            { label: 'Chênh lệch', value: gap === 0 ? 'Khớp' : vnd(gap) },
          ]}
        />
        {gap !== 0 && (
          <Field label="Lý do chênh lệch" hint="Ghi rõ để bước xử lý sau đọc được.">
            <Textarea rows={2} value={reason} onChange={(e) => setReason(e.target.value)} />
          </Field>
        )}
      </div>
    </Modal>
  )
}

function CollectionComposer({ onClose }: { onClose: () => void }) {
  const toast = useToast()
  const customers = useCollectionCustomers()
  const drivers = useCollectionDrivers()
  const create = useCreateCollection()

  const [customerId, setCustomerId] = useState('')
  const [driverId, setDriverId] = useState('')
  const [amount, setAmount] = useState<number | null>(null)
  const [scheduledDate, setScheduledDate] = useState(todayISO())
  const [dueDate, setDueDate] = useState(todayISO())
  const [dueTime, setDueTime] = useState('17:00')
  const [note, setNote] = useState('')
  const [touched, setTouched] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const problems = {
    customer: !customerId ? 'Chọn khách hàng' : null,
    driver: !driverId ? 'Chọn người đi thu' : null,
    amount: !amount || amount <= 0 ? 'Nhập số tiền phải thu' : null,
  }
  const valid = !problems.customer && !problems.driver && !problems.amount

  return (
    <Modal
      open
      onClose={onClose}
      dismissible={false}
      title="Giao lệnh thu tiền"
      description="Người đi thu nhận lệnh trên ứng dụng, đếm tiền rồi nộp về cho thủ quỹ."
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={create.isPending}>
            Huỷ
          </Button>
          <Button
            size="sm"
            variant="primary"
            loading={create.isPending}
            onClick={async () => {
              setTouched(true)
              if (!valid) return
              setError(null)
              try {
                const created = await create.mutateAsync({
                  customerId,
                  driverEmployeeId: driverId,
                  expectedAmount: amount ?? 0,
                  scheduledDate,
                  handoverDueAt: `${dueDate}T${dueTime || '17:00'}:00`,
                  note: note.trim(),
                })
                toast.success(`Đã giao lệnh ${created.orderNo}`)
                onClose()
              } catch (e) {
                setError(errorMessage(e, 'Không giao được lệnh thu.'))
              }
            }}
          >
            Giao lệnh
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 p-4">
        {error && <InlineAlert tone="danger">{error}</InlineAlert>}
        <FormGrid cols={2}>
          <Field label="Khách hàng" required error={touched ? problems.customer : null}>
            <Combobox
              value={customerId}
              onChange={setCustomerId}
              loading={customers.isLoading}
              placeholder="Chọn khách hàng"
              options={(customers.data ?? []).map((c) => ({ value: c.id, label: c.name, description: c.phone }))}
            />
          </Field>
          <Field label="Người đi thu" required error={touched ? problems.driver : null}>
            <Combobox
              value={driverId}
              onChange={setDriverId}
              loading={drivers.isLoading}
              placeholder="Chọn lái xe"
              options={(drivers.data ?? []).map((d) => ({ value: d.id, label: d.name, description: [d.employeeCode, d.position].filter(Boolean).join(' · ') }))}
            />
          </Field>
          <Field label="Số tiền phải thu" required error={touched ? problems.amount : null}>
            <NumberInput value={amount} onChange={setAmount} />
          </Field>
          <Field label="Ngày đi thu" required>
            <DatePicker value={scheduledDate} onChange={setScheduledDate} />
          </Field>
          <Field label="Hạn nộp về ngày" required>
            <DatePicker value={dueDate} onChange={setDueDate} />
          </Field>
          <Field label="Hạn nộp về giờ" required>
            <Input type="time" value={dueTime} onChange={(e) => setDueTime(e.target.value)} />
          </Field>
        </FormGrid>
        <Field label="Ghi chú">
          <Textarea rows={2} value={note} onChange={(e) => setNote(e.target.value)} />
        </Field>
      </div>
    </Modal>
  )
}

/* ============================================================================
   Phiếu chi tiền mặt

   Chốt chống gian lận của cả luồng: phiếu nào cần người nhận ký thì chưa quét mã QR là chưa duyệt
   chi được. Mã QR chỉ kế toán nhìn thấy để in ra cho người nhận quét.
   ========================================================================== */

export function PayoutVouchersPage() {
  const auth = useAuth()
  const toast = useToast()
  const fiscal = useFiscal()
  const canCreate = auth.can(PERM.payoutCreate)
  const [tab, setTab] = useState('awaiting-scan')
  const [search, setSearch] = useState('')
  const [categoryId, setCategoryId] = useState('')
  const [openId, setOpenId] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  const [cancelling, setCancelling] = useState<null | { voucher: PayoutVoucher; action: 'reject' | 'cancel' }>(null)

  const vouchers = usePayoutVouchers({ scope: 'all', month: fiscal.period, categoryId: categoryId || undefined })
  const summary = usePayoutSummary(fiscal.period)
  const categories = usePayoutCategories()
  const cancel = usePayoutCancel()

  const TAB_STATUS: Record<string, string[]> = {
    'awaiting-scan': ['AwaitingScan'],
    'awaiting-approval': ['AwaitingApproval', 'Confirmed'],
    approved: ['Approved'],
    paid: ['Paid'],
    rejected: ['Rejected', 'Cancelled'],
  }

  const all = vouchers.data ?? []
  const rows = useMemo(
    () =>
      all.filter((v) => {
        if (!TAB_STATUS[tab]?.includes(v.status)) return false
        if (search && !matches(`${v.voucherNo} ${v.employeeName} ${v.employeeCode} ${v.reason} ${v.categoryName}`, search)) return false
        return true
      }),
    [all, tab, search],
  )
  const count = (id: string) => all.filter((v) => TAB_STATUS[id]?.includes(v.status)).length

  const columns: Column<PayoutVoucher>[] = [
    {
      key: 'code',
      priority: 1,
      header: 'Phiếu chi',
      width: '8rem',
      cell: (row) => <span className="font-medium tnum">{row.voucherNo}</span>,
      sortValue: (r) => r.voucherNo,
      total: 'Tổng cộng',
    },
    { key: 'createdAt', priority: 1, header: 'Ngày lập', width: '9rem', cell: (row) => dateTime(row.createdAt), sortValue: (r) => r.createdAt },
    {
      key: 'recipient',
      priority: 1,
      header: 'Người nhận',
      cell: (row) => (
        <span className="flex flex-col">
          <span>{row.employeeName}</span>
          <span className="text-xs text-ink-3">{row.employeeCode}</span>
        </span>
      ),
      sortValue: (r) => r.employeeName,
    },
    { key: 'category', priority: 1, header: 'Khoản mục', width: '11rem', cell: (row) => row.categoryName, sortValue: (r) => r.categoryName },
    { key: 'reason', priority: 2, header: 'Nội dung chi', cell: (row) => row.reason, truncate: true },
    {
      key: 'amount',
      priority: 1,
      header: 'Số tiền',
      align: 'right',
      cell: (row) => <Money value={row.amount} />,
      sortValue: (r) => r.amount,
      total: <Money value={rows.reduce((s, r) => s + r.amount, 0)} zero="zero" />,
    },
    {
      key: 'stage',
      priority: 1,
      header: 'Trạng thái',
      width: '12rem',
      cell: (row) => <StatusBadge tone={payoutStatus(row.status).tone}>{payoutStatus(row.status).label}</StatusBadge>,
      sortValue: (r) => r.status,
    },
  ]

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label={`Đã chi trong ${monthLabel(fiscal.period)}`} value={summary.data ? vnd(summary.data.totalPaid) : '…'} />
            <Figure label="Đang chờ chi" value={summary.data ? vnd(summary.data.totalPending) : '…'} tone={summary.data?.totalPending ? 'warn' : undefined} />
            <Figure label="Chờ người nhận quét QR" value={vouchers.data ? count('awaiting-scan') : '…'} />
            <Figure label="Chờ duyệt chi" value={vouchers.data ? count('awaiting-approval') : '…'} />
          </FigureStrip>
        }
        tabs={[
          { id: 'awaiting-scan', label: 'Chờ quét QR', count: count('awaiting-scan') },
          { id: 'awaiting-approval', label: 'Chờ duyệt', count: count('awaiting-approval') },
          { id: 'approved', label: 'Đã duyệt', count: count('approved') },
          { id: 'paid', label: 'Đã chi', count: count('paid') },
          { id: 'rejected', label: 'Từ chối / huỷ', count: count('rejected') },
        ]}
        tab={tab}
        onTabChange={setTab}
        actions={
          canCreate && (
            <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setCreating(true)}>
              Lập phiếu chi
            </Button>
          )
        }
        filters={
          <>
            <SearchInput
              size="sm"
              className="w-64"
              placeholder="Người nhận, nội dung chi"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onClear={() => setSearch('')}
            />
            <MonthPicker value={fiscal.period} onChange={fiscal.setPeriod} size="sm" className="w-40" />
            <Select size="sm" className="w-48" value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
              <option value="">Mọi khoản mục</option>
              {(categories.data ?? []).map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </Select>
          </>
        }
        columns={columns}
        rows={rows}
        loading={vouchers.isLoading}
        error={vouchers.error}
        onRefresh={() => vouchers.refetch()}
        onRowClick={(row) => setOpenId(row.id)}
        activeKey={openId}
        defaultSort={{ key: 'createdAt', dir: 'desc' }}
        emptyTitle="Không có phiếu chi nào trong mục này"
        aside={
          summary.data && summary.data.byCategory.length > 0 ? (
            <Panel title="Chi theo khoản mục" meta={monthLabel(fiscal.period)}>
              <DataTable
                columns={[
                  { key: 'name', priority: 1, header: 'Khoản mục', cell: (row) => row.categoryName },
                  { key: 'paid', priority: 1, header: 'Đã chi', align: 'right', cell: (row) => <Money value={row.paidAmount} /> },
                  { key: 'pending', priority: 1, header: 'Chờ chi', align: 'right', cell: (row) => <Money value={row.pendingAmount} muted /> },
                ]}
                rows={summary.data.byCategory}
                getKey={(row) => row.categoryId ?? row.categoryName}
                density="compact"
              />
            </Panel>
          ) : undefined
        }
      />

      <PayoutDrawer
        voucher={all.find((v) => v.id === openId) ?? null}
        onClose={() => setOpenId(null)}
        onCancel={(voucher, action) => setCancelling({ voucher, action })}
      />
      {creating && <PayoutComposer onClose={() => setCreating(false)} />}

      <ConfirmDialog
        open={!!cancelling}
        onClose={() => setCancelling(null)}
        title={cancelling?.action === 'reject' ? 'Từ chối phiếu chi' : `Huỷ phiếu ${cancelling?.voucher.voucherNo ?? ''}`}
        message="Phiếu đóng lại và không chi được nữa."
        confirmLabel={cancelling?.action === 'reject' ? 'Từ chối' : 'Huỷ phiếu'}
        tone="danger"
        requireReason
        reasonLabel="Lý do"
        busy={cancel.isPending}
        onConfirm={async (reason) => {
          if (!cancelling) return
          try {
            await cancel.mutateAsync({ id: cancelling.voucher.id, action: cancelling.action, reason })
            toast.success(cancelling.action === 'reject' ? 'Đã từ chối phiếu' : 'Đã huỷ phiếu')
            setCancelling(null)
            setOpenId(null)
          } catch (e) {
            toast.error('Không thực hiện được', errorMessage(e))
          }
        }}
      />
    </>
  )
}

function PayoutDrawer({
  voucher,
  onClose,
  onCancel,
}: {
  voucher: PayoutVoucher | null
  onClose: () => void
  onCancel: (voucher: PayoutVoucher, action: 'reject' | 'cancel') => void
}) {
  const auth = useAuth()
  const toast = useToast()
  const history = usePayoutHistory(voucher?.id)
  const transition = usePayoutTransition()
  const regenerate = useRegenerateVoucherQr()

  const status = voucher ? payoutStatus(voucher.status) : null
  const waitingScan = voucher?.status === 'AwaitingScan' && voucher.requiresRecipientConfirmation
  const canApprove = auth.can(PERM.payoutApprove) && (voucher?.status === 'Confirmed' || voucher?.status === 'AwaitingApproval')
  const canPay = auth.can(PERM.payoutPay) && voucher?.status === 'Approved'

  return (
    <Drawer
      open={!!voucher}
      onClose={onClose}
      width="lg"
      title={voucher ? `Phiếu chi ${voucher.voucherNo}` : 'Phiếu chi'}
      meta={
        voucher && (
          <>
            <span>{voucher.employeeName}</span>
            <span>{vnd(voucher.amount)}</span>
            {status && <StatusBadge tone={status.tone}>{status.label}</StatusBadge>}
          </>
        )
      }
      actions={
        voucher && (
          <>
            {canApprove && (
              <Button
                size="sm"
                variant="primary"
                loading={transition.isPending}
                onClick={async () => {
                  try {
                    await transition.mutateAsync({ id: voucher.id, action: 'approve' })
                    toast.success('Đã duyệt chi')
                  } catch (e) {
                    toast.error('Không duyệt được', errorMessage(e))
                  }
                }}
              >
                Duyệt chi
              </Button>
            )}
            {canPay && (
              <Button
                size="sm"
                variant="primary"
                loading={transition.isPending}
                onClick={async () => {
                  try {
                    await transition.mutateAsync({ id: voucher.id, action: 'complete' })
                    toast.success('Đã ghi nhận thực chi')
                  } catch (e) {
                    toast.error('Không ghi nhận được', errorMessage(e))
                  }
                }}
              >
                Đã chi tiền
              </Button>
            )}
            {canApprove && (
              <Button size="sm" variant="danger" onClick={() => onCancel(voucher, 'reject')}>
                Từ chối
              </Button>
            )}
            {auth.can(PERM.payoutCreate) && ['AwaitingScan', 'AwaitingApproval', 'Confirmed'].includes(voucher.status) && (
              <Button size="sm" variant="ghost" className="text-danger" onClick={() => onCancel(voucher, 'cancel')}>
                Huỷ phiếu
              </Button>
            )}
          </>
        )
      }
    >
      <div className="flex flex-col gap-3 p-3">
        {waitingScan && (
          <Panel title="Mã ký nhận" meta={voucher?.qrExpiresAt ? `Hiệu lực đến ${dateTime(voucher.qrExpiresAt)}` : undefined} padded>
            {voucher?.qrValue ? (
              <div className="flex flex-col items-center gap-3">
                <QRCodeSVG value={voucher.qrValue} size={196} level="M" marginSize={0} title={`Mã ký nhận phiếu ${voucher.voucherNo}`} />
                <p className="text-center text-xs text-ink-3">
                  Người nhận quét mã này bằng ứng dụng để ký nhận. Chưa quét thì phiếu chưa duyệt chi được.
                </p>
                <Button
                  size="sm"
                  loading={regenerate.isPending}
                  onClick={async () => {
                    try {
                      await regenerate.mutateAsync(voucher.id)
                      toast.success('Đã tạo mã mới')
                    } catch (e) {
                      toast.error('Không tạo được mã', errorMessage(e))
                    }
                  }}
                >
                  Tạo mã mới
                </Button>
              </div>
            ) : (
              <InlineAlert tone="info">Chỉ kế toán lập phiếu mới xem được mã ký nhận.</InlineAlert>
            )}
          </Panel>
        )}

        <Panel title="Thông tin phiếu" padded>
          <KeyValue
            rows={[
              ['Người nhận', voucher?.employeeName],
              ['Mã nhân viên', voucher?.employeeCode || null],
              ['Khoản mục', voucher?.categoryName || null],
              ['Số tiền', voucher ? vnd(voucher.amount) : null],
              ['Nội dung chi', voucher?.reason || null],
              ['Ghi chú', voucher?.note || null],
              ['Nguồn phiếu', voucher?.sourceNo || null],
              ['Người lập', voucher?.createdBy || null],
              ['Ký nhận lúc', voucher?.confirmedAt ? dateTime(voucher.confirmedAt) : null],
              ['Duyệt bởi', voucher?.approvedBy || null],
              ['Thực chi lúc', voucher?.paidAt ? dateTime(voucher.paidAt) : null],
              ['Lý do từ chối', voucher?.rejectReason || null],
              ['Lý do huỷ', voucher?.cancelReason || null],
            ]}
          />
        </Panel>

        <Panel title="Dòng thời gian" meta={history.data ? `${history.data.length} mốc` : undefined}>
          <DataTable
            columns={[
              { key: 'at', priority: 1, header: 'Thời điểm', width: '10rem', cell: (row) => dateTime(row.occurredAt) },
              { key: 'action', priority: 1, header: 'Việc', width: '13rem', cell: (row) => PAYOUT_EVENT_LABELS[row.action] ?? row.action },
              { key: 'actor', priority: 2, header: 'Người thực hiện', cell: (row) => row.actorName || row.actor },
              { key: 'note', priority: 2, header: 'Nội dung', cell: (row) => row.note, truncate: true },
            ]}
            rows={history.data ?? []}
            getKey={(row) => row.id}
            loading={history.isLoading}
            density="compact"
          />
        </Panel>
      </div>
    </Drawer>
  )
}

function PayoutComposer({ onClose }: { onClose: () => void }) {
  const toast = useToast()
  const categories = usePayoutCategories()
  const recipients = usePayoutRecipients()
  const refunds = usePayoutRefundSources()
  const create = useCreatePayoutVoucher()

  const [categoryId, setCategoryId] = useState('')
  const [employeeId, setEmployeeId] = useState('')
  const [amount, setAmount] = useState<number | null>(null)
  const [reason, setReason] = useState('')
  const [note, setNote] = useState('')
  const [refundId, setRefundId] = useState('')
  const [requiresConfirmation, setRequiresConfirmation] = useState(true)
  const [touched, setTouched] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const problems = {
    category: !categoryId ? 'Chọn khoản mục' : null,
    employee: !employeeId ? 'Chọn người nhận' : null,
    amount: !amount || amount <= 0 ? 'Nhập số tiền' : null,
    reason: !reason.trim() ? 'Nhập nội dung chi' : null,
  }
  const valid = Object.values(problems).every((p) => p === null)

  return (
    <Modal
      open
      onClose={onClose}
      dismissible={false}
      size="lg"
      title="Lập phiếu chi tiền mặt"
      description="Phiếu cần người nhận ký thì phải quét mã QR trước khi duyệt chi."
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={create.isPending}>
            Huỷ
          </Button>
          <Button
            size="sm"
            variant="primary"
            loading={create.isPending}
            onClick={async () => {
              setTouched(true)
              if (!valid) return
              setError(null)
              try {
                const created = await create.mutateAsync({
                  categoryId,
                  employeeId,
                  amount: amount ?? 0,
                  reason: reason.trim(),
                  note: note.trim(),
                  sourceKind: refundId ? 'refund' : null,
                  sourceId: refundId || null,
                  requiresRecipientConfirmation: requiresConfirmation,
                })
                toast.success(`Đã lập phiếu ${created.voucherNo}`)
                onClose()
              } catch (e) {
                setError(errorMessage(e, 'Không lập được phiếu chi.'))
              }
            }}
          >
            Lập phiếu
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3 p-4">
        {error && <InlineAlert tone="danger">{error}</InlineAlert>}
        {(refunds.data?.length ?? 0) > 0 && (
          <Field label="Hoàn tiền phạt chờ chi" hint="Chọn một khoản để tự điền người nhận và số tiền.">
            <Select
              value={refundId}
              onChange={(e) => {
                const id = e.target.value
                setRefundId(id)
                const found = refunds.data?.find((r) => r.id === id)
                if (found) {
                  setEmployeeId(found.employeeId)
                  setAmount(found.amount)
                  setReason(`Hoàn tiền phạt ${found.penaltyNo}`.trim())
                }
              }}
            >
              <option value="">Không gắn khoản hoàn nào</option>
              {(refunds.data ?? []).map((r) => (
                <option key={r.id} value={r.id}>
                  {r.refundNo} · {r.employeeName} · {vnd(r.amount)}
                </option>
              ))}
            </Select>
          </Field>
        )}
        <FormGrid cols={2}>
          <Field label="Khoản mục" required error={touched ? problems.category : null}>
            <Select value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
              <option value="">Chọn khoản mục…</option>
              {(categories.data ?? []).map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </Select>
          </Field>
          <Field label="Người nhận" required error={touched ? problems.employee : null}>
            <Combobox
              value={employeeId}
              onChange={setEmployeeId}
              loading={recipients.isLoading}
              placeholder="Chọn người nhận tiền"
              options={(recipients.data ?? []).map((r) => ({
                value: r.id,
                label: r.fullName,
                description: [r.employeeCode, r.departmentName].filter(Boolean).join(' · '),
              }))}
            />
          </Field>
          <Field label="Số tiền" required error={touched ? problems.amount : null}>
            <NumberInput value={amount} onChange={setAmount} />
          </Field>
          <Field label="Nội dung chi" required error={touched ? problems.reason : null}>
            <Input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Ví dụ: mua dầu chạy máy phát" />
          </Field>
        </FormGrid>
        <Field label="Ghi chú">
          <Textarea rows={2} value={note} onChange={(e) => setNote(e.target.value)} />
        </Field>
        <Field hint="Bỏ tích chỉ khi khoản chi không cần chữ ký người nhận, ví dụ chuyển khoản nội bộ.">
          <Checkbox
            label="Người nhận phải quét mã QR ký nhận"
            checked={requiresConfirmation}
            onChange={(e) => setRequiresConfirmation(e.target.checked)}
          />
        </Field>
      </div>
    </Modal>
  )
}
