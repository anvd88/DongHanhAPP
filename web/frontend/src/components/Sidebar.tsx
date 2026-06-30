import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { ChevronDown, KeyRound, LogOut, UserCog } from "lucide-react";
import { NavLink, useLocation } from "react-router-dom";
import { NAV } from "./nav";
import { useAuth } from "../lib/auth";
import { isAdmin } from "../lib/types";
import { APP_BRAND_NAME } from "../lib/branding";
import { initials } from "../lib/format";
import { ChangePasswordModal, EditProfileModal } from "./AccountModals";
import { useChatNotifications } from "./ChatNotifications";

interface IndicatorState {
  x: number;
  y: number;
  w: number;
  h: number;
  vx: number;
  vy: number;
  vw: number;
  vh: number;
  targetX: number;
  targetY: number;
  targetW: number;
  targetH: number;
  initialized: boolean;
  moving: boolean;
  raf: number;
  lastTime: number;
}

const makeIndicatorState = (): IndicatorState => ({
  x: 0,
  y: 0,
  w: 0,
  h: 0,
  vx: 0,
  vy: 0,
  vw: 0,
  vh: 0,
  targetX: 0,
  targetY: 0,
  targetW: 0,
  targetH: 0,
  initialized: false,
  moving: false,
  raf: 0,
  lastTime: 0,
});

function isNavPathActive(pathname: string, path: string) {
  return pathname === path || pathname.startsWith(`${path}/`);
}

export function Sidebar({ onNavigate }: { onNavigate?: () => void }) {
  const { user, logout } = useAuth();
  const { unreadCount } = useChatNotifications();
  const location = useLocation();
  const admin = isAdmin(user);
  const [profileOpen, setProfileOpen] = useState(false);
  const [modal, setModal] = useState<null | "profile" | "password">(null);
  const navRef = useRef<HTMLElement | null>(null);
  const indicatorRef = useRef<HTMLDivElement | null>(null);
  const itemRefs = useRef(new Map<string, HTMLAnchorElement>());
  const indicatorState = useRef<IndicatorState>(makeIndicatorState());
  const animateIndicatorRef = useRef<(time: number) => void>(() => {});
  const measureFrame = useRef(0);

  const visibleSections = useMemo(
    () =>
      NAV.map((section) => ({
        ...section,
        items: section.items.filter((it) => !it.adminOnly || admin),
      })).filter((section) => section.items.length),
    [admin],
  );

  const activeKey = useMemo(() => {
    for (const section of visibleSections) {
      const activeItem = section.items.find((it) => isNavPathActive(location.pathname, it.path));
      if (activeItem) return activeItem.key;
    }
    return undefined;
  }, [location.pathname, visibleSections]);

  const profileName = user?.username || user?.fullName || "Tài khoản";
  const profileEmail = user?.email?.trim() || "Chưa có email";

  const paintIndicator = useCallback((stretch = 1, direction = 1) => {
    const el = indicatorRef.current;
    if (!el) return;
    const state = indicatorState.current;
    el.style.width = `${state.w}px`;
    el.style.height = `${state.h}px`;
    el.style.transform = `translate3d(${state.x}px, ${state.y}px, 0) scaleY(${stretch})`;
    el.style.transformOrigin = direction >= 0 ? "50% 0%" : "50% 100%";
    el.style.opacity = state.initialized ? "1" : "0";
  }, []);

  const animateIndicator = useCallback(
    (time: number) => {
      const state = indicatorState.current;
      const dt = state.lastTime ? Math.min((time - state.lastTime) / 1000, 0.032) : 0.016;
      state.lastTime = time;

      const stiffness = 92;
      const damping = 18;
      const stepSpring = (current: number, target: number, velocity: number) => {
        const acceleration = (target - current) * stiffness - velocity * damping;
        const nextVelocity = velocity + acceleration * dt;
        return {
          value: current + nextVelocity * dt,
          velocity: nextVelocity,
        };
      };

      const x = stepSpring(state.x, state.targetX, state.vx);
      const y = stepSpring(state.y, state.targetY, state.vy);
      const w = stepSpring(state.w, state.targetW, state.vw);
      const h = stepSpring(state.h, state.targetH, state.vh);

      state.x = x.value;
      state.y = y.value;
      state.w = w.value;
      state.h = h.value;
      state.vx = x.velocity;
      state.vy = y.velocity;
      state.vw = w.velocity;
      state.vh = h.velocity;

      const direction = state.targetY >= state.y ? 1 : -1;
      const stretch = 1 + Math.min(Math.abs(state.vy) * 0.00013, 0.04);
      paintIndicator(stretch, direction);

      const settled =
        Math.abs(state.targetX - state.x) < 0.18 &&
        Math.abs(state.targetY - state.y) < 0.18 &&
        Math.abs(state.targetW - state.w) < 0.18 &&
        Math.abs(state.targetH - state.h) < 0.18 &&
        Math.abs(state.vx) < 4.5 &&
        Math.abs(state.vy) < 4.5 &&
        Math.abs(state.vw) < 4.5 &&
        Math.abs(state.vh) < 4.5;

      if (settled) {
        state.x = state.targetX;
        state.y = state.targetY;
        state.w = state.targetW;
        state.h = state.targetH;
        state.vx = 0;
        state.vy = 0;
        state.vw = 0;
        state.vh = 0;
        state.moving = false;
        state.raf = 0;
        state.lastTime = 0;
        paintIndicator(1, direction);
        return;
      }

      state.raf = window.requestAnimationFrame((nextTime) => animateIndicatorRef.current(nextTime));
    },
    [paintIndicator],
  );

  useLayoutEffect(() => {
    animateIndicatorRef.current = animateIndicator;
  }, [animateIndicator]);

  const setIndicatorTarget = useCallback(
    (metrics: { x: number; y: number; w: number; h: number }) => {
      const state = indicatorState.current;
      state.targetX = metrics.x;
      state.targetY = metrics.y;
      state.targetW = metrics.w;
      state.targetH = metrics.h;

      if (!state.initialized) {
        state.x = metrics.x;
        state.y = metrics.y;
        state.w = metrics.w;
        state.h = metrics.h;
        state.initialized = true;
        paintIndicator(1, 1);
        return;
      }

      if (!state.moving) {
        state.moving = true;
        state.lastTime = 0;
        state.raf = window.requestAnimationFrame(animateIndicator);
      }
    },
    [animateIndicator, paintIndicator],
  );

  const measureActiveItem = useCallback(() => {
    const nav = navRef.current;
    const activeItem = activeKey ? itemRefs.current.get(activeKey) : undefined;
    if (!nav || !activeItem) return;

    const navRect = nav.getBoundingClientRect();
    const itemRect = activeItem.getBoundingClientRect();
    setIndicatorTarget({
      x: itemRect.left - navRect.left,
      y: itemRect.top - navRect.top,
      w: itemRect.width,
      h: itemRect.height,
    });
  }, [activeKey, setIndicatorTarget]);

  const setItemRef = useCallback((key: string, node: HTMLAnchorElement | null) => {
    if (node) {
      itemRefs.current.set(key, node);
      return;
    }
    itemRefs.current.delete(key);
  }, []);

  useLayoutEffect(() => {
    measureActiveItem();
  }, [measureActiveItem]);

  useEffect(() => {
    const scheduleMeasure = () => {
      if (measureFrame.current) window.cancelAnimationFrame(measureFrame.current);
      measureFrame.current = window.requestAnimationFrame(() => {
        measureFrame.current = 0;
        measureActiveItem();
      });
    };

    const observer = new ResizeObserver(scheduleMeasure);
    if (navRef.current) observer.observe(navRef.current);
    const activeItem = activeKey ? itemRefs.current.get(activeKey) : undefined;
    if (activeItem) observer.observe(activeItem);
    window.addEventListener("resize", scheduleMeasure);

    return () => {
      observer.disconnect();
      window.removeEventListener("resize", scheduleMeasure);
      if (measureFrame.current) window.cancelAnimationFrame(measureFrame.current);
    };
  }, [activeKey, measureActiveItem]);

  useEffect(
    () => () => {
      const state = indicatorState.current;
      if (state.raf) window.cancelAnimationFrame(state.raf);
      if (measureFrame.current) window.cancelAnimationFrame(measureFrame.current);
    },
    [],
  );

  return (
    <aside className="km-sidebar scroll-thin">
      <div className="km-sidebar-brand">
        <div className="km-sidebar-logo">CP</div>
        <div className="min-w-0 leading-tight">
          <div className="km-brand-title text-sm font-bold text-white">{APP_BRAND_NAME}</div>
          <div className="mt-1 text-[11px] text-[var(--sidebar-text)]">Hệ thống quản lý kế toán</div>
        </div>
      </div>

      <nav ref={navRef} className="liquid-sidebar-nav scroll-thin relative flex-1">
        <div ref={indicatorRef} className="liquid-active-indicator" aria-hidden="true" />
        <div className="km-sidebar-sections relative z-10">
          {visibleSections.map((section, i) => (
            <div key={i}>
              {section.title && (
                <div className="km-sidebar-section-title">
                  {section.title}
                </div>
              )}
              <div className="km-sidebar-items">
                {section.items.map((it) => {
                  const Icon = it.icon;
                  return (
                    <NavLink
                      key={it.key}
                      ref={(node) => setItemRef(it.key, node)}
                      to={it.path}
                      onClick={onNavigate}
                      className={({ isActive }) =>
                        `liquid-sidebar-link group relative flex items-center gap-2 rounded-[14px] px-2 py-1.5 text-[13px] font-semibold transition-all duration-300 ${
                          isActive
                            ? "is-active text-white"
                            : "hover:bg-white/8 hover:text-white"
                        }`
                      }
                    >
                      <span className="km-sidebar-icon"><Icon className="h-[18px] w-[18px] shrink-0" /></span>
                      <span className="min-w-0 flex-1 truncate">{it.label}</span>
                      {it.key === "chats" && unreadCount > 0 && (
                        <span className="km-notification-badge km-notification-badge--sidebar">
                          {unreadCount > 99 ? "99+" : unreadCount}
                        </span>
                      )}
                      {!it.ready && (
                        <span className="rounded-md bg-white/10 px-1.5 py-0.5 text-[9px] font-semibold opacity-70">
                          sắp có
                        </span>
                      )}
                    </NavLink>
                  );
                })}
              </div>
            </div>
          ))}
        </div>
      </nav>

      <div className="km-sidebar-profile-wrap">
        <button
          type="button"
          className="km-sidebar-profile-card"
          onClick={() => setProfileOpen((value) => !value)}
          aria-expanded={profileOpen}
        >
          <span className="km-sidebar-profile-avatar overflow-hidden">
            {user?.avatarUrl ? (
              <img src={user.avatarUrl} alt="" className="h-full w-full object-cover" />
            ) : (
              initials(profileName)
            )}
          </span>
          <span className="km-sidebar-profile-copy">
            <span className="km-sidebar-profile-name">{profileName}</span>
            <span className="km-sidebar-profile-email">{profileEmail}</span>
          </span>
          <ChevronDown className={`km-sidebar-profile-arrow h-4 w-4 ${profileOpen ? "rotate-180" : ""}`} />
        </button>

        {profileOpen && (
          <>
            <div className="fixed inset-0 z-20" onClick={() => setProfileOpen(false)} />
            <div className="km-sidebar-profile-menu fade-in">
              <button
                type="button"
                onClick={() => { setProfileOpen(false); setModal("profile"); }}
              >
                <UserCog className="h-4 w-4" /> Hồ sơ cá nhân
              </button>
              <button
                type="button"
                onClick={() => { setProfileOpen(false); setModal("password"); }}
              >
                <KeyRound className="h-4 w-4" /> Đổi mật khẩu
              </button>
              <div className="km-sidebar-profile-menu-separator" />
              <button type="button" className="is-danger" onClick={logout}>
                <LogOut className="h-4 w-4" /> Đăng xuất
              </button>
            </div>
          </>
        )}
      </div>

      {modal === "profile" && <EditProfileModal onClose={() => setModal(null)} />}
      {modal === "password" && <ChangePasswordModal onClose={() => setModal(null)} />}
    </aside>
  );
}
