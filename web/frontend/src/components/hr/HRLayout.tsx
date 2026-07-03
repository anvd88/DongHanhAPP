import { useMemo, useState, type ReactNode } from "react";
import { NavLink, useLocation } from "react-router-dom";
import {
  CalendarClock,
  FileText,
  Gavel,
  IdCard,
  Inbox,
  LogOut,
  Menu,
  ScanFace,
  Settings,
  UserCog,
  UserRound,
  Wallet,
  X,
} from "lucide-react";
import { useAuth } from "../../lib/auth";
import { initials } from "../../lib/format";
import { isAdmin } from "../../lib/types";
import "./hr-app.css";

const HR_NAV = [
  { key: "home", label: "Nhân sự", path: "/nhan-su", icon: IdCard },
  { key: "scan", label: "Chấm công", path: "/chamcong", icon: ScanFace },
  { key: "timesheet", label: "Bảng công", path: "/bangcong", icon: CalendarClock },
  { key: "requests", label: "Đơn từ", path: "/dontu", icon: FileText },
  { key: "approval", label: "Phê duyệt", path: "/pheduyet", icon: Inbox },
  { key: "profile", label: "Hồ sơ", path: "/hoso", icon: UserRound },
  { key: "penalty", label: "Phạt", path: "/phat", icon: Gavel },
  { key: "payroll", label: "Bảng lương", path: "/bang-luong", icon: Wallet, adminOnly: true },
  { key: "people", label: "Quản lý", path: "/quanly-nhansu", icon: UserCog, adminOnly: true },
  { key: "settings", label: "Cài đặt", path: "/caidat", icon: Settings },
];

function activeTitle(pathname: string) {
  return HR_NAV.find((item) => pathname === item.path || pathname.startsWith(`${item.path}/`))?.label ?? "Nhân sự";
}

export function HRLayout({ children }: { children: ReactNode }) {
  const { user, logout } = useAuth();
  const location = useLocation();
  const [menuOpen, setMenuOpen] = useState(false);
  const admin = isAdmin(user);
  const items = useMemo(() => HR_NAV.filter((item) => !item.adminOnly || admin), [admin]);
  const displayName = user?.fullName || user?.username || "Nhân viên";
  const attendanceMode = location.pathname === "/chamcong";

  return (
    <div className="hr-app-shell">
      <header className="hr-app-topbar">
        <button type="button" className="hr-icon-button" onClick={() => setMenuOpen(true)} aria-label="Mở menu">
          <Menu className="h-5 w-5" />
        </button>
        <div className="hr-app-title">
          <span>Cổng nhân sự</span>
          <strong>{activeTitle(location.pathname)}</strong>
        </div>
        <div className="hr-top-avatar">{initials(displayName)}</div>
      </header>

      <main className={`hr-app-main scroll-thin${attendanceMode ? " hr-app-main--attendance" : ""}`}>{children}</main>

      <nav className="hr-bottom-nav" aria-label="Điều hướng nhân sự">
        {items.slice(0, 5).map((item) => {
          const Icon = item.icon;
          return (
            <NavLink key={item.key} to={item.path} className={({ isActive }) => `hr-bottom-link ${isActive ? "is-active" : ""}`}>
              <Icon className="h-5 w-5" aria-hidden="true" />
              <span>{item.label}</span>
            </NavLink>
          );
        })}
      </nav>

      {menuOpen && (
        <div className="hr-drawer-layer">
          <button type="button" className="hr-drawer-backdrop" onClick={() => setMenuOpen(false)} aria-label="Đóng menu" />
          <aside className="hr-drawer">
            <div className="hr-drawer-head">
              <div className="hr-profile-line">
                <span className="hr-drawer-avatar">{initials(displayName)}</span>
                <div>
                  <strong>{displayName}</strong>
                  <span>{admin ? "Quản trị nhân sự" : "Nhân viên"}</span>
                </div>
              </div>
              <button type="button" className="hr-icon-button" onClick={() => setMenuOpen(false)} aria-label="Đóng menu">
                <X className="h-5 w-5" />
              </button>
            </div>
            <div className="hr-drawer-links">
              {items.map((item) => {
                const Icon = item.icon;
                return (
                  <NavLink
                    key={item.key}
                    to={item.path}
                    onClick={() => setMenuOpen(false)}
                    className={({ isActive }) => `hr-drawer-link ${isActive ? "is-active" : ""}`}
                  >
                    <Icon className="h-5 w-5" />
                    <span>{item.label}</span>
                  </NavLink>
                );
              })}
            </div>
            <button type="button" className="hr-logout" onClick={logout}>
              <LogOut className="h-5 w-5" />
              Đăng xuất
            </button>
          </aside>
        </div>
      )}
    </div>
  );
}
