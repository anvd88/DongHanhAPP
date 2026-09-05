import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { useAuth } from './AuthProvider'
import { BootSplash, ForbiddenPage } from '@/pages/system-states'

/** Chưa đăng nhập: chuyển về màn đăng nhập và ghi lại địa chỉ đích để quay lại sau. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { status } = useAuth()
  const location = useLocation()

  if (status === 'loading') return <BootSplash />
  if (status === 'anonymous')
    return <Navigate to="/dang-nhap" replace state={{ from: location.pathname }} />
  return <>{children}</>
}

/**
 * Chốt quyền ở tầng giao diện, chỉ nhằm tránh mở màn hình rồi nhận lỗi từ API. Chốt quyền thật
 * nằm ở endpoint và được dựng lại từ CSDL ở mỗi request.
 */
export function RequirePermission({
  requires,
  requiresAny,
  children,
}: {
  requires?: string
  requiresAny?: string[]
  children: ReactNode
}) {
  const auth = useAuth()
  if (!auth.can(requires) || !auth.canAny(requiresAny))
    return <ForbiddenPage requires={requires ? [requires] : requiresAny} />
  return <>{children}</>
}

/** Trang gốc "/" chuyển tới landingPath do máy chủ xác định theo hồ sơ truy cập. */
export function LandingRedirect() {
  const { status, profile } = useAuth()
  if (status === 'loading') return <BootSplash />
  if (status === 'anonymous') return <Navigate to="/dang-nhap" replace />
  return <Navigate to={profile?.landingPath || '/nhan-su'} replace />
}
