import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/http'

/* Quỹ tiền mặt, bảng điều hành, báo cáo, việc cần làm. */

// ----- Quỹ tiền mặt -----

export interface CashBalance {
  balance: number
  monthIn: number
  monthOut: number
  monthCount: number
  month: string
}

export interface CashLedgerEntry {
  sourceId: string
  sourceKind: 'collection' | 'payout' | 'receipt' | 'payment' | 'manual'
  sourceRef: string
  direction: 'in' | 'out'
  amount: number
  occurredAt: string
  reason: string
  counterparty: string
  actor: string
  note: string
  balanceAfter: number
}

export interface CashLedger {
  month: string
  openingBalance: number
  totalIn: number
  totalOut: number
  closingBalance: number
  entries: CashLedgerEntry[]
}

export const CASH_SOURCE_LABELS: Record<CashLedgerEntry['sourceKind'], string> = {
  collection: 'Lệnh thu tiền',
  payout: 'Phiếu chi tiền mặt',
  receipt: 'Phiếu thu',
  payment: 'Phiếu chi',
  manual: 'Bút toán thủ công',
}

const CASH = ['cash', 'cash-fund'] as const

export function useCashBalance(month: string, enabled = true) {
  return useQuery({
    queryKey: [...CASH, 'balance', month],
    queryFn: () => api.get<CashBalance>('/cash-fund/balance', { query: { month } }),
    enabled,
  })
}

export function useCashLedger(
  params: { month: string; direction?: string; source?: string; q?: string },
  enabled = true,
) {
  return useQuery({
    queryKey: [...CASH, 'ledger', params.month, params.direction ?? '', params.source ?? '', params.q ?? ''],
    queryFn: () => api.get<CashLedger>('/cash-fund', { query: params }),
    enabled,
  })
}

export function useCreateCashEntry() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: {
      direction: 'in' | 'out'
      amount: number
      occurredAt?: string
      reason: string
      counterparty?: string
      note?: string
      isOpening?: boolean
    }) => api.post<{ id: string }>('/cash-fund/entries', body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['cash'] }),
  })
}

// ----- Bảng điều hành, báo cáo, việc cần làm -----

export interface Dashboard {
  activeCustomers: number
  totalDocuments: number
  totalPayments: number
  monthRevenue: number
  month: number
  year: number
  recent: Array<{
    id: string
    voucherNo: string
    date: string
    customerName: string
    content: string
    total: number
  }>
}

export function useDashboard(enabled = true) {
  return useQuery({ queryKey: ['sales', 'dashboard'], queryFn: () => api.get<Dashboard>('/dashboard'), enabled })
}

export interface Reports {
  totalPayments: number
  monthRevenue: number
  totalDocuments: number
  activeCustomers: number
  monthly: Array<{ year: number; month: number; documentCount: number; paymentCount: number; total: number }>
}

export function useReports(enabled = true) {
  return useQuery({ queryKey: ['sales', 'reports'], queryFn: () => api.get<Reports>('/reports'), enabled })
}

export interface WorklistItem {
  key: string
  kind: 'approval' | 'payslip' | 'document' | 'contract' | 'notice'
  title: string
  description: string
  priority: 'high' | 'medium' | 'normal'
  dueAt: string | null
  route: string
}

export interface Worklist {
  items: WorklistItem[]
  summary: {
    total: number
    approvals: number
    payslips: number
    documents: number
    contracts: number
    notices: number
    overdue: number
  }
}

/** Đường dẫn của ứng dụng di động trong worklist không trùng web; ánh xạ về màn hình web. */
export function worklistRoute(item: WorklistItem) {
  switch (item.kind) {
    case 'approval':
      return '/pheduyet'
    case 'payslip':
      return '/nhan-su'
    case 'document':
    case 'contract':
      return '/nhan-su'
    default:
      return '/cong-thong-tin'
  }
}

export function useWorklist(enabled = true) {
  return useQuery({ queryKey: ['hr', 'worklist'], queryFn: () => api.get<Worklist>('/worklist'), enabled })
}
