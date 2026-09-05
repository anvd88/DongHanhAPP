import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/http'
import type { Tone } from '@/ui'

/* Chấm công: bảng công, ca làm, phân ca, ngày nghỉ, nhật ký chấm công, duyệt ngoại tuyến, khuôn mặt. */

// ----- Bảng công -----

export interface TimesheetDay {
  date: string
  shiftName: string
  holidayName: string
  holidayType: string
  shiftStart: string
  shiftEnd: string
  eventType: string
  checkIn: string | null
  checkOut: string | null
  lateMinutes: number
  earlyMinutes: number
  overtimeMinutes: number
  workedHours: number
  status: string
  isOvernight: boolean
  checkoutGraceMinutes: number
  missingCheckoutRequestStatus: string | null
  hasOpenCheckoutRequest: boolean | null
  missingCheckoutRequestId: string | null
}

export interface Timesheet {
  period: string
  summary: {
    workedDays: number
    absentDays: number
    lateDays: number
    earlyDays: number
    totalLateMinutes: number
    totalEarlyMinutes: number
    totalOvertimeMinutes: number
    totalWorkedHours: number
  }
  days: TimesheetDay[]
}

/** Trạng thái một ngày công do máy chủ tính. */
export function dayStatus(status: string): { label: string; tone: Tone } {
  const key = (status || '').toLowerCase()
  if (key === 'ok' || key === 'present' || key === 'worked') return { label: 'Đủ công', tone: 'ok' }
  if (key === 'late') return { label: 'Đi muộn', tone: 'warn' }
  if (key === 'early') return { label: 'Về sớm', tone: 'warn' }
  if (key === 'lateearly' || key === 'late_early') return { label: 'Muộn và về sớm', tone: 'warn' }
  if (key === 'absent') return { label: 'Vắng', tone: 'danger' }
  if (key === 'missing' || key === 'missingcheckout' || key === 'missing_checkout')
    return { label: 'Thiếu giờ ra', tone: 'danger' }
  if (key === 'holiday') return { label: 'Ngày nghỉ', tone: 'neutral' }
  if (key === 'weekend' || key === 'off') return { label: 'Không có ca', tone: 'neutral' }
  if (key === 'leave') return { label: 'Nghỉ phép', tone: 'info' }
  return { label: status || 'Không rõ', tone: 'neutral' }
}

const ATTENDANCE = ['attendance'] as const

export function useMyTimesheet(month: string) {
  return useQuery({
    queryKey: [...ATTENDANCE, 'timesheet', 'me', month],
    queryFn: () => api.get<Timesheet>('/timesheet/me', { query: { month } }),
  })
}

export function useEmployeeTimesheet(employeeId: string | undefined, month: string) {
  return useQuery({
    queryKey: [...ATTENDANCE, 'timesheet', employeeId, month],
    queryFn: () => api.get<Timesheet>(`/timesheet/employee/${employeeId}`, { query: { month } }),
    enabled: !!employeeId,
  })
}

// ----- Ca làm, phân ca, ngày nghỉ -----

export interface Shift {
  id: string
  code: string
  name: string
  startTime: string
  endTime: string
  breakMinutes: number
  lateGraceMinutes: number
  standardHours: number
  isOvernight: boolean
  checkoutGraceMinutes: number
}

export interface ShiftAssignment {
  id: string
  employeeId: string
  employeeName: string
  employeeCode: string
  shiftId: string
  shiftName: string
  workDate: string
  startTime: string
  endTime: string
  note: string
}

export interface Holiday {
  id: string
  holidayDate: string
  name: string
  holidayType: string
  note: string
  createdBy: string
  createdAt: string
}

export const HOLIDAY_TYPE_LABELS: Record<string, string> = {
  public: 'Nghỉ lễ',
  company: 'Nghỉ công ty',
}

export function useShifts() {
  return useQuery({ queryKey: [...ATTENDANCE, 'shifts'], queryFn: () => api.get<Shift[]>('/shifts') })
}

export interface SaveShiftRequest {
  code: string
  name: string
  startTime: string
  endTime: string
  breakMinutes: number
  lateGraceMinutes: number
  standardHours: number
  isOvernight: boolean
  checkoutGraceMinutes: number
}

export function useSaveShift() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, body }: { id?: string; body: SaveShiftRequest }) => {
      if (id) await api.put<void>(`/shifts/${id}`, body)
      else await api.post<{ id: string }>('/shifts', body)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['attendance'] }),
  })
}

export function useDeleteShift() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.del<void>(`/shifts/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['attendance'] }),
  })
}

export function useShiftAssignments(range: { from: string; to: string }, employeeId?: string) {
  return useQuery({
    queryKey: [...ATTENDANCE, 'assignments', range.from, range.to, employeeId ?? ''],
    queryFn: () => api.get<ShiftAssignment[]>('/shifts/assignments', { query: { ...range, employeeId } }),
    enabled: !!range.from && !!range.to,
  })
}

export function useAssignShift() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: { employeeId: string; shiftId: string; workDate: string; note?: string }) =>
      api.post<{ id: string }>('/shifts/assignments', body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['attendance'] }),
  })
}

export function useDeleteAssignment() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.del<void>(`/shifts/assignments/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['attendance'] }),
  })
}

export function useHolidays(range: { from: string; to: string }) {
  return useQuery({
    queryKey: [...ATTENDANCE, 'holidays', range.from, range.to],
    queryFn: () => api.get<Holiday[]>('/shifts/holidays', { query: range }),
  })
}

export function useSaveHoliday() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: { holidayDate: string; name: string; holidayType: string; note?: string }) =>
      api.post<{ id: string }>('/shifts/holidays', body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['attendance'] }),
  })
}

export function useDeleteHoliday() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.del<void>(`/shifts/holidays/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['attendance'] }),
  })
}

// ----- Trạm chấm công khuôn mặt -----

export interface FaceEngineStatus {
  engine: string
  matchThreshold: number
}

export interface SelfFaceStatus {
  registered: boolean
  sampleCount: number
  createdAt: string | null
  pending: boolean
  requestId: string | null
  requestStatus: string | null
  requestedAt: string | null
  reviewNote: string | null
}

/**
 * Kết quả một lượt chấm công. `status`:
 * ok | posture | eyesclosed | nosmile | lowquality | noface | spoof | unknown | expired | disabled.
 * `previewToken` chỉ có ở bước xem trước; gửi lại token đó để ghi công mà không nhận diện lại.
 */
export interface AttendanceResult {
  status: string
  matched: boolean
  username: string | null
  fullName: string | null
  similarity: number
  loai: string | null
  occurredAt: string | null
  quality: number
  message: string
  guidance: string | null
  previewToken: string | null
}

export function useFaceEngineStatus(enabled = true) {
  return useQuery({
    queryKey: [...ATTENDANCE, 'engine'],
    queryFn: () => api.get<FaceEngineStatus>('/chamcong/trangthai'),
    enabled,
    staleTime: 10 * 60_000,
    retry: false,
  })
}

export function useMyFaceStatus(enabled = true) {
  return useQuery({
    queryKey: [...ATTENDANCE, 'my-face'],
    queryFn: () => api.get<SelfFaceStatus>('/chamcong/dangky/cua-toi'),
    enabled,
  })
}

/** Bước xem trước: gửi loạt ảnh, máy chủ trả về ai và Vào hay Ra, chưa ghi nhật ký. */
export function usePreviewAttendance() {
  return useMutation({
    mutationFn: (images: string[]) =>
      api.post<AttendanceResult>('/chamcong/cham', { images, previewOnly: true, motionCheck: false }),
  })
}

/** Bước xác nhận: gửi lại vé của bước xem trước, máy chủ ghi công. */
export function useConfirmAttendance() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (confirmToken: string) => api.post<AttendanceResult>('/chamcong/cham', { confirmToken }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['attendance'] }),
  })
}

// ----- Quản trị chấm công -----

export interface AttendanceLog {
  id: number
  username: string
  fullName: string
  loai: string
  similarity: number
  occurredAt: string
  ghiChu: string
}

export interface OfflineRecord {
  id: number
  username: string
  fullName: string
  loai: string
  similarity: number
  quality: number
  occurredAt: string
  syncedAt: string
  backdateMinutes: number
  clientIp: string
  onCompanyLan: boolean
  gpsLat: number | null
  gpsLng: number | null
  distanceM: number | null
  inGeofence: boolean | null
  flags: string
  status: string
  reviewedBy: string
  reviewedAt: string | null
  reviewNote: string
}

/** Cờ rủi ro của một bản chấm công ngoại tuyến, tách từ chuỗi flags. */
export const RISK_LABELS: Record<string, string> = {
  backdate: 'Lùi giờ máy',
  offlan: 'Ngoài mạng công ty',
  geofence: 'Ngoài phạm vi công ty',
  nogps: 'Không có vị trí',
  lowquality: 'Ảnh mờ',
}

export function riskFlags(flags: string): string[] {
  return (flags || '')
    .split(/[,;\s]+/)
    .map((f) => f.trim())
    .filter(Boolean)
}

export interface FaceEnrollmentRequest {
  id: string
  username: string
  fullName: string
  status: string
  sampleCount: number
  requestedAt: string
  expiresAt: string
  reviewedBy: string
  reviewedAt: string | null
  reviewNote: string
  identityVerificationMethod: string
}

export interface RegisteredFace {
  username: string
  fullName: string
  soMau: number
  createdAt: string | null
}

export interface OfflineConfig {
  geofenceLat: number | null
  geofenceLng: number | null
  geofenceRadiusM: number
  maxBackdateMinutes: number
}

export function offlineStatus(status: string): { label: string; tone: Tone } {
  const key = (status || '').toLowerCase()
  if (key === 'approved') return { label: 'Đã duyệt', tone: 'ok' }
  if (key === 'rejected') return { label: 'Từ chối', tone: 'danger' }
  return { label: 'Chờ duyệt', tone: 'warn' }
}

export function enrollmentStatus(status: string): { label: string; tone: Tone } {
  const key = (status || '').toLowerCase()
  if (key === 'approved') return { label: 'Đã duyệt', tone: 'ok' }
  if (key === 'rejected') return { label: 'Từ chối', tone: 'danger' }
  if (key === 'expired') return { label: 'Hết hạn', tone: 'neutral' }
  return { label: 'Chờ duyệt', tone: 'warn' }
}

export function useAttendanceLog(params: { date?: string; search?: string } = {}, enabled = true) {
  return useQuery({
    queryKey: [...ATTENDANCE, 'log', params.date ?? '', params.search ?? ''],
    queryFn: () => api.get<AttendanceLog[]>('/chamcong/log', { query: params }),
    enabled,
  })
}

export function useOfflineRecords(status: string, enabled = true) {
  return useQuery({
    queryKey: [...ATTENDANCE, 'offline', status],
    queryFn: () => api.get<OfflineRecord[]>('/chamcong/offline', { query: { status } }),
    enabled,
  })
}

export function useReviewOffline() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, approve, note }: { id: number; approve: boolean; note?: string }) =>
      api.post<void>(`/chamcong/offline/${id}/${approve ? 'approve' : 'reject'}`, { note }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['attendance'] }),
  })
}

export function useFaceEnrollments(status: string, enabled = true) {
  return useQuery({
    queryKey: [...ATTENDANCE, 'face-enrollments', status],
    queryFn: () => api.get<FaceEnrollmentRequest[]>('/chamcong/face-enrollments', { query: { status } }),
    enabled,
  })
}

export function useRejectEnrollment() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      api.post<void>(`/chamcong/face-enrollments/${id}/reject`, { reason }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['attendance'] }),
  })
}

export function useRegisteredFaces(enabled = true) {
  return useQuery({
    queryKey: [...ATTENDANCE, 'registered-faces'],
    queryFn: () => api.get<RegisteredFace[]>('/chamcong/dadangky'),
    enabled,
  })
}

export function useDeleteFace() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (username: string) => api.del<void>(`/chamcong/dangky/${encodeURIComponent(username)}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['attendance'] }),
  })
}

export function useOfflineConfig(enabled = true) {
  return useQuery({
    queryKey: [...ATTENDANCE, 'offline-config'],
    queryFn: () => api.get<OfflineConfig>('/chamcong/offline-config'),
    enabled,
  })
}
