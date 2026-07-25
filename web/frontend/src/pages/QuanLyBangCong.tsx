import { useEffect, useMemo, useRef, useState } from "react";
import { CalendarClock, Check, ChevronDown, Download, Loader2, Search } from "lucide-react";
import { PageHeader } from "../components/Layout";
import { GlassPanel } from "../components/glass/GlassPanel";
import { TimesheetCalendar } from "../components/hr/TimesheetCalendar";
import { api } from "../lib/api";
import { useApi } from "../lib/useApi";
import { useAppNotifications } from "../components/app-notifications-context";
import type { EmployeeCard, Timesheet } from "../lib/hr";

function currentMonth() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
}

/** Bỏ dấu tiếng Việt + về chữ thường để tìm kiếm gõ "nguyen" vẫn khớp "Nguyễn". */
function normalize(s: string): string {
  return (s || "")
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .replace(/đ/gi, "d")
    .toLowerCase()
    .trim();
}

/** Ô vừa gõ tìm vừa chọn nhân viên. */
function EmployeePicker({
  employees,
  value,
  onChange,
}: {
  employees: EmployeeCard[];
  value: string;
  onChange: (id: string) => void;
}) {
  const selected = useMemo(() => employees.find((e) => e.id === value), [employees, value]);
  // Ô nhập khởi tạo THẲNG bằng tên người đang chọn — trước đây việc này do effect làm sau khi
  // render lần đầu, nên có một khung hình ô trống.
  const [query, setQuery] = useState(selected ? selected.fullName : "");
  const [open, setOpen] = useState(false);
  const [highlight, setHighlight] = useState(0);
  const boxRef = useRef<HTMLDivElement>(null);

  // Đồng bộ ô nhập với nhân viên đang chọn khi không mở dropdown. Gán lúc render thay vì trong
  // useEffect (React Compiler cấm setState đồng bộ trong effect). Mốc `pickerSeed` giữ đúng bộ deps
  // cũ [selected, open]: chỉ ghi đè ô nhập khi một trong hai ĐỔI, nên không xoá chữ đang gõ dở.
  const [pickerSeed, setPickerSeed] = useState<{ selected: EmployeeCard | undefined; open: boolean }>({
    selected,
    open: false,
  });
  if (pickerSeed.selected !== selected || pickerSeed.open !== open) {
    setPickerSeed({ selected, open });
    if (!open) setQuery(selected ? selected.fullName : "");
  }

  // Đóng khi bấm ra ngoài.
  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (boxRef.current && !boxRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
  }, [open]);

  const q = normalize(query);
  const selectedName = selected ? normalize(selected.fullName) : "";
  const filtered = useMemo(() => {
    // Chỉ tìm theo TÊN nhân viên (không theo phòng ban / chức vụ).
    if (!q || q === selectedName) return employees;
    return employees.filter((e) => normalize(e.fullName).includes(q));
  }, [employees, q, selectedName]);

  function pick(emp: EmployeeCard) {
    onChange(emp.id);
    setQuery(emp.fullName);
    setOpen(false);
  }

  return (
    <div ref={boxRef} className="relative">
      <div className="flex items-center gap-2 rounded-xl border border-[#d3ddec] bg-white px-3 py-2 dark:border-white/14 dark:bg-[#141d2c]">
        <Search className="h-4 w-4 shrink-0 text-[var(--accent)]" />
        <input
          value={query}
          placeholder="Gõ tên nhân viên để tìm…"
          onFocus={(e) => {
            setOpen(true);
            setHighlight(0);
            e.currentTarget.select();
          }}
          onChange={(e) => {
            setQuery(e.target.value);
            setOpen(true);
            setHighlight(0);
          }}
          onKeyDown={(e) => {
            if (e.key === "ArrowDown") {
              e.preventDefault();
              setOpen(true);
              setHighlight((h) => Math.min(h + 1, filtered.length - 1));
            } else if (e.key === "ArrowUp") {
              e.preventDefault();
              setHighlight((h) => Math.max(h - 1, 0));
            } else if (e.key === "Enter") {
              e.preventDefault();
              if (open && filtered[highlight]) pick(filtered[highlight]);
            } else if (e.key === "Escape") {
              setOpen(false);
            }
          }}
          className="w-full bg-transparent text-sm text-[var(--text)] outline-none"
        />
        <ChevronDown
          className={`h-4 w-4 shrink-0 cursor-pointer text-[var(--text-muted)] transition ${open ? "rotate-180" : ""}`}
          onClick={() => setOpen((o) => !o)}
        />
      </div>

      {open && (
        <div className="absolute z-30 mt-1 max-h-72 w-full overflow-auto rounded-xl border border-[#d3ddec] bg-white p-1 shadow-2xl ring-1 ring-black/5 dark:border-white/12 dark:bg-[#161f2e] dark:ring-white/10">
          {filtered.length === 0 ? (
            <div className="px-3 py-3 text-sm text-[var(--text-muted)]">Không tìm thấy nhân viên phù hợp.</div>
          ) : (
            filtered.map((e, i) => {
              const active = e.id === value;
              return (
                <button
                  key={e.id}
                  type="button"
                  onMouseEnter={() => setHighlight(i)}
                  onClick={() => pick(e)}
                  className={`flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-sm transition ${
                    i === highlight ? "bg-[var(--accent-soft)]" : ""
                  }`}
                >
                  <span className="min-w-0 flex-1">
                    <span className="block truncate font-semibold text-[var(--text)]">{e.fullName}</span>
                    <span className="block truncate text-xs text-[var(--text-muted)]">
                      {[e.employeeCode, e.departmentName, e.position].filter(Boolean).join(" · ")}
                    </span>
                  </span>
                  {active && <Check className="h-4 w-4 shrink-0 text-[var(--accent)]" />}
                </button>
              );
            })
          )}
        </div>
      )}
    </div>
  );
}

export function QuanLyBangCong() {
  const [month, setMonth] = useState(currentMonth());
  const [empId, setEmpId] = useState<string>("");
  const [exporting, setExporting] = useState(false);
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

  async function exportExcel() {
    setExporting(true);
    try {
      const blob = await api.getBlob(`/api/payroll/export?month=${month}`);
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
    } finally {
      setExporting(false);
    }
  }

  return (
    <div className="gc-root">
      <PageHeader
        title="Quản lý bảng công"
        subtitle="Xem bảng công theo lịch tháng của từng nhân viên và xuất Excel toàn công ty"
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <label className="flex items-center gap-2 rounded-xl border border-[var(--glass-border)] bg-white/55 px-3 py-2 text-sm dark:bg-white/5">
              <CalendarClock className="h-4 w-4 text-[var(--accent)]" />
              <input
                type="month"
                value={month}
                onChange={(e) => setMonth(e.target.value)}
                className="bg-transparent text-sm text-[var(--text)] outline-none"
              />
            </label>
            <button
              type="button"
              onClick={exportExcel}
              disabled={exporting}
              className="inline-flex items-center gap-2 rounded-xl bg-[var(--accent)] px-4 py-2 text-sm font-semibold text-white shadow-sm transition hover:opacity-90 disabled:opacity-60"
            >
              {exporting ? <Loader2 className="h-4 w-4 animate-spin" /> : <Download className="h-4 w-4" />}
              Xuất Excel
            </button>
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
