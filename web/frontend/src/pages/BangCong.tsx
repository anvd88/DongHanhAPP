import { useState } from "react";
import { PageHeader } from "../components/Layout";
import { MonthPicker } from "../components/DateField";
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
          <MonthPicker
            value={month}
            onChange={(next) => setMonth(next || currentMonth())}
            clearable={false}
            ariaLabel="Chọn tháng bảng công"
          />
        }
      />

      <TimesheetCalendar month={month} data={data ?? null} loading={loading} error={error} onReload={reload} />
    </div>
  );
}
