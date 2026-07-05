import { useEffect, useState } from "react";
import { AlarmClock, Clock, RefreshCw, TimerReset, TrendingUp } from "lucide-react";
import { GlassPanel } from "../glass/GlassPanel";
import { Badge } from "../ui";
import { date } from "../../lib/format";
import { timesheetStatusColor, type Timesheet, type TimesheetDay } from "../../lib/hr";

type CalendarTone = "worked" | "absent" | "overtime" | "warning" | "off" | "empty";
type CalendarCell = { key: string; day: number | null; dateKey: string | null };

const WEEKDAYS = ["T2", "T3", "T4", "T5", "T6", "T7", "CN"];

const calendarToneClass: Record<CalendarTone, string> = {
  worked: "border-emerald-300/70 bg-emerald-500/12 text-emerald-700 dark:border-emerald-500/30 dark:bg-emerald-500/15 dark:text-emerald-300",
  absent: "border-red-300/70 bg-red-500/12 text-red-700 dark:border-red-500/30 dark:bg-red-500/15 dark:text-red-300",
  overtime: "border-violet-300/70 bg-violet-500/12 text-violet-700 dark:border-violet-500/30 dark:bg-violet-500/15 dark:text-violet-300",
  warning: "border-amber-300/70 bg-amber-500/12 text-amber-700 dark:border-amber-500/30 dark:bg-amber-500/15 dark:text-amber-300",
  off: "border-slate-300/70 bg-slate-500/10 text-slate-600 dark:border-white/10 dark:bg-white/8 dark:text-slate-300",
  empty: "border-[var(--glass-border)] bg-white/35 text-[var(--text-muted)] dark:bg-white/5",
};

const calendarDotClass: Record<CalendarTone, string> = {
  worked: "bg-emerald-500",
  absent: "bg-red-500",
  overtime: "bg-violet-500",
  warning: "bg-amber-500",
  off: "bg-slate-400",
  empty: "bg-slate-300 dark:bg-slate-600",
};

const legend: Array<{ tone: CalendarTone; label: string }> = [
  { tone: "worked", label: "Đi làm" },
  { tone: "absent", label: "Nghỉ / vắng" },
  { tone: "overtime", label: "Tăng ca" },
  { tone: "warning", label: "Muộn / thiếu công" },
  { tone: "off", label: "Không phân ca" },
];

function fmtMinutes(m: number) {
  if (!m) return "—";
  const h = Math.floor(m / 60);
  const mm = m % 60;
  return h > 0 ? `${h}h${mm ? String(mm).padStart(2, "0") : ""}` : `${mm} phút`;
}

function calendarDays(month: string): CalendarCell[] {
  const [year, monthIndex] = month.split("-").map(Number);
  const first = new Date(year, monthIndex - 1, 1);
  const offset = (first.getDay() + 6) % 7;
  const total = new Date(year, monthIndex, 0).getDate();
  return [
    ...Array.from({ length: offset }, (_, i) => ({ key: `blank-${i}`, day: null, dateKey: null })),
    ...Array.from({ length: total }, (_, i) => {
      const day = i + 1;
      return {
        key: `${month}-${String(day).padStart(2, "0")}`,
        day,
        dateKey: `${month}-${String(day).padStart(2, "0")}`,
      };
    }),
  ];
}

function toneOfDay(day?: TimesheetDay): CalendarTone {
  if (!day) return "empty";
  if (day.holidayType) return "off";
  if (day.status === "Vắng") return "absent";
  if (day.overtimeMinutes > 0) return "overtime";
  if (day.lateMinutes > 0 || day.earlyMinutes > 0 || day.status.includes("muộn") || day.status.includes("sớm") || day.status.includes("Thiếu")) {
    return "warning";
  }
  if (day.workedHours > 0 || day.checkIn || day.checkOut || timesheetStatusColor(day.status) === "success") return "worked";
  if (day.status === "Không phân ca") return "off";
  return "empty";
}

function toneLabel(day?: TimesheetDay) {
  const tone = toneOfDay(day);
  if (!day) return "Chưa có dữ liệu";
  if (day.holidayType) return day.holidayName || (day.holidayType === "public" ? "Nghỉ lễ" : day.holidayType === "weekly" ? "Nghỉ chủ nhật" : "Nghỉ công ty");
  if (tone === "worked") return "Đi làm";
  if (tone === "absent") return "Nghỉ / vắng";
  if (tone === "overtime") return "Tăng ca";
  if (tone === "warning") return "Cần rà soát";
  if (tone === "off") return "Không phân ca";
  return day.status || "Chưa có dữ liệu";
}

function DetailItem({ label, value, tone }: { label: string; value: React.ReactNode; tone?: string }) {
  return (
    <div className="rounded-xl border border-[var(--glass-border)] bg-white/45 p-3 dark:bg-white/5">
      <div className="text-[0.7rem] font-semibold text-[var(--text-muted)]">{label}</div>
      <div className={`mt-1 text-sm font-bold ${tone ?? "text-[var(--text)]"}`}>{value}</div>
    </div>
  );
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

/**
 * Khung hiển thị bảng công theo lịch tháng: thẻ tổng hợp + lịch + chi tiết ngày.
 * Dùng chung cho trang bảng công cá nhân (BangCong) và trang admin xem của mọi người.
 */
export function TimesheetCalendar({
  month,
  data,
  loading,
  error,
  onReload,
  emptyHint,
}: {
  month: string;
  data: Timesheet | null;
  loading: boolean;
  error: string | null;
  onReload: () => void;
  emptyHint?: string;
}) {
  const [selectedDate, setSelectedDate] = useState<string | null>(null);
  // Đổi tháng / đổi nhân viên thì bỏ chọn ngày cũ.
  useEffect(() => setSelectedDate(null), [month, data]);

  const s = data?.summary;
  const daysByDate = new Map((data?.days ?? []).map((day) => [day.date.slice(0, 10), day]));
  const selectedDay = selectedDate ? daysByDate.get(selectedDate) : undefined;

  return (
    <>
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
          <div>
            <h2 className="font-bold text-[var(--text)]">Lịch tháng</h2>
            <p className="mt-1 text-xs font-medium text-[var(--text-muted)]">Chọn một ngày để xem chi tiết chấm công.</p>
          </div>
          <button
            type="button"
            onClick={() => onReload()}
            className="grid h-8 w-8 place-items-center rounded-full bg-[var(--accent-soft)] text-[var(--accent)]"
            aria-label="Làm mới"
          >
            <RefreshCw className={`h-4 w-4 ${loading ? "animate-spin" : ""}`} />
          </button>
        </div>

        {error ? (
          <div className="p-5 text-sm text-[var(--danger)]">{error}</div>
        ) : (
          <div className="p-4 sm:p-5">
            <div className="mb-4 flex flex-wrap gap-2">
              {legend.map((item) => (
                <span key={item.tone} className="inline-flex items-center gap-2 rounded-full border border-[var(--glass-border)] bg-white/40 px-3 py-1 text-xs font-semibold text-[var(--text-secondary)] dark:bg-white/5">
                  <span className={`h-2.5 w-2.5 rounded-full ${calendarDotClass[item.tone]}`} />
                  {item.label}
                </span>
              ))}
            </div>
            <div className="grid grid-cols-7 gap-1.5 text-center text-[0.7rem] font-black uppercase text-[var(--text-muted)] sm:gap-2">
              {WEEKDAYS.map((day) => <div key={day}>{day}</div>)}
            </div>
            <div className="mt-2 grid grid-cols-7 gap-1.5 sm:gap-2">
              {calendarDays(month).map((cell) => {
                if (!cell.dateKey) return <div key={cell.key} className="min-h-[62px] rounded-xl sm:min-h-[82px]" />;
                const day = daysByDate.get(cell.dateKey);
                const tone = toneOfDay(day);
                const selected = selectedDate === cell.dateKey;
                return (
                  <button
                    key={cell.key}
                    type="button"
                    title={`${date(cell.dateKey)} - ${day?.status ?? "Chưa có dữ liệu"}`}
                    onClick={() => setSelectedDate(cell.dateKey)}
                    className={`min-h-[62px] rounded-xl border p-1.5 text-left transition hover:-translate-y-0.5 hover:shadow-md focus:outline-none focus:ring-2 focus:ring-[var(--accent)] sm:min-h-[82px] sm:p-2 ${calendarToneClass[tone]} ${selected ? "ring-2 ring-[var(--accent)] ring-offset-2 ring-offset-transparent" : ""}`}
                  >
                    <span className="block text-sm font-black leading-none text-[var(--text)]">{cell.day}</span>
                    <span className={`mt-1.5 block h-1.5 w-1.5 rounded-full ${calendarDotClass[tone]}`} />
                    <span className="mt-1 hidden truncate text-[0.65rem] font-bold sm:block">{toneLabel(day)}</span>
                  </button>
                );
              })}
            </div>
            {loading && !data && <div className="mt-4 rounded-xl bg-[var(--accent-soft)] p-3 text-sm font-semibold text-[var(--accent)]">Đang tải bảng công...</div>}
            {!loading && !data && emptyHint && <div className="mt-4 rounded-xl border border-dashed border-[var(--glass-border)] p-4 text-sm font-semibold text-[var(--text-muted)]">{emptyHint}</div>}
          </div>
        )}
      </GlassPanel>

      {selectedDate && (
        <GlassPanel strong className="mt-4 rounded-[20px] p-5">
          <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
            <div>
              <h2 className="text-lg font-black text-[var(--text)]">Chi tiết ngày {date(selectedDate)}</h2>
              <p className="mt-1 text-sm text-[var(--text-muted)]">{selectedDay?.shiftName || "Chưa có ca làm cho ngày này"}</p>
            </div>
            {selectedDay ? <Badge color={timesheetStatusColor(selectedDay.status)}>{selectedDay.status}</Badge> : <Badge color="muted">Chưa có dữ liệu</Badge>}
          </div>
          {selectedDay ? (
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              <DetailItem label="Giờ vào" value={selectedDay.checkIn ?? "—"} />
              <DetailItem label="Giờ ra" value={selectedDay.checkOut ?? "—"} />
              <DetailItem label="Giờ làm" value={`${selectedDay.workedHours || 0} giờ`} />
              <DetailItem label="Tăng ca" value={fmtMinutes(selectedDay.overtimeMinutes)} tone={selectedDay.overtimeMinutes ? "text-violet-600 dark:text-violet-300" : undefined} />
              <DetailItem label="Đi muộn" value={fmtMinutes(selectedDay.lateMinutes)} tone={selectedDay.lateMinutes ? "text-amber-600 dark:text-amber-300" : undefined} />
              <DetailItem label="Về sớm" value={fmtMinutes(selectedDay.earlyMinutes)} tone={selectedDay.earlyMinutes ? "text-orange-600 dark:text-orange-300" : undefined} />
              <DetailItem label="Phân loại" value={toneLabel(selectedDay)} />
              {selectedDay.holidayType && <DetailItem label="Ngày nghỉ" value={selectedDay.holidayName || toneLabel(selectedDay)} />}
              <DetailItem label="Ca làm" value={selectedDay.shiftName || "—"} />
            </div>
          ) : (
            <div className="rounded-xl border border-dashed border-[var(--glass-border)] p-5 text-sm font-semibold text-[var(--text-muted)]">
              Chưa có dữ liệu chấm công cho ngày này.
            </div>
          )}
        </GlassPanel>
      )}
    </>
  );
}
