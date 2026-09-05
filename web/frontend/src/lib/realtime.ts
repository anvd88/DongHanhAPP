/**
 * Kết nối realtime nghiệp vụ: một EventSource duy nhất tới GET /api/realtime/stream.
 *
 * Hợp đồng backend (BuildingBlocks/Realtime/RealtimeEndpoints.cs):
 *  · Mỗi khung mang `id` = sequence_no; trình duyệt tự gửi lại `Last-Event-ID` khi nối lại.
 *  · Loại sự kiện: resync.required, invalidated, access.changed, session.revoked,
 *    presence.changed, feedback.resolved. Payload chỉ chứa `{ scope }`.
 *  · Sự kiện không mang dữ liệu, chỉ báo một phạm vi đã cũ; máy khách tự gọi lại API.
 *  · Nhịp tim khoảng 17 giây, gửi dưới dạng dòng chú thích `: heartbeat`.
 *
 * EventSource không tự nối lại sau 401/404 mà đóng hẳn. Vòng giám sát bên dưới xử lý việc đó:
 * readyState = CLOSED thì tạo kết nối mới với thời gian chờ tăng dần, mỗi lần nối lại đều nạp
 * lại toàn bộ dữ liệu.
 */

/**
 * Chủ đề dữ liệu. Phần tử đầu của mọi queryKey là một tên trong danh sách này, và máy chủ chỉ gửi
 * cho một kết nối những chủ đề mà kết nối đó đang mở (xem `topics` trong connectRealtime).
 *
 * Năm chủ đề đầu từng là một chủ đề duy nhất tên `data`. Gộp như thế nghĩa là một người sửa một
 * phiếu thu làm MỌI máy đang mở tải lại bán hàng, mua hàng, công nợ, sổ quỹ và danh mục hàng hoá —
 * đúng thứ lãng phí mà việc chẻ nhỏ này bỏ đi. Bảng nào phát chủ đề nào khai ở phía máy chủ, trong
 * `Watched` của Realtime/DatabaseChangePublisher.cs; hai nơi phải khớp nhau.
 */
export const SCOPES = [
  'sales',
  'debts',
  'cash',
  'purchases',
  'catalog',
  'hr',
  'attendance',
  'presence',
  'tasks',
  'portal',
  'config',
  'audit',
  'release',
  'feedback',
  'talent',
  'notify',
  'liveness',
  'access',
] as const

export type Scope = (typeof SCOPES)[number]

export type RealtimeStatus = 'connecting' | 'live' | 'offline'

/**
 * Nạp lại đúng những chủ đề mà một thao tác vừa làm cũ, ngay tại máy vừa bấm.
 *
 * Máy chủ cũng sẽ báo qua realtime, nhưng tín hiệu đó đi vòng qua outbox nên đến sau một nhịp; chờ
 * nó thì người vừa lưu phiếu nhìn thấy số cũ trong khoảnh khắc. Điểm khác so với trước là ở chỗ
 * KHÔNG xoá sạch bộ nhớ đệm nữa: chỉ những chủ đề thật sự chịu ảnh hưởng mới phải tải lại.
 */
export function refreshTopics(
  client: { invalidateQueries: (filters: { queryKey: readonly string[] }) => Promise<void> },
  ...topics: Scope[]
) {
  for (const topic of topics) void client.invalidateQueries({ queryKey: [topic] })
}

export interface RealtimeHandlers {
  /** Một phạm vi dữ liệu đã cũ; 'all' nghĩa là nạp lại toàn bộ. */
  onInvalidate: (scope: Scope | 'all') => void
  /** Quyền của tài khoản thay đổi: phải nạp lại hồ sơ truy cập vì menu có thể khác đi. */
  onAccessChanged: () => void
  /** Phiên bị thu hồi từ xa (đăng nhập máy khác, admin thu hồi thiết bị). */
  onSessionRevoked: () => void
  onStatus: (status: RealtimeStatus) => void
}

const RETRY_STEPS = [1000, 2000, 5000, 10_000, 20_000, 30_000]

/**
 * Mở luồng cho đúng những chủ đề `topics` đang cần. Danh sách rỗng nghĩa là nhận tất.
 *
 * Máy chủ bỏ khung ngoài danh sách nhưng KHÔNG bỏ mốc: `Last-Event-ID` của máy khách chỉ dừng ở
 * khung cuối cùng thật sự nhận được. Nhờ đó khi danh sách chủ đề rộng ra — người dùng mở một màn
 * hình mới — lần nối lại mang theo mốc cũ và máy chủ phát lại đúng những khung từng bị bỏ, nên dữ
 * liệu của màn hình vừa mở không thể là bản đã cũ mà không ai hay.
 */
export function connectRealtime(handlers: RealtimeHandlers, topics: readonly string[] = []) {
  let source: EventSource | null = null
  let attempt = 0
  let timer: number | undefined
  let stopped = false
  let lastEventId = ''

  const parseScope = (raw: string): Scope | 'all' => {
    try {
      const scope = (JSON.parse(raw) as { scope?: string }).scope
      return (scope as Scope) ?? 'all'
    } catch {
      return 'all'
    }
  }

  const scheduleReconnect = () => {
    if (stopped) return
    handlers.onStatus('offline')
    const wait = RETRY_STEPS[Math.min(attempt, RETRY_STEPS.length - 1)]
    attempt += 1
    timer = window.setTimeout(open, wait)
  }

  const streamUrl = () => {
    const query = new URLSearchParams()
    if (topics.length) query.set('topics', [...topics].join(','))
    // EventSource chỉ tự gửi lại Last-Event-ID cho lần nối lại của CHÍNH nó. Kết nối do mã này tạo
    // là một EventSource mới, mốc phải tự mang theo.
    if (lastEventId) query.set('after', lastEventId)
    const suffix = query.toString()
    return suffix ? `/api/realtime/stream?${suffix}` : '/api/realtime/stream'
  }

  function open() {
    if (stopped) return
    handlers.onStatus(attempt === 0 ? 'connecting' : 'offline')
    source = new EventSource(streamUrl(), { withCredentials: true })

    source.onopen = () => {
      attempt = 0
      handlers.onStatus('live')
    }

    source.onerror = () => {
      // CONNECTING: trình duyệt đang tự thử lại. CLOSED: đã đóng hẳn, phải tạo kết nối mới.
      if (source?.readyState === EventSource.CLOSED) {
        source.close()
        source = null
        scheduleReconnect()
      } else {
        handlers.onStatus('offline')
      }
    }

    const on = (type: string, fn: (event: MessageEvent) => void) =>
      source?.addEventListener(type, ((event: MessageEvent) => {
        if (event.lastEventId) lastEventId = event.lastEventId
        fn(event)
      }) as EventListener)

    on('resync.required', () => handlers.onInvalidate('all'))
    on('invalidated', (event) => handlers.onInvalidate(parseScope(event.data)))
    on('presence.changed', () => handlers.onInvalidate('presence'))
    on('feedback.resolved', () => handlers.onInvalidate('feedback'))
    on('access.changed', () => handlers.onAccessChanged())
    on('session.revoked', () => {
      stopped = true
      source?.close()
      handlers.onSessionRevoked()
    })
  }

  open()

  return () => {
    stopped = true
    window.clearTimeout(timer)
    source?.close()
    source = null
  }
}
