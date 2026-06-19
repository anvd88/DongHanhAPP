import { useMemo, useState } from "react";
import { Plus, Search, Trash2, Pencil } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassCard } from "../components/Glass";
import { Table } from "../components/Table";
import { Button, Input, Spinner } from "../components/ui";
import { useApi } from "../lib/useApi";
import { api } from "../lib/api";
import { money, date } from "../lib/format";
import type { Customer, DocumentListItem } from "../lib/types";
import { DocumentEditor } from "./DocumentEditor";

export function KeToan({ salesOnly = false }: { salesOnly?: boolean }) {
  const path = salesOnly ? "/api/sales" : "/api/documents";
  const { data, loading, error, reload } = useApi<DocumentListItem[]>(path, [salesOnly]);
  const { data: customers } = useApi<Customer[]>("/api/customers");
  const [search, setSearch] = useState("");
  const [editing, setEditing] = useState<string | null | "new">(null);

  const rows = useMemo(
    () =>
      (data ?? []).filter((d) => {
        const q = search.toLowerCase();
        return !q || d.voucherNo.toLowerCase().includes(q) || d.customerName.toLowerCase().includes(q) || d.content.toLowerCase().includes(q);
      }),
    [data, search]
  );

  const remove = async (id: string, e: React.MouseEvent) => {
    e.stopPropagation();
    if (!confirm("Xóa chứng từ này?")) return;
    await api.del(`/api/documents/${id}`);
    reload();
  };

  return (
    <div>
      <PageHeader
        title={salesOnly ? "Bán hàng" : "Kế toán"}
        subtitle={salesOnly ? "Quản lý đơn bán hàng" : "Quản lý chứng từ kế toán"}
        actions={
          !salesOnly && (
            <Button onClick={() => setEditing("new")}>
              <Plus className="h-4 w-4" /> Tạo phiếu
            </Button>
          )
        }
      />

      <GlassCard className="mb-4 p-3">
        <div className="relative max-w-md">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--text-muted)]" />
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Tìm số phiếu, khách hàng, nội dung…" className="pl-9" />
        </div>
      </GlassCard>

      <GlassCard className="overflow-hidden p-0">
        {loading ? (
          <Spinner />
        ) : error ? (
          <div className="p-5 text-sm text-[var(--danger)]">{error}</div>
        ) : (
          <Table<DocumentListItem>
            rows={rows}
            keyOf={(r) => r.id}
            onRowClick={(r) => setEditing(r.id)}
            empty="Chưa có chứng từ nào"
            columns={[
              { header: "Số phiếu", cell: (r) => <span className="font-semibold">{r.voucherNo}</span> },
              { header: "Ngày", cell: (r) => date(r.date) },
              { header: "Khách hàng", cell: (r) => r.customerName },
              { header: "Nội dung", cell: (r) => <span className="text-[var(--text-secondary)]">{r.content}</span> },
              { header: "Tổng tiền", align: "right", cell: (r) => <span className="font-semibold">{money(r.total)} ₫</span> },
              {
                header: "",
                align: "right",
                cell: (r) => (
                  <div className="flex justify-end gap-1">
                    <button onClick={(e) => { e.stopPropagation(); setEditing(r.id); }} className="rounded-lg p-1.5 text-[var(--text-muted)] hover:bg-[var(--accent-soft)] hover:text-[var(--accent)]">
                      <Pencil className="h-4 w-4" />
                    </button>
                    <button onClick={(e) => remove(r.id, e)} className="rounded-lg p-1.5 text-[var(--text-muted)] hover:bg-red-500/10 hover:text-[var(--danger)]">
                      <Trash2 className="h-4 w-4" />
                    </button>
                  </div>
                ),
              },
            ]}
          />
        )}
      </GlassCard>

      {editing !== null && (
        <DocumentEditor
          id={editing}
          customers={customers ?? []}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            reload();
          }}
        />
      )}
    </div>
  );
}
