import { Users, FileText, Wallet, TrendingUp } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassCard } from "../components/Glass";
import { StatCard } from "../components/StatCard";
import { Table } from "../components/Table";
import { Spinner } from "../components/ui";
import { useApi } from "../lib/useApi";
import { useAuth } from "../lib/auth";
import { money, date } from "../lib/format";
import type { Dashboard as Dash, DocumentListItem } from "../lib/types";

export function Dashboard() {
  const { user } = useAuth();
  const { data, loading, error } = useApi<Dash>("/api/dashboard");

  return (
    <div>
      <PageHeader
        title={`Chào mừng trở lại, ${user?.fullName || user?.username} 👋`}
        subtitle="Tổng quan hoạt động kế toán của doanh nghiệp"
      />

      {loading && <Spinner />}
      {error && <GlassCard className="p-5 text-sm text-[var(--danger)]">{error}</GlassCard>}

      {data && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <StatCard label="Khách hàng hoạt động" value={money(data.activeCustomers)} icon={Users} color="var(--accent)" />
            <StatCard label="Tổng chứng từ" value={money(data.totalDocuments)} icon={FileText} color="var(--purple)" />
            <StatCard label="Tổng thanh toán" value={money(data.totalPayments) + " ₫"} icon={Wallet} color="var(--success)" />
            <StatCard
              label="Doanh thu tháng"
              value={money(data.monthRevenue) + " ₫"}
              sub={`Tháng ${String(data.month).padStart(2, "0")}/${data.year}`}
              icon={TrendingUp}
              color="var(--warning)"
            />
          </div>

          <GlassCard className="mt-5 overflow-hidden p-0">
            <div className="border-b border-[var(--glass-border)] px-5 py-4">
              <h2 className="font-bold text-[var(--text)]">Hoạt động gần đây</h2>
              <p className="text-xs text-[var(--text-secondary)]">Các chứng từ mới nhất</p>
            </div>
            <Table<DocumentListItem>
              rows={data.recent}
              keyOf={(r) => r.id}
              columns={[
                { header: "Số phiếu", cell: (r) => <span className="font-semibold">{r.voucherNo}</span> },
                { header: "Ngày", cell: (r) => date(r.date) },
                { header: "Khách hàng", cell: (r) => r.customerName },
                { header: "Nội dung", cell: (r) => <span className="text-[var(--text-secondary)]">{r.content}</span> },
                { header: "Tổng tiền", align: "right", cell: (r) => <span className="font-semibold">{money(r.total)} ₫</span> },
              ]}
            />
          </GlassCard>
        </>
      )}
    </div>
  );
}
