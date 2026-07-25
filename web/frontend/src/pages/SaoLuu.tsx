import { useMemo, useState } from "react";
import { Download, RotateCcw, Search } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Table } from "../components/Table";
import { Badge, Button, Input, Select } from "../components/ui";
import { useApi } from "../lib/useApi";
import { api } from "../lib/api";
import { useAppNotifications } from "../components/app-notifications-context";
import { dateTime } from "../lib/format";

interface AuditItem {
  id: number;
  occurredAt: string;
  username: string;
  action: string;
  entity: string;
  entityName: string;
  details: string;
  before: string | null;
  after: string | null;
}
interface AuditPage {
  items: AuditItem[];
  total: number;
  page: number;
  pageSize: number;
}
interface AuditFilterOptions {
  actions: string[];
  entities: string[];
  /** Nhóm nghiệp vụ do server cấp (kế toán chỉ nhận nhóm "Thu chi tiền mặt"). */
  groups: { key: string; label: string }[];
  /** Dải tháng "yyyy-MM" từ bản ghi cũ nhất tới mới nhất, mới nhất trước. */
  months: string[];
  /** false = tài khoản này chỉ được xem phần tiền (server ép, không phải ẩn trên UI). */
  canSeeAll: boolean;
}

const PAGE_SIZE = 50;
const EMPTY = { search: "", action: "", entity: "", group: "", month: "", from: "", to: "" };

const monthLabel = (m: string) => {
  const [y, mm] = m.split("-");
  return `Tháng ${Number(mm)}/${y}`;
};

export function SaoLuu() {
  const { notify } = useAppNotifications();
  // Bộ lọc đã "áp dụng" (đưa vào query) tách khỏi ô đang gõ để không refetch theo từng ký tự.
  const [applied, setApplied] = useState(EMPTY);
  const [draft, setDraft] = useState(EMPTY);
  const [page, setPage] = useState(1);
  const [exporting, setExporting] = useState(false);

  const filterQs = useMemo(() => {
    const p = new URLSearchParams();
    if (applied.search) p.set("search", applied.search);
    if (applied.action) p.set("action", applied.action);
    if (applied.entity) p.set("entity", applied.entity);
    if (applied.group) p.set("group", applied.group);
    // Chọn tháng thì server bỏ qua from/to (tháng thắng) — đừng gửi kèm cho khỏi gây hiểu nhầm.
    if (applied.month) p.set("month", applied.month);
    else {
      if (applied.from) p.set("from", applied.from);
      if (applied.to) p.set("to", applied.to);
    }
    return p;
  }, [applied]);

  const query = useMemo(() => {
    const p = new URLSearchParams(filterQs);
    p.set("page", String(page));
    p.set("pageSize", String(PAGE_SIZE));
    return p.toString();
  }, [filterQs, page]);

  const { data, loading, error } = useApi<AuditPage>(`/api/audit?${query}`);
  const { data: opts } = useApi<AuditFilterOptions>("/api/audit/filters");

  const total = data?.total ?? 0;
  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  function applyFilters() {
    setPage(1);
    setApplied(draft);
  }
  function clearFilters() {
    setDraft(EMPTY);
    setApplied(EMPTY);
    setPage(1);
  }
  /** Xem nhanh một tháng (0 = tháng này, -1 = tháng trước): áp dụng ngay, không cần bấm "Lọc". */
  function quickMonth(offset: number) {
    const d = new Date();
    d.setDate(1);
    d.setMonth(d.getMonth() + offset);
    const month = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
    const next = { ...draft, month, from: "", to: "" };
    setDraft(next);
    setApplied(next);
    setPage(1);
  }

  async function exportFile(format: "csv" | "xlsx") {
    setExporting(true);
    try {
      const p = new URLSearchParams(filterQs);
      p.set("format", format);
      const blob = await api.getBlob(`/api/audit/export?${p.toString()}`);
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `NhatKy_${new Date().toISOString().slice(0, 10)}.${format === "xlsx" ? "xlsx" : "csv"}`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
      notify.success(`Đã xuất nhật ký (${format.toUpperCase()}).`, "Xuất nhật ký");
    } catch {
      notify.error("Không xuất được nhật ký. Vui lòng thử lại.", "Xuất nhật ký");
    } finally {
      setExporting(false);
    }
  }

  return (
    <div className="gc-root">
      {/* Tên cũ là "Sao lưu & Nhật ký" nhưng trang chưa từng có chức năng sao lưu nào — gọi đúng tên
          để người dùng biết vào đây tra cứu được gì. */}
      <PageHeader
        title="Nhật ký hoạt động"
        subtitle={
          opts && !opts.canSeeAll
            ? "Lịch sử thu chi tiền mặt của phòng kế toán"
            : "Lịch sử mọi thao tác trên hệ thống"
        }
      />

      <GlassPanel strong className="mb-4 rounded-[20px] p-4">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-6">
          <div className="lg:col-span-2">
            <Input
              placeholder="Tìm người dùng, hành động, chi tiết…"
              value={draft.search}
              onChange={(e) => setDraft({ ...draft, search: e.target.value })}
              onKeyDown={(e) => e.key === "Enter" && applyFilters()}
            />
          </div>
          <Select value={draft.action} onChange={(e) => setDraft({ ...draft, action: e.target.value })}>
            <option value="">Mọi hành động</option>
            {(opts?.actions ?? []).map((a) => (
              <option key={a} value={a}>{a}</option>
            ))}
          </Select>
          <Select value={draft.group} onChange={(e) => setDraft({ ...draft, group: e.target.value, entity: "" })}>
            <option value="">Mọi nghiệp vụ</option>
            {(opts?.groups ?? []).map((gr) => (
              <option key={gr.key} value={gr.key}>{gr.label}</option>
            ))}
          </Select>
          {/* Dải tháng do server suy từ mốc đầu/cuối của nhật ký (rẻ hơn quét cả bảng rất nhiều). */}
          <Select value={draft.month} onChange={(e) => setDraft({ ...draft, month: e.target.value, from: "", to: "" })}>
            <option value="">Mọi tháng</option>
            {(opts?.months ?? []).map((m) => (
              <option key={m} value={m}>{monthLabel(m)}</option>
            ))}
          </Select>
          <Select value={draft.entity} onChange={(e) => setDraft({ ...draft, entity: e.target.value })}>
            <option value="">Mọi đối tượng</option>
            {(opts?.entities ?? []).map((en) => (
              <option key={en} value={en}>{en}</option>
            ))}
          </Select>
        </div>
        {/* Khoảng ngày chỉ dùng khi KHÔNG lọc theo tháng — tránh hai bộ lọc thời gian đá nhau. */}
        {!draft.month && (
          <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-6">
            <label className="text-xs font-semibold text-[var(--text-secondary)] lg:col-span-1">
              Từ ngày
              <Input type="date" value={draft.from} onChange={(e) => setDraft({ ...draft, from: e.target.value })} />
            </label>
            <label className="text-xs font-semibold text-[var(--text-secondary)] lg:col-span-1">
              Đến ngày
              <Input type="date" value={draft.to} onChange={(e) => setDraft({ ...draft, to: e.target.value })} />
            </label>
          </div>
        )}
        <div className="mt-3 flex flex-wrap items-center gap-2">
          {/* Lối tắt hay dùng nhất: xem ngay tháng này / tháng trước, khỏi phải mở ô chọn. */}
          <Button variant="ghost" onClick={() => quickMonth(0)}>Tháng này</Button>
          <Button variant="ghost" onClick={() => quickMonth(-1)}>Tháng trước</Button>
          <Button onClick={applyFilters}>
            <Search className="h-4 w-4" /> Lọc
          </Button>
          <Button variant="ghost" onClick={clearFilters}>
            <RotateCcw className="h-4 w-4" /> Xóa lọc
          </Button>
          <div className="flex-1" />
          <Button variant="soft" onClick={() => exportFile("csv")} loading={exporting} disabled={exporting}>
            <Download className="h-4 w-4" /> CSV
          </Button>
          <Button variant="soft" onClick={() => exportFile("xlsx")} loading={exporting} disabled={exporting}>
            <Download className="h-4 w-4" /> Excel
          </Button>
        </div>
      </GlassPanel>

      <GlassPanel strong className="overflow-hidden rounded-[20px]">
        <div className="flex items-center justify-between border-b border-[var(--gc-border)] px-5 py-4">
          <div>
            <h2 className="font-bold text-[var(--text)]">Nhật ký hoạt động</h2>
            <p className="text-xs text-[var(--text-secondary)]">{total.toLocaleString("vi-VN")} bản ghi khớp bộ lọc</p>
          </div>
        </div>
        {error ? (
          <div className="p-5 text-sm text-[var(--danger)]">{error}</div>
        ) : (
          <>
            <Table<AuditItem>
              loading={loading}
              rows={data?.items ?? []}
              keyOf={(r) => r.id}
              empty="Chưa có nhật ký khớp bộ lọc"
              columns={[
                { header: "Thời gian", cell: (r) => <span className="whitespace-nowrap text-[var(--text-secondary)]">{dateTime(r.occurredAt)}</span> },
                { header: "Người dùng", cell: (r) => <span className="font-semibold">{r.username}</span> },
                { header: "Hành động", cell: (r) => <Badge>{r.action}</Badge> },
                { header: "Đối tượng", cell: (r) => <span className="text-[var(--text-secondary)]">{r.entity}</span> },
                {
                  header: "Chi tiết",
                  cell: (r) => (
                    <div className="min-w-0">
                      <span className="text-[var(--text-secondary)]">{r.details || r.entityName}</span>
                      {(r.before || r.after) && (
                        <div className="mt-1 flex flex-col gap-0.5 text-[11px] text-[var(--text-muted)]">
                          {r.before && <code className="block truncate" title={r.before}>− {r.before}</code>}
                          {r.after && <code className="block truncate" title={r.after}>+ {r.after}</code>}
                        </div>
                      )}
                    </div>
                  ),
                },
              ]}
            />
            <div className="flex items-center justify-between gap-3 border-t border-[var(--gc-border)] px-5 py-3 text-sm">
              <span className="text-[var(--text-secondary)]">
                Trang {data?.page ?? page} / {totalPages}
              </span>
              <div className="flex gap-2">
                <Button variant="ghost" disabled={page <= 1 || loading} onClick={() => setPage((p) => Math.max(1, p - 1))}>
                  Trước
                </Button>
                <Button variant="ghost" disabled={page >= totalPages || loading} onClick={() => setPage((p) => p + 1)}>
                  Sau
                </Button>
              </div>
            </div>
          </>
        )}
      </GlassPanel>
    </div>
  );
}
