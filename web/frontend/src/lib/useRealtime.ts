import { useEffect, useRef, useState } from 'react'
import { useQueryClient, type QueryClient } from '@tanstack/react-query'
import { connectRealtime, SCOPES, type RealtimeStatus, type Scope } from './realtime'
import { useAuth } from '@/auth/AuthProvider'

const KNOWN = new Set<string>(SCOPES)

/**
 * Chủ đề mà ứng dụng đang thật sự cần: lấy từ những truy vấn CÓ NGƯỜI XEM trong bộ nhớ đệm, tức
 * đúng các màn hình đang mở. Không phải một bảng "màn hình nào nghe chủ đề nào" viết tay — bảng đó
 * chắc chắn sẽ lệch với thực tế sau vài lần đổi giao diện.
 */
function activeTopics(client: QueryClient): string {
  const found = new Set<string>()
  for (const query of client.getQueryCache().getAll()) {
    if (query.getObserversCount() === 0) continue
    const head = query.queryKey[0]
    if (typeof head === 'string' && KNOWN.has(head)) found.add(head)
  }
  return [...found].sort().join(',')
}

/**
 * Đăng ký realtime cho toàn ứng dụng. Chỉ gọi một lần, ở khung ngoài cùng.
 *
 * Quy ước khoá truy vấn: phần tử đầu tiên của queryKey luôn là chủ đề realtime
 * (`['sales', 'documents', filters]`). Nhờ vậy một tín hiệu cho chủ đề `sales` chỉ cần
 * invalidateQueries({ queryKey: ['sales'] }), không phải duy trì bảng ánh xạ riêng.
 */
export function useRealtime() {
  const queryClient = useQueryClient()
  const { status: authStatus, refreshProfile } = useAuth()
  const [status, setStatus] = useState<RealtimeStatus>('connecting')
  // Chuỗi rỗng ở đây nghĩa là "chưa đo được", và máy chủ hiểu là nhận tất — đúng thứ ta muốn trong
  // khoảnh khắc đầu, trước khi màn hình đầu tiên kịp gắn truy vấn nào.
  const [topics, setTopics] = useState('')
  const previous = useRef('')

  useEffect(() => {
    if (authStatus !== 'ready') return
    const cache = queryClient.getQueryCache()
    let timer: number | undefined
    // Hoãn một nhịp: mở một màn hình gắn nhiều truy vấn liền nhau, đo ngay thì mỗi truy vấn là một
    // lần nối lại. Gom bằng cách CHỜ TRẦN chứ không phải chờ-im-lặng: bộ nhớ đệm còn phát tín hiệu
    // ở mỗi lần tải xong, nên chờ-im-lặng có thể bị dời mãi và không bao giờ đo.
    const measure = () => {
      if (timer !== undefined) return
      timer = window.setTimeout(() => {
        timer = undefined
        setTopics(activeTopics(queryClient))
      }, 400)
    }
    measure()
    const unsubscribe = cache.subscribe(measure)
    return () => {
      unsubscribe()
      window.clearTimeout(timer)
    }
  }, [authStatus, queryClient])

  useEffect(() => {
    if (authStatus !== 'ready') return
    // Danh sách rộng ra nghĩa là vừa có màn hình mới mở. Máy chủ phát lại các khung từng bị bỏ theo
    // mốc cũ, nhưng chỉ những khung còn trong 48 giờ lưu trữ; nạp lại phần mới thêm cho chắc.
    const added = topics.split(',').filter((t) => t && !previous.current.split(',').includes(t))
    previous.current = topics
    for (const topic of added) void queryClient.invalidateQueries({ queryKey: [topic] })

    return connectRealtime(
      {
        onStatus: setStatus,
        onInvalidate: (scope: Scope | 'all') => {
          if (scope === 'all') void queryClient.invalidateQueries()
          else void queryClient.invalidateQueries({ queryKey: [scope] })
        },
        onAccessChanged: () => {
          // Quyền thay đổi: nạp lại hồ sơ truy cập rồi xoá cache vì dữ liệu cũ có thể ngoài phạm vi mới.
          void refreshProfile().catch(() => undefined)
          void queryClient.invalidateQueries()
        },
        onSessionRevoked: () => {
          // Không xử lý đăng xuất tại đây: gọi lại hồ sơ truy cập để 401 đi qua lớp http, giữ một đường ra duy nhất.
          void refreshProfile().catch(() => undefined)
        },
      },
      topics ? topics.split(',') : [],
    )
  }, [authStatus, queryClient, refreshProfile, topics])

  return status
}
