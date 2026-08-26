import { useEffect, useMemo, useRef, useState } from "react";
import { Check, ChevronDown, Search } from "lucide-react";
import type { EmployeeCard } from "../../lib/hr";

/** Bỏ dấu tiếng Việt + về chữ thường để tìm kiếm gõ "nguyen" vẫn khớp "Nguyễn". */
function normalizeVietnamese(s: string): string {
  return (s || "")
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .replace(/đ/gi, "d")
    .toLowerCase()
    .trim();
}

/**
 * Ô vừa gõ tìm vừa chọn nhân viên (kiểu Data Validation List của Excel): gõ một phần tên — có dấu
 * hoặc không dấu — để lọc, bấm/Enter để chọn. Dùng chung cho màn bảng công và màn lập phiếu lương.
 */
export function EmployeePicker({
  employees,
  value,
  onChange,
  placeholder = "Gõ tên nhân viên để tìm…",
  allowClear = false,
  clearLabel = "-- Chưa chọn nhân viên --",
}: {
  employees: EmployeeCard[];
  value: string;
  onChange: (id: string) => void;
  placeholder?: string;
  /** Cho phép bỏ chọn (màn lập phiếu lương bắt đầu ở trạng thái chưa chọn ai). */
  allowClear?: boolean;
  clearLabel?: string;
}) {
  const selected = useMemo(() => employees.find((e) => e.id === value), [employees, value]);
  // Ô nhập khởi tạo THẲNG bằng tên người đang chọn — nếu để effect gán sau lần render đầu thì có
  // một khung hình ô trống.
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

  const q = normalizeVietnamese(query);
  const selectedName = selected ? normalizeVietnamese(selected.fullName) : "";
  const filtered = useMemo(() => {
    // Tìm theo TÊN nhân viên (và mã nhân viên), không theo phòng ban / chức vụ.
    if (!q || q === selectedName) return employees;
    return employees.filter(
      (e) => normalizeVietnamese(e.fullName).includes(q) || normalizeVietnamese(e.employeeCode).includes(q),
    );
  }, [employees, q, selectedName]);

  function pick(emp: EmployeeCard) {
    onChange(emp.id);
    setQuery(emp.fullName);
    setOpen(false);
  }

  function clear() {
    onChange("");
    setQuery("");
    setOpen(false);
  }

  return (
    <div ref={boxRef} className="relative">
      <div className="km-form-control flex w-full items-center gap-2 rounded-xl border px-3 py-2 transition-all focus-within:border-[var(--accent)] focus-within:ring-2 focus-within:ring-[var(--accent-soft)]">
        <Search className="h-4 w-4 shrink-0 text-[var(--accent)]" />
        <input
          value={query}
          placeholder={placeholder}
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
          className="w-full min-w-0 bg-transparent text-sm text-[var(--text)] outline-none"
        />
        <ChevronDown
          className={`h-4 w-4 shrink-0 cursor-pointer text-[var(--text-muted)] transition ${open ? "rotate-180" : ""}`}
          onClick={() => setOpen((o) => !o)}
        />
      </div>

      {open && (
        <div className="absolute z-30 mt-1 max-h-72 w-full overflow-auto rounded-xl border border-[#d2ddec] bg-white p-1 shadow-2xl ring-1 ring-black/5 dark:border-white/12 dark:bg-[#182437] dark:ring-white/10">
          {allowClear && (
            <button
              type="button"
              onClick={clear}
              className="flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-sm text-[var(--text-muted)] transition hover:bg-[var(--accent-soft)]"
            >
              {clearLabel}
            </button>
          )}
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
