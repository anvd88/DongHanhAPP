import { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  BriefcaseBusiness,
  CalendarDays,
  ChevronLeft,
  ChevronRight,
  Clock3,
  Inbox,
  UserCheck,
  UserRoundX,
  UsersRound,
} from "lucide-react";
import { GlassCard } from "../components/Glass";
import { DatePicker } from "../components/DateField";
import { PageHeader } from "../components/Layout";
import { Table } from "../components/Table";
import { useApi } from "../lib/useApi";
import "./executive-dashboard.css";

interface ManagerHeadcount {
  active: number;
  present: number;
  leave: number;
  business: number;
  absent: number;
  late: number;
  overtime: number;
  unassigned: number;
  pendingApprovals: number;
  expiringContracts: number;
  alerts: number;
}

interface ManagerDepartment {
  departmentId: string | null;
  departmentName: string;
  total: number;
  present: number;
  leave: number;
  business: number;
  absent: number;
}

interface ManagerSummary {
  date: string;
  month: string;
  headcount: ManagerHeadcount;
  departments: ManagerDepartment[];
}

interface ManagerAttendance {
  employeeId: string;
  employeeCode: string;
  employeeName: string;
  position: string;
  departmentName: string;
  status: string;
  statusLabel: string;
  shiftName: string;
  checkIn: string;
  checkOut: string;
  lateMinutes: number;
  overtimeMinutes: number;
  requestNo: string;
  requestTitle: string;
}

type AttendanceStatus = "all" | "present" | "late" | "leave" | "business" | "absent" | "unassigned";

function localDateKey(date = new Date()) {
  const offset = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - offset).toISOString().slice(0, 10);
}

function shiftDay(value: string, offset: number) {
  const date = new Date(`${value}T12:00:00`);
  date.setDate(date.getDate() + offset);
  return localDateKey(date);
}

function dayLabel(value: string) {
  return new Intl.DateTimeFormat("vi-VN", {
    weekday: "long",
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  }).format(new Date(`${value}T12:00:00`));
}

function minuteLabel(value: number) {
  if (!value) return "—";
  const hours = Math.floor(value / 60);
  const minutes = value % 60;
  return hours ? `${hours}h${minutes ? String(minutes).padStart(2, "0") : ""}` : `${minutes} phút`;
}

export function ExecutiveDashboard() {
  const navigate = useNavigate();
  const [date, setDate] = useState(localDateKey);
  const [status, setStatus] = useState<AttendanceStatus>("all");
  const [departmentId, setDepartmentId] = useState("");

  const { data: summary, loading: summaryLoading, error: summaryError } =
    useApi<ManagerSummary>(`/api/hr/manager/summary?date=${date}&month=${date.slice(0, 7)}`);
  const attendancePath = useMemo(() => {
    const query = new URLSearchParams({ date });
    if (status !== "all") query.set("status", status);
    if (departmentId) query.set("departmentId", departmentId);
    return `/api/hr/manager/attendance?${query.toString()}`;
  }, [date, departmentId, status]);
  const { data: attendance, loading: attendanceLoading, error: attendanceError } =
    useApi<ManagerAttendance[]>(attendancePath);

  const headcount = summary?.headcount;
  const metrics: Array<{
    status: AttendanceStatus;
    label: string;
    value: number;
    hint: string;
    icon: typeof UsersRound;
    tone: string;
  }> = [
    { status: "all", label: "Quân số", value: headcount?.active ?? 0, hint: `${headcount?.unassigned ?? 0} chưa phân ca`, icon: UsersRound, tone: "blue" },
    { status: "present", label: "Có mặt", value: headcount?.present ?? 0, hint: `${headcount?.overtime ?? 0} có tăng ca`, icon: UserCheck, tone: "green" },
    { status: "late", label: "Đi muộn", value: headcount?.late ?? 0, hint: "Cần theo dõi", icon: Clock3, tone: "amber" },
    { status: "leave", label: "Nghỉ phép", value: headcount?.leave ?? 0, hint: "Đơn đã duyệt", icon: CalendarDays, tone: "violet" },
    { status: "business", label: "Công tác", value: headcount?.business ?? 0, hint: "Ngoài văn phòng", icon: BriefcaseBusiness, tone: "cyan" },
    { status: "absent", label: "Vắng", value: headcount?.absent ?? 0, hint: "Có ca nhưng chưa vào", icon: UserRoundX, tone: "red" },
  ];

  return (
    <div className="executive-dashboard gc-root">
      <PageHeader
        title="Dashboard điều hành"
        subtitle={`Tình hình nhân sự ${dayLabel(date)}`}
      />

      <GlassCard className="ed-toolbar" glow={false}>
        <div className="ed-date-nav">
          <button type="button" onClick={() => setDate((value) => shiftDay(value, -1))} aria-label="Ngày trước">
            <ChevronLeft className="h-5 w-5" />
          </button>
          <div className="ed-date-field">
            <DatePicker
              value={date}
              onChange={(next) => setDate(next || localDateKey())}
              max={localDateKey()}
              ariaLabel="Chọn ngày xem"
            />
          </div>
          <button
            type="button"
            onClick={() => setDate((value) => shiftDay(value, 1))}
            disabled={date >= localDateKey()}
            aria-label="Ngày sau"
          >
            <ChevronRight className="h-5 w-5" />
          </button>
        </div>
        <button className="ed-today" type="button" onClick={() => setDate(localDateKey())}>Hôm nay</button>
      </GlassCard>

      {summaryError && <GlassCard className="ed-error" glow={false}>{summaryError}</GlassCard>}

      <section className="ed-metric-grid" aria-label="Tổng quan nhân sự">
        {metrics.map((metric) => {
          const Icon = metric.icon;
          return (
            <button
              type="button"
              key={metric.status}
              className="ed-metric"
              data-tone={metric.tone}
              data-active={status === metric.status}
              onClick={() => setStatus(metric.status)}
            >
              <span className="ed-metric-icon"><Icon className="h-5 w-5" /></span>
              <span>{metric.label}</span>
              <strong>{summaryLoading ? "…" : metric.value}</strong>
              <small>{metric.hint}</small>
            </button>
          );
        })}
        <button type="button" className="ed-metric" data-tone="indigo" onClick={() => navigate("/pheduyet")}>
          <span className="ed-metric-icon"><Inbox className="h-5 w-5" /></span>
          <span>Đơn chờ duyệt</span>
          <strong>{summaryLoading ? "…" : headcount?.pendingApprovals ?? 0}</strong>
          <small>Mở hàng đợi phê duyệt</small>
        </button>
      </section>

      <GlassCard className="ed-departments" glow={false}>
        <div className="ed-section-heading">
          <div>
            <h2>Phạm vi theo dõi</h2>
            <p>Chọn phòng ban để lọc danh sách bên dưới.</p>
          </div>
          <span>{attendance?.length ?? 0} nhân viên</span>
        </div>
        <div className="ed-chip-list">
          <button type="button" data-active={!departmentId} onClick={() => setDepartmentId("")}>Toàn công ty</button>
          {summary?.departments.map((department) => (
            <button
              type="button"
              key={department.departmentId ?? department.departmentName}
              data-active={departmentId === (department.departmentId ?? "")}
              onClick={() => setDepartmentId(department.departmentId ?? "")}
            >
              {department.departmentName}
              <span>{department.present}/{department.total}</span>
            </button>
          ))}
        </div>
      </GlassCard>

      <GlassCard className="ed-attendance" glow={false}>
        <div className="ed-section-heading">
          <div>
            <h2>Chi tiết chấm công</h2>
            <p>Dữ liệu theo phạm vi quyền của tài khoản đang đăng nhập.</p>
          </div>
          {status !== "all" && <button type="button" onClick={() => setStatus("all")}>Bỏ lọc</button>}
        </div>
        {attendanceError ? (
          <div className="ed-error">{attendanceError}</div>
        ) : (
          <Table<ManagerAttendance>
            loading={attendanceLoading}
            rows={attendance ?? []}
            keyOf={(row) => row.employeeId}
            empty="Không có nhân viên trong bộ lọc này."
            columns={[
              {
                header: "Nhân viên",
                cell: (row) => <div className="ed-person"><strong>{row.employeeName}</strong><small>{row.employeeCode} · {row.position || "Nhân viên"}</small></div>,
              },
              { header: "Phòng ban", cell: (row) => row.departmentName || "—" },
              { header: "Ca làm", cell: (row) => row.shiftName || "Chưa phân ca" },
              { header: "Vào / Ra", cell: (row) => <span className="ed-time">{row.checkIn || "--:--"} / {row.checkOut || "--:--"}</span> },
              { header: "Đi muộn", cell: (row) => minuteLabel(row.lateMinutes), align: "right" },
              { header: "Tăng ca", cell: (row) => minuteLabel(row.overtimeMinutes), align: "right" },
              {
                header: "Trạng thái",
                cell: (row) => <span className="ed-status" data-status={row.status}>{row.statusLabel || row.status}</span>,
                align: "center",
              },
            ]}
          />
        )}
      </GlassCard>
    </div>
  );
}
