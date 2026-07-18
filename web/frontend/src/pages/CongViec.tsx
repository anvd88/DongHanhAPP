import { useCallback, useEffect, useMemo, useState } from "react";
import {
  CalendarClock,
  CheckCircle2,
  ClipboardCheck,
  ClipboardList,
  Inbox,
  Pencil,
  Plus,
  RefreshCw,
  Send,
  Star,
  Trash2,
  Undo2,
  XCircle,
} from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Badge, Button, Field, Input, Select, Spinner, EmptyState } from "../components/ui";
import { Modal } from "../components/Modal";
import { useApi } from "../lib/useApi";
import { useAppNotifications } from "../components/AppNotifications";
import { dateTime, date as fmtDate } from "../lib/format";
import {
  PRIORITY_COLOR,
  PRIORITY_LABEL,
  STATUS_COLOR,
  STATUS_LABEL,
  tasksApi,
  type CreateTaskBody,
  type TaskDetailResult,
  type TaskListResult,
  type TaskMeta,
  type WorkTask,
} from "../lib/tasks";

type Tab = "inbox" | "outbox";

function StatusBadge({ status }: { status: string }) {
  return <Badge color={STATUS_COLOR[status] ?? "muted"}>{STATUS_LABEL[status] ?? status}</Badge>;
}
function PriorityBadge({ priority }: { priority: string }) {
  return <Badge color={PRIORITY_COLOR[priority] ?? "muted"}>{PRIORITY_LABEL[priority] ?? priority}</Badge>;
}

function ProgressBar({ value }: { value: number }) {
  const v = Math.max(0, Math.min(100, value));
  return (
    <div className="h-1.5 w-full overflow-hidden rounded-full bg-black/10 dark:bg-white/10">
      <div className="h-full rounded-full transition-all" style={{ width: `${v}%`, background: "var(--accent)" }} />
    </div>
  );
}

function TaskCard({ task, side, onOpen }: { task: WorkTask; side: Tab; onOpen: () => void }) {
  const who = side === "inbox" ? `Người giao: ${task.assignerName}` : `Người nhận: ${task.assigneeName}`;
  return (
    <button
      type="button"
      onClick={onOpen}
      className="flex w-full flex-col gap-2.5 rounded-2xl p-4 text-left transition hover:-translate-y-0.5"
      style={{ background: "var(--glass-bg-strong)", border: "1px solid var(--glass-border)" }}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <span className="text-xs font-bold text-[var(--text-muted)]">{task.taskNo}</span>
            <PriorityBadge priority={task.priority} />
          </div>
          <div className="mt-1 truncate font-bold text-[var(--text)]">{task.title}</div>
        </div>
        <StatusBadge status={task.status} />
      </div>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-[var(--text-secondary)]">
        <span>{who}</span>
        {task.dueAt && (
          <span className={`inline-flex items-center gap-1 ${task.overdue ? "font-semibold text-[var(--danger)]" : ""}`}>
            <CalendarClock className="h-3.5 w-3.5" /> Hạn {fmtDate(task.dueAt)}
            {task.overdue && " · quá hạn"}
          </span>
        )}
      </div>
      {(task.status === "in_progress" || task.status === "submitted" || task.progress > 0) && (
        <div className="flex items-center gap-2">
          <ProgressBar value={task.status === "accepted" ? 100 : task.progress} />
          <span className="w-9 text-right text-xs font-semibold text-[var(--text-secondary)]">{task.progress}%</span>
        </div>
      )}
    </button>
  );
}

// ── Modal giao việc / sửa việc ──
function TaskFormModal({
  open,
  onClose,
  meta,
  editing,
  onSaved,
}: {
  open: boolean;
  onClose: () => void;
  meta: TaskMeta | null;
  editing: WorkTask | null;
  onSaved: () => void;
}) {
  const { notify } = useAppNotifications();
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [assignee, setAssignee] = useState("");
  const [priority, setPriority] = useState("normal");
  const [dueAt, setDueAt] = useState("");
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!open) return;
    setTitle(editing?.title ?? "");
    setDescription(editing?.description ?? "");
    setAssignee(editing?.assigneeUsername ?? "");
    setPriority(editing?.priority ?? "normal");
    setDueAt(editing?.dueAt ? toLocalInput(editing.dueAt) : "");
  }, [open, editing]);

  const submit = async () => {
    if (!title.trim()) return notify.error("Vui lòng nhập tên công việc.");
    if (!assignee) return notify.error("Vui lòng chọn người nhận việc.");
    setSaving(true);
    const body: CreateTaskBody = {
      title: title.trim(),
      description: description.trim(),
      assigneeUsername: assignee,
      priority,
      dueAt: dueAt ? new Date(dueAt).toISOString() : null,
    };
    try {
      if (editing) await tasksApi.update(editing.id, body);
      else await tasksApi.create(body);
      notify.success(editing ? "Đã cập nhật công việc." : "Đã giao việc.");
      onSaved();
      onClose();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không lưu được công việc.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? `Sửa công việc ${editing.taskNo}` : "Giao việc mới"}
      panel
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Huỷ
          </Button>
          <Button loading={saving} onClick={submit}>
            {editing ? "Lưu thay đổi" : "Giao việc"}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <Field label="Tên công việc">
          <Input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Ví dụ: Kiểm kê kho vật tư tầng 2" />
        </Field>
        <Field label="Mô tả chi tiết">
          <textarea
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={4}
            placeholder="Yêu cầu, phạm vi, tiêu chí nghiệm thu…"
            className="km-form-control w-full resize-y rounded-xl border px-3.5 py-2.5 text-sm outline-none transition-all focus:border-[var(--accent)] focus:ring-2 focus:ring-[var(--accent-soft)]"
          />
        </Field>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Người nhận việc">
            <Select value={assignee} onChange={(e) => setAssignee(e.target.value)} className="w-full">
              <option value="">— Chọn nhân viên —</option>
              {(meta?.assignees ?? []).map((a) => (
                <option key={a.username} value={a.username}>
                  {a.fullName}
                  {a.department ? ` · ${a.department}` : ""}
                </option>
              ))}
            </Select>
          </Field>
          <Field label="Mức ưu tiên">
            <Select value={priority} onChange={(e) => setPriority(e.target.value)} className="w-full">
              {["low", "normal", "high", "urgent"].map((p) => (
                <option key={p} value={p}>
                  {PRIORITY_LABEL[p]}
                </option>
              ))}
            </Select>
          </Field>
        </div>
        <Field label="Hạn hoàn thành (không bắt buộc)">
          <Input type="datetime-local" value={dueAt} onChange={(e) => setDueAt(e.target.value)} />
        </Field>
      </div>
    </Modal>
  );
}

// ── Modal chi tiết + thao tác ──
function TaskDetailModal({
  id,
  onClose,
  onChanged,
  onEdit,
}: {
  id: string;
  onClose: () => void;
  onChanged: () => void;
  onEdit: (task: WorkTask) => void;
}) {
  const { notify, confirm } = useAppNotifications();
  const { data, loading, reload } = useApi<TaskDetailResult>(`/api/tasks/${id}`, [id]);
  const [note, setNote] = useState("");
  const [progress, setProgress] = useState(0);
  const [rating, setRating] = useState(0);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (data?.task) setProgress(data.task.progress);
  }, [data?.task]);

  const run = async (fn: () => Promise<unknown>, ok: string) => {
    setBusy(true);
    try {
      await fn();
      notify.success(ok);
      setNote("");
      setRating(0);
      reload({ silent: true });
      onChanged();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Thao tác thất bại.");
    } finally {
      setBusy(false);
    }
  };

  const task = data?.task;
  const flags = data?.flags;

  return (
    <Modal open onClose={onClose} title={task ? `${task.taskNo} · ${task.title}` : "Chi tiết công việc"} wide panel>
      {loading && !task ? (
        <Spinner />
      ) : !task || !flags ? (
        <EmptyState title="Không tải được công việc" />
      ) : (
        <div className="space-y-5">
          <div className="flex flex-wrap items-center gap-2">
            <StatusBadge status={task.status} />
            <PriorityBadge priority={task.priority} />
            {task.rating ? (
              <Badge color="warning">
                <Star className="h-3.5 w-3.5" /> {task.rating}/5
              </Badge>
            ) : null}
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
            <InfoRow label="Người giao" value={task.assignerName} />
            <InfoRow label="Người nhận" value={task.assigneeName} />
            <InfoRow label="Ngày giao" value={dateTime(task.createdAt)} />
            <InfoRow
              label="Hạn hoàn thành"
              value={task.dueAt ? dateTime(task.dueAt) + (task.overdue ? " (quá hạn)" : "") : "Không đặt hạn"}
              danger={task.overdue}
            />
          </div>

          {task.description && (
            <div>
              <div className="mb-1 text-xs font-semibold text-[var(--text-secondary)]">Mô tả</div>
              <p className="whitespace-pre-wrap text-sm text-[var(--text)]">{task.description}</p>
            </div>
          )}

          <div>
            <div className="mb-1 flex items-center justify-between text-xs font-semibold text-[var(--text-secondary)]">
              <span>Tiến độ</span>
              <span>{task.progress}%</span>
            </div>
            <ProgressBar value={task.status === "accepted" ? 100 : task.progress} />
          </div>

          {task.submitNote && (
            <InfoBlock label="Ghi chú khi nộp" text={task.submitNote} />
          )}
          {task.reviewNote && (
            <InfoBlock
              label={task.status === "rejected" ? "Lý do trả lại" : "Ý kiến nghiệm thu"}
              text={task.reviewNote}
              tone={task.status === "rejected" ? "danger" : undefined}
            />
          )}

          {/* Dòng thời gian */}
          <div>
            <div className="mb-2 text-xs font-semibold text-[var(--text-secondary)]">Lịch sử</div>
            <div className="space-y-2.5">
              {(data?.events ?? []).map((ev) => (
                <div key={ev.id} className="flex gap-3">
                  <div className="mt-1.5 h-2 w-2 shrink-0 rounded-full" style={{ background: "var(--accent)" }} />
                  <div className="min-w-0 flex-1">
                    <div className="text-sm text-[var(--text)]">
                      <span className="font-semibold">{ev.actorName}</span> · {EVENT_LABEL[ev.kind] ?? ev.kind}
                    </div>
                    {ev.note && <div className="text-sm text-[var(--text-secondary)]">{ev.note}</div>}
                    <div className="text-[0.7rem] text-[var(--text-muted)]">{dateTime(ev.createdAt)}</div>
                  </div>
                </div>
              ))}
            </div>
          </div>

          {/* Thao tác của người nhận */}
          {flags.mine && task.status !== "accepted" && task.status !== "cancelled" && task.status !== "submitted" && (
            <div className="space-y-3 rounded-2xl p-4" style={{ background: "var(--accent-soft)" }}>
              <div className="text-sm font-bold text-[var(--text)]">Bạn là người thực hiện</div>
              {flags.canStart && (
                <Button variant="soft" loading={busy} onClick={() => run(() => tasksApi.start(task.id), "Đã bắt đầu.")}>
                  Bắt đầu làm
                </Button>
              )}
              <div className="flex items-center gap-3">
                <input
                  type="range"
                  min={0}
                  max={100}
                  value={progress}
                  onChange={(e) => setProgress(Number(e.target.value))}
                  className="flex-1"
                />
                <span className="w-10 text-right text-sm font-semibold">{progress}%</span>
                <Button
                  variant="ghost"
                  loading={busy}
                  onClick={() => run(() => tasksApi.progress(task.id, progress, note), "Đã cập nhật tiến độ.")}
                >
                  Lưu tiến độ
                </Button>
              </div>
              <textarea
                value={note}
                onChange={(e) => setNote(e.target.value)}
                rows={2}
                placeholder="Ghi chú kết quả để nộp nghiệm thu…"
                className="km-form-control w-full resize-y rounded-xl border px-3.5 py-2.5 text-sm outline-none focus:border-[var(--accent)] focus:ring-2 focus:ring-[var(--accent-soft)]"
              />
              <Button loading={busy} onClick={() => run(() => tasksApi.submit(task.id, note), "Đã nộp nghiệm thu.")}>
                <Send className="h-4 w-4" /> Nộp để nghiệm thu
              </Button>
            </div>
          )}

          {/* Nghiệm thu của người giao */}
          {flags.canReview && (
            <div className="space-y-3 rounded-2xl p-4" style={{ background: "var(--accent-soft)" }}>
              <div className="text-sm font-bold text-[var(--text)]">Nghiệm thu công việc</div>
              <div className="flex items-center gap-1">
                <span className="mr-1 text-xs font-semibold text-[var(--text-secondary)]">Đánh giá:</span>
                {[1, 2, 3, 4, 5].map((n) => (
                  <button key={n} type="button" onClick={() => setRating(n)} aria-label={`${n} sao`}>
                    <Star
                      className="h-5 w-5"
                      style={{ fill: n <= rating ? "var(--accent)" : "transparent", color: "var(--accent)" }}
                    />
                  </button>
                ))}
              </div>
              <textarea
                value={note}
                onChange={(e) => setNote(e.target.value)}
                rows={2}
                placeholder="Nhận xét (bắt buộc khi trả lại)…"
                className="km-form-control w-full resize-y rounded-xl border px-3.5 py-2.5 text-sm outline-none focus:border-[var(--accent)] focus:ring-2 focus:ring-[var(--accent-soft)]"
              />
              <div className="flex flex-wrap gap-2">
                <Button
                  loading={busy}
                  onClick={() => run(() => tasksApi.accept(task.id, note, rating || undefined), "Đã nghiệm thu đạt.")}
                >
                  <CheckCircle2 className="h-4 w-4" /> Nghiệm thu đạt
                </Button>
                <Button
                  variant="danger"
                  loading={busy}
                  onClick={() => {
                    if (!note.trim()) return notify.error("Vui lòng nhập lý do trả lại.");
                    run(() => tasksApi.reject(task.id, note.trim()), "Đã trả lại công việc.");
                  }}
                >
                  <Undo2 className="h-4 w-4" /> Trả lại
                </Button>
              </div>
            </div>
          )}

          {/* Quản lý (người giao) */}
          {flags.assignedByMe && (
            <div className="flex flex-wrap gap-2 border-t border-[var(--glass-border)] pt-4">
              {flags.canEdit && (
                <Button variant="ghost" onClick={() => onEdit(task)}>
                  <Pencil className="h-4 w-4" /> Sửa
                </Button>
              )}
              {flags.canCancel && (
                <Button
                  variant="ghost"
                  loading={busy}
                  onClick={async () => {
                    const ok = await confirm({
                      title: "Huỷ công việc?",
                      description: "Công việc sẽ chuyển sang trạng thái Đã huỷ.",
                      confirmLabel: "Huỷ việc",
                      tone: "warning",
                    });
                    if (ok) run(() => tasksApi.cancel(task.id, note), "Đã huỷ công việc.");
                  }}
                >
                  <XCircle className="h-4 w-4" /> Huỷ việc
                </Button>
              )}
              <Button
                variant="danger"
                loading={busy}
                onClick={async () => {
                  const ok = await confirm({
                    title: "Xoá công việc?",
                    description: "Xoá vĩnh viễn công việc và toàn bộ lịch sử. Không thể hoàn tác.",
                    confirmLabel: "Xoá",
                    tone: "danger",
                  });
                  if (ok)
                    run(async () => {
                      await tasksApi.remove(task.id);
                      onClose();
                    }, "Đã xoá công việc.");
                }}
              >
                <Trash2 className="h-4 w-4" /> Xoá
              </Button>
            </div>
          )}
        </div>
      )}
    </Modal>
  );
}

const EVENT_LABEL: Record<string, string> = {
  assigned: "đã giao việc",
  reassigned: "đã chuyển người nhận",
  updated: "đã cập nhật thông tin",
  started: "đã bắt đầu làm",
  progress: "cập nhật tiến độ",
  submitted: "đã nộp nghiệm thu",
  accepted: "đã nghiệm thu đạt",
  rejected: "đã trả lại",
  cancelled: "đã huỷ việc",
  comment: "trao đổi",
};

function InfoRow({ label, value, danger }: { label: string; value: string; danger?: boolean }) {
  return (
    <div>
      <div className="text-xs font-semibold text-[var(--text-secondary)]">{label}</div>
      <div className={`text-sm ${danger ? "font-semibold text-[var(--danger)]" : "text-[var(--text)]"}`}>{value}</div>
    </div>
  );
}
function InfoBlock({ label, text, tone }: { label: string; text: string; tone?: "danger" }) {
  return (
    <div className="rounded-xl p-3" style={{ background: "var(--glass-bg-strong)", border: "1px solid var(--glass-border)" }}>
      <div className={`mb-1 text-xs font-semibold ${tone === "danger" ? "text-[var(--danger)]" : "text-[var(--text-secondary)]"}`}>
        {label}
      </div>
      <p className="whitespace-pre-wrap text-sm text-[var(--text)]">{text}</p>
    </div>
  );
}

function toLocalInput(iso: string): string {
  const d = new Date(iso);
  if (isNaN(d.getTime())) return "";
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

export function CongViec() {
  const { data, loading, error, reload } = useApi<TaskListResult>("/api/tasks");
  const { data: meta, reload: reloadMeta } = useApi<TaskMeta>("/api/tasks/meta");
  const [tab, setTab] = useState<Tab>("inbox");
  const [detailId, setDetailId] = useState<string | null>(null);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<WorkTask | null>(null);

  const canAssign = data?.canAssign ?? meta?.canAssign ?? false;
  const inbox = data?.inbox ?? [];
  const outbox = data?.outbox ?? [];
  const summary = data?.summary;

  // Nếu không phải người giao thì luôn ở tab "của tôi".
  useEffect(() => {
    if (!canAssign && tab === "outbox") setTab("inbox");
  }, [canAssign, tab]);

  const rows = tab === "inbox" ? inbox : outbox;
  const openForm = useCallback((task: WorkTask | null) => {
    setEditing(task);
    setFormOpen(true);
  }, []);

  const refreshAll = useCallback(() => {
    reload({ silent: true });
    reloadMeta({ silent: true });
  }, [reload, reloadMeta]);

  const tabs = useMemo(() => {
    const arr: { key: Tab; label: string; icon: typeof Inbox; count?: number }[] = [
      { key: "inbox", label: "Việc của tôi", icon: Inbox, count: summary?.inboxActionable || undefined },
    ];
    if (canAssign) arr.push({ key: "outbox", label: "Việc tôi giao", icon: ClipboardCheck, count: summary?.outboxReview || undefined });
    return arr;
  }, [canAssign, summary]);

  return (
    <div className="gc-root">
      <PageHeader
        title="Việc được giao"
        subtitle={canAssign ? "Giao việc cho nhân viên và nghiệm thu kết quả" : "Công việc được giao cho bạn"}
        actions={
          canAssign ? (
            <Button onClick={() => openForm(null)}>
              <Plus className="h-4 w-4" /> Giao việc mới
            </Button>
          ) : undefined
        }
      />

      <div className="mb-4 flex items-center gap-2">
        <div
          className="inline-grid rounded-2xl p-1"
          style={{ gridTemplateColumns: `repeat(${tabs.length}, minmax(0,1fr))`, background: "var(--glass-bg-strong)", border: "1px solid var(--glass-border)" }}
        >
          {tabs.map((t) => {
            const active = tab === t.key;
            const Icon = t.icon;
            return (
              <button
                key={t.key}
                type="button"
                onClick={() => setTab(t.key)}
                className="inline-flex items-center justify-center gap-2 rounded-xl px-4 py-2 text-sm font-bold transition"
                style={active ? { background: "var(--accent)", color: "#fff" } : { color: "var(--text-secondary)" }}
              >
                <Icon className="h-4 w-4" />
                {t.label}
                {t.count ? <Badge color={active ? "muted" : "warning"}>{t.count}</Badge> : null}
              </button>
            );
          })}
        </div>
        <button
          type="button"
          onClick={() => reload()}
          className="grid h-9 w-9 place-items-center rounded-full bg-[var(--accent-soft)] text-[var(--accent)]"
          aria-label="Làm mới"
        >
          <RefreshCw className={`h-4 w-4 ${loading ? "animate-spin" : ""}`} />
        </button>
      </div>

      <GlassPanel strong className="rounded-[20px] p-4">
        {error ? (
          <div className="p-4 text-sm text-[var(--danger)]">{error}</div>
        ) : loading && rows.length === 0 ? (
          <Spinner />
        ) : rows.length === 0 ? (
          <EmptyState
            icon={<ClipboardList />}
            title={tab === "inbox" ? "Chưa có việc nào được giao cho bạn" : "Bạn chưa giao việc nào"}
            hint={tab === "outbox" && canAssign ? 'Bấm "Giao việc mới" để bắt đầu.' : undefined}
          />
        ) : (
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {rows.map((task) => (
              <TaskCard key={task.id} task={task} side={tab} onOpen={() => setDetailId(task.id)} />
            ))}
          </div>
        )}
      </GlassPanel>

      {detailId && (
        <TaskDetailModal
          id={detailId}
          onClose={() => setDetailId(null)}
          onChanged={refreshAll}
          onEdit={(task) => {
            setDetailId(null);
            openForm(task);
          }}
        />
      )}
      <TaskFormModal
        open={formOpen}
        onClose={() => setFormOpen(false)}
        meta={meta}
        editing={editing}
        onSaved={refreshAll}
      />
    </div>
  );
}
