import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/http'
import { refreshTopics } from '@/lib/realtime'
import type { Tone } from '@/ui'

/*
 * Bán hàng, khách hàng, công nợ phải thu, danh mục hàng hoá.
 * Khoá truy vấn: phần tử đầu là chủ đề realtime ('sales', 'debts', 'cash', 'catalog'), máy chủ báo
 * đổi thì nạp lại đúng nhóm đó.
 */

export interface DocumentListItem {
  id: string
  voucherNo: string
  date: string
  documentType: string
  customerName: string
  content: string
  total: number
  createdBy: string
  issuedAt: string | null
  cancelledAt: string | null
  cancelledBy: string
  cancelReason: string
  deliveryMode: string
  deliveryDriverName: string
  deliveryTaskStatus: string
  deliveryReturnedAt: string | null
}

export interface DocumentLine {
  lineContent: string
  spec: string
  quantity: number
  unitPrice: number
  note: string
  productId?: string | null
  amount?: number
  /** Nguồn hàng: cuộn này là hàng nhập của nhà cung cấp nào. Nội bộ, không in ra phiếu. */
  supplierId?: string | null
  supplierName?: string
}

/** Một nguồn hàng của mặt hàng: đã nhập của ai, còn lại bao nhiêu, giá nhập gần nhất. */
export interface ProductSource {
  supplierId: string
  supplierName: string
  bought: number
  sold: number
  remaining: number
  lastCost: number | null
  lastBoughtDate: string | null
}

export interface DocumentDetail {
  id: string
  voucherNo: string
  date: string
  customerName: string
  content: string
  note: string
  lines: DocumentLine[]
  issuedAt: string | null
  cancelledAt: string | null
  cancelledBy: string
  cancelReason: string
}

export interface SaveDocumentRequest {
  voucherNo: string
  date: string
  customerName: string
  content: string
  note: string
  lines: DocumentLine[]
  documentType?: string | null
}

export type DocStatusId = 'draft' | 'issued' | 'delivering' | 'submitted' | 'returned' | 'cancelled'

export interface DocStatus {
  id: DocStatusId
  label: string
  tone: Tone
}

/** Trạng thái hiển thị của phiếu bán, suy từ các mốc thời gian và chặng giao hàng. */
export function documentStatus(
  row: Pick<DocumentListItem, 'issuedAt' | 'cancelledAt' | 'deliveryTaskStatus' | 'deliveryReturnedAt'>,
): DocStatus {
  if (row.cancelledAt) return { id: 'cancelled', label: 'Đã huỷ', tone: 'danger' }
  if (row.deliveryReturnedAt) return { id: 'returned', label: 'Đã về kho', tone: 'ok' }
  const task = (row.deliveryTaskStatus || '').toLowerCase()
  if (task === 'submitted') return { id: 'submitted', label: 'Đã nộp phiếu', tone: 'info' }
  if (task === 'assigned' || task === 'accepted' || task === 'in_progress' || task === 'inprogress')
    return { id: 'delivering', label: 'Đang giao', tone: 'warn' }
  if (row.issuedAt) return { id: 'issued', label: 'Đã phát hành', tone: 'brand' }
  return { id: 'draft', label: 'Nháp', tone: 'neutral' }
}

export const DOC_STATUS_FILTERS: { id: DocStatusId | 'all'; label: string }[] = [
  { id: 'all', label: 'Tất cả' },
  { id: 'draft', label: 'Nháp' },
  { id: 'issued', label: 'Đã phát hành' },
  { id: 'delivering', label: 'Đang giao' },
  { id: 'returned', label: 'Đã về kho' },
  { id: 'cancelled', label: 'Đã huỷ' },
]

const DOCS = ['sales', 'documents'] as const

export function useSalesDocuments() {
  return useQuery({
    queryKey: [...DOCS, 'sales'],
    queryFn: () => api.get<DocumentListItem[]>('/documents'),
  })
}

export function useSalesDocument(id: string | undefined) {
  return useQuery({
    queryKey: [...DOCS, 'sales', id],
    queryFn: () => api.get<DocumentDetail>(`/documents/${id}`),
    enabled: !!id,
  })
}

export function useSaveSalesDocument() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: SaveDocumentRequest }) =>
      id
        ? api.put<{ id: string }>(`/documents/${id}`, body)
        : api.post<{ id: string }>('/documents', body),
    onSuccess: () => refreshTopics(queryClient, 'sales', 'debts'),
  })
}

export function useCancelSalesDocument() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      api.put<void>(`/documents/${id}/cancel`, { reason }),
    onSuccess: () => refreshTopics(queryClient, 'sales', 'debts'),
  })
}

/** Phiếu thu / chi tiền mặt dùng chung cấu trúc với phiếu bán nhưng ở sổ riêng. */
export function useCashVouchers() {
  return useQuery({
    queryKey: [...DOCS, 'cash'],
    queryFn: () => api.get<DocumentListItem[]>('/cash-vouchers'),
  })
}

export function useSaveCashVoucher() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: SaveDocumentRequest }) =>
      id
        ? api.put<{ id: string }>(`/cash-vouchers/${id}`, body)
        : api.post<{ id: string }>('/cash-vouchers', body),
    onSuccess: () => refreshTopics(queryClient, 'cash', 'debts'),
  })
}

export function useIssueCashVoucher() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.put<void>(`/cash-vouchers/${id}/issued`),
    onSuccess: () => refreshTopics(queryClient, 'cash', 'debts'),
  })
}

export function useCancelCashVoucher() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      api.put<void>(`/cash-vouchers/${id}/cancel`, { reason }),
    onSuccess: () => refreshTopics(queryClient, 'cash', 'debts'),
  })
}

// ----- Khách hàng -----

export interface Customer {
  id: string
  name: string
  taxCode: string
  phone: string
  address: string
  isActive: boolean
  /** Các tên gọi khác cùng trỏ về khách này. Chỉ danh sách khách hàng trả về, nơi khác là mảng rỗng. */
  aliases?: string[]
}

export interface CustomerAlias {
  id: number
  alias: string
  createdBy: string
  createdAt: string
}

export interface SaveCustomerRequest {
  name: string
  taxCode: string
  phone: string
  address: string
}

export interface CustomerReport {
  customer: Customer
  documentCount: number
  total: number
  receiptTotal: number
  paymentTotal: number
  salesTotal: number
  documents: DocumentListItem[]
}

const CUSTOMERS = ['debts', 'customers'] as const

export function useCustomers() {
  return useQuery({ queryKey: [...CUSTOMERS], queryFn: () => api.get<Customer[]>('/customers') })
}

export function useCustomerReport(id: string | undefined) {
  return useQuery({
    queryKey: [...CUSTOMERS, id, 'report'],
    queryFn: () => api.get<CustomerReport>(`/customers/${id}/report`),
    enabled: !!id,
  })
}

export function useCustomerAliases(customerId: string | null | undefined) {
  return useQuery({
    queryKey: [...CUSTOMERS, customerId, 'aliases'],
    queryFn: () => api.get<{ items: CustomerAlias[] }>(`/customers/${customerId}/aliases`),
    enabled: !!customerId,
  })
}

export function useAddCustomerAlias() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ customerId, alias }: { customerId: string; alias: string }) =>
      api.post<{ alias: string }>(`/customers/${customerId}/aliases`, { alias }),
    onSuccess: () => refreshTopics(queryClient, 'debts'),
  })
}

export function useDeleteCustomerAlias() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ customerId, aliasId }: { customerId: string; aliasId: number }) =>
      api.del<void>(`/customers/${customerId}/aliases/${aliasId}`),
    onSuccess: () => refreshTopics(queryClient, 'debts'),
  })
}

export function useSaveCustomer() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: SaveCustomerRequest }) =>
      id ? api.put<Customer>(`/customers/${id}`, body) : api.post<Customer>('/customers', body),
    onSuccess: () => refreshTopics(queryClient, 'debts'),
  })
}

export function useDeleteCustomer() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.del<void>(`/customers/${id}`),
    onSuccess: () => refreshTopics(queryClient, 'debts'),
  })
}

// ----- Công nợ phải thu -----

/** Kỳ xem công nợ. Bỏ trống cả hai đầu là xem toàn bộ lịch sử. */
export interface DebtPeriod {
  from?: string
  to?: string
}

export interface DebtSummary {
  customer: Customer
  openingBalance: number
  openingDate: string | null
  openingNote: string
  /** Chỉ tính phát sinh trong kỳ đang xem, giống returnsTotal và collectedTotal. */
  salesTotal: number
  returnsTotal: number
  collectedTotal: number
  /** Dư cuối kỳ = carriedBalance + đã bán − trả lại − đã thu. */
  balance: number
  invoiceCount: number
  lastActivityDate: string | null
  /** Dư nợ mang sang: số dư luỹ kế tính đến ngay trước ngày đầu kỳ. */
  carriedBalance: number
}

export interface DebtOverview {
  totalOpeningBalance: number
  totalSales: number
  totalReturns: number
  totalCollected: number
  totalReceivable: number
  debtorCount: number
  customers: DebtSummary[]
  totalCarried: number
  from: string | null
  to: string | null
}

export interface DebtTransaction {
  id: string
  date: string
  reference: string
  kind: string
  description: string
  debit: number
  credit: number
  runningBalance: number
  cancelled: boolean
}

export interface DebtDetail {
  customer: Customer
  summary: DebtSummary
  transactions: DebtTransaction[]
  from: string | null
  to: string | null
}

const DEBTS = ['debts', 'debts'] as const

// Kỳ nằm trong khoá truy vấn: đổi tháng là một bộ nhớ đệm khác, không phải cùng một bảng vẽ lại.
const periodKey = (period?: DebtPeriod) => [period?.from ?? '', period?.to ?? '']

export function useDebts(period?: DebtPeriod, enabled = true) {
  return useQuery({
    queryKey: [...DEBTS, ...periodKey(period)],
    queryFn: () => api.get<DebtOverview>('/debts', { query: { from: period?.from, to: period?.to } }),
    enabled,
  })
}

export function useDebtDetail(customerId: string | undefined, period?: DebtPeriod) {
  return useQuery({
    queryKey: [...DEBTS, customerId, ...periodKey(period)],
    queryFn: () =>
      api.get<DebtDetail>(`/debts/${customerId}`, { query: { from: period?.from, to: period?.to } }),
    enabled: !!customerId,
  })
}

/** Đường tải sổ chi tiết công nợ dạng PDF; mở thẳng bằng thẻ neo để trình duyệt tự lưu tệp. */
export function debtStatementUrl(customerId: string, period?: DebtPeriod, details = true) {
  const params = new URLSearchParams()
  if (period?.from) params.set('from', period.from)
  if (period?.to) params.set('to', period.to)
  if (!details) params.set('details', 'false')
  const qs = params.toString()
  return `/api/debts/${customerId}/statement.pdf${qs ? `?${qs}` : ''}`
}

export function useRecordDebtPayment() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ customerId, amount, date, note }: { customerId: string; amount: number; date: string; note?: string }) =>
      api.post<void>(`/debts/${customerId}/payments`, { amount, date, note }),
    onSuccess: () => refreshTopics(queryClient, 'debts'),
  })
}

export function useSetOpeningBalance() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ customerId, amount, asOfDate, note }: { customerId: string; amount: number; asOfDate: string; note?: string }) =>
      api.put<void>(`/debts/${customerId}/opening-balance`, { amount, asOfDate, note }),
    onSuccess: () => refreshTopics(queryClient, 'debts'),
  })
}

// ----- Danh mục hàng hoá -----

export interface Product {
  id: string
  code: string
  name: string
  spec: string
  unit: string
  note: string
  isActive: boolean
  timesUsed: number
  soldQuantity: number
  soldAmount: number
  lastPrice: number | null
  lastSoldDate: string | null
  boughtQuantity: number
  boughtAmount: number
  lastCost: number | null
  lastBoughtDate: string | null
}

/**
 * Các nhà cung cấp đã từng nhập mặt hàng này, kèm số còn lại của từng nguồn.
 *
 * Chủ đề 'sales' vì số còn lại tụt xuống theo từng phiếu bán; vế nhập thưa hơn nhiều nên staleTime 0
 * là đủ để không bao giờ chọn nguồn theo một con số cũ.
 */
export function useProductSources(productId: string | null | undefined) {
  return useQuery({
    queryKey: ['sales', 'product-sources', productId],
    queryFn: () => api.get<{ items: ProductSource[] }>(`/products/${productId}/sources`),
    enabled: !!productId,
    staleTime: 0,
  })
}

export function useProducts(includeInactive = false) {
  return useQuery({
    queryKey: ['catalog', 'products', includeInactive],
    queryFn: () => api.get<{ items: Product[] }>('/products', { query: { includeInactive } }),
  })
}
