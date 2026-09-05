import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/http'
import type { Tone } from '@/ui'

/* ============================================================================
   Lệnh thu tiền và phiếu chi tiền mặt — hai luồng tiền mặt có kiểm đếm và ký nhận.
   ========================================================================== */

/* ── Lệnh thu tiền ──────────────────────────────────────────────────────── */

/** Mệnh giá tiền Việt máy chủ chấp nhận khi kiểm đếm. */
export const DENOMINATIONS = [500_000, 200_000, 100_000, 50_000, 20_000, 10_000, 5_000, 2_000, 1_000, 500, 200, 100]

export interface CashCollection {
  id: string
  orderNo: string
  customerId: string
  customerName: string
  customerPhone: string
  driverEmployeeId: string
  driverUsername: string
  driverName: string
  expectedAmount: number
  scheduledDate: string
  handoverDueAt: string
  note: string
  status: string
  createdBy: string
  createdAt: string
  acceptedBy: string
  acceptedAt: string | null
  collectedBy: string
  collectedAt: string | null
  collectedAmount: number | null
  failedBy: string
  failedAt: string | null
  failureReason: string
  receivedBy: string
  receivedAt: string | null
  receivedAmount: number | null
  paymentId: string | null
  cancelledBy: string
  cancelledAt: string | null
  cancelReason: string
  driverCash: Record<string, number>
  accountantCash: Record<string, number>
  overdue: boolean
  /** Lái xe khai khác số phải thu. */
  expectedVariance: boolean
  /** Thủ quỹ đếm ra khác số lái xe khai — đây là sai lệch phải xử lý. */
  cashVariance: boolean
  mine: boolean
  canAccept: boolean
  canCollect: boolean
  canFail: boolean
  canReceive: boolean
  canCancel: boolean
  canResolve: boolean
}

export interface CashCountSession {
  id: string
  stage: 'driver' | 'accountant'
  revision: number
  actor: string
  total: number
  confirmedAt: string
  lines: Array<{ denomination: number; quantity: number; subtotal: number }>
}

export interface CashCollectionEvent {
  id: string
  action: string
  actor: string
  beforeStatus: string | null
  afterStatus: string | null
  note: string
  occurredAt: string
}

export interface CashCollectionDetail {
  order: CashCollection
  counts: CashCountSession[]
  events: CashCollectionEvent[]
}

export interface CollectionCustomer {
  id: string
  name: string
  phone: string
}

export interface CollectionDriver {
  id: string
  username: string
  name: string
  employeeCode: string
  position: string
}

export function collectionStatus(status: string): { label: string; tone: Tone } {
  switch (status) {
    case 'Assigned':
      return { label: 'Đã giao lệnh', tone: 'neutral' }
    case 'Accepted':
      return { label: 'Người thu đã nhận', tone: 'brand' }
    case 'PendingHandover':
      return { label: 'Chờ thủ quỹ nhận', tone: 'warn' }
    case 'Variance':
      return { label: 'Sai lệch chờ duyệt', tone: 'danger' }
    case 'Completed':
      return { label: 'Đã nộp đủ tiền', tone: 'ok' }
    case 'Failed':
      return { label: 'Không thu được', tone: 'danger' }
    case 'Cancelled':
      return { label: 'Đã huỷ', tone: 'neutral' }
    default:
      return { label: status, tone: 'neutral' }
  }
}

export const COLLECTION_EVENT_LABELS: Record<string, string> = {
  created: 'Giao lệnh',
  accepted: 'Người thu nhận lệnh',
  collected: 'Người thu kiểm đếm',
  failed: 'Báo không thu được',
  received: 'Thủ quỹ kiểm đếm',
  variance_returned: 'Trả lại cho người thu',
  variance_resolved: 'Duyệt số thực nhận',
  cancelled: 'Huỷ lệnh',
}

const COLLECTIONS = ['cash', 'cash-collections'] as const

export function useCashCollections(scope: 'all' | 'mine', status?: string) {
  return useQuery({
    queryKey: [...COLLECTIONS, scope, status ?? ''],
    queryFn: () => api.get<CashCollection[]>('/cash-collections', { query: { scope, status } }),
  })
}

export function useCashCollection(id: string | null | undefined) {
  return useQuery({
    queryKey: [...COLLECTIONS, 'detail', id],
    queryFn: () => api.get<CashCollectionDetail>(`/cash-collections/${id}`),
    enabled: !!id,
  })
}

export function useCollectionCustomers(enabled = true) {
  return useQuery({
    queryKey: [...COLLECTIONS, 'customers'],
    queryFn: () => api.get<CollectionCustomer[]>('/cash-collections/customers'),
    enabled,
  })
}

export function useCollectionDrivers(enabled = true) {
  return useQuery({
    queryKey: [...COLLECTIONS, 'drivers'],
    queryFn: () => api.get<CollectionDriver[]>('/cash-collections/drivers'),
    enabled,
  })
}

function useCollectionMutation<TArgs, TResult = void>(fn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['cash'] }),
  })
}

export interface CreateCollectionRequest {
  customerId: string
  driverEmployeeId: string
  expectedAmount: number
  scheduledDate: string
  handoverDueAt: string
  note: string
}

export function useCreateCollection() {
  return useCollectionMutation((body: CreateCollectionRequest) =>
    api.post<{ id: string; orderNo: string }>('/cash-collections', body),
  )
}

export function useAcceptCollection() {
  return useCollectionMutation((id: string) => api.post<void>(`/cash-collections/${id}/accept`))
}

export function useFailCollection() {
  return useCollectionMutation(({ id, reason }: { id: string; reason: string }) =>
    api.post<void>(`/cash-collections/${id}/fail`, { reason }),
  )
}

/** Kiểm đếm theo mệnh giá. `stage` quyết định người khai: lái xe (collect) hay thủ quỹ (receive). */
export function useCountCash() {
  return useCollectionMutation(
    ({
      id,
      stage,
      lines,
      reason,
    }: {
      id: string
      stage: 'collect' | 'receive'
      lines: Array<{ denomination: number; quantity: number }>
      reason?: string
    }) => api.post<{ status: string }>(`/cash-collections/${id}/${stage}`, { lines, reason }),
  )
}

export function useResolveVariance() {
  return useCollectionMutation(
    ({ id, action, reason }: { id: string; action: 'approve_actual' | 'return_to_driver'; reason: string }) =>
      api.post<{ status: string }>(`/cash-collections/${id}/resolve`, { action, reason }),
  )
}

export function useCancelCollection() {
  return useCollectionMutation(({ id, reason }: { id: string; reason: string }) =>
    api.post<void>(`/cash-collections/${id}/cancel`, { reason }),
  )
}

/* ── Phiếu chi tiền mặt ─────────────────────────────────────────────────── */

export interface PayoutVoucher {
  id: string
  voucherNo: string
  categoryId: string | null
  categoryName: string
  categoryCode: string
  employeeId: string
  employeeName: string
  employeeCode: string
  amount: number
  sourceKind: string
  sourceNo: string
  reason: string
  note: string
  status: string
  createdBy: string
  requiresRecipientConfirmation: boolean
  confirmedAt: string | null
  confirmedBy: string
  approvedBy: string
  approvedAt: string | null
  paidAt: string | null
  completedBy: string
  completedAt: string | null
  rejectedBy: string
  rejectedAt: string | null
  rejectReason: string
  cancelledBy: string
  cancelledAt: string | null
  cancelReason: string
  createdAt: string
  /** Chỉ kế toán nhận được nội dung mã QR để in cho người nhận ký. */
  qrValue: string | null
  qrExpiresAt: string | null
}

export interface PayoutCategory {
  id: string
  code: string
  name: string
  description: string
  isActive: boolean
  isSystem: boolean
  sortOrder: number
}

export interface PayoutRecipient {
  id: string
  employeeCode: string
  fullName: string
  departmentName: string
}

export interface PayoutRefundSource {
  id: string
  refundNo: string
  employeeId: string
  employeeName: string
  employeeCode: string
  penaltyNo: string
  appealRequestNo: string
  amount: number
  reason: string
  createdAt: string
}

export interface PayoutSummary {
  month: string
  totalPaid: number
  totalPending: number
  byCategory: Array<{
    categoryId: string | null
    categoryName: string
    count: number
    paidAmount: number
    pendingAmount: number
  }>
}

export interface PayoutEvent {
  id: string
  action: string
  actor: string
  actorName: string
  beforeStatus: string | null
  afterStatus: string | null
  note: string
  occurredAt: string
}

export function payoutStatus(status: string): { label: string; tone: Tone } {
  switch (status) {
    case 'AwaitingScan':
      return { label: 'Chờ người nhận quét QR', tone: 'warn' }
    case 'Confirmed':
      return { label: 'Đã ký nhận', tone: 'brand' }
    case 'AwaitingApproval':
      return { label: 'Chờ duyệt chi', tone: 'warn' }
    case 'Approved':
      return { label: 'Đã duyệt chi', tone: 'brand' }
    case 'Paid':
      return { label: 'Đã chi', tone: 'ok' }
    case 'Rejected':
      return { label: 'Từ chối', tone: 'danger' }
    case 'Cancelled':
      return { label: 'Đã huỷ', tone: 'neutral' }
    default:
      return { label: status, tone: 'neutral' }
  }
}

export const PAYOUT_EVENT_LABELS: Record<string, string> = {
  created: 'Lập phiếu',
  qr_regenerated: 'Tạo lại mã QR',
  confirmed: 'Người nhận quét mã ký nhận',
  approved: 'Duyệt chi',
  paid: 'Thực chi',
  completed: 'Thực chi',
  rejected: 'Từ chối',
  cancelled: 'Huỷ phiếu',
}

/** Phiếu chi nằm ở phạm vi realtime `hr` theo bảng Watched của máy chủ. */
const PAYOUTS = ['cash', 'payout-vouchers'] as const

export function usePayoutVouchers(params: { scope?: 'all' | 'mine'; status?: string; categoryId?: string; month?: string } = {}) {
  return useQuery({
    queryKey: [...PAYOUTS, params.scope ?? 'all', params.status ?? '', params.categoryId ?? '', params.month ?? ''],
    queryFn: () => api.get<PayoutVoucher[]>('/payout-vouchers', { query: params }),
  })
}

export function usePayoutHistory(id: string | null | undefined) {
  return useQuery({
    queryKey: [...PAYOUTS, 'history', id],
    queryFn: () => api.get<PayoutEvent[]>(`/payout-vouchers/${id}/history`),
    enabled: !!id,
  })
}

export function usePayoutSummary(month: string) {
  return useQuery({
    queryKey: [...PAYOUTS, 'summary', month],
    queryFn: () => api.get<PayoutSummary>('/payout-vouchers/summary', { query: { month } }),
  })
}

export function usePayoutCategories(all = false) {
  return useQuery({
    queryKey: [...PAYOUTS, 'categories', all],
    queryFn: () => api.get<PayoutCategory[]>('/payout-vouchers/categories', { query: { all } }),
  })
}

export function usePayoutRecipients(enabled = true) {
  return useQuery({
    queryKey: [...PAYOUTS, 'recipients'],
    queryFn: () => api.get<PayoutRecipient[]>('/payout-vouchers/recipients'),
    enabled,
  })
}

export function usePayoutRefundSources(enabled = true) {
  return useQuery({
    queryKey: [...PAYOUTS, 'refunds'],
    queryFn: () => api.get<PayoutRefundSource[]>('/payout-vouchers/sources/refunds'),
    enabled,
  })
}

function usePayoutMutation<TArgs, TResult = void>(fn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['cash'] }),
  })
}

export interface CreateVoucherRequest {
  categoryId: string
  employeeId: string
  amount: number
  reason: string
  note: string
  sourceKind?: string | null
  sourceId?: string | null
  requiresRecipientConfirmation: boolean
}

export function useCreatePayoutVoucher() {
  return usePayoutMutation((body: CreateVoucherRequest) =>
    api.post<{ id: string; voucherNo: string }>('/payout-vouchers', body),
  )
}

/** Cấp mã QR mới cho phiếu đang chờ ký nhận; mã cũ hết hiệu lực ngay. */
export function useRegenerateVoucherQr() {
  return usePayoutMutation((id: string) =>
    api.post<{ qrValue: string; qrExpiresAt: string }>(`/payout-vouchers/${id}/qr`),
  )
}

export function usePayoutTransition() {
  return usePayoutMutation(
    ({ id, action, note }: { id: string; action: 'approve' | 'complete'; note?: string }) =>
      api.post<void>(`/payout-vouchers/${id}/${action}`, { note }),
  )
}

export function usePayoutCancel() {
  return usePayoutMutation(({ id, action, reason }: { id: string; action: 'reject' | 'cancel'; reason: string }) =>
    api.post<void>(`/payout-vouchers/${id}/${action}`, { reason }),
  )
}
