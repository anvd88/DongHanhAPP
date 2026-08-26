import { useMemo, useState } from "react";
import { ExportExcelButton, type ProgressReport } from "../components/ExportExcelButton";
import { PageHeader } from "../components/Layout";
import { MonthPicker } from "../components/DateField";
import { GlassPanel } from "../components/glass/GlassPanel";
import { TimesheetCalendar } from "../components/hr/TimesheetCalendar";
import { EmployeePicker } from "../components/hr/EmployeePicker";
import { api } from "../lib/api";
import { useApi } from "../lib/useApi";
import { useAppNotifications } from "../components/app-notifications-context";
import type { EmployeeCard, Timesheet } from "../lib/hr";

function currentMonth() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
}

export function QuanLyBangCong() {
  const [month, setMonth] = useState(currentMonth());
  const [empId, setEmpId] = useState<string>("");
  const { notify } = useAppNotifications();

  const { data: employees } = useApi<EmployeeCard[]>("/api/hr/employees");
  const { data, loading, error, reload } = useApi<Timesheet>(
    empId ? `/api/timesheet/employee/${empId}?month=${month}` : null,
    [empId, month],
  );

  // Tự chọn nhân viên đầu tiên khi có danh sách. Gán lúc render thay vì trong useEffect: effect vẽ
  // thừa một khung hình "chưa chọn ai" rồi mới nhảy, và React Compiler cấm setState đồng bộ trong
  // effect. Điều kiện tự chặn — chọn xong thì `empId` có giá trị nên không vào lại nhánh này.
  if (!empId && employees && employees.length > 0) setEmpId(employees[0].id);

  const selected = useMemo(() => employees?.find((e) => e.id === empId), [employees, empId]);

  async function exportExcel(report: ProgressReport) {
    try {
      // Tiến trình = số byte thật đã tải về. Máy chủ dựng xong workbook mới gửi byte đầu tiên, nên
      // quãng chờ máy chủ thanh vẫn ở chế độ "chưa đo được" chứ không chạy khống.
      const blob = await api.getBlob(`/api/payroll/export?month=${month}`, report);
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `BangCong_PhieuLuong_${month}.xlsx`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
      notify.success("Đã xuất file Excel bảng công + phiếu lương.", "Xuất Excel");
    } catch {
      notify.error("Không xuất được file Excel. Vui lòng thử lại.", "Xuất Excel");
      // false = nút bỏ qua trạng thái "Đã xuất" và quay về ngay.
      return false;
    }
  }

  return (
    <div className="gc-root">
      <PageHeader
        title="Quản lý bảng công"
        subtitle="Xem bảng công theo lịch tháng của từng nhân viên và xuất Excel toàn công ty"
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <MonthPicker
              value={month}
              onChange={(next) => setMonth(next || currentMonth())}
              clearable={false}
              ariaLabel="Chọn tháng bảng công"
            />
            <ExportExcelButton
              onExport={exportExcel}
              className="inline-flex items-center gap-2 rounded-xl bg-[var(--accent)] px-4 py-2 text-sm font-semibold text-white shadow-sm transition hover:opacity-90 disabled:opacity-60"
            />
          </div>
        }
      />

      <GlassPanel strong className="mb-4 rounded-2xl p-4">
        <label className="mb-2 block text-xs font-semibold text-[var(--text-muted)]">Chọn nhân viên</label>
        {employees && employees.length > 0 ? (
          <EmployeePicker employees={employees} value={empId} onChange={setEmpId} />
        ) : (
          <div className="rounded-xl border border-[var(--glass-border)] bg-white/55 px-3 py-2 text-sm text-[var(--text-muted)] dark:bg-white/5">
            Đang tải danh sách nhân viên…
          </div>
        )}
        {selected && (
          <p className="mt-2 text-xs text-[var(--text-muted)]">
            Đang xem bảng công của <span className="font-semibold text-[var(--text)]">{selected.fullName}</span>
            {selected.position ? ` — ${selected.position}` : ""}.
          </p>
        )}
      </GlassPanel>

      <TimesheetCalendar
        month={month}
        data={data ?? null}
        loading={loading}
        error={error}
        onReload={reload}
        emptyHint={empId ? "Chưa có dữ liệu bảng công cho nhân viên này trong tháng đã chọn." : "Hãy chọn một nhân viên để xem bảng công."}
      />
    </div>
  );
}
