import { useEffect, useRef, useState, type ReactNode } from "react";
import { NavLink, useLocation } from "react-router-dom";
import { Sidebar } from "./Sidebar";
import { Header } from "./Header";
import { QuickToolsDrawer } from "./QuickToolsDrawer";
import { HRLayout } from "./hr/HRLayout";
import { NAV } from "./nav";
import { useAuth } from "../lib/auth";
import { isAdmin } from "../lib/types";
import { IS_HR_APK } from "../lib/appConfig";
import { useChatNotifications } from "./ChatNotifications";

const MOBILE_NAV_KEYS = new Set(["dashboard", "giacong", "ketoan", "khachhang", "chats", "chamcong"]);
const HR_APK_BOTTOM_NAV_KEYS = new Set(["nhan-su-portal", "chamcong", "bangcong", "dontu", "pheduyet"]);

export function Layout({
  children,
  suppressMainWebSystem = false,
}: {
  children: ReactNode;
  suppressMainWebSystem?: boolean;
}) {
  if (IS_HR_APK) return <HRLayout>{children}</HRLayout>;
  return <ClassicLayout suppressMainWebSystem={suppressMainWebSystem}>{children}</ClassicLayout>;
}

function ClassicLayout({
  children,
  suppressMainWebSystem,
}: {
  children: ReactNode;
  suppressMainWebSystem: boolean;
}) {
  const [mobileOpen, setMobileOpen] = useState(false);
  const railRef = useRef<HTMLDivElement | null>(null);
  const location = useLocation();

  // Khi KHÔNG nhập liệu: nhấn Tab để bung sidebar (thu gọn) ra và đưa tiêu điểm
  // vào mục menu đầu tiên — sidebar tự mở nhờ :focus-within, tab tiếp để duyệt menu.
  useEffect(() => {
    const isEditable = (el: Element | null) => {
      if (!el) return false;
      const node = el as HTMLElement;
      const tag = node.tagName;
      return (
        tag === "INPUT" ||
        tag === "TEXTAREA" ||
        tag === "SELECT" ||
        node.isContentEditable ||
        node.getAttribute("role") === "textbox"
      );
    };

    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key !== "Tab" || e.shiftKey || e.altKey || e.ctrlKey || e.metaKey) return;
      const rail = railRef.current;
      // Chỉ khi rail desktop đang hiển thị (lg) và người dùng không đang gõ
      if (!rail || rail.offsetParent === null) return;
      if (isEditable(document.activeElement)) return;
      // Nếu tiêu điểm đã ở trong sidebar thì để Tab hoạt động bình thường
      if (rail.contains(document.activeElement)) return;
      const firstLink = rail.querySelector<HTMLElement>("a[href], button");
      if (!firstLink) return;
      e.preventDefault();
      firstLink.focus();
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);
  const { user } = useAuth();
  const { unreadCount } = useChatNotifications();
  const admin = isAdmin(user);
  const bottomNavKeys = IS_HR_APK ? HR_APK_BOTTOM_NAV_KEYS : MOBILE_NAV_KEYS;
  const mobileNavItems = NAV.flatMap((section) => section.items)
    .filter((item) => bottomNavKeys.has(item.key) && (!item.adminOnly || admin));

  return (
    <div className="km-app-shell">
      {/* Sidebar desktop */}
      <div ref={railRef} className="km-sidebar-rail hidden lg:block">
        <Sidebar />
      </div>

      {/* Sidebar mobile */}
      {mobileOpen && (
        <div className="fixed inset-0 z-40 lg:hidden">
          <div className="absolute inset-0 bg-black/40" onClick={() => setMobileOpen(false)} />
          <div className="relative h-full w-[260px]">
            <Sidebar onNavigate={() => setMobileOpen(false)} />
          </div>
        </div>
      )}

      <div className="km-main-shell">
        <Header onMenu={() => setMobileOpen(true)} />
        <main key={location.pathname} className="km-page scroll-thin">
          {children}
        </main>
      </div>
      <nav
        className="km-mobile-bottom-nav lg:hidden"
        aria-label="Điều hướng chính"
        style={{ gridTemplateColumns: `repeat(${Math.max(1, mobileNavItems.length)}, minmax(0, 1fr))` }}
      >
        {mobileNavItems.map((item) => {
          const Icon = item.icon;
          return (
            <NavLink
              key={item.key}
              to={item.path}
              className={({ isActive }) => `km-mobile-bottom-link ${isActive ? "is-active" : ""}`}
            >
              <Icon className="h-5 w-5" aria-hidden="true" />
              {item.key === "chats" && unreadCount > 0 && (
                <span className="km-notification-badge km-notification-badge--mobile">
                  {unreadCount > 99 ? "99+" : unreadCount}
                </span>
              )}
              <span>{item.label}</span>
            </NavLink>
          );
        })}
      </nav>
      {!suppressMainWebSystem && <QuickToolsDrawer />}
    </div>
  );
}

/** Tiêu đề trang dùng chung. */
export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: string; actions?: ReactNode }) {
  return (
    <div className="km-page-header">
      <div>
        <h1>{title}</h1>
        {subtitle && <p>{subtitle}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center justify-end gap-2">{actions}</div>}
    </div>
  );
}
