import { useState } from "react";
import { CalendarDays, ChevronLeft, ChevronRight, Clock3 } from "lucide-react";
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
  disabled = false,
  min,
  max,
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  className?: string;
  clearable?: boolean;
  ariaLabel?: string;
  /** Khoá ô chọn (vd. hợp đồng không thời hạn thì không có ngày kết thúc). */
  disabled?: boolean;
  /** Chặn chọn ngoài khoảng, dạng "YYYY-MM-DD" — thay cho min/max của input ngày cũ. */
  min?: string;
  max?: string;
}) {
  const [open, setOpen] = useState(false);
  const selected = parseDate(value);
  const minDate = parseDate(min ?? "");
  const maxDate = parseDate(max ?? "");
  // DayPicker nhận "matcher" chứ không nhận min/max chuỗi: dựng đúng những mốc thật sự có.
  const outOfRange = [
    ...(minDate ? [{ before: minDate }] : []),
    ...(maxDate ? [{ after: maxDate }] : []),
  ];
  const label = selected
    ? selected.toLocaleDateString("vi-VN", { weekday: "short", day: "2-digit", month: "2-digit", year: "numeric" })
    : placeholder;

  return (
    <Popover.Root open={open && !disabled} onOpenChange={(next) => { if (!disabled) setOpen(next); }}>
      <Popover.Trigger asChild>
        <button
          type="button"
          disabled={disabled}
          aria-label={ariaLabel ?? placeholder}
          className={`gc-datefield ${className}`}
        >
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
            startMonth={minDate}
            endMonth={maxDate}
            disabled={outOfRange.length ? outOfRange : undefined}
            formatters={dayFormatters}
            onSelect={(d) => {
              if (d) {
                onChange(formatDateValue(d));
                setOpen(false);
              }
            }}
          />
          <div className="gc-datepop-foot">
            {/* Hôm nay nằm ngoài khoảng cho phép thì giấu nút, đừng để bấm vào rồi bị từ chối. */}
            {!(minDate && calendarDayTime(new Date()) < calendarDayTime(minDate))
              && !(maxDate && calendarDayTime(new Date()) > calendarDayTime(maxDate)) && (
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
            )}
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
  disabled = false,
  min,
  max,
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  className?: string;
  clearable?: boolean;
  ariaLabel?: string;
  disabled?: boolean;
  /** Giới hạn kỳ chọn được, dạng "YYYY-MM". */
  min?: string;
  max?: string;
}) {
  const now = new Date();
  const selectedYear = value ? Number(value.slice(0, 4)) : undefined;
  const selectedMonth = value ? Number(value.slice(5, 7)) : undefined;
  const [open, setOpen] = useState(false);
  const [viewYear, setViewYear] = useState(selectedYear ?? now.getFullYear());
  const label = value ? `Tháng ${selectedMonth}/${selectedYear}` : placeholder;

  return (
    <Popover.Root
      open={open && !disabled}
      onOpenChange={(next) => {
        if (disabled) return;
        setOpen(next);
        if (next) setViewYear(selectedYear ?? now.getFullYear());
      }}
    >
      <Popover.Trigger asChild>
        <button
          type="button"
          disabled={disabled}
          aria-label={ariaLabel ?? placeholder}
          className={`gc-datefield ${className}`}
        >
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
              const key = `${viewYear}-${pad(m)}`;
              const isSelected = selectedYear === viewYear && selectedMonth === m;
              const isCurrent = now.getFullYear() === viewYear && now.getMonth() + 1 === m;
              // So sánh chuỗi "YYYY-MM" là đủ: định dạng cố định độ dài nên thứ tự chữ = thứ tự thời gian.
              const blocked = (!!min && key < min) || (!!max && key > max);
              return (
                <button
                  key={m}
                  type="button"
                  disabled={blocked}
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

/* ============================================================================================
 * GIỜ và NGÀY-GIỜ
 *
 * Hai bộ dưới đây khép lại việc "toàn web dùng chung một kiểu chọn thời gian": trước đây ngày và
 * tháng đã có popover riêng, còn giờ/ngày-giờ vẫn là ô mặc định của trình duyệt — mỗi trình duyệt
 * một kiểu, khác hẳn phần còn lại của trang, và trên Firefox thì không có lịch nào cả.
 * Hợp đồng chuỗi giữ nguyên như input gốc: "HH:mm" và "YYYY-MM-DDTHH:mm".
 * ========================================================================================== */

const HOURS = Array.from({ length: 24 }, (_, i) => i);

/** Danh sách phút theo bước nhảy, LUÔN chèn thêm phút đang chọn nếu nó lệch bước. */
function minuteOptions(step: number, current?: number) {
  const safeStep = Math.min(30, Math.max(1, Math.round(step)));
  const list = Array.from({ length: Math.ceil(60 / safeStep) }, (_, i) => i * safeStep).filter((m) => m < 60);
  if (current !== undefined && !list.includes(current)) list.push(current);
  return list.sort((a, b) => a - b);
}

function parseClock(value: string): { hour: number; minute: number } | undefined {
  const match = /^(\d{1,2}):(\d{2})/.exec((value || "").trim());
  if (!match) return undefined;
  const hour = Number(match[1]);
  const minute = Number(match[2]);
  if (hour > 23 || minute > 59) return undefined;
  return { hour, minute };
}

const formatClock = (hour: number, minute: number) => `${pad(hour)}:${pad(minute)}`;

/** Hai cột giờ/phút dùng chung cho TimePicker và DateTimePicker. */
function ClockColumns({
  hour,
  minute,
  minuteStep,
  onPick,
}: {
  hour?: number;
  minute?: number;
  minuteStep: number;
  onPick: (hour: number, minute: number) => void;
}) {
  const minutes = minuteOptions(minuteStep, minute);
  return (
    <div className="gc-clock">
      <div className="gc-clock-col" role="listbox" aria-label="Giờ">
        <div className="gc-clock-head">Giờ</div>
        <div className="gc-clock-scroll">
          {HOURS.map((h) => (
            <button
              key={h}
              type="button"
              role="option"
              aria-selected={h === hour}
              className={`gc-clock-cell ${h === hour ? "is-selected" : ""}`}
              onClick={() => onPick(h, minute ?? 0)}
            >
              {pad(h)}
            </button>
          ))}
        </div>
      </div>
      <div className="gc-clock-col" role="listbox" aria-label="Phút">
        <div className="gc-clock-head">Phút</div>
        <div className="gc-clock-scroll">
          {minutes.map((m) => (
            <button
              key={m}
              type="button"
              role="option"
              aria-selected={m === minute}
              className={`gc-clock-cell ${m === minute ? "is-selected" : ""}`}
              onClick={() => onPick(hour ?? 0, m)}
            >
              {pad(m)}
            </button>
          ))}
        </div>
      </div>
    </div>
  );
}

/** Ô chọn giờ dạng popover, giữ hợp đồng value/onChange chuỗi "HH:mm". */
export function TimePicker({
  value,
  onChange,
  placeholder = "Chọn giờ",
  className = "",
  clearable = false,
  ariaLabel,
  disabled = false,
  minuteStep = 5,
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  className?: string;
  clearable?: boolean;
  ariaLabel?: string;
  disabled?: boolean;
  /** Bước nhảy phút trong danh sách; phút đang chọn luôn được giữ dù lệch bước. */
  minuteStep?: number;
}) {
  const [open, setOpen] = useState(false);
  const clock = parseClock(value);

  return (
    <Popover.Root open={open && !disabled} onOpenChange={(next) => { if (!disabled) setOpen(next); }}>
      <Popover.Trigger asChild>
        <button
          type="button"
          disabled={disabled}
          aria-label={ariaLabel ?? placeholder}
          className={`gc-datefield ${className}`}
        >
          <Clock3 className="gc-datefield-icon" aria-hidden="true" />
          <span className={`gc-datefield-value ${clock ? "" : "is-placeholder"}`}>
            {clock ? formatClock(clock.hour, clock.minute) : placeholder}
          </span>
        </button>
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content
          className="gc-root gc-datepop gc-timepop gc-panel gc-panel--strong gc-menu-content"
          sideOffset={8}
          align="start"
        >
          <ClockColumns
            hour={clock?.hour}
            minute={clock?.minute}
            minuteStep={minuteStep}
            onPick={(h, m) => onChange(formatClock(h, m))}
          />
          <div className="gc-datepop-foot">
            <button
              type="button"
              className="gc-datepop-quick"
              onClick={() => {
                const now = new Date();
                onChange(formatClock(now.getHours(), now.getMinutes()));
                setOpen(false);
              }}
            >
              Bây giờ
            </button>
            {clearable && value && (
              <button type="button" className="gc-datepop-clear" onClick={() => { onChange(""); setOpen(false); }}>
                Xóa
              </button>
            )}
            <button type="button" className="gc-datepop-done" onClick={() => setOpen(false)}>Xong</button>
          </div>
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );
}

/** Ô chọn ngày + giờ, giữ hợp đồng value/onChange chuỗi "YYYY-MM-DDTHH:mm" như input datetime-local. */
export function DateTimePicker({
  value,
  onChange,
  placeholder = "Chọn ngày giờ",
  className = "",
  clearable = false,
  ariaLabel,
  disabled = false,
  minuteStep = 5,
  min,
  max,
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  className?: string;
  clearable?: boolean;
  ariaLabel?: string;
  disabled?: boolean;
  minuteStep?: number;
  /** Giới hạn phần NGÀY, dạng "YYYY-MM-DD". */
  min?: string;
  max?: string;
}) {
  const [open, setOpen] = useState(false);
  const [datePart = "", timePart = ""] = (value || "").split("T");
  const selected = parseDate(datePart);
  const clock = parseClock(timePart);
  const minDate = parseDate(min ?? "");
  const maxDate = parseDate(max ?? "");
  const outOfRange = [
    ...(minDate ? [{ before: minDate }] : []),
    ...(maxDate ? [{ after: maxDate }] : []),
  ];

  // Chọn ngày trước hay giờ trước đều được: phần còn thiếu lấy mặc định của "bây giờ" thay vì bắt
  // người dùng phải bấm đúng thứ tự.
  const commit = (nextDate: string, nextClock: { hour: number; minute: number }) =>
    onChange(`${nextDate}T${formatClock(nextClock.hour, nextClock.minute)}`);

  const label = selected && clock
    ? `${selected.toLocaleDateString("vi-VN", { day: "2-digit", month: "2-digit", year: "numeric" })} · ${formatClock(clock.hour, clock.minute)}`
    : placeholder;

  return (
    <Popover.Root open={open && !disabled} onOpenChange={(next) => { if (!disabled) setOpen(next); }}>
      <Popover.Trigger asChild>
        <button
          type="button"
          disabled={disabled}
          aria-label={ariaLabel ?? placeholder}
          className={`gc-datefield ${className}`}
        >
          <CalendarDays className="gc-datefield-icon" aria-hidden="true" />
          <span className={`gc-datefield-value ${selected && clock ? "" : "is-placeholder"}`}>{label}</span>
        </button>
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content
          className="gc-root gc-datepop gc-datetimepop gc-panel gc-panel--strong gc-menu-content"
          sideOffset={8}
          align="start"
          collisionPadding={12}
        >
          <div className="gc-datetimepop-body">
            <DayPicker
              mode="single"
              locale={vi}
              weekStartsOn={1}
              showOutsideDays
              selected={selected}
              defaultMonth={selected}
              startMonth={minDate}
              endMonth={maxDate}
              disabled={outOfRange.length ? outOfRange : undefined}
              formatters={dayFormatters}
              onSelect={(d) => {
                if (!d) return;
                const now = new Date();
                commit(formatDateValue(d), clock ?? { hour: now.getHours(), minute: now.getMinutes() });
              }}
            />
            <ClockColumns
              hour={clock?.hour}
              minute={clock?.minute}
              minuteStep={minuteStep}
              onPick={(h, m) => commit(datePart || formatDateValue(new Date()), { hour: h, minute: m })}
            />
          </div>
          <div className="gc-datepop-foot">
            <button
              type="button"
              className="gc-datepop-quick"
              onClick={() => {
                const now = new Date();
                commit(formatDateValue(now), { hour: now.getHours(), minute: now.getMinutes() });
                setOpen(false);
              }}
            >
              Bây giờ
            </button>
            {clearable && value && (
              <button type="button" className="gc-datepop-clear" onClick={() => { onChange(""); setOpen(false); }}>
                Xóa
              </button>
            )}
            <button type="button" className="gc-datepop-done" onClick={() => setOpen(false)}>Xong</button>
          </div>
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );
}
