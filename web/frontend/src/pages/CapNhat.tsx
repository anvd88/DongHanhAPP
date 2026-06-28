import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Table } from "../components/Table";
import { Badge } from "../components/ui";
import { useApi } from "../lib/useApi";
import { dateTime } from "../lib/format";
import type { Release } from "../lib/types";

export function CapNhat() {
  const { data, loading, error } = useApi<Release[]>("/api/releases/");

  return (
    <div className="gc-root">
      <PageHeader title="Cập nhật phiên bản" subtitle="Lịch sử phát hành ứng dụng" />
      <GlassPanel strong className="overflow-hidden rounded-[20px]">
        {error ? (
          <div className="p-5 text-sm text-[var(--danger)]">{error}</div>
        ) : (
          <Table<Release>
            loading={loading}
            rows={data ?? []}
            keyOf={(r) => r.id}
            empty="Chưa có bản phát hành"
            columns={[
              { header: "Phiên bản", cell: (r) => <span className="font-semibold">{r.version}</span> },
              { header: "Bắt buộc", cell: (r) => (r.isMandatory ? <Badge color="danger">Bắt buộc</Badge> : <Badge color="muted">Tùy chọn</Badge>) },
              { header: "Trạng thái", cell: (r) => (r.isPublished ? <Badge color="success">Đã phát hành</Badge> : <Badge color="muted">Nháp</Badge>) },
              { header: "Ngày", cell: (r) => dateTime(r.publishedAt) },
              { header: "Người phát hành", cell: (r) => r.publishedBy || "—" },
              { header: "Ghi chú", cell: (r) => <span className="text-[var(--text-secondary)]">{r.releaseNotes}</span> },
            ]}
          />
        )}
      </GlassPanel>
    </div>
  );
}
