import { useCallback, useEffect, useRef, useState } from 'react'
import { ArrowLeft, RefreshCw } from 'lucide-react'
import { QRCodeSVG } from 'qrcode.react'
import { ApiError, api } from '@/lib/http'
import { cn } from '@/lib/cn'
import { useAuth } from '@/auth/AuthProvider'
import { browserSid } from '@/auth/session'
import { Avatar, Button, InlineAlert, Spinner } from '@/ui'

/**
 * Bảng đăng nhập bằng mã QR, hiển thị trong thẻ đăng nhập thay cho ô tài khoản/mật khẩu.
 * Điện thoại đã đăng nhập quét mã rồi xác nhận; trình duyệt hỏi trạng thái theo nhịp và nhận
 * cookie phiên ngay trong phản hồi "authenticated". Không có token nào đi qua JavaScript.
 */

interface QrSessionResponse {
  qrCode: string
  pollToken: string
  expiresAt: string
  clientMode: 'desktop_qr'
}

type QrSession = QrSessionResponse & {
  /** Mốc hết hạn theo đồng hồ máy này, đã chặn trường hợp lệch giờ với máy chủ. */
  deadline: number
}

interface QrAccount {
  username: string
  fullName: string
  avatarUrl?: string | null
}

type QrPollResult =
  | { status: 'pending' | 'expired' | 'rejected' }
  | { status: 'scanned'; account: QrAccount }
  | { status: 'authenticated' }

const QR_LIFETIME_SECONDS = 5 * 60
const POLL_INTERVAL_MS = 1_500

export function QrLoginPanel({
  onBack,
  onAuthenticated,
}: {
  onBack: () => void
  onAuthenticated: () => void
}) {
  const { adoptSession } = useAuth()
  const [session, setSession] = useState<QrSession | null>(null)
  const [secondsLeft, setSecondsLeft] = useState(QR_LIFETIME_SECONDS)
  const [loading, setLoading] = useState(true)
  const [expired, setExpired] = useState(false)
  const [error, setError] = useState('')
  const [confirmed, setConfirmed] = useState(false)
  const [rejected, setRejected] = useState(false)
  const [scannedAccount, setScannedAccount] = useState<QrAccount | null>(null)
  const pollTokenRef = useRef<string | null>(null)
  const avatarLookupTokenRef = useRef<string | null>(null)
  const generationRef = useRef(0)

  const cancelToken = useCallback(async (token: string | null) => {
    if (!token) return
    await api.post('/auth/qr/cancel', { pollToken: token }, { quiet: true }).catch(() => {})
  }, [])

  const refresh = useCallback(async () => {
    const generation = ++generationRef.current
    const oldToken = pollTokenRef.current
    pollTokenRef.current = null
    avatarLookupTokenRef.current = null
    setLoading(true)
    setError('')
    setExpired(false)
    setConfirmed(false)
    setRejected(false)
    setScannedAccount(null)
    setSession(null)
    setSecondsLeft(QR_LIFETIME_SECONDS)

    try {
      // Huỷ phiên cũ trước khi tạo phiên mới để điện thoại không quét nhầm mã trình duyệt đã bỏ.
      await cancelToken(oldToken)
      if (generation !== generationRef.current) return

      const created = await api.post<QrSessionResponse>(
        '/auth/qr/start',
        { sid: browserSid(), clientMode: 'desktop_qr' },
        { quiet: true },
      )
      if (generation !== generationRef.current) {
        void cancelToken(created.pollToken)
        return
      }
      pollTokenRef.current = created.pollToken
      // Đếm ngược theo mốc của máy chủ khi mốc đó hợp lý so với đồng hồ máy này; máy lệch giờ thì
      // lấy trọn vòng đời tính từ lúc nhận mã. Phán quyết cuối cùng vẫn của máy chủ.
      const serverDeadline = new Date(created.expiresAt).getTime()
      const localDeadline = Date.now() + QR_LIFETIME_SECONDS * 1_000
      const trustServer =
        Number.isFinite(serverDeadline) && serverDeadline > Date.now() && serverDeadline <= localDeadline
      setSession({ ...created, deadline: trustServer ? serverDeadline : localDeadline })
    } catch (err) {
      if (generation === generationRef.current)
        setError(err instanceof Error ? err.message : 'Không tạo được mã QR.')
    } finally {
      if (generation === generationRef.current) setLoading(false)
    }
  }, [cancelToken])

  useEffect(() => {
    const startTimer = window.setTimeout(() => void refresh(), 0)
    return () => {
      window.clearTimeout(startTimer)
      generationRef.current += 1
      void cancelToken(pollTokenRef.current)
      pollTokenRef.current = null
    }
  }, [cancelToken, refresh])

  useEffect(() => {
    if (!session || confirmed || rejected || expired) return
    const tick = () => {
      const left = Math.max(0, Math.ceil((session.deadline - Date.now()) / 1_000))
      setSecondsLeft(left)
      // Đồng hồ chạy hết là hết hạn, không chờ máy chủ: mất mạng thì vẫn có đường tạo lại mã.
      if (left === 0) setExpired(true)
    }
    tick()
    const timer = window.setInterval(tick, 250)
    return () => window.clearInterval(timer)
  }, [confirmed, expired, rejected, session])

  useEffect(() => {
    if (!session || expired || confirmed || rejected) return
    const generation = generationRef.current
    const controller = new AbortController()
    let stopped = false
    let terminal = false
    let timer: number | undefined

    const poll = async () => {
      let retryDelay = POLL_INTERVAL_MS
      try {
        const result = await api.post<QrPollResult>(
          '/auth/qr/poll',
          { pollToken: session.pollToken },
          { signal: controller.signal, quiet: true },
        )
        if (stopped || generation !== generationRef.current) return

        if (result.status === 'authenticated') {
          terminal = true
          void api.post('/auth/qr/ack', { pollToken: session.pollToken }, { quiet: true }).catch(() => {})
          pollTokenRef.current = null
          setConfirmed(true)
          await adoptSession()
          onAuthenticated()
          return
        }
        if (result.status === 'expired') {
          terminal = true
          setScannedAccount(null)
          setExpired(true)
          setError('')
          return
        }
        if (result.status === 'rejected') {
          terminal = true
          setScannedAccount(null)
          setRejected(true)
          setError('')
          return
        }
        if (result.status === 'scanned') {
          setScannedAccount((current) =>
            current?.username === result.account.username
              ? { ...result.account, avatarUrl: current.avatarUrl }
              : result.account,
          )
          // Ảnh đại diện lấy riêng đúng một lần cho mỗi phiên để vòng hỏi luôn nhẹ.
          if (avatarLookupTokenRef.current !== session.pollToken) {
            avatarLookupTokenRef.current = session.pollToken
            void api
              .post<{ avatarUrl?: string | null }>(
                '/auth/qr/account',
                { pollToken: session.pollToken },
                { signal: controller.signal, quiet: true },
              )
              .then(({ avatarUrl }) => {
                if (stopped || generation !== generationRef.current) return
                setScannedAccount((current) =>
                  current?.username === result.account.username ? { ...current, avatarUrl: avatarUrl ?? null } : current,
                )
              })
              .catch(() => {})
          }
        } else {
          setScannedAccount(null)
        }
        setError('')
      } catch (err) {
        if (stopped || controller.signal.aborted || generation !== generationRef.current) return
        setError(err instanceof Error ? err.message : 'Mất kết nối tới máy chủ.')
        if (err instanceof ApiError && err.status === 401) retryDelay = 250
      } finally {
        if (!stopped && !terminal && generation === generationRef.current)
          timer = window.setTimeout(poll, retryDelay)
      }
    }

    timer = window.setTimeout(poll, 250)
    return () => {
      stopped = true
      controller.abort()
      if (timer !== undefined) window.clearTimeout(timer)
    }
  }, [adoptSession, confirmed, expired, onAuthenticated, rejected, session])

  const back = () => {
    generationRef.current += 1
    void cancelToken(pollTokenRef.current)
    pollTokenRef.current = null
    onBack()
  }

  const minutes = String(Math.floor(secondsLeft / 60)).padStart(2, '0')
  const seconds = String(secondsLeft % 60).padStart(2, '0')
  const progress = Math.max(0, Math.min(100, (secondsLeft / QR_LIFETIME_SECONDS) * 100))
  const scannedName = scannedAccount?.fullName.trim() || scannedAccount?.username || ''
  const canRenew = !confirmed && (expired || rejected || (!loading && !session))
  const active = !confirmed && !rejected && !expired

  return (
    <div className="flex flex-col gap-4">
      <div
        className="mx-auto grid size-[232px] place-items-center rounded-md border border-line bg-white p-2"
        aria-live="polite"
      >
        {confirmed ? (
          <StateText title="Đăng nhập thành công" tone="ok" />
        ) : rejected ? (
          <StateText title="Đã từ chối đăng nhập" tone="danger" />
        ) : expired ? (
          <StateText title="Mã QR đã hết hạn" />
        ) : scannedAccount ? (
          <div className="flex flex-col items-center gap-2 px-3 text-center">
            <Avatar url={scannedAccount.avatarUrl} name={scannedName} size="lg" />
            <p className="text-sm font-medium text-ink">{scannedName}</p>
            <p className="text-xs text-ink-3">Chọn Đăng nhập trên điện thoại để hoàn tất.</p>
          </div>
        ) : loading ? (
          <Spinner className="size-5 text-ink-3" />
        ) : session ? (
          <QRCodeSVG value={session.qrCode} size={212} level="M" marginSize={0} title="Mã QR đăng nhập web" />
        ) : (
          <StateText title="Chưa tạo được mã QR" />
        )}
      </div>

      {active && session && (
        <div>
          <div className="flex items-center justify-between text-xs text-ink-3">
            <span>{scannedAccount ? 'Đang chờ xác nhận trên điện thoại' : 'Đang chờ quét mã'}</span>
            <span className={cn('tnum font-medium', secondsLeft <= 30 ? 'text-danger' : 'text-ink-2')}>
              {minutes}:{seconds}
            </span>
          </div>
          <div className="mt-1.5 h-0.5 w-full bg-line" aria-hidden>
            <div className="h-full bg-brand" style={{ width: `${progress}%` }} />
          </div>
        </div>
      )}

      {active && !scannedAccount && (
        <p className="text-xs text-ink-2">
          Mở ứng dụng Nhân sự đã đăng nhập, vào <strong className="font-medium text-ink">Cài đặt</strong>, chọn{' '}
          <strong className="font-medium text-ink">Đăng nhập web bằng QR</strong> rồi quét mã.
        </p>
      )}

      {error && <InlineAlert tone="danger">{error}</InlineAlert>}

      {canRenew && (
        <Button
          type="button"
          className="h-9 w-full"
          onClick={() => void refresh()}
          loading={loading}
          icon={<RefreshCw className="size-4" strokeWidth={1.7} />}
        >
          Tạo mã QR mới
        </Button>
      )}

      <div className="border-t border-line-2 pt-3">
        <Button type="button" variant="link" size="sm" icon={<ArrowLeft className="size-3.5" strokeWidth={1.8} />} onClick={back}>
          Quay lại đăng nhập tài khoản
        </Button>
      </div>
    </div>
  )
}

function StateText({ title, tone }: { title: string; tone?: 'ok' | 'danger' }) {
  return (
    <p className={cn('px-4 text-center text-sm font-medium', tone === 'ok' ? 'text-ok' : tone === 'danger' ? 'text-danger' : 'text-ink-2')}>
      {title}
    </p>
  )
}
