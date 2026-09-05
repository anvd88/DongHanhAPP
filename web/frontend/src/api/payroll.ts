import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/http'
import type { Tone } from '@/ui'

/* Bảng lương, phiếu lương, phạt và hoàn tiền phạt. */

export interface SalaryRow {
  employeeId: string
  employeeName: string
  employeeCode: string
  departmentName: string
  /** Có mức lương dùng được: hợp đồng có lương, hoặc bản ghi lương cũ. */
  hasSalary: boolean
  baseSalary: number
  allowance: number
  overtimeRate: number
  extraCount: number
  hardSalary: {
    amount: number
    fromContract: boolean
    contractId: string | null
    contractNo: string
    contractType: string
    contractBase: number
    raiseTotal: number
    effective: boolean
    contractEnd: string | null
  }
}

export interface PublishedPayslip {
  id: string
  employeeId: string
  employeeCode: string
  employeeName: string
  departmentName: string
  locationName: string
  period: string
  overtimeHours: number
  totalEarnings: number
  totalDeductions: number
  netPay: number
  status: 'Published' | 'Acknowledged'
  acknowledgedAt: string | null
  updatedAt: string
}

export interface PublishedPayslips {
  period: string
  search: string
  status: string
  page: number
  pageSize: number
  totalItems: number
  totalPages: number
  summary: {
    activeEmployeeCount: number
    publishedCount: number
    acknowledgedCount: number
    pendingAcknowledgementCount: number
    totalEarnings: number
    totalDeductions: number
    totalNetPay: number
  }
  items: PublishedPayslip[]
}

export interface MyPayslip {
  id: string
  period: string
  baseSalary: number
  allowance: number
  overtimePay: number
  overtimeHours: number
  workedDays: number
  absentDays: number
  lateDays: number
  totalWorkedHours: number
  overtimeRate: number
  earnings: Array<{ label: string; amount: number }>
  deductions: Array<{ label: string; amount: number }>
  totalEarnings: number
  totalDeductions: number
  netPay: number
  note: string
  publishedAt: string
  updatedAt: string
  revisionToken: string
  acknowledgementDueAt: string
  acknowledgementOverdue: boolean
  acknowledgedAt: string | null
}

const PAYROLL = ['hr', 'payroll'] as const

export function useSalaries(enabled = true) {
  return useQuery({ queryKey: [...PAYROLL, 'salaries'], queryFn: () => api.get<SalaryRow[]>('/payroll/salaries'), enabled })
}

export function usePublishedPayslips(period: string, params: { search?: string; status?: string } = {}, enabled = true) {
  return useQuery({
    queryKey: [...PAYROLL, 'published', period, params.search ?? '', params.status ?? 'all'],
    queryFn: () =>
      api.get<PublishedPayslips>('/payroll/payslips/published', {
        query: { period, search: params.search, status: params.status, pageSize: 200 },
      }),
    enabled,
  })
}

export function useMyPayslips(enabled = true) {
  return useQuery({ queryKey: [...PAYROLL, 'mine'], queryFn: () => api.get<MyPayslip[]>('/payroll/my-payslips'), enabled })
}

export function useAcknowledgePayslip() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, revision }: { id: string; revision: string }) =>
      api.post<void>(`/payroll/my-payslips/${id}/ack`, undefined, { query: { expectedRevision: revision } }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['hr'] }),
  })
}

export function useSendPayslipInquiry() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, content }: { id: string; content: string }) =>
      api.post<void>(`/payroll/my-payslips/${id}/inquiries`, { content }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['hr'] }),
  })
}

export interface MyEstimate {
  employeeName: string
  employeeCode: string
  period: string
  earnings: Array<{ label: string; amount: number }>
  deductions: Array<{ label: string; amount: number }>
  [key: string]: unknown
}

export function useMyEstimate(enabled = true) {
  return useQuery({
    queryKey: [...PAYROLL, 'my-estimate'],
    queryFn: () => api.get<MyEstimate>('/payroll/my-estimate'),
    enabled,
    retry: false,
  })
}

// ----- Phạt và kỷ luật -----

export interface PenaltyProgress {
  settled: boolean
  deducted: number
  remaining: number
  paidMonths: number
  nextPeriod: string | null
  nextAmount: number
  schedule?: Array<{ period: string; amount: number; paid: boolean }>
}

export interface Penalty {
  id: string
  penaltyNo: string
  employeeId: string
  employeeName: string
  employeeCode: string
  penaltyType: string
  penaltyTypeLabel: string
  penaltyDate: string
  amount: number
  installments: number
  startPeriod: string
  reason: string
  note: string
  status: string
  createdBy: string
  createdAt: string
  progress: PenaltyProgress | null
}

export interface PenaltyType {
  type: string
  label: string
}

export function penaltyStatus(p: Penalty): { label: string; tone: Tone } {
  if (p.status === 'Waived') return { label: 'Đã miễn', tone: 'neutral' }
  if (p.progress?.settled) return { label: 'Đã tất toán', tone: 'ok' }
  if (p.penaltyType !== 'fine') return { label: 'Đã ghi nhận', tone: 'info' }
  if ((p.progress?.deducted ?? 0) > 0) return { label: 'Đang khấu trừ', tone: 'warn' }
  return { label: 'Chưa khấu trừ', tone: 'danger' }
}

export function usePenalties(params: { scope?: string; employeeId?: string; month?: string } = {}, enabled = true) {
  return useQuery({
    queryKey: [...PAYROLL, 'penalties', params.scope ?? '', params.employeeId ?? '', params.month ?? ''],
    queryFn: () => api.get<Penalty[]>('/penalties', { query: params }),
    enabled,
  })
}

export function usePenaltyTypes(enabled = true) {
  return useQuery({
    queryKey: [...PAYROLL, 'penalty-types'],
    queryFn: () => api.get<PenaltyType[]>('/penalties/types'),
    enabled,
    staleTime: 60 * 60_000,
  })
}

export interface SavePenaltyRequest {
  employeeId: string
  penaltyType: string
  penaltyDate: string
  amount: number
  installments: number
  startPeriod: string
  reason: string
  note: string
}

export function useSavePenalty() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, body }: { id?: string; body: SavePenaltyRequest }) => {
      if (id) await api.put<void>(`/penalties/${id}`, body)
      else await api.post<{ id: string }>('/penalties', body)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['hr'] }),
  })
}

export function useWaivePenalty() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.post<void>(`/penalties/${id}/waive`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['hr'] }),
  })
}

export interface PenaltyRefund {
  id: string
  refundNo: string
  employeeId: string
  employeeName: string
  employeeCode: string
  penaltyId: string | null
  penaltyNo: string
  appealRequestNo: string
  amount: number
  reason: string
  status: string
  payoutMethod: string
  appliedPeriod: string
  createdBy: string
  approvedBy: string
  note: string
  createdAt: string
  decidedAt: string | null
}

export function refundStatus(status: string): { label: string; tone: Tone } {
  const key = (status || '').toLowerCase()
  if (key === 'paid') return { label: 'Đã hoàn', tone: 'ok' }
  if (key === 'approved') return { label: 'Đã duyệt', tone: 'info' }
  if (key === 'rejected') return { label: 'Từ chối', tone: 'danger' }
  if (key === 'pendingaccounting') return { label: 'Chờ kế toán', tone: 'warn' }
  return { label: status || 'Không rõ', tone: 'neutral' }
}

export function usePenaltyRefunds(scope: string, enabled = true) {
  return useQuery({
    queryKey: [...PAYROLL, 'penalty-refunds', scope],
    queryFn: () => api.get<PenaltyRefund[]>('/penalty-refunds', { query: { scope } }),
    enabled,
  })
}
