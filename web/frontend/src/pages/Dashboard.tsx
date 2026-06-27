import { BarChart3, FileText, Filter, Landmark, Users } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassCard } from "../components/Glass";
import { StatCard } from "../components/StatCard";
import { Table } from "../components/Table";
import { useApi } from "../lib/useApi";
import { useAuth } from "../lib/auth";
import { money, date } from "../lib/format";
import type { Dashboard as Dash, DocumentListItem } from "../lib/types";

const activityType = (row: DocumentListItem, index: number) => {
  const content = `${row.content} ${row.voucherNo}`.toLowerCase();
  if (content.includes("chi") || content.includes("nhập") || index % 3 === 1) return "Chi";
  return "Thu";
};

export function Dashboard() {
  const { user } = useAuth();
  const { data, loading, error } = useApi<Dash>("/api/dashboard");

  return (
    <div className="km-dashboard">
      <PageHeader
        title="Tổng quan"
        subtitle={`Chào mừng trở lại, ${user?.fullName || user?.username || "admin"} 👋`}
      />

      {error && <GlassCard className="p-5 text-sm text-[var(--danger)]">{error}</GlassCard>}

      {!error && (
        <>
          <section className="km-stats-grid">
            <StatCard
              loading={loading}
              label="Khách hàng"
              value={money(data?.activeCustomers ?? 0)}
              sub="Đang hoạt động"
              trend="▲ 8.2%"
              icon={Users}
              tone="blue"
            />
            <StatCard
              loading={loading}
              label="Chứng từ"
              value={money(data?.totalDocuments ?? 0)}
              sub="Tổng phiếu"
              icon={FileText}
              tone="mint"
            />
            <StatCard
              loading={loading}
              label="Thu chi"
              value={money(data?.totalPayments ?? 0)}
              sub="Tổng thanh toán"
              icon={Landmark}
              tone="amber"
            />
            <StatCard
              loading={loading}
              label="Doanh thu tháng"
              value={`${money(data?.monthRevenue ?? 0)} ₫`}
              sub={data ? `Tháng ${String(data.month).padStart(2, "0")}/${data.year}` : "Đang tải"}
              trend="▲ 15.3%"
              icon={BarChart3}
              tone="violet"
            />
          </section>

          <GlassCard className="km-activity-panel" glow={false}>
            <div className="km-activity-header">
              <h2>Hoạt động gần đây</h2>
              <button type="button" className="km-filter-button" aria-label="Lọc hoạt động">
                <Filter className="h-5 w-5" />
              </button>
            </div>
            <Table<DocumentListItem>
              loading={loading}
              rows={data?.recent ?? []}
              keyOf={(row) => row.id}
              empty="Chưa có hoạt động gần đây"
              columns={[
                { header: "Số chứng từ", cell: (row) => <span className="font-semibold">{row.voucherNo}</span> },
                { header: "Ngày", cell: (row) => date(row.date) },
                { header: "Đối tượng", cell: (row) => row.customerName },
                { header: "Nội dung", cell: (row) => <span className="text-[var(--text-secondary)]">{row.content}</span> },
                {
                  header: "Loại",
                  cell: (row, index) => {
                    const type = activityType(row, index);
                    return <span className={`km-type-badge ${type === "Thu" ? "is-income" : "is-expense"}`}>{type}</span>;
                  },
                },
                { header: "Số tiền", align: "right", cell: (row) => <span className="font-semibold">{money(row.total)}</span> },
              ]}
            />
            <button className="km-see-more" type="button">Xem thêm</button>
          </GlassCard>
        </>
      )}
    </div>
  );
}
