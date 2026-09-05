import type { ReactNode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter } from 'react-router-dom'
import { ApiError } from '@/lib/http'
import { AuthProvider } from '@/auth/AuthProvider'
import { FiscalProvider } from '@/shell/FiscalContext'
import { ToastProvider } from '@/ui'

/**
 * Chính sách nạp dữ liệu dùng chung.
 *
 * `refetchOnWindowFocus` tắt vì máy chủ đã phát tín hiệu làm mới qua kết nối realtime.
 * Không thử lại với lỗi nghiệp vụ (400/401/403/404/409/413/429) vì đó không phải lỗi tạm thời.
 */
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      gcTime: 5 * 60_000,
      refetchOnWindowFocus: false,
      retry: (failureCount, error) => {
        if (error instanceof ApiError && [400, 401, 403, 404, 409, 413, 429].includes(error.status))
          return false
        return failureCount < 2
      },
    },
    mutations: { retry: false },
  },
})

export function Providers({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <ToastProvider>
          <FiscalProvider>
            <AuthProvider>{children}</AuthProvider>
          </FiscalProvider>
        </ToastProvider>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
