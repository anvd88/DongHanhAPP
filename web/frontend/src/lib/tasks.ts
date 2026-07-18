import { api } from "./api";

// ── Kiểu dữ liệu Giao việc & nghiệm thu (khớp TaskAssignmentEndpoints của backend) ──
export type TaskStatus = "assigned" | "in_progress" | "submitted" | "accepted" | "rejected" | "cancelled";
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
  summary: { inbox: number; inboxActionable: number; outbox: number; outboxReview: number };
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
export const STATUS_LABEL: Record<string, string> = {
  assigned: "Chờ nhận",
  in_progress: "Đang làm",
  submitted: "Chờ nghiệm thu",
  accepted: "Đã nghiệm thu",
  rejected: "Bị trả lại",
  cancelled: "Đã huỷ",
};
export const STATUS_COLOR: Record<string, string> = {
  assigned: "accent",
  in_progress: "purple",
  submitted: "warning",
  accepted: "success",
  rejected: "danger",
  cancelled: "muted",
};
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
