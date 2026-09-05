import { api } from '@/lib/http'
import type { AccessProfile } from '@/lib/permissions'

/**
 * Đăng nhập web gồm hai bước:
 *   1. POST /api/auth/bootstrap — máy chủ phát vé ngắn hạn vào cookie HttpOnly, ràng buộc theo
 *      `sid` của trình duyệt và User-Agent. Thiếu bước này thì /login trả 428.
 *   2. POST /api/auth/login — đúng mật khẩu thì máy chủ đặt cookie km_auth và km_csrf. Thân phản
 *      hồi không chứa token; chỉ ứng dụng native mới nhận token trong body.
 */

const SID_KEY = 'km.sid'

/** Mã phiên trình duyệt, giữ nguyên giữa các lần mở web để danh sách thiết bị không sinh thêm bản ghi. */
export function browserSid() {
  let sid = localStorage.getItem(SID_KEY)
  if (!sid) {
    const bytes = crypto.getRandomValues(new Uint8Array(12))
    sid = 'web:' + Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('')
    localStorage.setItem(SID_KEY, sid)
  }
  return sid
}

export interface LoggedInUser {
  id: string
  username: string
  fullName: string
  email: string
  role: string
  avatarUrl?: string | null
  verified?: boolean
  isDiamond?: boolean
  faceRegistered?: boolean
}

interface LoginResponse {
  token: string | null
  user: LoggedInUser
}

export async function bootstrapLogin() {
  await api.post<{ ready: boolean; expiresAt: string; protocol: string; secureTransport: boolean }>(
    '/auth/bootstrap',
    { sid: browserSid() },
  )
}

export async function login(username: string, password: string) {
  await bootstrapLogin()
  const res = await api.post<LoginResponse>('/auth/login', {
    username: username.trim(),
    password,
    sid: browserSid(),
  })
  return res.user
}

/** Hồ sơ truy cập: nguồn duy nhất để dựng menu, nút thao tác và trang đích. */
export const fetchAccessProfile = (quiet = false) =>
  api.get<AccessProfile>('/auth/access-profile', { quiet })

export const fetchMe = () => api.get<LoggedInUser>('/auth/me')

export const heartbeat = () =>
  api.post<void>('/auth/heartbeat', { sid: browserSid() }, { background: true })

export const logout = () => api.post<void>('/auth/logout', { sid: browserSid() })
