import { useState } from "react";
import { CalendarDays, ChevronLeft, ChevronRight } from "lucide-react";
import * as Popover from "@radix-ui/react-popover";
import { DayPicker } from "react-day-picker";
import { vi } from "react-day-picker/locale";
import "react-day-picker/style.css";
import "./date-field.css";

const pad = (n: number) => String(n).padStart(2, "0");
const WEEKDAYS = ["CN", "T2", "T3", "T4", "T5", "T6", "T7"];

const dayFormatters = {
  formatWeekdayName: (date: Date) => WEEKDAYS[date.getDay()],
  formatCaption: (date: Date) => `Tháng ${date.getMonth() + 1}, ${date.getFullYear()}`,
};

function parseDate(value: string): Date | undefined {
  if (!value) return undefined;
  const [y, m, d] = value.split("-").map(Number);
  if (!y || !m || !d) return undefined;
  const dt = new Date(y, m - 1, d);
  return Number.isNaN(dt.getTime()) ? undefined : dt;
}

const formatDateValue = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;

/** Ô chọn ngày dạng popover lịch, giữ nguyên hợp đồng value/onChange chuỗi "YYYY-MM-DD". */
export function DatePicker({
  value,
  onChange,
  placeholder = "Chọn ngày",
  className = "",
  clearable = false,
  ariaLabel,
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  className?: string;
  clearable?: boolean;
  ariaLabel?: string;
}) {
  const [open, setOpen] = useState(false);
  const selected = parseDate(value);
  const label = selected
    ? selected.toLocaleDateString("vi-VN", { weekday: "short", day: "2-digit", month: "2-digit", year: "numeric" })
    : placeholder;

  return (
    <Popover.Root open={open} onOpenChange={setOpen}>
      <Popover.Trigger asChild>
        <button type="button" aria-label={ariaLabel ?? placeholder} className={`gc-datefield ${className}`}>
          <CalendarDays className="gc-datefield-icon" aria-hidden="true" />
          <span className={`gc-datefield-value ${selected ? "" : "is-placeholder"}`}>{label}</span>
        </button>
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content
          className="gc-root gc-datepop gc-panel gc-panel--strong gc-menu-content"
          sideOffset={8}
          align="start"
        >
          <DayPicker
            mode="single"
            locale={vi}
            weekStartsOn={1}
            showOutsideDays
            selected={selected}
            defaultMonth={selected}
            formatters={dayFormatters}
            onSelect={(d) => {
              if (d) {
                onChange(formatDateValue(d));
                setOpen(false);
              }
            }}
          />
          <div className="gc-datepop-foot">
            <button
              type="button"
              className="gc-datepop-quick"
              onClick={() => {
                onChange(formatDateValue(new Date()));
                setOpen(false);
              }}
            >
              Hôm nay
            </button>
            {clearable && value && (
              <button
                type="button"
                className="gc-datepop-clear"
                onClick={() => {
                  onChange("");
                  setOpen(false);
                }}
              >
                Xóa
              </button>
            )}
          </div>
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );
}

/** Ô chọn tháng dạng popover lưới 12 tháng, giữ hợp đồng value/onChange chuỗi "YYYY-MM". */
export function MonthPicker({
  value,
  onChange,
  placeholder = "Tất cả các tháng",
  className = "",
  clearable = true,
  ariaLabel,
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  className?: string;
  clearable?: boolean;
  ariaLabel?: string;
}) {
  const now = new Date();
  const selectedYear = value ? Number(value.slice(0, 4)) : undefined;
  const selectedMonth = value ? Number(value.slice(5, 7)) : undefined;
  const [open, setOpen] = useState(false);
  const [viewYear, setViewYear] = useState(selectedYear ?? now.getFullYear());
  const label = value ? `Tháng ${selectedMonth}/${selectedYear}` : placeholder;

  return (
    <Popover.Root
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (next) setViewYear(selectedYear ?? now.getFullYear());
      }}
    >
      <Popover.Trigger asChild>
        <button type="button" aria-label={ariaLabel ?? placeholder} className={`gc-datefield ${className}`}>
          <CalendarDays className="gc-datefield-icon" aria-hidden="true" />
          <span className={`gc-datefield-value ${value ? "" : "is-placeholder"}`}>{label}</span>
        </button>
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content
          className="gc-root gc-monthpop gc-panel gc-panel--strong gc-menu-content"
          sideOffset={8}
          align="start"
        >
          <div className="gc-monthpop-head">
            <button type="button" className="gc-monthpop-nav" aria-label="Năm trước" onClick={() => setViewYear((y) => y - 1)}>
              <ChevronLeft className="h-4 w-4" />
            </button>
            <span className="gc-monthpop-year">{viewYear}</span>
            <button type="button" className="gc-monthpop-nav" aria-label="Năm sau" onClick={() => setViewYear((y) => y + 1)}>
              <ChevronRight className="h-4 w-4" />
            </button>
          </div>
          <div className="gc-monthpop-grid">
            {Array.from({ length: 12 }).map((_, i) => {
              const m = i + 1;
              const isSelected = selectedYear === viewYear && selectedMonth === m;
              const isCurrent = now.getFullYear() === viewYear && now.getMonth() + 1 === m;
              return (
                <button
                  key={m}
                  type="button"
                  className={`gc-monthpop-cell ${isSelected ? "is-selected" : ""} ${isCurrent ? "is-today" : ""}`}
                  onClick={() => {
                    onChange(`${viewYear}-${pad(m)}`);
                    setOpen(false);
                  }}
                >
                  Tháng {m}
                </button>
              );
            })}
          </div>
          <div className="gc-datepop-foot">
            <button
              type="button"
              className="gc-datepop-quick"
              onClick={() => {
                onChange(`${now.getFullYear()}-${pad(now.getMonth() + 1)}`);
                setOpen(false);
              }}
            >
              Tháng này
            </button>
            {clearable && value && (
              <button
                type="button"
                className="gc-datepop-clear"
                onClick={() => {
                  onChange("");
                  setOpen(false);
                }}
              >
                Tất cả
              </button>
            )}
          </div>
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );
}
