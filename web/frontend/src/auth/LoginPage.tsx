import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { ArrowLeft, CheckCircle2, Eye, EyeOff, Moon, QrCode, Smartphone, Sun } from 'lucide-react'
import { ApiError, api } from '@/lib/http'
import { applyTheme, readTheme } from '@/lib/theme'
import { cn } from '@/lib/cn'
import { Button, Field, InlineAlert, Input, useFieldContext } from '@/ui'
import { useAuth } from './AuthProvider'
import { browserSid } from './session'
import { AppLoginModal } from './AppLoginModal'
import { QrLoginPanel } from './QrLoginPanel'
import { RecoveryOtpField, type OtpStatus } from './RecoveryOtpField'

/**
 * Màn đăng nhập theo hệ giao diện phần mềm kế toán: một thẻ căn giữa trên nền xám nhạt, không
 * ảnh nền, không hoạt cảnh mở màn. Ba cảnh dùng chung một thẻ:
 *   · account — tên đăng nhập và mật khẩu;
 *   · qr      — quét mã bằng ứng dụng Nhân sự đã đăng nhập (máy tính);
 *   · recover — khôi phục mật khẩu ba bước: tên đăng nhập → mã khôi phục → mật khẩu mới.
 *
 * Đăng nhập gồm hai bước (xin vé phiên bảo mật rồi mới gửi mật khẩu). Vé có thể hết hạn nếu biểu
 * mẫu mở quá lâu; khi đó máy chủ trả mã `login_bootstrap_required` và ở đây thử lại đúng một lần.
 */

const APP_BRAND_NAME = 'KetoanMini'

type LoginMode = 'account' | 'qr' | 'recover'
type BootstrapState = 'initializing' | 'ready' | 'error'

interface LoginBootstrapResponse {
  ready: boolean
  expiresAt: string
  protocol: string
}

type RecoverStep = 'username' | 'code' | 'password'
const RECOVER_STEP_ORDER: RecoverStep[] = ['username', 'code', 'password']

/** Mã khôi phục do quản trị viên cấp dài 5 ký tự, chữ hoa và số. */
const RECOVERY_CODE_LENGTH = 5
const RECOVERY_RESEND_SECONDS = 60
const emptyRecoveryDigits = () => Array<string>(RECOVERY_CODE_LENGTH).fill('')

export function LoginPage() {
  const { signIn, endedReason } = useAuth()
  const navigate = useNavigate()
  const [theme, setTheme] = useState(() => (document.documentElement.dataset.theme === 'dark' ? 'dark' : 'light'))

  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const [loginSuccess, setLoginSuccess] = useState(false)
  const [bootstrapState, setBootstrapState] = useState<BootstrapState>('initializing')
  const [bootstrapError, setBootstrapError] = useState('')
  const bootstrapRequestIdRef = useRef(0)
  const [mode, setMode] = useState<LoginMode>('account')
  const [appLoginOpen, setAppLoginOpen] = useState(false)

  // Khôi phục mật khẩu chạy trong cùng thẻ, không mở hộp thoại.
  const [recoverStep, setRecoverStep] = useState<RecoverStep>('username')
  const [recoverDigits, setRecoverDigits] = useState<string[]>(emptyRecoveryDigits)
  const [codeStatus, setCodeStatus] = useState<OtpStatus>('idle')
  const [codeFocusKey, setCodeFocusKey] = useState(0)
  const [resendLeft, setResendLeft] = useState(RECOVERY_RESEND_SECONDS)
  const [resendHint, setResendHint] = useState('')
  const [resendBusy, setResendBusy] = useState(false)
  const [recoverPassword, setRecoverPassword] = useState('')
  const [recoverConfirm, setRecoverConfirm] = useState('')
  const [recoverError, setRecoverError] = useState('')
  const [recoverLoading, setRecoverLoading] = useState(false)
  const [recoverDone, setRecoverDone] = useState(false)
  const recoverTimersRef = useRef<number[]>([])
  const recoverCode = recoverDigits.join('')

  const requestedClientMode = new URLSearchParams(window.location.search).get('client_mode')
  const isAndroidMobile =
    requestedClientMode === 'mobile_app' ||
    (requestedClientMode !== 'desktop_qr' && /Android/i.test(navigator.userAgent))
  const autoFocusUsername = window.matchMedia('(hover: hover) and (pointer: fine)').matches

  const toggleTheme = () => {
    const next = theme === 'light' ? 'dark' : 'light'
    applyTheme(next)
    setTheme(next)
  }

  useEffect(() => {
    if (readTheme() === 'system')
      setTheme(document.documentElement.dataset.theme === 'dark' ? 'dark' : 'light')
  }, [])

  const initializeSecureSession = useCallback(async (signal?: AbortSignal) => {
    const requestId = bootstrapRequestIdRef.current + 1
    bootstrapRequestIdRef.current = requestId
    setBootstrapState('initializing')
    setBootstrapError('')

    const requestController = new AbortController()
    let timedOut = false
    const abortFromCaller = () => requestController.abort()
    signal?.addEventListener('abort', abortFromCaller, { once: true })
    const timeoutId = window.setTimeout(() => {
      timedOut = true
      requestController.abort()
    }, 10_000)

    try {
      const response = await api.post<LoginBootstrapResponse>(
        '/auth/bootstrap',
        { sid: browserSid() },
        { signal: requestController.signal, quiet: true },
      )
      if (signal?.aborted || requestId !== bootstrapRequestIdRef.current) return false
      if (!response.ready || response.protocol !== 'preauth-v1')
        throw new Error('Máy chủ trả về phiên khởi tạo không hợp lệ.')
      setBootstrapState('ready')
      return true
    } catch (err) {
      if (signal?.aborted || requestId !== bootstrapRequestIdRef.current) return false
      setBootstrapState('error')
      setBootstrapError(
        timedOut
          ? 'Máy chủ phản hồi quá lâu. Kiểm tra kết nối rồi thử lại.'
          : err instanceof Error
            ? err.message
            : 'Không kết nối được máy chủ.',
      )
      return false
    } finally {
      window.clearTimeout(timeoutId)
      signal?.removeEventListener('abort', abortFromCaller)
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    // Đẩy sang microtask để lần chạy thăm dò của StrictMode dọn dẹp trước, không phát hai vé.
    queueMicrotask(() => {
      if (!controller.signal.aborted) void initializeSecureSession(controller.signal)
    })
    return () => controller.abort()
  }, [initializeSecureSession])

  const clearRecoverTimers = () => {
    recoverTimersRef.current.forEach((id) => window.clearTimeout(id))
    recoverTimersRef.current = []
  }
  const scheduleRecover = (run: () => void, delay: number) => {
    recoverTimersRef.current.push(window.setTimeout(run, delay))
  }
  useEffect(() => clearRecoverTimers, [])

  const goRecoverStep = (next: RecoverStep) => {
    setRecoverStep(next)
    setRecoverError('')
    if (next === 'code') {
      setResendLeft(RECOVERY_RESEND_SECONDS)
      setResendHint('')
      setCodeStatus('idle')
      setRecoverDigits(emptyRecoveryDigits())
      setCodeFocusKey((key) => key + 1)
    }
  }

  const openRecover = () => {
    clearRecoverTimers()
    setRecoverStep('username')
    setRecoverDigits(emptyRecoveryDigits())
    setCodeStatus('idle')
    setResendLeft(RECOVERY_RESEND_SECONDS)
    setResendHint('')
    setRecoverPassword('')
    setRecoverConfirm('')
    setRecoverError('')
    setRecoverDone(false)
    setMode('recover')
  }

  const submitRecoverUsername = (event: FormEvent) => {
    event.preventDefault()
    if (!username.trim()) {
      setRecoverError('Nhập tên đăng nhập cần đặt lại mật khẩu.')
      return
    }
    goRecoverStep('code')
  }

  // Bước 2 chỉ kiểm tra mã. Giữ hoạt cảnh tối thiểu ~0,9 giây để người dùng kịp thấy trạng thái;
  // mạng nội bộ trả lời quá nhanh thì hiệu ứng chớp qua, nhìn như giật.
  const verifyRecoveryCode = async (code: string) => {
    setCodeStatus('verifying')
    setRecoverError('')
    setResendHint('')
    const startedAt = Date.now()
    const holdAnimation = async (minimum: number) => {
      const remain = minimum - (Date.now() - startedAt)
      if (remain > 0)
        await new Promise((resolve) => {
          scheduleRecover(() => resolve(null), remain)
        })
    }
    try {
      await api.post('/auth/verify-recovery-code', { username: username.trim(), code })
      await holdAnimation(900)
      setCodeStatus('success')
      scheduleRecover(() => goRecoverStep('password'), 1050)
    } catch (err) {
      await holdAnimation(700)
      setCodeStatus('error')
      setRecoverError(err instanceof Error ? err.message : 'Mã khôi phục không đúng hoặc đã hết hạn.')
      scheduleRecover(() => {
        setRecoverDigits(emptyRecoveryDigits())
        setCodeStatus('idle')
        setCodeFocusKey((key) => key + 1)
      }, 1480)
    }
  }

  // Nhập đủ 5 ký tự là tự xác thực; bước này cố ý không có nút xác nhận.
  useEffect(() => {
    if (mode !== 'recover' || recoverStep !== 'code' || recoverDone) return
    if (codeStatus !== 'idle' || recoverCode.length < RECOVERY_CODE_LENGTH) return
    void verifyRecoveryCode(recoverCode)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mode, recoverStep, recoverDone, codeStatus, recoverCode])

  useEffect(() => {
    if (mode !== 'recover' || recoverStep !== 'code' || recoverDone) return
    const id = window.setInterval(() => setResendLeft((left) => (left <= 0 ? 0 : left - 1)), 1000)
    return () => window.clearInterval(id)
  }, [mode, recoverStep, recoverDone])

  /**
   * Xin mã mới. Máy chủ tự gửi khi đã bật một kênh (thư điện tử hoặc Zalo); chưa bật kênh nào thì nó
   * trả về "channel_unavailable" và ở đây chỉ còn cách nhắc liên hệ quản trị viên — đúng như hệ
   * thống đang chạy hôm nay. Bật kênh trong cấu hình là nút này gửi thật, không phải sửa lại màn hình.
   */
  const requestNewRecoveryCode = async () => {
    setResendLeft(RECOVERY_RESEND_SECONDS)
    setResendHint('')
    setResendBusy(true)
    try {
      const result = await api.post<{ message?: string }>('/auth/request-recovery-code', {
        username: username.trim(),
      })
      setResendHint(result?.message || 'Đã gửi mã khôi phục nếu tài khoản có thông tin liên hệ.')
    } catch (error) {
      setResendHint(
        error instanceof ApiError && error.message
          ? error.message
          : 'Mã khôi phục do quản trị viên cấp trực tiếp. Liên hệ quản trị viên để nhận mã mới.',
      )
    } finally {
      setResendBusy(false)
    }
  }

  const submitRecover = async (event: FormEvent) => {
    event.preventDefault()
    setRecoverError('')
    if (!username.trim()) {
      setRecoverError('Nhập tên đăng nhập cần đặt lại mật khẩu.')
      return
    }
    if (recoverCode.length < RECOVERY_CODE_LENGTH) {
      goRecoverStep('code')
      return
    }
    if (recoverPassword.length < 6) {
      setRecoverError('Mật khẩu mới cần ít nhất 6 ký tự.')
      return
    }
    if (recoverPassword !== recoverConfirm) {
      setRecoverError('Xác nhận mật khẩu không khớp.')
      return
    }
    setRecoverLoading(true)
    try {
      await api.post('/auth/reset-with-recovery-code', {
        username: username.trim(),
        code: recoverCode,
        newPassword: recoverPassword,
      })
      setRecoverDone(true)
      setRecoverDigits(emptyRecoveryDigits())
      setRecoverPassword('')
      setRecoverConfirm('')
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Không đặt lại được mật khẩu.'
      // Mã hết hạn hoặc bị dùng mất trong lúc đặt mật khẩu thì đưa về bước nhập mã.
      if (/mã khôi phục/i.test(message)) goRecoverStep('code')
      setRecoverError(message)
    } finally {
      setRecoverLoading(false)
    }
  }

  const goToApp = useCallback(
    (landingPath?: string) => {
      setLoginSuccess(true)
      navigate(landingPath || '/', { replace: true })
    },
    [navigate],
  )

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError('')
    setLoading(true)
    let authenticated = false
    try {
      let profile
      try {
        profile = await signIn(username.trim(), password)
      } catch (first) {
        if (first instanceof ApiError && first.code === 'login_bootstrap_required')
          profile = await signIn(username.trim(), password)
        else throw first
      }
      authenticated = true
      goToApp(profile.landingPath)
    } catch (err) {
      setError(
        err instanceof ApiError
          ? err.status === 429
            ? 'Bạn đã thử quá nhiều lần. Đợi một phút rồi đăng nhập lại.'
            : err.message
          : 'Đăng nhập thất bại.',
      )
    } finally {
      if (!authenticated) setLoading(false)
    }
  }

  const recoverStepIndex = RECOVER_STEP_ORDER.indexOf(recoverStep)
  const shownError = error || endedReason || ''

  return (
    <main className="flex min-h-dvh flex-col bg-paper" aria-busy={loginSuccess}>
      <button
        type="button"
        onClick={toggleTheme}
        aria-label={`Chuyển sang giao diện ${theme === 'light' ? 'tối' : 'sáng'}`}
        title={`Chuyển sang giao diện ${theme === 'light' ? 'tối' : 'sáng'}`}
        className="fixed top-3 right-3 grid size-8 place-items-center rounded-md border border-line bg-panel text-ink-2 hover:bg-panel-2 hover:text-ink"
      >
        {theme === 'light' ? <Moon className="size-4" strokeWidth={1.7} /> : <Sun className="size-4" strokeWidth={1.7} />}
      </button>

      <div className="flex flex-1 items-center justify-center px-4 py-10">
        <div className="w-full max-w-[400px]">
          <div className="mb-5 flex items-center gap-2.5">
            <span aria-hidden className="grid size-8 place-items-center rounded-sm bg-brand text-on-brand">
              <svg viewBox="0 0 24 24" className="size-4.5" fill="none">
                <path d="M5 6.5h14M5 12h14M5 17.5h8" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" />
              </svg>
            </span>
            <span className="text-lg font-semibold tracking-tight text-ink">{APP_BRAND_NAME}</span>
          </div>

          <section className="panel" aria-label="Đăng nhập hệ thống">
            <div key={mode} style={{ animation: 'km-fade 0.16s var(--ease-out-soft)' }}>
              {mode === 'account' && (
                <div className="px-6 py-6">
                  <h1 className="text-base font-semibold text-ink">Đăng nhập</h1>

                  <form onSubmit={submit} className="mt-5 flex flex-col gap-4">
                    <Field label="Tên đăng nhập">
                      <Input
                        autoFocus={autoFocusUsername}
                        autoComplete="username"
                        value={username}
                        onChange={(event) => setUsername(event.target.value)}
                        required
                        className="h-9"
                      />
                    </Field>

                    <Field label="Mật khẩu">
                      <PasswordInput
                        value={password}
                        onChange={setPassword}
                        shown={showPassword}
                        onToggle={() => setShowPassword((value) => !value)}
                        autoComplete="current-password"
                      />
                    </Field>

                    {bootstrapState === 'error' && (
                      <InlineAlert
                        tone="danger"
                        title="Không mở được phiên bảo mật"
                        action={
                          <Button size="sm" onClick={() => void initializeSecureSession()}>
                            Thử lại
                          </Button>
                        }
                      >
                        {bootstrapError}
                      </InlineAlert>
                    )}

                    {shownError && <InlineAlert tone="danger">{shownError}</InlineAlert>}

                    <Button
                      type="submit"
                      variant="primary"
                      className="h-9 w-full"
                      loading={loading || bootstrapState === 'initializing'}
                      disabled={loginSuccess || bootstrapState !== 'ready'}
                      icon={loginSuccess ? <CheckCircle2 className="size-4" strokeWidth={1.8} /> : undefined}
                    >
                      {loginSuccess
                        ? 'Đăng nhập thành công'
                        : bootstrapState === 'initializing'
                          ? 'Đang kết nối máy chủ'
                          : 'Đăng nhập'}
                    </Button>

                    <div className="-mt-1 text-right">
                      <Button type="button" variant="link" size="sm" onClick={openRecover}>
                        Quên mật khẩu?
                      </Button>
                    </div>
                  </form>

                  <div className="my-4 flex items-center gap-3 text-xs text-ink-3">
                    <span className="h-px flex-1 bg-line" />
                    hoặc
                    <span className="h-px flex-1 bg-line" />
                  </div>

                  <Button
                    type="button"
                    className="h-9 w-full"
                    icon={
                      isAndroidMobile ? (
                        <Smartphone className="size-4" strokeWidth={1.7} />
                      ) : (
                        <QrCode className="size-4" strokeWidth={1.7} />
                      )
                    }
                    onClick={() => (isAndroidMobile ? setAppLoginOpen(true) : setMode('qr'))}
                  >
                    {isAndroidMobile ? 'Đăng nhập bằng ứng dụng Nhân sự' : 'Đăng nhập bằng mã QR'}
                  </Button>
                </div>
              )}

              {mode === 'qr' && (
                <div className="px-6 py-6">
                  <h1 className="text-base font-semibold text-ink">Đăng nhập bằng mã QR</h1>
                  <div className="mt-5">
                    <QrLoginPanel onBack={() => setMode('account')} onAuthenticated={() => goToApp()} />
                  </div>
                </div>
              )}

              {mode === 'recover' && (
                <div className="px-6 py-6">
                  <div className="flex items-baseline justify-between gap-3">
                    <h1 className="text-base font-semibold text-ink">
                      {recoverDone
                        ? 'Đặt lại mật khẩu'
                        : recoverStep === 'code'
                          ? 'Nhập mã khôi phục'
                          : recoverStep === 'password'
                            ? 'Đặt mật khẩu mới'
                            : 'Khôi phục mật khẩu'}
                    </h1>
                    {!recoverDone && (
                      <span className="tnum text-xs text-ink-3">Bước {recoverStepIndex + 1}/3</span>
                    )}
                  </div>
                  {!recoverDone && (
                    <div className="mt-3 grid grid-cols-3 gap-1" aria-hidden>
                      {RECOVER_STEP_ORDER.map((step, index) => (
                        <span
                          key={step}
                          className={cn('h-0.5 rounded-full', index <= recoverStepIndex ? 'bg-brand' : 'bg-line')}
                        />
                      ))}
                    </div>
                  )}

                  <div className="mt-5">
                    {recoverDone ? (
                      <div className="flex flex-col items-center gap-2 py-4 text-center">
                        <CheckCircle2 className="size-8 text-ok" strokeWidth={1.6} />
                        <p className="text-sm font-medium text-ink">Đã đặt lại mật khẩu</p>
                        <p className="text-xs text-ink-3">Mọi phiên đăng nhập cũ của tài khoản đã bị thu hồi.</p>
                        <Button variant="primary" className="mt-3 h-9 w-full" onClick={() => setMode('account')}>
                          Đăng nhập ngay
                        </Button>
                      </div>
                    ) : recoverStep === 'username' ? (
                      <form onSubmit={submitRecoverUsername} className="flex flex-col gap-4">
                        <Field label="Tên đăng nhập">
                          <Input
                            autoFocus
                            autoComplete="username"
                            value={username}
                            onChange={(event) => setUsername(event.target.value)}
                            required
                            className="h-9"
                          />
                        </Field>
                        {recoverError && <InlineAlert tone="danger">{recoverError}</InlineAlert>}
                        <Button type="submit" variant="primary" className="h-9 w-full">
                          Tiếp tục
                        </Button>
                      </form>
                    ) : recoverStep === 'code' ? (
                      <div className="flex flex-col gap-4">
                        <p className="text-xs text-ink-2">
                          Mã khôi phục của tài khoản <strong className="font-medium text-ink">{username.trim()}</strong>.
                        </p>
                        <RecoveryOtpField
                          digits={recoverDigits}
                          onDigitsChange={setRecoverDigits}
                          status={codeStatus}
                          focusKey={codeFocusKey}
                        />
                        <p
                          className={cn(
                            'min-h-4 text-center text-xs',
                            codeStatus === 'error' ? 'text-danger' : codeStatus === 'success' ? 'text-ok' : 'text-ink-3',
                          )}
                          role="status"
                        >
                          {codeStatus === 'verifying'
                            ? 'Đang xác thực mã'
                            : codeStatus === 'success'
                              ? 'Xác thực thành công'
                              : codeStatus === 'error'
                                ? recoverError || 'Mã khôi phục không đúng'
                                : ''}
                        </p>
                        <div className="flex items-center justify-between gap-2 text-xs text-ink-3">
                          <span>Chưa có mã?</span>
                          {resendLeft > 0 ? (
                            <span className="tnum">Gửi lại sau {resendLeft} giây</span>
                          ) : (
                            <Button
                              type="button"
                              variant="link"
                              size="sm"
                              loading={resendBusy}
                              onClick={requestNewRecoveryCode}
                            >
                              Gửi lại mã
                            </Button>
                          )}
                        </div>
                        {resendHint && <InlineAlert tone="info">{resendHint}</InlineAlert>}
                      </div>
                    ) : (
                      <form onSubmit={submitRecover} className="flex flex-col gap-4">
                        <Field label="Mật khẩu mới" hint="Ít nhất 6 ký tự">
                          <PasswordInput
                            value={recoverPassword}
                            onChange={setRecoverPassword}
                            shown={showPassword}
                            onToggle={() => setShowPassword((value) => !value)}
                            autoComplete="new-password"
                            autoFocus
                          />
                        </Field>
                        <Field label="Nhập lại mật khẩu mới">
                          <PasswordInput
                            value={recoverConfirm}
                            onChange={setRecoverConfirm}
                            shown={showPassword}
                            onToggle={() => setShowPassword((value) => !value)}
                            autoComplete="new-password"
                          />
                        </Field>
                        {recoverError && <InlineAlert tone="danger">{recoverError}</InlineAlert>}
                        <Button type="submit" variant="primary" className="h-9 w-full" loading={recoverLoading}>
                          Đặt lại mật khẩu
                        </Button>
                      </form>
                    )}
                  </div>

                  <div className="mt-5 border-t border-line-2 pt-3">
                    <Button
                      type="button"
                      variant="link"
                      size="sm"
                      icon={<ArrowLeft className="size-3.5" strokeWidth={1.8} />}
                      disabled={codeStatus === 'verifying' || codeStatus === 'success'}
                      onClick={() => {
                        if (recoverDone || recoverStep === 'username') {
                          setMode('account')
                          return
                        }
                        clearRecoverTimers()
                        goRecoverStep(recoverStep === 'password' ? 'code' : 'username')
                      }}
                    >
                      {recoverDone || recoverStep === 'username' ? 'Quay lại đăng nhập' : 'Quay lại bước trước'}
                    </Button>
                  </div>
                </div>
              )}
            </div>
          </section>

          <p className="mt-4 text-center text-xs text-ink-3">© {new Date().getFullYear()} {APP_BRAND_NAME}</p>
        </div>
      </div>

      {appLoginOpen && <AppLoginModal onClose={() => setAppLoginOpen(false)} onAuthenticated={() => goToApp()} />}
    </main>
  )
}

/** Ô mật khẩu có nút hiện/ẩn, nằm trong Field để nhận id và trạng thái lỗi. */
function PasswordInput({
  value,
  onChange,
  shown,
  onToggle,
  autoComplete,
  autoFocus,
}: {
  value: string
  onChange: (value: string) => void
  shown: boolean
  onToggle: () => void
  autoComplete: string
  autoFocus?: boolean
}) {
  const ctx = useFieldContext()
  return (
    <div className="control flex h-9 items-center px-0" aria-invalid={ctx?.invalid || undefined}>
      <input
        id={ctx?.id}
        aria-describedby={ctx?.describedBy}
        type={shown ? 'text' : 'password'}
        autoComplete={autoComplete}
        autoFocus={autoFocus}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        required
        className="h-full min-w-0 flex-1 bg-transparent px-2 outline-none"
      />
      <button
        type="button"
        tabIndex={-1}
        onClick={onToggle}
        aria-label={shown ? 'Ẩn mật khẩu' : 'Hiện mật khẩu'}
        className="grid h-full w-9 shrink-0 place-items-center text-ink-3 hover:text-ink"
      >
        {shown ? <EyeOff className="size-4" strokeWidth={1.7} /> : <Eye className="size-4" strokeWidth={1.7} />}
      </button>
    </div>
  )
}
