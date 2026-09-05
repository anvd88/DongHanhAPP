import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/http'

/* ============================================================================
   Cổng thông tin: tin tức & sự kiện, khảo sát, trung tâm trợ giúp, phản hồi.
   ========================================================================== */

/* ── Tin tức & sự kiện ──────────────────────────────────────────────────── */

export interface PortalPost {
  id: number
  /** news | event */
  kind: string
  title: string
  summary: string
  body: string
  coverImage: string | null
  location: string
  eventAt: string | null
  pinned: boolean
  published: boolean
  authorUsername: string
  authorName: string
  createdAt: string
  updatedAt: string
}

export interface PortalAbout {
  title: string
  content: string
  coverImage: string | null
  address: string
  hotline: string
  email: string
  website: string
  updatedAt: string
}

export interface PortalFeed {
  about: PortalAbout
  news: PortalPost[]
  events: PortalPost[]
}

export interface SavePostRequest {
  kind: string
  title: string
  summary: string
  body: string
  coverImage?: string | null
  location: string
  eventAt?: string | null
  pinned: boolean
  published: boolean
}

const PORTAL = ['portal'] as const

export function usePortalFeed() {
  return useQuery({ queryKey: [...PORTAL, 'feed'], queryFn: () => api.get<PortalFeed>('/portal/feed') })
}

/** Danh sách quản trị: gồm cả bài chưa đăng. Chỉ người có quyền quản trị cổng thông tin gọi được. */
export function usePortalPosts(kind: string, enabled = true) {
  return useQuery({
    queryKey: [...PORTAL, 'posts', kind],
    queryFn: () => api.get<PortalPost[]>('/portal/posts', { query: { kind } }),
    enabled,
  })
}

export function usePortalAbout() {
  return useQuery({ queryKey: [...PORTAL, 'about'], queryFn: () => api.get<PortalAbout>('/portal/about') })
}

function usePortalMutation<TArgs, TResult = void>(fn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['portal'] }),
  })
}

export function useSavePost() {
  return usePortalMutation(
    async ({ id, body }: { id?: number; body: SavePostRequest }): Promise<{ id: number } | null> => {
      if (id) {
        await api.put<void>(`/portal/posts/${id}`, body)
        return null
      }
      return api.post<{ id: number }>('/portal/posts', body)
    },
  )
}

export function useDeletePost() {
  return usePortalMutation((id: number) => api.del<void>(`/portal/posts/${id}`))
}

export function useSaveAbout() {
  return usePortalMutation((body: Omit<PortalAbout, 'updatedAt'>) => api.put<void>('/portal/about', body))
}

/* ── Khảo sát & bình chọn ───────────────────────────────────────────────── */

export interface SurveyRow {
  id: string
  title: string
  description: string
  isAnonymous: boolean
  allowMultiple: boolean
  isActive: boolean
  createdAt: string
  closesAt: string | null
  responses: number
}

export interface SurveyOpen {
  id: string
  title: string
  description: string
  isAnonymous: boolean
  allowMultiple: boolean
  closesAt: string | null
  /** Đã trả lời rồi thì không gửi lại được (trừ khảo sát cho phép nhiều lượt). */
  responded: boolean
}

export interface SurveyQuestion {
  id: string
  question: string
  /** single | multi | text | rating */
  qtype: string
  options: string[]
  required: boolean
}

export interface SurveyDetail {
  survey: {
    id: string
    title: string
    description: string
    isAnonymous: boolean
    allowMultiple: boolean
    isActive: boolean
    closesAt: string | null
  }
  questions: SurveyQuestion[]
}

export interface SurveyResults {
  total: number
  results: Array<{
    questionId: string
    question: string
    qtype: string
    options: string[]
    optionCounts: number[]
    texts: string[]
    ratingAvg: number | null
  }>
}

export interface CreateSurveyRequest {
  title: string
  description: string
  isAnonymous: boolean
  allowMultiple: boolean
  closesAt?: string | null
  questions: Array<{ question: string; qtype: string; options: string[]; required: boolean }>
}

export const SURVEY_TYPE_LABELS: Record<string, string> = {
  single: 'Chọn một',
  multi: 'Chọn nhiều',
  text: 'Trả lời tự do',
  rating: 'Chấm điểm',
}

/** Khảo sát nằm ở phạm vi realtime `data` theo bảng Watched của máy chủ. */
const SURVEYS = ['portal', 'surveys'] as const

export function useSurveys(enabled = true) {
  return useQuery({
    queryKey: [...SURVEYS, 'all'],
    queryFn: () => api.get<SurveyRow[]>('/surveys'),
    enabled,
  })
}

export function useOpenSurveys() {
  return useQuery({ queryKey: [...SURVEYS, 'active'], queryFn: () => api.get<SurveyOpen[]>('/surveys/active') })
}

export function useSurvey(id: string | null | undefined) {
  return useQuery({
    queryKey: [...SURVEYS, 'detail', id],
    queryFn: () => api.get<SurveyDetail>(`/surveys/${id}`),
    enabled: !!id,
  })
}

export function useSurveyResults(id: string | null | undefined, enabled = true) {
  return useQuery({
    queryKey: [...SURVEYS, 'results', id],
    queryFn: () => api.get<SurveyResults>(`/surveys/${id}/results`),
    enabled: !!id && enabled,
  })
}

function useSurveyMutation<TArgs, TResult = void>(fn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['portal', 'surveys'] }),
  })
}

export function useCreateSurvey() {
  return useSurveyMutation((body: CreateSurveyRequest) => api.post<{ id: string }>('/surveys', body))
}

export function useCloseSurvey() {
  return useSurveyMutation((id: string) => api.post<void>(`/surveys/${id}/close`))
}

export function useDeleteSurvey() {
  return useSurveyMutation((id: string) => api.del<void>(`/surveys/${id}`))
}

export function useRespondSurvey() {
  return useSurveyMutation(
    ({ id, answers }: { id: string; answers: Array<{ questionId: string; answer: string; optionIndices: number[] }> }) =>
      api.post<{ ok: boolean }>(`/surveys/${id}/respond`, { answers }),
  )
}

/* ── Trung tâm trợ giúp ─────────────────────────────────────────────────── */

export interface Faq {
  id: string
  category: string
  question: string
  answer: string
  orderNo: number
  isPublished: boolean
}

export function useFaqs() {
  return useQuery({ queryKey: ['config', 'faqs'], queryFn: () => api.get<Faq[]>('/help/faqs') })
}

export function useHelpStatus() {
  return useQuery({
    queryKey: ['config', 'help-status'],
    queryFn: () => api.get<{ db: string; serverTime: string }>('/help/status'),
    refetchInterval: 60_000,
  })
}

function useFaqMutation<TArgs, TResult = void>(fn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['config', 'faqs'] }),
  })
}

export function useSaveFaq() {
  return useFaqMutation(
    async ({ id, body }: { id?: string; body: Omit<Faq, 'id'> }): Promise<{ id: string } | null> => {
      if (id) {
        await api.put<void>(`/help/faqs/${id}`, body)
        return null
      }
      return api.post<{ id: string }>('/help/faqs', body)
    },
  )
}

export function useDeleteFaq() {
  return useFaqMutation((id: string) => api.del<void>(`/help/faqs/${id}`))
}

/* ── Phản hồi & hỗ trợ ──────────────────────────────────────────────────── */

export interface FeedbackItem {
  id: number
  type: string
  typeLabel: string
  reporterUsername: string
  reporterName: string
  targetName: string
  reason: string
  createdAt: string
}

export interface GeneralFeedback {
  id: string
  message: string
  status: string
  response: string
  createdAt: string
}

export interface SupportTicket {
  id: string
  code: string
  message: string
  status: string
  response: string
  createdAt: string
}

const FEEDBACK = ['feedback'] as const

export function useFeedback() {
  return useQuery({ queryKey: [...FEEDBACK, 'list'], queryFn: () => api.get<FeedbackItem[]>('/feedback') })
}

export function useMyGeneralFeedback() {
  return useQuery({ queryKey: [...FEEDBACK, 'general'], queryFn: () => api.get<GeneralFeedback[]>('/feedback/general/mine') })
}

export function useMySupportTickets() {
  return useQuery({ queryKey: [...FEEDBACK, 'support'], queryFn: () => api.get<SupportTicket[]>('/feedback/support/mine') })
}

function useFeedbackMutation<TArgs, TResult = void>(fn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['feedback'] }),
  })
}

export function useResolveFeedback() {
  return useFeedbackMutation((id: number) => api.post<void>(`/feedback/${id}/resolve`))
}

/** Góp ý chung. Gửi ẩn danh thì máy chủ không lưu tên đăng nhập. */
export function useSendGeneralFeedback() {
  return useFeedbackMutation((body: { message: string; anonymous: boolean }) =>
    api.post<{ id: string; status: string }>('/feedback/general', body),
  )
}

export function useSendSupportTicket() {
  return useFeedbackMutation((body: { message: string; appVersion?: string; deviceModel?: string }) =>
    api.post<{ id: string; code: string; status: string }>('/feedback/support', body),
  )
}
