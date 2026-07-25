import { useState } from "react";
import { Inbox, RefreshCw } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Badge, Button } from "../components/ui";
import { Table } from "../components/Table";
import { dateTime } from "../lib/format";
import { useApi } from "../lib/useApi";
import { useAppNotifications } from "../components/app-notifications-context";
import { RequestReviewModal } from "../components/hr/RequestReviewModal";
import { type RequestListItem } from "../lib/hr";

export function PheDuyet() {
  const { notify } = useAppNotifications();
  const { data, loading, error, reload } = useApi<RequestListItem[]>("/api/requests?scope=inbox");
  const [activeId, setActiveId] = useState<string | null>(null);
  const rows = data ?? [];

  return (
    <div className="gc-root">
      <PageHeader title="Phê duyệt" subtitle="Các đơn đang chờ bạn duyệt ở bước hiện tại" />

      <GlassPanel strong className="overflow-hidden rounded-[20px]">
        <div className="flex items-center justify-between border-b border-[var(--gc-border)] px-5 py-4">
          <div>
            <h2 className="font-bold text-[var(--text)]">Hộp thư duyệt</h2>
            <p className="text-xs text-[var(--text-secondary)]">Bấm “Xem &amp; duyệt” để xem chi tiết, ký xác nhận và quyết định.</p>
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
            empty="Không có đơn nào chờ bạn duyệt 🎉"
            columns={[
              { header: "Mã đơn", cell: (r) => <span className="font-mono text-xs font-bold text-[var(--accent)]">{r.requestNo}</span> },
              { header: "Loại đơn", cell: (r) => <span className="font-semibold">{r.typeLabel}</span> },
              { header: "Người gửi", cell: (r) => <span>{r.employeeName} <span className="text-xs text-[var(--text-muted)]">({r.employeeCode})</span></span> },
              { header: "Bước", cell: (r) => <Badge color="warning">Bước {r.currentStep}/{r.totalSteps}</Badge> },
              { header: "Ngày gửi", cell: (r) => <span className="whitespace-nowrap text-xs text-[var(--text-secondary)]">{dateTime(r.createdAt)}</span> },
              {
                header: "",
                align: "right",
                cell: (r) => (
                  <Button variant="soft" onClick={() => setActiveId(r.id)}>
                    <Inbox className="h-4 w-4" /> Xem &amp; duyệt
                  </Button>
                ),
              },
            ]}
          />
        )}
      </GlassPanel>

      {activeId && (
        <RequestReviewModal
          id={activeId}
          mode="act"
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
