import { useState } from "react";
import { CalendarDays, ChevronLeft, ChevronRight } from "lucide-react";
import * as Popover from "@radix-ui/react-popover";
import { DayButton, DayPicker, type DateRange } from "react-day-picker";
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
const formatShortDate = (d?: Date) =>
  d ? `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()}` : "Chưa chọn";
const addDays = (date: Date, days: number) => {
  const next = new Date(date);
  next.setDate(next.getDate() + days);
  return next;
};
const inclusiveDayCount = (from: Date, to: Date) =>
  Math.floor((new Date(to.getFullYear(), to.getMonth(), to.getDate()).getTime()
    - new Date(from.getFullYear(), from.getMonth(), from.getDate()).getTime()) / 86_400_000) + 1;
const calendarDayTime = (date: Date) =>
  new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime();
const isSameCalendarDay = (left: Date, right?: Date) =>
  !!right && calendarDayTime(left) === calendarDayTime(right);

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

/** Bộ chọn khoảng ngày hai lịch, dùng cho báo cáo và giới hạn số ngày theo nghiệp vụ. */
export function DateRangePicker({
  from,
  to,
  onChange,
  maxDays = 60,
  className = "",
  ariaLabel = "Chọn khoảng ngày",
}: {
  from: string;
  to: string;
  onChange: (from: string, to: string) => void;
  maxDays?: number;
  className?: string;
  ariaLabel?: string;
}) {
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState<DateRange | undefined>({
    from: parseDate(from),
    to: parseDate(to),
  });
  const [selectingRangeEnd, setSelectingRangeEnd] = useState(false);
  const [hoveredDay, setHoveredDay] = useState<Date>();
  const [error, setError] = useState("");
  const selectedFrom = parseDate(from);
  const selectedTo = parseDate(to);
  const isChoosingRangeEnd = selectingRangeEnd && !!draft?.from;
  const hoverDayCount = draft?.from && !draft.to && hoveredDay
    ? Math.abs(inclusiveDayCount(draft.from, hoveredDay) - 1) + 1
    : 0;
  const isHoverRangeValid = hoverDayCount > 0 && hoverDayCount <= maxDays;
  const hoverRangeStart = draft?.from && hoveredDay
    ? (calendarDayTime(draft.from) <= calendarDayTime(hoveredDay) ? draft.from : hoveredDay)
    : undefined;
  const hoverRangeEnd = draft?.from && hoveredDay
    ? (calendarDayTime(draft.from) <= calendarDayTime(hoveredDay) ? hoveredDay : draft.from)
    : undefined;
  const hoverLabel = hoverDayCount > maxDays
    ? `${hoverDayCount} ngày · vượt giới hạn`
    : `${hoverDayCount} ngày`;
  const label = selectedFrom && selectedTo
    ? `${formatShortDate(selectedFrom)} — ${formatShortDate(selectedTo)}`
    : "Chọn từ ngày đến ngày";

  const chooseQuickRange = (days: number) => {
    const rangeTo = new Date();
    const rangeFrom = addDays(rangeTo, -(days - 1));
    setDraft({ from: rangeFrom, to: rangeTo });
    setSelectingRangeEnd(false);
    setHoveredDay(undefined);
    setError("");
  };

  return (
    <Popover.Root
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (next) {
          setDraft({ from: parseDate(from), to: parseDate(to) });
          setSelectingRangeEnd(false);
          setHoveredDay(undefined);
          setError("");
        }
      }}
    >
      <Popover.Trigger asChild>
        <button
          type="button"
          aria-label={ariaLabel}
          className={`gc-datefield gc-daterange-trigger ${className}`}
        >
          <CalendarDays className="gc-datefield-icon" aria-hidden="true" />
          <span className="gc-daterange-trigger-copy">
            <span className="gc-daterange-trigger-label">Từ ngày – đến ngày</span>
            <span className="gc-datefield-value">{label}</span>
          </span>
        </button>
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content
          className="gc-root gc-datepop gc-rangepop gc-panel gc-panel--strong gc-menu-content"
          sideOffset={8}
          align="end"
          collisionPadding={12}
        >
          <div className="gc-rangepop-heading">
            <div>
              <div className="gc-rangepop-title">Chọn khoảng thời gian</div>
              <div className="gc-rangepop-subtitle">Bấm ngày bắt đầu, sau đó bấm ngày kết thúc.</div>
            </div>
            <span className="gc-rangepop-limit">Tối đa {maxDays} ngày</span>
          </div>

          <div className="gc-rangepop-values">
            <div className="gc-rangepop-value">
              <span>Từ ngày</span>
              <strong>{formatShortDate(draft?.from)}</strong>
            </div>
            <div className="gc-rangepop-value">
              <span>Đến ngày</span>
              <strong>{formatShortDate(draft?.to)}</strong>
            </div>
          </div>

          <DayPicker
            mode="range"
            locale={vi}
            weekStartsOn={1}
            showOutsideDays
            numberOfMonths={2}
            pagedNavigation
            captionLayout="dropdown"
            startMonth={new Date(2020, 0)}
            endMonth={new Date(new Date().getFullYear() + 5, 11)}
            selected={draft}
            defaultMonth={draft?.from}
            max={Math.max(0, maxDays - 1)}
            formatters={dayFormatters}
            modifiers={{
              hover_range_start: isHoverRangeValid && hoverRangeStart ? hoverRangeStart : false,
              hover_range_middle: isHoverRangeValid && hoverRangeStart && hoverRangeEnd
                ? { after: hoverRangeStart, before: hoverRangeEnd }
                : false,
              hover_range_end: isHoverRangeValid && hoverRangeEnd ? hoverRangeEnd : false,
              hover_range_invalid: hoverDayCount > maxDays && hoveredDay ? hoveredDay : false,
            }}
            modifiersClassNames={{
              hover_range_start: "gc-range-preview-start",
              hover_range_middle: "gc-range-preview-middle",
              hover_range_end: "gc-range-preview-end",
              hover_range_invalid: "gc-range-preview-invalid",
            }}
            components={{
              DayButton: (props) => (
                <DayButton
                  {...props}
                  data-range-preview-label={
                    isChoosingRangeEnd && isSameCalendarDay(props.day.date, hoveredDay)
                      ? hoverLabel
                      : undefined
                  }
                />
              ),
            }}
            onDayMouseEnter={(day) => {
              if (isChoosingRangeEnd) setHoveredDay(day);
            }}
            onDayMouseLeave={(day) => {
              if (isSameCalendarDay(day, hoveredDay)) setHoveredDay(undefined);
            }}
            onSelect={(_next, day) => {
              setHoveredDay(undefined);
              if (!selectingRangeEnd || !draft?.from) {
                setDraft({ from: day });
                setSelectingRangeEnd(true);
                setError("");
                return;
              }

              const rangeFrom = calendarDayTime(draft.from) <= calendarDayTime(day) ? draft.from : day;
              const rangeTo = calendarDayTime(draft.from) <= calendarDayTime(day) ? day : draft.from;
              if (inclusiveDayCount(rangeFrom, rangeTo) > maxDays) {
                setError(`Chỉ được chọn tối đa ${maxDays} ngày.`);
                return;
              }

              setDraft({ from: rangeFrom, to: rangeTo });
              setSelectingRangeEnd(false);
              setError("");
            }}
          />

          <div className="gc-rangepop-note">
            {error || `Khoảng báo cáo được giới hạn trong ${maxDays} ngày để dữ liệu tải nhanh và dễ kiểm tra.`}
          </div>

          <div className="gc-rangepop-actions">
            <div className="gc-rangepop-quick-list">
              <button type="button" onClick={() => chooseQuickRange(1)}>Hôm nay</button>
              <button type="button" onClick={() => chooseQuickRange(7)}>7 ngày</button>
              <button type="button" onClick={() => chooseQuickRange(30)}>30 ngày</button>
            </div>
            <div className="gc-rangepop-confirm">
              <button type="button" className="gc-datepop-clear" onClick={() => setOpen(false)}>Đóng</button>
              <button
                type="button"
                className="gc-rangepop-apply"
                disabled={!draft?.from || !draft.to || !!error}
                onClick={() => {
                  if (!draft?.from || !draft.to) return;
                  onChange(formatDateValue(draft.from), formatDateValue(draft.to));
                  setOpen(false);
                }}
              >
                Áp dụng
              </button>
            </div>
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
