import { useState, type ReactNode } from "react";
import { NavLink, useLocation } from "react-router-dom";
import { Sidebar } from "./Sidebar";
import { Header } from "./Header";
import { QuickToolsDrawer } from "./QuickToolsDrawer";
import { NAV } from "./nav";
import { useAuth } from "../lib/auth";
import { isAdmin } from "../lib/types";
import { useChatNotifications } from "./ChatNotifications";

const MOBILE_NAV_KEYS = new Set(["dashboard", "giacong", "ketoan", "khachhang", "chats", "chamcong"]);

export function Layout({ children }: { children: ReactNode }) {
  const [mobileOpen, setMobileOpen] = useState(false);
  const location = useLocation();
  const { user } = useAuth();
  const { unreadCount } = useChatNotifications();
  const admin = isAdmin(user);
  const mobileNavItems = NAV.flatMap((section) => section.items)
    .filter((item) => MOBILE_NAV_KEYS.has(item.key) && (!item.adminOnly || admin));

  return (
    <div className="km-app-shell">
      {/* Sidebar desktop */}
      <div className="hidden lg:block">
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
      <nav className="km-mobile-bottom-nav lg:hidden" aria-label="Điều hướng chính">
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
      <QuickToolsDrawer />
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
