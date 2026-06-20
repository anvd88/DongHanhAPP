import { useState, type ChangeEvent, type ReactNode } from "react";
import { Plus, Search, Trash2, RefreshCw } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassCard } from "../components/Glass";
import { Table } from "../components/Table";
import { Button, Spinner, Badge } from "../components/ui";
import { LiquidTabs as LiquidGlassTabs } from "../components/LiquidTabs";
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
            <Button variant="ghost" onClick={() => reload()}><RefreshCw className="h-4 w-4" /> Làm mới</Button>
            <Button onClick={() => setEditing("new")}><Plus className="h-4 w-4" /> Tạo phiếu</Button>
          </>
        }
      />

      <LiquidGlassToolbar>
        <LiquidGlassTabs tabs={TABS} value={filter} onChange={setFilter} />
        <LiquidGlassSearch value={search} onChange={(e) => setSearch(e.target.value)} />
      </LiquidGlassToolbar>

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

function LiquidGlassToolbar({ children }: { children: ReactNode }) {
  return <div className="giacong-liquid-toolbar">{children}</div>;
}

function LiquidGlassSearch({
  value,
  onChange,
}: {
  value: string;
  onChange: (event: ChangeEvent<HTMLInputElement>) => void;
}) {
  return (
    <label className="giacong-liquid-search">
      <Search className="giacong-liquid-search-icon" aria-hidden="true" />
      <input
        value={value}
        onChange={onChange}
        aria-label="Tìm mã phiếu, đối tác"
        placeholder="Tìm mã phiếu, đối tác..."
        className="giacong-liquid-search-input"
      />
    </label>
  );
}
