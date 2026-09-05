/**
 * Lớp gọi API duy nhất của web. Ba ràng buộc của backend được xử lý tập trung tại đây:
 *
 *  1. Phiên web nằm trong cookie HttpOnly `km_auth`, nên mọi request dùng `credentials: 'include'`.
 *     JavaScript không đọc được token.
 *  2. Chống CSRF theo cơ chế double-submit: mọi request thay đổi dữ liệu gửi lại giá trị cookie
 *     `km_csrf` ở header `X-CSRF-Token`.
 *  3. Mã 401 nghĩa là phiên không còn hiệu lực (khoá tài khoản, thu hồi thiết bị, quá hạn nhàn
 *     rỗi) vì backend dựng lại quyền từ CSDL ở mỗi request. Lỗi mạng không sinh 401 và không
 *     được xử lý như mất phiên.
 */

export const CSRF_COOKIE = 'km_csrf'
export const CSRF_HEADER = 'X-CSRF-Token'

const UNSAFE = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])

export class ApiError extends Error {
  readonly status: number
  readonly code?: string
  readonly payload?: unknown

  constructor(status: number, message: string, code?: string, payload?: unknown) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.payload = payload
  }

  /** Phiên hết hiệu lực — lớp xác thực sẽ đưa về màn đăng nhập. */
  get isUnauthorized() {
    return this.status === 401
  }

  /** Không đủ quyền, nhưng vẫn đang đăng nhập. Giữ nguyên màn hình, chỉ báo lỗi. */
  get isForbidden() {
    return this.status === 403
  }

  /** 503: backend không truy cập được CSDL. */
  get isUnavailable() {
    return this.status === 503
  }
}

export function readCookie(name: string): string | null {
  const hit = document.cookie.split('; ').find((c) => c.startsWith(`${name}=`))
  return hit ? decodeURIComponent(hit.slice(name.length + 1)) : null
}

type Handler = () => void
const sessionLostHandlers = new Set<Handler>()

/** Đăng ký callback khi phiên mất hiệu lực; AuthProvider dùng để dọn trạng thái và chuyển màn hình. */
export function onSessionLost(handler: Handler) {
  sessionLostHandlers.add(handler)
  return () => {
    sessionLostHandlers.delete(handler)
  }
}

export interface RequestOptions extends Omit<RequestInit, 'body'> {
  body?: unknown
  query?: Record<string, string | number | boolean | null | undefined>
  /** Request nền (chuông, số huy hiệu): backend bỏ qua việc cập nhật last_seen cho request có cờ này. */
  background?: boolean
  /** Không kích hoạt luồng mất phiên khi gặp 401; dùng cho lần dò phiên đầu tiên. */
  quiet?: boolean
}

function buildUrl(path: string, query?: RequestOptions['query']) {
  const url = path.startsWith('/api') ? path : `/api${path.startsWith('/') ? '' : '/'}${path}`
  if (!query) return url
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query))
    if (value !== null && value !== undefined && value !== '') params.set(key, String(value))
  const qs = params.toString()
  return qs ? `${url}${url.includes('?') ? '&' : '?'}${qs}` : url
}

async function readError(res: Response): Promise<ApiError> {
  let message = ''
  let code: string | undefined
  let payload: unknown
  try {
    const text = await res.text()
    if (text) {
      try {
        payload = JSON.parse(text)
        const obj = payload as Record<string, unknown>
        message = typeof obj?.message === 'string' ? obj.message : ''
        code = typeof obj?.code === 'string' ? obj.code : undefined
      } catch {
        message = text.slice(0, 300)
      }
    }
  } catch {
    /* Không đọc được thân phản hồi: dùng thông điệp mặc định bên dưới. */
  }
  if (!message) {
    message =
      res.status === 401
        ? 'Phiên đăng nhập đã kết thúc.'
        : res.status === 403
          ? 'Bạn không có quyền thực hiện việc này.'
          : res.status === 503
            ? 'Máy chủ tạm thời không đọc được dữ liệu. Vui lòng thử lại.'
            : `Yêu cầu thất bại (${res.status}).`
  }
  return new ApiError(res.status, message, code, payload)
}

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { body, query, background, quiet, headers, ...rest } = options
  const method = (rest.method ?? (body === undefined ? 'GET' : 'POST')).toUpperCase()

  const finalHeaders = new Headers(headers)
  finalHeaders.set('Accept', 'application/json')
  if (background) finalHeaders.set('X-Background-Poll', '1')
  if (UNSAFE.has(method)) {
    const csrf = readCookie(CSRF_COOKIE)
    if (csrf) finalHeaders.set(CSRF_HEADER, csrf)
  }

  let payload: BodyInit | undefined
  if (body instanceof FormData || body instanceof Blob) {
    payload = body
  } else if (body !== undefined) {
    finalHeaders.set('Content-Type', 'application/json')
    payload = JSON.stringify(body)
  }

  let res: Response
  try {
    res = await fetch(buildUrl(path, query), {
      ...rest,
      method,
      headers: finalHeaders,
      body: payload,
      credentials: 'include',
    })
  } catch {
    // Lỗi mạng không phải mất phiên: giữ nguyên màn hình đang mở.
    throw new ApiError(0, 'Không kết nối được máy chủ. Kiểm tra đường truyền rồi thử lại.')
  }

  if (res.status === 401) {
    const err = await readError(res)
    if (!quiet) for (const handler of sessionLostHandlers) handler()
    throw err
  }
  if (!res.ok) throw await readError(res)
  if (res.status === 204) return undefined as T

  const type = res.headers.get('Content-Type') ?? ''
  if (!type.includes('application/json')) return (await res.text()) as T
  const text = await res.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const api = {
  get: <T>(path: string, options?: RequestOptions) => request<T>(path, { ...options, method: 'GET' }),
  post: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    request<T>(path, { ...options, method: 'POST', body: body ?? {} }),
  put: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    request<T>(path, { ...options, method: 'PUT', body: body ?? {} }),
  del: <T>(path: string, options?: RequestOptions) => request<T>(path, { ...options, method: 'DELETE' }),
}
