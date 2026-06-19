import { Wallet, TrendingUp, FileText, Users } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassCard } from "../components/Glass";
import { StatCard } from "../components/StatCard";
import { Table } from "../components/Table";
import { Spinner } from "../components/ui";
import { useApi } from "../lib/useApi";
import { money } from "../lib/format";
import type { Reports } from "../lib/types";

export function BaoCao() {
  const { data, loading, error } = useApi<Reports>("/api/reports");

  return (
    <div>
      <PageHeader title="Báo cáo" subtitle="Tổng hợp doanh thu và chứng từ" />
      {loading && <Spinner />}
      {error && <GlassCard className="p-5 text-sm text-[var(--danger)]">{error}</GlassCard>}
      {data && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <StatCard label="Tổng thu chi" value={money(data.totalPayments) + " ₫"} icon={Wallet} color="var(--success)" />
            <StatCard label="Doanh thu tháng này" value={money(data.monthRevenue) + " ₫"} icon={TrendingUp} color="var(--warning)" />
            <StatCard label="Tổng chứng từ" value={money(data.totalDocuments)} icon={FileText} color="var(--purple)" />
            <StatCard label="Khách hàng" value={money(data.activeCustomers)} icon={Users} color="var(--accent)" />
          </div>

          <GlassCard className="mt-5 overflow-hidden p-0">
            <div className="border-b border-[var(--glass-border)] px-5 py-4">
              <h2 className="font-bold text-[var(--text)]">Tổng hợp theo tháng</h2>
              <p className="text-xs text-[var(--text-secondary)]">12 tháng gần nhất</p>
            </div>
            <Table
              rows={data.monthly}
              keyOf={(r) => `${r.year}-${r.month}`}
              empty="Chưa có dữ liệu"
              columns={[
                { header: "Tháng", cell: (r) => <span className="font-semibold">{String(r.month).padStart(2, "0")}/{r.year}</span> },
                { header: "Số chứng từ", align: "center", cell: (r) => r.documentCount },
                { header: "Tổng tiền", align: "right", cell: (r) => <span className="font-semibold accent-text">{money(r.total)} ₫</span> },
              ]}
            />
          </GlassCard>
        </>
      )}
    </div>
  );
}
