import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/http'
import type { Tone } from '@/ui'

/*
 * Nhân sự: hồ sơ, phòng ban, địa điểm, chức danh, hợp đồng, giấy tờ, ngày phép, danh bạ,
 * tài khoản ngân hàng, phát triển nhân sự. Khoá truy vấn bắt đầu bằng phạm vi realtime.
 */

export interface EmployeePosition {
  id: string
  code: string
  name: string
  defaultRole: string
  defaultRoleLabel: string
  isPrimary: boolean
}

export interface EmployeeCard {
  id: string
  employeeCode: string
  username: string
  fullName: string
  position: string
  positionId: string | null
  positionCode: string
  positions: EmployeePosition[]
  positionIds: string[]
  hireDate: string | null
  status: string
  phone: string
  email: string
  avatar: string | null
  departmentId: string | null
  departmentName: string
  locationId: string | null
  locationName: string
  accessRole: string
  managerName: string
}

export interface EmployeeDetail extends EmployeeCard {
  dob: string | null
  gender: string
  address: string
  managerId: string | null
  isAccounting: boolean
}

/** Trạng thái hồ sơ nhân viên do máy chủ trả về ở dạng chuỗi tự do. */
export function employeeStatus(status: string): { label: string; tone: Tone } {
  const key = (status || '').toLowerCase()
  if (key === 'active') return { label: 'Đang làm việc', tone: 'ok' }
  if (key === 'probation') return { label: 'Thử việc', tone: 'warn' }
  if (key === 'leave' || key === 'onleave') return { label: 'Tạm nghỉ', tone: 'warn' }
  if (key === 'resigned' || key === 'terminated' || key === 'inactive')
    return { label: 'Đã nghỉ', tone: 'neutral' }
  return { label: status || 'Không rõ', tone: 'neutral' }
}

export interface Department {
  id: string
  code: string
  name: string
  parentId: string | null
  parentName: string
  managerEmployeeId: string | null
  managerName: string
  isAccounting: boolean
  employeeCount: number
}

export interface Location {
  id: string
  code: string
  name: string
  address: string
  employeeCount: number
}

export interface JobPosition {
  id: string
  code: string
  name: string
  defaultRole: string
  defaultRoleLabel: string
  defaultAccessRole: string
  isSystem: boolean
  isActive: boolean
  sortOrder: number
}

export interface Contract {
  id: string
  contractNo: string
  contractType: string
  startDate: string | null
  endDate: string | null
  baseSalary: number
  allowance: number
  status: string
  note: string
  raiseTotal: number
  raiseCount: number
  currentSalary: number
}

export interface HrDocument {
  id: string
  docType: string
  title: string
  issuedBy: string
  issuedDate: string | null
  docNumber: string
  expiresAt: string | null
  approvalStatus: string
  fileName: string
  mimeType: string
  fileUrl: string
  hasFile: boolean
  note: string
}

export interface LeaveBalance {
  id: string
  year: number
  leaveType: string
  totalDays: number
  usedDays: number
  remainingDays: number
}

export const LEAVE_TYPE_LABELS: Record<string, string> = {
  annual: 'Phép năm',
  sick: 'Nghỉ ốm',
  unpaid: 'Nghỉ không lương',
  maternity: 'Thai sản',
}

const HR = ['hr'] as const

export function useMyEmployee() {
  return useQuery({ queryKey: [...HR, 'me'], queryFn: () => api.get<EmployeeDetail>('/hr/me') })
}

export function useMyDocuments() {
  return useQuery({ queryKey: [...HR, 'me', 'documents'], queryFn: () => api.get<HrDocument[]>('/hr/me/documents') })
}

export function useEmployees(params: { search?: string; departmentId?: string } = {}, enabled = true) {
  return useQuery({
    queryKey: [...HR, 'employees', params.search ?? '', params.departmentId ?? ''],
    queryFn: () => api.get<EmployeeCard[]>('/hr/employees', { query: params }),
    enabled,
  })
}

export function useEmployee(id: string | undefined) {
  return useQuery({
    queryKey: [...HR, 'employees', id],
    queryFn: () => api.get<EmployeeDetail>(`/hr/employees/${id}`),
    enabled: !!id,
  })
}

export function useDepartments(enabled = true) {
  return useQuery({ queryKey: [...HR, 'departments'], queryFn: () => api.get<Department[]>('/hr/departments'), enabled })
}

export function useLocations(enabled = true) {
  return useQuery({ queryKey: [...HR, 'locations'], queryFn: () => api.get<Location[]>('/hr/locations'), enabled })
}

export function useJobPositions(enabled = true) {
  return useQuery({
    queryKey: [...HR, 'job-positions'],
    queryFn: () => api.get<JobPosition[]>('/hr/job-positions'),
    enabled,
  })
}

export function useContracts(employeeId: string | undefined) {
  return useQuery({
    queryKey: [...HR, 'employees', employeeId, 'contracts'],
    queryFn: () => api.get<Contract[]>(`/hr/employees/${employeeId}/contracts`),
    enabled: !!employeeId,
  })
}

export function useEmployeeDocuments(employeeId: string | undefined) {
  return useQuery({
    queryKey: [...HR, 'employees', employeeId, 'documents'],
    queryFn: () => api.get<HrDocument[]>(`/hr/employees/${employeeId}/documents`),
    enabled: !!employeeId,
  })
}

export function useLeaveBalances(employeeId: string | undefined) {
  return useQuery({
    queryKey: [...HR, 'employees', employeeId, 'leave-balances'],
    queryFn: () => api.get<LeaveBalance[]>(`/hr/employees/${employeeId}/leave-balances`),
    enabled: !!employeeId,
  })
}

export interface SaveDepartmentRequest {
  code: string
  name: string
  parentId?: string | null
  managerEmployeeId?: string | null
}

export function useSaveDepartment() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, body }: { id?: string; body: SaveDepartmentRequest }) => {
      if (id) await api.put<void>(`/hr/departments/${id}`, body)
      else await api.post<{ id: string }>('/hr/departments', body)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['hr'] }),
  })
}

export interface SaveLocationRequest {
  code: string
  name: string
  address: string
}

export function useSaveLocation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, body }: { id?: string; body: SaveLocationRequest }) => {
      if (id) await api.put<void>(`/hr/locations/${id}`, body)
      else await api.post<{ id: string }>('/hr/locations', body)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['hr'] }),
  })
}

// ----- Danh bạ và sơ đồ tổ chức -----

export interface DirectoryEntry {
  id: string
  fullName: string
  position: string
  departmentId: string | null
  departmentName: string | null
  managerId: string | null
  managerName: string | null
  phone: string | null
  email: string | null
  canSeeContact: boolean
  online: boolean
}

export interface OrgNode {
  id: string
  fullName: string
  position: string
  departmentName: string | null
  reports: OrgNode[]
}

export function useDirectory(params: { search?: string; departmentId?: string } = {}) {
  return useQuery({
    queryKey: ['presence', 'directory', params.search ?? '', params.departmentId ?? ''],
    queryFn: () => api.get<DirectoryEntry[]>('/directory', { query: params }),
  })
}

export function useOrgChart(enabled = true) {
  return useQuery({
    queryKey: ['presence', 'directory', 'org-chart'],
    queryFn: () => api.get<OrgNode[]>('/directory/org-chart'),
    enabled,
  })
}

// ----- Tài khoản ngân hàng -----

export interface BankAccount {
  id: string
  employeeId: string
  employeeName: string
  employeeCode: string
  bank: string
  accountNumber: string
  accountHolder: string
  branch: string
  isDefault: boolean
  note: string
}

export interface BankInfo {
  code: string
  name: string
  shortName: string
}

export function useBanks() {
  return useQuery({
    queryKey: [...HR, 'banks'],
    queryFn: () => api.get<BankInfo[]>('/bank-accounts/banks'),
    staleTime: 60 * 60_000,
  })
}

export function useBankAccounts() {
  return useQuery({ queryKey: [...HR, 'bank-accounts'], queryFn: () => api.get<BankAccount[]>('/bank-accounts') })
}

export interface SaveBankAccountRequest {
  bank: string
  accountNumber: string
  accountHolder: string
  branch: string
  isDefault: boolean
  note: string
}

export function useSaveBankAccount() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, body }: { id?: string; body: SaveBankAccountRequest }) => {
      if (id) await api.put<void>(`/bank-accounts/${id}`, body)
      else await api.post<{ id: string }>('/bank-accounts', body)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['hr'] }),
  })
}

export function useSetDefaultBankAccount() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.post<void>(`/bank-accounts/${id}/default`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['hr'] }),
  })
}

export function useDeleteBankAccount() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.del<void>(`/bank-accounts/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['hr'] }),
  })
}

// ----- Phát triển nhân sự -----

export interface OnboardingTask {
  id: string
  title: string
  actionKey: string
  dueAt: string | null
  policyText: string
  completed: boolean
  acknowledged: boolean
}

export interface PerformanceGoal {
  id: string
  title: string
  description: string
  target: number
  progress: number
  unit: string
  dueAt: string | null
}

export interface PerformanceReview {
  id: string
  period: string
  closesAt: string | null
  selfAssessment: string
  managerComment: string
  score: number | null
  status: string
}

export interface TrainingCourse {
  id: string
  title: string
  description: string
  materialUrl: string
  videoUrl: string
  quiz: Array<{ text: string; options: string[] }>
  progress: number
  resumeSeconds: number
  score: number | null
  completedAt: string | null
  certificateExpiresAt: string | null
}

export interface BenefitsPayload {
  leaveTotal: number
  leaveUsed: number
  leaveRemaining: number
  leaveHistory: Array<{ requestNo: string; payload: unknown; status: string; createdAt: string }>
  benefits: Array<{ id: string; type: string; title: string; value: string; validFrom: string | null; validTo: string | null }>
  rewards: Array<{ id: string; title: string; points: number; awardedAt: string; note: string }>
  birthday: string | null
  hireDate: string | null
}

const TALENT = ['talent'] as const

export function useOnboarding(enabled = true) {
  return useQuery({
    queryKey: [...TALENT, 'onboarding'],
    queryFn: () => api.get<{ mentorName: string; items: OnboardingTask[] }>('/talent/onboarding'),
    enabled,
  })
}

export function useCompleteOnboardingTask() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.post<void>(`/talent/onboarding/${id}/complete`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['talent'] }),
  })
}

export function usePerformance(enabled = true) {
  return useQuery({
    queryKey: [...TALENT, 'performance'],
    queryFn: () => api.get<{ goals: PerformanceGoal[]; reviews: PerformanceReview[] }>('/talent/performance'),
    enabled,
  })
}

export function useSaveSelfReview() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, text }: { id: string; text: string }) =>
      api.put<void>(`/talent/performance/reviews/${id}/self`, { text }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['talent'] }),
  })
}

export function useTraining(enabled = true) {
  return useQuery({ queryKey: [...TALENT, 'training'], queryFn: () => api.get<TrainingCourse[]>('/talent/training'), enabled })
}

export function useBenefits(enabled = true) {
  return useQuery({ queryKey: [...TALENT, 'benefits'], queryFn: () => api.get<BenefitsPayload>('/talent/benefits'), enabled })
}
