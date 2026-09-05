import { Link } from 'react-router-dom'
import { useAuth } from '@/auth/AuthProvider'
import { buttonClass, KeyValue, PageHeader, Panel, Stack } from '@/ui'

/** Màn chờ trong lúc dò phiên khi mở trang. Giữ tối giản để không nháy bố cục. */
export function BootSplash() {
  return (
    <div className="grid min-h-dvh place-items-center bg-paper">
      <div className="flex flex-col items-center gap-3">
        <span aria-hidden className="h-0.5 w-10 animate-pulse bg-brand" />
        <p className="text-sm text-ink-3">Đang kiểm tra phiên đăng nhập</p>
      </div>
    </div>
  )
}

/** Màn hình không đủ quyền. Chỉ cho biết vai trò hiện tại, không in mã quyền. */
export function ForbiddenPage({ requires: _requires }: { requires?: string[] }) {
  const { profile } = useAuth()

  return (
    <Stack>
      <PageHeader title="Bạn chưa có quyền vào màn hình này" />
      <Panel padded>
        <KeyValue rows={[['Vai trò của bạn', profile?.roleLabels.join(', ') || null]]} />
      </Panel>
      <div>
        <Link to="/" className={buttonClass('default', 'sm')}>
          Về trang đích của tôi
        </Link>
      </div>
    </Stack>
  )
}

export function NotFoundPage() {
  return (
    <Stack>
      <PageHeader title="Không có màn hình ở địa chỉ này" />
      <div>
        <Link to="/" className={buttonClass('primary', 'sm')}>
          Về trang đích của tôi
        </Link>
      </div>
    </Stack>
  )
}
