import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Bell } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { api } from '@/lib/http'
import { ago } from '@/lib/format'
import { cn } from '@/lib/cn'
import { Button, EmptyState, Skeleton } from '@/ui'
import { Popover } from './Popover'

interface WebNotification {
  id: number
  title: string
  body: string
  category: string
  /** Đường dẫn màn hình web do máy chủ gắn sẵn. */
  link: string
  appTarget: string
  notifId: string
  createdAt: string
  read: boolean
}

interface Inbox {
  unread: number
  items: WebNotification[]
}

/**
 * Chuông thông báo, đọc hộp thư web do máy chủ ghi kèm mỗi lần đẩy thông báo.
 * Không hẹn giờ nạp lại: máy chủ phát tín hiệu làm mới qua kết nối realtime.
 */
export function NotificationBell() {
  const queryClient = useQueryClient()
  const navigate = useNavigate()

  const inbox = useQuery({
    queryKey: ['notify', 'inbox'],
    queryFn: () => api.get<Inbox>('/notifications', { query: { limit: 20 }, background: true }),
    staleTime: 30_000,
  })

  const markRead = useMutation({
    mutationFn: (id: number) => api.post<void>(`/notifications/${id}/read`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['notify'] }),
  })

  const markAll = useMutation({
    mutationFn: () => api.post<void>('/notifications/read-all'),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['notify'] }),
  })

  const unread = inbox.data?.unread ?? 0

  return (
    <Popover
      label="Thông báo"
      panelClassName="w-[26rem] max-w-[calc(100vw-1rem)]"
      trigger={({ toggle, open }) => (
        <button
          type="button"
          onClick={toggle}
          aria-expanded={open}
          aria-label={unread ? `Thông báo, ${unread} chưa đọc` : 'Thông báo'}
          className="relative grid size-8 place-items-center rounded-sm text-ink-2 hover:bg-panel-3 hover:text-ink"
        >
          <Bell className="size-[18px]" strokeWidth={1.7} />
          {unread > 0 && (
            <span className="tnum absolute top-0.5 right-0.5 min-w-4 rounded-sm bg-brand px-1 text-center text-[10px] leading-4 font-semibold text-on-brand">
              {unread > 99 ? '99+' : unread}
            </span>
          )}
        </button>
      )}
    >
      {(close) => (
        <>
          <header className="flex items-center gap-2 border-b border-line px-3.5 py-2">
            <h3 className="flex-1 text-sm font-semibold text-ink">Thông báo</h3>
            {unread > 0 && (
              <Button size="sm" variant="ghost" onClick={() => markAll.mutate()} loading={markAll.isPending}>
                Đánh dấu đã đọc
              </Button>
            )}
          </header>

          <div className="max-h-[26rem] overflow-y-auto">
            {inbox.isLoading && (
              <div className="flex flex-col gap-2 p-3.5">
                <Skeleton className="h-3.5 w-2/3" />
                <Skeleton className="h-3.5 w-full" />
                <Skeleton className="h-3.5 w-1/2" />
              </div>
            )}

            {!inbox.isLoading && (inbox.data?.items.length ?? 0) === 0 && (
              <EmptyState title="Chưa có thông báo" compact />
            )}

            <ul>
              {inbox.data?.items.map((item) => (
                <li key={item.id}>
                  <button
                    type="button"
                    onClick={() => {
                      if (!item.read) markRead.mutate(item.id)
                      if (item.link) navigate(item.link)
                      close()
                    }}
                    className={cn(
                      'block w-full border-b border-line-2 border-l-2 px-3.5 py-2 text-left last:border-b-0 hover:bg-panel-2',
                      item.read ? 'border-l-transparent' : 'border-l-brand',
                    )}
                  >
                    <span className="flex items-baseline gap-2">
                      <span className={cn('flex-1 text-sm', item.read ? 'text-ink-2' : 'font-medium text-ink')}>
                        {item.title}
                      </span>
                      <span className="shrink-0 text-xs text-ink-3">{ago(item.createdAt)}</span>
                    </span>
                    {item.body && <span className="mt-0.5 line-clamp-2 text-xs text-ink-3">{item.body}</span>}
                  </button>
                </li>
              ))}
            </ul>
          </div>

          <footer className="border-t border-line px-2 py-1.5">
            <Button
              size="sm"
              variant="ghost"
              onClick={() => {
                navigate('/thong-bao')
                close()
              }}
            >
              Xem tất cả thông báo
            </Button>
          </footer>
        </>
      )}
    </Popover>
  )
}
