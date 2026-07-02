import { useEffect, useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import {
  Ban,
  Banknote,
  CalendarClock,
  CheckCircle2,
  ChevronRight,
  CloudOff,
  FilePlus2,
  FileText,
  Gavel,
  IdCard,
  Inbox,
  Megaphone,
  Pencil,
  Plus,
  RefreshCw,
  Save,
  ScanFace,
  Send,
  Trash2,
  UserCog,
  Wallet,
  WifiOff,
  X,
  XCircle,
} from "lucide-react";
import { Field, Input, Select } from "../../components/ui";
import { CheckInScanner } from "../../features/chamcong/CheckInScanner";
import { api } from "../../lib/api";
import { useAuth } from "../../lib/auth";
import { date, dateTime, initials, moneyVnd } from "../../lib/format";
import {
  fieldDisplayValue,
  fieldLabel,
  leaveTypeLabel,
  payoutMethodLabel,
  penaltySchedule,
  penaltyStatusLabel,
  penaltyTypeColor,
  refundStatusColor,
  refundStatusLabel,
  requestFields,
  requestStatusLabel,
  timesheetStatusColor,
  type Department,
  type EmployeeCard,
  type EmployeeDetail,
  type PayLine,
  type PayrollCompute,
  type Penalty,
  type PenaltyRefund,
  type PenaltyType,
  type Payslip,
  type RequestDetail,
  type SalaryComponent,
  type SalaryDetail,
  type SalaryListItem,
  type RequestListItem,
  type RequestType,
  type Shift,
  type ShiftAssignment,
  type Timesheet,
  type TimesheetDay,
} from "../../lib/hr";
import {
  getOfflineCount,
  subscribeOfflineCount,
  syncOfflineAttendance,
} from "../../lib/offlineAttendance";
import { useApi } from "../../lib/useApi";
import { useAppNotifications } from "../../components/AppNotifications";
import { isAdmin } from "../../lib/types";
import type { ChamCongLog, FaceNguoiDung, RtspAttendanceStatus } from "../../lib/types";
import "./hr-pages.css";

type Tone = "neutral" | "success" | "warning" | "danger" | "muted";

function currentMonth() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
}

function todayKey() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

function fmtMinutes(m = 0) {
  if (!m) return "0 phút";
  const h = Math.floor(m / 60);
  const mm = m % 60;
  return h > 0 ? `${h}h${mm ? String(mm).padStart(2, "0") : ""}` : `${mm} phút`;
}

/** Cộng thêm i tháng vào kỳ "yyyy-MM"; trả về "MM/yyyy" để hiển thị. */
function addMonths(period: string, i: number) {
  const [y, m] = period.split("-").map(Number);
  if (!y || !m) return period;
  const total = (y * 12 + (m - 1)) + i;
  const ny = Math.floor(total / 12);
  const nm = (total % 12) + 1;
  return `${String(nm).padStart(2, "0")}/${ny}`;
}

function HrPage({
  eyebrow,
  title,
  children,
  action,
  className = "",
}: {
  eyebrow?: string;
  title: string;
  children: ReactNode;
  action?: ReactNode;
  className?: string;
}) {
  return (
    <div className={`hr-page ${className}`.trim()}>
      <div className="hr-page-head">
        <div>
          {eyebrow && <p>{eyebrow}</p>}
          <h1>{title}</h1>
        </div>
        {action}
      </div>
      {children}
    </div>
  );
}

function HrCard({ children, className = "" }: { children: ReactNode; className?: string }) {
  return <section className={`hr-card ${className}`}>{children}</section>;
}

function HrButton({
  children,
  onClick,
  type = "button",
  tone = "primary",
  disabled,
}: {
  children: ReactNode;
  onClick?: () => void;
  type?: "button" | "submit";
  tone?: "primary" | "secondary" | "danger";
  disabled?: boolean;
}) {
  return (
    <button type={type} className="hr-button" data-tone={tone} onClick={onClick} disabled={disabled}>
      {children}
    </button>
  );
}

/**
 * Ô nhập số tiền: hiển thị dấu ngăn cách hàng nghìn (1,000,000) cho dễ đọc, nhưng phát ra chuỗi
 * chỉ gồm chữ số (không dấu) để state/tính toán vẫn dùng Number() như bình thường.
 */
function MoneyInput({ value, onChange, placeholder = "0" }: { value: string | number; onChange: (raw: string) => void; placeholder?: string }) {
  const n = Number(String(value ?? "").replace(/[^\d]/g, "")) || 0;
  const display = n ? n.toLocaleString("en-US") : "";
  return (
    <Input
      type="text"
      inputMode="numeric"
      value={display}
      placeholder={placeholder}
      onChange={(e) => onChange(e.target.value.replace(/[^\d]/g, ""))}
    />
  );
}

function HrStat({ label, value, hint, tone = "neutral" }: { label: string; value: string; hint?: string; tone?: Tone }) {
  return (
    <article className="hr-stat" data-tone={tone}>
      <span>{label}</span>
      <strong>{value}</strong>
      {hint && <small>{hint}</small>}
    </article>
  );
}

function HrStatus({ status, children }: { status: Tone; children: ReactNode }) {
  return (
    <span className="hr-status" data-tone={status}>
      {children}
    </span>
  );
}

function HrEmpty({ text }: { text: string }) {
  return <div className="hr-empty">{text}</div>;
}

function HrModal({
  title,
  children,
  footer,
  onClose,
  wide,
}: {
  title: string;
  children: ReactNode;
  footer?: ReactNode;
  onClose: () => void;
  wide?: boolean;
}) {
  return (
    <div className="hr-modal-layer" onClick={onClose}>
      <div className="hr-modal" data-wide={wide} onClick={(event) => event.stopPropagation()}>
        <header>
          <h2>{title}</h2>
          <button type="button" onClick={onClose} aria-label="Đóng">
            <X className="h-5 w-5" />
          </button>
        </header>
        <div className="hr-modal-body scroll-thin">{children}</div>
        {footer && <footer>{footer}</footer>}
      </div>
    </div>
  );
}

function toneForRequest(status?: string): Tone {
  if (status === "Approved") return "success";
  if (status === "Rejected" || status === "Cancelled") return "danger";
  if (status === "Pending") return "warning";
  return "muted";
}

function toneForTimesheet(day?: TimesheetDay): Tone {
  if (!day) return "muted";
  const color = timesheetStatusColor(day.status);
  if (color === "success") return "success";
  if (color === "danger") return "danger";
  if (color === "warning") return "warning";
  return "muted";
}

type TimesheetCalendarTone = "worked" | "absent" | "overtime" | "warning" | "off" | "empty";
type TimesheetCalendarCell = { key: string; day: number | null; dateKey: string | null };

const timesheetWeekdays = ["T2", "T3", "T4", "T5", "T6", "T7", "CN"];

function timesheetCalendarDays(month: string): TimesheetCalendarCell[] {
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

function timesheetCalendarTone(day?: TimesheetDay): TimesheetCalendarTone {
  if (!day) return "empty";
  if (day.status === "Vắng") return "absent";
  if (day.overtimeMinutes > 0) return "overtime";
  if (day.lateMinutes > 0 || day.earlyMinutes > 0 || day.status.includes("muộn") || day.status.includes("sớm") || day.status.includes("Thiếu")) {
    return "warning";
  }
  if (day.workedHours > 0 || day.checkIn || day.checkOut || timesheetStatusColor(day.status) === "success") return "worked";
  if (day.status === "Không phân ca") return "off";
  return "empty";
}

function timesheetCalendarLabel(day?: TimesheetDay) {
  const tone = timesheetCalendarTone(day);
  if (!day) return "Chưa có dữ liệu";
  if (tone === "worked") return "Đi làm";
  if (tone === "absent") return "Nghỉ / vắng";
  if (tone === "overtime") return "Tăng ca";
  if (tone === "warning") return "Cần rà soát";
  if (tone === "off") return "Không phân ca";
  return day.status || "Chưa có dữ liệu";
}

export function HRHomePage() {
  const { user } = useAuth();
  const admin = isAdmin(user);
  const { data: me } = useApi<EmployeeDetail>("/api/hr/me");
  const { data: timesheet } = useApi<Timesheet>(`/api/timesheet/me?month=${currentMonth()}`);
  const { data: requests } = useApi<RequestListItem[]>("/api/requests?scope=mine");
  const { data: inbox } = useApi<RequestListItem[]>(admin ? "/api/requests?scope=inbox" : null, [admin]);
  const today = timesheet?.days.find((day) => day.date.slice(0, 10) === todayKey());
  const displayName = me?.fullName || user?.fullName || user?.username || "Nhân viên";

  return (
    <HrPage eyebrow="Tổng quan" title="Cổng nhân sự">
      <HrCard className="hr-hero-card">
        <div className="hr-person">
          <span className="hr-person-avatar">{me?.avatar ? <img src={me.avatar} alt="" /> : initials(displayName)}</span>
          <div>
            <h2>{displayName}</h2>
            <p>{me?.position || "Nhân viên"}{me?.departmentName ? ` · ${me.departmentName}` : ""}</p>
            <small>{me?.employeeCode || user?.username}</small>
          </div>
        </div>
        <Link className="hr-scan-cta" to="/chamcong">
          <ScanFace className="h-5 w-5" />
          Chấm công
        </Link>
      </HrCard>

      <div className="hr-grid-3">
        <HrStat label="Hôm nay" value={today?.status || "Chưa chấm"} hint={`Vào ${today?.checkIn || "--:--"} · Ra ${today?.checkOut || "--:--"}`} tone={toneForTimesheet(today)} />
        <HrStat label="Ngày công" value={`${timesheet?.summary.workedDays ?? 0}`} hint={`Vắng ${timesheet?.summary.absentDays ?? 0} ngày`} />
        <HrStat label="Đơn gần nhất" value={requests?.[0] ? requestStatusLabel(requests[0].status) : "Chưa có"} hint={requests?.[0]?.typeLabel || "Tạo đơn khi cần"} tone={toneForRequest(requests?.[0]?.status)} />
      </div>

      <div className="hr-action-list">
        <HomeAction icon={<IdCard />} title="Hồ sơ của tôi" hint="Thông tin cá nhân, hợp đồng, lương" to="/hoso" />
        <HomeAction icon={<CalendarClock />} title="Bảng công" hint="Công, đi muộn, về sớm, tăng ca" to="/bangcong" />
        <HomeAction icon={<FileText />} title="Đơn từ" hint="Nghỉ phép, tăng ca, quên chấm công" to="/dontu" />
        <HomeAction icon={<Inbox />} title="Phê duyệt" hint={admin ? `${inbox?.filter((x) => x.status === "Pending").length ?? 0} đơn chờ xử lý` : "Hộp thư duyệt của bạn"} to="/pheduyet" />
        <HomeAction icon={<Gavel />} title="Phạt / kỷ luật" hint={admin ? "Lập & quản lý quyết định phạt" : "Xem các lần bị phạt"} to="/phat" />
        {admin && <HomeAction icon={<Wallet />} title="Bảng lương" hint="Mức lương & lập phiếu lương" to="/bang-luong" />}
        {admin && <HomeAction icon={<UserCog />} title="Quản lý nhân sự" hint="Nhân viên, phòng ban, ca làm" to="/quanly-nhansu" />}
      </div>
    </HrPage>
  );
}

function HomeAction({ icon, title, hint, to }: { icon: ReactNode; title: string; hint: string; to: string }) {
  return (
    <Link className="hr-home-action" to={to}>
      <span>{icon}</span>
      <div>
        <strong>{title}</strong>
        <small>{hint}</small>
      </div>
      <ChevronRight className="h-4 w-4" />
    </Link>
  );
}

export function HRProfilePage() {
  const { notify } = useAppNotifications();
  const { data: me, loading, reload } = useApi<EmployeeDetail>("/api/hr/me");
  const [tab, setTab] = useState<"info" | "contract" | "salary" | "leave">("info");
  const [saving, setSaving] = useState(false);
  const [form, setForm] = useState<Partial<Pick<EmployeeDetail, "phone" | "email" | "address" | "dob" | "gender">>>({});

  const save = async () => {
    if (!me) return;
    setSaving(true);
    try {
      await api.put(`/api/hr/employees/${me.id}`, {
        fullName: me.fullName,
        phone: form.phone ?? me.phone ?? "",
        email: form.email ?? me.email ?? "",
        address: form.address ?? me.address ?? "",
        dob: form.dob ?? me.dob ?? "",
        gender: form.gender ?? me.gender ?? "",
      });
      notify.success("Đã lưu hồ sơ.");
      reload({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không lưu được hồ sơ.");
    } finally {
      setSaving(false);
    }
  };

  if (loading && !me) return <HrPage title="Hồ sơ"><HrCard><HrEmpty text="Đang tải hồ sơ..." /></HrCard></HrPage>;
  if (!me) return <HrPage title="Hồ sơ"><HrCard><HrEmpty text="Không tìm thấy hồ sơ nhân sự." /></HrCard></HrPage>;

  return (
    <HrPage eyebrow="Cá nhân" title="Hồ sơ của tôi">
      <HrCard className="hr-profile-card">
        <span className="hr-person-avatar is-large">{me.avatar ? <img src={me.avatar} alt="" /> : initials(me.fullName)}</span>
        <div>
          <h2>{me.fullName}</h2>
          <p>{me.position || "Nhân viên"}{me.departmentName ? ` · ${me.departmentName}` : ""}</p>
          <small>{me.employeeCode}</small>
        </div>
      </HrCard>

      <div className="hr-tabs">
        {[
          ["info", "Thông tin"],
          ["contract", "Hợp đồng"],
          ["salary", "Lương"],
          ["leave", "Ngày phép"],
        ].map(([key, label]) => (
          <button key={key} type="button" data-active={tab === key} onClick={() => setTab(key as typeof tab)}>{label}</button>
        ))}
      </div>

      {tab === "info" && (
        <HrCard>
          <div className="hr-form-grid">
            <Read label="Mã nhân viên" value={me.employeeCode} />
            <Read label="Tài khoản" value={me.username || "--"} />
            <Read label="Phòng ban" value={me.departmentName || "--"} />
            <Read label="Quản lý" value={me.managerName || "--"} />
            <Field label="Ngày sinh"><Input type="date" value={form.dob ?? me.dob ?? ""} onChange={(e) => setForm((s) => ({ ...s, dob: e.target.value }))} /></Field>
            <Field label="Giới tính"><Input value={form.gender ?? me.gender ?? ""} onChange={(e) => setForm((s) => ({ ...s, gender: e.target.value }))} /></Field>
            <Field label="Số điện thoại"><Input value={form.phone ?? me.phone ?? ""} onChange={(e) => setForm((s) => ({ ...s, phone: e.target.value }))} /></Field>
            <Field label="Email"><Input value={form.email ?? me.email ?? ""} onChange={(e) => setForm((s) => ({ ...s, email: e.target.value }))} /></Field>
            <div className="hr-field-full"><Field label="Địa chỉ"><Input value={form.address ?? me.address ?? ""} onChange={(e) => setForm((s) => ({ ...s, address: e.target.value }))} /></Field></div>
          </div>
          <div className="hr-card-actions"><HrButton onClick={save} disabled={saving}><Save className="h-4 w-4" /> Lưu hồ sơ</HrButton></div>
        </HrCard>
      )}
      {tab === "contract" && <HRProfileContracts empId={me.id} />}
      {tab === "salary" && <HRProfilePayslips empId={me.id} />}
      {tab === "leave" && <HRProfileLeave empId={me.id} />}
    </HrPage>
  );
}

function Read({ label, value }: { label: string; value: string }) {
  return <div className="hr-read"><span>{label}</span><strong>{value}</strong></div>;
}

function HRProfileContracts({ empId }: { empId: string }) {
  const { data, loading } = useApi<Array<{ id: string; contractNo: string; contractType: string; startDate?: string; endDate?: string; baseSalary: number; status: string }>>(`/api/hr/employees/${empId}/contracts`, [empId]);
  return <HrCard>{loading ? <HrEmpty text="Đang tải hợp đồng..." /> : (data?.length ? data.map((item) => <ListRow key={item.id} title={item.contractNo || item.contractType || "Hợp đồng"} meta={`${item.startDate ? date(item.startDate) : "--"} · ${moneyVnd(item.baseSalary)}`} status={item.status === "Active" ? "Hiệu lực" : item.status} />) : <HrEmpty text="Chưa có hợp đồng." />)}</HrCard>;
}

function HRProfilePayslips({ empId }: { empId: string }) {
  const { data, loading } = useApi<Payslip[]>(`/api/hr/employees/${empId}/payslips`, [empId]);
  const [detail, setDetail] = useState<Payslip | null>(null);
  return (
    <HrCard>
      {loading ? <HrEmpty text="Đang tải phiếu lương..." /> : (data?.length ? data.map((item) => (
        <ListRow key={item.id} title={`Kỳ ${item.period}`} meta={`${item.workDays} ngày công · ${moneyVnd(item.netPay)}`} status={item.published ? "Đã phát hành" : "Nháp"} onClick={() => setDetail(item)} />
      )) : <HrEmpty text="Chưa có phiếu lương." />)}
      {detail && <PayslipDetailModal payslip={detail} onClose={() => setDetail(null)} />}
    </HrCard>
  );
}

export function PayslipDetailModal({ payslip, onClose }: { payslip: Payslip; onClose: () => void }) {
  const d = payslip.details;
  const earnings = d?.earnings ?? [
    { label: "Lương cơ bản", amount: payslip.baseSalary },
    ...(payslip.allowance ? [{ label: "Phụ cấp", amount: payslip.allowance }] : []),
    ...(payslip.overtimePay ? [{ label: "Tăng ca", amount: payslip.overtimePay }] : []),
  ];
  const deductions = d?.deductions ?? (payslip.deductions ? [{ label: "Khấu trừ", amount: payslip.deductions }] : []);
  const totalEarnings = d?.totalEarnings ?? earnings.reduce((s, e) => s + e.amount, 0);
  const totalDeductions = d?.totalDeductions ?? deductions.reduce((s, e) => s + e.amount, 0);
  return (
    <HrModal title={`Phiếu lương · Kỳ ${payslip.period}`} onClose={onClose} footer={<HrButton tone="secondary" onClick={onClose}>Đóng</HrButton>}>
      <div className="hr-payslip-lines">
        <div className="hr-payslip-group">
          <h3>Khoản cộng</h3>
          {earnings.map((e, i) => <PaylineRow key={`e${i}`} label={e.label} amount={e.amount} />)}
          <PaylineRow label="Tổng thu nhập" amount={totalEarnings} total />
        </div>
        <div className="hr-payslip-group">
          <h3>Khoản trừ</h3>
          {deductions.length === 0 && <p className="hr-salary-empty">Không có khoản trừ.</p>}
          {deductions.map((e, i) => <PaylineRow key={`d${i}`} label={e.label} amount={e.amount} minus />)}
          <PaylineRow label="Tổng khấu trừ" amount={totalDeductions} total minus />
        </div>
      </div>
      <div className="hr-payslip-net">
        <span>Thực nhận</span>
        <strong>{moneyVnd(payslip.netPay)}</strong>
      </div>
    </HrModal>
  );
}

function HRProfileLeave({ empId }: { empId: string }) {
  const { data, loading } = useApi<Array<{ id: string; year: number; leaveType: string; totalDays: number; usedDays: number; remainingDays: number }>>(`/api/hr/employees/${empId}/leave-balances`, [empId]);
  return <HrCard>{loading ? <HrEmpty text="Đang tải ngày phép..." /> : (data?.length ? data.map((item) => <ListRow key={item.id} title={`${leaveTypeLabel(item.leaveType)} ${item.year}`} meta={`Tổng ${item.totalDays} · Đã dùng ${item.usedDays}`} status={`Còn ${item.remainingDays}`} />) : <HrEmpty text="Chưa thiết lập ngày phép." />)}</HrCard>;
}

function ListRow({ title, meta, status, onClick }: { title: string; meta?: string; status?: string; onClick?: () => void }) {
  return (
    <button type="button" className="hr-list-row" onClick={onClick} disabled={!onClick}>
      <div>
        <strong>{title}</strong>
        {meta && <small>{meta}</small>}
      </div>
      {status && <span>{status}</span>}
    </button>
  );
}

export function HRTimesheetPage() {
  const [month, setMonth] = useState(currentMonth());
  const [selectedDate, setSelectedDate] = useState<string | null>(null);
  const { data, loading, reload } = useApi<Timesheet>(`/api/timesheet/me?month=${month}`, [month]);
  const s = data?.summary;
  const daysByDate = new Map((data?.days ?? []).map((day) => [day.date.slice(0, 10), day]));
  const selectedDay = selectedDate ? daysByDate.get(selectedDate) : undefined;

  return (
    <HrPage
      eyebrow="Chấm công"
      title="Bảng công"
      action={<input className="hr-month-input" type="month" value={month} onChange={(e) => { setMonth(e.target.value); setSelectedDate(null); }} />}
    >
      <div className="hr-grid-4">
        <HrStat label="Ngày công" value={`${s?.workedDays ?? 0}`} hint={`Vắng ${s?.absentDays ?? 0}`} />
        <HrStat label="Đi muộn" value={`${s?.lateDays ?? 0}`} hint={fmtMinutes(s?.totalLateMinutes)} tone="warning" />
        <HrStat label="Về sớm" value={`${s?.earlyDays ?? 0}`} hint={fmtMinutes(s?.totalEarlyMinutes)} tone="warning" />
        <HrStat label="Tăng ca" value={fmtMinutes(s?.totalOvertimeMinutes)} hint={`${s?.totalWorkedHours ?? 0} giờ`} />
      </div>
      <HrCard>
        <div className="hr-card-head">
          <div>
            <h2>Lịch tháng</h2>
            <p>Chọn ngày để xem chi tiết công.</p>
          </div>
          <button type="button" onClick={() => reload()} aria-label="Làm mới"><RefreshCw className={`h-4 w-4 ${loading ? "animate-spin" : ""}`} /></button>
        </div>
        <div className="hr-timesheet-legend">
          {[
            ["worked", "Đi làm"],
            ["absent", "Nghỉ / vắng"],
            ["overtime", "Tăng ca"],
            ["warning", "Muộn / thiếu công"],
            ["off", "Không phân ca"],
          ].map(([tone, label]) => (
            <span key={tone}><i data-tone={tone} />{label}</span>
          ))}
        </div>
        <div className="hr-timesheet-weekdays">
          {timesheetWeekdays.map((day) => <span key={day}>{day}</span>)}
        </div>
        <div className="hr-timesheet-calendar">
          {timesheetCalendarDays(month).map((cell) => {
            if (!cell.dateKey) return <span key={cell.key} className="hr-timesheet-blank" />;
            const day = daysByDate.get(cell.dateKey);
            const tone = timesheetCalendarTone(day);
            return (
              <button
                key={cell.key}
                type="button"
                data-tone={tone}
                data-active={selectedDate === cell.dateKey}
                title={`${date(cell.dateKey)} - ${day?.status ?? "Chưa có dữ liệu"}`}
                onClick={() => setSelectedDate(cell.dateKey)}
              >
                <strong>{cell.day}</strong>
                <i />
                <small>{timesheetCalendarLabel(day)}</small>
              </button>
            );
          })}
        </div>
        {loading && !data && <HrEmpty text="Đang tải bảng công..." />}
      </HrCard>
      {selectedDate && (
        <HrCard>
          <div className="hr-card-head">
            <div>
              <h2>Chi tiết ngày {date(selectedDate)}</h2>
              <p>{selectedDay?.shiftName || "Chưa có ca làm cho ngày này"}</p>
            </div>
            {selectedDay ? <HrStatus status={toneForTimesheet(selectedDay)}>{selectedDay.status}</HrStatus> : <HrStatus status="muted">Chưa có dữ liệu</HrStatus>}
          </div>
          {selectedDay ? <TimesheetRow day={selectedDay} detailed /> : <HrEmpty text="Chưa có dữ liệu chấm công cho ngày này." />}
        </HrCard>
      )}
    </HrPage>
  );
}

function TimesheetDetailCell({ label, value, tone }: { label: string; value: string; tone?: Tone }) {
  return (
    <div className="hr-timesheet-detail-cell" data-tone={tone}>
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function TimesheetRow({ day, detailed = false }: { day: TimesheetDay; detailed?: boolean }) {
  if (detailed) {
    return (
      <div className="hr-timesheet-detail-grid">
        <TimesheetDetailCell label="Giờ vào" value={day.checkIn || "--:--"} />
        <TimesheetDetailCell label="Giờ ra" value={day.checkOut || "--:--"} />
        <TimesheetDetailCell label="Giờ làm" value={`${day.workedHours || 0} giờ`} />
        <TimesheetDetailCell label="Tăng ca" value={fmtMinutes(day.overtimeMinutes)} tone={day.overtimeMinutes ? "success" : undefined} />
        <TimesheetDetailCell label="Đi muộn" value={fmtMinutes(day.lateMinutes)} tone={day.lateMinutes ? "warning" : undefined} />
        <TimesheetDetailCell label="Về sớm" value={fmtMinutes(day.earlyMinutes)} tone={day.earlyMinutes ? "warning" : undefined} />
        <TimesheetDetailCell label="Phân loại" value={timesheetCalendarLabel(day)} />
        <TimesheetDetailCell label="Ca làm" value={day.shiftName || "--"} />
      </div>
    );
  }

  return (
    <article className="hr-day-row">
      <div>
        <strong>{date(day.date)}</strong>
        <small>{day.shiftName || "Không phân ca"}</small>
      </div>
      <div className="hr-day-times">
        <span>{day.checkIn || "--:--"}</span>
        <span>{day.checkOut || "--:--"}</span>
      </div>
      <HrStatus status={toneForTimesheet(day)}>{day.status}</HrStatus>
    </article>
  );
}

export function HRRequestsPage() {
  const { notify, confirm } = useAppNotifications();
  const { data: types } = useApi<RequestType[]>("/api/requests/types");
  const { data, loading, reload } = useApi<RequestListItem[]>("/api/requests?scope=mine");
  const [createOpen, setCreateOpen] = useState(false);
  const [detailId, setDetailId] = useState<string | null>(null);

  return (
    <HrPage eyebrow="Đơn từ" title="Đơn của tôi" action={<HrButton onClick={() => setCreateOpen(true)}><FilePlus2 className="h-4 w-4" /> Tạo đơn</HrButton>}>
      <HrCard>
        {loading && !data ? <HrEmpty text="Đang tải đơn..." /> : (data?.length ? data.map((item) => (
          <RequestRow key={item.id} item={item} onClick={() => setDetailId(item.id)} />
        )) : <HrEmpty text="Bạn chưa gửi đơn nào." />)}
      </HrCard>
      {createOpen && <CreateRequestModal types={types ?? []} onClose={() => setCreateOpen(false)} onCreated={() => { setCreateOpen(false); reload({ silent: true }); notify.success("Đã gửi đơn."); }} />}
      {detailId && <RequestDetailModal id={detailId} onClose={() => setDetailId(null)} onCancelled={() => { setDetailId(null); reload({ silent: true }); }} confirm={confirm} />}
    </HrPage>
  );
}

function RequestRow({ item, onClick }: { item: RequestListItem; onClick: () => void }) {
  return (
    <button type="button" className="hr-request-row" onClick={onClick}>
      <div>
        <strong>{item.typeLabel}</strong>
        <small>{item.title || item.requestNo} · {dateTime(item.createdAt)}</small>
      </div>
      <HrStatus status={toneForRequest(item.status)}>{requestStatusLabel(item.status)}</HrStatus>
    </button>
  );
}

function CreateRequestModal({ types, onClose, onCreated }: { types: RequestType[]; onClose: () => void; onCreated: () => void }) {
  const { notify } = useAppNotifications();
  const [type, setType] = useState(types[0]?.type ?? "leave");
  const [title, setTitle] = useState("");
  const [values, setValues] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const fields = requestFields[type] ?? [];
  const setField = (key: string, value: string) => setValues((s) => ({ ...s, [key]: value }));

  const submit = async () => {
    setSaving(true);
    try {
      const payload: Record<string, unknown> = {};
      for (const f of fields) {
        const v = values[f.key];
        if (v === undefined || v === "") continue;
        payload[f.key] = f.type === "number" || f.type === "money" ? Number(v) : v;
      }
      await api.post("/api/requests", { type, title: title.trim(), payload });
      onCreated();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không gửi được đơn.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <HrModal
      title="Tạo đơn mới"
      onClose={onClose}
      footer={<><HrButton tone="secondary" onClick={onClose}>Hủy</HrButton><HrButton onClick={submit} disabled={saving}><Send className="h-4 w-4" /> Gửi đơn</HrButton></>}
    >
      <div className="hr-form-stack">
        <Field label="Loại đơn">
          <Select value={type} onChange={(e) => { setType(e.target.value); setValues({}); }} className="w-full">
            {types.map((item) => <option key={item.type} value={item.type}>{item.label}</option>)}
          </Select>
        </Field>
        <Field label="Tiêu đề">
          <Input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Có thể để trống" />
        </Field>
        {fields.map((field) => {
          if (field.type === "checkboxes") {
            return (
              <div key={field.key} className="hr-check-choice-group" role="group" aria-label={field.label}>
                {field.options?.map((option) => {
                  const checked = values[field.key] === option.value;
                  return (
                    <label key={option.value} className="hr-check-choice" data-checked={checked}>
                      <input
                        type="checkbox"
                        checked={checked}
                        onChange={(e) => setField(field.key, e.target.checked ? option.value : "")}
                      />
                      <span>{option.label}</span>
                    </label>
                  );
                })}
              </div>
            );
          }

          return (
            <Field key={field.key} label={field.label}>
              {field.type === "textarea" ? (
                <textarea className="hr-textarea" value={values[field.key] ?? ""} onChange={(e) => setField(field.key, e.target.value)} rows={4} />
              ) : field.type === "select" ? (
                <Select value={values[field.key] ?? ""} onChange={(e) => setField(field.key, e.target.value)} className="w-full">
                  <option value="" disabled>-- Chọn --</option>
                  {field.options?.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
                </Select>
              ) : (
                <Input
                  type={field.type === "date" ? "date" : field.type === "time" ? "time" : field.type === "number" || field.type === "money" ? "number" : "text"}
                  value={values[field.key] ?? ""}
                  onChange={(e) => setField(field.key, e.target.value)}
                />
              )}
            </Field>
          );
        })}
      </div>
    </HrModal>
  );
}

function RequestDetailModal({ id, onClose, onCancelled, confirm }: { id: string; onClose: () => void; onCancelled: () => void; confirm: ReturnType<typeof useAppNotifications>["confirm"] }) {
  const { notify } = useAppNotifications();
  const { data, loading } = useApi<RequestDetail>(`/api/requests/${id}`);
  const [busy, setBusy] = useState(false);

  const cancel = async () => {
    const ok = await confirm({ title: "Hủy đơn này?", description: "Đơn đang chờ duyệt sẽ bị hủy.", confirmLabel: "Hủy đơn", tone: "warning" });
    if (!ok) return;
    setBusy(true);
    try {
      await api.post(`/api/requests/${id}/cancel`);
      notify.success("Đã hủy đơn.");
      onCancelled();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không hủy được đơn.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <HrModal
      title={data ? `${data.request.typeLabel} · ${data.request.requestNo}` : "Chi tiết đơn"}
      onClose={onClose}
      wide
      footer={data?.request.status === "Pending" ? <HrButton tone="danger" onClick={cancel} disabled={busy}>Hủy đơn</HrButton> : <HrButton tone="secondary" onClick={onClose}>Đóng</HrButton>}
    >
      {loading || !data ? <HrEmpty text="Đang tải..." /> : (
        <div className="hr-detail-grid">
          <div className="hr-detail-box">
            <HrStatus status={toneForRequest(data.request.status)}>{requestStatusLabel(data.request.status)}</HrStatus>
            <h3>{data.request.title || data.request.typeLabel}</h3>
            <dl>
              <Row label="Người gửi" value={`${data.request.employeeName} (${data.request.employeeCode})`} />
              <Row label="Phòng ban" value={data.request.departmentName || "--"} />
              {Object.entries(data.request.payload ?? {}).map(([k, v]) => <Row key={k} label={fieldLabel(data.request.type, k)} value={fieldDisplayValue(data.request.type, k, v)} />)}
            </dl>
          </div>
          <div className="hr-detail-box">
            <h3>Tiến trình duyệt</h3>
            {data.approvals.map((step) => (
              <div key={step.stepNo} className="hr-step-row">
                <span>{step.stepNo}</span>
                <div>
                  <strong>{step.approverName || step.approverUsername || step.approverRole}</strong>
                  <small>{requestStatusLabel(step.status)}{step.decidedAt ? ` · ${dateTime(step.decidedAt)}` : ""}</small>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
    </HrModal>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return <div><dt>{label}</dt><dd>{value}</dd></div>;
}

export function HRApprovalPage() {
  const { notify } = useAppNotifications();
  const { data, loading, reload } = useApi<RequestListItem[]>("/api/requests?scope=inbox");
  const [activeId, setActiveId] = useState<string | null>(null);

  return (
    <HrPage eyebrow="Phê duyệt" title="Hộp thư duyệt">
      <HrCard>
        {loading && !data ? <HrEmpty text="Đang tải đơn chờ duyệt..." /> : (data?.length ? data.map((item) => <RequestRow key={item.id} item={item} onClick={() => setActiveId(item.id)} />) : <HrEmpty text="Không có đơn nào chờ bạn duyệt." />)}
      </HrCard>
      {activeId && <ApproveModal id={activeId} onClose={() => setActiveId(null)} onDone={(message) => { setActiveId(null); reload({ silent: true }); notify.success(message); }} />}
    </HrPage>
  );
}

function ApproveModal({ id, onClose, onDone }: { id: string; onClose: () => void; onDone: (message: string) => void }) {
  const { notify } = useAppNotifications();
  const { data, loading } = useApi<RequestDetail>(`/api/requests/${id}`);
  const [comment, setComment] = useState("");
  const [busy, setBusy] = useState<"approve" | "reject" | null>(null);

  const decide = async (approve: boolean) => {
    setBusy(approve ? "approve" : "reject");
    try {
      await api.post(`/api/requests/${id}/${approve ? "approve" : "reject"}`, { comment: comment.trim(), signature: null });
      onDone(approve ? "Đã duyệt đơn." : "Đã từ chối đơn.");
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không xử lý được.");
    } finally {
      setBusy(null);
    }
  };

  return (
    <HrModal
      title={data ? `${data.request.typeLabel} · ${data.request.requestNo}` : "Duyệt đơn"}
      onClose={onClose}
      wide
      footer={<><HrButton tone="danger" onClick={() => decide(false)} disabled={busy !== null}><XCircle className="h-4 w-4" /> Từ chối</HrButton><HrButton onClick={() => decide(true)} disabled={busy !== null}><CheckCircle2 className="h-4 w-4" /> Duyệt</HrButton></>}
    >
      {loading || !data ? <HrEmpty text="Đang tải..." /> : (
        <div className="hr-detail-grid">
          <div className="hr-detail-box">
            <h3>{data.request.title || data.request.typeLabel}</h3>
            <dl>
              <Row label="Người gửi" value={`${data.request.employeeName} (${data.request.employeeCode})`} />
              <Row label="Phòng ban" value={data.request.departmentName || "--"} />
              {Object.entries(data.request.payload ?? {}).map(([k, v]) => <Row key={k} label={fieldLabel(data.request.type, k)} value={fieldDisplayValue(data.request.type, k, v)} />)}
            </dl>
          </div>
          <div className="hr-detail-box">
            <Field label="Ý kiến duyệt">
              <textarea className="hr-textarea" rows={6} value={comment} onChange={(e) => setComment(e.target.value)} />
            </Field>
          </div>
        </div>
      )}
    </HrModal>
  );
}

export function HRPenaltyPage() {
  const { user } = useAuth();
  const admin = isAdmin(user);
  const { notify, confirm } = useAppNotifications();
  const [month, setMonth] = useState("");
  const query = admin
    ? `/api/penalties?scope=all${month ? `&month=${month}` : ""}`
    : "/api/penalties?scope=mine";
  const { data, loading, reload } = useApi<Penalty[]>(query, [query]);
  const { data: me } = useApi<EmployeeDetail>("/api/hr/me");
  const canAccounting = admin || !!me?.isAccounting;
  const { data: myRefunds, reload: reloadMyRefunds } = useApi<PenaltyRefund[]>("/api/penalty-refunds?scope=mine");
  const { data: queueRefunds, reload: reloadQueue } = useApi<PenaltyRefund[]>(
    canAccounting ? "/api/penalty-refunds?scope=queue" : null,
    [canAccounting],
  );
  const [editing, setEditing] = useState<Penalty | "new" | null>(null);
  const [appealing, setAppealing] = useState<Penalty | null>(null);
  const [approvingRefund, setApprovingRefund] = useState<PenaltyRefund | null>(null);

  const items = data ?? [];
  const activeFines = items
    .filter((p) => p.status === "Active" && p.penaltyType === "fine")
    .reduce((sum, p) => sum + (p.amount || 0), 0);

  const waive = async (p: Penalty) => {
    const ok = await confirm({ title: "Miễn phạt?", description: `${p.penaltyNo} sẽ chuyển sang trạng thái đã miễn.`, confirmLabel: "Miễn phạt", tone: "warning" });
    if (!ok) return;
    try {
      await api.post(`/api/penalties/${p.id}/waive`);
      notify.success("Đã miễn phạt.");
      reload({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không xử lý được.");
    }
  };

  const remove = async (p: Penalty) => {
    const ok = await confirm({ title: "Xóa quyết định phạt?", description: `Xóa vĩnh viễn ${p.penaltyNo}.`, confirmLabel: "Xóa", tone: "danger" });
    if (!ok) return;
    try {
      await api.del(`/api/penalties/${p.id}`);
      notify.success("Đã xóa.");
      reload({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không xóa được.");
    }
  };

  const refundAction = async (r: PenaltyRefund, action: "reject" | "mark-paid") => {
    const label = action === "reject" ? "Từ chối khoản hoàn?" : "Xác nhận đã chi tiền mặt?";
    const ok = await confirm({ title: label, description: `${r.refundNo} · ${moneyVnd(r.amount)}`, confirmLabel: "Xác nhận", tone: action === "reject" ? "danger" : "info" });
    if (!ok) return;
    try {
      await api.post(`/api/penalty-refunds/${r.id}/${action}`, action === "reject" ? { note: "" } : {});
      notify.success(action === "reject" ? "Đã từ chối." : "Đã đánh dấu đã chi.");
      reloadQueue({ silent: true });
      reloadMyRefunds({ silent: true });
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không xử lý được.");
    }
  };

  const pendingQueue = (queueRefunds ?? []).filter((r) => r.status === "PendingAccounting" || r.status === "Approved");

  return (
    <HrPage
      eyebrow="Kỷ luật"
      title={admin ? "Phạt / kỷ luật" : "Phạt của tôi"}
      action={admin ? <HrButton onClick={() => setEditing("new")}><FilePlus2 className="h-4 w-4" /> Lập phạt</HrButton> : undefined}
    >
      <div className="hr-grid-3">
        <HrStat label="Số lần bị phạt" value={`${items.length}`} tone={items.length ? "warning" : "neutral"} />
        <HrStat label="Còn hiệu lực" value={`${items.filter((p) => p.status === "Active").length}`} />
        <HrStat label="Tổng phạt tiền" value={moneyVnd(activeFines)} hint="Đang hiệu lực" tone={activeFines > 0 ? "danger" : "neutral"} />
      </div>

      {admin && (
        <HrCard>
          <div className="hr-range-row">
            <Field label="Lọc theo tháng">
              <Input type="month" value={month} onChange={(e) => setMonth(e.target.value)} />
            </Field>
            {month && <HrButton tone="secondary" onClick={() => setMonth("")}>Xóa lọc</HrButton>}
          </div>
        </HrCard>
      )}

      <HrCard>
        {loading && !data ? <HrEmpty text="Đang tải danh sách phạt..." /> : (items.length ? items.map((p) => (
          <PenaltyRow key={p.id} p={p} admin={admin} onEdit={() => setEditing(p)} onWaive={() => waive(p)} onDelete={() => remove(p)} onAppeal={() => setAppealing(p)} />
        )) : <HrEmpty text={admin ? "Chưa có quyết định phạt nào." : "Bạn chưa bị phạt lần nào. 🎉"} />)}
      </HrCard>

      {(myRefunds?.length ?? 0) > 0 && (
        <HrCard>
          <div className="hr-card-title">Hoàn tiền phạt của tôi</div>
          {myRefunds!.map((r) => <RefundRow key={r.id} r={r} showEmployee={false} />)}
        </HrCard>
      )}

      {canAccounting && (
        <HrCard>
          <div className="hr-card-title">Duyệt chi hoàn phạt {pendingQueue.length > 0 ? `(${pendingQueue.length})` : ""}</div>
          {pendingQueue.length === 0 ? <HrEmpty text="Không có khoản hoàn nào chờ xử lý." /> : pendingQueue.map((r) => (
            <RefundRow
              key={r.id}
              r={r}
              showEmployee
              actions={
                r.status === "PendingAccounting" ? (
                  <>
                    <HrButton onClick={() => setApprovingRefund(r)}><CheckCircle2 className="h-4 w-4" /> Duyệt</HrButton>
                    <HrButton tone="danger" onClick={() => refundAction(r, "reject")}><XCircle className="h-4 w-4" /> Từ chối</HrButton>
                  </>
                ) : r.status === "Approved" && r.payoutMethod === "cash" ? (
                  <HrButton onClick={() => refundAction(r, "mark-paid")}><Banknote className="h-4 w-4" /> Đã chi tiền</HrButton>
                ) : null
              }
            />
          ))}
        </HrCard>
      )}

      {approvingRefund && (
        <RefundApproveModal
          refund={approvingRefund}
          onClose={() => setApprovingRefund(null)}
          onDone={() => { setApprovingRefund(null); reloadQueue({ silent: true }); reloadMyRefunds({ silent: true }); notify.success("Đã duyệt khoản hoàn."); }}
        />
      )}

      {editing && (
        <PenaltyModal
          penalty={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => { setEditing(null); reload({ silent: true }); notify.success("Đã lưu quyết định phạt."); }}
        />
      )}

      {appealing && (
        <PenaltyAppealModal
          penalty={appealing}
          onClose={() => setAppealing(null)}
          onSent={() => { setAppealing(null); notify.success("Đã gửi khiếu nại. Theo dõi tại mục Đơn từ."); }}
        />
      )}
    </HrPage>
  );
}

function PenaltyRow({ p, admin, onEdit, onWaive, onDelete, onAppeal }: { p: Penalty; admin: boolean; onEdit: () => void; onWaive: () => void; onDelete: () => void; onAppeal: () => void }) {
  const waived = p.status === "Waived";
  const progress = p.progress ?? null;
  return (
    <article className="hr-day-row" style={waived ? { opacity: 0.6 } : undefined}>
      <div>
        <strong>{admin ? p.employeeName : p.penaltyTypeLabel}</strong>
        <small>
          {admin ? `${p.penaltyTypeLabel} · ` : ""}
          {p.penaltyDate ? date(p.penaltyDate) : "--"}
          {p.amount > 0 ? ` · ${moneyVnd(p.amount)}` : ""}
          {p.penaltyType === "fine" && p.installments > 1 ? ` · trừ ${p.installments} tháng` : ""}
          {p.reason ? ` · ${p.reason}` : ""}
        </small>
      </div>
      <div className="hr-penalty-right">
        <HrStatus status={waived ? "muted" : (penaltyTypeColor(p.penaltyType) as Tone)}>
          {waived ? penaltyStatusLabel(p.status) : p.penaltyTypeLabel}
        </HrStatus>
        {admin ? (
          <div className="hr-penalty-actions">
            <button type="button" className="hr-icon-btn" onClick={onEdit} aria-label="Sửa"><Pencil className="h-4 w-4" /></button>
            {!waived && <button type="button" className="hr-icon-btn" onClick={onWaive} aria-label="Miễn phạt"><Ban className="h-4 w-4" /></button>}
            <button type="button" className="hr-icon-btn" onClick={onDelete} aria-label="Xóa"><Trash2 className="h-4 w-4" /></button>
          </div>
        ) : (
          !waived && (
            <button type="button" className="hr-appeal-btn" onClick={onAppeal}>
              <Megaphone className="h-3.5 w-3.5" /> Khiếu nại
            </button>
          )
        )}
      </div>

      {progress && <PenaltyProgressView progress={progress} />}
    </article>
  );
}

/** Thanh tiến trình khấu trừ phạt tiền: đã trừ / còn lại / còn bao nhiêu kỳ, kèm chi tiết từng kỳ. */
function PenaltyProgressView({ progress }: { progress: NonNullable<Penalty["progress"]> }) {
  const [open, setOpen] = useState(false);
  const pct = progress.total > 0 ? Math.min(100, Math.round((progress.deducted / progress.total) * 100)) : 0;
  const done = progress.remainingMonths === 0;
  return (
    <div className="hr-penalty-progress">
      <div className="hr-penalty-progress-bar">
        <span style={{ width: `${pct}%` }} data-done={done} />
      </div>
      <div className="hr-penalty-progress-meta">
        <span>Đã trừ <b>{moneyVnd(progress.deducted)}</b> / {moneyVnd(progress.total)}</span>
        <span>Còn lại <b>{moneyVnd(progress.remaining)}</b></span>
        <span>{done ? "Đã trừ xong" : `Còn ${progress.remainingMonths}/${progress.totalMonths} kỳ`}</span>
      </div>
      {!done && progress.nextPeriod && (
        <small className="hr-penalty-progress-next">
          Kỳ tới ({addMonths(progress.nextPeriod, 0)}) sẽ trừ <b>{moneyVnd(progress.nextAmount)}</b> khi có phiếu lương.
        </small>
      )}
      <button type="button" className="hr-penalty-progress-toggle" onClick={() => setOpen((v) => !v)}>
        {open ? "Ẩn chi tiết" : "Xem chi tiết từng kỳ"}
      </button>
      {open && (
        <div className="hr-penalty-schedule">
          {progress.periods.map((it) => (
            <div key={it.installmentNo}>
              <span>
                Kỳ {addMonths(it.period, 0)}{progress.totalMonths > 1 ? ` · đợt ${it.installmentNo}/${progress.totalMonths}` : ""}
                {it.paid ? " · đã trừ" : " · chưa tới"}
              </span>
              <b data-paid={it.paid}>{it.paid ? "✓ " : ""}{moneyVnd(it.amount)}</b>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

/** Khiếu nại một án phạt: tạo đơn từ loại "penalty_appeal" ngay trong giao diện phạt của nhân viên. */
function PenaltyAppealModal({ penalty, onClose, onSent }: { penalty: Penalty; onClose: () => void; onSent: () => void }) {
  const { notify } = useAppNotifications();
  const [reason, setReason] = useState("");
  const [saving, setSaving] = useState(false);

  const submit = async () => {
    if (!reason.trim()) { notify.error("Vui lòng nhập nội dung khiếu nại."); return; }
    setSaving(true);
    try {
      await api.post("/api/requests", {
        type: "penalty_appeal",
        title: `Khiếu nại án phạt ${penalty.penaltyNo}`,
        payload: {
          penaltyNo: penalty.penaltyNo,
          penaltyType: penalty.penaltyTypeLabel,
          penaltyAmount: penalty.amount || 0,
          reason: reason.trim(),
        },
      });
      onSent();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không gửi được khiếu nại.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <HrModal
      title={`Khiếu nại án phạt · ${penalty.penaltyNo}`}
      onClose={onClose}
      footer={<><HrButton tone="secondary" onClick={onClose}>Hủy</HrButton><HrButton onClick={submit} disabled={saving}><Send className="h-4 w-4" /> Gửi khiếu nại</HrButton></>}
    >
      <div className="hr-form-stack">
        <div className="hr-penalty-schedule">
          <div><span>Hình thức</span><b>{penalty.penaltyTypeLabel}</b></div>
          {penalty.amount > 0 && <div><span>Số tiền phạt</span><b>{moneyVnd(penalty.amount)}</b></div>}
          {penalty.reason && <div><span>Lý do phạt</span><b>{penalty.reason}</b></div>}
        </div>
        <Field label="Nội dung khiếu nại">
          <textarea className="hr-textarea" rows={4} value={reason} onChange={(e) => setReason(e.target.value)}
            placeholder="Trình bày lý do bạn không đồng ý với quyết định phạt này…" />
        </Field>
        <p className="hr-hint-text">Khiếu nại sẽ được gửi như một đơn từ và chuyển qua quản lý trực tiếp rồi đến quản trị / HR để xem xét. Bạn theo dõi tiến trình tại mục Đơn từ.</p>
      </div>
    </HrModal>
  );
}

/** Một dòng khoản hoàn tiền phạt (dùng cho "của tôi" và hàng đợi kế toán). */
function RefundRow({ r, showEmployee, actions }: { r: PenaltyRefund; showEmployee: boolean; actions?: ReactNode }) {
  const paid = r.status === "Paid";
  return (
    <article className="hr-day-row" style={r.status === "Rejected" ? { opacity: 0.6 } : undefined}>
      <div>
        <strong>{showEmployee ? r.employeeName : `Hoàn phạt ${r.penaltyNo}`}</strong>
        <small>
          {showEmployee ? `${r.refundNo} · phạt ${r.penaltyNo} · ` : `${r.refundNo} · `}
          {moneyVnd(r.amount)}
          {r.payoutMethod ? ` · ${payoutMethodLabel(r.payoutMethod)}` : ""}
          {paid && r.payoutMethod === "payroll" && r.appliedPeriod ? ` (kỳ ${r.appliedPeriod})` : ""}
        </small>
      </div>
      <div className="hr-penalty-right">
        <HrStatus status={refundStatusColor(r.status) as Tone}>{refundStatusLabel(r.status)}</HrStatus>
      </div>
      {actions && <div className="hr-refund-actions">{actions}</div>}
    </article>
  );
}

/** Kế toán duyệt khoản hoàn: chọn hình thức chi trả (cộng vào lương / chi tiền mặt). */
function RefundApproveModal({ refund, onClose, onDone }: { refund: PenaltyRefund; onClose: () => void; onDone: () => void }) {
  const { notify } = useAppNotifications();
  const [payoutMethod, setPayoutMethod] = useState<"payroll" | "cash">("payroll");
  const [note, setNote] = useState("");
  const [saving, setSaving] = useState(false);

  const submit = async () => {
    setSaving(true);
    try {
      await api.post(`/api/penalty-refunds/${refund.id}/approve`, { payoutMethod, note: note.trim() });
      onDone();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không duyệt được.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <HrModal
      title={`Duyệt hoàn phạt · ${refund.refundNo}`}
      onClose={onClose}
      footer={<><HrButton tone="secondary" onClick={onClose}>Hủy</HrButton><HrButton onClick={submit} disabled={saving}><CheckCircle2 className="h-4 w-4" /> Duyệt chi</HrButton></>}
    >
      <div className="hr-form-stack">
        <div className="hr-penalty-schedule">
          <div><span>Nhân viên</span><b>{refund.employeeName} ({refund.employeeCode})</b></div>
          <div><span>Án phạt</span><b>{refund.penaltyNo}</b></div>
          <div><span>Số tiền hoàn</span><b>{moneyVnd(refund.amount)}</b></div>
          {refund.reason && <div><span>Lý do</span><b>{refund.reason}</b></div>}
        </div>
        <Field label="Hình thức chi trả">
          <div className="hr-radio-stack">
            <label><input type="radio" name="payout" checked={payoutMethod === "payroll"} onChange={() => setPayoutMethod("payroll")} /> Cộng vào phiếu lương kỳ kế tiếp</label>
            <label><input type="radio" name="payout" checked={payoutMethod === "cash"} onChange={() => setPayoutMethod("cash")} /> Chi tiền mặt (nhân viên nhận tại phòng kế toán)</label>
          </div>
        </Field>
        <Field label="Ghi chú"><textarea className="hr-textarea" rows={2} value={note} onChange={(e) => setNote(e.target.value)} /></Field>
        <p className="hr-hint-text">
          {payoutMethod === "payroll"
            ? "Khoản hoàn sẽ tự cộng vào phiếu lương kỳ tiếp theo khi lập phiếu."
            : "Sau khi duyệt, bấm “Đã chi tiền” khi nhân viên đã nhận tiền mặt."}
        </p>
      </div>
    </HrModal>
  );
}

function PenaltyModal({ penalty, onClose, onSaved }: { penalty: Penalty | null; onClose: () => void; onSaved: () => void }) {
  const { notify } = useAppNotifications();
  const { data: employees } = useApi<EmployeeCard[]>(penalty ? null : "/api/hr/employees");
  const { data: types } = useApi<PenaltyType[]>("/api/penalties/types");
  const [employeeId, setEmployeeId] = useState(penalty?.employeeId ?? "");
  const [penaltyType, setPenaltyType] = useState(penalty?.penaltyType ?? "reminder");
  const [penaltyDate, setPenaltyDate] = useState(penalty?.penaltyDate?.slice(0, 10) ?? todayKey());
  const [amount, setAmount] = useState(penalty ? String(penalty.amount ?? 0) : "");
  const [installments, setInstallments] = useState(String(penalty?.installments ?? 1));
  const [startPeriod, setStartPeriod] = useState(penalty?.startPeriod || (penalty?.penaltyDate?.slice(0, 7)) || currentMonth());
  const [reason, setReason] = useState(penalty?.reason ?? "");
  const [note, setNote] = useState(penalty?.note ?? "");
  const [status, setStatus] = useState(penalty?.status ?? "Active");
  const [saving, setSaving] = useState(false);

  const isFine = penaltyType === "fine";
  const nInstallments = Math.max(1, Number(installments) || 1);
  const schedule = isFine ? penaltySchedule(Number(amount) || 0, nInstallments) : [];

  const submit = async () => {
    if (!penalty && !employeeId) { notify.error("Vui lòng chọn nhân viên."); return; }
    if (!reason.trim()) { notify.error("Vui lòng nhập lý do phạt."); return; }
    setSaving(true);
    try {
      const body = {
        employeeId: penalty?.employeeId ?? employeeId,
        penaltyType,
        penaltyDate: penaltyDate || null,
        amount: Number(amount) || 0,
        installments: isFine ? nInstallments : 1,
        startPeriod: isFine ? startPeriod : "",
        reason: reason.trim(),
        note: note.trim(),
        status,
      };
      if (penalty) await api.put(`/api/penalties/${penalty.id}`, body);
      else await api.post("/api/penalties", body);
      onSaved();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không lưu được.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <HrModal
      title={penalty ? `Sửa phạt · ${penalty.penaltyNo}` : "Lập quyết định phạt"}
      onClose={onClose}
      footer={<><HrButton tone="secondary" onClick={onClose}>Hủy</HrButton><HrButton onClick={submit} disabled={saving}><Save className="h-4 w-4" /> Lưu</HrButton></>}
    >
      <div className="hr-form-stack">
        {penalty ? (
          <Field label="Nhân viên"><Input value={`${penalty.employeeName} (${penalty.employeeCode})`} disabled /></Field>
        ) : (
          <Field label="Nhân viên">
            <Select value={employeeId} onChange={(e) => setEmployeeId(e.target.value)} className="w-full">
              <option value="">-- Chọn nhân viên --</option>
              {employees?.map((emp) => <option key={emp.id} value={emp.id}>{emp.fullName} ({emp.employeeCode})</option>)}
            </Select>
          </Field>
        )}
        <Field label="Hình thức">
          <Select value={penaltyType} onChange={(e) => setPenaltyType(e.target.value)} className="w-full">
            {(types ?? []).map((t) => <option key={t.type} value={t.type}>{t.label}</option>)}
          </Select>
        </Field>
        <Field label="Ngày phạt"><Input type="date" value={penaltyDate} onChange={(e) => setPenaltyDate(e.target.value)} /></Field>
        <Field label="Số tiền phạt (₫)"><MoneyInput value={amount} onChange={setAmount} /></Field>
        {isFine && (
          <>
            <Field label="Trừ trong (số tháng)">
              <Input type="number" min={1} max={60} value={installments} onChange={(e) => setInstallments(e.target.value)} />
            </Field>
            <Field label="Bắt đầu trừ từ kỳ"><Input type="month" value={startPeriod} onChange={(e) => setStartPeriod(e.target.value)} /></Field>
            {schedule.length > 0 && (
              <div className="hr-penalty-schedule">
                <strong>Lịch khấu trừ dự kiến</strong>
                {schedule.map((amt, i) => (
                  <div key={i}>
                    <span>Kỳ {addMonths(startPeriod, i)}{nInstallments > 1 ? ` · đợt ${i + 1}/${schedule.length}` : ""}</span>
                    <b>{moneyVnd(amt)}</b>
                  </div>
                ))}
              </div>
            )}
          </>
        )}
        <Field label="Lý do"><textarea className="hr-textarea" rows={3} value={reason} onChange={(e) => setReason(e.target.value)} /></Field>
        <Field label="Ghi chú"><textarea className="hr-textarea" rows={2} value={note} onChange={(e) => setNote(e.target.value)} /></Field>
        {penalty && (
          <Field label="Trạng thái">
            <Select value={status} onChange={(e) => setStatus(e.target.value)} className="w-full">
              <option value="Active">Còn hiệu lực</option>
              <option value="Waived">Đã miễn</option>
            </Select>
          </Field>
        )}
      </div>
    </HrModal>
  );
}

export function HRPayrollPage() {
  const [tab, setTab] = useState<"salary" | "payslip">("salary");
  return (
    <HrPage eyebrow="Quản trị" title="Bảng lương">
      <div className="hr-tabs">
        {[
          ["salary", "Mức lương"],
          ["payslip", "Lập phiếu lương"],
        ].map(([key, label]) => (
          <button key={key} type="button" data-active={tab === key} onClick={() => setTab(key as typeof tab)}>{label}</button>
        ))}
      </div>
      {tab === "salary" ? <SalaryAdmin /> : <PayslipMaker />}
    </HrPage>
  );
}

function SalaryAdmin() {
  const { data, loading, reload } = useApi<SalaryListItem[]>("/api/payroll/salaries");
  const [edit, setEdit] = useState<SalaryListItem | null>(null);
  return (
    <HrCard>
      {loading && !data ? <HrEmpty text="Đang tải mức lương..." /> : (data?.length ? data.map((row) => (
        <article key={row.employeeId} className="hr-day-row">
          <div>
            <strong>{row.employeeName}</strong>
            <small>
              {row.employeeCode}
              {row.hasSalary ? ` · CB ${moneyVnd(row.baseSalary)}` : ""}
              {row.hasSalary && row.allowance > 0 ? ` · PC ${moneyVnd(row.allowance)}` : ""}
              {row.hasSalary && row.extraCount > 0 ? ` · +${row.extraCount} khoản` : ""}
            </small>
          </div>
          <div className="hr-penalty-right">
            <HrStatus status={row.hasSalary ? "success" : "muted"}>{row.hasSalary ? "Đã gán" : "Chưa gán"}</HrStatus>
            <div className="hr-penalty-actions">
              <button type="button" className="hr-icon-btn" onClick={() => setEdit(row)} aria-label="Sửa mức lương"><Pencil className="h-4 w-4" /></button>
            </div>
          </div>
        </article>
      )) : <HrEmpty text="Chưa có nhân viên." />)}
      {edit && <SalaryModal item={edit} onClose={() => setEdit(null)} onSaved={() => { setEdit(null); reload({ silent: true }); }} />}
    </HrCard>
  );
}

function SalaryModal({ item, onClose, onSaved }: { item: SalaryListItem; onClose: () => void; onSaved: () => void }) {
  const { notify } = useAppNotifications();
  const { data, loading } = useApi<SalaryDetail>(`/api/payroll/salaries/${item.employeeId}`);
  const [base, setBase] = useState("");
  const [allowance, setAllowance] = useState("");
  const [overtimeRate, setOvertimeRate] = useState("");
  const [components, setComponents] = useState<SalaryComponent[]>([]);
  const [note, setNote] = useState("");
  const [ready, setReady] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!data || ready) return;
    setBase(String(data.baseSalary ?? 0));
    setAllowance(String(data.allowance ?? 0));
    setOvertimeRate(String(data.overtimeRate ?? 0));
    setComponents(data.components ?? []);
    setNote(data.note ?? "");
    setReady(true);
  }, [data, ready]);

  const addComponent = (kind: "earning" | "deduction") => setComponents((s) => [...s, { label: "", amount: 0, kind }]);
  const updateComponent = (i: number, patch: Partial<SalaryComponent>) =>
    setComponents((s) => s.map((c, idx) => (idx === i ? { ...c, ...patch } : c)));
  const removeComponent = (i: number) => setComponents((s) => s.filter((_, idx) => idx !== i));

  const submit = async () => {
    setSaving(true);
    try {
      await api.put(`/api/payroll/salaries/${item.employeeId}`, {
        baseSalary: Number(base) || 0,
        allowance: Number(allowance) || 0,
        overtimeRate: Number(overtimeRate) || 0,
        components: components
          .filter((c) => c.label.trim())
          .map((c) => ({ label: c.label.trim(), amount: Number(c.amount) || 0, kind: c.kind })),
        note: note.trim(),
      });
      notify.success("Đã lưu mức lương.");
      onSaved();
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không lưu được.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <HrModal
      title={`Mức lương · ${item.employeeName}`}
      onClose={onClose}
      footer={<><HrButton tone="secondary" onClick={onClose}>Hủy</HrButton><HrButton onClick={submit} disabled={saving || !ready}><Save className="h-4 w-4" /> Lưu</HrButton></>}
    >
      {loading && !data ? <HrEmpty text="Đang tải..." /> : (
        <div className="hr-form-stack">
          <Field label="Lương cơ bản (₫)"><MoneyInput value={base} onChange={setBase} /></Field>
          <Field label="Phụ cấp (₫)"><MoneyInput value={allowance} onChange={setAllowance} /></Field>
          <Field label="Đơn giá tăng ca (₫/giờ)"><MoneyInput value={overtimeRate} onChange={setOvertimeRate} /></Field>

          <div className="hr-salary-components">
            <div className="hr-salary-components-head">
              <strong>Khoản khác</strong>
              <div>
                <button type="button" onClick={() => addComponent("earning")}><Plus className="h-3.5 w-3.5" /> Cộng</button>
                <button type="button" onClick={() => addComponent("deduction")}><Plus className="h-3.5 w-3.5" /> Trừ</button>
              </div>
            </div>
            {components.length === 0 && <p className="hr-salary-empty">Chưa có khoản cộng/trừ cố định nào.</p>}
            {components.map((c, i) => (
              <div key={i} className="hr-salary-comp-row" data-kind={c.kind}>
                <Input value={c.label} placeholder={c.kind === "earning" ? "Tên khoản cộng" : "Tên khoản trừ"} onChange={(e) => updateComponent(i, { label: e.target.value })} />
                <MoneyInput value={c.amount} onChange={(raw) => updateComponent(i, { amount: Number(raw) || 0 })} />
                <span className="hr-salary-comp-tag">{c.kind === "earning" ? "Cộng" : "Trừ"}</span>
                <button type="button" className="hr-icon-btn" onClick={() => removeComponent(i)} aria-label="Xóa"><Trash2 className="h-4 w-4" /></button>
              </div>
            ))}
          </div>

          <Field label="Ghi chú"><textarea className="hr-textarea" rows={2} value={note} onChange={(e) => setNote(e.target.value)} /></Field>
        </div>
      )}
    </HrModal>
  );
}

function PayslipMaker() {
  const { notify } = useAppNotifications();
  const { data: employees } = useApi<EmployeeCard[]>("/api/hr/employees");
  const [employeeId, setEmployeeId] = useState("");
  const [period, setPeriod] = useState(currentMonth());
  const [published, setPublished] = useState(true);
  const [adjustments, setAdjustments] = useState<SalaryComponent[]>([]);
  const [saving, setSaving] = useState(false);
  const canQuery = Boolean(employeeId && period);
  const { data: compute, loading } = useApi<PayrollCompute>(
    canQuery ? `/api/payroll/compute?employeeId=${employeeId}&period=${period}` : null,
    [employeeId, period],
  );

  const addAdj = (kind: "earning" | "deduction") => setAdjustments((s) => [...s, { label: "", amount: 0, kind }]);
  const updateAdj = (i: number, patch: Partial<SalaryComponent>) => setAdjustments((s) => s.map((c, idx) => (idx === i ? { ...c, ...patch } : c)));
  const removeAdj = (i: number) => setAdjustments((s) => s.filter((_, idx) => idx !== i));

  const adjEarnings = adjustments.filter((a) => a.kind === "earning" && a.label.trim());
  const adjDeductions = adjustments.filter((a) => a.kind === "deduction" && a.label.trim());
  const earnings: PayLine[] = [...(compute?.earnings ?? []), ...adjEarnings.map((a) => ({ label: a.label.trim(), amount: Number(a.amount) || 0 }))];
  const deductions: PayLine[] = [...(compute?.deductions ?? []), ...adjDeductions.map((a) => ({ label: a.label.trim(), amount: Number(a.amount) || 0 }))];
  const totalEarnings = earnings.reduce((s, e) => s + e.amount, 0);
  const totalDeductions = deductions.reduce((s, e) => s + e.amount, 0);
  const net = totalEarnings - totalDeductions;

  const create = async () => {
    if (!compute) return;
    setSaving(true);
    try {
      await api.post("/api/payroll/payslips", {
        employeeId,
        period,
        published,
        adjustments: adjustments.filter((a) => a.label.trim()).map((a) => ({ label: a.label.trim(), amount: Number(a.amount) || 0, kind: a.kind })),
      });
      notify.success("Đã lập phiếu lương.");
      setAdjustments([]);
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không lập được phiếu.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <>
      <HrCard>
        <div className="hr-form-grid">
          <Field label="Nhân viên">
            <Select value={employeeId} onChange={(e) => setEmployeeId(e.target.value)} className="w-full">
              <option value="">-- Chọn nhân viên --</option>
              {employees?.map((emp) => <option key={emp.id} value={emp.id}>{emp.fullName} ({emp.employeeCode})</option>)}
            </Select>
          </Field>
          <Field label="Kỳ lương"><Input type="month" value={period} onChange={(e) => setPeriod(e.target.value)} /></Field>
        </div>
      </HrCard>

      {!canQuery ? (
        <HrCard><HrEmpty text="Chọn nhân viên và kỳ lương để tính." /></HrCard>
      ) : loading && !compute ? (
        <HrCard><HrEmpty text="Đang tính lương..." /></HrCard>
      ) : compute ? (
        <>
          <div className="hr-grid-3">
            <HrStat label="Ngày công" value={`${compute.workedDays}`} hint={`Vắng ${compute.absentDays}`} />
            <HrStat label="Tăng ca" value={`${compute.overtimeHours} giờ`} hint={moneyVnd(compute.overtimePay)} />
            <HrStat label="Đi muộn" value={`${compute.lateDays}`} tone={compute.lateDays > 0 ? "warning" : "neutral"} />
          </div>

          <HrCard>
            <div className="hr-payslip-lines">
              <div className="hr-payslip-group">
                <h3>Khoản cộng</h3>
                {earnings.map((e, i) => <PaylineRow key={`e${i}`} label={e.label} amount={e.amount} />)}
                <PaylineRow label="Tổng thu nhập" amount={totalEarnings} total />
              </div>
              <div className="hr-payslip-group">
                <h3>Khoản trừ</h3>
                {deductions.length === 0 && <p className="hr-salary-empty">Không có khoản trừ.</p>}
                {deductions.map((d, i) => <PaylineRow key={`d${i}`} label={d.label} amount={d.amount} minus />)}
                <PaylineRow label="Tổng khấu trừ" amount={totalDeductions} total minus />
              </div>
            </div>
            <div className="hr-payslip-net">
              <span>Thực nhận</span>
              <strong>{moneyVnd(net)}</strong>
            </div>
          </HrCard>

          <HrCard>
            <div className="hr-salary-components-head">
              <strong>Điều chỉnh thêm cho kỳ này</strong>
              <div>
                <button type="button" onClick={() => addAdj("earning")}><Plus className="h-3.5 w-3.5" /> Cộng</button>
                <button type="button" onClick={() => addAdj("deduction")}><Plus className="h-3.5 w-3.5" /> Trừ</button>
              </div>
            </div>
            {adjustments.length === 0 && <p className="hr-salary-empty">Không có điều chỉnh.</p>}
            {adjustments.map((c, i) => (
              <div key={i} className="hr-salary-comp-row" data-kind={c.kind}>
                <Input value={c.label} placeholder={c.kind === "earning" ? "Tên khoản cộng" : "Tên khoản trừ"} onChange={(e) => updateAdj(i, { label: e.target.value })} />
                <MoneyInput value={c.amount} onChange={(raw) => updateAdj(i, { amount: Number(raw) || 0 })} />
                <span className="hr-salary-comp-tag">{c.kind === "earning" ? "Cộng" : "Trừ"}</span>
                <button type="button" className="hr-icon-btn" onClick={() => removeAdj(i)} aria-label="Xóa"><Trash2 className="h-4 w-4" /></button>
              </div>
            ))}
          </HrCard>

          <HrCard className="hr-sync-card">
            <label className="hr-publish-check">
              <input type="checkbox" checked={published} onChange={(e) => setPublished(e.target.checked)} /> Phát hành cho nhân viên
            </label>
            <HrButton onClick={create} disabled={saving}><Banknote className="h-4 w-4" /> Lập phiếu lương</HrButton>
          </HrCard>
        </>
      ) : <HrCard><HrEmpty text="Không tính được lương." /></HrCard>}
    </>
  );
}

function PaylineRow({ label, amount, minus, total }: { label: string; amount: number; minus?: boolean; total?: boolean }) {
  return (
    <div className="hr-payline" data-total={total}>
      <span>{label}</span>
      <b>{minus && amount > 0 ? "−" : ""}{moneyVnd(amount)}</b>
    </div>
  );
}

export function HRAttendancePage() {
  const { notify } = useAppNotifications();
  const [pending, setPending] = useState(0);
  const [online, setOnline] = useState(navigator.onLine);
  const [syncing, setSyncing] = useState(false);
  const [now, setNow] = useState(() => new Date());

  useEffect(() => {
    void getOfflineCount().then(setPending);
    const unsub = subscribeOfflineCount(setPending);
    const on = () => setOnline(true);
    const off = () => setOnline(false);
    window.addEventListener("online", on);
    window.addEventListener("offline", off);
    return () => {
      unsub();
      window.removeEventListener("online", on);
      window.removeEventListener("offline", off);
    };
  }, []);

  useEffect(() => {
    const id = window.setInterval(() => setNow(new Date()), 10000);
    return () => window.clearInterval(id);
  }, []);

  const syncNow = async () => {
    setSyncing(true);
    try {
      const result = await syncOfflineAttendance();
      notify.success(result.synced > 0 ? `Đã đồng bộ ${result.synced} lượt.` : "Không còn lượt chờ đồng bộ.");
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không đồng bộ được.");
    } finally {
      setSyncing(false);
    }
  };

  const timeText = now.toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit" });
  const dateText = now.toLocaleDateString("vi-VN", { weekday: "long", day: "2-digit", month: "2-digit", year: "numeric" });

  return (
    <HrPage eyebrow="Camera" title="Chấm công" className="hr-page--attendance">
      <div className="hr-attendance-console">
        <header className="hr-attendance-console-head">
          <div className="hr-attendance-titleblock">
            <span><ScanFace className="h-4 w-4" /> Máy chấm công</span>
            <strong>Nhận diện khuôn mặt</strong>
          </div>
          <div className="hr-attendance-clock">
            <strong>{timeText}</strong>
            <span>{dateText}</span>
          </div>
        </header>

        <div className="hr-attendance-statusbar">
          <span className="hr-attendance-chip" data-tone={online ? "success" : "danger"}>
            {online ? <CheckCircle2 className="h-4 w-4" /> : <WifiOff className="h-4 w-4" />}
            {online ? "Trực tuyến" : "Ngoại tuyến"}
          </span>
          <span className="hr-attendance-chip" data-tone={pending > 0 ? "warning" : "neutral"}>
            <CloudOff className="h-4 w-4" />
            {pending > 0 ? `${pending} lượt chờ đồng bộ` : "Dữ liệu đã đồng bộ"}
          </span>
          {pending > 0 && (
            <button className="hr-attendance-sync-btn" type="button" onClick={syncNow} disabled={!online || syncing}>
              <RefreshCw className={`h-4 w-4 ${syncing ? "animate-spin" : ""}`} />
              {online ? "Đồng bộ ngay" : "Chờ mạng"}
            </button>
          )}
        </div>

        <div className="hr-scanner-shell">
          {/* selfOnly: nhân viên chỉ được chấm công bằng khuôn mặt của CHÍNH mình (chặn chấm hộ). */}
          <CheckInScanner selfOnly />
        </div>
      </div>
    </HrPage>
  );
}

export function HRManagerPage() {
  const [tab, setTab] = useState<"employees" | "departments" | "shifts" | "assignments">("employees");
  return (
    <HrPage eyebrow="Quản trị" title="Quản lý nhân sự">
      <div className="hr-tabs">
        {[
          ["employees", "Nhân viên"],
          ["departments", "Phòng ban"],
          ["shifts", "Ca làm"],
          ["assignments", "Phân ca"],
        ].map(([key, label]) => <button key={key} type="button" data-active={tab === key} onClick={() => setTab(key as typeof tab)}>{label}</button>)}
      </div>
      {tab === "employees" && <HREmployeesAdmin />}
      {tab === "departments" && <HRDepartmentsAdmin />}
      {tab === "shifts" && <HRShiftsAdmin />}
      {tab === "assignments" && <HRAssignmentsAdmin />}
    </HrPage>
  );
}

function HREmployeesAdmin() {
  const { data, loading } = useApi<EmployeeCard[]>("/api/hr/employees");
  return (
    <HrCard>
      {loading && !data ? <HrEmpty text="Đang tải nhân viên..." /> : (data?.length ? data.map((emp) => (
        <article key={emp.id} className="hr-admin-row">
          <span>{initials(emp.fullName)}</span>
          <div>
            <strong>{emp.fullName}</strong>
            <small>{emp.employeeCode} · {emp.position || "Nhân viên"} · {emp.departmentName || "Chưa có phòng ban"}</small>
          </div>
          <HrStatus status={emp.status === "Active" ? "success" : "muted"}>{emp.status === "Active" ? "Đang làm" : emp.status}</HrStatus>
        </article>
      )) : <HrEmpty text="Chưa có nhân viên." />)}
    </HrCard>
  );
}

function HRDepartmentsAdmin() {
  const { data, loading } = useApi<Department[]>("/api/hr/departments");
  return (
    <HrCard>
      {loading && !data ? <HrEmpty text="Đang tải phòng ban..." /> : (data?.length ? data.map((dep) => (
        <ListRow key={dep.id} title={dep.name} meta={`${dep.code || "--"} · Trưởng phòng: ${dep.managerName || "--"}`} status={`${dep.employeeCount} người`} />
      )) : <HrEmpty text="Chưa có phòng ban." />)}
    </HrCard>
  );
}

function HRShiftsAdmin() {
  const { data, loading } = useApi<Shift[]>("/api/shifts");
  return (
    <HrCard>
      {loading && !data ? <HrEmpty text="Đang tải ca làm..." /> : (data?.length ? data.map((shift) => (
        <ListRow key={shift.id} title={shift.name} meta={`${shift.startTime} - ${shift.endTime} · Nghỉ ${shift.breakMinutes} phút`} status={`${shift.standardHours}h`} />
      )) : <HrEmpty text="Chưa có ca làm." />)}
    </HrCard>
  );
}

function HRAssignmentsAdmin() {
  const today = new Date();
  const first = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, "0")}-01`;
  const last = new Date(today.getFullYear(), today.getMonth() + 1, 0).toISOString().slice(0, 10);
  const [range, setRange] = useState({ from: first, to: last });
  const { data, loading } = useApi<ShiftAssignment[]>(`/api/shifts/assignments?from=${range.from}&to=${range.to}`, [range.from, range.to]);
  return (
    <HrCard>
      <div className="hr-range-row">
        <Input type="date" value={range.from} onChange={(e) => setRange((s) => ({ ...s, from: e.target.value }))} />
        <Input type="date" value={range.to} onChange={(e) => setRange((s) => ({ ...s, to: e.target.value }))} />
      </div>
      {loading && !data ? <HrEmpty text="Đang tải phân ca..." /> : (data?.length ? data.map((item) => (
        <ListRow key={item.id} title={`${item.employeeName} · ${date(item.workDate)}`} meta={`${item.shiftName} · ${item.startTime}-${item.endTime}`} status={item.employeeCode} />
      )) : <HrEmpty text="Chưa có phân ca trong khoảng này." />)}
    </HrCard>
  );
}

export function HRAttendanceAdminPage() {
  const { data: status, loading, reload } = useApi<RtspAttendanceStatus>("/api/chamcong/rtsp/status");
  const { data: faces } = useApi<FaceNguoiDung[]>("/api/chamcong/dadangky");
  const { data: logs } = useApi<ChamCongLog[]>("/api/chamcong/log");
  return (
    <HrPage eyebrow="Quản trị" title="Quản lý chấm công" action={<HrButton tone="secondary" onClick={() => reload()}><RefreshCw className={`h-4 w-4 ${loading ? "animate-spin" : ""}`} /> Làm mới</HrButton>}>
      <div className="hr-grid-3">
        <HrStat label="Camera" value={status?.cameraConnected ? "Đã kết nối" : "Chưa kết nối"} hint={status?.mode || "RTSP"} tone={status?.cameraConnected ? "success" : "warning"} />
        <HrStat label="Mẫu khuôn mặt" value={`${status?.enrolledTemplates ?? faces?.reduce((sum, f) => sum + f.soMau, 0) ?? 0}`} hint={`${faces?.length ?? 0} nhân viên`} />
        <HrStat label="Scan gần nhất" value={status?.lastMatchedName || status?.lastMatchedUser || "--"} hint={status?.lastMessage || "Chưa có dữ liệu"} />
      </div>
      <HrCard>
        <div className="hr-card-head"><h2>Dữ liệu khuôn mặt</h2></div>
        {faces?.length ? faces.map((face) => <ListRow key={face.username} title={face.fullName || face.username} meta={face.username} status={`${face.soMau} mẫu`} />) : <HrEmpty text="Chưa có nhân viên đăng ký khuôn mặt." />}
      </HrCard>
      <HrCard>
        <div className="hr-card-head"><h2>Nhật ký chấm công</h2></div>
        {logs?.slice(0, 30).map((log) => <ListRow key={log.id} title={log.fullName || log.username} meta={`${log.loai} · ${dateTime(log.occurredAt)}`} status={`${(log.similarity * 100).toFixed(1)}%`} />) ?? <HrEmpty text="Chưa có nhật ký." />}
      </HrCard>
    </HrPage>
  );
}
