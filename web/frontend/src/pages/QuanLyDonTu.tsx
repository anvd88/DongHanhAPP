import { useMemo, useState } from "react";
import { CheckCircle2, ClipboardList, Clock, RefreshCw, Search, XCircle } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Badge, Input, Select } from "../components/ui";
import { Table } from "../components/Table";
import { dateTime } from "../lib/format";
import { useApi } from "../lib/useApi";
import { useAppNotifications } from "../components/AppNotifications";
import { RequestReviewModal } from "../components/hr/RequestReviewModal";
import { requestStatusColor, requestStatusLabel, type RequestListItem, type RequestType } from "../lib/hr";

type StatusKey = "all" | "Pending" | "Approved" | "Rejected" | "Cancelled";

const STATUS_TABS: { key: StatusKey; label: string }[] = [
  { key: "all", label: "Tất cả" },
  { key: "Pending", label: "Chờ duyệt" },
  { key: "Approved", label: "Đã duyệt" },
  { key: "Rejected", label: "Từ chối" },
  { key: "Cancelled", label: "Đã hủy" },
];

function StatCard({ label, value, tone }: { label: string; value: number; tone: string }) {
  const toneCls: Record<string, string> = {
    accent: "text-[var(--accent)]",
    warning: "text-amber-500",
    success: "text-emerald-500",
    danger: "text-red-500",
  };
  return (
    <GlassPanel className="rounded-2xl px-4 py-3.5">
      <div className="text-xs font-semibold text-[var(--text-secondary)]">{label}</div>
      <div className={`mt-1 text-2xl font-extrabold ${toneCls[tone] ?? toneCls.accent}`}>{value}</div>
    </GlassPanel>
  );
}

export function QuanLyDonTu() {
  const { notify } = useAppNotifications();
  const { data: types } = useApi<RequestType[]>("/api/requests/types");
  const { data, loading, error, reload } = useApi<RequestListItem[]>("/api/requests?scope=all");
  const [status, setStatus] = useState<StatusKey>("all");
  const [type, setType] = useState<string>("all");
  const [search, setSearch] = useState("");
  const [activeId, setActiveId] = useState<string | null>(null);

  const all = useMemo(() => data ?? [], [data]);

  const counts = useMemo(
    () => ({
      total: all.length,
      pending: all.filter((r) => r.status === "Pending").length,
      approved: all.filter((r) => r.status === "Approved").length,
      rejected: all.filter((r) => r.status === "Rejected").length,
    }),
    [all],
  );

  const rows = useMemo(() => {
    const q = search.trim().toLowerCase();
    return all
      .filter((r) => {
        if (status !== "all" && r.status !== status) return false;
        if (type !== "all" && r.type !== type) return false;
        if (
          q &&
          ![r.requestNo, r.employeeName, r.employeeCode, r.typeLabel, r.title]
            .some((f) => (f ?? "").toLowerCase().includes(q))
        )
          return false;
        return true;
      })
      // Ưu tiên đơn CHƯA xử lý (Chờ duyệt) lên đầu; trong mỗi nhóm sắp theo ngày gửi mới nhất trước.
      .sort((a, b) => {
        const pa = a.status === "Pending" ? 0 : 1;
        const pb = b.status === "Pending" ? 0 : 1;
        if (pa !== pb) return pa - pb;
        return new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime();
      });
  }, [all, status, type, search]);

  return (
    <div className="gc-root">
      <PageHeader
        title="Quản lý đơn từ"
        subtitle="Theo dõi và xử lý toàn bộ đơn từ trong công ty"
      />

      <div className="mb-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
        <StatCard label="Tổng số đơn" value={counts.total} tone="accent" />
        <StatCard label="Chờ duyệt" value={counts.pending} tone="warning" />
        <StatCard label="Đã duyệt" value={counts.approved} tone="success" />
        <StatCard label="Từ chối" value={counts.rejected} tone="danger" />
      </div>

      <GlassPanel strong className="overflow-hidden rounded-[20px]">
        <div className="flex flex-col gap-3 border-b border-[var(--gc-border)] px-5 py-4">
          <div className="flex flex-wrap items-center gap-2">
            {STATUS_TABS.map((t) => {
              const active = status === t.key;
              return (
                <button
                  key={t.key}
                  type="button"
                  onClick={() => setStatus(t.key)}
                  className={`rounded-full px-3.5 py-1.5 text-sm font-semibold transition-all ${
                    active
                      ? "bg-[var(--accent)] text-white shadow-sm"
                      : "bg-[var(--accent-soft)] text-[var(--text-secondary)] hover:text-[var(--accent)]"
                  }`}
                >
                  {t.label}
                </button>
              );
            })}
            <button
              type="button"
              onClick={() => reload()}
              className="ml-auto grid h-8 w-8 place-items-center rounded-full bg-[var(--accent-soft)] text-[var(--accent)]"
              aria-label="Làm mới"
            >
              <RefreshCw className={`h-4 w-4 ${loading ? "animate-spin" : ""}`} />
            </button>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <div className="relative flex-1 min-w-[200px]">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--text-muted)]" />
              <Input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Tìm mã đơn, tên nhân viên, mã NV…"
                className="pl-9"
              />
            </div>
            <Select value={type} onChange={(e) => setType(e.target.value)}>
              <option value="all">Tất cả loại đơn</option>
              {(types ?? []).map((t) => (
                <option key={t.type} value={t.type}>{t.label}</option>
              ))}
            </Select>
          </div>
        </div>

        {error ? (
          <div className="p-5 text-sm text-[var(--danger)]">{error}</div>
        ) : (
          <Table<RequestListItem>
            loading={loading}
            rows={rows}
            keyOf={(r) => r.id}
            onRowClick={(r) => setActiveId(r.id)}
            empty={all.length === 0 ? "Chưa có đơn từ nào" : "Không có đơn khớp bộ lọc"}
            columns={[
              { header: "Mã đơn", cell: (r) => <span className="font-mono text-xs font-bold text-[var(--accent)]">{r.requestNo}</span> },
              { header: "Loại đơn", cell: (r) => <span className="font-semibold">{r.typeLabel}</span> },
              {
                header: "Người gửi",
                cell: (r) => (
                  <span>
                    {r.employeeName} <span className="text-xs text-[var(--text-muted)]">({r.employeeCode})</span>
                  </span>
                ),
              },
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
              {
                header: "Trạng thái",
                align: "right",
                cell: (r) => (
                  <Badge color={requestStatusColor(r.status)}>
                    {r.status === "Pending" ? <Clock className="h-3.5 w-3.5" /> : r.status === "Approved" ? <CheckCircle2 className="h-3.5 w-3.5" /> : r.status === "Rejected" ? <XCircle className="h-3.5 w-3.5" /> : <ClipboardList className="h-3.5 w-3.5" />}
                    {requestStatusLabel(r.status)}
                  </Badge>
                ),
              },
            ]}
          />
        )}
      </GlassPanel>

      {activeId && (
        <RequestReviewModal
          id={activeId}
          mode="manage"
          onClose={() => setActiveId(null)}
          onDecided={(msg) => {
            setActiveId(null);
            reload({ silent: true });
            notify.success(msg);
          }}
        />
      )}
    </div>
  );
}
