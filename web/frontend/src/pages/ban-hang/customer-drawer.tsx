import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '@/auth/AuthProvider'
import { PERM } from '@/lib/permissions'
import { date, todayISO, vnd } from '@/lib/format'
import {
  debtStatementUrl,
  documentStatus,
  useAddCustomerAlias,
  useCustomerAliases,
  useCustomerReport,
  useDebtDetail,
  useDeleteCustomerAlias,
  useRecordDebtPayment,
  type Customer,
  type DebtPeriod,
} from '@/api/sales'
import { Trash2 } from 'lucide-react'
import {
  Button,
  buttonClass,
  DataTable,
  DatePicker,
  Drawer,
  Field,
  Figure,
  FigureStrip,
  IconButton,
  Input,
  KeyValue,
  Modal,
  Money,
  NumberInput,
  Panel,
  StatusBadge,
  Tabs,
  Textarea,
  useToast,
} from '@/ui'
import { errorMessage } from '../_shared'

/** Ngăn kéo chi tiết khách hàng: tổng quan, chứng từ, công nợ, ghi nhận thanh toán. */
export function CustomerDrawer({
  customerId,
  onClose,
  onEdit,
  initialTab = 'overview',
  period,
}: {
  customerId: string | null
  onClose: () => void
  onEdit?: (customer: Customer) => void
  initialTab?: string
  /** Kỳ đang xem ở màn hình gọi ngăn kéo. Bỏ trống là toàn bộ lịch sử. */
  period?: DebtPeriod
}) {
  const auth = useAuth()
  const toast = useToast()
  const [tab, setTab] = useState(initialTab)
  const [paying, setPaying] = useState(false)
  const report = useCustomerReport(customerId ?? undefined)
  const debt = useDebtDetail(customerId ?? undefined, period)
  const payment = useRecordDebtPayment()
  const navigate = useNavigate()

  useEffect(() => {
    if (customerId) setTab(initialTab)
  }, [customerId, initialTab])

  const customer = report.data?.customer ?? debt.data?.customer
  const summary = debt.data?.summary

  return (
    <>
      <Drawer
        open={!!customerId}
        onClose={onClose}
        width="lg"
        title={customer?.name ?? 'Khách hàng'}
        meta={
          customer && (
            <>
              {customer.phone && <span className="tnum">{customer.phone}</span>}
              {customer.taxCode && <span className="tnum">MST {customer.taxCode}</span>}
            </>
          )
        }
        actions={
          <>
            {customerId && (
              <a className={buttonClass('default', 'sm')} href={debtStatementUrl(customerId, period)}>
                Xuất PDF
              </a>
            )}
            {auth.can(PERM.vouchersCreate) && summary && (
              <Button size="sm" onClick={() => setPaying(true)}>
                Ghi nhận thanh toán
              </Button>
            )}
            {onEdit && customer && auth.can(PERM.vouchersUpdate) && (
              <Button size="sm" onClick={() => onEdit(customer)}>
                Sửa
              </Button>
            )}
          </>
        }
      >
        <div className="border-b border-line bg-panel">
          <FigureStrip className="rounded-none border-0">
            <Figure label="Đầu kỳ" value={summary ? vnd(summary.carriedBalance) : '…'} />
            <Figure label="Đã bán" value={summary ? vnd(summary.salesTotal) : '…'} />
            <Figure label="Đã thu" value={summary ? vnd(summary.collectedTotal) : '…'} />
            <Figure label="Còn nợ" value={summary ? vnd(summary.balance) : '…'} tone={summary && summary.balance > 0 ? 'warn' : undefined} />
          </FigureStrip>
          <Tabs
            items={[
              { id: 'overview', label: 'Tổng quan' },
              { id: 'documents', label: 'Chứng từ', count: report.data?.documentCount },
              { id: 'debt', label: 'Sổ công nợ', count: debt.data?.transactions.length },
              { id: 'aliases', label: 'Bí danh' },
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
                  ['Tên khách hàng', customer?.name],
                  ['Mã số thuế', customer?.taxCode || null],
                  ['Điện thoại', customer?.phone || null],
                  ['Địa chỉ', customer?.address || null],
                  ['Dư nợ đầu kỳ', summary ? <Money key="o" value={summary.openingBalance} zero="zero" /> : null],
                  ['Ngày đầu kỳ', summary?.openingDate ? date(summary.openingDate) : null],
                  ['Ghi chú đầu kỳ', summary?.openingNote || null],
                  ['Hàng trả lại', summary ? <Money key="r" value={summary.returnsTotal} zero="zero" /> : null],
                  ['Số phiếu bán', summary?.invoiceCount ?? null],
                  ['Hoạt động gần nhất', summary?.lastActivityDate ? date(summary.lastActivityDate) : null],
                ]}
              />
            </Panel>
          )}

          {tab === 'documents' && (
            <Panel>
              <DataTable
                columns={[
                  { key: 'voucherNo', header: 'Số phiếu', cell: (row) => <span className="font-medium">{row.voucherNo}</span> },
                  { key: 'type', header: 'Loại', cell: (row) => row.documentType, truncate: true },
                  { key: 'date', header: 'Ngày', cell: (row) => date(row.date) },
                  { key: 'total', header: 'Số tiền', align: 'right', cell: (row) => <Money value={row.total} muted={!!row.cancelledAt} /> },
                  { key: 'status', header: 'Trạng thái', cell: (row) => <StatusBadge tone={documentStatus(row).tone}>{documentStatus(row).label}</StatusBadge> },
                ]}
                rows={report.data?.documents ?? []}
                getKey={(row) => row.id}
                loading={report.isLoading}
                onRowClick={(row) => {
                  onClose()
                  navigate(`/ban-hang/${row.id}`)
                }}
                emptyTitle="Khách hàng này chưa có chứng từ"
                density="compact"
              />
            </Panel>
          )}

          {tab === 'aliases' && customerId && <CustomerAliasPanel customerId={customerId} />}

          {tab === 'debt' && (
            <Panel>
              <DataTable
                columns={[
                  { key: 'date', header: 'Ngày', cell: (row) => date(row.date) },
                  { key: 'ref', header: 'Chứng từ', cell: (row) => <span className="tnum">{row.reference}</span> },
                  { key: 'desc', header: 'Diễn giải', cell: (row) => row.description, truncate: true },
                  { key: 'debit', header: 'Tăng nợ', align: 'right', cell: (row) => <Money value={row.debit} zero="blank" muted={row.cancelled} /> },
                  { key: 'credit', header: 'Đã thu', align: 'right', cell: (row) => <Money value={row.credit} zero="blank" muted={row.cancelled} /> },
                  { key: 'balance', header: 'Còn nợ', align: 'right', cell: (row) => <Money value={row.runningBalance} zero="zero" strong /> },
                ]}
                rows={debt.data?.transactions ?? []}
                getKey={(row) => row.id}
                loading={debt.isLoading}
                rowClassName={(row) => (row.cancelled ? 'is-muted line-through' : undefined)}
                emptyTitle="Chưa có phát sinh công nợ"
                density="compact"
              />
            </Panel>
          )}
        </div>
      </Drawer>

      <PaymentModal
        open={paying}
        onClose={() => setPaying(false)}
        customer={customer}
        balance={summary?.balance ?? 0}
        balanceAsOf={period?.to && period.to < todayISO() ? period.to : undefined}
        busy={payment.isPending}
        onSubmit={async (amount, when, note) => {
          if (!customerId) return
          try {
            await payment.mutateAsync({ customerId, amount, date: when, note })
            toast.success('Đã ghi nhận thanh toán')
            setPaying(false)
          } catch (error) {
            toast.error('Không ghi nhận được', errorMessage(error))
          }
        }}
      />
    </>
  )
}


/**
 * Bí danh của một khách hàng.
 *
 * Cùng một khách nhưng mỗi người gọi một kiểu: "Công ty Hoà Phát" trên hoá đơn, "anh Ba - Hoà Phát"
 * ngoài kho. Đặt bí danh rồi thì lập phiếu gõ kiểu nào cũng về đúng một hồ sơ, thay vì đẻ ra khách
 * thứ hai và chẻ đôi sổ công nợ.
 */
function CustomerAliasPanel({ customerId }: { customerId: string }) {
  const toast = useToast()
  const aliases = useCustomerAliases(customerId)
  const add = useAddCustomerAlias()
  const remove = useDeleteCustomerAlias()
  const [draft, setDraft] = useState('')

  const submit = async () => {
    const alias = draft.trim()
    if (!alias) return
    try {
      await add.mutateAsync({ customerId, alias })
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
            placeholder="Ví dụ: anh Ba - Hoà Phát"
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
                    .mutateAsync({ customerId, aliasId: row.id })
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
        emptyDescription="Đặt bí danh để lập phiếu gõ kiểu nào cũng về đúng khách hàng này."
        density="compact"
      />
    </Panel>
  )
}

function PaymentModal({
  open,
  onClose,
  customer,
  balance,
  balanceAsOf,
  busy,
  onSubmit,
}: {
  open: boolean
  onClose: () => void
  customer?: Customer
  balance: number
  /**
   * Ngày cuối của kỳ đang xem, chỉ truyền khi kỳ đó đã qua. Số dư trên màn hình lúc ấy là số dư
   * CUỐI KỲ chứ không phải nợ hôm nay, nên phải nói rõ — nếu không người thu tiền sẽ bấm "thu đủ"
   * theo một con số cũ.
   */
  balanceAsOf?: string
  busy: boolean
  onSubmit: (amount: number, date: string, note: string) => void
}) {
  const [amount, setAmount] = useState<number | null>(null)
  const [when, setWhen] = useState(todayISO())
  const [note, setNote] = useState('')
  useEffect(() => {
    if (open) {
      setAmount(null)
      setWhen(todayISO())
      setNote('')
    }
  }, [open])
  const valid = !!amount && amount > 0 && !!when
  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Ghi nhận khách thanh toán"
      description={
        customer
          ? balanceAsOf
            ? `${customer.name} · còn nợ đến hết ${date(balanceAsOf)}: ${vnd(balance)}`
            : `${customer.name} · còn nợ ${vnd(balance)}`
          : undefined
      }
      size="sm"
      footer={
        <>
          <Button size="sm" onClick={onClose} disabled={busy}>
            Huỷ
          </Button>
          <Button size="sm" variant="primary" loading={busy} disabled={!valid} onClick={() => amount && onSubmit(amount, when, note.trim())}>
            Ghi nhận
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        <Field label="Số tiền" required>
          <NumberInput value={amount} onChange={setAmount} autoFocus data-autofocus="" />
        </Field>
        <Field label="Ngày thanh toán" required>
          <DatePicker value={when} onChange={setWhen} clearable={false} />
        </Field>
        <Field label="Ghi chú">
          <Textarea value={note} onChange={(e) => setNote(e.target.value)} rows={2} />
        </Field>
        {balance > 0 && (
          <Button size="sm" variant="link" onClick={() => setAmount(balance)} className="self-start">
            {balanceAsOf ? `Thu đủ số cuối kỳ ${vnd(balance)}` : `Thu đủ ${vnd(balance)}`}
          </Button>
        )}
      </div>
    </Modal>
  )
}
