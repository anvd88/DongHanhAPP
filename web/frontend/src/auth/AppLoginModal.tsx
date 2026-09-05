import { useCallback, useEffect, useRef, useState } from 'react'
import { CheckCircle2, CircleX, Smartphone, Timer } from 'lucide-react'
import { api } from '@/lib/http'
import { useAuth } from '@/auth/AuthProvider'
import { browserSid } from '@/auth/session'
import { Button, InlineAlert, Modal, Spinner } from '@/ui'

/**
 * Đăng nhập web trên điện thoại Android bằng chính ứng dụng Nhân sự đã đăng nhập: trình duyệt mở
 * ứng dụng qua intent, người dùng xác nhận trong ứng dụng, trình duyệt nhận cookie phiên qua
 * vòng hỏi trạng thái.
 */

const CLIENT_MODE = 'mobile_app'
const POLL_INTERVAL_MS = 1_000

interface AppLoginSession {
  requestCode: string
  pollToken: string
  expiresAt: string
  clientMode: typeof CLIENT_MODE
}

type AppLoginPollResult = {
  status: 'pending' | 'opened' | 'rejected' | 'expired' | 'authenticated'
  expiresAt?: string
}

type Phase = 'starting' | 'waiting' | 'opened' | 'authenticated' | 'rejected' | 'expired' | 'error'

export function AppLoginModal({ onClose, onAuthenticated }: { onClose: () => void; onAuthenticated: () => void }) {
  const { adoptSession } = useAuth()
  const [session, setSession] = useState<AppLoginSession | null>(null)
  const [phase, setPhase] = useState<Phase>('starting')
  const [error, setError] = useState('')
  const [secondsLeft, setSecondsLeft] = useState(5 * 60)
  const pollTokenRef = useRef<string | null>(null)

  const openApp = useCallback((requestCode: string) => {
    const request = encodeURIComponent(requestCode)
    const mode = encodeURIComponent(CLIENT_MODE)
    const fallback = encodeURIComponent(new URL('/tai-apk', window.location.origin).toString())
    // Gửi mã ở cả địa chỉ lẫn phần dữ liệu kèm theo của intent: một số bản Chrome làm mất phần truy vấn.
    window.location.href = `intent://app-login?request=${request}&client_mode=${mode}#Intent;scheme=ketoanhr;package=com.ketoanapk.hr;S.request_code=${request};S.client_mode=${mode};S.browser_fallback_url=${fallback};end`
  }, [])

  useEffect(() => {
    let stopped = false
    void api
      .post<AppLoginSession>('/auth/app-login/start', { sid: browserSid(), clientMode: CLIENT_MODE }, { quiet: true })
      .then((created) => {
        if (stopped) {
          void api
            .post('/auth/app-login/cancel', { pollToken: created.pollToken, clientMode: CLIENT_MODE }, { quiet: true })
            .catch(() => {})
          return
        }
        pollTokenRef.current = created.pollToken
        setSession(created)
        setPhase('waiting')
        openApp(created.requestCode)
      })
      .catch((err) => {
        if (stopped) return
        setError(err instanceof Error ? err.message : 'Không tạo được yêu cầu đăng nhập ứng dụng.')
        setPhase('error')
      })
    return () => {
      stopped = true
    }
  }, [openApp])

  const terminal = phase === 'authenticated' || phase === 'rejected' || phase === 'expired'

  useEffect(() => {
    if (!session || terminal) return
    const expiresAt = new Date(session.expiresAt).getTime()
    const update = () => setSecondsLeft(Math.max(0, Math.ceil((expiresAt - Date.now()) / 1_000)))
    update()
    const timer = window.setInterval(update, 250)
    return () => window.clearInterval(timer)
  }, [session, terminal])

  useEffect(() => {
    if (!session || terminal) return
    let stopped = false
    let timer: number | undefined
    const controller = new AbortController()

    const poll = async () => {
      try {
        const result = await api.post<AppLoginPollResult>(
          '/auth/app-login/poll',
          { pollToken: session.pollToken, clientMode: CLIENT_MODE },
          { signal: controller.signal, quiet: true },
        )
        if (stopped) return
        if (result.status === 'authenticated') {
          void api
            .post('/auth/app-login/ack', { pollToken: session.pollToken, clientMode: CLIENT_MODE }, { quiet: true })
            .catch(() => {})
          pollTokenRef.current = null
          setPhase('authenticated')
          await adoptSession()
          onAuthenticated()
          return
        }
        if (result.status === 'opened') setPhase('opened')
        if (result.status === 'rejected') {
          setPhase('rejected')
          return
        }
        if (result.status === 'expired') {
          setPhase('expired')
          return
        }
        timer = window.setTimeout(poll, POLL_INTERVAL_MS)
      } catch (err) {
        if (stopped || controller.signal.aborted) return
        setError(err instanceof Error ? err.message : 'Mất kết nối tới máy chủ.')
        timer = window.setTimeout(poll, 1_500)
      }
    }
    timer = window.setTimeout(poll, 250)
    return () => {
      stopped = true
      controller.abort()
      if (timer !== undefined) window.clearTimeout(timer)
    }
  }, [adoptSession, onAuthenticated, session, terminal])

  const close = useCallback(() => {
    const token = pollTokenRef.current
    pollTokenRef.current = null
    if (token)
      void api.post('/auth/app-login/cancel', { pollToken: token, clientMode: CLIENT_MODE }, { quiet: true }).catch(() => {})
    onClose()
  }, [onClose])

  const minutes = String(Math.floor(secondsLeft / 60)).padStart(2, '0')
  const seconds = String(secondsLeft % 60).padStart(2, '0')

  const body = {
    starting: { icon: <Spinner className="size-6 text-ink-3" />, title: 'Đang mở ứng dụng Nhân sự', sub: 'Xác nhận trong ứng dụng để đăng nhập trình duyệt này.' },
    waiting: { icon: <Spinner className="size-6 text-ink-3" />, title: 'Đang mở ứng dụng Nhân sự', sub: 'Xác nhận trong ứng dụng để đăng nhập trình duyệt này.' },
    opened: { icon: <Smartphone className="size-7 text-brand" strokeWidth={1.6} />, title: 'Ứng dụng đã nhận yêu cầu', sub: 'Chọn Đăng nhập trong ứng dụng Nhân sự.' },
    authenticated: { icon: <CheckCircle2 className="size-7 text-ok" strokeWidth={1.6} />, title: 'Đăng nhập thành công', sub: '' },
    rejected: { icon: <CircleX className="size-7 text-danger" strokeWidth={1.6} />, title: 'Bạn đã từ chối yêu cầu', sub: '' },
    expired: { icon: <Timer className="size-7 text-ink-3" strokeWidth={1.6} />, title: 'Yêu cầu đã hết hạn', sub: '' },
    error: { icon: <CircleX className="size-7 text-danger" strokeWidth={1.6} />, title: 'Không mở được đăng nhập ứng dụng', sub: '' },
  }[phase]

  return (
    <Modal
      open
      onClose={close}
      title="Đăng nhập bằng ứng dụng Nhân sự"
      size="sm"
      footer={
        session && !terminal && phase !== 'error' ? (
          <>
            <span className="tnum mr-auto text-xs text-ink-3">Hiệu lực {minutes}:{seconds}</span>
            <Button variant="primary" size="sm" onClick={() => openApp(session.requestCode)} icon={<Smartphone className="size-3.5" strokeWidth={1.7} />}>
              Mở lại ứng dụng
            </Button>
          </>
        ) : (
          <Button size="sm" onClick={close}>
            Đóng
          </Button>
        )
      }
    >
      <div className="flex flex-col items-center gap-2 py-3 text-center" aria-live="polite">
        {body.icon}
        <p className="text-sm font-medium text-ink">{body.title}</p>
        {body.sub && <p className="text-xs text-ink-3">{body.sub}</p>}
      </div>
      {error && <InlineAlert tone="danger">{error}</InlineAlert>}
    </Modal>
  )
}
