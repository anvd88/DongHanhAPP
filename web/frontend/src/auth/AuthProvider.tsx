import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { ApiError, onSessionLost } from '@/lib/http'
import type { AccessProfile } from '@/lib/permissions'
import { can, canAny } from '@/lib/permissions'
import * as session from './session'

type Status = 'loading' | 'anonymous' | 'ready'

interface AuthValue {
  status: Status
  profile: AccessProfile | null
  user: session.LoggedInUser | null
  /** Lý do phiên kết thúc: khoá tài khoản, thu hồi thiết bị hoặc quá hạn nhàn rỗi. */
  endedReason: string | null
  signIn: (username: string, password: string) => Promise<AccessProfile>
  /**
   * Phiên đã được cấp ở nơi khác — quét mã QR hoặc xác nhận trong ứng dụng Nhân sự. Máy chủ đặt
   * cookie ngay trong phản hồi của vòng hỏi trạng thái, ở đây chỉ nạp lại hồ sơ truy cập.
   */
  adoptSession: () => Promise<AccessProfile>
  signOut: () => Promise<void>
  refreshProfile: () => Promise<void>
  /** Nạp lại phần hồ sơ hiển thị (tên, thư điện tử, ảnh đại diện) sau khi người dùng tự sửa. */
  refreshUser: () => Promise<void>
  can: (permission?: string) => boolean
  canAny: (permissions?: readonly string[]) => boolean
}

const AuthContext = createContext<AuthValue | null>(null)

/** Nhịp giữ phiên. Gửi kèm cờ nền để backend không cập nhật last_seen, tránh việc một tab để mở
 *  lâu làm tài khoản luôn hiển thị trực tuyến. */
const HEARTBEAT_MS = 4 * 60 * 1000

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<Status>('loading')
  const [profile, setProfile] = useState<AccessProfile | null>(null)
  const [user, setUser] = useState<session.LoggedInUser | null>(null)
  const [endedReason, setEndedReason] = useState<string | null>(null)
  const loadingUser = useRef(false)

  const loadUser = useCallback(async () => {
    if (loadingUser.current) return
    loadingUser.current = true
    try {
      setUser(await session.fetchMe())
    } catch {
      /* Thiếu hồ sơ hiển thị không ảnh hưởng tới việc sử dụng, bỏ qua lỗi tại đây. */
    } finally {
      loadingUser.current = false
    }
  }, [])

  const refreshProfile = useCallback(async () => {
    const next = await session.fetchAccessProfile()
    setProfile(next)
    setStatus('ready')
  }, [])

  // Dò phiên khi mở trang: cookie km_auth có thể còn hiệu lực từ lần trước.
  useEffect(() => {
    let alive = true
    ;(async () => {
      try {
        const next = await session.fetchAccessProfile(true)
        if (!alive) return
        setProfile(next)
        setStatus('ready')
        void loadUser()
      } catch (error) {
        if (!alive) return
        setStatus('anonymous')
        // 401 tại bước này là trạng thái bình thường (chưa đăng nhập). Chỉ hiển thị thông báo khi
        // tài khoản thực sự có vấn đề: bị khoá hoặc đang chờ duyệt.
        if (error instanceof ApiError && !error.isUnauthorized && error.status > 0 && error.status < 500)
          setEndedReason(error.message)
      }
    })()
    return () => {
      alive = false
    }
  }, [loadUser])

  // Mọi 401 trong ứng dụng đều đi qua đây; phiên mất hiệu lực chỉ chuyển về màn đăng nhập một lần.
  useEffect(
    () =>
      onSessionLost(() => {
        setStatus((prev) => {
          if (prev === 'anonymous') return prev
          setEndedReason('Phiên đăng nhập đã kết thúc. Vui lòng đăng nhập lại.')
          setProfile(null)
          setUser(null)
          return 'anonymous'
        })
      }),
    [],
  )

  useEffect(() => {
    if (status !== 'ready') return
    const timer = window.setInterval(() => {
      void session.heartbeat().catch(() => undefined)
    }, HEARTBEAT_MS)
    return () => window.clearInterval(timer)
  }, [status])

  const signIn = useCallback(
    async (username: string, password: string) => {
      const account = await session.login(username, password)
      const next = await session.fetchAccessProfile()
      setUser(account)
      setProfile(next)
      setEndedReason(null)
      setStatus('ready')
      return next
    },
    [],
  )

  const adoptSession = useCallback(async () => {
    const next = await session.fetchAccessProfile()
    setProfile(next)
    setEndedReason(null)
    setStatus('ready')
    void loadUser()
    return next
  }, [loadUser])

  const signOut = useCallback(async () => {
    try {
      await session.logout()
    } catch {
      /* Máy chủ lỗi vẫn phải dọn trạng thái phía trình duyệt. */
    }
    setProfile(null)
    setUser(null)
    setEndedReason(null)
    setStatus('anonymous')
  }, [])

  const value = useMemo<AuthValue>(
    () => ({
      status,
      profile,
      user,
      endedReason,
      signIn,
      adoptSession,
      signOut,
      refreshProfile,
      refreshUser: loadUser,
      can: (permission?: string) => can(profile, permission),
      canAny: (permissions?: readonly string[]) => canAny(profile, permissions),
    }),
    [status, profile, user, endedReason, signIn, adoptSession, signOut, refreshProfile, loadUser],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const value = useContext(AuthContext)
  if (!value) throw new Error('useAuth phải nằm trong <AuthProvider>')
  return value
}
