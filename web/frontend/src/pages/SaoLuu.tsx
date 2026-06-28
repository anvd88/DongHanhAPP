import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Table } from "../components/Table";
import { Badge } from "../components/ui";
import { useApi } from "../lib/useApi";
import { dateTime } from "../lib/format";
import type { AuditEntry } from "../lib/types";

export function SaoLuu() {
  const { data, loading, error } = useApi<AuditEntry[]>("/api/audit?take=200");

  return (
    <div className="gc-root">
      <PageHeader title="Sao lưu & Nhật ký" subtitle="Nhật ký hoạt động hệ thống" />
      <GlassPanel strong className="overflow-hidden rounded-[20px]">
        <div className="border-b border-[var(--gc-border)] px-5 py-4">
          <h2 className="font-bold text-[var(--text)]">Nhật ký hoạt động</h2>
          <p className="text-xs text-[var(--text-secondary)]">200 hoạt động gần nhất</p>
        </div>
        {error ? (
          <div className="p-5 text-sm text-[var(--danger)]">{error}</div>
        ) : (
          <Table<AuditEntry>
            loading={loading}
            rows={data ?? []}
            keyOf={(_, i) => i}
            empty="Chưa có nhật ký"
            columns={[
              { header: "Thời gian", cell: (r) => <span className="whitespace-nowrap text-[var(--text-secondary)]">{dateTime(r.occurredAt)}</span> },
              { header: "Người dùng", cell: (r) => <span className="font-semibold">{r.username}</span> },
              { header: "Hành động", cell: (r) => <Badge>{r.action}</Badge> },
              { header: "Đối tượng", cell: (r) => <span className="text-[var(--text-secondary)]">{r.entity}</span> },
              { header: "Chi tiết", cell: (r) => <span className="text-[var(--text-secondary)]">{r.details || r.entityName}</span> },
            ]}
          />
        )}
      </GlassPanel>
    </div>
  );
}
