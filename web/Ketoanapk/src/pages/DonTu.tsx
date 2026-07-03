import { useMemo, useState } from "react";
import { CheckCircle2, Clock, FilePlus2, RefreshCw, Send, X, XCircle } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Modal } from "../components/Modal";
import { Badge, Button, Field, Input, Select } from "../components/ui";
import { Table } from "../components/Table";
import { api } from "../lib/api";
import { dateTime } from "../lib/format";
import { useApi } from "../lib/useApi";
import { useAppNotifications } from "../components/AppNotifications";
import {
  fieldDisplayValue,
  fieldLabel,
  requestFields,
  requestStatusColor,
  requestStatusLabel,
  type RequestDetail,
  type RequestListItem,
  type RequestType,
} from "../lib/hr";

/** Số ngày nghỉ tính cả ngày đầu và ngày cuối; null nếu thiếu/không hợp lệ. */
function inclusiveDays(from?: string, to?: string): number | null {
  if (!from || !to) return null;
  const a = new Date(from + "T00:00:00");
  const b = new Date(to + "T00:00:00");
  if (isNaN(a.getTime()) || isNaN(b.getTime()) || b < a) return null;
  return Math.round((b.getTime() - a.getTime()) / 86_400_000) + 1;
}

function StatusBadge({ status }: { status: string }) {
  const color = requestStatusColor(status);
  const Icon = status === "Approved" ? CheckCircle2 : status === "Rejected" ? XCircle : status === "Cancelled" ? X : Clock;
  return (
    <Badge color={color}>
      <Icon className="h-3.5 w-3.5" /> {requestStatusLabel(status)}
    </Badge>
  );
}

export function DonTu() {
  const { notify, confirm } = useAppNotifications();
  const { data: types } = useApi<RequestType[]>("/api/requests/types");
  const { data, loading, error, reload } = useApi<RequestListItem[]>("/api/requests?scope=mine");
  const [createOpen, setCreateOpen] = useState(false);
  const [detailId, setDetailId] = useState<string | null>(null);

  const rows = data ?? [];

  return (
    <div className="gc-root">
      <PageHeader
        title="Đơn từ"
        subtitle="Gửi và theo dõi trạng thái các đơn nghỉ phép, tăng ca, thanh toán…"
        actions={
          <Button onClick={() => setCreateOpen(true)}>
            <FilePlus2 className="h-4 w-4" /> Tạo đơn mới
          </Button>
        }
      />

      <GlassPanel strong className="overflow-hidden rounded-[20px]">
        <div className="flex items-center justify-between border-b border-[var(--gc-border)] px-5 py-4">
          <div>
            <h2 className="font-bold text-[var(--text)]">Đơn của tôi</h2>
            <p className="text-xs text-[var(--text-secondary)]">Bấm vào một đơn để xem chi tiết và tiến trình duyệt.</p>
          </div>
          <button
            type="button"
            onClick={() => reload()}
            className="grid h-8 w-8 place-items-center rounded-full bg-[var(--accent-soft)] text-[var(--accent)]"
            aria-label="Làm mới"
          >
            <RefreshCw className={`h-4 w-4 ${loading ? "animate-spin" : ""}`} />
          </button>
        </div>

        {error ? (
          <div className="p-5 text-sm text-[var(--danger)]">{error}</div>
        ) : (
          <Table<RequestListItem>
            loading={loading}
            rows={rows}
            keyOf={(r) => r.id}
            onRowClick={(r) => setDetailId(r.id)}
            empty="Bạn chưa gửi đơn nào"
            columns={[
              { header: "Mã đơn", cell: (r) => <span className="font-mono text-xs font-bold text-[var(--accent)]">{r.requestNo}</span> },
              { header: "Loại đơn", cell: (r) => <span className="font-semibold">{r.typeLabel}</span> },
              { header: "Tiêu đề", cell: (r) => <span className="text-[var(--text-secondary)]">{r.title}</span> },
              {
                header: "Tiến trình",
                cell: (r) =>
                  r.status === "Pending" ? (
                    <span className="text-xs text-[var(--text-secondary)]">Bước {r.currentStep}/{r.totalSteps}</span>
                  ) : (
                    <span className="text-xs text-[var(--text-muted)]">—</span>
                  ),
              },
              { header: "Ngày gửi", cell: (r) => <span className="whitespace-nowrap text-xs text-[var(--text-secondary)]">{dateTime(r.createdAt)}</span> },
              { header: "Trạng thái", align: "right", cell: (r) => <StatusBadge status={r.status} /> },
            ]}
          />
        )}
      </GlassPanel>

      {createOpen && (
        <CreateRequestModal
          types={types ?? []}
          onClose={() => setCreateOpen(false)}
          onCreated={() => {
            setCreateOpen(false);
            reload({ silent: true });
            notify.success("Đã gửi đơn. Bạn có thể theo dõi trạng thái tại đây.");
          }}
        />
      )}

      {detailId && (
        <RequestDetailModal
          id={detailId}
          onClose={() => setDetailId(null)}
          onCancelled={() => {
            setDetailId(null);
            reload({ silent: true });
          }}
          confirm={confirm}
          notify={notify}
        />
      )}
    </div>
  );
}

function CreateRequestModal({
  types,
  onClose,
  onCreated,
}: {
  types: RequestType[];
  onClose: () => void;
  onCreated: () => void;
}) {
  const { notify } = useAppNotifications();
  const [type, setType] = useState(types[0]?.type ?? "leave");
  const [title, setTitle] = useState("");
  const [values, setValues] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const fields = requestFields[type] ?? [];
  const daysAutoSynced = type === "leave" || type === "sick";

  const setField = (k: string, v: string) =>
    setValues((s) => {
      const next = { ...s, [k]: v };
      // Nghỉ phép/nghỉ ốm: số ngày nghỉ tự tính khớp khoảng Từ ngày → Đến ngày (tính cả 2 đầu).
      if (daysAutoSynced && (k === "fromDate" || k === "toDate")) {
        const d = inclusiveDays(next.fromDate, next.toDate);
        next.days = d != null ? String(d) : "";
      }
      return next;
    });

  const submit = async () => {
    setSaving(true);
    try {
      const payload: Record<string, unknown> = {};
      for (const f of fields) {
        const v = values[f.key];
        if (v === undefined || v === "") continue;
        payload[f.key] = f.type === "number" || f.type === "money" ? Number(v) : v;
      }
      await api.post("/api/requests", { type, title: title.trim(), payload });
      onCreated();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không gửi được đơn.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title="Tạo đơn mới"
      panel
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Hủy</Button>
          <Button onClick={submit} loading={saving}><Send className="h-4 w-4" /> Gửi đơn</Button>
        </>
      }
    >
      <div className="space-y-4">
        <Field label="Loại đơn">
          <Select
            value={type}
            onChange={(e) => {
              setType(e.target.value);
              setValues({});
            }}
            className="w-full"
          >
            {types.map((t) => (
              <option key={t.type} value={t.type}>{t.label}</option>
            ))}
          </Select>
        </Field>

        <Field label="Tiêu đề (tùy chọn)">
          <Input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Mặc định theo loại đơn" />
        </Field>

        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          {fields.map((f) => {
            const autoDays = daysAutoSynced && f.key === "days";
            if (f.type === "checkboxes") {
              return (
                <div key={f.key} className="sm:col-span-2">
                  <div role="group" aria-label={f.label} className="grid grid-cols-2 gap-2">
                    {f.options?.map((o) => {
                      const checked = values[f.key] === o.value;
                      return (
                        <label
                          key={o.value}
                          className={`flex h-11 cursor-pointer items-center gap-2 rounded-xl border px-3.5 text-sm font-semibold transition-all ${
                            checked
                              ? "border-[var(--accent)] bg-[var(--accent-soft)] text-[var(--accent)] ring-2 ring-[var(--accent-soft)]"
                              : "border-[var(--glass-border)] bg-white/55 text-[var(--text)] hover:border-[var(--accent)] dark:bg-white/5"
                          }`}
                        >
                          <input
                            type="checkbox"
                            checked={checked}
                            onChange={(e) => setField(f.key, e.target.checked ? o.value : "")}
                            className="h-4 w-4 rounded border-[var(--glass-border)] accent-[var(--accent)]"
                          />
                          <span>{o.label}</span>
                        </label>
                      );
                    })}
                  </div>
                </div>
              );
            }
            return (
            <div key={f.key} className={f.type === "textarea" ? "sm:col-span-2" : ""}>
              <Field label={autoDays ? `${f.label} (tự tính)` : f.label}>
                {f.type === "textarea" ? (
                  <textarea
                    value={values[f.key] ?? ""}
                    onChange={(e) => setField(f.key, e.target.value)}
                    rows={3}
                    className="w-full rounded-xl border border-[var(--glass-border)] bg-white/55 px-3.5 py-2.5 text-sm text-[var(--text)] outline-none focus:border-[var(--accent)] focus:ring-2 focus:ring-[var(--accent-soft)] dark:bg-white/5"
                  />
                ) : f.type === "money" ? (
                  <Input
                    inputMode="numeric"
                    value={values[f.key] ? Number(values[f.key]).toLocaleString("en-US") : ""}
                    onChange={(e) => setField(f.key, e.target.value.replace(/[^\d]/g, ""))}
                  />
                ) : f.type === "select" ? (
                  <Select
                    value={values[f.key] ?? ""}
                    onChange={(e) => setField(f.key, e.target.value)}
                    className="w-full"
                  >
                    <option value="" disabled>— Chọn —</option>
                    {f.options?.map((o) => (
                      <option key={o.value} value={o.value}>{o.label}</option>
                    ))}
                  </Select>
                ) : (
                  <Input
                    type={f.type === "number" ? "number" : f.type === "date" ? "date" : f.type === "time" ? "time" : "text"}
                    value={values[f.key] ?? ""}
                    onChange={(e) => setField(f.key, e.target.value)}
                    readOnly={autoDays}
                    className={autoDays ? "cursor-not-allowed opacity-80" : undefined}
                  />
                )}
              </Field>
            </div>
            );
          })}
        </div>
      </div>
    </Modal>
  );
}

function RequestDetailModal({
  id,
  onClose,
  onCancelled,
  confirm,
  notify,
}: {
  id: string;
  onClose: () => void;
  onCancelled: () => void;
  confirm: ReturnType<typeof useAppNotifications>["confirm"];
  notify: ReturnType<typeof useAppNotifications>["notify"];
}) {
  const { data, loading } = useApi<RequestDetail>(`/api/requests/${id}`);
  const [cancelling, setCancelling] = useState(false);

  const payloadRows = useMemo(() => {
    if (!data) return [];
    const p = data.request.payload ?? {};
    const fieldTypeOf = (k: string) => requestFields[data.request.type]?.find((f) => f.key === k)?.type;
    return Object.entries(p).map(([k, v]) => ({
      label: fieldLabel(data.request.type, k),
      value:
        fieldTypeOf(k) === "money" && v != null && v !== ""
          ? Number(v).toLocaleString("en-US")
          : fieldDisplayValue(data.request.type, k, v),
    }));
  }, [data]);

  const doCancel = async () => {
    const ok = await confirm({
      title: "Hủy đơn này?",
      description: "Đơn đang chờ duyệt sẽ bị hủy và không thể khôi phục.",
      confirmLabel: "Hủy đơn",
      tone: "warning",
    });
    if (!ok) return;
    setCancelling(true);
    try {
      await api.post(`/api/requests/${id}/cancel`);
      notify.success("Đã hủy đơn.");
      onCancelled();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không hủy được đơn.");
    } finally {
      setCancelling(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={data ? `${data.request.typeLabel} · ${data.request.requestNo}` : "Chi tiết đơn"}
      panel
      wide
      footer={
        data?.request.status === "Pending" ? (
          <Button variant="danger" onClick={doCancel} loading={cancelling}>Hủy đơn</Button>
        ) : (
          <Button variant="ghost" onClick={onClose}>Đóng</Button>
        )
      }
    >
      {loading || !data ? (
        <div className="py-10 text-center text-sm text-[var(--text-muted)]">Đang tải…</div>
      ) : (
        <div className="grid grid-cols-1 gap-5 lg:grid-cols-2">
          <div>
            <div className="mb-3 flex items-center gap-2">
              <StatusBadge status={data.request.status} />
              <span className="text-xs text-[var(--text-muted)]">Gửi lúc {dateTime(data.request.createdAt)}</span>
            </div>
            <div className="rounded-2xl border border-[var(--glass-border)] p-4">
              <div className="mb-2 text-sm font-bold text-[var(--text)]">{data.request.title}</div>
              <dl className="space-y-2 text-sm">
                <div className="flex justify-between gap-3">
                  <dt className="text-[var(--text-secondary)]">Người gửi</dt>
                  <dd className="font-medium text-[var(--text)]">{data.request.employeeName} ({data.request.employeeCode})</dd>
                </div>
                {data.request.departmentName && (
                  <div className="flex justify-between gap-3">
                    <dt className="text-[var(--text-secondary)]">Phòng ban</dt>
                    <dd className="font-medium text-[var(--text)]">{data.request.departmentName}</dd>
                  </div>
                )}
                {payloadRows.map((row) => (
                  <div key={row.label} className="flex justify-between gap-3">
                    <dt className="text-[var(--text-secondary)]">{row.label}</dt>
                    <dd className="text-right font-medium text-[var(--text)]">{row.value}</dd>
                  </div>
                ))}
              </dl>
            </div>
          </div>

          <div>
            <div className="mb-3 text-sm font-bold text-[var(--text)]">Tiến trình phê duyệt</div>
            <ol className="space-y-3">
              {data.approvals.map((a) => {
                const done = a.status !== "Pending";
                const color = a.status === "Approved" ? "success" : a.status === "Rejected" ? "danger" : "warning";
                return (
                  <li key={a.stepNo} className="flex gap-3 rounded-2xl border border-[var(--glass-border)] p-3">
                    <div className="mt-0.5">
                      <span className={`grid h-7 w-7 place-items-center rounded-full text-xs font-bold ${
                        a.status === "Approved" ? "bg-emerald-500/15 text-emerald-600"
                          : a.status === "Rejected" ? "bg-red-500/15 text-red-600"
                          : "bg-amber-500/15 text-amber-600"}`}>
                        {a.stepNo}
                      </span>
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center justify-between gap-2">
                        <span className="truncate text-sm font-semibold text-[var(--text)]">
                          {a.approverName || (a.approverRole === "Admin" ? "Quản trị viên / HR" : a.approverUsername)}
                        </span>
                        <Badge color={color}>{requestStatusLabel(a.status)}</Badge>
                      </div>
                      {done && (
                        <div className="mt-1 text-xs text-[var(--text-muted)]">
                          {a.decidedBy} · {a.decidedAt ? dateTime(a.decidedAt) : ""}
                          {a.hasSignature && <span className="ml-1 text-[var(--accent)]">· đã ký</span>}
                        </div>
                      )}
                      {a.comment && <div className="mt-1 text-xs text-[var(--text-secondary)]">“{a.comment}”</div>}
                    </div>
                  </li>
                );
              })}
            </ol>
          </div>
        </div>
      )}
    </Modal>
  );
}
