import { useEffect, useMemo, useState } from 'react'
import { Plus } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { PERM } from '@/lib/permissions'
import { date, vnd } from '@/lib/format'
import { matches } from '@/lib/text'
import { useCustomers, useDebts, useSaveCustomer, type Customer, type DebtSummary } from '@/api/sales'
import {
  Button,
  Field,
  Figure,
  FigureStrip,
  Input,
  Modal,
  Money,
  SearchInput,
  StatusBadge,
  Textarea,
  useToast,
} from '@/ui'
import { ModuleScreen, errorMessage } from '../_shared'
import { CustomerDrawer } from './customer-drawer'

interface CustomerRow {
  customer: Customer
  debt?: DebtSummary
}

/** Danh mục khách hàng: danh sách, dư nợ của từng khách, ngăn kéo hồ sơ. */
export function CustomersPage() {
  const auth = useAuth()
  const customers = useCustomers()
  const debts = useDebts()
  const [tab, setTab] = useState('all')
  const [search, setSearch] = useState('')
  const [openId, setOpenId] = useState<string | null>(null)
  const [editing, setEditing] = useState<Customer | null | 'new'>(null)

  const rows = useMemo<CustomerRow[]>(() => {
    const byId = new Map((debts.data?.customers ?? []).map((d) => [d.customer.id, d]))
    return (customers.data ?? [])
      .map((customer) => ({ customer, debt: byId.get(customer.id) }))
      .filter((row) => {
        const balance = row.debt?.balance ?? 0
        if (tab === 'owing' && balance <= 0) return false
        if (tab === 'clear' && balance > 0) return false
        if (search && !matches(`${row.customer.name} ${row.customer.phone} ${row.customer.taxCode}`, search)) return false
        return true
      })
  }, [customers.data, debts.data, tab, search])

  const totalReceivable = rows.reduce((sum, r) => sum + Math.max(r.debt?.balance ?? 0, 0), 0)

  return (
    <>
      <ModuleScreen
        figures={
          <FigureStrip>
            <Figure label="Khách đang giao dịch" value={customers.data?.length ?? '…'} />
            <Figure label="Khách còn nợ" value={debts.data?.debtorCount ?? '…'} />
            <Figure label="Tổng phải thu" value={debts.data ? vnd(debts.data.totalReceivable) : '…'} to="/cong-no" />
          </FigureStrip>
        }
        tabs={[
          { id: 'all', label: 'Tất cả' },
          { id: 'owing', label: 'Còn nợ', count: debts.data?.debtorCount },
          { id: 'clear', label: 'Không nợ' },
        ]}
        tab={tab}
        onTabChange={setTab}
        filters={
          <SearchInput size="sm" className="w-64" placeholder="Tên, điện thoại, mã số thuế" value={search} onChange={(e) => setSearch(e.target.value)} onClear={() => setSearch('')} />
        }
        actions={
          auth.can(PERM.vouchersCreate) && (
            <Button variant="primary" size="sm" icon={<Plus className="size-3.5" strokeWidth={2} />} onClick={() => setEditing('new')}>
              Thêm khách hàng
            </Button>
          )
        }
        columns={[
          { key: 'name', priority: 1, header: 'Khách hàng', cell: (row) => <span className="font-medium">{row.customer.name}</span>, sortValue: (r) => r.customer.name },
          { key: 'taxCode', priority: 3, header: 'Mã số thuế', cell: (row) => <span className="tnum">{row.customer.taxCode}</span> },
          { key: 'phone', priority: 2, header: 'Điện thoại', cell: (row) => <span className="tnum">{row.customer.phone}</span> },
          { key: 'address', priority: 3, header: 'Địa chỉ', cell: (row) => row.customer.address, truncate: true, hidden: true },
          { key: 'invoices', priority: 3, header: 'Số phiếu', align: 'right', cell: (row) => row.debt?.invoiceCount ?? 0, sortValue: (r) => r.debt?.invoiceCount ?? 0 },
          { key: 'sales', priority: 3, header: 'Đã bán', align: 'right', cell: (row) => <Money value={row.debt?.salesTotal} />, sortValue: (r) => r.debt?.salesTotal ?? 0, hidden: true },
          {
            key: 'balance', priority: 1,
            header: 'Dư nợ',
            align: 'right',
            cell: (row) => <Money value={row.debt?.balance} strong />,
            sortValue: (r) => r.debt?.balance ?? 0,
            total: <Money value={totalReceivable} zero="zero" />,
          },
          { key: 'last', priority: 3, header: 'Hoạt động gần nhất', cell: (row) => date(row.debt?.lastActivityDate), sortValue: (r) => r.debt?.lastActivityDate ?? '' },
          {
            key: 'status', priority: 1,
            header: 'Trạng thái',
            cell: (row) =>
              !row.customer.isActive ? (
                <StatusBadge>Ngừng giao dịch</StatusBadge>
              ) : (row.debt?.balance ?? 0) > 0 ? (
                <StatusBadge tone="warn">Còn nợ</StatusBadge>
              ) : (
                <StatusBadge tone="ok">Không nợ</StatusBadge>
              ),
          },
        ]}
        rows={rows}
        getKey={(row) => row.customer.id}
        loading={customers.isLoading}
        error={customers.error}
        onRefresh={() => {
          void customers.refetch()
          void debts.refetch()
        }}
        onRowClick={(row) => setOpenId(row.customer.id)}
        activeKey={openId}
        defaultSort={{ key: 'balance', dir: 'desc' }}
        emptyTitle="Chưa có khách hàng nào khớp"
      />

      <CustomerDrawer customerId={openId} onClose={() => setOpenId(null)} onEdit={(c) => setEditing(c)} />
      <CustomerModal customer={editing} onClose={() => setEditing(null)} />
    </>
  )
}

/** Thêm khách hàng mới hoặc sửa hồ sơ một khách đang có. */
function CustomerModal({ customer, onClose }: { customer: Customer | null | 'new'; onClose: () => void }) {
  const toast = useToast()
  const save = useSaveCustomer()
  const open = customer !== null
  const editing = customer && customer !== 'new' ? customer : null
  const [form, setForm] = useState({ name: '', taxCode: '', phone: '', address: '' })
  const [touched, setTouched] = useState(false)
  useEffect(() => {
    if (open) {
      setForm({
        name: editing?.name ?? '',
        taxCode: editing?.taxCode ?? '',
        phone: editing?.phone ?? '',
        address: editing?.address ?? '',
      })
      setTouched(false)
    }
  }, [open, editing])

  const nameError = touched && !form.name.trim() ? 'Nhập tên khách hàng' : null

  const submit = async () => {
    setTouched(true)
    if (!form.name.trim()) return
    try {
      await save.mutateAsync({ id: editing?.id, body: { ...form, name: form.name.trim() } })
      toast.success(editing ? 'Đã cập nhật khách hàng' : 'Đã thêm khách hàng')
      onClose()
    } catch (error) {
      toast.error('Không lưu được', errorMessage(error))
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? `Sửa khách hàng ${editing.name}` : 'Thêm khách hàng'}
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
        <Field label="Tên khách hàng" required error={nameError}>
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
      </div>
    </Modal>
  )
}
