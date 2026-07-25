import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  Bell,
  CalendarDays,
  ChevronDown,
  Clock3,
  KeyRound,
  LogOut,
  Menu,
  Moon,
  Search,
  Sun,
  UserCog,
} from "lucide-react";
import { useAuth } from "../lib/auth";
import { useTheme } from "../lib/theme-context";
import { initials } from "../lib/format";
import { isAdmin } from "../lib/types";
import { APP_BRAND_NAME } from "../lib/branding";
import { VerifiedBadge } from "./VerifiedBadge";
import { EditProfileModal, ChangePasswordModal } from "./AccountModals";
import { useChatNotifications } from "./chat-notifications-context";

export function Header({ onMenu }: { onMenu: () => void }) {
  const { user, logout } = useAuth();
  const { theme, toggle } = useTheme();
  const navigate = useNavigate();
  const { unreadCount } = useChatNotifications();
  const [menuOpen, setMenuOpen] = useState(false);
  const [shiftOpen, setShiftOpen] = useState(false);
  const [modal, setModal] = useState<null | "profile" | "password">(null);
  const searchRef = useRef<HTMLInputElement>(null);
  const now = new Date();

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        searchRef.current?.focus();
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  return (
    <header className="km-header">
      <button
        onClick={onMenu}
        className="km-icon-button lg:hidden"
        aria-label="Mở menu"
        type="button"
      >
        <Menu className="h-5 w-5" />
      </button>

      <div className="km-header-company">
        <div className="km-brand-title text-sm font-bold text-[var(--text)]">{APP_BRAND_NAME}</div>
        <div className="mt-1 text-xs font-medium text-[var(--text-secondary)]">
          Hệ thống kế toán doanh nghiệp
        </div>
      </div>

      <label className="km-header-search">
        <Search className="h-5 w-5 text-[#6d82a6]" aria-hidden="true" />
        <input ref={searchRef} placeholder="Nhập để tìm kiếm..." aria-label="Nhập để tìm kiếm" />
        <kbd>Ctrl + K</kbd>
      </label>

      <div className="relative">
        <button
          type="button"
          className="km-shift-widget"
          onClick={() => setShiftOpen((value) => !value)}
          aria-expanded={shiftOpen}
        >
          <span className="flex items-center justify-between gap-4">
            <span className="text-xs font-semibold">Ca làm việc</span>
            <span className="inline-flex items-center gap-1.5 text-xs font-semibold text-emerald-300">
              <span className="h-1.5 w-1.5 rounded-full bg-emerald-300 shadow-[0_0_10px_rgba(52,211,153,.9)]" />
              Đang làm việc
            </span>
          </span>
          <span className="mt-1.5 flex items-center justify-between gap-4">
            <span className="inline-flex items-center gap-2 text-sm font-bold">
              <Clock3 className="h-4 w-4" />
              08:00 - 17:00
            </span>
            <ChevronDown className={`h-4 w-4 transition-transform ${shiftOpen ? "rotate-180" : ""}`} />
          </span>
        </button>
        {shiftOpen && (
          <>
            <div className="fixed inset-0 z-10" onClick={() => setShiftOpen(false)} />
            <div className="km-shift-menu">
              <div className="text-xs font-semibold text-[var(--text-muted)]">Lịch làm việc hôm nay</div>
              <div className="mt-2 rounded-2xl bg-white/20 p-3 text-sm font-semibold text-[var(--text)] dark:bg-white/[0.06]">
                08:00 - 12:00, 13:00 - 17:00
              </div>
            </div>
          </>
        )}
      </div>

      <div className="km-period-pill">
        <CalendarDays className="h-4 w-4" />
        {String(now.getMonth() + 1).padStart(2, "0")}/{now.getFullYear()}
      </div>

      <button onClick={toggle} className="km-theme-switch" type="button" aria-label="Đổi giao diện">
        <Sun className="h-4 w-4" />
        <span className="km-theme-knob" data-theme={theme} />
        <Moon className="h-4 w-4" />
      </button>

      <button
        className="km-icon-button relative"
        type="button"
        aria-label={unreadCount ? `${unreadCount} tin nh\u1eafn ch\u01b0a \u0111\u1ecdc` : "Th\u00f4ng b\u00e1o"}
        onClick={() => navigate("/chats")}
      >
        <Bell className="h-5 w-5" />
        {unreadCount > 0 && (
          <span className="km-notification-badge km-notification-badge--header">
            {unreadCount > 99 ? "99+" : unreadCount}
          </span>
        )}
      </button>

      <div className="relative">
        <button
          onClick={() => setMenuOpen((value) => !value)}
          className="km-avatar-button"
          type="button"
          aria-expanded={menuOpen}
        >
          <span className="km-avatar overflow-hidden">
            {user?.avatarUrl ? (
              <img src={user.avatarUrl} alt="" className="h-full w-full object-cover" />
            ) : (
              initials(user?.fullName || user?.username || "?")
            )}
          </span>
          <ChevronDown className="h-4 w-4" />
        </button>

        {menuOpen && (
          <>
            <div
              className="fixed inset-0 z-10"
              style={{ background: "rgba(8,12,20,0.45)", backdropFilter: "blur(4px)", WebkitBackdropFilter: "blur(4px)" }}
              onClick={() => setMenuOpen(false)}
            />
            <div className="profile-menu-popover glass glass-strong fade-in z-20 w-60 overflow-hidden rounded-2xl p-1.5">
              <div className="px-3 py-2 text-xs text-[var(--text-muted)]">
                <div className="inline-flex items-center gap-1 font-semibold text-[var(--text-secondary)]">
                  {user?.fullName || user?.username}
                  {user?.verified && <VerifiedBadge size={14} />}
                </div>
                <div className="mt-0.5">{isAdmin(user) ? "Quản trị viên" : "Nhân viên"}</div>
              </div>
              <button
                onClick={() => { setMenuOpen(false); setModal("profile"); }}
                className="flex w-full items-center gap-2 rounded-xl px-3 py-2.5 text-sm font-medium text-[var(--text-secondary)] transition-colors hover:bg-black/10 dark:hover:bg-white/[0.12]"
                type="button"
              >
                <UserCog className="h-4 w-4" /> Sửa hồ sơ
              </button>
              <button
                onClick={() => { setMenuOpen(false); setModal("password"); }}
                className="flex w-full items-center gap-2 rounded-xl px-3 py-2.5 text-sm font-medium text-[var(--text-secondary)] transition-colors hover:bg-black/10 dark:hover:bg-white/[0.12]"
                type="button"
              >
                <KeyRound className="h-4 w-4" /> Đổi mật khẩu
              </button>
              <div className="my-1 border-t border-[var(--glass-border)]" />
              <button
                onClick={logout}
                className="flex w-full items-center gap-2 rounded-xl px-3 py-2.5 text-sm font-medium text-[var(--danger)] transition-colors hover:bg-red-500/10"
                type="button"
              >
                <LogOut className="h-4 w-4" /> Đăng xuất
              </button>
            </div>
          </>
        )}
      </div>

      {modal === "profile" && <EditProfileModal onClose={() => setModal(null)} />}
      {modal === "password" && <ChangePasswordModal onClose={() => setModal(null)} />}
    </header>
  );
}
