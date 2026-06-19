import { useState } from "react";
import { Plus, Search, Trash2, RefreshCw } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassCard } from "../components/Glass";
import { Table } from "../components/Table";
import { Button, Input, Spinner, Badge } from "../components/ui";
import { useApi } from "../lib/useApi";
import { api } from "../lib/api";
import { money, date } from "../lib/format";
import type { GiaCongListItem } from "../lib/types";
import { GiaCongEditor } from "./GiaCongEditor";

const TABS = [
  { key: "all", label: "Tất cả" },
  { key: "xuat", label: "Xuất gia công" },
  { key: "nhap", label: "Nhập gia công" },
  { key: "dangxuly", label: "Đang xử lý" },
];

const statusColor = (s: string) =>
  s === "Hoàn thành" ? "success" : s === "Đang xử lý" ? "accent" : s === "Chờ đối tác" ? "warning" : "danger";

export function GiaCong() {
  const [filter, setFilter] = useState("all");
  const [search, setSearch] = useState("");
  const [editing, setEditing] = useState<number | "new" | null>(null);
  const { data, loading, error, reload } = useApi<GiaCongListItem[]>(
    `/api/giacong/?filter=${filter}&search=${encodeURIComponent(search)}`,
    [filter, search]
  );

  const remove = async (id: number, e: React.MouseEvent) => {
    e.stopPropagation();
    if (!confirm("Xóa phiếu gia công này?")) return;
    await api.del(`/api/giacong/${id}`);
    reload();
  };

  return (
    <div>
      <PageHeader
        title="Gia công"
        subtitle="Quản lý phiếu gia công xuất / nhập"
        actions={
          <>
            <Button variant="ghost" onClick={reload}><RefreshCw className="h-4 w-4" /> Làm mới</Button>
            <Button onClick={() => setEditing("new")}><Plus className="h-4 w-4" /> Tạo phiếu</Button>
          </>
        }
      />

      <GlassCard className="mb-4 flex flex-wrap items-center gap-3 p-3">
        <div className="flex flex-wrap gap-1.5">
          {TABS.map((t) => (
            <button
              key={t.key}
              onClick={() => setFilter(t.key)}
              className={`rounded-xl px-3.5 py-2 text-sm font-semibold transition-all ${
                filter === t.key ? "text-white" : "text-[var(--text-secondary)] hover:bg-black/5 dark:hover:bg-white/10"
              }`}
              style={filter === t.key ? { background: "var(--accent)" } : undefined}
            >
              {t.label}
            </button>
          ))}
        </div>
        <div className="relative ml-auto max-w-xs flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--text-muted)]" />
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Tìm mã phiếu, đối tác…" className="pl-9" />
        </div>
      </GlassCard>

      <GlassCard className="overflow-hidden p-0">
        {loading ? (
          <Spinner />
        ) : error ? (
          <div className="p-5 text-sm text-[var(--danger)]">{error}</div>
        ) : (
          <Table<GiaCongListItem>
            rows={data ?? []}
            keyOf={(r) => r.id}
            onRowClick={(r) => setEditing(r.id)}
            empty="Chưa có phiếu gia công"
            columns={[
              { header: "Mã phiếu", cell: (r) => <span className="font-semibold">{r.maPhieu}</span> },
              { header: "Đối tác", cell: (r) => r.doiTac || "—" },
              { header: "Loại", cell: (r) => <span className="text-[var(--text-secondary)]">{r.loaiPhieu}</span> },
              { header: "Ngày lập", cell: (r) => date(r.ngayLap) },
              { header: "Mặt hàng", align: "center", cell: (r) => r.soMatHang },
              { header: "Tổng GT", align: "right", cell: (r) => <span className="font-semibold">{money(r.tongGiaTri)} ₫</span> },
              { header: "Tiến độ", align: "center", cell: (r) => (
                <div className="flex items-center gap-2">
                  <div className="h-1.5 w-16 overflow-hidden rounded-full bg-black/10 dark:bg-white/10">
                    <div className="h-full rounded-full" style={{ width: `${r.tienDo}%`, background: "var(--accent)" }} />
                  </div>
                  <span className="text-xs text-[var(--text-muted)]">{r.tienDo}%</span>
                </div>
              ) },
              { header: "Trạng thái", cell: (r) => <Badge color={statusColor(r.trangThai)}>{r.trangThai}</Badge> },
              { header: "", align: "right", cell: (r) => (
                <button onClick={(e) => remove(r.id, e)} className="rounded-lg p-1.5 text-[var(--text-muted)] hover:bg-red-500/10 hover:text-[var(--danger)]">
                  <Trash2 className="h-4 w-4" />
                </button>
              ) },
            ]}
          />
        )}
      </GlassCard>

      {editing !== null && (
        <GiaCongEditor id={editing} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); reload(); }} />
      )}
    </div>
  );
}
