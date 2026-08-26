import { api } from "./api";

// ── Kiểu dữ liệu Giao việc & nghiệm thu (khớp TaskAssignmentEndpoints của backend) ──
// "completed" chỉ dùng cho việc GIAO HÀNG: kế toán đã nhận lại tờ phiếu giấy sau khi nghiệm thu.
export type TaskStatus =
  | "assigned"
  | "in_progress"
  | "submitted"
  | "accepted"
  | "completed"
  | "rejected"
  | "cancelled";
export type TaskPriority = "low" | "normal" | "high" | "urgent";

export interface WorkTask {
  id: string;
  taskNo: string;
  title: string;
  description: string;
  assignerUsername: string;
  assignerName: string;
  assigneeUsername: string;
  assigneeName: string;
  priority: TaskPriority | string;
  dueAt?: string | null;
  status: TaskStatus | string;
  progress: number;
  submitNote: string;
  submittedAt?: string | null;
  reviewNote: string;
  rating?: number | null;
  reviewedAt?: string | null;
  reviewedBy: string;
  createdAt: string;
  updatedAt: string;
  overdue: boolean;
  /** Có giá trị khi việc sinh từ phiếu xuất kho (source_kind='delivery'). */
  delivery?: WorkTaskDelivery | null;
}

/** Phần phiếu xuất kho của một việc giao hàng, kèm lệnh thu tiền cùng khách (nếu có). */
export interface WorkTaskDelivery {
  documentId: string;
  voucherNo: string;
  customerName: string;
  customerId?: string | null;
  collection?: {
    id: string;
    orderNo: string;
    customerId: string;
    customerName: string;
    expectedAmount: number;
    status: string;
    handoverDueAt: string;
  } | null;
}

export interface WorkTaskEvent {
  id: number;
  actorUsername: string;
  actorName: string;
  kind: string;
  note: string;
  createdAt: string;
}

export interface TaskListResult {
  canAssign: boolean;
  isAdmin: boolean;
  inbox: WorkTask[];
  outbox: WorkTask[];
  summary: {
    inbox: number;
    inboxActionable: number;
    outbox: number;
    outboxReview: number;
    /** Việc giao hàng đã giao xong, đang chờ tờ phiếu về kho (không phải "chờ nghiệm thu"). */
    outboxAwaitingVoucher?: number;
  };
}

export interface TaskAssignee {
  username: string;
  fullName: string;
  position: string;
  department: string;
}
export interface TaskMeta {
  canAssign: boolean;
  priorities: string[];
  assignees: TaskAssignee[];
}

export interface TaskDetailResult {
  task: WorkTask;
  events: WorkTaskEvent[];
  flags: {
    mine: boolean;
    assignedByMe: boolean;
    canSubmit: boolean;
    canStart: boolean;
    canReview: boolean;
    /** Trả lại chuyến/việc — việc giao hàng không nghiệm thu nhưng vẫn trả lại được. */
    canReject: boolean;
    canEdit: boolean;
    canCancel: boolean;
  };
}

export interface CreateTaskBody {
  title: string;
  description?: string;
  assigneeUsername: string;
  priority?: string;
  dueAt?: string | null;
}

// ── Nhãn & màu hiển thị ──
/**
 * Việc GIAO HÀNG không có chặng nghiệm thu: 'submitted' nghĩa là lái xe đã giao xong và đang chờ
 * nộp tờ phiếu ký nhận về kho, chứ không phải "chờ ai đó chấm điểm".
 */
export function statusLabel(status: string, isDelivery = false): string {
  if (isDelivery && status === "submitted") return "Đã giao, chờ nộp phiếu";
  return STATUS_LABEL[status] ?? status;
}

export const STATUS_LABEL: Record<string, string> = {
  assigned: "Chờ nhận",
  in_progress: "Đang làm",
  submitted: "Chờ nghiệm thu",
  accepted: "Đã nghiệm thu",
  completed: "Đã hoàn thành",
  rejected: "Bị trả lại",
  cancelled: "Đã huỷ",
};
export const STATUS_COLOR: Record<string, string> = {
  assigned: "accent",
  in_progress: "purple",
  submitted: "warning",
  accepted: "success",
  completed: "success",
  rejected: "danger",
  cancelled: "muted",
};
/**
 * Việc đã ĐÓNG SỔ: không sửa, không huỷ, không thao tác được nữa. Giữ chung một chỗ để thêm
 * trạng thái kết thúc mới (như "completed") không phải đi sửa từng điều kiện rải rác.
 */
export const isTaskClosed = (status: string) =>
  status === "accepted" || status === "completed" || status === "cancelled";

export const PRIORITY_LABEL: Record<string, string> = {
  low: "Thấp",
  normal: "Bình thường",
  high: "Cao",
  urgent: "Khẩn cấp",
};
export const PRIORITY_COLOR: Record<string, string> = {
  low: "muted",
  normal: "accent",
  high: "warning",
  urgent: "danger",
};

// ── Gọi API ──
export const tasksApi = {
  list: () => api.get<TaskListResult>("/api/tasks"),
  meta: () => api.get<TaskMeta>("/api/tasks/meta"),
  detail: (id: string) => api.get<TaskDetailResult>(`/api/tasks/${id}`),
  create: (body: CreateTaskBody) => api.post<{ id: string; taskNo: string }>("/api/tasks", body),
  update: (id: string, body: CreateTaskBody) => api.put(`/api/tasks/${id}`, body),
  start: (id: string) => api.post(`/api/tasks/${id}/start`),
  progress: (id: string, progress: number, note?: string) => api.post(`/api/tasks/${id}/progress`, { progress, note }),
  submit: (id: string, note?: string) => api.post(`/api/tasks/${id}/submit`, { note }),
  accept: (id: string, note?: string, rating?: number) => api.post(`/api/tasks/${id}/accept`, { note, rating }),
  reject: (id: string, note: string) => api.post(`/api/tasks/${id}/reject`, { note }),
  cancel: (id: string, note?: string) => api.post(`/api/tasks/${id}/cancel`, { note }),
  comment: (id: string, note: string) => api.post(`/api/tasks/${id}/comment`, { note }),
  remove: (id: string) => api.del(`/api/tasks/${id}`),
};
