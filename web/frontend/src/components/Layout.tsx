import { Suspense, useEffect, useRef, useState, type ReactNode } from "react";
import { NavLink, useLocation } from "react-router-dom";
import { Sidebar } from "./Sidebar";
import { Header } from "./Header";
import { PageSkeleton } from "./PageSkeleton";
import { AppErrorBoundary } from "./AppErrorBoundary";
import { QuickToolsDrawer } from "./QuickToolsDrawer";
import { NAV } from "./nav";
import { useAuth } from "../lib/auth";
import { useWorkArea } from "../lib/workArea";
import { useChatNotifications } from "./chat-notifications-context";
import { RailSlotProvider, RailSwitch, useRailContent } from "./rail-slot";

// Thanh điều hướng dưới cùng (mobile) khác nhau theo không gian: người làm nghiệp vụ cần lối tắt tới
// chứng từ/khách hàng, còn nhân viên cần lối tắt tới việc của mình.
const MOBILE_NAV_KEYS: Record<"admin" | "work", string[]> = {
  admin: ["dashboard", "giacong", "ketoan", "khachhang", "chats"],
  work: ["nhan-su-portal", "cong-viec", "chamcong", "dontu", "chats"],
};

export function Layout({
  children,
  suppressMainWebSystem = false,
}: {
  children: ReactNode;
  suppressMainWebSystem?: boolean;
}) {
  return (
    <RailSlotProvider>
      <ClassicLayout suppressMainWebSystem={suppressMainWebSystem}>{children}</ClassicLayout>
    </RailSlotProvider>
  );
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
  const { unreadCount } = useChatNotifications();
  // Trang có thể mượn thanh bên trái (xem rail-slot). Không mượn thì vẫn là menu như cũ.
  const railOverride = useRailContent();
  const { loginTransitionPhase } = useAuth();
  const { area, can } = useWorkArea();
  const wanted = MOBILE_NAV_KEYS[area];
  const mobileNavItems = NAV.flatMap((section) => section.items)
    .filter((item) =>
      wanted.includes(item.key) &&
      (!item.permission || can(item.permission)) &&
      (!item.permissionsAny || item.permissionsAny.some(can))
    )
    .sort((a, b) => wanted.indexOf(a.key) - wanted.indexOf(b.key));

  return (
    // data-area cho phép CSS phân biệt khu quản trị và không gian làm việc mà không cần dựng hai cây
    // component riêng (mọi khác biệt thật đã nằm ở menu + route, đều chốt bằng quyền).
    <div
      className="km-app-shell"
      data-area={area}
      inert={loginTransitionPhase ? true : undefined}
      aria-hidden={loginTransitionPhase ? true : undefined}
    >
      {/* Sidebar desktop */}
      <div ref={railRef} className="km-sidebar-rail hidden lg:block" data-rail={railOverride ? "stack" : "menu"}>
        <RailSwitch menu={<Sidebar />} />
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
        <main key={pageKey(location.pathname)} className="km-page scroll-thin">
          {/* Ranh giới Suspense NẰM TRONG vỏ app: chuyển sang trang lazy chưa tải thì chỉ vùng này
              hiện skeleton, còn sidebar + header đứng yên (không nháy cả màn hình như trước). */}
          {/* Ranh giới lỗi RIÊNG cho vùng nội dung: trang lỗi thì chỉ vùng này báo lỗi, sidebar và
              header vẫn còn để người dùng đi chỗ khác. `key` của <main> đổi theo trang nên ranh giới
              tự khởi động lại khi chuyển trang. */}
          <AppErrorBoundary label={`page:${location.pathname}`}>
            <Suspense fallback={<PageSkeleton />}>
              {children}
              <span hidden data-login-route-ready={location.pathname} />
            </Suspense>
          </AppErrorBoundary>
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

/**
 * Khoá của vùng nội dung: mỗi TRANG là một cây riêng nên chuyển trang thì cuộn/hiệu ứng reset sạch.
 *
 * Ngoại lệ các đường dẫn có tham số: ở đó đổi tham số là ĐỔI LỰA CHỌN chứ không phải đổi trang.
 * Gắn key theo đường dẫn đầy đủ sẽ dựng lại cả trang mỗi lần bấm — màn hình giật một nhịp, và thanh
 * bên do trang mượn cũng bị gỡ rồi gắn lại nên menu nháy lên giữa chừng.
 */
function pageKey(pathname: string) {
  const detail = pathname.match(/^(\/ban-hang)\/[^/]+$/);
  return detail ? `${detail[1]}/:id` : pathname;
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
