import { useState } from "react";
import { CalendarClock } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { TimesheetCalendar } from "../components/hr/TimesheetCalendar";
import { useApi } from "../lib/useApi";
import type { Timesheet } from "../lib/hr";

function currentMonth() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
}

export function BangCong() {
  const [month, setMonth] = useState(currentMonth());
  const { data, loading, error, reload } = useApi<Timesheet>(`/api/timesheet/me?month=${month}`, [month]);

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

      <TimesheetCalendar month={month} data={data ?? null} loading={loading} error={error} onReload={reload} />
    </div>
  );
}
