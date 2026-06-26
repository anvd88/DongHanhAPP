import { useMemo, useState } from "react";
import { ArrowDownToLine, ArrowUpFromLine, FileText, PackageCheck, TrendingUp, Users, Wallet } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassCard } from "../components/Glass";
import { StatCard } from "../components/StatCard";
import { Table } from "../components/Table";
import { Spinner } from "../components/ui";
import { useApi } from "../lib/useApi";
import { money, num } from "../lib/format";
import type { GiaCongReport, Reports } from "../lib/types";

const vnd = (value: number) => `${money(value)} ₫`;

export function BaoCao() {
  const [doiTac, setDoiTac] = useState("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const { data, loading, error } = useApi<Reports>("/api/reports");

  const giaCongPath = useMemo(() => {
    const qs = new URLSearchParams();
    if (doiTac.trim()) qs.set("doiTac", doiTac.trim());
    if (fromDate) qs.set("from", fromDate);
    if (toDate) qs.set("to", toDate);
    const query = qs.toString();
    return `/api/giacong/report${query ? `?${query}` : ""}`;
  }, [doiTac, fromDate, toDate]);

  const { data: giaCong, loading: giaCongLoading, error: giaCongError } = useApi<GiaCongReport>(giaCongPath);

  return (
    <div>
      <PageHeader title="Báo cáo" subtitle="Tổng hợp doanh thu, chứng từ và tồn gia công" />
      {loading && <Spinner />}
      {error && <GlassCard className="p-5 text-sm text-[var(--danger)]">{error}</GlassCard>}
      {data && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <StatCard label="Tổng thu chi" value={vnd(data.totalPayments)} icon={Wallet} color="var(--success)" />
            <StatCard label="Doanh thu tháng này" value={vnd(data.monthRevenue)} icon={TrendingUp} color="var(--warning)" />
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
                { header: "Tổng tiền", align: "right", cell: (r) => <span className="font-semibold accent-text">{vnd(r.total)}</span> },
              ]}
            />
          </GlassCard>
        </>
      )}

      <GlassCard className="mt-5 p-4">
        <div className="flex flex-wrap items-end gap-3">
          <div className="min-w-[220px] flex-1">
            <label className="mb-1 block text-xs font-bold text-[var(--text-secondary)]">Đối tác gia công</label>
            <input
              value={doiTac}
              onChange={(e) => setDoiTac(e.target.value)}
              placeholder="Lọc theo đối tác"
              className="w-full rounded-xl border border-[var(--glass-border)] bg-white/45 px-3 py-2 text-sm font-semibold text-[var(--text)] outline-none transition focus:border-[var(--accent)] dark:bg-white/5"
            />
          </div>
          <div>
            <label className="mb-1 block text-xs font-bold text-[var(--text-secondary)]">Từ ngày</label>
            <input
              type="date"
              value={fromDate}
              onChange={(e) => setFromDate(e.target.value)}
              className="rounded-xl border border-[var(--glass-border)] bg-white/45 px-3 py-2 text-sm font-semibold text-[var(--text)] outline-none transition focus:border-[var(--accent)] dark:bg-white/5"
            />
          </div>
          <div>
            <label className="mb-1 block text-xs font-bold text-[var(--text-secondary)]">Đến ngày</label>
            <input
              type="date"
              value={toDate}
              onChange={(e) => setToDate(e.target.value)}
              className="rounded-xl border border-[var(--glass-border)] bg-white/45 px-3 py-2 text-sm font-semibold text-[var(--text)] outline-none transition focus:border-[var(--accent)] dark:bg-white/5"
            />
          </div>
        </div>
      </GlassCard>

      {giaCongLoading && <Spinner />}
      {giaCongError && <GlassCard className="mt-4 p-5 text-sm text-[var(--danger)]">{giaCongError}</GlassCard>}
      {giaCong && (
        <>
          <div className="mt-4 grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <StatCard label="Đã xuất gia công" value={num(giaCong.soLuongXuat)} icon={ArrowUpFromLine} tone="blue" />
            <StatCard label="Đã nhập về" value={num(giaCong.soLuongNhap)} icon={ArrowDownToLine} tone="mint" />
            <StatCard label="Còn lại sau nhập" value={num(giaCong.soLuongConTaiCongTy)} icon={PackageCheck} tone="amber" />
            <StatCard label="Phí gia công phải trả" value={vnd(giaCong.tienGiaCongPhaiTra)} icon={Wallet} tone="violet" />
          </div>

          <GlassCard className="mt-5 overflow-hidden p-0">
            <div className="border-b border-[var(--glass-border)] px-5 py-4">
              <h2 className="font-bold text-[var(--text)]">Gia công theo đối tác</h2>
            </div>
            <Table
              rows={giaCong.partners}
              keyOf={(r) => r.doiTac}
              empty="Chưa có dữ liệu gia công"
              columns={[
                { header: "Đối tác", cell: (r) => <span className="font-semibold">{r.doiTac}</span> },
                { header: "Xuất", align: "right", cell: (r) => num(r.soLuongXuat) },
                { header: "Nhập", align: "right", cell: (r) => num(r.soLuongNhap) },
                { header: "Còn lại", align: "right", cell: (r) => <span className="font-semibold accent-text">{num(r.soLuongConTaiCongTy)}</span> },
                { header: "Phí gia công", align: "right", cell: (r) => vnd(r.tienGiaCongPhaiTra) },
              ]}
            />
          </GlassCard>

          <GlassCard className="mt-5 overflow-hidden p-0">
            <div className="border-b border-[var(--glass-border)] px-5 py-4">
              <h2 className="font-bold text-[var(--text)]">Gia công theo mặt hàng</h2>
            </div>
            <Table
              rows={giaCong.items}
              keyOf={(r, i) => `${r.doiTac}-${r.tenHang}-${r.quyCach}-${i}`}
              empty="Chưa có dữ liệu mặt hàng"
              columns={[
                { header: "Đối tác", cell: (r) => r.doiTac },
                { header: "Tên hàng", cell: (r) => <span className="font-semibold">{r.tenHang}</span> },
                { header: "Quy cách", cell: (r) => r.quyCach },
                { header: "ĐVT", cell: (r) => r.donViTinh },
                { header: "Xuất", align: "right", cell: (r) => num(r.soLuongXuat) },
                { header: "Nhập", align: "right", cell: (r) => num(r.soLuongNhap) },
                { header: "Còn lại", align: "right", cell: (r) => <span className="font-semibold accent-text">{num(r.soLuongConTaiCongTy)}</span> },
                { header: "Phí gia công", align: "right", cell: (r) => vnd(r.tienGiaCongPhaiTra) },
              ]}
            />
          </GlassCard>
        </>
      )}
    </div>
  );
}
