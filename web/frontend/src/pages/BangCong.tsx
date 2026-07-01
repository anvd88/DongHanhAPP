import { useState } from "react";
import { AlarmClock, CalendarClock, Clock, RefreshCw, TimerReset, TrendingUp } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { Badge } from "../components/ui";
import { Table } from "../components/Table";
import { date } from "../lib/format";
import { useApi } from "../lib/useApi";
import { timesheetStatusColor, type Timesheet, type TimesheetDay } from "../lib/hr";

function currentMonth() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
}

function fmtMinutes(m: number) {
  if (!m) return "—";
  const h = Math.floor(m / 60);
  const mm = m % 60;
  return h > 0 ? `${h}h${mm ? String(mm).padStart(2, "0") : ""}` : `${mm} phút`;
}

function StatCard({ icon, label, value, hint, tone }: { icon: React.ReactNode; label: string; value: string; hint?: string; tone: string }) {
  return (
    <GlassPanel strong className="flex items-center gap-3 rounded-2xl p-4">
      <span className={`grid h-11 w-11 shrink-0 place-items-center rounded-xl ${tone}`}>{icon}</span>
      <div className="min-w-0">
        <div className="text-xs text-[var(--text-secondary)]">{label}</div>
        <div className="text-xl font-bold text-[var(--text)]">{value}</div>
        {hint && <div className="text-[0.7rem] text-[var(--text-muted)]">{hint}</div>}
      </div>
    </GlassPanel>
  );
}

export function BangCong() {
  const [month, setMonth] = useState(currentMonth());
  const { data, loading, error, reload } = useApi<Timesheet>(`/api/timesheet/me?month=${month}`, [month]);
  const s = data?.summary;

  return (
    <div className="gc-root">
      <PageHeader
        title="Bảng công"
        subtitle="Đối chiếu giờ chấm công khuôn mặt với ca làm — tự tính đi muộn, về sớm, tăng ca"
        actions={
          <label className="flex items-center gap-2 rounded-xl border border-[var(--glass-border)] bg-white/55 px-3 py-2 text-sm dark:bg-white/5">
            <CalendarClock className="h-4 w-4 text-[var(--accent)]" />
            <input
              type="month"
              value={month}
              onChange={(e) => setMonth(e.target.value)}
              className="bg-transparent text-sm text-[var(--text)] outline-none"
            />
          </label>
        }
      />

      <div className="mb-4 grid grid-cols-2 gap-3 lg:grid-cols-4">
        <StatCard icon={<Clock className="h-5 w-5 text-emerald-600" />} tone="bg-emerald-500/15"
          label="Ngày công" value={`${s?.workedDays ?? 0}`} hint={`Vắng: ${s?.absentDays ?? 0} ngày`} />
        <StatCard icon={<AlarmClock className="h-5 w-5 text-amber-600" />} tone="bg-amber-500/15"
          label="Đi muộn" value={`${s?.lateDays ?? 0} lần`} hint={`Tổng ${fmtMinutes(s?.totalLateMinutes ?? 0)}`} />
        <StatCard icon={<TimerReset className="h-5 w-5 text-orange-600" />} tone="bg-orange-500/15"
          label="Về sớm" value={`${s?.earlyDays ?? 0} lần`} hint={`Tổng ${fmtMinutes(s?.totalEarlyMinutes ?? 0)}`} />
        <StatCard icon={<TrendingUp className="h-5 w-5 text-violet-600" />} tone="bg-violet-500/15"
          label="Tăng ca" value={fmtMinutes(s?.totalOvertimeMinutes ?? 0)} hint={`${s?.totalWorkedHours ?? 0} giờ làm`} />
      </div>

      <GlassPanel strong className="overflow-hidden rounded-[20px]">
        <div className="flex items-center justify-between border-b border-[var(--gc-border)] px-5 py-4">
          <h2 className="font-bold text-[var(--text)]">Chi tiết theo ngày</h2>
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
          <Table<TimesheetDay>
            loading={loading}
            rows={data?.days ?? []}
            keyOf={(r) => r.date}
            empty="Chưa có dữ liệu chấm công trong tháng này"
            columns={[
              { header: "Ngày", cell: (r) => <span className="whitespace-nowrap font-semibold">{date(r.date)}</span> },
              { header: "Ca làm", cell: (r) => <span className="text-[var(--text-secondary)]">{r.shiftName || "—"}</span> },
              { header: "Giờ vào", cell: (r) => <span className="font-mono">{r.checkIn ?? "—"}</span> },
              { header: "Giờ ra", cell: (r) => <span className="font-mono">{r.checkOut ?? "—"}</span> },
              { header: "Đi muộn", align: "right", cell: (r) => r.lateMinutes ? <span className="text-amber-600">{fmtMinutes(r.lateMinutes)}</span> : <span className="text-[var(--text-muted)]">—</span> },
              { header: "Về sớm", align: "right", cell: (r) => r.earlyMinutes ? <span className="text-orange-600">{fmtMinutes(r.earlyMinutes)}</span> : <span className="text-[var(--text-muted)]">—</span> },
              { header: "Tăng ca", align: "right", cell: (r) => r.overtimeMinutes ? <span className="text-violet-600">{fmtMinutes(r.overtimeMinutes)}</span> : <span className="text-[var(--text-muted)]">—</span> },
              { header: "Trạng thái", align: "right", cell: (r) => <Badge color={timesheetStatusColor(r.status)}>{r.status}</Badge> },
            ]}
          />
        )}
      </GlassPanel>
    </div>
  );
}
