import { PageHeader } from "../components/Layout";
import { GlassCard } from "../components/Glass";
import { Table } from "../components/Table";
import { Badge } from "../components/ui";
import { useApi } from "../lib/useApi";
import { dateTime } from "../lib/format";
import type { AuditEntry } from "../lib/types";

export function SaoLuu() {
  const { data, loading, error } = useApi<AuditEntry[]>("/api/audit?take=200");

  return (
    <div>
      <PageHeader title="Sao lưu & Nhật ký" subtitle="Nhật ký hoạt động hệ thống" />
      <GlassCard className="overflow-hidden p-0">
        <div className="border-b border-[var(--glass-border)] px-5 py-4">
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
      </GlassCard>
    </div>
  );
}
