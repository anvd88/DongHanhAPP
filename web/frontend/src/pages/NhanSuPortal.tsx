import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import {
  AlertCircle,
  Building2,
  CalendarClock,
  ChevronRight,
  CloudOff,
  CreditCard,
  FileText,
  Gavel,
  IdCard,
  Inbox,
  LayoutDashboard,
  ScanFace,
  UserCog,
  Wallet,
  WifiOff,
} from "lucide-react";
import { useAuth } from "../lib/auth";
import { IS_HR_APK } from "../lib/appConfig";
import { initials } from "../lib/format";
import { isAdmin } from "../lib/types";
import { useApi } from "../lib/useApi";
import {
  requestStatusLabel,
  type EmployeeDetail,
  type RequestListItem,
  type Timesheet,
  type TimesheetDay,
} from "../lib/hr";
import {
  getOfflineCount,
  subscribeOfflineCount,
  syncOfflineAttendance,
} from "../lib/offlineAttendance";
import { useAppNotifications } from "../components/AppNotifications";
import "./nhan-su-portal.css";

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

function statusTone(status?: string) {
  if (!status) return "muted";
  if (status === "Approved") return "success";
  if (status === "Rejected" || status === "Cancelled") return "danger";
  return "warning";
}

function attendanceTone(day?: TimesheetDay) {
  if (!day) return "muted";
  if (day.status.includes("muộn") || day.status.includes("sớm") || day.status.includes("Thiếu")) return "warning";
  if (day.status === "Vắng") return "danger";
  return "success";
}

export function NhanSuPortal() {
  const { user } = useAuth();
  const { notify } = useAppNotifications();
  const admin = isAdmin(user);
  const { data: me } = useApi<EmployeeDetail>("/api/hr/me");
  const { data: timesheet, loading: timesheetLoading } = useApi<Timesheet>(`/api/timesheet/me?month=${currentMonth()}`);
  const { data: requests } = useApi<RequestListItem[]>("/api/requests?scope=mine");
  const { data: approvals } = useApi<RequestListItem[]>(admin ? "/api/requests?scope=inbox" : null, [admin]);
  const [pendingOffline, setPendingOffline] = useState(0);
  const [online, setOnline] = useState(navigator.onLine);
  const [syncing, setSyncing] = useState(false);

  useEffect(() => {
    void getOfflineCount().then(setPendingOffline);
    const unsub = subscribeOfflineCount(setPendingOffline);
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

  const today = useMemo(
    () => timesheet?.days.find((day) => day.date.slice(0, 10) === todayKey()),
    [timesheet],
  );
  const latestRequest = requests?.[0];
  const inboxCount = approvals?.filter((item) => item.status === "Pending").length ?? 0;

  const syncNow = async () => {
    if (!online || syncing) return;
    setSyncing(true);
    try {
      const result = await syncOfflineAttendance();
      if (result.synced > 0) notify.success(`Đã đồng bộ ${result.synced} lượt chấm công.`);
      else notify.success("Không còn lượt chấm công chờ đồng bộ.");
    } catch (e) {
      notify.error(e instanceof Error ? e.message : "Không đồng bộ được chấm công.");
    } finally {
      setSyncing(false);
    }
  };

  const displayName = me?.fullName || user?.fullName || user?.username || "Nhân viên";
  const department = me?.departmentName || "Công ty";
  const role = me?.position || (admin ? "Quản trị nhân sự" : "Nhân viên");

  return (
    <div className="hr-mobile-portal">
      <header className="hrp-top">
        <div className="hrp-identity">
          <span className="hrp-avatar">
            {me?.avatar ? <img src={me.avatar} alt="" /> : initials(displayName)}
          </span>
          <div className="hrp-identity-copy">
            <p>{department}</p>
            <h1>{displayName}</h1>
            <span>{role}</span>
          </div>
        </div>
        <Link className="hrp-main-link" to={IS_HR_APK ? "/dashboard" : "/dashboard"} aria-label="Mở web chính">
          <LayoutDashboard className="h-4 w-4" />
          <span>Web chính</span>
        </Link>
      </header>

      <section className="hrp-attendance" aria-labelledby="hrp-attendance-title">
        <div>
          <p className="hrp-kicker">Chấm công hôm nay</p>
          <h2 id="hrp-attendance-title">
            {timesheetLoading ? "Đang tải dữ liệu..." : today?.status || "Chưa có lượt chấm công"}
          </h2>
          <div className="hrp-attendance-meta">
            <span>Vào: {today?.checkIn || "--:--"}</span>
            <span>Ra: {today?.checkOut || "--:--"}</span>
          </div>
        </div>
        <span className="hrp-status-dot" data-tone={attendanceTone(today)} />
        <Link className="hrp-primary-action" to="/chamcong">
          <ScanFace className="h-5 w-5" />
          Chấm công
        </Link>
      </section>

      {(pendingOffline > 0 || !online) && (
        <section className="hrp-sync" data-offline={!online}>
          <div>
            <strong>{online ? `${pendingOffline} lượt đang chờ đồng bộ` : "Đang ngoại tuyến"}</strong>
            <span>{online ? "Bấm đồng bộ để gửi lên backend." : "Dữ liệu sẽ tự gửi khi có mạng."}</span>
          </div>
          <button type="button" onClick={syncNow} disabled={!online || syncing || pendingOffline === 0}>
            {online ? <CloudOff className="h-4 w-4" /> : <WifiOff className="h-4 w-4" />}
            {syncing ? "Đang gửi" : "Đồng bộ"}
          </button>
        </section>
      )}

      <section className="hrp-summary-grid" aria-label="Tóm tắt nhân sự">
        <SummaryCard label="Ngày công" value={`${timesheet?.summary.workedDays ?? 0}`} hint={`Vắng ${timesheet?.summary.absentDays ?? 0} ngày`} />
        <SummaryCard label="Đi muộn" value={`${timesheet?.summary.lateDays ?? 0}`} hint={fmtMinutes(timesheet?.summary.totalLateMinutes)} />
        <SummaryCard label="Tăng ca" value={fmtMinutes(timesheet?.summary.totalOvertimeMinutes)} hint={`${timesheet?.summary.totalWorkedHours ?? 0} giờ làm`} />
      </section>

      <section className="hrp-section">
        <div className="hrp-section-head">
          <h2>Nhân sự</h2>
          <span>Các thao tác thường dùng</span>
        </div>
        <div className="hrp-action-list">
          <PortalAction icon={<IdCard />} title="Hồ sơ của tôi" hint="Thông tin cá nhân, hợp đồng, lương" to="/hoso" />
          <PortalAction icon={<CalendarClock />} title="Bảng công" hint="Đối chiếu công, đi muộn, tăng ca" to="/bangcong" />
          <PortalAction icon={<FileText />} title="Đơn từ" hint={latestRequest ? `Gần nhất: ${requestStatusLabel(latestRequest.status)}` : "Tạo và theo dõi đơn"} to="/dontu" tone={statusTone(latestRequest?.status)} />
          <PortalAction icon={<Inbox />} title="Phê duyệt" hint={admin ? `${inboxCount} đơn đang chờ` : "Các đơn cần bạn xử lý"} to="/pheduyet" tone={inboxCount > 0 ? "warning" : "muted"} />
          <PortalAction icon={<Gavel />} title="Phạt / kỷ luật" hint={admin ? "Lập & quản lý quyết định phạt" : "Xem các lần bị phạt"} to="/phat" />
          <PortalAction icon={<CreditCard />} title="Tài khoản ngân hàng" hint="Thẻ nhận lương của bạn" to="/tai-khoan-ngan-hang" />
          {admin && <PortalAction icon={<Wallet />} title="Bảng lương" hint="Mức lương & lập phiếu lương" to="/bang-luong" />}
          {admin && <PortalAction icon={<UserCog />} title="Quản lý nhân sự" hint="Nhân viên, phòng ban, ca làm" to="/quanly-nhansu" />}
        </div>
      </section>

      <section className="hrp-section hrp-company">
        <div>
          <Building2 className="h-5 w-5" />
          <div>
            <h2>Cổng nhân sự công ty</h2>
            <p>Dùng chung backend với web chính, dữ liệu được cập nhật theo thời gian thực.</p>
          </div>
        </div>
        <Link to="/ql-chamcong">
          Quản lý chấm công
          <ChevronRight className="h-4 w-4" />
        </Link>
      </section>
    </div>
  );
}

function SummaryCard({ label, value, hint }: { label: string; value: string; hint: string }) {
  return (
    <article className="hrp-summary-card">
      <span>{label}</span>
      <strong>{value}</strong>
      <small>{hint}</small>
    </article>
  );
}

function PortalAction({
  icon,
  title,
  hint,
  to,
  tone = "neutral",
}: {
  icon: React.ReactElement;
  title: string;
  hint: string;
  to: string;
  tone?: string;
}) {
  return (
    <Link className="hrp-action" to={to} data-tone={tone}>
      <span className="hrp-action-icon">
        {icon}
      </span>
      <span className="hrp-action-copy">
        <strong>{title}</strong>
        <small>{hint}</small>
      </span>
      {tone === "warning" && <AlertCircle className="hrp-action-alert h-4 w-4" />}
      {tone !== "warning" && <ChevronRight className="hrp-action-chevron h-4 w-4" />}
    </Link>
  );
}
