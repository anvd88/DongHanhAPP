import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, request } from '@/lib/http'
import type { Tone } from '@/ui'

/* ============================================================================
   Hệ thống: tài khoản & phân quyền, cấu hình từ xa, bản cập nhật APK, nhật ký hoạt động.
   ========================================================================== */

/* ── Tài khoản & phân quyền ─────────────────────────────────────────────── */

export interface UserAccount {
  id: string
  username: string
  fullName: string
  email: string
  role: string
  isActive: boolean
  approvalStatus: string
  createdAt: string | null
  isOnline: boolean
  lastSeen: string | null
  verified: boolean
  isDiamond: boolean
  secondaryRoles: string[]
  /** Vai trò lấy từ chức vụ trong hồ sơ nhân sự: đổi ở màn Quản lý nhân sự chứ không đổi ở đây. */
  rolesManagedByPositions: boolean
}

export interface RoleCatalogEntry {
  role: string
  label: string
  assignable: boolean
  technical: boolean
  permissions: Array<{ key: string; label: string }>
}

export function accountState(user: UserAccount): { id: 'pending' | 'locked' | 'active'; label: string; tone: Tone } {
  if (user.approvalStatus === 'Pending') return { id: 'pending', label: 'Chờ duyệt', tone: 'warn' }
  if (!user.isActive) return { id: 'locked', label: 'Đã khoá', tone: 'danger' }
  return { id: 'active', label: 'Đang hoạt động', tone: 'ok' }
}

const USERS = ['presence', 'users'] as const

export function useUsers(params: { search?: string; role?: string } = {}) {
  return useQuery({
    queryKey: [...USERS, params.search ?? '', params.role ?? ''],
    queryFn: () => api.get<UserAccount[]>('/users', { query: params }),
  })
}

export function useRoleCatalog(enabled = true) {
  return useQuery({
    queryKey: [...USERS, 'roles'],
    queryFn: () => api.get<RoleCatalogEntry[]>('/roles/catalog'),
    enabled,
    staleTime: 10 * 60 * 1000,
  })
}

function useUserMutation<TArgs, TResult = void>(fn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['presence'] }),
  })
}

export function useCreateUser() {
  return useUserMutation((body: { username: string; fullName: string; email: string; password: string; role: string }) =>
    api.post<UserAccount>('/users', body),
  )
}

export function useSetPrimaryRole() {
  return useUserMutation(({ id, role, reason }: { id: string; role: string; reason?: string }) =>
    api.post<void>(`/users/${id}/role`, { role, reason }),
  )
}

export function useSetSecondaryRole() {
  return useUserMutation(
    ({ id, role, grant, reason }: { id: string; role: string; grant: boolean; reason?: string }) =>
      api.post<void>(`/users/${id}/secondary-role`, { role, grant, reason }),
  )
}

export function useApproveUser() {
  return useUserMutation((id: string) => api.post<void>(`/users/${id}/approve`))
}

export function useSetUserLock() {
  return useUserMutation(({ id, locked }: { id: string; locked: boolean }) =>
    api.post<void>(`/users/${id}/lock`, { locked }),
  )
}

export function useDeleteUser() {
  return useUserMutation((id: string) => api.del<void>(`/users/${id}`))
}

export interface RecoveryCodeResult {
  /** Chỉ có khi mã được cấp tay; máy chủ tự gửi đi rồi thì trường này là null. */
  code: string | null
  channel: string
  delivered: boolean
  message: string
  sentTo: string | null
}

/**
 * Cấp MÃ KHÔI PHỤC 5 ký tự cho người dùng tự đặt lại mật khẩu ở màn quên mật khẩu.
 *
 * Kênh chuyển mã do máy chủ chọn: hôm nay là cấp tay nên mã trả về để quản trị viên đọc; khi bật
 * gửi qua thư điện tử hoặc Zalo thì mã đi thẳng tới chủ tài khoản và không trả về đây nữa.
 */
export function useIssueRecoveryCode() {
  return useUserMutation(({ id, channel }: { id: string; channel?: string }) =>
    api.post<RecoveryCodeResult>(`/users/${id}/recovery-code`, { channel }),
  )
}

/**
 * Đặt lại mật khẩu thay người dùng: máy chủ sinh một mật khẩu tạm và đổi luôn.
 *
 * Khác hẳn mã khôi phục ở trên — đây là MẬT KHẨU đăng nhập được ngay, dài 16 ký tự, không gõ vào ô
 * mã khôi phục được. Chỉ dùng khi người dùng không tự thao tác được.
 */
export function useResetPassword() {
  return useUserMutation((id: string) => api.post<{ code: string }>(`/users/${id}/reset-password`))
}

/* ── Cấu hình điều khiển từ xa ──────────────────────────────────────────── */

export interface AppConfig {
  announcement: string
  announcementLevel: 'info' | 'warning' | 'critical'
  faceEnrollBannerEnabled: boolean
  foregroundPollSeconds: number
  portraitHeightFactor: number
  portraitVerticalNudge: number
  portraitAspect: number
  portraitMinWidthFactor: number
  features: {
    locationEnabled: boolean
    offlineAttendanceEnabled: boolean
    biometricAttendanceEnabled: boolean
    companyPortalEnabled: boolean
  }
  onboarding: {
    cameraReason: string
    locationReason: string
    notificationReason: string
    introText: string
  }
  notices: string[]
}

export type AppConfigPatch = Partial<Omit<AppConfig, 'features' | 'onboarding'>> & {
  features?: Partial<AppConfig['features']>
  onboarding?: Partial<AppConfig['onboarding']>
}

export function useAppConfig() {
  return useQuery({
    queryKey: ['config', 'app-config'],
    queryFn: () => api.get<AppConfig>('/app-config'),
  })
}

export function useSaveAppConfig() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: AppConfigPatch) => api.put<AppConfig>('/app-config', body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['config'] }),
  })
}

/* ── Bản cập nhật APK ───────────────────────────────────────────────────── */

export interface Release {
  id: number
  appTarget: string
  version: string
  versionCode: number
  releaseNotes: string
  isMandatory: boolean
  isPublished: boolean
  publishedAt: string
  publishedBy: string
  apkFileName: string
  apkSize: number
  apkSha256: string
}

const RELEASES = ['release', 'list'] as const

export function useReleases() {
  return useQuery({ queryKey: [...RELEASES], queryFn: () => api.get<Release[]>('/releases') })
}

function useReleaseMutation<TArgs>(fn: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['release'] }),
  })
}

export interface UploadReleaseRequest {
  file: File
  version: string
  versionCode: number
  appTarget: string
  releaseNotes: string
  isMandatory: boolean
  isPublished: boolean
}

/**
 * Đăng bản mới. Gửi multipart vì tệp APK đi thẳng từ trình duyệt xuống đĩa máy chủ; JSON base64 sẽ
 * phình gấp rưỡi và vượt trần thân request.
 */
export function useUploadRelease() {
  return useReleaseMutation((body: UploadReleaseRequest) => {
    const form = new FormData()
    form.append('version', body.version)
    form.append('versionCode', String(body.versionCode))
    form.append('appTarget', body.appTarget)
    form.append('releaseNotes', body.releaseNotes)
    form.append('isMandatory', String(body.isMandatory))
    form.append('isPublished', String(body.isPublished))
    form.append('file', body.file, body.file.name)
    return request<Release>('/releases', { method: 'POST', body: form })
  })
}

export function usePublishRelease() {
  return useReleaseMutation(({ id, isPublished, isMandatory }: { id: number; isPublished: boolean; isMandatory?: boolean }) =>
    api.post<void>(`/releases/${id}/publish`, { isPublished, isMandatory }),
  )
}

export function useDeleteReleases() {
  return useReleaseMutation((ids: number[]) =>
    ids.length === 1 ? api.del<void>(`/releases/${ids[0]}`) : api.post<{ deleted: number }>('/releases/bulk-delete', { ids }),
  )
}

/* ── Nhật ký hoạt động ──────────────────────────────────────────────────── */

export interface AuditItem {
  id: number
  occurredAt: string
  username: string
  action: string
  entity: string
  entityName: string
  details: string
  before: string | null
  after: string | null
}

export interface AuditPage {
  items: AuditItem[]
  total: number
  page: number
  pageSize: number
}

export interface AuditFilters {
  actions: string[]
  entities: string[]
  groups: Array<{ key: string; label: string }>
  months: string[]
  canSeeAll: boolean
}

export interface AuditQuery {
  page?: number
  pageSize?: number
  search?: string
  username?: string
  action?: string
  entity?: string
  group?: string
  month?: string
}

export function useAudit(query: AuditQuery) {
  return useQuery({
    queryKey: ['audit', 'page', query],
    queryFn: () => api.get<AuditPage>('/audit', { query: query as Record<string, string | number | undefined> }),
  })
}

export function useAuditFilters() {
  return useQuery({
    queryKey: ['audit', 'filters'],
    queryFn: () => api.get<AuditFilters>('/audit/filters'),
    staleTime: 5 * 60 * 1000,
  })
}

/** Đường tải tệp xuất, mở thẳng bằng thẻ neo để trình duyệt tự lưu. */
export function auditExportUrl(query: AuditQuery, format: 'csv' | 'excel') {
  const params = new URLSearchParams({ format })
  for (const [key, value] of Object.entries(query))
    if (value !== undefined && value !== null && value !== '' && key !== 'page' && key !== 'pageSize')
      params.set(key, String(value))
  return `/api/audit/export?${params.toString()}`
}
