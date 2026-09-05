import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/lib/http'
import type { Tone } from '@/ui'

/* ============================================================================
   Giao việc (TaskAssignmentEndpoints) và đơn từ (RequestEndpoints).

   Hai nhóm nằm chung một tệp vì màn hình "Công việc" trộn cả hai: việc được giao, đơn mình gửi,
   đơn chờ mình duyệt. Khoá truy vấn bắt đầu bằng phạm vi realtime — việc là `tasks`, đơn từ là
   `hr` — nên một tín hiệu từ máy chủ làm mới đúng phần cần làm mới.
   ========================================================================== */

/* ── Giao việc ──────────────────────────────────────────────────────────── */

export interface TaskCollection {
  id: string
  orderNo: string
  customerId: string
  customerName: string
  expectedAmount: number
  status: string
  handoverDueAt: string
}

export interface TaskDelivery {
  documentId: string
  voucherNo: string
  customerName: string
  customerId: string | null
  collection: TaskCollection | null
}

export interface WorkTask {
  id: string
  taskNo: string
  title: string
  description: string
  assignerUsername: string
  assignerName: string
  assigneeUsername: string
  assigneeName: string
  priority: string
  dueAt: string | null
  status: string
  progress: number
  submitNote: string
  submittedAt: string | null
  reviewNote: string
  rating: number | null
  reviewedAt: string | null
  reviewedBy: string
  createdAt: string
  updatedAt: string
  overdue: boolean
  delivery: TaskDelivery | null
}

export interface TaskEvent {
  id: number
  actorUsername: string
  actorName: string
  kind: string
  note: string
  createdAt: string
}

export interface TaskBoard {
  canAssign: boolean
  isAdmin: boolean
  /** Việc được giao cho tôi. */
  inbox: WorkTask[]
  /** Việc tôi giao — quản trị viên thấy toàn bộ. */
  outbox: WorkTask[]
  /** Lệnh thu tiền chưa gộp được vào việc giao hàng nào. */
  collections: TaskCollection[]
  summary: {
    inbox: number
    inboxActionable: number
    outbox: number
    outboxReview: number
    outboxAwaitingVoucher: number
    collections: number
    collectionsStandalone: number
  }
}

export interface TaskAssignee {
  username: string
  fullName: string
  position: string
  department: string
  attendanceStatus: string
  attendanceNote: string
  /** Người chưa chấm công hoặc đang nghỉ vẫn hiện nhưng không chọn được. */
  selectable: boolean
}

export interface TaskMeta {
  canAssign: boolean
  priorities: string[]
  assignees: TaskAssignee[]
}

export interface TaskDetail {
  task: WorkTask
  events: TaskEvent[]
  flags: {
    mine: boolean
    assignedByMe: boolean
    canSubmit: boolean
    canStart: boolean
    canReview: boolean
    canReject: boolean
    canEdit: boolean
    canCancel: boolean
  }
}

export interface TaskHistory {
  from: string
  to: string
  isAdmin: boolean
  items: WorkTask[]
  people: Array<{ username: string; fullName: string; count: number }>
  total: number
}

export interface SaveTaskRequest {
  title: string
  description: string
  assigneeUsername: string
  priority: string
  dueAt?: string | null
}

/** Nhãn và sắc thái của một chặng trong vòng đời việc. */
export function taskStage(task: Pick<WorkTask, 'status' | 'delivery' | 'overdue'>): {
  label: string
  tone: Tone
} {
  switch (task.status) {
    case 'assigned':
      return { label: 'Mới giao', tone: task.overdue ? 'danger' : 'neutral' }
    case 'in_progress':
      return { label: 'Đang làm', tone: task.overdue ? 'danger' : 'brand' }
    case 'submitted':
      return task.delivery
        ? { label: 'Chờ phiếu về kho', tone: 'warn' }
        : { label: 'Chờ nghiệm thu', tone: 'warn' }
    case 'accepted':
      return { label: 'Đã nghiệm thu', tone: 'ok' }
    case 'completed':
      return { label: 'Đã hoàn thành', tone: 'ok' }
    case 'rejected':
      return { label: 'Bị trả lại', tone: 'danger' }
    case 'cancelled':
      return { label: 'Đã huỷ', tone: 'neutral' }
    default:
      return { label: task.status, tone: 'neutral' }
  }
}

export const TASK_PRIORITY_LABELS: Record<string, string> = {
  low: 'Thấp',
  normal: 'Bình thường',
  high: 'Cao',
  urgent: 'Khẩn',
}

export const TASK_PRIORITY_TONES: Record<string, Tone> = {
  low: 'neutral',
  normal: 'neutral',
  high: 'warn',
  urgent: 'danger',
}

export const TASK_EVENT_LABELS: Record<string, string> = {
  assign: 'Giao việc',
  start: 'Nhận việc',
  progress: 'Cập nhật tiến độ',
  submit: 'Nộp việc',
  accept: 'Nghiệm thu đạt',
  reject: 'Trả lại',
  cancel: 'Huỷ việc',
  comment: 'Trao đổi',
  edit: 'Sửa việc',
}

const TASKS = ['tasks', 'work-tasks'] as const

export function useTaskBoard() {
  return useQuery({
    queryKey: [...TASKS, 'board'],
    queryFn: () => api.get<TaskBoard>('/tasks'),
  })
}

export function useTaskMeta(enabled = true) {
  return useQuery({
    queryKey: [...TASKS, 'meta'],
    queryFn: () => api.get<TaskMeta>('/tasks/meta'),
    enabled,
  })
}

export function useTask(id: string | null | undefined) {
  return useQuery({
    queryKey: [...TASKS, 'detail', id],
    queryFn: () => api.get<TaskDetail>(`/tasks/${id}`),
    enabled: !!id,
  })
}

export function useTaskHistory(params: { from: string; to: string; assignee?: string }, enabled = true) {
  return useQuery({
    queryKey: [...TASKS, 'history', params.from, params.to, params.assignee ?? ''],
    queryFn: () => api.get<TaskHistory>('/tasks/history', { query: params }),
    enabled,
  })
}

function useTaskMutation<TArgs, TResult = void>(fn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['tasks'] }),
  })
}

export function useSaveTask() {
  return useTaskMutation(
    async ({ id, body }: { id?: string; body: SaveTaskRequest }): Promise<{ id: string; taskNo: string } | null> => {
      if (id) {
        await api.put<void>(`/tasks/${id}`, body)
        return null
      }
      return api.post<{ id: string; taskNo: string }>('/tasks', body)
    },
  )
}

/** Chuyển chặng của một việc: nhận, nộp, nghiệm thu, trả lại, huỷ, trao đổi. */
export function useTaskAction() {
  return useTaskMutation(
    ({
      id,
      action,
      body,
    }: {
      id: string
      action: 'start' | 'progress' | 'submit' | 'accept' | 'reject' | 'cancel' | 'comment'
      body?: { note?: string; progress?: number; rating?: number }
    }) => api.post<void>(`/tasks/${id}/${action}`, body ?? {}),
  )
}

export function useDeleteTask() {
  return useTaskMutation((id: string) => api.del<void>(`/tasks/${id}`))
}

/* ── Đơn từ ─────────────────────────────────────────────────────────────── */

export interface RequestFieldOption {
  value: string
  label: string
}

export interface RequestField {
  key: string
  label: string
  /** text | date | time | number | money | textarea | select | checkboxes */
  type: string
  hint: string
  required: boolean
  options: RequestFieldOption[]
}

export interface RequestType {
  type: string
  label: string
  category: string
  fields: RequestField[]
}

export interface RequestRow {
  id: string
  requestNo: string
  type: string
  typeLabel: string
  title: string
  requesterUsername: string
  employeeName: string
  employeeCode: string
  status: string
  currentStep: number
  totalSteps: number
  createdAt: string
}

export interface RequestApproval {
  stepNo: number
  approverRole: string
  approverUsername: string
  approverName: string
  status: string
  decidedAt: string | null
  decidedBy: string
  comment: string
  hasSignature: boolean
}

export interface RequestDetail {
  request: {
    id: string
    requestNo: string
    type: string
    typeLabel: string
    title: string
    requesterUsername: string
    employeeName: string
    employeeCode: string
    departmentName: string
    payload: Record<string, unknown> | null
    status: string
    currentStep: number
    createdAt: string
    dueAt: string | null
  }
  approvals: RequestApproval[]
  attachments: Array<{ id: number; fileName: string; mimeType: string; fileSize: number }>
}

export type RequestScope = 'mine' | 'inbox' | 'all'

export function requestStatus(status: string): { label: string; tone: Tone } {
  switch (status) {
    case 'Pending':
      return { label: 'Đang xử lý', tone: 'warn' }
    case 'Approved':
      return { label: 'Đã duyệt', tone: 'ok' }
    case 'Rejected':
      return { label: 'Từ chối', tone: 'danger' }
    case 'Cancelled':
      return { label: 'Đã huỷ', tone: 'neutral' }
    default:
      return { label: status, tone: 'neutral' }
  }
}

const REQUESTS = ['hr', 'requests'] as const

export function useRequestTypes() {
  return useQuery({
    queryKey: [...REQUESTS, 'types'],
    queryFn: () => api.get<RequestType[]>('/requests/types'),
    staleTime: 10 * 60 * 1000,
  })
}

export function useRequests(scope: RequestScope, status?: string, enabled = true) {
  return useQuery({
    queryKey: [...REQUESTS, 'list', scope, status ?? ''],
    queryFn: () => api.get<RequestRow[]>('/requests', { query: { scope, status } }),
    enabled,
  })
}

export function useRequest(id: string | null | undefined) {
  return useQuery({
    queryKey: [...REQUESTS, 'detail', id],
    queryFn: () => api.get<RequestDetail>(`/requests/${id}`),
    enabled: !!id,
  })
}

function useRequestMutation<TArgs, TResult = void>(fn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['hr'] }),
  })
}

export function useSaveRequest() {
  return useRequestMutation(
    async ({
      id,
      body,
    }: {
      id?: string
      body: { type: string; title: string; payload: Record<string, unknown> }
    }): Promise<{ id: string; requestNo: string } | null> => {
      if (id) {
        await api.put<void>(`/requests/${id}`, body)
        return null
      }
      return api.post<{ id: string; requestNo: string }>('/requests', body)
    },
  )
}

/** Duyệt hoặc từ chối một đơn. Chữ ký điện tử là ảnh dataURL, để trống nếu không ký. */
export function useDecideRequest() {
  return useRequestMutation(
    ({
      id,
      decision,
      comment,
      signature,
    }: {
      id: string
      decision: 'approve' | 'reject'
      comment?: string
      signature?: string
    }) => api.post<void>(`/requests/${id}/${decision}`, { comment, signature }),
  )
}

export function useCancelRequest() {
  return useRequestMutation((id: string) => api.post<void>(`/requests/${id}/cancel`))
}

export function useRemindRequest() {
  return useRequestMutation((id: string) => api.post<void>(`/requests/${id}/remind`))
}

export function useSaveDelegation() {
  return useRequestMutation((body: { toUsername: string; fromDate: string; toDate: string }) =>
    api.put<void>('/requests/delegations/me', body),
  )
}
